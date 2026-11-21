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
// ---- controlli: acquisizione
    ComboBox cbVideo, cbAudio, cbInput, cbStd, cbSize, cbFps, cbPixFmt, cbDeint, cbPreset;
    CheckBox chkDriver, chkDriverMem, chkLowLat, chkCfr, chkLossMez;
    NumericUpDown nRtBuf;
    Button btnDriver, btnAnalyze, btnAutoProbe;
    Label lblDetected;
    // immagine
    ComboBox cbAspect, cbLevels, cbScope;
    CheckBox chkDenoise, chkChromaNR, chkGrid;
    NumericUpDown nChromaShift, nHeadSwitch;
    // audio
    ComboBox cbRateHz, cbChannels, cbMic;
    NumericUpDown nGain, nAudioDelay;
    CheckBox chkMic;
    Label lblClip;
    // registrazione
    ComboBox cbFormat, cbCodec, cbRateCtl, cbEncPreset, cbProfile, cbTune, cbOutSize, cbACodec;
    NumericUpDown nQuality, nRecBitrate, nKeySec, nAKbps, nSplit, nSplitGB, nStopAfter, nStopGB, nStopDrops, nPreroll, nReplaySec;
    CheckBox chkInterlaced, chkArchive, chkReplay, chkSchedule;
    Label lblEstimate;
    // file
    TextBox tbTemplate, tbTitle, tbTape, tbNotes, tbCopyTo;
    CheckBox chkDateFolder, chkChapters, chkRemux, chkVerify, chkMd5, chkAutoCompress, chkAutoSplit, chkCopyTo;
    Button btnCopyTo;
    // elenco file

    // ---- stato
    bool[] encOk;
    double freeGbCache = -1;
    DeviceCaps caps;
    string capsFor;
    Consumer arch;
    string archFile;
    int recPart;
    long dropAtRecStart;
    int prerollUsed;
    string detected;
    DateTime clipUntil = DateTime.MinValue;
    string profileKey;
    bool hotplugPending;
    DateTime hotplugAt;
    readonly object pumpLock = new object();
    readonly Queue<KeyValuePair<string, Action>> postQueue = new Queue<KeyValuePair<string, Action>>();
    bool postWorker;

    const string TestSourceName = "(Sorgente di prova: barre colore)";

    static NumericUpDown NumNeg(int min, int max, int val) { return new NumericUpDown { Minimum = min, Maximum = max, Value = val, Width = 64 }; }

    // ================================================================= Impostazioni
    void LoadSettings()
    {
        loading = true;
        cbStd.SelectedIndex = Clamp(cfg.GetInt("std", 0), 0, 6);
        SetComboText(cbSize, cfg.Get("size", "Auto")); SetComboText(cbFps, cfg.Get("fps", "Auto")); SetComboText(cbPixFmt, cfg.Get("pixfmt", "Auto"));
        cbDeint.SelectedIndex = Clamp(cfg.GetInt("deint2", 0), 0, 5);
        chkDriver.Checked = cfg.GetBool("driverdlg", false);
        chkDriverMem.Checked = cfg.GetBool("drivermem", true);
        nRtBuf.Value = Clamp(cfg.GetInt("rtbuf", 256), 32, 2048);
        chkLowLat.Checked = cfg.GetBool("lowlat", false);
        chkCfr.Checked = cfg.GetBool("cfr", true);
        tbBright.Value = Clamp(cfg.GetInt("bright", 0), -50, 50);
        tbContrast.Value = Clamp(cfg.GetInt("contrast", 100), 50, 150);
        tbSat.Value = Clamp(cfg.GetInt("sat", 100), 0, 200);
        nCropL.Value = ClampD(cfg.GetDec("cropL", 0), 0, 25); nCropR.Value = ClampD(cfg.GetDec("cropR", 0), 0, 25);
        nCropT.Value = ClampD(cfg.GetDec("cropT", 0), 0, 25); nCropB.Value = ClampD(cfg.GetDec("cropB", 0), 0, 25);
        cbAspect.SelectedIndex = Clamp(cfg.GetInt("aspectmode", 1), 0, 2);
        cbLevels.SelectedIndex = Clamp(cfg.GetInt("levels", 0), 0, 2);
        chkDenoise.Checked = cfg.GetBool("denoise", false);
        chkChromaNR.Checked = cfg.GetBool("chromanr", false);
        nChromaShift.Value = Clamp(cfg.GetInt("chromashift", 0), -8, 8);
        nHeadSwitch.Value = Clamp(cfg.GetInt("headswitch", 0), 0, 24);
        chkSharp.Checked = cfg.GetBool("sharp", false);
        cbPreviewSync.SelectedIndex = Math.Min(1, Math.Max(0, cfg.GetInt("previewsync", 0)));
        chkGpuLive.Checked = cfg.GetBool("gpulive", true);
        chkScan.Checked = cfg.GetBool("scan", false);
        chkIntScale.Checked = cfg.GetBool("intscale", false);
        cbScope.SelectedIndex = Clamp(cfg.GetInt("scope", 0), 0, 3);
        chkGrid.Checked = cfg.GetBool("grid", false);
        chkCam.Checked = cfg.GetBool("cam", false);
        cbCamPos.SelectedIndex = Clamp(cfg.GetInt("campos", 3), 0, 3);
        nCamSize.Value = Clamp(cfg.GetInt("camsize", 25), 10, 50);
        chkLogo.Checked = cfg.GetBool("logo", false);
        tbLogo.Text = cfg.Get("logofile", "");
        cbLogoPos.SelectedIndex = Clamp(cfg.GetInt("logopos", 1), 0, 3);
        chkText.Checked = cfg.GetBool("text", false);
        tbText.Text = cfg.Get("textvalue", "");
        cbTextPos.SelectedIndex = Clamp(cfg.GetInt("textpos", 0), 0, 3);
        tbVolume.Value = Clamp(cfg.GetInt("volume", 100), 0, 200); volume = tbVolume.Value;
        chkListen.Checked = cfg.GetBool("listen", true); listen = chkListen.Checked;
        cbRateHz.SelectedIndex = Clamp(cfg.GetInt("ratehz", 0), 0, 3);
        cbChannels.SelectedIndex = Clamp(cfg.GetInt("channels", 0), 0, 2);
        nGain.Value = Clamp(cfg.GetInt("gain", 0), -20, 20);
        chkADenoise.Checked = cfg.GetBool("adenoise", false);
        chkANorm.Checked = cfg.GetBool("anorm", false);
        chkAudio24.Checked = cfg.GetBool("audio24", false);
        Enc.BottomFirst = cfg.GetBool("bff", false);
        nAudioDelay.Value = Clamp(cfg.GetInt("adelay", 0), -1000, 1000);
        chkMic.Checked = cfg.GetBool("mic", false);
        tbFolder.Text = cfg.Get("folder", Environment.GetFolderPath(Environment.SpecialFolder.MyVideos));
        cbFormat.SelectedIndex = Clamp(cfg.GetInt("format2", 0), 0, cbFormat.Items.Count - 1);
        cbCodec.SelectedIndex = Clamp(cfg.GetInt("codec", 0), 0, Enc.Codecs.Length - 1);
        FillCodecOptions();
        cbRateCtl.SelectedIndex = Clamp(cfg.GetInt("ratectl", 0), 0, 2);
        nQuality.Value = Clamp(cfg.GetInt("quality2", 20), 0, 51);
        nRecBitrate.Value = Clamp(cfg.GetInt("recbitrate", 8000), 500, 100000);
        nKeySec.Value = Clamp(cfg.GetInt("keysec", 2), 1, 10);
        SetComboText(cbEncPreset, cfg.Get("encpreset", Enc.DefaultPreset(Enc.Codecs[cbCodec.SelectedIndex].Family) ?? ""));
        SetComboText(cbProfile, cfg.Get("profile", "auto"));
        SetComboText(cbTune, cfg.Get("tune", ""));
        cbOutSize.SelectedIndex = Clamp(cfg.GetInt("outsize", 0), 0, 2);
        cbACodec.SelectedIndex = Clamp(cfg.GetInt("acodec", 0), 0, 4);
        nAKbps.Value = Clamp(cfg.GetInt("akbps", 192), 64, 512);
        chkInterlaced.Checked = cfg.GetBool("interlaced", false);
        chkArchive.Checked = cfg.GetBool("archive", false);
        nSplit.Value = Clamp(cfg.GetInt("split", 0), 0, 600);
        nSplitGB.Value = ClampD(cfg.GetDec("splitgb", 0), 0, 500);
        nStopAfter.Value = Clamp(cfg.GetInt("stopafter", 0), 0, 1440);
        nStopGB.Value = Clamp(cfg.GetInt("stopgb", 0), 0, 2000);
        nStopDrops.Value = Clamp(cfg.GetInt("stopdrops", 0), 0, 100000);
        nPreroll.Value = Clamp(cfg.GetInt("preroll", 0), 0, 60);
        chkReplay.Checked = cfg.GetBool("replay", false);
        nReplaySec.Value = Clamp(cfg.GetInt("replaysec", 30), 10, 120);
        tbTemplate.Text = cfg.Get("template", "Acquisizione_{data}_{ora}");
        chkDateFolder.Checked = cfg.GetBool("datefolder", false);
        tbTitle.Text = cfg.Get("title", ""); tbTape.Text = cfg.Get("tape", ""); tbNotes.Text = cfg.Get("notes", "");
        chkChapters.Checked = cfg.GetBool("chapters", true);
        chkRemux.Checked = cfg.GetBool("remux", false);
        chkVerify.Checked = cfg.GetBool("verify", false);
        chkMd5.Checked = cfg.GetBool("md5", false);
        chkAutoCompress.Checked = cfg.GetBool("autocompress", false);
        chkAutoSplit.Checked = cfg.GetBool("autosplit", false);
        chkCopyTo.Checked = cfg.GetBool("copyto", false);
        tbCopyTo.Text = cfg.Get("copytodir", "");
        tbRtmp.Text = cfg.GetSecret("rtmp");
        nBitrate.Value = Clamp(cfg.GetInt("bitrate", 3500), 500, 12000);
        chkReconnect.Checked = cfg.GetBool("reconnect", true);
        chkAutoPreview.Checked = cfg.GetBool("autopreview", false);
        chkAutoLive.Checked = cfg.GetBool("autolive", false);
        chkTray.Checked = cfg.GetBool("tray", false);
        chkGlobalKeys.Checked = cfg.GetBool("globalkeys", false);
        useTcp = cfg.Get("transport", "pipe") == "tcp";
        chkDark.Checked = cfg.GetBool("dark", true);
        chkLossMez.Checked = cfg.GetBool("lossmez", true);
        chkAutoLevels.Checked = cfg.GetBool("autolevels", false); chkSharpen.Checked = cfg.GetBool("sharpen", false);
        chkLut.Checked = cfg.GetBool("lut", false); tbLut.Text = cfg.Get("lutfile", "");
        cbScope.SelectedIndex = Clamp(cfg.GetInt("scope", 0), 0, 3); chkGrid.Checked = cfg.GetBool("grid", false);
        tbSceneStart.Text = cfg.Get("scene1", tbSceneStart.Text); tbScenePause.Text = cfg.Get("scene2", tbScenePause.Text);
        tbSceneEnd.Text = cfg.Get("scene3", tbSceneEnd.Text); tbSceneCustom.Text = cfg.Get("scene4", "");
        chkSceneMute.Checked = cfg.GetBool("scenemute", false);
        chkTimer.Checked = cfg.GetBool("timer", false); cbTimerPos.SelectedIndex = Clamp(cfg.GetInt("timerpos", 1), 0, 3);
        chkAutoRecSignal.Checked = cfg.GetBool("autorec", false); chkEndOfTape.Checked = cfg.GetBool("endtape", false);
        nEndTapeSec.Value = Clamp(cfg.GetInt("endtapesec", 30), 5, 600);
        chkTapeSeries.Checked = cfg.GetBool("tapeseries", false); nTapeNumber.Value = Clamp(cfg.GetInt("tapenumber", 1), 1, 9999);
        chkInterval.Checked = false; nIntervalSec.Value = Clamp(cfg.GetInt("intervalsec", 10), 1, 3600);
        chkSounds.Checked = cfg.GetBool("sounds", true); chkNotify.Checked = cfg.GetBool("notify", true);
        chkLiveSecond.Checked = cfg.GetBool("livesecond", false);
        chkReport.Checked = cfg.GetBool("report", false);
        chkStream2.Checked = cfg.GetBool("stream2", false); tbRtmp2.Text = cfg.GetSecret("rtmp2");
        if (keyActions != null) { LoadKeys(); RegisterGlobalKeys(); }      // dopo un ripristino i tasti globali vanno registrati di nuovo
        tbUpdateUrl.Text = cfg.Get("updateurl", ""); chkAutoUpdate.Checked = cfg.GetBool("autoupdate", true);
        cbLang.SelectedIndex = cfg.Get("lang", "it") == "en" ? 1 : 0;
        FillOutputDevices(cfg.Get("outdev", ""));
        WriteOverlayText();
        UpdateReplayState();
        UpdateViewOptions();
        loading = false;
        UpdateEncUi();
    }

    // uscite audio (cuffie, casse): si rileggono anche quando si collega un dispositivo USB
    void FillOutputDevices(string wanted)
    {
        bool was = loading;
        loading = true;
        cbOutDev.Items.Clear();
        cbOutDev.Items.Add(L.T("Predefinita di Windows", "Windows default"));
        foreach (string d in WavePlayer.Devices()) cbOutDev.Items.Add(d);
        int od = cbOutDev.Items.IndexOf(wanted ?? "");
        cbOutDev.SelectedIndex = od > 0 ? od : 0;
        outDevice = cbOutDev.SelectedIndex - 1;
        loading = was;
    }

    static void SetComboText(ComboBox c, string text)
    {
        int i = c.Items.IndexOf(text);
        if (i < 0 && !string.IsNullOrEmpty(text) && text != "Auto" && c.Items.Count > 0 && c.Items[0].ToString() == "Auto") { c.Items.Add(text); i = c.Items.Count - 1; }
        if (i >= 0) c.SelectedIndex = i;
        else if (c.Items.Count > 0 && c.SelectedIndex < 0) c.SelectedIndex = 0;
    }

    // Impostazioni di cattura (anche per profili e per dispositivo)
    static readonly string[] CaptureKeys = { "std", "size", "fps", "pixfmt", "deint2", "aspectmode", "levels", "cropL", "cropR", "cropT", "cropB", "bright", "contrast", "sat", "denoise", "chromanr", "chromashift", "headswitch", "adelay", "gain" };

    void StoreCapture(Settings s)
    {
        s.Set("std", cbStd.SelectedIndex); s.Set("size", cbSize.Text); s.Set("fps", cbFps.Text); s.Set("pixfmt", cbPixFmt.Text);
        s.Set("deint2", cbDeint.SelectedIndex); s.Set("aspectmode", cbAspect.SelectedIndex); s.Set("levels", cbLevels.SelectedIndex);
        s.Set("cropL", nCropL.Value); s.Set("cropR", nCropR.Value); s.Set("cropT", nCropT.Value); s.Set("cropB", nCropB.Value);
        s.Set("bright", tbBright.Value); s.Set("contrast", tbContrast.Value); s.Set("sat", tbSat.Value);
        s.Set("denoise", chkDenoise.Checked); s.Set("chromanr", chkChromaNR.Checked); s.Set("chromashift", nChromaShift.Value); s.Set("headswitch", nHeadSwitch.Value);
        s.Set("adelay", nAudioDelay.Value); s.Set("gain", nGain.Value);
    }

    void StoreSettings(Settings s, bool full)
    {
        s.Set("video", cbVideo.SelectedItem ?? s.Get("video", ""));
        s.Set("audio", cbAudio.SelectedItem ?? s.Get("audio", ""));
        s.Set("input", InputKey());
        StoreCapture(s);
        s.Set("driverdlg", chkDriver.Checked); s.Set("drivermem", chkDriverMem.Checked); s.Set("rtbuf", nRtBuf.Value);
        s.Set("lowlat", chkLowLat.Checked); s.Set("cfr", chkCfr.Checked); s.Set("lossmez", chkLossMez.Checked);
        s.Set("sharp", chkSharp.Checked); s.Set("previewsync", cbPreviewSync.SelectedIndex); s.Set("gpulive", chkGpuLive.Checked); s.Set("scan", chkScan.Checked); s.Set("intscale", chkIntScale.Checked);
        s.Set("scope", cbScope.SelectedIndex); s.Set("grid", chkGrid.Checked);
        s.Set("adenoise", chkADenoise.Checked); s.Set("anorm", chkANorm.Checked); s.Set("audio24", chkAudio24.Checked);
        s.Set("ratehz", cbRateHz.SelectedIndex); s.Set("channels", cbChannels.SelectedIndex);
        s.Set("format2", cbFormat.SelectedIndex); s.Set("codec", cbCodec.SelectedIndex); s.Set("ratectl", cbRateCtl.SelectedIndex);
        s.Set("quality2", nQuality.Value); s.Set("recbitrate", nRecBitrate.Value); s.Set("keysec", nKeySec.Value);
        s.Set("encpreset", cbEncPreset.Text); s.Set("profile", cbProfile.Text); s.Set("tune", cbTune.Text);
        s.Set("outsize", cbOutSize.SelectedIndex); s.Set("acodec", cbACodec.SelectedIndex); s.Set("akbps", nAKbps.Value);
        s.Set("interlaced", chkInterlaced.Checked);
        if (!full) return;
        s.Set("mic", chkMic.Checked); s.Set("micdev", cbMic.SelectedItem ?? s.Get("micdev", ""));
        s.Set("cam", chkCam.Checked); s.Set("camdev", cbCam.SelectedItem ?? s.Get("camdev", "")); s.Set("campos", cbCamPos.SelectedIndex); s.Set("camsize", nCamSize.Value);
        s.Set("logo", chkLogo.Checked); s.Set("logofile", tbLogo.Text); s.Set("logopos", cbLogoPos.SelectedIndex);
        s.Set("text", chkText.Checked); s.Set("textvalue", tbText.Text); s.Set("textpos", cbTextPos.SelectedIndex);
        s.Set("volume", tbVolume.Value); s.Set("listen", chkListen.Checked);
        s.Set("outdev", cbOutDev.SelectedIndex > 0 ? cbOutDev.SelectedItem : "");
        s.Set("folder", tbFolder.Text);
        s.Set("archive", chkArchive.Checked);
        s.Set("split", nSplit.Value); s.Set("splitgb", nSplitGB.Value); s.Set("stopafter", nStopAfter.Value); s.Set("stopgb", nStopGB.Value); s.Set("stopdrops", nStopDrops.Value);
        s.Set("preroll", nPreroll.Value); s.Set("replay", chkReplay.Checked); s.Set("replaysec", nReplaySec.Value);
        s.Set("template", tbTemplate.Text); s.Set("datefolder", chkDateFolder.Checked);
        s.Set("title", tbTitle.Text); s.Set("tape", tbTape.Text); s.Set("notes", tbNotes.Text);
        s.Set("chapters", chkChapters.Checked); s.Set("remux", chkRemux.Checked); s.Set("verify", chkVerify.Checked); s.Set("md5", chkMd5.Checked);
        s.Set("autocompress", chkAutoCompress.Checked); s.Set("autosplit", chkAutoSplit.Checked);
        s.Set("copyto", chkCopyTo.Checked); s.Set("copytodir", tbCopyTo.Text);
        s.SetSecret("rtmp", tbRtmp.Text.Trim());
        s.Set("bitrate", nBitrate.Value); s.Set("reconnect", chkReconnect.Checked);
        s.Set("autopreview", chkAutoPreview.Checked); s.Set("autolive", chkAutoLive.Checked);
        s.Set("tray", chkTray.Checked); s.Set("globalkeys", chkGlobalKeys.Checked); s.Set("dark", chkDark.Checked);
        s.Set("lang", cbLang.SelectedIndex == 1 ? "en" : "it");
        s.Set("transport", useTcp ? "tcp" : "pipe");
        if (keyActions != null) StoreKeys(s);
        s.Set("updateurl", tbUpdateUrl.Text.Trim()); s.Set("autoupdate", chkAutoUpdate.Checked);
        s.Set("autolevels", chkAutoLevels.Checked); s.Set("sharpen", chkSharpen.Checked); s.Set("lut", chkLut.Checked); s.Set("lutfile", tbLut.Text);
        s.Set("scene1", tbSceneStart.Text); s.Set("scene2", tbScenePause.Text); s.Set("scene3", tbSceneEnd.Text); s.Set("scene4", tbSceneCustom.Text);
        s.Set("scenemute", chkSceneMute.Checked); s.Set("timer", chkTimer.Checked); s.Set("timerpos", cbTimerPos.SelectedIndex);
        s.Set("autorec", chkAutoRecSignal.Checked); s.Set("endtape", chkEndOfTape.Checked); s.Set("endtapesec", nEndTapeSec.Value);
        s.Set("tapeseries", chkTapeSeries.Checked); s.Set("tapenumber", nTapeNumber.Value); s.Set("intervalsec", nIntervalSec.Value);
        s.Set("sounds", chkSounds.Checked); s.Set("notify", chkNotify.Checked); s.Set("livesecond", chkLiveSecond.Checked);
        s.Set("report", chkReport.Checked); s.Set("stream2", chkStream2.Checked); s.SetSecret("rtmp2", tbRtmp2.Text.Trim());
        if (profileKey != null) SaveDeviceProfile(profileKey);
    }

    // ---- profili per dispositivo + ingresso
    void SaveDeviceProfile(string key)
    {
        var tmp = new Settings(Path.Combine(Settings.AppDir, "__nessuno__.ini"));
        tmp.D.Clear();
        StoreCapture(tmp);
        foreach (var kv in tmp.D) cfg.D["dev[" + key + "]." + kv.Key] = kv.Value;
    }

    bool LoadDeviceProfile(string key)
    {
        string prefix = "dev[" + key + "].";
        bool any = false;
        foreach (string k in CaptureKeys) if (cfg.D.ContainsKey(prefix + k)) { cfg.D[k] = cfg.D[prefix + k]; any = true; }
        if (!any) return false;
        bool was = loading;
        loading = true;
        cbStd.SelectedIndex = Clamp(cfg.GetInt("std", 0), 0, 6);
        SetComboText(cbSize, cfg.Get("size", "Auto")); SetComboText(cbFps, cfg.Get("fps", "Auto")); SetComboText(cbPixFmt, cfg.Get("pixfmt", "Auto"));
        cbDeint.SelectedIndex = Clamp(cfg.GetInt("deint2", 0), 0, 5);
        cbAspect.SelectedIndex = Clamp(cfg.GetInt("aspectmode", 1), 0, 2);
        cbLevels.SelectedIndex = Clamp(cfg.GetInt("levels", 0), 0, 2);
        nCropL.Value = ClampD(cfg.GetDec("cropL", 0), 0, 25); nCropR.Value = ClampD(cfg.GetDec("cropR", 0), 0, 25);
        nCropT.Value = ClampD(cfg.GetDec("cropT", 0), 0, 25); nCropB.Value = ClampD(cfg.GetDec("cropB", 0), 0, 25);
        tbBright.Value = Clamp(cfg.GetInt("bright", 0), -50, 50); tbContrast.Value = Clamp(cfg.GetInt("contrast", 100), 50, 150); tbSat.Value = Clamp(cfg.GetInt("sat", 100), 0, 200);
        chkDenoise.Checked = cfg.GetBool("denoise", false); chkChromaNR.Checked = cfg.GetBool("chromanr", false);
        nChromaShift.Value = Clamp(cfg.GetInt("chromashift", 0), -8, 8); nHeadSwitch.Value = Clamp(cfg.GetInt("headswitch", 0), 0, 24);
        nAudioDelay.Value = Clamp(cfg.GetInt("adelay", 0), -1000, 1000); nGain.Value = Clamp(cfg.GetInt("gain", 0), -20, 20);
        loading = was;
        return true;
    }

    string CurrentProfileKey()
    {
        if (cbVideo.SelectedItem == null) return null;
        return cbVideo.SelectedItem + "|" + InputKey();
    }

    // nome dell'ingresso indipendente dalla lingua (la voce "Predefinito" diventa "Default" in inglese)
    string InputKey()
    {
        return cbInput.SelectedIndex <= 0 ? "Predefinito" : cbInput.Text;
    }

    void OnDeviceChanged()
    {
        if (loading) return;
        // altro dispositivo: i ripieghi decisi per il precedente (16 bit, formato automatico, memoria driver) si riprovano
        audio24Failed = false; formatFallback = false; driverMemFailed = false;
        string key = CurrentProfileKey();
        if (profileKey != null && key != profileKey) SaveDeviceProfile(profileKey);
        if (key != null && key != profileKey && LoadDeviceProfile(key)) SetStatus(L.T("Impostazioni ricordate per questo ingresso.", "Settings restored for this input."), false, 4);
        profileKey = key;
        if (cbVideo.SelectedItem as string != capsFor) LoadCapsAsync();
        UpdateViewOptions();
        ScheduleApply();
    }

    void OnStdChanged()
    {
        if (loading) { UpdateViewOptions(); return; }
        int i = cbStd.SelectedIndex;
        bool was = loading;
        loading = true;
        if (i == 0 || i == 3 || i == 4) { SetComboText(cbSize, "720x576"); SetComboText(cbFps, "25"); }
        else if (i == 1 || i == 2) { SetComboText(cbSize, "720x480"); SetComboText(cbFps, DeviceCaps.RateLabel("30000/1001")); }
        else if (i == 5) { SetComboText(cbSize, "Auto"); SetComboText(cbFps, "Auto"); }
        loading = was;
        if (i <= 4) SetStatus(L.T("Lo standard esatto (es. PAL_B o PAL_I) si sceglie in 'Impostazioni driver' e viene ricordato.", "The exact standard (e.g. PAL_B or PAL_I) is set in 'Driver settings' and remembered."), false, 6);
        ScheduleApply();
    }

    void ApplyPreset(int i)
    {
        if (loading || i <= 0) return;
        string name = cbPreset.Items[i].ToString();
        // lo standard (PAL/NTSC) resta quello scelto: una console NTSC non va forzata a PAL
        switch (i)
        {
            case 1: cbDeint.SelectedIndex = 5; chkDenoise.Checked = false; chkSharp.Checked = true; chkLowLat.Checked = true; cbAspect.SelectedIndex = 1; cbPreviewSync.SelectedIndex = 0; break;
            case 2: cbDeint.SelectedIndex = 3; chkDenoise.Checked = false; chkSharp.Checked = false; chkLowLat.Checked = true; cbAspect.SelectedIndex = 1; cbPreviewSync.SelectedIndex = 0; break;
            case 3: cbDeint.SelectedIndex = 3; chkDenoise.Checked = true; chkChromaNR.Checked = true; chkSharp.Checked = false; chkLowLat.Checked = false; cbAspect.SelectedIndex = 1; nHeadSwitch.Value = Math.Max(nHeadSwitch.Value, 8); cbPreviewSync.SelectedIndex = 1; break;
            case 4: cbDeint.SelectedIndex = 3; chkDenoise.Checked = false; chkSharp.Checked = false; chkLowLat.Checked = false; cbAspect.SelectedIndex = 1; break;
            case 5:
                cbDeint.SelectedIndex = 0; chkInterlaced.Checked = true; cbAspect.SelectedIndex = 2; chkLossMez.Checked = true;
                cbCodec.SelectedIndex = 12; cbFormat.SelectedIndex = 0; cbACodec.SelectedIndex = 2; chkDenoise.Checked = false; chkChromaNR.Checked = false;
                break;
        }
        SetStatus(L.T("Preimpostazione applicata: ", "Preset applied: ") + name, false, 4);
        loading = true; cbPreset.SelectedIndex = 0; loading = false;
    }
}
