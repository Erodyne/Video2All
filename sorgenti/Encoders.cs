using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

// ---------------------------------------------------------------- Catalogo encoder e contenitori
public enum Fam { X264, X265, Nvenc, Qsv, Amf, Aom, Lossless }

public sealed class VCodec
{
    public string Label, Name;
    public Fam Family;
    public string Kind;          // "h264", "hevc", "av1", "lossless"
    public bool Hardware;
    public VCodec(string label, string name, Fam f, string kind, bool hw) { Label = label; Name = name; Family = f; Kind = kind; Hardware = hw; }
    public string LabelL
    {
        get
        {
            if (!L.En) return Label;
            return Label.Replace("Senza perdita", "Lossless").Replace("(archivio)", "(archive)").Replace("(lento)", "(slow)");
        }
    }
}

public sealed class EncSettings
{
    public int Codec;            // indice in Enc.Codecs
    public int Rate;             // 0 qualità costante, 1 VBR, 2 CBR
    public int Quality = 20;     // CRF / CQ / QP
    public int Bitrate = 8000;   // kbps
    public int KeySec = 2;
    public string Preset, Profile, Tune;
    public int AudioCodec;       // 0 AAC, 1 Opus, 2 FLAC, 3 PCM, 4 MP3
    public int AudioKbps = 192;
    public int OutSize;          // 0 originale, 1 960p, 2 1080p
    public bool Interlaced;      // flusso con campi originali
    public string Aspect;        // "4:3" ecc. se proporzioni nei metadati
    public bool Pal = true;      // per i metadati colore
    public bool Audio24;         // PCM/FLAC a 24 bit
    public string SquareScale;   // diretta: filtro per rendere quadrati i pixel (le piattaforme ignorano le proporzioni nei metadati)
}

public static class Enc
{
    public static readonly VCodec[] Codecs =
    {
        new VCodec("H.264 - CPU (x264)", "libx264", Fam.X264, "h264", false),
        new VCodec("H.264 - NVIDIA", "h264_nvenc", Fam.Nvenc, "h264", true),
        new VCodec("H.264 - Intel", "h264_qsv", Fam.Qsv, "h264", true),
        new VCodec("H.264 - AMD", "h264_amf", Fam.Amf, "h264", true),
        new VCodec("H.265/HEVC - CPU (x265)", "libx265", Fam.X265, "hevc", false),
        new VCodec("H.265/HEVC - NVIDIA", "hevc_nvenc", Fam.Nvenc, "hevc", true),
        new VCodec("H.265/HEVC - Intel", "hevc_qsv", Fam.Qsv, "hevc", true),
        new VCodec("H.265/HEVC - AMD", "hevc_amf", Fam.Amf, "hevc", true),
        new VCodec("AV1 - CPU (lento)", "libaom-av1", Fam.Aom, "av1", false),
        new VCodec("AV1 - NVIDIA (RTX 40+)", "av1_nvenc", Fam.Nvenc, "av1", true),
        new VCodec("AV1 - Intel (Arc)", "av1_qsv", Fam.Qsv, "av1", true),
        new VCodec("AV1 - AMD (RX 7000+)", "av1_amf", Fam.Amf, "av1", true),
        new VCodec("Senza perdita - FFV1 (archivio)", "ffv1", Fam.Lossless, "lossless", false),
        new VCodec("Senza perdita - UT Video", "utvideo", Fam.Lossless, "lossless", false),
        new VCodec("Senza perdita - HuffYUV", "ffvhuff", Fam.Lossless, "lossless", false),
        new VCodec("Senza perdita - H.264 lossless", "libx264", Fam.Lossless, "lossless", false),
    };

    public const int LosslessX264 = 15;

    // ordine dei campi del segnale (dall'analisi): quasi sempre "prima il superiore", alcuni grabber NTSC il contrario
    public static bool BottomFirst;

    // Contenitori video
    public static readonly string[] Containers = { "MKV (consigliato)", "MP4", "MP4 frammentato (resiste ai crash)", "MOV", "AVI", "TS" };
    public static readonly string[] ContainerExt = { ".mkv", ".mp4", ".mp4", ".mov", ".avi", ".ts" };
    // Solo audio
    public static readonly string[] AudioOnly = { "WAV", "FLAC", "MP3", "M4A (AAC)", "OGG (Opus)", "WMA" };
    public static readonly string[] AudioOnlyExt = { ".wav", ".flac", ".mp3", ".m4a", ".ogg", ".wma" };
    public static readonly string[] AudioCodecs = { "AAC", "Opus", "FLAC", "PCM (senza perdita)", "MP3" };

    public static string[] Presets(Fam f)
    {
        switch (f)
        {
            case Fam.X264:
            case Fam.X265: return new[] { "ultrafast", "superfast", "veryfast", "faster", "fast", "medium", "slow", "slower", "veryslow" };
            case Fam.Nvenc: return new[] { "p1", "p2", "p3", "p4", "p5", "p6", "p7" };
            case Fam.Qsv: return new[] { "veryfast", "faster", "fast", "medium", "slow", "slower", "veryslow" };
            case Fam.Amf: return new[] { "speed", "balanced", "quality" };
            case Fam.Aom: return new[] { "10", "9", "8", "7", "6", "5", "4" };
            default: return new string[0];
        }
    }

    public static string DefaultPreset(Fam f)
    {
        switch (f)
        {
            case Fam.X264: case Fam.X265: return "veryfast";
            case Fam.Nvenc: return "p5";
            case Fam.Qsv: return "medium";
            case Fam.Amf: return "balanced";
            case Fam.Aom: return "8";
            default: return null;
        }
    }

    public static string[] Profiles(VCodec c)
    {
        if (c.Family == Fam.Lossless || c.Family == Fam.Aom) return new[] { "auto" };
        if (c.Kind == "h264") return c.Family == Fam.X264 ? new[] { "auto", "baseline", "main", "high", "high422" } : new[] { "auto", "baseline", "main", "high" };
        if (c.Kind == "hevc") return c.Family == Fam.X265 ? new[] { "auto", "main", "main10", "main422-10" } : new[] { "auto", "main", "main10" };
        return new[] { "auto" };
    }

    public static string[] Tunes(VCodec c)
    {
        switch (c.Family)
        {
            case Fam.X264: return new[] { "nessuno", "film", "grain", "animation", "fastdecode", "zerolatency" };
            case Fam.X265: return new[] { "nessuno", "grain", "animation", "fastdecode", "zerolatency" };
            case Fam.Nvenc: return new[] { "hq", "ll", "ull" };
            default: return new[] { "nessuno" };
        }
    }

    public static bool IsLossless(int codec) { return Codecs[codec].Family == Fam.Lossless; }

    // Parametri video dell'encoder
    public static string Video(EncSettings s, int fps, bool live)
    {
        VCodec c = Codecs[s.Codec];
        var sb = new StringBuilder();
        int B = Math.Max(300, s.Bitrate), q = Math.Max(0, Math.Min(51, s.Quality));
        string pix = "yuv420p";
        string prof = s.Profile == null || s.Profile == "auto" ? null : s.Profile;
        if (prof == "high422" || prof == "main422-10") pix = prof == "high422" ? "yuv422p" : "yuv422p10le";
        else if (prof == "main10") pix = c.Hardware ? "p010le" : "yuv420p10le";

        if (c.Family == Fam.Lossless)
        {
            if (s.Codec == LosslessX264) sb.Append(" -c:v libx264 -preset veryfast -qp 0 -pix_fmt yuv422p");
            else if (c.Name == "ffv1") sb.Append(" -c:v ffv1 -level 3 -g 1 -slices 16 -slicecrc 1 -pix_fmt yuv422p");
            else if (c.Name == "utvideo") sb.Append(" -c:v utvideo -pred median -pix_fmt yuv422p");
            else sb.Append(" -c:v ffvhuff -pix_fmt yuv422p");
            if (s.Interlaced) sb.Append(BottomFirst ? " -field_order bb" : " -field_order tt");
            return sb.ToString() + ColorTags(s);
        }

        sb.Append(" -c:v ").Append(c.Name);
        string preset = s.Preset ?? DefaultPreset(c.Family);
        int rate = live && s.Rate == 0 ? 2 : s.Rate;       // in diretta sempre a bitrate costante
        switch (c.Family)
        {
            case Fam.X264:
            case Fam.X265:
                sb.Append(" -preset ").Append(preset);
                if (s.Tune != null && s.Tune != "nessuno") sb.Append(" -tune ").Append(s.Tune);
                else if (live) sb.Append(" -tune zerolatency");
                if (rate == 0) sb.Append(" -crf ").Append(q);
                else if (rate == 1) sb.Append(" -b:v " + B + "k -maxrate " + (B * 3 / 2) + "k -bufsize " + (B * 2) + "k");
                else sb.Append(" -b:v " + B + "k -minrate " + B + "k -maxrate " + B + "k -bufsize " + (live ? B * 2 : B) + "k");
                {
                    var xp = new List<string>();
                    if (rate == 2 && c.Family == Fam.X264 && !live) xp.Add("nal-hrd=cbr");
                    if (s.Interlaced && c.Family == Fam.X264) { xp.Add("interlaced=1"); xp.Add(BottomFirst ? "bff=1" : "tff=1"); sb.Append(" -flags +ilme+ildct"); }
                    if (xp.Count > 0) sb.Append(" -x264-params ").Append(string.Join(":", xp.ToArray()));
                }
                break;
            case Fam.Nvenc:
                sb.Append(" -preset ").Append(preset);
                sb.Append(" -tune ").Append(live ? "ll" : (s.Tune == null || s.Tune == "nessuno" ? "hq" : s.Tune));
                if (rate == 0) sb.Append(" -rc constqp -qp ").Append(q);
                else if (rate == 1) sb.Append(" -rc vbr -cq " + q + " -b:v " + B + "k -maxrate " + (B * 3 / 2) + "k");
                else sb.Append(" -rc cbr -b:v " + B + "k -maxrate " + B + "k -bufsize " + (B * 2) + "k");
                break;
            case Fam.Qsv:
                sb.Append(" -preset ").Append(preset);
                if (rate == 0) sb.Append(" -global_quality ").Append(Math.Max(1, q));
                else if (rate == 1) sb.Append(" -b:v " + B + "k -maxrate " + (B * 3 / 2) + "k");
                else sb.Append(" -b:v " + B + "k -maxrate " + B + "k -bufsize " + (B * 2) + "k");
                break;
            case Fam.Amf:
                sb.Append(" -quality ").Append(preset);
                if (live) sb.Append(" -usage lowlatency");
                if (rate == 0) sb.Append(" -rc cqp -qp_i " + q + " -qp_p " + q + (c.Kind == "h264" ? " -qp_b " + q : ""));
                else if (rate == 1) sb.Append(" -rc vbr_peak -b:v " + B + "k -maxrate " + (B * 3 / 2) + "k");
                else sb.Append(" -rc cbr -b:v " + B + "k -maxrate " + B + "k -bufsize " + (B * 2) + "k");
                break;
            case Fam.Aom:
                sb.Append(" -cpu-used ").Append(preset).Append(" -row-mt 1 -usage realtime");
                if (rate == 0) sb.Append(" -crf " + Math.Min(63, q + 10) + " -b:v 0");
                else if (rate == 1) sb.Append(" -b:v " + B + "k");
                else sb.Append(" -b:v " + B + "k -minrate " + B + "k -maxrate " + B + "k");
                break;
        }
        if (prof != null) sb.Append(" -profile:v ").Append(prof);
        int g = Math.Max(1, s.KeySec) * Math.Max(1, fps);
        sb.Append(" -g ").Append(g);
        if (c.Family == Fam.X264 || c.Family == Fam.X265) sb.Append(" -keyint_min ").Append(Math.Min(g, fps));
        sb.Append(" -pix_fmt ").Append(pix);
        return sb.ToString() + ColorTags(s);
    }

    static string ColorTags(EncSettings s)
    {
        string m = s.Pal ? "bt470bg" : "smpte170m";
        string r = " -colorspace " + m + " -color_primaries " + m + " -color_trc " + (s.Pal ? "gamma28" : "smpte170m") + " -color_range tv";
        if (s.Aspect != null) r += " -aspect " + s.Aspect;
        return r;
    }

    public static string Audio(int codec, int kbps) { return Audio(codec, kbps, false); }

    public static string Audio(int codec, int kbps, bool bits24)
    {
        switch (codec)
        {
            case 1: return " -c:a libopus -b:a " + kbps + "k";
            case 2: return bits24 ? " -c:a flac -sample_fmt s32 -bits_per_raw_sample 24" : " -c:a flac";
            case 3: return bits24 ? " -c:a pcm_s24le" : " -c:a pcm_s16le";
            case 4: return " -c:a libmp3lame -b:a " + kbps + "k";
            default: return " -c:a aac -b:a " + kbps + "k";
        }
    }

    // Corregge combinazioni contenitore/codec non valide; restituisce un avviso o null
    public static string Fix(ref int container, EncSettings s)
    {
        string warn = null;
        VCodec c = Codecs[s.Codec];
        bool mp4like = container == 1 || container == 2 || container == 3;
        if (c.Family == Fam.Lossless && s.Codec != LosslessX264 && (mp4like || container == 5))
        { container = 0; warn = L.T("Questo codec senza perdita richiede MKV: uso MKV.", "This lossless codec needs MKV: using MKV."); }
        if (container == 3 && c.Kind == "av1")
        { container = 1; warn = L.T("MOV non supporta AV1: uso MP4.", "MOV does not support AV1: using MP4."); }
        if (container == 4 && (c.Kind == "hevc" || c.Kind == "av1"))
        { container = 0; warn = L.T("AVI non è adatto a HEVC/AV1: uso MKV.", "AVI does not suit HEVC/AV1: using MKV."); }
        if ((container == 1 || container == 2) && s.AudioCodec == 3) { s.AudioCodec = 0; warn = L.T("MP4 non supporta PCM: audio in AAC.", "MP4 does not support PCM: AAC audio."); }
        if (container == 4 && (s.AudioCodec == 0 || s.AudioCodec == 1 || s.AudioCodec == 2)) { s.AudioCodec = 3; warn = L.T("In AVI l'audio va in PCM.", "AVI uses PCM audio."); }
        if (container == 5 && (s.AudioCodec == 2 || s.AudioCodec == 3)) { s.AudioCodec = 0; warn = L.T("In TS l'audio va in AAC.", "TS uses AAC audio."); }
        if (container == 3 && (s.AudioCodec == 1 || s.AudioCodec == 2)) { s.AudioCodec = s.AudioCodec == 2 ? 3 : 0; warn = L.T("MOV non supporta Opus/FLAC: audio cambiato.", "MOV does not support Opus/FLAC: audio changed."); }
        if (s.Interlaced && s.OutSize > 0) warn = L.T("Con 'Mantieni interlacciato' il video resta alla risoluzione originale.", "With 'Keep interlaced' the video keeps its original size.");
        if (s.Interlaced && s.Profile == "baseline") { s.Profile = "main"; warn = L.T("Il profilo baseline non supporta l'interlacciato: uso main.", "Baseline does not support interlacing: using main."); }
        if (s.Interlaced && (c.Family != Fam.X264 && c.Family != Fam.Lossless))
        { s.Interlaced = false; warn = L.T("Solo x264 e i codec senza perdita salvano l'interlacciato: salvo progressivo.", "Only x264 and lossless codecs keep interlacing: saving progressive."); }
        return warn;
    }

    static string Muxer(int container)
    {
        switch (container)
        {
            case 1: return " -f mp4";
            case 2: return " -f mp4 -movflags +frag_keyframe+empty_moov+default_base_moof";
            case 3: return " -f mov";
            case 4: return " -f avi";
            case 5: return " -f mpegts";
            default: return " -f matroska";
        }
    }

    const string In = "-y -hide_banner -loglevel warning -stats -stats_period 1 -probesize 8M -analyzeduration 1M -f mpegts -i -";

    // Etichette tradotte
    public static object[] ContainerLabels()
    {
        return new object[] { L.T("MKV (consigliato)", "MKV (recommended)"), "MP4", L.T("MP4 frammentato (resiste ai crash)", "Fragmented MP4 (crash-safe)"), "MOV", "AVI", "TS" };
    }
    public static object[] AudioCodecLabels() { return new object[] { "AAC", "Opus", "FLAC", L.T("PCM (senza perdita)", "PCM (lossless)"), "MP3" }; }

    public static string OutScale(int outSize)
    {
        return outSize == 1 ? "scale=-2:960:flags=neighbor" : outSize == 2 ? "scale=-2:1080:flags=neighbor" : null;
    }

    // Registrazione video dal flusso interno
    public static string Recorder(string file, int container, EncSettings s, int fps, bool hasAudio, bool hasMic, int splitMin, Dictionary<string, string> meta)
    {
        return Recorder(file, container, s, fps, hasAudio, hasMic, splitMin, meta, false);
    }

    public static string Recorder(string file, int container, EncSettings s, int fps, bool hasAudio, bool hasMic, int splitMin, Dictionary<string, string> meta, bool pausable)
    {
        var sb = new StringBuilder(In);
        sb.Append(" -map 0:v:0");
        if (hasAudio) sb.Append(" -map 0:a:0");
        if (hasMic) sb.Append(" -map 0:a:" + (hasAudio ? 1 : 0));      // senza audio del grabber il microfono è la prima traccia
        // il senza perdita e l'interlacciato restano alla risoluzione originale (ingrandire i campi li rovinerebbe)
        string sc = IsLossless(s.Codec) || s.Interlaced ? null : OutScale(s.OutSize);
        var vf = new List<string>();
        if (pausable) vf.Insert(0, "setpts=N/FRAME_RATE/TB");          // la pausa non lascia buchi
        if (sc != null) vf.Add(sc);
        if (vf.Count > 0) sb.Append(" -vf ").Append(string.Join(",", vf.ToArray()));
        sb.Append(Video(s, fps, false));
        if (hasAudio || hasMic) sb.Append(Audio(s.AudioCodec, s.AudioKbps, s.Audio24));
        if (pausable && (hasAudio || hasMic)) sb.Append(" -af asetpts=N/SR/TB");
        if (hasAudio) sb.Append(" -metadata:s:a:0 title=" + L.T("Gioco", "Game"));
        if (hasMic) sb.Append(" -metadata:s:a:" + (hasAudio ? 1 : 0) + " title=" + L.T("Microfono", "Microphone"));
        if (s.AudioCodec == 1 || s.AudioCodec == 2) sb.Append(" -strict -2");
        sb.Append(Meta(meta));
        if (splitMin > 0)
        {
            string fmt = container == 0 ? "matroska" : container == 4 ? "avi" : container == 5 ? "mpegts" : container == 3 ? "mov" : "mp4";
            sb.Append(" -f segment -segment_time " + (splitMin * 60) + " -reset_timestamps 1 -segment_format " + fmt);
            if (container == 2) sb.Append(" -segment_format_options movflags=+frag_keyframe+empty_moov+default_base_moof");
        }
        else sb.Append(Muxer(container));
        return sb.Append(" \"").Append(file).Append("\"").ToString();
    }

    public static string AudioRecorder(string file, int fmt, int kbps, bool hasMic, Dictionary<string, string> meta) { return AudioRecorder(file, fmt, kbps, hasMic, meta, false); }

    public static string AudioRecorder(string file, int fmt, int kbps, bool hasMic, Dictionary<string, string> meta, bool bits24) { return AudioRecorder(file, fmt, kbps, true, hasMic, meta, bits24); }

    public static string AudioRecorder(string file, int fmt, int kbps, bool hasAudio, bool hasMic, Dictionary<string, string> meta, bool bits24)
    {
        var sb = new StringBuilder(In);
        if (hasAudio && hasMic) sb.Append(" -filter_complex \"[0:a:0][0:a:1]amix=inputs=2:normalize=0,alimiter=limit=0.95,asetpts=N/SR/TB[a]\" -map [a]");
        else sb.Append(" -map 0:a:0 -af asetpts=N/SR/TB");
        switch (fmt)
        {
            case 1: sb.Append(bits24 ? " -c:a flac -sample_fmt s32 -bits_per_raw_sample 24" : " -c:a flac"); break;
            case 2: sb.Append(" -c:a libmp3lame -b:a " + kbps + "k"); break;
            case 3: sb.Append(" -c:a aac -b:a " + kbps + "k"); break;
            case 4: sb.Append(" -c:a libopus -b:a " + kbps + "k"); break;
            case 5: sb.Append(" -c:a wmav2 -b:a " + kbps + "k"); break;
            default: sb.Append(bits24 ? " -c:a pcm_s24le" : " -c:a pcm_s16le").Append(" -rf64 auto"); break;     // oltre 4 GB il WAV classico si rompe
        }
        sb.Append(Meta(meta));
        return sb.Append(" \"").Append(file).Append("\"").ToString();
    }

    // Copia d'archivio senza perdita in parallelo
    public static string Archive(string file, bool hasAudio, bool hasMic, bool interlaced, Dictionary<string, string> meta) { return Archive(file, hasAudio, hasMic, interlaced, meta, false); }

    public static string Archive(string file, bool hasAudio, bool hasMic, bool interlaced, Dictionary<string, string> meta, bool bits24)
    {
        var sb = new StringBuilder(In);
        sb.Append(" -map 0:v:0");
        if (hasAudio) sb.Append(" -map 0:a:0");
        if (hasMic) sb.Append(" -map 0:a:" + (hasAudio ? 1 : 0));
        sb.Append(" -c:v ffv1 -level 3 -g 1 -slices 16 -slicecrc 1 -pix_fmt yuv422p");
        if (interlaced) sb.Append(BottomFirst ? " -field_order bb" : " -field_order tt");
        if (hasAudio || hasMic) sb.Append(bits24 ? " -c:a flac -sample_fmt s32 -bits_per_raw_sample 24" : " -c:a flac");
        sb.Append(Meta(meta)).Append(" -f matroska");
        return sb.Append(" \"").Append(file).Append("\"").ToString();
    }

    public static string Streamer(string url, EncSettings s, int fps, bool hasAudio, bool hasMic)
    {
        var sb = new StringBuilder(In);
        string sc = OutScale(s.OutSize);
        if (s.SquareScale != null) sc = sc == null ? s.SquareScale : s.SquareScale + "," + sc;
        var e = new EncSettings { Codec = s.Codec, Rate = 2, Quality = s.Quality, Bitrate = s.Bitrate, KeySec = 2, Preset = s.Preset, OutSize = s.OutSize, Pal = s.Pal };
        if (IsLossless(e.Codec) || Codecs[e.Codec].Kind == "av1" && !Codecs[e.Codec].Hardware) e.Codec = 0;
        if (e.Codec != s.Codec) e.Preset = null;
        if (hasAudio && hasMic)
            sb.Append(" -filter_complex \"[0:v:0]" + (sc ?? "null") + "[v];[0:a:0][0:a:1]amix=inputs=2:normalize=0,alimiter=limit=0.95[a]\" -map [v] -map [a]");
        else
        {
            sb.Append(" -map 0:v:0");
            if (hasAudio || hasMic) sb.Append(" -map 0:a:0");
            if (sc != null) sb.Append(" -vf \"").Append(sc).Append("\"");
        }
        sb.Append(Video(e, fps, true));
        if (hasAudio || hasMic) sb.Append(" -c:a aac -b:a 160k -ar 48000");
        bool srt = url.StartsWith("srt", StringComparison.OrdinalIgnoreCase);
        return sb.Append(srt ? " -f mpegts \"" : " -f flv \"").Append(url.Replace("\"", "")).Append("\"").ToString();
    }

    static string Meta(Dictionary<string, string> meta)
    {
        if (meta == null) return "";
        var sb = new StringBuilder();
        foreach (var kv in meta)
            if (!string.IsNullOrEmpty(kv.Value))
                sb.Append(" -metadata ").Append(kv.Key).Append("=\"").Append(QuoteSafe(kv.Value.Replace("\"", "'"))).Append("\"");
        return sb.ToString();
    }

    // Una \ finale prima delle virgolette di chiusura le renderebbe "escape" (regole della riga di comando di Windows)
    public static string QuoteSafe(string v)
    {
        int n = 0;
        while (n < v.Length && v[v.Length - 1 - n] == '\\') n++;
        return n == 0 ? v : v + new string('\\', n);
    }

    // Stima in MB al minuto
    public static double MBperMin(EncSettings s, int container, bool audioOnly, int audioFmt, int fps, int height)
    {
        // PCM 48 kHz stereo: 1536 kbps a 16 bit, 2304 a 24 bit; FLAC circa il 60%
        double pcm = s.Audio24 ? 2304 : 1536, flac = s.Audio24 ? 1350 : 900;
        double akbps = audioOnly ? (audioFmt == 0 ? pcm : audioFmt == 1 ? flac : s.AudioKbps) :
                       (s.AudioCodec == 3 ? pcm : s.AudioCodec == 2 ? flac : s.AudioKbps);
        if (audioOnly) return akbps * 60 / 8 / 1024;
        VCodec c = Codecs[s.Codec];
        double vk;
        double pixels = (height > 0 ? height : 576) * (height > 0 ? height : 576) * 4.0 / 3.0;
        double fpsF = Math.Max(1, fps) / 25.0;
        if (c.Family == Fam.Lossless)
        {
            double perPix = c.Name == "ffv1" ? 3.5 : c.Name == "utvideo" ? 4.5 : s.Codec == LosslessX264 ? 3.5 : 6;   // bit per pixel circa (4:2:2, analogico)
            vk = pixels * perPix * 25 * fpsF / 1000;
        }
        else if (s.Rate > 0) vk = s.Bitrate;
        else
        {
            double baseK = 3200 * Math.Pow(2, (18 - s.Quality) / 6.0);      // SD H.264 a CRF 18
            if (c.Kind == "hevc") baseK *= 0.65;
            if (c.Kind == "av1") baseK *= 0.55;
            if (c.Hardware) baseK *= 1.25;
            double outH = s.OutSize == 1 ? 960 : s.OutSize == 2 ? 1080 : (height > 0 ? height : 576);
            vk = baseK * Math.Pow(outH / 576.0, 1.6) * Math.Sqrt(fpsF);
        }
        return (vk + akbps) * 60 / 8 / 1024;
    }

    // ---- preset di esportazione dei file
    public static string[] ExportPresets
    {
        get
        {
            return new[] { L.T("YouTube (1080p nitido)", "YouTube (1080p sharp)"), L.T("Telefono (720p)", "Phone (720p)"), "WhatsApp (max 16 MB)", L.T("Telegram (720p leggero)", "Telegram (720p light)"),
                "PSP (480x272)", L.T("Solo audio MP3", "Audio only MP3"), L.T("Converti in MP4 (senza ricodifica)", "Convert to MP4 (no re-encode)") };
        }
    }

    public static string Export(int preset, string inFile, string outFile, double duration)
    {
        string i = "-y -hide_banner -loglevel error -i \"" + inFile + "\"";
        string fit = "scale=w='if(gt(a,{0}/{1}),{0},-2)':h='if(gt(a,{0}/{1}),-2,{1})'";
        switch (preset)
        {
            case 0: return i + " -map 0:v:0 -map 0:a:0? -vf scale=-2:1080:flags=neighbor -c:v libx264 -preset slow -crf 18 -pix_fmt yuv420p -c:a aac -b:a 192k -movflags +faststart \"" + outFile + "\"";
            case 1: return i + " -map 0:v:0 -map 0:a:0? -vf scale=-2:720 -c:v libx264 -preset medium -crf 23 -profile:v high -pix_fmt yuv420p -c:a aac -b:a 128k -movflags +faststart \"" + outFile + "\"";
            case 2:
                {
                    double d = Math.Max(1, duration);
                    int vk = (int)Math.Min(4000, Math.Max(150, (15.5 * 8 * 1024 / d) - 96));
                    return i + " -map 0:v:0 -map 0:a:0? -vf \"" + string.Format(fit, 640, 480) + "\" -c:v libx264 -preset medium -profile:v baseline -level 3.1 -b:v " + vk + "k -maxrate " + vk + "k -bufsize " + (vk * 2) + "k -pix_fmt yuv420p -c:a aac -b:a 96k -ac 2 -movflags +faststart \"" + outFile + "\"";
                }
            case 3: return i + " -map 0:v:0 -map 0:a:0? -vf scale=-2:720 -c:v libx264 -preset medium -crf 26 -pix_fmt yuv420p -c:a aac -b:a 128k -movflags +faststart \"" + outFile + "\"";
            case 4: return i + " -map 0:v:0 -map 0:a:0? -vf \"scale=480:272:force_original_aspect_ratio=decrease,pad=480:272:(ow-iw)/2:(oh-ih)/2\" -c:v libx264 -profile:v main -level 3.0 -preset medium -b:v 768k -maxrate 1000k -bufsize 1500k -pix_fmt yuv420p -c:a aac -b:a 128k -ar 48000 -ac 2 \"" + outFile + "\"";
            case 5: return i + " -map 0:a:0 -c:a libmp3lame -b:a 192k \"" + outFile + "\"";
            default: return i + " -map 0 -c copy -movflags +faststart \"" + outFile + "\"";
        }
    }

    public static string ExportExt(int preset) { return preset == 5 ? ".mp3" : ".mp4"; }
    public static string ExportSuffix(int preset)
    {
        string[] s = { "_youtube", "_telefono", "_whatsapp", "_telegram", "_psp", "_audio", "" };
        return s[Math.Max(0, Math.Min(s.Length - 1, preset))];
    }
}
