using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;

// ---------------------------------------------------------------- Lingua
public static class L
{
    public static bool En;
    public static string T(string it, string en) { return En ? en : it; }
}

// ---------------------------------------------------------------- Log
public static class Log
{
    static readonly object sync = new object();
    public static string Dir
    {
        get { return Path.Combine(Settings.AppDir, "logs"); }
    }

    public static void W(string msg)
    {
        try
        {
            lock (sync)
            {
                Directory.CreateDirectory(Dir);
                File.AppendAllText(Path.Combine(Dir, DateTime.Now.ToString("yyyy-MM-dd") + ".log"),
                    DateTime.Now.ToString("HH:mm:ss.fff") + "  " + msg + "\r\n", Encoding.UTF8);
            }
        }
        catch { }
    }
}

// ---------------------------------------------------------------- Impostazioni
public sealed class Settings
{
    public static string AppDir
    {
        get
        {
            string root = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            string dir = Path.Combine(root, "Video2All");
            if (!migrated)
            {
                migrated = true;
                // prima versione col nuovo nome: si portano impostazioni e log da "AcquisizioneVideo"
                try
                {
                    string old = Path.Combine(root, "AcquisizioneVideo");
                    if (!Directory.Exists(dir) && Directory.Exists(old))
                    {
                        Directory.CreateDirectory(dir);
                        foreach (string f in Directory.GetFiles(old, "*", SearchOption.AllDirectories))
                        {
                            string t = Path.Combine(dir, f.Substring(old.Length).TrimStart('\\', '/'));
                            Directory.CreateDirectory(Path.GetDirectoryName(t));
                            File.Copy(f, t, false);
                        }
                    }
                }
                catch { }
            }
            return dir;
        }
    }
    static bool migrated;

    readonly string path;
    public readonly Dictionary<string, string> D = new Dictionary<string, string>();

    public Settings() : this(Path.Combine(AppDir, "settings.ini")) { }

    public Settings(string file)
    {
        path = file;
        try
        {
            foreach (string l in File.ReadAllLines(path, Encoding.UTF8))
            {
                int i = l.IndexOf('=');
                if (i > 0) D[l.Substring(0, i)] = l.Substring(i + 1);
            }
        }
        catch { }
    }

    // usato anche da thread in background: ogni accesso è protetto
    public string Get(string k, string def) { string v; lock (D) return D.TryGetValue(k, out v) ? v : def; }
    public int GetInt(string k, int def) { int v; return int.TryParse(Get(k, ""), NumberStyles.Integer, CultureInfo.InvariantCulture, out v) ? v : def; }
    public decimal GetDec(string k, decimal def) { decimal v; return decimal.TryParse(Get(k, ""), NumberStyles.Number, CultureInfo.InvariantCulture, out v) ? v : def; }
    public bool GetBool(string k, bool def) { string v = Get(k, null); return v == null ? def : v == "1"; }
    public void Set(string k, object v)
    {
        if (v is bool) v = (bool)v ? "1" : "0";
        string s = Convert.ToString(v, CultureInfo.InvariantCulture) ?? "";
        lock (D) D[k] = s;
    }

    // Chiave della diretta cifrata con l'account Windows (DPAPI)
    public string GetSecret(string k)
    {
        string v = Get(k, "");
        if (v.Length == 0) return "";
        try { return Encoding.UTF8.GetString(ProtectedData.Unprotect(Convert.FromBase64String(v), null, DataProtectionScope.CurrentUser)); }
        catch { return ""; }
    }

    public void SetSecret(string k, string plain)
    {
        string v;
        try { v = plain.Length == 0 ? "" : Convert.ToBase64String(ProtectedData.Protect(Encoding.UTF8.GetBytes(plain), null, DataProtectionScope.CurrentUser)); }
        catch { v = ""; }
        lock (D) D[k] = v;
    }

    static readonly object saveLock = new object();

    public void Save()
    {
        lock (saveLock) SaveCore();
    }

    void SaveCore()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            var sb = new StringBuilder();
            lock (D) foreach (var kv in D) sb.Append(kv.Key).Append('=').Append((kv.Value ?? "").Replace("\r", "").Replace("\n", "")).Append("\r\n");
            string tmp = path + ".tmp";
            File.WriteAllText(tmp, sb.ToString(), Encoding.UTF8);
            try
            {
                if (File.Exists(path)) File.Replace(tmp, path, null);
                else File.Move(tmp, path);
            }
            catch { File.Copy(tmp, path, true); File.Delete(tmp); }
        }
        catch (Exception ex) { Log.W("Salvataggio impostazioni: " + ex.Message); }
    }
}

// ---------------------------------------------------------------- Win32
public static class Native
{
    [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
    [DllImport("user32.dll")] public static extern bool RegisterHotKey(IntPtr hWnd, int id, uint mods, uint vk);
    [DllImport("user32.dll")] public static extern bool UnregisterHotKey(IntPtr hWnd, int id);
    [DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr h);
    [DllImport("user32.dll")] static extern bool BringWindowToTop(IntPtr h);
    [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr h);
    [DllImport("user32.dll")] static extern bool IsIconic(IntPtr h);
    [DllImport("user32.dll")] static extern bool ShowWindow(IntPtr h, int cmd);
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
    delegate bool EnumProc(IntPtr h, IntPtr l);
    [DllImport("user32.dll")] static extern bool EnumWindows(EnumProc cb, IntPtr l);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern bool GetDiskFreeSpaceEx(string dir, out ulong freeForUser, out ulong total, out ulong totalFree);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct SHFILEOPSTRUCT { public IntPtr hwnd; public uint wFunc; public string pFrom, pTo; public ushort fFlags; public bool fAnyOperationsAborted; public IntPtr hNameMappings; public string lpszProgressTitle; }
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] static extern int SHFileOperation(ref SHFILEOPSTRUCT op);

    // Sposta nel Cestino
    public static bool Recycle(string file)
    {
        var op = new SHFILEOPSTRUCT();
        op.wFunc = 3;                         // FO_DELETE
        op.pFrom = file + "\0\0";
        op.fFlags = 0x40 | 0x10 | 0x400;      // ALLOWUNDO | NOCONFIRMATION | NOERRORUI
        return SHFileOperation(ref op) == 0 && !op.fAnyOperationsAborted;
    }

    [DllImport("kernel32.dll")] static extern uint SetThreadExecutionState(uint flags);

    // Impedisce la sospensione (e lo spegnimento dello schermo durante il Live)
    public static void KeepAwake(bool on, bool display)
    {
        try
        {
            const uint CONT = 0x80000000, SYS = 0x1, DISP = 0x2;
            SetThreadExecutionState(on ? (CONT | SYS | (display ? DISP : 0)) : CONT);
        }
        catch { }
    }

    // ---- Job object: i processi ffmpeg si chiudono se il programma termina (anche in crash)
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] static extern IntPtr CreateJobObject(IntPtr a, string name);
    [DllImport("kernel32.dll")] static extern bool SetInformationJobObject(IntPtr job, int cls, IntPtr info, uint len);
    [DllImport("kernel32.dll")] static extern bool AssignProcessToJobObject(IntPtr job, IntPtr proc);

    [StructLayout(LayoutKind.Sequential)]
    struct BASIC { public long PerProcessUserTimeLimit, PerJobUserTimeLimit; public uint LimitFlags; public UIntPtr MinimumWorkingSetSize, MaximumWorkingSetSize; public uint ActiveProcessLimit; public UIntPtr Affinity; public uint PriorityClass, SchedulingClass; }
    [StructLayout(LayoutKind.Sequential)]
    struct IOC { public ulong a, b, c, d, e, f; }
    [StructLayout(LayoutKind.Sequential)]
    struct EXTENDED { public BASIC Basic; public IOC Io; public UIntPtr ProcessMemoryLimit, JobMemoryLimit, PeakProcessMemoryUsed, PeakJobMemoryUsed; }

    static IntPtr job = IntPtr.Zero;
    static bool jobFailed;

    public static void AddToJob(System.Diagnostics.Process p)
    {
        try
        {
            if (jobFailed) return;
            if (job == IntPtr.Zero)
            {
                job = CreateJobObject(IntPtr.Zero, null);
                var info = new EXTENDED();
                info.Basic.LimitFlags = 0x2000;      // JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE
                int len = Marshal.SizeOf(typeof(EXTENDED));
                IntPtr ptr = Marshal.AllocHGlobal(len);
                Marshal.StructureToPtr(info, ptr, false);
                if (!SetInformationJobObject(job, 9, ptr, (uint)len)) jobFailed = true;
                Marshal.FreeHGlobal(ptr);
            }
            AssignProcessToJobObject(job, p.Handle);
        }
        catch { jobFailed = true; }
    }

    public const uint MOD_ALT = 1, MOD_CONTROL = 2, MOD_NOREPEAT = 0x4000;

    // Porta in primo piano le finestre (es. impostazioni driver) aperte da un processo
    public static int FocusProcessWindows(int pid)
    {
        int found = 0;
        EnumWindows(delegate (IntPtr h, IntPtr l)
        {
            uint p;
            GetWindowThreadProcessId(h, out p);
            if (p == (uint)pid && IsWindowVisible(h)) { if (IsIconic(h)) ShowWindow(h, 9); BringWindowToTop(h); SetForegroundWindow(h); found++; }
            return true;
        }, IntPtr.Zero);
        return found;
    }

    // Funziona anche con percorsi di rete \\server\cartella
    public static double FreeGB(string folder)
    {
        try
        {
            string dir = Path.GetFullPath(folder);
            while (!Directory.Exists(dir))
            {
                string parent = Path.GetDirectoryName(dir);
                if (parent == null || parent == dir) break;
                dir = parent;
            }
            ulong free, total, tfree;
            if (GetDiskFreeSpaceEx(dir, out free, out total, out tfree)) return free / 1073741824.0;
        }
        catch { }
        return -1;
    }
}

// ---------------------------------------------------------------- Uscita audio (waveOut)
public sealed class WavePlayer : IDisposable
{
    [StructLayout(LayoutKind.Sequential)]
    struct WAVEFORMATEX { public ushort wFormatTag, nChannels; public uint nSamplesPerSec, nAvgBytesPerSec; public ushort nBlockAlign, wBitsPerSample, cbSize; }
    [StructLayout(LayoutKind.Sequential)]
    struct WAVEHDR { public IntPtr lpData; public uint dwBufferLength, dwBytesRecorded; public IntPtr dwUser; public uint dwFlags, dwLoops; public IntPtr lpNext, reserved; }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct WAVEOUTCAPS { public ushort wMid, wPid; public uint vDriverVersion; [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string szPname; public uint dwFormats; public ushort wChannels, wReserved1; public uint dwSupport; }

    [DllImport("winmm.dll")] static extern int waveOutOpen(out IntPtr h, int dev, ref WAVEFORMATEX f, IntPtr cb, IntPtr inst, int flags);
    [DllImport("winmm.dll")] static extern int waveOutPrepareHeader(IntPtr h, IntPtr hdr, int size);
    [DllImport("winmm.dll")] static extern int waveOutUnprepareHeader(IntPtr h, IntPtr hdr, int size);
    [DllImport("winmm.dll")] static extern int waveOutWrite(IntPtr h, IntPtr hdr, int size);
    [DllImport("winmm.dll")] static extern int waveOutReset(IntPtr h);
    [DllImport("winmm.dll")] static extern int waveOutClose(IntPtr h);
    [DllImport("winmm.dll")] static extern int waveOutGetNumDevs();
    [DllImport("winmm.dll", CharSet = CharSet.Unicode)] static extern int waveOutGetDevCapsW(IntPtr id, ref WAVEOUTCAPS caps, int size);

    public static List<string> Devices()
    {
        var list = new List<string>();
        try
        {
            int n = waveOutGetNumDevs();
            for (int i = 0; i < n; i++)
            {
                var c = new WAVEOUTCAPS();
                if (waveOutGetDevCapsW(new IntPtr(i), ref c, Marshal.SizeOf(typeof(WAVEOUTCAPS))) == 0) list.Add(c.szPname);
                else list.Add("#" + i);
            }
        }
        catch { }
        return list;
    }

    const int WHDR_DONE = 1;
    IntPtr h;
    readonly IntPtr[] hdrs, bufs;
    readonly bool[] used;
    readonly int hdrSize, flagsOff, lenOff, bufLen;
    readonly object sync = new object();
    bool disposed;

    public WavePlayer(int device, int bufLen, int count)
    {
        this.bufLen = bufLen;
        var f = new WAVEFORMATEX();
        f.wFormatTag = 1; f.nChannels = 2; f.nSamplesPerSec = 48000; f.nAvgBytesPerSec = 192000; f.nBlockAlign = 4; f.wBitsPerSample = 16;
        if (waveOutOpen(out h, device, ref f, IntPtr.Zero, IntPtr.Zero, 0) != 0) throw new Exception("waveOutOpen");
        hdrSize = Marshal.SizeOf(typeof(WAVEHDR));
        flagsOff = (int)Marshal.OffsetOf(typeof(WAVEHDR), "dwFlags");
        lenOff = (int)Marshal.OffsetOf(typeof(WAVEHDR), "dwBufferLength");
        hdrs = new IntPtr[count]; bufs = new IntPtr[count]; used = new bool[count];
        for (int i = 0; i < count; i++)
        {
            bufs[i] = Marshal.AllocHGlobal(bufLen);
            hdrs[i] = Marshal.AllocHGlobal(hdrSize);
            var w = new WAVEHDR();
            w.lpData = bufs[i]; w.dwBufferLength = (uint)bufLen;
            Marshal.StructureToPtr(w, hdrs[i], false);
            waveOutPrepareHeader(h, hdrs[i], hdrSize);
        }
    }

    public int Queued
    {
        get
        {
            lock (sync)
            {
                if (disposed) return 0;
                int q = 0;
                for (int i = 0; i < hdrs.Length; i++)
                    if (used[i] && (Marshal.ReadInt32(hdrs[i], flagsOff) & WHDR_DONE) == 0) q++;
                return q;
            }
        }
    }

    public void Write(byte[] data, int len)
    {
        if (len > bufLen) len = bufLen;
        lock (sync)
        {
            if (disposed) return;
            for (int i = 0; i < hdrs.Length; i++)
            {
                int flags = Marshal.ReadInt32(hdrs[i], flagsOff);
                if (used[i] && (flags & WHDR_DONE) == 0) continue;
                Marshal.Copy(data, 0, bufs[i], len);
                Marshal.WriteInt32(hdrs[i], lenOff, len);
                Marshal.WriteInt32(hdrs[i], flagsOff, flags & ~WHDR_DONE);
                used[i] = true;
                waveOutWrite(h, hdrs[i], hdrSize);
                return;
            }
        }
    }

    public void Dispose()
    {
        lock (sync)
        {
            if (disposed) return;
            disposed = true;
            waveOutReset(h);
            for (int i = 0; i < hdrs.Length; i++)
            {
                waveOutUnprepareHeader(h, hdrs[i], hdrSize);
                Marshal.FreeHGlobal(hdrs[i]);
                Marshal.FreeHGlobal(bufs[i]);
            }
            waveOutClose(h);
        }
    }
}
