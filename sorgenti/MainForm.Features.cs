using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Media;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows.Forms;

// Funzioni aggiunte nella versione 5
public partial class MainForm
{
    Toggle chkReport;
    Consumer str2;
    int str2Retries;
    DateTime str2RetryAt = DateTime.MaxValue;
    volatile bool recPaused;
    DateTime pauseStart;
    int sceneIndex;
    bool timerRunning;
    DateTime timerStart;
    TimeSpan timerAccum;
    string lastTimerText;
    DateTime nextTimerSend, nextIntervalPhoto = DateTime.MaxValue;
    volatile int contentLevel;            // varianza della luminanza dell'anteprima (0 = schermo uniforme)
    DateTime blankSince = DateTime.MaxValue;
    bool hadContent;
    DateTime schedStopAt = DateTime.MaxValue;
    BlockingCollection<string> cmdQueue;
    List<FileInfo> allFiles = new List<FileInfo>();
    string lastDevicesOutput;
    bool sleepBlocked, sleepDisplay;
    int recDropsAtStart;

    // ================================================================= Eventi dei controlli
    void WireEvents()
    {
        foreach (ComboBox c in new ComboBox[] { cbAudio, cbSize, cbFps, cbPixFmt, cbDeint, cbCam, cbCamPos, cbLogoPos, cbTextPos, cbAspect, cbLevels, cbRateHz, cbChannels, cbMic, cbTimerPos })
            c.SelectedIndexChanged += delegate { ScheduleApply(); };
        cbVideo.SelectedIndexChanged += delegate { OnDeviceChanged(); };
        cbInput.SelectedIndexChanged += delegate { if (!suppressInputChange) OnDeviceChanged(); };
        cbStd.SelectedIndexChanged += delegate { OnStdChanged(); };
        foreach (Slider tb in new[] { tbBright, tbContrast, tbSat }) tb.ValueChanged += delegate { ScheduleApply(); };
        foreach (NumericUpDown n in new[] { nCropL, nCropR, nCropT, nCropB, nAudioDelay, nCamSize, nChromaShift, nHeadSwitch, nGain, nRtBuf })
            n.ValueChanged += delegate { ScheduleApply(); };
        foreach (CheckBox c in new CheckBox[] { chkDenoise, chkChromaNR, chkCam, chkLogo, chkADenoise, chkANorm, chkLowLat, chkCfr, chkLossMez, chkInterlaced, chkMic, chkDriverMem, chkAutoLevels, chkSharpen, chkLut, chkTimer, chkText })
            c.CheckedChanged += delegate { ScheduleApply(); };
        tbLogo.Leave += delegate { if (chkLogo.Checked) ScheduleApply(); };
        cbMic.SelectedIndexChanged += delegate { skipMic = false; };
        foreach (ComboBox c in new ComboBox[] { cbSize, cbFps, cbPixFmt, cbStd }) c.SelectedIndexChanged += delegate { formatFallback = false; };
        cbCam.SelectedIndexChanged += delegate { skipCam = false; };
        chkCam.CheckedChanged += delegate { skipCam = false; };
        chkMic.CheckedChanged += delegate { skipMic = false; };
        chkAudio24.CheckedChanged += delegate { audio24Failed = false; ScheduleApply(); };
        chkDriverMem.CheckedChanged += delegate { driverMemFailed = false; };
        tbLut.Leave += delegate { if (chkLut.Checked) ScheduleApply(); };
        cbOutDev.SelectedIndexChanged += delegate { outDevice = cbOutDev.SelectedIndex - 1; ScheduleApply(); };
        foreach (CheckBox c in new CheckBox[] { chkSharp, chkScan, chkIntScale, chkGrid }) c.CheckedChanged += delegate { UpdateViewOptions(); };
        cbScope.SelectedIndexChanged += delegate { UpdateViewOptions(); };
        tbText.TextChanged += delegate { if (textTimer != null) { textTimer.Stop(); textTimer.Start(); } };
        foreach (TextBox t in new[] { tbSceneStart, tbScenePause, tbSceneEnd, tbSceneCustom }) t.Leave += delegate { if (sceneIndex > 0) ApplyScene(sceneIndex); };
        chkSceneMute.CheckedChanged += delegate { if (sceneIndex > 0) ApplyScene(sceneIndex); };
        cbCodec.SelectedIndexChanged += delegate { OnCodecChanged(); };
        foreach (ComboBox c in new ComboBox[] { cbFormat, cbRateCtl, cbOutSize, cbACodec, cbEncPreset }) c.SelectedIndexChanged += delegate { UpdateEncUi(); };
        foreach (NumericUpDown n in new[] { nQuality, nRecBitrate, nAKbps }) n.ValueChanged += delegate { UpdateEstimate(); };
        chkArchive.CheckedChanged += delegate { if (chkArchive.Checked && !chkLossMez.Checked && !loading) chkLossMez.Checked = true; UpdateEstimate(); };
        chkInterval.CheckedChanged += delegate { nextIntervalPhoto = chkInterval.Checked ? DateTime.Now.AddSeconds((double)nIntervalSec.Value) : DateTime.MaxValue; };
        chkNotify.CheckedChanged += delegate { if (tray != null) tray.Visible = chkNotify.Checked || !Visible; };
        tbFolder.Leave += delegate { RefreshFiles(); };
        chkDateFolder.CheckedChanged += delegate { RefreshFiles(); };
    }

    // ================================================================= Comandi dal vivo a ffmpeg
    void StartCommandSender(Process p)
    {
        var q = new BlockingCollection<string>(200);
        cmdQueue = q;
        StartThread("comandi", delegate
        {
            try
            {
                foreach (string line in q.GetConsumingEnumerable())
                {
                    if (p.HasExited) break;
                    lock (p) { p.StandardInput.Write("c"); p.StandardInput.Write(line + "\n"); p.StandardInput.Flush(); }
                    OnCommandWritten(line);
                    Thread.Sleep(130);        // ffmpeg legge un comando ogni ~100 ms
                }
            }
            catch (Exception ex) { Log.W("Comandi: " + ex.Message); }
        });
    }

    void SendCmd(string target, string command, string arg)
    {
        var q = cmdQueue;
        if (q == null || cap == null) return;
        try { q.TryAdd(target + " -1 " + command + " " + arg); } catch { }
    }

    static string DrawTextArg(string text)
    {
        text = (text ?? "").Replace("\r", " ").Replace("\n", " ");
        if (text.Length > 200) text = text.Substring(0, 200);
        return "text='" + text.Replace("'", "'\\''") + "'";
    }

    // ---- Scene
    string SceneText(int i)
    {
        switch (i)
        {
            case 1: return tbSceneStart.Text;
            case 2: return tbScenePause.Text;
            case 3: return tbSceneEnd.Text;
            case 4: return tbSceneCustom.Text;
            default: return "";
        }
    }

    void ApplyScene(int i)
    {
        sceneIndex = Math.Max(0, Math.Min(4, i));
        if (cbSceneQuick.SelectedIndex != sceneIndex) { cbSceneQuick.SelectedIndex = sceneIndex; return; }
        if (cap == null) return;
        SendCmd("drawbox@slate", "color", sceneIndex > 0 ? "0x101218@1" : "0x101218@0");
        SendCmd("drawtext@scene", "reinit", DrawTextArg(SceneText(sceneIndex)));
        if (capAudio) SendCmd("volume@scenevol", "volume", sceneIndex > 0 && chkSceneMute.Checked ? "0" : "1");
        SetStatus(sceneIndex == 0 ? L.T("Scena: gioco", "Scene: game") : L.T("Scena: ", "Scene: ") + cbSceneQuick.Text, false, 3);
    }

    // ---- Timer da speedrun
    TimeSpan TimerValue { get { return timerAccum + (timerRunning ? DateTime.UtcNow - timerStart : TimeSpan.Zero); } }

    static string FormatTimer(TimeSpan t)
    {
        string s = t.TotalHours >= 1 ? ((int)t.TotalHours) + ":" + t.Minutes.ToString("00") + ":" + t.Seconds.ToString("00") : t.Minutes + ":" + t.Seconds.ToString("00");
        return s + "." + (t.Milliseconds / 100);
    }

    void TimerToggle()
    {
        if (timerRunning) { timerAccum += DateTime.UtcNow - timerStart; timerRunning = false; }
        else { timerStart = DateTime.UtcNow; timerRunning = true; }
        SendTimer(true);
    }

    void TimerReset()
    {
        timerRunning = false;
        timerAccum = TimeSpan.Zero;
        SendTimer(true);
    }

    void SendTimer(bool force)
    {
        if (!chkTimer.Checked || cap == null) return;
        string t = FormatTimer(TimerValue);
        if (!force && t == lastTimerText) return;
        lastTimerText = t;
        SendCmd("drawtext@timer", "reinit", DrawTextArg(t));
    }

    void WriteOverlayTextLive()
    {
        if (cap != null && chkText.Checked) SendCmd("drawtext@ovl", "reinit", DrawTextArg(tbText.Text));
    }

    // ================================================================= Pausa registrazione
    void TogglePauseRecording()
    {
        if (rec == null) return;
        if (!chkCfr.Checked) SetStatus(L.T("Suggerimento: con 'Frequenza costante' attiva la pausa non lascia buchi nel file.", "Tip: enable 'Constant frame rate' for gapless pause."), true, 6);
        if (!recPaused)
        {
            recPaused = true;
            pauseStart = DateTime.UtcNow;
            SetStatus(L.T("Registrazione in pausa", "Recording paused"), true, 0);
        }
        else
        {
            if (recPausable) recStart += DateTime.UtcNow - pauseStart;     // con i buchi nel file i tempi non si spostano
            recPaused = false;
            SetStatus(L.T("Registrazione ripresa", "Recording resumed"), false, 3);
        }
        btnPause.Active = recPaused;
        btnPause.Invalidate();
    }

    // ================================================================= Diretta su due destinazioni
    void LaunchStreamer2()
    {
        str2RetryAt = DateTime.MaxValue;
        if (!wantStream || cap == null || !chkStream2.Checked) return;
        string url = tbRtmp2.Text.Trim();
        if (!Regex.IsMatch(url, "^(rtmps?|srt)://", RegexOptions.IgnoreCase))
        {
            Alert(L.T("Seconda diretta non avviata: indirizzo mancante o non valido.", "Second stream not started: missing or invalid URL."), true);
            return;
        }
        try
        {
            var c = new Consumer(ffmpeg, Enc.Streamer(url, CurrentEncForStream(), Fps(), capAudio, capHasMic), "diretta2", Consumer.UrlSecrets(url));
            c.Exited += delegate (Consumer x) { Invoke2(delegate { OnStreamer2Exited(x); }); };
            c.Start();
            lock (pumpLock) str2 = c;
        }
        catch (Exception ex) { SetStatus(L.T("Seconda diretta non avviata: ", "Second stream not started: ") + ex.Message, true, 8); }
    }

    void OnStreamer2Exited(Consumer s)
    {
        lock (pumpLock) if (s == str2) str2 = null;
        if (s.StoppedByUser || !wantStream) return;
        if (chkReconnect.Checked && str2Retries < 20)
        {
            str2Retries++;
            str2RetryAt = DateTime.Now.AddSeconds(Math.Min(30, 3 * str2Retries));
            SetStatus(L.T("Seconda diretta caduta, riconnessione...", "Second stream dropped, reconnecting..."), true, 6);
        }
        else Alert(L.T("La seconda diretta si è interrotta.", "The second stream stopped."), true);
    }

    void StopStreamer2()
    {
        Consumer s;
        lock (pumpLock) { s = str2; str2 = null; }
        str2RetryAt = DateTime.MaxValue;
        if (s != null) { s.Finish(); StartThread("stop diretta2", delegate { if (!s.P.WaitForExit(5000)) s.Kill(); }); }
    }

    // ================================================================= Avvisi
    void Alert(string text, bool warn)
    {
        SetStatus(text, warn, 10);
        if (chkSounds != null && chkSounds.Checked) { try { if (warn) SystemSounds.Exclamation.Play(); else SystemSounds.Asterisk.Play(); } catch { } }
        if (chkNotify != null && chkNotify.Checked && tray != null)
        {
            try { tray.Visible = true; tray.ShowBalloonTip(5000, "Video2All", text, warn ? ToolTipIcon.Warning : ToolTipIcon.Info); } catch { }
        }
    }

    // ================================================================= Analisi contenuto (per auto-registrazione e fine nastro)
    static int ContentLevel(byte[] bgr, int w, int h)
    {
        long sum = 0, sum2 = 0; int n = 0;
        for (int y = h / 8; y < h - h / 8; y += 8)
            for (int x = w / 8; x < w - w / 8; x += 8)
            {
                int i = (y * w + x) * 3;
                int l = (bgr[i] * 29 + bgr[i + 1] * 150 + bgr[i + 2] * 77) >> 8;
                sum += l; sum2 += l * l; n++;
            }
        if (n == 0) return 0;
        double mean = sum / (double)n, var = sum2 / (double)n - mean * mean;
        if (mean < 18) return 0;              // nero
        return (int)Math.Min(10000, var);
    }

    bool HasContent { get { return cap != null && (DateTime.Now - lastFrame).TotalSeconds < 2 && contentLevel > 60; } }

    // ================================================================= Programmazione
    sealed class SchedItem { public DateTime Next; public int Minutes; public int Repeat; }
    readonly List<SchedItem> schedule = new List<SchedItem>();

    void LoadSchedule()
    {
        schedule.Clear();
        foreach (string part in cfg.Get("schedule", "").Split(';'))
        {
            string[] f = part.Split('|');
            if (f.Length < 3) continue;
            DateTime d; int m, r;
            if (DateTime.TryParseExact(f[0], "yyyy-MM-ddTHH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out d) && int.TryParse(f[1], out m) && int.TryParse(f[2], out r))
                schedule.Add(new SchedItem { Next = d, Minutes = m, Repeat = r });
        }
        // eventi passati mentre il programma era chiuso
        DateTime now = DateTime.Now;
        foreach (SchedItem s in schedule.ToArray())
        {
            while (s.Next.AddMinutes(s.Minutes) < now && s.Repeat > 0) s.Next = NextOccurrence(s);
            if (s.Repeat == 0 && s.Next.AddMinutes(s.Minutes) < now) schedule.Remove(s);
        }
        ShowSchedule();
    }

    void SaveSchedule()
    {
        var sb = new StringBuilder();
        foreach (SchedItem s in schedule) sb.Append(s.Next.ToString("yyyy-MM-ddTHH:mm", CultureInfo.InvariantCulture)).Append('|').Append(s.Minutes).Append('|').Append(s.Repeat).Append(';');
        cfg.Set("schedule", sb.ToString());
        QueueSave();
    }

    void ShowSchedule()
    {
        if (lstSched == null) return;
        schedule.Sort(delegate (SchedItem a, SchedItem b) { return a.Next.CompareTo(b.Next); });
        lstSched.Items.Clear();
        string[] rep = { L.T("una volta", "once"), L.T("ogni giorno", "daily"), L.T("lun-ven", "mon-fri"), L.T("ogni settimana", "weekly") };
        foreach (SchedItem s in schedule)
            lstSched.Items.Add(s.Next.ToString("ddd dd/MM HH:mm") + "  ·  " + s.Minutes + " min  ·  " + rep[Math.Max(0, Math.Min(3, s.Repeat))]);
    }

    static DateTime NextOccurrence(SchedItem s)
    {
        DateTime n = s.Next;
        switch (s.Repeat)
        {
            case 1: return n.AddDays(1);
            case 2: do n = n.AddDays(1); while (n.DayOfWeek == DayOfWeek.Saturday || n.DayOfWeek == DayOfWeek.Sunday); return n;
            case 3: return n.AddDays(7);
            default: return DateTime.MaxValue;
        }
    }

    void ScheduleAdd()
    {
        DateTime at = dtSchedDate.Value.Date + dtSchedTime.Value.TimeOfDay;
        at = new DateTime(at.Year, at.Month, at.Day, at.Hour, at.Minute, 0);
        if (at <= DateTime.Now) { Msg(L.T("Scegli una data e un'ora future.", "Pick a future date and time.")); return; }
        schedule.Add(new SchedItem { Next = at, Minutes = (int)nSchedMin.Value, Repeat = Math.Max(0, cbSchedRepeat.SelectedIndex) });
        SaveSchedule();
        ShowSchedule();
        SetStatus(L.T("Registrazione programmata.", "Recording scheduled."), false, 4);
    }

    void ScheduleRemove()
    {
        int i = lstSched.SelectedIndex;
        if (i < 0 || i >= schedule.Count) return;
        schedule.RemoveAt(i);
        SaveSchedule();
        ShowSchedule();
    }

    void CheckSchedule(DateTime now)
    {
        if (rec != null && now >= schedStopAt) { schedStopAt = DateTime.MaxValue; StopRecordingCore(L.T("fine programmazione", "schedule ended")); return; }
        foreach (SchedItem s in schedule.ToArray())
        {
            if (now < s.Next) continue;
            DateTime end = s.Next.AddMinutes(s.Minutes);
            if (s.Repeat == 0) schedule.Remove(s); else s.Next = NextOccurrence(s);
            SaveSchedule();
            ShowSchedule();
            if (now >= end) continue;
            if (rec == null)
            {
                Log.W("Programmazione: avvio registrazione fino alle " + end);
                if (cap == null) StartCapture(false);
                StartRecording();
                schedStopAt = end;
                if (rec != null || capStopping) Alert(L.T("Registrazione programmata avviata.", "Scheduled recording started."), false);
                else Alert(L.T("Registrazione programmata NON avviata: controlla il grabber.", "Scheduled recording NOT started: check the grabber."), true);
            }
            else Alert(L.T("Programmazione delle ", "Schedule at ") + now.ToString("HH:mm") + L.T(": c'era già una registrazione in corso, lasciata così.", ": a recording was already running, left as is."), true);
            break;
        }
    }

    // ================================================================= Clock delle funzioni nuove (chiamato da OnClock)
    void FeaturesTick(DateTime now)
    {
        // impedisce la sospensione di Windows durante registrazione/diretta/Live
        bool busyNow = rec != null || str != null || wantStream || savingCount > 0 || liveForm != null;
        bool liveNow = liveForm != null;
        if (busyNow != sleepBlocked || liveNow != sleepDisplay) { sleepBlocked = busyNow; sleepDisplay = liveNow; Native.KeepAwake(busyNow, liveNow); }

        // timer
        if (timerRunning && chkTimer.Checked && now >= nextTimerSend) { nextTimerSend = now.AddMilliseconds(200); SendTimer(false); }

        // contenuto: auto-registrazione e fine nastro
        // durante una scena a schermo pieno l'immagine è uniforme per scelta: non è "fine nastro"
        bool content = HasContent;
        if (content || (sceneIndex > 0 && cap != null)) blankSince = DateTime.MaxValue;
        else if (blankSince == DateTime.MaxValue) blankSince = now;
        if (cap != null && content && !hadContent && chkAutoRecSignal.Checked && rec == null && (now - capStart).TotalSeconds > 3)
        {
            Log.W("Contenuto rilevato: avvio registrazione automatica");
            StartRecording();
            if (rec != null || capStopping) Alert(L.T("Segnale rilevato: registrazione avviata.", "Signal detected: recording started."), false);
        }
        hadContent = content;
        if (rec != null && chkEndOfTape.Checked && blankSince != DateTime.MaxValue && (now - blankSince).TotalSeconds >= (double)nEndTapeSec.Value && RecElapsed().TotalSeconds > (double)nEndTapeSec.Value + 5)
        {
            Log.W("Fine nastro rilevata");
            StopRecordingCore(L.T("fine nastro", "end of tape"));
            Alert(L.T("Fine nastro: registrazione salvata.", "End of tape: recording saved."), false);
        }

        // foto a intervalli
        if (chkInterval.Checked && cap != null && now >= nextIntervalPhoto)
        {
            nextIntervalPhoto = now.AddSeconds((double)nIntervalSec.Value);
            TakePhoto();
        }

        // seconda diretta
        if (wantStream && chkStream2.Checked && str2 == null && now >= str2RetryAt) LaunchStreamer2();

        // programmazione
        CheckSchedule(now);

        // salute di registrazione e diretta
        CheckHealth(now);
    }

    DateTime slowSince = DateTime.MaxValue, netSlowSince = DateTime.MaxValue;
    long lastRecDropped, lastStrDropped;
    Consumer lastHealthRec, lastHealthStr;

    void CheckHealth(DateTime now)
    {
        Consumer r = rec;
        if (r != lastHealthRec) { lastHealthRec = r; lastRecDropped = 0; slowSince = DateTime.MaxValue; }
        if (r != null && r.Speed > 0)
        {
            if (r.Speed < 0.93) { if (slowSince == DateTime.MaxValue) slowSince = now; }
            else slowSince = DateTime.MaxValue;
            if (slowSince != DateTime.MaxValue && (now - slowSince).TotalSeconds > 8)
            {
                slowSince = now.AddSeconds(60);
                Alert(L.T("L'encoder non regge il ritmo (velocità ", "The encoder cannot keep up (speed ") + r.Speed.ToString("0.00") + L.T("x): scegli un preset più veloce o un encoder della scheda video.", "x): choose a faster preset or a GPU encoder."), true);
            }
            if (r.Dropped > lastRecDropped) { lastRecDropped = r.Dropped; Alert(L.T("Attenzione: dati persi nella registrazione (il PC è troppo carico).", "Warning: data lost in recording (PC overloaded)."), true); }
        }
        else { slowSince = DateTime.MaxValue; lastRecDropped = r != null ? r.Dropped : 0; }
        Consumer s = str;
        if (s != lastHealthStr) { lastHealthStr = s; lastStrDropped = 0; netSlowSince = DateTime.MaxValue; }
        if (s != null && s.Speed > 0)
        {
            if (s.Speed < 0.95) { if (netSlowSince == DateTime.MaxValue) netSlowSince = now; }
            else netSlowSince = DateTime.MaxValue;
            if (netSlowSince != DateTime.MaxValue && (now - netSlowSince).TotalSeconds > 6)
            {
                netSlowSince = now.AddSeconds(60);
                Alert(L.T("Diretta lenta: connessione o encoder insufficienti. Abbassa il bitrate.", "Stream is slow: lower the bitrate."), true);
            }
            if (s.Dropped > lastStrDropped)
            {
                lastStrDropped = s.Dropped;
                Alert(L.T("Diretta: dati persi (connessione troppo lenta). Abbassa il bitrate.", "Stream: data lost (connection too slow). Lower the bitrate."), true);
            }
        }
    }

    // ================================================================= Libreria
    static readonly string[] MediaExt = { ".mp4", ".mkv", ".mov", ".avi", ".ts", ".wav", ".flac", ".mp3", ".m4a", ".ogg", ".wma", ".png", ".jpg", ".gif" };

    int refreshToken;

    // la cartella si legge in background (su disco di rete può richiedere secondi); vale solo l'ultima richiesta
    void RefreshFiles()
    {
        string root = tbFolder.Text.Trim();
        int token = ++refreshToken;
        StartThread("libreria", delegate
        {
            var list = new List<FileInfo>();
            try
            {
                var di = new DirectoryInfo(root);
                if (root.Length > 0 && di.Exists)
                {
                    var dirs = new List<DirectoryInfo> { di };
                    try
                    {
                        foreach (DirectoryInfo sub in di.GetDirectories())
                            if (Regex.IsMatch(sub.Name, @"^\d{4}-\d{2}-\d{2}$")) dirs.Add(sub);   // sottocartelle per data
                    }
                    catch { }
                    foreach (DirectoryInfo d in dirs)
                    {
                        FileInfo[] all;
                        try { all = d.GetFiles(); } catch { continue; }      // una sottocartella non leggibile non svuota tutta la libreria
                        foreach (FileInfo f in all)
                        {
                            string ext = f.Extension.ToLowerInvariant();
                            if (Array.IndexOf(MediaExt, ext) < 0) continue;
                            if (f.Name.Contains(".tmp") || f.Name.StartsWith("acqv_")) continue;
                            try { long len = f.Length; } catch { continue; }    // legge subito dimensione e data (fuori dall'interfaccia)
                            list.Add(f);
                        }
                    }
                }
            }
            catch { }
            Invoke2(delegate
            {
                if (token != refreshToken) return;
                allFiles = list;
                FilterFiles();
            });
        });
    }

    void FilterFiles()
    {
        string q = tbFind != null ? tbFind.Text.Trim().ToLowerInvariant() : "";
        var files = allFiles.FindAll(delegate (FileInfo f) { return q.Length == 0 || f.Name.ToLowerInvariant().Contains(q); });
        int sort = cbSort != null ? cbSort.SelectedIndex : 0;
        files.Sort(delegate (FileInfo a, FileInfo b)
        {
            switch (sort)
            {
                case 1: return a.LastWriteTime.CompareTo(b.LastWriteTime);
                case 2: return string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
                case 3: return b.Length.CompareTo(a.Length);
                default: return b.LastWriteTime.CompareTo(a.LastWriteTime);
            }
        });
        var selected = new HashSet<string>();
        foreach (ListViewItem it in lvFiles.SelectedItems) selected.Add((string)it.Tag);
        lvFiles.BeginUpdate();
        lvFiles.Items.Clear();
        int max = Math.Min(files.Count, 500);
        for (int i = 0; i < max; i++)
        {
            FileInfo f = files[i];
            string size = f.Length >= 1073741824 ? (f.Length / 1073741824.0).ToString("0.0") + " GB" : (f.Length / 1048576.0).ToString("0.0") + " MB";
            var it = new ListViewItem(f.Name) { Tag = f.FullName, ToolTipText = f.Name + "\n" + size + " · " + f.LastWriteTime.ToString("g") };
            it.SubItems.Add(size);
            it.SubItems.Add(f.LastWriteTime.ToString("dd/MM HH:mm"));
            if (thumbs.Images.ContainsKey(f.FullName)) it.ImageKey = f.FullName;
            else lock (thumbQueue) thumbQueue.Enqueue(f.FullName);
            if (selected.Contains(f.FullName)) it.Selected = true;
            lvFiles.Items.Add(it);
        }
        lvFiles.EndUpdate();
        if (lblLibCount != null) lblLibCount.Text = files.Count + (files.Count == 1 ? L.T(" file", " file") : L.T(" file", " files")) + (files.Count > max ? " (" + max + L.T(" mostrati)", " shown)") : "");
        StartThumbWorker();
    }

    // ================================================================= Strumenti sui file
    void EditMetadataSelected()
    {
        string f = SelectedFile();
        if (f == null) return;
        if (IsBusyFile(f)) { SetStatus(L.T("Il file è in registrazione.", "File is being recorded."), true, 5); return; }
        bool media = IsVideo(f) || Regex.IsMatch(Path.GetExtension(f), @"\.(mp3|m4a|flac|ogg|wav|wma)$", RegexOptions.IgnoreCase);
        using (var dlg = new Form { Text = L.T("Rinomina e metadati", "Rename and metadata"), FormBorderStyle = FormBorderStyle.FixedDialog, StartPosition = FormStartPosition.CenterParent, MinimizeBox = false, MaximizeBox = false, ClientSize = new Size(440, 250), BackColor = Theme.Bg, ForeColor = Theme.Text })
        {
            var name = new TextBox { Text = Path.GetFileNameWithoutExtension(f), Location = new Point(16, 34), Width = 408, BackColor = Theme.Surface2, ForeColor = Theme.Text, BorderStyle = BorderStyle.FixedSingle };
            var title = new TextBox { Location = new Point(16, 94), Width = 408, BackColor = Theme.Surface2, ForeColor = Theme.Text, BorderStyle = BorderStyle.FixedSingle, Enabled = media };
            var note = new TextBox { Location = new Point(16, 154), Width = 408, BackColor = Theme.Surface2, ForeColor = Theme.Text, BorderStyle = BorderStyle.FixedSingle, Enabled = media };
            dlg.Controls.Add(new Label { Text = L.T("Nome del file", "File name"), Location = new Point(14, 12), AutoSize = true, ForeColor = Theme.TextDim });
            dlg.Controls.Add(new Label { Text = L.T("Titolo (lascia vuoto per non cambiarlo)", "Title (empty = unchanged)"), Location = new Point(14, 72), AutoSize = true, ForeColor = Theme.TextDim });
            dlg.Controls.Add(new Label { Text = L.T("Note", "Notes"), Location = new Point(14, 132), AutoSize = true, ForeColor = Theme.TextDim });
            var ok = new ModernButton { Text = "OK", Kind = BtnKind.Primary, Location = new Point(324, 200), Size = new Size(100, 34), DialogResult = DialogResult.OK };
            dlg.Controls.AddRange(new Control[] { name, title, note, ok });
            dlg.AcceptButton = ok;
            dlg.Load += delegate { Theme.ApplyWindow(dlg); };
            if (dlg.ShowDialog(this) != DialogResult.OK) return;
            string newName = name.Text.Trim();
            foreach (char ch in Path.GetInvalidFileNameChars()) newName = newName.Replace(ch, '_');
            string t = title.Text.Trim(), n = note.Text.Trim();
            RunJob(L.T("Modifica file", "Edit file"), delegate
            {
                string cur = f;
                if ((t.Length > 0 || n.Length > 0) && media)
                {
                    string tmp = Path.Combine(Path.GetDirectoryName(f), "acqv_meta_" + Guid.NewGuid().ToString("N").Substring(0, 8) + Path.GetExtension(f));
                    string e;
                    string meta = (t.Length > 0 ? " -metadata title=\"" + Enc.QuoteSafe(t.Replace("\"", "'")) + "\"" : "") + (n.Length > 0 ? " -metadata comment=\"" + Enc.QuoteSafe(n.Replace("\"", "'")) + "\"" : "");
                    if (Tools.Run(ffmpeg, "-y -hide_banner -loglevel error -i \"" + f + "\" -map 0 -c copy" + meta + " \"" + tmp + "\"", out e, 3600000) != 0) { SafeDelete(tmp); return "!" + L.T("Metadati non scritti: ", "Metadata not written: ") + Tail(e ?? "", 200); }
                    if (!ReplaceVerified(f, tmp)) return "!" + L.T("Il nuovo file non è risultato valido: originale conservato.", "New file invalid: original kept.");
                    if (File.Exists(f + ".md5")) File.WriteAllText(f + ".md5", Tools.Md5(f) + " *" + Path.GetFileName(f) + "\r\n");   // il contenuto è cambiato
                }
                if (newName.Length > 0 && newName != Path.GetFileNameWithoutExtension(f))
                {
                    string dst = UniqueName(Path.Combine(Path.GetDirectoryName(f), newName + Path.GetExtension(f)));
                    File.Move(cur, dst);
                    // il file .md5 contiene anche il nome: si riscrive con quello nuovo (altrimenti la verifica fallisce)
                    if (File.Exists(cur + ".md5"))
                    {
                        string line = File.ReadAllText(cur + ".md5").Trim();
                        int star = line.IndexOf(" *");
                        string hash = star > 0 ? line.Substring(0, star) : line.Split(' ')[0];
                        File.WriteAllText(dst + ".md5", hash + " *" + Path.GetFileName(dst) + "\r\n");
                        File.Delete(cur + ".md5");
                    }
                    string mk = MarkersFile(cur);
                    if (File.Exists(mk)) File.Move(mk, MarkersFile(dst));
                    string rp = Path.Combine(Path.GetDirectoryName(cur), Path.GetFileNameWithoutExtension(cur) + "_rapporto.txt");
                    if (File.Exists(rp)) File.Move(rp, Path.Combine(Path.GetDirectoryName(dst), Path.GetFileNameWithoutExtension(dst) + "_rapporto.txt"));
                    cur = dst;
                }
                return L.T("Aggiornato: ", "Updated: ") + Path.GetFileName(cur);
            });
        }
    }

    void MarkerClipsSelected()
    {
        foreach (string f in VideoFiles())
        {
            string mf = MarkersFile(f);
            if (!File.Exists(mf)) { SetStatus(L.T("Nessun marcatore per ", "No markers for ") + Path.GetFileName(f), true, 5); continue; }
            string file = f;
            RunJob(L.T("Clip dai marcatori", "Clips from markers"), delegate
            {
                var times = new List<double>();
                foreach (string l in File.ReadAllLines(mf))
                {
                    string t = l.Trim().TrimStart('\uFEFF');
                    double s;
                    if (t.Length >= 8 && Tools.ParseTime(t.Substring(0, 8), out s)) times.Add(s);
                }
                int ok = 0;
                for (int i = 0; i < times.Count; i++)
                {
                    string outF = UniqueName(Path.Combine(Path.GetDirectoryName(file), Path.GetFileNameWithoutExtension(file) + "_momento" + (i + 1).ToString("00") + ".mp4"));
                    string e;
                    if (Tools.Run(ffmpeg, "-y -hide_banner -loglevel error -ss " + Tools.Ts(Math.Max(0, times[i] - 10)) + " -t 30 -i \"" + file + "\" -map 0:v:0 -map 0:a:0? -c:v libx264 -preset veryfast -crf 19 -pix_fmt yuv420p -c:a aac -b:a 160k -movflags +faststart \"" + outF + "\"", out e, 600000) == 0) ok++;
                }
                return L.T("Creati ", "Created ") + ok + L.T(" momenti (10 s prima, 20 s dopo ogni marcatore).", " clips (10 s before, 20 s after each marker).");
            });
        }
    }

    List<string> VideoFiles()
    {
        List<string> files = SelectedFiles();
        files.RemoveAll(delegate (string x) { return !IsVideo(x) || IsBusyFile(x); });
        if (files.Count == 0 && lvFiles.SelectedItems.Count > 0) SetStatus(L.T("Seleziona video completati.", "Select finished videos."), true, 5);
        return files;
    }

    void JoinSelected()
    {
        List<string> files = VideoFiles();
        if (files.Count < 2) { SetStatus(L.T("Seleziona almeno due video da unire.", "Select at least two videos."), true, 5); return; }
        files.Sort(delegate (string a, string b) { return File.GetLastWriteTime(a).CompareTo(File.GetLastWriteTime(b)); });
        string outF = UniqueName(Path.Combine(Path.GetDirectoryName(files[0]), Path.GetFileNameWithoutExtension(files[0]) + "_unito" + Path.GetExtension(files[0])));
        RunJob(L.T("Unione di ", "Joining ") + files.Count + " file", delegate
        {
            string list = Path.Combine(Path.GetDirectoryName(outF), "acqv_lista_" + Guid.NewGuid().ToString("N") + ".txt");
            var sb = new StringBuilder();
            foreach (string f in files) sb.Append("file '").Append(f.Replace("\\", "/").Replace("'", "'\\''")).Append("'\n");
            File.WriteAllText(list, sb.ToString(), new UTF8Encoding(false));
            string e;
            // la copia diretta funziona solo se tutti i file hanno codec, dimensioni e audio uguali; altrimenti il risultato è rovinato
            bool same = true;
            string first = StreamSignature(files[0]);
            for (int i = 1; i < files.Count && same; i++) same = StreamSignature(files[i]) == first;
            int code = same ? Tools.Run(ffmpeg, "-y -hide_banner -loglevel error -f concat -safe 0 -i \"" + list + "\" -map 0 -c copy \"" + outF + "\"", out e, 6 * 3600000) : -1;
            e = null;
            if (code != 0)
            {
                SafeDelete(outF);
                outF = UniqueName(Path.ChangeExtension(outF, ".mp4"));
                code = Tools.Run(ffmpeg, "-y -hide_banner -loglevel error -f concat -safe 0 -i \"" + list + "\" -map 0:v:0 -map 0:a? -c:v libx264 -preset veryfast -crf 18 -pix_fmt yuv420p -c:a aac -b:a 192k \"" + outF + "\"", out e, 12 * 3600000);
            }
            SafeDelete(list);
            return code == 0 ? L.T("Unito: ", "Joined: ") + Path.GetFileName(outF) : "!" + L.T("Unione non riuscita (formati diversi?): ", "Join failed (different formats?): ") + Tail(e ?? "", 200);
        });
    }

    // descrizione dei flussi (codec, dimensioni, formato audio) per capire se due file si possono unire senza ricodifica
    string StreamSignature(string file)
    {
        string e;
        try { Tools.Run(ffmpeg, "-hide_banner -i \"" + file + "\"", out e, 30000); } catch { return Guid.NewGuid().ToString(); }
        var sb = new StringBuilder();
        foreach (Match m in Regex.Matches(e ?? "", @"Stream #\d+:\d+[^:]*: (Video|Audio): (\w+)[^\n]*"))
        {
            string line = m.Value;
            Match size = Regex.Match(line, @"\b(\d{2,5})x(\d{2,5})\b");
            Match rate = Regex.Match(line, @"(\d+) Hz, ([\w.()]+)");
            Match pix = Regex.Match(line, @"Video: \w+[^,]*, (\w+)");
            sb.Append(m.Groups[1].Value).Append(':').Append(m.Groups[2].Value)
              .Append(size.Success ? " " + size.Value : "").Append(pix.Success ? " " + pix.Groups[1].Value : "")
              .Append(rate.Success ? " " + rate.Groups[1].Value + " " + rate.Groups[2].Value : "").Append('|');
        }
        return sb.ToString();
    }

    void NormalizeSelected()
    {
        foreach (string f0 in VideoFilesOrAudio())
        {
            string f = f0;
            // il video viene copiato: si resta nel contenitore d'origine se l'MP4 non può contenerlo (FFV1, HuffYUV...)
            string vext = Regex.IsMatch(Path.GetExtension(f), @"\.(mp4|mov|m4v)$", RegexOptions.IgnoreCase) ? ".mp4" : ".mkv";
            string outF = UniqueName(Path.Combine(Path.GetDirectoryName(f), Path.GetFileNameWithoutExtension(f) + "_normalizzato" + (IsVideo(f) ? vext : ".m4a")));
            RunJob(L.T("Normalizzazione audio", "Audio normalization"), delegate
            {
                string e;
                Tools.Run(ffmpeg, "-hide_banner -i \"" + f + "\" -map 0:a:0 -af loudnorm=I=-16:TP=-1.5:LRA=11:print_format=json -f null -", out e, 6 * 3600000);
                Func<string, string> val = delegate (string k) { Match m = Regex.Match(e ?? "", "\"" + k + "\"\\s*:\\s*\"([-\\d.]+)\""); return m.Success ? m.Groups[1].Value : null; };
                string ii = val("input_i"), tp = val("input_tp"), lra = val("input_lra"), th = val("input_thresh"), off = val("target_offset");
                string af = ii != null && tp != null && lra != null && th != null
                    ? "loudnorm=I=-16:TP=-1.5:LRA=11:measured_I=" + ii + ":measured_TP=" + tp + ":measured_LRA=" + lra + ":measured_thresh=" + th + (off != null ? ":offset=" + off : "") + ":linear=true"
                    : "loudnorm=I=-16:TP=-1.5:LRA=11";
                string args = IsVideo(f)
                    // la traccia del gioco viene normalizzata; l'eventuale microfono (seconda traccia) resta com'era
                    ? "-y -hide_banner -loglevel error -i \"" + f + "\" -filter_complex \"[0:a:0]" + af + ",aresample=48000[an]\" -map 0:v:0 -map [an] -map 0:a:1? -c:v copy -c:a:0 aac -b:a:0 192k -c:a:1 aac -b:a:1 192k -movflags +faststart \"" + outF + "\""
                    : "-y -hide_banner -loglevel error -i \"" + f + "\" -map 0:a:0 -af " + af + ",aresample=48000 -c:a aac -b:a 192k \"" + outF + "\"";
                int code = Tools.Run(ffmpeg, args, out e, 6 * 3600000);
                return code == 0 ? L.T("Audio normalizzato: ", "Audio normalized: ") + Path.GetFileName(outF) : "!" + L.T("Normalizzazione non riuscita: ", "Normalization failed: ") + Tail(e ?? "", 200);
            });
        }
    }

    List<string> VideoFilesOrAudio()
    {
        List<string> files = SelectedFiles();
        files.RemoveAll(delegate (string x) { return IsBusyFile(x) || Regex.IsMatch(Path.GetExtension(x), @"\.(png|jpg|gif)$", RegexOptions.IgnoreCase); });
        return files;
    }

    void StabilizeSelected()
    {
        foreach (string f0 in VideoFiles())
        {
            string f = f0;
            string outF = UniqueName(Path.Combine(Path.GetDirectoryName(f), Path.GetFileNameWithoutExtension(f) + "_stabilizzato.mp4"));
            RunJob(L.T("Stabilizzazione (2 passaggi)", "Stabilization (2 passes)"), delegate
            {
                string trf = Path.Combine(Path.GetDirectoryName(outF), "acqv_" + Guid.NewGuid().ToString("N") + ".trf");
                string e;
                int code = Tools.Run(ffmpeg, "-y -hide_banner -loglevel error -i \"" + f + "\" -vf vidstabdetect=shakiness=6:accuracy=12:result='" + Cmd.Esc(trf) + "' -f null -", out e, 12 * 3600000);
                if (code == 0)
                    code = Tools.Run(ffmpeg, "-y -hide_banner -loglevel error -i \"" + f + "\" -map 0:v:0 -map 0:a? -vf vidstabtransform=input='" + Cmd.Esc(trf) + "':smoothing=20:zoom=1,unsharp=5:5:0.6 -c:v libx264 -preset medium -crf 18 -pix_fmt yuv420p -c:a copy -movflags +faststart \"" + outF + "\"", out e, 12 * 3600000);
                SafeDelete(trf);
                return code == 0 ? L.T("Stabilizzato: ", "Stabilized: ") + Path.GetFileName(outF) : "!" + L.T("Stabilizzazione non riuscita: ", "Stabilization failed: ") + Tail(e ?? "", 200);
            });
        }
    }

    void RestoreVhsSelected()
    {
        foreach (string f0 in VideoFiles())
        {
            string f = f0;
            string outF = UniqueName(Path.Combine(Path.GetDirectoryName(f), Path.GetFileNameWithoutExtension(f) + "_restaurato.mp4"));
            RunJob(L.T("Restauro VHS", "VHS restoration"), delegate
            {
                string e;
                string vf = "bwdif=mode=send_frame:deint=interlaced,hqdn3d=3:3:6:6,unsharp=5:5:0.5:3:3:0.2,eq=saturation=1.08,scale=-2:1080:flags=lanczos,setsar=1";
                int code = Tools.Run(ffmpeg, "-y -hide_banner -loglevel error -i \"" + f + "\" -map 0:v:0 -map 0:a:0? -vf " + vf + " -c:v libx264 -preset slow -crf 18 -pix_fmt yuv420p -af highpass=f=40,lowpass=f=15000,afftdn=nf=-25 -c:a aac -b:a 192k -movflags +faststart \"" + outF + "\"", out e, 24 * 3600000);
                return code == 0 ? L.T("Restaurato: ", "Restored: ") + Path.GetFileName(outF) : "!" + L.T("Restauro non riuscito: ", "Restoration failed: ") + Tail(e ?? "", 200);
            });
        }
    }

    // ================================================================= Sostituzione sicura dei file
    static void SafeDelete(string f) { try { if (File.Exists(f)) File.Delete(f); } catch { } }

    // Sostituisce "original" con "candidate" solo se la durata è coerente
    bool ReplaceVerified(string original, string candidate)
    {
        double a = Tools.Duration(ffmpeg, original), b = Tools.Duration(ffmpeg, candidate);
        if (!File.Exists(candidate) || new FileInfo(candidate).Length == 0 || b <= 0 || (a > 0 && Math.Abs(a - b) > Math.Max(2.0, a * 0.02)))
        {
            Log.W("Verifica fallita: " + candidate + " (durata " + b + " vs " + a + ")");
            SafeDelete(candidate);
            return false;
        }
        string bak = original + ".bak";
        try { if (File.Exists(bak)) File.Delete(bak); File.Move(original, bak); }
        catch (Exception ex) { Log.W("Sostituzione non possibile (file aperto?): " + ex.Message); SafeDelete(candidate); return false; }
        try { File.Move(candidate, original); File.Delete(bak); return true; }
        catch { try { if (!File.Exists(original)) File.Move(bak, original); } catch { } return false; }
    }

    // Converte in un altro file (es. MKV->MP4) e cancella l'originale solo se il nuovo è valido
    bool ConvertVerified(string original, string converted)
    {
        double a = Tools.Duration(ffmpeg, original), b = Tools.Duration(ffmpeg, converted);
        if (!File.Exists(converted) || b <= 0 || (a > 0 && Math.Abs(a - b) > Math.Max(2.0, a * 0.02)))
        {
            Log.W("Conversione non valida: " + converted);
            SafeDelete(converted);
            return false;
        }
        SafeDelete(original);
        return true;
    }

    // ================================================================= Rapporto
    // valori dell'interfaccia letti nel thread dell'interfaccia (il rapporto si scrive in background)
    string[] ReportInfo()
    {
        return new[] { cbVideo.Text + "  ·  " + cbInput.Text, (AudioOnlyFormat ? cbFormat.Text : cbCodec.Text + "  ·  " + cbFormat.Text), tbTape.Text, tbNotes.Text, detected ?? "?", (Interlocked.Read(ref dropped) - recDropsAtStart).ToString() };
    }

    void WriteReport(string file, Consumer r, string[] info)
    {
        try
        {
            var sb = new StringBuilder();
            sb.AppendLine("Video2All - " + L.T("rapporto di registrazione", "recording report"));
            sb.AppendLine(new string('-', 50));
            sb.AppendLine(L.T("File: ", "File: ") + Path.GetFileName(file));
            sb.AppendLine(L.T("Inizio: ", "Start: ") + r.Started.ToString("yyyy-MM-dd HH:mm:ss"));
            sb.AppendLine(L.T("Fine: ", "End: ") + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            double d = Tools.Duration(ffmpeg, file);
            sb.AppendLine(L.T("Durata: ", "Duration: ") + Hms(TimeSpan.FromSeconds(d)));
            sb.AppendLine(L.T("Dimensione: ", "Size: ") + (new FileInfo(file).Length / 1048576.0).ToString("0.0") + " MB");
            sb.AppendLine(L.T("Dispositivo: ", "Device: ") + info[0]);
            sb.AppendLine(L.T("Formato ricevuto: ", "Received format: ") + info[4]);
            sb.AppendLine(L.T("Codec: ", "Codec: ") + info[1]);
            sb.AppendLine(L.T("Fotogrammi persi dal grabber: ", "Frames dropped by grabber: ") + info[5]);
            sb.AppendLine(L.T("Blocchi persi in codifica: ", "Chunks lost in encoding: ") + r.Dropped);
            string mf = MarkersFile(file);
            if (File.Exists(mf)) sb.AppendLine(L.T("Marcatori: ", "Markers: ") + File.ReadAllLines(mf).Length);
            if (info[2].Length > 0) sb.AppendLine(L.T("Nastro: ", "Tape: ") + info[2]);
            if (info[3].Length > 0) sb.AppendLine(L.T("Note: ", "Notes: ") + info[3]);
            File.WriteAllText(Path.Combine(Path.GetDirectoryName(file), Path.GetFileNameWithoutExtension(file) + "_rapporto.txt"), sb.ToString(), new UTF8Encoding(false));
        }
        catch (Exception ex) { Log.W("Rapporto: " + ex.Message); }
    }

    // ================================================================= Backup impostazioni
    void BackupSettings()
    {
        SaveSettings();
        using (var d = new SaveFileDialog { Filter = L.T("Impostazioni|*.ini", "Settings|*.ini"), FileName = "Video2All_backup_" + DateTime.Now.ToString("yyyy-MM-dd") + ".ini" })
        {
            if (d.ShowDialog(this) != DialogResult.OK) return;
            try
            {
                var s = new Settings(d.FileName);
                s.D.Clear();
                foreach (var kv in cfg.D) if (kv.Key != "rtmp" && kv.Key != "rtmp2") s.D[kv.Key] = kv.Value;
                s.Save();
                SetStatus(L.T("Backup salvato (le chiavi della diretta non sono incluse).", "Backup saved (stream keys excluded)."), false, 6);
            }
            catch (Exception ex) { Msg(ex.Message); }
        }
    }

    void RestoreSettings()
    {
        if (cap != null || rec != null || str != null) { Msg(L.T("Ferma anteprima, registrazione e diretta prima di ripristinare.", "Stop preview, recording and streaming first.")); return; }
        using (var d = new OpenFileDialog { Filter = L.T("Impostazioni|*.ini", "Settings|*.ini") })
        {
            if (d.ShowDialog(this) != DialogResult.OK) return;
            var s = new Settings(d.FileName);
            if (s.D.Count == 0) { Msg(L.T("File non valido.", "Invalid file.")); return; }
            foreach (var kv in s.D) if (kv.Key != "rtmp" && kv.Key != "rtmp2") cfg.D[kv.Key] = kv.Value;
            cfg.Save();
            LoadSettings();
            LoadDevices();
            SetStatus(L.T("Impostazioni ripristinate.", "Settings restored."), false, 6);
        }
    }

    // ================================================================= Manutenzione
    void Housekeeping()
    {
        string recFolder = tbFolder.Text.Trim();
        StartThread("pulizia", delegate
        {
            // file temporanei lasciati da lavori interrotti (chiusura o crash) più vecchi di un giorno
            try
            {
                if (recFolder.Length > 0 && Directory.Exists(recFolder))
                {
                    DateTime limit = DateTime.Now.AddDays(-1);
                    foreach (string d in Directory.GetDirectories(recFolder, "~taglio_*"))
                        if (Directory.GetLastWriteTime(d) < limit) try { Directory.Delete(d, true); } catch { }
                    foreach (string pat in new[] { "acqv_*" })
                        foreach (string f in Directory.GetFiles(recFolder, pat))
                            if (File.GetLastWriteTime(f) < limit) SafeDelete(f);
                }
            }
            catch { }
            try
            {
                if (Directory.Exists(Log.Dir))
                    foreach (string f in Directory.GetFiles(Log.Dir, "*.log"))
                        if (File.GetLastWriteTime(f) < DateTime.Now.AddDays(-14)) SafeDelete(f);
                // miniature delle versioni precedenti nel profilo roaming: non servono più
                string oldThumbs = Path.Combine(Settings.AppDir, "miniature");
                if (Directory.Exists(oldThumbs)) try { Directory.Delete(oldThumbs, true); } catch { }
                if (Directory.Exists(ThumbDir))
                {
                    foreach (FileInfo n in new DirectoryInfo(ThumbDir).GetFiles("*.none"))
                        if (n.LastWriteTime < DateTime.Now.AddDays(-30)) SafeDelete(n.FullName);
                    var files = new List<FileInfo>(new DirectoryInfo(ThumbDir).GetFiles("*.jpg"));
                    if (files.Count > 1000)
                    {
                        files.Sort(delegate (FileInfo a, FileInfo b) { return a.LastAccessTime.CompareTo(b.LastAccessTime); });
                        for (int i = 0; i < files.Count - 800; i++) SafeDelete(files[i].FullName);
                    }
                }
            }
            catch { }
        });
    }

    static string StableHash(string s)
    {
        using (var md5 = System.Security.Cryptography.MD5.Create())
        {
            byte[] h = md5.ComputeHash(Encoding.UTF8.GetBytes(s));
            var sb = new StringBuilder();
            for (int i = 0; i < 8; i++) sb.Append(h[i].ToString("x2"));
            return sb.ToString();
        }
    }

    // ================================================================= Etichette di stato in alto
    void UpdateChips()
    {
        if (chipSignal == null) return;
        DateTime now = DateTime.Now;
        bool running = cap != null && !capStopping;
        bool noSig = running && (now - capStart).TotalSeconds > 4 && (now - lastFrame).TotalSeconds > 2.5;
        if (!running) chipSignal.Set(L.T("Anteprima spenta", "Preview off"), Theme.TextDim, false);
        else if (noSig) chipSignal.Set(L.T("Nessun segnale", "No signal"), Theme.Danger, true);
        else chipSignal.Set(L.T("Segnale", "Signal") + (detected != null ? "  " + detected.Split(' ')[0] : ""), Theme.Success, false);
        chipRec.Visible = rec != null;
        if (rec != null) chipRec.Set((recPaused ? L.T("PAUSA ", "PAUSED ") : "REC ") + Hms(RecElapsed()), recPaused ? Theme.Warning : Theme.Danger, !recPaused);
        chipStream.Visible = str != null || wantStream;
        if (chipStream.Visible) chipStream.Set(str != null ? "LIVE " + str.BitrateKbps.ToString("0") + " kbps" : L.T("Riconnessione", "Reconnecting"), str != null ? Theme.Accent : Theme.Warning, str == null);
        chipFps.Visible = running;
        if (running) chipFps.Set(fps + " fps" + (Interlocked.Read(ref dropped) > 0 ? " · " + Interlocked.Read(ref dropped) + L.T(" persi", " dropped") : ""), Interlocked.Read(ref dropped) > 0 ? Theme.Warning : Theme.Accent2, false);
        chipCpu.Visible = running;
        if (running) chipCpu.Set("CPU " + cpuPct + "%", cpuPct > 85 ? Theme.Warning : Theme.Accent2, false);
        chipDisk.Set(freeGbCache < 0 ? L.T("Disco ?", "Disk ?") : freeGbCache.ToString("0") + L.T(" GB liberi", " GB free"), freeGbCache >= 0 && freeGbCache < 5 ? Theme.Warning : Theme.Accent2, false);
    }
}
