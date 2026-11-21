using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Threading;
using System.Windows.Forms;

// Motore anteprima: sincronia audio/video, misura della latenza, scelta del disegno (GPU / GDI)
public partial class MainForm
{
    // 0 = bassa latenza (il video non aspetta mai), 1 = audio e video sincronizzati
    volatile int previewSync;
    volatile int videoDelayMs;          // ritardo applicato al video in modalità sincronizzata
    double smoothAudioMs;
    FrameDelay frameDelay;

    // statistiche
    long lastArrivalTicks;
    double jitterMs, presentMs;
    static readonly double TickMs = 1000.0 / Stopwatch.Frequency;

    void OnPreviewFrame(byte[] buf)
    {
        long now = Stopwatch.GetTimestamp();
        if (lastArrivalTicks != 0)
        {
            double dt = (now - lastArrivalTicks) * TickMs;
            if (dt < 500)
            {
                jitterMs = jitterMs * 0.95 + Math.Abs(dt - intervalMs) * 0.05;
                intervalMs = intervalMs * 0.95 + dt * 0.05;
            }
        }
        lastArrivalTicks = now;
        if (probeSentTicks != 0) CheckProbe(buf, now);
        else cornerMagenta = IsMagenta(buf);

        FrameDelay fd = frameDelay;
        if (previewSync == 1 && fd != null && videoDelayMs > 4) fd.Enqueue(buf, now + (long)(videoDelayMs / TickMs));
        else
        {
            if (fd != null) fd.Flush();          // passando a bassa latenza non si mostrano fotogrammi vecchi
            PresentFrame(buf, now);
        }
    }

    void PresentFrame(byte[] buf, long arrival)
    {
        store.Push(buf);
        presentMs = presentMs * 0.9 + (Stopwatch.GetTimestamp() - arrival) * TickMs * 0.1;
        lastFrame = DateTime.Now;
        GlView gl = glLive;                  // copia locale: il Live può chiudersi in questo istante (altrimenti l'anteprima si fermava)
        if (gl != null) gl.Upload(buf);
        if (Interlocked.Exchange(ref uiPending, 1) == 0)
        {
            try
            {
                BeginInvoke((MethodInvoker)delegate
                {
                    Interlocked.Exchange(ref uiPending, 0);
                    if (glLive != null) glLive.Invalidate();
                    else if (liveView != null) liveView.Invalidate(); else view.Invalidate();
                    // con il Live aperto l'anteprima principale si aggiorna comunque, più di rado (non sembra bloccata)
                    if ((glLive != null || liveView != null) && (++mainTick % 6) == 0) view.Invalidate();
                    if (miniView != null) miniView.Invalidate();
                });
            }
            catch { Interlocked.Exchange(ref uiPending, 0); }
        }
    }

    double intervalMs = 40;
    double PreviewFps() { return 1000.0 / Math.Max(5, intervalMs); }

    // chiamato dal ciclo audio: aggiorna il ritardo video per allinearlo a ciò che si sente
    void OnAudioQueue(int queuedMs)
    {
        smoothAudioMs = smoothAudioMs * 0.9 + queuedMs * 0.1;
        // ~20 ms di latenza fissa del driver audio di Windows
        videoDelayMs = previewSync == 1 && capPreviewAudio && listen ? (int)Math.Min(400, smoothAudioMs + 20) : 0;
    }

    int AudioQueueLimit()
    {
        if (previewSync == 1) return 6;          // 120 ms: suono continuo, video ritardato di pari misura
        // bassa latenza: si scarta l'audio in eccesso; sotto i 60 ms waveOut va spesso a vuoto (crepitii)
        return chkLowLatSafe ? 3 : 4;
    }

    void StartFrameDelay()
    {
        StopFrameDelay();
        // nuova cattura: niente ritardo ereditato dalla precedente (magari senza audio in anteprima)
        videoDelayMs = 0; smoothAudioMs = 0; lastArrivalTicks = 0; intervalMs = 40; jitterMs = 0;
        frameDelay = new FrameDelay(Cmd.PW * Cmd.PH * 3, 16, PresentFrame);
    }

    void StopFrameDelay()
    {
        FrameDelay fd = frameDelay;
        frameDelay = null;
        if (fd != null) fd.Dispose();
    }

    // ---------------------------------------------------------------- misura della latenza
    // Si accende un quadratino magenta nell'anteprima con un comando dal vivo e si misura
    // quanto tempo passa prima che arrivi al programma. Si ripete 6 volte e si tiene il minimo
    // (ffmpeg legge i comandi ogni ~100 ms). Non comprende il ritardo del grabber e dello schermo.
    long probeSentTicks;
    readonly List<double> probeResults = new List<double>();
    int probeRuns;
    System.Windows.Forms.Timer probeTimer;

    void StartLatencyTest()
    {
        if (cap == null || cmdQueue == null) { Msg(L.T("Avvia prima l'anteprima.", "Start the preview first.")); return; }
        if (probeTimer != null) return;
        probeResults.Clear();
        probeRuns = 0;
        SetStatus(L.T("Misura della latenza in corso…", "Measuring latency…"), false, 5);
        probeTimer = new System.Windows.Forms.Timer { Interval = 700 };
        probeTimer.Tick += delegate
        {
            if (probeSentTicks != 0 && (Stopwatch.GetTimestamp() - probeSentTicks) * TickMs > 2000) probeSentTicks = 0;   // perso
            if (probeRuns >= 6 || cap == null) { FinishLatencyTest(); return; }
            if (probeSentTicks != 0) return;
            probeRuns++;
            if (cornerMagenta) return;           // l'immagine ha già quel colore nell'angolo: misura non affidabile
            SendProbe(true);
        };
        probeTimer.Start();
    }

    void SendProbe(bool on)
    {
        var q = cmdQueue;
        if (q == null) return;
        string line = "drawbox@probe -1 color " + (on ? "0xFF00FF@1" : "0xFF00FF@0");
        if (on) probeWanted = true;
        try { q.TryAdd(line); } catch { }
    }

    volatile bool probeWanted;

    // chiamato dal thread dei comandi subito dopo aver scritto la riga
    void OnCommandWritten(string line)
    {
        if (probeWanted && line.StartsWith("drawbox@probe") && line.EndsWith("@1")) { probeWanted = false; probeSentTicks = Stopwatch.GetTimestamp(); }
    }

    volatile bool cornerMagenta;

    static bool IsMagenta(byte[] buf)
    {
        int i = (3 * Cmd.PW + 3) * 3;      // pixel (3,3) BGR
        return buf[i] > 200 && buf[i + 1] < 60 && buf[i + 2] > 200;
    }

    void CheckProbe(byte[] buf, long now)
    {
        if (IsMagenta(buf))
        {
            double ms = (now - probeSentTicks) * TickMs;
            probeSentTicks = 0;
            lock (probeResults) probeResults.Add(ms);
            SendProbe(false);
        }
    }

    void FinishLatencyTest()
    {
        if (probeTimer != null) { probeTimer.Stop(); probeTimer.Dispose(); probeTimer = null; }
        probeSentTicks = 0;
        SendProbe(false);
        double min = 0, avg = 0;
        int n;
        lock (probeResults)
        {
            n = probeResults.Count;
            if (n > 0) { min = double.MaxValue; foreach (double v in probeResults) { min = Math.Min(min, v); avg += v; } avg /= n; }
        }
        if (n == 0) { Msg(L.T("Misura non riuscita (nessun quadratino ricevuto). Riprova con l'anteprima attiva.", "Measurement failed. Try again with the preview running.")); return; }
        double frame = 1000.0 / PreviewFps();
        double paint = presentMs + frame / 2;      // in media mezzo fotogramma prima del disegno
        double total = min + paint;
        string txt = L.T("Latenza del programma (dal fotogramma elaborato allo schermo):", "Program latency (from processed frame to screen):") + "\n\n" +
            L.T("  elaborazione ffmpeg: ", "  ffmpeg processing: ") + min.ToString("0") + " ms (" + L.T("media ", "avg ") + avg.ToString("0") + ")\n" +
            L.T("  consegna e disegno: ", "  delivery and drawing: ") + paint.ToString("0") + " ms\n" +
            (videoDelayMs > 0 ? L.T("  ritardo per sincronia audio: ", "  delay for audio sync: ") + videoDelayMs + " ms\n" : "") +
            (capPreviewAudio ? L.T("  audio in coda: ", "  queued audio: ") + audioQueueMs + " ms\n" : "") +
            L.T("  irregolarità arrivo fotogrammi: ", "  frame arrival jitter: ") + jitterMs.ToString("0.0") + " ms\n\n" +
            L.T("Totale video ≈ ", "Total video ≈ ") + (total + videoDelayMs).ToString("0") + " ms\n\n" +
            L.T("A questo si aggiungono il grabber (di solito 30–80 ms) e lo schermo. Per giocare: modalità \"Bassa latenza\", Live a schermo intero, disegno GPU.",
                "Add the grabber (usually 30–80 ms) and the display. For gaming: \"Low latency\" mode, fullscreen Live, GPU drawing.");
        Log.W("Latenza: " + txt.Replace("\n", " | "));
        Msg(txt);
    }

    // ---------------------------------------------------------------- Live con OpenGL
    GlView glLive;

    Control CreateLiveSurface()
    {
        glLive = null;
        if (chkGpuLive != null && chkGpuLive.Checked && !gpuFailed)
        {
            try
            {
                var g = new GlView(Cmd.PW, Cmd.PH) { Dock = DockStyle.Fill };
                g.Failed += delegate
                {
                    gpuFailed = true;
                    Log.W("OpenGL non disponibile: torno al disegno GDI");
                    BeginInvoke((MethodInvoker)delegate { if (glLive == g) { CloseLive(); ToggleLive(); } });
                };
                g.Aspect = view.Aspect; g.Sharp = view.Sharp; g.Scanlines = view.Scanlines;
                glLive = g;
                // avviso "nessun segnale" sopra il disegno della scheda grafica
                glNoSignal = new Label { Text = L.T("NESSUN SEGNALE", "NO SIGNAL"), AutoSize = false, Dock = DockStyle.Top, Height = 54, TextAlign = ContentAlignment.MiddleCenter,
                    BackColor = Theme.Danger, ForeColor = Color.White, Font = Theme.UiBold(18f), Visible = false };
                g.Controls.Add(glNoSignal);
                Bitmap snap = store.Snapshot();
                if (snap != null) { g.UploadBitmap(snap); snap.Dispose(); }
                return g;
            }
            catch (Exception ex) { gpuFailed = true; Log.W("OpenGL: " + ex.Message); glLive = null; }
        }
        return null;
    }

    bool gpuFailed;
    int mainTick;
    Label glNoSignal;
}

// Coda di fotogrammi con ritardo, su un thread dedicato (non blocca mai la lettura da ffmpeg)
public sealed class FrameDelay : IDisposable
{
    readonly Queue<byte[]> free = new Queue<byte[]>();
    readonly Queue<KeyValuePair<long, byte[]>> pending = new Queue<KeyValuePair<long, byte[]>>();
    readonly Action<byte[], long> present;
    readonly Thread thread;
    readonly object sync = new object();
    volatile bool stop;

    public FrameDelay(int size, int count, Action<byte[], long> present)
    {
        this.present = present;
        for (int i = 0; i < count; i++) free.Enqueue(new byte[size]);
        thread = new Thread(Run) { IsBackground = true, Name = "presentazione", Priority = ThreadPriority.AboveNormal };
        thread.Start();
    }

    public void Enqueue(byte[] src, long due)
    {
        lock (sync)
        {
            byte[] b;
            if (free.Count > 0) b = free.Dequeue();
            else b = pending.Dequeue().Value;          // coda piena: si scarta il più vecchio
            Buffer.BlockCopy(src, 0, b, 0, Math.Min(src.Length, b.Length));
            pending.Enqueue(new KeyValuePair<long, byte[]>(due, b));
            Monitor.Pulse(sync);
        }
    }

    void Run()
    {
        while (!stop)
        {
            KeyValuePair<long, byte[]> item;
            lock (sync)
            {
                while (pending.Count == 0 && !stop) Monitor.Wait(sync, 200);
                if (stop) return;
                item = pending.Peek();
                long wait = (long)((item.Key - Stopwatch.GetTimestamp()) * 1000.0 / Stopwatch.Frequency);
                if (wait > 1) { Monitor.Wait(sync, (int)Math.Min(wait, 50)); continue; }
                pending.Dequeue();
            }
            try { present(item.Value, item.Key); } catch { }
            lock (sync) free.Enqueue(item.Value);
        }
    }

    public void Flush()
    {
        lock (sync)
        {
            if (pending.Count == 0) return;
            while (pending.Count > 0) free.Enqueue(pending.Dequeue().Value);
        }
    }

    public void Dispose()
    {
        stop = true;
        lock (sync) Monitor.PulseAll(sync);
    }
}
