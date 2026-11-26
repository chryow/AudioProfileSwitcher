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

    // Cached fonts: created once, shared by standard DPI-autoscaled controls.
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

/// <summary>Custom dark horizontal 0..100% slider with mouse drag, wheel, and keyboard support.</summary>
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
        Size = new Size(180, 22);
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

    private int ThumbRadius => Math.Max(5, Height / 3);

    private void SetFromX(int x)
    {
        int pad = ThumbRadius + 2;
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
        int pad = r + 2;
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
        if (fillW > 1)
        {
            var fgTrack = new Rectangle(pad, trackY, fillW, trackH);
            using var path = Gfx.RoundRect(fgTrack, trackH / 2);
            using var b = new SolidBrush(Enabled ? Theme.Accent : Theme.TextMuted);
            g.FillPath(b, path);
        }

        var knobRect = new Rectangle(thumbX - r, Height / 2 - r, r * 2, r * 2);
        using (var knob = new SolidBrush(Enabled ? Color.White : Theme.TextMuted))
            g.FillEllipse(knob, knobRect);

        if (Enabled && (_hover || _dragging || (Focused && ShowFocusCues)))
        {
            using var ring = new Pen(Theme.Accent, 1.5f);
            g.DrawEllipse(ring, knobRect);
        }
    }
}

/// <summary>Custom-painted dark checklist with rounded accent checkboxes, wheel scroll, and keyboard navigation.</summary>
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

    private int MaxScroll => Math.Max(0, _entries.Count * RowHeight - (Height - 4));

    private void ClampScroll() => _scrollOffset = Math.Clamp(_scrollOffset, 0, MaxScroll);

    private int IndexAtY(int y)
    {
        if (_entries.Count == 0 || y < 2 || y >= Height - 2) return -1;
        int idx = (y - 2 + _scrollOffset) / RowHeight;
        return idx >= 0 && idx < _entries.Count ? idx : -1;
    }

    private void EnsureVisible(int index)
    {
        if (index < 0 || index >= _entries.Count) return;
        int top = index * RowHeight;
        int bottom = top + RowHeight;
        int viewH = Math.Max(RowHeight, Height - 4);
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
        using var outerPath = Gfx.RoundRect(outer, (int)Math.Round(6 * k));
        using (var bg = new SolidBrush(Theme.BgCard)) g.FillPath(bg, outerPath);

        var clipState = g.Save();
        g.SetClip(new Rectangle(2, 2, Width - 4, Height - 4));

        if (_entries.Count == 0)
        {
            TextRenderer.DrawText(g, "No devices available", Theme.Small,
                new Rectangle(8, 2, Width - 16, Height - 4), Theme.TextMuted,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
        }
        else
        {
            int rowH = RowHeight;
            int boxSize = (int)Math.Round(15 * k);
            int padLeft = (int)Math.Round(10 * k);
            int scrollBarSpace = MaxScroll > 0 ? (int)Math.Round(8 * k) : 0;

            for (int i = 0; i < _entries.Count; i++)
            {
                int y = 2 + i * rowH - _scrollOffset;
                if (y + rowH < 2 || y > Height - 2) continue;

                var rowRect = new Rectangle(3, y, Width - 6 - scrollBarSpace, rowH);
                if (i == _hoverIndex || (Focused && i == _focusIndex))
                {
                    using var hPath = Gfx.RoundRect(rowRect, (int)Math.Round(4 * k));
                    using var hb = new SolidBrush(Theme.BgCardHover);
                    g.FillPath(hb, hPath);
                }

                var entry = _entries[i];
                var boxRect = new Rectangle(padLeft, y + (rowH - boxSize) / 2, boxSize, boxSize);
                using (var boxPath = Gfx.RoundRect(boxRect, (int)Math.Round(4 * k)))
                {
                    if (entry.Checked)
                    {
                        using var fill = new SolidBrush(Theme.Accent);
                        g.FillPath(fill, boxPath);
                        using var checkPen = new Pen(Color.White, Math.Max(1.6f, 1.6f * k))
                        {
                            StartCap = LineCap.Round,
                            EndCap = LineCap.Round,
                            LineJoin = LineJoin.Round
                        };
                        var p1 = new PointF(boxRect.X + boxSize * 0.24f, boxRect.Y + boxSize * 0.52f);
                        var p2 = new PointF(boxRect.X + boxSize * 0.44f, boxRect.Y + boxSize * 0.72f);
                        var p3 = new PointF(boxRect.X + boxSize * 0.78f, boxRect.Y + boxSize * 0.30f);
                        g.DrawLines(checkPen, new[] { p1, p2, p3 });
                    }
                    else
                    {
                        using var pen = new Pen(Theme.BgBorder, 1.2f);
                        g.DrawPath(pen, boxPath);
                    }
                }

                int textX = boxRect.Right + (int)Math.Round(9 * k);
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
                int viewH = Height - 8;
                int totalH = _entries.Count * rowH;
                int thumbH = Math.Max((int)Math.Round(18 * k), (int)Math.Round(viewH * (viewH / (double)totalH)));
                int thumbY = 4 + (int)Math.Round((viewH - thumbH) * (_scrollOffset / (double)MaxScroll));
                var thumbRect = new Rectangle(Width - (int)Math.Round(6 * k), thumbY, (int)Math.Round(3 * k), thumbH);
                using var sb = new SolidBrush(Theme.BgBorder);
                using var sp = Gfx.RoundRect(thumbRect, 2);
                g.FillPath(sb, sp);
            }
        }

        g.Restore(clipState);

        using var borderPen = new Pen(Focused ? Theme.Accent : Theme.BgBorder, 1);
        g.DrawPath(borderPen, outerPath);
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
/// One audio card: custom painted, diff-reusable across renders, with hover/focus states,
/// keyboard support, right-click dark context menu, inline Edit/Delete, and drag-to-reorder.
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
        Card.CommOutputId.HasValue ? (int)Math.Round(97 * scale) : (int)Math.Round(78 * scale);

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
        int badgeSpace = _isActive ? P(58) : (_isFallback ? P(40) : 0);
        int nameRight = showButtons ? EditRect().X - P(6) : Width - P(12) - badgeSpace;
        TextRenderer.DrawText(g, Card.Name, Theme.Title,
            new Rectangle(left, P(8), Math.Max(P(40), nameRight - left), P(24)), Theme.TextPrimary, TextFlags);

        DrawDeviceRow(g, "OUT", Card.OutputId.HasValue ? Card.OutputName : null, Card.OutputVolume, _outMissing, left, P(36));

        if (Card.CommOutputId.HasValue)
        {
            DrawDeviceRow(g, "COMM", Card.CommOutputName, null, _commMissing, left, P(55));
            DrawDeviceRow(g, "IN", Card.InputId.HasValue ? Card.InputName : null, Card.InputVolume, _inMissing, left, P(74));
        }
        else
        {
            DrawDeviceRow(g, "IN", Card.InputId.HasValue ? Card.InputName : null, Card.InputVolume, _inMissing, left, P(55));
        }

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
        else if (_isFallback)
        {
            var pill = new Rectangle(Width - P(10) - P(30), P(11), P(30), P(18));
            using var pp = Gfx.RoundRect(pill, P(9));
            using (var fill = new SolidBrush(Theme.BgBorder)) g.FillPath(fill, pp);
            TextRenderer.DrawText(g, "FB", Theme.SmallBold, pill, Theme.TextSoft,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
        }
    }

    private void DrawDeviceRow(Graphics g, string tag, string? name, int? volume, bool missing, int left, int y)
    {
        int tagW = P(38);
        TextRenderer.DrawText(g, tag, Theme.SmallBold, new Rectangle(left, y, tagW, P(18)), Theme.TextMuted, TextFlags);

        string text = name ?? "Not changed";
        if (volume.HasValue) text += $"  · {volume.Value}%";
        if (missing) text += "  (not connected)";
        var color = missing ? Theme.Warning : (name == null && !volume.HasValue ? Theme.TextMuted : Theme.TextSoft);

        int x = left + tagW;
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
        if (disposing)
        {
            _confirmTimer.Dispose();
            _contextMenu.Dispose();
        }
        base.Dispose(disposing);
    }
}

/// <summary>Always-on-top, never-activating PerMonitorV2-aware overlay window (click-through by default).</summary>
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

internal sealed class OsdBannerForm : OverlayForm
{
    private readonly System.Windows.Forms.Timer _timer = new() { Interval = 1600 };
    private readonly Label _catLbl = new();
    private readonly Label _msgLbl = new() { AutoEllipsis = true };
    private Color _accent = Theme.Accent;
    private Rectangle? _targetArea;

    public OsdBannerForm()
    {
        BackColor = Color.FromArgb(20, 22, 27);
        Opacity = 0.94;

        _catLbl.ForeColor = Theme.TextMuted;
        _msgLbl.ForeColor = Theme.TextPrimary;
        Controls.AddRange(new Control[] { _catLbl, _msgLbl });

        ApplyDpiLayout();
        _timer.Tick += (_, _) => { _timer.Stop(); Hide(); };
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

        Size = new Size(Px(340), Px(60));
        _catLbl.Location = new Point(Px(16), Px(10));
        _catLbl.Size = new Size(Px(308), Px(16));
        _msgLbl.Location = new Point(Px(16), Px(28));
        _msgLbl.Size = new Size(Px(308), Px(24));

        if (_targetArea is { } area)
        {
            Location = new Point(area.Left + (area.Width - Width) / 2, area.Bottom - Height - Px(36));
        }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        using var border = new Pen(Theme.BgBorder, 1);
        e.Graphics.DrawRectangle(border, 0, 0, Width - 1, Height - 1);
        using var bar = new SolidBrush(_accent);
        e.Graphics.FillRectangle(bar, 0, 0, Math.Max(2, Px(4)), Height);
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

        Location = new Point(area.Value.Left + (area.Value.Width - Width) / 2, area.Value.Bottom - Height - Px(36));
        ApplyDpiLayout();
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
        BackColor = Color.FromArgb(32, 18, 20);
        Opacity = 0.92;

        _label = new Label
        {
            Text = "● MIC MUTED",
            ForeColor = Color.FromArgb(245, 90, 93),
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

        Size = new Size(Px(128), Px(30));
        if (_targetArea is { } area && !_dragged)
        {
            Location = ProfileLogic.ComputeMuteBannerLocation(area, Size, _position, Dpi, _customX, _customY);
        }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        var borderColor = Interactive && _hover ? Color.FromArgb(255, 130, 132) : Theme.Danger;
        using var border = new Pen(borderColor, 1);
        e.Graphics.DrawRectangle(border, 0, 0, Width - 1, Height - 1);
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