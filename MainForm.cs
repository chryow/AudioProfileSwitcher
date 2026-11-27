using System.Diagnostics;
using System.Drawing.Drawing2D;
using Microsoft.Win32;

namespace AudioProfileSwitcher;

public sealed class MainForm : Form
{
    private const string StartupRegKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";
    private const string AppRegName = "AudioProfileSwitcher";

    private readonly ConfigStore _store = new();
    private readonly AppConfig _config;
    private readonly AudioService _audio = new();
    private readonly VoiceControlService _voice = new();
    private readonly HotKeyService _hotKeys;
    private readonly OsdBannerForm _osd = new();
    private readonly MuteBannerForm _muteBanner = new();
    private readonly NotifyIcon _tray;
    private readonly ContextMenuStrip _trayMenu = new();
    private readonly ToolTip _tip = new();
    private readonly SingleFlightCoalescer _refreshCoalescer;
    private readonly System.Windows.Forms.Timer _eventCoalesceTimer = new() { Interval = 120 };
    private readonly System.Windows.Forms.Timer _pollTimer = new() { Interval = 45_000 };
    private readonly System.Windows.Forms.Timer _statusTimer = new() { Interval = 6000 };
    private readonly System.Windows.Forms.Timer _debounceTimer = new() { Interval = 2000 };
    private readonly System.Windows.Forms.Timer _recoveryTimer = new() { Interval = 1000 };
    private readonly bool _startMinimized;
    private readonly string? _updatedToVersion;

    private readonly Panel _titleBar;
    private readonly FlowLayoutPanel _cardsPanel;
    private readonly TableLayoutPanel _emptyPanel;
    private readonly Panel _settingsPanel;
    private readonly LiveNowPanel _liveNow;
    private readonly Label _statusLabel;
    private readonly Panel _statusDot;
    private readonly ModernButton _gearBtn;
    private readonly Dictionary<HotKeyTarget, ModernButton> _hkButtons = new();
    private Label _jumpLabel = null!;
    private DarkCombo _fallbackCombo = null!;
    private DarkCombo _muteBannerPosCombo = null!;
    private ToggleSwitch _voiceToggle = null!;
    private DarkTextBox _voiceWakeBox = null!;
    private DarkCombo _voiceMicCombo = null!;
    private DarkSlider _voiceConfSlider = null!;
    private Label _voiceConfValueLbl = null!;
    private Label _voiceStatusLbl = null!;
    private Label _voiceHintLbl = null!;
    private ModernButton _checkUpdateBtn = null!;
    private Color _statusDotColor = Theme.Success;

    // Auto-activate debounce & flapping guard
    private readonly HashSet<Guid> _pendingNewDevices = new();
    private readonly Dictionary<Guid, Queue<DateTime>> _connectHistory = new();

    private bool _settingsOpen;
    private bool _updatingFallbackCombo;
    private bool _updatingBannerPosCombo;
    private bool _updatingVoiceControls;
    private bool _voiceListening;
    private string _voiceStatusText = "Disabled";
    private ReleaseCheckResult? _availableUpdate;
    private Rectangle _updatePillRect = Rectangle.Empty;
    private bool _updatePillHover;
    private bool _checkingUpdates;
    private bool _resizingHeight;
    private int _resizeStartScreenY;
    private int _resizeStartHeight;
    private HotKeyTarget? _capturing;
    private AudioSnapshot? _snap;
    private bool _micMuted;
    private bool _allowClose;
    private int _dropIndex = -1;
    private int _dropLineY = -1;

    private sealed record FallbackChoice(FallbackMode Mode, Guid? CardId, string Label)
    {
        public override string ToString() => Label;
    }

    private sealed record BannerPosChoice(MuteBannerPosition Position, string Label)
    {
        public override string ToString() => Label;
    }

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.ClassStyle |= 0x00020000; // CS_DROPSHADOW for crisp depth on Windows 10 & 11
            return cp;
        }
    }

    public MainForm(bool startMinimized = false, string? updatedToVersion = null)
    {
        _startMinimized = startMinimized;
        _updatedToVersion = updatedToVersion;
        _config = _store.Load();
        _hotKeys = new HotKeyService();
        _refreshCoalescer = new SingleFlightCoalescer(ExecuteRefreshPassAsync);

        // ---- window ----
        AutoScaleDimensions = new SizeF(96F, 96F);
        AutoScaleMode = AutoScaleMode.Dpi;
        Text = "AudioSwitcher";
        Icon = TrayIcons.AppWindow;
        MinimumSize = new Size(396, 440);
        MaximumSize = new Size(396, 1400);
        Size = new Size(396, ProfileLogic.ClampWindowHeight(_config.WindowHeight, 440, 1400, 648));
        FormBorderStyle = FormBorderStyle.None;
        RestoreWindowLocation();
        BackColor = Theme.BgMain;
        ForeColor = Theme.TextPrimary;
        Padding = new Padding(1);
        DoubleBuffered = true;

        // ---- modern title bar with vector emblem + version / update & voice pills ----
        var titleBar = new Panel { Dock = DockStyle.Top, Height = 50, BackColor = Theme.BgMain };
        _titleBar = titleBar;
        titleBar.Paint += (_, e) =>
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            float k = Math.Max(1f, titleBar.DeviceDpi / 96f);
            int P(int v) => (int)Math.Round(v * k);

            var emblem = new RectangleF(P(16), (titleBar.Height - P(22)) / 2f, P(22), P(22));
            Gfx.DrawAppEmblem(g, emblem);

            int titleX = P(46);
            var titleRect = new Rectangle(titleX, 0, P(116), titleBar.Height);
            TextRenderer.DrawText(g, "AudioSwitcher", Theme.Title, titleRect, Theme.TextPrimary,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);

            var measured = TextRenderer.MeasureText(g, "AudioSwitcher", Theme.Title);
            int pillX = titleX + measured.Width + P(2);
            int pillY = (titleBar.Height - P(18)) / 2;
            Rectangle badgeRect;

            if (_availableUpdate is { UpdateAvailable: true } upd)
            {
                string updLabel = $"↑ {upd.LatestVersion}";
                int badgeW = Math.Clamp(TextRenderer.MeasureText(g, updLabel, Theme.MicroBold).Width + P(10), P(52), P(74));
                badgeRect = new Rectangle(pillX, pillY, badgeW, P(18));
                _updatePillRect = badgeRect;

                using var up = Gfx.RoundRect(badgeRect, P(9));
                using (var ub = new SolidBrush(_updatePillHover ? Color.FromArgb(62, Theme.Success) : Theme.SuccessSoft))
                    g.FillPath(ub, up);
                using (var upen = new Pen(Color.FromArgb(_updatePillHover ? 220 : 140, Theme.Success), 1f))
                    g.DrawPath(upen, up);
                TextRenderer.DrawText(g, updLabel, Theme.MicroBold, badgeRect, Theme.Success,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
            }
            else
            {
                _updatePillRect = Rectangle.Empty;
                badgeRect = new Rectangle(pillX, pillY, P(44), P(18));
                using var vp = Gfx.RoundRect(badgeRect, P(9));
                using (var vb = new SolidBrush(Theme.AccentSoft)) g.FillPath(vb, vp);
                TextRenderer.DrawText(g, $"v{AppInfo.Version}", Theme.MicroBold, badgeRect, Theme.AccentEnd,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
            }

            if (_voiceListening)
            {
                var voiceRect = new Rectangle(badgeRect.Right + P(5), pillY, P(48), P(18));
                int rightLimit = (_gearBtn?.Left ?? (titleBar.Width - P(112))) - P(4);
                if (voiceRect.Right < rightLimit)
                {
                    using var vcp = Gfx.RoundRect(voiceRect, P(9));
                    using (var vcb = new SolidBrush(Theme.SuccessSoft)) g.FillPath(vcb, vcp);
                    using (var vcpen = new Pen(Color.FromArgb(110, Theme.Success), 1f)) g.DrawPath(vcpen, vcp);
                    TextRenderer.DrawText(g, "● VOICE", Theme.MicroBold, voiceRect, Theme.Success,
                        TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
                }
            }
        };
        titleBar.MouseMove += (_, e) =>
        {
            bool hit = !_updatePillRect.IsEmpty && _updatePillRect.Contains(e.Location);
            if (hit != _updatePillHover)
            {
                _updatePillHover = hit;
                titleBar.Cursor = hit ? Cursors.Hand : Cursors.Default;
                titleBar.Invalidate();
            }
        };
        titleBar.MouseLeave += (_, _) =>
        {
            if (_updatePillHover)
            {
                _updatePillHover = false;
                titleBar.Cursor = Cursors.Default;
                titleBar.Invalidate();
            }
        };
        titleBar.MouseDown += (_, e) =>
        {
            if (e.Button != MouseButtons.Left) return;
            if (!_updatePillRect.IsEmpty && _updatePillRect.Contains(e.Location))
            {
                ShowUpdateDialog();
                return;
            }
            Native.DragWindow(Handle);
            CaptureWindowBounds(persist: true);
        };

        _gearBtn = MakeIconButton(Theme.Glyph("\uE713", "⚙"), "Settings & hotkeys", (_, _) => ToggleSettings(), ModernButtonKind.Ghost);
        var minBtn = MakeIconButton(Theme.Glyph("\uE921", "—"), "Minimize", (_, _) => WindowState = FormWindowState.Minimized, ModernButtonKind.Ghost);
        var closeBtn = MakeIconButton(Theme.Glyph("\uE8BB", "×"), "Hide to tray (keeps running)", (_, _) => HideToTray(), ModernButtonKind.DangerGhost);
        titleBar.Controls.Add(_gearBtn);
        titleBar.Controls.Add(minBtn);
        titleBar.Controls.Add(closeBtn);

        // ---- elevated live "now" hero panel (clickable mic mute row) ----
        _liveNow = new LiveNowPanel { Dock = DockStyle.Top, Height = 76 };
        _liveNow.MicMuteToggleRequested += () => _ = ToggleMicMuteAsync();
        _tip.SetToolTip(_liveNow, "Active audio devices · click the mic row to toggle mute");

        // ---- action bar ----
        var actionBar = new Panel
        {
            Dock = DockStyle.Top,
            Height = 46,
            BackColor = Theme.BgMain,
            Padding = new Padding(16, 6, 16, 6)
        };
        var addBtn = new ModernButton(ModernButtonKind.Primary)
        {
            Text = "+  New Card",
            Size = new Size(116, 32),
            Location = new Point(16, 7),
            CornerRadius = 8
        };
        addBtn.Click += (_, _) => _ = OpenCardEditorAsync(NewCardFromCurrentDevices(), true);
        var hintLbl = new Label
        {
            Text = "Click to switch · right-click for menu",
            Font = Theme.Small,
            ForeColor = Theme.TextMuted,
            AutoSize = true,
            Location = new Point(144, 15)
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
            Padding = new Padding(16, 4, 16, 10),
            AllowDrop = true,
            Visible = false
        };
        _cardsPanel.ClientSizeChanged += (_, _) => ResizeCards();
        _cardsPanel.DragEnter += OnCardsDragEnter;
        _cardsPanel.DragOver += OnCardsDragOver;
        _cardsPanel.DragLeave += (_, _) =>
        {
            if (_dropLineY >= 0 || _dropIndex >= 0)
            {
                _dropLineY = -1;
                _dropIndex = -1;
                _cardsPanel.Invalidate();
            }
        };
        _cardsPanel.DragDrop += OnCardsDragDrop;
        _cardsPanel.Paint += (_, e) =>
        {
            if (_dropLineY < 0) return;
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            int x1 = _cardsPanel.Padding.Left;
            int x2 = _cardsPanel.ClientSize.Width - _cardsPanel.Padding.Right;
            using var pen = new Pen(Theme.AccentEnd, 2.5f) { StartCap = LineCap.Round, EndCap = LineCap.Round };
            e.Graphics.DrawLine(pen, x1, _dropLineY, x2, _dropLineY);
        };

        _emptyPanel = BuildEmptyPanel();
        _settingsPanel = BuildSettingsPanel();

        var content = new Panel { Dock = DockStyle.Fill, BackColor = Theme.BgMain };
        content.Controls.Add(_cardsPanel);
        content.Controls.Add(_emptyPanel);
        content.Controls.Add(_settingsPanel);

        // ---- sleek footer bar with live status dot & resize grip pill ----
        var footerBar = new Panel
        {
            Dock = DockStyle.Bottom,
            Height = 32,
            BackColor = Theme.BgSurface
        };
        footerBar.Paint += (_, e) =>
        {
            using var p = new Pen(Theme.BgBorder, 1f);
            e.Graphics.DrawLine(p, 0, 0, footerBar.Width, 0);
        };

        _statusDot = new Panel
        {
            Dock = DockStyle.Left,
            Width = 24,
            BackColor = Theme.BgSurface
        };
        _statusDot.Paint += (_, e) =>
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            int d = 6;
            using var b = new SolidBrush(_statusDotColor);
            e.Graphics.FillEllipse(b, 14, (_statusDot.Height - d) / 2, d, d);
        };

        var resizeGrip = new Panel
        {
            Dock = DockStyle.Right,
            Width = 34,
            BackColor = Theme.BgSurface,
            Cursor = Cursors.SizeNS
        };
        resizeGrip.Paint += (_, e) =>
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using var p = new Pen(Theme.BgBorderStrong, 2f) { StartCap = LineCap.Round, EndCap = LineCap.Round };
            int cy = resizeGrip.Height / 2;
            e.Graphics.DrawLine(p, 8, cy - 2, 22, cy - 2);
            e.Graphics.DrawLine(p, 11, cy + 2, 19, cy + 2);
        };
        _tip.SetToolTip(resizeGrip, "Drag up or down to resize window height");
        AttachVerticalResize(resizeGrip);

        _statusLabel = new Label
        {
            Dock = DockStyle.Fill,
            Font = Theme.Small,
            ForeColor = Theme.TextMuted,
            BackColor = Theme.BgSurface,
            TextAlign = ContentAlignment.MiddleLeft,
            Padding = new Padding(2, 0, 4, 0),
            AutoEllipsis = true,
            Text = "Ready"
        };
        _statusTimer.Tick += (_, _) =>
        {
            _statusTimer.Stop();
            _statusLabel.Text = "Ready";
            _statusLabel.ForeColor = Theme.TextMuted;
            _statusDotColor = Theme.Success;
            _statusDot.Invalidate();
        };

        var bottomResizeStrip = new Panel
        {
            Dock = DockStyle.Bottom,
            Height = 4,
            BackColor = Theme.BgSurface,
            Cursor = Cursors.SizeNS
        };
        AttachVerticalResize(bottomResizeStrip);

        footerBar.Controls.Add(_statusLabel);
        footerBar.Controls.Add(_statusDot);
        footerBar.Controls.Add(resizeGrip);
        footerBar.Controls.Add(bottomResizeStrip);

        Controls.Add(content);
        Controls.Add(footerBar);
        Controls.Add(actionBar);
        Controls.Add(_liveNow);
        Controls.Add(titleBar);

        // ---- mute banner interactive wiring ----
        _muteBanner.Interactive = _config.MuteBannerInteractive;
        _muteBanner.BannerMoved += pt =>
        {
            _config.MuteBannerPosition = MuteBannerPosition.Custom;
            _config.MuteBannerX = pt.X;
            _config.MuteBannerY = pt.Y;
            Save();
            RefreshMuteBannerPosCombo();
            SetStatus("Saved custom mute banner position", false);
        };
        _muteBanner.UnmuteRequested += () => _ = ToggleMicMuteAsync();

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
        _tray.MouseClick += (_, e) => { if (e.Button == MouseButtons.Left) ToggleWindowVisibility(); };

        // ---- timers, CoreAudio events & system events ----
        _eventCoalesceTimer.Tick += (_, _) =>
        {
            _eventCoalesceTimer.Stop();
            _ = RefreshStateAsync(allowAutoEvents: true);
        };
        _audio.StateChanged += OnCoreAudioStateChanged;
        _voice.CommandRecognized += OnVoiceCommandRecognized;
        _voice.StatusChanged += OnVoiceStatusChanged;

        _debounceTimer.Tick += (_, _) => { _debounceTimer.Stop(); ProcessDebouncedAutoActivate(); };
        _recoveryTimer.Tick += (_, _) =>
        {
            _recoveryTimer.Stop();
            AppLog.Info("Recovering hotkeys and audio state after power/session event.");
            RegisterAllHotKeys(force: true);
            _ = ForceRefreshAsync();
            SyncVoiceControl();
        };

        SystemEvents.PowerModeChanged += OnPowerModeChanged;
        SystemEvents.SessionSwitch += OnSessionSwitch;

        // ---- go ----
        RenderCards();
        _ = RefreshStateAsync(allowAutoEvents: false, force: true);
        _pollTimer.Tick += (_, _) => _ = RefreshStateAsync(allowAutoEvents: true);
        _pollTimer.Start();
        SyncVoiceControl();

        if (_store.LoadWarning != null) SetStatus(_store.LoadWarning, true);
        if (!string.IsNullOrEmpty(_updatedToVersion)) _ = ShowUpdatedConfirmationAsync(_updatedToVersion);
        if (_config.AutoCheckUpdates) _ = CheckForUpdatesSilentlyAsync();
    }

    // =========================================================================================
    // Window plumbing, vertical resize, saved bounds & power/session recovery
    // =========================================================================================

    private void AttachVerticalResize(Control handle)
    {
        handle.MouseDown += (_, e) =>
        {
            if (e.Button != MouseButtons.Left) return;
            _resizingHeight = true;
            _resizeStartScreenY = Cursor.Position.Y;
            _resizeStartHeight = Height;
            handle.Capture = true;
        };
        handle.MouseMove += (_, _) =>
        {
            if (!_resizingHeight) return;
            int dy = Cursor.Position.Y - _resizeStartScreenY;
            int maxH = 1400;
            try
            {
                var scr = Screen.FromControl(this);
                maxH = Math.Max(MinimumSize.Height, scr.WorkingArea.Bottom - Top);
            }
            catch { }

            int nextH = Math.Clamp(_resizeStartHeight + dy, MinimumSize.Height, Math.Min(MaximumSize.Height, maxH));
            if (Height != nextH) Height = nextH;
        };
        handle.MouseUp += (_, e) =>
        {
            if (!_resizingHeight || e.Button != MouseButtons.Left) return;
            _resizingHeight = false;
            handle.Capture = false;
            CaptureWindowBounds(persist: true);
        };
    }

    private void RestoreWindowLocation()
    {
        if (!_config.WindowX.HasValue || !_config.WindowY.HasValue)
        {
            StartPosition = FormStartPosition.CenterScreen;
            return;
        }

        try
        {
            var areas = Screen.AllScreens.Select(s => s.WorkingArea).ToList();
            if (areas.Count == 0)
            {
                StartPosition = FormStartPosition.CenterScreen;
                return;
            }

            StartPosition = FormStartPosition.Manual;
            Location = ProfileLogic.ClampWindowLocation(
                new Point(_config.WindowX.Value, _config.WindowY.Value),
                Size,
                areas);
        }
        catch
        {
            StartPosition = FormStartPosition.CenterScreen;
        }
    }

    private void CaptureWindowBounds(bool persist)
    {
        if (WindowState != FormWindowState.Normal || !IsHandleCreated) return;
        bool changed = _config.WindowX != Location.X ||
                       _config.WindowY != Location.Y ||
                       _config.WindowHeight != Height;
        if (!changed) return;

        _config.WindowX = Location.X;
        _config.WindowY = Location.Y;
        _config.WindowHeight = Height;
        if (persist) Save();
    }

    protected override void OnResizeEnd(EventArgs e)
    {
        base.OnResizeEnd(e);
        CaptureWindowBounds(persist: true);
    }

    private void OnCoreAudioStateChanged()
    {
        if (IsDisposed || !IsHandleCreated) return;
        try
        {
            BeginInvoke(new Action(() =>
            {
                if (IsDisposed) return;
                _eventCoalesceTimer.Stop();
                _eventCoalesceTimer.Start();
            }));
        }
        catch { /* closing */ }
    }

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
        Native.SetBorderColor(Handle, Theme.BgBorderStrong);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        using var pen = new Pen(Theme.BgBorder, 1f);
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

        CaptureWindowBounds(persist: true);

        _audio.StateChanged -= OnCoreAudioStateChanged;
        _voice.CommandRecognized -= OnVoiceCommandRecognized;
        _voice.StatusChanged -= OnVoiceStatusChanged;
        SystemEvents.PowerModeChanged -= OnPowerModeChanged;
        SystemEvents.SessionSwitch -= OnSessionSwitch;

        _eventCoalesceTimer.Stop();
        _pollTimer.Stop();
        _statusTimer.Stop();
        _debounceTimer.Stop();
        _recoveryTimer.Stop();
        _eventCoalesceTimer.Dispose();
        _pollTimer.Dispose();
        _statusTimer.Dispose();
        _debounceTimer.Dispose();
        _recoveryTimer.Dispose();
        _voice.Dispose();
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

    private void ToggleWindowVisibility()
    {
        if (Visible && WindowState != FormWindowState.Minimized && ContainsFocus)
            HideToTray();
        else
            ShowFromTray();
    }

    private void HideToTray()
    {
        CaptureWindowBounds(persist: true);
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
        if (IsHandleCreated) Native.BringToFrontNative(Handle);
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
        _statusLabel.ForeColor = warning ? Theme.Warning : Theme.TextSoft;
        _statusDotColor = warning ? Theme.Warning : Theme.AccentEnd;
        _statusDot.Invalidate();
        _statusTimer.Stop();
        _statusTimer.Start();
    }

    // =========================================================================================
    // UI builders
    // =========================================================================================

    private ModernButton MakeIconButton(string glyph, string tip, EventHandler onClick, ModernButtonKind kind)
    {
        var btn = new ModernButton(kind)
        {
            Text = glyph,
            Dock = DockStyle.Right,
            Width = 38,
            BackColor = Theme.BgMain
        };
        btn.Click += onClick;
        _tip.SetToolTip(btn, tip);
        return btn;
    }

    private TableLayoutPanel BuildEmptyPanel()
    {
        var t = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 5,
            BackColor = Theme.BgMain,
            Visible = false
        };
        t.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        t.RowStyles.Add(new RowStyle(SizeType.Percent, 28));
        t.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        t.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        t.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        t.RowStyles.Add(new RowStyle(SizeType.Percent, 72));

        var emblem = new Panel { Size = new Size(64, 64), BackColor = Theme.BgMain, Anchor = AnchorStyles.None, Margin = new Padding(0, 0, 0, 12) };
        emblem.Paint += (_, e) =>
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using var halo = new SolidBrush(Theme.AccentSoft);
            e.Graphics.FillEllipse(halo, 2, 2, 60, 60);
            Gfx.DrawAppEmblem(e.Graphics, new RectangleF(10, 10, 44, 44));
        };

        var lbl = new Label
        {
            Text = "No profile cards yet\nPair an output, microphone & volume so one click or hotkey switches everything.",
            Font = Theme.Body,
            ForeColor = Theme.TextMuted,
            AutoSize = false,
            Size = new Size(310, 66),
            TextAlign = ContentAlignment.MiddleCenter,
            Anchor = AnchorStyles.None
        };
        var btn = new ModernButton(ModernButtonKind.Primary)
        {
            Text = "Create card from current devices",
            Size = new Size(256, 38),
            Anchor = AnchorStyles.None,
            CornerRadius = 10,
            Margin = new Padding(0, 8, 0, 0)
        };
        btn.Click += (_, _) => _ = OpenCardEditorAsync(NewCardFromCurrentDevices(), true);

        t.Controls.Add(emblem, 0, 1);
        t.Controls.Add(lbl, 0, 2);
        t.Controls.Add(btn, 0, 3);
        return t;
    }

    private Panel BuildSettingsPanel()
    {
        var panel = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = Theme.BgMain,
            Padding = new Padding(16, 4, 16, 12),
            AutoScroll = true,
            Visible = false
        };

        var stack = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            ColumnCount = 1,
            BackColor = Theme.BgMain
        };
        stack.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        // ---- Section 1: Global Hotkeys ----
        var hkGrid = MakeSectionGrid();
        AddSectionTitle(hkGrid, "GLOBAL HOTKEYS  ·  CLICK TO REBIND");
        AddHotKeyRow(hkGrid, "Cycle Cards", HotKeyTarget.Card);
        AddHotKeyRow(hkGrid, "Cycle Output", HotKeyTarget.Output);
        AddHotKeyRow(hkGrid, "Cycle Input", HotKeyTarget.Input);
        AddHotKeyRow(hkGrid, "Toggle Mic Mute", HotKeyTarget.Mute);
        AddHotKeyRow(hkGrid, "Show / Hide Window", HotKeyTarget.ToggleWindow);

        _jumpLabel = MakeRowLabel("", 36);
        hkGrid.Controls.Add(_jumpLabel);
        hkGrid.Controls.Add(NewToggle(_config.JumpHotKeysEnabled, on =>
        {
            _config.JumpHotKeysEnabled = on;
            Save();
            RegisterAllHotKeys();
            RefreshCardSlots();
        }));
        stack.Controls.Add(WrapSectionCard(hkGrid));

        // ---- Section 2: Behavior & Audio Cues ----
        var behGrid = MakeSectionGrid();
        AddSectionTitle(behGrid, "BEHAVIOR & AUDIO CUES");
        behGrid.Controls.Add(MakeRowLabel("Fallback on disconnect", 34));
        _fallbackCombo = new DarkCombo { Size = new Size(146, 26), Anchor = AnchorStyles.Right };
        _fallbackCombo.SelectedIndexChanged += (_, _) =>
        {
            if (_updatingFallbackCombo || _fallbackCombo.SelectedItem is not FallbackChoice choice) return;
            _config.FallbackMode = choice.Mode;
            _config.FallbackCardId = choice.Mode == FallbackMode.SpecificCard ? choice.CardId : null;
            Save();
            RefreshCardStates();
        };
        behGrid.Controls.Add(_fallbackCombo);

        behGrid.Controls.Add(MakeRowLabel("Play chime on profile / device switch", 32));
        behGrid.Controls.Add(NewToggle(_config.ChimeEnabled, on => { _config.ChimeEnabled = on; Save(); }));

        behGrid.Controls.Add(MakeRowLabel("Play chime on mic mute / unmute", 32));
        behGrid.Controls.Add(NewToggle(_config.MuteChimeEnabled, on => { _config.MuteChimeEnabled = on; Save(); }));
        stack.Controls.Add(WrapSectionCard(behGrid));

        // ---- Section 3: Mic Mute Overlay ----
        var muteGrid = MakeSectionGrid();
        AddSectionTitle(muteGrid, "MIC MUTE OVERLAY");
        muteGrid.Controls.Add(MakeRowLabel("Show persistent MIC MUTED banner", 32));
        muteGrid.Controls.Add(NewToggle(_config.MuteBannerEnabled, on =>
        {
            _config.MuteBannerEnabled = on;
            Save();
            ApplyMuteUi(_micMuted);
        }));

        muteGrid.Controls.Add(MakeRowLabel("Interactive banner (drag / click to unmute)", 32));
        muteGrid.Controls.Add(NewToggle(_config.MuteBannerInteractive, on =>
        {
            _config.MuteBannerInteractive = on;
            _muteBanner.Interactive = on;
            Save();
            ApplyMuteUi(_micMuted);
        }));

        muteGrid.Controls.Add(MakeRowLabel("Mute banner position", 34));
        _muteBannerPosCombo = new DarkCombo { Size = new Size(146, 26), Anchor = AnchorStyles.Right };
        _muteBannerPosCombo.Items.Add(new BannerPosChoice(MuteBannerPosition.TopCenter, "Top center"));
        _muteBannerPosCombo.Items.Add(new BannerPosChoice(MuteBannerPosition.TopRight, "Top right"));
        _muteBannerPosCombo.Items.Add(new BannerPosChoice(MuteBannerPosition.BottomRight, "Bottom right"));
        _muteBannerPosCombo.Items.Add(new BannerPosChoice(MuteBannerPosition.ActiveScreenTop, "Active monitor top"));
        _muteBannerPosCombo.Items.Add(new BannerPosChoice(MuteBannerPosition.Custom, "Custom (dragged)"));
        _muteBannerPosCombo.SelectedIndexChanged += (_, _) =>
        {
            if (_updatingBannerPosCombo || _muteBannerPosCombo.SelectedItem is not BannerPosChoice pos) return;
            _config.MuteBannerPosition = pos.Position;
            Save();
            ApplyMuteUi(_micMuted);
        };
        muteGrid.Controls.Add(_muteBannerPosCombo);
        stack.Controls.Add(WrapSectionCard(muteGrid));

        // ---- Section 4: Offline Voice Control ----
        var voiceGrid = MakeSectionGrid();
        AddSectionTitle(voiceGrid, "VOICE CONTROL  ·  OFFLINE");
        voiceGrid.Controls.Add(MakeRowLabel("Enable voice commands", 32));
        _voiceToggle = NewToggle(_config.VoiceControlEnabled, on =>
        {
            if (_updatingVoiceControls) return;
            _config.VoiceControlEnabled = on;
            Save();
            SyncVoiceControl();
        });
        voiceGrid.Controls.Add(_voiceToggle);

        voiceGrid.Controls.Add(MakeRowLabel("Wake phrase", 34));
        _voiceWakeBox = new DarkTextBox
        {
            Text = _config.VoiceWakePhrase,
            Size = new Size(146, 28),
            Anchor = AnchorStyles.Right,
            Padding = new Padding(8, 5, 8, 5)
        };
        _voiceWakeBox.Leave += (_, _) => CommitVoiceWakePhrase();
        voiceGrid.Controls.Add(_voiceWakeBox);

        voiceGrid.Controls.Add(MakeRowLabel("Listening microphone", 34));
        _voiceMicCombo = new DarkCombo { Size = new Size(146, 26), Anchor = AnchorStyles.Right };
        _tip.SetToolTip(_voiceMicCombo, "Pin a USB/desk mic so Bluetooth headsets never drop to Hands-Free mono mode");
        _voiceMicCombo.SelectedIndexChanged += (_, _) =>
        {
            if (_updatingVoiceControls || _voiceMicCombo.SelectedItem is not DeviceItem item) return;
            _config.VoiceInputDeviceId = item.Id;
            _config.VoiceInputDeviceName = item.Id.HasValue ? item.Name : "Default";
            Save();
            SyncVoiceControl();
        };
        voiceGrid.Controls.Add(_voiceMicCombo);

        voiceGrid.Controls.Add(MakeRowLabel("Confidence threshold", 30));
        var confPanel = new Panel { Size = new Size(146, 26), Anchor = AnchorStyles.Right, BackColor = Theme.BgSurface };
        _voiceConfSlider = new DarkSlider
        {
            Minimum = 50,
            Maximum = 95,
            Value = Math.Clamp(_config.VoiceConfidencePercent, 50, 95),
            Location = new Point(0, 1),
            Size = new Size(106, 24)
        };
        _voiceConfValueLbl = new Label
        {
            Text = $"{_voiceConfSlider.Value}%",
            Font = Theme.SmallBold,
            ForeColor = Theme.AccentEnd,
            BackColor = Theme.BgSurface,
            Location = new Point(108, 3),
            Size = new Size(38, 20),
            TextAlign = ContentAlignment.MiddleRight
        };
        _voiceConfSlider.ValueChanged += (_, _) =>
        {
            _voiceConfValueLbl.Text = $"{_voiceConfSlider.Value}%";
            if (_updatingVoiceControls) return;
            _config.VoiceConfidencePercent = _voiceConfSlider.Value;
            Save();
            SyncVoiceControl();
        };
        confPanel.Controls.Add(_voiceConfSlider);
        confPanel.Controls.Add(_voiceConfValueLbl);
        voiceGrid.Controls.Add(confPanel);

        _voiceStatusLbl = new Label
        {
            Text = "Status: Disabled",
            Font = Theme.SmallBold,
            ForeColor = Theme.TextMuted,
            BackColor = Theme.BgSurface,
            AutoSize = false,
            Dock = DockStyle.Fill,
            Height = 20,
            Margin = new Padding(0, 6, 0, 2)
        };
        voiceGrid.Controls.Add(_voiceStatusLbl);
        voiceGrid.SetColumnSpan(_voiceStatusLbl, 2);

        _voiceHintLbl = new Label
        {
            Font = Theme.Small,
            ForeColor = Theme.TextMuted,
            BackColor = Theme.BgSurface,
            AutoSize = false,
            Dock = DockStyle.Fill,
            Height = 46,
            Margin = new Padding(0, 0, 0, 2)
        };
        voiceGrid.Controls.Add(_voiceHintLbl);
        voiceGrid.SetColumnSpan(_voiceHintLbl, 2);
        stack.Controls.Add(WrapSectionCard(voiceGrid));

        // ---- Section 5: Profiles, System & Updates ----
        var sysGrid = MakeSectionGrid();
        AddSectionTitle(sysGrid, "PROFILES, SYSTEM & UPDATES");
        sysGrid.Controls.Add(MakeRowLabel("Start with Windows (minimized to tray)", 32));
        sysGrid.Controls.Add(NewToggle(IsStartupEnabled(), SetStartupSafe));

        sysGrid.Controls.Add(MakeRowLabel("Check for updates automatically on startup", 32));
        sysGrid.Controls.Add(NewToggle(_config.AutoCheckUpdates, on =>
        {
            _config.AutoCheckUpdates = on;
            Save();
            if (on && _availableUpdate == null) _ = CheckForUpdatesSilentlyAsync(delayMs: 250);
        }));

        var filterBtn = MakeActionButton("Choose devices for cycling…", (_, _) => _ = OpenDeviceFilterDialogAsync());
        filterBtn.Margin = new Padding(0, 6, 0, 0);
        sysGrid.Controls.Add(filterBtn);
        sysGrid.SetColumnSpan(filterBtn, 2);

        var profileIoRow = MakeTwoButtonRow(
            ("Export Profiles…", (_, _) => ExportProfiles()),
            ("Import Profiles…", (_, _) => ImportProfiles()));
        sysGrid.Controls.Add(profileIoRow);
        sysGrid.SetColumnSpan(profileIoRow, 2);

        var dataRow = MakeTwoButtonRow(
            ("Open Data Folder", (_, _) => OpenPathInShell(AppLog.DataDir, isDirectory: true)),
            ("Open Log File", (_, _) => OpenPathInShell(AppLog.FilePath, isDirectory: false)));
        sysGrid.Controls.Add(dataRow);
        sysGrid.SetColumnSpan(dataRow, 2);

        _checkUpdateBtn = MakeActionButton($"Check for Updates  (v{AppInfo.Version})", (_, _) =>
        {
            if (_availableUpdate is { UpdateAvailable: true })
                ShowUpdateDialog();
            else
                _ = CheckForUpdatesAsync();
        });
        _checkUpdateBtn.Margin = new Padding(0, 6, 0, 0);
        sysGrid.Controls.Add(_checkUpdateBtn);
        sysGrid.SetColumnSpan(_checkUpdateBtn, 2);

        var pathLbl = new Label
        {
            Text = $"Build: v{AppInfo.Version} ({AppInfo.BuildFlavorLabel})\nSettings: {_store.FilePath}\nLog: {AppLog.FilePath}",
            Font = Theme.Small,
            ForeColor = Theme.TextMuted,
            BackColor = Theme.BgSurface,
            AutoSize = false,
            Dock = DockStyle.Fill,
            Height = 60,
            Margin = new Padding(0, 8, 0, 0)
        };
        sysGrid.Controls.Add(pathLbl);
        sysGrid.SetColumnSpan(pathLbl, 2);
        stack.Controls.Add(WrapSectionCard(sysGrid));

        panel.Controls.Add(stack);
        UpdateHotKeyButtons();
        RefreshFallbackCombo();
        RefreshMuteBannerPosCombo();
        RefreshVoiceMicCombo();
        RefreshVoiceHints();
        return panel;
    }

    private static TableLayoutPanel MakeSectionGrid()
    {
        var t = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            ColumnCount = 2,
            BackColor = Theme.BgSurface
        };
        t.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        t.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 152));
        return t;
    }

    private static SectionCard WrapSectionCard(Control content)
    {
        var card = new SectionCard
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            Margin = new Padding(0, 0, 0, 10)
        };
        card.Controls.Add(content);
        return card;
    }

    private static TableLayoutPanel MakeTwoButtonRow(
        (string Text, EventHandler OnClick) left,
        (string Text, EventHandler OnClick) right)
    {
        var row = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Height = 36,
            ColumnCount = 2,
            RowCount = 1,
            Margin = new Padding(0, 6, 0, 0),
            BackColor = Theme.BgSurface
        };
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));

        var leftBtn = MakeActionButton(left.Text, left.OnClick);
        leftBtn.Margin = new Padding(0, 0, 4, 0);
        var rightBtn = MakeActionButton(right.Text, right.OnClick);
        rightBtn.Margin = new Padding(4, 0, 0, 0);
        row.Controls.Add(leftBtn, 0, 0);
        row.Controls.Add(rightBtn, 1, 0);
        return row;
    }

    private static ModernButton MakeActionButton(string text, EventHandler onClick)
    {
        var b = new ModernButton(ModernButtonKind.Secondary)
        {
            Text = text,
            Dock = DockStyle.Fill,
            Height = 32,
            CornerRadius = 8
        };
        b.Click += onClick;
        return b;
    }

    private void ExportProfiles()
    {
        using var sfd = new SaveFileDialog
        {
            Title = "Export Audio Profiles",
            Filter = "JSON Profile Bundle (*.json)|*.json|All Files (*.*)|*.*",
            FileName = "AudioProfileSwitcher-profiles.json",
            DefaultExt = "json"
        };
        if (sfd.ShowDialog(this) != DialogResult.OK) return;

        try
        {
            File.WriteAllText(sfd.FileName, ProfileLogic.ExportProfilesJson(_config));
            SetStatus($"Exported {_config.Cards.Count} card(s) to {Path.GetFileName(sfd.FileName)}", false);
        }
        catch (Exception ex)
        {
            AppLog.Error("Failed to export profiles", ex);
            SetStatus("Export failed: " + ex.Message, true);
        }
    }

    private void ImportProfiles()
    {
        using var ofd = new OpenFileDialog
        {
            Title = "Import Audio Profiles",
            Filter = "JSON Profile Bundle (*.json)|*.json|All Files (*.*)|*.*"
        };
        if (ofd.ShowDialog(this) != DialogResult.OK) return;

        try
        {
            var info = new FileInfo(ofd.FileName);
            if (info.Length > ProfileLogic.MaxProfileJsonBytes)
            {
                SetStatus($"Import failed: file exceeds {ProfileLogic.MaxProfileJsonBytes / 1024} KB limit", true);
                return;
            }

            var json = File.ReadAllText(ofd.FileName);
            var (addedCards, addedFilters) = ProfileLogic.ImportProfilesJson(json, _config, _snap);
            Save();
            RenderCards();
            SetStatus($"Imported {addedCards} new card(s) and {addedFilters} filter item(s)", false);
        }
        catch (Exception ex)
        {
            AppLog.Error("Failed to import profiles", ex);
            SetStatus("Import failed: " + ex.Message, true);
        }
    }

    private async Task ShowUpdatedConfirmationAsync(string version)
    {
        try
        {
            await Task.Delay(350);
            if (IsDisposed) return;
            ShowFeedback("UPDATED", $"Now running {version}", Theme.Success, playSwitchChime: false);
            AppLog.Info($"Post-update startup confirmed on {version}.");
        }
        catch { }
    }

    private async Task CheckForUpdatesSilentlyAsync(int delayMs = 4000)
    {
        try
        {
            await Task.Delay(delayMs);
            if (IsDisposed || _checkingUpdates || _availableUpdate is { UpdateAvailable: true }) return;

            _checkingUpdates = true;
            var res = await UpdateService.CheckForUpdatesAsync();
            if (IsDisposed) return;

            if (res.UpdateAvailable)
            {
                ApplyAvailableUpdateState(res);
                AppLog.Info($"Background update check found {res.LatestVersion} (verified asset: {res.HasVerifiedDownload}).");
            }
        }
        catch (Exception ex)
        {
            AppLog.Info("Silent startup update check skipped: " + ex.Message);
        }
        finally
        {
            _checkingUpdates = false;
        }
    }

    private async Task CheckForUpdatesAsync()
    {
        if (_checkingUpdates) return;
        _checkingUpdates = true;
        _checkUpdateBtn.Enabled = false;
        _checkUpdateBtn.Text = "Checking GitHub Releases…";
        try
        {
            var res = await UpdateService.CheckForUpdatesAsync();
            if (IsDisposed) return;

            if (!res.UpdateAvailable)
            {
                _availableUpdate = null;
                _titleBar.Invalidate();
                SetStatus($"You're on the latest version (v{AppInfo.Version})", false);
                return;
            }

            ApplyAvailableUpdateState(res);
            SetStatus($"Update available: {res.LatestVersion}", false);
            ShowUpdateDialog();
        }
        catch (Exception ex)
        {
            AppLog.Error("Update check failed", ex);
            if (!IsDisposed) SetStatus("Couldn't check for updates: " + ex.Message, true);
        }
        finally
        {
            _checkingUpdates = false;
            if (!IsDisposed)
            {
                _checkUpdateBtn.Enabled = true;
                RefreshUpdateButton();
            }
        }
    }

    private void ApplyAvailableUpdateState(ReleaseCheckResult release)
    {
        _availableUpdate = release;
        RefreshUpdateButton();
        _titleBar.Invalidate();
    }

    private void RefreshUpdateButton()
    {
        if (_availableUpdate is { UpdateAvailable: true } upd)
        {
            _checkUpdateBtn.Kind = ModernButtonKind.Primary;
            _checkUpdateBtn.Text = $"Install Update  ({upd.LatestVersion})";
        }
        else
        {
            _checkUpdateBtn.Kind = ModernButtonKind.Secondary;
            _checkUpdateBtn.Text = $"Check for Updates  (v{AppInfo.Version})";
        }
    }

    private void ShowUpdateDialog()
    {
        if (_availableUpdate is not { UpdateAvailable: true } upd || IsDisposed) return;

        bool wasHidden = !Visible || WindowState == FormWindowState.Minimized;
        if (wasHidden) ShowFromTray();

        using var dlg = new UpdateDialog(upd, relaunchMinimized: wasHidden);
        dlg.ShowDialog(this);
        if (dlg.UpdateLaunched)
        {
            _allowClose = true;
            Close();
        }
    }

    private void OpenPathInShell(string path, bool isDirectory)
    {
        try
        {
            Directory.CreateDirectory(AppLog.DataDir);
            if (!isDirectory && !File.Exists(path))
            {
                File.WriteAllText(path, "");
            }
            Process.Start(new ProcessStartInfo { FileName = path, UseShellExecute = true });
        }
        catch (Exception ex)
        {
            AppLog.Error($"Failed to open '{path}'", ex);
            SetStatus("Couldn't open path: " + ex.Message, true);
        }
    }

    private void RefreshMuteBannerPosCombo()
    {
        if (_muteBannerPosCombo == null) return;
        _updatingBannerPosCombo = true;
        try
        {
            int idx = _muteBannerPosCombo.Items.Cast<BannerPosChoice>()
                .ToList()
                .FindIndex(c => c.Position == _config.MuteBannerPosition);
            _muteBannerPosCombo.SelectedIndex = idx >= 0 ? idx : 0;
        }
        finally
        {
            _updatingBannerPosCombo = false;
        }
    }

    private void RefreshFallbackCombo()
    {
        if (_fallbackCombo == null) return;
        _updatingFallbackCombo = true;
        try
        {
            _fallbackCombo.Items.Clear();
            _fallbackCombo.Items.Add(new FallbackChoice(FallbackMode.None, null, "(None)"));
            _fallbackCombo.Items.Add(new FallbackChoice(FallbackMode.PreviousCard, null, "(Previous card)"));
            foreach (var c in _config.Cards)
                _fallbackCombo.Items.Add(new FallbackChoice(FallbackMode.SpecificCard, c.Id, c.Name));

            int idx = _config.FallbackMode switch
            {
                FallbackMode.PreviousCard => 1,
                FallbackMode.SpecificCard when _config.FallbackCardId.HasValue =>
                    _config.Cards.FindIndex(c => c.Id == _config.FallbackCardId.Value) is int ci && ci >= 0 ? ci + 2 : 0,
                _ => 0
            };
            _fallbackCombo.SelectedIndex = idx;
        }
        finally
        {
            _updatingFallbackCombo = false;
        }
    }

    private static Label MakeRowLabel(string text, int height = 34) => new()
    {
        Text = text,
        Font = Theme.Body,
        ForeColor = Theme.TextSoft,
        BackColor = Theme.BgSurface,
        AutoSize = false,
        Dock = DockStyle.Fill,
        Height = height,
        TextAlign = ContentAlignment.MiddleLeft
    };

    private static void AddSectionTitle(TableLayoutPanel t, string text)
    {
        var h = new Label
        {
            Text = text,
            Font = Theme.MicroBold,
            ForeColor = Theme.AccentEnd,
            BackColor = Theme.BgSurface,
            AutoSize = false,
            Dock = DockStyle.Fill,
            Height = 20,
            TextAlign = ContentAlignment.MiddleLeft,
            Margin = new Padding(0, 0, 0, 6)
        };
        t.Controls.Add(h);
        t.SetColumnSpan(h, 2);
    }

    private void AddHotKeyRow(TableLayoutPanel t, string text, HotKeyTarget target)
    {
        var btn = new ModernButton(ModernButtonKind.Keycap)
        {
            Size = new Size(146, 26),
            Anchor = AnchorStyles.Right,
            Margin = new Padding(0, 2, 0, 2)
        };
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
    // Views & diffed CardControl rendering
    // =========================================================================================

    private void ToggleSettings()
    {
        if (_capturing != null) CancelCapture();
        if (_settingsOpen) CommitVoiceWakePhrase();
        _settingsOpen = !_settingsOpen;
        UpdateViews();
    }

    private void UpdateViews()
    {
        bool hasCards = _config.Cards.Count > 0;
        _settingsPanel.Visible = _settingsOpen;
        _cardsPanel.Visible = !_settingsOpen && hasCards;
        _emptyPanel.Visible = !_settingsOpen && !hasCards;
        _gearBtn.Active = _settingsOpen;
    }

    private string? SlotFor(int index) =>
        _config.JumpHotKeysEnabled && _config.CardHotKey.IsEnabled && index < 9 ? (index + 1).ToString() : null;

    private void RenderCards()
    {
        _cardsPanel.SuspendLayout();

        var existingById = _cardsPanel.Controls.OfType<CardControl>()
            .ToDictionary(cc => cc.Card.Id);

        for (int i = 0; i < _config.Cards.Count; i++)
        {
            var card = _config.Cards[i];
            if (existingById.Remove(card.Id, out var cc))
            {
                cc.BindCard(card);
                cc.SlotText = SlotFor(i);
            }
            else
            {
                cc = CreateCardControl(card, SlotFor(i));
                _cardsPanel.Controls.Add(cc);
            }

            if (_cardsPanel.Controls.GetChildIndex(cc, false) != i)
            {
                _cardsPanel.Controls.SetChildIndex(cc, i);
            }
        }

        foreach (var stale in existingById.Values)
        {
            _cardsPanel.Controls.Remove(stale);
            stale.Dispose();
        }

        ResizeCards();
        RefreshCardStates();
        RefreshFallbackCombo();
        RefreshVoiceHints();
        _cardsPanel.ResumeLayout(true);
        UpdateViews();
        RegisterAllHotKeys();
        SyncVoiceControl();
    }

    private CardControl CreateCardControl(AudioCard card, string? slot)
    {
        var cc = new CardControl(card) { SlotText = slot };
        cc.ActivateRequested += c => _ = ActivateCardAsync(c);
        cc.EditRequested += c => _ = OpenCardEditorAsync(c, false);
        cc.DuplicateRequested += DuplicateCard;
        cc.SetFallbackRequested += ToggleCardAsFallback;
        cc.DeleteRequested += DeleteCard;
        return cc;
    }

    private void DuplicateCard(AudioCard card)
    {
        int idx = _config.Cards.IndexOf(card);
        var copy = card.CloneAsDuplicate();
        if (idx >= 0 && idx + 1 <= _config.Cards.Count)
            _config.Cards.Insert(idx + 1, copy);
        else
            _config.Cards.Add(copy);

        Save();
        RenderCards();
        SetStatus($"Duplicated '{card.Name}'", false);
    }

    private void ToggleCardAsFallback(AudioCard card)
    {
        if (_config.FallbackMode == FallbackMode.SpecificCard && _config.FallbackCardId == card.Id)
        {
            _config.FallbackMode = FallbackMode.None;
            _config.FallbackCardId = null;
            SetStatus($"Cleared '{card.Name}' as fallback", false);
        }
        else
        {
            _config.FallbackMode = FallbackMode.SpecificCard;
            _config.FallbackCardId = card.Id;
            SetStatus($"Fallback set to '{card.Name}'", false);
        }

        Save();
        RefreshFallbackCombo();
        RefreshCardStates();
    }

    private void DeleteCard(AudioCard card)
    {
        _config.Cards.Remove(card);
        ProfileLogic.HandleCardDeleted(_config, card.Id);
        Save();
        QueueRender();
    }

    private void QueueRender()
    {
        if (IsHandleCreated) BeginInvoke(new Action(RenderCards));
        else RenderCards();
    }

    private void ResizeCards()
    {
        float k = _cardsPanel.DeviceDpi / 96f;
        int width = Math.Max((int)Math.Round(220 * k), _cardsPanel.ClientSize.Width - _cardsPanel.Padding.Horizontal);
        int marginBottom = (int)Math.Round(10 * k);
        foreach (var cc in _cardsPanel.Controls.OfType<CardControl>())
        {
            cc.Scale = k;
            cc.Margin = new Padding(0, 0, 0, marginBottom);
            cc.Size = new Size(width, cc.PreferredHeight(k));
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
            cc.IsActive = snap != null && ProfileLogic.CardIsActive(c, snap);
            cc.IsFallback = _config.FallbackMode == FallbackMode.SpecificCard && _config.FallbackCardId == c.Id;
            cc.OutMissing = snap != null && c.OutputId.HasValue && !snap.OutputIds.Contains(c.OutputId.Value);
            cc.CommMissing = snap != null && c.CommOutputId.HasValue && !snap.OutputIds.Contains(c.CommOutputId.Value);
            cc.InMissing = snap != null && c.InputId.HasValue && !snap.InputIds.Contains(c.InputId.Value);
        }
    }

    private void UpdateNow()
    {
        string outText = _snap?.OutName ?? "No output device";
        string? commShort = null;
        if (_snap?.DefaultCommOut is Guid commId && commId != _snap.DefaultOut && !string.IsNullOrEmpty(_snap.CommOutName))
        {
            commShort = ProfileLogic.ExtractShortDeviceName(_snap.CommOutName) ?? _snap.CommOutName;
        }
        string inText = _snap?.InName ?? "No input device";
        _liveNow.UpdateLiveState(outText, commShort, inText, _micMuted);
        UpdateTrayTooltip();
    }

    private void UpdateTrayTooltip()
    {
        var lines = new List<string>();
        var activeCard = _snap != null ? _config.Cards.FirstOrDefault(c => ProfileLogic.CardIsActive(c, _snap)) : null;
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

        int gap = (int)Math.Round(_cardsPanel.DeviceDpi / 96f * 5f);
        int newDropIndex = cards.Count;
        int newDropLineY = cards[^1].Bottom + gap;
        for (int i = 0; i < cards.Count; i++)
        {
            if (pt.Y < cards[i].Top + cards[i].Height / 2)
            {
                newDropIndex = i;
                newDropLineY = cards[i].Top - gap;
                break;
            }
        }

        if (newDropIndex != _dropIndex || newDropLineY != _dropLineY)
        {
            _dropIndex = newDropIndex;
            _dropLineY = newDropLineY;
            _cardsPanel.Invalidate();
        }
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
        var snap = _snap;
        var card = new AudioCard
        {
            Name = ProfileLogic.SuggestCardName(snap?.OutName, snap?.InName)
        };
        if (snap?.DefaultOut is Guid o) { card.OutputId = o; card.OutputName = snap.OutName ?? "None"; }
        if (snap?.DefaultCommOut is Guid co && co != snap.DefaultOut)
        {
            card.CommOutputId = co;
            card.CommOutputName = snap.CommOutName ?? "None";
        }
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

        var co = dlg.SelectedCommOutput;
        card.CommOutputId = co?.Id;
        card.CommOutputName = co?.Id == null ? "None" : co.Name;

        var i = dlg.SelectedInput;
        card.InputId = i?.Id;
        card.InputName = i?.Id == null ? "None" : i.Name;

        card.OutputVolume = dlg.OutputVolume;
        card.InputVolume = dlg.InputVolume;
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

        using var dlg = new DeviceFilterDialog(outputs, inputs, _config.CycleOutputs, _config.CycleInputs);
        if (dlg.ShowDialog(this) != DialogResult.OK) return;

        _config.CycleOutputs = dlg.SelectedOutputs;
        _config.CycleInputs = dlg.SelectedInputs;
        Save();
        SetStatus("Cycle filter saved", false);
    }

    // =========================================================================================
    // Hotkeys & Esc navigation
    // =========================================================================================

    private HotKeyBinding GetBinding(HotKeyTarget t) => t switch
    {
        HotKeyTarget.Card => _config.CardHotKey,
        HotKeyTarget.Output => _config.OutputHotKey,
        HotKeyTarget.Input => _config.InputHotKey,
        HotKeyTarget.ToggleWindow => _config.ToggleWindowHotKey,
        _ => _config.MuteHotKey
    };

    private void SetBinding(HotKeyTarget t, HotKeyBinding b)
    {
        switch (t)
        {
            case HotKeyTarget.Card: _config.CardHotKey = b; break;
            case HotKeyTarget.Output: _config.OutputHotKey = b; break;
            case HotKeyTarget.Input: _config.InputHotKey = b; break;
            case HotKeyTarget.ToggleWindow: _config.ToggleWindowHotKey = b; break;
            default: _config.MuteHotKey = b; break;
        }
    }

    private static string TargetName(HotKeyTarget t) => t switch
    {
        HotKeyTarget.Card => "Cycle Cards",
        HotKeyTarget.Output => "Cycle Output",
        HotKeyTarget.Input => "Cycle Input",
        HotKeyTarget.ToggleWindow => "Show / Hide Window",
        _ => "Toggle Mic Mute"
    };

    private bool RegisterAllHotKeys(bool force = false)
    {
        if (_capturing != null) return true;

        var items = new List<(string Name, HotKeyBinding Binding, Action Action)>
        {
            ("Cycle Cards", _config.CardHotKey, () => _ = CycleCardAsync()),
            ("Cycle Output", _config.OutputHotKey, () => _ = CycleOutputAsync()),
            ("Cycle Input", _config.InputHotKey, () => _ = CycleInputAsync()),
            ("Toggle Mic Mute", _config.MuteHotKey, () => _ = ToggleMicMuteAsync()),
            ("Show / Hide Window", _config.ToggleWindowHotKey, ToggleWindowVisibility)
        };

        if (_config.JumpHotKeysEnabled && _config.CardHotKey.IsEnabled)
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

        var failed = _hotKeys.RegisterAll(items, force);
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
            var b = GetBinding(target);
            btn.Active = false;
            btn.Text = b.Format();
            btn.ForeColor = b.IsEnabled ? Theme.TextPrimary : Theme.TextMuted;
        }
        _jumpLabel.Text = _config.CardHotKey.IsEnabled
            ? $"Jump straight to card N with {_config.CardHotKey.FormatModifiers()} + 1-9"
            : "Jump straight to card N (requires Cycle Cards hotkey)";
    }

    private void BeginCapture(HotKeyTarget target)
    {
        _capturing = target;
        _hotKeys.UnregisterAll();
        UpdateHotKeyButtons();
        var btn = _hkButtons[target];
        btn.Active = true;
        btn.Text = "Press keys…";
        btn.ForeColor = Theme.AccentEnd;
        SetStatus("Press shortcut (Backspace/Del to disable, Esc to cancel)", false);
    }

    private bool EndCapture()
    {
        _capturing = null;
        UpdateHotKeyButtons();
        RefreshCardSlots();
        return RegisterAllHotKeys(force: true);
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
        {
            if (keyData == Keys.Escape)
            {
                if (_settingsOpen) ToggleSettings();
                else HideToTray();
                return true;
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }

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

        if (mods == HotKeyModifiers.None && key is Keys.Back or Keys.Delete)
        {
            SetBinding(target, new HotKeyBinding { Modifiers = HotKeyModifiers.None, Key = Keys.None });
            Save();
            EndCapture();
            SetStatus($"{TargetName(target)} disabled", false);
            return true;
        }

        if (mods == HotKeyModifiers.None)
        {
            SetStatus("Add at least one modifier (Ctrl, Shift, Alt or Win)", true);
            return true;
        }

        var binding = new HotKeyBinding { Modifiers = mods, Key = key };
        var clash = ProfileLogic.FindHotKeyConflict(_config, target, binding);
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

    // =========================================================================================
    // Audio actions (off UI thread, directly consuming returned AudioSnapshot)
    // =========================================================================================

    private async Task ActivateCardAsync(AudioCard card, string? categoryOverride = null)
    {
        ApplyResult result;
        try
        {
            result = await _audio.ApplyAsync(
                card.OutputId,
                card.OutputName,
                card.InputId,
                card.InputName,
                card.CommOutputId,
                card.CommOutputName,
                card.OutputVolume,
                card.InputVolume);
        }
        catch (Exception ex)
        {
            AppLog.Error($"Failed to activate card '{card.Name}'", ex);
            if (!IsDisposed) ShowFeedback("SWITCH FAILED", ex.Message, Theme.Danger, playSwitchChime: false);
            return;
        }

        if (IsDisposed) return;

        bool relinked = false;
        if (result.ResolvedOutputId.HasValue)
        {
            card.OutputId = result.ResolvedOutputId.Value;
            relinked = true;
            AppLog.Info($"Re-linked output for card '{card.Name}' by name to {card.OutputId}.");
        }
        if (result.ResolvedCommOutputId.HasValue)
        {
            card.CommOutputId = result.ResolvedCommOutputId.Value;
            relinked = true;
            AppLog.Info($"Re-linked comm output for card '{card.Name}' by name to {card.CommOutputId}.");
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
            ShowFeedback("CARD UNAVAILABLE", $"{card.Name}: {result.Problem ?? "nothing to apply"}", Theme.Warning, playSwitchChime: false);
            return;
        }

        ProfileLogic.RecordCardSwitch(_config, card, _snap);
        Save();

        if (result.Snapshot != null)
            ApplySnapshot(result.Snapshot, allowAutoEvents: false, force: true);
        else if (relinked)
            RefreshCardStates();

        if (result.Problem != null)
            ShowFeedback("CARD PARTLY ACTIVE", $"{card.Name}: {result.Problem}", Theme.Warning, playSwitchChime: false);
        else
            ShowFeedback(categoryOverride ?? "CARD ACTIVE", card.Name, Theme.Accent, playSwitchChime: true);

        AppLog.Info($"Activated card '{card.Name}' ({categoryOverride ?? "manual"}).");
    }

    private Task JumpToCardAsync(int slot) =>
        slot < _config.Cards.Count ? ActivateCardAsync(_config.Cards[slot]) : Task.CompletedTask;

    private async Task CycleCardAsync()
    {
        if (_config.Cards.Count == 0)
        {
            ShowFeedback("CARDS", "No cards yet", Theme.Warning, playSwitchChime: false);
            return;
        }

        var next = ProfileLogic.FindNextAvailableCard(_config.Cards, _snap, _config.ActiveCardId);
        if (next == null)
        {
            ShowFeedback("CARDS", "No card has all its devices connected", Theme.Warning, playSwitchChime: false);
            return;
        }

        await ActivateCardAsync(next);
    }

    private async Task CycleOutputAsync()
    {
        try
        {
            var res = await _audio.CycleOutputAsync(_config.CycleOutputs);
            if (IsDisposed) return;
            if (res.Device == null)
            {
                ShowFeedback("OUTPUT", "Fewer than two devices to cycle through", Theme.Warning, playSwitchChime: false);
                return;
            }
            if (res.Snapshot != null) ApplySnapshot(res.Snapshot, allowAutoEvents: false, force: true);
            ShowFeedback("OUTPUT", res.Device.Name, Theme.Accent, playSwitchChime: true);
            AppLog.Info($"Cycled output to '{res.Device.Name}'.");
        }
        catch (Exception ex)
        {
            AppLog.Error("CycleOutput failed", ex);
            if (!IsDisposed) ShowFeedback("SWITCH FAILED", ex.Message, Theme.Danger, playSwitchChime: false);
        }
    }

    private async Task CycleInputAsync()
    {
        try
        {
            var res = await _audio.CycleInputAsync(_config.CycleInputs);
            if (IsDisposed) return;
            if (res.Device == null)
            {
                ShowFeedback("INPUT", "Fewer than two devices to cycle through", Theme.Warning, playSwitchChime: false);
                return;
            }
            if (res.Snapshot != null) ApplySnapshot(res.Snapshot, allowAutoEvents: false, force: true);
            ShowFeedback("INPUT", res.Device.Name, Theme.Accent, playSwitchChime: true);
            AppLog.Info($"Cycled input to '{res.Device.Name}'.");
        }
        catch (Exception ex)
        {
            AppLog.Error("CycleInput failed", ex);
            if (!IsDisposed) ShowFeedback("SWITCH FAILED", ex.Message, Theme.Danger, playSwitchChime: false);
        }
    }

    private async Task SwitchSingleDeviceAsync(bool isOutput, Guid deviceId, string deviceName)
    {
        try
        {
            var res = isOutput
                ? await _audio.ApplyAsync(deviceId, deviceName, null, null)
                : await _audio.ApplyAsync(null, null, deviceId, deviceName);
            if (IsDisposed) return;
            if (!res.AnyApplied)
            {
                ShowFeedback(isOutput ? "OUTPUT" : "INPUT", res.Problem ?? "Device unavailable", Theme.Warning, playSwitchChime: false);
                return;
            }
            if (res.Snapshot != null) ApplySnapshot(res.Snapshot, allowAutoEvents: false, force: true);
            ShowFeedback(isOutput ? "OUTPUT" : "INPUT", deviceName, Theme.Accent, playSwitchChime: isOutput);
            AppLog.Info($"Switched {(isOutput ? "output" : "input")} via tray menu to '{deviceName}'.");
        }
        catch (Exception ex)
        {
            AppLog.Error("Tray quick device switch failed", ex);
            if (!IsDisposed) ShowFeedback("SWITCH FAILED", ex.Message, Theme.Danger, playSwitchChime: false);
        }
    }

    private async Task ToggleMicMuteAsync()
    {
        MuteToggleResult res;
        try
        {
            res = await _audio.ToggleMuteAsync();
        }
        catch (Exception ex)
        {
            AppLog.Error("ToggleMicMute failed", ex);
            if (!IsDisposed) ShowFeedback("MICROPHONE", ex.Message, Theme.Danger, playSwitchChime: false);
            return;
        }

        if (IsDisposed) return;

        if (res.Muted == null)
        {
            ShowFeedback("MICROPHONE", "No input device", Theme.Warning, playSwitchChime: false);
            return;
        }

        bool muted = res.Muted.Value;
        if (res.Snapshot != null)
            ApplySnapshot(res.Snapshot, allowAutoEvents: false, force: true);
        else
            ApplyMuteUi(muted);

        ShowFeedback("MICROPHONE", muted ? "Muted" : "Live (Unmuted)",
            muted ? Theme.Danger : Theme.Success, playSwitchChime: false);

        if (_config.MuteChimeEnabled)
        {
            ChimePlayer.Play(muted ? ChimeCue.Muted : ChimeCue.Unmuted, waitForDeviceRouting: false);
        }

        AppLog.Info($"Mic mute toggled -> {(muted ? "Muted" : "Live")}.");
    }

    private async Task SetMicMuteAsync(bool mute)
    {
        MuteToggleResult res;
        try
        {
            res = await _audio.SetMuteAsync(mute);
        }
        catch (Exception ex)
        {
            AppLog.Error("SetMicMute failed", ex);
            if (!IsDisposed) ShowFeedback("MICROPHONE", ex.Message, Theme.Danger, playSwitchChime: false);
            return;
        }

        if (IsDisposed) return;

        if (res.Muted == null)
        {
            ShowFeedback("MICROPHONE", "No input device", Theme.Warning, playSwitchChime: false);
            return;
        }

        bool muted = res.Muted.Value;
        if (res.Snapshot != null)
            ApplySnapshot(res.Snapshot, allowAutoEvents: false, force: true);
        else
            ApplyMuteUi(muted);

        ShowFeedback("MICROPHONE", muted ? "Muted" : "Live (Unmuted)",
            muted ? Theme.Danger : Theme.Success, playSwitchChime: false);

        if (_config.MuteChimeEnabled)
        {
            ChimePlayer.Play(muted ? ChimeCue.Muted : ChimeCue.Unmuted, waitForDeviceRouting: false);
        }

        AppLog.Info($"Mic mute set -> {(muted ? "Muted" : "Live")}.");
    }

    // =========================================================================================
    // Offline Voice Control
    // =========================================================================================

    private void CommitVoiceWakePhrase()
    {
        if (_voiceWakeBox == null) return;
        string clean = ProfileLogic.SanitizeWakePhrase(_voiceWakeBox.Text);
        if (!string.Equals(_voiceWakeBox.Text, clean, StringComparison.Ordinal))
            _voiceWakeBox.Text = clean;

        if (string.Equals(_config.VoiceWakePhrase, clean, StringComparison.Ordinal)) return;
        _config.VoiceWakePhrase = clean;
        Save();
        RefreshVoiceHints();
        SyncVoiceControl();
    }

    private void RefreshVoiceMicCombo()
    {
        if (_voiceMicCombo == null) return;
        _updatingVoiceControls = true;
        try
        {
            _voiceMicCombo.Items.Clear();
            _voiceMicCombo.Items.Add(new DeviceItem(null, "(Default microphone)"));

            var inputs = _snap?.InputNames
                .OrderBy(kv => kv.Value, StringComparer.CurrentCultureIgnoreCase)
                .ToList() ?? new List<KeyValuePair<Guid, string>>();

            int selected = 0;
            foreach (var kv in inputs)
            {
                _voiceMicCombo.Items.Add(new DeviceItem(kv.Key, kv.Value, Connected: true));
                if (_config.VoiceInputDeviceId == kv.Key)
                    selected = _voiceMicCombo.Items.Count - 1;
            }

            if (_config.VoiceInputDeviceId.HasValue && selected == 0)
            {
                string label = ProfileLogic.IsMeaningfulDeviceName(_config.VoiceInputDeviceName)
                    ? _config.VoiceInputDeviceName
                    : "Pinned microphone";
                _voiceMicCombo.Items.Add(new DeviceItem(_config.VoiceInputDeviceId, label, Connected: false));
                selected = _voiceMicCombo.Items.Count - 1;
            }

            _voiceMicCombo.SelectedIndex = selected;
        }
        finally
        {
            _updatingVoiceControls = false;
        }
    }

    private void RefreshVoiceHints()
    {
        if (_voiceHintLbl == null) return;
        string wake = ProfileLogic.SanitizeWakePhrase(_config.VoiceWakePhrase);
        string sampleCard = _config.Cards.Count > 0
            ? ProfileLogic.SanitizeSpokenCardName(_config.Cards[0].Name)
            : "Headset";
        if (string.IsNullOrEmpty(sampleCard)) sampleCard = "card 1";

        _voiceHintLbl.Text =
            $"Try: \"{wake} switch to {sampleCard}\" · \"{wake} card 1\"\n" +
            $"\"{wake} next card\" · \"{wake} mute mic\" · \"{wake} show window\"";
    }

    private void SyncVoiceControl()
    {
        _voice.Configure(
            _config.VoiceControlEnabled,
            _config.VoiceWakePhrase,
            _config.VoiceConfidencePercent,
            _config.VoiceInputDeviceId,
            _config.VoiceInputDeviceName,
            _config.Cards);
    }

    private void OnVoiceStatusChanged(bool listening, string statusText)
    {
        if (IsDisposed || !IsHandleCreated) return;
        try
        {
            BeginInvoke(new Action(() =>
            {
                if (IsDisposed) return;
                _voiceListening = listening;
                _voiceStatusText = statusText;
                _titleBar.Invalidate();
                if (_voiceStatusLbl != null)
                {
                    _voiceStatusLbl.Text = $"Status: {statusText}";
                    _voiceStatusLbl.ForeColor = listening
                        ? Theme.Success
                        : (_config.VoiceControlEnabled ? Theme.Warning : Theme.TextMuted);
                }
                if (_config.VoiceControlEnabled && !listening && statusText.StartsWith("Voice unavailable", StringComparison.Ordinal))
                {
                    SetStatus(statusText, true);
                }
            }));
        }
        catch { }
    }

    private void OnVoiceCommandRecognized(VoiceCommand cmd)
    {
        if (IsDisposed || !IsHandleCreated) return;
        try
        {
            BeginInvoke(new Action(() => _ = HandleVoiceCommandAsync(cmd)));
        }
        catch { }
    }

    private async Task HandleVoiceCommandAsync(VoiceCommand cmd)
    {
        if (IsDisposed) return;

        switch (cmd.Kind)
        {
            case VoiceCommandKind.ActivateCard when cmd.CardId.HasValue:
                var card = _config.Cards.FirstOrDefault(c => c.Id == cmd.CardId.Value);
                if (card != null) await ActivateCardAsync(card, "VOICE SWITCH");
                break;
            case VoiceCommandKind.CycleCard:
                await CycleCardAsync();
                break;
            case VoiceCommandKind.CycleOutput:
                await CycleOutputAsync();
                break;
            case VoiceCommandKind.CycleInput:
                await CycleInputAsync();
                break;
            case VoiceCommandKind.MuteMic:
                await SetMicMuteAsync(true);
                break;
            case VoiceCommandKind.UnmuteMic:
                await SetMicMuteAsync(false);
                break;
            case VoiceCommandKind.ToggleMute:
                await ToggleMicMuteAsync();
                break;
            case VoiceCommandKind.ShowWindow:
                ShowFromTray();
                ShowFeedback("VOICE", "Showing AudioSwitcher", Theme.Accent, playSwitchChime: false);
                break;
            case VoiceCommandKind.HideWindow:
                HideToTray();
                ShowFeedback("VOICE", "Hidden to tray", Theme.Accent, playSwitchChime: false);
                break;
        }
    }

    private void ShowFeedback(string category, string title, Color accent, bool playSwitchChime)
    {
        SetStatus($"{category}: {title}", accent == Theme.Warning || accent == Theme.Danger);
        _osd.ShowMessage(category, title, accent);
        if (playSwitchChime && _config.ChimeEnabled)
        {
            ChimePlayer.Play(ChimeCue.Switch, waitForDeviceRouting: true);
        }
    }

    // =========================================================================================
    // Live state (single-flight coalesced), name re-linking, auto-activate & fallback modes
    // =========================================================================================

    private Task ForceRefreshAsync() => RefreshStateAsync(allowAutoEvents: false, force: true);

    private Task RefreshStateAsync(bool allowAutoEvents, bool force = false) =>
        _refreshCoalescer.RunAsync(allowAutoEvents, force);

    private async Task ExecuteRefreshPassAsync(bool allowAutoEvents, bool force)
    {
        try
        {
            var snap = await _audio.SnapshotAsync();
            if (IsDisposed || snap == null) return;
            ApplySnapshot(snap, allowAutoEvents, force);
        }
        catch (Exception ex)
        {
            AppLog.Error("Snapshot refresh failed", ex);
        }
    }

    private void ApplySnapshot(AudioSnapshot snap, bool allowAutoEvents, bool force)
    {
        if (!force && snap.EqualsState(_snap))
            return;

        var previous = _snap;
        _snap = snap;

        if (ProfileLogic.RelinkByName(_config, snap, AppLog.Info))
        {
            Save();
        }

        ApplyMuteUi(snap.MicMuted);
        RefreshCardStates();
        RefreshVoiceMicCombo();
        SyncVoiceControl();

        if (allowAutoEvents && previous != null)
        {
            HandleDeviceChanges(previous, snap);
        }
    }

    private void HandleDeviceChanges(AudioSnapshot previous, AudioSnapshot now)
    {
        var newIds = new HashSet<Guid>(now.OutputIds.Except(previous.OutputIds));
        newIds.UnionWith(now.InputIds.Except(previous.InputIds));

        var removedIds = new HashSet<Guid>(previous.OutputIds.Except(now.OutputIds));
        removedIds.UnionWith(previous.InputIds.Except(now.InputIds));

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

        var fallback = ProfileLogic.ResolveFallbackCard(_config, previous, now, removedIds);
        if (fallback != null)
        {
            AppLog.Info($"Active card lost a device; switching to fallback '{fallback.Name}' ({_config.FallbackMode}).");
            _ = ActivateCardAsync(fallback, "FALLBACK ACTIVE");
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
            if (!c.AutoActivate || !ProfileLogic.CardIsAvailable(c, _snap) || ProfileLogic.CardIsActive(c, _snap)) continue;
            bool touchesNew =
                (c.OutputId.HasValue && stillConnected.Contains(c.OutputId.Value)) ||
                (c.CommOutputId.HasValue && stillConnected.Contains(c.CommOutputId.Value)) ||
                (c.InputId.HasValue && stillConnected.Contains(c.InputId.Value));
            if (!touchesNew) continue;

            _ = ActivateCardAsync(c, "AUTO-SWITCHED");
            break;
        }
    }

    private void ApplyMuteUi(bool muted)
    {
        _micMuted = muted;
        _muteBanner.Interactive = _config.MuteBannerInteractive;
        if (muted && _config.MuteBannerEnabled)
        {
            _muteBanner.ShowBanner(_config.MuteBannerPosition, _config.MuteBannerX, _config.MuteBannerY);
        }
        else
        {
            _muteBanner.Hide();
        }
        _tray.Icon = muted ? TrayIcons.Muted : TrayIcons.Normal;
        UpdateNow();
    }

    // =========================================================================================
    // Tray menu (cards, quick output/input submenus, and shortcut hints)
    // =========================================================================================

    private void RebuildTrayMenu()
    {
        var items = _trayMenu.Items;
        foreach (var item in items.Cast<ToolStripItem>().ToList())
        {
            items.Remove(item);
            item.Dispose();
        }

        var openItem = new ToolStripMenuItem("Open AudioSwitcher")
        {
            ShortcutKeyDisplayString = _config.ToggleWindowHotKey.IsEnabled ? _config.ToggleWindowHotKey.Format() : ""
        };
        openItem.Click += (_, _) => ShowFromTray();
        items.Add(openItem);

        if (_availableUpdate is { UpdateAvailable: true } upd)
        {
            var updateItem = new ToolStripMenuItem($"Update Available ({upd.LatestVersion})…")
            {
                ForeColor = Theme.Success
            };
            updateItem.Click += (_, _) => ShowUpdateDialog();
            items.Add(updateItem);
        }

        if (_config.Cards.Count > 0)
        {
            items.Add(new ToolStripSeparator());
            for (int i = 0; i < _config.Cards.Count; i++)
            {
                var c = _config.Cards[i];
                var mi = new ToolStripMenuItem(c.Name)
                {
                    Checked = _snap != null && ProfileLogic.CardIsActive(c, _snap),
                    Enabled = ProfileLogic.CardIsAvailable(c, _snap),
                    ShortcutKeyDisplayString = SlotFor(i) is string slot
                        ? $"{_config.CardHotKey.FormatModifiers()} + {slot}"
                        : ""
                };
                mi.Click += (_, _) => _ = ActivateCardAsync(c);
                items.Add(mi);
            }

            var cycleCardsItem = new ToolStripMenuItem("Cycle Cards")
            {
                ShortcutKeyDisplayString = _config.CardHotKey.IsEnabled ? _config.CardHotKey.Format() : ""
            };
            cycleCardsItem.Click += (_, _) => _ = CycleCardAsync();
            items.Add(cycleCardsItem);
        }

        items.Add(new ToolStripSeparator());
        items.Add(BuildDeviceSubMenu("Output Device", isOutput: true));
        items.Add(BuildDeviceSubMenu("Input Device", isOutput: false));

        items.Add(new ToolStripSeparator());
        var muteItem = new ToolStripMenuItem("Toggle Mic Mute")
        {
            Checked = _micMuted,
            ShortcutKeyDisplayString = _config.MuteHotKey.IsEnabled ? _config.MuteHotKey.Format() : ""
        };
        muteItem.Click += (_, _) => _ = ToggleMicMuteAsync();
        items.Add(muteItem);

        var voiceItem = new ToolStripMenuItem($"Voice Control ({_voiceStatusText})")
        {
            Checked = _config.VoiceControlEnabled
        };
        voiceItem.Click += (_, _) =>
        {
            _config.VoiceControlEnabled = !_config.VoiceControlEnabled;
            _updatingVoiceControls = true;
            if (_voiceToggle != null) _voiceToggle.Checked = _config.VoiceControlEnabled;
            _updatingVoiceControls = false;
            Save();
            SyncVoiceControl();
        };
        items.Add(voiceItem);

        items.Add("Exit", null, (_, _) => { _allowClose = true; Close(); });
    }

    private ToolStripMenuItem BuildDeviceSubMenu(string label, bool isOutput)
    {
        var root = new ToolStripMenuItem(label);
        root.DropDown.Renderer = new DarkMenuRenderer();
        root.DropDown.BackColor = Theme.BgCard;
        root.DropDown.ForeColor = Theme.TextPrimary;
        root.DropDown.Font = Theme.Body;

        var names = isOutput ? _snap?.OutputNames : _snap?.InputNames;
        var currentDefault = isOutput ? _snap?.DefaultOut : _snap?.DefaultIn;

        if (names is { Count: > 0 })
        {
            foreach (var kv in names.OrderBy(kv => kv.Value, StringComparer.CurrentCultureIgnoreCase))
            {
                var id = kv.Key;
                var devName = kv.Value;
                var item = new ToolStripMenuItem(devName)
                {
                    Checked = currentDefault == id
                };
                item.Click += (_, _) => _ = SwitchSingleDeviceAsync(isOutput, id, devName);
                root.DropDownItems.Add(item);
            }

            root.DropDownItems.Add(new ToolStripSeparator());
            var hk = isOutput ? _config.OutputHotKey : _config.InputHotKey;
            var cycleItem = new ToolStripMenuItem(isOutput ? "Cycle Output" : "Cycle Input")
            {
                ShortcutKeyDisplayString = hk.IsEnabled ? hk.Format() : ""
            };
            cycleItem.Click += (_, _) =>
            {
                if (isOutput) _ = CycleOutputAsync();
                else _ = CycleInputAsync();
            };
            root.DropDownItems.Add(cycleItem);
        }
        else
        {
            root.DropDownItems.Add(new ToolStripMenuItem("No devices connected") { Enabled = false });
        }

        return root;
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