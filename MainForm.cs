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

    private bool _settingsOpen;
    private HotKeyTarget? _capturing;
    private AudioSnapshot? _snap;
    private string _lastSignature = "";
    private bool _micMuted;
    private bool _allowClose;
    private int _dropIndex = -1;
    private int _dropLineY = -1;

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
        Size = new Size(380, 600);
        MinimumSize = new Size(380, 600);
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
        titleBar.Controls.Add(titleLbl);   // Fill first, then the right-docked buttons
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
        addBtn.Click += (_, _) => OpenCardEditor(NewCardFromCurrentDevices(), true);
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

        // Dock order: Fill first, then the edge panels (last added is docked first).
        Controls.Add(content);
        Controls.Add(_statusLabel);
        Controls.Add(actionBar);
        Controls.Add(nowPanel);
        Controls.Add(titleBar);

        // ---- tray ----
        _trayMenu.Opening += (_, _) => RebuildTrayMenu();
        _tray = new NotifyIcon
        {
            Icon = TrayIcons.Normal,
            Text = "AudioSwitcher",
            Visible = true,
            ContextMenuStrip = _trayMenu
        };
        _tray.MouseClick += (_, e) => { if (e.Button == MouseButtons.Left) ShowFromTray(); };

        // ---- go ----
        RenderCards();            // also registers hotkeys
        RefreshState(false);
        _pollTimer.Tick += (_, _) => RefreshState(true);
        _pollTimer.Start();

        if (_store.LoadWarning != null) SetStatus(_store.LoadWarning, true);
    }

    // =========================================================================================
    // Window plumbing
    // =========================================================================================

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
        // Only intercept the user's own close; let Windows shutdown/logoff proceed.
        if (!_allowClose && e.CloseReason == CloseReason.UserClosing)
        {
            e.Cancel = true;
            HideToTray();
            return;
        }

        _pollTimer.Stop();
        _statusTimer.Stop();
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
        btn.Click += (_, _) => OpenCardEditor(NewCardFromCurrentDevices(), true);

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
        filterBtn.Click += (_, _) => OpenDeviceFilterDialog();
        t.Controls.Add(filterBtn);
        t.SetColumnSpan(filterBtn, 2);

        var pathLbl = new Label
        {
            Text = "Settings file:\n" + _store.FilePath,
            Font = Theme.Small,
            ForeColor = Theme.TextMuted,
            AutoSize = false,
            Dock = DockStyle.Fill,
            Height = 52,
            Margin = new Padding(0, 12, 0, 0)
        };
        t.Controls.Add(pathLbl);
        t.SetColumnSpan(pathLbl, 2);

        panel.Controls.Add(t);
        UpdateHotKeyButtons();
        return panel;
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
            cc.ActivateRequested += ActivateCard;
            cc.EditRequested += card => OpenCardEditor(card, false);
            cc.DeleteRequested += card =>
            {
                _config.Cards.Remove(card);
                if (_config.ActiveCardId == card.Id) _config.ActiveCardId = null;
                Save();
                QueueRender();
            };
            _cardsPanel.Controls.Add(cc);
        }

        ResizeCards();
        RefreshCardStates();
        _cardsPanel.ResumeLayout(true);
        UpdateViews();
        RegisterAllHotKeys(); // per-card hotkeys depend on how many cards exist
    }

    private void QueueRender()
    {
        // Deferred so we never dispose a card control from inside its own event handler.
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
    }

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

    private void OpenCardEditor(AudioCard card, bool isNew)
    {
        List<DeviceItem> outputs, inputs;
        try
        {
            outputs = _audio.OutputItems();
            inputs = _audio.InputItems();
        }
        catch (Exception ex)
        {
            SetStatus("Couldn't list audio devices: " + ex.Message, true);
            return;
        }

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

    private void OpenDeviceFilterDialog()
    {
        List<DeviceItem> outputs, inputs;
        try
        {
            outputs = _audio.OutputItems();
            inputs = _audio.InputItems();
        }
        catch (Exception ex)
        {
            SetStatus("Couldn't list audio devices: " + ex.Message, true);
            return;
        }

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

    /// <summary>Returns false (and tells the user) if any hotkey is owned by another app.</summary>
    private bool RegisterAllHotKeys()
    {
        if (_capturing != null) return true; // hotkeys stay unregistered while rebinding

        var items = new List<(string Name, HotKeyBinding Binding, Action Action)>
        {
            ("Cycle Cards", _config.CardHotKey, CycleCard),
            ("Cycle Output", _config.OutputHotKey, CycleOutput),
            ("Cycle Input", _config.InputHotKey, CycleInput),
            ("Toggle Mic Mute", _config.MuteHotKey, ToggleMicMute)
        };

        if (_config.JumpHotKeysEnabled)
        {
            int count = Math.Min(9, _config.Cards.Count);
            for (int i = 0; i < count; i++)
            {
                int slot = i;
                items.Add(($"Card {i + 1}",
                    new HotKeyBinding { Modifiers = _config.CardHotKey.Modifiers, Key = Keys.D1 + i },
                    () => JumpToCard(slot)));
            }
        }

        var failed = _hotKeys.RegisterAll(items);
        if (failed.Count == 0) return true;

        SetStatus("Hotkey already used by another app: " + string.Join(", ", failed.Take(2)) +
                  (failed.Count > 2 ? "…" : ""), true);
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
        _hotKeys.UnregisterAll(); // so pressing an existing combo reaches us instead of triggering it
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
            return true; // wait for the real key

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
            return true; // stay in capture mode so the user can try another combo
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
    // Audio actions
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

    private void ActivateCard(AudioCard card)
    {
        ApplyResult result;
        try
        {
            result = _audio.Apply(card.OutputId, card.InputId);
        }
        catch (Exception ex)
        {
            ShowFeedback("SWITCH FAILED", ex.Message, Theme.Danger, false);
            return;
        }

        if (!result.AnyApplied)
        {
            ShowFeedback("CARD UNAVAILABLE", $"{card.Name}: {result.Problem ?? "nothing to apply"}", Theme.Warning, false);
            return;
        }

        _config.ActiveCardId = card.Id;
        Save();
        ForceRefresh();

        if (result.Problem != null)
            ShowFeedback("CARD PARTLY ACTIVE", $"{card.Name}: {result.Problem}", Theme.Warning, false);
        else
            ShowFeedback("CARD ACTIVE", card.Name, Theme.Accent, true);
    }

    private void JumpToCard(int slot)
    {
        if (slot < _config.Cards.Count) ActivateCard(_config.Cards[slot]);
    }

    private void CycleCard()
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
            if (!CardIsAvailable(next)) continue; // skip cards whose devices are unplugged
            ActivateCard(next);
            return;
        }
        ShowFeedback("CARDS", "No card has all its devices connected", Theme.Warning, false);
    }

    private void CycleOutput()
    {
        try
        {
            var next = _audio.CycleOutput(_config.CycleOutputIds);
            if (next == null)
            {
                ShowFeedback("OUTPUT", "Fewer than two devices to cycle through", Theme.Warning, false);
                return;
            }
            ForceRefresh();
            ShowFeedback("OUTPUT", next.FullName, Theme.Accent, true);
        }
        catch (Exception ex)
        {
            ShowFeedback("SWITCH FAILED", ex.Message, Theme.Danger, false);
        }
    }

    private void CycleInput()
    {
        try
        {
            var next = _audio.CycleInput(_config.CycleInputIds);
            if (next == null)
            {
                ShowFeedback("INPUT", "Fewer than two devices to cycle through", Theme.Warning, false);
                return;
            }
            ForceRefresh();
            ShowFeedback("INPUT", next.FullName, Theme.Accent, true);
        }
        catch (Exception ex)
        {
            ShowFeedback("SWITCH FAILED", ex.Message, Theme.Danger, false);
        }
    }

    private void ToggleMicMute()
    {
        bool? muted;
        try
        {
            muted = _audio.ToggleMute();
        }
        catch (Exception ex)
        {
            ShowFeedback("MICROPHONE", ex.Message, Theme.Danger, false);
            return;
        }

        if (muted == null)
        {
            ShowFeedback("MICROPHONE", "No input device", Theme.Warning, false);
            return;
        }

        ForceRefresh();
        ApplyMuteUi(muted.Value); // don't wait for the driver to report the new state
        ShowFeedback("MICROPHONE", muted.Value ? "Muted" : "Live (Unmuted)",
            muted.Value ? Theme.Danger : Theme.Success, false);
    }

    private void ShowFeedback(string category, string title, Color accent, bool playChime)
    {
        SetStatus($"{category}: {title}", accent == Theme.Warning || accent == Theme.Danger);
        _osd.ShowMessage(category, title, accent);
        if (playChime && _config.ChimeEnabled) ChimePlayer.PlayAsync();
    }

    // =========================================================================================
    // Live state (polling) and auto-activation
    // =========================================================================================

    private void ForceRefresh()
    {
        _lastSignature = "";
        RefreshState(false);
    }

    private void RefreshState(bool allowAutoActivate)
    {
        AudioSnapshot snap;
        try { snap = _audio.Snapshot(); }
        catch { return; }

        var signature = snap.Signature;
        if (signature == _lastSignature) return;

        var previous = _snap;
        _snap = snap;
        _lastSignature = signature;

        ApplyMuteUi(snap.MicMuted);
        RefreshCardStates();

        if (allowAutoActivate && previous != null) AutoActivateForNewDevices(previous, snap);
    }

    private void ApplyMuteUi(bool muted)
    {
        _micMuted = muted;
        if (muted) _muteBanner.ShowBanner();
        else _muteBanner.Hide();
        _tray.Icon = muted ? TrayIcons.Muted : TrayIcons.Normal;
        UpdateNow();
    }

    private void AutoActivateForNewDevices(AudioSnapshot previous, AudioSnapshot now)
    {
        var newIds = new HashSet<Guid>(now.OutputIds.Except(previous.OutputIds));
        newIds.UnionWith(now.InputIds.Except(previous.InputIds));
        if (newIds.Count == 0) return;

        foreach (var c in _config.Cards)
        {
            if (!c.AutoActivate || !CardIsAvailable(c) || CardIsActive(c, now)) continue;
            bool touchesNewDevice =
                (c.OutputId.HasValue && newIds.Contains(c.OutputId.Value)) ||
                (c.InputId.HasValue && newIds.Contains(c.InputId.Value));
            if (!touchesNewDevice) continue;

            ActivateCard(c);
            break;
        }
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
                mi.Click += (_, _) => ActivateCard(c);
                items.Add(mi);
            }
        }

        items.Add(new ToolStripSeparator());
        items.Add("Toggle Mic Mute", null, (_, _) => ToggleMicMute());
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
            SetStatus("Couldn't change the startup setting: " + ex.Message, true);
        }
    }
}
