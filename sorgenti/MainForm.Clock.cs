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
    // ================================================================= Timer
    void OnClock()
    {
        chkLowLatSafe = chkLowLat.Checked;
        int lvl = audioLevel;
        meter.Value = Math.Max(0, Math.Min(100, lvl));
        FeaturesTick(DateTime.Now);
        audioLevel = lvl * 7 / 10;
        DateTime now = DateTime.Now;
        if (lvl >= 98) clipUntil = now.AddSeconds(2);
        string clip = now < clipUntil ? L.T("SATURA! abbassa il guadagno", "CLIPPING! lower the gain") : "";
        if (lblClip.Text != clip) lblClip.Text = clip;

        bool running = cap != null && !capStopping;
        bool noSignal = running && (now - capStart).TotalSeconds > 4 && (now - lastFrame).TotalSeconds > 2.5;
        string overlay = noSignal ? L.T("NESSUN SEGNALE", "NO SIGNAL") : null;
        if (view.Overlay != overlay) { view.Overlay = overlay; view.Invalidate(); }
        if (liveView != null && liveView.Overlay != overlay) { liveView.Overlay = overlay; liveView.Invalidate(); }
        if (glNoSignal != null && glNoSignal.Visible != (overlay != null)) glNoSignal.Visible = overlay != null;
        if (miniView != null && miniView.Overlay != overlay) { miniView.Overlay = overlay; miniView.Invalidate(); }
        if (!running && lblDetected.Text.Length > 0) lblDetected.Text = "";

        bool signal = running && !noSignal && lastFrame > capStart;
        if (signal && !hadSignal && chkAutoLive.Checked && liveForm == null) ToggleLive();
        hadSignal = signal;

        if ((now - lastFpsTick).TotalSeconds >= 1)
        {
            double secs = (now - lastFpsTick).TotalSeconds;
            fps = (int)Math.Round(Interlocked.Exchange(ref frames, 0) / secs);
            lastFpsTick = now;
            UpdateCpu();
            UpdateInfo();
        }

        // l'aggiornamento resta in attesa finché anteprima/registrazione non sono ferme
        if (hotplugPending && now >= hotplugAt && cap == null && rec == null && str == null && !capStopping && savingCount == 0)
        {
            hotplugPending = false;
            Log.W("Cambio dispositivi USB: aggiorno l'elenco");
            LoadDevices();
        }

        if (wantStream && str == null && now >= streamRetryAt) LaunchStreamer();

        if (scheduleArmed && now >= scheduleAt)
        {
            scheduleArmed = false;
            chkSchedule.Checked = false;
            if (rec == null) StartRecording();
        }

        if (rec != null)
        {
            int stopAfter = (int)nStopAfter.Value;
            long drops = Interlocked.Read(ref dropped) - dropAtRecStart;
            double recGb = recBytesCache / 1073741824.0;
            if (stopAfter > 0 && RecElapsed().TotalMinutes >= stopAfter) StopRecordingCore(L.T("tempo raggiunto", "time reached"));
            else if (nStopGB.Value > 0 && recGb >= (double)nStopGB.Value) StopRecordingCore(L.T("dimensione raggiunta", "size reached"));
            else if (nStopDrops.Value > 0 && drops > nStopDrops.Value) { StopRecordingCore(L.T("troppi fotogrammi persi", "too many dropped frames")); Msg(L.T("Registrazione fermata: troppi fotogrammi persi (", "Recording stopped: too many dropped frames (") + drops + ")."); }
            else if (freeGbCache >= 0 && freeGbCache < 0.5) { StopRecordingCore(L.T("disco pieno", "disk full")); Msg(L.T("Disco quasi pieno: registrazione fermata.", "Disk almost full: recording stopped.")); }
            else if (nSplitGB.Value > 0 && nSplit.Value == 0 && partBytesCache >= (double)nSplitGB.Value * 1073741824.0 - 96.0 * 1048576.0 && (DateTime.UtcNow - partStart).TotalSeconds > 10) RotateRecording();
        }

        if (now >= holdStatusUntil)
        {
            if (noSignal) SetStatusRaw(L.T("Nessun segnale dal grabber (console spenta o cavo scollegato?)", "No signal from the grabber (console off or cable unplugged?)"), true);
            else if (running || rec != null || str != null || wantStream) SetStatusRaw(StateText(), false);
        }
    }

    long recBytesCache, partBytesCache;
    volatile bool freeGbBusy;

    void UpdateInfo()
    {
        // lo spazio libero si legge in background: una cartella di rete lenta o scollegata bloccherebbe la finestra
        // anche le dimensioni dei file in registrazione: su disco di rete leggerle può richiedere secondi
        if (!freeGbBusy)
        {
            freeGbBusy = true;
            string folder = tbFolder.Text.Trim().Length > 0 ? tbFolder.Text.Trim() : ".";
            string recF = rec != null ? rec.File : null, archF = arch != null ? arch.File : null;
            StartThread("spazio", delegate
            {
                double gb;
                try { gb = Native.FreeGB(folder); } catch { gb = -1; }
                freeGbCache = gb;
                long part = FileBytes(recF);
                partBytesCache = part;
                recBytesCache = part + FileBytes(archF);
                freeGbBusy = false;
            });
        }
        var sb = new StringBuilder();
        sb.Append(L.T("Disco: ", "Disk: ")).Append(freeGbCache < 0 ? "?" : freeGbCache.ToString("0.0")).Append(" GB");
        if (cap != null)
        {
            sb.Append("  |  ").Append(fps).Append(" fps");
            sb.Append("  |  ").Append(L.T("persi: ", "dropped: ")).Append(Interlocked.Read(ref dropped));
            sb.Append("  |  CPU ffmpeg: ").Append(cpuPct).Append("%");
            if (capPreviewAudio) sb.Append("  |  ").Append(L.T("buffer audio: ", "audio buffer: ")).Append(audioQueueMs).Append(" ms");
        }
        if (rec != null)
        {
            sb.Append("  |  REC ").Append((partBytesCache / 1048576.0).ToString("0")).Append(" MB");
            if (recPart > 1) sb.Append(L.T(" (parte ", " (part ")).Append(recPart).Append(")");
            if (arch != null) sb.Append(L.T("  + archivio ", "  + archive ")).Append(((recBytesCache - partBytesCache) / 1048576.0).ToString("0")).Append(" MB");
            if (rec.Dropped > 0) sb.Append(L.T(" (blocchi persi ", " (lost chunks ")).Append(rec.Dropped).Append(")");
        }
        if (str != null) sb.Append("  |  ").Append(L.T("Diretta ", "Stream ")).Append(str.BitrateKbps.ToString("0")).Append(" kbps · ").Append(str.Speed.ToString("0.00")).Append("x");
        if (rec != null && rec.Speed > 0) sb.Append("  |  ").Append(L.T("codifica ", "encoding ")).Append(rec.Speed.ToString("0.00")).Append("x");
        if (replayOn) sb.Append("  |  Buffer ").Append((replay.Bytes / 1048576.0).ToString("0")).Append(" MB");
        int jobs = jobCount + postRunning;
        if (jobs > 0) sb.Append("  |  ").Append(L.T("lavori: ", "jobs: ")).Append(jobs);
        if (savingCount > 0) sb.Append("  |  ").Append(L.T("salvataggi: ", "saving: ")).Append(savingCount);
        if (str2 != null) sb.Append("  |  ").Append(L.T("Diretta 2 ", "Stream 2 ")).Append(str2.BitrateKbps.ToString("0")).Append(" kbps");
        if (recPaused) sb.Append("  |  ").Append(L.T("IN PAUSA", "PAUSED"));
        if (chkTimer.Checked || timerRunning) sb.Append("  |  Timer ").Append(FormatTimer(TimerValue));
        if (replayOn && replay.Bytes > 500L * 1024 * 1024) sb.Append(L.T(" (replay al limite di memoria)", " (replay at memory limit)"));
        lblInfo.Text = sb.ToString();
        UpdateChips();
        UpdateEstimate();
    }

    static long FileBytes(string file)
    {
        try
        {
            if (file == null) return 0;
            if (!file.Contains("%03d")) return File.Exists(file) ? new FileInfo(file).Length : 0;
            long sum = 0;
            foreach (string f in Directory.GetFiles(Path.GetDirectoryName(file), Path.GetFileName(file).Replace("%03d", "*"))) sum += new FileInfo(f).Length;
            return sum;
        }
        catch { return 0; }
    }

    string StateText()
    {
        var sb = new StringBuilder();
        if (rec != null) sb.Append("● REC ").Append(Hms(RecElapsed())).Append("  ").Append(Path.GetFileName((rec.File ?? "").Replace("_%03d", "")));
        if (arch != null) sb.Append(L.T(" + archivio", " + archive"));
        if (str != null) sb.Append(sb.Length > 0 ? "   " : "").Append(L.T("IN DIRETTA", "LIVE"));
        else if (wantStream) sb.Append(sb.Length > 0 ? "   " : "").Append(L.T("diretta: riconnessione...", "stream: reconnecting..."));
        if (sb.Length == 0) sb.Append(cap != null ? L.T("Anteprima attiva", "Preview running") : L.T("Pronto", "Ready"));
        if (scheduleArmed) sb.Append(L.T("   programmata alle ", "   scheduled at ")).Append(scheduleAt.ToString("HH:mm"));
        return sb.ToString();
    }

    void SetUi()
    {
        bool active = cap != null;
        bool busyCap = capStopping;
        bool locked = rec != null || str != null || wantStream || busyCap;
        btnPreview.Enabled = !locked;
        btnPreview.Text = active && !locked ? L.T("Chiudi anteprima", "Stop preview") : L.T("Anteprima", "Preview");
        btnRec.Enabled = rec == null && !busyCap;
        btnStop.Enabled = rec != null;
        btnPause.Enabled = rec != null;
        btnNewFile.Enabled = rec != null && !AudioOnlyFormat;
        btnRec.Active = rec != null;
        btnStream.Active = str != null || wantStream;
        btnLive.Active = liveForm != null;
        btnPreview.Active = active && !locked;
        foreach (ModernButton b in new[] { btnRec, btnStream, btnLive, btnPreview, btnPause }) b.Invalidate();
        foreach (Control c in new Control[] { chkAutoLevels, chkSharpen, chkLut, tbLut, btnLut, chkTimer, cbTimerPos, chkSceneMute })
            c.Enabled = !locked || c == chkSceneMute;
        tbRtmp2.Enabled = chkStream2.Enabled = str == null && !wantStream;
        btnStream.Enabled = !busyCap;
        btnStream.Text = (str != null || wantStream) ? L.T("Ferma diretta", "Stop stream") : L.T("Diretta", "Stream");
        btnLive.Text = liveForm != null ? L.T("Chiudi Live (F11)", "Close Live (F11)") : "Live (F11)";
        btnPhoto.Enabled = active;
        btnReplay.Enabled = active;
        btnMarker.Enabled = rec != null;
        btnDetect.Enabled = active && !locked;
        btnRefresh.Enabled = !locked && savingCount == 0 && !listingDevices;
        btnLoadProfile.Enabled = !locked;
        cbPreset.Enabled = !locked;
        btnDriver.Enabled = !locked && !IsTestSource;
        btnAnalyze.Enabled = btnAutoProbe.Enabled = !locked && !IsTestSource;
        foreach (Control c in new Control[] { cbVideo, cbAudio, cbInput, cbStd, cbSize, cbFps, cbPixFmt, cbDeint, chkDriver, chkDriverMem, nRtBuf, chkLowLat, chkCfr, chkLossMez,
            tbBright, tbContrast, tbSat, nCropL, nCropR, nCropT, nCropB, cbAspect, cbLevels, chkDenoise, chkChromaNR, nChromaShift, nHeadSwitch, btnResetImg, cbScope, chkGrid,
            nAudioDelay, chkADenoise, chkANorm, chkAudio24, cbOutDev, cbRateHz, cbChannels, nGain, chkMic, cbMic,
            chkCam, cbCam, cbCamPos, nCamSize, chkLogo, tbLogo, btnLogo, cbLogoPos, chkText, cbTextPos })
            c.Enabled = !locked;
        foreach (Control c in new Control[] { tbFolder, btnBrowse, cbFormat, nSplit, nSplitGB, chkArchive, tbTemplate, chkDateFolder, tbTitle, tbTape, tbNotes, nPreroll })
            c.Enabled = rec == null;
        foreach (Control c in new Control[] { tbRtmp, nBitrate })
            c.Enabled = str == null && !wantStream;
        UpdateEncUi();
    }
}
