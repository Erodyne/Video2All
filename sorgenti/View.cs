using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Windows.Forms;

// Doppio buffer di fotogrammi: il thread di lettura scrive in Back, l'interfaccia disegna Front
public sealed class FrameStore
{
    public readonly object Sync = new object();
    public Bitmap Front, Back;
    public readonly int W, H;
    public bool HasFrame;

    public FrameStore(int w, int h)
    {
        W = w; H = h;
        Front = new Bitmap(w, h, PixelFormat.Format24bppRgb);
        Back = new Bitmap(w, h, PixelFormat.Format24bppRgb);
    }

    // Copia dei pixel BGR24 nel buffer posteriore e scambio
    readonly object pushLock = new object();

    public void Push(byte[] bgr)
    {
        lock (pushLock) PushCore(bgr);
    }

    void PushCore(byte[] bgr)
    {
        Bitmap b = Back;
        BitmapData d = b.LockBits(new Rectangle(0, 0, W, H), ImageLockMode.WriteOnly, PixelFormat.Format24bppRgb);
        try
        {
            int row = W * 3;
            if (d.Stride == row) System.Runtime.InteropServices.Marshal.Copy(bgr, 0, d.Scan0, row * H);
            else
                for (int y = 0; y < H; y++)
                    System.Runtime.InteropServices.Marshal.Copy(bgr, y * row, new IntPtr(d.Scan0.ToInt64() + (long)y * d.Stride), row);
        }
        finally { b.UnlockBits(d); }
        lock (Sync) { Back = Front; Front = b; HasFrame = true; }
    }

    public Bitmap Snapshot()
    {
        lock (Sync) return HasFrame ? (Bitmap)Front.Clone() : null;
    }

    public void Clear()
    {
        lock (Sync)
        {
            HasFrame = false;
            using (var g = Graphics.FromImage(Front)) g.Clear(Color.Black);
        }
    }
}

// Controllo che disegna l'ultimo fotogramma con proporzioni, nitidezza, scanline, griglia e strumenti di misura
public sealed class VideoView : Control
{
    public FrameStore Store;
    public float Aspect = 4f / 3f;
    public bool Sharp, Scanlines, IntScale, Rounded, ShowGrid;
    public int Scope;                 // 0 no, 1 istogramma, 2 waveform, 3 vettorscopio
    public int BaseLines = 288;
    public string Overlay;
    Bitmap scopeBmp;
    DateTime scopeAt;

    public VideoView()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        BackColor = Color.Black;
    }

    public Rectangle DestRect()
    {
        int W = ClientSize.Width, H = ClientSize.Height;
        if (W <= 0 || H <= 0) return Rectangle.Empty;
        float a = Aspect > 0.1f ? Aspect : 4f / 3f;
        int dw, dh;
        if (IntScale && BaseLines > 0 && H >= BaseLines)
        {
            int k = H / BaseLines;
            dh = k * BaseLines; dw = (int)(dh * a);
            while (dw > W && k > 1) { k--; dh = k * BaseLines; dw = (int)(dh * a); }
            if (dw <= W) return new Rectangle((W - dw) / 2, (H - dh) / 2, dw, dh);
        }
        dw = W; dh = (int)(W / a);
        if (dh > H) { dh = H; dw = (int)(H * a); }
        return new Rectangle((W - dw) / 2, (H - dh) / 2, dw, dh);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        Graphics g = e.Graphics;
        if (Rounded)
        {
            g.Clear(Parent != null ? Parent.BackColor : Theme.Bg);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (var path = Theme.Round(new RectangleF(0, 0, Width - 1, Height - 1), 14))
            {
                using (var b = new SolidBrush(Color.Black)) g.FillPath(b, path);
                g.SetClip(path);
            }
            g.SmoothingMode = SmoothingMode.None;
        }
        else g.Clear(Color.Black);
        Rectangle r = DestRect();
        bool has = false;
        if (Store != null && r.Width > 0)
        {
            lock (Store.Sync)
            {
                if (Store.HasFrame)
                {
                    has = true;
                    g.InterpolationMode = Sharp ? InterpolationMode.NearestNeighbor : InterpolationMode.Bilinear;
                    g.PixelOffsetMode = PixelOffsetMode.Half;
                    g.CompositingQuality = CompositingQuality.HighSpeed;
                    g.DrawImage(Store.Front, r, 0, 0, Store.W, Store.H, GraphicsUnit.Pixel);
                    if (Scope > 0 && (DateTime.Now - scopeAt).TotalMilliseconds > 150) { scopeAt = DateTime.Now; BuildScope(Store.Front); }
                }
            }
            if (Scanlines && BaseLines > 0)
            {
                float step = r.Height / (float)BaseLines;
                if (step >= 2f)
                    using (var br = new SolidBrush(Color.FromArgb(90, 0, 0, 0)))
                        for (float y = r.Top + step / 2f; y < r.Bottom; y += step)
                            g.FillRectangle(br, r.Left, y, r.Width, step / 2f);
            }
            if (ShowGrid && has)
            {
                using (var p = new Pen(Color.FromArgb(70, 255, 255, 255)))
                {
                    for (int i = 1; i < 8; i++) g.DrawLine(p, r.Left + r.Width * i / 8, r.Top, r.Left + r.Width * i / 8, r.Bottom);
                    for (int i = 1; i < 6; i++) g.DrawLine(p, r.Left, r.Top + r.Height * i / 6, r.Right, r.Top + r.Height * i / 6);
                }
                using (var p = new Pen(Color.FromArgb(200, 255, 200, 0), 2))
                    g.DrawRectangle(p, r.Left + r.Width * 0.05f, r.Top + r.Height * 0.05f, r.Width * 0.9f, r.Height * 0.9f);
            }
            if (Scope > 0 && has && scopeBmp != null)
            {
                int sw = scopeBmp.Width, sh = scopeBmp.Height;
                var dst = new Rectangle(r.Right - sw - 12, r.Bottom - sh - 12, sw, sh);
                using (var b = new SolidBrush(Color.FromArgb(170, 0, 0, 0))) g.FillRectangle(b, dst.X - 4, dst.Y - 4, sw + 8, sh + 8);
                g.DrawImageUnscaled(scopeBmp, dst.Location);
            }
        }
        if (!string.IsNullOrEmpty(Overlay))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (var f = new Font(Theme.UiFamily, Math.Max(10f, Height / 30f), FontStyle.Bold))
            {
                SizeF s = g.MeasureString(Overlay, f);
                var box = new RectangleF((Width - s.Width) / 2 - 14, 14, s.Width + 28, s.Height + 10);
                using (var path = Theme.Round(box, box.Height / 2))
                using (var b = new SolidBrush(Color.FromArgb(220, Theme.Danger))) g.FillPath(b, path);
                g.DrawString(Overlay, f, Brushes.White, box.Left + 14, box.Top + 5);
            }
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing && scopeBmp != null) { scopeBmp.Dispose(); scopeBmp = null; }
        base.Dispose(disposing);
    }

    // Strumenti di misura calcolati dal fotogramma (non finiscono in foto, Live o registrazioni)
    void BuildScope(Bitmap src)
    {
        int w = src.Width, h = src.Height;
        BitmapData d = src.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.ReadOnly, PixelFormat.Format24bppRgb);
        var px = new byte[d.Stride * h];
        System.Runtime.InteropServices.Marshal.Copy(d.Scan0, px, 0, px.Length);
        int stride = d.Stride;
        src.UnlockBits(d);
        Bitmap bmp;
        if (Scope == 3)
        {
            bmp = new Bitmap(180, 180, PixelFormat.Format32bppArgb);
            var acc = new int[180 * 180];
            var col = new int[180 * 180];
            for (int y = 0; y < h; y += 3)
                for (int x = 0; x < w; x += 3)
                {
                    int i = y * stride + x * 3;
                    int B = px[i], G = px[i + 1], R = px[i + 2];
                    int cb = (int)(-0.1687 * R - 0.3313 * G + 0.5 * B + 128), cr = (int)(0.5 * R - 0.4187 * G - 0.0813 * B + 128);
                    int vx = cb * 179 / 255, vy = 179 - cr * 179 / 255;
                    int k = vy * 180 + vx;
                    acc[k]++;
                    col[k] = (R << 16) | (G << 8) | B;
                }
            for (int k = 0; k < acc.Length; k++)
                if (acc[k] > 0) bmp.SetPixel(k % 180, k / 180, Color.FromArgb(Math.Min(255, 90 + acc[k] * 20), Color.FromArgb(col[k] | unchecked((int)0xFF000000))));
            using (var g = Graphics.FromImage(bmp))
            using (var p = new Pen(Color.FromArgb(80, 120, 255, 120)))
            {
                g.DrawEllipse(p, 2, 2, 175, 175);
                g.DrawLine(p, 90, 0, 90, 180); g.DrawLine(p, 0, 90, 180, 90);
            }
        }
        else
        {
            const int SW = 256, SH = 128;
            bmp = new Bitmap(SW, SH, PixelFormat.Format32bppArgb);
            if (Scope == 1)
            {
                var hist = new int[256];
                int max = 1;
                for (int y = 0; y < h; y += 2)
                    for (int x = 0; x < w; x += 2)
                    {
                        int i = y * stride + x * 3;
                        hist[(px[i] * 29 + px[i + 1] * 150 + px[i + 2] * 77) >> 8]++;
                    }
                for (int i = 0; i < 256; i++) if (hist[i] > max) max = hist[i];
                using (var g = Graphics.FromImage(bmp))
                using (var p = new Pen(Color.FromArgb(220, 230, 230, 240)))
                    for (int i = 0; i < 256; i++)
                    {
                        int hh = (int)(Math.Sqrt(hist[i] / (double)max) * (SH - 2));
                        g.DrawLine(p, i, SH - 1, i, SH - 1 - hh);
                    }
            }
            else
            {
                var acc = new int[SW * SH];
                for (int y = 0; y < h; y += 2)
                    for (int x = 0; x < w; x += 2)
                    {
                        int i = y * stride + x * 3;
                        int l = (px[i] * 29 + px[i + 1] * 150 + px[i + 2] * 77) >> 8;
                        int cx = x * (SW - 1) / (w - 1), cy = (SH - 1) - l * (SH - 1) / 255;
                        acc[cy * SW + cx]++;
                    }
                for (int k = 0; k < acc.Length; k++)
                    if (acc[k] > 0) bmp.SetPixel(k % SW, k / SW, Color.FromArgb(Math.Min(255, 60 + acc[k] * 25), 120, 255, 140));
                using (var g = Graphics.FromImage(bmp))
                using (var p = new Pen(Color.FromArgb(70, 255, 255, 255)))
                {
                    g.DrawLine(p, 0, SH - 1 - 16 * (SH - 1) / 255, SW, SH - 1 - 16 * (SH - 1) / 255);
                    g.DrawLine(p, 0, SH - 1 - 235 * (SH - 1) / 255, SW, SH - 1 - 235 * (SH - 1) / 255);
                }
            }
        }
        Bitmap old = scopeBmp;
        scopeBmp = bmp;
        if (old != null) old.Dispose();
    }
}
