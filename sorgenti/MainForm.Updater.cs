using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Windows.Forms;

// Aggiornamento del programma da un indirizzo scelto dall'utente.
// Il file all'indirizzo è un testo semplice:
//   version=6.1.0
//   url=https://.../Video2All.exe
//   sha256=<impronta del file>
//   notes=cosa cambia (facoltativo)
public partial class MainForm
{
    public const string AppVersion = "6.1.0";
    TextBox tbUpdateUrl;
    CheckBox chkAutoUpdate;

    void BuildUpdateCard(FlowLayoutPanel page)
    {
        Card c = Section(page, L.T("Aggiornamenti del programma", "Program updates"));
        tbUpdateUrl = Box(360); tbUpdateUrl.Tag = "wide";
        tbUpdateUrl.TextChanged += delegate { QueueSave(); };
        Row(c, L.T("Indirizzo del file di aggiornamento", "Update manifest address"),
            L.T("Testo con righe version=, url=, sha256=. Il file scaricato viene installato solo se l'impronta SHA-256 coincide.", "Text with version=, url=, sha256= lines. The download is installed only if the SHA-256 matches."), tbUpdateUrl);
        chkAutoUpdate = Chk(L.T("Controlla all'avvio (una volta al giorno)", "Check on launch (once a day)"));
        chkAutoUpdate.CheckedChanged += delegate { QueueSave(); };
        var now = Btn2(L.T("Controlla ora", "Check now"));
        now.Click += delegate { CheckAppUpdate(true); };
        var ver = new Label { AutoSize = true, Tag = "dim", Text = L.T("Versione ", "Version ") + AppVersion, Margin = new Padding(8, 8, 0, 0) };
        Row(c, null, null, chkAutoUpdate, now, ver);
    }

    void AutoUpdateOnStart()
    {
        CleanupOldExe();
        if (!chkAutoUpdate.Checked || tbUpdateUrl.Text.Trim().Length == 0) return;
        string last = cfg.Get("updchecked", "");
        string today = DateTime.Today.ToString("yyyyMMdd");
        if (last == today) return;
        cfg.Set("updchecked", today);
        CheckAppUpdate(false);
    }

    static Dictionary<string, string> ParseManifest(string text)
    {
        var d = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (string raw in (text ?? "").Split('\n'))
        {
            string l = raw.Trim().TrimStart('﻿');
            int i = l.IndexOf('=');
            if (l.StartsWith("#") || i <= 0) continue;
            d[l.Substring(0, i).Trim()] = l.Substring(i + 1).Trim();
        }
        return d;
    }

    void CheckAppUpdate(bool interactive)
    {
        string url = tbUpdateUrl.Text.Trim();
        if (url.Length == 0)
        {
            if (interactive) Msg(L.T("Inserisci prima l'indirizzo del file di aggiornamento (Opzioni › Aggiornamenti del programma).", "Enter the update manifest address first (Options › Program updates)."));
            return;
        }
        if (!url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            if (interactive) Msg(L.T("Per sicurezza l'indirizzo deve iniziare con https://", "For safety the address must start with https://"));
            return;
        }
        StartThread("aggiornamento", delegate
        {
            string text = null, error = null;
            try
            {
                ServicePointManager.SecurityProtocol = (SecurityProtocolType)3072;
                using (var wc = new WebClient()) { wc.Encoding = Encoding.UTF8; text = wc.DownloadString(url); }
            }
            catch (Exception ex) { error = ex.Message; }
            Invoke2(delegate
            {
                if (text == null) { Log.W("Aggiornamento: " + error); if (interactive) Msg(L.T("Impossibile leggere l'indirizzo: ", "Could not read the address: ") + error); return; }
                var m = ParseManifest(text);
                string ver, file, sha, notes;
                m.TryGetValue("version", out ver); m.TryGetValue("url", out file); m.TryGetValue("sha256", out sha); m.TryGetValue("notes", out notes);
                if (string.IsNullOrEmpty(ver) || string.IsNullOrEmpty(file) || string.IsNullOrEmpty(sha) || sha.Length != 64)
                { if (interactive) Msg(L.T("Il file di aggiornamento non è valido (servono version, url e sha256).", "The manifest is invalid (version, url and sha256 are required).")); return; }
                if (CompareVersions(ver, AppVersion) <= 0) { if (interactive) Msg(L.T("Il programma è aggiornato (", "The program is up to date (") + AppVersion + ")."); return; }
                if (!file.StartsWith("https://", StringComparison.OrdinalIgnoreCase)) { if (interactive) Msg(L.T("Il file da scaricare deve essere https://", "The download must be https://")); return; }
                if (rec != null || str != null) { SetStatus(L.T("Aggiornamento disponibile: ", "Update available: ") + ver + L.T(" (installalo a registrazione finita)", " (install after recording)"), false, 10); if (!interactive) return; }
                string q = L.T("Disponibile la versione ", "Version available: ") + ver + L.T(" (installata ", " (installed ") + AppVersion + ")" +
                    (string.IsNullOrEmpty(notes) ? "" : "\n\n" + notes) + "\n\n" + L.T("Scaricare e installare? Il programma si riavvierà.", "Download and install? The program will restart.");
                if (Dlg.Show(this, q, Text, MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes) DownloadAndInstall(file, sha);
            });
        });
    }

    void DownloadAndInstall(string url, string sha)
    {
        if (rec != null || str != null || savingCount > 0) { Msg(L.T("Ferma prima registrazione e diretta.", "Stop recording and streaming first.")); return; }
        if (jobCount > 0 || postRunning > 0) { Msg(L.T("Ci sono lavori sui file in corso: aggiorna quando sono finiti.", "File jobs are running: update when they finish.")); return; }
        // si aggiorna sempre Video2All.exe nella cartella del programma (mai l'eseguibile che ci ha avviato:
        // con l'avvio alternativo sarebbe powershell.exe)
        string dir = Path.GetDirectoryName(ffmpeg);
        string exe = Path.Combine(dir, "Video2All.exe");
        string tmp = Path.Combine(dir, "Video2All.new");
        SetStatus(L.T("Download aggiornamento…", "Downloading update…"), false, 0);
        StartThread("download", delegate
        {
            string error = null;
            try
            {
                ServicePointManager.SecurityProtocol = (SecurityProtocolType)3072;
                using (var wc = new WebClient()) wc.DownloadFile(url, tmp);
                string got;
                using (var h = SHA256.Create())
                using (var fs = File.OpenRead(tmp))
                {
                    var sb = new StringBuilder();
                    foreach (byte b in h.ComputeHash(fs)) sb.Append(b.ToString("x2"));
                    got = sb.ToString();
                }
                if (!string.Equals(got, sha.Trim(), StringComparison.OrdinalIgnoreCase)) { File.Delete(tmp); error = L.T("impronta SHA-256 diversa: file scartato", "SHA-256 mismatch: file discarded"); }
                else
                {
                    // un .exe in esecuzione si può rinominare: si mette da parte e si mette il nuovo al suo posto
                    string old = Path.Combine(dir, "Video2All.old");
                    if (File.Exists(old)) File.Delete(old);
                    File.Move(exe, old);
                    try { File.Move(tmp, exe); }
                    catch { File.Move(old, exe); throw; }
                }
            }
            catch (Exception ex) { error = ex.Message; try { if (File.Exists(tmp)) File.Delete(tmp); } catch { } }
            Invoke2(delegate
            {
                if (error != null) { Log.W("Aggiornamento: " + error); Msg(L.T("Aggiornamento non riuscito: ", "Update failed: ") + error); SetStatus("", false, 1); return; }
                Log.W("Aggiornamento installato, riavvio");
                SaveSettings();
                try { Process.Start(exe, "--updated"); } catch { }
                closing = true;
                Close();
            });
        });
    }

    void CleanupOldExe()
    {
        try
        {
            string old = Path.Combine(Path.GetDirectoryName(ffmpeg), "Video2All.old");
            if (File.Exists(old)) File.Delete(old);
        }
        catch { }
    }
}
