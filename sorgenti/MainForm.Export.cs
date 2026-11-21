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
    // ================================================================= Strumenti sui file
    List<string> SelectedFiles()
    {
        var l = new List<string>();
        foreach (ListViewItem it in lvFiles.SelectedItems) l.Add((string)it.Tag);
        if (l.Count == 0) SetStatus(L.T("Seleziona uno o più file nell'elenco.", "Select one or more files."), true, 4);
        return l;
    }

    void ExportSelected()
    {
        List<string> files = SelectedFiles();
        if (files.Count == 0) return;
        files.RemoveAll(delegate (string f) { return !IsVideo(f) || IsBusyFile(f); });
        if (files.Count == 0) { SetStatus(L.T("Seleziona video completati.", "Select finished videos."), true, 5); return; }
        int preset = AskChoice(L.T("Esporta per", "Export for"), Enc.ExportPresets);
        if (preset < 0) return;
        foreach (string f0 in files)
        {
            string f = f0;
            string outF = UniqueName(Path.Combine(Path.GetDirectoryName(f), Path.GetFileNameWithoutExtension(f) + Enc.ExportSuffix(preset) + Enc.ExportExt(preset)));
            RunJob(L.T("Esportazione ", "Export ") + Path.GetFileName(f), delegate
            {
                double d = preset == 2 ? Tools.Duration(ffmpeg, f) : 0;
                string e;
                int code = Tools.Run(ffmpeg, Enc.Export(preset, f, outF, d), out e, 12 * 3600 * 1000);
                bool reencoded = false;
                if (code != 0 && preset == 6)
                {
                    // il video (es. FFV1 d'archivio) non entra in MP4 così com'è: si ricodifica in alta qualità
                    code = Tools.Run(ffmpeg, "-y -hide_banner -loglevel error -i \"" + f + "\" -map 0:v:0 -map 0:a? -c:v libx264 -preset medium -crf 16 -pix_fmt yuv420p -c:a aac -b:a 256k -movflags +faststart \"" + outF + "\"", out e, 12 * 3600 * 1000);
                    reencoded = code == 0;
                }
                if (code != 0) return "!" + L.T("Esportazione non riuscita: ", "Export failed: ") + Tail(e ?? "", 200);
                long bytes = new FileInfo(outF).Length;
                string size = (bytes / 1048576.0).ToString("0.0");
                if (preset == 2 && bytes > 16L * 1048576)
                    return "!" + L.T("Esportato ma supera i 16 MB di WhatsApp (video troppo lungo): taglialo prima. ", "Exported but over WhatsApp's 16 MB (too long): trim it first. ") + Path.GetFileName(outF) + " (" + size + " MB)";
                return L.T("Esportato: ", "Exported: ") + Path.GetFileName(outF) + " (" + size + " MB)" + (reencoded ? L.T(" — ricodificato: il video originale non entra in MP4", " — re-encoded: the original video does not fit MP4") : "");
            });
        }
    }

    int AskChoice(string title, string[] options)
    {
        using (var dlg = new Form { Text = title, FormBorderStyle = FormBorderStyle.FixedDialog, StartPosition = FormStartPosition.CenterParent, MinimizeBox = false, MaximizeBox = false, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Padding = new Padding(10) })
        {
            var lb = new ListBox { Width = 300, Height = 22 * options.Length + 8 };
            lb.Items.AddRange(options);
            lb.SelectedIndex = 0;
            var ok = new Button { Text = "OK", DialogResult = DialogResult.OK, AutoSize = true };
            var panel = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.TopDown };
            panel.Controls.Add(lb); panel.Controls.Add(ok);
            dlg.Controls.Add(panel);
            dlg.AcceptButton = ok;
            lb.DoubleClick += delegate { dlg.DialogResult = DialogResult.OK; };
            ThemeDialog(dlg);
            return dlg.ShowDialog(this) == DialogResult.OK ? lb.SelectedIndex : -1;
        }
    }

    void ClipSelected()
    {
        string f = SelectedFile();
        if (f == null) return;
        if (!IsVideo(f) || IsBusyFile(f)) { SetStatus(L.T("Seleziona un video completato.", "Select a finished video."), true, 5); return; }
        using (var dlg = new Form { Text = L.T("Clip o GIF", "Clip or GIF"), FormBorderStyle = FormBorderStyle.FixedDialog, StartPosition = FormStartPosition.CenterParent, MinimizeBox = false, MaximizeBox = false, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Padding = new Padding(10) })
        {
            var tStart = new TextBox { Text = "0:00", Width = 90 };
            var tEnd = new TextBox { Text = "0:10", Width = 90 };
            var cbType = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 220 };
            cbType.Items.AddRange(new object[] { "GIF (480 px, 15 fps)", "GIF " + L.T("piccola (320 px, 10 fps)", "small (320 px, 10 fps)"), L.T("Clip MP4", "MP4 clip") });
            cbType.SelectedIndex = 0;
            var ok = new Button { Text = "OK", DialogResult = DialogResult.OK, AutoSize = true };
            var panel = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.TopDown };
            panel.Controls.Add(new Label { Text = L.T("Inizio:", "Start:"), AutoSize = true }); panel.Controls.Add(tStart);
            panel.Controls.Add(new Label { Text = L.T("Fine:", "End:"), AutoSize = true }); panel.Controls.Add(tEnd);
            panel.Controls.Add(cbType); panel.Controls.Add(ok);
            dlg.Controls.Add(panel);
            dlg.AcceptButton = ok;
            ThemeDialog(dlg);
            if (dlg.ShowDialog(this) != DialogResult.OK) return;
            double a, b;
            if (!Tools.ParseTime(tStart.Text, out a) || !Tools.ParseTime(tEnd.Text, out b) || b <= a) { Msg(L.T("Tempi non validi.", "Invalid times.")); return; }
            if (cbType.SelectedIndex < 2 && b - a > 30) { Msg(L.T("Per le GIF usa al massimo 30 secondi.", "Use at most 30 seconds for GIFs.")); return; }
            int type = cbType.SelectedIndex;
            string outF = UniqueName(Path.Combine(Path.GetDirectoryName(f), Path.GetFileNameWithoutExtension(f) + "_clip" + (type < 2 ? ".gif" : ".mp4")));
            string range = " -ss " + Tools.Ts(a) + " -to " + Tools.Ts(b);
            string args = type < 2
                ? "-y -hide_banner -loglevel error" + range + " -i \"" + f + "\" -vf \"fps=" + (type == 0 ? 15 : 10) + ",scale=" + (type == 0 ? 480 : 320) + ":-1:flags=lanczos,split[a][b];[a]palettegen=stats_mode=diff[p];[b][p]paletteuse=dither=bayer:bayer_scale=4\" -loop 0 \"" + outF + "\""
                : "-y -hide_banner -loglevel error" + range + " -i \"" + f + "\" -map 0:v:0 -map 0:a:0? -c:v libx264 -preset veryfast -crf 20 -pix_fmt yuv420p -c:a aac -b:a 160k -movflags +faststart \"" + outF + "\"";
            RunJob("Clip", delegate
            {
                string e;
                int code = Tools.Run(ffmpeg, args, out e, 3600 * 1000);
                return code == 0 ? L.T("Creato: ", "Created: ") + Path.GetFileName(outF) + " (" + (new FileInfo(outF).Length / 1048576.0).ToString("0.0") + " MB)" : "!" + L.T("Clip non riuscita: ", "Clip failed: ") + Tail(e ?? "", 200);
            });
        }
    }

    void SheetSelected()
    {
        List<string> files = SelectedFiles();
        int before = files.Count;
        files.RemoveAll(delegate (string f) { return !IsVideo(f) || IsBusyFile(f); });
        if (files.Count == 0) { if (before > 0) SetStatus(L.T("Il foglio provini si fa solo dai video completati.", "Contact sheets need finished videos."), true, 5); return; }
        foreach (string f0 in files)
        {
            string f = f0;
            string outF = UniqueName(Path.Combine(Path.GetDirectoryName(f), Path.GetFileNameWithoutExtension(f) + "_provini.jpg"));
            RunJob(L.T("Provini ", "Contact sheet ") + Path.GetFileName(f), delegate
            {
                double d = Tools.Duration(ffmpeg, f);
                if (d <= 0) return "!" + L.T("Durata non leggibile.", "Cannot read duration.");
                string rate = Tools.Ts(Math.Max(0.001, 24.0 / d));
                string e;
                int code = Tools.Run(ffmpeg, "-y -hide_banner -loglevel error -i \"" + f + "\" -vf \"fps=" + rate + ",scale=320:-2,tile=4x6:padding=6:margin=6:color=black\" -frames:v 1 -q:v 3 \"" + outF + "\"", out e, 3 * 3600 * 1000);
                return code == 0 ? L.T("Provini creati: ", "Contact sheet: ") + Path.GetFileName(outF) : "!" + L.T("Provini non riusciti: ", "Failed: ") + Tail(e ?? "", 200);
            });
        }
    }
}
