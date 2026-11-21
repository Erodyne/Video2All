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
    // ================================================================= Dispositivi
    void ParseDevices(string output)
    {
        bool wasLoading = loading;
        loading = true;
        string prevV = cbVideo.SelectedItem as string ?? cfg.Get("video", "");
        string prevA = cbAudio.SelectedItem as string ?? cfg.Get("audio", "");
        string prevC = cbCam.SelectedItem as string ?? cfg.Get("camdev", "");
        string prevM = cbMic.SelectedItem as string ?? cfg.Get("micdev", "");
        cbVideo.Items.Clear(); cbAudio.Items.Clear(); cbCam.Items.Clear(); cbMic.Items.Clear(); altNames.Clear(); displayToDev.Clear();
        skipMic = skipCam = false;      // elenco aggiornato: si riprovano microfono e webcam
        cbAudio.Items.Add(L.T("(nessun audio)", "(no audio)"));
        try { File.WriteAllText(Path.Combine(Settings.AppDir, "dispositivi.txt"), output); } catch { }

        string section = "", lastDisplay = null;
        var rx = new Regex("\"([^\"]+)\"(?:\\s+\\(([^)]*)\\))?");
        var seen = new Dictionary<string, int>();
        var video = new List<string>();
        var audio = new List<string>();
        foreach (string raw in output.Split(new[] { '\n' }))
        {
            string line = raw.Trim();
            if (line.Contains("DirectShow video devices")) { section = "video"; continue; }
            if (line.Contains("DirectShow audio devices")) { section = "audio"; continue; }
            Match m = rx.Match(line);
            if (!m.Success) continue;
            if (line.Contains("Alternative name"))
            {
                if (lastDisplay != null && !altNames.ContainsKey(lastDisplay)) altNames[lastDisplay] = m.Groups[1].Value;
                continue;
            }
            string name = m.Groups[1].Value;
            string kind = m.Groups[2].Success ? m.Groups[2].Value.ToLowerInvariant() : section;
            string key = kind + "|" + name;
            int n;
            seen.TryGetValue(key, out n);
            seen[key] = ++n;
            string display = n > 1 ? name + " (" + n + ")" : name;
            displayToDev[display] = name;
            lastDisplay = display;
            if (kind.Contains("video") && !video.Contains(display)) video.Add(display);
            if (kind.Contains("audio") && !audio.Contains(display)) audio.Add(display);
        }
        foreach (string v in video) { cbVideo.Items.Add(v); cbCam.Items.Add(v); }
        cbVideo.Items.Add(TestSourceName);
        foreach (string a in audio) { cbAudio.Items.Add(a); cbMic.Items.Add(a); }

        int iv = cbVideo.Items.IndexOf(prevV);
        cbVideo.SelectedIndex = iv >= 0 ? iv : (video.Count > 0 ? BestVideo() : cbVideo.Items.Count - 1);
        int ia = cbAudio.Items.IndexOf(prevA);
        if (ia < 0) ia = cbAudio.Items.IndexOf(cbVideo.SelectedItem);
        cbAudio.SelectedIndex = ia >= 0 ? ia : 0;
        int ic = cbCam.Items.IndexOf(prevC);
        if (ic < 0) for (int i = 0; i < cbCam.Items.Count; i++) if (i != cbVideo.SelectedIndex) { ic = i; break; }
        if (ic >= 0) cbCam.SelectedIndex = ic;
        int im = cbMic.Items.IndexOf(prevM);
        if (im < 0) for (int i = 0; i < cbMic.Items.Count; i++) if (cbMic.Items[i].ToString().IndexOf("micro", StringComparison.OrdinalIgnoreCase) >= 0) { im = i; break; }
        if (im >= 0) cbMic.SelectedIndex = im; else if (cbMic.Items.Count > 0) cbMic.SelectedIndex = 0;

        if (video.Count > 0) SetStatus(L.T("Pronto. Premi Anteprima.", "Ready. Press Preview."), false, 0);
        else SetStatus(L.T("Nessun grabber trovato: collega il dispositivo (si aggiorna da solo) o usa la sorgente di prova.", "No grabber found: connect it (auto refresh) or use the test source."), true, 15);
        loading = wasLoading;
        profileKey = CurrentProfileKey();
        if (profileKey != null) LoadDeviceProfile(profileKey);
        LoadCapsAsync();
        SetUi();
    }

    bool IsTestSource { get { return (cbVideo.SelectedItem as string) == TestSourceName; } }

    // Formati, ingressi crossbar e frequenze audio del dispositivo
    bool capsPending;

    void LoadCapsAsync()
    {
        string dev = cbVideo.SelectedItem as string;
        // il dispositivo è occupato dall'anteprima: si legge al prossimo riavvio della cattura
        if (cap != null && !IsTestSource) { capsPending = true; return; }
        capsPending = false;
        capsFor = dev;
        if (dev == null || IsTestSource) { ApplyCaps(new DeviceCaps(), dev); return; }
        string vname = DevName(dev);
        string aname = cbAudio.SelectedIndex > 0 ? DevName(cbAudio.SelectedItem) : null;
        StartThread("formati", delegate
        {
            string e1 = "", e2 = "";
            try { Tools.Run(ffmpeg, "-hide_banner -loglevel verbose -list_options true -f dshow -i \"video=" + vname + "\"", out e1, 20000); } catch { }
            if (aname != null) try { Tools.Run(ffmpeg, "-hide_banner -list_options true -f dshow -i \"audio=" + aname + "\"", out e2, 20000); } catch { }
            DeviceCaps c = DeviceCaps.Parse((e1 ?? "") + "\n" + (e2 ?? ""));
            try { File.WriteAllText(Path.Combine(Settings.AppDir, "formati_dispositivo.txt"), (e1 ?? "") + "\n----\n" + (e2 ?? "")); } catch { }
            Invoke2(delegate { ApplyCaps(c, dev); });
        });
    }

    bool autoPreviewPending;

    void RunPendingAutoPreview()
    {
        if (!autoPreviewPending) return;
        autoPreviewPending = false;
        if (cap == null && !capStopping && cbVideo.SelectedItem != null) StartCapture(false);
    }

    void ApplyCaps(DeviceCaps c, string dev)
    {
        if (dev != (cbVideo.SelectedItem as string)) { RunPendingAutoPreview(); return; }
        caps = c;
        bool was = loading;
        loading = true;
        string size = cbSize.Text, fpsT = cbFps.Text, pf = cbPixFmt.Text, inp = cfg.Get("input", cbInput.Text);
        cbSize.Items.Clear(); cbSize.Items.Add("Auto");
        foreach (string s in c.Sizes) cbSize.Items.Add(s);
        foreach (string s in new[] { "720x576", "720x480", "704x576", "640x480", "352x288" }) if (!cbSize.Items.Contains(s)) cbSize.Items.Add(s);
        cbFps.Items.Clear(); cbFps.Items.Add("Auto");
        foreach (string r in c.Rates) cbFps.Items.Add(DeviceCaps.RateLabel(r));
        foreach (string r in new[] { "25", "29,97", "50", "59,94" }) if (!cbFps.Items.Contains(r)) cbFps.Items.Add(r);
        cbPixFmt.Items.Clear(); cbPixFmt.Items.Add("Auto");
        foreach (string p in c.PixFmts) cbPixFmt.Items.Add(p);
        cbInput.Items.Clear(); cbInput.Items.Add(L.T("Predefinito", "Default"));
        foreach (var kv in c.VideoInputs) cbInput.Items.Add(kv.Value + " (pin " + kv.Key + ")");
        SetComboText(cbSize, size); SetComboText(cbFps, fpsT); SetComboText(cbPixFmt, pf);
        int ii = inp == "Predefinito" || inp == "Default" ? 0 : cbInput.Items.IndexOf(inp);
        cbInput.SelectedIndex = ii >= 0 ? ii : 0;
        loading = was;
        profileKey = CurrentProfileKey();
        if (!loading)
        {
            string info = c.VideoInputs.Count > 0 ? L.T("Ingressi trovati: ", "Inputs found: ") + c.VideoInputs.Count : L.T("Il driver non espone gli ingressi (si usa quello predefinito).", "Driver exposes no inputs (default used).");
            SetStatus(info, false, 4);
        }
        RunPendingAutoPreview();
    }

    int SelectedPin(out int audioPin)
    {
        audioPin = -1;
        if (caps == null || cbInput.SelectedIndex <= 0 || cbInput.SelectedIndex - 1 >= caps.VideoInputs.Count) return -1;
        var kv = caps.VideoInputs[cbInput.SelectedIndex - 1];
        int ap;
        if (caps.RelatedAudio.TryGetValue(kv.Key, out ap) && ap >= 0) audioPin = ap;
        return kv.Key;
    }
}
