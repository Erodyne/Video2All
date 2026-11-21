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

public static class Program
{
    static Mutex single;

    [STAThread]
    public static void Main()
    {
        Run(AppDomain.CurrentDomain.BaseDirectory);
    }

    public static void Run(string dir)
    {
        bool isNew;
        single = new Mutex(true, "Video2All_SingleInstance", out isNew);
        var pre = new Settings();
        L.En = pre.Get("lang", "it") == "en";
        if (!isNew && Array.IndexOf(Environment.GetCommandLineArgs(), "--updated") > 0)
        {
            // riavvio dopo un aggiornamento: si aspetta che la versione vecchia si chiuda
            try { isNew = single.WaitOne(20000); } catch (AbandonedMutexException) { isNew = true; }
        }
        if (!isNew)
        {
            // porta in primo piano la finestra già aperta invece di un semplice messaggio
            try
            {
                var me = Process.GetCurrentProcess();
                // solo con Video2All.exe: con l'avvio alternativo il processo è powershell e non va confuso con altre finestre
                if (string.Equals(me.ProcessName, "Video2All", StringComparison.OrdinalIgnoreCase))
                foreach (Process o in Process.GetProcessesByName(me.ProcessName))
                    if (o.Id != me.Id && Native.FocusProcessWindows(o.Id) > 0) return;
            }
            catch { }
            Dlg.Show(L.T("Video2All è già aperto.", "Video2All is already running."), "Video2All");
            return;
        }
        try { Native.SetProcessDPIAware(); } catch { }
        try { Application.EnableVisualStyles(); } catch { }
        DateTime lastErrorBox = DateTime.MinValue;
        Application.ThreadException += delegate (object s, ThreadExceptionEventArgs e)
        {
            Log.W("ECCEZIONE UI: " + e.Exception);
            // un errore che si ripete (es. in un ridisegno) non deve aprire una finestra dopo l'altra
            if ((DateTime.UtcNow - lastErrorBox).TotalSeconds < 15) return;
            lastErrorBox = DateTime.UtcNow;
            Dlg.Show(L.T("Errore imprevisto (dettagli nel log):\n", "Unexpected error (details in log):\n") + e.Exception.Message, "Video2All");
            lastErrorBox = DateTime.UtcNow;
        };
        AppDomain.CurrentDomain.UnhandledException += delegate (object s, UnhandledExceptionEventArgs e) { Log.W("ECCEZIONE: " + e.ExceptionObject); };
        string ff = Path.Combine(dir, "ffmpeg.exe");
        if (!File.Exists(ff))
        {
            Dlg.Show(L.T("ffmpeg.exe non trovato in:\n", "ffmpeg.exe not found in:\n") + dir, "Video2All");
            return;
        }
        Log.W("=== Avvio, cartella " + dir);
        MainForm main;
        try { main = new MainForm(ff); }
        catch (Exception ex)
        {
            // un errore all'avvio (es. impostazioni rovinate) non deve chiudere il programma senza spiegazioni
            Log.W("ERRORE ALL'AVVIO: " + ex);
            Dlg.Show(L.T("Il programma non è riuscito ad avviarsi:\n", "The program could not start:\n") + ex.Message + "\n\n" +
                L.T("Dettagli nel log: ", "Details in the log: ") + Log.Dir, "Video2All");
            return;
        }
        Application.Run(main);
        GC.KeepAlive(single);
    }
}

public partial class MainForm : Form
{
    readonly string ffmpeg;
    readonly Settings cfg = new Settings();
    readonly FrameStore store = new FrameStore(Cmd.PW, Cmd.PH);
    readonly ReplayRing replay = new ReplayRing();

    // ---- controlli
    Button btnRefresh, btnSaveProfile, btnLoadProfile;
    Slider tbBright, tbContrast, tbSat;
    NumericUpDown nCropL, nCropR, nCropT, nCropB;
    Button btnDetect, btnResetImg;
    ComboBox cbCam, cbCamPos, cbLogoPos, cbTextPos;
    NumericUpDown nCamSize;
    CheckBox chkCam, chkLogo, chkText;
    TextBox tbLogo, tbText;
    Button btnLogo;
    ComboBox cbOutDev;
    Slider tbVolume;
    CheckBox chkListen, chkADenoise, chkANorm, chkAudio24;
    TextBox tbFolder;
    Button btnBrowse;
    DateTimePicker dtSchedule;
    TextBox tbRtmp;
    CheckBox chkShowKey, chkReconnect;
    NumericUpDown nBitrate;
    FlatCombo cbPreviewSync; CheckBox chkGpuLive;
    CheckBox chkSharp, chkScan, chkIntScale, chkAutoLive, chkAutoPreview, chkTray, chkGlobalKeys, chkDark;
    ComboBox cbLang;
    Button btnUpdate, btnShortcut, btnLogs;
    VideoView view;
    ListView lvFiles;
    ImageList thumbs;
    ModernButton btnPreview, btnRec, btnStop, btnStream, btnLive, btnPhoto, btnReplay, btnMarker;
    Meter meter;
    Label lblStatus, lblInfo;
    NotifyIcon tray;
    ToolTip tips = new ToolTip();

    // ---- stato
    Process cap;
    Endpoint epA, epM;
    bool useTcp;
    bool capAudio, capPreviewAudio, capStopping, triedNoPreviewAudio, capAudio24;
    DateTime capStart = DateTime.MinValue;
    readonly StringBuilder capErr = new StringBuilder();
    Consumer rec, str;
    bool wantStream;
    int streamRetries;
    DateTime streamRetryAt = DateTime.MaxValue;
    string currentFile;
    DateTime recStart;
    int savingCount, jobCount;
    volatile bool replayOn, listen = true;
    volatile int volume = 100, outDevice = -1, audioLevel, audioQueueMs;
    int frames, fps, uiPending;
    long dropped;
    DateTime lastFrame = DateTime.MinValue, lastFpsTick = DateTime.Now, holdStatusUntil = DateTime.MinValue;
    bool loading = true, closing, hadSignal, scheduleArmed;
    DateTime scheduleAt;
    readonly Dictionary<string, string> altNames = new Dictionary<string, string>();
    readonly Dictionary<string, string> displayToDev = new Dictionary<string, string>();
    System.Windows.Forms.Timer clock, applyTimer, saveTimer, textTimer;
    TimeSpan lastCpu = TimeSpan.Zero;
    DateTime lastCpuAt = DateTime.Now;
    int cpuPct;
    Form liveForm;
    VideoView liveView;
    Cursor blankCursor;
    readonly Queue<string> thumbQueue = new Queue<string>();
    bool thumbWorker;
    int contentTick;
    WheelFilter wheelFilter;

    // le miniature sono rigenerabili: vanno nella cartella locale, non in quella che Windows sincronizza tra PC
    string ThumbDir { get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Video2All", "miniature"); } }
    string TextFilePath { get { return Path.Combine(Settings.AppDir, "testo_sovrimpressione.txt"); } }

    public MainForm(string ffmpegPath)
    {
        ffmpeg = ffmpegPath;
        Text = "Video2All";
        AutoScaleDimensions = new SizeF(96F, 96F);
        AutoScaleMode = AutoScaleMode.Dpi;
        ClientSize = new Size(1440, 880);
        MinimumSize = new Size(1100, 700);
        StartPosition = FormStartPosition.CenterScreen;
        KeyPreview = true;
        // icona del programma anche con l'avvio alternativo (dove l'eseguibile è powershell.exe)
        try
        {
            string exeIcon = Path.Combine(Path.GetDirectoryName(ffmpegPath), "Video2All.exe");
            Icon = Icon.ExtractAssociatedIcon(File.Exists(exeIcon) ? exeIcon : Application.ExecutablePath);
        }
        catch { }

        BuildUi();
        LoadSettings();
        RestoreWindowBounds();
        BuildTray();

        clock = new System.Windows.Forms.Timer { Interval = 250 };
        clock.Tick += delegate { OnClock(); };
        clock.Start();
        applyTimer = new System.Windows.Forms.Timer { Interval = 700 };
        applyTimer.Tick += delegate { applyTimer.Stop(); ApplyCaptureChange(); };
        textTimer = new System.Windows.Forms.Timer { Interval = 400 };
        textTimer.Tick += delegate { textTimer.Stop(); WriteOverlayTextLive(); };
        saveTimer = new System.Windows.Forms.Timer { Interval = 1500 };
        saveTimer.Tick += delegate { saveTimer.Stop(); SaveSettings(); };

        KeyDown += OnKey;
        wheelFilter = new WheelFilter();
        Application.AddMessageFilter(wheelFilter);
        Shown += delegate
        {
            LoadSchedule();
            LoadDevices();
            RefreshFiles();
            CheckEncodersAsync();
            Housekeeping();
            AutoUpdateOnStart();
            if (!cfg.GetBool("wizard_done", false)) BeginInvoke((MethodInvoker)delegate { ShowWizard(); });
        };
        FormClosing += OnClosingForm;
        Resize += delegate
        {
            if (WindowState != FormWindowState.Minimized) { lastWindowState = WindowState; LayoutPanels(); }
            else if (chkTray.Checked) { Hide(); tray.Visible = true; }
        };
        SystemEvents.DisplaySettingsChanged += OnDisplayChanged;
        ApplyTheme();
    }

    void WireAutosave(Control root)
    {
        foreach (Control c in root.Controls)
        {
            if (c is ComboBox) ((ComboBox)c).SelectedIndexChanged += delegate { QueueSave(); };
            else if (c is CheckBox) ((CheckBox)c).CheckedChanged += delegate { QueueSave(); };
            else if (c is Slider) ((Slider)c).ValueChanged += delegate { QueueSave(); };
            else if (c is NumericUpDown) ((NumericUpDown)c).ValueChanged += delegate { QueueSave(); };
            else if (c is TextBox) ((TextBox)c).TextChanged += delegate { QueueSave(); };
            else if (c is DateTimePicker) ((DateTimePicker)c).ValueChanged += delegate { QueueSave(); };
            WireAutosave(c);
        }
    }

    void QueueSave() { if (loading || saveTimer == null) return; saveTimer.Stop(); saveTimer.Start(); }

    void BuildTray()
    {
        tray = new NotifyIcon { Icon = Icon ?? SystemIcons.Application, Text = "Video2All", Visible = false };
        var m = new ContextMenuStrip();
        m.Items.Add(L.T("Mostra", "Show"), null, delegate { ShowFromTray(); });
        m.Items.Add(L.T("Registra / Stop", "Record / Stop"), null, delegate { if (rec != null) StopRecording(); else StartRecording(); });
        m.Items.Add("Live", null, delegate { ToggleLive(); });
        m.Items.Add(L.T("Esci", "Exit"), null, delegate { ShowFromTray(); Close(); });
        tray.ContextMenuStrip = m;
        tray.DoubleClick += delegate { ShowFromTray(); };
    }

    FormWindowState lastWindowState = FormWindowState.Normal;

    // torna com'era (anche ingrandita); l'icona resta se servono le notifiche
    void ShowFromTray() { Show(); WindowState = lastWindowState; Activate(); tray.Visible = chkNotify != null && chkNotify.Checked; }

    void SaveSettings() { StoreSettings(cfg, true); StoreWindowBounds(); cfg.Save(); }

    // posizione e dimensione della finestra (solo se lo schermo esiste ancora)
    void RestoreWindowBounds()
    {
        int x = cfg.GetInt("winX", int.MinValue), y = cfg.GetInt("winY", int.MinValue), w = cfg.GetInt("winW", 0), h = cfg.GetInt("winH", 0);
        if (w < MinimumSize.Width || h < MinimumSize.Height || x == int.MinValue) return;
        var r = new Rectangle(x, y, w, h);
        foreach (Screen s in Screen.AllScreens)
            if (s.WorkingArea.IntersectsWith(new Rectangle(r.X + 40, r.Y + 10, Math.Max(1, r.Width - 80), 40)))
            {
                StartPosition = FormStartPosition.Manual;
                Bounds = r;
                if (cfg.GetBool("winMax", false)) WindowState = FormWindowState.Maximized;
                return;
            }
    }

    void StoreWindowBounds()
    {
        if (WindowState == FormWindowState.Minimized || !IsHandleCreated) return;
        Rectangle r = WindowState == FormWindowState.Normal ? Bounds : RestoreBounds;
        cfg.Set("winX", r.X); cfg.Set("winY", r.Y); cfg.Set("winW", r.Width); cfg.Set("winH", r.Height);
        cfg.Set("winMax", WindowState == FormWindowState.Maximized);
    }

    static int Clamp(int v, int a, int b) { return v < a ? a : v > b ? b : v; }
    static decimal ClampD(decimal v, decimal a, decimal b) { return v < a ? a : v > b ? b : v; }


    void SaveProfile()
    {
        using (var d = new SaveFileDialog { Filter = L.T("Profilo|*.ini", "Profile|*.ini"), InitialDirectory = Path.Combine(Settings.AppDir, "profili"), FileName = "profilo.ini" })
        {
            try { Directory.CreateDirectory(d.InitialDirectory); } catch { }
            if (d.ShowDialog(this) != DialogResult.OK) return;
            var s = new Settings(d.FileName);
            s.D.Clear();
            StoreSettings(s, false);
            s.Save();
            SetStatus(L.T("Profilo salvato.", "Profile saved."), false, 4);
        }
    }

    void LoadProfile()
    {
        using (var d = new OpenFileDialog { Filter = L.T("Profilo|*.ini", "Profile|*.ini"), InitialDirectory = Path.Combine(Settings.AppDir, "profili") })
        {
            if (d.ShowDialog(this) != DialogResult.OK) return;
            var s = new Settings(d.FileName);
            foreach (var kv in s.D) if (kv.Key != "rtmp" && kv.Key != "rtmp2") cfg.D[kv.Key] = kv.Value;
            LoadSettings();
            int iv = cbVideo.Items.IndexOf(cfg.Get("video", ""));
            if (iv >= 0) cbVideo.SelectedIndex = iv;
            int ia = cbAudio.Items.IndexOf(cfg.Get("audio", ""));
            if (ia >= 0) cbAudio.SelectedIndex = ia;
            SetStatus(L.T("Profilo caricato.", "Profile loaded."), false, 4);
            ScheduleApply();
        }
    }
}
