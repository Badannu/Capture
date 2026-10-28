using System.Drawing.Text;
using System.Runtime.InteropServices;

namespace CaptureDemo.Ui;

/// <summary>
/// The feedback line under the version stamp: one row of channels, each a glyph, a label
/// and a value, separated by semicolons, the whole row centred.
/// <para>
/// Deliberately not a card and deliberately without a caption. Everything above it is a
/// setting with an editor; this is the only line on the page you read instead of operate,
/// so it stays one sentence under the version number. Each channel copies its value on
/// click, because a mail address in a <see cref="Label"/> cannot be selected and retyping
/// it by hand is the fastest way to lose a bug report.
/// </para>
/// </summary>
internal sealed class ContactBlock : Control
{
    private const int BaseRowHeight = 26;
    private const int BaseIconGap = 6;
    private const int BaseItemGap = 10;
    private const int BaseCopyGap = 12;

    /// <summary>Icon side as a fraction of the body font's line height.</summary>
    private const float IconRatio = 0.86f;

    private readonly float _scale;
    private readonly (string Label, string Value, Action<Graphics, Rectangle, Color> Icon)[] _items;
    private readonly System.Windows.Forms.Timer _flash;

    private int _hot = -1;
    private int _copied = -1;

    public ContactBlock(
        float scale,
        params (string Label, string Value, Action<Graphics, Rectangle, Color> Icon)[] items)
    {
        _scale = scale <= 0f ? 1f : scale;
        _items = items;

        SetStyle(
            ControlStyles.UserPaint |
            ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.ResizeRedraw,
            true);
        BackColor = Theme.AppBack;
        TabStop = false;
        Height = S(BaseRowHeight);

        _flash = new System.Windows.Forms.Timer { Interval = 1600 };
        _flash.Tick += (_, _) =>
        {
            _flash.Stop();
            _copied = -1;
            Invalidate();
        };
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(BackColor);
        g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;

        var layout = MeasureRow();
        var iconSide = IconSide();
        for (var i = 0; i < _items.Length; i++)
        {
            var x = layout.Left + layout.Starts[i];
            _items[i].Icon(g, new Rectangle(x, (Height - iconSide) / 2, iconSide, iconSide), Theme.Muted);

            var label = LabelText(i);
            var labelWidth = TextWidth(label, UiFonts.Body);
            var valueWidth = TextWidth(_items[i].Value, UiFonts.Body);
            var textLeft = x + iconSide + S(BaseIconGap);

            TextRenderer.DrawText(
                g,
                label,
                UiFonts.Body,
                new Rectangle(textLeft, 0, labelWidth + S(1), Height),
                Theme.Muted,
                LineFlags);

            // The gap after the colon is whatever the font says a space is: measuring the
            // whole line and subtracting both halves leaves exactly one space, padding included.
            var valueLeft = textLeft + ValueOffset(i);
            TextRenderer.DrawText(
                g,
                _items[i].Value,
                UiFonts.Body,
                new Rectangle(valueLeft, 0, valueWidth + S(1), Height),
                _hot == i || _copied == i ? Theme.Accent : Theme.Muted,
                LineFlags);

            if (i < _items.Length - 1)
            {
                TextRenderer.DrawText(
                    g,
                    ";",
                    UiFonts.Body,
                    new Rectangle(x + layout.Widths[i], 0, S(8), Height),
                    Theme.Muted,
                    LineFlags);
            }
        }

        if (_copied >= 0)
        {
            var hintLeft = layout.Left + layout.Width + S(BaseCopyGap);
            if (hintLeft < Width - S(4))
            {
                TextRenderer.DrawText(
                    g,
                    "已复制",
                    UiFonts.Caption,
                    new Rectangle(hintLeft, 0, Math.Max(0, Width - hintLeft), Height),
                    Theme.Accent,
                    LineFlags);
            }
        }
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        var hot = HitIndex(e.Location);
        if (hot == _hot)
        {
            return;
        }

        _hot = hot;
        Cursor = hot >= 0 ? Cursors.Hand : Cursors.Default;
        Invalidate();
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        Cursor = Cursors.Default;
        if (_hot < 0)
        {
            return;
        }

        _hot = -1;
        Invalidate();
    }

    protected override void OnMouseClick(MouseEventArgs e)
    {
        base.OnMouseClick(e);
        if (e.Button != MouseButtons.Left)
        {
            return;
        }

        var index = HitIndex(e.Location);
        if (index >= 0)
        {
            CopyRow(index);
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _flash.Stop();
            _flash.Dispose();
        }

        base.Dispose(disposing);
    }

    /// <summary>Copies one channel's value and flashes the confirmation. Also used by the dev preview.</summary>
    internal void CopyRow(int index)
    {
        if (index < 0 || index >= _items.Length)
        {
            return;
        }

        try
        {
            Clipboard.SetText(_items[index].Value);
        }
        catch (ExternalException)
        {
            // Another process holds the clipboard open. Keep the row as it was rather
            // than claiming a copy that did not happen.
            return;
        }

        _copied = index;
        _flash.Stop();
        _flash.Start();
        Invalidate();
    }

    private (int Left, int Width, int[] Starts, int[] Widths) MeasureRow()
    {
        var starts = new int[_items.Length];
        var widths = new int[_items.Length];
        var total = 0;
        for (var i = 0; i < _items.Length; i++)
        {
            starts[i] = total;
            widths[i] = ItemWidth(i);
            total += widths[i];
            if (i < _items.Length - 1)
            {
                total += SeparatorWidth();
            }
        }

        return (Math.Max(S(2), (Width - total) / 2), total, starts, widths);
    }

    /// <summary>Icon, a gap, then the label and its value.</summary>
    private int ItemWidth(int index) =>
        IconSide() + S(BaseIconGap) + ValueOffset(index) + TextWidth(_items[index].Value, UiFonts.Body);

    /// <summary>The semicolon that follows a value, plus the air before the next glyph.</summary>
    private int SeparatorWidth() => TextWidth(";", UiFonts.Body) + S(BaseItemGap);

    /// <summary>
    /// Ink width from the icon-less text start to the value: label, colon and one space.
    /// Both measurements drop TextRenderer's padding (<see cref="LineFlags"/>), so the
    /// difference is the glyph advance of "label: " and the gap is exactly one space.
    /// </summary>
    private int ValueOffset(int index)
    {
        var value = _items[index].Value;
        return TextWidth(LabelText(index) + " " + value, UiFonts.Body) - TextWidth(value, UiFonts.Body);
    }

    private string LabelText(int index) => _items[index].Label + ":";

    /// <summary>Icons are drawn from the 24-grid, so their ink is a little smaller than the box.</summary>
    private int IconSide() => Math.Max(S(12), (int)Math.Round(UiFonts.Body.Height * IconRatio));

    /// <summary>Hit area covers the icon plus its text only, so a hand cursor never appears over air.</summary>
    private Rectangle ItemBounds(int index)
    {
        var layout = MeasureRow();
        return new Rectangle(layout.Left + layout.Starts[index], 0, layout.Widths[index], Height);
    }

    private int HitIndex(Point point)
    {
        for (var i = 0; i < _items.Length; i++)
        {
            if (ItemBounds(i).Contains(point))
            {
                return i;
            }
        }

        return -1;
    }

    private int S(int px) => Math.Max(1, (int)Math.Round(px * _scale));

    /// <summary>
    /// Ink width, i.e. the glyphs and nothing else. Measured and drawn with the same
    /// <see cref="LineFlags"/>, so the numbers here are what lands on screen and a
    /// centred row is centred by pixels rather than by an estimate.
    /// </summary>
    private static int TextWidth(string text, Font font) =>
        TextRenderer.MeasureText(text, font, MeasureBounds, LineFlags).Width;

    /// <summary>Dev-only: layout numbers, so --ui-demo screenshots can be checked against math.</summary>
    internal string Probe()
    {
        var layout = MeasureRow();
        var sb = new System.Text.StringBuilder();
        sb.Append($"bounds={Bounds} body={UiFonts.Body.Height} icon={IconSide()} left={layout.Left} total={layout.Width}");
        for (var i = 0; i < _items.Length; i++)
        {
            sb.Append($" | [{i}] {layout.Starts[i]}+{layout.Widths[i]} off={ValueOffset(i)}");
        }

        return sb.ToString();
    }

    private static readonly Size MeasureBounds = new(short.MaxValue, short.MaxValue);

    private const TextFormatFlags LineFlags =
        TextFormatFlags.Left |
        TextFormatFlags.VerticalCenter |
        TextFormatFlags.NoPrefix |
        TextFormatFlags.NoPadding;
}
