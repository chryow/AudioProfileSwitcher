using System.Runtime.InteropServices;

[assembly: DefaultDllImportSearchPaths(DllImportSearchPath.System32)]

namespace AudioProfileSwitcher;

static class Program
{
    [STAThread]
    static void Main(string[] args)
    {
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        Application.ThreadException += (_, e) => HandleCrash("UI thread", e.Exception);
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            HandleCrash("AppDomain", e.ExceptionObject as Exception);
        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            AppLog.Error("Unobserved task exception", e.Exception);
            e.SetObserved();
        };

        // Internal one-shot self-update applier (strictly restricted to %AppData%\AudioProfileSwitcher\updates\).
        if (args.Any(a => a.Equals("--apply-update", StringComparison.OrdinalIgnoreCase)))
        {
            Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            UpdateService.RunApplyUpdateMode();
            return;
        }

        string? updatedToVersion = null;
        for (int i = 0; i < args.Length - 1; i++)
        {
            if (args[i].Equals("--updated-to", StringComparison.OrdinalIgnoreCase))
            {
                updatedToVersion = ProfileLogic.SanitizeDisplayString(args[i + 1], 32, "");
                if (updatedToVersion.Length == 0) updatedToVersion = null;
                break;
            }
        }

        // Single instance: a second launch just asks the first one to show its window.
        using var mutex = new Mutex(true, @"Local\AudioProfileSwitcher.Instance", out bool isFirst);
        using var showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, @"Local\AudioProfileSwitcher.Show");
        if (!isFirst && updatedToVersion != null)
        {
            try { isFirst = mutex.WaitOne(5000); }
            catch (AbandonedMutexException) { isFirst = true; }
        }
        if (!isFirst)
        {
            showEvent.Set();
            return;
        }

        AppLog.Info($"AudioProfileSwitcher v{AppInfo.Version} ({AppInfo.BuildFlavorLabel}) starting.");
        UpdateService.CleanupStagedUpdatesAsync();

        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);

        bool startMinimized = args.Contains("--minimized", StringComparer.OrdinalIgnoreCase);
        var form = new MainForm(startMinimized, updatedToVersion);

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
        AppLog.Info("AudioProfileSwitcher exited cleanly.");
    }

    private static void HandleCrash(string source, Exception? ex)
    {
        AppLog.Error($"Unhandled exception ({source})", ex);
        try
        {
            MessageBox.Show(
                $"AudioSwitcher ran into an unexpected error:\n\n{ex?.Message ?? "Unknown error"}\n\nDetails were written to:\n{AppLog.FilePath}",
                "AudioSwitcher Error",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
        catch { }
    }
}

internal static class Native
{
    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

    [DllImport("user32.dll")] private static extern bool ReleaseCapture();
    [DllImport("user32.dll")] private static extern int SendMessage(IntPtr hWnd, int msg, int wParam, int lParam);
    [DllImport("user32.dll")] private static extern short GetAsyncKeyState(int vKey);
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr hWnd);

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

    /// <summary>Subtle small-radius rounded corners on Windows 11 for floating pills.</summary>
    public static void RoundCornersSmall(IntPtr hwnd)
    {
        int round = 3; // DWMWCP_ROUNDSMALL
        try { DwmSetWindowAttribute(hwnd, 33, ref round, sizeof(int)); } catch { }
    }

    /// <summary>Tints the native Windows 11 DWM border (ignored on Windows 10).</summary>
    public static void SetBorderColor(IntPtr hwnd, Color color)
    {
        int colorRef = color.R | (color.G << 8) | (color.B << 16);
        try { DwmSetWindowAttribute(hwnd, 34, ref colorRef, sizeof(int)); } catch { }
    }

    public static void DragWindow(IntPtr hwnd)
    {
        ReleaseCapture();
        SendMessage(hwnd, 0xA1, 0x2, 0); // WM_NCLBUTTONDOWN, HTCAPTION
    }

    public static void BringToFrontNative(IntPtr hwnd)
    {
        try { SetForegroundWindow(hwnd); } catch { }
    }

    public static bool WinKeyDown() =>
        (GetAsyncKeyState(0x5B) & 0x8000) != 0 || (GetAsyncKeyState(0x5C) & 0x8000) != 0;
}