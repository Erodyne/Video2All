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
    // ================================================================= Registrazione
    string BuildFileName(string folder, string ext, int part)
    {
        DateTime now = DateTime.Now;
        string t = tbTemplate.Text.Trim();
        if (t.Length == 0) t = "Acquisizione_{data}_{ora}";
        string dev = (cbVideo.SelectedItem as string ?? "").Split('(')[0].Trim();
        t = t.Replace("{data}", now.ToString("yyyy-MM-dd")).Replace("{ora}", now.ToString("HH-mm-ss"))
             .Replace("{nastro}", tbTape.Text.Trim()).Replace("{titolo}", tbTitle.Text.Trim())
             .Replace("{profilo}", cbInput.Text.Split('(')[0].Trim()).Replace("{dispositivo}", dev)
             .Replace("{n}", part.ToString("00"));
        foreach (char c in Path.GetInvalidFileNameChars()) t = t.Replace(c, '_');
        t = t.Replace('%', '_');        // % ha un significato speciale per la divisione in parti
        t = t.Trim(' ', '.', '_');
        if (t.Length == 0) t = "Acquisizione_" + now.ToString("yyyy-MM-dd_HH-mm-ss");
        if (t.Length > 120) t = t.Substring(0, 120).TrimEnd(' ', '.', '_');      // titoli lunghi: percorso oltre il limite di Windows
        // nomi riservati di Windows (CON, NUL, COM1...): il file non si potrebbe creare
        if (Regex.IsMatch(t, @"^(con|prn|aux|nul|com\d|lpt\d)$", RegexOptions.IgnoreCase)) t = "_" + t;
        if (part > 1 && !tbTemplate.Text.Contains("{n}")) t += "_parte" + part.ToString("00");
        if (chkDateFolder.Checked) { folder = Path.Combine(folder, now.ToString("yyyy-MM-dd")); Directory.CreateDirectory(folder); }
        return UniqueName(Path.Combine(folder, t + ext));
    }

    Dictionary<string, string> Metadata()
    {
        var m = new Dictionary<string, string>();
        m["title"] = tbTitle.Text.Trim().Length > 0 ? tbTitle.Text.Trim() : tbTape.Text.Trim();
        m["comment"] = tbNotes.Text.Trim();
        m["album"] = tbTape.Text.Trim();
        m["date"] = DateTime.Now.ToString("yyyy-MM-dd");
        m["encoder_note"] = "Video2All";
        return m;
    }

    void StartRecording()
    {
        if (rec != null) return;
        try { Validate(); } catch { }     // un numero appena scritto (senza uscire dalla casella) vale subito, anche con i tasti F
        if (capStopping) { SetStatus(L.T("Attendi: la cattura si sta riavviando, poi premi di nuovo Registra.", "Wait: capture is restarting, then press Record again."), true, 4); return; }
        bool ao = AudioOnlyFormat;
        if (ao && cbAudio.SelectedIndex <= 0 && !IsTestSource && !chkMic.Checked) { Msg(L.T("Per registrare solo l'audio seleziona un dispositivo audio.", "Select an audio device to record audio only.")); return; }
        string folder;
        try { folder = RecordFolder(); }
        catch (Exception ex) { Msg(L.T("Cartella non valida:\n", "Invalid folder:\n") + ex.Message); return; }
        double gb = Native.FreeGB(folder);
        if (gb >= 0 && gb < 1.0) { Msg(L.T("Spazio su disco insufficiente (meno di 1 GB).", "Not enough disk space (less than 1 GB).")); return; }
        // Il senza perdita / interlacciato richiedono la cattura adatta
        bool needLossless = !ao && (Enc.IsLossless(cbCodec.SelectedIndex) || chkArchive.Checked);
        if (needLossless && !chkLossMez.Checked) { loading = true; chkLossMez.Checked = true; loading = false; }
        if (cap != null && (needLossless && !lastLosslessMez || chkInterlaced.Checked != lastKeepInterlaced))
        {
            // riavviare la cattura farebbe cadere la diretta in corso
            if (str != null || wantStream) { Msg(L.T("Per registrare con queste impostazioni (senza perdita / interlacciato) bisogna riavviare la cattura: ferma prima la diretta.", "These settings (lossless / interlaced) need a capture restart: stop the stream first.")); return; }
            StopCapture(delegate { if (StartCapture(false)) StartRecording(); });
            return;
        }
        if (cap == null && !StartCapture(false)) return;
        recPart = 1;
        recBytesCache = partBytesCache = 0;      // i valori della registrazione precedente non devono far scattare gli stop
        // la pausa "senza buchi" esiste solo a frequenza costante (o in solo audio); altrimenti il file conserva l'intervallo
        recPausable = ao || (chkCfr.Checked && SelectedFpsValue() != null);
        recPaused = false; btnPause.Active = false;
        recDropsAtStart = (int)Interlocked.Read(ref dropped);
        if (chkTapeSeries.Checked) tbTape.Text = L.T("Nastro ", "Tape ") + nTapeNumber.Value;
        partStart = DateTime.UtcNow;
        dropAtRecStart = Interlocked.Read(ref dropped);
        Consumer c = CreateRecorder(folder, recPart, true);
        if (c == null) return;
        if (chkArchive.Checked && !ao)
        {
            archFile = BuildFileName(folder, "_archivio.mkv", 1);
            try
            {
                var a = new Consumer(ffmpeg, Enc.Archive(archFile, capAudio, capHasMic, chkInterlaced.Checked, Metadata(), capAudio24), "archivio", (string[])null);
                a.File = archFile;
                a.Exited += delegate (Consumer x) { Invoke2(delegate { OnRecorderExited(x); }); };
                a.Start();
                // pre-registrazione e collegamento insieme: nessun blocco perso o doppio tra i due passaggi
                lock (pumpLock) { FeedPreroll(a, prerollUsed); arch = a; }
            }
            catch (Exception ex) { SetStatus(L.T("Copia d'archivio non avviata: ", "Archive copy not started: ") + ex.Message, true, 8); }
        }
        SetStatus(StateText(), false, 0);
        SetUi();
        UpdateEncUi();
    }

    bool lastLosslessMez, lastKeepInterlaced;

    // Crea e avvia il registratore; con "first" applica la pre-registrazione
    Consumer CreateRecorder(string folder, int part, bool first)
    {
        bool ao = AudioOnlyFormat;
        string file, args;
        EncSettings es = CurrentEnc();
        int container = cbFormat.SelectedIndex;
        if (ao)
        {
            int af = container - Enc.Containers.Length;
            file = BuildFileName(folder, Enc.AudioOnlyExt[af], part);
            args = Enc.AudioRecorder(file, af, (int)nAKbps.Value, capAudio, capHasMic, Metadata(), capAudio24);
        }
        else
        {
            string warn = Enc.Fix(ref container, es);
            if (warn != null) SetStatus(warn, true, 8);
            file = BuildFileName(folder, Enc.ContainerExt[container], part);
            int splitMin = (int)nSplit.Value;
            if (splitMin > 0) file = file.Substring(0, file.Length - Path.GetExtension(file).Length) + "_%03d" + Path.GetExtension(file);
            args = Enc.Recorder(file, container, es, Fps(), capAudio, capHasMic, splitMin, Metadata(), chkCfr.Checked && SelectedFpsValue() != null);
        }
        Consumer c;
        try
        {
            c = new Consumer(ffmpeg, args, "registrazione", (string[])null);
            c.File = file;
            c.Exited += delegate (Consumer x) { Invoke2(delegate { OnRecorderExited(x); }); };
            c.Start();
        }
        catch (Exception ex) { Msg(L.T("Impossibile avviare la registrazione:\n", "Cannot start recording:\n") + ex.Message); return null; }
        int pre = first ? (int)nPreroll.Value : 0;
        prerollUsed = first ? pre : 0;
        lock (pumpLock)
        {
            if (pre > 0) foreach (byte[] chunk in replay.Snapshot(pre)) c.Feed(chunk);
            rec = c;
        }
        if (first)
        {
            currentFile = file;
            recStart = DateTime.UtcNow.AddSeconds(-pre);   // ora UTC: i cambi di ora legale non spostano i tempi
        }
        return c;
    }

    void FeedPreroll(Consumer c, int sec)
    {
        if (sec <= 0) return;
        foreach (byte[] chunk in replay.Snapshot(sec)) c.Feed(chunk);
    }

    // Nuovo file senza fermare la cattura (divisione per dimensione)
    void RotateRecording()
    {
        Consumer old = rec;
        if (old == null) return;
        string folder;
        try { folder = RecordFolder(); } catch { return; }
        recPart++;
        partBytesCache = 0;
        Consumer c = CreateRecorder(folder, recPart, false);     // sostituisce rec sotto pumpLock
        if (c == null) { recPart--; return; }
        // le parti successive usano i marcatori del primo file, spostati all'inizio della parte
        lock (partMarkers) partMarkers[c.File] = new KeyValuePair<string, double>(MarkersFile(currentFile), RecElapsed().TotalSeconds);
        savingCount++;
        old.Finish();
        StartThread("attesa parte", delegate { if (!old.P.WaitForExit(600000)) old.Kill(); });   // se non chiude, non blocca la chiusura del programma
        partStart = DateTime.UtcNow;
        SetStatus(L.T("Nuova parte: ", "New part: ") + Path.GetFileName(c.File), false, 5);
    }

    DateTime partStart;
    bool recPausable;
    readonly Dictionary<string, KeyValuePair<string, double>> partMarkers = new Dictionary<string, KeyValuePair<string, double>>();

    void StopRecording()
    {
        if (rec == null) return;
        StopRecordingCore(null);
    }

    void StopRecordingCore(string reason)
    {
        Consumer r, a;
        lock (pumpLock) { r = rec; a = arch; rec = null; arch = null; }
        recPaused = false; btnPause.Active = false; schedStopAt = DateTime.MaxValue;
        foreach (Consumer c in new[] { r, a })
        {
            if (c == null) continue;
            savingCount++;
            c.Finish();
            var cc = c;
            StartThread("attesa", delegate { if (!cc.P.WaitForExit(600000)) cc.Kill(); });
        }
        SetStatus(L.T("Salvataggio in corso...", "Saving...") + (reason != null ? " (" + reason + ")" : ""), false, 0);
        SetUi();
        UpdateEncUi();
    }

    void OnRecorderExited(Consumer r)
    {
        bool wasActive = r == rec || r == arch;
        lock (pumpLock) { if (r == rec) rec = null; if (r == arch) arch = null; }
        if (!wasActive) savingCount = Math.Max(0, savingCount - 1);
        int code = r.SafeExit();
        string name = Path.GetFileName((r.File ?? "").Replace("_%03d", ""));
        if (r.StoppedByUser && code == 0)
        {
            SetStatus(L.T("Salvato: ", "Saved: ") + name + (r.Dropped > 0 ? L.T("  (blocchi persi: ", "  (lost chunks: ") + r.Dropped + ")" : ""), false, 8);
            if (r.Name == "registrazione")
            {
                // il rapporto si scrive dopo la post-elaborazione, con il nome definitivo del file (es. dopo MKV -> MP4)
                string[] reportInfo = chkReport.Checked && r.File != null && !r.File.Contains("%03d") ? ReportInfo() : null;
                if (chkTapeSeries.Checked && r.File == currentFile) nTapeNumber.Value = Math.Min(nTapeNumber.Maximum, nTapeNumber.Value + 1);
                Alert(L.T("Registrazione salvata: ", "Recording saved: ") + name, false);
                QueuePostProcess(r.File, prerollUsed, r, reportInfo);
            }
            else if (r.Name == "archivio" && (chkMd5.Checked || chkCopyTo.Checked)) QueuePostProcess(r.File, -1, null, null);
        }
        else if (r.StoppedByUser)
            SetStatus(L.T("Salvato con avvisi (codice ", "Saved with warnings (code ") + code + "): " + name, true, 10);
        else if (r.Name == "archivio" && rec != null)
        {
            // si è fermata solo la copia d'archivio: la registrazione principale continua
            Alert(L.T("Copia d'archivio interrotta (la registrazione continua).", "Archive copy stopped (recording continues)."), true);
            SetStatus(L.T("Copia d'archivio interrotta: ", "Archive copy stopped: ") + Tail(r.Error, 160), true, 15);
        }
        else
        {
            if (wasActive && r.Name == "registrazione" && arch != null) { var a = arch; lock (pumpLock) arch = null; savingCount++; a.Finish(); }
            Alert(L.T("Registrazione interrotta.", "Recording stopped unexpectedly."), true);
            Msg(L.T("La registrazione si è interrotta.\n\n", "Recording stopped unexpectedly.\n\n") + Explain(r.Error));
        }
        RefreshFiles();
        SetUi();
        UpdateEncUi();
        if (closing && savingCount == 0 && rec == null && str == null) Close();
    }

    void LaunchStreamer()
    {
        streamRetryAt = DateTime.MaxValue;
        if (!wantStream || cap == null) return;
        string url = tbRtmp.Text.Trim();
        string args = Enc.Streamer(url, CurrentEncForStream(), Fps(), capAudio, capHasMic);
        try
        {
            var c = new Consumer(ffmpeg, args, "diretta", Consumer.UrlSecrets(url));
            c.Exited += delegate (Consumer x) { Invoke2(delegate { OnStreamerExited(x); }); };
            c.Start();
            lock (pumpLock) str = c;
        }
        catch (Exception ex) { wantStream = false; Msg(L.T("Impossibile avviare la diretta:\n", "Cannot start stream:\n") + ex.Message); }
        SetUi();
    }

    EncSettings CurrentEncForStream()
    {
        EncSettings s = CurrentEnc();
        s.Bitrate = (int)nBitrate.Value;
        s.Interlaced = false;
        s.Aspect = null;
        if (cbAspect.SelectedIndex == 2) s.SquareScale = "scale=round(ih*" + Cmd.F(OutputAspect()) + "/2)*2:ih,setsar=1";
        return s;
    }

    void AddMarker()
    {
        if (rec == null || currentFile == null) { SetStatus(L.T("I marcatori si aggiungono durante la registrazione.", "Markers are added while recording."), true, 5); return; }
        TimeSpan t = RecElapsed();
        string mf = MarkersFile(currentFile);
        try
        {
            int n = File.Exists(mf) ? File.ReadAllLines(mf).Length + 1 : 1;
            File.AppendAllText(mf, Hms(t) + " " + L.T("Marcatore ", "Marker ") + n + "\r\n", new UTF8Encoding(false));
            SetStatus(L.T("Marcatore ", "Marker ") + n + L.T(" a ", " at ") + Hms(t), false, 4);
        }
        catch (Exception ex) { SetStatus(ex.Message, true, 5); }
    }

    // durata registrata (ferma durante la pausa) e formato ore:minuti:secondi anche oltre le 24 ore
    TimeSpan RecElapsed() { return (recPaused && recPausable ? pauseStart : DateTime.UtcNow) - recStart; }
    static string Hms(TimeSpan t)
    {
        if (t < TimeSpan.Zero) t = TimeSpan.Zero;
        return ((int)t.TotalHours).ToString("00") + ":" + t.Minutes.ToString("00") + ":" + t.Seconds.ToString("00");
    }

    static string MarkersFile(string recFile)
    {
        string b = recFile.Replace("_%03d", "");
        return Path.Combine(Path.GetDirectoryName(b), Path.GetFileNameWithoutExtension(b) + "_marcatori.txt");
    }

    void UpdateReplayState()
    {
        int need = Math.Max(chkReplay.Checked ? (int)nReplaySec.Value : 0, (int)nPreroll.Value);
        replay.Seconds = Math.Max(5, need);
        replayOn = need > 0;
        if (!replayOn) replay.Clear();
    }
}
