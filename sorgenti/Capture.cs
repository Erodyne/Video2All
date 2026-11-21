using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

// ---------------------------------------------------------------- Opzioni di cattura
public sealed class CaptureOptions
{
    public string Video, Audio, Cam, Mic;          // nomi dispositivo per ffmpeg (null = assente)
    public bool TestSource;                         // barre colore + tono al posto del grabber
    public int Width, Height;                       // 0 = automatico
    public string Fps;                              // null = automatico ("25", "30000/1001"...)
    public string PixFmt;                           // null = automatico
    public int RtBufMB = 256;
    public int VideoPin = -1, AudioPin = -1;        // ingressi crossbar (-1 = predefinito)
    public string DevLoad, DevSave;                 // memoria delle impostazioni del driver
    public bool Dialogs;
    public int SampleRate, Channels;                // 0 = automatico
    public int Deint;                               // 0 no, 1 yadif, 2 yadif 2x, 3 bwdif 2x, 4 w3fdif, 5 retro 240p
    public bool KeepInterlaced;                     // registra i campi originali (archivio)
    public int AspectMode = 1;                      // 0 nessuna, 1 scala a 4:3, 2 solo metadati
    public decimal CropL, CropR, CropT, CropB;
    public int Levels;                              // 0 invariati, 1 espandi TV->PC, 2 limita PC->TV
    public bool Denoise, ChromaNR;
    public int ChromaShift, HeadSwitch;             // pixel, righe
    public int Bright, Contrast = 100, Sat = 100;
    public int CamPos, CamSize = 25;
    public string Logo; public int LogoPos;
    public string TextFile; public int TextPos;
    public string OverlayText;                      // testo in sovrimpressione (aggiornabile dal vivo)
    public int Scene; public string SceneText;      // 0 = gioco, >0 = scena a schermo pieno
    public bool SceneMute;
    public bool Timer; public int TimerPos; public string TimerText;
    public bool AutoLevels, Sharpen; public string Lut;
    public bool Audio24;                            // audio a 24 bit (se il dispositivo lo fornisce)
    public int Scope;                               // 0 no, 1 istogramma, 2 waveform, 3 vettorscopio (solo anteprima)
    public bool Grid;                               // griglia e area sicura (solo anteprima)
    public bool PreviewAudio, AudioDenoise, AudioNorm, LowLatency, Cfr;
    public double GainDb;
    public int AudioDelay;                          // ms, anche negativo
    public bool LosslessMez;                        // flusso interno senza perdita
    public string OutA, OutM;
}

public static class Cmd
{
    public const int PW = 768, PH = 576;           // anteprima fissa

    public static string F(double v) { return v.ToString("0.#####", CultureInfo.InvariantCulture); }

    public static string Esc(string path)          // percorso dentro un filtro ffmpeg
    {
        return path.Replace("\\", "/").Replace(":", "\\:").Replace("'", "'\\''");
    }

    static string Pos(int p, string w, string h, string ow, string oh)
    {
        string x = (p == 1 || p == 3) ? w + "-" + ow + "-16" : "16";
        string y = (p == 2 || p == 3) ? h + "-" + oh + "-16" : "16";
        return "x=" + x + ":y=" + y;
    }

    public static string FontFile()
    {
        string dir = Environment.GetFolderPath(Environment.SpecialFolder.Fonts);
        foreach (string f in new[] { "arialbd.ttf", "arial.ttf", "segoeuib.ttf", "segoeui.ttf", "tahoma.ttf" })
        {
            string p = Path.Combine(dir, f);
            if (File.Exists(p)) return p;
        }
        return null;
    }

    // Il testo vero arriva subito dopo l'avvio con un comando dal vivo (niente problemi di escape nel grafo)
    static string TextArg(string t) { return "''"; }

    public static bool IsBob(int deint) { return deint == 2 || deint == 3 || deint == 5; }

    // dal grabber i fotogrammi non dicono quale campo viene prima: lo si indica (dall'analisi del segnale)
    static string DeintFilter(int d)
    {
        string par = Enc.BottomFirst ? "1" : "0";
        string field = Enc.BottomFirst ? "setfield=bff," : "setfield=tff,";
        switch (d)
        {
            case 1: return "yadif=0:" + par;
            case 2: return "yadif=1:" + par;
            case 3: return "bwdif=1:" + par;
            case 4: return field + "w3fdif";
            case 5: return field + "separatefields,scale=iw:ih*2:flags=neighbor";   // modalità Retro: ogni campo è un fotogramma 240p
            default: return null;
        }
    }

    // Opzioni di ingresso dshow (usate anche per diagnostica e analisi)
    public static string Input(CaptureOptions o)
    {
        var sb = new StringBuilder();
        bool hasAudio = o.Audio != null;
        if (o.TestSource)
        {
            string size = (o.Width > 0 ? o.Width : 720) + "x" + (o.Height > 0 ? o.Height : 576);
            string fps = o.Fps ?? "25";
            sb.Append(" -re -f lavfi -i \"smptebars=size=" + size + ":rate=" + fps + "[out0];sine=f=1000:r=48000[out1]\"");
            return sb.ToString();
        }
        // in bassa latenza code corte: se il PC non sta al passo si perde un fotogramma invece di accumulare secondi di ritardo
        sb.Append(" -f dshow -rtbufsize ").Append(o.LowLatency ? Math.Min(o.RtBufMB, 32) : o.RtBufMB).Append("M -thread_queue_size ").Append(o.LowLatency ? 16 : 1024);
        if (hasAudio) sb.Append(" -audio_buffer_size ").Append(o.LowLatency ? "20" : "40");
        if (o.Width > 0 && o.Height > 0) sb.Append(" -video_size " + o.Width + "x" + o.Height);
        if (o.Fps != null) sb.Append(" -framerate " + o.Fps);
        if (o.PixFmt != null) sb.Append(" -pixel_format " + o.PixFmt);
        if (hasAudio && o.SampleRate > 0) sb.Append(" -sample_rate " + o.SampleRate);
        if (hasAudio && o.Channels > 0) sb.Append(" -channels " + o.Channels);
        if (hasAudio && o.Audio24) sb.Append(" -sample_size 24");
        if (o.VideoPin >= 0) sb.Append(" -crossbar_video_input_pin_number " + o.VideoPin);
        if (o.AudioPin >= 0) sb.Append(" -crossbar_audio_input_pin_number " + o.AudioPin);
        if (o.DevLoad != null && File.Exists(o.DevLoad)) sb.Append(" -video_device_load \"" + o.DevLoad + "\"");
        if (o.DevSave != null) sb.Append(" -video_device_save \"" + o.DevSave + "\"");
        if (o.Dialogs) sb.Append(" -show_video_device_dialog true -show_video_crossbar_connection_dialog true");
        string spec = "video=" + o.Video + (hasAudio ? ":audio=" + o.Audio : "");
        sb.Append(" -i \"").Append(spec.Replace("\"", "")).Append("\"");
        return sb.ToString();
    }

    public static string Capture(CaptureOptions o)
    {
        bool hasAudio = o.Audio != null || o.TestSource;
        var sb = new StringBuilder("-y -hide_banner -nostats -loglevel info");
        sb.Append(Input(o));
        int idx = 1, camIdx = -1, logoIdx = -1, micIdx = -1;
        if (o.Cam != null && !o.TestSource)
        {
            sb.Append(" -f dshow -rtbufsize 64M -thread_queue_size 512 -i \"video=").Append(o.Cam.Replace("\"", "")).Append("\"");
            camIdx = idx++;
        }
        if (o.Mic != null && !o.TestSource)
        {
            sb.Append(" -f dshow -thread_queue_size 1024 -audio_buffer_size 40 -i \"audio=").Append(o.Mic.Replace("\"", "")).Append("\"");
            micIdx = idx++;
        }
        if (o.Logo != null && File.Exists(o.Logo))
        {
            sb.Append(" -i \"").Append(o.Logo).Append("\"");
            logoIdx = idx++;
        }

        // ---- catena comune (prima della divisione anteprima / flusso interno)
        var pre = new List<string>();
        if (o.Levels == 1) pre.Add("lutyuv=y='clip((val-16)*255/219,0,255)'");
        else if (o.Levels == 2) pre.Add("lutyuv=y='16+val*219/255'");
        if (o.ChromaShift != 0) pre.Add("chromashift=cbh=" + o.ChromaShift + ":crh=" + o.ChromaShift);
        if (o.HeadSwitch > 0) pre.Add("drawbox=x=0:y=ih-" + o.HeadSwitch + ":w=iw:h=" + o.HeadSwitch + ":color=black:t=fill");
        decimal cw = 1 - (o.CropL + o.CropR) / 100m, ch = 1 - (o.CropT + o.CropB) / 100m;
        if (cw < 1 || ch < 1)
        {
            if (cw < 0.5m) cw = 0.5m;
            if (ch < 0.5m) ch = 0.5m;
            pre.Add("crop=w='trunc(iw*" + F((double)cw) + "/2)*2':h='trunc(ih*" + F((double)ch) + "/2)*2':x='min(trunc(iw*" + F((double)(o.CropL / 100m)) + "/2)*2,iw-ow)':y='min(trunc(ih*" + F((double)(o.CropT / 100m)) + "/2)*2,ih-oh)'");
        }
        if (o.ChromaNR) pre.Add("hqdn3d=0:4:0:6");
        if (o.Denoise) pre.Add("hqdn3d=3:3:4:4");
        if (o.Bright != 0 || o.Contrast != 100 || o.Sat != 100)
            pre.Add("eq=brightness=" + F(o.Bright / 100.0) + ":contrast=" + F(o.Contrast / 100.0) + ":saturation=" + F(o.Sat / 100.0));
        if (o.AutoLevels) pre.Add("format=gbrp,normalize=smoothing=25:independence=0.5:strength=0.8,format=yuv422p");
        if (o.Lut != null && File.Exists(o.Lut)) pre.Add("lut3d=file='" + Esc(o.Lut) + "'");
        if (o.Sharpen) pre.Add("unsharp=5:5:0.7:3:3:0");
        if (o.AudioDelay < 0) pre.Add("setpts=PTS+" + F(-o.AudioDelay / 1000.0) + "/TB");   // audio "in anticipo": si ritarda il video
        pre.Add("scale=trunc(iw/2)*2:trunc(ih/2)*2");

        var g = new StringBuilder();
        g.Append("[0:v]").Append(string.Join(",", pre.ToArray())).Append("[b0]");
        string cur = "[b0]";
        if (camIdx >= 0)
        {
            int ph = Math.Max(10, Math.Min(50, o.CamSize));
            g.Append(";[").Append(camIdx).Append(":v]scale=-2:trunc(").Append(F(ph * 5.76)).Append("/2)*2[cam]");
            g.Append(";").Append(cur).Append("[cam]overlay=").Append(Pos(o.CamPos, "W", "H", "w", "h")).Append(":eof_action=pass[b1]");
            cur = "[b1]";
        }
        if (logoIdx >= 0)
        {
            g.Append(";[").Append(logoIdx).Append(":v]scale=-2:72,format=rgba[logo]");
            g.Append(";").Append(cur).Append("[logo]overlay=").Append(Pos(o.LogoPos, "W", "H", "w", "h")).Append(":eof_action=repeat[b2]");
            cur = "[b2]";
        }
        string font = FontFile();
        var post = new List<string>();
        if (font != null && o.OverlayText != null)
            post.Add("drawtext@ovl=fontfile='" + Esc(font) + "':expansion=none:text=" + TextArg(o.OverlayText) + ":fontsize=h/16:fontcolor=white:borderw=3:bordercolor=black@0.8:" + Pos(o.TextPos, "w", "h", "tw", "th"));
        if (font != null && o.Timer)
            post.Add("drawtext@timer=fontfile='" + Esc(font) + "':expansion=none:text=" + TextArg(o.TimerText ?? "0:00.0") + ":fontsize=h/14:fontcolor=white:box=1:boxcolor=black@0.55:boxborderw=8:" + Pos(o.TimerPos, "w", "h", "tw", "th"));
        // scena a schermo pieno: si accende/spegne con comandi dal vivo
        post.Add("drawbox@slate=x=0:y=0:w=iw:h=ih:color=0x101218@" + (o.Scene > 0 ? "1" : "0") + ":t=fill");
        if (font != null)
            post.Add("drawtext@scene=fontfile='" + Esc(font) + "':expansion=none:text=" + TextArg(o.Scene > 0 ? o.SceneText : "") + ":fontsize=h/10:fontcolor=white:x=(w-tw)/2:y=(h-th)/2");
        g.Append(";").Append(cur).Append(string.Join(",", post.ToArray())).Append(",split=2[vp][vm]");

        // ---- ramo anteprima
        var pv = new List<string>();
        string dpv = DeintFilter(o.Deint);
        if (dpv != null) pv.Add(dpv);
        if (o.AspectMode > 0) pv.Add("scale=trunc(ih*4/3/2)*2:ih");
        pv.Add("scale=" + PW + ":" + PH);
        pv.Add("drawbox@probe=x=0:y=0:w=8:h=8:color=0xFF00FF@0:t=fill");   // per la misura della latenza
        // griglia e strumenti di misura sono disegnati dal programma solo nell'anteprima principale
        g.Append(";[vp]").Append(string.Join(",", pv.ToArray())).Append(",format=bgr24[vpo]");

        // ---- ramo flusso interno (registrazione e diretta)
        var mv = new List<string>();
        if (!o.KeepInterlaced) { string d = DeintFilter(o.Deint); if (d != null) mv.Add(d); }
        if (o.AspectMode == 1) mv.Add("scale=trunc(ih*4/3/2)*2:ih");
        if (o.Cfr && o.Fps != null)
        {
            string f = o.Fps;
            if (!o.KeepInterlaced && IsBob(o.Deint))
            {
                string[] p = f.Split('/');
                double num;
                if (!double.TryParse(p[0], NumberStyles.Float, CultureInfo.InvariantCulture, out num)) num = 25;
                // frequenza doppia anche per valori decimali (es. 12.5 -> 25)
                f = p.Length > 1 ? Cmd.F(num * 2) + "/" + p[1] : Cmd.F(num * 2);
            }
            mv.Add("fps=" + f);
        }
        mv.Add("format=yuv422p");
        g.Append(";[vm]").Append(string.Join(",", mv.ToArray())).Append("[vmo]");

        // ---- audio
        if (hasAudio)
        {
            string sf = o.Audio24 ? "s32" : "s16";
            g.Append(";[0:a]aresample=48000:async=1000,aformat=sample_fmts=" + sf + ":channel_layouts=stereo");
            if (Math.Abs(o.GainDb) > 0.05) g.Append(",volume=" + F(o.GainDb) + "dB");
            if (o.AudioDenoise) g.Append(",afftdn=nf=-25");
            if (o.AudioNorm) g.Append(",dynaudnorm=f=250:g=15");
            if (o.AudioDelay > 0) g.Append(",adelay=" + o.AudioDelay + "|" + o.AudioDelay);
            g.Append(",volume@scenevol=volume=" + (o.Scene > 0 && o.SceneMute ? "0" : "1"));
            // blocchi piccoli: il codec interno (s302m) rifiuta fotogrammi audio troppo grandi (dynaudnorm/afftdn a 24 bit)
            g.Append(",aformat=sample_fmts=" + sf + ":channel_layouts=stereo,asetnsamples=n=1024:p=0");
            g.Append(o.PreviewAudio ? ",asplit=2[ap0][am];[ap0]aformat=sample_fmts=s16[ap]" : "[am]");
        }
        if (micIdx >= 0)
            // il microfono ha un suo orologio: aresample con async compensa la deriva rispetto al grabber
            g.Append(";[" + micIdx + ":a]aresample=48000:async=1000:min_comp=0.001:max_soft_comp=0.02,aformat=sample_fmts=" + (o.Audio24 ? "s32" : "s16") + ":channel_layouts=stereo,asetnsamples=n=1024:p=0[mic]");
        sb.Append(" -filter_complex \"").Append(g).Append("\"");

        // ---- uscite
        sb.Append(" -map [vpo] -c:v rawvideo -f rawvideo -");
        if (hasAudio && o.PreviewAudio) sb.Append(" -map [ap] -c:a pcm_s16le -ar 48000 -ac 2 -f s16le " + o.OutA);
        sb.Append(" -map [vmo]");
        if (hasAudio) sb.Append(" -map [am]");
        if (micIdx >= 0) sb.Append(" -map [mic]");
        if (o.LosslessMez) sb.Append(" -c:v libx264 -preset ultrafast -qp 0 -g 1 -pix_fmt yuv422p");
        else sb.Append(" -c:v mpeg2video -g 1 -q:v 2 -intra_vlc 1 -pix_fmt yuv422p");
        if (o.KeepInterlaced && !o.LosslessMez) sb.Append(" -flags +ilme+ildct -top " + (Enc.BottomFirst ? "0" : "1"));
        if (hasAudio || micIdx >= 0) sb.Append(" -c:a s302m -strict -2" + (o.Audio24 ? " -bits_per_raw_sample:a 24" : ""));
        sb.Append(" -f mpegts -muxdelay 0 -flush_packets 1 " + o.OutM);
        return sb.ToString();
    }

    // Analisi del segnale (interlacciato/progressivo) per pochi secondi
    public static string Analyze(CaptureOptions o, int seconds)
    {
        return "-hide_banner -nostats -loglevel info" + Input(o) + " -t " + seconds + " -map 0:v -vf idet -f null -";
    }

    // Prova di acquisizione per la diagnostica
    public static string Probe(CaptureOptions o, int seconds)
    {
        return "-hide_banner -loglevel info -stats" + Input(o) + " -t " + seconds + " -map 0:v -f null -";
    }
}

// ---------------------------------------------------------------- Informazioni sul dispositivo (list_options)
public sealed class DeviceCaps
{
    public readonly List<string> PixFmts = new List<string>();
    public readonly List<string> Sizes = new List<string>();
    public readonly List<string> Rates = new List<string>();
    public readonly List<KeyValuePair<int, string>> VideoInputs = new List<KeyValuePair<int, string>>();
    public readonly Dictionary<int, int> RelatedAudio = new Dictionary<int, int>();
    public readonly List<int> SampleRates = new List<int>();

    public static DeviceCaps Parse(string text)
    {
        var c = new DeviceCaps();
        foreach (Match m in Regex.Matches(text, @"(?:pixel_format|vcodec)=(\S+)\s+min s=(\d+)x(\d+) fps=([\d.]+) max s=(\d+)x(\d+) fps=([\d.]+)"))
        {
            string pf = m.Groups[1].Value;
            if (!c.PixFmts.Contains(pf)) c.PixFmts.Add(pf);
            foreach (int k in new[] { 2, 5 })
            {
                string s = m.Groups[k].Value + "x" + m.Groups[k + 1].Value;
                if (!c.Sizes.Contains(s)) c.Sizes.Add(s);
            }
            foreach (int k in new[] { 4, 7 })
            {
                string r = NormRate(m.Groups[k].Value);
                if (r != null && !c.Rates.Contains(r)) c.Rates.Add(r);
            }
        }
        // ingressi che il crossbar può davvero collegare all'uscita video (es. "compatible input pins: 0 1")
        HashSet<int> routable = null;
        Match vo = Regex.Match(text, "Crossbar Output pin \\d+: \"Video[^\"]*\"[^\\n]*?compatible input pins:([ \\d]*)");
        if (vo.Success)
        {
            routable = new HashSet<int>();
            foreach (string t in vo.Groups[1].Value.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries)) { int v; if (int.TryParse(t, out v)) routable.Add(v); }
            if (routable.Count == 0) routable = null;
        }
        var audioPins = new HashSet<int>();
        foreach (Match m in Regex.Matches(text, "Crossbar Input pin (\\d+) - \"([^\"]+)\"\\s+related input pin:\\s*(-?\\d+)"))
        {
            int pin = int.Parse(m.Groups[1].Value);
            string name = m.Groups[2].Value;
            if (name.StartsWith("Audio", StringComparison.OrdinalIgnoreCase)) { audioPins.Add(pin); continue; }
            if (name.IndexOf("Tuner", StringComparison.OrdinalIgnoreCase) >= 0 || name.IndexOf("Unknown", StringComparison.OrdinalIgnoreCase) >= 0 || name.IndexOf("Decoder", StringComparison.OrdinalIgnoreCase) >= 0) continue;
            if (routable != null && !routable.Contains(pin)) continue;
            if (c.RelatedAudio.ContainsKey(pin)) continue;
            c.VideoInputs.Add(new KeyValuePair<int, string>(pin, name));
            c.RelatedAudio[pin] = int.Parse(m.Groups[3].Value);
        }
        // l'ingresso audio collegato deve essere davvero un pin audio
        foreach (var kv in c.VideoInputs) if (!audioPins.Contains(c.RelatedAudio[kv.Key])) c.RelatedAudio[kv.Key] = -1;
        foreach (Match m in Regex.Matches(text, @"rate=\s*(\d+)"))
        {
            int r = int.Parse(m.Groups[1].Value);
            if (r >= 8000 && !c.SampleRates.Contains(r)) c.SampleRates.Add(r);
        }
        c.Sizes.Sort(delegate (string a, string b) { return Area(b).CompareTo(Area(a)); });
        return c;
    }

    static int Area(string s) { string[] p = s.Split('x'); return int.Parse(p[0]) * int.Parse(p[1]); }

    static string NormRate(string f)
    {
        double v;
        if (!double.TryParse(f, NumberStyles.Float, CultureInfo.InvariantCulture, out v) || v <= 0) return null;
        if (Math.Abs(v - 29.97) < 0.02) return "30000/1001";
        if (Math.Abs(v - 59.94) < 0.02) return "60000/1001";
        if (Math.Abs(v - 23.976) < 0.02) return "24000/1001";
        return Math.Round(v).ToString(CultureInfo.InvariantCulture);
    }

    public static string RateLabel(string r)
    {
        if (r == "30000/1001") return "29,97";
        if (r == "60000/1001") return "59,94";
        if (r == "24000/1001") return "23,976";
        return r;
    }

    // Formato realmente ricevuto (dalle informazioni di ffmpeg)
    public static string Detected(string stderr, out int width, out int height, out double fps)
    {
        width = height = 0; fps = 0;
        Match m = Regex.Match(stderr ?? "", @"Stream #0:0[^\n]*Video:\s*([^\n]*?)(\d{3,4})x(\d{3,4})[^\n]*?([\d.]+) (?:fps|tbr)");
        if (!m.Success) return null;
        width = int.Parse(m.Groups[2].Value); height = int.Parse(m.Groups[3].Value);
        double.TryParse(m.Groups[4].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out fps);
        string codec = m.Groups[1].Value.Split(',')[0].Trim();
        return width + "x" + height + "  " + fps.ToString("0.##", CultureInfo.InvariantCulture) + " fps  " + codec;
    }
}
