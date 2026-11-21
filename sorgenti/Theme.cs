using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Runtime.InteropServices;
using System.Windows.Forms;

// ---------------------------------------------------------------- Tema grafico
public static class Theme
{
    public static bool Dark = true;

    // Palette comune della suite (uguale a MultiTool)
    public static Color Bg { get { return Dark ? C(0x0B, 0x0D, 0x12) : C(0xF4, 0xF5, 0xF9); } }
    public static Color Surface { get { return Dark ? C(0x12, 0x15, 0x1D) : C(0xFF, 0xFF, 0xFF); } }
    public static Color Surface2 { get { return Dark ? C(0x16, 0x1A, 0x25) : C(0xF3, 0xF4, 0xF8); } }
    public static Color Surface3 { get { return Dark ? C(0x19, 0x1D, 0x28) : C(0xE9, 0xEB, 0xF2); } }
    public static Color Border { get { return Dark ? C(0x1F, 0x24, 0x34) : C(0xE2, 0xE5, 0xEC); } }
    public static Color Border2 { get { return Dark ? C(0x2A, 0x30, 0x42) : C(0xD3, 0xD7, 0xE0); } }
    public static Color Text { get { return Dark ? C(0xE8, 0xEA, 0xF2) : C(0x1B, 0x1E, 0x27); } }
    public static Color TextDim { get { return Dark ? C(0x9A, 0xA0, 0xB8) : C(0x5B, 0x61, 0x75); } }
    public static Color TextFaint { get { return Dark ? C(0x6B, 0x72, 0x90) : C(0x7A, 0x80, 0x96); } }
    public static Color Accent { get { return C(0x7C, 0x5C, 0xFF); } }
    public static Color Accent2 { get { return Dark ? C(0x22, 0xD3, 0xEE) : C(0x08, 0x91, 0xB2); } }
    public static Color AccentSoft { get { return Dark ? C(0xA7, 0x8B, 0xFA) : C(0x6D, 0x4C, 0xF0); } }
    public static Color Danger { get { return C(0xF8, 0x71, 0x71); } }
    public static Color Success { get { return C(0x34, 0xD3, 0x99); } }
    public static Color Warning { get { return C(0xFB, 0xBF, 0x24); } }

    // sfumatura viola -> azzurro della suite
    public static Brush AccentBrush(RectangleF r)
    {
        if (r.Width < 1) r.Width = 1;
        if (r.Height < 1) r.Height = 1;
        return new LinearGradientBrush(new RectangleF(r.X - 1, r.Y - 1, r.Width + 2, r.Height + 2), C(0x7C, 0x5C, 0xFF), C(0x22, 0xD3, 0xEE), 35f);
    }

    // alone sfumato (come lo sfondo di MultiTool)
    public static void Glow(Graphics g, RectangleF r, Color c, int alpha)
    {
        using (var path = new GraphicsPath())
        {
            path.AddEllipse(r);
            using (var b = new PathGradientBrush(path) { CenterColor = Color.FromArgb(alpha, c), SurroundColors = new[] { Color.FromArgb(0, c) } })
                g.FillPath(b, path);
        }
    }

    static Color C(int r, int g, int b) { return Color.FromArgb(r, g, b); }

    public static Color Mix(Color a, Color b, float t)
    {
        return Color.FromArgb((int)(a.R + (b.R - a.R) * t), (int)(a.G + (b.G - a.G) * t), (int)(a.B + (b.B - a.B) * t));
    }

    static string uiFamily, iconFamily;

    static bool HasFont(string name)
    {
        using (var fc = new InstalledFontCollection())
            foreach (FontFamily f in fc.Families) if (string.Equals(f.Name, name, StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }

    public static string UiFamily
    {
        get
        {
            if (uiFamily == null)
            {
                try { uiFamily = HasFont("Segoe UI Variable Display") ? "Segoe UI Variable Display" : HasFont("Segoe UI Variable Text") ? "Segoe UI Variable Text" : HasFont("Segoe UI") ? "Segoe UI" : SystemFonts.MessageBoxFont.FontFamily.Name; }
                catch { uiFamily = "Segoe UI"; }
            }
            return uiFamily;
        }
    }

    public static string IconFamily
    {
        get
        {
            if (iconFamily == null)
            {
                try { iconFamily = HasFont("Segoe Fluent Icons") ? "Segoe Fluent Icons" : HasFont("Segoe MDL2 Assets") ? "Segoe MDL2 Assets" : ""; }
                catch { iconFamily = ""; }
            }
            return iconFamily;
        }
    }

    public static Font Ui(float size) { return new Font(UiFamily, size); }
    public static Font UiBold(float size) { return new Font(UiFamily, size, FontStyle.Bold); }
    public static Font Icon(float size) { return IconFamily.Length > 0 ? new Font(IconFamily, size) : null; }

    public static GraphicsPath Round(RectangleF r, float radius)
    {
        var p = new GraphicsPath();
        float d = Math.Min(radius * 2, Math.Min(r.Width, r.Height));
        if (d <= 1) { p.AddRectangle(r); return p; }
        p.AddArc(r.X, r.Y, d, d, 180, 90);
        p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        p.CloseFigure();
        return p;
    }

    // ---- barra del titolo scura e angoli arrotondati (Windows 10/11)
    [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

    public static void ApplyWindow(Form f)
    {
        try
        {
            int on = Dark ? 1 : 0;
            if (DwmSetWindowAttribute(f.Handle, 20, ref on, 4) != 0) DwmSetWindowAttribute(f.Handle, 19, ref on, 4);
            int round = 2;
            DwmSetWindowAttribute(f.Handle, 33, ref round, 4);
        }
        catch { }
    }

    // Applica i colori a un albero di controlli standard
    public static void Apply(Control root)
    {
        foreach (Control c in root.Controls) ApplyOne(c);
    }

    [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)] static extern int SetWindowTheme(IntPtr h, string app, string idList);

    // barre di scorrimento scure (Windows 10 1809 e successivi); in tema chiaro quelle normali
    static void DarkScroll(Control c)
    {
        if (!(c is ScrollableControl || c is ListView || c is ListBox || (c is TextBox && ((TextBox)c).Multiline))) return;
        MethodInvoker set = delegate { try { SetWindowTheme(c.Handle, Dark ? "DarkMode_Explorer" : "Explorer", null); } catch { } };
        if (c.IsHandleCreated) set();
        else c.HandleCreated += delegate { set(); };
    }

    static void ApplyOne(Control c)
    {
        DarkScroll(c);
        if (c is VideoView || c is BrandMark || c is ModernButton || c is Toggle || c is Slider || c is Chip || c is Meter || c is NavButton) { c.Invalidate(); }
        else if (c is Card) { c.BackColor = Surface; c.ForeColor = Text; c.Invalidate(); }
        else if (c is TextBox) { var t = (TextBox)c; t.BackColor = Surface2; t.ForeColor = Text; t.BorderStyle = BorderStyle.FixedSingle; }
        else if (c is NumericUpDown) { c.BackColor = Surface2; c.ForeColor = Text; ((NumericUpDown)c).BorderStyle = BorderStyle.FixedSingle; }
        else if (c is FlatCombo) { c.BackColor = Surface2; c.ForeColor = Text; c.Invalidate(); }
        else if (c is ComboBox) { c.BackColor = Surface2; c.ForeColor = Text; ((ComboBox)c).FlatStyle = FlatStyle.Flat; }
        else if (c is ListView) { c.BackColor = Surface; c.ForeColor = Text; }
        else if (c is ListBox) { c.BackColor = Surface2; c.ForeColor = Text; }
        else if (c is DateTimePicker) { }
        else if (c is Button) { var b = (Button)c; b.FlatStyle = FlatStyle.Flat; b.BackColor = Surface3; b.ForeColor = Text; b.FlatAppearance.BorderColor = Border2; b.Padding = new Padding(10, 3, 10, 3); }
        else if (c is CheckBox) { c.ForeColor = Text; }
        else if (c is Label && (c.Tag as string) == "dim") { c.ForeColor = TextDim; c.BackColor = Color.Transparent; }
        else if (c is Label && (c.Tag as string) == "title") { c.ForeColor = Text; c.BackColor = Color.Transparent; }
        else if (c is Label) { if (c.Tag == null) c.ForeColor = Text; c.BackColor = Color.Transparent; }
        else if (c.Tag as string == "surface") { c.BackColor = Surface; c.ForeColor = Text; }
        else if (c.Tag as string == "surface2") { c.BackColor = Surface2; c.ForeColor = Text; }
        else if (c is Panel || c is FlowLayoutPanel || c is TableLayoutPanel) { c.BackColor = c.Parent is Card ? Surface : (c.Tag as string == "bg" ? Bg : c.Parent != null ? c.Parent.BackColor : Bg); c.ForeColor = Text; }
        foreach (Control ch in c.Controls) ApplyOne(ch);
    }
}

// ---------------------------------------------------------------- Menu contestuali scuri
public sealed class DarkRenderer : ToolStripProfessionalRenderer
{
    public DarkRenderer() : base(new DarkColors()) { RoundedEdges = true; }
    protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e) { e.TextColor = e.Item.Enabled ? Theme.Text : Theme.TextDim; base.OnRenderItemText(e); }
    protected override void OnRenderArrow(ToolStripArrowRenderEventArgs e) { e.ArrowColor = Theme.Text; base.OnRenderArrow(e); }

    sealed class DarkColors : ProfessionalColorTable
    {
        public override Color ToolStripDropDownBackground { get { return Theme.Surface2; } }
        public override Color ImageMarginGradientBegin { get { return Theme.Surface2; } }
        public override Color ImageMarginGradientMiddle { get { return Theme.Surface2; } }
        public override Color ImageMarginGradientEnd { get { return Theme.Surface2; } }
        public override Color MenuBorder { get { return Theme.Border; } }
        public override Color MenuItemBorder { get { return Theme.Accent; } }
        public override Color MenuItemSelected { get { return Theme.Surface3; } }
        public override Color MenuItemSelectedGradientBegin { get { return Theme.Surface3; } }
        public override Color MenuItemSelectedGradientEnd { get { return Theme.Surface3; } }
        public override Color SeparatorDark { get { return Theme.Border; } }
        public override Color SeparatorLight { get { return Theme.Border; } }
    }
}

// ---------------------------------------------------------------- Pulsante moderno
public enum BtnKind { Secondary, Primary, Danger, Ghost }

public class ModernButton : Button
{
    public BtnKind Kind = BtnKind.Secondary;
    public string Glyph;
    public string Fallback;           // testo se manca il font delle icone
    public bool Active;               // stato "acceso" (es. registrazione in corso)
    string ShownText { get { return string.IsNullOrEmpty(Text) && Theme.IconFamily.Length == 0 && Fallback != null ? Fallback : Text; } }
    bool hover, down;

    public ModernButton()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor, true);
        FlatStyle = FlatStyle.Flat;
        FlatAppearance.BorderSize = 0;
        Cursor = Cursors.Hand;
        Height = 36;
        Margin = new Padding(3);
        BackColor = Color.Transparent;
    }

    public override Size GetPreferredSize(Size proposed)
    {
        using (var g = CreateGraphics())
        using (var f = Theme.Ui(9.5f))
        {
            int w = 20;
            if (!string.IsNullOrEmpty(ShownText)) w += (int)Math.Ceiling(g.MeasureString(ShownText, f).Width);
            if (!string.IsNullOrEmpty(Glyph) && Theme.IconFamily.Length > 0) w += string.IsNullOrEmpty(Text) ? 18 : 24;
            return new Size(Math.Max(w, Height), Height);
        }
    }

    protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { hover = down = false; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnMouseDown(MouseEventArgs e) { down = true; Invalidate(); base.OnMouseDown(e); }
    protected override void OnMouseUp(MouseEventArgs e) { down = false; Invalidate(); base.OnMouseUp(e); }
    protected override void OnEnabledChanged(EventArgs e) { Invalidate(); base.OnEnabledChanged(e); }
    protected override void OnTextChanged(EventArgs e) { if (AutoSize) Size = GetPreferredSize(Size.Empty); Invalidate(); base.OnTextChanged(e); }

    protected override void OnPaint(PaintEventArgs e)
    {
        Graphics g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
        Color parentBg = Parent != null ? Parent.BackColor : Theme.Bg;
        if (parentBg == Color.Transparent) parentBg = Theme.Bg;
        g.Clear(parentBg);
        Color fill, fore;
        switch (Kind)
        {
            case BtnKind.Primary: fill = Theme.Accent; fore = Color.White; break;
            case BtnKind.Secondary: fill = Theme.Surface3; fore = Theme.Text; break;
            case BtnKind.Danger: fill = Theme.Danger; fore = Color.White; break;
            case BtnKind.Ghost: fill = parentBg; fore = Theme.Text; break;
            default: fill = Theme.Surface3; fore = Theme.Text; break;
        }
        bool gradient = Enabled && (Kind == BtnKind.Primary || (Active && Kind != BtnKind.Danger));
        switch (Kind)
        {
            default: break;
        }
        if (Active && Kind != BtnKind.Danger) { fill = Theme.Accent; fore = Color.White; }
        if (!Enabled) { fill = Theme.Mix(fill, parentBg, 0.6f); fore = Theme.Mix(fore, parentBg, 0.55f); }
        else if (down) fill = Theme.Mix(fill, Color.Black, 0.18f);
        else if (hover) fill = Kind == BtnKind.Ghost ? Theme.Surface3 : Theme.Mix(fill, Color.White, 0.10f);
        var r = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
        using (var path = Theme.Round(r, Math.Min(10, Height / 2f)))
        {
            if (gradient)
            {
                using (Brush b = Theme.AccentBrush(r)) g.FillPath(b, path);
                if (hover || down) using (var b = new SolidBrush(Color.FromArgb(down ? 50 : 28, down ? Color.Black : Color.White))) g.FillPath(b, path);
            }
            else
            {
                using (var b = new SolidBrush(fill)) g.FillPath(b, path);
                if (Kind == BtnKind.Secondary) using (var p = new Pen(Enabled ? Theme.Border2 : Theme.Mix(Theme.Border2, parentBg, 0.5f))) g.DrawPath(p, path);
            }
            if (Focused && ShowFocusCues) using (var p = new Pen(Theme.Accent2, 1.5f)) g.DrawPath(p, path);
        }
        float x = 10;
        string shown = ShownText;
        bool hasText = !string.IsNullOrEmpty(shown);
        Font icon = !string.IsNullOrEmpty(Glyph) ? Theme.Icon(11f) : null;
        float textW = 0;
        using (var f = Theme.Ui(9.5f))
        {
            if (hasText) textW = g.MeasureString(shown, f).Width;
            float total = textW + (icon != null ? (hasText ? 22 : 16) : 0);
            x = Math.Max(8, (Width - total) / 2f);
            if (icon != null)
            {
                using (var b = new SolidBrush(fore)) g.DrawString(Glyph, icon, b, x - 2, (Height - icon.Height) / 2f + 1);
                x += hasText ? 22 : 16;
                icon.Dispose();
            }
            if (hasText) TextRenderer.DrawText(g, shown, f, new Point((int)x, (int)((Height - f.Height) / 2f)), fore, TextFormatFlags.NoPadding);
        }
    }
}

// ---------------------------------------------------------------- Interruttore (sostituisce la casella di spunta)
public class Toggle : CheckBox
{
    bool hover;

    public Toggle()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        AutoSize = false;
        Height = 26;
        Cursor = Cursors.Hand;
        Margin = new Padding(2, 2, 10, 2);
    }

    public override Size GetPreferredSize(Size proposed)
    {
        using (var g = CreateGraphics())
        using (var f = Theme.Ui(9f))
            return new Size(48 + (int)Math.Ceiling(g.MeasureString(Text ?? "", f).Width), 26);
    }

    protected override void OnTextChanged(EventArgs e) { Size = GetPreferredSize(Size.Empty); base.OnTextChanged(e); }
    protected override void OnCreateControl() { Size = GetPreferredSize(Size.Empty); base.OnCreateControl(); }
    protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { hover = false; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnEnabledChanged(EventArgs e) { Invalidate(); base.OnEnabledChanged(e); }

    protected override void OnPaint(PaintEventArgs e)
    {
        Graphics g = e.Graphics;
        Color bg = Parent != null ? Parent.BackColor : Theme.Surface;
        g.Clear(bg);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        var track = new RectangleF(1, (Height - 18) / 2f, 34, 18);
        Color on = Theme.Accent, off = Theme.Surface3;
        Color fill = Checked ? on : off;
        if (hover && Enabled) fill = Theme.Mix(fill, Color.White, 0.08f);
        if (!Enabled) fill = Theme.Mix(fill, bg, 0.6f);
        using (var path = Theme.Round(track, 9))
        {
            if (Checked && Enabled) using (Brush b = Theme.AccentBrush(track)) g.FillPath(b, path);
            else using (var b = new SolidBrush(fill)) g.FillPath(b, path);
        }
        if (!Checked) using (var path = Theme.Round(track, 9)) using (var p = new Pen(Theme.Border)) g.DrawPath(p, path);
        float kx = Checked ? track.Right - 16 : track.X + 2;
        using (var b = new SolidBrush(Enabled ? Color.White : Theme.Mix(Color.White, bg, 0.5f))) g.FillEllipse(b, kx, track.Y + 2, 14, 14);
        Color fore = Enabled ? Theme.Text : Theme.TextDim;
        using (var f = Theme.Ui(9f))
            TextRenderer.DrawText(g, Text, f, new Point(42, (Height - f.Height) / 2), fore, TextFormatFlags.NoPadding);
        if (Focused && ShowFocusCues) using (var p = new Pen(Theme.Accent2)) g.DrawRectangle(p, 0, 0, Width - 1, Height - 1);
    }
}

// ---------------------------------------------------------------- Cursore (sostituisce TrackBar)
[DefaultEvent("ValueChanged")]
public class Slider : Control
{
    int min, max = 100, val;
    public int TickFrequency;
    public string Suffix = "";
    public event EventHandler ValueChanged;
    bool drag;

    public Slider()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
        Height = 28;
        Cursor = Cursors.Hand;
        TabStop = true;
    }

    public int Minimum { get { return min; } set { min = value; if (max < min) max = min; Value = val; Invalidate(); } }
    public int Maximum { get { return max; } set { max = Math.Max(value, min); Value = val; Invalidate(); } }
    public int Value
    {
        get { return val; }
        set
        {
            int v = Math.Max(min, Math.Min(max, value));
            if (v == val) return;
            val = v;
            Invalidate();
            var h = ValueChanged;
            if (h != null) h(this, EventArgs.Empty);
        }
    }

    RectangleF Track { get { return new RectangleF(8, Height / 2f - 2, Math.Max(10, Width - 64), 4); } }

    void SetFromX(int x)
    {
        RectangleF t = Track;
        float p = Math.Max(0, Math.Min(1, (x - t.X) / t.Width));
        Value = min + (int)Math.Round(p * (max - min));
    }

    protected override void OnMouseDown(MouseEventArgs e) { Focus(); if (e.Button == MouseButtons.Left) { drag = true; SetFromX(e.X); } base.OnMouseDown(e); }
    protected override void OnMouseMove(MouseEventArgs e) { if (drag) SetFromX(e.X); base.OnMouseMove(e); }
    protected override void OnMouseUp(MouseEventArgs e) { drag = false; base.OnMouseUp(e); }
    protected override void OnMouseWheel(MouseEventArgs e) { if (Focused) Value += e.Delta > 0 ? 1 : -1; if (e is HandledMouseEventArgs) ((HandledMouseEventArgs)e).Handled = true; base.OnMouseWheel(e); }
    protected override void OnDoubleClick(EventArgs e) { Value = DefaultValue; base.OnDoubleClick(e); }
    public int DefaultValue;
    protected override bool IsInputKey(Keys k) { return k == Keys.Left || k == Keys.Right || base.IsInputKey(k); }
    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Left) Value -= 1; else if (e.KeyCode == Keys.Right) Value += 1;
        base.OnKeyDown(e);
    }
    protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
    protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }
    protected override void OnEnabledChanged(EventArgs e) { Invalidate(); base.OnEnabledChanged(e); }

    protected override void OnPaint(PaintEventArgs e)
    {
        Graphics g = e.Graphics;
        Color bg = Parent != null ? Parent.BackColor : Theme.Surface;
        g.Clear(bg);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        RectangleF t = Track;
        float p = max > min ? (val - min) / (float)(max - min) : 0;
        using (var path = Theme.Round(t, 2)) using (var b = new SolidBrush(Theme.Surface3)) g.FillPath(b, path);
        var filled = new RectangleF(t.X, t.Y, t.Width * p, t.Height);
        if (filled.Width > 1)
            using (var path = Theme.Round(filled, 2))
            using (var b = new LinearGradientBrush(new RectangleF(t.X - 1, t.Y, t.Width + 2, t.Height), Theme.Accent, Theme.Accent2, 0f))
                g.FillPath(b, path);
        float kx = t.X + t.Width * p;
        Color knob = Enabled ? Color.White : Theme.TextDim;
        using (var b = new SolidBrush(knob)) g.FillEllipse(b, kx - 7, Height / 2f - 7, 14, 14);
        if (Focused) using (var pen = new Pen(Theme.Accent2, 2)) g.DrawEllipse(pen, kx - 8, Height / 2f - 8, 16, 16);
        using (var f = Theme.Ui(8.5f))
            TextRenderer.DrawText(g, val + Suffix, f, new Rectangle((int)t.Right + 6, 0, Width - (int)t.Right - 6, Height), Enabled ? Theme.Text : Theme.TextDim, TextFormatFlags.VerticalCenter | TextFormatFlags.Right);
    }
}

// ---------------------------------------------------------------- Menu a tendina scuro
public class FlatCombo : ComboBox
{
    public FlatCombo()
    {
        DropDownStyle = ComboBoxStyle.DropDownList;
        DrawMode = DrawMode.OwnerDrawFixed;
        FlatStyle = FlatStyle.Flat;
        ItemHeight = 20;
        BackColor = Theme.Surface2;
        ForeColor = Theme.Text;
        IntegralHeight = false;
        MaxDropDownItems = 16;
    }

    protected override void OnDrawItem(DrawItemEventArgs e)
    {
        if (e.Index < 0) return;
        bool sel = (e.State & DrawItemState.Selected) != 0 && (e.State & DrawItemState.ComboBoxEdit) == 0;
        using (var b = new SolidBrush(sel ? Theme.Accent : Theme.Surface2)) e.Graphics.FillRectangle(b, e.Bounds);
        TextRenderer.DrawText(e.Graphics, GetItemText(Items[e.Index]), Font, new Rectangle(e.Bounds.X + 4, e.Bounds.Y, e.Bounds.Width - 4, e.Bounds.Height),
            Enabled ? (sel ? Color.White : Theme.Text) : Theme.TextDim, TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
    }
}

// ---------------------------------------------------------------- Scheda arrotondata con titolo
public class Card : Panel
{
    public string Title;
    public readonly TableLayoutPanel Body;

    public Card(string title)
    {
        Title = title;
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.UserPaint, true);
        Padding = new Padding(14, string.IsNullOrEmpty(title) ? 10 : 34, 14, 12);
        Margin = new Padding(0, 0, 0, 10);
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        BackColor = Theme.Surface;
        Body = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, ColumnCount = 1, Margin = new Padding(0), Padding = new Padding(0), BackColor = Theme.Surface };
        Body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        Controls.Add(Body);
    }

    protected override void OnPaintBackground(PaintEventArgs e)
    {
        Color parent = Parent != null ? Parent.BackColor : Theme.Bg;
        e.Graphics.Clear(parent);
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using (var path = Theme.Round(new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f), 12))
        {
            using (var b = new SolidBrush(Theme.Surface)) e.Graphics.FillPath(b, path);
            using (var p = new Pen(Theme.Border)) e.Graphics.DrawPath(p, path);
        }
        if (!string.IsNullOrEmpty(Title))
            using (var f = Theme.UiBold(10f))
                TextRenderer.DrawText(e.Graphics, Title, f, new Point(14, 10), Theme.Text, TextFormatFlags.NoPadding);
    }

    protected override void OnPaint(PaintEventArgs e) { }
}

// ---------------------------------------------------------------- Pulsante della barra laterale
public class NavButton : Control
{
    public string Glyph;
    public bool Selected;
    bool hover;

    public NavButton()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        Size = new Size(56, 52);
        Cursor = Cursors.Hand;
        Margin = new Padding(4, 2, 4, 2);
    }

    protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { hover = false; Invalidate(); base.OnMouseLeave(e); }

    protected override void OnPaint(PaintEventArgs e)
    {
        Graphics g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(Parent != null ? Parent.BackColor : Theme.Surface);
        var r = new RectangleF(4, 2, Width - 8, Height - 4);
        if (Selected || hover)
            using (var path = Theme.Round(r, 10)) using (var b = new SolidBrush(Selected ? Theme.Mix(Theme.Accent, Theme.Surface, Theme.Dark ? 0.82f : 0.88f) : Theme.Surface3)) g.FillPath(b, path);
        if (Selected)
            using (var path = Theme.Round(new RectangleF(0, Height / 2f - 11, 3.5f, 22), 1.7f))
            using (Brush b = Theme.AccentBrush(new RectangleF(0, Height / 2f - 11, 3.5f, 22))) g.FillPath(b, path);
        Color fore = Selected ? Theme.Text : Theme.TextDim;
        Color iconColor = Selected ? Theme.AccentSoft : Theme.TextDim;
        Font icon = Theme.Icon(15f);
        if (icon != null)
        {
            using (icon)
            {
                SizeF s = g.MeasureString(Glyph, icon);
                using (var b = new SolidBrush(iconColor)) g.DrawString(Glyph, icon, b, (Width - s.Width) / 2 + 1, 6);
            }
            // il carattere si riduce finché la parola intera ci sta (es. "Acquisizione", "Automazioni")
            float size = 7f;
            Font f = Theme.Ui(size);
            while (size > 5.5f && TextRenderer.MeasureText(Text, f).Width > Width - 2) { f.Dispose(); size -= 0.25f; f = Theme.Ui(size); }
            using (f)
                TextRenderer.DrawText(g, Text, f, new Rectangle(0, Height - 18, Width, 14), fore, TextFormatFlags.HorizontalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);
        }
        else using (var f = Theme.Ui(7.5f))
                TextRenderer.DrawText(g, Text, f, new Rectangle(0, 0, Width, Height), fore, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.WordBreak);
    }
}

// ---------------------------------------------------------------- Etichetta a pillola (stato)
public class Chip : Control
{
    public Color Dot = Color.Gray;
    public bool Pulse;
    int phase;
    readonly Timer timer;

    public Chip()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        Height = 26;
        Margin = new Padding(3, 11, 3, 0);
        timer = new Timer { Interval = 500 };
        timer.Tick += delegate { phase ^= 1; if (Pulse) Invalidate(); };
        timer.Start();
    }

    public void Set(string text, Color dot, bool pulse)
    {
        if (Text == text && Dot == dot && Pulse == pulse) return;
        Text = text; Dot = dot; Pulse = pulse;
        using (var g = CreateGraphics()) using (var f = Theme.Ui(8.5f)) Width = 30 + (int)g.MeasureString(text, f).Width;
        Invalidate();
    }

    protected override void Dispose(bool disposing) { if (disposing) timer.Dispose(); base.Dispose(disposing); }

    protected override void OnPaint(PaintEventArgs e)
    {
        Graphics g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(Parent != null ? Parent.BackColor : Theme.Surface);
        using (var path = Theme.Round(new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f), Height / 2f))
        using (var b = new SolidBrush(Theme.Surface3)) g.FillPath(b, path);
        Color d = Pulse && phase == 1 ? Theme.Mix(Dot, Theme.Surface3, 0.6f) : Dot;
        using (var b = new SolidBrush(d)) g.FillEllipse(b, 10, Height / 2f - 4, 8, 8);
        using (var f = Theme.Ui(8.5f))
            TextRenderer.DrawText(g, Text, f, new Point(22, (Height - f.Height) / 2), Theme.Text, TextFormatFlags.NoPadding);
    }
}

// ---------------------------------------------------------------- Indicatore livello audio
public class Meter : Control
{
    int level, peak;
    DateTime peakAt;

    public Meter()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        Size = new Size(140, 14);
        Margin = new Padding(8, 14, 4, 0);
    }

    public int Value
    {
        get { return level; }
        set
        {
            level = Math.Max(0, Math.Min(100, value));
            if (level >= peak || (DateTime.Now - peakAt).TotalSeconds > 1.5) { peak = level; peakAt = DateTime.Now; }
            Invalidate();
        }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        Graphics g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(Parent != null ? Parent.BackColor : Theme.Surface);
        var r = new RectangleF(0, 2, Width - 1, Height - 4);
        using (var path = Theme.Round(r, 4)) using (var b = new SolidBrush(Theme.Surface3)) g.FillPath(b, path);
        float w = r.Width * level / 100f;
        if (w > 1)
            using (var b = new LinearGradientBrush(r, Theme.Success, Theme.Danger, 0f))
            {
                var blend = new ColorBlend(3);
                blend.Colors = new[] { Theme.Success, Theme.Warning, Theme.Danger };
                blend.Positions = new[] { 0f, 0.75f, 1f };
                b.InterpolationColors = blend;
                using (var path = Theme.Round(new RectangleF(r.X, r.Y, w, r.Height), 4)) g.FillPath(b, path);
            }
        float px = r.Width * peak / 100f;
        if (peak > 0) using (var p = new Pen(peak >= 98 ? Theme.Danger : Theme.Text, 2)) g.DrawLine(p, px, r.Y, px, r.Bottom);
    }
}

// ---------------------------------------------------------------- Marchio della suite (icona + nome sfumato)
public class BrandMark : Control
{
    public string Product = "Video2All";
    public string Suite = "";

    public BrandMark()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        Size = new Size(240, 40);
    }

    // tessera dell'icona: fondo scuro con aloni viola/azzurro e simbolo sfumato (come l'icona di MultiTool)
    public static void DrawTile(Graphics g, RectangleF t)
    {
        g.SmoothingMode = SmoothingMode.AntiAlias;
        float rad = t.Width * 0.24f;
        using (var path = Theme.Round(t, rad))
        {
            using (var b = new SolidBrush(Color.FromArgb(0x0B, 0x0D, 0x12))) g.FillPath(b, path);
            GraphicsState state = g.Save();
            g.SetClip(path, CombineMode.Intersect);
            Theme.Glow(g, new RectangleF(t.X + t.Width * 0.35f, t.Y - t.Height * 0.55f, t.Width * 1.1f, t.Height * 1.1f), Color.FromArgb(0x7C, 0x5C, 0xFF), 150);
            Theme.Glow(g, new RectangleF(t.X - t.Width * 0.55f, t.Y + t.Height * 0.5f, t.Width * 1.1f, t.Height * 1.1f), Color.FromArgb(0x22, 0xD3, 0xEE), 90);
            g.Restore(state);
            using (var p = new Pen(Color.FromArgb(0x2A, 0x30, 0x48), Math.Max(1f, t.Width / 40f))) g.DrawPath(p, path);
        }
        // simbolo: videocamera
        float u = t.Width / 32f;
        var body = new RectangleF(t.X + 6 * u, t.Y + 10 * u, 14 * u, 12 * u);
        using (var glyph = new GraphicsPath())
        {
            using (GraphicsPath rb = Theme.Round(body, 3 * u)) glyph.AddPath(rb, false);
            glyph.AddPolygon(new[] { new PointF(t.X + 21 * u, t.Y + 14.5f * u), new PointF(t.X + 26.5f * u, t.Y + 11 * u), new PointF(t.X + 26.5f * u, t.Y + 21 * u), new PointF(t.X + 21 * u, t.Y + 17.5f * u) });
            using (Brush b = Theme.AccentBrush(new RectangleF(t.X + 6 * u, t.Y + 10 * u, 21 * u, 12 * u))) g.FillPath(b, glyph);
        }
        using (var b = new SolidBrush(Color.FromArgb(0x0B, 0x0D, 0x12))) g.FillEllipse(b, body.X + 3 * u, body.Y + 3 * u, 3.2f * u, 3.2f * u);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        Graphics g = e.Graphics;
        g.Clear(Parent != null ? Parent.BackColor : Theme.Surface);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
        float s = Height - 8;
        DrawTile(g, new RectangleF(2, 4, s, s));
        using (var f = new Font(Theme.UiFamily, 13f, FontStyle.Bold))
        {
            SizeF ts = g.MeasureString(Product, f);
            float x = s + 12, y = (Height - ts.Height) / 2f + (Suite.Length > 0 ? -6 : 0);
            using (Brush b = Theme.AccentBrush(new RectangleF(x, y, ts.Width, ts.Height))) g.DrawString(Product, f, b, x, y);
            if (Suite.Length > 0)
                using (var f2 = Theme.Ui(7.5f)) using (var b2 = new SolidBrush(Theme.TextFaint)) g.DrawString(Suite, f2, b2, x + 1, y + ts.Height - 3);
        }
    }
}

// ---------------------------------------------------------------- Rotellina del mouse
// La rotellina va al controllo sotto il puntatore. Su caselle numeriche, menu e cursori NON selezionati
// scorre la pagina invece di cambiarne il valore (prima, scorrendo le impostazioni, si cambiavano valori per sbaglio).
public sealed class WheelFilter : IMessageFilter
{
    [DllImport("user32.dll")] static extern IntPtr WindowFromPoint(Point p);
    [DllImport("user32.dll")] static extern IntPtr SendMessage(IntPtr h, int msg, IntPtr w, IntPtr l);
    const int WM_MOUSEWHEEL = 0x020A;

    public bool PreFilterMessage(ref Message m)
    {
        if (m.Msg != WM_MOUSEWHEEL) return false;
        try
        {
            IntPtr under = WindowFromPoint(Cursor.Position);
            if (under == IntPtr.Zero) return false;
            Control c = Control.FromChildHandle(under);
            if (c == null) return false;
            Control valueCtl = c is ComboBox || c is NumericUpDown || c is Slider ? c : (c.Parent is NumericUpDown ? c.Parent : null);
            if (valueCtl != null && !valueCtl.Focused && !valueCtl.ContainsFocus)
            {
                Control p = valueCtl.Parent;
                while (p != null && !(p is ScrollableControl && ((ScrollableControl)p).AutoScroll)) p = p.Parent;
                if (p != null) { SendMessage(p.Handle, WM_MOUSEWHEEL, m.WParam, m.LParam); return true; }
                return true;            // nessuna pagina da scorrere: si ignora, il valore non cambia
            }
            if (under != m.HWnd) { SendMessage(under, WM_MOUSEWHEEL, m.WParam, m.LParam); return true; }
        }
        catch { }
        return false;
    }
}
