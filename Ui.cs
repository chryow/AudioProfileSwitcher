using System.Drawing.Drawing2D;
using System.Drawing.Text;

namespace AudioProfileSwitcher;

/// <summary>
/// Centralized Editorial E-Ink color tokens, Windows system fonts, and cached GDI+ pens/brushes
/// (printed ledger / Japanese stationery / tactile e-reader aesthetic).
/// </summary>
internal static class Theme
{
    // ---- Core Editorial E-Ink Palette -------------------------------------------------------
    public static readonly Color Paper = Color.FromArgb(247, 245, 240);          // #F7F5F0 Unbleached Paper
    public static readonly Color Sheet = Color.FromArgb(255, 255, 255);          // #FFFFFF Crisp Sheet
    public static readonly Color PressedPaper = Color.FromArgb(239, 236, 228);   // #EFECE4 Hover / Pressed Paper
    public static readonly Color Ink = Color.FromArgb(26, 26, 24);               // #1A1A18 Rich Ink
    public static readonly Color InkHover = Color.FromArgb(46, 46, 42);          // Subtle pressed/hover Rich Ink
    public static readonly Color Graphite = Color.FromArgb(107, 105, 99);        // #6B6963 Muted Text / Sublabels
    public static readonly Color Rule = Color.FromArgb(216, 212, 202);           // #D8D4CA Hairline Borders & Dividers
    public static readonly Color StrongBorder = Color.FromArgb(26, 26, 24);      // #1A1A18 Strong / Active Border
    public static readonly Color Vermilion = Color.FromArgb(200, 62, 43);        // #C83E2B Stamp Vermilion
    public static readonly Color VermilionHover = Color.FromArgb(176, 52, 35);   // Pressed / Hover Vermilion
    public static readonly Color FountainGreen = Color.FromArgb(35, 78, 61);     // #234E3D Fountain Pen Green

    // ---- Semantic Aliases -------------------------------------------------------------------
    public static Color BgMain => Paper;
    public static Color BgSurface => Sheet;
    public static Color BgCard => Sheet;
    public static Color BgCardHover => PressedPaper;
    public static Color BgElevated => PressedPaper;
    public static Color BgBorder => Rule;
    public static Color BgBorderStrong => StrongBorder;

    public static Color TextPrimary => Ink;
    public static Color TextSoft => Graphite;
    public static Color TextMuted => Graphite;

    public static Color Accent => Vermilion;
    public static Color AccentEnd => Vermilion;
    public static Color Danger => Vermilion;
    public static Color Warning => Vermilion;
    public static Color Success => FountainGreen;

    // ---- Typography (Built-in Windows System Fonts) -----------------------------------------
    private static readonly string MonoFamily = ResolveMonoFontFamily();
    public static readonly bool HasMdl2 = HasFontFamily("Segoe MDL2 Assets");

    private static string ResolveMonoFontFamily()
    {
        if (HasFontFamily("Cascadia Mono")) return "Cascadia Mono";
        return "Consolas";
    }

    private static bool HasFontFamily(string name)
    {
        try
        {
            return FontFamily.Families.Any(f => f.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
        }
        catch
        {
            return false;
        }
    }

    // Editorial Serif (Window Title & Profile Headings)
    public static readonly Font Title = new("Georgia", 11f, FontStyle.Bold);
    public static readonly Font CardTitle = new("Georgia", 10f, FontStyle.Bold);
    public static readonly Font Header = new("Georgia", 10.5f, FontStyle.Bold);

    // Clean UI Sans (Labels, Device Names, Dialog Controls)
    public static readonly Font Body = new("Segoe UI", 9f, FontStyle.Regular);
    public static readonly Font BodyBold = new("Segoe UI", 9f, FontStyle.Bold);
    public static readonly Font Small = new("Segoe UI", 8.5f, FontStyle.Regular);

    // Monospace (Hotkeys, Status Tags, Badges, Colophon)
    public static readonly Font Mono = new(MonoFamily, 8f, FontStyle.Regular);
    public static readonly Font MonoBold = new(MonoFamily, 8f, FontStyle.Bold);
    public static readonly Font MonoSmall = new(MonoFamily, 7.5f, FontStyle.Regular);
    public static readonly Font MonoSmallBold = new(MonoFamily, 7.5f, FontStyle.Bold);

    public static Font SmallBold => MonoBold;
    public static Font MicroBold => MonoSmallBold;
    public static readonly Font IconFont = HasMdl2 ? new Font("Segoe MDL2 Assets", 9.5f) : new Font("Segoe UI", 9.5f);

    public static string Glyph(string mdl2, string fallback) => HasMdl2 ? mdl2 : fallback;
    public static string MonospaceFontName => MonoFamily;

    // ---- Cached GDI+ Brushes & Pens (disposed via DisposeResources on app exit) -------------
    public static readonly SolidBrush BrushPaper = new(Paper);
    public static readonly SolidBrush BrushSheet = new(Sheet);
    public static readonly SolidBrush BrushPressedPaper = new(PressedPaper);
    public static readonly SolidBrush BrushInk = new(Ink);
    public static readonly SolidBrush BrushInkHover = new(InkHover);
    public static readonly SolidBrush BrushGraphite = new(Graphite);
    public static readonly SolidBrush BrushRule = new(Rule);
    public static readonly SolidBrush BrushVermilion = new(Vermilion);
    public static readonly SolidBrush BrushVermilionHover = new(VermilionHover);
    public static readonly SolidBrush BrushFountainGreen = new(FountainGreen);

    public static readonly Pen PenRule = new(Rule, 1f);
    public static readonly Pen PenInk = new(Ink, 1f);
    public static readonly Pen PenInkFocus = new(Ink, 1.5f);
    public static readonly Pen PenVermilion = new(Vermilion, 1f);
    public static readonly Pen PenFountainGreen = new(FountainGreen, 1f);
    public static readonly Pen PenGraphite = new(Graphite, 1f);

    private static int _disposed;

    public static void ConfigureGraphics(Graphics g)
    {
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
    }

    public static void DisposeResources()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;

        Title.Dispose();
        CardTitle.Dispose();
        Header.Dispose();
        Body.Dispose();
        BodyBold.Dispose();
        Small.Dispose();
        Mono.Dispose();
        MonoBold.Dispose();
        MonoSmall.Dispose();
        MonoSmallBold.Dispose();
        IconFont.Dispose();

        BrushPaper.Dispose();
        BrushSheet.Dispose();
        BrushPressedPaper.Dispose();
        BrushInk.Dispose();
        BrushInkHover.Dispose();
        BrushGraphite.Dispose();
        BrushRule.Dispose();
        BrushVermilion.Dispose();
        BrushVermilionHover.Dispose();
        BrushFountainGreen.Dispose();

        PenRule.Dispose();
        PenInk.Dispose();
        PenInkFocus.Dispose();
        PenVermilion.Dispose();
        PenFountainGreen.Dispose();
        PenGraphite.Dispose();
    }
}

/// <summary>Crisp Editorial E-Ink vector primitives (zero gradients, zero glow, tight 0–4px corners).</summary>
internal static class Gfx
{
    public static GraphicsPath RoundRect(Rectangle r, int radius)
    {
        var path = new GraphicsPath();
        int clamped = Math.Clamp(radius, 0, Math.Min(4, Math.Min(r.Width, r.Height) / 2));
        if (clamped <= 0)
        {
            path.AddRectangle(r);
            return path;
        }

        int d = clamped * 2;
        path.AddArc(r.X, r.Y, d, d, 180, 90);
        path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }

    public static void FillRect(Graphics g, Rectangle r, int radius, Brush brush)
    {
        if (r.Width <= 0 || r.Height <= 0) return;
        if (radius <= 0)
        {
            g.FillRectangle(brush, r);
            return;
        }
        using var path = RoundRect(r, radius);
        g.FillPath(brush, path);
    }

    public static void StrokeRect(Graphics g, Rectangle r, int radius, Pen pen)
    {
        if (r.Width <= 0 || r.Height <= 0) return;
        if (radius <= 0)
        {
            g.DrawRectangle(pen, r);
            return;
        }
        using var path = RoundRect(r, radius);
        g.DrawPath(pen, path);
    }

    public static void FillAndStrokeRect(Graphics g, Rectangle r, int radius, Brush fill, Pen border)
    {
        if (r.Width <= 0 || r.Height <= 0) return;
        if (radius <= 0)
        {
            g.FillRectangle(fill, r);
            g.DrawRectangle(border, r);
            return;
        }
        using var path = RoundRect(r, radius);
        g.FillPath(fill, path);
        g.DrawPath(border, path);
    }

    /// <summary>Draws a crisp Editorial E-Ink stamp emblem (ink square, paper speaker glyph, vermilion seal dot).</summary>
    public static void DrawAppEmblem(Graphics g, RectangleF r)
    {
        var ri = Rectangle.Round(r);
        using (var path = RoundRect(ri, 2))
            g.FillPath(Theme.BrushInk, path);

        float x = r.X;
        float y = r.Y;
        float w = r.Width;
        float h = r.Height;

        g.FillPolygon(Theme.BrushPaper, new[]
        {
            new PointF(x + w * 0.20f, y + h * 0.40f),
            new PointF(x + w * 0.36f, y + h * 0.40f),
            new PointF(x + w * 0.54f, y + h * 0.25f),
            new PointF(x + w * 0.54f, y + h * 0.75f),
            new PointF(x + w * 0.36f, y + h * 0.60f),
            new PointF(x + w * 0.20f, y + h * 0.60f)
        });

        using var wavePen = new Pen(Theme.Paper, Math.Max(1.25f, w * 0.075f))
        {
            StartCap = LineCap.Square,
            EndCap = LineCap.Square
        };
        g.DrawArc(wavePen, x + w * 0.46f, y + h * 0.34f, w * 0.26f, h * 0.32f, -45, 90);

        // Stamp Vermilion editorial square seal in the top-right corner
        float dot = Math.Max(3f, w * 0.20f);
        g.FillRectangle(Theme.BrushVermilion, x + w - dot - Math.Max(2f, w * 0.10f), y + Math.Max(2f, h * 0.10f), dot, dot);
    }

    public static void DrawSpeakerIcon(Graphics g, RectangleF r, Color color)
    {
        float x = r.X, y = r.Y, w = r.Width, h = r.Height;
        using var b = new SolidBrush(color);
        g.FillPolygon(b, new[]
        {
            new PointF(x + w * 0.10f, y + h * 0.36f),
            new PointF(x + w * 0.34f, y + h * 0.36f),
            new PointF(x + w * 0.56f, y + h * 0.18f),
            new PointF(x + w * 0.56f, y + h * 0.82f),
            new PointF(x + w * 0.34f, y + h * 0.64f),
            new PointF(x + w * 0.10f, y + h * 0.64f)
        });
        using var p = new Pen(color, Math.Max(1.2f, w * 0.10f)) { StartCap = LineCap.Flat, EndCap = LineCap.Flat };
        g.DrawArc(p, x + w * 0.48f, y + h * 0.28f, w * 0.34f, h * 0.44f, -45, 90);
    }

    public static void DrawMicIcon(Graphics g, RectangleF r, Color color, bool muted = false)
    {
        float x = r.X, y = r.Y, w = r.Width, h = r.Height;
        float capW = w * 0.32f;
        float capH = h * 0.46f;
        float capX = x + (w - capW) * 0.5f;
        float capY = y + h * 0.12f;

        using var b = new SolidBrush(color);
        using (var capPath = RoundRect(Rectangle.Round(new RectangleF(capX, capY, capW, capH)), 2))
            g.FillPath(b, capPath);

        using var p = new Pen(color, Math.Max(1.2f, w * 0.10f)) { StartCap = LineCap.Flat, EndCap = LineCap.Flat };
        g.DrawArc(p, x + w * 0.20f, y + h * 0.28f, w * 0.60f, h * 0.44f, 0, 180);
        g.DrawLine(p, x + w * 0.50f, y + h * 0.72f, x + w * 0.50f, y + h * 0.88f);
        g.DrawLine(p, x + w * 0.32f, y + h * 0.88f, x + w * 0.68f, y + h * 0.88f);

        if (muted)
        {
            using var slash = new Pen(Theme.Vermilion, Math.Max(1.5f, w * 0.12f)) { StartCap = LineCap.Flat, EndCap = LineCap.Flat };
            g.DrawLine(slash, x + w * 0.14f, y + h * 0.84f, x + w * 0.86f, y + h * 0.16f);
        }
    }
}

internal enum ModernButtonKind
{
    Primary,
    AccentPrimary,
    Secondary,
    Ghost,
    DangerGhost,
    Keycap,
    Chip
}

/// <summary>Crisp Editorial E-Ink flat button with sharp 2px geometry and ink hairline borders.</summary>
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
            ModernButtonKind.Primary or ModernButtonKind.AccentPrimary => Theme.BodyBold,
            ModernButtonKind.Keycap => Theme.MonoBold,
            ModernButtonKind.Chip => Theme.MonoSmall,
            ModernButtonKind.Ghost or ModernButtonKind.DangerGhost => Theme.IconFont,
            _ => Theme.Body
        };
        ForeColor = Theme.Ink;
        BackColor = Theme.Paper;
        Size = new Size(100, 32);
        TabStop = kind is not (ModernButtonKind.Ghost or ModernButtonKind.DangerGhost or ModernButtonKind.Chip);
    }

    public ModernButtonKind Kind
    {
        get => _kind;
        set
        {
            _kind = value;
            Font = value switch
            {
                ModernButtonKind.Primary or ModernButtonKind.AccentPrimary => Theme.BodyBold,
                ModernButtonKind.Keycap => Theme.MonoBold,
                ModernButtonKind.Chip => Theme.MonoSmall,
                ModernButtonKind.Ghost or ModernButtonKind.DangerGhost => Theme.IconFont,
                _ => Theme.Body
            };
            Invalidate();
        }
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

    public int CornerRadius { get; set; } = 2;

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
        Theme.ConfigureGraphics(g);
        g.Clear(Parent?.BackColor ?? Theme.Paper);

        int inset = _pressed ? 1 : 0;
        var rect = new Rectangle(inset, inset, Math.Max(4, Width - 1 - inset * 2), Math.Max(4, Height - 1 - inset * 2));
        int rad = Math.Clamp(CornerRadius, 0, 3);
        Color textColor = Enabled ? ForeColor : Theme.Graphite;
        string displayText = Text;

        switch (_kind)
        {
            case ModernButtonKind.Primary:
                if (Enabled)
                {
                    Gfx.FillAndStrokeRect(g, rect, rad, _hover ? Theme.BrushInkHover : Theme.BrushInk, Theme.PenInk);
                    textColor = Theme.Paper;
                }
                else
                {
                    Gfx.FillAndStrokeRect(g, rect, rad, Theme.BrushPressedPaper, Theme.PenRule);
                    textColor = Theme.Graphite;
                }
                break;

            case ModernButtonKind.AccentPrimary:
                if (Enabled)
                {
                    var fill = _hover ? Theme.BrushVermilionHover : Theme.BrushVermilion;
                    Gfx.FillAndStrokeRect(g, rect, rad, fill, Theme.PenVermilion);
                    textColor = Theme.Paper;
                }
                else
                {
                    Gfx.FillAndStrokeRect(g, rect, rad, Theme.BrushPressedPaper, Theme.PenRule);
                    textColor = Theme.Graphite;
                }
                break;

            case ModernButtonKind.Secondary:
                Gfx.FillAndStrokeRect(
                    g,
                    rect,
                    rad,
                    _hover && Enabled ? Theme.BrushPressedPaper : Theme.BrushSheet,
                    Enabled ? Theme.PenInk : Theme.PenRule);
                textColor = Enabled ? Theme.Ink : Theme.Graphite;
                break;

            case ModernButtonKind.Keycap:
                displayText = Text.ToUpperInvariant();
                var kcFill = _active || _hover ? Theme.BrushPressedPaper : Theme.BrushSheet;
                var kcBorder = _active ? Theme.PenVermilion : (_hover ? Theme.PenInk : Theme.PenRule);
                Gfx.FillAndStrokeRect(g, rect, 2, kcFill, kcBorder);
                textColor = _active ? Theme.Vermilion : (Enabled ? ForeColor : Theme.Graphite);
                break;

            case ModernButtonKind.Chip:
                Gfx.FillAndStrokeRect(
                    g,
                    rect,
                    2,
                    _hover ? Theme.BrushPressedPaper : Theme.BrushSheet,
                    _hover ? Theme.PenInk : Theme.PenRule);
                textColor = _hover ? Theme.Ink : Theme.Graphite;
                break;

            case ModernButtonKind.Ghost:
                var gRect = Rectangle.Inflate(rect, -4, -6);
                if (_active)
                {
                    Gfx.FillAndStrokeRect(g, gRect, 2, Theme.BrushInk, Theme.PenInk);
                    textColor = Theme.Paper;
                }
                else if (_hover)
                {
                    Gfx.FillAndStrokeRect(g, gRect, 2, Theme.BrushPressedPaper, Theme.PenInk);
                    textColor = Theme.Ink;
                }
                else
                {
                    textColor = Theme.Ink;
                }
                break;

            case ModernButtonKind.DangerGhost:
                var dRect = Rectangle.Inflate(rect, -4, -6);
                if (_hover)
                {
                    Gfx.FillAndStrokeRect(g, dRect, 2, Theme.BrushVermilion, Theme.PenVermilion);
                    textColor = Theme.Paper;
                }
                else
                {
                    textColor = Theme.Ink;
                }
                break;
        }

        if (Focused && ShowFocusCues &&
            _kind is ModernButtonKind.Primary or ModernButtonKind.AccentPrimary or ModernButtonKind.Secondary or ModernButtonKind.Keycap)
        {
            var focusRect = Rectangle.Inflate(rect, -2, -2);
            if (focusRect.Width > 2 && focusRect.Height > 2)
            {
                using var fpen = new Pen(_kind is ModernButtonKind.Primary or ModernButtonKind.AccentPrimary ? Theme.Paper : Theme.Ink, 1f)
                {
                    DashStyle = DashStyle.Dot
                };
                Gfx.StrokeRect(g, focusRect, 0, fpen);
            }
        }

        TextRenderer.DrawText(g, displayText, Font, rect, textColor,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis);
    }
}

/// <summary>Flat Crisp Sheet text box with 1px Rule border that sharpens to Rich Ink on focus.</summary>
internal sealed class DarkTextBox : Panel
{
    private readonly TextBox _inner;
    private bool _focused;

    public DarkTextBox()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                 ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        BackColor = Theme.Sheet;
        Padding = new Padding(9, 6, 9, 6);
        Size = new Size(320, 32);

        _inner = new TextBox
        {
            BorderStyle = BorderStyle.None,
            Dock = DockStyle.Fill,
            BackColor = Theme.Sheet,
            ForeColor = Theme.Ink,
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
        Theme.ConfigureGraphics(g);
        g.Clear(Parent?.BackColor ?? Theme.Paper);

        var r = new Rectangle(0, 0, Width - 1, Height - 1);
        Gfx.FillAndStrokeRect(g, r, 2, Theme.BrushSheet, _focused ? Theme.PenInk : Theme.PenRule);
    }
}

/// <summary>Flat Crisp Sheet ledger section container with a 1px solid hairline border.</summary>
internal sealed class SectionCard : Panel
{
    public SectionCard()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                 ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        BackColor = Theme.Sheet;
        Padding = new Padding(14, 10, 14, 12);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        Theme.ConfigureGraphics(g);
        g.Clear(Parent?.BackColor ?? Theme.Paper);

        var r = new Rectangle(0, 0, Width - 1, Height - 1);
        Gfx.FillAndStrokeRect(g, r, 2, Theme.BrushSheet, Theme.PenRule);
    }
}

/// <summary>
/// Printed ledger live-status card showing active Output (+ Comm) and Input devices with
/// crisp vector icons and a one-click square-cornered LIVE / MUTED stamp.
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
        BackColor = Theme.Paper;
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
        int padX = (int)Math.Round(14 * k);
        int padTop = (int)Math.Round(8 * k);
        int padBot = (int)Math.Round(4 * k);
        return new Rectangle(padX, padTop, Math.Max(20, Width - padX * 2 - 1), Math.Max(20, Height - padTop - padBot - 1));
    }

    private Rectangle MicRowRect()
    {
        var c = CardRect();
        int mid = c.Y + c.Height / 2;
        return new Rectangle(c.X + 1, mid + 1, c.Width - 2, c.Bottom - mid - 2);
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
        Theme.ConfigureGraphics(g);
        g.Clear(Parent?.BackColor ?? Theme.Paper);

        float k = Math.Max(1f, DeviceDpi / 96f);
        int P(int v) => (int)Math.Round(v * k);

        var card = CardRect();
        Gfx.FillAndStrokeRect(g, card, 2, Theme.BrushSheet, _micMuted ? Theme.PenVermilion : Theme.PenRule);

        int midY = card.Y + card.Height / 2;

        // Hover wash on the interactive mic row (drawn beneath border & divider)
        var micRow = MicRowRect();
        if (_hoverMicRow)
        {
            g.FillRectangle(Theme.BrushPressedPaper, micRow);
        }

        // Hairline ledger divider between Output and Input
        g.DrawLine(Theme.PenRule, card.X + 1, midY, card.Right - 1, midY);

        // ---- Row 1: Output ----
        int row1H = midY - card.Y;
        var spkRect = new RectangleF(card.X + P(10), card.Y + (row1H - P(14)) / 2f, P(14), P(14));
        Gfx.DrawSpeakerIcon(g, spkRect, Theme.Ink);

        int textLeft = card.X + P(30);
        int outRight = card.Right - P(8);

        if (!string.IsNullOrEmpty(_commShort))
        {
            int tagW = P(42);
            var tagRect = new Rectangle(card.Right - P(8) - tagW, card.Y + (row1H - P(16)) / 2, tagW, P(16));
            Gfx.FillAndStrokeRect(g, tagRect, 1, Theme.BrushPaper, Theme.PenRule);
            TextRenderer.DrawText(g, "COMM", Theme.MonoSmallBold, tagRect, Theme.Graphite,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
            outRight = tagRect.X - P(6);
        }

        var outTextRect = new Rectangle(textLeft, card.Y, Math.Max(P(20), outRight - textLeft), row1H);
        TextRenderer.DrawText(g, _outText, Theme.BodyBold, outTextRect, Theme.Ink,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine);

        // ---- Row 2: Input (click-to-mute) ----
        int row2H = card.Bottom - midY;
        var micIconRect = new RectangleF(card.X + P(10), midY + (row2H - P(14)) / 2f, P(14), P(14));
        Gfx.DrawMicIcon(g, micIconRect, _micMuted ? Theme.Vermilion : Theme.FountainGreen, muted: _micMuted);

        int stampW = _micMuted ? P(56) : P(48);
        var stampRect = new Rectangle(card.Right - P(8) - stampW, midY + (row2H - P(17)) / 2, stampW, P(17));
        if (_micMuted)
        {
            Gfx.FillAndStrokeRect(g, stampRect, 1, Theme.BrushVermilion, Theme.PenVermilion);
            int sq = Math.Max(4, P(4));
            g.FillRectangle(Theme.BrushPaper, stampRect.X + P(5), stampRect.Y + (stampRect.Height - sq) / 2, sq, sq);
            var lblRect = new Rectangle(stampRect.X + P(10), stampRect.Y, stampRect.Width - P(11), stampRect.Height);
            TextRenderer.DrawText(g, "MUTED", Theme.MonoSmallBold, lblRect, Theme.Paper,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
        }
        else
        {
            Gfx.FillAndStrokeRect(g, stampRect, 1, Theme.BrushPaper, Theme.PenFountainGreen);
            int sq = Math.Max(4, P(4));
            g.FillRectangle(Theme.BrushFountainGreen, stampRect.X + P(5), stampRect.Y + (stampRect.Height - sq) / 2, sq, sq);
            var lblRect = new Rectangle(stampRect.X + P(10), stampRect.Y, stampRect.Width - P(11), stampRect.Height);
            TextRenderer.DrawText(g, "LIVE", Theme.MonoSmallBold, lblRect, Theme.FountainGreen,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
        }

        var inTextRect = new Rectangle(textLeft, midY, Math.Max(P(20), stampRect.X - P(8) - textLeft), row2H);
        TextRenderer.DrawText(g, _inText, Theme.Body, inTextRect, _micMuted ? Theme.Vermilion : Theme.Graphite,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine);
    }
}

/// <summary>Editorial E-Ink renderer for system tray and profile card ContextMenuStrips.</summary>
internal sealed class DarkMenuRenderer : ToolStripProfessionalRenderer
{
    public DarkMenuRenderer() : base(new EditorialColorTable()) { }

    protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e)
    {
        e.Graphics.FillRectangle(Theme.BrushPaper, e.AffectedBounds);
    }

    protected override void OnRenderImageMargin(ToolStripRenderEventArgs e)
    {
        e.Graphics.FillRectangle(Theme.BrushPaper, e.AffectedBounds);
    }

    protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e)
    {
        e.Graphics.DrawRectangle(Theme.PenInk, 0, 0, e.AffectedBounds.Width - 1, e.AffectedBounds.Height - 1);
    }

    protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
    {
        var r = new Rectangle(2, 1, e.Item.Width - 4, e.Item.Height - 2);
        if (e.Item.Selected && e.Item.Enabled)
        {
            e.Graphics.FillRectangle(Theme.BrushPressedPaper, r);
            e.Graphics.DrawRectangle(Theme.PenRule, r);
        }
        else
        {
            e.Graphics.FillRectangle(Theme.BrushPaper, e.Item.ContentRectangle);
        }
    }

    protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
    {
        bool isShortcut = e.Item is ToolStripMenuItem mi &&
                          !string.IsNullOrEmpty(mi.ShortcutKeyDisplayString) &&
                          string.Equals(e.Text, mi.ShortcutKeyDisplayString, StringComparison.Ordinal);
        if (!e.Item.Enabled)
        {
            e.TextColor = Theme.Graphite;
        }
        else if (isShortcut)
        {
            e.TextColor = Theme.Graphite;
            e.TextFont = Theme.MonoSmall;
        }
        else if (e.Item.ForeColor != Theme.Ink && e.Item.ForeColor != SystemColors.ControlText)
        {
            e.TextColor = e.Item.ForeColor;
        }
        else
        {
            e.TextColor = Theme.Ink;
        }
        base.OnRenderItemText(e);
    }

    protected override void OnRenderArrow(ToolStripArrowRenderEventArgs e)
    {
        e.ArrowColor = e.Item?.Enabled == true ? Theme.Ink : Theme.Graphite;
        base.OnRenderArrow(e);
    }

    protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e)
    {
        int y = e.Item.Height / 2;
        e.Graphics.DrawLine(Theme.PenRule, 6, y, e.Item.Width - 6, y);
    }

    protected override void OnRenderItemCheck(ToolStripItemImageRenderEventArgs e)
    {
        var r = e.ImageRectangle;
        var sq = new Rectangle(r.X + (r.Width - 6) / 2, r.Y + (r.Height - 6) / 2, 6, 6);
        e.Graphics.FillRectangle(Theme.BrushVermilion, sq);
    }

    private sealed class EditorialColorTable : ProfessionalColorTable
    {
        public override Color ToolStripDropDownBackground => Theme.Paper;
        public override Color MenuBorder => Theme.StrongBorder;
        public override Color MenuItemBorder => Theme.Rule;
        public override Color MenuItemSelected => Theme.PressedPaper;
        public override Color ImageMarginGradientBegin => Theme.Paper;
        public override Color ImageMarginGradientMiddle => Theme.Paper;
        public override Color ImageMarginGradientEnd => Theme.Paper;
    }
}

/// <summary>Tray &amp; window icons rendered in the Editorial E-Ink stamp aesthetic.</summary>
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
            Theme.ConfigureGraphics(g);
            g.Clear(Color.Transparent);
            Gfx.DrawAppEmblem(g, new RectangleF(1f, 1f, 30f, 30f));

            if (muted)
            {
                using var ring = new Pen(Theme.Paper, 2f);
                g.FillRectangle(Theme.BrushVermilion, 18, 18, 12, 12);
                g.DrawRectangle(ring, 18, 18, 12, 12);
            }
        }
        return Icon.FromHandle(bmp.GetHicon());
    }
}

/// <summary>Tactile rectangular E-Ink toggle switch (zero gradients, zero pill rounding).</summary>
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
        Size = new Size(38, 20);
        TabStop = true;

        _animTimer.Tick += (_, _) =>
        {
            float target = _checked ? 1f : 0f;
            float diff = target - _thumbProgress;
            if (Math.Abs(diff) < 0.05f)
            {
                _thumbProgress = target;
                _animTimer.Stop();
            }
            else
            {
                _thumbProgress += diff * 0.42f;
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
        Theme.ConfigureGraphics(g);
        g.Clear(Parent?.BackColor ?? Theme.Sheet);

        var r = new Rectangle(0, 0, Width - 1, Height - 1);
        bool on = _thumbProgress >= 0.5f;

        Gfx.FillAndStrokeRect(
            g,
            r,
            2,
            on ? Theme.BrushInk : Theme.BrushPaper,
            on || Focused ? Theme.PenInk : Theme.PenGraphite);

        int pad = Math.Max(3, Height / 6);
        int size = Math.Max(8, Height - 1 - pad * 2);
        int x = (int)Math.Round(pad + (Width - 1 - pad * 2 - size) * _thumbProgress);
        var knobRect = new Rectangle(x, pad, size, size);

        Gfx.FillRect(g, knobRect, 1, on ? Theme.BrushPaper : Theme.BrushGraphite);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _animTimer.Dispose();
        base.Dispose(disposing);
    }
}

/// <summary>Tactile flat hairline horizontal slider with crisp square ink thumb.</summary>
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

    private int ThumbHalfW => Math.Max(4, Height / 5);

    private void SetFromX(int x)
    {
        int pad = ThumbHalfW + 2;
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
        Theme.ConfigureGraphics(g);
        g.Clear(Parent?.BackColor ?? Theme.Paper);

        int hw = ThumbHalfW;
        int hh = Math.Max(6, Height / 3);
        int pad = hw + 2;
        int trackH = 2;
        int trackY = (Height - trackH) / 2;
        int usable = Math.Max(1, Width - pad * 2);
        double ratio = (_value - _min) / (double)Math.Max(1, _max - _min);
        int thumbX = pad + (int)Math.Round(ratio * usable);

        g.FillRectangle(Theme.BrushRule, pad, trackY, usable, trackH);

        int fillW = thumbX - pad;
        if (fillW > 0)
        {
            g.FillRectangle(Enabled ? Theme.BrushInk : Theme.BrushGraphite, pad, trackY, fillW, trackH);
        }

        var knobRect = new Rectangle(thumbX - hw, Height / 2 - hh, hw * 2, hh * 2);
        var knobFill = !Enabled
            ? Theme.BrushPressedPaper
            : (_dragging ? Theme.BrushVermilion : (_hover ? Theme.BrushPressedPaper : Theme.BrushSheet));
        var knobPen = !Enabled
            ? Theme.PenRule
            : (_dragging ? Theme.PenVermilion : Theme.PenInk);
        Gfx.FillAndStrokeRect(g, knobRect, 1, knobFill, knobPen);
    }
}

/// <summary>Crisp Editorial E-Ink checklist with square ink checkboxes, wheel scroll, and keyboard navigation.</summary>
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
        BackColor = Theme.Sheet;
        ForeColor = Theme.Ink;
        Font = Theme.Body;
        Cursor = Cursors.Hand;
        TabStop = true;
    }

    private float ScaleFactor => Math.Max(1f, DeviceDpi / 96f);
    private int RowHeight => (int)Math.Round(28 * ScaleFactor);

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
        Theme.ConfigureGraphics(g);
        g.Clear(Parent?.BackColor ?? Theme.Paper);

        float k = ScaleFactor;
        var outer = new Rectangle(0, 0, Width - 1, Height - 1);
        Gfx.FillRect(g, outer, 2, Theme.BrushSheet);

        var clipState = g.Save();
        g.SetClip(new Rectangle(2, 2, Width - 4, Height - 4));

        if (_entries.Count == 0)
        {
            TextRenderer.DrawText(g, "NO DEVICES AVAILABLE", Theme.MonoSmall,
                new Rectangle(8, 3, Width - 16, Height - 6), Theme.Graphite,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
        }
        else
        {
            int rowH = RowHeight;
            int boxSize = (int)Math.Round(14 * k);
            int padLeft = (int)Math.Round(10 * k);
            int scrollBarSpace = MaxScroll > 0 ? (int)Math.Round(8 * k) : 0;

            for (int i = 0; i < _entries.Count; i++)
            {
                int y = 3 + i * rowH - _scrollOffset;
                if (y + rowH < 3 || y > Height - 3) continue;

                var rowRect = new Rectangle(3, y + 1, Width - 6 - scrollBarSpace, rowH - 2);
                if (i == _hoverIndex || (Focused && i == _focusIndex))
                {
                    g.FillRectangle(Theme.BrushPressedPaper, rowRect);
                }

                var entry = _entries[i];
                var boxRect = new Rectangle(padLeft, y + (rowH - boxSize) / 2, boxSize, boxSize);
                if (entry.Checked)
                {
                    Gfx.FillAndStrokeRect(g, boxRect, 1, Theme.BrushInk, Theme.PenInk);
                    using var checkPen = new Pen(Theme.Paper, Math.Max(1.5f, 1.6f * k))
                    {
                        StartCap = LineCap.Square,
                        EndCap = LineCap.Square,
                        LineJoin = LineJoin.Miter
                    };
                    var p1 = new PointF(boxRect.X + boxSize * 0.22f, boxRect.Y + boxSize * 0.54f);
                    var p2 = new PointF(boxRect.X + boxSize * 0.44f, boxRect.Y + boxSize * 0.74f);
                    var p3 = new PointF(boxRect.X + boxSize * 0.80f, boxRect.Y + boxSize * 0.28f);
                    g.DrawLines(checkPen, new[] { p1, p2, p3 });
                }
                else
                {
                    Gfx.FillAndStrokeRect(g, boxRect, 1, Theme.BrushSheet, Theme.PenGraphite);
                }

                int textX = boxRect.Right + (int)Math.Round(9 * k);
                var textRect = new Rectangle(textX, y, Math.Max(20, rowRect.Right - textX - 6), rowH);
                var textColor = !entry.Item.Connected
                    ? Theme.Vermilion
                    : (entry.Checked ? Theme.Ink : Theme.Graphite);
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
                var thumbRect = new Rectangle(Width - (int)Math.Round(6 * k), thumbY, Math.Max(3, (int)Math.Round(3 * k)), thumbH);
                g.FillRectangle(Theme.BrushGraphite, thumbRect);
            }
        }

        g.Restore(clipState);
        Gfx.StrokeRect(g, outer, 2, Focused ? Theme.PenInk : Theme.PenRule);
    }
}

/// <summary>Flat Editorial E-Ink owner-drawn combo box with crisp ink chevron and Pressed Paper highlight.</summary>
internal sealed class DarkCombo : ComboBox
{
    private const int WM_PAINT = 0x000F;
    private bool _hover;

    public DarkCombo()
    {
        DrawMode = DrawMode.OwnerDrawFixed;
        DropDownStyle = ComboBoxStyle.DropDownList;
        FlatStyle = FlatStyle.Flat;
        BackColor = Theme.Sheet;
        ForeColor = Theme.Ink;
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

        e.Graphics.FillRectangle(highlighted ? Theme.BrushPressedPaper : Theme.BrushSheet, e.Bounds);

        var text = Items[e.Index]?.ToString() ?? "";
        TextRenderer.DrawText(e.Graphics, text, Font, Rectangle.Inflate(e.Bounds, -8, 0), Theme.Ink,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
    }

    protected override void WndProc(ref Message m)
    {
        base.WndProc(ref m);
        if (m.Msg != WM_PAINT || Width <= 4 || Height <= 4) return;

        using var g = Graphics.FromHwnd(Handle);
        Theme.ConfigureGraphics(g);

        float k = Math.Max(1f, DeviceDpi / 96f);
        int arrowAreaW = (int)Math.Round(24 * k);

        var arrowRect = new Rectangle(Width - arrowAreaW, 1, arrowAreaW - 1, Height - 2);
        g.FillRectangle(_hover ? Theme.BrushPressedPaper : Theme.BrushSheet, arrowRect);

        var outer = new Rectangle(0, 0, Width - 1, Height - 1);
        using (var cover = new Pen(Theme.Sheet, 2f))
            g.DrawRectangle(cover, 1, 1, Width - 3, Height - 3);
        g.DrawRectangle(Focused || _hover ? Theme.PenInk : Theme.PenRule, outer);

        float cx = arrowRect.X + arrowRect.Width * 0.5f;
        float cy = arrowRect.Y + arrowRect.Height * 0.52f;
        float sz = 3.4f * k;
        using var chev = new Pen(_hover || Focused ? Theme.Ink : Theme.Graphite, Math.Max(1.3f, 1.35f * k))
        {
            StartCap = LineCap.Flat,
            EndCap = LineCap.Flat,
            LineJoin = LineJoin.Miter
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
/// Editorial E-Ink profile ledger card: flat #FFFFFF sheet, 1px hairline border (#1A1A18 on hover/active),
/// Pressed Paper hover fill, 3px Stamp Vermilion left bar and uppercase monospace ACTIVE stamp when live,
/// Georgia serif title, Graphite Segoe UI device sublabels, and bordered monospace hotkey keycap badge.
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
        BackColor = Theme.Paper;

        _confirmTimer.Tick += (_, _) => { _confirmTimer.Stop(); _confirmDelete = false; Invalidate(); };

        _contextMenu.Renderer = new DarkMenuRenderer();
        _contextMenu.BackColor = Theme.Paper;
        _contextMenu.ForeColor = Theme.Ink;
        _contextMenu.Font = Theme.Body;
        _contextMenu.Opening += (_, _) => PopulateContextMenu();
        ContextMenuStrip = _contextMenu;
    }

    public void BindCard(AudioCard card)
    {
        Card = card;
        Invalidate();
    }

    private bool HasMetaRow => !string.IsNullOrEmpty(_slotText) || Card.AutoActivate || _isFallback;

    public int PreferredHeight(float scale)
    {
        int baseH = Card.CommOutputId.HasValue ? 100 : 80;
        if (HasMetaRow) baseH += 24;
        return (int)Math.Round(baseH * scale);
    }

    public bool IsActive { get => _isActive; set => Upd(ref _isActive, value); }
    public bool IsFallback { get => _isFallback; set => Upd(ref _isFallback, value); }
    public bool OutMissing { get => _outMissing; set => Upd(ref _outMissing, value); }
    public bool CommMissing { get => _commMissing; set => Upd(ref _commMissing, value); }
    public bool InMissing { get => _inMissing; set => Upd(ref _inMissing, value); }

    /// <summary>Assigned hotkey badge text (e.g. "CTRL+SHIFT+1"), rendered in a bordered monospace keycap.</summary>
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
        var delItem = new ToolStripMenuItem("Delete") { ForeColor = Theme.Vermilion };
        delItem.Click += (_, _) =>
        {
            _confirmTimer.Stop();
            _confirmDelete = false;
            DeleteRequested?.Invoke(Card);
        };
        items.Add(delItem);
    }

    private int P(int v) => (int)Math.Round(v * Scale);
    private bool ButtonsVisible => _hover || _confirmDelete || (Focused && _keyboardFocus);

    private Rectangle DelRect()
    {
        int w = _confirmDelete ? P(66) : P(24);
        return new Rectangle(Width - P(10) - w, P(9), w, P(22));
    }

    private Rectangle EditRect()
    {
        var d = DelRect();
        return new Rectangle(d.X - P(4) - P(44), P(9), P(44), P(22));
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
        Theme.ConfigureGraphics(g);
        g.Clear(Parent?.BackColor ?? Theme.Paper);

        var r = new Rectangle(0, 0, Width - 1, Height - 1);
        var fill = _hover ? Theme.BrushPressedPaper : Theme.BrushSheet;
        var borderPen = (_isActive || _hover || (_keyboardFocus && Focused)) ? Theme.PenInk : Theme.PenRule;
        Gfx.FillRect(g, r, 2, fill);

        // Active card: crisp 3px vertical Stamp Vermilion left accent strip flush inside the card edge
        if (_isActive)
        {
            int barW = Math.Max(3, P(3));
            g.FillRectangle(Theme.BrushVermilion, 1, 1, barW, Math.Max(2, Height - 2));
        }

        Gfx.StrokeRect(g, r, 2, borderPen);

        int left = P(14);
        bool showButtons = ButtonsVisible;
        int rightBadgeSpace = showButtons
            ? Width - EditRect().X + P(6)
            : (_isActive ? P(74) : P(12));

        // ---- Header Row: Georgia Serif Profile Heading ----
        int titleMaxW = Math.Max(P(40), Width - left - rightBadgeSpace);
        var titleRect = new Rectangle(left, P(9), titleMaxW, P(22));
        TextRenderer.DrawText(g, Card.Name, Theme.CardTitle, titleRect, Theme.Ink, TextFlags);

        // ---- Device Rows: Monospace tag + Graphite Segoe UI device name + optional volume ----
        DrawDeviceRow(g, "OUT", Card.OutputId.HasValue ? Card.OutputName : null, Card.OutputVolume, _outMissing, left, P(36));

        int nextY;
        if (Card.CommOutputId.HasValue)
        {
            DrawDeviceRow(g, "COMM", Card.CommOutputName, null, _commMissing, left, P(56));
            DrawDeviceRow(g, "IN", Card.InputId.HasValue ? Card.InputName : null, Card.InputVolume, _inMissing, left, P(76));
            nextY = P(98);
        }
        else
        {
            DrawDeviceRow(g, "IN", Card.InputId.HasValue ? Card.InputName : null, Card.InputVolume, _inMissing, left, P(56));
            nextY = P(78);
        }

        // ---- Bottom Meta Strip: Bordered Monospace Keycap Hotkey + AUTO / FALLBACK stamps ----
        if (HasMetaRow)
        {
            int metaX = left;
            int badgeH = P(18);
            int metaY = nextY;

            if (!string.IsNullOrEmpty(_slotText))
            {
                string keyLabel = $"[ {_slotText.ToUpperInvariant()} ]";
                var measured = TextRenderer.MeasureText(g, keyLabel, Theme.MonoSmallBold);
                int kw = Math.Min(Width - left - P(12), measured.Width + P(10));
                var keyRect = new Rectangle(metaX, metaY, kw, badgeH);
                Gfx.FillAndStrokeRect(
                    g,
                    keyRect,
                    1,
                    _hover ? Theme.BrushSheet : Theme.BrushPaper,
                    _isActive ? Theme.PenInk : Theme.PenRule);
                TextRenderer.DrawText(g, keyLabel, Theme.MonoSmallBold, keyRect,
                    _isActive ? Theme.Ink : Theme.Graphite,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
                metaX = keyRect.Right + P(6);
            }

            if (Card.AutoActivate)
            {
                int autoW = P(46);
                var autoRect = new Rectangle(metaX, metaY, autoW, badgeH);
                if (autoRect.Right <= Width - P(10))
                {
                    Gfx.FillAndStrokeRect(g, autoRect, 1, _hover ? Theme.BrushSheet : Theme.BrushPaper, Theme.PenRule);
                    int sq = Math.Max(4, P(4));
                    g.FillRectangle(Theme.BrushVermilion, autoRect.X + P(5), autoRect.Y + (autoRect.Height - sq) / 2, sq, sq);
                    var txtR = new Rectangle(autoRect.X + P(10), autoRect.Y, autoRect.Width - P(11), autoRect.Height);
                    TextRenderer.DrawText(g, "AUTO", Theme.MonoSmallBold, txtR, Theme.Graphite,
                        TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
                    metaX = autoRect.Right + P(6);
                }
            }

            if (_isFallback)
            {
                int fbW = P(64);
                var fbRect = new Rectangle(metaX, metaY, fbW, badgeH);
                if (fbRect.Right <= Width - P(10))
                {
                    Gfx.FillAndStrokeRect(g, fbRect, 1, _hover ? Theme.BrushSheet : Theme.BrushPaper, Theme.PenRule);
                    TextRenderer.DrawText(g, "FALLBACK", Theme.MonoSmallBold, fbRect, Theme.Graphite,
                        TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
                }
            }
        }

        // ---- Top-Right Stamp / Hover Controls ----
        if (showButtons)
        {
            DrawEditButton(g, EditRect());
            DrawDeleteButton(g, DelRect());
        }
        else if (_isActive)
        {
            var stamp = new Rectangle(Width - P(10) - P(60), P(10), P(60), P(19));
            Gfx.FillAndStrokeRect(g, stamp, 1, Theme.BrushPaper, Theme.PenFountainGreen);
            int sq = Math.Max(4, P(4));
            g.FillRectangle(Theme.BrushFountainGreen, stamp.X + P(5), stamp.Y + (stamp.Height - sq) / 2, sq, sq);
            var textR = new Rectangle(stamp.X + P(10), stamp.Y, stamp.Width - P(11), stamp.Height);
            TextRenderer.DrawText(g, "ACTIVE", Theme.MonoSmallBold, textR, Theme.FountainGreen,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
        }
    }

    private void DrawDeviceRow(Graphics g, string tag, string? name, int? volume, bool missing, int left, int y)
    {
        int tagW = P(34);
        int rowH = P(18);
        var tagRect = new Rectangle(left, y, tagW, rowH);
        TextRenderer.DrawText(g, tag, Theme.MonoSmallBold, tagRect, Theme.Graphite,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);

        int rightLimit = Width - P(10);
        if (volume.HasValue)
        {
            int volW = P(34);
            var volRect = new Rectangle(rightLimit - volW, y + P(1), volW, P(16));
            Gfx.FillAndStrokeRect(g, volRect, 1, _hover ? Theme.BrushSheet : Theme.BrushPaper, Theme.PenRule);
            TextRenderer.DrawText(g, $"{volume.Value}%", Theme.MonoSmallBold, volRect, Theme.Ink,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
            rightLimit = volRect.X - P(6);
        }

        string text = name ?? "Not changed";
        if (missing) text += " (offline)";
        var color = missing ? Theme.Vermilion : Theme.Graphite;

        int x = left + tagW + P(4);
        TextRenderer.DrawText(g, text, Theme.Small,
            new Rectangle(x, y, Math.Max(P(20), rightLimit - x), rowH), color, TextFlags);
    }

    private void DrawEditButton(Graphics g, Rectangle rect)
    {
        Gfx.FillAndStrokeRect(
            g,
            rect,
            1,
            _hoverEdit ? Theme.BrushInk : Theme.BrushSheet,
            Theme.PenInk);
        TextRenderer.DrawText(g, "EDIT", Theme.MonoSmallBold, rect, _hoverEdit ? Theme.Paper : Theme.Ink,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
    }

    private void DrawDeleteButton(Graphics g, Rectangle rect)
    {
        bool vermilionSolid = _confirmDelete || _hoverDel;
        Gfx.FillAndStrokeRect(
            g,
            rect,
            1,
            vermilionSolid ? Theme.BrushVermilion : Theme.BrushSheet,
            vermilionSolid ? Theme.PenVermilion : Theme.PenInk);

        string text = _confirmDelete ? "DELETE?" : "×";
        var color = vermilionSolid ? Theme.Paper : Theme.Ink;
        TextRenderer.DrawText(g, text, _confirmDelete ? Theme.MonoSmallBold : Theme.BodyBold, rect, color,
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

/// <summary>Always-on-top, never-activating PerMonitorV2-aware crisp printed-slip overlay window.</summary>
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

    protected Font CreateDpiFont(string family, float ptAt96Dpi, FontStyle style)
    {
        float pxSize = Math.Max(8f, ptAt96Dpi * (96f / 72f) * Dpi);
        var f = new Font(family, pxSize, style, GraphicsUnit.Pixel);
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
        Native.CrispCorners(Handle);
        Native.SetBorderColor(Handle, Theme.StrongBorder);
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
            return cp;
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) ClearOwnedFonts();
        base.Dispose(disposing);
    }
}

/// <summary>
/// Compact Editorial E-Ink printed-slip OSD toast: #F7F5F0 paper background, 1px solid #1A1A18 border,
/// 5×5px #C83E2B square accent, Georgia serif profile heading, and monospace output/input summary.
/// </summary>
internal sealed class OsdBannerForm : OverlayForm
{
    private const double TargetOpacity = 0.98;

    private readonly System.Windows.Forms.Timer _holdTimer = new() { Interval = 1700 };
    private readonly System.Windows.Forms.Timer _fadeTimer = new() { Interval = 16 };
    private Font _catFont = Theme.MonoSmallBold;
    private Font _titleFont = Theme.CardTitle;
    private Font _subFont = Theme.MonoSmall;
    private string _category = "";
    private string _title = "";
    private string? _subtitle;
    private Color _accent = Theme.Vermilion;
    private Rectangle? _targetArea;
    private bool _fadingOut;

    public OsdBannerForm()
    {
        BackColor = Theme.Paper;
        Opacity = 0;

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
                double next = Math.Min(TargetOpacity, Opacity + 0.16);
                Opacity = next;
                if (next >= TargetOpacity - 0.005) _fadeTimer.Stop();
            }
            else
            {
                double next = Math.Max(0.0, Opacity - 0.12);
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
        ClearOwnedFonts();
        _catFont = CreateDpiFont(Theme.MonospaceFontName, 7.5f, FontStyle.Bold);
        _titleFont = CreateDpiFont("Georgia", 10.5f, FontStyle.Bold);
        _subFont = CreateDpiFont(Theme.MonospaceFontName, 7.5f, FontStyle.Regular);

        int height = string.IsNullOrWhiteSpace(_subtitle) ? Px(62) : Px(78);
        Size = new Size(Px(352), height);

        if (_targetArea is { } area)
        {
            Location = new Point(area.Left + (area.Width - Width) / 2, area.Bottom - Height - Px(38));
        }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        Theme.ConfigureGraphics(g);

        g.FillRectangle(Theme.BrushPaper, 0, 0, Width, Height);
        g.DrawRectangle(Theme.PenInk, 0, 0, Width - 1, Height - 1);

        int padX = Px(14);
        int sq = Math.Max(5, Px(5));
        int catY = Px(10);

        // 5×5px Stamp Vermilion (or status accent) editorial square
        using (var sqBrush = new SolidBrush(_accent))
            g.FillRectangle(sqBrush, padX, catY + Px(5), sq, sq);

        var catRect = new Rectangle(padX + sq + Px(7), catY, Width - padX * 2 - sq - Px(7), Px(15));
        TextRenderer.DrawText(g, _category.ToUpperInvariant(), _catFont, catRect, Theme.Graphite,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine);

        var titleRect = new Rectangle(padX, Px(28), Width - padX * 2, Px(22));
        TextRenderer.DrawText(g, _title, _titleFont, titleRect, Theme.Ink,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine);

        if (!string.IsNullOrWhiteSpace(_subtitle))
        {
            g.DrawLine(Theme.PenRule, padX, Px(54), Width - padX, Px(54));
            var subRect = new Rectangle(padX, Px(57), Width - padX * 2, Px(15));
            TextRenderer.DrawText(g, _subtitle, _subFont, subRect, Theme.Graphite,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine);
        }
    }

    public void ShowMessage(string category, string message, Color accentColor, string? summary = null)
    {
        Screen? activeScreen = null;
        try { activeScreen = Screen.FromPoint(Cursor.Position); } catch { }
        var area = TryGetWorkingArea(activeScreen);
        if (area == null) return;

        _accent = accentColor;
        _category = category;
        _title = message;
        _subtitle = summary;
        _targetArea = area.Value;

        ApplyDpiLayout();
        Location = new Point(area.Value.Left + (area.Value.Width - Width) / 2, area.Value.Bottom - Height - Px(38));
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

/// <summary>Compact Editorial E-Ink MIC MUTED printed stamp slip with optional drag-to-move and click-to-unmute.</summary>
internal sealed class MuteBannerForm : OverlayForm
{
    private Font _labelFont = Theme.MonoBold;
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
        BackColor = Theme.Paper;
        Opacity = 0.98;

        MouseEnter += (_, _) => { if (Interactive) { _hover = true; Invalidate(); } };
        MouseLeave += (_, _) => { if (_hover) { _hover = false; Invalidate(); } };
        MouseDown += OnInteractiveMouseDown;
        MouseMove += OnInteractiveMouseMove;
        MouseUp += OnInteractiveMouseUp;

        ApplyDpiLayout();
    }

    public bool Interactive
    {
        get => !ClickThrough;
        set
        {
            ClickThrough = !value;
            Cursor = value ? Cursors.Hand : Cursors.Default;
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
        Capture = true;
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
            Cursor = Cursors.SizeAll;
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
        Capture = false;
        Cursor = Interactive ? Cursors.Hand : Cursors.Default;

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
        ClearOwnedFonts();
        _labelFont = CreateDpiFont(Theme.MonospaceFontName, 8.25f, FontStyle.Bold);

        Size = new Size(Px(124), Px(30));
        if (_targetArea is { } area && !_dragged)
        {
            Location = ProfileLogic.ComputeMuteBannerLocation(area, Size, _position, Dpi, _customX, _customY);
        }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        Theme.ConfigureGraphics(g);

        g.FillRectangle(Interactive && _hover ? Theme.BrushPressedPaper : Theme.BrushPaper, 0, 0, Width, Height);
        g.FillRectangle(Theme.BrushVermilion, 1, 1, Math.Max(3, Px(3)), Height - 2);
        g.DrawRectangle(Theme.PenInk, 0, 0, Width - 1, Height - 1);

        int sq = Math.Max(5, Px(5));
        int sqX = Px(13);
        int sqY = (Height - sq) / 2;
        g.FillRectangle(Theme.BrushVermilion, sqX, sqY, sq, sq);

        var textRect = new Rectangle(sqX + sq + Px(6), 0, Width - (sqX + sq + Px(12)), Height);
        TextRenderer.DrawText(g, "MIC MUTED", _labelFont, textRect, Theme.Vermilion,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine);
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

/// <summary>Crisp flat Editorial E-Ink progress bar (zero gradients, solid ink fill).</summary>
internal sealed class DarkProgressBar : Control
{
    private readonly System.Windows.Forms.Timer _animTimer = new() { Interval = 28 };
    private int _value;
    private bool _indeterminate;
    private float _pulsePos = -0.30f;

    public DarkProgressBar()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                 ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        Size = new Size(340, 8);
        BackColor = Theme.Paper;
        _animTimer.Tick += (_, _) =>
        {
            _pulsePos += 0.035f;
            if (_pulsePos > 1.05f) _pulsePos = -0.30f;
            Invalidate();
        };
    }

    public int Value
    {
        get => _value;
        set
        {
            int clamped = Math.Clamp(value, 0, 100);
            if (_value != clamped)
            {
                _value = clamped;
                Invalidate();
            }
        }
    }

    public bool Indeterminate
    {
        get => _indeterminate;
        set
        {
            if (_indeterminate == value) return;
            _indeterminate = value;
            if (_indeterminate)
            {
                _pulsePos = -0.30f;
                _animTimer.Start();
            }
            else
            {
                _animTimer.Stop();
            }
            Invalidate();
        }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        Theme.ConfigureGraphics(g);
        g.Clear(Parent?.BackColor ?? Theme.Paper);

        var r = new Rectangle(0, 0, Width - 1, Height - 1);
        if (r.Width <= 2 || r.Height <= 2) return;

        g.FillRectangle(Theme.BrushSheet, r);

        var inner = new Rectangle(1, 1, Math.Max(1, r.Width - 1), Math.Max(1, r.Height - 1));
        g.SetClip(inner);
        if (_indeterminate)
        {
            int segW = Math.Max(22, (int)(inner.Width * 0.28f));
            int segX = inner.X + (int)(_pulsePos * inner.Width);
            g.FillRectangle(Theme.BrushInk, segX, inner.Y, segW, inner.Height);
        }
        else if (_value > 0)
        {
            int fillW = (int)Math.Round(inner.Width * (_value / 100.0));
            if (fillW > 0) g.FillRectangle(Theme.BrushInk, inner.X, inner.Y, fillW, inner.Height);
        }
        g.ResetClip();

        g.DrawRectangle(Theme.PenInk, r);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _animTimer.Stop();
            _animTimer.Dispose();
        }
        base.Dispose(disposing);
    }
}