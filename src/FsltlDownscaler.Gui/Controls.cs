using System.ComponentModel;
using System.Drawing.Drawing2D;

namespace FsltlDownscaler.Gui;

internal static class Shapes
{
    public static GraphicsPath Rounded(Rectangle r, int radius)
    {
        int d = Math.Max(1, Math.Min(radius * 2, Math.Min(r.Width, r.Height)));
        var p = new GraphicsPath();
        p.AddArc(r.X, r.Y, d, d, 180, 90);
        p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        p.CloseFigure();
        return p;
    }
}

/// <summary>A layout panel that paints itself as a rounded card.</summary>
internal sealed class CardPanel : TableLayoutPanel
{
    public CardPanel()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        Dock = DockStyle.Fill;
        Padding = new Padding(14, 12, 14, 12);
    }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Color FillColor { get; set; } = Color.White;

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Color BorderColor { get; set; } = Color.Gainsboro;

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Color CanvasColor { get; set; } = Color.WhiteSmoke;

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public int Radius { get; set; } = 10;

    protected override void OnPaintBackground(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(CanvasColor);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        var r = ClientRectangle;
        r.Width -= 1;
        r.Height -= 1;
        using var path = Shapes.Rounded(r, LogicalToDeviceUnits(Radius));
        using (var fill = new SolidBrush(FillColor))
        {
            g.FillPath(fill, path);
        }

        if (BorderColor != Color.Empty && BorderColor != FillColor)
        {
            using var pen = new Pen(BorderColor);
            g.DrawPath(pen, path);
        }
    }
}

/// <summary>Selectable tile for one target size.</summary>
internal sealed class SizeTile : Control
{
    private string _title = string.Empty;
    private string _subtitle = string.Empty;
    private bool _selected;
    private bool _hover;

    public SizeTile()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer
                 | ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
        TabStop = true;
        Cursor = Cursors.Hand;
        AccessibleRole = AccessibleRole.RadioButton;
        Height = 64;
    }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Theme Theme { get; set; } = Theme.Light;

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public int Value { get; set; }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public string Title
    {
        get => _title;
        set { _title = value; AccessibleName = value; Invalidate(); }
    }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public string Subtitle
    {
        get => _subtitle;
        set { _subtitle = value; AccessibleDescription = value; Invalidate(); }
    }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool Selected
    {
        get => _selected;
        set { _selected = value; Invalidate(); }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var t = Theme;
        var g = e.Graphics;
        g.Clear(t.Window);
        g.SmoothingMode = SmoothingMode.AntiAlias;

        int borderWidth = _selected ? LogicalToDeviceUnits(2) : 1;
        var r = ClientRectangle;
        r.Inflate(-borderWidth, -borderWidth);
        Color fill = _selected ? t.AccentSoft : t.Card;
        Color border = _selected || (_hover && Enabled) ? t.Accent : t.Border;

        using (var path = Shapes.Rounded(r, LogicalToDeviceUnits(8)))
        {
            using var brush = new SolidBrush(fill);
            g.FillPath(brush, path);
            using var pen = new Pen(border, borderWidth);
            g.DrawPath(pen, path);
        }

        Color titleColor = !Enabled ? t.TextMuted : _selected ? t.AccentSoftText : t.Text;
        Color subColor = !Enabled ? t.TextMuted : _selected ? t.AccentSoftText : t.TextSecondary;
        using var titleFont = new Font(Font.FontFamily, Font.Size + 2.5f, FontStyle.Bold);
        int x = LogicalToDeviceUnits(14);
        TextRenderer.DrawText(g, _title, titleFont, new Point(x, LogicalToDeviceUnits(10)), titleColor, TextFormatFlags.NoPrefix);
        TextRenderer.DrawText(g, _subtitle, Font, new Rectangle(x, LogicalToDeviceUnits(36), Width - 2 * x, LogicalToDeviceUnits(20)),
            subColor, TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis);

        if (Focused && ShowFocusCues)
        {
            var fr = ClientRectangle;
            fr.Inflate(-LogicalToDeviceUnits(5), -LogicalToDeviceUnits(5));
            ControlPaint.DrawFocusRectangle(g, fr, titleColor, fill);
        }
    }

    protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { _hover = false; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
    protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }
    protected override void OnEnabledChanged(EventArgs e) { Invalidate(); base.OnEnabledChanged(e); }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        Focus();
        base.OnMouseDown(e);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.KeyCode is Keys.Space or Keys.Enter)
        {
            OnClick(EventArgs.Empty);
            e.Handled = true;
        }

        base.OnKeyDown(e);
    }
}

/// <summary>Rounded flat button in the app's colours. Primary = accent fill, otherwise outlined.</summary>
internal sealed class RoundButton : Button
{
    private bool _hover;
    private bool _down;

    public RoundButton()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        FlatStyle = FlatStyle.Flat;
        FlatAppearance.BorderSize = 0;
        Cursor = Cursors.Hand;
        Padding = new Padding(14, 0, 14, 0);
    }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Theme Theme { get; set; } = Theme.Light;

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool Primary { get; set; }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Color CanvasColor { get; set; } = Color.White;

    protected override void OnPaint(PaintEventArgs e)
    {
        var t = Theme;
        var g = e.Graphics;
        g.Clear(CanvasColor);
        g.SmoothingMode = SmoothingMode.AntiAlias;

        Color fill, border, text;
        if (!Enabled)
        {
            fill = t.Track;
            border = t.Track;
            text = t.TextMuted;
        }
        else if (Primary)
        {
            fill = _hover || _down ? t.AccentHover : t.Accent;
            border = fill;
            text = t.OnAccent;
        }
        else
        {
            fill = _down ? t.Border : _hover ? t.ButtonHover : t.Card;
            border = t.Border;
            text = t.Text;
        }

        var r = ClientRectangle;
        r.Width -= 1;
        r.Height -= 1;
        using (var path = Shapes.Rounded(r, LogicalToDeviceUnits(8)))
        {
            using var brush = new SolidBrush(fill);
            g.FillPath(brush, path);
            using var pen = new Pen(border);
            g.DrawPath(pen, path);
        }

        TextRenderer.DrawText(g, Text, Font, ClientRectangle, text,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix);

        if (Focused && ShowFocusCues)
        {
            var fr = ClientRectangle;
            fr.Inflate(-LogicalToDeviceUnits(4), -LogicalToDeviceUnits(4));
            ControlPaint.DrawFocusRectangle(g, fr, text, fill);
        }
    }

    protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { _hover = false; _down = false; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnMouseDown(MouseEventArgs e) { _down = true; Invalidate(); base.OnMouseDown(e); }
    protected override void OnMouseUp(MouseEventArgs e) { _down = false; Invalidate(); base.OnMouseUp(e); }
    protected override void OnEnabledChanged(EventArgs e) { Invalidate(); base.OnEnabledChanged(e); }
}

/// <summary>Check box with a clearly visible box and tick in light and dark mode.</summary>
internal sealed class CheckOption : Control
{
    private bool _checked;
    private bool _radio;

    public CheckOption()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer
                 | ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
        TabStop = true;
        Cursor = Cursors.Hand;
        AccessibleRole = AccessibleRole.CheckButton;
        AutoSize = true;
    }

    public event EventHandler? CheckedChanged;

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Theme Theme { get; set; } = Theme.Light;

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Color CanvasColor { get; set; } = Color.White;

    /// <summary>Round indicator. A click only checks it, the form unchecks the others of the group.</summary>
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool Radio
    {
        get => _radio;
        set
        {
            _radio = value;
            AccessibleRole = value ? AccessibleRole.RadioButton : AccessibleRole.CheckButton;
            Invalidate();
        }
    }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool Checked
    {
        get => _checked;
        set
        {
            if (_checked == value)
            {
                return;
            }

            _checked = value;
            Invalidate();
            CheckedChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private int Box => LogicalToDeviceUnits(18);
    private int Gap => LogicalToDeviceUnits(9);

    public override Size GetPreferredSize(Size proposedSize)
    {
        var text = TextRenderer.MeasureText(Text, Font, Size.Empty, TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine);
        return new Size(Box + Gap + text.Width + 2, Math.Max(Box, text.Height) + LogicalToDeviceUnits(4));
    }

    protected override void OnTextChanged(EventArgs e)
    {
        base.OnTextChanged(e);
        PerformLayout();
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var t = Theme;
        var g = e.Graphics;
        g.Clear(CanvasColor);
        g.SmoothingMode = SmoothingMode.AntiAlias;

        int box = Box;
        var r = new Rectangle(1, (Height - box) / 2, box - 2, box - 2);
        if (_radio)
        {
            Color ring = _checked && Enabled ? t.Accent : t.TextSecondary;
            using (var fillBrush = new SolidBrush(t.Card))
            {
                g.FillEllipse(fillBrush, r);
            }

            using (var pen = new Pen(ring, LogicalToDeviceUnits(1)))
            {
                g.DrawEllipse(pen, r);
            }

            if (_checked)
            {
                int inset = r.Width / 4;
                var dot = Rectangle.Inflate(r, -inset, -inset);
                using var dotBrush = new SolidBrush(Enabled ? t.Accent : t.TextMuted);
                g.FillEllipse(dotBrush, dot);
            }

            DrawLabel(g, t, box);
            return;
        }

        using (var path = Shapes.Rounded(r, LogicalToDeviceUnits(4)))
        {
            Color fill = _checked && Enabled ? t.Accent : t.Card;
            Color border = _checked && Enabled ? t.Accent : t.TextSecondary;
            using var brush = new SolidBrush(fill);
            g.FillPath(brush, path);
            using var pen = new Pen(border, LogicalToDeviceUnits(1));
            g.DrawPath(pen, path);
        }

        if (_checked)
        {
            float s = r.Width / 16f;
            using var tick = new Pen(Enabled ? t.OnAccent : t.TextMuted, 2.2f * s) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round };
            g.DrawLines(tick, new[]
            {
                new PointF(r.X + 3.5f * s, r.Y + 8.5f * s),
                new PointF(r.X + 6.8f * s, r.Y + 11.8f * s),
                new PointF(r.X + 12.5f * s, r.Y + 4.5f * s),
            });
        }

        DrawLabel(g, t, box);
    }

    private void DrawLabel(Graphics g, Theme t, int box)
    {
        var textRect = new Rectangle(box + Gap, 0, Width - box - Gap, Height);
        TextRenderer.DrawText(g, Text, Font, textRect, Enabled ? t.Text : t.TextMuted,
            TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine);

        if (Focused && ShowFocusCues)
        {
            ControlPaint.DrawFocusRectangle(g, textRect, t.Text, CanvasColor);
        }
    }

    protected override void OnClick(EventArgs e)
    {
        if (Enabled)
        {
            // A radio option stays checked when clicked again.
            Checked = _radio || !Checked;
        }

        base.OnClick(e);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Space)
        {
            OnClick(EventArgs.Empty);
            e.Handled = true;
        }

        base.OnKeyDown(e);
    }

    protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
    protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }
    protected override void OnEnabledChanged(EventArgs e) { Invalidate(); base.OnEnabledChanged(e); }
}

/// <summary>Flat rounded bar: package size after downscaling, or run progress.</summary>
internal sealed class SizeBar : Control
{
    private double _value;

    public SizeBar()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        Height = 10;
    }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Color CanvasColor { get; set; } = Color.White;

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Color TrackColor { get; set; } = Color.Gainsboro;

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Color FillColor { get; set; } = Color.SteelBlue;

    /// <summary>0 to 1.</summary>
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public double Value
    {
        get => _value;
        set { _value = Math.Clamp(value, 0, 1); Invalidate(); }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(CanvasColor);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        var r = new Rectangle(0, 0, Width - 1, Height - 1);
        int radius = Height / 2;
        using (var track = Shapes.Rounded(r, radius))
        using (var brush = new SolidBrush(TrackColor))
        {
            g.FillPath(brush, track);
        }

        int w = (int)Math.Round(r.Width * _value);
        if (w > 0)
        {
            var fr = new Rectangle(0, 0, Math.Max(w, Height), r.Height);
            using var fill = Shapes.Rounded(fr, radius);
            using var brush = new SolidBrush(FillColor);
            g.FillPath(brush, fill);
        }
    }
}

/// <summary>
/// A tiny cat in the corner. No function, just an easter egg for people who look closely.
/// Hover: it turns to the accent color and says hello. Click: it blinks.
/// </summary>
internal sealed class CatMark : Control
{
    private bool _hover;
    private bool _blink;
    private readonly System.Windows.Forms.Timer _blinkTimer = new() { Interval = 180 };

    public CatMark()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        Size = new Size(16, 16);
        TabStop = false;
        _blinkTimer.Tick += (_, _) =>
        {
            _blinkTimer.Stop();
            _blink = false;
            Invalidate();
        };
    }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Color CanvasColor { get; set; } = Color.White;

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Color CatColor { get; set; } = Color.Gray;

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Color HoverColor { get; set; } = Color.SteelBlue;

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(CanvasColor);
        g.SmoothingMode = SmoothingMode.AntiAlias;

        // Drawn on a 16 x 16 grid and scaled to the control.
        float s = Math.Min(Width, Height) / 16f;
        PointF P(float x, float y) => new(x * s, y * s);

        using var cat = new GraphicsPath(FillMode.Winding);
        cat.AddPolygon(new[] { P(2.2f, 8f), P(3f, 1.5f), P(7.2f, 5f) });
        cat.AddPolygon(new[] { P(13.8f, 8f), P(13f, 1.5f), P(8.8f, 5f) });
        cat.AddEllipse(2f * s, 4f * s, 12f * s, 10.5f * s);

        using (var fill = new SolidBrush(_hover ? HoverColor : CatColor))
        {
            g.FillPath(fill, cat);
        }

        using var eye = new SolidBrush(CanvasColor);
        if (_blink)
        {
            g.FillRectangle(eye, 4.6f * s, 8.9f * s, 2.2f * s, 0.7f * s);
            g.FillRectangle(eye, 9.2f * s, 8.9f * s, 2.2f * s, 0.7f * s);
        }
        else
        {
            g.FillEllipse(eye, 5f * s, 7.6f * s, 1.6f * s, 2.6f * s);
            g.FillEllipse(eye, 9.4f * s, 7.6f * s, 1.6f * s, 2.6f * s);
        }
    }

    protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { _hover = false; Invalidate(); base.OnMouseLeave(e); }

    protected override void OnClick(EventArgs e)
    {
        _blink = true;
        Invalidate();
        _blinkTimer.Stop();
        _blinkTimer.Start();
        base.OnClick(e);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _blinkTimer.Dispose();
        }

        base.Dispose(disposing);
    }
}
