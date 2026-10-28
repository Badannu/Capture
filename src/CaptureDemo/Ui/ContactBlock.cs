using System.Drawing.Text;
using System.Runtime.InteropServices;

namespace CaptureDemo.Ui;

/// <summary>
/// The feedback footer under the version stamp: a caption plus one line per channel.
/// <para>
/// Deliberately not a card. Everything above it is a setting with an editor; this is
/// the only block on the page you read instead of operate, and a card would imply a
/// control that is not there. Rows copy their value on click, because a mail address
/// in a <see cref="Label"/> cannot be selected and retyping it by hand is the fastest
/// way to lose a bug report.
/// </para>
/// </summary>
internal sealed class ContactBlock : Control
{
    private const int BasePadTop = 2;
    private const int BaseCaptionHeight = 18;
    private const int BaseCaptionGap = 6;
    private const int BaseRowHeight = 24;

    private readonly float _scale;
    private readonly string _caption;
    private readonly (string Label, string Value)[] _rows;
    private readonly System.Windows.Forms.Timer _flash;

    private int _hot = -1;
    private int _copied = -1;

    public ContactBlock(float scale, string caption, params (string Label, string Value)[] rows)
    {
        _scale = scale <= 0f ? 1f : scale;
        _caption = caption;
        _rows = rows;

        SetStyle(
            ControlStyles.UserPaint |
            ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.ResizeRedraw,
            true);
        BackColor = Theme.AppBack;
        TabStop = false;
        Height = S(BasePadTop + BaseCaptionHeight + BaseCaptionGap + (rows.Length * BaseRowHeight));

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

        var left = GroupLeft();
        var pad = S(BasePadTop);
        TextRenderer.DrawText(
            g,
            _caption,
            UiFonts.Section,
            new Rectangle(left, pad, Math.Max(0, Width - left - S(2)), S(BaseCaptionHeight)),
            Theme.Muted,
            LineFlags);

        // Caption and labels share one left edge; each value starts right after its own
        // colon, so the two rows read as prose rather than as a two-column table.
        for (var i = 0; i < _rows.Length; i++)
        {
            var top = RowTop(i);
            var label = LabelText(i);
            TextRenderer.DrawText(
                g,
                label,
                UiFonts.Body,
                new Rectangle(left, top, Math.Max(0, Width - left - S(2)), S(BaseRowHeight)),
                Theme.Muted,
                LineFlags);

            var valueLeft = left + ValueOffset(i);
            var valueWidth = TextRenderer.MeasureText(_rows[i].Value, UiFonts.Body).Width;
            var valueRect = new Rectangle(valueLeft, top, valueWidth, S(BaseRowHeight));
            TextRenderer.DrawText(
                g,
                _rows[i].Value,
                UiFonts.Body,
                valueRect,
                _hot == i || _copied == i ? Theme.Accent : Theme.Muted,
                LineFlags);

            if (_copied == i)
            {
                var hintLeft = valueLeft + valueWidth + S(14);
                if (hintLeft < Width - S(4))
                {
                    TextRenderer.DrawText(
                        g,
                        "已复制",
                        UiFonts.Caption,
                        new Rectangle(hintLeft, top, Width - hintLeft, S(BaseRowHeight)),
                        Theme.Accent,
                        LineFlags);
                }
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

    /// <summary>Copies one row's value and flashes the confirmation. Also used by the dev preview.</summary>
    internal void CopyRow(int index)
    {
        if (index < 0 || index >= _rows.Length)
        {
            return;
        }

        try
        {
            Clipboard.SetText(_rows[index].Value);
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

    /// <summary>Rows are laid out as one left-aligned block, centred as a whole.</summary>
    private int GroupLeft()
    {
        var width = TextRenderer.MeasureText(_caption, UiFonts.Section).Width;
        for (var i = 0; i < _rows.Length; i++)
        {
            width = Math.Max(width, ValueOffset(i) + TextRenderer.MeasureText(_rows[i].Value, UiFonts.Body).Width);
        }

        return Math.Max(S(2), (Width - width) / 2);
    }

    /// <summary>
    /// Distance from the block's left edge to the value: the label plus one space.
    /// <para>
    /// Measuring the label alone would add TextRenderer's ~3px padding on both the label
    /// and the value rect, leaving a gap that reads as two spaces. Measuring the full line
    /// and subtracting the value cancels that padding on both sides.
    /// </para>
    /// </summary>
    private int ValueOffset(int index)
    {
        var value = _rows[index].Value;
        return TextRenderer.MeasureText(LabelText(index) + " " + value, UiFonts.Body).Width -
            TextRenderer.MeasureText(value, UiFonts.Body).Width;
    }

    private string LabelText(int index) => _rows[index].Label + ":";

    private int RowTop(int index) =>
        S(BasePadTop + BaseCaptionHeight + BaseCaptionGap) + (index * S(BaseRowHeight));

    /// <summary>Hit area covers the text only, so a hand cursor never appears over empty space.</summary>
    private Rectangle RowBounds(int index)
    {
        var width = ValueOffset(index) + TextRenderer.MeasureText(_rows[index].Value, UiFonts.Body).Width;
        return new Rectangle(GroupLeft(), RowTop(index), width, S(BaseRowHeight));
    }

    private int HitIndex(Point point)
    {
        for (var i = 0; i < _rows.Length; i++)
        {
            if (RowBounds(i).Contains(point))
            {
                return i;
            }
        }

        return -1;
    }

    private int S(int px) => Math.Max(1, (int)Math.Round(px * _scale));

    private const TextFormatFlags LineFlags =
        TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix;
}
