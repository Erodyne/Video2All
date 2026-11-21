using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

// Taglio "intelligente": ricodifica solo i pezzi prima del primo e dopo l'ultimo fotogramma chiave,
// copia senza perdita la parte centrale. Solo H.264; per gli altri formati si usa il taglio preciso.
public static class SmartTrim
{
    public sealed class Info { public string Codec, Profile, PixFmt, Fps, Field; public bool HasAudio; public double Duration; }

    public static Info Probe(string ffmpeg, string file)
    {
        string e;
        Tools.Run(ffmpeg, "-hide_banner -i \"" + file + "\"", out e, 30000);
        e = e ?? "";
        var i = new Info();
        Match v = Regex.Match(e, @"Stream #0:\d+[^:]*: Video: (\w+)(?: \(([^)]*)\))?[^,]*, (\w+)(\(([^)]*)\))?");
        if (!v.Success) return null;
        i.Codec = v.Groups[1].Value;
        i.Profile = v.Groups[2].Value;
        i.PixFmt = v.Groups[3].Value;
        string extra = v.Groups[5].Value;
        if (extra.Contains("top first")) i.Field = "tff"; else if (extra.Contains("bottom first")) i.Field = "bff";
        Match f = Regex.Match(e, @"Video:.*?, ([\d.]+) fps");
        // "fps" è la frequenza dei fotogrammi; "tbr" serve solo se manca (nei file interlacciati può valere il doppio)
        if (!f.Success) f = Regex.Match(e, @"Video:.*?, ([\d.]+) tbr");
        if (f.Success) i.Fps = f.Groups[1].Value;
        i.HasAudio = Regex.IsMatch(e, @"Stream #0:\d+[^:]*: Audio:");
        Match d = Regex.Match(e, @"Duration:\s*(\d+):(\d+):([\d.]+)");
        if (d.Success) i.Duration = int.Parse(d.Groups[1].Value) * 3600 + int.Parse(d.Groups[2].Value) * 60 + double.Parse(d.Groups[3].Value, CultureInfo.InvariantCulture);
        return i;
    }

    // tempi dei fotogrammi chiave (decodifica solo quelli)
    public static List<double> Keyframes(string ffmpeg, string file)
    {
        string e;
        Tools.Run(ffmpeg, "-hide_banner -loglevel info -skip_frame nokey -i \"" + file + "\" -map 0:v:0 -an -sn -vf showinfo -f null -", out e, 2 * 3600 * 1000);
        var list = new List<double>();
        foreach (Match m in Regex.Matches(e ?? "", @"pts_time:\s*(-?[\d.]+)"))
        {
            double t;
            if (double.TryParse(m.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out t)) list.Add(t);
        }
        list.Sort();
        return list;
    }

    static string X264(Info i)
    {
        var sb = new StringBuilder(" -c:v libx264 -preset medium -crf 15");
        string p = (i.Profile ?? "").ToLowerInvariant();
        if (p.StartsWith("high 4:2:2")) sb.Append(" -profile:v high422");
        else if (p.StartsWith("high 4:4:4")) sb.Append(" -profile:v high444");
        else if (p.StartsWith("high")) sb.Append(" -profile:v high");
        else if (p.StartsWith("main")) sb.Append(" -profile:v main");
        if (!string.IsNullOrEmpty(i.PixFmt)) sb.Append(" -pix_fmt " + i.PixFmt);
        sb.Append(" -fps_mode passthrough");
        if (i.Field != null) sb.Append(" -flags +ildct+ilme -x264opts " + (i.Field == "tff" ? "tff=1" : "bff=1"));
        return sb.ToString();
    }

    static string T(double s) { return s.ToString("0.000###", CultureInfo.InvariantCulture); }

    // Restituisce null se riuscito, altrimenti il motivo (il chiamante passa al taglio preciso)
    public static string Run(string ffmpeg, string src, double a, double b, string dst, Action<string> progress)
    {
        Info info = Probe(ffmpeg, src);
        if (info == null) return "formato non riconosciuto";
        if (info.Codec != "h264") return "solo H.264";
        if (b <= 0 || b > info.Duration) b = info.Duration;
        if (b - a < 1) return "pezzo troppo corto";
        progress("fotogrammi chiave");
        List<double> keys = Keyframes(ffmpeg, src);
        if (keys.Count < 2) return "fotogrammi chiave non trovati";
        const double eps = 0.02;
        double k1 = -1, k2 = -1;
        foreach (double k in keys) if (k >= a - eps) { k1 = k; break; }
        for (int j = keys.Count - 1; j >= 0; j--) if (keys[j] <= b + eps) { k2 = keys[j]; break; }
        bool toEnd = b >= info.Duration - eps;
        if (toEnd) k2 = b;
        if (k1 < 0 || k2 <= k1 + 0.5) return "nessun tratto copiabile";

        string tmp = Path.Combine(Path.GetDirectoryName(dst), "~taglio_" + Guid.NewGuid().ToString("N").Substring(0, 8));
        Directory.CreateDirectory(tmp);
        try
        {
            var parts = new List<string>();
            string err;
            string enc = X264(info);
            double fps = 25;
            double.TryParse(info.Fps ?? "25", NumberStyles.Float, CultureInfo.InvariantCulture, out fps);
            if (fps < 1 || fps > 300) fps = 25;
            if (k1 - a > eps)
            {
                progress("inizio");
                string head = Path.Combine(tmp, "1.ts");
                if (Tools.Run(ffmpeg, "-y -hide_banner -loglevel error -ss " + T(a) + " -i \"" + src + "\" -frames:v " + Math.Max(1, (int)Math.Round(k1 * fps) - (int)Math.Ceiling(a * fps - 1e-3)) + " -map 0:v:0 -an -sn" + enc + " -f mpegts \"" + head + "\"", out err, 3600 * 1000) != 0) return "inizio: " + err;
                parts.Add(head);
            }
            progress("parte centrale");
            string mid = Path.Combine(tmp, "2.ts");
            // ricerca approssimata prima di -i, poi esatta dopo -i (alcuni contenitori atterrano su un fotogramma chiave precedente)
            double coarse = Math.Max(0, k1 - 20);
            // soglia un po' prima di k1 (il confronto avviene sul tempo di decodifica) ma dopo il fotogramma chiave precedente
            double prev = -1;
            foreach (double k in keys) if (k < k1 - eps) prev = k;
            double cut = Math.Max(prev + 0.01, k1 - 0.25);
            string midArgs = "-y -hide_banner -loglevel error" + (coarse > 0 ? " -ss " + T(coarse) : "") + " -i \"" + src + "\" -ss " + T(Math.Max(0, cut - coarse)) + (toEnd ? "" : " -frames:v " + (int)Math.Round((k2 - k1) * fps)) +
                " -map 0:v:0 -an -sn -c:v copy -bsf:v h264_mp4toannexb -f mpegts \"" + mid + "\"";
            if (Tools.Run(ffmpeg, midArgs, out err, 4 * 3600 * 1000) != 0) return "centro: " + err;
            parts.Add(mid);
            if (!toEnd && b - k2 > eps)
            {
                progress("fine");
                string tail = Path.Combine(tmp, "3.ts");
                if (Tools.Run(ffmpeg, "-y -hide_banner -loglevel error -ss " + T(k2) + " -i \"" + src + "\" -frames:v " + Math.Max(1, (int)Math.Ceiling(b * fps - 1e-3) - (int)Math.Round(k2 * fps)) + " -map 0:v:0 -an -sn" + enc + " -f mpegts \"" + tail + "\"", out err, 3600 * 1000) != 0) return "fine: " + err;
                parts.Add(tail);
            }
            string list = Path.Combine(tmp, "lista.txt");
            var sb = new StringBuilder();
            foreach (string p in parts) sb.Append("file '").Append(p.Replace("'", "'\\''")).Append("'\n");
            File.WriteAllText(list, sb.ToString());

            string aud = null;
            if (info.HasAudio)
            {
                progress("audio");
                // -ss dopo -i: in copia l'audio viene tagliato al pacchetto, non al fotogramma chiave video
                aud = Path.Combine(tmp, "audio.mka");
                if (Tools.Run(ffmpeg, "-y -hide_banner -loglevel error -i \"" + src + "\" -ss " + T(a) + " -t " + T(b - a) + " -map 0:a -vn -sn -c:a copy \"" + aud + "\"", out err, 3600 * 1000) != 0)
                    return "audio: " + err;
            }
            progress("unione");
            string ext = Path.GetExtension(dst).ToLowerInvariant();
            bool mp4 = ext == ".mp4" || ext == ".mov" || ext == ".m4v";
            string args = "-y -hide_banner -loglevel error -f concat -safe 0 -i \"" + list + "\"" +
                (aud != null ? " -i \"" + aud + "\"" : "") +
                " -map 0:v:0" + (aud != null ? " -map 1:a" : "") +
                " -c copy" + (mp4 ? " -tag:v avc3 -movflags +faststart" : "") + " -avoid_negative_ts make_zero \"" + dst + "\"";
            if (Tools.Run(ffmpeg, args, out err, 4 * 3600 * 1000) != 0) { TryDelete(dst); return "unione: " + err; }

            progress("verifica");
            string bad = Tools.Verify(ffmpeg, dst);
            double got = Tools.Duration(ffmpeg, dst);
            if (bad != null) { TryDelete(dst); return "verifica: " + bad; }
            if (Math.Abs(got - (b - a)) > 0.6) { TryDelete(dst); return "durata " + T(got) + " invece di " + T(b - a); }
            return null;
        }
        finally { try { Directory.Delete(tmp, true); } catch { } }
    }

    static void TryDelete(string f) { try { if (File.Exists(f)) File.Delete(f); } catch { } }
}
