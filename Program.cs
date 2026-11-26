using System.Runtime.InteropServices;

namespace AudioProfileSwitcher;

static class Program
{
    [STAThread]
    static void Main(string[] args)
    {
        // Single instance: a second launch just asks the first one to show its window.
        using var mutex = new Mutex(true, @"Local\AudioProfileSwitcher.Instance", out bool isFirst);
        using var showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, @"Local\AudioProfileSwitcher.Show");
        if (!isFirst)
        {
            showEvent.Set();
            return;
        }

        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);

        bool startMinimized = args.Contains("--minimized", StringComparer.OrdinalIgnoreCase);
        var form = new MainForm(startMinimized);

        var listener = new Thread(() =>
        {
            while (true)
            {
                try { showEvent.WaitOne(); } catch { return; }
                if (form.IsDisposed) return;
                try
                {
                    if (form.IsHandleCreated) form.BeginInvoke(new Action(form.ShowFromTray));
                }
                catch { /* form is closing */ }
            }
        })
        { IsBackground = true };
        listener.Start();

        Application.Run(form);
    }
}

internal static class Native
{
    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

    [DllImport("user32.dll")] private static extern bool ReleaseCapture();
    [DllImport("user32.dll")] private static extern int SendMessage(IntPtr hWnd, int msg, int wParam, int lParam);
    [DllImport("user32.dll")] private static extern short GetAsyncKeyState(int vKey);

    /// <summary>Dark caption bar for standard dialogs (Windows 10 20H1+ / 11).</summary>
    public static void DarkTitleBar(IntPtr hwnd)
    {
        int on = 1;
        try { DwmSetWindowAttribute(hwnd, 20, ref on, sizeof(int)); } catch { }
    }

    /// <summary>Rounded window corners on Windows 11 (ignored elsewhere).</summary>
    public static void RoundCorners(IntPtr hwnd)
    {
        int round = 2; // DWMWCP_ROUND
        try { DwmSetWindowAttribute(hwnd, 33, ref round, sizeof(int)); } catch { }
    }

    public static void DragWindow(IntPtr hwnd)
    {
        ReleaseCapture();
        SendMessage(hwnd, 0xA1, 0x2, 0); // WM_NCLBUTTONDOWN, HTCAPTION
    }

    public static bool WinKeyDown() =>
        (GetAsyncKeyState(0x5B) & 0x8000) != 0 || (GetAsyncKeyState(0x5C) & 0x8000) != 0;
}
