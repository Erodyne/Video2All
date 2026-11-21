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
    // ================================================================= Analisi e diagnostica
    void AnalyzeSignal()
    {
        if (rec != null || str != null || capStopping || IsTestSource) return;
        bool wasRunning = cap != null;
        CaptureOptions o = BuildOptions(false, false);
        StopCapture(delegate
        {
            RunJobNow(L.T("Analisi del segnale (6 s)", "Analyzing signal (6 s)"), delegate
            {
                string e;
                Tools.Run(ffmpeg, Cmd.Analyze(o, 6), out e, 60000, false);
                int w, h; double f;
                string det = DeviceCaps.Detected(e, out w, out h, out f);
                MatchCollection all = Regex.Matches(e ?? "", @"Multi frame detection: TFF:\s*(\d+) BFF:\s*(\d+) Progressive:\s*(\d+) Undetermined:\s*(\d+)");
                Match m = all.Count > 0 ? all[all.Count - 1] : Match.Empty;
                if (det == null && !m.Success)
                {
                    if (wasRunning) Invoke2(delegate { StartCapture(false); });     // l'anteprima torna com'era
                    return "!" + L.T("Nessun segnale analizzabile. ", "No signal to analyze. ") + Tail(e ?? "", 200);
                }
                long tff = m.Success ? long.Parse(m.Groups[1].Value) : 0, bff = m.Success ? long.Parse(m.Groups[2].Value) : 0, prog = m.Success ? long.Parse(m.Groups[3].Value) : 0;
                bool interlaced = tff + bff > prog;
                Invoke2(delegate
                {
                    string std = h == 480 ? "NTSC (720x480, 29,97)" : h == 576 ? "PAL (720x576, 25)" : w + "x" + h;
                    string msg = L.T("Formato: ", "Format: ") + (det ?? "?") + "\n" + L.T("Standard probabile: ", "Likely standard: ") + std + "\n" +
                        L.T("Campi: ", "Fields: ") + (interlaced ? (tff >= bff ? L.T("interlacciato (TFF)", "interlaced (TFF)") : L.T("interlacciato (BFF)", "interlaced (BFF)")) : L.T("progressivo", "progressive")) +
                        " [TFF " + tff + ", BFF " + bff + ", prog " + prog + "]\n\n" +
                        L.T("Consiglio: ", "Advice: ") + (interlaced ? L.T("Bwdif 50/60 per video e TV; per console a 240p usa 'Retro 240p'.", "Bwdif 50/60 for video/TV; for 240p consoles use 'Retro 240p'.") : L.T("nessuna deinterlacciatura.", "no deinterlacing.")) +
                        "\n\n" + L.T("Applicare queste impostazioni?", "Apply these settings?");
                    if (Dlg.Show(this, msg, Text, MessageBoxButtons.YesNo, MessageBoxIcon.Information) == DialogResult.Yes)
                    {
                        if (h == 480) cbStd.SelectedIndex = 1; else if (h == 576) cbStd.SelectedIndex = 0;
                        cbDeint.SelectedIndex = interlaced ? 3 : 0;
                        if (interlaced) { Enc.BottomFirst = bff > tff; cfg.Set("bff", Enc.BottomFirst); QueueSave(); }
                    }
                    if (wasRunning) StartCapture(false);
                });
                return L.T("Analisi completata.", "Analysis done.");
            });
        });
    }

    // Prova combinazioni di formato finché una funziona
    void AutoProbe()
    {
        if (rec != null || str != null || capStopping || IsTestSource) return;
        bool wasRunning = cap != null;
        StopCapture(delegate
        {
            var tries = new List<KeyValuePair<string, CaptureOptions>>();
            Func<string, string, string, CaptureOptions> mk = delegate (string size, string fr, string pf)
            {
                CaptureOptions o = BuildOptions(false, false);
                o.DevLoad = null; o.DevSave = null;
                o.Width = o.Height = 0; o.Fps = null; o.PixFmt = pf;
                if (size != null) { string[] p = size.Split('x'); o.Width = int.Parse(p[0]); o.Height = int.Parse(p[1]); }
                o.Fps = fr;
                return o;
            };
            string[] pfs = caps != null && caps.PixFmts.Count > 0 ? caps.PixFmts.ToArray() : new string[] { null };
            foreach (string pf in pfs)
            {
                tries.Add(new KeyValuePair<string, CaptureOptions>("PAL 720x576 " + pf, mk("720x576", "25", pf)));
                tries.Add(new KeyValuePair<string, CaptureOptions>("NTSC 720x480 " + pf, mk("720x480", "30000/1001", pf)));
            }
            if (caps != null)
                foreach (var inp in caps.VideoInputs)
                    foreach (string std in new[] { "PAL", "NTSC" })      // anche NTSC: console americane/giapponesi
                    {
                        CaptureOptions oi = std == "PAL" ? mk("720x576", "25", null) : mk("720x480", "30000/1001", null);
                        oi.VideoPin = inp.Key;
                        int ap; oi.AudioPin = caps.RelatedAudio.TryGetValue(inp.Key, out ap) ? ap : -1;
                        tries.Add(new KeyValuePair<string, CaptureOptions>(std + " " + inp.Value, oi));
                    }
            tries.Add(new KeyValuePair<string, CaptureOptions>("Auto", mk(null, null, null)));
            RunJobNow(L.T("Prova automatica dei formati", "Auto format test"), delegate
            {
                var report = new StringBuilder();
                foreach (var t in tries)
                {
                    string e;
                    Tools.Run(ffmpeg, Cmd.Probe(t.Value, 3), out e, 30000, false);   // priorità normale: si contano i fotogrammi
                    MatchCollection fr = Regex.Matches(e ?? "", @"frame=\s*(\d+)");
                    int n = fr.Count > 0 ? int.Parse(fr[fr.Count - 1].Groups[1].Value) : 0;
                    report.AppendLine(t.Key + ": " + n + L.T(" fotogrammi", " frames"));
                    if (n >= 30)
                    {
                        var ok = t.Value;
                        Invoke2(delegate
                        {
                            loading = true;
                            if (ok.Height == 576) { cbStd.SelectedIndex = 0; SetComboText(cbSize, "720x576"); SetComboText(cbFps, "25"); }
                            else if (ok.Height == 480) { cbStd.SelectedIndex = 1; SetComboText(cbSize, "720x480"); SetComboText(cbFps, "29,97"); }
                            else { cbStd.SelectedIndex = 5; SetComboText(cbSize, "Auto"); SetComboText(cbFps, "Auto"); }
                            SetComboText(cbPixFmt, ok.PixFmt ?? "Auto");
                            // se funziona solo con un ingresso preciso, si seleziona quell'ingresso
                            int idx = 0;
                            if (ok.VideoPin >= 0 && caps != null)
                                for (int k = 0; k < caps.VideoInputs.Count; k++) if (caps.VideoInputs[k].Key == ok.VideoPin) idx = k + 1;
                            if (idx < cbInput.Items.Count) { suppressInputChange = true; try { cbInput.SelectedIndex = idx; } finally { suppressInputChange = false; } }
                            profileKey = CurrentProfileKey();
                            loading = false;
                            QueueSave();
                            Msg(L.T("Combinazione funzionante trovata e applicata:\n", "Working combination found and applied:\n") + t.Key + "\n\n" + report);
                            if (wasRunning && cap == null) StartCapture(false);     // l'anteprima riparte con il formato trovato
                        });
                        return L.T("Trovato: ", "Found: ") + t.Key;
                    }
                }
                Invoke2(delegate { Msg(L.T("Nessuna combinazione ha prodotto immagini. Controlla cavi, driver e che la sorgente sia accesa.\n\n", "No combination produced frames. Check cables, driver and that the source is on.\n\n") + report); });
                return "!" + L.T("Nessun formato funzionante.", "No working format.");
            });
        });
    }
}
