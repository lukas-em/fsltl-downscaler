using System.Drawing.Drawing2D;

namespace FsltlDownscaler.Gui;

/// <summary>One row in the package picker.</summary>
internal sealed record PickerEntry(string Title, string? Detail, string Glyph, bool Current, Action Action, bool SeparatorBefore = false);

/// <summary>
/// Drop-down under the Change button: the detected packages and "Browse for another folder…".
/// Drawn in the app's colours, with rounded corners on Windows 11.
/// </summary>
internal sealed class PackagePicker : ToolStripDropDown
{
    private readonly Theme _t;
    private readonly PickerList _list;
    private bool _roundedByDwm;

    public PackagePicker(Theme theme, IReadOnlyList<PickerEntry> entries, Font font, int dpi, int width)
    {
        _t = theme;
        _list = new PickerList(theme, entries, dpi) { Font = font };
        _list.Size = new Size(width, _list.PreferredHeight);

        var host = new ToolStripControlHost(_list) { AutoSize = false, Size = _list.Size, Margin = Padding.Empty, Padding = Padding.Empty };
        Padding = Padding.Empty;
        DropShadowEnabled = true;
        BackColor = theme.Card;
        Renderer = new FlatRenderer(this);
        Items.Add(host);

        _list.Chosen += entry =>
        {
            Close(ToolStripDropDownCloseReason.ItemClicked);
            entry.Action();
        };
        _list.Cancelled += () => Close(ToolStripDropDownCloseReason.Keyboard);
        Opened += (_, _) => _list.Focus();
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        _roundedByDwm = NativeMethods.TryRoundCorners(Handle, _t.Border);
    }

    private sealed class FlatRenderer(PackagePicker owner) : ToolStripRenderer
    {
        protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e)
        {
            using var brush = new SolidBrush(owner._t.Card);
            e.Graphics.FillRectangle(brush, e.AffectedBounds);
        }

        protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e)
        {
            // Windows 11 draws a rounded border itself. Older systems get a plain one.
            if (owner._roundedByDwm)
            {
                return;
            }

            using var pen = new Pen(owner._t.Border);
            var r = e.AffectedBounds;
            e.Graphics.DrawRectangle(pen, r.X, r.Y, r.Width - 1, r.Height - 1);
        }
    }
}

/// <summary>The rows of the picker. Mouse and keyboard (arrow keys, Enter, Escape).</summary>
internal sealed class PickerList : Control
{
    private const string GlyphFont = "Segoe MDL2 Assets";
    private const string GlyphCheck = "\uE73E";

    private readonly Theme _t;
    private readonly IReadOnlyList<PickerEntry> _entries;
    private readonly int _dpi;
    private int _hot;

    public PickerList(Theme theme, IReadOnlyList<PickerEntry> entries, int dpi)
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer
                 | ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
        _t = theme;
        _entries = entries;
        _dpi = dpi;
        TabStop = true;
        Cursor = Cursors.Hand;
        AccessibleRole = AccessibleRole.List;
        int current = entries.ToList().FindIndex(x => x.Current);
        _hot = current >= 0 ? current : 0;
    }

    public event Action<PickerEntry>? Chosen;

    public event Action? Cancelled;

    public int PreferredHeight
    {
        get
        {
            int h = 2 * Pad;
            foreach (var e in _entries)
            {
                h += (e.SeparatorBefore ? SepHeight : 0) + RowHeight(e);
            }

            return h;
        }
    }

    private int Pad => S(6);
    private int SepHeight => S(9);

    private int S(int v) => (int)Math.Round(v * _dpi / 96f);

    private int RowHeight(PickerEntry e) => S(e.Detail is null ? 40 : 54);

    private Rectangle RowRect(int index)
    {
        int y = Pad;
        for (int i = 0; i <= index; i++)
        {
            if (_entries[i].SeparatorBefore)
            {
                y += SepHeight;
            }

            if (i < index)
            {
                y += RowHeight(_entries[i]);
            }
        }

        return new Rectangle(Pad, y, Width - 2 * Pad, RowHeight(_entries[index]));
    }

    private int HitTest(Point p)
    {
        for (int i = 0; i < _entries.Count; i++)
        {
            if (RowRect(i).Contains(p))
            {
                return i;
            }
        }

        return -1;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(_t.Card);
        g.SmoothingMode = SmoothingMode.AntiAlias;

        using var titleFont = new Font(Font, FontStyle.Bold);
        using var glyphFont = new Font(GlyphFont, Font.Size + 2f);
        using var checkFont = new Font(GlyphFont, Font.Size);

        for (int i = 0; i < _entries.Count; i++)
        {
            var entry = _entries[i];
            var r = RowRect(i);

            if (entry.SeparatorBefore)
            {
                int ly = r.Y - SepHeight / 2 - 1;
                using var pen = new Pen(_t.Border);
                g.DrawLine(pen, r.X + S(10), ly, r.Right - S(10), ly);
            }

            bool hot = i == _hot;
            if (entry.Current || hot)
            {
                var fr = r;
                fr.Width -= 1;
                fr.Height -= 1;
                using var path = Shapes.Rounded(fr, S(6));
                using var brush = new SolidBrush(entry.Current ? _t.AccentSoft : _t.ButtonHover);
                g.FillPath(brush, path);
                if (entry.Current && hot)
                {
                    using var pen = new Pen(_t.Accent);
                    g.DrawPath(pen, path);
                }
            }

            Color titleColor = entry.Current ? _t.AccentSoftText : _t.Text;
            Color detailColor = entry.Current ? _t.AccentSoftText : _t.TextSecondary;
            Color glyphColor = entry.Current ? _t.Accent : _t.TextSecondary;

            var glyphRect = new Rectangle(r.X + S(8), r.Y, S(30), r.Height);
            TextRenderer.DrawText(g, entry.Glyph, glyphFont, glyphRect, glyphColor,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine);

            int textX = r.X + S(46);
            int checkW = S(34);
            int textW = r.Right - checkW - textX;
            const TextFormatFlags flags = TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis;

            if (entry.Detail is null)
            {
                TextRenderer.DrawText(g, entry.Title, Font, new Rectangle(textX, r.Y, textW, r.Height), titleColor,
                    flags | TextFormatFlags.VerticalCenter);
            }
            else
            {
                TextRenderer.DrawText(g, entry.Title, titleFont, new Rectangle(textX, r.Y + S(8), textW, S(20)), titleColor, flags);
                TextRenderer.DrawText(g, entry.Detail, Font, new Rectangle(textX, r.Y + S(28), textW, S(20)), detailColor,
                    TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine | TextFormatFlags.PathEllipsis);
            }

            if (entry.Current)
            {
                TextRenderer.DrawText(g, GlyphCheck, checkFont, new Rectangle(r.Right - checkW, r.Y, checkW - S(6), r.Height), _t.Accent,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine);
            }
        }
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        int i = HitTest(e.Location);
        if (i >= 0 && i != _hot)
        {
            _hot = i;
            Invalidate();
        }

        base.OnMouseMove(e);
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        if (e.Button != MouseButtons.Left)
        {
            return;
        }

        int i = HitTest(e.Location);
        if (i >= 0)
        {
            Chosen?.Invoke(_entries[i]);
        }
    }

    protected override bool IsInputKey(Keys keyData) =>
        keyData is Keys.Up or Keys.Down or Keys.Enter or Keys.Escape || base.IsInputKey(keyData);

    protected override void OnKeyDown(KeyEventArgs e)
    {
        switch (e.KeyCode)
        {
            case Keys.Up:
                _hot = (_hot - 1 + _entries.Count) % _entries.Count;
                Invalidate();
                e.Handled = true;
                break;
            case Keys.Down:
                _hot = (_hot + 1) % _entries.Count;
                Invalidate();
                e.Handled = true;
                break;
            case Keys.Enter:
            case Keys.Space:
                Chosen?.Invoke(_entries[_hot]);
                e.Handled = true;
                break;
            case Keys.Escape:
                Cancelled?.Invoke();
                e.Handled = true;
                break;
        }

        base.OnKeyDown(e);
    }
}
