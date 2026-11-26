using System.Drawing.Drawing2D;
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
        Application.Run(new MainForm());
    }
}

public class AudioCard
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "New Card";
    public Guid? OutputId { get; set; }
    public string OutputName { get; set; } = "None";
    public Guid? InputId { get; set; }
    public string InputName { get; set; } = "None";
}

public class HotKeyBinding
{
    public uint Modifiers { get; set; } = 0x0002 | 0x0004; // Ctrl + Shift
    public Keys Key { get; set; } = Keys.A;

    public string Format()
    {
        var parts = new List<string>();
        if ((Modifiers & 0x0002) != 0) parts.Add("Ctrl");
        if ((Modifiers & 0x0004) != 0) parts.Add("Shift");
        if ((Modifiers & 0x0001) != 0) parts.Add("Alt");
        parts.Add(Key.ToString());
        return string.Join(" + ", parts);
    }
}

public class AppConfig
{
    public List<AudioCard> Cards { get; set; } = new();
    public Guid? ActiveCardId { get; set; }
    public HotKeyBinding CardHotKey { get; set; } = new() { Modifiers = 0x0006, Key = Keys.A };
    public HotKeyBinding OutputHotKey { get; set; } = new() { Modifiers = 0x0006, Key = Keys.O };
    public HotKeyBinding InputHotKey { get; set; } = new() { Modifiers = 0x0006, Key = Keys.I };
}

public sealed class MainForm : Form
{
    // Theme palette
    private static readonly Color BgMain = Color.FromArgb(18, 19, 22);
    private static readonly Color BgCard = Color.FromArgb(27, 29, 35);
    private static readonly Color BgCardHover = Color.FromArgb(35, 38, 46);
    private static readonly Color BgBorder = Color.FromArgb(46, 50, 60);
    private static readonly Color Accent = Color.FromArgb(88, 101, 242);
    private static readonly Color TextPrimary = Color.FromArgb(242, 244, 248);
    private static readonly Color TextMuted = Color.FromArgb(148, 155, 168);

    private readonly CoreAudioController _audio = new();
    private readonly string _configPath;
    private readonly NotifyIcon _trayIcon;
    private readonly HotKeyManager _hotKeys;
    private AppConfig _config;

    private readonly FlowLayoutPanel _cardsPanel;
    private readonly Label _statusLabel;
    private readonly Button _btnCardHk;
    private readonly Button _btnOutHk;
    private readonly Button _btnInHk;

    private string? _capturingHotKeyTarget;
    private bool _allowClose;

    // Drag borderless window
    [DllImport("user32.dll")] private static extern bool ReleaseCapture();
    [DllImport("user32.dll")] private static extern int SendMessage(IntPtr hWnd, int msg, int wParam, int lParam);

    public MainForm()
    {
        _configPath = Path.Combine(AppContext.BaseDirectory, "config.json");
        _config = LoadConfig();

        // Window setup
        Text = "AudioSwitcher";
        Size = new Size(370, 500);
        MinimumSize = new Size(370, 500);
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = BgMain;
        ForeColor = TextPrimary;
        DoubleBuffered = true;
        KeyPreview = true;

        // Custom Title Bar
        var titleBar = new Panel { Dock = DockStyle.Top, Height = 44, BackColor = BgMain, Padding = new Padding(16, 0, 10, 0) };
        titleBar.MouseDown += (_, e) => { if (e.Button == MouseButtons.Left) { ReleaseCapture(); SendMessage(Handle, 0xA1, 0x2, 0); } };

        var titleLbl = new Label
        {
            Text = "AUDIO SWITCHER",
            Font = new Font("Segoe UI", 9.5f, FontStyle.Bold),
            ForeColor = TextMuted,
            AutoSize = true,
            Location = new Point(16, 14)
        };
        titleLbl.MouseDown += (_, e) => { if (e.Button == MouseButtons.Left) { ReleaseCapture(); SendMessage(Handle, 0xA1, 0x2, 0); } };

        var closeBtn = MakeIconButton("×", new Point(332, 8), (_, _) => HideToTray());
        var minBtn = MakeIconButton("—", new Point(302, 8), (_, _) => HideToTray());
        titleBar.Controls.AddRange(new Control[] { titleLbl, minBtn, closeBtn });

        // Top Action Bar
        var actionBar = new Panel { Dock = DockStyle.Top, Height = 42, BackColor = BgMain, Padding = new Padding(16, 0, 16, 8) };
        _statusLabel = new Label
        {
            Text = "Select or add a card",
            Font = new Font("Segoe UI", 8.5f),
            ForeColor = TextMuted,
            AutoSize = false,
            Width = 210,
            Height = 28,
            TextAlign = ContentAlignment.MiddleLeft,
            Location = new Point(16, 2)
        };

        var addBtn = new Button
        {
            Text = "+ New Card",
            Font = new Font("Segoe UI", 8.5f, FontStyle.Bold),
            ForeColor = TextPrimary,
            BackColor = Accent,
            FlatStyle = FlatStyle.Flat,
            Size = new Size(96, 28),
            Location = new Point(256, 2),
            Cursor = Cursors.Hand
        };
        addBtn.FlatAppearance.BorderSize = 0;
        addBtn.Click += (_, _) => OpenCardEditor(null);
        actionBar.Controls.AddRange(new Control[] { _statusLabel, addBtn });

        // Cards List
        _cardsPanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            AutoScroll = true,
            BackColor = BgMain,
            Padding = new Padding(16, 4, 16, 8)
        };

        // Hotkeys Footer
        var footer = new Panel { Dock = DockStyle.Bottom, Height = 122, BackColor = BgCard, Padding = new Padding(16, 10, 16, 10) };
        var hkHeader = new Label
        {
            Text = "GLOBAL HOTKEYS (CLICK TO REBIND)",
            Font = new Font("Segoe UI", 7.5f, FontStyle.Bold),
            ForeColor = TextMuted,
            Location = new Point(16, 10),
            AutoSize = true
        };
        footer.Controls.Add(hkHeader);

        _btnCardHk = AddHotKeyRow(footer, "Cycle Cards", _config.CardHotKey.Format(), 34, "card");
        _btnOutHk = AddHotKeyRow(footer, "Cycle Output", _config.OutputHotKey.Format(), 62, "output");
        _btnInHk = AddHotKeyRow(footer, "Cycle Input", _config.InputHotKey.Format(), 90, "input");

        Controls.Add(_cardsPanel);
        Controls.Add(actionBar);
        Controls.Add(titleBar);
        Controls.Add(footer);

        // Tray Icon
        var trayMenu = new ContextMenuStrip();
        trayMenu.Items.Add("Open AudioSwitcher", null, (_, _) => ShowFromTray());
        trayMenu.Items.Add("Exit", null, (_, _) => { _allowClose = true; Close(); });

        _trayIcon = new NotifyIcon
        {
            Icon = SystemIcons.Application,
            Text = "AudioSwitcher",
            Visible = true,
            ContextMenuStrip = trayMenu
        };
        _trayIcon.Click += (_, e) => { if (e is MouseEventArgs me && me.Button == MouseButtons.Left) ShowFromTray(); };

        // Global Hotkeys
        _hotKeys = new HotKeyManager(CycleCard, CycleOutput, CycleInput);
        RegisterAllHotKeys();

        RenderCards();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        using var pen = new Pen(BgBorder, 1);
        e.Graphics.DrawRectangle(pen, 0, 0, Width - 1, Height - 1);
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (!_allowClose)
        {
            e.Cancel = true;
            HideToTray();
            return;
        }
        _hotKeys.Dispose();
        _trayIcon.Visible = false;
        _trayIcon.Dispose();
        base.OnFormClosing(e);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (_capturingHotKeyTarget != null)
        {
            e.SuppressKeyPress = true;
            if (e.KeyCode is Keys.ControlKey or Keys.ShiftKey or Keys.Menu) return;

            if (e.KeyCode == Keys.Escape)
            {
                _capturingHotKeyTarget = null;
                UpdateHotKeyButtons();
                return;
            }

            uint mods = 0;
            if (e.Control) mods |= 0x0002;
            if (e.Shift) mods |= 0x0004;
            if (e.Alt) mods |= 0x0001;
            if (mods == 0) mods = 0x0002 | 0x0004; // fallback to Ctrl+Shift if no modifier pressed

            var binding = new HotKeyBinding { Modifiers = mods, Key = e.KeyCode };
            if (_capturingHotKeyTarget == "card") _config.CardHotKey = binding;
            else if (_capturingHotKeyTarget == "output") _config.OutputHotKey = binding;
            else if (_capturingHotKeyTarget == "input") _config.InputHotKey = binding;

            _capturingHotKeyTarget = null;
            SaveConfig();
            RegisterAllHotKeys();
            UpdateHotKeyButtons();
            return;
        }
        base.OnKeyDown(e);
    }

    private Button MakeIconButton(string text, Point loc, EventHandler onClick)
    {
        var btn = new Button
        {
            Text = text,
            Font = new Font("Segoe UI", 10f),
            ForeColor = TextMuted,
            BackColor = BgMain,
            FlatStyle = FlatStyle.Flat,
            Size = new Size(28, 28),
            Location = loc,
            Cursor = Cursors.Hand
        };
        btn.FlatAppearance.BorderSize = 0;
        btn.FlatAppearance.MouseOverBackColor = BgCardHover;
        btn.Click += onClick;
        return btn;
    }

    private Button AddHotKeyRow(Panel parent, string label, string value, int y, string target)
    {
        var lbl = new Label
        {
            Text = label,
            Font = new Font("Segoe UI", 8.5f),
            ForeColor = TextPrimary,
            Location = new Point(16, y + 3),
            AutoSize = true
        };
        var btn = new Button
        {
            Text = value,
            Font = new Font("Segoe UI", 8f, FontStyle.Bold),
            ForeColor = TextPrimary,
            BackColor = BgMain,
            FlatStyle = FlatStyle.Flat,
            Size = new Size(136, 22),
            Location = new Point(216, y),
            Cursor = Cursors.Hand
        };
        btn.FlatAppearance.BorderColor = BgBorder;
        btn.Click += (_, _) =>
        {
            _capturingHotKeyTarget = target;
            UpdateHotKeyButtons();
            btn.Text = "Press keys...";
            btn.ForeColor = Accent;
        };
        parent.Controls.AddRange(new Control[] { lbl, btn });
        return btn;
    }

    private void UpdateHotKeyButtons()
    {
        _btnCardHk.Text = _config.CardHotKey.Format(); _btnCardHk.ForeColor = TextPrimary;
        _btnOutHk.Text = _config.OutputHotKey.Format(); _btnOutHk.ForeColor = TextPrimary;
        _btnInHk.Text = _config.InputHotKey.Format(); _btnInHk.ForeColor = TextPrimary;
    }

    private void RegisterAllHotKeys()
    {
        _hotKeys.Register(_config.CardHotKey, _config.OutputHotKey, _config.InputHotKey);
    }

    private void RenderCards()
    {
        _cardsPanel.Controls.Clear();

        if (_config.Cards.Count == 0)
        {
            var empty = new Label
            {
                Text = "No cards yet.\nClick '+ New Card' to create your first audio profile.",
                Font = new Font("Segoe UI", 9f),
                ForeColor = TextMuted,
                Size = new Size(330, 80),
                TextAlign = ContentAlignment.MiddleCenter
            };
            _cardsPanel.Controls.Add(empty);
            return;
        }

        foreach (var card in _config.Cards)
        {
            var isActive = _config.ActiveCardId == card.Id;
            var cardPanel = new Panel
            {
                Size = new Size(334, 74),
                BackColor = BgCard,
                Margin = new Padding(0, 0, 0, 8),
                Cursor = Cursors.Hand
            };

            cardPanel.Paint += (_, e) =>
            {
                using var borderPen = new Pen(isActive ? Accent : BgBorder, 1);
                e.Graphics.DrawRectangle(borderPen, 0, 0, cardPanel.Width - 1, cardPanel.Height - 1);
                if (isActive)
                {
                    using var accentBrush = new SolidBrush(Accent);
                    e.Graphics.FillRectangle(accentBrush, 0, 0, 4, cardPanel.Height);
                }
            };

            var nameLbl = new Label
            {
                Text = card.Name,
                Font = new Font("Segoe UI", 10f, FontStyle.Bold),
                ForeColor = TextPrimary,
                Location = new Point(14, 10),
                Size = new Size(240, 20),
                AutoEllipsis = true
            };
            var outLbl = new Label
            {
                Text = $"OUT  {card.OutputName}",
                Font = new Font("Segoe UI", 8f),
                ForeColor = TextMuted,
                Location = new Point(14, 33),
                Size = new Size(245, 16),
                AutoEllipsis = true
            };
            var inLbl = new Label
            {
                Text = $"IN     {card.InputName}",
                Font = new Font("Segoe UI", 8f),
                ForeColor = TextMuted,
                Location = new Point(14, 50),
                Size = new Size(245, 16),
                AutoEllipsis = true
            };

            var editBtn = new Button
            {
                Text = "Edit",
                Font = new Font("Segoe UI", 7.5f),
                ForeColor = TextMuted,
                BackColor = Color.Transparent,
                FlatStyle = FlatStyle.Flat,
                Size = new Size(40, 22),
                Location = new Point(260, 10),
                Cursor = Cursors.Hand
            };
            editBtn.FlatAppearance.BorderSize = 0;
            editBtn.Click += (_, _) => OpenCardEditor(card);

            var delBtn = new Button
            {
                Text = "×",
                Font = new Font("Segoe UI", 9.5f),
                ForeColor = TextMuted,
                BackColor = Color.Transparent,
                FlatStyle = FlatStyle.Flat,
                Size = new Size(24, 22),
                Location = new Point(302, 10),
                Cursor = Cursors.Hand
            };
            delBtn.FlatAppearance.BorderSize = 0;
            delBtn.Click += (_, _) =>
            {
                _config.Cards.Remove(card);
                if (_config.ActiveCardId == card.Id) _config.ActiveCardId = null;
                SaveConfig();
                RenderCards();
            };

            EventHandler activate = (_, _) => ActivateCard(card);
            cardPanel.Click += activate;
            nameLbl.Click += activate;
            outLbl.Click += activate;
            inLbl.Click += activate;

            cardPanel.Controls.AddRange(new Control[] { nameLbl, outLbl, inLbl, editBtn, delBtn });
            _cardsPanel.Controls.Add(cardPanel);
        }
    }

    private void OpenCardEditor(AudioCard? existing)
    {
        var outputs = _audio.GetPlaybackDevices(DeviceState.Active).ToList();
        var inputs = _audio.GetCaptureDevices(DeviceState.Active).ToList();

        using var dlg = new Form
        {
            Text = existing == null ? "New Card" : "Edit Card",
            Size = new Size(340, 260),
            FormBorderStyle = FormBorderStyle.FixedDialog,
            StartPosition = FormStartPosition.CenterParent,
            BackColor = BgMain,
            ForeColor = TextPrimary,
            MaximizeBox = false,
            MinimizeBox = false
        };

        var nameLbl = new Label { Text = "Card Name", Location = new Point(16, 14), AutoSize = true, ForeColor = TextMuted };
        var nameBox = new TextBox
        {
            Text = existing?.Name ?? "",
            Location = new Point(16, 34),
            Width = 290,
            BackColor = BgCard,
            ForeColor = TextPrimary,
            BorderStyle = BorderStyle.FixedSingle
        };

        var outLbl = new Label { Text = "Output Device (Speakers / Headset)", Location = new Point(16, 68), AutoSize = true, ForeColor = TextMuted };
        var outCombo = new ComboBox
        {
            Location = new Point(16, 88),
            Width = 290,
            DropDownStyle = ComboBoxStyle.DropDownList,
            BackColor = BgCard,
            ForeColor = TextPrimary
        };
        foreach (var d in outputs) outCombo.Items.Add(new DeviceItem(d.Id, d.FullName));
        if (existing?.OutputId != null)
            outCombo.SelectedIndex = outputs.FindIndex(d => d.Id == existing.OutputId);
        else if (outputs.Count > 0) outCombo.SelectedIndex = 0;

        var inLbl = new Label { Text = "Input Device (Microphone)", Location = new Point(16, 122), AutoSize = true, ForeColor = TextMuted };
        var inCombo = new ComboBox
        {
            Location = new Point(16, 142),
            Width = 290,
            DropDownStyle = ComboBoxStyle.DropDownList,
            BackColor = BgCard,
            ForeColor = TextPrimary
        };
        foreach (var d in inputs) inCombo.Items.Add(new DeviceItem(d.Id, d.FullName));
        if (existing?.InputId != null)
            inCombo.SelectedIndex = inputs.FindIndex(d => d.Id == existing.InputId);
        else if (inputs.Count > 0) inCombo.SelectedIndex = 0;

        var saveBtn = new Button
        {
            Text = "Save Card",
            BackColor = Accent,
            ForeColor = TextPrimary,
            FlatStyle = FlatStyle.Flat,
            Size = new Size(100, 30),
            Location = new Point(206, 180)
        };
        saveBtn.FlatAppearance.BorderSize = 0;
        saveBtn.Click += (_, _) =>
        {
            var card = existing ?? new AudioCard();
            card.Name = string.IsNullOrWhiteSpace(nameBox.Text) ? "Untitled Card" : nameBox.Text.Trim();
            if (outCombo.SelectedItem is DeviceItem o) { card.OutputId = o.Id; card.OutputName = o.Name; }
            if (inCombo.SelectedItem is DeviceItem i) { card.InputId = i.Id; card.InputName = i.Name; }

            if (existing == null) _config.Cards.Add(card);
            SaveConfig();
            RenderCards();
            dlg.DialogResult = DialogResult.OK;
        };

        dlg.Controls.AddRange(new Control[] { nameLbl, nameBox, outLbl, outCombo, inLbl, inCombo, saveBtn });
        dlg.ShowDialog(this);
    }

    private void ActivateCard(AudioCard card)
    {
        if (card.OutputId.HasValue)
        {
            var dev = _audio.GetDevice(card.OutputId.Value);
            dev?.SetAsDefault();
            dev?.SetAsDefaultCommunications();
        }
        if (card.InputId.HasValue)
        {
            var dev = _audio.GetDevice(card.InputId.Value);
            dev?.SetAsDefault();
            dev?.SetAsDefaultCommunications();
        }

        _config.ActiveCardId = card.Id;
        SaveConfig();
        RenderCards();
        Notify($"Card: {card.Name}");
    }

    private void CycleCard()
    {
        if (_config.Cards.Count == 0) return;
        var idx = _config.Cards.FindIndex(c => c.Id == _config.ActiveCardId);
        var next = _config.Cards[(idx + 1) % _config.Cards.Count];
        ActivateCard(next);
    }

    private void CycleOutput()
    {
        var devices = _audio.GetPlaybackDevices(DeviceState.Active).ToList();
        if (devices.Count < 2) return;
        var current = _audio.DefaultPlaybackDevice;
        var idx = current != null ? devices.FindIndex(d => d.Id == current.Id) : -1;
        var next = devices[(idx + 1) % devices.Count];
        next.SetAsDefault();
        next.SetAsDefaultCommunications();
        Notify($"Output: {next.FullName}");
    }

    private void CycleInput()
    {
        var devices = _audio.GetCaptureDevices(DeviceState.Active).ToList();
        if (devices.Count < 2) return;
        var current = _audio.DefaultCaptureDevice;
        var idx = current != null ? devices.FindIndex(d => d.Id == current.Id) : -1;
        var next = devices[(idx + 1) % devices.Count];
        next.SetAsDefault();
        next.SetAsDefaultCommunications();
        Notify($"Input: {next.FullName}");
    }

    private void Notify(string message)
    {
        _statusLabel.Text = message;
        _trayIcon.ShowBalloonTip(1200, "AudioSwitcher", message, ToolTipIcon.Info);
    }

    private void HideToTray() { Hide(); }
    private void ShowFromTray() { Show(); WindowState = FormWindowState.Normal; Activate(); }

    private AppConfig LoadConfig()
    {
        try
        {
            if (File.Exists(_configPath))
                return JsonSerializer.Deserialize<AppConfig>(File.ReadAllText(_configPath)) ?? new AppConfig();
        }
        catch { }
        return new AppConfig();
    }

    private void SaveConfig()
    {
        File.WriteAllText(_configPath, JsonSerializer.Serialize(_config, new JsonSerializerOptions { WriteIndented = true }));
    }

    private record DeviceItem(Guid Id, string Name) { public override string ToString() => Name; }
}

public sealed class HotKeyManager : NativeWindow, IDisposable
{
    private const int WM_HOTKEY = 0x0312;
    private readonly Action _onCard, _onOutput, _onInput;

    [DllImport("user32.dll")] private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);
    [DllImport("user32.dll")] private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    public HotKeyManager(Action onCard, Action onOutput, Action onInput)
    {
        _onCard = onCard; _onOutput = onOutput; _onInput = onInput;
        CreateHandle(new CreateParams());
    }

    public void Register(HotKeyBinding card, HotKeyBinding output, HotKeyBinding input)
    {
        UnregisterHotKey(Handle, 1);
        UnregisterHotKey(Handle, 2);
        UnregisterHotKey(Handle, 3);
        RegisterHotKey(Handle, 1, card.Modifiers, (uint)card.Key);
        RegisterHotKey(Handle, 2, output.Modifiers, (uint)output.Key);
        RegisterHotKey(Handle, 3, input.Modifiers, (uint)input.Key);
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == WM_HOTKEY)
        {
            switch (m.WParam.ToInt32())
            {
                case 1: _onCard(); break;
                case 2: _onOutput(); break;
                case 3: _onInput(); break;
            }
        }
        base.WndProc(ref m);
    }

    public void Dispose()
    {
        UnregisterHotKey(Handle, 1);
        UnregisterHotKey(Handle, 2);
        UnregisterHotKey(Handle, 3);
        DestroyHandle();
    }
}