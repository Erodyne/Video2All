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
    // ================================================================= Azioni
    void TogglePreview()
    {
        try { Validate(); } catch { }
        if (rec != null || str != null || capStopping) return;
        if (cap != null) StopCapture(delegate { SetStatus(L.T("Pronto", "Ready"), false, 0); });
        else if (capsPending) { triedNoPreviewAudio = false; autoPreviewPending = true; LoadCapsAsync(); }   // prima gli ingressi del nuovo dispositivo
        else { triedNoPreviewAudio = false; StartCapture(true); }
    }

    static string UniqueFile(string folder, string prefix, string ext)
    {
        string baseName = prefix + DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");
        string f = Path.Combine(folder, baseName + ext);
        int i = 2;
        while (File.Exists(f) || File.Exists(Path.Combine(folder, Path.GetFileNameWithoutExtension(f) + "_000" + ext)))
            f = Path.Combine(folder, baseName + "_" + (i++) + ext);
        return f;
    }

    string RecordFolder()
    {
        string folder = tbFolder.Text.Trim();
        if (folder.Length == 0) folder = Environment.GetFolderPath(Environment.SpecialFolder.MyVideos);
        Directory.CreateDirectory(folder);
        return folder;
    }



    void ToggleStream()
    {
        try { Validate(); } catch { }
        if (str != null || wantStream)
        {
            wantStream = false;
            streamRetryAt = DateTime.MaxValue;
            var s = str;
            lock (pumpLock) str = null;
            if (s != null) { s.Finish(); StartThread("stop diretta", delegate { if (!s.P.WaitForExit(5000)) s.Kill(); }); }
            StopStreamer2();
            SetStatus(L.T("Diretta terminata.", "Stream ended."), false, 5);
            SetUi();
            return;
        }
        string url = tbRtmp.Text.Trim();
        if (!Regex.IsMatch(url, "^(rtmps?|srt)://", RegexOptions.IgnoreCase))
        {
            Msg(L.T("Inserisci nella scheda Diretta l'indirizzo RTMP completo di chiave.", "Enter the full RTMP URL with key in the Stream tab."));
            return;
        }
        // RTMP: serve "server/app/CHIAVE"; senza chiave il server rifiuta con un errore poco chiaro
        if (Regex.IsMatch(url, "^rtmps?://", RegexOptions.IgnoreCase) && !Regex.IsMatch(url, "^rtmps?://[^/]+/[^/]+/[^/?]+", RegexOptions.IgnoreCase))
        {
            Msg(L.T("Nell'indirizzo manca la chiave della diretta (es. rtmp://live.twitch.tv/app/LA_TUA_CHIAVE).", "The stream key is missing from the URL (e.g. rtmp://live.twitch.tv/app/YOUR_KEY)."));
            return;
        }
        if (cap == null && !StartCapture(false)) return;
        wantStream = true;
        streamRetries = 0; str2Retries = 0;
        LaunchStreamer();
        LaunchStreamer2();
    }


    void OnStreamerExited(Consumer s)
    {
        if (s == str) str = null;
        if (s.StoppedByUser || !wantStream) { SetUi(); if (closing && savingCount == 0 && rec == null) Close(); return; }
        bool quick = (DateTime.Now - s.Started).TotalSeconds < 10;
        if (chkReconnect.Checked && streamRetries < 20 && !(quick && streamRetries >= 3))
        {
            streamRetries++;
            streamRetryAt = DateTime.Now.AddSeconds(Math.Min(30, 3 * streamRetries));
            SetStatus(L.T("Diretta caduta, riconnessione n. ", "Stream dropped, reconnect #") + streamRetries + "...", true, 0);
        }
        else
        {
            wantStream = false;
            SetStatus(L.T("Diretta interrotta.", "Stream stopped."), true, 15);
            Msg(L.T("La diretta si è interrotta.\n\n", "The stream stopped.\n\n") + Explain(s.Error));
        }
        SetUi();
    }

    // ---- Live a schermo intero
    void ToggleLive() { ToggleLive(null); }

    void ToggleLive(Screen target)
    {
        if (liveForm != null) { CloseLive(); return; }
        if (cap == null && !capStopping && !StartCapture(false)) return;
        Screen scr = target ?? Screen.FromControl(this);
        liveForm = new LiveWindow(this)
        {
            FormBorderStyle = FormBorderStyle.None, BackColor = Color.Black, StartPosition = FormStartPosition.Manual,
            Bounds = scr.Bounds, TopMost = true, KeyPreview = true, Text = "Live", ShowInTaskbar = false
        };
        if (blankCursor == null)
            using (var bmp = new Bitmap(16, 16)) blankCursor = new Cursor(bmp.GetHicon());
        Control surface = CreateLiveSurface();
        if (surface == null) { liveView = new VideoView { Dock = DockStyle.Fill, Store = store }; surface = liveView; }
        surface.Cursor = blankCursor;
        liveForm.Controls.Add(surface);
        surface.DoubleClick += delegate { CloseLive(); };
        liveForm.KeyDown += delegate (object s, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Escape || e.KeyCode == Keys.F11) CloseLive();
            else if (e.KeyCode == Keys.M) MoveLiveToNextScreen();
            else OnKey(s, e);
        };
        // la mini-finestra (sempre in primo piano) non deve coprire il gioco sullo stesso schermo
        bool miniHidden = false;
        if (miniForm != null && Screen.FromControl(miniForm).Equals(scr)) { miniForm.Hide(); miniHidden = true; }
        liveForm.FormClosed += delegate
        {
            liveForm = null; liveView = null; glLive = null; glNoSignal = null; view.Invalidate(); SetUi();
            if (miniHidden && miniForm != null) miniForm.Show();
        };
        UpdateViewOptions();
        // senza proprietario: riducendo la finestra principale (anche nell'area di notifica) il Live resta visibile
        liveForm.Show();
        liveForm.Activate();
        SetUi();
    }

    void CloseLive() { if (liveForm != null) liveForm.Close(); }

    void MoveLiveToNextScreen()
    {
        if (liveForm == null) return;
        Screen[] all = Screen.AllScreens;
        Screen cur = Screen.FromControl(liveForm);
        int i = Array.IndexOf(all, cur);
        Screen next = all[(i + 1) % all.Length];
        liveForm.Bounds = next.Bounds;
        // la mini-finestra si nasconde se ora starebbe sopra il Live, riappare se il Live va altrove
        if (miniForm != null)
        {
            bool same = Screen.FromControl(miniForm).Equals(next);
            if (same && miniForm.Visible) miniForm.Hide();
            else if (!same && !miniForm.Visible) miniForm.Show();
        }
    }

    void OnDisplayChanged(object sender, EventArgs e)
    {
        try { BeginInvoke((MethodInvoker)delegate { if (liveForm != null) liveForm.Bounds = Screen.FromControl(liveForm).Bounds; }); } catch { }
    }

    // ---- Foto, replay, marcatori
    void TakePhoto()
    {
        Bitmap snap = store.Snapshot();
        if (snap == null) { SetStatus(L.T("Avvia l'anteprima per scattare una foto.", "Start the preview to take a photo."), true, 5); return; }
        try
        {
            string folder = RecordFolder();
            string f = UniqueFile(folder, "Foto_", ".png");
            int h = Cmd.PH, w = (int)Math.Round(h * OutputAspect());
            using (var outBmp = new Bitmap(w, h))
            {
                using (var g = Graphics.FromImage(outBmp))
                {
                    g.InterpolationMode = chkSharp.Checked ? System.Drawing.Drawing2D.InterpolationMode.NearestNeighbor : System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                    g.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.Half;
                    g.DrawImage(snap, new Rectangle(0, 0, w, h));
                }
                outBmp.Save(f, ImageFormat.Png);
            }
            SetStatus(L.T("Foto salvata: ", "Photo saved: ") + Path.GetFileName(f), false, 5);
            RefreshFiles();
        }
        catch (Exception ex) { SetStatus(L.T("Foto non salvata: ", "Photo not saved: ") + ex.Message, true, 8); }
        finally { snap.Dispose(); }
    }

    void SaveReplay()
    {
        if (!replayOn) { SetStatus(L.T("Attiva il Replay buffer nella scheda Registrazione.", "Enable the Replay buffer in the Recording tab."), true, 6); return; }
        if (replay.Bytes == 0) { SetStatus(L.T("Replay vuoto: avvia l'anteprima.", "Replay empty: start the preview."), true, 5); return; }
        string folder;
        try { folder = RecordFolder(); } catch (Exception ex) { Msg(ex.Message); return; }
        string tmp = Path.Combine(folder, "acqv_replay_" + Guid.NewGuid().ToString("N") + ".ts");
        string outFile = UniqueFile(folder, "Replay_", ".mp4");
        bool audio = capAudio;
        RunJob(L.T("Salvataggio replay", "Saving replay"), delegate
        {
            string e;
            // scrittura del buffer fuori dall'interfaccia (può essere centinaia di MB)
            try { if (!replay.SaveTo(tmp)) return "!" + L.T("Replay vuoto.", "Replay empty."); }
            catch (Exception ex) { try { File.Delete(tmp); } catch { } return "!" + L.T("Replay non salvato: ", "Replay not saved: ") + ex.Message; }
            int code = Tools.Run(ffmpeg, "-y -hide_banner -loglevel error -f mpegts -i \"" + tmp + "\" -map 0:v" + (audio ? " -map 0:a" : "") + " -c:v libx264 -preset veryfast -crf 20 -pix_fmt yuv420p" + (audio ? " -c:a aac -b:a 192k" : "") + " -movflags +faststart \"" + outFile + "\"", out e, 600000);
            try { File.Delete(tmp); } catch { }
            return code == 0 ? L.T("Replay salvato: ", "Replay saved: ") + Path.GetFileName(outFile) : "!" + L.T("Replay non salvato: ", "Replay not saved: ") + Tail(e ?? "", 200);
        });
    }


    // ---- Bordi neri (sulla percentuale del fotogramma attuale)
    void DetectBorders()
    {
        Bitmap bmp = store.Snapshot();
        if (bmp == null) { Msg(L.T("Avvia l'anteprima con un'immagine a schermo, poi premi Rileva.", "Start the preview with a picture on screen, then press Detect.")); return; }
        try
        {
            int w = bmp.Width, h = bmp.Height;
            BitmapData d = bmp.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.ReadOnly, PixelFormat.Format24bppRgb);
            var px = new byte[d.Stride * h];
            System.Runtime.InteropServices.Marshal.Copy(d.Scan0, px, 0, px.Length);
            int stride = d.Stride;
            bmp.UnlockBits(d);
            // soglia adattiva: un po' sopra il nero più scuro dei bordi (le VHS hanno bordi rumorosi)
            long cs = 0; int cn = 0;
            for (int y = h / 3; y < 2 * h / 3; y += 6) for (int x = w / 3; x < 2 * w / 3; x += 6) { int i = y * stride + x * 3; cs += (px[i] + px[i + 1] + px[i + 2]) / 3; cn++; }
            int center = cn > 0 ? (int)(cs / cn) : 128;
            int thr = Math.Max(24, Math.Min(48, center / 4));
            Func<int, int, bool> dark = delegate (int x, int y) { int i = y * stride + x * 3; return (px[i] + px[i + 1] + px[i + 2]) / 3 < thr; };
            Func<int, bool> colDark = delegate (int x) { int b = 0, n = 0; for (int y = 0; y < h; y += 4) { n++; if (!dark(x, y)) b++; } return b <= n / 20; };
            Func<int, bool> rowDark = delegate (int y) { int b = 0, n = 0; for (int x = 0; x < w; x += 4) { n++; if (!dark(x, y)) b++; } return b <= n / 20; };
            int l = 0; while (l < w / 4 && colDark(l)) l++;
            int r = 0; while (r < w / 4 && colDark(w - 1 - r)) r++;
            int t = 0; while (t < h / 4 && rowDark(t)) t++;
            int b2 = 0; while (b2 < h / 4 && rowDark(h - 1 - b2)) b2++;
            if (l + r + t + b2 == 0) { SetStatus(L.T("Nessun bordo nero rilevato.", "No black borders found."), false, 5); return; }
            decimal remW = 100 - nCropL.Value - nCropR.Value, remH = 100 - nCropT.Value - nCropB.Value;
            Func<decimal, decimal> rnd = delegate (decimal v) { return Math.Round(v * 2, MidpointRounding.AwayFromZero) / 2; };
            nCropL.Value = ClampD(rnd(nCropL.Value + remW * l / w), 0, 25);
            nCropR.Value = ClampD(rnd(nCropR.Value + remW * r / w), 0, 25);
            nCropT.Value = ClampD(rnd(nCropT.Value + remH * t / h), 0, 25);
            nCropB.Value = ClampD(rnd(nCropB.Value + remH * b2 / h), 0, 25);
            SetStatus(L.T("Bordi rilevati e ritagliati.", "Borders detected and cropped."), false, 5);
        }
        catch (Exception ex) { SetStatus(L.T("Rilevamento non riuscito: ", "Detection failed: ") + ex.Message, true, 6); }
        finally { bmp.Dispose(); }
    }

    // ---- Programmazione
    void ArmSchedule()
    {
        scheduleArmed = chkSchedule.Checked;
        if (!scheduleArmed) return;
        DateTime now = DateTime.Now;
        scheduleAt = now.Date + dtSchedule.Value.TimeOfDay;
        if (scheduleAt <= now.AddSeconds(5)) scheduleAt = scheduleAt.AddDays(1);
        if (!loading) SetStatus(L.T("Registrazione programmata: ", "Recording scheduled: ") + scheduleAt.ToString("dd/MM HH:mm"), false, 5);
    }
}

// Finestra del Live: i tasti assegnati (anche F10 e combinazioni con Alt) arrivano al programma prima che Windows li usi per il menu
sealed class LiveWindow : Form
{
    readonly MainForm owner;
    public LiveWindow(MainForm owner) { this.owner = owner; }
    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (((keyData & Keys.KeyCode) == Keys.F10 || (keyData & Keys.Alt) != 0) && owner.HandleLiveKey(keyData)) return true;
        return base.ProcessCmdKey(ref msg, keyData);
    }
}
