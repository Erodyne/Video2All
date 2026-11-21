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
    // ================================================================= Collegamento a caldo
    const int WM_DEVICECHANGE = 0x0219;

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == WM_HOTKEY)
        {
            HotKey(m.WParam.ToInt32());
            return;
        }
        if (m.Msg == WM_DEVICECHANGE && m.WParam.ToInt32() == 7)     // DBT_DEVNODES_CHANGED
        {
            hotplugPending = true;
            hotplugAt = DateTime.Now.AddSeconds(2);
        }
        base.WndProc(ref m);
    }
}
