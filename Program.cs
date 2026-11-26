using System.Runtime.InteropServices;
using System.Text.Json;
using AudioSwitcher.AudioApi;
using AudioSwitcher.AudioApi.CoreAudio;

namespace AudioProfileSwitcher;

static class Program
{
    [STAThread]
    static void Main()
    {
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Application.Run(new TrayAppContext());
    }
}

public class AppConfig
{
    public Guid? DeskOutputId { get; set; }
    public Guid? DeskInputId { get; set; }
    public Guid? RoamOutputId { get; set; }
    public Guid? RoamInputId { get; set; }
    public bool IsRoamMode { get; set; }
}

public class TrayAppContext : ApplicationContext
{
    private readonly NotifyIcon _trayIcon;
    private readonly CoreAudioController _audioController;
    private readonly HotKeyWindow _hotKeyWindow;
    private readonly string _configPath;
    private AppConfig _config;

    public TrayAppContext()
    {
        _audioController = new CoreAudioController();
        _configPath = Path.Combine(AppContext.BaseDirectory, "config.json");
        _config = LoadConfig();

        _trayIcon = new NotifyIcon
        {
            Icon = SystemIcons.Application,
            Text = GetTooltipText(),
            Visible = true,
            ContextMenuStrip = new ContextMenuStrip()
        };

        _trayIcon.ContextMenuStrip.Opening += (_, _) => RebuildMenu();
        _trayIcon.DoubleClick += (_, _) => ToggleProfile();

        // Global hotkey: Ctrl + Shift + A
        _hotKeyWindow = new HotKeyWindow(ToggleProfile);
    }

    private void RebuildMenu()
    {
        var menu = _trayIcon.ContextMenuStrip!;
        menu.Items.Clear();

        var modeLabel = _config.IsRoamMode ? "Current: Roam Mode (Headset)" : "Current: Desk Mode (Speakers)";
        menu.Items.Add(new ToolStripMenuItem(modeLabel) { Enabled = false });
        menu.Items.Add(new ToolStripSeparator());

        menu.Items.Add("Switch to Desk Mode", null, (_, _) => ApplyProfile(false));
        menu.Items.Add("Switch to Roam Mode", null, (_, _) => ApplyProfile(true));
        menu.Items.Add(new ToolStripSeparator());

        var playbackDevices = _audioController.GetPlaybackDevices(DeviceState.Active).ToList();
        var captureDevices = _audioController.GetCaptureDevices(DeviceState.Active).ToList();

        menu.Items.Add(BuildDeviceSubMenu("Set Desk Output (Speakers)", playbackDevices, _config.DeskOutputId, id => _config.DeskOutputId = id));
        menu.Items.Add(BuildDeviceSubMenu("Set Desk Input (External Mic)", captureDevices, _config.DeskInputId, id => _config.DeskInputId = id));
        menu.Items.Add(BuildDeviceSubMenu("Set Roam Output (Headset)", playbackDevices, _config.RoamOutputId, id => _config.RoamOutputId = id));
        menu.Items.Add(BuildDeviceSubMenu("Set Roam Input (Headset Mic)", captureDevices, _config.RoamInputId, id => _config.RoamInputId = id));

        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Exit", null, (_, _) => ExitApp());
    }

    private ToolStripMenuItem BuildDeviceSubMenu(string title, List<CoreAudioDevice> devices, Guid? selectedId, Action<Guid> onSelect)
    {
        var parent = new ToolStripMenuItem(title);
        if (devices.Count == 0)
        {
            parent.DropDownItems.Add(new ToolStripMenuItem("No active devices found") { Enabled = false });
            return parent;
        }

        foreach (var device in devices)
        {
            var item = new ToolStripMenuItem(device.FullName)
            {
                Checked = selectedId.HasValue && device.Id == selectedId.Value
            };
            item.Click += (_, _) =>
            {
                onSelect(device.Id);
                SaveConfig();
            };
            parent.DropDownItems.Add(item);
        }

        return parent;
    }

    private void ToggleProfile() => ApplyProfile(!_config.IsRoamMode);

    private void ApplyProfile(bool roamMode)
    {
        var outputId = roamMode ? _config.RoamOutputId : _config.DeskOutputId;
        var inputId = roamMode ? _config.RoamInputId : _config.DeskInputId;

        if (!outputId.HasValue && !inputId.HasValue)
        {
            _trayIcon.ShowBalloonTip(2500, "Audio Profile Switcher", "Right-click the tray icon to assign your devices first.", ToolTipIcon.Warning);
            return;
        }

        if (outputId.HasValue)
        {
            var outputDevice = _audioController.GetDevice(outputId.Value);
            if (outputDevice != null)
            {
                outputDevice.SetAsDefault();
                outputDevice.SetAsDefaultCommunications();
            }
        }

        if (inputId.HasValue)
        {
            var inputDevice = _audioController.GetDevice(inputId.Value);
            if (inputDevice != null)
            {
                inputDevice.SetAsDefault();
                inputDevice.SetAsDefaultCommunications();
            }
        }

        _config.IsRoamMode = roamMode;
        SaveConfig();

        _trayIcon.Text = GetTooltipText();
        var activeName = roamMode ? "Roam Mode (Headset)" : "Desk Mode (Speakers)";
        _trayIcon.ShowBalloonTip(1500, "Audio Switched", $"Active: {activeName}", ToolTipIcon.Info);
    }

    private string GetTooltipText() =>
        _config.IsRoamMode ? "Audio Switcher: Roam Mode" : "Audio Switcher: Desk Mode";

    private AppConfig LoadConfig()
    {
        try
        {
            if (File.Exists(_configPath))
            {
                var json = File.ReadAllText(_configPath);
                return JsonSerializer.Deserialize<AppConfig>(json) ?? new AppConfig();
            }
        }
        catch { }
        return new AppConfig();
    }

    private void SaveConfig()
    {
        var json = JsonSerializer.Serialize(_config, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(_configPath, json);
    }

    private void ExitApp()
    {
        _hotKeyWindow.Dispose();
        _trayIcon.Visible = false;
        _trayIcon.Dispose();
        Application.Exit();
    }
}

public sealed class HotKeyWindow : NativeWindow, IDisposable
{
    private const int WM_HOTKEY = 0x0312;
    private const int HOTKEY_ID = 9001;
    private const uint MOD_CONTROL = 0x0002;
    private const uint MOD_SHIFT = 0x0004;

    private readonly Action _onHotKeyPressed;

    [DllImport("user32.dll")]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll")]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    public HotKeyWindow(Action onHotKeyPressed)
    {
        _onHotKeyPressed = onHotKeyPressed;
        CreateHandle(new CreateParams());
        RegisterHotKey(Handle, HOTKEY_ID, MOD_CONTROL | MOD_SHIFT, (uint)Keys.A);
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == WM_HOTKEY && m.WParam.ToInt32() == HOTKEY_ID)
        {
            _onHotKeyPressed();
        }
        base.WndProc(ref m);
    }

    public void Dispose()
    {
        UnregisterHotKey(Handle, HOTKEY_ID);
        DestroyHandle();
    }
}