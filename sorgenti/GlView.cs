using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Windows.Forms;

// Superficie video disegnata dalla scheda grafica (OpenGL 1.1, presente in ogni Windows).
// Se qualcosa non va solleva Failed e il programma torna al disegno GDI.
public sealed class GlView : Control
{
    [StructLayout(LayoutKind.Sequential)]
    struct PIXELFORMATDESCRIPTOR
    {
        public ushort nSize, nVersion; public uint dwFlags; public byte iPixelType, cColorBits, cRedBits, cRedShift, cGreenBits, cGreenShift, cBlueBits, cBlueShift, cAlphaBits, cAlphaShift,
            cAccumBits, cAccumRedBits, cAccumGreenBits, cAccumBlueBits, cAccumAlphaBits, cDepthBits, cStencilBits, cAuxBuffers, iLayerType, bReserved;
        public uint dwLayerMask, dwVisibleMask, dwDamageMask;
    }
    [DllImport("user32.dll")] static extern IntPtr GetDC(IntPtr hwnd);
    [DllImport("user32.dll")] static extern int ReleaseDC(IntPtr hwnd, IntPtr dc);
    [DllImport("gdi32.dll")] static extern int ChoosePixelFormat(IntPtr dc, ref PIXELFORMATDESCRIPTOR pfd);
    [DllImport("gdi32.dll")] static extern bool SetPixelFormat(IntPtr dc, int format, ref PIXELFORMATDESCRIPTOR pfd);
    [DllImport("gdi32.dll")] static extern bool SwapBuffers(IntPtr dc);
    [DllImport("opengl32.dll")] static extern IntPtr wglCreateContext(IntPtr dc);
    [DllImport("opengl32.dll")] static extern bool wglMakeCurrent(IntPtr dc, IntPtr rc);
    [DllImport("opengl32.dll")] static extern bool wglDeleteContext(IntPtr rc);
    [DllImport("opengl32.dll")] static extern IntPtr wglGetProcAddress(string name);
    [DllImport("opengl32.dll")] static extern void glViewport(int x, int y, int w, int h);
    [DllImport("opengl32.dll")] static extern void glClearColor(float r, float g, float b, float a);
    [DllImport("opengl32.dll")] static extern void glClear(uint mask);
    [DllImport("opengl32.dll")] static extern void glEnable(uint cap);
    [DllImport("opengl32.dll")] static extern void glDisable(uint cap);
    [DllImport("opengl32.dll")] static extern void glGenTextures(int n, out uint tex);
    [DllImport("opengl32.dll")] static extern void glDeleteTextures(int n, ref uint tex);
    [DllImport("opengl32.dll")] static extern void glBindTexture(uint target, uint tex);
    [DllImport("opengl32.dll")] static extern void glTexParameteri(uint target, uint name, int value);
    [DllImport("opengl32.dll")] static extern void glPixelStorei(uint name, int value);
    [DllImport("opengl32.dll")] static extern void glTexImage2D(uint target, int level, int internalFormat, int w, int h, int border, uint format, uint type, IntPtr data);
    [DllImport("opengl32.dll")] static extern void glTexSubImage2D(uint target, int level, int x, int y, int w, int h, uint format, uint type, byte[] data);
    [DllImport("opengl32.dll")] static extern void glMatrixMode(uint mode);
    [DllImport("opengl32.dll")] static extern void glLoadIdentity();
    [DllImport("opengl32.dll")] static extern void glOrtho(double l, double r, double b, double t, double n, double f);
    [DllImport("opengl32.dll")] static extern void glBegin(uint mode);
    [DllImport("opengl32.dll")] static extern void glEnd();
    [DllImport("opengl32.dll")] static extern void glTexCoord2f(float s, float t);
    [DllImport("opengl32.dll")] static extern void glVertex2f(float x, float y);
    [DllImport("opengl32.dll")] static extern void glColor4f(float r, float g, float b, float a);
    [DllImport("opengl32.dll")] static extern void glBlendFunc(uint s, uint d);
    [DllImport("opengl32.dll")] static extern uint glGetError();
    delegate bool SwapIntervalProc(int interval);

    const uint GL_TEXTURE_2D = 0x0DE1, GL_RGB = 0x1907, GL_BGR = 0x80E0, GL_UNSIGNED_BYTE = 0x1401, GL_QUADS = 7, GL_LINES = 1,
        GL_TEXTURE_MIN_FILTER = 0x2801, GL_TEXTURE_MAG_FILTER = 0x2800, GL_NEAREST = 0x2600, GL_LINEAR = 0x2601,
        GL_TEXTURE_WRAP_S = 0x2802, GL_TEXTURE_WRAP_T = 0x2803, GL_CLAMP_TO_EDGE = 0x812F, GL_UNPACK_ALIGNMENT = 0x0CF5,
        GL_COLOR_BUFFER_BIT = 0x4000, GL_PROJECTION = 0x1701, GL_MODELVIEW = 0x1700, GL_BLEND = 0x0BE2, GL_SRC_ALPHA = 0x0302, GL_ONE_MINUS_SRC_ALPHA = 0x0303;

    readonly int fw, fh, tw = 1024, th = 1024;
    readonly byte[] frame;
    readonly object sync = new object();
    bool dirty, hasFrame, failed;
    IntPtr dc, rc;
    uint tex;
    public float Aspect = 4f / 3f;
    public bool Sharp, Scanlines, IntScale;
    public int BaseLines = 288;
    public event EventHandler Failed;

    public GlView(int w, int h)
    {
        fw = w; fh = h;
        frame = new byte[w * h * 3];
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.Opaque, true);
        SetStyle(ControlStyles.OptimizedDoubleBuffer, false);
        BackColor = Color.Black;
    }

    protected override CreateParams CreateParams
    {
        get { CreateParams cp = base.CreateParams; cp.ClassStyle |= 0x20; return cp; }   // CS_OWNDC
    }

    public void Upload(byte[] bgr)
    {
        lock (sync) { Buffer.BlockCopy(bgr, 0, frame, 0, Math.Min(bgr.Length, frame.Length)); dirty = hasFrame = true; }
    }

    // nessuna immagine: si torna al nero (come il disegno normale quando la cattura si ferma)
    public void ClearFrame()
    {
        lock (sync) { hasFrame = false; dirty = false; }
        Invalidate();
    }

    public void UploadBitmap(Bitmap b)
    {
        if (b.Width != fw || b.Height != fh) return;
        BitmapData d = b.LockBits(new Rectangle(0, 0, fw, fh), ImageLockMode.ReadOnly, PixelFormat.Format24bppRgb);
        try
        {
            lock (sync)
            {
                for (int y = 0; y < fh; y++) Marshal.Copy(new IntPtr(d.Scan0.ToInt64() + (long)y * d.Stride), frame, y * fw * 3, fw * 3);
                dirty = hasFrame = true;
            }
        }
        finally { b.UnlockBits(d); }
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        try
        {
            dc = GetDC(Handle);
            var pfd = new PIXELFORMATDESCRIPTOR();
            pfd.nSize = (ushort)Marshal.SizeOf(typeof(PIXELFORMATDESCRIPTOR));
            pfd.nVersion = 1;
            pfd.dwFlags = 0x4 | 0x20 | 0x1;          // DRAW_TO_WINDOW | SUPPORT_OPENGL | DOUBLEBUFFER
            pfd.cColorBits = 32;
            int pf = ChoosePixelFormat(dc, ref pfd);
            if (pf == 0 || !SetPixelFormat(dc, pf, ref pfd)) throw new Exception("pixel format");
            rc = wglCreateContext(dc);
            if (rc == IntPtr.Zero || !wglMakeCurrent(dc, rc)) throw new Exception("wglCreateContext");
            IntPtr si = wglGetProcAddress("wglSwapIntervalEXT");
            if (si != IntPtr.Zero) ((SwapIntervalProc)Marshal.GetDelegateForFunctionPointer(si, typeof(SwapIntervalProc)))(1);
            glGenTextures(1, out tex);
            glBindTexture(GL_TEXTURE_2D, tex);
            glPixelStorei(GL_UNPACK_ALIGNMENT, 1);
            glTexParameteri(GL_TEXTURE_2D, GL_TEXTURE_WRAP_S, (int)GL_CLAMP_TO_EDGE);
            glTexParameteri(GL_TEXTURE_2D, GL_TEXTURE_WRAP_T, (int)GL_CLAMP_TO_EDGE);
            glTexImage2D(GL_TEXTURE_2D, 0, (int)GL_RGB, tw, th, 0, GL_BGR, GL_UNSIGNED_BYTE, IntPtr.Zero);
            if (glGetError() != 0) throw new Exception("glTexImage2D");
        }
        catch (Exception ex) { Fail(ex.Message); }
    }

    void Fail(string why)
    {
        if (failed) return;
        failed = true;
        Log.W("GPU: " + why);
        var h = Failed;
        if (h != null) { try { BeginInvoke((MethodInvoker)delegate { h(this, EventArgs.Empty); }); } catch { } }
    }

    protected override void OnPaintBackground(PaintEventArgs e) { if (failed) e.Graphics.Clear(Color.Black); }

    protected override void OnPaint(PaintEventArgs e)
    {
        if (failed || rc == IntPtr.Zero) return;
        try
        {
            wglMakeCurrent(dc, rc);
            int w = Math.Max(1, ClientSize.Width), h = Math.Max(1, ClientSize.Height);
            glViewport(0, 0, w, h);
            glClearColor(0, 0, 0, 1);
            glClear(GL_COLOR_BUFFER_BIT);
            glMatrixMode(GL_PROJECTION); glLoadIdentity(); glOrtho(0, w, h, 0, -1, 1);
            glMatrixMode(GL_MODELVIEW); glLoadIdentity();
            bool draw;
            lock (sync)
            {
                draw = hasFrame;
                if (dirty)
                {
                    glBindTexture(GL_TEXTURE_2D, tex);
                    glTexSubImage2D(GL_TEXTURE_2D, 0, 0, 0, fw, fh, GL_BGR, GL_UNSIGNED_BYTE, frame);
                    dirty = false;
                }
            }
            if (draw)
            {
                float a = Aspect > 0 ? Aspect : (float)fw / fh;
                float dw = w, dh = w / a;
                if (dh > h) { dh = h; dw = h * a; }
                // scala intera (come nel disegno normale): multipli esatti delle righe del segnale
                if (IntScale && BaseLines > 0 && h >= BaseLines)
                {
                    int k = h / BaseLines;
                    while (k > 1 && k * BaseLines * a > w) k--;
                    if (k * BaseLines * a <= w) { dh = k * BaseLines; dw = (float)Math.Floor(dh * a); }
                }
                float x0 = (w - dw) / 2, y0 = (h - dh) / 2;
                float u = (float)fw / tw, v = (float)fh / th;
                glEnable(GL_TEXTURE_2D);
                glBindTexture(GL_TEXTURE_2D, tex);
                int filt = (int)(Sharp ? GL_NEAREST : GL_LINEAR);
                glTexParameteri(GL_TEXTURE_2D, GL_TEXTURE_MIN_FILTER, filt);
                glTexParameteri(GL_TEXTURE_2D, GL_TEXTURE_MAG_FILTER, filt);
                glColor4f(1, 1, 1, 1);
                glBegin(GL_QUADS);
                glTexCoord2f(0, 0); glVertex2f(x0, y0);
                glTexCoord2f(u, 0); glVertex2f(x0 + dw, y0);
                glTexCoord2f(u, v); glVertex2f(x0 + dw, y0 + dh);
                glTexCoord2f(0, v); glVertex2f(x0, y0 + dh);
                glEnd();
                glDisable(GL_TEXTURE_2D);
                if (Scanlines)
                {
                    glEnable(GL_BLEND);
                    glBlendFunc(GL_SRC_ALPHA, GL_ONE_MINUS_SRC_ALPHA);
                    glColor4f(0, 0, 0, 0.28f);
                    float step = Math.Max(2f, dh / (float)Math.Max(120, BaseLines));
                    glBegin(GL_LINES);
                    for (float y = y0 + step - 0.5f; y < y0 + dh; y += step) { glVertex2f(x0, y); glVertex2f(x0 + dw, y); }
                    glEnd();
                    glDisable(GL_BLEND);
                }
            }
            SwapBuffers(dc);
            uint err = glGetError();
            if (err != 0) Fail("errore OpenGL " + err);
        }
        catch (Exception ex) { Fail(ex.Message); }
    }

    protected override void OnResize(EventArgs e) { base.OnResize(e); Invalidate(); }

    protected override void OnHandleDestroyed(EventArgs e)
    {
        try
        {
            if (rc != IntPtr.Zero)
            {
                wglMakeCurrent(dc, rc);
                if (tex != 0) glDeleteTextures(1, ref tex);
                wglMakeCurrent(IntPtr.Zero, IntPtr.Zero);
                wglDeleteContext(rc);
                rc = IntPtr.Zero;
            }
            if (dc != IntPtr.Zero) { ReleaseDC(Handle, dc); dc = IntPtr.Zero; }
        }
        catch { }
        base.OnHandleDestroyed(e);
    }
}
