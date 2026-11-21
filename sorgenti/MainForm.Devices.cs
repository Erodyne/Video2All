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
    void LoadDevices()
    {
        if (rec != null || str != null || savingCount > 0) return;
        bool wasRunning = cap != null;
        // l'anteprima che era accesa riparte da sola dopo l'aggiornamento dell'elenco
        StopCapture(delegate { if (wasRunning) autoPreviewPending = true; DoLoadDevices(); });
    }

    bool listingDevices;

    void DoLoadDevices()
    {
        if (listingDevices) return;
        listingDevices = true;
        btnRefresh.Enabled = false;
        SetStatus(L.T("Ricerca dispositivi...", "Searching devices..."), false, 0);
        StartThread("dispositivi", delegate
        {
            string output = null, error = null;
            try { Tools.Run(ffmpeg, "-hide_banner -list_devices true -f dshow -i dummy", out output, 20000); }
            catch (Exception ex) { error = ex.Message; }
            Invoke2(delegate
            {
                listingDevices = false;
                if (cap == null) FillOutputDevices(cbOutDev.SelectedIndex > 0 ? cbOutDev.SelectedItem as string : cfg.Get("outdev", ""));
                if (error != null) { SetUi(); Msg(L.T("Impossibile avviare ffmpeg.exe:\n", "Cannot start ffmpeg.exe:\n") + error); return; }
                // collegamenti USB che non cambiano i dispositivi video/audio: niente da fare
                if (output != null && output == lastDevicesOutput && cbVideo.Items.Count > 0) { SetUi(); RunPendingAutoPreview(); return; }
                lastDevicesOutput = output;
                ParseDevices(output ?? "");
                // l'anteprima automatica parte dopo la lettura dei formati: due ffmpeg sullo stesso grabber danno "I/O error"
                if (chkAutoPreview.Checked && !autoPreviewDone && cbVideo.SelectedItem != null) { autoPreviewDone = true; autoPreviewPending = true; if (capsFor == null || IsTestSource) RunPendingAutoPreview(); }
            });
        });
    }

    bool autoPreviewDone;


    int BestVideo()
    {
        int best = 0, bestScore = int.MinValue;
        string[] good = { "ezcap", "easycap", "grabber", "capture", "usb2.0", "usb 2.0", "av to usb", "elgato", "hdmi" };
        string[] bad = { "cam", "integrated", "virtual", "obs", "screen", "facetime", "ir " };
        for (int i = 0; i < cbVideo.Items.Count; i++)
        {
            string s = cbVideo.Items[i].ToString().ToLowerInvariant();
            int score = 0;
            foreach (string g in good) if (s.Contains(g)) score += 10;
            foreach (string b in bad) if (s.Contains(b)) score -= 10;
            if (score > bestScore) { bestScore = score; best = i; }
        }
        return best;
    }

    // Nome da passare a ffmpeg: quello alternativo se il nome è ambiguo o contiene caratteri problematici
    string DevName(object item)
    {
        string display = item as string;
        if (display == null) return null;
        string name, alt;
        if (!displayToDev.TryGetValue(display, out name)) name = display;
        bool needAlt = display != name || name.Contains(":") || name.Contains("\"");
        if (needAlt && altNames.TryGetValue(display, out alt)) return alt;
        return name;
    }



    void WriteOverlayText()
    {
        try
        {
            Directory.CreateDirectory(Settings.AppDir);
            string tmp = TextFilePath + ".tmp";
            File.WriteAllText(tmp, tbText.Text, new UTF8Encoding(false));
            if (File.Exists(TextFilePath)) File.Replace(tmp, TextFilePath, null); else File.Move(tmp, TextFilePath);
        }
        catch { }
    }


    static void StartThread(string name, ThreadStart fn) { new Thread(fn) { IsBackground = true, Name = name }.Start(); }

    void PreviewLoop(Process p)
    {
        Stream s;
        try { s = p.StandardOutput.BaseStream; } catch { return; }
        int size = Cmd.PW * Cmd.PH * 3;
        var buf = new byte[size];
        try
        {
            while (true)
            {
                int got = 0;
                while (got < size)
                {
                    int n = s.Read(buf, got, size - got);
                    if (n <= 0) return;
                    got += n;
                }
                if ((Interlocked.Increment(ref contentTick) & 7) == 0) contentLevel = ContentLevel(buf, Cmd.PW, Cmd.PH);
                Interlocked.Increment(ref frames);
                // un errore nel disegno non deve fermare la lettura da ffmpeg (si bloccherebbe tutta la cattura)
                try { OnPreviewFrame(buf); } catch (Exception ex) { Log.W("Fotogramma: " + ex.Message); }
            }
        }
        catch (Exception ex) { Log.W("Anteprima: " + ex.Message); }
    }

    void PumpLoop(Endpoint ep)
    {
        try
        {
            Stream pipe = ep.Accept();
            const int PK = 188;
            var buf = new byte[PK * 700];
            int fill = 0;
            bool synced = false;
            while (true)
            {
                int n = pipe.Read(buf, fill, buf.Length - fill);
                if (n <= 0) break;
                fill += n;
                if (!synced)
                {
                    int i = 0;
                    while (i < fill && buf[i] != 0x47) i++;
                    if (i > 0) { Buffer.BlockCopy(buf, i, buf, 0, fill - i); fill -= i; }
                    if (fill == 0) continue;
                    synced = true;
                }
                int aligned = fill - fill % PK;
                if (aligned < PK * 64 && fill < buf.Length) continue;
                var chunk = new byte[aligned];
                Buffer.BlockCopy(buf, 0, chunk, 0, aligned);
                int rest = fill - aligned;
                if (rest > 0) Buffer.BlockCopy(buf, aligned, buf, 0, rest);
                fill = rest;
                lock (pumpLock)
                {
                    if (!recPaused)
                    {
                        if (rec != null) rec.Feed(chunk);
                        if (arch != null) arch.Feed(chunk);
                    }
                    if (str != null) str.Feed(chunk);
                    if (str2 != null) str2.Feed(chunk);
                    // il replay è un extra: se manca memoria si svuota, ma il flusso verso registrazione e diretta continua
                    if (replayOn) { try { replay.Add(chunk); } catch (OutOfMemoryException) { replay.Clear(); Log.W("Replay: memoria esaurita, svuotato"); } }
                }
            }
        }
        catch (Exception ex) { Log.W("Flusso: " + ex.Message); }
        finally { ep.Dispose(); }
    }

    void AudioLoop(Endpoint ep)
    {
        WavePlayer wp = null;
        int dev = outDevice;
        try { wp = new WavePlayer(dev, 3840, 10); }
        catch { try { wp = new WavePlayer(-1, 3840, 10); } catch { Log.W("Audio: uscita non disponibile"); } }
        try
        {
            Stream pipe = ep.Accept();
            var buf = new byte[3840];             // 20 ms
            while (true)
            {
                int got = 0;
                while (got < buf.Length)
                {
                    int n = pipe.Read(buf, got, buf.Length - got);
                    if (n <= 0) return;
                    got += n;
                }
                int peak = 0, vol = volume;
                for (int i = 0; i + 1 < got; i += 2)
                {
                    int v = (short)(buf[i] | (buf[i + 1] << 8));
                    int a = v < 0 ? -v : v;
                    if (a > peak) peak = a;
                    if (vol != 100)
                    {
                        v = v * vol / 100;
                        if (v > 32767) v = 32767; else if (v < -32768) v = -32768;
                        buf[i] = (byte)v; buf[i + 1] = (byte)(v >> 8);
                    }
                }
                int lvl = peak * 100 / 32768;
                if (lvl > audioLevel) audioLevel = lvl;
                if (wp == null) continue;
                try
                {
                    int q = wp.Queued;
                    audioQueueMs = q * 20;
                    OnAudioQueue(audioQueueMs);
                    int max = AudioQueueLimit();
                    if (listen && q < max) wp.Write(buf, got);
                }
                catch (Exception ex)
                {
                    // uscita audio sparita (cuffie scollegate): si smette di ascoltare ma si continua a leggere,
                    // altrimenti ffmpeg resterebbe bloccato e con lui registrazione e diretta
                    Log.W("Uscita audio: " + ex.Message);
                    try { wp.Dispose(); } catch { }
                    wp = null;
                }
            }
        }
        catch (Exception ex) { Log.W("Audio: " + ex.Message); }
        finally
        {
            if (wp != null) wp.Dispose();
            ep.Dispose();
        }
    }

    volatile bool chkLowLatSafe;

    void ClosePipes()
    {
        if (epA != null) epA.Dispose();
        if (epM != null) epM.Dispose();
        epA = epM = null;
    }

    // Ferma la cattura senza bloccare l'interfaccia
    void StopCapture(MethodInvoker after)
    {
        Process p = cap;
        StopFrameDelay();
        if (p == null) { after(); return; }
        cap = null;
        capStopping = true;
        // niente più comandi dal vivo: una riga a metà insieme a "q" confonderebbe ffmpeg
        if (cmdQueue != null) { try { cmdQueue.CompleteAdding(); } catch { } cmdQueue = null; }
        SetStatus(L.T("Arresto...", "Stopping..."), false, 0);
        SetUi();
        StartThread("stop", delegate
        {
            try
            {
                if (!p.HasExited)
                {
                    try { lock (p) { p.StandardInput.Write("q"); p.StandardInput.Flush(); } } catch { }
                    if (!p.WaitForExit(6000)) p.Kill();
                    p.WaitForExit(4000);
                }
            }
            catch { }
            Invoke2(delegate
            {
                ClosePipes();
                capStopping = false;
                replay.Clear();
                store.Clear();
                view.Invalidate();
                if (glLive != null) glLive.ClearFrame();
                after();
                SetUi();
            });
        });
    }


    static int SafeExit(Process p) { try { return p.ExitCode; } catch { return -1; } }

    static string Explain(string err)
    {
        string e = err.ToLowerInvariant();
        string hint = null;
        if (e.Contains("could not setup crossbar") || e.Contains("unable to route"))
            hint = L.T("Il grabber non accetta l'ingresso scelto (Composito/S-Video). In Acquisizione > Ingresso scegli \"Predefinito\" o un altro ingresso.", "The grabber does not accept the chosen input. In Capture > Input choose \"Default\" or another input.");
        else if (e.Contains("could not find video device") || e.Contains("could not find audio"))
            hint = L.T("Dispositivo non trovato: controlla che sia collegato e premi Aggiorna.", "Device not found: check it is connected and press Refresh.");
        else if (e.Contains("could not renderstream") || e.Contains("could not connect pins") || e.Contains("could not set video options") || e.Contains("could not set audio options"))
            hint = L.T("Il grabber non accetta questo formato: prova un altro valore in 'Segnale' (PAL / NTSC / Automatico).", "The grabber does not accept this format: try another 'Signal' value (PAL / NTSC / Automatic).");
        else if (e.Contains("i/o error") || e.Contains("error opening input") || e.Contains("could not run graph") || e.Contains("busy"))
            hint = L.T("Il grabber è occupato da un altro programma (es. OBS) oppure è stato scollegato.", "The grabber is in use by another program (e.g. OBS) or was disconnected.");
        else if (e.Contains("nvenc") || e.Contains("qsv") || e.Contains("amf") || e.Contains("no capable devices") || e.Contains("cannot load"))
            hint = L.T("L'encoder scelto non è disponibile su questo PC: in Registrazione scegli 'CPU (x264)'.", "The selected encoder is not available: choose 'CPU (x264)' in Recording.");
        else if (e.Contains("rtmp") || e.Contains("connection") || e.Contains("flv") || e.Contains("broken pipe"))
            hint = L.T("Problema di collegamento con il server della diretta: controlla URL, chiave e connessione.", "Connection problem with the streaming server: check URL, key and network.");
        else if (e.Contains("no space"))
            hint = L.T("Disco pieno.", "Disk full.");
        string tail = Tail(err, 900);
        if (hint == null) return tail.Length > 0 ? L.T("Dettagli:\n", "Details:\n") + tail : L.T("Nessun dettaglio (segnale assente o grabber scollegato?).", "No details (no signal or grabber disconnected?).");
        return hint + (tail.Length > 0 ? "\n\n" + L.T("Dettagli:\n", "Details:\n") + tail : "");
    }

    static string Tail(string s, int max) { return s.Length <= max ? s : "..." + s.Substring(s.Length - max); }

    void ApplyCaptureChange()
    {
        if (cap == null || rec != null || str != null || capStopping) return;
        bool liveOpen = liveForm != null;
        StopCapture(delegate
        {
            // cambiato dispositivo durante l'anteprima: prima si leggono i suoi ingressi/formati, poi si riparte
            if (capsPending) { autoPreviewPending = true; LoadCapsAsync(); return; }
            StartCapture(false);
            if (liveOpen && liveForm == null) ToggleLive();
        });
    }
}
