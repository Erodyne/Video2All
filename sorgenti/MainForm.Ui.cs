using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

// Interfaccia grafica 2026: barra superiore, barra laterale, pannello impostazioni, libreria, barra comandi
public partial class MainForm
{
    // ---- struttura
    Panel topBar, sidebar, settingsHost, centerPanel, libraryPanel, controlBar, statusBar;
    readonly List<FlowLayoutPanel> pages = new List<FlowLayoutPanel>();
    readonly List<NavButton> navs = new List<NavButton>();
    int currentPage = -1;
    Label lblPageTitle;
    Chip chipSignal, chipRec, chipStream, chipFps, chipCpu, chipDisk;
    ModernButton btnPalette, btnMini, btnThemeToggle;
    // ---- funzioni nuove
    FlatCombo cbSceneQuick, cbTimerPos;
    TextBox tbSceneStart, tbScenePause, tbSceneEnd, tbSceneCustom;
    Toggle chkSceneMute, chkTimer, chkAutoLevels, chkSharpen, chkLut;
    TextBox tbLut;
    ModernButton btnLut, btnTimerStart, btnTimerReset, btnPause, btnNewFile;
    Toggle chkAutoRecSignal, chkEndOfTape, chkTapeSeries, chkInterval, chkSounds, chkNotify, chkLiveSecond, chkStream2;
    NumericUpDown nEndTapeSec, nTapeNumber, nIntervalSec, nSchedMin;
    ListBox lstSched;
    DateTimePicker dtSchedDate, dtSchedTime;
    FlatCombo cbSchedRepeat;
    ModernButton btnSchedAdd, btnSchedDel, btnBackup, btnRestore, btnWizard;
    TextBox tbRtmp2;
    TextBox tbFind;
    FlatCombo cbSort;
    ModernButton btnViewMode, btnTools;
    Label lblLibCount;
    ContextMenuStrip toolsMenu;

    sealed class SearchEntry { public string Text; public int Page; public Control Target; }
    readonly List<SearchEntry> searchIndex = new List<SearchEntry>();

    static class G   // glifi Segoe Fluent Icons / MDL2
    {
        public const string Video = "", Photo = "", Tv = "", Volume = "", Record = "", Clock = "",
            Folder = "", Wifi = "", Settings = "", Play = "", Pause = "", Stop = "", Add = "",
            Full = "", Camera = "", History = "", Flag = "", Search = "", Mini = "", Sun = "",
            More = "", Refresh = "", Sort = "", List = "", Grid = "", Stopwatch = "", Delete = "",
            Help = "", Save = "", Open = "";
    }

    // ================================================================= Costruzione
    void BuildUi()
    {
        encOk = new bool[Enc.Codecs.Length];
        for (int i = 0; i < encOk.Length; i++) encOk[i] = !Enc.Codecs[i].Hardware;
        Theme.Dark = cfg.GetBool("dark", true);
        BackColor = Theme.Bg;
        ForeColor = Theme.Text;
        Font = Theme.Ui(9f);
        tips.AutoPopDelay = 15000; tips.InitialDelay = 400;

        // ---------- barra superiore
        topBar = new Panel { Dock = DockStyle.Top, Height = 52, Tag = "surface", Padding = new Padding(14, 0, 10, 0) };
        var title = new BrandMark { Location = new Point(10, 6), Size = new Size(250, 40) };
        var chips = new FlowLayoutPanel { Location = new Point(272, 0), Height = 52, AutoSize = true, WrapContents = false, Tag = "surface" };
        chipSignal = new Chip(); chipRec = new Chip(); chipStream = new Chip(); chipFps = new Chip(); chipCpu = new Chip(); chipDisk = new Chip();
        chips.Controls.AddRange(new Control[] { chipSignal, chipRec, chipStream, chipFps, chipCpu, chipDisk });
        var right = new FlowLayoutPanel { Dock = DockStyle.Right, AutoSize = true, WrapContents = false, Padding = new Padding(0, 8, 0, 0), Tag = "surface" };
        btnPalette = IconBtn(G.Search, L.T("Cerca impostazioni (Ctrl+K)", "Search settings (Ctrl+K)"));
        btnPalette.Text = L.T("Cerca  Ctrl+K", "Search  Ctrl+K"); btnPalette.AutoSize = true;
        btnMini = IconBtn(G.Mini, L.T("Mini-finestra sempre in primo piano", "Always-on-top mini window"), "Mini");
        btnThemeToggle = IconBtn(G.Sun, L.T("Tema chiaro/scuro", "Light/dark theme"), L.T("Tema", "Theme"));
        btnPalette.Click += delegate { ShowPalette(); };
        btnMini.Click += delegate { ToggleMini(); };
        btnThemeToggle.Click += delegate { chkDark.Checked = !chkDark.Checked; };
        right.Controls.AddRange(new Control[] { btnPalette, btnMini, btnThemeToggle });
        topBar.Controls.Add(chips); topBar.Controls.Add(title); topBar.Controls.Add(right);

        // ---------- barra laterale
        sidebar = new Panel { Dock = DockStyle.Left, Width = 72, Tag = "surface", Padding = new Padding(4, 8, 4, 8) };
        var navFlow = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, Tag = "surface" };
        sidebar.Controls.Add(navFlow);

        // ---------- pannello impostazioni
        settingsHost = new Panel { Dock = DockStyle.Left, Width = 430, Visible = false, Tag = "bg", Padding = new Padding(10, 6, 4, 6) };
        lblPageTitle = new Label { Dock = DockStyle.Top, Height = 34, Font = Theme.UiBold(14f), Tag = "title", Padding = new Padding(4, 4, 0, 0) };
        settingsHost.Controls.Add(lblPageTitle);

        string[] pageNames = { L.T("Acquisizione", "Capture"), L.T("Immagine", "Picture"), L.T("Scene", "Scenes"), "Audio", L.T("Registra", "Record"), L.T("Automazioni", "Automation"), "File", L.T("Diretta", "Stream"), L.T("Opzioni", "Options") };
        string[] glyphs = { G.Video, G.Photo, G.Tv, G.Volume, G.Record, G.Clock, G.Folder, G.Wifi, G.Settings };
        for (int i = 0; i < pageNames.Length; i++)
        {
            var page = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true, Visible = false, Tag = "bg", Padding = new Padding(2, 4, 8, 20) };
            page.Resize += delegate (object s, EventArgs e) { FitPage((FlowLayoutPanel)s); };
            page.HorizontalScroll.Maximum = 0;
            page.AutoScroll = false;
            page.HorizontalScroll.Visible = false;
            page.HorizontalScroll.Enabled = false;
            page.AutoScroll = true;
            pages.Add(page);
            settingsHost.Controls.Add(page);
            page.BringToFront();
            var nb = new NavButton { Text = pageNames[i], Glyph = glyphs[i] };
            int idx = i;
            nb.Click += delegate { ShowPage(currentPage == idx ? -1 : idx); };
            tips.SetToolTip(nb, pageNames[i]);
            navs.Add(nb);
            navFlow.Controls.Add(nb);
        }

        BuildPageCapture(pages[0]);
        BuildPagePicture(pages[1]);
        BuildPageScenes(pages[2]);
        BuildPageAudio(pages[3]);
        BuildPageRecord(pages[4]);
        BuildPageAutomation(pages[5]);
        BuildPageFiles(pages[6]);
        BuildPageStream(pages[7]);
        BuildPageOptions(pages[8]);
        InitKeyActions();
        BuildKeysCard(pages[8]);

        // ---------- area video
        centerPanel = new Panel { Dock = DockStyle.Fill, Tag = "bg", Padding = new Padding(10, 10, 10, 6) };
        view = new VideoView { Dock = DockStyle.Fill, Store = store, Rounded = true };
        view.DoubleClick += delegate { ToggleLive(); };
        centerPanel.Controls.Add(view);

        // ---------- libreria
        BuildLibrary();

        // ---------- barra comandi
        controlBar = new Panel { Dock = DockStyle.Bottom, Height = 62, Tag = "surface", Padding = new Padding(10, 12, 10, 8) };
        var bar = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false, AutoScroll = false, Tag = "surface" };
        btnPreview = BarBtn(L.T("Anteprima", "Preview"), G.Play, BtnKind.Secondary, L.T("Avvia/ferma l'anteprima del grabber", "Start/stop the grabber preview"));
        btnRec = BarBtn(L.T("Registra", "Record"), G.Record, BtnKind.Danger, L.T("Avvia la registrazione (F9)", "Start recording (F9)"));
        btnPause = BarBtn("", G.Pause, BtnKind.Secondary, L.T("Pausa / riprendi la registrazione (F4)", "Pause / resume recording (F4)"), L.T("Pausa", "Pause"));
        btnStop = BarBtn("Stop", G.Stop, BtnKind.Secondary, L.T("Ferma la registrazione e salva (F10)", "Stop and save (F10)"));
        btnNewFile = BarBtn("", G.Add, BtnKind.Secondary, L.T("Chiude il file e ne apre uno nuovo senza interrompere (F3)", "Close the file and start a new one without stopping (F3)"), L.T("Nuovo file", "New file"));
        btnStream = BarBtn(L.T("Diretta", "Stream"), G.Wifi, BtnKind.Secondary, L.T("Avvia/ferma la diretta", "Start/stop streaming"));
        btnLive = BarBtn("Live", G.Full, BtnKind.Secondary, L.T("Schermo intero per giocare (F11). M = cambia monitor, Esc = esci", "Fullscreen for gaming (F11). M = switch monitor, Esc = exit"));
        btnPhoto = BarBtn("", G.Camera, BtnKind.Secondary, L.T("Foto (F12)", "Photo (F12)"), L.T("Foto", "Photo"));
        btnReplay = BarBtn("", G.History, BtnKind.Secondary, L.T("Salva il replay degli ultimi secondi (F7)", "Save the replay of the last seconds (F7)"), "Replay");
        btnMarker = BarBtn("", G.Flag, BtnKind.Secondary, L.T("Aggiungi un marcatore/capitolo (F8)", "Add a marker/chapter (F8)"), L.T("Segna", "Mark"));
        btnPreview.Click += delegate { TogglePreview(); };
        btnRec.Click += delegate { StartRecording(); };
        btnPause.Click += delegate { TogglePauseRecording(); };
        btnStop.Click += delegate { StopRecording(); };
        btnNewFile.Click += delegate { if (rec != null) RotateRecording(); };
        btnStream.Click += delegate { ToggleStream(); };
        btnLive.Click += delegate { ToggleLive(); };
        btnPhoto.Click += delegate { TakePhoto(); };
        btnReplay.Click += delegate { SaveReplay(); };
        btnMarker.Click += delegate { AddMarker(); };
        cbSceneQuick = new FlatCombo { Width = 150, Margin = new Padding(10, 6, 3, 0) };
        cbSceneQuick.Items.AddRange(new object[] { L.T("Scena: Gioco", "Scene: Game"), L.T("Inizio a breve", "Starting soon"), L.T("Pausa", "Break"), L.T("Fine", "Ending"), L.T("Personalizzata", "Custom") });
        cbSceneQuick.SelectedIndex = 0;
        cbSceneQuick.SelectedIndexChanged += delegate { ApplyScene(cbSceneQuick.SelectedIndex); };
        tips.SetToolTip(cbSceneQuick, L.T("Cambia scena dal vivo (anche in registrazione e diretta). Testi nella pagina Scene.", "Switch scene live. Texts in the Scenes page."));
        meter = new Meter();
        tips.SetToolTip(meter, L.T("Livello audio: rosso = saturazione", "Audio level: red = clipping"));
        lblClip = new Label { AutoSize = true, Padding = new Padding(4, 12, 0, 0), ForeColor = Theme.Danger, Font = Theme.UiBold(8.5f), Tag = "keep" };
        bar.Controls.AddRange(new Control[] { btnPreview, btnRec, btnPause, btnStop, btnNewFile, btnStream, btnLive, btnPhoto, btnReplay, btnMarker, cbSceneQuick, meter, lblClip });
        controlBar.Controls.Add(bar);

        // ---------- barra di stato
        statusBar = new Panel { Dock = DockStyle.Bottom, Height = 44, Tag = "bg", Padding = new Padding(14, 2, 14, 2) };
        lblStatus = new Label { Dock = DockStyle.Top, Height = 20, Text = L.T("Pronto", "Ready"), Font = Theme.UiBold(9.5f), AutoEllipsis = true, Tag = "keep" };
        lblInfo = new Label { Dock = DockStyle.Top, Height = 20, AutoEllipsis = true, Tag = "dim" };
        statusBar.Controls.Add(lblInfo);
        statusBar.Controls.Add(lblStatus);

        Controls.Add(centerPanel);
        Controls.Add(libraryPanel);
        Controls.Add(settingsHost);
        Controls.Add(controlBar);
        Controls.Add(statusBar);
        Controls.Add(sidebar);
        Controls.Add(topBar);

        WireEvents();
        WireAutosave(this);
    }

    // ---------- helper di costruzione
    ModernButton IconBtn(string glyph, string tip) { return IconBtn(glyph, tip, null); }

    ModernButton IconBtn(string glyph, string tip, string fallback)
    {
        var b = new ModernButton { Glyph = glyph, Kind = BtnKind.Ghost, Width = 36, Height = 36, Fallback = fallback };
        if (Theme.IconFamily.Length == 0 && fallback != null) { b.AutoSize = true; b.Size = b.GetPreferredSize(Size.Empty); }
        tips.SetToolTip(b, tip);
        return b;
    }

    ModernButton BarBtn(string text, string glyph, BtnKind kind, string tip) { return BarBtn(text, glyph, kind, tip, null); }

    ModernButton BarBtn(string text, string glyph, BtnKind kind, string tip, string fallback)
    {
        var b = new ModernButton { Text = text, Glyph = glyph, Kind = kind, Height = 38, AutoSize = true, Fallback = fallback };
        b.Size = b.GetPreferredSize(Size.Empty);
        tips.SetToolTip(b, tip);
        return b;
    }

    static ModernButton Btn2(string text) { var b = new ModernButton { Text = text, AutoSize = true, Height = 30 }; b.Size = b.GetPreferredSize(Size.Empty); return b; }
    static ModernButton SmallBtn(string text) { return Btn2(text); }
    static FlatCombo Combo() { return new FlatCombo { Width = 360, Tag = "wide" }; }
    static FlatCombo SmallCombo(object[] items, int w)
    {
        var c = new FlatCombo { Width = w };
        c.Items.AddRange(items);
        if (items.Length > 0) c.SelectedIndex = 0;
        return c;
    }
    static Slider Track(int min, int max, int val) { return new Slider { Minimum = min, Maximum = max, Value = val, DefaultValue = val, Width = 360, Tag = "wide" }; }
    static NumericUpDown Num(int min, int max, int val, int dec)
    {
        return new NumericUpDown { Minimum = min, Maximum = max, Value = val, Width = 70, DecimalPlaces = dec, Increment = dec > 0 ? 0.5m : 1, BorderStyle = BorderStyle.FixedSingle };
    }
    static Label Lbl(string t) { return new Label { Text = t, AutoSize = true, Padding = new Padding(2, 7, 2, 0), Tag = "dim" }; }
    static Toggle Chk(string t) { var c = new Toggle { Text = t }; return c; }
    static TextBox Box(int w) { return new TextBox { Width = w, BorderStyle = BorderStyle.FixedSingle }; }

    Card Section(FlowLayoutPanel page, string title)
    {
        var c = new Card(title);
        page.Controls.Add(c);
        return c;
    }

    // Riga: etichetta sopra, controlli sotto (vanno a capo da soli)
    void Row(Card card, string label, string tip, params Control[] ctrls)
    {
        var flow = new FlowLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, WrapContents = true, Margin = new Padding(0, 0, 0, 6), Padding = new Padding(0), Tag = "row" };
        if (!string.IsNullOrEmpty(label))
        {
            var l = new Label { Text = label, AutoSize = true, Margin = new Padding(0, 2, 0, 2), Tag = "dim", Font = Theme.Ui(8.5f) };
            flow.Controls.Add(l);
            flow.SetFlowBreak(l, true);
            if (tip != null) tips.SetToolTip(l, tip);
        }
        foreach (Control c in ctrls)
        {
            if (c.Margin == new Padding(3)) c.Margin = new Padding(0, 0, 8, 4);
            flow.Controls.Add(c);
            if (tip != null) tips.SetToolTip(c, tip);
        }
        card.Body.Controls.Add(flow);
        int page = pages.IndexOf(card.Parent as FlowLayoutPanel);
        Control target = ctrls.Length > 0 ? ctrls[0] : flow;
        string text = (card.Title ?? "") + " · " + (label ?? "");
        foreach (Control c in ctrls) if (c is CheckBox || c is Button) text += " · " + c.Text;
        if (tip != null) text += " · " + tip;
        searchIndex.Add(new SearchEntry { Text = text, Page = page, Target = target });
    }

    void FitPage(FlowLayoutPanel page)
    {
        int w = page.ClientSize.Width - page.Padding.Horizontal - SystemInformation.VerticalScrollBarWidth - 6;
        if (w < 200) return;
        page.SuspendLayout();
        foreach (Control c in page.Controls)
        {
            var card = c as Card;
            if (card == null) continue;
            card.MinimumSize = new Size(w, 0);
            card.MaximumSize = new Size(w, 0);
            int inner = w - card.Padding.Horizontal;
            foreach (Control row in card.Body.Controls)
            {
                row.MaximumSize = new Size(inner, 0);
                foreach (Control x in row.Controls)
                    if ((x.Tag as string) == "wide") x.Width = Math.Max(120, inner - 4 - (x is TextBox && row.Controls.Count > 2 ? 60 : 0));
            }
        }
        page.ResumeLayout();
    }

    void ShowPage(int i)
    {
        currentPage = i;
        for (int k = 0; k < pages.Count; k++) { pages[k].Visible = k == i; navs[k].Selected = k == i; navs[k].Invalidate(); }
        settingsHost.Visible = i >= 0;
        if (i >= 0) { lblPageTitle.Text = navs[i].Text; FitPage(pages[i]); }
        LayoutPanels();
    }

    // finestre strette: la libreria si restringe e, con le impostazioni aperte, lascia spazio al video
    void LayoutPanels()
    {
        if (libraryPanel == null || settingsHost == null) return;
        int w = ClientSize.Width;
        libraryPanel.Width = w < 1400 ? 260 : 350;
        bool narrow = settingsHost.Visible && w - sidebar.Width - settingsHost.Width - libraryPanel.Width < 480;
        libraryPanel.Visible = !narrow;
    }

    // ================================================================= Pagine
    void BuildPageCapture(FlowLayoutPanel p)
    {
        Card c = Section(p, L.T("Sorgente", "Source"));
        cbVideo = Combo(); cbAudio = Combo();
        btnRefresh = Btn2(L.T("Aggiorna", "Refresh"));
        btnRefresh.Click += delegate { LoadDevices(); };
        Row(c, L.T("Video", "Video"), L.T("Il grabber (es. ezcap). 'Sorgente di prova' mostra barre colore senza dispositivo.", "The grabber. 'Test source' shows color bars."), cbVideo, btnRefresh);
        Row(c, "Audio", L.T("Di solito lo stesso nome del grabber", "Usually the same name as the grabber"), cbAudio);
        cbInput = SmallCombo(new object[] { L.T("Predefinito", "Default") }, 220);
        Row(c, L.T("Ingresso", "Input"), L.T("Composito (giallo) o S-Video. Ogni ingresso ricorda le sue impostazioni.", "Composite or S-Video. Each input remembers its settings."), cbInput);

        c = Section(p, L.T("Segnale", "Signal"));
        cbStd = SmallCombo(new object[] { "PAL B/G/D/K/I (Europa)", "NTSC-M (USA/Giappone)", "PAL-M (Brasile)", "PAL-N (Argentina)", "SECAM (Francia/Est)", L.T("Automatico", "Automatic"), L.T("Personalizzato", "Custom") }, 220);
        Row(c, "Standard", L.T("In Italia: PAL. Lo standard esatto (PAL_B, PAL_I) si sceglie in 'Impostazioni driver'.", "Italy/Europe: PAL. Exact standard in 'Driver settings'."), cbStd);
        cbSize = SmallCombo(new object[] { "Auto" }, 110); cbFps = SmallCombo(new object[] { "Auto" }, 80); cbPixFmt = SmallCombo(new object[] { "Auto" }, 100);
        Row(c, L.T("Risoluzione · fps · formato colore", "Size · fps · color format"), L.T("Valori letti dal grabber. 'Auto' lascia decidere al driver.", "Values read from the grabber."), cbSize, cbFps, cbPixFmt);
        cbDeint = SmallCombo(new object[] { L.T("Nessuna", "None"), "Yadif 25/30", "Yadif 50/60", L.T("Bwdif 50/60 (consigliata)", "Bwdif 50/60 (recommended)"), "W3fdif", L.T("Retro 240p (console)", "Retro 240p (consoles)") }, 220);
        Row(c, L.T("Deinterlacciatura", "Deinterlacing"), L.T("Retro 240p: PS1/console. Bwdif: VHS e TV. Nessuna: archivio interlacciato.", "Retro 240p: consoles. Bwdif: VHS/TV."), cbDeint);
        lblDetected = new Label { AutoSize = true, ForeColor = Theme.Accent2, Tag = "keep", Padding = new Padding(0, 4, 0, 0) };
        btnAnalyze = Btn2(L.T("Analizza segnale", "Analyze signal"));
        btnAnalyze.Click += delegate { AnalyzeSignal(); };
        btnAutoProbe = Btn2(L.T("Prova automatica", "Auto test"));
        btnAutoProbe.Click += delegate { AutoProbe(); };
        Row(c, L.T("Diagnostica", "Diagnostics"), L.T("Analizza: dice se il segnale è PAL/NTSC e interlacciato. Prova automatica: cerca un formato che funziona.", "Analyze PAL/NTSC and interlacing. Auto test finds a working format."), btnAnalyze, btnAutoProbe, lblDetected);

        c = Section(p, L.T("Preimpostazioni e profili", "Presets and profiles"));
        cbPreset = SmallCombo(new object[] { L.T("— scegli —", "— choose —"), "PS1 / retro 240p", "PS1 480i / PS2", "VHS", "TV / decoder", L.T("Archivio senza perdita", "Lossless archive") }, 220);
        cbPreset.SelectedIndexChanged += delegate { ApplyPreset(cbPreset.SelectedIndex); };
        btnSaveProfile = Btn2(L.T("Salva profilo", "Save profile")); btnLoadProfile = Btn2(L.T("Carica profilo", "Load profile"));
        btnSaveProfile.Click += delegate { SaveProfile(); };
        btnLoadProfile.Click += delegate { LoadProfile(); };
        Row(c, L.T("Preimpostazione", "Preset"), L.T("Imposta tutto per un uso tipico in un clic", "One-click settings for a typical use"), cbPreset);
        Row(c, null, null, btnSaveProfile, btnLoadProfile);

        c = Section(p, "Driver");
        chkDriverMem = Chk(L.T("Ricorda impostazioni driver", "Remember driver settings"));
        chkDriver = Chk(L.T("Mostra finestra driver all'avvio", "Show driver dialog on start"));
        btnDriver = Btn2(L.T("Impostazioni driver...", "Driver settings..."));
        btnDriver.Click += delegate { OpenDriverDialogs(); };
        Row(c, null, L.T("La finestra del driver regola standard, ingresso, luminosità e colore direttamente nel grabber.", "The driver dialog sets standard, input and color inside the grabber."), btnDriver, chkDriverMem, chkDriver);
        nRtBuf = Num(32, 2048, 256, 0); nRtBuf.Increment = 64;
        Row(c, L.T("Buffer (MB)", "Buffer (MB)"), L.T("Aumentalo se compaiono fotogrammi persi", "Increase if frames are dropped"), nRtBuf);

        c = Section(p, L.T("Prestazioni", "Performance"));
        chkLowLat = Chk(L.T("Bassa latenza (giocare)", "Low latency (gaming)"));
        chkCfr = Chk(L.T("Frequenza costante (CFR)", "Constant frame rate (CFR)"));
        chkLossMez = Chk(L.T("Qualità interna senza perdita", "Lossless internal quality"));
        Row(c, null, L.T("Senza perdita: nessuna doppia compressione, più CPU e memoria per il replay. CFR: evita desincronizzazioni nei file lunghi.", "Lossless: no double compression, more CPU. CFR avoids drift."), chkLowLat, chkCfr, chkLossMez);
    }

    void BuildPagePicture(FlowLayoutPanel p)
    {
        Card c = Section(p, L.T("Colore", "Color"));
        tbBright = Track(-50, 50, 0); tbContrast = Track(50, 150, 100); tbSat = Track(0, 200, 100);
        tbContrast.Suffix = "%"; tbSat.Suffix = "%";
        Row(c, L.T("Luminosità", "Brightness"), L.T("Doppio clic: valore iniziale", "Double-click: reset"), tbBright);
        Row(c, L.T("Contrasto", "Contrast"), null, tbContrast);
        Row(c, L.T("Saturazione", "Saturation"), null, tbSat);
        cbLevels = SmallCombo(new object[] { L.T("Livelli invariati", "Levels unchanged"), L.T("Espandi TV→PC", "Expand TV→PC"), L.T("Limita PC→TV", "Limit PC→TV") }, 200);
        chkAutoLevels = Chk(L.T("Livelli automatici", "Auto levels"));
        Row(c, L.T("Livelli", "Levels"), L.T("Espandi: neri più neri e bianchi più bianchi. Automatici: corregge da solo immagini spente.", "Expand: deeper blacks. Auto: fixes dull pictures."), cbLevels, chkAutoLevels);
        chkSharpen = Chk(L.T("Nitidezza", "Sharpen"));
        chkLut = Chk("LUT"); tbLut = Box(220); btnLut = Btn2("...");
        btnLut.Click += delegate { using (var d = new OpenFileDialog { Filter = "LUT 3D (*.cube)|*.cube" }) if (d.ShowDialog(this) == DialogResult.OK) { tbLut.Text = d.FileName; chkLut.Checked = true; } };
        Row(c, L.T("Correzioni", "Corrections"), L.T("LUT: file .cube per uno stile di colore", "LUT: .cube color look"), chkSharpen, chkLut, tbLut, btnLut);

        c = Section(p, L.T("Inquadratura", "Framing"));
        nCropL = Num(0, 25, 0, 1); nCropR = Num(0, 25, 0, 1); nCropT = Num(0, 25, 0, 1); nCropB = Num(0, 25, 0, 1);
        Row(c, L.T("Ritaglio % (sx · dx · su · giù)", "Crop % (L · R · T · B)"), L.T("Toglie i bordi neri. 'Rileva' li calcola dall'immagine.", "Removes black borders."), nCropL, nCropR, nCropT, nCropB);
        btnDetect = Btn2(L.T("Rileva bordi neri", "Detect borders"));
        btnDetect.Click += delegate { DetectBorders(); };
        btnResetImg = Btn2(L.T("Azzera tutto", "Reset all"));
        btnResetImg.Click += delegate
        {
            tbBright.Value = 0; tbContrast.Value = 100; tbSat.Value = 100;
            nCropL.Value = nCropR.Value = nCropT.Value = nCropB.Value = 0;
            cbLevels.SelectedIndex = 0; nChromaShift.Value = 0; nHeadSwitch.Value = 0;
            chkDenoise.Checked = chkChromaNR.Checked = chkAutoLevels.Checked = chkSharpen.Checked = chkLut.Checked = false;
        };
        Row(c, null, null, btnDetect, btnResetImg);
        cbAspect = SmallCombo(new object[] { L.T("Nessuna", "None"), L.T("Scala a 4:3", "Scale to 4:3"), L.T("4:3 nei metadati", "4:3 in metadata") }, 200);
        Row(c, L.T("Proporzioni", "Aspect ratio"), L.T("Metadati: nessuna perdita, il player mostra 4:3", "Metadata: lossless, player shows 4:3"), cbAspect);

        c = Section(p, "VHS");
        chkDenoise = Chk(L.T("Riduci rumore", "Denoise")); chkChromaNR = Chk(L.T("Rumore colore", "Chroma noise"));
        Row(c, null, null, chkDenoise, chkChromaNR);
        nChromaShift = NumNeg(-8, 8, 0); nHeadSwitch = Num(0, 24, 0, 0);
        Row(c, L.T("Sposta colore (px) · copri disturbo in basso (righe)", "Chroma shift (px) · mask head-switch (lines)"), L.T("Il disturbo in basso è tipico dei videoregistratori", "The bottom noise is typical of VCRs"), nChromaShift, nHeadSwitch);

        c = Section(p, L.T("Anteprima", "Preview"));
        chkSharp = Chk(L.T("Pixel nitidi", "Sharp pixels")); chkScan = Chk("Scanline CRT"); chkIntScale = Chk(L.T("Scala intera", "Integer scale"));
        Row(c, null, L.T("Solo visualizzazione: non cambiano i file", "Display only"), chkSharp, chkScan, chkIntScale);
        cbScope = SmallCombo(new object[] { L.T("Nessuno strumento", "No scope"), L.T("Istogramma", "Histogram"), "Waveform", L.T("Vettorscopio", "Vectorscope") }, 170);
        chkGrid = Chk(L.T("Griglia e area sicura", "Grid and safe area"));
        Row(c, L.T("Strumenti di misura", "Scopes"), L.T("Visibili solo qui: non finiscono in foto, Live o registrazioni", "Shown only here: not in photos, Live or files"), cbScope, chkGrid);
        cbPreviewSync = SmallCombo(new object[] { L.T("Bassa latenza (giocare)", "Low latency (gaming)"), L.T("Audio e video sincronizzati", "Audio/video in sync") }, 230);
        cbPreviewSync.SelectedIndexChanged += delegate { UpdateViewOptions(); QueueSave(); };
        Row(c, L.T("Ascolto e immagine", "Listening and picture"), L.T("Bassa latenza: l'immagine non aspetta mai. Sincronizzati: l'immagine aspetta l'audio (per VHS e film)", "Low latency: picture never waits. In sync: picture waits for audio"), cbPreviewSync);
        chkGpuLive = Chk(L.T("Live disegnato dalla scheda grafica", "Live drawn by the graphics card"));
        chkGpuLive.CheckedChanged += delegate { gpuFailed = false; QueueSave(); };
        var btnLat = Btn2(L.T("Misura latenza", "Measure latency"));
        btnLat.Click += delegate { StartLatencyTest(); };
        Row(c, null, L.T("Se la scheda grafica dà problemi si torna da soli al disegno normale", "Falls back automatically if the graphics card has problems"), chkGpuLive, btnLat);
    }

    void BuildPageScenes(FlowLayoutPanel p)
    {
        Card c = Section(p, L.T("Scene dal vivo", "Live scenes"));
        tbSceneStart = Box(360); tbSceneStart.Tag = "wide"; tbSceneStart.Text = L.T("Inizio a breve", "Starting soon");
        tbScenePause = Box(360); tbScenePause.Tag = "wide"; tbScenePause.Text = L.T("Torno subito", "Be right back");
        tbSceneEnd = Box(360); tbSceneEnd.Tag = "wide"; tbSceneEnd.Text = L.T("Grazie per la visione!", "Thanks for watching!");
        tbSceneCustom = Box(360); tbSceneCustom.Tag = "wide";
        Row(c, L.T("Testo 'Inizio a breve'", "'Starting soon' text"), L.T("La scena copre il video in anteprima, registrazione e diretta", "The scene covers video everywhere"), tbSceneStart);
        Row(c, L.T("Testo 'Pausa'", "'Break' text"), null, tbScenePause);
        Row(c, L.T("Testo 'Fine'", "'Ending' text"), null, tbSceneEnd);
        Row(c, L.T("Testo personalizzato", "Custom text"), null, tbSceneCustom);
        chkSceneMute = Chk(L.T("Silenzia l'audio durante le scene", "Mute audio during scenes"));
        Row(c, null, L.T("Cambio scena: menu nella barra in basso o Ctrl+Alt+1..5", "Switch: bottom bar menu or Ctrl+Alt+1..5"), chkSceneMute);

        c = Section(p, L.T("Timer (speedrun)", "Timer (speedrun)"));
        chkTimer = Chk(L.T("Mostra timer sul video", "Show timer on video"));
        cbTimerPos = SmallCombo(new object[] { L.T("In alto a sx", "Top left"), L.T("In alto a dx", "Top right"), L.T("In basso a sx", "Bottom left"), L.T("In basso a dx", "Bottom right") }, 130);
        cbTimerPos.SelectedIndex = 1;
        Row(c, null, L.T("Avvia/pausa: F5 o Ctrl+Alt+T. Azzera: F6 o Ctrl+Alt+Y", "Start/pause: F5 or Ctrl+Alt+T. Reset: F6 or Ctrl+Alt+Y"), chkTimer, cbTimerPos);
        btnTimerStart = Btn2(L.T("Avvia / pausa", "Start / pause")); btnTimerReset = Btn2(L.T("Azzera", "Reset"));
        btnTimerStart.Click += delegate { TimerToggle(); };
        btnTimerReset.Click += delegate { TimerReset(); };
        Row(c, null, null, btnTimerStart, btnTimerReset);

        string[] corners = { L.T("In alto a sx", "Top left"), L.T("In alto a dx", "Top right"), L.T("In basso a sx", "Bottom left"), L.T("In basso a dx", "Bottom right") };
        c = Section(p, "Webcam");
        chkCam = Chk(L.T("Mostra webcam", "Show webcam")); cbCam = SmallCombo(new object[0], 360); cbCam.Tag = "wide";
        cbCamPos = SmallCombo(corners, 130); cbCamPos.SelectedIndex = 3; nCamSize = Num(10, 50, 25, 0);
        Row(c, null, null, chkCam);
        Row(c, L.T("Dispositivo", "Device"), null, cbCam);
        Row(c, L.T("Posizione · altezza %", "Position · height %"), null, cbCamPos, nCamSize);

        c = Section(p, L.T("Logo e testo", "Logo and text"));
        chkLogo = Chk("Logo"); tbLogo = Box(250); btnLogo = Btn2("..."); cbLogoPos = SmallCombo(corners, 130); cbLogoPos.SelectedIndex = 1;
        btnLogo.Click += delegate { using (var d = new OpenFileDialog { Filter = "PNG/JPG|*.png;*.jpg;*.jpeg" }) if (d.ShowDialog(this) == DialogResult.OK) { tbLogo.Text = d.FileName; if (chkLogo.Checked) ScheduleApply(); else chkLogo.Checked = true; } };
        Row(c, null, null, chkLogo, cbLogoPos);
        Row(c, L.T("File immagine", "Image file"), null, tbLogo, btnLogo);
        chkText = Chk(L.T("Testo", "Text")); tbText = Box(360); tbText.Tag = "wide"; cbTextPos = SmallCombo(corners, 130);
        Row(c, null, null, chkText, cbTextPos);
        Row(c, L.T("Testo (si aggiorna dal vivo)", "Text (updates live)"), null, tbText);
    }

    void BuildPageAudio(FlowLayoutPanel p)
    {
        Card c = Section(p, L.T("Ascolto", "Monitoring"));
        cbOutDev = Combo();
        Row(c, L.T("Uscita", "Output"), L.T("Dove senti l'audio del grabber (cuffie, casse)", "Where you hear the grabber"), cbOutDev);
        tbVolume = Track(0, 200, 100); tbVolume.Suffix = "%";
        tbVolume.ValueChanged += delegate { volume = tbVolume.Value; };
        chkListen = Chk(L.T("Ascolta (Ctrl+Alt+A)", "Listen (Ctrl+Alt+A)")); chkListen.Checked = true;
        chkListen.CheckedChanged += delegate { listen = chkListen.Checked; };
        Row(c, L.T("Volume", "Volume"), null, tbVolume);
        Row(c, null, null, chkListen);

        c = Section(p, L.T("Ingresso", "Input"));
        cbRateHz = SmallCombo(new object[] { "Auto", "48000 Hz", "44100 Hz", "32000 Hz" }, 110);
        cbChannels = SmallCombo(new object[] { "Auto", "Stereo", "Mono" }, 90);
        Row(c, L.T("Frequenza · canali", "Rate · channels"), null, cbRateHz, cbChannels);
        nGain = NumNeg(-20, 20, 0);
        Row(c, L.T("Guadagno (dB)", "Gain (dB)"), L.T("Se compare 'SATURA' abbassalo", "Lower it if 'CLIPPING' appears"), nGain);
        nAudioDelay = NumNeg(-1000, 1000, 0); nAudioDelay.Increment = 10;
        Row(c, L.T("Ritardo audio (ms, negativo = anticipa)", "Audio delay (ms, negative = earlier)"), L.T("Per correggere audio fuori sincrono", "To fix out-of-sync audio"), nAudioDelay);

        c = Section(p, L.T("Filtri", "Filters"));
        chkADenoise = Chk(L.T("Riduci rumore di fondo", "Noise reduction"));
        chkANorm = Chk(L.T("Normalizza volume", "Normalize volume"));
        Row(c, null, null, chkADenoise, chkANorm);
        chkAudio24 = Chk(L.T("Audio a 24 bit (WAV/FLAC/archivio)", "24-bit audio (WAV/FLAC/archive)"));
        Row(c, null, L.T("Utile per le cassette audio e i microfoni buoni; i formati compressi non cambiano", "Useful for audio tapes and good mics; compressed formats are unchanged"), chkAudio24);

        c = Section(p, L.T("Microfono", "Microphone"));
        chkMic = Chk(L.T("Registra il microfono su traccia separata", "Record microphone on its own track"));
        cbMic = SmallCombo(new object[0], 360); cbMic.Tag = "wide";
        Row(c, null, L.T("In diretta viene mixato con l'audio del gioco", "Mixed with game audio when streaming"), chkMic);
        Row(c, L.T("Dispositivo", "Device"), null, cbMic);
    }

    void BuildPageRecord(FlowLayoutPanel p)
    {
        Card c = Section(p, L.T("Destinazione", "Destination"));
        tbFolder = Box(300); tbFolder.Tag = "wide";
        btnBrowse = Btn2(L.T("Sfoglia", "Browse"));
        btnBrowse.Click += delegate { Browse(); };
        Row(c, L.T("Cartella", "Folder"), null, tbFolder, btnBrowse);
        lblEstimate = new Label { AutoSize = true, ForeColor = Theme.Accent2, Tag = "keep" };
        Row(c, L.T("Stima", "Estimate"), L.T("Spazio occupato al minuto e ore disponibili sul disco", "Space per minute and hours left"), lblEstimate);

        c = Section(p, L.T("Formato", "Format"));
        var fmts = new List<object>(Enc.ContainerLabels());
        foreach (string a in Enc.AudioOnly) fmts.Add(L.T("Solo audio ", "Audio only ") + a);
        cbFormat = SmallCombo(fmts.ToArray(), 360); cbFormat.Tag = "wide";
        Row(c, L.T("Contenitore", "Container"), L.T("MKV: il più sicuro. MP4: il più compatibile.", "MKV: safest. MP4: most compatible."), cbFormat);
        var codecs = new List<object>();
        foreach (VCodec v in Enc.Codecs) codecs.Add(v.LabelL);
        cbCodec = SmallCombo(codecs.ToArray(), 360); cbCodec.Tag = "wide";
        Row(c, "Codec", L.T("H.264: compatibile ovunque. HEVC/AV1: file più piccoli. Senza perdita: archivio.", "H.264: everywhere. HEVC/AV1: smaller. Lossless: archive."), cbCodec);
        cbOutSize = SmallCombo(new object[] { L.T("Originale", "Original"), L.T("960p nitido", "960p sharp"), L.T("1080p nitido", "1080p sharp") }, 160);
        Row(c, L.T("Dimensione di uscita", "Output size"), L.T("960p/1080p: ingrandisce con pixel nitidi (ideale per YouTube)", "Upscale with sharp pixels"), cbOutSize);

        c = Section(p, L.T("Qualità", "Quality"));
        cbRateCtl = SmallCombo(new object[] { L.T("Qualità costante", "Constant quality"), L.T("Bitrate variabile", "Variable bitrate"), L.T("Bitrate costante", "Constant bitrate") }, 180);
        nQuality = Num(0, 51, 20, 0); nRecBitrate = Num(500, 100000, 8000, 0); nRecBitrate.Increment = 500; nKeySec = Num(1, 10, 2, 0);
        Row(c, L.T("Controllo · qualità · kbps", "Rate control · quality · kbps"), L.T("Qualità: numeri bassi = migliore (18-23 tipico)", "Quality: lower = better (18-23 typical)"), cbRateCtl, nQuality, nRecBitrate);
        cbEncPreset = SmallCombo(new object[0], 100); cbProfile = SmallCombo(new object[0], 100); cbTune = SmallCombo(new object[0], 100);
        Row(c, L.T("Preset · profilo · tune", "Preset · profile · tune"), L.T("Preset più lento = file più piccolo a parità di qualità", "Slower preset = smaller file"), cbEncPreset, cbProfile, cbTune);
        Row(c, L.T("Keyframe (secondi)", "Keyframe (seconds)"), null, nKeySec);
        cbACodec = SmallCombo(Enc.AudioCodecLabels(), 180); nAKbps = Num(64, 512, 192, 0); nAKbps.Increment = 32;
        Row(c, L.T("Audio · kbps", "Audio · kbps"), null, cbACodec, nAKbps);

        c = Section(p, L.T("Archivio", "Archive"));
        chkInterlaced = Chk(L.T("Mantieni interlacciato", "Keep interlaced"));
        chkArchive = Chk(L.T("Copia FFV1 in parallelo", "Parallel FFV1 copy"));
        Row(c, null, L.T("Per conservare le cassette alla massima qualità", "To preserve tapes at maximum quality"), chkInterlaced, chkArchive);

        c = Section(p, L.T("Divisione e stop", "Split and stop"));
        nSplit = Num(0, 600, 0, 0); nSplitGB = Num(0, 500, 0, 1); nStopAfter = Num(0, 1440, 0, 0); nStopGB = Num(0, 2000, 0, 0); nStopDrops = Num(0, 100000, 0, 0); nPreroll = Num(0, 60, 0, 0);
        Row(c, L.T("Dividi ogni (min · GB)", "Split every (min · GB)"), L.T("0 = mai. 4 GB per chiavette FAT32", "0 = never. 4 GB for FAT32 sticks"), nSplit, nSplitGB);
        Row(c, L.T("Ferma dopo (min · GB · fotogrammi persi)", "Stop after (min · GB · dropped)"), null, nStopAfter, nStopGB, nStopDrops);
        Row(c, L.T("Pre-registrazione (secondi)", "Pre-roll (seconds)"), L.T("Il file include anche gli ultimi secondi prima di premere Registra", "Includes the seconds before pressing Record"), nPreroll);
        chkReplay = Chk(L.T("Replay buffer (F7)", "Replay buffer (F7)")); nReplaySec = Num(10, 120, 30, 0);
        chkReplay.CheckedChanged += delegate { UpdateReplayState(); };
        nReplaySec.ValueChanged += delegate { UpdateReplayState(); };
        nPreroll.ValueChanged += delegate { UpdateReplayState(); };
        Row(c, L.T("Replay (secondi)", "Replay (seconds)"), L.T("F7 salva gli ultimi secondi anche senza registrare", "F7 saves the last seconds"), chkReplay, nReplaySec);
        chkSchedule = Chk(""); dtSchedule = new DateTimePicker();     // sostituiti dalla programmazione in Automazioni
    }

    void BuildPageAutomation(FlowLayoutPanel p)
    {
        Card c = Section(p, L.T("Registrazione automatica", "Automatic recording"));
        chkAutoRecSignal = Chk(L.T("Registra quando arriva il segnale", "Record when signal appears"));
        chkEndOfTape = Chk(L.T("Ferma a fine nastro", "Stop at end of tape"));
        nEndTapeSec = Num(5, 600, 30, 0);
        Row(c, null, L.T("Ideale per le cassette: premi Play sul videoregistratore e il resto è automatico", "Ideal for tapes: press Play on the VCR"), chkAutoRecSignal);
        Row(c, L.T("Fine nastro: schermo nero o senza segnale per (secondi)", "End of tape: black/no signal for (seconds)"), null, chkEndOfTape, nEndTapeSec);
        chkTapeSeries = Chk(L.T("Nastri in serie: numera da solo", "Tape series: auto-number"));
        nTapeNumber = Num(1, 9999, 1, 0);
        Row(c, L.T("Prossimo nastro n.", "Next tape #"), L.T("A ogni registrazione il campo 'Nastro' diventa 'Nastro 1, 2, 3...'", "Each recording becomes 'Tape 1, 2, 3...'"), chkTapeSeries, nTapeNumber);

        c = Section(p, L.T("Programmazione", "Schedule"));
        lstSched = new ListBox { Height = 90, Width = 360, Tag = "wide", BorderStyle = BorderStyle.FixedSingle };
        Row(c, null, null, lstSched);
        dtSchedDate = new DateTimePicker { Format = DateTimePickerFormat.Short, Width = 110 };
        dtSchedTime = new DateTimePicker { Format = DateTimePickerFormat.Time, ShowUpDown = true, Width = 90 };
        nSchedMin = Num(1, 1440, 60, 0);
        cbSchedRepeat = SmallCombo(new object[] { L.T("Una volta", "Once"), L.T("Ogni giorno", "Daily"), L.T("Lun-Ven", "Mon-Fri"), L.T("Ogni settimana", "Weekly") }, 120);
        Row(c, L.T("Data · ora · durata (min) · ripeti", "Date · time · duration (min) · repeat"), null, dtSchedDate, dtSchedTime, nSchedMin, cbSchedRepeat);
        btnSchedAdd = Btn2(L.T("Aggiungi", "Add")); btnSchedDel = Btn2(L.T("Rimuovi", "Remove"));
        btnSchedAdd.Click += delegate { ScheduleAdd(); };
        btnSchedDel.Click += delegate { ScheduleRemove(); };
        Row(c, null, L.T("Le programmazioni restano salvate. Il PC deve essere acceso con il programma aperto.", "Schedules are saved. PC must be on with the app open."), btnSchedAdd, btnSchedDel);

        c = Section(p, L.T("Foto a intervalli", "Interval photos"));
        chkInterval = Chk(L.T("Scatta una foto ogni (secondi)", "Take a photo every (seconds)")); nIntervalSec = Num(1, 3600, 10, 0);
        Row(c, null, null, chkInterval, nIntervalSec);

        c = Section(p, L.T("Avvisi", "Alerts"));
        chkSounds = Chk(L.T("Avvisi sonori", "Sound alerts")); chkNotify = Chk(L.T("Notifiche di Windows", "Windows notifications"));
        Row(c, null, L.T("Segnale perso, errori, fine registrazione", "Signal lost, errors, recording finished"), chkSounds, chkNotify);
        chkLiveSecond = Chk(L.T("Apri il Live sul secondo monitor all'avvio dell'anteprima", "Open Live on the second monitor when preview starts"));
        chkAutoLive = Chk(L.T("Apri il Live quando arriva il segnale", "Open Live when signal appears"));
        Row(c, "Live", null, chkLiveSecond, chkAutoLive);
    }

    void BuildPageFiles(FlowLayoutPanel p)
    {
        Card c = Section(p, L.T("Nome dei file", "File names"));
        tbTemplate = Box(360); tbTemplate.Tag = "wide"; tbTemplate.Text = "{nastro}_{data}_{ora}";
        Row(c, L.T("Modello", "Template"), L.T("Segnaposto: {data} {ora} {nastro} {titolo} {profilo} {dispositivo} {n}", "Tokens: {data} {ora} {nastro} {titolo} {profilo} {dispositivo} {n}"), tbTemplate);
        chkDateFolder = Chk(L.T("Sottocartella per data", "Subfolder per date"));
        Row(c, null, null, chkDateFolder);

        c = Section(p, L.T("Metadati", "Metadata"));
        tbTitle = Box(360); tbTitle.Tag = "wide"; tbTape = Box(360); tbTape.Tag = "wide"; tbNotes = Box(360); tbNotes.Tag = "wide";
        Row(c, L.T("Titolo", "Title"), null, tbTitle);
        Row(c, L.T("Nastro", "Tape"), null, tbTape);
        Row(c, L.T("Note", "Notes"), null, tbNotes);

        c = Section(p, L.T("Dopo la registrazione", "After recording"));
        chkChapters = Chk(L.T("Capitoli dai marcatori", "Chapters from markers")); chkChapters.Checked = true;
        chkRemux = Chk(L.T("Converti MKV in MP4", "Convert MKV to MP4"));
        chkVerify = Chk(L.T("Verifica integrità", "Verify integrity"));
        chkMd5 = Chk(L.T("Crea file MD5", "Create MD5 file"));
        chkAutoCompress = Chk(L.T("Crea copia compressa", "Make compressed copy"));
        chkAutoSplit = Chk(L.T("Dividi su schermate nere", "Split on black screens"));
        chkReport = Chk(L.T("Rapporto della registrazione", "Recording report"));
        Row(c, null, L.T("Gli originali vengono sostituiti solo dopo aver verificato i nuovi file", "Originals are replaced only after verifying new files"), chkChapters, chkRemux, chkVerify, chkMd5, chkAutoCompress, chkAutoSplit, chkReport);
        chkCopyTo = Chk(L.T("Copia anche in", "Also copy to")); tbCopyTo = Box(260); btnCopyTo = Btn2("...");
        btnCopyTo.Click += delegate { using (var d = new FolderBrowserDialog()) { d.SelectedPath = tbCopyTo.Text; if (d.ShowDialog(this) == DialogResult.OK) tbCopyTo.Text = d.SelectedPath; } };
        Row(c, null, L.T("Cartella OneDrive/Drive o disco esterno", "OneDrive/Drive folder or external disk"), chkCopyTo);
        Row(c, null, null, tbCopyTo, btnCopyTo);
    }

    void BuildPageStream(FlowLayoutPanel p)
    {
        Card c = Section(p, L.T("Destinazione principale", "Main destination"));
        tbRtmp = Box(360); tbRtmp.Tag = "wide"; tbRtmp.UseSystemPasswordChar = true;
        chkShowKey = Chk(L.T("Mostra chiavi", "Show keys"));
        chkShowKey.CheckedChanged += delegate { tbRtmp.UseSystemPasswordChar = tbRtmp2.UseSystemPasswordChar = !chkShowKey.Checked; };
        Row(c, L.T("URL + chiave", "URL + key"), "Twitch: rtmp://live.twitch.tv/app/CHIAVE · YouTube: rtmp://a.rtmp.youtube.com/live2/CHIAVE", tbRtmp);
        Row(c, null, null, chkShowKey);

        c = Section(p, L.T("Seconda destinazione", "Second destination"));
        chkStream2 = Chk(L.T("Trasmetti anche qui", "Also stream here"));
        tbRtmp2 = Box(360); tbRtmp2.Tag = "wide"; tbRtmp2.UseSystemPasswordChar = true;
        Row(c, null, L.T("Diretta su due piattaforme insieme (serve più banda in upload)", "Two platforms at once (needs more upload)"), chkStream2);
        Row(c, L.T("URL + chiave", "URL + key"), null, tbRtmp2);

        c = Section(p, L.T("Qualità", "Quality"));
        nBitrate = Num(500, 12000, 3500, 0); nBitrate.Increment = 250;
        chkReconnect = Chk(L.T("Riconnetti automaticamente", "Reconnect automatically")); chkReconnect.Checked = true;
        Row(c, "Bitrate (kbps)", L.T("Twitch: fino a 6000. Codec e dimensione dalla pagina Registra.", "Twitch: up to 6000. Codec and size from Record page."), nBitrate, chkReconnect);
    }

    void BuildPageOptions(FlowLayoutPanel p)
    {
        Card c = Section(p, L.T("Avvio", "Startup"));
        chkAutoPreview = Chk(L.T("Avvia l'anteprima all'apertura", "Start preview on launch"));
        chkTray = Chk(L.T("Riduci nell'area di notifica", "Minimize to tray"));
        Row(c, null, null, chkAutoPreview, chkTray);

        c = Section(p, L.T("Aspetto e tasti", "Look and keys"));
        chkDark = Chk(L.T("Tema scuro", "Dark theme"));
        chkDark.CheckedChanged += delegate { ApplyTheme(); };
        chkGlobalKeys = Chk(L.T("Tasti globali (anche in gioco)", "Global keys (also in game)"));
        chkGlobalKeys.CheckedChanged += delegate { RegisterGlobalKeys(); };
        Row(c, null, L.T("I tasti si cambiano nella sezione Tasti qui sotto", "Keys can be changed in the Keys section below"), chkDark, chkGlobalKeys);
        cbLang = SmallCombo(new object[] { "Italiano", "English" }, 120);
        Row(c, L.T("Lingua (al riavvio)", "Language (on restart)"), null, cbLang);

        c = Section(p, L.T("Manutenzione", "Maintenance"));
        btnBackup = Btn2(L.T("Backup impostazioni", "Backup settings")); btnRestore = Btn2(L.T("Ripristina", "Restore"));
        btnBackup.Click += delegate { BackupSettings(); };
        btnRestore.Click += delegate { RestoreSettings(); };
        Row(c, null, null, btnBackup, btnRestore);
        btnWizard = Btn2(L.T("Procedura guidata", "Setup wizard"));
        btnWizard.Click += delegate { ShowWizard(); };
        BuildUpdateCard(p);
        btnUpdate = Btn2(L.T("Aggiornamenti ffmpeg", "ffmpeg updates"));
        btnUpdate.Click += delegate { CheckUpdate(); };
        btnShortcut = Btn2(L.T("Collegamento desktop", "Desktop shortcut"));
        btnShortcut.Click += delegate { CreateShortcut(); };
        btnLogs = Btn2(L.T("Apri log", "Open logs"));
        btnLogs.Click += delegate { try { Directory.CreateDirectory(Log.Dir); Process.Start("explorer.exe", "\"" + Log.Dir + "\""); } catch { } };
        Row(c, null, null, btnWizard, btnUpdate, btnShortcut, btnLogs);
        var lic = new Label { AutoSize = true, MaximumSize = new Size(360, 0), Tag = "dim", Text = L.T("Include FFmpeg (licenza GPL, ffmpeg.org) con x264 e x265. Vedi LICENZE.txt.", "Includes FFmpeg (GPL, ffmpeg.org) with x264 and x265. See LICENZE.txt.") };
        Row(c, null, null, lic);
    }

    // ================================================================= Libreria (destra)
    void BuildLibrary()
    {
        libraryPanel = new Panel { Dock = DockStyle.Right, Width = 350, Tag = "bg", Padding = new Padding(4, 10, 12, 6) };
        var card = new Panel { Dock = DockStyle.Fill, Tag = "surface", Padding = new Padding(10) };
        var head = new Panel { Dock = DockStyle.Top, Height = 30, Tag = "surface" };
        var t = new Label { Text = L.T("Libreria", "Library"), Font = Theme.UiBold(11.5f), AutoSize = true, Location = new Point(2, 4), Tag = "title" };
        lblLibCount = new Label { AutoSize = true, Location = new Point(90, 8), Tag = "dim" };
        head.Controls.Add(t); head.Controls.Add(lblLibCount);
        var headRight = new FlowLayoutPanel { Dock = DockStyle.Right, AutoSize = true, WrapContents = false, Tag = "surface", Margin = new Padding(0) };
        head.Controls.Add(headRight);
        head.Height = 38;
        var tools = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 40, WrapContents = false, Tag = "surface" };
        tbFind = Box(140);
        tbFind.Margin = new Padding(0, 6, 4, 0);
        tips.SetToolTip(tbFind, L.T("Cerca per nome", "Search by name"));
        cbSort = SmallCombo(new object[] { L.T("Più recenti", "Newest"), L.T("Meno recenti", "Oldest"), L.T("Nome", "Name"), L.T("Dimensione", "Size") }, 92);
        cbSort.Margin = new Padding(0, 6, 4, 0);
        btnViewMode = IconBtn(G.List, L.T("Icone / dettagli", "Icons / details"), L.T("Vista", "View"));
        btnTools = IconBtn(G.More, L.T("Strumenti per i file selezionati", "Tools for selected files"), L.T("Strumenti", "Tools"));
        tools.Controls.AddRange(new Control[] { tbFind, cbSort });
        headRight.Controls.AddRange(new Control[] { btnViewMode, btnTools });
        tbFind.Width = 170; cbSort.Width = 120;
        thumbs = new ImageList { ImageSize = new Size(120, 90), ColorDepth = ColorDepth.Depth24Bit };
        lvFiles = new ListView { Dock = DockStyle.Fill, View = System.Windows.Forms.View.LargeIcon, LargeImageList = thumbs, MultiSelect = true, HideSelection = false, BorderStyle = BorderStyle.None, ShowItemToolTips = true };
        lvFiles.Columns.Add(L.T("Nome", "Name"), 170); lvFiles.Columns.Add(L.T("Dim.", "Size"), 60, HorizontalAlignment.Right); lvFiles.Columns.Add(L.T("Data", "Date"), 90);
        lvFiles.DoubleClick += delegate { OpenSelected(); };
        lvFiles.KeyDown += delegate (object s, KeyEventArgs e) { if (e.KeyCode == Keys.Delete) DeleteSelected(); else if (e.KeyCode == Keys.Enter) OpenSelected(); else if (e.KeyCode == Keys.F2) EditMetadataSelected(); };
        tbFind.TextChanged += delegate { FilterFiles(); };
        cbSort.SelectedIndexChanged += delegate { FilterFiles(); };
        btnViewMode.Click += delegate { lvFiles.View = lvFiles.View == System.Windows.Forms.View.LargeIcon ? System.Windows.Forms.View.Details : System.Windows.Forms.View.LargeIcon; };
        toolsMenu = BuildToolsMenu();
        lvFiles.ContextMenuStrip = toolsMenu;
        btnTools.Click += delegate { toolsMenu.Show(btnTools, new Point(0, btnTools.Height)); };
        card.Controls.Add(lvFiles);
        card.Controls.Add(tools);
        card.Controls.Add(head);
        libraryPanel.Controls.Add(card);
    }

    ContextMenuStrip BuildToolsMenu()
    {
        var m = new ContextMenuStrip { Renderer = new DarkRenderer(), ShowImageMargin = false };
        Action<string, Action> add = delegate (string text, Action a) { m.Items.Add(text, null, delegate { a(); }); };
        add(L.T("Apri", "Open"), OpenSelected);
        add(L.T("Apri cartella", "Open folder"), OpenFolder);
        add(L.T("Aggiorna elenco", "Refresh list"), RefreshFiles);
        m.Items.Add(new ToolStripSeparator());
        add(L.T("Rinomina e metadati...  (F2)", "Rename and metadata...  (F2)"), EditMetadataSelected);
        add(L.T("Taglia...", "Trim..."), TrimSelected);
        add(L.T("Clip o GIF...", "Clip or GIF..."), ClipSelected);
        add(L.T("Clip dai marcatori", "Clips from markers"), MarkerClipsSelected);
        add(L.T("Unisci i file selezionati", "Join selected files"), JoinSelected);
        add(L.T("Dividi su schermate nere", "Split on black screens"), SplitBlackSelected);
        m.Items.Add(new ToolStripSeparator());
        add(L.T("Esporta per...", "Export for..."), ExportSelected);
        add(L.T("Comprimi", "Compress"), CompressSelected);
        add(L.T("Normalizza audio (EBU R128)", "Normalize audio (EBU R128)"), NormalizeSelected);
        add(L.T("Stabilizza video", "Stabilize video"), StabilizeSelected);
        add(L.T("Restauro VHS", "VHS restoration"), RestoreVhsSelected);
        add(L.T("Foglio provini", "Contact sheet"), SheetSelected);
        m.Items.Add(new ToolStripSeparator());
        add(L.T("Sposta nel Cestino  (Canc)", "Move to Recycle Bin  (Del)"), DeleteSelected);
        return m;
    }

    // ================================================================= Tema
    void ApplyTheme()
    {
        Theme.Dark = chkDark == null || chkDark.Checked;
        BackColor = Theme.Bg;
        ForeColor = Theme.Text;
        Theme.Apply(this);
        if (lblDetected != null) lblDetected.ForeColor = Theme.Accent2;
        if (lblEstimate != null) lblEstimate.ForeColor = Theme.Accent2;
        if (lblClip != null) lblClip.ForeColor = Theme.Danger;
        if (lblStatus != null) lblStatus.ForeColor = Theme.Text;
        if (IsHandleCreated) Theme.ApplyWindow(this);
        if (toolsMenu != null) toolsMenu.Renderer = new DarkRenderer();
        if (tray != null && tray.ContextMenuStrip != null) tray.ContextMenuStrip.Renderer = new DarkRenderer();
        Invalidate(true);
    }

    // ================================================================= Ricerca impostazioni (Ctrl+K)
    void ShowPalette()
    {
        using (var f = new Form { FormBorderStyle = FormBorderStyle.None, StartPosition = FormStartPosition.Manual, ShowInTaskbar = false, BackColor = Theme.Surface2, Size = new Size(520, 360), KeyPreview = true })
        {
            f.Location = new Point(Left + (Width - f.Width) / 2, Top + 80);
            f.TopMost = liveForm != null;      // con il Live aperto non deve finire dietro (sembrerebbe tutto bloccato)
            var box = new TextBox { Dock = DockStyle.Top, Font = Theme.Ui(12f), BorderStyle = BorderStyle.FixedSingle, BackColor = Theme.Surface, ForeColor = Theme.Text };
            var list = new ListBox { Dock = DockStyle.Fill, BorderStyle = BorderStyle.None, BackColor = Theme.Surface2, ForeColor = Theme.Text, Font = Theme.Ui(9.5f), IntegralHeight = false };
            var hint = new Label { Dock = DockStyle.Bottom, Height = 22, ForeColor = Theme.TextDim, Text = L.T("  Scrivi per cercare · Invio per aprire · Esc per chiudere", "  Type to search · Enter to open · Esc to close") };
            f.Controls.Add(list); f.Controls.Add(hint); f.Controls.Add(box);
            var shown = new List<SearchEntry>();
            Action fill = delegate
            {
                list.BeginUpdate(); list.Items.Clear(); shown.Clear();
                string[] words = box.Text.ToLowerInvariant().Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                foreach (SearchEntry e in searchIndex)
                {
                    string t = e.Text.ToLowerInvariant();
                    bool ok = true;
                    foreach (string w in words) if (!t.Contains(w)) { ok = false; break; }
                    if (!ok || e.Page < 0) continue;
                    shown.Add(e);
                    list.Items.Add(navs[e.Page].Text + "  ›  " + e.Text.Trim(' ', '·'));
                    if (shown.Count >= 60) break;
                }
                if (list.Items.Count > 0) list.SelectedIndex = 0;
                list.EndUpdate();
            };
            Action go = delegate
            {
                if (list.SelectedIndex < 0) return;
                SearchEntry e = shown[list.SelectedIndex];
                f.Close();
                ShowPage(e.Page);
                pages[e.Page].ScrollControlIntoView(e.Target);
                if (e.Target.CanFocus) e.Target.Focus();
                FlashControl(e.Target);
            };
            box.TextChanged += delegate { fill(); };
            f.KeyDown += delegate (object s, KeyEventArgs e)
            {
                if (e.KeyCode == Keys.Escape) f.Close();
                else if (e.KeyCode == Keys.Enter) { go(); e.SuppressKeyPress = true; }
                else if (e.KeyCode == Keys.Down && list.SelectedIndex < list.Items.Count - 1) { list.SelectedIndex++; e.Handled = true; }
                else if (e.KeyCode == Keys.Up && list.SelectedIndex > 0) { list.SelectedIndex--; e.Handled = true; }
            };
            list.DoubleClick += delegate { go(); };
            f.Deactivate += delegate { f.Close(); };
            fill();
            f.Shown += delegate { box.Focus(); };
            f.ShowDialog(this);
        }
    }

    void FlashControl(Control c)
    {
        Control row = c.Parent ?? c;
        Color old = row.BackColor;
        int n = 0;
        var t = new Timer { Interval = 180 };
        t.Tick += delegate
        {
            n++;
            row.BackColor = n % 2 == 1 ? Theme.Mix(Theme.Accent, Theme.Surface, 0.6f) : old;
            if (n >= 6) { t.Stop(); t.Dispose(); row.BackColor = old; }
        };
        t.Start();
    }

    // ================================================================= Mini-finestra
    Form miniForm;
    VideoView miniView;

    void ToggleMini()
    {
        if (miniForm != null) { miniForm.Close(); return; }
        if (liveForm != null && Screen.FromControl(liveForm).Equals(Screen.FromControl(this)))
        { SetStatus(L.T("La mini-finestra coprirebbe il Live: chiudi prima il Live o spostalo sull'altro schermo (tasto M).", "The mini window would cover Live: close Live or move it to the other screen (M)."), true, 6); return; }
        miniForm = new Form
        {
            FormBorderStyle = FormBorderStyle.SizableToolWindow, TopMost = true, ShowInTaskbar = false, Text = "Video2All · mini",
            Size = new Size(380, 300), StartPosition = FormStartPosition.Manual, BackColor = Color.Black
        };
        Rectangle wa = Screen.FromControl(this).WorkingArea;
        miniForm.Location = new Point(wa.Right - miniForm.Width - 20, wa.Bottom - miniForm.Height - 20);
        miniView = new VideoView { Dock = DockStyle.Fill, Store = store };
        var bar = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 42, BackColor = Theme.Surface, Padding = new Padding(4) };
        var rb = new ModernButton { Glyph = G.Record, Kind = BtnKind.Danger, Width = 36, Height = 32 };
        var sb = new ModernButton { Glyph = G.Stop, Width = 36, Height = 32 };
        var lb = new ModernButton { Glyph = G.Full, Width = 36, Height = 32 };
        var miniLbl = new Label { AutoSize = true, ForeColor = Theme.Text, Padding = new Padding(4, 9, 0, 0) };
        rb.Click += delegate { StartRecording(); };
        sb.Click += delegate { StopRecording(); };
        lb.Click += delegate { ToggleLive(); };
        bar.Controls.AddRange(new Control[] { rb, sb, lb, miniLbl });
        miniForm.Controls.Add(miniView);
        miniForm.Controls.Add(bar);
        var t = new Timer { Interval = 500 };
        t.Tick += delegate { miniLbl.Text = rec != null ? "REC " + Hms(RecElapsed()) : (cap != null ? "Live" : "—"); };
        t.Start();
        miniForm.FormClosed += delegate { t.Stop(); t.Dispose(); miniForm = null; miniView = null; };
        UpdateViewOptions();
        miniForm.Show();          // senza proprietario: resta visibile anche con la finestra principale ridotta
        Theme.ApplyWindow(miniForm);
    }

    // ================================================================= Procedura guidata
    void ShowWizard()
    {
        using (var f = new Form { Text = L.T("Benvenuto", "Welcome"), FormBorderStyle = FormBorderStyle.FixedDialog, StartPosition = FormStartPosition.CenterParent, MaximizeBox = false, MinimizeBox = false, ClientSize = new Size(560, 430), BackColor = Theme.Bg, ForeColor = Theme.Text })
        {
            var title = new Label { Text = L.T("Cosa vuoi fare con il grabber?", "What do you want to do with the grabber?"), Font = Theme.UiBold(15f), AutoSize = true, Location = new Point(24, 20) };
            var sub = new Label { Text = L.T("Scegli un uso: imposto tutto io. Potrai cambiare ogni cosa dopo.", "Pick a use: I set everything. You can change anything later."), AutoSize = true, ForeColor = Theme.TextDim, Location = new Point(26, 56) };
            f.Controls.Add(title); f.Controls.Add(sub);
            string[][] opts =
            {
                new[] { L.T("Giocare e registrare una console", "Play and record a console"), L.T("PS1, PS2, retro: bassa latenza, pixel nitidi, Live a schermo intero", "PS1/PS2/retro: low latency, sharp pixels, fullscreen Live") },
                new[] { L.T("Fare dirette su Twitch/YouTube", "Stream to Twitch/YouTube"), L.T("Bassa latenza, diretta con riconnessione, scene e timer", "Low latency, auto-reconnect, scenes and timer") },
                new[] { L.T("Digitalizzare cassette VHS", "Digitize VHS tapes"), L.T("Registrazione automatica a inizio/fine nastro, filtri VHS, MP4", "Auto start/stop per tape, VHS filters, MP4") },
                new[] { L.T("Archivio di qualità massima", "Maximum-quality archive"), L.T("Senza perdita (FFV1), interlacciato, MD5 e verifica", "Lossless FFV1, interlaced, MD5 and verification") },
            };
            int choice = -1;
            for (int i = 0; i < opts.Length; i++)
            {
                var b = new ModernButton { Text = opts[i][0], Kind = i == 0 ? BtnKind.Primary : BtnKind.Secondary, Location = new Point(24, 92 + i * 72), Size = new Size(512, 40) };
                var d = new Label { Text = opts[i][1], AutoSize = true, ForeColor = Theme.TextDim, Location = new Point(28, 92 + i * 72 + 44) };
                int idx = i;
                b.Click += delegate { choice = idx; f.DialogResult = DialogResult.OK; };
                f.Controls.Add(b); f.Controls.Add(d);
            }
            var skip = new ModernButton { Text = L.T("Lo configuro io", "I'll set it up myself"), Kind = BtnKind.Ghost, Location = new Point(380, 384), Size = new Size(156, 34) };
            skip.Click += delegate { f.DialogResult = DialogResult.Cancel; };
            f.Controls.Add(skip);
            f.Load += delegate { Theme.ApplyWindow(f); };
            f.ShowDialog(this);
            cfg.Set("wizard_done", true);
            cfg.Save();                       // salvato subito: non riappare se il programma si chiude male
            if (choice < 0) return;
            switch (choice)
            {
                case 0: ApplyPreset(1); cbCodec.SelectedIndex = 0; cbFormat.SelectedIndex = 0; chkRemux.Checked = true; cbOutSize.SelectedIndex = 0; break;
                case 1: ApplyPreset(1); chkReconnect.Checked = true; cbCodec.SelectedIndex = encOk[1] ? 1 : 0; cbOutSize.SelectedIndex = 1; ShowPage(7); break;
                case 2: ApplyPreset(3); cbCodec.SelectedIndex = 0; cbFormat.SelectedIndex = 0; chkRemux.Checked = true; chkAutoRecSignal.Checked = true; chkEndOfTape.Checked = true; chkTapeSeries.Checked = true; chkChapters.Checked = true; break;
                case 3: ApplyPreset(5); chkMd5.Checked = true; chkVerify.Checked = true; chkReport.Checked = true; break;
            }
            SaveSettings();
            SetStatus(L.T("Impostazioni pronte. Collega il grabber e premi Anteprima.", "Settings ready. Connect the grabber and press Preview."), false, 8);
        }
    }
}
