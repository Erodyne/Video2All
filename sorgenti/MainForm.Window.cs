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
    // ================================================================= Tasti, tema, chiusura, timer
    const int WM_HOTKEY = 0x0312;

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        Theme.ApplyWindow(this);
        RegisterGlobalKeys();
    }

    void OnClosingForm(object sender, FormClosingEventArgs e)
    {
        SaveSettings();
        int pending;
        lock (postQueue) pending = postQueue.Count;
        if (jobCount + postRunning + pending > 0 && !closing && e.CloseReason == CloseReason.UserClosing &&
            Dlg.Show(this, L.T("Ci sono lavori in corso (taglio, compressione, conversione o copia dopo la registrazione). Chiudere comunque? Verranno interrotti.", "Jobs are running (trim/compress/replay). Close anyway? They will be stopped."), Text, MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
        { e.Cancel = true; return; }
        if (rec != null || str != null || savingCount > 0)
        {
            if (!closing)
            {
                closing = true;
                e.Cancel = true;
                wantStream = false;
                if (str != null) { var s = str; lock (pumpLock) str = null; s.Kill(); }
                StopStreamer2();
                if (rec != null) StopRecording();
                SetStatus(L.T("Chiusura: salvataggio in corso...", "Closing: saving..."), false, 0);
                Hide();
                tray.Visible = false;
                return;
            }
            if (savingCount > 0 || rec != null) { e.Cancel = true; return; }
        }
        closing = true;
        CloseLive();
        Process p = cap;
        cap = null;
        if (p != null) { try { if (!p.HasExited) p.Kill(); } catch { } }
        ClosePipes();
        SystemEvents.DisplaySettingsChanged -= OnDisplayChanged;
        UnregisterGlobalKeys();
        if (miniForm != null) miniForm.Close();
        Native.KeepAwake(false, false);
        tray.Visible = false;
        tray.Dispose();
        Log.W("=== Chiusura");
    }



    void UpdateCpu()
    {
        TimeSpan total = TimeSpan.Zero;
        foreach (Process p in new[] { cap, rec != null ? rec.P : null, str != null ? str.P : null })
        {
            if (p == null) continue;
            try { if (!p.HasExited) total += p.TotalProcessorTime; } catch { }
        }
        DateTime now = DateTime.Now;
        double ms = (now - lastCpuAt).TotalMilliseconds;
        if (ms > 0 && total >= lastCpu) cpuPct = (int)((total - lastCpu).TotalMilliseconds * 100 / ms / Environment.ProcessorCount);
        lastCpu = total;
        lastCpuAt = now;
    }


    void SetStatus(string t, bool warn, int holdSeconds)
    {
        SetStatusRaw(t, warn);
        holdStatusUntil = holdSeconds > 0 ? DateTime.Now.AddSeconds(holdSeconds) : DateTime.MinValue;
        // fumetto solo se la finestra non si vede (in area di notifica o ridotta): altrimenti basta la barra di stato
        if (tray != null && tray.Visible && warn && (!Visible || WindowState == FormWindowState.Minimized)) tray.ShowBalloonTip(3000, "Video2All", t, ToolTipIcon.Warning);
    }

    void SetStatusRaw(string t, bool warn)
    {
        if (lblStatus.Text != t) lblStatus.Text = t;
        lblStatus.ForeColor = warn ? Theme.Danger : (rec != null ? Theme.Danger : Theme.Text);
    }

    // Messaggio sempre visibile: compare sopra il Live (che resta aperto: non si interrompe chi sta giocando)
    void Msg(string text)
    {
        if (!Visible && liveForm == null) { Show(); WindowState = lastWindowState; }
        Dlg.Show(liveForm != null ? (IWin32Window)liveForm : this, text, Text);
    }

    void Invoke2(MethodInvoker m)
    {
        try { if (IsHandleCreated && !IsDisposed) BeginInvoke(m); } catch { }
    }
}
