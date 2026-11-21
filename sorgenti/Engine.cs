using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.IO.Pipes;
using System.Net;
using System.Net.Sockets;

// ---------------------------------------------------------------- Canale interno ffmpeg -> programma (named pipe o TCP locale)
public sealed class Endpoint : IDisposable
{
    NamedPipeServerStream pipe;
    TcpListener tcp;
    TcpClient client;
    string pipeName;
    public string Target;
    volatile bool disposed;

    public static Endpoint Create(bool useTcp, string name, int bufSize)
    {
        var e = new Endpoint();
        if (useTcp)
        {
            e.tcp = new TcpListener(IPAddress.Loopback, 0);
            e.tcp.Start(1);
            e.Target = "tcp://127.0.0.1:" + ((IPEndPoint)e.tcp.LocalEndpoint).Port;
        }
        else
        {
            e.pipeName = name;
            e.pipe = new NamedPipeServerStream(name, PipeDirection.In, 1, PipeTransmissionMode.Byte, PipeOptions.None, bufSize, 0);
            e.Target = "\\\\.\\pipe\\" + name;
        }
        return e;
    }

    public Stream Accept()
    {
        if (pipe != null) { pipe.WaitForConnection(); return pipe; }
        client = tcp.AcceptTcpClient();
        client.ReceiveBufferSize = 1 << 20;
        client.NoDelay = true;
        return client.GetStream();
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        if (pipe != null)
        {
            bool connected = true;
            try { connected = pipe.IsConnected; } catch { }
            if (!connected)
                try { using (var c = new NamedPipeClientStream(".", pipeName, PipeDirection.Out)) c.Connect(150); } catch { }
            try { pipe.Dispose(); } catch { }
        }
        if (tcp != null) { try { tcp.Stop(); } catch { } }
        if (client != null) { try { client.Close(); } catch { } }
    }
}

// ---------------------------------------------------------------- Processo che riceve il flusso intermedio
public sealed class Consumer
{
    public readonly Process P;
    public readonly string Name;
    public readonly DateTime Started = DateTime.Now;
    public volatile bool StoppedByUser;
    public long Dropped, Written;
    public volatile float Speed, BitrateKbps;
    readonly BlockingCollection<byte[]> q = new BlockingCollection<byte[]>(768);
    readonly StringBuilder err = new StringBuilder();
    public event Action<Consumer> Exited;

    public string File;

    readonly string[] secrets;

    // Parti segrete di un indirizzo di diretta: chiave (ultimo pezzo del percorso) e parametri (?streamid=, passphrase=)
    public static string[] UrlSecrets(string url)
    {
        var list = new List<string>();
        if (string.IsNullOrEmpty(url)) return list.ToArray();
        int q = url.IndexOf('?');
        if (q >= 0 && q < url.Length - 1) list.Add(url.Substring(q + 1));
        string path = (q >= 0 ? url.Substring(0, q) : url).TrimEnd('/');      // anche con la barra finale
        int slash = path.LastIndexOf('/');
        if (slash > 8 && slash < path.Length - 1) list.Add(path.Substring(slash + 1));
        return list.ToArray();
    }

    string Mask(string s)
    {
        if (secrets == null || s == null) return s;
        foreach (string k in secrets) if (k.Length >= 4) s = s.Replace(k, "***");
        return s;
    }

    public Consumer(string ffmpeg, string args, string name, string[] secretParts)
    {
        Name = name;
        secrets = secretParts;
        Log.W(name + ": ffmpeg " + Mask(args));
        var psi = new ProcessStartInfo(ffmpeg, args);
        psi.UseShellExecute = false; psi.CreateNoWindow = true;
        psi.RedirectStandardInput = true; psi.RedirectStandardError = true; psi.RedirectStandardOutput = true;
        psi.StandardErrorEncoding = Encoding.UTF8;
        P = new Process { StartInfo = psi, EnableRaisingEvents = true };
        P.ErrorDataReceived += delegate (object s, DataReceivedEventArgs e)
        {
            if (e.Data == null) return;
            if (e.Data.Contains("speed=") && e.Data.Contains("frame="))
            {
                Match ms = Regex.Match(e.Data, @"speed=\s*([\d.]+)x");
                Match mb = Regex.Match(e.Data, @"bitrate=\s*([\d.]+)kbits");
                float v;
                if (ms.Success && float.TryParse(ms.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out v)) Speed = v;
                if (mb.Success && float.TryParse(mb.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out v)) BitrateKbps = v;
                return;
            }
            lock (err) { err.AppendLine(e.Data); if (err.Length > 16000) err.Remove(0, 8000); }
        };
        P.Exited += delegate
        {
            try { q.CompleteAdding(); } catch { }
            Log.W(Name + ": terminato, codice " + SafeExit() + (StoppedByUser ? " (fermato)" : "") + "\r\n" + Error);
            var h = Exited;
            if (h != null) h(this);
        };
    }

    // Da chiamare dopo aver collegato l'evento Exited
    public void Start()
    {
        P.Start();
        Native.AddToJob(P);
        P.BeginErrorReadLine();
        P.StandardOutput.BaseStream.CopyToAsync(Stream.Null);
        var t = new Thread(Writer) { IsBackground = true, Name = Name };
        t.Start();
    }

    public string Error { get { lock (err) return Mask(err.ToString().Trim()); } }

    public int SafeExit() { try { return P.ExitCode; } catch { return -1; } }

    void Writer()
    {
        Stream s = P.StandardInput.BaseStream;
        try
        {
            foreach (byte[] c in q.GetConsumingEnumerable())
            {
                s.Write(c, 0, c.Length);
                Written += c.Length;
            }
        }
        catch { }
        finally { try { s.Close(); } catch { } }
    }

    public void Feed(byte[] chunk)
    {
        try { if (!q.TryAdd(chunk)) Interlocked.Increment(ref Dropped); }
        catch { }
    }

    // Chiusura pulita: chiude l'ingresso, ffmpeg finalizza il file
    public void Finish() { StoppedByUser = true; try { q.CompleteAdding(); } catch { } }

    public void Kill() { StoppedByUser = true; try { q.CompleteAdding(); } catch { } try { if (!P.HasExited) P.Kill(); } catch { } }

    public bool HasExited { get { try { return P.HasExited; } catch { return true; } } }
}

// ---------------------------------------------------------------- Replay buffer (ultimi N secondi)
public sealed class ReplayRing
{
    // tempo monotono: non risente dei cambi d'ora del PC
    readonly LinkedList<KeyValuePair<long, byte[]>> items = new LinkedList<KeyValuePair<long, byte[]>>();
    static long NowMs() { return Stopwatch.GetTimestamp() * 1000 / Stopwatch.Frequency; }
    long bytes;
    public int Seconds = 30;
    const long MaxBytes = 600L * 1024 * 1024;

    public void Add(byte[] chunk)
    {
        lock (items)
        {
            long now = NowMs();
            items.AddLast(new KeyValuePair<long, byte[]>(now, chunk));
            bytes += chunk.Length;
            while (items.Count > 0 && ((now - items.First.Value.Key) > Seconds * 1000L || bytes > MaxBytes))
            {
                bytes -= items.First.Value.Value.Length;
                items.RemoveFirst();
            }
        }
    }

    public void Clear() { lock (items) { items.Clear(); bytes = 0; } }

    // Blocchi degli ultimi "sec" secondi (per la pre-registrazione)
    public List<byte[]> Snapshot(int sec)
    {
        var list = new List<byte[]>();
        lock (items)
        {
            long from = NowMs() - sec * 1000L;
            foreach (var kv in items) if (kv.Key >= from) list.Add(kv.Value);
        }
        return list;
    }

    public long Bytes { get { lock (items) return bytes; } }

    // si copia l'elenco e si scrive fuori dal blocco: la cattura non si ferma durante il salvataggio
    public bool SaveTo(string file)
    {
        var copy = new List<byte[]>();
        lock (items) foreach (var kv in items) copy.Add(kv.Value);
        if (copy.Count == 0) return false;
        using (var fs = new FileStream(file, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 20))
            foreach (byte[] b in copy) fs.Write(b, 0, b.Length);
        return true;
    }
}

// ---------------------------------------------------------------- Strumenti ffmpeg (in background)
public static class Tools
{
    public static int Run(string ffmpeg, string args, out string stderr, int timeoutMs) { return Run(ffmpeg, args, out stderr, timeoutMs, true); }

    public static int Run(string ffmpeg, string args, out string stderr, int timeoutMs, bool lowPriority)
    {
        args = "-nostdin " + args;
        Log.W("Strumento: ffmpeg " + args);
        var psi = new ProcessStartInfo(ffmpeg, args);
        psi.UseShellExecute = false; psi.CreateNoWindow = true;
        psi.RedirectStandardError = true; psi.RedirectStandardOutput = true; psi.RedirectStandardInput = true;
        psi.StandardErrorEncoding = Encoding.UTF8;
        using (var p = Process.Start(psi))
        {
            Native.AddToJob(p);
            // priorità bassa: i lavori sui file non devono togliere CPU alla cattura dal vivo
            if (lowPriority) try { p.PriorityClass = ProcessPriorityClass.BelowNormal; } catch { }
            var errTask = p.StandardError.ReadToEndAsync();
            p.StandardOutput.BaseStream.CopyToAsync(Stream.Null);
            if (!p.WaitForExit(timeoutMs)) { try { p.Kill(); } catch { } stderr = "timeout"; return -1; }
            p.WaitForExit();
            stderr = errTask.Result;
            return p.ExitCode;
        }
    }

    public static bool EncoderWorks(string ffmpeg, string enc)
    {
        string e;
        try { return Run(ffmpeg, "-hide_banner -loglevel error -f lavfi -i color=black:s=320x240:r=25:d=0.4 -c:v " + enc + " -f null -", out e, 20000) == 0; }
        catch { return false; }
    }

    public static string Version(string ffmpeg)
    {
        try
        {
            var psi = new ProcessStartInfo(ffmpeg, "-version") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true };
            using (var p = Process.Start(psi))
            {
                string first = p.StandardOutput.ReadLine() ?? "";
                p.StandardOutput.ReadToEnd();
                p.WaitForExit(5000);
                Match m = Regex.Match(first, @"version\s+(\S+)");
                return m.Success ? m.Groups[1].Value : "?";
            }
        }
        catch { return "?"; }
    }

    public static bool Thumb(string ffmpeg, string file, string outJpg)
    {
        string e;
        string vf = "scale=160:120:force_original_aspect_ratio=decrease,pad=160:120:(ow-iw)/2:(oh-ih)/2";
        try { if (File.Exists(outJpg)) File.Delete(outJpg); } catch { }
        if (Run(ffmpeg, "-y -hide_banner -loglevel error -ss 2 -i \"" + file + "\" -frames:v 1 -vf " + vf + " \"" + outJpg + "\"", out e, 30000) == 0 && File.Exists(outJpg)) return true;
        return Run(ffmpeg, "-y -hide_banner -loglevel error -i \"" + file + "\" -frames:v 1 -vf " + vf + " \"" + outJpg + "\"", out e, 30000) == 0 && File.Exists(outJpg);
    }

    public static string Ts(double s) { return s.ToString("0.###", CultureInfo.InvariantCulture); }

    public static bool ParseTime(string t, out double sec)
    {
        sec = 0;
        t = (t ?? "").Trim().Replace(',', '.');
        if (t.Length == 0) return false;
        string[] parts = t.Split(':');
        if (parts.Length > 3) return false;
        double acc = 0;
        foreach (string p in parts)
        {
            double v;
            if (!double.TryParse(p, NumberStyles.Float, CultureInfo.InvariantCulture, out v) || v < 0) return false;
            acc = acc * 60 + v;
        }
        sec = acc;
        return true;
    }

    public static double Duration(string ffmpeg, string file)
    {
        string e;
        try { Run(ffmpeg, "-hide_banner -i \"" + file + "\"", out e, 30000); } catch { return 0; }
        Match m = Regex.Match(e ?? "", @"Duration:\s*(\d+):(\d+):([\d.]+)");
        if (!m.Success) return 0;
        return int.Parse(m.Groups[1].Value) * 3600 + int.Parse(m.Groups[2].Value) * 60 + double.Parse(m.Groups[3].Value, CultureInfo.InvariantCulture);
    }

    // Decodifica completa: nessun errore = file integro
    public static string Verify(string ffmpeg, string file)
    {
        string e;
        int code = Run(ffmpeg, "-hide_banner -v error -i \"" + file + "\" -map 0 -f null -", out e, 12 * 3600 * 1000);
        e = (e ?? "").Trim();
        return code == 0 && e.Length == 0 ? null : (e.Length > 0 ? e : "codice " + code);
    }

    public static string Md5(string file)
    {
        using (var md5 = System.Security.Cryptography.MD5.Create())
        using (var fs = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 20))
        {
            byte[] h = md5.ComputeHash(fs);
            var sb = new StringBuilder();
            foreach (byte b in h) sb.Append(b.ToString("x2"));
            return sb.ToString();
        }
    }

    // Capitoli dai marcatori: "hh:mm:ss testo" per riga
    public static string ChaptersMeta(string markersFile, double duration, double offsetSec)
    {
        var starts = new List<KeyValuePair<double, string>>();
        foreach (string l in File.ReadAllLines(markersFile))
        {
            string t = l.Trim().TrimStart('\uFEFF');
            if (t.Length < 8) continue;
            double sec;
            if (!ParseTime(t.Substring(0, 8), out sec)) continue;
            string title = t.Length > 8 ? t.Substring(8).Trim() : "";
            if (sec + offsetSec < -0.5) continue;                     // marcatore di una parte precedente
            double at = Math.Max(0, sec + offsetSec);
            if (duration > 0 && at >= duration - 0.5) continue;      // oltre la fine del file (es. parti successive)
            starts.Add(new KeyValuePair<double, string>(at, title));
        }
        if (starts.Count == 0) return null;
        starts.Sort(delegate (KeyValuePair<double, string> a, KeyValuePair<double, string> b) { return a.Key.CompareTo(b.Key); });
        if (starts[0].Key > 1) starts.Insert(0, new KeyValuePair<double, string>(0, L.T("Inizio", "Start")));
        var sb = new StringBuilder(";FFMETADATA1\n");
        for (int i = 0; i < starts.Count; i++)
        {
            long a = (long)(starts[i].Key * 1000);
            long b = i + 1 < starts.Count ? (long)(starts[i + 1].Key * 1000) : (long)(Math.Max(duration, starts[i].Key + 1) * 1000);
            if (b <= a) b = a + 1;
            sb.Append("[CHAPTER]\nTIMEBASE=1/1000\nSTART=").Append(a).Append("\nEND=").Append(b).Append("\ntitle=")
              .Append(MetaEsc(starts[i].Value.Length > 0 ? starts[i].Value : L.T("Capitolo ", "Chapter ") + (i + 1))).Append("\n");
        }
        return sb.ToString();
    }

    // caratteri speciali del formato FFMETADATA: \ = ; # e a capo
    static string MetaEsc(string s)
    {
        return s.Replace("\\", "\\\\").Replace("=", "\\=").Replace(";", "\\;").Replace("#", "\\#").Replace("\r", "").Replace("\n", " ");
    }

    public static List<double> BlackCuts(string ffmpeg, string file, out string error)
    {
        var cuts = new List<double>();
        string e;
        int code = Run(ffmpeg, "-hide_banner -nostats -i \"" + file + "\" -vf blackdetect=d=1:pix_th=0.10 -an -f null -", out e, 6 * 3600 * 1000);
        error = code == 0 ? null : e;
        foreach (Match m in Regex.Matches(e ?? "", @"black_start:\s*([\d.]+)\s+black_end:\s*([\d.]+)"))
        {
            double a = double.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
            double b = double.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture);
            double mid = (a + b) / 2;
            if (mid > 5 && (cuts.Count == 0 || mid - cuts[cuts.Count - 1] > 10)) cuts.Add(mid);
        }
        return cuts;
    }
}
