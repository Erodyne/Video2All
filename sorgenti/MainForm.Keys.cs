using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

// Tasti personalizzabili (locali e globali)
public partial class MainForm
{
    sealed class KeyAction { public string Id, It, En; public Keys Local, Global; public Action Run; }
    List<KeyAction> keyActions;
    readonly Dictionary<string, KeyBox> keyBoxes = new Dictionary<string, KeyBox>();
    int registeredGlobal;

    const Keys CA = Keys.Control | Keys.Alt;

    void InitKeyActions()
    {
        keyActions = new List<KeyAction>
        {
            new KeyAction { Id = "rec", It = "Registra / ferma", En = "Record / stop", Local = Keys.F9, Global = CA | Keys.R, Run = delegate { if (rec != null) StopRecording(); else StartRecording(); } },
            new KeyAction { Id = "stop", It = "Stop registrazione", En = "Stop recording", Local = Keys.F10, Global = CA | Keys.S, Run = delegate { StopRecording(); } },
            new KeyAction { Id = "pause", It = "Pausa / riprendi", En = "Pause / resume", Local = Keys.F4, Global = Keys.None, Run = delegate { TogglePauseRecording(); } },
            new KeyAction { Id = "newfile", It = "Nuovo file", En = "New file", Local = Keys.F3, Global = Keys.None, Run = delegate { if (rec != null) RotateRecording(); } },
            new KeyAction { Id = "live", It = "Live schermo intero", En = "Fullscreen Live", Local = Keys.F11, Global = CA | Keys.L, Run = delegate { ToggleLive(); } },
            new KeyAction { Id = "photo", It = "Foto", En = "Photo", Local = Keys.F12, Global = CA | Keys.P, Run = delegate { TakePhoto(); } },
            new KeyAction { Id = "replay", It = "Salva replay", En = "Save replay", Local = Keys.F7, Global = CA | Keys.B, Run = delegate { SaveReplay(); } },
            new KeyAction { Id = "marker", It = "Marcatore", En = "Marker", Local = Keys.F8, Global = CA | Keys.M, Run = delegate { AddMarker(); } },
            new KeyAction { Id = "timer", It = "Timer avvia / pausa", En = "Timer start / pause", Local = Keys.F5, Global = CA | Keys.T, Run = delegate { TimerToggle(); } },
            new KeyAction { Id = "timerreset", It = "Timer azzera", En = "Timer reset", Local = Keys.F6, Global = CA | Keys.Y, Run = delegate { TimerReset(); } },
            new KeyAction { Id = "listen", It = "Ascolto sì / no", En = "Listen on / off", Local = Keys.None, Global = CA | Keys.A, Run = delegate { chkListen.Checked = !chkListen.Checked; } },
            new KeyAction { Id = "scene0", It = "Scena: gioco", En = "Scene: game", Local = Keys.None, Global = CA | Keys.D1, Run = delegate { ApplyScene(0); } },
            new KeyAction { Id = "scene1", It = "Scena: inizio a breve", En = "Scene: starting soon", Local = Keys.None, Global = CA | Keys.D2, Run = delegate { ApplyScene(1); } },
            new KeyAction { Id = "scene2", It = "Scena: pausa", En = "Scene: break", Local = Keys.None, Global = CA | Keys.D3, Run = delegate { ApplyScene(2); } },
            new KeyAction { Id = "scene3", It = "Scena: fine", En = "Scene: ending", Local = Keys.None, Global = CA | Keys.D4, Run = delegate { ApplyScene(3); } },
            new KeyAction { Id = "scene4", It = "Scena: personalizzata", En = "Scene: custom", Local = Keys.None, Global = CA | Keys.D5, Run = delegate { ApplyScene(4); } },
            new KeyAction { Id = "search", It = "Cerca impostazioni", En = "Search settings", Local = Keys.Control | Keys.K, Global = Keys.None, Run = delegate { ShowPalette(); } },
            new KeyAction { Id = "mini", It = "Mini-finestra", En = "Mini window", Local = Keys.None, Global = Keys.None, Run = delegate { ToggleMini(); } },
        };
    }

    void LoadKeys()
    {
        foreach (KeyAction a in keyActions)
        {
            int v;
            if (int.TryParse(cfg.Get("key." + a.Id, ""), out v)) a.Local = (Keys)v;
            if (int.TryParse(cfg.Get("gkey." + a.Id, ""), out v)) a.Global = (Keys)v;
            KeyBox kb;
            if (keyBoxes.TryGetValue("l." + a.Id, out kb)) kb.Value = a.Local;
            if (keyBoxes.TryGetValue("g." + a.Id, out kb)) kb.Value = a.Global;
        }
    }

    void StoreKeys(Settings s)
    {
        foreach (KeyAction a in keyActions)
        {
            s.Set("key." + a.Id, ((int)a.Local).ToString());
            s.Set("gkey." + a.Id, ((int)a.Global).ToString());
        }
    }

    // Sezione "Tasti" nella pagina Opzioni
    void BuildKeysCard(FlowLayoutPanel page)
    {
        Card c = Section(page, L.T("Tasti", "Keys"));
        var hint = new Label { AutoSize = true, MaximumSize = new Size(360, 0), Tag = "dim", Text = L.T("Clic su una casella e premi il tasto (anche con Ctrl/Alt/Shift). Canc = nessuno. Colonna destra: tasti globali, funzionano anche mentre giochi.", "Click a box and press the key (with Ctrl/Alt/Shift). Del = none. Right column: global keys, work while gaming.") };
        Row(c, null, null, hint);
        foreach (KeyAction a in keyActions)
        {
            KeyAction act = a;
            var lk = new KeyBox { Width = 120 };
            var gk = new KeyBox { Width = 140, Global = true };
            keyBoxes["l." + a.Id] = lk;
            keyBoxes["g." + a.Id] = gk;
            lk.ValueChanged += delegate { act.Local = lk.Value; CheckKeyConflicts(); QueueSave(); };
            gk.ValueChanged += delegate { act.Global = gk.Value; RegisterGlobalKeys(); QueueSave(); };
            Row(c, L.T(a.It, a.En), null, lk, gk);
        }
        var reset = Btn2(L.T("Ripristina tasti predefiniti", "Restore default keys"));
        reset.Click += delegate
        {
            foreach (KeyAction a in keyActions) { cfg.D.Remove("key." + a.Id); cfg.D.Remove("gkey." + a.Id); }
            // si copiano i valori predefiniti negli oggetti esistenti: le caselle restano collegate alle stesse azioni
            List<KeyAction> current = keyActions;
            InitKeyActions();
            List<KeyAction> defaults = keyActions;
            keyActions = current;
            for (int i = 0; i < current.Count && i < defaults.Count; i++) { current[i].Local = defaults[i].Local; current[i].Global = defaults[i].Global; }
            LoadKeys();
            RegisterGlobalKeys();
            QueueSave();
        };
        Row(c, null, null, reset);
    }

    void CheckKeyConflicts()
    {
        var seen = new Dictionary<Keys, string>();
        foreach (KeyAction a in keyActions)
        {
            if (a.Local == Keys.None) continue;
            string other;
            if (seen.TryGetValue(a.Local, out other)) { SetStatus(L.T("Tasto usato due volte: ", "Key used twice: ") + KeyBox.Format(a.Local) + " (" + other + " / " + L.T(a.It, a.En) + ")", true, 6); return; }
            seen[a.Local] = L.T(a.It, a.En);
        }
    }

    // F10 (e Alt) per Windows attivano il menu di sistema: la finestra poi "ingoia" i tasti successivi.
    // Qui si intercettano prima, se sono assegnati a un'azione.
    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (keyActions != null && !(ActiveControl is KeyBox) && ((keyData & Keys.KeyCode) == Keys.F10 || (keyData & Keys.Alt) != 0))
            foreach (KeyAction a in keyActions)
                if (a.Local != Keys.None && a.Local == keyData) { a.Run(); return true; }
        return base.ProcessCmdKey(ref msg, keyData);
    }

    internal bool HandleLiveKey(Keys keyData)
    {
        if (keyActions == null) return false;
        foreach (KeyAction a in keyActions) if (a.Local != Keys.None && a.Local == keyData) { a.Run(); return true; }
        return false;
    }

    void OnKey(object sender, KeyEventArgs e)
    {
        if (keyActions == null) return;
        if (ActiveControl is KeyBox) return;
        bool typing = ActiveControl is TextBox || ActiveControl is NumericUpDown;
        foreach (KeyAction a in keyActions)
        {
            if (a.Local == Keys.None || e.KeyData != a.Local) continue;
            // mentre si scrive valgono solo i tasti F e le combinazioni con Ctrl/Alt
            bool fkey = (a.Local & Keys.KeyCode) >= Keys.F1 && (a.Local & Keys.KeyCode) <= Keys.F24;
            if (typing && !fkey && (a.Local & (Keys.Control | Keys.Alt)) == 0) return;
            a.Run();
            e.Handled = true;
            e.SuppressKeyPress = true;
            return;
        }
    }

    void UnregisterGlobalKeys()
    {
        if (!IsHandleCreated) return;
        try { for (int i = 1; i <= Math.Max(registeredGlobal, 32); i++) Native.UnregisterHotKey(Handle, i); } catch { }
        registeredGlobal = 0;
    }

    void RegisterGlobalKeys()
    {
        if (!IsHandleCreated || keyActions == null) return;
        try { UnregisterGlobalKeys(); } catch (Exception ex) { Log.W("Tasti globali: " + ex.Message); return; }
        if (chkGlobalKeys == null || !chkGlobalKeys.Checked) return;
        var failed = new List<string>();
        var dup = new List<string>();
        var seenGlobal = new HashSet<Keys>();
        for (int i = 0; i < keyActions.Count; i++)
        {
            Keys k = keyActions[i].Global;
            if ((k & Keys.KeyCode) == Keys.None) continue;
            if (!seenGlobal.Add(k)) { dup.Add(KeyBox.Format(k)); continue; }      // stesso tasto per due azioni: vale la prima
            uint mods = Native.MOD_NOREPEAT;
            if ((k & Keys.Control) != 0) mods |= Native.MOD_CONTROL;
            if ((k & Keys.Alt) != 0) mods |= Native.MOD_ALT;
            if ((k & Keys.Shift) != 0) mods |= 0x4;
            try { if (!Native.RegisterHotKey(Handle, i + 1, mods, (uint)(k & Keys.KeyCode))) failed.Add(KeyBox.Format(k)); }
            catch { failed.Add(KeyBox.Format(k)); }
        }
        registeredGlobal = keyActions.Count;
        if (dup.Count > 0) SetStatus(L.T("Tasto globale assegnato a due azioni: ", "Global key assigned twice: ") + string.Join(", ", dup.ToArray()), true, 8);
        else if (failed.Count > 0) SetStatus(L.T("Tasti globali già usati da altri programmi: ", "Global keys already in use: ") + string.Join(", ", failed.ToArray()), true, 8);
    }

    void HotKey(int id)
    {
        if (keyActions != null && id >= 1 && id <= keyActions.Count) keyActions[id - 1].Run();
    }
}

// Casella che cattura una combinazione di tasti
public class KeyBox : TextBox
{
    Keys value;
    public bool Global;
    public event EventHandler ValueChanged;

    public KeyBox()
    {
        ReadOnly = true;
        BorderStyle = BorderStyle.FixedSingle;
        Cursor = Cursors.Hand;
        ShortcutsEnabled = false;
        TextAlign = HorizontalAlignment.Center;
    }

    public Keys Value
    {
        get { return value; }
        set { this.value = value; Text = Format(value); }
    }

    public static string Format(Keys k)
    {
        if ((k & Keys.KeyCode) == Keys.None) return "—";
        var parts = new List<string>();
        if ((k & Keys.Control) != 0) parts.Add("Ctrl");
        if ((k & Keys.Alt) != 0) parts.Add("Alt");
        if ((k & Keys.Shift) != 0) parts.Add("Shift");
        Keys code = k & Keys.KeyCode;
        string name = code.ToString();
        if (code >= Keys.D0 && code <= Keys.D9) name = ((int)(code - Keys.D0)).ToString();
        parts.Add(name);
        return string.Join("+", parts.ToArray());
    }

    protected override bool IsInputKey(Keys keyData) { return true; }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if ((keyData & Keys.KeyCode) == Keys.Tab && (keyData & (Keys.Control | Keys.Alt)) == 0) return base.ProcessCmdKey(ref msg, keyData);
        return false;
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        e.SuppressKeyPress = true;
        e.Handled = true;
        Keys code = e.KeyCode;
        if (code == Keys.ControlKey || code == Keys.Menu || code == Keys.ShiftKey || code == Keys.LWin || code == Keys.RWin) return;
        Keys v = (code == Keys.Delete || code == Keys.Back || code == Keys.Escape) ? Keys.None : e.KeyData;
        if (Global && v != Keys.None && (v & (Keys.Control | Keys.Alt)) == 0 && !((v & Keys.KeyCode) >= Keys.F13 && (v & Keys.KeyCode) <= Keys.F24))
        {
            Text = "Ctrl/Alt + ...";
            return;
        }
        Value = v;
        var h = ValueChanged;
        if (h != null) h(this, EventArgs.Empty);
    }

    protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); BackColor = Theme.Mix(Theme.Accent, Theme.Surface2, 0.6f); }
    protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); BackColor = Theme.Surface2; Text = Format(value); }
}
