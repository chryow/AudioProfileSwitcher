using Microsoft.Win32;

namespace AudioProfileSwitcher;

public sealed class MainForm : Form
{
    private const string StartupRegKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";
    private const string AppRegName = "AudioProfileSwitcher";

    private readonly ConfigStore _store = new();
    private readonly AppConfig _config;
    private readonly AudioService _audio = new();
    private readonly HotKeyService _hotKeys;
    private readonly OsdBannerForm _osd = new();
    private readonly MuteBannerForm _muteBanner = new();
    private readonly NotifyIcon _tray;
    private readonly ContextMenuStrip _trayMenu = new();
    private readonly ToolTip _tip = new();
    private readonly System.Windows.Forms.Timer _pollTimer = new() { Interval = 1500 };
    private readonly System.Windows.Forms.Timer _statusTimer = new() { Interval = 6000 };
    private readonly System.Windows.Forms.Timer _debounceTimer = new() { Interval = 2000 };
    private readonly System.Windows.Forms.Timer _recoveryTimer = new() { Interval = 1000 };
    private readonly bool _startMinimized;

    private readonly FlowLayoutPanel _cardsPanel;
    private readonly TableLayoutPanel _emptyPanel;
    private readonly Panel _settingsPanel;
    private readonly Label _nowOut;
    private readonly Label _nowIn;
    private readonly Label _statusLabel;
    private readonly Button _gearBtn;
    private readonly Dictionary<HotKeyTarget, Button> _hkButtons = new();
    private Label _jumpLabel = null!;
    private DarkCombo _fallbackCombo = null!;

    // Auto-activate debounce & flapping guard
    private readonly HashSet<Guid> _pendingNewDevices = new();
    private readonly Dictionary<Guid, Queue<DateTime>> _connectHistory = new();

    private bool _settingsOpen;
    private bool _updatingFallbackCombo;
    private bool _polling;
    private HotKeyTarget? _capturing;
    private AudioSnapshot? _snap;
    private string _lastSignature = "";
    private bool _micMuted;
    private bool _allowClose;
    private int _dropIndex = -1;
    private int _dropLineY = -1;

    private sealed record CardChoice(Guid? Id, string Name)
    {
        public override string ToString() => Name;
    }

    public MainForm(bool startMinimized = false)
    {
        _startMinimized = startMinimized;
        _config = _store.Load();
        _hotKeys = new HotKeyService();

        // ---- window ----
        AutoScaleDimensions = new SizeF(96F, 96F);
        AutoScaleMode = AutoScaleMode.Dpi;
        Text = "AudioSwitcher";
        Icon = TrayIcons.Normal;
        Size = new Size(380, 620);
        MinimumSize = new Size(380, 620);
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = Theme.BgMain;
        ForeColor = Theme.TextPrimary;
        Padding = new Padding(1); // room for the painted border
        DoubleBuffered = true;

        // ---- title bar ----
        var titleBar = new Panel { Dock = DockStyle.Top, Height = 44, BackColor = Theme.BgMain };
        var titleLbl = new Label
        {
            Text = "AUDIO SWITCHER",
            Font = Theme.Header,
            ForeColor = Theme.TextMuted,
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            Padding = new Padding(16, 0, 0, 0)
        };
        MouseEventHandler drag = (_, e) => { if (e.Button == MouseButtons.Left) Native.DragWindow(Handle); };
        titleBar.MouseDown += drag;
        titleLbl.MouseDown += drag;

        _gearBtn = MakeIconButton(Theme.Glyph("\uE713", "⚙"), "Settings & hotkeys", (_, _) => ToggleSettings(), Theme.BgCardHover);
        var minBtn = MakeIconButton(Theme.Glyph("\uE921", "—"), "Minimize", (_, _) => WindowState = FormWindowState.Minimized, Theme.BgCardHover);
        var closeBtn = MakeIconButton(Theme.Glyph("\uE8BB", "×"), "Hide to tray (keeps running)", (_, _) => HideToTray(), Theme.Danger);
        titleBar.Controls.Add(titleLbl);
        titleBar.Controls.Add(_gearBtn);
        titleBar.Controls.Add(minBtn);
        titleBar.Controls.Add(closeBtn);

        // ---- live "now" panel ----
        var nowPanel = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            Height = 58,
            ColumnCount = 2,
            RowCount = 2,
            BackColor = Theme.BgMain,
            Padding = new Padding(16, 2, 16, 6)
        };
        nowPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 40));
        nowPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        nowPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
        nowPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
        nowPanel.Controls.Add(MakeTag("OUT"), 0, 0);
        nowPanel.Controls.Add(_nowOut = MakeNowLabel(), 1, 0);
        nowPanel.Controls.Add(MakeTag("IN"), 0, 1);
        nowPanel.Controls.Add(_nowIn = MakeNowLabel(), 1, 1);

        // ---- action bar ----
        var actionBar = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            Height = 40,
            BackColor = Theme.BgMain,
            Padding = new Padding(16, 4, 16, 4),
            WrapContents = false
        };
        var addBtn = new Button
        {
            Text = "+ New Card",
            Font = Theme.BodyBold,
            ForeColor = Theme.TextPrimary,
            BackColor = Theme.Accent,
            FlatStyle = FlatStyle.Flat,
            Size = new Size(110, 30),
            Margin = new Padding(0, 0, 12, 0),
            Cursor = Cursors.Hand
        };
        addBtn.FlatAppearance.BorderSize = 0;
        addBtn.Click += (_, _) => _ = OpenCardEditorAsync(NewCardFromCurrentDevices(), true);
        var hintLbl = new Label
        {
            Text = "Click to switch · drag to reorder",
            Font = Theme.Small,
            ForeColor = Theme.TextMuted,
            AutoSize = true,
            Margin = new Padding(0, 8, 0, 0)
        };
        actionBar.Controls.Add(addBtn);
        actionBar.Controls.Add(hintLbl);

        // ---- cards list (with drag-to-reorder) ----
        _cardsPanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            AutoScroll = true,
            BackColor = Theme.BgMain,
            Padding = new Padding(16, 4, 16, 8),
            AllowDrop = true,
            Visible = false
        };
        _cardsPanel.ClientSizeChanged += (_, _) => ResizeCards();
        _cardsPanel.DragEnter += OnCardsDragEnter;
        _cardsPanel.DragOver += OnCardsDragOver;
        _cardsPanel.DragLeave += (_, _) => { _dropLineY = -1; _dropIndex = -1; _cardsPanel.Invalidate(); };
        _cardsPanel.DragDrop += OnCardsDragDrop;
        _cardsPanel.Paint += (_, e) =>
        {
            if (_dropLineY < 0) return;
            using var pen = new Pen(Theme.Accent, 2);
            e.Graphics.DrawLine(pen, _cardsPanel.Padding.Left, _dropLineY, _cardsPanel.ClientSize.Width - _cardsPanel.Padding.Right, _dropLineY);
        };

        _emptyPanel = BuildEmptyPanel();
        _settingsPanel = BuildSettingsPanel();

        var content = new Panel { Dock = DockStyle.Fill, BackColor = Theme.BgMain };
        content.Controls.Add(_cardsPanel);
        content.Controls.Add(_emptyPanel);
        content.Controls.Add(_settingsPanel);

        // ---- status bar ----
        _statusLabel = new Label
        {
            Dock = DockStyle.Bottom,
            Height = 26,
            Font = Theme.Small,
            ForeColor = Theme.TextMuted,
            BackColor = Theme.BgCard,
            TextAlign = ContentAlignment.MiddleLeft,
            Padding = new Padding(16, 0, 16, 0),
            AutoEllipsis = true,
            Text = "Ready"
        };
        _statusTimer.Tick += (_, _) => { _statusTimer.Stop(); _statusLabel.Text = "Ready"; _statusLabel.ForeColor = Theme.TextMuted; };

        Controls.Add(content);
        Controls.Add(_statusLabel);
        Controls.Add(actionBar);
        Controls.Add(nowPanel);
        Controls.Add(titleBar);

        // ---- dark tray menu ----
        _trayMenu.Renderer = new DarkMenuRenderer();
        _trayMenu.BackColor = Theme.BgCard;
        _trayMenu.ForeColor = Theme.TextPrimary;
        _trayMenu.Font = Theme.Body;
        _trayMenu.Opening += (_, _) => RebuildTrayMenu();

        _tray = new NotifyIcon
        {
            Icon = TrayIcons.Normal,
            Text = "AudioSwitcher",
            Visible = true,
            ContextMenuStrip = _trayMenu
        };
        _tray.MouseClick += (_, e) => { if (e.Button == MouseButtons.Left) ShowFromTray(); };

        // ---- timers & system events ----
        _debounceTimer.Tick += (_, _) => { _debounceTimer.Stop(); ProcessDebouncedAutoActivate(); };
        _recoveryTimer.Tick += (_, _) =>
        {
            _recoveryTimer.Stop();
            AppLog.Info("Recovering hotkeys and audio state after power/session event.");
            RegisterAllHotKeys();
            _ = ForceRefreshAsync();
        };

        SystemEvents.PowerModeChanged += OnPowerModeChanged;
        SystemEvents.SessionSwitch += OnSessionSwitch;

        // ---- go ----
        RenderCards();
        _ = RefreshStateAsync(false, force: true);
        _pollTimer.Tick += (_, _) => _ = RefreshStateAsync(true);
        _pollTimer.Start();

        if (_store.LoadWarning != null) SetStatus(_store.LoadWarning, true);
    }

    // =========================================================================================
    // Window plumbing & power/session recovery
    // =========================================================================================

    private void OnPowerModeChanged(object? sender, PowerModeChangedEventArgs e)
    {
        if (e.Mode != PowerModes.Resume) return;
        ScheduleRecovery();
    }

    private void OnSessionSwitch(object? sender, SessionSwitchEventArgs e)
    {
        if (e.Reason is SessionSwitchReason.SessionUnlock or SessionSwitchReason.ConsoleConnect or SessionSwitchReason.RemoteConnect)
            ScheduleRecovery();
    }

    private void ScheduleRecovery()
    {
        if (IsDisposed || !IsHandleCreated) return;
        try
        {
            BeginInvoke(new Action(() =>
            {
                if (IsDisposed) return;
                _recoveryTimer.Stop();
                _recoveryTimer.Start();
            }));
        }
        catch { }
    }

    protected override void SetVisibleCore(bool value)
    {
        if (_startMinimized && !IsHandleCreated)
        {
            CreateHandle();
            base.SetVisibleCore(false);
            return;
        }
        base.SetVisibleCore(value);
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        Native.RoundCorners(Handle);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        using var pen = new Pen(Theme.BgBorder, 1);
        e.Graphics.DrawRectangle(pen, 0, 0, Width - 1, Height - 1);
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (!_allowClose && e.CloseReason == CloseReason.UserClosing)
        {
            e.Cancel = true;
            HideToTray();
            return;
        }

        SystemEvents.PowerModeChanged -= OnPowerModeChanged;
        SystemEvents.SessionSwitch -= OnSessionSwitch;

        _pollTimer.Stop();
        _statusTimer.Stop();
        _debounceTimer.Stop();
        _recoveryTimer.Stop();
        _pollTimer.Dispose();
        _statusTimer.Dispose();
        _debounceTimer.Dispose();
        _recoveryTimer.Dispose();
        _hotKeys.Dispose();
        _osd.Close();
        _muteBanner.Close();
        _tray.Visible = false;
        _tray.Dispose();
        _trayMenu.Dispose();
        _tip.Dispose();
        _audio.Dispose();
        base.OnFormClosing(e);
    }

    private void HideToTray()
    {
        Hide();
        if (!_config.TrayHintShown)
        {
            _config.TrayHintShown = true;
            Save();
            _tray.ShowBalloonTip(3000, "AudioSwitcher",
                "Still running in the tray. Right-click the icon to switch cards or exit.", ToolTipIcon.Info);
        }
    }

    public void ShowFromTray()
    {
        Show();
        WindowState = FormWindowState.Normal;
        Activate();
    }

    private void Save()
    {
        var error = _store.Save(_config);
        if (error != null) SetStatus("Couldn't save settings: " + error, true);
    }

    private void SetStatus(string text, bool warning)
    {
        _statusLabel.Text = text;
        _statusLabel.ForeColor = warning ? Theme.Warning : Theme.TextMuted;
        _statusTimer.Stop();
        _statusTimer.Start();
    }

    // =========================================================================================
    // UI builders
    // =========================================================================================

    private Button MakeIconButton(string glyph, string tip, EventHandler onClick, Color hover)
    {
        var btn = new Button
        {
            Text = glyph,
            Font = Theme.IconFont,
            ForeColor = Theme.TextMuted,
            BackColor = Theme.BgMain,
            FlatStyle = FlatStyle.Flat,
            Dock = DockStyle.Right,
            Width = 40,
            Cursor = Cursors.Hand,
            TabStop = false
        };
        btn.FlatAppearance.BorderSize = 0;
        btn.FlatAppearance.MouseOverBackColor = hover;
        btn.FlatAppearance.MouseDownBackColor = hover;
        btn.Click += onClick;
        _tip.SetToolTip(btn, tip);
        return btn;
    }

    private static Label MakeTag(string text) => new()
    {
        Text = text,
        Font = Theme.SmallBold,
        ForeColor = Theme.TextMuted,
        Dock = DockStyle.Fill,
        TextAlign = ContentAlignment.MiddleLeft
    };

    private static Label MakeNowLabel() => new()
    {
        Font = Theme.BodyBold,
        ForeColor = Theme.TextPrimary,
        Dock = DockStyle.Fill,
        TextAlign = ContentAlignment.MiddleLeft,
        AutoEllipsis = true,
        Text = "—"
    };

    private TableLayoutPanel BuildEmptyPanel()
    {
        var t = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 4,
            BackColor = Theme.BgMain,
            Visible = false
        };
        t.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        t.RowStyles.Add(new RowStyle(SizeType.Percent, 35));
        t.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        t.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        t.RowStyles.Add(new RowStyle(SizeType.Percent, 65));

        var lbl = new Label
        {
            Text = "No cards yet.\nA card remembers an output + input pair, so one click or hotkey switches both.",
            Font = Theme.Body,
            ForeColor = Theme.TextMuted,
            AutoSize = false,
            Size = new Size(300, 70),
            TextAlign = ContentAlignment.MiddleCenter,
            Anchor = AnchorStyles.None
        };
        var btn = new Button
        {
            Text = "Create card from current devices",
            Font = Theme.BodyBold,
            ForeColor = Theme.TextPrimary,
            BackColor = Theme.Accent,
            FlatStyle = FlatStyle.Flat,
            Size = new Size(240, 34),
            Anchor = AnchorStyles.None,
            Cursor = Cursors.Hand
        };
        btn.FlatAppearance.BorderSize = 0;
        btn.Click += (_, _) => _ = OpenCardEditorAsync(NewCardFromCurrentDevices(), true);

        t.Controls.Add(lbl, 0, 1);
        t.Controls.Add(btn, 0, 2);
        return t;
    }

    private Panel BuildSettingsPanel()
    {
        var panel = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = Theme.BgCard,
            Padding = new Padding(16, 8, 16, 12),
            AutoScroll = true,
            Visible = false
        };

        var t = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            ColumnCount = 2,
            BackColor = Theme.BgCard
        };
        t.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        t.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 146));

        AddHeader(t, "GLOBAL HOTKEYS (CLICK TO REBIND)");
        AddHotKeyRow(t, "Cycle Cards", HotKeyTarget.Card);
        AddHotKeyRow(t, "Cycle Output", HotKeyTarget.Output);
        AddHotKeyRow(t, "Cycle Input", HotKeyTarget.Input);
        AddHotKeyRow(t, "Toggle Mic Mute", HotKeyTarget.Mute);

        _jumpLabel = MakeRowLabel("", 36);
        t.Controls.Add(_jumpLabel);
        t.Controls.Add(NewToggle(_config.JumpHotKeysEnabled, on =>
        {
            _config.JumpHotKeysEnabled = on;
            Save();
            RegisterAllHotKeys();
            RefreshCardSlots();
        }));

        AddHeader(t, "BEHAVIOR");
        t.Controls.Add(MakeRowLabel("Fallback card on disconnect", 34));
        _fallbackCombo = new DarkCombo { Size = new Size(140, 24), Anchor = AnchorStyles.Right };
        _fallbackCombo.SelectedIndexChanged += (_, _) =>
        {
            if (_updatingFallbackCombo) return;
            _config.FallbackCardId = (_fallbackCombo.SelectedItem as CardChoice)?.Id;
            Save();
        };
        t.Controls.Add(_fallbackCombo);

        t.Controls.Add(MakeRowLabel("Play confirmation chime on switch"));
        t.Controls.Add(NewToggle(_config.ChimeEnabled, on => { _config.ChimeEnabled = on; Save(); }));
        t.Controls.Add(MakeRowLabel("Start with Windows (minimized to tray)"));
        t.Controls.Add(NewToggle(IsStartupEnabled(), SetStartupSafe));

        var filterBtn = new Button
        {
            Text = "Choose devices for cycling…",
            Font = Theme.Body,
            ForeColor = Theme.TextPrimary,
            BackColor = Theme.BgMain,
            FlatStyle = FlatStyle.Flat,
            Dock = DockStyle.Fill,
            Height = 30,
            Margin = new Padding(0, 12, 0, 0),
            Cursor = Cursors.Hand
        };
        filterBtn.FlatAppearance.BorderColor = Theme.BgBorder;
        filterBtn.Click += (_, _) => _ = OpenDeviceFilterDialogAsync();
        t.Controls.Add(filterBtn);
        t.SetColumnSpan(filterBtn, 2);

        var pathLbl = new Label
        {
            Text = $"Settings: {_store.FilePath}\nLog: {AppLog.FilePath}",
            Font = Theme.Small,
            ForeColor = Theme.TextMuted,
            AutoSize = false,
            Dock = DockStyle.Fill,
            Height = 56,
            Margin = new Padding(0, 12, 0, 0)
        };
        t.Controls.Add(pathLbl);
        t.SetColumnSpan(pathLbl, 2);

        panel.Controls.Add(t);
        UpdateHotKeyButtons();
        RefreshFallbackCombo();
        return panel;
    }

    private void RefreshFallbackCombo()
    {
        if (_fallbackCombo == null) return;
        _updatingFallbackCombo = true;
        try
        {
            _fallbackCombo.Items.Clear();
            _fallbackCombo.Items.Add(new CardChoice(null, "(None)"));
            foreach (var c in _config.Cards)
                _fallbackCombo.Items.Add(new CardChoice(c.Id, c.Name));

            int idx = _config.FallbackCardId.HasValue
                ? _config.Cards.FindIndex(c => c.Id == _config.FallbackCardId.Value) + 1
                : 0;
            _fallbackCombo.SelectedIndex = idx >= 0 && idx < _fallbackCombo.Items.Count ? idx : 0;
        }
        finally
        {
            _updatingFallbackCombo = false;
        }
    }

    private static Label MakeRowLabel(string text, int height = 36) => new()
    {
        Text = text,
        Font = Theme.Body,
        ForeColor = Theme.TextPrimary,
        AutoSize = false,
        Dock = DockStyle.Fill,
        Height = height,
        TextAlign = ContentAlignment.MiddleLeft
    };

    private static void AddHeader(TableLayoutPanel t, string text)
    {
        var h = new Label
        {
            Text = text,
            Font = Theme.SmallBold,
            ForeColor = Theme.TextMuted,
            AutoSize = false,
            Dock = DockStyle.Fill,
            Height = 28,
            TextAlign = ContentAlignment.BottomLeft,
            Margin = new Padding(0, 8, 0, 2)
        };
        t.Controls.Add(h);
        t.SetColumnSpan(h, 2);
    }

    private void AddHotKeyRow(TableLayoutPanel t, string text, HotKeyTarget target)
    {
        var btn = new Button
        {
            Font = Theme.SmallBold,
            ForeColor = Theme.TextPrimary,
            BackColor = Theme.BgMain,
            FlatStyle = FlatStyle.Flat,
            Size = new Size(140, 26),
            Anchor = AnchorStyles.Right,
            Cursor = Cursors.Hand
        };
        btn.FlatAppearance.BorderColor = Theme.BgBorder;
        btn.Click += (_, _) => BeginCapture(target);
        btn.LostFocus += (_, _) => { if (_capturing == target) CancelCapture(); };
        _hkButtons[target] = btn;

        t.Controls.Add(MakeRowLabel(text, 30));
        t.Controls.Add(btn);
    }

    private static ToggleSwitch NewToggle(bool value, Action<bool> onChange)
    {
        var sw = new ToggleSwitch { Checked = value, Anchor = AnchorStyles.Right };
        sw.CheckedChanged += (_, _) => onChange(sw.Checked);
        return sw;
    }

    // =========================================================================================
    // Views
    // =========================================================================================

    private void ToggleSettings()
    {
        if (_capturing != null) CancelCapture();
        _settingsOpen = !_settingsOpen;
        UpdateViews();
    }

    private void UpdateViews()
    {
        bool hasCards = _config.Cards.Count > 0;
        _settingsPanel.Visible = _settingsOpen;
        _cardsPanel.Visible = !_settingsOpen && hasCards;
        _emptyPanel.Visible = !_settingsOpen && !hasCards;
        _gearBtn.BackColor = _settingsOpen ? Theme.BgCardHover : Theme.BgMain;
        _gearBtn.ForeColor = _settingsOpen ? Theme.Accent : Theme.TextMuted;
    }

    private string? SlotFor(int index) => _config.JumpHotKeysEnabled && index < 9 ? (index + 1).ToString() : null;

    private void RenderCards()
    {
        _cardsPanel.SuspendLayout();
        var old = _cardsPanel.Controls.Cast<Control>().ToList();
        _cardsPanel.Controls.Clear();
        foreach (var c in old) c.Dispose();

        for (int i = 0; i < _config.Cards.Count; i++)
        {
            var cc = new CardControl(_config.Cards[i]) { SlotText = SlotFor(i) };
            cc.ActivateRequested += card => _ = ActivateCardAsync(card);
            cc.EditRequested += card => _ = OpenCardEditorAsync(card, false);
            cc.DeleteRequested += card =>
            {
                _config.Cards.Remove(card);
                if (_config.ActiveCardId == card.Id) _config.ActiveCardId = null;
                if (_config.FallbackCardId == card.Id) _config.FallbackCardId = null;
                Save();
                QueueRender();
            };
            _cardsPanel.Controls.Add(cc);
        }

        ResizeCards();
        RefreshCardStates();
        RefreshFallbackCombo();
        _cardsPanel.ResumeLayout(true);
        UpdateViews();
        RegisterAllHotKeys();
    }

    private void QueueRender()
    {
        if (IsHandleCreated) BeginInvoke(new Action(RenderCards));
        else RenderCards();
    }

    private void ResizeCards()
    {
        float k = _cardsPanel.DeviceDpi / 96f;
        int width = Math.Max((int)(220 * k), _cardsPanel.ClientSize.Width - _cardsPanel.Padding.Horizontal);
        foreach (var cc in _cardsPanel.Controls.OfType<CardControl>())
        {
            cc.Scale = k;
            cc.Margin = new Padding(0, 0, 0, (int)(8 * k));
            cc.Size = new Size(width, (int)(78 * k));
        }
    }

    private void RefreshCardSlots()
    {
        int i = 0;
        foreach (var cc in _cardsPanel.Controls.OfType<CardControl>())
            cc.SlotText = SlotFor(i++);
    }

    private void RefreshCardStates()
    {
        var snap = _snap;
        foreach (var cc in _cardsPanel.Controls.OfType<CardControl>())
        {
            var c = cc.Card;
            cc.IsActive = snap != null && CardIsActive(c, snap);
            cc.OutMissing = snap != null && c.OutputId.HasValue && !snap.OutputIds.Contains(c.OutputId.Value);
            cc.InMissing = snap != null && c.InputId.HasValue && !snap.InputIds.Contains(c.InputId.Value);
        }
    }

    private void UpdateNow()
    {
        _nowOut.Text = _snap?.OutName ?? "No output device";
        _nowIn.Text = (_snap?.InName ?? "No input device") + (_micMuted ? "   ● MUTED" : "");
        _nowIn.ForeColor = _micMuted ? Theme.Danger : Theme.TextPrimary;
        UpdateTrayTooltip();
    }

    private void UpdateTrayTooltip()
    {
        var lines = new List<string>();
        var activeCard = _snap != null ? _config.Cards.FirstOrDefault(c => CardIsActive(c, _snap)) : null;
        lines.Add(activeCard != null ? $"Card: {Shorten(activeCard.Name, 30)}" : "AudioSwitcher");

        if (!string.IsNullOrEmpty(_snap?.OutName))
            lines.Add("Out: " + Shorten(_snap.OutName, 34));
        if (!string.IsNullOrEmpty(_snap?.InName))
            lines.Add("In: " + Shorten(_snap.InName, 28) + (_micMuted ? " [MUTED]" : ""));

        var full = string.Join("\n", lines);
        try { _tray.Text = Shorten(full, 127); }
        catch
        {
            try { _tray.Text = Shorten(full, 63); } catch { }
        }
    }

    private static string Shorten(string s, int max) =>
        s.Length <= max ? s : s[..(max - 1)] + "…";

    // =========================================================================================
    // Drag to reorder
    // =========================================================================================

    private void OnCardsDragEnter(object? sender, DragEventArgs e)
    {
        e.Effect = e.Data?.GetDataPresent(typeof(AudioCard)) == true ? DragDropEffects.Move : DragDropEffects.None;
    }

    private void OnCardsDragOver(object? sender, DragEventArgs e)
    {
        if (e.Data?.GetDataPresent(typeof(AudioCard)) != true) { e.Effect = DragDropEffects.None; return; }
        e.Effect = DragDropEffects.Move;

        var pt = _cardsPanel.PointToClient(new Point(e.X, e.Y));
        var cards = _cardsPanel.Controls.OfType<CardControl>().ToList();
        if (cards.Count == 0) return;

        int gap = _cardsPanel.DeviceDpi / 96 * 4;
        _dropIndex = cards.Count;
        _dropLineY = cards[^1].Bottom + gap;
        for (int i = 0; i < cards.Count; i++)
        {
            if (pt.Y < cards[i].Top + cards[i].Height / 2)
            {
                _dropIndex = i;
                _dropLineY = cards[i].Top - gap;
                break;
            }
        }
        _cardsPanel.Invalidate();
    }

    private void OnCardsDragDrop(object? sender, DragEventArgs e)
    {
        int idx = _dropIndex;
        _dropIndex = -1;
        _dropLineY = -1;
        _cardsPanel.Invalidate();

        if (e.Data?.GetData(typeof(AudioCard)) is not AudioCard moved || idx < 0) return;
        int old = _config.Cards.IndexOf(moved);
        if (old < 0) return;

        _config.Cards.RemoveAt(old);
        if (old < idx) idx--;
        idx = Math.Clamp(idx, 0, _config.Cards.Count);
        _config.Cards.Insert(idx, moved);

        Save();
        QueueRender();
    }

    // =========================================================================================
    // Dialogs
    // =========================================================================================

    private AudioCard NewCardFromCurrentDevices()
    {
        var card = new AudioCard { Name = "New Card" };
        var snap = _snap;
        if (snap?.DefaultOut is Guid o) { card.OutputId = o; card.OutputName = snap.OutName ?? "None"; }
        if (snap?.DefaultIn is Guid i) { card.InputId = i; card.InputName = snap.InName ?? "None"; }
        return card;
    }

    private async Task OpenCardEditorAsync(AudioCard card, bool isNew)
    {
        List<DeviceItem> outputs, inputs;
        try
        {
            (outputs, inputs) = await _audio.GetDeviceItemsAsync();
        }
        catch (Exception ex)
        {
            AppLog.Error("Failed to list audio devices for card editor", ex);
            SetStatus("Couldn't list audio devices: " + ex.Message, true);
            return;
        }

        if (IsDisposed) return;

        using var dlg = new CardEditorDialog(card, isNew, outputs, inputs);
        if (dlg.ShowDialog(this) != DialogResult.OK) return;

        card.Name = dlg.CardName;
        var o = dlg.SelectedOutput;
        card.OutputId = o?.Id;
        card.OutputName = o?.Id == null ? "None" : o.Name;
        var i = dlg.SelectedInput;
        card.InputId = i?.Id;
        card.InputName = i?.Id == null ? "None" : i.Name;
        card.AutoActivate = dlg.AutoActivate;

        if (isNew) _config.Cards.Add(card);
        Save();
        QueueRender();
    }

    private async Task OpenDeviceFilterDialogAsync()
    {
        List<DeviceItem> outputs, inputs;
        try
        {
            (outputs, inputs) = await _audio.GetDeviceItemsAsync();
        }
        catch (Exception ex)
        {
            AppLog.Error("Failed to list audio devices for filter dialog", ex);
            SetStatus("Couldn't list audio devices: " + ex.Message, true);
            return;
        }

        if (IsDisposed) return;

        using var dlg = new DeviceFilterDialog(outputs, inputs, _config.CycleOutputIds, _config.CycleInputIds);
        if (dlg.ShowDialog(this) != DialogResult.OK) return;

        _config.CycleOutputIds = dlg.OutputIds;
        _config.CycleInputIds = dlg.InputIds;
        Save();
        SetStatus("Cycle filter saved", false);
    }

    // =========================================================================================
    // Hotkeys
    // =========================================================================================

    private HotKeyBinding GetBinding(HotKeyTarget t) => t switch
    {
        HotKeyTarget.Card => _config.CardHotKey,
        HotKeyTarget.Output => _config.OutputHotKey,
        HotKeyTarget.Input => _config.InputHotKey,
        _ => _config.MuteHotKey
    };

    private void SetBinding(HotKeyTarget t, HotKeyBinding b)
    {
        switch (t)
        {
            case HotKeyTarget.Card: _config.CardHotKey = b; break;
            case HotKeyTarget.Output: _config.OutputHotKey = b; break;
            case HotKeyTarget.Input: _config.InputHotKey = b; break;
            default: _config.MuteHotKey = b; break;
        }
    }

    private static string TargetName(HotKeyTarget t) => t switch
    {
        HotKeyTarget.Card => "Cycle Cards",
        HotKeyTarget.Output => "Cycle Output",
        HotKeyTarget.Input => "Cycle Input",
        _ => "Toggle Mic Mute"
    };

    private bool RegisterAllHotKeys()
    {
        if (_capturing != null) return true;

        var items = new List<(string Name, HotKeyBinding Binding, Action Action)>
        {
            ("Cycle Cards", _config.CardHotKey, () => _ = CycleCardAsync()),
            ("Cycle Output", _config.OutputHotKey, () => _ = CycleOutputAsync()),
            ("Cycle Input", _config.InputHotKey, () => _ = CycleInputAsync()),
            ("Toggle Mic Mute", _config.MuteHotKey, () => _ = ToggleMicMuteAsync())
        };

        if (_config.JumpHotKeysEnabled)
        {
            int count = Math.Min(9, _config.Cards.Count);
            for (int i = 0; i < count; i++)
            {
                int slot = i;
                items.Add(($"Card {i + 1}",
                    new HotKeyBinding { Modifiers = _config.CardHotKey.Modifiers, Key = Keys.D1 + i },
                    () => _ = JumpToCardAsync(slot)));
            }
        }

        var failed = _hotKeys.RegisterAll(items);
        if (failed.Count == 0) return true;

        var msg = "Hotkey already used by another app: " + string.Join(", ", failed.Take(2)) +
                  (failed.Count > 2 ? "…" : "");
        AppLog.Warn(msg);
        SetStatus(msg, true);
        return false;
    }

    private void UpdateHotKeyButtons()
    {
        foreach (var (target, btn) in _hkButtons)
        {
            btn.Text = GetBinding(target).Format();
            btn.ForeColor = Theme.TextPrimary;
        }
        _jumpLabel.Text = $"Jump straight to card N with {_config.CardHotKey.FormatModifiers()} + 1-9";
    }

    private void BeginCapture(HotKeyTarget target)
    {
        _capturing = target;
        _hotKeys.UnregisterAll();
        UpdateHotKeyButtons();
        var btn = _hkButtons[target];
        btn.Text = "Press keys…";
        btn.ForeColor = Theme.Accent;
        SetStatus("Press the new shortcut (Esc to cancel)", false);
    }

    private bool EndCapture()
    {
        _capturing = null;
        UpdateHotKeyButtons();
        return RegisterAllHotKeys();
    }

    private void CancelCapture()
    {
        if (_capturing == null) return;
        EndCapture();
        SetStatus("Rebind cancelled", false);
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (_capturing is not HotKeyTarget target)
            return base.ProcessCmdKey(ref msg, keyData);

        var key = keyData & Keys.KeyCode;
        if (key is Keys.ControlKey or Keys.ShiftKey or Keys.Menu or Keys.LWin or Keys.RWin)
            return true;

        if (key == Keys.Escape)
        {
            CancelCapture();
            return true;
        }

        var mods = HotKeyModifiers.None;
        if ((keyData & Keys.Control) != 0) mods |= HotKeyModifiers.Control;
        if ((keyData & Keys.Shift) != 0) mods |= HotKeyModifiers.Shift;
        if ((keyData & Keys.Alt) != 0) mods |= HotKeyModifiers.Alt;
        if (Native.WinKeyDown()) mods |= HotKeyModifiers.Win;

        if (mods == HotKeyModifiers.None)
        {
            SetStatus("Add at least one modifier (Ctrl, Shift, Alt or Win)", true);
            return true;
        }

        var binding = new HotKeyBinding { Modifiers = mods, Key = key };
        var clash = FindConflict(target, binding);
        if (clash != null)
        {
            SetStatus($"{binding.Format()} is already used for \"{clash}\"", true);
            return true;
        }

        SetBinding(target, binding);
        Save();
        if (EndCapture()) SetStatus($"{TargetName(target)} → {binding.Format()}", false);
        return true;
    }

    private string? FindConflict(HotKeyTarget target, HotKeyBinding candidate)
    {
        foreach (var other in Enum.GetValues<HotKeyTarget>())
            if (other != target && GetBinding(other).SameAs(candidate)) return TargetName(other);

        if (_config.JumpHotKeysEnabled)
        {
            static bool IsJump(HotKeyBinding b, HotKeyModifiers mods) =>
                b.Modifiers == mods && b.Key >= Keys.D1 && b.Key <= Keys.D9;

            if (target != HotKeyTarget.Card && IsJump(candidate, _config.CardHotKey.Modifiers))
                return "Jump to card";

            if (target == HotKeyTarget.Card)
                foreach (var other in Enum.GetValues<HotKeyTarget>())
                    if (other != target && IsJump(GetBinding(other), candidate.Modifiers)) return TargetName(other);
        }
        return null;
    }

    // =========================================================================================
    // Audio actions (off UI thread)
    // =========================================================================================

    private static bool CardIsActive(AudioCard c, AudioSnapshot s) =>
        (c.OutputId.HasValue || c.InputId.HasValue)
        && (!c.OutputId.HasValue || c.OutputId == s.DefaultOut)
        && (!c.InputId.HasValue || c.InputId == s.DefaultIn);

    private bool CardIsAvailable(AudioCard c)
    {
        var s = _snap;
        if (s == null) return true;
        return (!c.OutputId.HasValue || s.OutputIds.Contains(c.OutputId.Value))
            && (!c.InputId.HasValue || s.InputIds.Contains(c.InputId.Value));
    }

    private async Task ActivateCardAsync(AudioCard card, string? categoryOverride = null)
    {
        ApplyResult result;
        try
        {
            result = await _audio.ApplyAsync(card.OutputId, card.OutputName, card.InputId, card.InputName);
        }
        catch (Exception ex)
        {
            AppLog.Error($"Failed to activate card '{card.Name}'", ex);
            if (!IsDisposed) ShowFeedback("SWITCH FAILED", ex.Message, Theme.Danger, false);
            return;
        }

        if (IsDisposed) return;

        // Auto-relink GUIDs if matched by name fallback
        bool relinked = false;
        if (result.ResolvedOutputId.HasValue)
        {
            card.OutputId = result.ResolvedOutputId.Value;
            relinked = true;
            AppLog.Info($"Re-linked output for card '{card.Name}' by name to {card.OutputId}.");
        }
        if (result.ResolvedInputId.HasValue)
        {
            card.InputId = result.ResolvedInputId.Value;
            relinked = true;
            AppLog.Info($"Re-linked input for card '{card.Name}' by name to {card.InputId}.");
        }

        if (!result.AnyApplied)
        {
            AppLog.Warn($"Card '{card.Name}' unavailable: {result.Problem}");
            ShowFeedback("CARD UNAVAILABLE", $"{card.Name}: {result.Problem ?? "nothing to apply"}", Theme.Warning, false);
            return;
        }

        _config.ActiveCardId = card.Id;
        Save();
        if (relinked) RefreshCardStates();
        await ForceRefreshAsync();

        if (IsDisposed) return;

        if (result.Problem != null)
            ShowFeedback("CARD PARTLY ACTIVE", $"{card.Name}: {result.Problem}", Theme.Warning, false);
        else
            ShowFeedback(categoryOverride ?? "CARD ACTIVE", card.Name, Theme.Accent, true);

        AppLog.Info($"Activated card '{card.Name}' ({categoryOverride ?? "manual"}).");
    }

    private Task JumpToCardAsync(int slot) =>
        slot < _config.Cards.Count ? ActivateCardAsync(_config.Cards[slot]) : Task.CompletedTask;

    private async Task CycleCardAsync()
    {
        var cards = _config.Cards;
        if (cards.Count == 0)
        {
            ShowFeedback("CARDS", "No cards yet", Theme.Warning, false);
            return;
        }

        int start = _snap != null ? cards.FindIndex(c => CardIsActive(c, _snap)) : -1;
        if (start < 0) start = cards.FindIndex(c => c.Id == _config.ActiveCardId);

        for (int step = 1; step <= cards.Count; step++)
        {
            var next = cards[(start + step) % cards.Count];
            if (!CardIsAvailable(next)) continue;
            await ActivateCardAsync(next);
            return;
        }
        ShowFeedback("CARDS", "No card has all its devices connected", Theme.Warning, false);
    }

    private async Task CycleOutputAsync()
    {
        try
        {
            var next = await _audio.CycleOutputAsync(_config.CycleOutputIds);
            if (IsDisposed) return;
            if (next == null)
            {
                ShowFeedback("OUTPUT", "Fewer than two devices to cycle through", Theme.Warning, false);
                return;
            }
            await ForceRefreshAsync();
            ShowFeedback("OUTPUT", next.Name, Theme.Accent, true);
            AppLog.Info($"Cycled output to '{next.Name}'.");
        }
        catch (Exception ex)
        {
            AppLog.Error("CycleOutput failed", ex);
            if (!IsDisposed) ShowFeedback("SWITCH FAILED", ex.Message, Theme.Danger, false);
        }
    }

    private async Task CycleInputAsync()
    {
        try
        {
            var next = await _audio.CycleInputAsync(_config.CycleInputIds);
            if (IsDisposed) return;
            if (next == null)
            {
                ShowFeedback("INPUT", "Fewer than two devices to cycle through", Theme.Warning, false);
                return;
            }
            await ForceRefreshAsync();
            ShowFeedback("INPUT", next.Name, Theme.Accent, true);
            AppLog.Info($"Cycled input to '{next.Name}'.");
        }
        catch (Exception ex)
        {
            AppLog.Error("CycleInput failed", ex);
            if (!IsDisposed) ShowFeedback("SWITCH FAILED", ex.Message, Theme.Danger, false);
        }
    }

    private async Task ToggleMicMuteAsync()
    {
        bool? muted;
        try
        {
            muted = await _audio.ToggleMuteAsync();
        }
        catch (Exception ex)
        {
            AppLog.Error("ToggleMicMute failed", ex);
            if (!IsDisposed) ShowFeedback("MICROPHONE", ex.Message, Theme.Danger, false);
            return;
        }

        if (IsDisposed) return;

        if (muted == null)
        {
            ShowFeedback("MICROPHONE", "No input device", Theme.Warning, false);
            return;
        }

        await ForceRefreshAsync();
        ApplyMuteUi(muted.Value);
        ShowFeedback("MICROPHONE", muted.Value ? "Muted" : "Live (Unmuted)",
            muted.Value ? Theme.Danger : Theme.Success, false);
        AppLog.Info($"Mic mute toggled -> {(muted.Value ? "Muted" : "Live")}.");
    }

    private void ShowFeedback(string category, string title, Color accent, bool playChime)
    {
        SetStatus($"{category}: {title}", accent == Theme.Warning || accent == Theme.Danger);
        _osd.ShowMessage(category, title, accent);
        if (playChime && _config.ChimeEnabled) ChimePlayer.PlayAsync();
    }

    // =========================================================================================
    // Live state (polling), name re-linking, debounced auto-activate & fallback card
    // =========================================================================================

    private Task ForceRefreshAsync()
    {
        _lastSignature = "";
        return RefreshStateAsync(false, force: true);
    }

    private async Task RefreshStateAsync(bool allowAutoEvents, bool force = false)
    {
        if (_polling && !force) return;
        _polling = true;
        try
        {
            var snap = await _audio.SnapshotAsync();
            if (IsDisposed) return;

            var signature = snap.Signature;
            if (signature == _lastSignature) return;

            var previous = _snap;
            _snap = snap;
            _lastSignature = signature;

            RelinkCardsByName(snap);
            ApplyMuteUi(snap.MicMuted);
            RefreshCardStates();

            if (allowAutoEvents && previous != null)
            {
                HandleDeviceChanges(previous, snap);
            }
        }
        catch (Exception ex)
        {
            AppLog.Error("Snapshot refresh failed", ex);
        }
        finally
        {
            _polling = false;
        }
    }

    private void RelinkCardsByName(AudioSnapshot snap)
    {
        bool changed = false;
        foreach (var card in _config.Cards)
        {
            if (card.OutputId.HasValue && !snap.OutputIds.Contains(card.OutputId.Value) &&
                !string.IsNullOrWhiteSpace(card.OutputName) && !card.OutputName.Equals("None", StringComparison.OrdinalIgnoreCase))
            {
                var match = snap.OutputNames.FirstOrDefault(kv => kv.Value.Equals(card.OutputName, StringComparison.OrdinalIgnoreCase));
                if (match.Key != Guid.Empty)
                {
                    card.OutputId = match.Key;
                    changed = true;
                    AppLog.Info($"Auto-relinked card '{card.Name}' output by name to {match.Key}.");
                }
            }

            if (card.InputId.HasValue && !snap.InputIds.Contains(card.InputId.Value) &&
                !string.IsNullOrWhiteSpace(card.InputName) && !card.InputName.Equals("None", StringComparison.OrdinalIgnoreCase))
            {
                var match = snap.InputNames.FirstOrDefault(kv => kv.Value.Equals(card.InputName, StringComparison.OrdinalIgnoreCase));
                if (match.Key != Guid.Empty)
                {
                    card.InputId = match.Key;
                    changed = true;
                    AppLog.Info($"Auto-relinked card '{card.Name}' input by name to {match.Key}.");
                }
            }
        }
        if (changed) Save();
    }

    private void HandleDeviceChanges(AudioSnapshot previous, AudioSnapshot now)
    {
        var newIds = new HashSet<Guid>(now.OutputIds.Except(previous.OutputIds));
        newIds.UnionWith(now.InputIds.Except(previous.InputIds));

        var removedIds = new HashSet<Guid>(previous.OutputIds.Except(now.OutputIds));
        removedIds.UnionWith(previous.InputIds.Except(now.InputIds));

        // 1. Debounced auto-activate for newly connected devices (with flapping guard)
        if (newIds.Count > 0)
        {
            var cutoff = DateTime.UtcNow.AddSeconds(-30);
            foreach (var id in newIds)
            {
                if (!_connectHistory.TryGetValue(id, out var q))
                    _connectHistory[id] = q = new Queue<DateTime>();

                while (q.Count > 0 && q.Peek() < cutoff) q.Dequeue();
                q.Enqueue(DateTime.UtcNow);

                if (q.Count >= 3)
                {
                    AppLog.Warn($"Ignoring flapping device {id} ({q.Count} connects in 30s).");
                    continue;
                }
                _pendingNewDevices.Add(id);
            }

            if (_pendingNewDevices.Count > 0)
            {
                _debounceTimer.Stop();
                _debounceTimer.Start();
            }
        }

        // 2. Fallback card if the active card's device disconnected
        if (removedIds.Count > 0 && _config.FallbackCardId.HasValue)
        {
            var activeCard = _config.Cards.FirstOrDefault(c => c.Id == _config.ActiveCardId)
                ?? _config.Cards.FirstOrDefault(c => CardIsActive(c, previous));

            if (activeCard != null && activeCard.Id != _config.FallbackCardId.Value)
            {
                bool lostDevice =
                    (activeCard.OutputId.HasValue && removedIds.Contains(activeCard.OutputId.Value)) ||
                    (activeCard.InputId.HasValue && removedIds.Contains(activeCard.InputId.Value));

                if (lostDevice)
                {
                    var fallback = _config.Cards.FirstOrDefault(c => c.Id == _config.FallbackCardId.Value);
                    if (fallback != null && CardIsAvailable(fallback) && !CardIsActive(fallback, now))
                    {
                        AppLog.Info($"Active card '{activeCard.Name}' lost a device; switching to fallback '{fallback.Name}'.");
                        _ = ActivateCardAsync(fallback, "FALLBACK ACTIVE");
                    }
                }
            }
        }
    }

    private void ProcessDebouncedAutoActivate()
    {
        if (_snap == null || _pendingNewDevices.Count == 0) return;

        var stillConnected = _pendingNewDevices
            .Where(id => _snap.OutputIds.Contains(id) || _snap.InputIds.Contains(id))
            .ToHashSet();
        _pendingNewDevices.Clear();

        if (stillConnected.Count == 0) return;

        foreach (var c in _config.Cards)
        {
            if (!c.AutoActivate || !CardIsAvailable(c) || CardIsActive(c, _snap)) continue;
            bool touchesNew =
                (c.OutputId.HasValue && stillConnected.Contains(c.OutputId.Value)) ||
                (c.InputId.HasValue && stillConnected.Contains(c.InputId.Value));
            if (!touchesNew) continue;

            _ = ActivateCardAsync(c, "AUTO-SWITCHED");
            break;
        }
    }

    private void ApplyMuteUi(bool muted)
    {
        _micMuted = muted;
        if (muted) _muteBanner.ShowBanner();
        else _muteBanner.Hide();
        _tray.Icon = muted ? TrayIcons.Muted : TrayIcons.Normal;
        UpdateNow();
    }

    // =========================================================================================
    // Tray menu
    // =========================================================================================

    private void RebuildTrayMenu()
    {
        var items = _trayMenu.Items;
        foreach (var item in items.Cast<ToolStripItem>().ToList())
        {
            items.Remove(item);
            item.Dispose();
        }

        items.Add("Open AudioSwitcher", null, (_, _) => ShowFromTray());

        if (_config.Cards.Count > 0)
        {
            items.Add(new ToolStripSeparator());
            foreach (var card in _config.Cards)
            {
                var c = card;
                var mi = new ToolStripMenuItem(c.Name)
                {
                    Checked = _snap != null && CardIsActive(c, _snap),
                    Enabled = CardIsAvailable(c)
                };
                mi.Click += (_, _) => _ = ActivateCardAsync(c);
                items.Add(mi);
            }
        }

        items.Add(new ToolStripSeparator());
        items.Add("Toggle Mic Mute", null, (_, _) => _ = ToggleMicMuteAsync());
        items.Add("Exit", null, (_, _) => { _allowClose = true; Close(); });
    }

    // =========================================================================================
    // Start with Windows
    // =========================================================================================

    private static bool IsStartupEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(StartupRegKey, false);
        return key?.GetValue(AppRegName) != null;
    }

    private void SetStartupSafe(bool enable)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(StartupRegKey, true);
            if (key == null) return;

            if (enable)
            {
                var exePath = Environment.ProcessPath;
                if (!string.IsNullOrEmpty(exePath))
                    key.SetValue(AppRegName, $"\"{exePath}\" --minimized");
            }
            else
            {
                key.DeleteValue(AppRegName, false);
            }
        }
        catch (Exception ex)
        {
            AppLog.Error("Failed to update Windows startup registry key", ex);
            SetStatus("Couldn't change the startup setting: " + ex.Message, true);
        }
    }
}