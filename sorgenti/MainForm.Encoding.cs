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
    // ================================================================= Codifica
    void FillCodecOptions()
    {
        VCodec c = Enc.Codecs[Math.Max(0, cbCodec.SelectedIndex)];
        bool was = loading;
        loading = true;
        string pr = cbEncPreset.Text, pf = cbProfile.Text, tu = cbTune.Text;
        cbEncPreset.Items.Clear(); cbEncPreset.Items.AddRange(Enc.Presets(c.Family));
        cbProfile.Items.Clear(); cbProfile.Items.AddRange(Enc.Profiles(c));
        cbTune.Items.Clear(); cbTune.Items.AddRange(Enc.Tunes(c));
        int i = cbEncPreset.Items.IndexOf(pr);
        cbEncPreset.SelectedIndex = i >= 0 ? i : (cbEncPreset.Items.Count > 0 ? Math.Max(0, cbEncPreset.Items.IndexOf(Enc.DefaultPreset(c.Family))) : -1);
        i = cbProfile.Items.IndexOf(pf); cbProfile.SelectedIndex = i >= 0 ? i : 0;
        i = cbTune.Items.IndexOf(tu); cbTune.SelectedIndex = i >= 0 ? i : 0;
        loading = was;
    }

    void OnCodecChanged()
    {
        FillCodecOptions();
        int c = cbCodec.SelectedIndex;
        if (!loading && c >= 0 && !encOk[c] && Enc.Codecs[c].Hardware)
            SetStatus(L.T("Questo encoder non risulta disponibile sul PC: in registrazione si userà la CPU.", "This encoder is not available: CPU will be used."), true, 6);
        if (!loading && Enc.IsLossless(c) && !chkLossMez.Checked)
        {
            chkLossMez.Checked = true;
            SetStatus(L.T("Attivata la qualità interna senza perdita, necessaria per un file davvero senza perdita.", "Lossless internal quality enabled (needed for true lossless)."), false, 6);
        }
        UpdateEncUi();
    }

    bool AudioOnlyFormat { get { return cbFormat.SelectedIndex >= Enc.Containers.Length; } }

    void UpdateEncUi()
    {
        bool ao = AudioOnlyFormat;
        bool lossless = Enc.IsLossless(Math.Max(0, cbCodec.SelectedIndex));
        cbCodec.Enabled = !ao && rec == null;
        bool enc = !ao && !lossless && rec == null;
        foreach (Control c in new Control[] { cbRateCtl, nKeySec, cbEncPreset, cbProfile, cbTune, cbOutSize })
            c.Enabled = enc;
        nQuality.Enabled = enc && cbRateCtl.SelectedIndex != 2;
        nRecBitrate.Enabled = enc && cbRateCtl.SelectedIndex != 0;
        cbACodec.Enabled = !ao && rec == null;
        nAKbps.Enabled = rec == null;
        chkInterlaced.Enabled = !ao && rec == null && str == null;
        UpdateEstimate();
    }

    EncSettings CurrentEnc()
    {
        var s = new EncSettings();
        s.Codec = Math.Max(0, cbCodec.SelectedIndex);
        if (Enc.Codecs[s.Codec].Hardware && !encOk[s.Codec])
        {
            string k = Enc.Codecs[s.Codec].Kind;
            s.Codec = k == "hevc" ? 4 : k == "av1" ? 8 : 0;
        }
        s.Rate = Math.Max(0, cbRateCtl.SelectedIndex);
        s.Quality = (int)nQuality.Value;
        s.Bitrate = (int)nRecBitrate.Value;
        s.KeySec = (int)nKeySec.Value;
        s.Preset = s.Codec == cbCodec.SelectedIndex && cbEncPreset.Text.Length > 0 ? cbEncPreset.Text : null;
        s.Profile = s.Codec == cbCodec.SelectedIndex ? cbProfile.Text : null;
        s.Tune = s.Codec == cbCodec.SelectedIndex ? cbTune.Text : null;
        s.AudioCodec = Math.Max(0, cbACodec.SelectedIndex);
        s.AudioKbps = (int)nAKbps.Value;
        s.OutSize = Math.Max(0, cbOutSize.SelectedIndex);
        s.Interlaced = chkInterlaced.Checked;
        s.Pal = SourceHeight() != 480;
        s.Audio24 = cap != null ? capAudio24 : chkAudio24.Checked && !audio24Failed;    // segue ciò che la cattura fornisce davvero
        if (cbAspect.SelectedIndex == 2)
        {
            // proporzioni esatte come frazione (1.33333 darebbe rapporti tipo 4000:3999)
            float a = OutputAspect();
            s.Aspect = Math.Abs(a - 4f / 3f) < 0.002f ? "4:3" : Math.Abs(a - 16f / 9f) < 0.002f ? "16:9" : ((int)Math.Round(a * 1000)) + ":1000";
        }
        return s;
    }

    void UpdateEstimate()
    {
        if (lblEstimate == null || cbCodec == null || loading) return;
        try
        {
            bool ao = AudioOnlyFormat;
            double mb = Enc.MBperMin(CurrentEnc(), cbFormat.SelectedIndex, ao, ao ? cbFormat.SelectedIndex - Enc.Containers.Length : 0, Fps(), SourceHeight());
            if (chkArchive.Checked && !ao) mb += Enc.MBperMin(new EncSettings { Codec = 12 }, 0, false, 0, Fps(), SourceHeight());
            // il microfono è una seconda traccia (nei file video; nel solo audio viene mixato)
            if (chkMic.Checked && !ao)
            {
                EncSettings es = CurrentEnc();
                double mk = es.AudioCodec == 3 ? (es.Audio24 ? 2304 : 1536) : es.AudioCodec == 2 ? (es.Audio24 ? 1350 : 900) : es.AudioKbps;
                mb += mk * 60 / 8 / 1024;
            }
            string free = "";
            double gb = freeGbCache;      // aggiornato in background (mai letto qui: potrebbe bloccare)
            if (gb > 0 && mb > 0) free = "  ·  " + L.T("spazio per ", "room for ") + (gb * 1024 / mb / 60).ToString("0.0") + L.T(" ore", " hours");
            lblEstimate.Text = "≈ " + mb.ToString(mb < 10 ? "0.0" : "0") + " MB/min" + free;
        }
        catch { lblEstimate.Text = ""; }
    }
}
