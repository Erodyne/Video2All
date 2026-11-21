using Microsoft.Win32;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing.Imaging;
using System.Drawing;
using System.Globalization;
using System.IO.Pipes;
using System.IO;
using System.Net;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using System;

public partial class MainForm
{
    // ================================================================= File e strumenti
    void Browse()
    {
        using (var d = new FolderBrowserDialog())
        {
            d.SelectedPath = tbFolder.Text;
            if (d.ShowDialog(this) == DialogResult.OK) { tbFolder.Text = d.SelectedPath; RefreshFiles(); }
        }
    }

    void StartThumbWorker()
    {
        lock (thumbQueue) { if (thumbWorker) return; thumbWorker = true; }
        StartThread("miniature", delegate
        {
            while (true)
            {
                string file;
                lock (thumbQueue)
                {
                    if (thumbQueue.Count == 0) { thumbWorker = false; return; }
                    file = thumbQueue.Dequeue();
                }
                Image img = MakeThumb(file);
                if (img == null) continue;
                Invoke2(delegate
                {
                    foreach (ListViewItem it in lvFiles.Items)
                        if ((string)it.Tag == file)
                        {
                            if (!thumbs.Images.ContainsKey(file)) thumbs.Images.Add(file, img); else img.Dispose();
                            it.ImageKey = file;
                            break;
                        }
                });
            }
        });
    }

    Image MakeThumb(string file)
    {
        try
        {
            string cur = currentFile == null ? null : currentFile.Replace("_%03d", "");
            if (rec != null && cur != null && Path.GetFileNameWithoutExtension(file).StartsWith(Path.GetFileNameWithoutExtension(cur))) return null;
            string ext = Path.GetExtension(file).ToLowerInvariant();
            // file solo audio: niente immagine da estrarre (ffmpeg verrebbe lanciato due volte a ogni aggiornamento)
            if (ext == ".wav" || ext == ".mp3" || ext == ".flac" || ext == ".m4a" || ext == ".ogg" || ext == ".wma") return null;
            if (ext == ".png" || ext == ".jpg")
            {
                using (var fs = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                using (var src = Image.FromStream(fs))
                    return Fit(src);
            }
            var fi = new FileInfo(file);
            string key = StableHash(file + "|" + fi.Length + "|" + fi.LastWriteTimeUtc.Ticks);
            Directory.CreateDirectory(ThumbDir);
            string jpg = Path.Combine(ThumbDir, key + ".jpg");
            string none = Path.Combine(ThumbDir, key + ".none");
            if (File.Exists(none)) return null;              // già provato senza successo (file rovinato o senza video)
            if (!File.Exists(jpg) && !Tools.Thumb(ffmpeg, file, jpg)) { try { File.WriteAllText(none, ""); } catch { } return null; }
            using (var fs = new FileStream(jpg, FileMode.Open, FileAccess.Read))
            using (var src = Image.FromStream(fs))
                return Fit(src);
        }
        catch { return null; }
    }

    static Image Fit(Image src)
    {
        var b = new Bitmap(120, 90);
        using (var g = Graphics.FromImage(b))
        {
            g.Clear(Color.Black);
            float s = Math.Min(120f / src.Width, 90f / src.Height);
            int w = (int)(src.Width * s), h = (int)(src.Height * s);
            g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBilinear;
            g.DrawImage(src, (120 - w) / 2, (90 - h) / 2, w, h);
        }
        return b;
    }

    string SelectedFile()
    {
        if (lvFiles.SelectedItems.Count == 0) { SetStatus(L.T("Seleziona un file nell'elenco.", "Select a file in the list."), true, 4); return null; }
        return lvFiles.SelectedItems[0].Tag as string;
    }

    bool IsBusyFile(string f)
    {
        if (rec == null || currentFile == null) return false;
        string cur = Path.GetFileNameWithoutExtension(currentFile.Replace("_%03d", ""));
        return Path.GetFileNameWithoutExtension(f).StartsWith(cur);
    }

    void OpenSelected()
    {
        string f = SelectedFile();
        if (f == null) return;
        try { Process.Start(f); } catch (Exception ex) { Msg(ex.Message); }
    }

    // con un file selezionato si apre la sua cartella (anche le sottocartelle per data) evidenziandolo
    void OpenFolder()
    {
        try
        {
            string sel = lvFiles.SelectedItems.Count > 0 ? lvFiles.SelectedItems[0].Tag as string : null;
            if (sel != null && File.Exists(sel)) Process.Start("explorer.exe", "/select,\"" + sel + "\"");
            else Process.Start("explorer.exe", "\"" + RecordFolder() + "\"");
        }
        catch { }
    }

    // tutti i file selezionati (non solo il primo)
    void DeleteSelected()
    {
        List<string> files = SelectedFiles();
        if (files.Count == 0) return;
        int busy = files.RemoveAll(IsBusyFile);
        if (busy > 0) SetStatus(L.T("Il file in registrazione non viene toccato.", "The file being recorded is left alone."), true, 5);
        if (files.Count == 0) return;
        string what = files.Count == 1 ? Path.GetFileName(files[0]) : files.Count + L.T(" file", " files");
        // sui dischi di rete il Cestino non esiste: Windows cancella definitivamente
        bool network = false;
        foreach (string f in files)
        {
            try
            {
                string root = Path.GetPathRoot(Path.GetFullPath(f)) ?? "";
                if (root.StartsWith(@"\\")) network = true;
                else { DriveType dt = new DriveInfo(root).DriveType; if (dt == DriveType.Network || (dt == DriveType.Removable && new DriveInfo(root).DriveFormat != "NTFS")) network = true; }
            }
            catch { }
        }
        string question = network
            ? L.T("ATTENZIONE: su questo disco (rete o chiavetta) il Cestino non c'è.\nI file verranno CANCELLATI DEFINITIVAMENTE:\n", "WARNING: network folder, no Recycle Bin.\nFiles will be PERMANENTLY DELETED:\n") + what
            : L.T("Spostare nel Cestino?\n", "Move to Recycle Bin?\n") + what;
        if (Dlg.Show(this, question, Text, MessageBoxButtons.YesNo, network ? MessageBoxIcon.Warning : MessageBoxIcon.Question) != DialogResult.Yes) return;
        var failed = new List<string>();
        foreach (string f in files)
        {
            try { if (!Native.Recycle(f)) { failed.Add(Path.GetFileName(f)); continue; } } catch { failed.Add(Path.GetFileName(f)); continue; }
            // anche i file di accompagnamento (impronta, marcatori, rapporto), se non servono ad altri file con lo stesso nome
            string b = Path.Combine(Path.GetDirectoryName(f), Path.GetFileNameWithoutExtension(f));
            bool others = false;
            foreach (string ext in MediaExt) if (File.Exists(b + ext)) others = true;
            foreach (string side in new[] { f + ".md5", others ? null : b + "_marcatori.txt", others ? null : b + "_rapporto.txt" })
                if (side != null && File.Exists(side)) try { Native.Recycle(side); } catch { }
        }
        RefreshFiles();
        if (failed.Count > 0) Msg(L.T("Non spostati nel Cestino:\n", "Not moved to Recycle Bin:\n") + string.Join("\n", failed.ToArray()));
    }

    bool IsVideo(string f)
    {
        string e = Path.GetExtension(f).ToLowerInvariant();
        return e == ".mp4" || e == ".mkv" || e == ".mov" || e == ".avi" || e == ".ts" || e == ".m4v";
    }

    void TrimSelected()
    {
        string f = SelectedFile();
        if (f == null) return;
        if (!IsVideo(f) || IsBusyFile(f)) { SetStatus(L.T("Seleziona un video completato.", "Select a finished video."), true, 5); return; }
        using (var dlg = new Form { Text = L.T("Taglia video", "Trim video"), FormBorderStyle = FormBorderStyle.FixedDialog, StartPosition = FormStartPosition.CenterParent, MinimizeBox = false, MaximizeBox = false, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Padding = new Padding(10) })
        {
            var tStart = new TextBox { Text = "0:00", Width = 90 };
            var tEnd = new TextBox { Text = "", Width = 90 };
            var precise = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 380 };
            precise.Items.AddRange(new object[] {
                L.T("Veloce: taglia sui fotogrammi chiave (nessuna perdita)", "Fast: cut on keyframes (lossless)"),
                L.T("Intelligente: preciso, ricodifica solo i bordi (H.264)", "Smart: exact, re-encodes only the edges (H.264)"),
                L.T("Preciso: ricodifica tutto (più lento)", "Precise: re-encode all (slower)") });
            precise.SelectedIndex = Math.Min(2, Math.Max(0, cfg.GetInt("trimmode", 1)));
            var ok = new Button { Text = "OK", DialogResult = DialogResult.OK, AutoSize = true };
            var panel = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.TopDown };
            panel.Controls.Add(new Label { Text = L.T("Inizio (mm:ss o hh:mm:ss):", "Start (mm:ss or hh:mm:ss):"), AutoSize = true });
            panel.Controls.Add(tStart);
            panel.Controls.Add(new Label { Text = L.T("Fine (vuoto = fino alla fine):", "End (empty = to the end):"), AutoSize = true });
            panel.Controls.Add(tEnd);
            panel.Controls.Add(new Label { Text = L.T("Modo:", "Mode:"), AutoSize = true });
            panel.Controls.Add(precise);
            panel.Controls.Add(ok);
            dlg.Controls.Add(panel);
            ThemeDialog(dlg);
            dlg.AcceptButton = ok;
            if (dlg.ShowDialog(this) != DialogResult.OK) return;
            double a, b = 0;
            if (!Tools.ParseTime(tStart.Text, out a) || (tEnd.Text.Trim().Length > 0 && (!Tools.ParseTime(tEnd.Text, out b) || b <= a)))
            {
                Msg(L.T("Tempi non validi.", "Invalid times."));
                return;
            }
            string outF = Path.Combine(Path.GetDirectoryName(f), Path.GetFileNameWithoutExtension(f) + "_taglio" + Path.GetExtension(f));
            outF = UniqueName(outF);
            string range = " -ss " + Tools.Ts(a) + (b > 0 ? " -to " + Tools.Ts(b) : "");
            int mode = precise.SelectedIndex;
            cfg.Set("trimmode", mode);
            // ricodificando si tengono solo video e audio (sottotitoli/dati non sempre ricodificabili)
            string preciseCodec = " -map 0:v:0 -map 0:a? -c:v libx264 -preset veryfast -crf 18 -pix_fmt yuv420p -c:a aac -b:a 192k";
            string codec = mode == 2 ? preciseCodec : " -map 0 -c copy -avoid_negative_ts make_zero";
            string args = "-y -hide_banner -loglevel error" + range + " -i \"" + f + "\"" + codec + " \"" + outF + "\"";
            string fallbackArgs = "-y -hide_banner -loglevel error" + range + " -i \"" + f + "\"" + preciseCodec + " \"" + outF + "\"";
            double ta = a, tb = b;
            RunJob(L.T("Taglio", "Trim"), delegate
            {
                string e;
                if (mode == 1)
                {
                    string why = SmartTrim.Run(ffmpeg, f, ta, tb, outF, delegate (string step) { });
                    if (why == null) return L.T("Creato (taglio intelligente): ", "Created (smart cut): ") + Path.GetFileName(outF);
                    Log.W("Taglio intelligente non possibile (" + why + "): ricodifica completa");
                    args = fallbackArgs;
                }
                int code = Tools.Run(ffmpeg, args, out e, 4 * 3600 * 1000);
                return code == 0 ? L.T("Creato: ", "Created: ") + Path.GetFileName(outF) : "!" + L.T("Taglio non riuscito: ", "Trim failed: ") + Tail(e ?? "", 200);
            });
        }
    }

    // finestre di dialogo semplici con i colori del programma
    static void ThemeDialog(Form dlg)
    {
        dlg.BackColor = Theme.Bg; dlg.ForeColor = Theme.Text; dlg.Font = Theme.Ui(9.5f);
        Theme.Apply(dlg);
        dlg.Load += delegate { Theme.ApplyWindow(dlg); };
    }

    static string UniqueName(string f)
    {
        if (!File.Exists(f)) return f;
        string dir = Path.GetDirectoryName(f), name = Path.GetFileNameWithoutExtension(f), ext = Path.GetExtension(f);
        int i = 2;
        string c;
        do c = Path.Combine(dir, name + "_" + (i++) + ext); while (File.Exists(c));
        return c;
    }

    void CompressSelected()
    {
        foreach (string f in VideoFiles()) CompressOne(f);
    }

    void CompressOne(string f)
    {
        string outF = UniqueName(Path.Combine(Path.GetDirectoryName(f), Path.GetFileNameWithoutExtension(f) + "_compresso.mp4"));
        RunJob(L.T("Compressione", "Compression"), delegate
        {
            string e;
            int code = Tools.Run(ffmpeg, "-y -hide_banner -loglevel error -i \"" + f + "\" -map 0:v:0 -map 0:a? -c:v libx264 -preset medium -crf 24 -pix_fmt yuv420p -c:a aac -b:a 128k -movflags +faststart \"" + outF + "\"", out e, 12 * 3600 * 1000);
            return code == 0 ? L.T("Compresso: ", "Compressed: ") + Path.GetFileName(outF) : "!" + L.T("Compressione non riuscita: ", "Compression failed: ") + Tail(e ?? "", 200);
        });
    }

    void SplitBlackSelected()
    {
        foreach (string f in VideoFiles()) SplitBlackOne(f);
    }

    void SplitBlackOne(string f)
    {
        RunJob(L.T("Divisione su schermate nere", "Split on black screens"), delegate
        {
            string err;
            List<double> cuts = Tools.BlackCuts(ffmpeg, f, out err);
            if (cuts.Count == 0) return "!" + L.T("Nessuna schermata nera trovata.", "No black screens found.");
            var points = new List<double> { 0 };
            points.AddRange(cuts);
            string dir = Path.GetDirectoryName(f), name = Path.GetFileNameWithoutExtension(f), ext = Path.GetExtension(f);
            int ok = 0;
            for (int i = 0; i < points.Count; i++)
            {
                string outF = UniqueName(Path.Combine(dir, name + "_parte" + (i + 1).ToString("00") + ext));
                string to = i + 1 < points.Count ? " -to " + Tools.Ts(points[i + 1]) : "";
                string e;
                if (Tools.Run(ffmpeg, "-y -hide_banner -loglevel error -ss " + Tools.Ts(points[i]) + to + " -i \"" + f + "\" -map 0 -c copy -avoid_negative_ts make_zero \"" + outF + "\"", out e, 3600 * 1000) == 0) ok++;
            }
            return L.T("Creati ", "Created ") + ok + L.T(" spezzoni.", " parts.");
        });
    }

    // Lavori in background: il testo restituito va nello stato ("!" = avviso)
    // I lavori vanno in coda e si eseguono uno alla volta: dieci esportazioni insieme
    // saturerebbero il PC e farebbero perdere fotogrammi a una registrazione in corso
    readonly Queue<KeyValuePair<string, Func<string>>> jobQueue = new Queue<KeyValuePair<string, Func<string>>>();
    bool jobWorker;

    // Lavoro immediato (fuori coda): per la diagnostica del grabber, che ha l'anteprima ferma e non può aspettare
    void RunJobNow(string title, Func<string> work)
    {
        jobCount++;
        SetStatus(title + "...", false, 3);
        StartThread("diagnostica", delegate
        {
            string result;
            try { result = work(); }
            catch (Exception ex) { result = "!" + ex.Message; Log.W(title + ": " + ex); }
            Invoke2(delegate
            {
                jobCount--;
                if (result == null) result = "";
                bool warn = result.StartsWith("!");
                SetStatus(warn ? result.Substring(1) : result, warn, 10);
            });
        });
    }

    void RunJob(string title, Func<string> work)
    {
        jobCount++;
        bool start;
        lock (jobQueue) { jobQueue.Enqueue(new KeyValuePair<string, Func<string>>(title, work)); start = !jobWorker; if (start) jobWorker = true; }
        int waiting;
        lock (jobQueue) waiting = jobQueue.Count - (start ? 1 : 0);
        SetStatus(title + (waiting > 0 ? L.T("... (in coda: ", "... (queued: ") + waiting + ")" : "..."), false, 3);
        if (!start) return;
        StartThread("lavori", delegate
        {
            while (true)
            {
                KeyValuePair<string, Func<string>> job;
                lock (jobQueue)
                {
                    if (jobQueue.Count == 0) { jobWorker = false; return; }
                    job = jobQueue.Dequeue();
                }
                string t = job.Key;
                Invoke2(delegate { SetStatus(t + "...", false, 3); });
                string result;
                try { result = job.Value(); }
                catch (Exception ex) { result = "!" + ex.Message; Log.W(t + ": " + ex); }
                Invoke2(delegate
                {
                    jobCount--;
                    if (result == null) result = "";
                    bool warn = result.StartsWith("!");
                    SetStatus(warn ? result.Substring(1) : result, warn, 10);
                    RefreshFiles();
                });
            }
        });
    }

    void CheckUpdate()
    {
        SetStatus(L.T("Verifica aggiornamenti...", "Checking for updates..."), false, 3);
        StartThread("aggiornamenti", delegate
        {
            string local = Tools.Version(ffmpeg), remote = null;
            try
            {
                ServicePointManager.SecurityProtocol = (SecurityProtocolType)3072;
                using (var wc = new WebClient()) remote = wc.DownloadString("https://www.gyan.dev/ffmpeg/builds/release-version").Trim();
            }
            catch (Exception ex) { Log.W("Aggiornamenti: " + ex.Message); }
            Invoke2(delegate
            {
                if (remote == null) { Msg(L.T("Impossibile verificare (sei connesso a internet?).\nVersione installata: ", "Could not check (are you online?).\nInstalled version: ") + local); return; }
                bool newer = CompareVersions(remote, local) > 0;
                if (newer)
                {
                    if (Dlg.Show(this, L.T("Versione installata: ", "Installed: ") + local + "\n" + L.T("Disponibile: ", "Available: ") + remote + "\n\n" + L.T("Aprire la pagina di download? Sostituisci ffmpeg.exe nella cartella del programma.", "Open the download page? Replace ffmpeg.exe in the program folder."), Text, MessageBoxButtons.YesNo) == DialogResult.Yes)
                        try { Process.Start("https://www.gyan.dev/ffmpeg/builds/"); } catch { }
                }
                else Msg(L.T("ffmpeg è aggiornato (", "ffmpeg is up to date (") + local + ").");
            });
        });
    }

    static int CompareVersions(string a, string b)
    {
        MatchCollection ma = Regex.Matches(a ?? "", @"\d+"), mb = Regex.Matches(b ?? "", @"\d+");
        for (int i = 0; i < Math.Max(ma.Count, mb.Count) && i < 3; i++)
        {
            int x = i < ma.Count ? int.Parse(ma[i].Value) : 0, y = i < mb.Count ? int.Parse(mb[i].Value) : 0;
            if (x != y) return x.CompareTo(y);
        }
        return 0;
    }

    void CreateShortcut()
    {
        try
        {
            string desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
            string lnk = Path.Combine(desktop, "Video2All.lnk");
            Type t = Type.GetTypeFromProgID("WScript.Shell");
            object shell = Activator.CreateInstance(t);
            object sc = t.InvokeMember("CreateShortcut", BindingFlags.InvokeMethod, null, shell, new object[] { lnk });
            Type st = sc.GetType();
            string exe = Path.Combine(Path.GetDirectoryName(ffmpeg), "Video2All.exe");
            if (!File.Exists(exe)) exe = Application.ExecutablePath;
            st.InvokeMember("TargetPath", BindingFlags.SetProperty, null, sc, new object[] { exe });
            st.InvokeMember("WorkingDirectory", BindingFlags.SetProperty, null, sc, new object[] { Path.GetDirectoryName(exe) });
            st.InvokeMember("IconLocation", BindingFlags.SetProperty, null, sc, new object[] { exe + ",0" });
            st.InvokeMember("Save", BindingFlags.InvokeMethod, null, sc, null);
            SetStatus(L.T("Collegamento creato sul desktop.", "Desktop shortcut created."), false, 5);
        }
        catch (Exception ex) { Msg(L.T("Collegamento non creato: ", "Shortcut not created: ") + ex.Message); }
    }
}
