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
    // ================================================================= Opzioni di cattura
    string SelectedFpsValue()
    {
        string t = cbFps.Text;
        if (t == "Auto" || t.Length == 0) return null;
        if (t == "29,97") return "30000/1001";
        if (t == "59,94") return "60000/1001";
        if (t == "23,976") return "24000/1001";
        return t.Replace(',', '.');
    }

    int SourceHeight()
    {
        Match m = Regex.Match(cbSize.Text, @"(\d+)x(\d+)");
        if (m.Success) return int.Parse(m.Groups[2].Value);
        if (detectedH > 0) return detectedH;
        return cbStd.SelectedIndex == 1 || cbStd.SelectedIndex == 2 ? 480 : 576;
    }

    int detectedH, detectedW;

    int Fps()
    {
        string f = SelectedFpsValue();
        double v = 0;
        if (f != null)
        {
            if (f.Contains("/")) { string[] p = f.Split('/'); v = double.Parse(p[0], CultureInfo.InvariantCulture) / double.Parse(p[1], CultureInfo.InvariantCulture); }
            else double.TryParse(f, NumberStyles.Float, CultureInfo.InvariantCulture, out v);
        }
        if (v <= 0) v = SourceHeight() == 480 ? 30 : 25;
        int b = (int)Math.Round(v);
        return Cmd.IsBob(cbDeint.SelectedIndex) && !chkInterlaced.Checked ? b * 2 : b;
    }

    float OutputAspect()
    {
        int h = SourceHeight();
        // senza correzione: proporzioni dei pixel ricevuti (larghezza reale se nota)
        Match ms = Regex.Match(cbSize.Text, @"(\d+)x(\d+)");
        int w = ms.Success ? int.Parse(ms.Groups[1].Value) : detectedW > 0 ? detectedW : 720;
        float a = cbAspect.SelectedIndex > 0 ? 4f / 3f : (float)w / h;
        decimal cw = Math.Max(0.5m, 1 - (nCropL.Value + nCropR.Value) / 100m);
        decimal ch = Math.Max(0.5m, 1 - (nCropT.Value + nCropB.Value) / 100m);
        return a * (float)(cw / ch);
    }

    void UpdateViewOptions()
    {
        foreach (VideoView v in new[] { view, liveView, miniView })
        {
            if (v == null) continue;
            v.ShowGrid = v == view && chkGrid.Checked;
            v.Scope = v == view ? Math.Max(0, cbScope.SelectedIndex) : 0;
            v.Aspect = OutputAspect();
            v.Sharp = chkSharp.Checked;
            v.Scanlines = chkScan.Checked;
            v.IntScale = chkIntScale.Checked;
            decimal ch = Math.Max(0.5m, 1 - (nCropT.Value + nCropB.Value) / 100m);
            v.BaseLines = Math.Max(100, (int)(SourceHeight() / 2 * ch));
            v.Invalidate();
        }
        if (glLive != null) { glLive.Aspect = OutputAspect(); glLive.Sharp = chkSharp.Checked; glLive.Scanlines = chkScan.Checked; glLive.BaseLines = view.BaseLines; glLive.IntScale = chkIntScale.Checked; glLive.Invalidate(); }
        previewSync = cbPreviewSync != null ? Math.Max(0, cbPreviewSync.SelectedIndex) : 0;
    }

    string DriverFile()
    {
        string key = (cbVideo.SelectedItem as string ?? "") + "|" + InputKey();
        return Path.Combine(Settings.AppDir, "driver", StableHash(key) + ".bin");
    }

    bool driverMemFailed, forceDialogs;

    CaptureOptions BuildOptions(bool dialogs, bool previewAudio)
    {
        var o = new CaptureOptions();
        o.TestSource = IsTestSource;
        o.Video = o.TestSource ? null : DevName(cbVideo.SelectedItem);
        o.Audio = o.TestSource ? null : (cbAudio.SelectedIndex > 0 ? DevName(cbAudio.SelectedItem) : null);
        Match m = Regex.Match(cbSize.Text, @"(\d+)x(\d+)");
        if (m.Success) { o.Width = int.Parse(m.Groups[1].Value); o.Height = int.Parse(m.Groups[2].Value); }
        o.Fps = SelectedFpsValue();
        o.PixFmt = cbPixFmt.Text == "Auto" || cbPixFmt.Text.Length == 0 ? null : cbPixFmt.Text;
        o.RtBufMB = (int)nRtBuf.Value;
        int ap;
        o.VideoPin = SelectedPin(out ap);
        o.AudioPin = o.VideoPin >= 0 ? ap : -1;
        if (chkDriverMem.Checked && !driverMemFailed && !o.TestSource)
        {
            string f = DriverFile();
            try { Directory.CreateDirectory(Path.GetDirectoryName(f)); } catch { }
            o.DevLoad = File.Exists(f) && !dialogs ? f : null;
            o.DevSave = f;
        }
        o.Dialogs = dialogs && !o.TestSource && (chkDriver.Checked || forceDialogs);
        forceDialogs = false;
        o.SampleRate = cbRateHz.SelectedIndex == 1 ? 48000 : cbRateHz.SelectedIndex == 2 ? 44100 : cbRateHz.SelectedIndex == 3 ? 32000 : 0;
        o.Channels = cbChannels.SelectedIndex == 1 ? 2 : cbChannels.SelectedIndex == 2 ? 1 : 0;
        o.Deint = cbDeint.SelectedIndex;
        o.KeepInterlaced = chkInterlaced.Checked;
        o.AspectMode = cbAspect.SelectedIndex;
        o.CropL = nCropL.Value; o.CropR = nCropR.Value; o.CropT = nCropT.Value; o.CropB = nCropB.Value;
        o.Levels = cbLevels.SelectedIndex;
        o.Denoise = chkDenoise.Checked; o.ChromaNR = chkChromaNR.Checked;
        o.ChromaShift = (int)nChromaShift.Value; o.HeadSwitch = (int)nHeadSwitch.Value;
        o.Bright = tbBright.Value; o.Contrast = tbContrast.Value; o.Sat = tbSat.Value;
        if (chkCam.Checked && cbCam.SelectedItem != null && (cbCam.SelectedItem as string) != (cbVideo.SelectedItem as string)) o.Cam = DevName(cbCam.SelectedItem);
        o.CamPos = cbCamPos.SelectedIndex; o.CamSize = (int)nCamSize.Value;
        if (chkMic.Checked && cbMic.SelectedItem != null && (cbMic.SelectedItem as string) != (cbAudio.SelectedItem as string)) o.Mic = DevName(cbMic.SelectedItem);
        if (chkLogo.Checked && File.Exists(tbLogo.Text.Trim())) { o.Logo = tbLogo.Text.Trim(); o.LogoPos = cbLogoPos.SelectedIndex; }
        if (chkText.Checked) o.TextPos = cbTextPos.SelectedIndex;
        o.Scope = cbScope.SelectedIndex; o.Grid = chkGrid.Checked;
        o.PreviewAudio = previewAudio;
        o.AudioDenoise = chkADenoise.Checked; o.AudioNorm = chkANorm.Checked; o.Audio24 = chkAudio24.Checked && !audio24Failed; o.LowLatency = chkLowLat.Checked;
        o.Cfr = chkCfr.Checked && o.Fps != null;
        o.GainDb = (double)nGain.Value;
        o.AudioDelay = (int)nAudioDelay.Value;
        o.LosslessMez = chkLossMez.Checked;
        o.Scope = 0; o.Grid = false;
        o.OverlayText = chkText.Checked ? tbText.Text : null;
        o.Scene = sceneIndex; o.SceneText = SceneText(sceneIndex); o.SceneMute = chkSceneMute.Checked;
        o.Timer = chkTimer.Checked; o.TimerPos = Math.Max(0, cbTimerPos.SelectedIndex); o.TimerText = FormatTimer(TimerValue);
        o.AutoLevels = chkAutoLevels.Checked; o.Sharpen = chkSharpen.Checked;
        o.Lut = chkLut.Checked && File.Exists(tbLut.Text.Trim()) ? tbLut.Text.Trim() : null;
        // con la sorgente di prova webcam e microfono non vengono aperti: non vanno nemmeno considerati presenti
        if (o.TestSource) { o.Mic = null; o.Cam = null; }
        if (formatFallback) { o.Width = o.Height = 0; o.Fps = null; o.PixFmt = null; o.Cfr = false; }
        if (skipMic) o.Mic = null;
        if (skipCam) o.Cam = null;
        if (Cmd.FontFile() == null && (o.OverlayText != null || o.Timer))
            SetStatus(L.T("Nessun carattere di sistema trovato (Arial/Segoe): testo e timer non verranno mostrati.", "No system font found: text and timer will not be shown."), true, 8);
        return o;
    }

    bool capHasMic, audio24Failed, suppressInputChange;
    int capVideoPin = -1;
    string capMicName, capCamName;
    bool skipMic, skipCam, formatFallback;

    bool StartCapture(bool dialogs) { return StartCapture(dialogs, true); }

    bool StartCapture(bool dialogs, bool previewAudio)
    {
        if (cap != null) return true;
        if (cbVideo.SelectedItem == null) { SetStatus(L.T("Seleziona un dispositivo video (premi Aggiorna).", "Select a video device (press Refresh)."), true, 6); return false; }
        lock (capErr) capErr.Clear();
        dropped = 0; frames = 0; fps = 0; detected = null;
        replay.Clear();
        string id = Guid.NewGuid().ToString("N").Substring(0, 12);
        CaptureOptions o = BuildOptions(dialogs, previewAudio);
        capAudio = o.Audio != null || o.TestSource;
        capHasMic = o.Mic != null;
        capAudio24 = o.Audio24;
        capVideoPin = o.VideoPin;
        capMicName = o.Mic; capCamName = o.Cam;
        capPreviewAudio = capAudio && previewAudio;
        o.PreviewAudio = capPreviewAudio;
        try
        {
            epM = Endpoint.Create(useTcp, "acqv_m_" + id, 4 << 20);
            epA = capPreviewAudio ? Endpoint.Create(useTcp, "acqv_a_" + id, 1 << 16) : null;
        }
        catch (Exception ex)
        {
            ClosePipes();
            Msg(L.T("Impossibile creare i canali interni:\n", "Cannot create internal pipes:\n") + ex.Message);
            return false;
        }
        o.OutM = epM.Target;
        o.OutA = epA != null ? epA.Target : null;
        lastLosslessMez = o.LosslessMez; lastKeepInterlaced = o.KeepInterlaced;
        string args = Cmd.Capture(o);
        Log.W("Cattura (" + (useTcp ? "tcp" : "pipe") + "): ffmpeg " + args);
        var psi = new ProcessStartInfo(ffmpeg, args);
        psi.UseShellExecute = false; psi.CreateNoWindow = true;
        psi.RedirectStandardInput = true; psi.RedirectStandardOutput = true; psi.RedirectStandardError = true;
        psi.StandardErrorEncoding = Encoding.UTF8;
        var p = new Process { StartInfo = psi, EnableRaisingEvents = true };
        p.ErrorDataReceived += delegate (object s, DataReceivedEventArgs e)
        {
            if (e.Data == null) return;
            if (e.Data.Contains("frame dropped")) Interlocked.Increment(ref dropped);
            lock (capErr) { capErr.AppendLine(e.Data); if (capErr.Length > 30000) capErr.Remove(0, 15000); }
            if (detected == null && e.Data.Contains("Stream #0:0") && e.Data.Contains("Video:"))
            {
                int w, h; double f;
                string d = DeviceCaps.Detected(e.Data, out w, out h, out f);
                if (d != null) { detected = d; detectedH = h; detectedW = w; Invoke2(delegate { OnDetected(w, h, f); }); }
            }
        };
        p.Exited += delegate { OnCaptureExited(p); };
        try { p.Start(); }
        catch (Exception ex)
        {
            ClosePipes();
            Msg(L.T("Impossibile avviare ffmpeg.exe:\n", "Cannot start ffmpeg.exe:\n") + ex.Message);
            return false;
        }
        p.BeginErrorReadLine();
        cap = p;
        capStopping = false;
        capStart = DateTime.Now;
        lastFrame = DateTime.MinValue;
        hadSignal = false;
        StartCommandSender(p);
        StartFrameDelay();
        if (o.OverlayText != null) SendCmd("drawtext@ovl", "reinit", DrawTextArg(o.OverlayText));
        if (o.Timer) SendCmd("drawtext@timer", "reinit", DrawTextArg(o.TimerText));
        if (o.Scene > 0) SendCmd("drawtext@scene", "reinit", DrawTextArg(o.SceneText));
        Native.AddToJob(p);
        StartThread("anteprima", delegate { PreviewLoop(p); });
        if (chkLiveSecond.Checked && liveForm == null && Screen.AllScreens.Length > 1)
        {
            Screen mine = Screen.FromControl(this);
            foreach (Screen sc in Screen.AllScreens) if (!sc.Equals(mine)) { var target = sc; BeginInvoke((MethodInvoker)delegate { if (liveForm == null && cap != null) ToggleLive(target); }); break; }
        }
        var pm = epM;
        StartThread("flusso", delegate { PumpLoop(pm); });
        if (epA != null) { var pa = epA; StartThread("audio", delegate { AudioLoop(pa); }); }
        if (o.Dialogs)
        {
            int pid = p.Id;
            StartThread("dialoghi", delegate { try { for (int i = 0; i < 40; i++) { Thread.Sleep(250); if (Native.FocusProcessWindows(pid) > 0) break; } } catch (Exception ex) { Log.W("Dialoghi: " + ex.Message); } });
        }
        SetStatus(StateText(), false, 0);
        SetUi();
        return true;
    }

    // Formato ricevuto: avviso se non corrisponde allo standard scelto
    void OnDetected(int w, int h, double f)
    {
        lblDetected.Text = L.T("Ricevuto: ", "Receiving: ") + detected;
        UpdateViewOptions();
        int std = cbStd.SelectedIndex;
        bool expect576 = std == 0 || std == 3 || std == 4, expect480 = std == 1 || std == 2;
        if ((expect576 && h == 480) || (expect480 && h == 576))
            SetStatus(L.T("Attenzione: il grabber invia ", "Warning: the grabber sends ") + w + "x" + h + L.T(" ma lo standard scelto è diverso. Controlla 'Standard' o le impostazioni driver.", " but the chosen standard differs. Check 'Standard' or driver settings."), true, 12);
    }

    void OnCaptureExited(Process p)
    {
        string err;
        lock (capErr) err = capErr.ToString().Trim();
        Log.W("Cattura terminata, codice " + SafeExit(p) + "\r\n" + Tail(err, 6000));
        Invoke2(delegate
        {
            if (p != cap) return;
            cap = null;
            ClosePipes();
            StopFrameDelay();
            if (glLive != null) glLive.ClearFrame();
            if (cmdQueue != null) { try { cmdQueue.CompleteAdding(); } catch { } cmdQueue = null; }
            store.Clear();
            view.Invalidate();
            bool early = (DateTime.Now - capStart).TotalSeconds < 8;
            bool canRetry = early && rec == null && str == null;
            // 0) l'ingresso scelto (Composito / S-Video) non si può collegare: si riprova con quello predefinito del driver
            if (canRetry && capVideoPin >= 0 && (err.Contains("Could not setup CrossBar") || err.Contains("Unable to route")))
            {
                Log.W("Ingresso crossbar " + capVideoPin + " non utilizzabile: uso l'ingresso predefinito");
                try { File.Delete(DriverFile()); } catch { }
                suppressInputChange = true;
                try { cbInput.SelectedIndex = 0; } finally { suppressInputChange = false; }
                QueueSave();
                if (StartCapture(false, capPreviewAudio))
                {
                    SetStatus(L.T("L'ingresso scelto non è utilizzabile con questo grabber: uso quello predefinito. Se l'immagine è nera, cambia ingresso dalle impostazioni del driver.", "The chosen input cannot be used with this grabber: using the default one."), true, 12);
                    return;
                }
            }
            // 0b) microfono o webcam scollegati/occupati: l'anteprima del grabber parte lo stesso senza di loro
            bool micErr = capMicName != null && err.Contains("audio=" + capMicName);
            bool camErr = capCamName != null && err.Contains("video=" + capCamName);
            if (canRetry && (micErr || camErr))
            {
                if (micErr) skipMic = true;
                if (camErr) skipCam = true;
                Log.W("Dispositivo aggiuntivo non disponibile: " + (micErr ? capMicName : capCamName));
                if (StartCapture(false, capPreviewAudio))
                {
                    SetStatus((micErr ? L.T("Microfono", "Microphone") : "Webcam") + L.T(" non disponibile: anteprima avviata senza. Ricontrolla il collegamento.", " not available: preview started without it."), true, 12);
                    return;
                }
            }
            // 0c) formato scelto rifiutato dal grabber: si riprova lasciando decidere il driver
            string el = err.ToLowerInvariant();
            if (canRetry && !formatFallback && !IsTestSource && (el.Contains("could not set video options") || el.Contains("could not renderstream")) && (cbSize.SelectedIndex > 0 || cbFps.SelectedIndex > 0 || cbPixFmt.SelectedIndex > 0))
            {
                formatFallback = true;
                Log.W("Formato rifiutato dal grabber: riprovo con formato automatico");
                if (StartCapture(false, capPreviewAudio))
                {
                    SetStatus(L.T("Il grabber non accetta risoluzione/fps scelti: uso il formato automatico (vedi 'Ricevuto').", "The grabber rejects the chosen size/fps: using automatic format."), true, 12);
                    return;
                }
            }
            // 1) memoria driver non supportata dal dispositivo
            if (canRetry && chkDriverMem.Checked && !driverMemFailed && (err.Contains("IPersistStream") || err.Contains("Could not save capture filter") || err.Contains("Could not load capture filter")))
            {
                driverMemFailed = true;
                Log.W("Memoria driver non supportata: disattivata per questa sessione");
                try { File.Delete(DriverFile()); } catch { }
                if (StartCapture(false, capPreviewAudio)) { SetStatus(L.T("Questo driver non permette di ricordare le impostazioni.", "This driver cannot remember settings."), true, 8); return; }
            }
            // 2) canali interni: passa a TCP
            bool pipeErr = err.Contains("pipe") || err.Contains("Error opening output");
            if (canRetry && pipeErr && !useTcp)
            {
                useTcp = true;
                cfg.Set("transport", "tcp");
                Log.W("Canali named pipe non funzionanti: passo a TCP locale");
                if (StartCapture(false, capPreviewAudio)) { SetStatus(L.T("Canale interno cambiato (TCP locale).", "Internal channel switched (local TCP)."), false, 6); return; }
            }
            // 2b) il dispositivo non accetta 24 bit: torna a 16
            if (canRetry && capAudio24 && !audio24Failed && (err.Contains("sample_size") || err.Contains("Could not set audio options") || err.Contains("audio format")))
            {
                audio24Failed = true;
                Log.W("24 bit non supportati dal dispositivo audio");
                if (StartCapture(false, capPreviewAudio)) { SetStatus(L.T("Il dispositivo non fornisce audio a 24 bit: uso 16 bit.", "Device has no 24-bit audio: using 16-bit."), true, 8); return; }
            }
            // 3) audio anteprima
            if (canRetry && capPreviewAudio && !triedNoPreviewAudio && pipeErr)
            {
                triedNoPreviewAudio = true;
                if (StartCapture(false, false)) { SetStatus(L.T("Audio in anteprima non disponibile: registrazione e diretta mantengono l'audio.", "Preview audio unavailable."), true, 10); return; }
            }
            bool hadRec = rec != null, hadStr = str != null;
            if (rec != null) StopRecordingCore(L.T("segnale interrotto", "signal stopped"));
            if (str != null) { wantStream = false; var s = str; lock (pumpLock) str = null; s.Kill(); }
            StopStreamer2();
            CloseLive();
            SetUi();
            RefreshFiles();
            string what = hadRec ? L.T("Registrazione interrotta (il file è stato salvato fino a qui)", "Recording interrupted (file saved up to here)") : hadStr ? L.T("Diretta interrotta", "Stream interrupted") : L.T("Anteprima interrotta", "Preview interrupted");
            SetStatus(what + ".", true, 15);
            if (!closing) Msg(what + ".\n\n" + Explain(err));
        });
    }

    void OpenDriverDialogs()
    {
        if (rec != null || str != null || capStopping || IsTestSource) return;
        StopCapture(delegate { forceDialogs = true; StartCapture(true); });
    }

    void ScheduleApply()
    {
        UpdateViewOptions();
        UpdateEstimate();
        if (loading || cap == null || rec != null || str != null) return;
        applyTimer.Stop();
        applyTimer.Start();
    }
}
