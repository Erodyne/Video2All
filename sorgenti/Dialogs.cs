using System;
using System.Drawing;
using System.Windows.Forms;

// Finestre di messaggio con il tema del programma (sostituiscono MessageBox)
public static class Dlg
{
    public static DialogResult Show(string text, string caption) { return Show(null, text, caption, MessageBoxButtons.OK, MessageBoxIcon.None); }
    public static DialogResult Show(IWin32Window owner, string text, string caption) { return Show(owner, text, caption, MessageBoxButtons.OK, MessageBoxIcon.None); }
    public static DialogResult Show(IWin32Window owner, string text, string caption, MessageBoxButtons buttons) { return Show(owner, text, caption, buttons, MessageBoxIcon.None); }

    public static DialogResult Show(IWin32Window owner, string text, string caption, MessageBoxButtons buttons, MessageBoxIcon icon)
    {
        try { return ShowThemed(owner, text, caption, buttons, icon); }
        catch { return owner != null ? MessageBox.Show(owner, text, caption, buttons, icon) : MessageBox.Show(text, caption, buttons, icon); }
    }

    static DialogResult ShowThemed(IWin32Window owner, string text, string caption, MessageBoxButtons buttons, MessageBoxIcon icon)
    {
        using (var f = new Form())
        {
            f.Text = caption;
            f.FormBorderStyle = FormBorderStyle.FixedDialog;
            f.MaximizeBox = f.MinimizeBox = false;
            f.ShowInTaskbar = owner == null;
            f.StartPosition = owner != null ? FormStartPosition.CenterParent : FormStartPosition.CenterScreen;
            f.BackColor = Theme.Bg;
            f.ForeColor = Theme.Text;
            f.Font = Theme.Ui(9.5f);
            f.KeyPreview = true;
            // con il Live a schermo intero (sempre in primo piano) la domanda non deve finire dietro
            try { foreach (Form o in Application.OpenForms) if (o.TopMost && o.Visible) { f.TopMost = true; break; } } catch { }
            text = (text ?? "").Replace("\r\n", "\n").Replace("\n", "\r\n");
            bool longText = text.Length > 420 || text.Split('\n').Length > 12;
            int width = longText ? 620 : 460;

            Color accent = icon == MessageBoxIcon.Error || icon == MessageBoxIcon.Warning ? Theme.Danger : icon == MessageBoxIcon.Question ? Theme.Accent2 : Theme.Accent;
            var bar = new Panel { Dock = DockStyle.Top, Height = 4, BackColor = accent };
            if (accent == Theme.Accent) bar.Paint += delegate (object s2, PaintEventArgs pe) { using (Brush b = Theme.AccentBrush(bar.ClientRectangle)) pe.Graphics.FillRectangle(b, bar.ClientRectangle); };
            var title = new Label { Text = caption, Font = Theme.UiBold(12f), AutoSize = true, Location = new Point(22, 20), ForeColor = Theme.Text };
            Control body;
            if (longText)
                body = new TextBox { Text = text, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, BorderStyle = BorderStyle.None, BackColor = Theme.Surface, ForeColor = Theme.Text, Location = new Point(22, 56), Size = new Size(width - 44, 260), TabStop = false };
            else
            {
                Size need = TextRenderer.MeasureText(text, f.Font, new Size(width - 44, int.MaxValue), TextFormatFlags.WordBreak | TextFormatFlags.TextBoxControl);
                body = new Label { Text = text, AutoSize = false, Size = new Size(width - 44, need.Height + need.Height / 8 + 6), Location = new Point(22, 56), ForeColor = Theme.Text };
            }
            f.Controls.Add(bar); f.Controls.Add(title); f.Controls.Add(body);

            string[] labels; DialogResult[] results;
            switch (buttons)
            {
                case MessageBoxButtons.YesNo: labels = new[] { L.T("Sì", "Yes"), "No" }; results = new[] { DialogResult.Yes, DialogResult.No }; break;
                case MessageBoxButtons.OKCancel: labels = new[] { "OK", L.T("Annulla", "Cancel") }; results = new[] { DialogResult.OK, DialogResult.Cancel }; break;
                case MessageBoxButtons.YesNoCancel: labels = new[] { L.T("Sì", "Yes"), "No", L.T("Annulla", "Cancel") }; results = new[] { DialogResult.Yes, DialogResult.No, DialogResult.Cancel }; break;
                default: labels = new[] { "OK" }; results = new[] { DialogResult.OK }; break;
            }
            int bodyBottom = body.Bottom;
            int y = Math.Max(bodyBottom + 22, 110);
            int x = width - 22;
            ModernButton first = null;
            for (int i = labels.Length - 1; i >= 0; i--)
            {
                var b = new ModernButton { Text = labels[i], Kind = i == 0 ? BtnKind.Primary : BtnKind.Secondary, Height = 34, AutoSize = true };
                b.Size = new Size(Math.Max(96, b.GetPreferredSize(Size.Empty).Width), 34);
                x -= b.Width;
                b.Location = new Point(x, y);
                x -= 8;
                DialogResult r = results[i];
                b.Click += delegate { f.DialogResult = r; f.Close(); };
                f.Controls.Add(b);
                if (i == 0) first = b;
            }
            f.ClientSize = new Size(width, y + 34 + 20);
            f.AcceptButton = first;
            f.KeyDown += delegate (object s, KeyEventArgs e)
            {
                if (e.KeyCode == Keys.Escape) { f.DialogResult = results[results.Length - 1]; f.Close(); }
                else if (e.Control && e.KeyCode == Keys.C) { try { Clipboard.SetText(text); } catch { } }
            };
            f.Load += delegate { Theme.ApplyWindow(f); if (first != null) first.Focus(); };
            DialogResult res = owner != null ? f.ShowDialog(owner) : f.ShowDialog();
            return res == DialogResult.None || res == DialogResult.Cancel && buttons == MessageBoxButtons.OK ? DialogResult.OK : res;
        }
    }
}
