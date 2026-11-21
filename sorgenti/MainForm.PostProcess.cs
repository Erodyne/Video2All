using Microsoft.Win32;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing.Imaging;
using System.Drawing;
using System.Globalization;
using System.IO.Pipes;
using System.IO;
using System.Net;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using System;

public partial class MainForm
{
    // ================================================================= Post-elaborazione (una alla volta)
    void QueuePostProcess(string file, int prerollSec, Consumer recConsumer, string[] reportInfo)
    {
        if (file == null) return;
        if (file.Contains("%03d"))
        {
            // divisione a tempo: ogni pezzo viene elaborato (MD5, copia, verifica...), senza capitoli
            try
            {
                string[] parts = Directory.GetFiles(Path.GetDirectoryName(file), Path.GetFileName(file).Replace("%03d", "???"));
                Array.Sort(parts, StringComparer.OrdinalIgnoreCase);
                foreach (string part in parts) QueuePostProcess(part, -2, null, null);
            }
            catch (Exception ex) { Log.W("Pezzi della registrazione: " + ex.Message); }
            return;
        }
        bool isArchive = prerollSec == -1;
        bool isSegment = prerollSec == -2;
        bool chapters = !isArchive && !isSegment && chkChapters.Checked, remux = !isArchive && chkRemux.Checked, verify = !isArchive && chkVerify.Checked;
        bool md5 = chkMd5.Checked, compress = !isArchive && chkAutoCompress.Checked, split = !isArchive && chkAutoSplit.Checked;
        string copyDir = chkCopyTo.Checked ? tbCopyTo.Text.Trim() : null;
        if (reportInfo != null && !(chapters || remux)) { var rr = recConsumer; StartThread("rapporto", delegate { WriteReport(file, rr, reportInfo); }); reportInfo = null; }
        if (!(chapters || remux || verify || md5 || compress || split || copyDir != null)) return;
        string ext = Path.GetExtension(file).ToLowerInvariant();
        bool video = ext == ".mkv" || ext == ".mp4" || ext == ".mov" || ext == ".avi" || ext == ".ts";
        int codecSnap = cbCodec.SelectedIndex, aCodecSnap = cbACodec.SelectedIndex;
        Action work = delegate
        {
            string f = file;
            var done = new List<string>();
            string e;
            // capitoli dai marcatori
            string mf = MarkersFile(f);
            double markerOffset = 0;
            KeyValuePair<string, double> part;
            bool isPart;
            lock (partMarkers) isPart = partMarkers.TryGetValue(file, out part);
            if (isPart) { lock (partMarkers) partMarkers.Remove(file); if (!File.Exists(mf)) { mf = part.Key; markerOffset = -part.Value; } }
            if (chapters && video && File.Exists(mf) && (ext == ".mkv" || ext == ".mp4" || ext == ".mov"))
            {
                double dur = Tools.Duration(ffmpeg, f);
                string meta = Tools.ChaptersMeta(mf, dur, markerOffset);
                if (meta != null)
                {
                    string mt = Path.Combine(Path.GetTempPath(), "acqv_cap_" + Guid.NewGuid().ToString("N") + ".txt");
                    File.WriteAllText(mt, meta, new UTF8Encoding(false));
                    string tmp = Path.Combine(Path.GetDirectoryName(f), "acqv_cap_" + Guid.NewGuid().ToString("N").Substring(0, 8) + ext);
                    if (Tools.Run(ffmpeg, "-y -hide_banner -loglevel error -i \"" + f + "\" -f ffmetadata -i \"" + mt + "\" -map 0 -map_metadata 0 -map_chapters 1 -c copy \"" + tmp + "\"", out e, 3600000) == 0 && ReplaceVerified(f, tmp))
                        done.Add(L.T("capitoli", "chapters"));
                    else SafeDelete(tmp);
                    try { File.Delete(mt); } catch { }
                }
            }
            // MKV -> MP4
            // l'MP4 accetta AAC, MP3, Opus, FLAC e PCM (ffmpeg recente); non accetta FFV1, UT Video e HuffYUV
            if (remux && ext == ".mkv" && (!Enc.IsLossless(codecSnap) || codecSnap == Enc.LosslessX264))
            {
                {
                    string mp4 = UniqueName(Path.ChangeExtension(f, ".mp4"));
                    if (Tools.Run(ffmpeg, "-y -hide_banner -loglevel error -i \"" + f + "\" -map 0 -c copy -movflags +faststart \"" + mp4 + "\"", out e, 3600000) == 0 && ConvertVerified(f, mp4))
                    { f = mp4; done.Add("MP4"); }
                    else { SafeDelete(mp4); done.Add(L.T("MP4 non riuscito, MKV conservato", "MP4 failed, MKV kept")); }
                }
            }
            if (reportInfo != null) WriteReport(f, recConsumer, reportInfo);
            if (verify && (video || Regex.IsMatch(ext, @"^\.(wav|flac|mp3|m4a|ogg|wma)$")))     // anche le registrazioni solo audio
            {
                string err = Tools.Verify(ffmpeg, f);
                done.Add(err == null ? L.T("integro", "verified") : L.T("ERRORI nel file", "file ERRORS"));
                if (err != null) Log.W("Verifica " + f + ": " + err);
            }
            if (md5)
            {
                string h = Tools.Md5(f);
                File.WriteAllText(f + ".md5", h + " *" + Path.GetFileName(f) + "\r\n");
                done.Add("MD5");
            }
            if (compress && video)
            {
                string outF = UniqueName(Path.Combine(Path.GetDirectoryName(f), Path.GetFileNameWithoutExtension(f) + "_compresso.mp4"));
                if (Tools.Run(ffmpeg, "-y -hide_banner -loglevel error -i \"" + f + "\" -map 0:v:0 -map 0:a? -c:v libx264 -preset medium -crf 24 -pix_fmt yuv420p -c:a aac -b:a 128k -movflags +faststart \"" + outF + "\"", out e, 12 * 3600000) == 0)
                    done.Add(L.T("copia compressa", "compressed copy"));
            }
            if (split && video)
            {
                int n = SplitOnBlack(f);
                if (n > 0) done.Add(n + L.T(" spezzoni", " parts"));
            }
            if (copyDir != null && copyDir.Length > 0)
            {
                try
                {
                    Directory.CreateDirectory(copyDir);
                    double needGb = new FileInfo(f).Length / 1073741824.0 + 0.2;
                    double freeGb = Native.FreeGB(copyDir);
                    if (freeGb >= 0 && freeGb < needGb) throw new IOException(L.T("spazio insufficiente nella cartella di copia", "not enough space in the copy folder"));
                    string dstCopy = UniqueName(Path.Combine(copyDir, Path.GetFileName(f)));
                    // copia su file temporaneo e poi rinomina: un'interruzione non lascia un video a metà con il nome giusto
                    string partFile = dstCopy + ".part";
                    try { File.Copy(f, partFile, true); File.Move(partFile, dstCopy); }
                    catch { SafeDelete(partFile); throw; }
                    if (File.Exists(f + ".md5")) File.Copy(f + ".md5", dstCopy + ".md5", true);
                    if (File.Exists(mf)) File.Copy(mf, Path.Combine(copyDir, Path.GetFileName(mf)), true);
                    done.Add(L.T("copiato", "copied"));
                }
                catch (Exception ex) { done.Add(L.T("copia non riuscita: ", "copy failed: ") + ex.Message); }
            }
            string msg = Path.GetFileName(f) + ": " + (done.Count > 0 ? string.Join(", ", done.ToArray()) : L.T("nessuna operazione", "nothing done"));
            Invoke2(delegate { SetStatus(msg, msg.Contains("ERRORI") || msg.Contains("ERRORS"), 10); RefreshFiles(); });
        };
        lock (postQueue) postQueue.Enqueue(new KeyValuePair<string, Action>(file, work));
        StartPostWorker();
    }


    void StartPostWorker()
    {
        lock (postQueue) { if (postWorker) return; postWorker = true; }
        StartThread("post", delegate
        {
            while (true)
            {
                KeyValuePair<string, Action> job;
                lock (postQueue)
                {
                    if (postQueue.Count == 0) { postWorker = false; return; }
                    job = postQueue.Dequeue();
                }
                Interlocked.Increment(ref postRunning);
                try { job.Value(); }
                catch (Exception ex) { Log.W("Post-elaborazione " + job.Key + ": " + ex); Invoke2(delegate { SetStatus(L.T("Post-elaborazione non riuscita: ", "Post-processing failed: ") + ex.Message, true, 10); }); }
                Interlocked.Decrement(ref postRunning);
            }
        });
    }

    int postRunning;

    int SplitOnBlack(string f)
    {
        string err;
        List<double> cuts = Tools.BlackCuts(ffmpeg, f, out err);
        if (cuts.Count == 0) return 0;
        var points = new List<double> { 0 };
        points.AddRange(cuts);
        string dir = Path.GetDirectoryName(f), name = Path.GetFileNameWithoutExtension(f), ext = Path.GetExtension(f);
        int ok = 0;
        for (int i = 0; i < points.Count; i++)
        {
            string outF = UniqueName(Path.Combine(dir, name + "_parte" + (i + 1).ToString("00") + ext));
            string to = i + 1 < points.Count ? " -to " + Tools.Ts(points[i + 1]) : "";
            string e;
            if (Tools.Run(ffmpeg, "-y -hide_banner -loglevel error -ss " + Tools.Ts(points[i]) + to + " -i \"" + f + "\" -map 0 -c copy -avoid_negative_ts make_zero \"" + outF + "\"", out e, 3600 * 1000) == 0) ok++;
        }
        return ok;
    }
}
