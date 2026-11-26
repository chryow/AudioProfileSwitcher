using System.Drawing.Drawing2D;

namespace AudioProfileSwitcher;

internal static class Theme
{
    public static readonly Color BgMain = Color.FromArgb(18, 19, 22);
    public static readonly Color BgCard = Color.FromArgb(27, 29, 35);
    public static readonly Color BgCardHover = Color.FromArgb(35, 38, 46);
    public static readonly Color BgBorder = Color.FromArgb(46, 50, 60);
    public static readonly Color Accent = Color.FromArgb(88, 101, 242);
    public static readonly Color Danger = Color.FromArgb(237, 66, 69);
    public static readonly Color Success = Color.FromArgb(35, 165, 89);
    public static readonly Color Warning = Color.FromArgb(250, 166, 26);
    public static readonly Color TextPrimary = Color.FromArgb(242, 244, 248);
    public static readonly Color TextSoft = Color.FromArgb(190, 196, 208);
    public static readonly Color TextMuted = Color.FromArgb(148, 155, 168);

    // Cached fonts: created once, shared by every control (no per-render GDI leaks).
    public static readonly Font Body = new("Segoe UI", 8.5f);
    public static readonly Font BodyBold = new("Segoe UI", 8.5f, FontStyle.Bold);
    public static readonly Font Title = new("Segoe UI", 10f, FontStyle.Bold);
    public static readonly Font Header = new("Segoe UI", 9.5f, FontStyle.Bold);
    public static readonly Font Small = new("Segoe UI", 8f);
    public static readonly Font SmallBold = new("Segoe UI", 7.5f, FontStyle.Bold);

    public static readonly bool HasMdl2 = FontFamily.Families.Any(f => f.Name == "Segoe MDL2 Assets");
    public static readonly Font IconFont = HasMdl2 ? new Font("Segoe MDL2 Assets", 9f) : new Font("Segoe UI", 10f);

    public static string Glyph(string mdl2, string fallback) => HasMdl2 ? mdl2 : fallback;
}

internal static class Gfx
{
    public static GraphicsPath RoundRect(Rectangle r, int radius)
    {
        int d = Math.Max(1, radius * 2);
        var path = new GraphicsPath();
        path.AddArc(r.X, r.Y, d, d, 180, 90);
        path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }
}

/// <summary>Dark renderer for the system tray ContextMenuStrip.</summary>
internal sealed class DarkMenuRenderer : ToolStripProfessionalRenderer
{
    public DarkMenuRenderer() : base(new DarkColorTable()) { }

    protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e)
    {
        using var b = new SolidBrush(Theme.BgCard);
        e.Graphics.FillRectangle(b, e.AffectedBounds);
    }

    protected override void OnRenderImageMargin(ToolStripRenderEventArgs e)
    {
        using var b = new SolidBrush(Theme.BgCard);
        e.Graphics.FillRectangle(b, e.AffectedBounds);
    }

    protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e)
    {
        using var p = new Pen(Theme.BgBorder);
        e.Graphics.DrawRectangle(p, 0, 0, e.AffectedBounds.Width - 1, e.AffectedBounds.Height - 1);
    }

    protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
    {
        var r = new Rectangle(2, 1, e.Item.Width - 4, e.Item.Height - 2);
        using var b = new SolidBrush(e.Item.Selected && e.Item.Enabled ? Theme.BgCardHover : Theme.BgCard);
        e.Graphics.FillRectangle(b, r);
    }

    protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
    {
        e.TextColor = e.Item.Enabled ? Theme.TextPrimary : Theme.TextMuted;
        base.OnRenderItemText(e);
    }

    protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e)
    {
        int y = e.Item.Height / 2;
        using var p = new Pen(Theme.BgBorder);
        e.Graphics.DrawLine(p, 8, y, e.Item.Width - 8, y);
    }

    protected override void OnRenderItemCheck(ToolStripItemImageRenderEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        var r = e.ImageRectangle;
        var dot = new Rectangle(r.X + (r.Width - 8) / 2, r.Y + (r.Height - 8) / 2, 8, 8);
        using var b = new SolidBrush(Theme.Accent);
        g.FillEllipse(b, dot);
    }

    private sealed class DarkColorTable : ProfessionalColorTable
    {
        public override Color ToolStripDropDownBackground => Theme.BgCard;
        public override Color MenuBorder => Theme.BgBorder;
        public override Color MenuItemBorder => Theme.BgCardHover;
        public override Color MenuItemSelected => Theme.BgCardHover;
        public override Color ImageMarginGradientBegin => Theme.BgCard;
        public override Color ImageMarginGradientMiddle => Theme.BgCard;
        public override Color ImageMarginGradientEnd => Theme.BgCard;
    }
}

/// <summary>Tray icons drawn at startup (speaker glyph; red dot when the mic is muted).</summary>
internal static class TrayIcons
{
    public static readonly Icon Normal = Create(false);
    public static readonly Icon Muted = Create(true);

    private static Icon Create(bool muted)
    {
        using var bmp = new Bitmap(32, 32);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Color.Transparent);

            using (var bg = new SolidBrush(Theme.Accent)) g.FillEllipse(bg, 0, 0, 31, 31);

            using (var white = new SolidBrush(Color.White))
            {
                g.FillPolygon(white, new[]
                {
                    new PointF(8, 13), new PointF(13, 13), new PointF(19, 8),
                    new PointF(19, 24), new PointF(13, 19), new PointF(8, 19)
                });
            }
            using (var pen = new Pen(Color.White, 2f)) g.DrawArc(pen, 14, 11, 10, 10, -50, 100);

            if (muted)
            {
                using var red = new SolidBrush(Theme.Danger);
                using var ring = new Pen(Color.White, 2f);
                g.FillEllipse(red, 18, 18, 13, 13);
                g.DrawEllipse(ring, 18, 18, 13, 13);
            }
        }
        // Two static icons for the whole process lifetime: the handles are intentionally kept.
        return Icon.FromHandle(bmp.GetHicon());
    }
}

internal sealed class ToggleSwitch : Control
{
    private bool _checked;
    public event EventHandler? CheckedChanged;

    public ToggleSwitch()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                 ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw |
                 ControlStyles.Selectable, true);
        Cursor = Cursors.Hand;
        Size = new Size(40, 20);
        TabStop = true;
    }

    public bool Checked
    {
        get => _checked;
        set
        {
            if (_checked == value) return;
            _checked = value;
            Invalidate();
            CheckedChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    protected override void OnClick(EventArgs e)
    {
        Checked = !Checked;
        base.OnClick(e);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Space) { Checked = !Checked; e.Handled = true; }
        base.OnKeyDown(e);
    }

    protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
    protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(Parent?.BackColor ?? Theme.BgCard);

        var r = new Rectangle(0, 0, Width - 1, Height - 1);
        using var track = Gfx.RoundRect(r, Height / 2);
        using (var b = new SolidBrush(_checked ? Theme.Accent : Theme.BgBorder)) g.FillPath(b, track);

        int pad = Math.Max(2, Height / 8);
        int d = Height - 1 - pad * 2;
        int x = _checked ? Width - 1 - pad - d : pad;
        using (var knob = new SolidBrush(Color.White)) g.FillEllipse(knob, x, pad, d, d);

        if (Focused && ShowFocusCues)
        {
            using var p = new Pen(Theme.TextMuted);
            g.DrawPath(p, track);
        }
    }
}

/// <summary>Owner-drawn combo so the drop-down actually follows the dark theme.</summary>
internal sealed class DarkCombo : ComboBox
{
    public DarkCombo()
    {
        DrawMode = DrawMode.OwnerDrawFixed;
        DropDownStyle = ComboBoxStyle.DropDownList;
        FlatStyle = FlatStyle.Flat;
        BackColor = Theme.BgCard;
        ForeColor = Theme.TextPrimary;
        Font = Theme.Body;
        ItemHeight = 22;
    }

    protected override void OnDrawItem(DrawItemEventArgs e)
    {
        if (e.Index < 0) return;
        bool highlighted = (e.State & DrawItemState.Selected) != 0 && (e.State & DrawItemState.ComboBoxEdit) == 0;
        using (var bg = new SolidBrush(highlighted ? Theme.Accent : Theme.BgCard))
            e.Graphics.FillRectangle(bg, e.Bounds);

        var text = Items[e.Index]?.ToString() ?? "";
        TextRenderer.DrawText(e.Graphics, text, Font, Rectangle.Inflate(e.Bounds, -6, 0), Theme.TextPrimary,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
    }
}

/// <summary>
/// One audio card: custom painted, with hover/focus states, keyboard support,
/// hover-revealed Edit/Delete (delete asks to be clicked twice) and drag-to-reorder.
/// </summary>
internal sealed class CardControl : Control
{
    private const TextFormatFlags TextFlags =
        TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis |
        TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine;

    private readonly System.Windows.Forms.Timer _confirmTimer = new() { Interval = 2500 };
    private bool _hover, _hoverEdit, _hoverDel, _confirmDelete, _pressed, _downOnButton, _keyboardFocus;
    private bool _isActive, _outMissing, _inMissing;
    private string? _slotText;
    private Point _downPoint;

    public AudioCard Card { get; }

    /// <summary>DPI scale factor (1.0 = 96 dpi), set by the parent.</summary>
    public new float Scale { get; set; } = 1f;

    public event Action<AudioCard>? ActivateRequested;
    public event Action<AudioCard>? EditRequested;
    public event Action<AudioCard>? DeleteRequested;

    public CardControl(AudioCard card)
    {
        Card = card;
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                 ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw |
                 ControlStyles.Selectable, true);
        TabStop = true;
        Cursor = Cursors.Hand;
        BackColor = Theme.BgMain;
        _confirmTimer.Tick += (_, _) => { _confirmTimer.Stop(); _confirmDelete = false; Invalidate(); };
    }

    public bool IsActive { get => _isActive; set => Upd(ref _isActive, value); }
    public bool OutMissing { get => _outMissing; set => Upd(ref _outMissing, value); }
    public bool InMissing { get => _inMissing; set => Upd(ref _inMissing, value); }

    public string? SlotText
    {
        get => _slotText;
        set { if (_slotText != value) { _slotText = value; Invalidate(); } }
    }

    private void Upd(ref bool field, bool value)
    {
        if (field == value) return;
        field = value;
        Invalidate();
    }

    private int P(int v) => (int)Math.Round(v * Scale);
    private bool ButtonsVisible => _hover || _confirmDelete || (Focused && _keyboardFocus);

    private Rectangle DelRect()
    {
        int w = _confirmDelete ? P(64) : P(28);
        return new Rectangle(Width - P(10) - w, P(8), w, P(24));
    }

    private Rectangle EditRect()
    {
        var d = DelRect();
        return new Rectangle(d.X - P(4) - P(40), P(8), P(40), P(24));
    }

    private void HandleDelete()
    {
        if (_confirmDelete)
        {
            _confirmTimer.Stop();
            _confirmDelete = false;
            DeleteRequested?.Invoke(Card);
            return;
        }
        _confirmDelete = true;
        _confirmTimer.Start();
        Invalidate();
    }

    // ---- input ----------------------------------------------------------------------------

    protected override bool IsInputKey(Keys keyData) => keyData is Keys.Up or Keys.Down || base.IsInputKey(keyData);

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        _keyboardFocus = true;
        switch (e.KeyCode)
        {
            case Keys.Enter:
            case Keys.Space:
                ActivateRequested?.Invoke(Card); e.Handled = true; break;
            case Keys.F2:
                EditRequested?.Invoke(Card); e.Handled = true; break;
            case Keys.Delete:
                HandleDelete(); e.Handled = true; break;
            case Keys.Up:
            case Keys.Down:
                Parent?.SelectNextControl(this, e.KeyCode == Keys.Down, true, false, false);
                e.Handled = true; break;
        }
        Invalidate();
    }

    protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
    protected override void OnLostFocus(EventArgs e) { _keyboardFocus = false; Invalidate(); base.OnLostFocus(e); }

    protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }

    protected override void OnMouseLeave(EventArgs e)
    {
        _hover = _hoverEdit = _hoverDel = false;
        Invalidate();
        base.OnMouseLeave(e);
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button != MouseButtons.Left) return;
        _keyboardFocus = false;
        Focus();
        _pressed = true;
        _downPoint = e.Location;
        _downOnButton = ButtonsVisible && (EditRect().Contains(e.Location) || DelRect().Contains(e.Location));
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);

        bool he = ButtonsVisible && EditRect().Contains(e.Location);
        bool hd = ButtonsVisible && DelRect().Contains(e.Location);
        if (he != _hoverEdit || hd != _hoverDel)
        {
            _hoverEdit = he;
            _hoverDel = hd;
            Invalidate();
        }

        if (_pressed && !_downOnButton && e.Button == MouseButtons.Left)
        {
            var drag = SystemInformation.DragSize;
            if (Math.Abs(e.X - _downPoint.X) > drag.Width || Math.Abs(e.Y - _downPoint.Y) > drag.Height)
            {
                _pressed = false;
                DoDragDrop(Card, DragDropEffects.Move);
            }
        }
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        if (e.Button != MouseButtons.Left || !_pressed) return;
        _pressed = false;
        if (!ClientRectangle.Contains(e.Location)) return;

        if (ButtonsVisible && EditRect().Contains(e.Location)) EditRequested?.Invoke(Card);
        else if (ButtonsVisible && DelRect().Contains(e.Location)) HandleDelete();
        else ActivateRequested?.Invoke(Card);
    }

    // ---- painting -------------------------------------------------------------------------

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(Parent?.BackColor ?? Theme.BgMain);

        var r = new Rectangle(0, 0, Width - 1, Height - 1);
        using var path = Gfx.RoundRect(r, P(8));

        using (var bg = new SolidBrush(_hover ? Theme.BgCardHover : Theme.BgCard)) g.FillPath(bg, path);

        if (_isActive)
        {
            var state = g.Save();
            g.SetClip(path);
            using (var bar = new SolidBrush(Theme.Accent)) g.FillRectangle(bar, 0, 0, P(4), Height);
            g.Restore(state);
        }

        var borderColor = _isActive ? Theme.Accent : (_keyboardFocus && Focused ? Theme.TextMuted : Theme.BgBorder);
        using (var pen = new Pen(borderColor, 1)) g.DrawPath(pen, path);

        bool hasSlot = !string.IsNullOrEmpty(_slotText);
        int left = hasSlot ? P(46) : P(18);

        if (hasSlot)
        {
            TextRenderer.DrawText(g, _slotText!, Theme.Title, new Rectangle(P(10), 0, P(30), Height),
                _isActive ? Theme.Accent : Theme.TextMuted,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
        }

        bool showButtons = ButtonsVisible;
        int nameRight = showButtons ? EditRect().X - P(6) : Width - P(12) - (_isActive ? P(58) : 0);
        TextRenderer.DrawText(g, Card.Name, Theme.Title,
            new Rectangle(left, P(8), Math.Max(P(40), nameRight - left), P(24)), Theme.TextPrimary, TextFlags);

        DrawDeviceRow(g, "OUT", Card.OutputId.HasValue ? Card.OutputName : null, _outMissing, left, P(36));
        DrawDeviceRow(g, "IN", Card.InputId.HasValue ? Card.InputName : null, _inMissing, left, P(55));

        if (showButtons)
        {
            DrawEditButton(g, EditRect());
            DrawDeleteButton(g, DelRect());
        }
        else if (_isActive)
        {
            var pill = new Rectangle(Width - P(10) - P(48), P(11), P(48), P(18));
            using var pp = Gfx.RoundRect(pill, P(9));
            using (var fill = new SolidBrush(Color.FromArgb(60, Theme.Accent))) g.FillPath(fill, pp);
            TextRenderer.DrawText(g, "ACTIVE", Theme.SmallBold, pill, Theme.Accent,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
        }
    }

    private void DrawDeviceRow(Graphics g, string tag, string? name, bool missing, int left, int y)
    {
        TextRenderer.DrawText(g, tag, Theme.SmallBold, new Rectangle(left, y, P(30), P(18)), Theme.TextMuted, TextFlags);

        string text = name ?? "Not changed";
        if (missing) text += "  (not connected)";
        var color = missing ? Theme.Warning : (name == null ? Theme.TextMuted : Theme.TextSoft);

        int x = left + P(30);
        TextRenderer.DrawText(g, text, Theme.Small,
            new Rectangle(x, y, Math.Max(P(20), Width - P(12) - x), P(18)), color, TextFlags);
    }

    private void DrawEditButton(Graphics g, Rectangle rect)
    {
        if (_hoverEdit)
        {
            using var p = Gfx.RoundRect(rect, P(5));
            using var b = new SolidBrush(Theme.BgBorder);
            g.FillPath(b, p);
        }
        TextRenderer.DrawText(g, "Edit", Theme.Small, rect, _hoverEdit ? Theme.TextPrimary : Theme.TextMuted,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
    }

    private void DrawDeleteButton(Graphics g, Rectangle rect)
    {
        if (_confirmDelete || _hoverDel)
        {
            using var p = Gfx.RoundRect(rect, P(5));
            using var b = new SolidBrush(_confirmDelete ? Theme.Danger : Color.FromArgb(70, Theme.Danger));
            g.FillPath(b, p);
        }
        string text = _confirmDelete ? "Delete?" : "×";
        var color = _confirmDelete ? Color.White : (_hoverDel ? Theme.Danger : Theme.TextMuted);
        TextRenderer.DrawText(g, text, _confirmDelete ? Theme.SmallBold : Theme.Title, rect, color,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _confirmTimer.Dispose();
        base.Dispose(disposing);
    }
}

/// <summary>Always-on-top, click-through, never-activating window.</summary>
internal abstract class OverlayForm : Form
{
    protected OverlayForm()
    {
        AutoScaleMode = AutoScaleMode.None; // sizes are scaled by hand from DeviceDpi
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        TopMost = true;
        StartPosition = FormStartPosition.Manual;
        DoubleBuffered = true;
    }

    protected float Dpi => DeviceDpi / 96f;

    protected override bool ShowWithoutActivation => true;

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.ExStyle |= 0x08000000 | 0x00000080 | 0x00000020 | 0x00000008; // NOACTIVATE | TOOLWINDOW | TRANSPARENT | TOPMOST
            return cp;
        }
    }
}

internal sealed class OsdBannerForm : OverlayForm
{
    private readonly System.Windows.Forms.Timer _timer = new() { Interval = 1600 };
    private readonly Label _catLbl;
    private readonly Label _msgLbl;
    private Color _accent = Theme.Accent;

    public OsdBannerForm()
    {
        float k = Dpi;
        Size = new Size((int)(340 * k), (int)(60 * k));
        BackColor = Color.FromArgb(20, 22, 27);
        Opacity = 0.94;

        _catLbl = new Label
        {
            Font = Theme.SmallBold,
            ForeColor = Theme.TextMuted,
            Location = new Point((int)(16 * k), (int)(10 * k)),
            Size = new Size((int)(308 * k), (int)(16 * k))
        };
        _msgLbl = new Label
        {
            Font = Theme.Title,
            ForeColor = Theme.TextPrimary,
            Location = new Point((int)(16 * k), (int)(28 * k)),
            Size = new Size((int)(308 * k), (int)(24 * k)),
            AutoEllipsis = true
        };
        Controls.AddRange(new Control[] { _catLbl, _msgLbl });

        _timer.Tick += (_, _) => { _timer.Stop(); Hide(); };
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        using var border = new Pen(Theme.BgBorder, 1);
        e.Graphics.DrawRectangle(border, 0, 0, Width - 1, Height - 1);
        using var bar = new SolidBrush(_accent);
        e.Graphics.FillRectangle(bar, 0, 0, (int)(4 * Dpi), Height);
    }

    public void ShowMessage(string category, string message, Color accentColor)
    {
        _accent = accentColor;
        _catLbl.Text = category;
        _msgLbl.Text = message;

        // Show on whichever monitor the user is currently working on.
        var area = Screen.FromPoint(Cursor.Position).WorkingArea;
        Location = new Point(area.Left + (area.Width - Width) / 2, area.Bottom - Height - (int)(36 * Dpi));
        Invalidate();
        if (!Visible) Show();
        _timer.Stop();
        _timer.Start();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _timer.Dispose();
        base.Dispose(disposing);
    }
}

internal sealed class MuteBannerForm : OverlayForm
{
    public MuteBannerForm()
    {
        float k = Dpi;
        Size = new Size((int)(124 * k), (int)(30 * k));
        BackColor = Color.FromArgb(32, 18, 20);
        Opacity = 0.92;

        Controls.Add(new Label
        {
            Text = "● MIC MUTED",
            Font = Theme.BodyBold,
            ForeColor = Color.FromArgb(245, 90, 93),
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleCenter
        });
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        using var border = new Pen(Theme.Danger, 1);
        e.Graphics.DrawRectangle(border, 0, 0, Width - 1, Height - 1);
    }

    public void ShowBanner()
    {
        var area = Screen.PrimaryScreen!.WorkingArea;
        Location = new Point(area.Left + (area.Width - Width) / 2, area.Top + (int)(14 * Dpi));
        if (!Visible) Show();
    }
}