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
    // ================================================================= Encoder disponibili
    void CheckEncodersAsync()
    {
        string stamp = "";
        try { var fi = new FileInfo(ffmpeg); stamp = fi.Length + "_" + Environment.MachineName; } catch { }
        string cached = cfg.Get("encok", "");
        DateTime when;
        if (cached.StartsWith(stamp + "|") && DateTime.TryParse(cfg.Get("encok_date", ""), CultureInfo.InvariantCulture, DateTimeStyles.None, out when) && (DateTime.Now - when).TotalDays < 30)
        {
            string bits = cached.Substring(stamp.Length + 1);
            if (bits.Length == Enc.Codecs.Length) { var okc = new bool[bits.Length]; for (int i = 0; i < bits.Length; i++) okc[i] = bits[i] == '1'; ApplyEncOk(okc); return; }
        }
        StartThread("encoder", delegate
        {
            var ok = new bool[Enc.Codecs.Length];
            for (int i = 0; i < ok.Length; i++)
                ok[i] = !Enc.Codecs[i].Hardware || Tools.EncoderWorks(ffmpeg, Enc.Codecs[i].Name);
            var bitsB = new StringBuilder();
            foreach (bool b in ok) bitsB.Append(b ? '1' : '0');
            Invoke2(delegate { cfg.Set("encok", stamp + "|" + bitsB); cfg.Set("encok_date", DateTime.Now.ToString("o", CultureInfo.InvariantCulture)); QueueSave(); ApplyEncOk(ok); });
        });
    }

    void ApplyEncOk(bool[] ok)
    {
        {
            {
                encOk = ok;
                bool was = loading;
                loading = true;
                int sel = cbCodec.SelectedIndex;
                for (int i = 0; i < ok.Length; i++)
                    if (Enc.Codecs[i].Hardware) cbCodec.Items[i] = Enc.Codecs[i].LabelL + (ok[i] ? " ✓" : L.T(" (non disponibile)", " (unavailable)"));
                cbCodec.SelectedIndex = sel;
                loading = was;
            }
        }
    }
}
