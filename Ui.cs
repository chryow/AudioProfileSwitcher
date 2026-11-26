using System.Drawing.Drawing2D;

namespace AudioProfileSwitcher;

internal static class Theme
{
    // Deep obsidian-slate surfaces with clear visual elevation hierarchy
    public static readonly Color BgMain = Color.FromArgb(13, 15, 20);
    public static readonly Color BgSurface = Color.FromArgb(19, 22, 30);
    public static readonly Color BgCard = Color.FromArgb(24, 28, 38);
    public static readonly Color BgCardTop = Color.FromArgb(29, 34, 46);
    public static readonly Color BgCardHover = Color.FromArgb(33, 39, 53);
    public static readonly Color BgElevated = Color.FromArgb(36, 42, 58);
    public static readonly Color BgBorder = Color.FromArgb(44, 51, 68);
    public static readonly Color BgBorderStrong = Color.FromArgb(62, 72, 96);
    public static readonly Color Sheen = Color.FromArgb(24, 255, 255, 255);

    // Indigo -> Violet modern accent gradient + semantic colors
    public static readonly Color Accent = Color.FromArgb(99, 102, 241);
    public static readonly Color AccentEnd = Color.FromArgb(139, 92, 246);
    public static readonly Color AccentHoverStart = Color.FromArgb(115, 118, 250);
    public static readonly Color AccentHoverEnd = Color.FromArgb(155, 110, 252);
    public static readonly Color AccentSoft = Color.FromArgb(34, 99, 102, 241);
    public static readonly Color AccentGlow = Color.FromArgb(72, 129, 140, 248);

    public static readonly Color Danger = Color.FromArgb(244, 63, 94);
    public static readonly Color DangerSoft = Color.FromArgb(38, 244, 63, 94);
    public static readonly Color Success = Color.FromArgb(34, 197, 94);
    public static readonly Color SuccessSoft = Color.FromArgb(34, 34, 197, 94);
    public static readonly Color Warning = Color.FromArgb(245, 158, 11);
    public static readonly Color WarningSoft = Color.FromArgb(36, 245, 158, 11);

    public static readonly Color TextPrimary = Color.FromArgb(244, 246, 251);
    public static readonly Color TextSoft = Color.FromArgb(194, 202, 218);
    public static readonly Color TextMuted = Color.FromArgb(134, 145, 166);

    // Cached fonts (Prefer Segoe UI Variable on Windows 11, fallback to Segoe UI)
    private static readonly string UiFamily = ResolveUiFontFamily();

    private static string ResolveUiFontFamily()
    {
        try
        {
            if (FontFamily.Families.Any(f => f.Name.Equals("Segoe UI Variable Text", StringComparison.OrdinalIgnoreCase)))
                return "Segoe UI Variable Text";
        }
        catch { }
        return "Segoe UI";
    }

    public static readonly Font Body = new(UiFamily, 8.75f);
    public static readonly Font BodyBold = new(UiFamily, 8.75f, FontStyle.Bold);
    public static readonly Font Title = new(UiFamily, 10f, FontStyle.Bold);
    public static readonly Font Header = new(UiFamily, 9.5f, FontStyle.Bold);
    public static readonly Font Small = new(UiFamily, 8f);
    public static readonly Font SmallBold = new(UiFamily, 7.5f, FontStyle.Bold);
    public static readonly Font MicroBold = new(UiFamily, 6.75f, FontStyle.Bold);

    public static readonly bool HasMdl2 = FontFamily.Families.Any(f => f.Name == "Segoe MDL2 Assets");
    public static readonly Font IconFont = HasMdl2 ? new Font("Segoe MDL2 Assets", 9.5f) : new Font(UiFamily, 10f);

    public static string Glyph(string mdl2, string fallback) => HasMdl2 ? mdl2 : fallback;
}

internal static class Gfx
{
    public static GraphicsPath RoundRect(Rectangle r, int radius)
    {
        int rad = Math.Max(1, Math.Min(radius, Math.Min(r.Width, r.Height) / 2));
        int d = rad * 2;
        var path = new GraphicsPath();
        path.AddArc(r.X, r.Y, d, d, 180, 90);
        path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }

    public static void FillGradientRoundRect(
        Graphics g,
        Rectangle r,
        int radius,
        Color start,
        Color end,
        float angle = 0f)
    {
        if (r.Width <= 0 || r.Height <= 0) return;
        using var path = RoundRect(r, radius);
        using var brush = new LinearGradientBrush(r, start, end, angle);
        g.FillPath(brush, path);
    }

    public static void DrawTopSheen(Graphics g, Rectangle r, int radius)
    {
        if (r.Width <= radius * 2 + 2) return;
        using var pen = new Pen(Theme.Sheen, 1f);
        g.DrawLine(pen, r.X + radius, r.Y + 1, r.Right - radius, r.Y + 1);
    }

    /// <summary>Draws the miniature indigo-violet gradient speaker emblem.</summary>
    public static void DrawAppEmblem(Graphics g, RectangleF r)
    {
        using var grad = new LinearGradientBrush(r, Theme.Accent, Theme.AccentEnd, 135f);
        g.FillEllipse(grad, r);

        float x = r.X;
        float y = r.Y;
        float w = r.Width;
        float h = r.Height;

        using var white = new SolidBrush(Color.White);
        g.FillPolygon(white, new[]
        {
            new PointF(x + w * 0.24f, y + h * 0.40f),
            new PointF(x + w * 0.40f, y + h * 0.40f),
            new PointF(x + w * 0.58f, y + h * 0.25f),
            new PointF(x + w * 0.58f, y + h * 0.75f),
            new PointF(x + w * 0.40f, y + h * 0.60f),
            new PointF(x + w * 0.24f, y + h * 0.60f)
        });

        using var pen = new Pen(Color.White, Math.Max(1.3f, w * 0.075f)) { StartCap = LineCap.Round, EndCap = LineCap.Round };
        g.DrawArc(pen, x + w * 0.48f, y + h * 0.34f, w * 0.26f, h * 0.32f, -48, 96);
    }

    public static void DrawSpeakerIcon(Graphics g, RectangleF r, Color color)
    {
        float x = r.X, y = r.Y, w = r.Width, h = r.Height;
        using var b = new SolidBrush(color);
        g.FillPolygon(b, new[]
        {
            new PointF(x + w * 0.12f, y + h * 0.36f),
            new PointF(x + w * 0.34f, y + h * 0.36f),
            new PointF(x + w * 0.56f, y + h * 0.18f),
            new PointF(x + w * 0.56f, y + h * 0.82f),
            new PointF(x + w * 0.34f, y + h * 0.64f),
            new PointF(x + w * 0.12f, y + h * 0.64f)
        });
        using var p = new Pen(color, Math.Max(1.2f, w * 0.1f)) { StartCap = LineCap.Round, EndCap = LineCap.Round };
        g.DrawArc(p, x + w * 0.48f, y + h * 0.28f, w * 0.34f, h * 0.44f, -46, 92);
    }

    public static void DrawMicIcon(Graphics g, RectangleF r, Color color, bool muted = false)
    {
        float x = r.X, y = r.Y, w = r.Width, h = r.Height;
        float capW = w * 0.34f;
        float capH = h * 0.48f;
        float capX = x + (w - capW) * 0.5f;
        float capY = y + h * 0.12f;

        using var b = new SolidBrush(color);
        using (var capPath = RoundRect(Rectangle.Round(new RectangleF(capX, capY, capW, capH)), Math.Max(2, (int)(capW / 2))))
            g.FillPath(b, capPath);

        using var p = new Pen(color, Math.Max(1.25f, w * 0.1f)) { StartCap = LineCap.Round, EndCap = LineCap.Round };
        g.DrawArc(p, x + w * 0.20f, y + h * 0.28f, w * 0.60f, h * 0.44f, 0, 180);
        g.DrawLine(p, x + w * 0.50f, y + h * 0.72f, x + w * 0.50f, y + h * 0.88f);
        g.DrawLine(p, x + w * 0.32f, y + h * 0.88f, x + w * 0.68f, y + h * 0.88f);

        if (muted)
        {
            using var slash = new Pen(Theme.Danger, Math.Max(1.6f, w * 0.12f)) { StartCap = LineCap.Round, EndCap = LineCap.Round };
            g.DrawLine(slash, x + w * 0.16f, y + h * 0.84f, x + w * 0.84f, y + h * 0.16f);
        }
    }

    public static void DrawBoltIcon(Graphics g, RectangleF r, Color color)
    {
        float x = r.X, y = r.Y, w = r.Width, h = r.Height;
        using var b = new SolidBrush(color);
        g.FillPolygon(b, new[]
        {
            new PointF(x + w * 0.56f, y + h * 0.08f),
            new PointF(x + w * 0.20f, y + h * 0.54f),
            new PointF(x + w * 0.48f, y + h * 0.54f),
            new PointF(x + w * 0.42f, y + h * 0.92f),
            new PointF(x + w * 0.80f, y + h * 0.44f),
            new PointF(x + w * 0.52f, y + h * 0.44f)
        });
    }
}

internal enum ModernButtonKind
{
    Primary,
    Secondary,
    Ghost,
    DangerGhost,
    Keycap,
    Chip
}

/// <summary>Custom-painted modern rounded button with gradient primary style, hover elevation, and focus rings.</summary>
internal sealed class ModernButton : Control
{
    private ModernButtonKind _kind;
    private bool _hover;
    private bool _pressed;
    private bool _active;
    private DialogResult _dialogResult = DialogResult.None;

    public ModernButton(ModernButtonKind kind = ModernButtonKind.Secondary)
    {
        _kind = kind;
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                 ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw |
                 ControlStyles.Selectable, true);
        Cursor = Cursors.Hand;
        Font = kind switch
        {
            ModernButtonKind.Primary => Theme.BodyBold,
            ModernButtonKind.Keycap => Theme.SmallBold,
            ModernButtonKind.Chip => Theme.Small,
            ModernButtonKind.Ghost or ModernButtonKind.DangerGhost => Theme.IconFont,
            _ => Theme.Body
        };
        ForeColor = Theme.TextPrimary;
        BackColor = Theme.BgMain;
        Size = new Size(100, 32);
        TabStop = kind is not (ModernButtonKind.Ghost or ModernButtonKind.DangerGhost or ModernButtonKind.Chip);
    }

    public ModernButtonKind Kind
    {
        get => _kind;
        set { _kind = value; Invalidate(); }
    }

    public bool Active
    {
        get => _active;
        set { if (_active != value) { _active = value; Invalidate(); } }
    }

    public DialogResult DialogResult
    {
        get => _dialogResult;
        set => _dialogResult = value;
    }

    public int CornerRadius { get; set; } = 8;

    protected override void OnTextChanged(EventArgs e) { Invalidate(); base.OnTextChanged(e); }
    protected override void OnEnabledChanged(EventArgs e) { Invalidate(); base.OnEnabledChanged(e); }
    protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
    protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }
    protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { _hover = false; _pressed = false; Invalidate(); base.OnMouseLeave(e); }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (!Enabled || e.Button != MouseButtons.Left) return;
        _pressed = true;
        Invalidate();
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        if (!_pressed) return;
        _pressed = false;
        Invalidate();
    }

    protected override void OnClick(EventArgs e)
    {
        if (!Enabled) return;
        if (_dialogResult != DialogResult.None && FindForm() is Form form)
        {
            form.DialogResult = _dialogResult;
        }
        base.OnClick(e);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (Enabled && e.KeyCode is Keys.Space or Keys.Enter)
        {
            OnClick(EventArgs.Empty);
            e.Handled = true;
        }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(Parent?.BackColor ?? Theme.BgMain);

        float k = Math.Max(1f, DeviceDpi / 96f);
        int inset = _pressed ? 1 : 0;
        var rect = new Rectangle(inset, inset, Math.Max(4, Width - 1 - inset * 2), Math.Max(4, Height - 1 - inset * 2));
        int rad = _kind == ModernButtonKind.Chip
            ? Math.Max(4, rect.Height / 2)
            : (int)Math.Round(CornerRadius * k);

        Color textColor = Enabled ? ForeColor : Theme.TextMuted;

        switch (_kind)
        {
            case ModernButtonKind.Primary:
                if (Enabled)
                {
                    var c1 = _hover ? Theme.AccentHoverStart : Theme.Accent;
                    var c2 = _hover ? Theme.AccentHoverEnd : Theme.AccentEnd;
                    Gfx.FillGradientRoundRect(g, rect, rad, c1, c2, 12f);
                    Gfx.DrawTopSheen(g, rect, rad);
                }
                else
                {
                    using var p = Gfx.RoundRect(rect, rad);
                    using var b = new SolidBrush(Theme.BgElevated);
                    g.FillPath(b, p);
                }
                textColor = Enabled ? Color.White : Theme.TextMuted;
                break;

            case ModernButtonKind.Secondary:
                using (var p = Gfx.RoundRect(rect, rad))
                {
                    using var bg = new SolidBrush(_hover ? Theme.BgElevated : Theme.BgCard);
                    g.FillPath(bg, p);
                    using var pen = new Pen(_hover ? Theme.BgBorderStrong : Theme.BgBorder, 1f);
                    g.DrawPath(pen, p);
                }
                if (_hover) Gfx.DrawTopSheen(g, rect, rad);
                break;

            case ModernButtonKind.Keycap:
                using (var p = Gfx.RoundRect(rect, (int)Math.Round(6 * k)))
                {
                    var fill = _active ? Theme.AccentSoft : (_hover ? Theme.BgElevated : Theme.BgSurface);
                    using var bg = new SolidBrush(fill);
                    g.FillPath(bg, p);
                    var border = _active ? Theme.Accent : (_hover ? Theme.BgBorderStrong : Theme.BgBorder);
                    using var pen = new Pen(border, 1f);
                    g.DrawPath(pen, p);
                }
                Gfx.DrawTopSheen(g, rect, (int)Math.Round(6 * k));
                break;

            case ModernButtonKind.Chip:
                using (var p = Gfx.RoundRect(rect, rad))
                {
                    using var bg = new SolidBrush(_hover ? Theme.AccentSoft : Theme.BgCard);
                    g.FillPath(bg, p);
                    using var pen = new Pen(_hover ? Theme.Accent : Theme.BgBorder, 1f);
                    g.DrawPath(pen, p);
                }
                textColor = _hover ? Theme.TextPrimary : Theme.TextSoft;
                break;

            case ModernButtonKind.Ghost:
                if (_hover || _active)
                {
                    var pill = Rectangle.Inflate(rect, -3, -5);
                    using var p = Gfx.RoundRect(pill, (int)Math.Round(7 * k));
                    using var bg = new SolidBrush(_active ? Theme.AccentSoft : Theme.BgCardHover);
                    g.FillPath(bg, p);
                    if (_active)
                    {
                        using var pen = new Pen(Color.FromArgb(90, Theme.Accent), 1f);
                        g.DrawPath(pen, p);
                    }
                }
                textColor = _active ? Theme.AccentEnd : (_hover ? Theme.TextPrimary : Theme.TextMuted);
                break;

            case ModernButtonKind.DangerGhost:
                if (_hover)
                {
                    var pill = Rectangle.Inflate(rect, -3, -5);
                    using var p = Gfx.RoundRect(pill, (int)Math.Round(7 * k));
                    using var bg = new SolidBrush(Theme.Danger);
                    g.FillPath(bg, p);
                }
                textColor = _hover ? Color.White : Theme.TextMuted;
                break;
        }

        if (Focused && ShowFocusCues && _kind is ModernButtonKind.Primary or ModernButtonKind.Secondary or ModernButtonKind.Keycap)
        {
            using var fp = Gfx.RoundRect(rect, rad);
            using var fpen = new Pen(Theme.AccentEnd, 1.5f);
            g.DrawPath(fpen, fp);
        }

        TextRenderer.DrawText(g, Text, Font, rect, textColor,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis);
    }
}

/// <summary>Rounded modern dark text box with animated-style focus border.</summary>
internal sealed class DarkTextBox : Panel
{
    private readonly TextBox _inner;
    private bool _focused;

    public DarkTextBox()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                 ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        BackColor = Theme.BgCard;
        Padding = new Padding(10, 6, 10, 6);
        Size = new Size(320, 32);

        _inner = new TextBox
        {
            BorderStyle = BorderStyle.None,
            Dock = DockStyle.Fill,
            BackColor = Theme.BgCard,
            ForeColor = Theme.TextPrimary,
            Font = Theme.Body
        };
        _inner.GotFocus += (_, _) => { _focused = true; Invalidate(); };
        _inner.LostFocus += (_, _) => { _focused = false; Invalidate(); };
        _inner.TextChanged += (_, e) => OnTextChanged(e);

        Controls.Add(_inner);
        Click += (_, _) => _inner.Focus();
    }

    [System.Diagnostics.CodeAnalysis.AllowNull]
    public override string Text
    {
        get => _inner.Text;
        set => _inner.Text = value ?? "";
    }

    public int SelectionStart
    {
        get => _inner.SelectionStart;
        set => _inner.SelectionStart = value;
    }

    public void FocusInput(bool selectAll = false)
    {
        _inner.Focus();
        if (selectAll) _inner.SelectAll();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(Parent?.BackColor ?? Theme.BgMain);

        float k = Math.Max(1f, DeviceDpi / 96f);
        var r = new Rectangle(0, 0, Width - 1, Height - 1);
        int rad = (int)Math.Round(7 * k);
        using var path = Gfx.RoundRect(r, rad);
        using (var bg = new SolidBrush(Theme.BgCard)) g.FillPath(bg, path);
        using (var pen = new Pen(_focused ? Theme.Accent : Theme.BgBorder, _focused ? 1.4f : 1f))
            g.DrawPath(pen, path);
    }
}

/// <summary>Elevated rounded section card used in Settings.</summary>
internal sealed class SectionCard : Panel
{
    public SectionCard()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                 ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        BackColor = Theme.BgSurface;
        Padding = new Padding(14, 10, 14, 12);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(Parent?.BackColor ?? Theme.BgMain);

        float k = Math.Max(1f, DeviceDpi / 96f);
        var r = new Rectangle(0, 0, Width - 1, Height - 1);
        int rad = (int)Math.Round(10 * k);
        using var path = Gfx.RoundRect(r, rad);
        using (var bg = new SolidBrush(Theme.BgSurface)) g.FillPath(bg, path);
        Gfx.DrawTopSheen(g, r, rad);
        using (var pen = new Pen(Theme.BgBorder, 1f)) g.DrawPath(pen, path);
    }
}

/// <summary>
/// Elevated live audio header card displaying active Output (+ Comm) and Input devices with
/// crisp vector icons and a one-click interactive microphone mute pill.
/// </summary>
internal sealed class LiveNowPanel : Control
{
    private string _outText = "—";
    private string? _commShort;
    private string _inText = "—";
    private bool _micMuted;
    private bool _hoverMicRow;

    public event Action? MicMuteToggleRequested;

    public LiveNowPanel()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                 ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        BackColor = Theme.BgMain;
        Height = 76;
    }

    public void UpdateLiveState(string outName, string? commShort, string inName, bool micMuted)
    {
        if (_outText == outName && _commShort == commShort && _inText == inName && _micMuted == micMuted)
            return;

        _outText = outName;
        _commShort = commShort;
        _inText = inName;
        _micMuted = micMuted;
        Invalidate();
    }

    private Rectangle CardRect()
    {
        float k = Math.Max(1f, DeviceDpi / 96f);
        int padX = (int)Math.Round(16 * k);
        int padTop = (int)Math.Round(2 * k);
        int padBot = (int)Math.Round(6 * k);
        return new Rectangle(padX, padTop, Math.Max(20, Width - padX * 2 - 1), Math.Max(20, Height - padTop - padBot - 1));
    }

    private Rectangle MicRowRect()
    {
        var c = CardRect();
        int mid = c.Y + c.Height / 2;
        return new Rectangle(c.X + 4, mid + 2, c.Width - 8, c.Bottom - mid - 5);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        bool over = MicRowRect().Contains(e.Location);
        if (over != _hoverMicRow)
        {
            _hoverMicRow = over;
            Cursor = over ? Cursors.Hand : Cursors.Default;
            Invalidate();
        }
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        if (_hoverMicRow)
        {
            _hoverMicRow = false;
            Cursor = Cursors.Default;
            Invalidate();
        }
        base.OnMouseLeave(e);
    }

    protected override void OnMouseClick(MouseEventArgs e)
    {
        base.OnMouseClick(e);
        if (e.Button == MouseButtons.Left && MicRowRect().Contains(e.Location))
        {
            MicMuteToggleRequested?.Invoke();
        }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(Parent?.BackColor ?? Theme.BgMain);

        float k = Math.Max(1f, DeviceDpi / 96f);
        int P(int v) => (int)Math.Round(v * k);

        var card = CardRect();
        int rad = P(11);
        using var cardPath = Gfx.RoundRect(card, rad);
        using (var bg = new LinearGradientBrush(card, Theme.BgCardTop, Theme.BgSurface, 90f))
            g.FillPath(bg, cardPath);
        Gfx.DrawTopSheen(g, card, rad);

        var borderColor = _micMuted ? Color.FromArgb(130, Theme.Danger) : Theme.BgBorder;
        using (var pen = new Pen(borderColor, 1f))
            g.DrawPath(pen, cardPath);

        int midY = card.Y + card.Height / 2;
        using (var div = new Pen(Color.FromArgb(26, 255, 255, 255), 1f))
            g.DrawLine(div, card.X + P(12), midY, card.Right - P(12), midY);

        // ---- Row 1: Output ----
        int row1H = midY - card.Y;
        var spkRect = new RectangleF(card.X + P(12), card.Y + (row1H - P(14)) / 2f, P(14), P(14));
        Gfx.DrawSpeakerIcon(g, spkRect, Theme.AccentEnd);

        int textLeft = card.X + P(34);
        int outRight = card.Right - P(10);

        if (!string.IsNullOrEmpty(_commShort))
        {
            string commLabel = "COMM";
            int pillW = P(42);
            var pill = new Rectangle(card.Right - P(10) - pillW, card.Y + (row1H - P(17)) / 2, pillW, P(17));
            using var pp = Gfx.RoundRect(pill, P(8));
            using (var pb = new SolidBrush(Theme.AccentSoft)) g.FillPath(pb, pp);
            TextRenderer.DrawText(g, commLabel, Theme.MicroBold, pill, Theme.AccentEnd,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
            outRight = pill.X - P(6);
        }

        var outTextRect = new Rectangle(textLeft, card.Y, Math.Max(P(20), outRight - textLeft), row1H);
        TextRenderer.DrawText(g, _outText, Theme.BodyBold, outTextRect, Theme.TextPrimary,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine);

        // ---- Row 2: Input (interactive click-to-mute) ----
        var micRow = MicRowRect();
        if (_hoverMicRow)
        {
            using var hp = Gfx.RoundRect(micRow, P(7));
            using var hb = new SolidBrush(_micMuted ? Theme.DangerSoft : Color.FromArgb(22, 255, 255, 255));
            g.FillPath(hb, hp);
        }

        int row2H = card.Bottom - midY;
        var micIconRect = new RectangleF(card.X + P(12), midY + (row2H - P(14)) / 2f, P(14), P(14));
        Gfx.DrawMicIcon(g, micIconRect, _micMuted ? Theme.Danger : Theme.Success, muted: _micMuted);

        int badgeW = _micMuted ? P(56) : P(44);
        var statusPill = new Rectangle(card.Right - P(10) - badgeW, midY + (row2H - P(18)) / 2, badgeW, P(18));
        using (var sp = Gfx.RoundRect(statusPill, P(9)))
        {
            using var sb = new SolidBrush(_micMuted ? Theme.DangerSoft : Theme.SuccessSoft);
            g.FillPath(sb, sp);
            using var spen = new Pen(Color.FromArgb(110, _micMuted ? Theme.Danger : Theme.Success), 1f);
            g.DrawPath(spen, sp);
        }
        TextRenderer.DrawText(g, _micMuted ? "● MUTED" : "● LIVE", Theme.MicroBold, statusPill,
            _micMuted ? Theme.Danger : Theme.Success,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);

        var inTextRect = new Rectangle(textLeft, midY, Math.Max(P(20), statusPill.X - P(8) - textLeft), row2H);
        TextRenderer.DrawText(g, _inText, Theme.Body, inTextRect, _micMuted ? Theme.Danger : Theme.TextSoft,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine);
    }
}

/// <summary>Dark renderer for system tray and card ContextMenuStrips.</summary>
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
        using var p = new Pen(Theme.BgBorderStrong);
        e.Graphics.DrawRectangle(p, 0, 0, e.AffectedBounds.Width - 1, e.AffectedBounds.Height - 1);
    }

    protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
    {
        var r = new Rectangle(3, 1, e.Item.Width - 6, e.Item.Height - 2);
        if (e.Item.Selected && e.Item.Enabled)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using var p = Gfx.RoundRect(r, 5);
            using var b = new SolidBrush(Theme.BgElevated);
            e.Graphics.FillPath(b, p);
        }
        else
        {
            using var b = new SolidBrush(Theme.BgCard);
            e.Graphics.FillRectangle(b, e.Item.ContentRectangle);
        }
    }

    protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
    {
        bool isShortcut = e.Item is ToolStripMenuItem mi &&
                          !string.IsNullOrEmpty(mi.ShortcutKeyDisplayString) &&
                          string.Equals(e.Text, mi.ShortcutKeyDisplayString, StringComparison.Ordinal);
        e.TextColor = !e.Item.Enabled ? Theme.TextMuted : (isShortcut ? Theme.TextMuted : Theme.TextPrimary);
        base.OnRenderItemText(e);
    }

    protected override void OnRenderArrow(ToolStripArrowRenderEventArgs e)
    {
        e.ArrowColor = e.Item?.Enabled == true ? Theme.TextSoft : Theme.TextMuted;
        base.OnRenderArrow(e);
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
        using var b = new SolidBrush(Theme.AccentEnd);
        g.FillEllipse(b, dot);
    }

    private sealed class DarkColorTable : ProfessionalColorTable
    {
        public override Color ToolStripDropDownBackground => Theme.BgCard;
        public override Color MenuBorder => Theme.BgBorderStrong;
        public override Color MenuItemBorder => Theme.BgElevated;
        public override Color MenuItemSelected => Theme.BgElevated;
        public override Color ImageMarginGradientBegin => Theme.BgCard;
        public override Color ImageMarginGradientMiddle => Theme.BgCard;
        public override Color ImageMarginGradientEnd => Theme.BgCard;
    }
}

/// <summary>Tray &amp; window icons drawn/loaded at startup.</summary>
internal static class TrayIcons
{
    public static readonly Icon Normal = Create(false);
    public static readonly Icon Muted = Create(true);
    public static readonly Icon AppWindow = LoadProcessIcon() ?? Normal;

    private static Icon? LoadProcessIcon()
    {
        try
        {
            using var stream = typeof(TrayIcons).Assembly.GetManifestResourceStream("AudioProfileSwitcher.app.ico");
            if (stream != null) return new Icon(stream);
        }
        catch { }

        try
        {
            var exe = Environment.ProcessPath;
            if (!string.IsNullOrEmpty(exe) && File.Exists(exe))
                return Icon.ExtractAssociatedIcon(exe);
        }
        catch { }
        return null;
    }

    private static Icon Create(bool muted)
    {
        using var bmp = new Bitmap(32, 32);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Color.Transparent);
            Gfx.DrawAppEmblem(g, new RectangleF(0.5f, 0.5f, 30f, 30f));

            if (muted)
            {
                using var red = new SolidBrush(Theme.Danger);
                using var ring = new Pen(Color.White, 2f);
                g.FillEllipse(red, 18, 18, 13, 13);
                g.DrawEllipse(ring, 18, 18, 13, 13);
            }
        }
        return Icon.FromHandle(bmp.GetHicon());
    }
}

/// <summary>Smoothly animated modern pill toggle switch with indigo-violet gradient track.</summary>
internal sealed class ToggleSwitch : Control
{
    private readonly System.Windows.Forms.Timer _animTimer = new() { Interval = 15 };
    private bool _checked;
    private float _thumbProgress; // 0 = off, 1 = on
    public event EventHandler? CheckedChanged;

    public ToggleSwitch()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                 ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw |
                 ControlStyles.Selectable, true);
        Cursor = Cursors.Hand;
        Size = new Size(42, 22);
        TabStop = true;

        _animTimer.Tick += (_, _) =>
        {
            float target = _checked ? 1f : 0f;
            float diff = target - _thumbProgress;
            if (Math.Abs(diff) < 0.04f)
            {
                _thumbProgress = target;
                _animTimer.Stop();
            }
            else
            {
                _thumbProgress += diff * 0.38f;
            }
            Invalidate();
        };
    }

    public bool Checked
    {
        get => _checked;
        set
        {
            if (_checked == value) return;
            _checked = value;
            if (IsHandleCreated && Visible) _animTimer.Start();
            else _thumbProgress = value ? 1f : 0f;
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
        g.Clear(Parent?.BackColor ?? Theme.BgSurface);

        var r = new Rectangle(0, 0, Width - 1, Height - 1);
        int rad = Height / 2;
        using var track = Gfx.RoundRect(r, rad);

        using (var offBrush = new SolidBrush(Theme.BgBorder))
            g.FillPath(offBrush, track);

        if (_thumbProgress > 0.01f)
        {
            int alpha = Math.Clamp((int)Math.Round(_thumbProgress * 255f), 0, 255);
            using var onGrad = new LinearGradientBrush(
                r,
                Color.FromArgb(alpha, Theme.Accent),
                Color.FromArgb(alpha, Theme.AccentEnd),
                0f);
            g.FillPath(onGrad, track);
        }

        int pad = Math.Max(3, Height / 7);
        int d = Math.Max(8, Height - 1 - pad * 2);
        float x = pad + (Width - 1 - pad * 2 - d) * _thumbProgress;

        using (var shadow = new SolidBrush(Color.FromArgb(55, 0, 0, 0)))
            g.FillEllipse(shadow, x, pad + 1f, d, d);

        using (var knob = new SolidBrush(Color.White))
            g.FillEllipse(knob, x, pad, d, d);

        if (Focused && ShowFocusCues)
        {
            using var p = new Pen(Theme.AccentEnd, 1.2f);
            g.DrawPath(p, track);
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _animTimer.Dispose();
        base.Dispose(disposing);
    }
}

/// <summary>Custom dark horizontal 0..100% slider with gradient fill, glow halo, wheel, and keyboard support.</summary>
internal sealed class DarkSlider : Control
{
    private int _min;
    private int _max = 100;
    private int _value = 80;
    private bool _dragging;
    private bool _hover;

    public event EventHandler? ValueChanged;

    public DarkSlider()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                 ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw |
                 ControlStyles.Selectable, true);
        Cursor = Cursors.Hand;
        Size = new Size(180, 24);
        TabStop = true;
    }

    public int Minimum
    {
        get => _min;
        set { _min = value; if (_value < _min) Value = _min; Invalidate(); }
    }

    public int Maximum
    {
        get => _max;
        set { _max = Math.Max(_min + 1, value); if (_value > _max) Value = _max; Invalidate(); }
    }

    public int Value
    {
        get => _value;
        set
        {
            int clamped = Math.Clamp(value, _min, _max);
            if (_value == clamped) return;
            _value = clamped;
            Invalidate();
            ValueChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private int ThumbRadius => Math.Max(5, Height / 3 - 1);

    private void SetFromX(int x)
    {
        int pad = ThumbRadius + 3;
        int usable = Math.Max(1, Width - pad * 2);
        double ratio = Math.Clamp((x - pad) / (double)usable, 0.0, 1.0);
        Value = _min + (int)Math.Round(ratio * (_max - _min));
    }

    protected override bool IsInputKey(Keys keyData) =>
        keyData is Keys.Left or Keys.Right or Keys.Home or Keys.End || base.IsInputKey(keyData);

    protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { _hover = false; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
    protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }
    protected override void OnEnabledChanged(EventArgs e) { Invalidate(); base.OnEnabledChanged(e); }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (!Enabled || e.Button != MouseButtons.Left) return;
        Focus();
        _dragging = true;
        Capture = true;
        SetFromX(e.X);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (_dragging && Enabled) SetFromX(e.X);
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        if (e.Button == MouseButtons.Left && _dragging)
        {
            _dragging = false;
            Capture = false;
            Invalidate();
        }
    }

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        base.OnMouseWheel(e);
        if (!Enabled) return;
        Value += e.Delta > 0 ? 2 : -2;
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (!Enabled) return;
        switch (e.KeyCode)
        {
            case Keys.Left: Value -= 1; e.Handled = true; break;
            case Keys.Right: Value += 1; e.Handled = true; break;
            case Keys.PageDown: Value -= 10; e.Handled = true; break;
            case Keys.PageUp: Value += 10; e.Handled = true; break;
            case Keys.Home: Value = _min; e.Handled = true; break;
            case Keys.End: Value = _max; e.Handled = true; break;
        }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(Parent?.BackColor ?? Theme.BgMain);

        int r = ThumbRadius;
        int pad = r + 3;
        int trackH = Math.Max(4, Height / 5);
        int trackY = (Height - trackH) / 2;
        int usable = Math.Max(1, Width - pad * 2);
        double ratio = (_value - _min) / (double)Math.Max(1, _max - _min);
        int thumbX = pad + (int)Math.Round(ratio * usable);

        var bgTrack = new Rectangle(pad, trackY, usable, trackH);
        using (var path = Gfx.RoundRect(bgTrack, trackH / 2))
        using (var b = new SolidBrush(Theme.BgBorder))
            g.FillPath(b, path);

        int fillW = thumbX - pad;
        if (fillW > 2)
        {
            var fgTrack = new Rectangle(pad, trackY, fillW, trackH);
            if (Enabled)
            {
                Gfx.FillGradientRoundRect(g, fgTrack, trackH / 2, Theme.Accent, Theme.AccentEnd, 0f);
            }
            else
            {
                using var path = Gfx.RoundRect(fgTrack, trackH / 2);
                using var b = new SolidBrush(Theme.TextMuted);
                g.FillPath(b, path);
            }
        }

        if (Enabled && (_hover || _dragging || (Focused && ShowFocusCues)))
        {
            var halo = new Rectangle(thumbX - r - 3, Height / 2 - r - 3, (r + 3) * 2, (r + 3) * 2);
            using var hb = new SolidBrush(Theme.AccentSoft);
            g.FillEllipse(hb, halo);
        }

        var knobRect = new Rectangle(thumbX - r, Height / 2 - r, r * 2, r * 2);
        using (var knob = new SolidBrush(Enabled ? Color.White : Theme.TextMuted))
            g.FillEllipse(knob, knobRect);

        if (Enabled)
        {
            using var ring = new Pen(_dragging ? Theme.AccentEnd : Theme.Accent, 1.4f);
            g.DrawEllipse(ring, knobRect);
        }
    }
}

/// <summary>Custom-painted dark checklist with rounded gradient checkboxes, wheel scroll, and keyboard navigation.</summary>
internal sealed class DarkCheckedList : Control
{
    private sealed class Entry(DeviceItem item, bool isChecked)
    {
        public DeviceItem Item { get; } = item;
        public bool Checked { get; set; } = isChecked;
    }

    private readonly List<Entry> _entries = new();
    private int _scrollOffset;
    private int _hoverIndex = -1;
    private int _focusIndex;

    public DarkCheckedList()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                 ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw |
                 ControlStyles.Selectable, true);
        BackColor = Theme.BgCard;
        ForeColor = Theme.TextPrimary;
        Font = Theme.Body;
        Cursor = Cursors.Hand;
        TabStop = true;
    }

    private float ScaleFactor => Math.Max(1f, DeviceDpi / 96f);
    private int RowHeight => (int)Math.Round(30 * ScaleFactor);

    public void AddItem(DeviceItem item, bool isChecked)
    {
        _entries.Add(new Entry(item, isChecked));
        Invalidate();
    }

    public void SetAllChecked(bool isChecked)
    {
        foreach (var e in _entries) e.Checked = isChecked;
        Invalidate();
    }

    public List<DeviceItem> GetCheckedItems() =>
        _entries.Where(e => e.Checked).Select(e => e.Item).ToList();

    private int MaxScroll => Math.Max(0, _entries.Count * RowHeight - (Height - 6));

    private void ClampScroll() => _scrollOffset = Math.Clamp(_scrollOffset, 0, MaxScroll);

    private int IndexAtY(int y)
    {
        if (_entries.Count == 0 || y < 3 || y >= Height - 3) return -1;
        int idx = (y - 3 + _scrollOffset) / RowHeight;
        return idx >= 0 && idx < _entries.Count ? idx : -1;
    }

    private void EnsureVisible(int index)
    {
        if (index < 0 || index >= _entries.Count) return;
        int top = index * RowHeight;
        int bottom = top + RowHeight;
        int viewH = Math.Max(RowHeight, Height - 6);
        if (top < _scrollOffset) _scrollOffset = top;
        else if (bottom > _scrollOffset + viewH) _scrollOffset = bottom - viewH;
        ClampScroll();
    }

    protected override bool IsInputKey(Keys keyData) =>
        keyData is Keys.Up or Keys.Down or Keys.Home or Keys.End || base.IsInputKey(keyData);

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        ClampScroll();
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        int idx = IndexAtY(e.Y);
        if (idx != _hoverIndex)
        {
            _hoverIndex = idx;
            Invalidate();
        }
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        _hoverIndex = -1;
        Invalidate();
        base.OnMouseLeave(e);
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button != MouseButtons.Left) return;
        Focus();
        int idx = IndexAtY(e.Y);
        if (idx >= 0)
        {
            _focusIndex = idx;
            _entries[idx].Checked = !_entries[idx].Checked;
            Invalidate();
        }
    }

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        base.OnMouseWheel(e);
        if (MaxScroll <= 0) return;
        _scrollOffset -= Math.Sign(e.Delta) * RowHeight;
        ClampScroll();
        Invalidate();
    }

    protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
    protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (_entries.Count == 0) return;

        switch (e.KeyCode)
        {
            case Keys.Up:
                _focusIndex = Math.Max(0, _focusIndex - 1);
                EnsureVisible(_focusIndex);
                Invalidate();
                e.Handled = true;
                break;
            case Keys.Down:
                _focusIndex = Math.Min(_entries.Count - 1, _focusIndex + 1);
                EnsureVisible(_focusIndex);
                Invalidate();
                e.Handled = true;
                break;
            case Keys.Home:
                _focusIndex = 0;
                EnsureVisible(_focusIndex);
                Invalidate();
                e.Handled = true;
                break;
            case Keys.End:
                _focusIndex = _entries.Count - 1;
                EnsureVisible(_focusIndex);
                Invalidate();
                e.Handled = true;
                break;
            case Keys.Space:
                if (_focusIndex >= 0 && _focusIndex < _entries.Count)
                {
                    _entries[_focusIndex].Checked = !_entries[_focusIndex].Checked;
                    Invalidate();
                }
                e.Handled = true;
                break;
        }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(Parent?.BackColor ?? Theme.BgMain);

        float k = ScaleFactor;
        var outer = new Rectangle(0, 0, Width - 1, Height - 1);
        int rad = (int)Math.Round(8 * k);
        using var outerPath = Gfx.RoundRect(outer, rad);
        using (var bg = new SolidBrush(Theme.BgSurface)) g.FillPath(bg, outerPath);

        var clipState = g.Save();
        g.SetClip(new Rectangle(3, 3, Width - 6, Height - 6));

        if (_entries.Count == 0)
        {
            TextRenderer.DrawText(g, "No devices available", Theme.Small,
                new Rectangle(8, 3, Width - 16, Height - 6), Theme.TextMuted,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
        }
        else
        {
            int rowH = RowHeight;
            int boxSize = (int)Math.Round(16 * k);
            int padLeft = (int)Math.Round(10 * k);
            int scrollBarSpace = MaxScroll > 0 ? (int)Math.Round(9 * k) : 0;

            for (int i = 0; i < _entries.Count; i++)
            {
                int y = 3 + i * rowH - _scrollOffset;
                if (y + rowH < 3 || y > Height - 3) continue;

                var rowRect = new Rectangle(4, y + 1, Width - 8 - scrollBarSpace, rowH - 2);
                if (i == _hoverIndex || (Focused && i == _focusIndex))
                {
                    using var hPath = Gfx.RoundRect(rowRect, (int)Math.Round(6 * k));
                    using var hb = new SolidBrush(Theme.BgCardHover);
                    g.FillPath(hb, hPath);
                }

                var entry = _entries[i];
                var boxRect = new Rectangle(padLeft, y + (rowH - boxSize) / 2, boxSize, boxSize);
                int boxRad = (int)Math.Round(5 * k);
                if (entry.Checked)
                {
                    Gfx.FillGradientRoundRect(g, boxRect, boxRad, Theme.Accent, Theme.AccentEnd, 135f);
                    using var checkPen = new Pen(Color.White, Math.Max(1.6f, 1.7f * k))
                    {
                        StartCap = LineCap.Round,
                        EndCap = LineCap.Round,
                        LineJoin = LineJoin.Round
                    };
                    var p1 = new PointF(boxRect.X + boxSize * 0.24f, boxRect.Y + boxSize * 0.53f);
                    var p2 = new PointF(boxRect.X + boxSize * 0.44f, boxRect.Y + boxSize * 0.72f);
                    var p3 = new PointF(boxRect.X + boxSize * 0.78f, boxRect.Y + boxSize * 0.30f);
                    g.DrawLines(checkPen, new[] { p1, p2, p3 });
                }
                else
                {
                    using var boxPath = Gfx.RoundRect(boxRect, boxRad);
                    using var pen = new Pen(Theme.BgBorderStrong, 1.2f);
                    g.DrawPath(pen, boxPath);
                }

                int textX = boxRect.Right + (int)Math.Round(10 * k);
                var textRect = new Rectangle(textX, y, Math.Max(20, rowRect.Right - textX - 6), rowH);
                var textColor = !entry.Item.Connected
                    ? Theme.Warning
                    : (entry.Checked ? Theme.TextPrimary : Theme.TextSoft);
                TextRenderer.DrawText(g, entry.Item.ToString(), Font, textRect, textColor,
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis |
                    TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine);
            }

            if (MaxScroll > 0)
            {
                int viewH = Height - 10;
                int totalH = _entries.Count * rowH;
                int thumbH = Math.Max((int)Math.Round(18 * k), (int)Math.Round(viewH * (viewH / (double)totalH)));
                int thumbY = 5 + (int)Math.Round((viewH - thumbH) * (_scrollOffset / (double)MaxScroll));
                var thumbRect = new Rectangle(Width - (int)Math.Round(7 * k), thumbY, (int)Math.Round(3 * k), thumbH);
                using var sb = new SolidBrush(Theme.BgBorderStrong);
                using var sp = Gfx.RoundRect(thumbRect, 2);
                g.FillPath(sb, sp);
            }
        }

        g.Restore(clipState);

        using var borderPen = new Pen(Focused ? Theme.Accent : Theme.BgBorder, 1f);
        g.DrawPath(borderPen, outerPath);
    }
}

/// <summary>Dark owner-drawn combo with custom WM_PAINT border and vector chevron (zero legacy Win32 gray chrome).</summary>
internal sealed class DarkCombo : ComboBox
{
    private const int WM_PAINT = 0x000F;
    private bool _hover;

    public DarkCombo()
    {
        DrawMode = DrawMode.OwnerDrawFixed;
        DropDownStyle = ComboBoxStyle.DropDownList;
        FlatStyle = FlatStyle.Flat;
        BackColor = Theme.BgCard;
        ForeColor = Theme.TextPrimary;
        Font = Theme.Body;
        ItemHeight = 24;
        Cursor = Cursors.Hand;
    }

    protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { _hover = false; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
    protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }

    protected override void OnDrawItem(DrawItemEventArgs e)
    {
        if (e.Index < 0) return;
        bool isDropdownItem = (e.State & DrawItemState.ComboBoxEdit) == 0;
        bool highlighted = isDropdownItem && (e.State & DrawItemState.Selected) != 0;

        using (var bg = new SolidBrush(highlighted ? Theme.Accent : Theme.BgCard))
            e.Graphics.FillRectangle(bg, e.Bounds);

        var text = Items[e.Index]?.ToString() ?? "";
        var color = highlighted ? Color.White : Theme.TextPrimary;
        TextRenderer.DrawText(e.Graphics, text, Font, Rectangle.Inflate(e.Bounds, -8, 0), color,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
    }

    protected override void WndProc(ref Message m)
    {
        base.WndProc(ref m);
        if (m.Msg != WM_PAINT || Width <= 4 || Height <= 4) return;

        using var g = Graphics.FromHwnd(Handle);
        g.SmoothingMode = SmoothingMode.AntiAlias;

        float k = Math.Max(1f, DeviceDpi / 96f);
        int arrowAreaW = (int)Math.Round(24 * k);

        // Paint over Win32's default light-gray dropdown button area
        var arrowRect = new Rectangle(Width - arrowAreaW, 1, arrowAreaW - 1, Height - 2);
        using (var bg = new SolidBrush(_hover ? Theme.BgElevated : Theme.BgCard))
            g.FillRectangle(bg, arrowRect);

        // Clean border covering legacy Win32 square chrome
        var outer = new Rectangle(0, 0, Width - 1, Height - 1);
        using (var cover = new Pen(Theme.BgCard, 2f))
            g.DrawRectangle(cover, 1, 1, Width - 3, Height - 3);
        using (var border = new Pen(Focused ? Theme.Accent : (_hover ? Theme.BgBorderStrong : Theme.BgBorder), 1f))
            g.DrawRectangle(border, outer);

        // Anti-aliased downward chevron
        float cx = arrowRect.X + arrowRect.Width * 0.5f;
        float cy = arrowRect.Y + arrowRect.Height * 0.52f;
        float sz = 3.5f * k;
        using var chev = new Pen(_hover || Focused ? Theme.TextPrimary : Theme.TextMuted, Math.Max(1.4f, 1.4f * k))
        {
            StartCap = LineCap.Round,
            EndCap = LineCap.Round,
            LineJoin = LineJoin.Round
        };
        g.DrawLines(chev, new[]
        {
            new PointF(cx - sz, cy - sz * 0.45f),
            new PointF(cx, cy + sz * 0.55f),
            new PointF(cx + sz, cy - sz * 0.45f)
        });
    }
}

/// <summary>
/// Modern rounded audio profile card with gradient elevation, glowing active capsule,
/// keycap slot badge, micro-pill device tags, volume pills, auto-activate bolt badge,
/// drag grip dots, hover Edit/Delete pills, and dark right-click context menu.
/// </summary>
internal sealed class CardControl : Control
{
    private const TextFormatFlags TextFlags =
        TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis |
        TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine;

    private readonly System.Windows.Forms.Timer _confirmTimer = new() { Interval = 2500 };
    private readonly ContextMenuStrip _contextMenu = new();
    private bool _hover, _hoverEdit, _hoverDel, _confirmDelete, _pressed, _downOnButton, _keyboardFocus;
    private bool _isActive, _isFallback, _outMissing, _commMissing, _inMissing;
    private string? _slotText;
    private Point _downPoint;

    public AudioCard Card { get; private set; }

    /// <summary>DPI scale factor (1.0 = 96 dpi), set by the parent.</summary>
    public new float Scale { get; set; } = 1f;

    public event Action<AudioCard>? ActivateRequested;
    public event Action<AudioCard>? EditRequested;
    public event Action<AudioCard>? DuplicateRequested;
    public event Action<AudioCard>? SetFallbackRequested;
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

        _contextMenu.Renderer = new DarkMenuRenderer();
        _contextMenu.BackColor = Theme.BgCard;
        _contextMenu.ForeColor = Theme.TextPrimary;
        _contextMenu.Font = Theme.Body;
        _contextMenu.Opening += (_, _) => PopulateContextMenu();
        ContextMenuStrip = _contextMenu;
    }

    public void BindCard(AudioCard card)
    {
        Card = card;
        Invalidate();
    }

    public int PreferredHeight(float scale) =>
        Card.CommOutputId.HasValue ? (int)Math.Round(106 * scale) : (int)Math.Round(86 * scale);

    public bool IsActive { get => _isActive; set => Upd(ref _isActive, value); }
    public bool IsFallback { get => _isFallback; set => Upd(ref _isFallback, value); }
    public bool OutMissing { get => _outMissing; set => Upd(ref _outMissing, value); }
    public bool CommMissing { get => _commMissing; set => Upd(ref _commMissing, value); }
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

    private void PopulateContextMenu()
    {
        var items = _contextMenu.Items;
        foreach (var old in items.Cast<ToolStripItem>().ToList())
        {
            items.Remove(old);
            old.Dispose();
        }

        items.Add("Activate", null, (_, _) => ActivateRequested?.Invoke(Card));
        items.Add("Edit", null, (_, _) => EditRequested?.Invoke(Card));
        items.Add("Duplicate", null, (_, _) => DuplicateRequested?.Invoke(Card));

        var fbItem = new ToolStripMenuItem("Use as Fallback") { Checked = _isFallback };
        fbItem.Click += (_, _) => SetFallbackRequested?.Invoke(Card);
        items.Add(fbItem);

        items.Add(new ToolStripSeparator());
        items.Add("Delete", null, (_, _) =>
        {
            _confirmTimer.Stop();
            _confirmDelete = false;
            DeleteRequested?.Invoke(Card);
        });
    }

    private int P(int v) => (int)Math.Round(v * Scale);
    private bool ButtonsVisible => _hover || _confirmDelete || (Focused && _keyboardFocus);

    private Rectangle DelRect()
    {
        int w = _confirmDelete ? P(66) : P(26);
        return new Rectangle(Width - P(11) - w, P(10), w, P(24));
    }

    private Rectangle EditRect()
    {
        var d = DelRect();
        return new Rectangle(d.X - P(5) - P(42), P(10), P(42), P(24));
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

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(Parent?.BackColor ?? Theme.BgMain);

        var r = new Rectangle(0, 0, Width - 1, Height - 1);
        int rad = P(12);
        using var path = Gfx.RoundRect(r, rad);

        // Layered gradient surface
        var topCol = _hover ? Theme.BgElevated : (_isActive ? Color.FromArgb(30, 34, 52) : Theme.BgCardTop);
        var botCol = _hover ? Theme.BgCardHover : (_isActive ? Color.FromArgb(22, 25, 39) : Theme.BgCard);
        using (var bg = new LinearGradientBrush(r, topCol, botCol, 90f))
            g.FillPath(bg, path);

        // Ambient indigo wash on active cards
        if (_isActive)
        {
            var state = g.Save();
            g.SetClip(path);
            using var wash = new LinearGradientBrush(
                new Rectangle(0, 0, Math.Max(40, Width / 2), Height),
                Color.FromArgb(36, Theme.Accent),
                Color.FromArgb(0, Theme.Accent),
                0f);
            g.FillRectangle(wash, 0, 0, Math.Max(40, Width / 2), Height);

            // Glowing vertical accent capsule
            var barRect = new Rectangle(P(6), P(12), Math.Max(3, P(3)), Height - P(24));
            Gfx.FillGradientRoundRect(g, barRect, 2, Theme.Accent, Theme.AccentEnd, 90f);
            g.Restore(state);
        }

        Gfx.DrawTopSheen(g, r, rad);

        var borderColor = _isActive
            ? Color.FromArgb(170, Theme.Accent)
            : (_hover || (_keyboardFocus && Focused) ? Theme.BgBorderStrong : Theme.BgBorder);
        using (var pen = new Pen(borderColor, 1f))
            g.DrawPath(pen, path);

        bool hasSlot = !string.IsNullOrEmpty(_slotText);
        int left = hasSlot ? P(46) : P(18);

        if (hasSlot)
        {
            // Sleek keycap badge for 1-9 jump hotkey
            var keycap = new Rectangle(P(14), (Height - P(24)) / 2, P(24), P(24));
            using var kp = Gfx.RoundRect(keycap, P(6));
            using (var kb = new SolidBrush(_isActive ? Theme.AccentSoft : Theme.BgSurface))
                g.FillPath(kb, kp);
            using (var kpen = new Pen(_isActive ? Theme.Accent : Theme.BgBorder, 1f))
                g.DrawPath(kpen, kp);
            TextRenderer.DrawText(g, _slotText!, Theme.SmallBold, keycap,
                _isActive ? Theme.AccentEnd : Theme.TextMuted,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
        }
        else if (_hover && !_isActive)
        {
            // Subtle drag grip dots on hover
            using var dotBrush = new SolidBrush(Theme.BgBorderStrong);
            int gx = P(7);
            int gy = Height / 2 - P(7);
            int d = Math.Max(2, P(2));
            for (int row = 0; row < 3; row++)
            {
                g.FillEllipse(dotBrush, gx, gy + row * P(5), d, d);
                g.FillEllipse(dotBrush, gx + P(4), gy + row * P(5), d, d);
            }
        }

        bool showButtons = ButtonsVisible;
        int rightBadgeSpace = showButtons
            ? Width - EditRect().X + P(6)
            : (_isActive ? P(76) : (_isFallback ? P(46) : P(14)));

        int titleMaxW = Math.Max(P(40), Width - left - rightBadgeSpace);
        int boltPad = Card.AutoActivate ? P(18) : 0;
        var titleRect = new Rectangle(left, P(10), Math.Max(P(30), titleMaxW - boltPad), P(24));
        TextRenderer.DrawText(g, Card.Name, Theme.Title, titleRect, Theme.TextPrimary, TextFlags);

        if (Card.AutoActivate)
        {
            var measured = TextRenderer.MeasureText(g, Card.Name, Theme.Title, titleRect.Size, TextFlags);
            int boltX = Math.Min(titleRect.Right, left + measured.Width + P(2));
            Gfx.DrawBoltIcon(g, new RectangleF(boltX, P(15), P(12), P(13)), Theme.Warning);
        }

        DrawDeviceRow(g, "OUT", Card.OutputId.HasValue ? Card.OutputName : null, Card.OutputVolume, _outMissing, left, P(39));

        if (Card.CommOutputId.HasValue)
        {
            DrawDeviceRow(g, "COMM", Card.CommOutputName, null, _commMissing, left, P(60));
            DrawDeviceRow(g, "IN", Card.InputId.HasValue ? Card.InputName : null, Card.InputVolume, _inMissing, left, P(81));
        }
        else
        {
            DrawDeviceRow(g, "IN", Card.InputId.HasValue ? Card.InputName : null, Card.InputVolume, _inMissing, left, P(60));
        }

        if (showButtons)
        {
            DrawEditButton(g, EditRect());
            DrawDeleteButton(g, DelRect());
        }
        else if (_isActive)
        {
            var pill = new Rectangle(Width - P(12) - P(60), P(12), P(60), P(20));
            using var pp = Gfx.RoundRect(pill, P(10));
            using (var fill = new SolidBrush(Theme.AccentSoft)) g.FillPath(fill, pp);
            using (var pen = new Pen(Color.FromArgb(115, Theme.Accent), 1f)) g.DrawPath(pen, pp);
            TextRenderer.DrawText(g, "● ACTIVE", Theme.MicroBold, pill, Theme.AccentEnd,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
        }
        else if (_isFallback)
        {
            var pill = new Rectangle(Width - P(12) - P(34), P(12), P(34), P(20));
            using var pp = Gfx.RoundRect(pill, P(10));
            using (var fill = new SolidBrush(Theme.BgElevated)) g.FillPath(fill, pp);
            using (var pen = new Pen(Theme.BgBorderStrong, 1f)) g.DrawPath(pen, pp);
            TextRenderer.DrawText(g, "FB", Theme.MicroBold, pill, Theme.TextSoft,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
        }
    }

    private void DrawDeviceRow(Graphics g, string tag, string? name, int? volume, bool missing, int left, int y)
    {
        int pillW = P(38);
        int pillH = P(16);
        var tagRect = new Rectangle(left, y + P(1), pillW, pillH);
        using (var tp = Gfx.RoundRect(tagRect, P(4)))
        {
            using var tb = new SolidBrush(Theme.BgSurface);
            g.FillPath(tb, tp);
            using var tpen = new Pen(Color.FromArgb(30, 255, 255, 255), 1f);
            g.DrawPath(tpen, tp);
        }
        TextRenderer.DrawText(g, tag, Theme.MicroBold, tagRect, Theme.TextMuted,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);

        int rightLimit = Width - P(12);
        if (volume.HasValue)
        {
            int volW = P(36);
            var volRect = new Rectangle(rightLimit - volW, y + P(1), volW, pillH);
            using var vp = Gfx.RoundRect(volRect, P(8));
            using (var vb = new SolidBrush(Theme.AccentSoft)) g.FillPath(vb, vp);
            TextRenderer.DrawText(g, $"{volume.Value}%", Theme.MicroBold, volRect, Theme.AccentEnd,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
            rightLimit = volRect.X - P(6);
        }

        string text = name ?? "Not changed";
        if (missing) text += "  (offline)";
        var color = missing ? Theme.Warning : (name == null && !volume.HasValue ? Theme.TextMuted : Theme.TextSoft);

        int x = left + pillW + P(8);
        TextRenderer.DrawText(g, text, Theme.Small,
            new Rectangle(x, y, Math.Max(P(20), rightLimit - x), P(18)), color, TextFlags);
    }

    private void DrawEditButton(Graphics g, Rectangle rect)
    {
        using var p = Gfx.RoundRect(rect, P(6));
        using (var b = new SolidBrush(_hoverEdit ? Theme.BgBorderStrong : Theme.BgSurface))
            g.FillPath(b, p);
        using (var pen = new Pen(_hoverEdit ? Theme.BgBorderStrong : Theme.BgBorder, 1f))
            g.DrawPath(pen, p);
        TextRenderer.DrawText(g, "Edit", Theme.Small, rect, _hoverEdit ? Theme.TextPrimary : Theme.TextSoft,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
    }

    private void DrawDeleteButton(Graphics g, Rectangle rect)
    {
        using var p = Gfx.RoundRect(rect, P(6));
        using (var b = new SolidBrush(_confirmDelete ? Theme.Danger : (_hoverDel ? Theme.DangerSoft : Theme.BgSurface)))
            g.FillPath(b, p);
        using (var pen = new Pen(_confirmDelete || _hoverDel ? Theme.Danger : Theme.BgBorder, 1f))
            g.DrawPath(pen, p);

        string text = _confirmDelete ? "Delete?" : "×";
        var color = _confirmDelete ? Color.White : (_hoverDel ? Theme.Danger : Theme.TextMuted);
        TextRenderer.DrawText(g, text, _confirmDelete ? Theme.SmallBold : Theme.Title, rect, color,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _confirmTimer.Dispose();
            _contextMenu.Dispose();
        }
        base.Dispose(disposing);
    }
}

/// <summary>Always-on-top, never-activating PerMonitorV2-aware rounded overlay window.</summary>
internal abstract class OverlayForm : Form
{
    private readonly List<Font> _ownedFonts = new();
    private bool _clickThrough = true;

    protected OverlayForm()
    {
        AutoScaleMode = AutoScaleMode.None;
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        TopMost = true;
        StartPosition = FormStartPosition.Manual;
        DoubleBuffered = true;
    }

    protected bool ClickThrough
    {
        get => _clickThrough;
        set
        {
            if (_clickThrough == value) return;
            _clickThrough = value;
            if (IsHandleCreated) UpdateStyles();
        }
    }

    protected float Dpi => Math.Max(0.5f, DeviceDpi / 96f);

    protected int Px(int logicalPx) => (int)Math.Round(logicalPx * Dpi);

    protected Font CreateDpiFont(float ptAt96Dpi, FontStyle style)
    {
        float pxSize = Math.Max(8f, ptAt96Dpi * (96f / 72f) * Dpi);
        var f = new Font("Segoe UI", pxSize, style, GraphicsUnit.Pixel);
        _ownedFonts.Add(f);
        return f;
    }

    protected void ClearOwnedFonts()
    {
        foreach (var f in _ownedFonts) f.Dispose();
        _ownedFonts.Clear();
    }

    protected static Rectangle? TryGetWorkingArea(Screen? preferred)
    {
        try
        {
            var screen = preferred ?? Screen.PrimaryScreen;
            if (screen == null)
            {
                var all = Screen.AllScreens;
                if (all.Length > 0) screen = all[0];
            }
            return screen?.WorkingArea;
        }
        catch
        {
            return null;
        }
    }

    protected abstract void ApplyDpiLayout();

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        Native.RoundCornersSmall(Handle);
    }

    protected override void OnDpiChanged(DpiChangedEventArgs e)
    {
        base.OnDpiChanged(e);
        ApplyDpiLayout();
        Invalidate();
    }

    protected override bool ShowWithoutActivation => true;

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.ExStyle |= 0x08000000 | 0x00000080 | 0x00000008; // NOACTIVATE | TOOLWINDOW | TOPMOST
            if (_clickThrough) cp.ExStyle |= 0x00000020; // WS_EX_TRANSPARENT
            cp.ClassStyle |= 0x00020000; // CS_DROPSHADOW
            return cp;
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) ClearOwnedFonts();
        base.Dispose(disposing);
    }
}

/// <summary>Modern rounded floating toast with smooth opacity fade-in / fade-out.</summary>
internal sealed class OsdBannerForm : OverlayForm
{
    private const double TargetOpacity = 0.96;

    private readonly System.Windows.Forms.Timer _holdTimer = new() { Interval = 1600 };
    private readonly System.Windows.Forms.Timer _fadeTimer = new() { Interval = 16 };
    private readonly Label _catLbl = new() { BackColor = Color.Transparent };
    private readonly Label _msgLbl = new() { AutoEllipsis = true, BackColor = Color.Transparent };
    private Color _accent = Theme.Accent;
    private Rectangle? _targetArea;
    private bool _fadingOut;

    public OsdBannerForm()
    {
        BackColor = Theme.BgCard;
        Opacity = 0;

        _catLbl.ForeColor = Theme.TextMuted;
        _msgLbl.ForeColor = Theme.TextPrimary;
        Controls.AddRange(new Control[] { _catLbl, _msgLbl });

        ApplyDpiLayout();

        _holdTimer.Tick += (_, _) =>
        {
            _holdTimer.Stop();
            _fadingOut = true;
            _fadeTimer.Start();
        };

        _fadeTimer.Tick += (_, _) =>
        {
            if (!_fadingOut)
            {
                double next = Math.Min(TargetOpacity, Opacity + 0.14);
                Opacity = next;
                if (next >= TargetOpacity - 0.005) _fadeTimer.Stop();
            }
            else
            {
                double next = Math.Max(0.0, Opacity - 0.11);
                Opacity = next;
                if (next <= 0.005)
                {
                    _fadeTimer.Stop();
                    Hide();
                }
            }
        };
    }

    protected override void ApplyDpiLayout()
    {
        var catFont = new Font("Segoe UI", Math.Max(8f, 7.5f * (96f / 72f) * Dpi), FontStyle.Bold, GraphicsUnit.Pixel);
        var msgFont = new Font("Segoe UI", Math.Max(10f, 10f * (96f / 72f) * Dpi), FontStyle.Bold, GraphicsUnit.Pixel);

        _catLbl.Font = catFont;
        _msgLbl.Font = msgFont;
        ClearOwnedFonts();
        _catLbl.Font = CreateDpiFont(7.5f, FontStyle.Bold);
        _msgLbl.Font = CreateDpiFont(10f, FontStyle.Bold);
        catFont.Dispose();
        msgFont.Dispose();

        Size = new Size(Px(356), Px(64));
        _catLbl.Location = new Point(Px(20), Px(11));
        _catLbl.Size = new Size(Px(320), Px(16));
        _msgLbl.Location = new Point(Px(20), Px(29));
        _msgLbl.Size = new Size(Px(320), Px(24));

        if (_targetArea is { } area)
        {
            Location = new Point(area.Left + (area.Width - Width) / 2, area.Bottom - Height - Px(38));
        }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        var r = new Rectangle(0, 0, Width - 1, Height - 1);

        using (var bg = new LinearGradientBrush(r, Theme.BgCardTop, Theme.BgSurface, 90f))
            g.FillRectangle(bg, r);

        // Ambient glow strip along left edge
        using (var glow = new LinearGradientBrush(new Rectangle(0, 0, Px(80), Height), Color.FromArgb(40, _accent), Color.FromArgb(0, _accent), 0f))
            g.FillRectangle(glow, 0, 0, Px(80), Height);

        var pillBar = new Rectangle(Px(7), Px(12), Math.Max(3, Px(4)), Height - Px(24));
        using (var bp = Gfx.RoundRect(pillBar, 2))
        using (var bb = new SolidBrush(_accent))
            g.FillPath(bb, bp);

        using (var border = new Pen(Theme.BgBorderStrong, 1f))
            g.DrawRectangle(border, 0, 0, Width - 1, Height - 1);
    }

    public void ShowMessage(string category, string message, Color accentColor)
    {
        Screen? activeScreen = null;
        try { activeScreen = Screen.FromPoint(Cursor.Position); } catch { }
        var area = TryGetWorkingArea(activeScreen);
        if (area == null) return;

        _accent = accentColor;
        _catLbl.Text = category;
        _msgLbl.Text = message;
        _targetArea = area.Value;

        Location = new Point(area.Value.Left + (area.Value.Width - Width) / 2, area.Value.Bottom - Height - Px(38));
        ApplyDpiLayout();
        Invalidate();

        _fadingOut = false;
        if (!Visible)
        {
            Opacity = 0.05;
            Show();
        }
        _fadeTimer.Start();
        _holdTimer.Stop();
        _holdTimer.Start();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _holdTimer.Dispose();
            _fadeTimer.Dispose();
        }
        base.Dispose(disposing);
    }
}

/// <summary>Modern crimson floating MIC MUTED pill with optional drag-to-move and click-to-unmute.</summary>
internal sealed class MuteBannerForm : OverlayForm
{
    private readonly Label _label;
    private MuteBannerPosition _position = MuteBannerPosition.TopCenter;
    private int? _customX;
    private int? _customY;
    private Rectangle? _targetArea;
    private bool _mouseDown;
    private bool _dragged;
    private bool _hover;
    private Point _dragStartScreen;
    private Point _formStartLocation;

    public event Action<Point>? BannerMoved;
    public event Action? UnmuteRequested;

    public MuteBannerForm()
    {
        BackColor = Color.FromArgb(36, 16, 22);
        Opacity = 0.94;

        _label = new Label
        {
            Text = "●  MIC MUTED",
            ForeColor = Color.FromArgb(255, 112, 128),
            BackColor = Color.Transparent,
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleCenter
        };
        _label.MouseEnter += (_, _) => { if (Interactive) { _hover = true; Invalidate(); } };
        _label.MouseLeave += (_, _) => { if (_hover) { _hover = false; Invalidate(); } };
        _label.MouseDown += OnInteractiveMouseDown;
        _label.MouseMove += OnInteractiveMouseMove;
        _label.MouseUp += OnInteractiveMouseUp;

        Controls.Add(_label);
        ApplyDpiLayout();
    }

    public bool Interactive
    {
        get => !ClickThrough;
        set
        {
            ClickThrough = !value;
            _label.Cursor = value ? Cursors.Hand : Cursors.Default;
            Invalidate();
        }
    }

    private void OnInteractiveMouseDown(object? sender, MouseEventArgs e)
    {
        if (!Interactive || e.Button != MouseButtons.Left) return;
        _mouseDown = true;
        _dragged = false;
        _dragStartScreen = Cursor.Position;
        _formStartLocation = Location;
        _label.Capture = true;
    }

    private void OnInteractiveMouseMove(object? sender, MouseEventArgs e)
    {
        if (!_mouseDown || !Interactive) return;
        var cur = Cursor.Position;
        int dx = cur.X - _dragStartScreen.X;
        int dy = cur.Y - _dragStartScreen.Y;

        if (!_dragged && (Math.Abs(dx) > 4 || Math.Abs(dy) > 4))
        {
            _dragged = true;
            _label.Cursor = Cursors.SizeAll;
        }

        if (_dragged)
        {
            var next = new Point(_formStartLocation.X + dx, _formStartLocation.Y + dy);
            Screen? scr = null;
            try { scr = Screen.FromPoint(cur); } catch { }
            if (TryGetWorkingArea(scr) is { } area)
            {
                next = new Point(
                    Math.Clamp(next.X, area.Left, Math.Max(area.Left, area.Right - Width)),
                    Math.Clamp(next.Y, area.Top, Math.Max(area.Top, area.Bottom - Height)));
                _targetArea = area;
            }
            Location = next;
        }
    }

    private void OnInteractiveMouseUp(object? sender, MouseEventArgs e)
    {
        if (!_mouseDown || e.Button != MouseButtons.Left) return;
        _mouseDown = false;
        _label.Capture = false;
        _label.Cursor = Interactive ? Cursors.Hand : Cursors.Default;

        if (_dragged)
        {
            _position = MuteBannerPosition.Custom;
            _customX = Location.X;
            _customY = Location.Y;
            BannerMoved?.Invoke(Location);
        }
        else if (Interactive && ClientRectangle.Contains(PointToClient(Cursor.Position)))
        {
            UnmuteRequested?.Invoke();
        }
    }

    protected override void ApplyDpiLayout()
    {
        var tempFont = new Font("Segoe UI", Math.Max(9f, 8.5f * (96f / 72f) * Dpi), FontStyle.Bold, GraphicsUnit.Pixel);
        _label.Font = tempFont;
        ClearOwnedFonts();
        _label.Font = CreateDpiFont(8.5f, FontStyle.Bold);
        tempFont.Dispose();

        Size = new Size(Px(136), Px(32));
        if (_targetArea is { } area && !_dragged)
        {
            Location = ProfileLogic.ComputeMuteBannerLocation(area, Size, _position, Dpi, _customX, _customY);
        }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        var r = new Rectangle(0, 0, Width - 1, Height - 1);

        using (var bg = new LinearGradientBrush(r, Color.FromArgb(46, 18, 26), Color.FromArgb(28, 12, 18), 90f))
            g.FillRectangle(bg, r);

        if (Interactive)
        {
            // Subtle grip dots on right edge when draggable
            using var dot = new SolidBrush(Color.FromArgb(120, 255, 130, 145));
            int gx = Width - Px(11);
            int gy = Height / 2 - Px(4);
            int d = Math.Max(2, Px(2));
            g.FillEllipse(dot, gx, gy, d, d);
            g.FillEllipse(dot, gx, gy + Px(5), d, d);
        }

        var borderColor = Interactive && _hover ? Color.FromArgb(255, 132, 148) : Theme.Danger;
        using var border = new Pen(borderColor, 1.2f);
        g.DrawRectangle(border, 0, 0, Width - 1, Height - 1);
    }

    public void ShowBanner(MuteBannerPosition position = MuteBannerPosition.TopCenter, int? customX = null, int? customY = null)
    {
        Screen? preferred = null;
        if (position == MuteBannerPosition.ActiveScreenTop)
        {
            try { preferred = Screen.FromPoint(Cursor.Position); } catch { }
        }
        else if (position == MuteBannerPosition.Custom && customX.HasValue && customY.HasValue)
        {
            try { preferred = Screen.FromPoint(new Point(customX.Value, customY.Value)); } catch { }
        }

        var area = TryGetWorkingArea(preferred);
        if (area == null) return;

        _position = position;
        _customX = customX;
        _customY = customY;
        _targetArea = area.Value;
        Location = ProfileLogic.ComputeMuteBannerLocation(area.Value, Size, position, Dpi, customX, customY);
        ApplyDpiLayout();
        Invalidate();
        if (!Visible) Show();
    }
}