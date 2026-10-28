using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using CaptureDemo.Core;

namespace CaptureDemo.Snip;

/// <summary>
/// Two small popups: one row of preset ink colours, one row of stroke weights.
/// Deliberately not a colour wheel — a snip needs a fast, unambiguous pick.
/// The popup stays put until the caller dismisses it (a click outside, or Esc):
/// closing on a timer made it vanish while the pointer was still travelling from
/// the chip to the swatch.
/// </summary>
internal sealed class StylePillForm : LayeredChromeForm
{
    private const int ColorBase = 200;
    private const int WeightBase = 300;
    private const int Swatch = 26;
    private const int SwatchGap = 6;
    private const int WeightSlot = 52;

    /// <summary>Preview thickness per weight step, in 96-dpi pixels. Kept wider apart than the true
    /// ratios so the four choices stay distinguishable at a glance.</summary>
    private static readonly int[] WeightSampleThickness = { 2, 4, 7, 10 };

    private int _hover;
    private int[] _widths = Array.Empty<int>();
    private int[] _ids = Array.Empty<int>();
    private int _currentColor = AnnoPalette.Colors[AnnoPalette.Default].ToArgb();
    private float _currentWeight = AnnoPalette.Weights[1];
    private bool _weightsMode;

    public event Action<int>? ColorPicked;

    public event Action<float>? WeightPicked;

    public void ShowColors(int current)
    {
        _weightsMode = false;
        _currentColor = current;
        _hover = 0;
        Relayout();
    }

    public void ShowWeights(float current)
    {
        _weightsMode = true;
        _currentWeight = current;
        _hover = 0;
        Relayout();
    }

    protected override void Relayout()
    {
        BuildSlots();
        var pad = S(5);
        var height = S(PillMetrics.BarHeight);
        ClientSize = new Size(PillMetrics.ContentWidth(pad, _widths) + S(PillMetrics.Shadow) * 2, height + S(PillMetrics.Shadow) * 2);
    }

    /// <summary>
    /// Slots must be built in device pixels. Building them at 96 dpi while drawing at the
    /// monitor scale makes each swatch wider than its slot, which erases the gaps.
    /// </summary>
    private void BuildSlots()
    {
        var count = _weightsMode ? AnnoPalette.Weights.Length : AnnoPalette.Colors.Length;
        var slot = _weightsMode ? S(WeightSlot) : S(Swatch);
        var baseId = _weightsMode ? WeightBase : ColorBase;
        var widths = new List<int>(count * 2);
        var ids = new List<int>(count * 2);
        for (var i = 0; i < count; i++)
        {
            if (i > 0)
            {
                widths.Add(S(SwatchGap));
                ids.Add(0);
            }

            widths.Add(slot);
            ids.Add(baseId + i);
        }

        _widths = widths.ToArray();
        _ids = ids.ToArray();
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        var hit = Hit(e.Location);
        Cursor = hit == 0 ? Cursors.Default : Cursors.Hand;
        if (hit != _hover)
        {
            _hover = hit;
            Render();
        }

        base.OnMouseMove(e);
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        _hover = 0;
        Render();
        base.OnMouseLeave(e);
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        var hit = Hit(e.Location);
        if (hit >= WeightBase)
        {
            var index = hit - WeightBase;
            if (index >= 0 && index < AnnoPalette.Weights.Length)
            {
                _currentWeight = AnnoPalette.Weights[index];
                WeightPicked?.Invoke(_currentWeight);
                // Stays open on purpose: the selection ring has to be visible, and a
                // second try must not need a second trip to the chip.
                Render();
            }
        }
        else if (hit >= ColorBase)
        {
            var index = hit - ColorBase;
            if (index >= 0 && index < AnnoPalette.Colors.Length)
            {
                _currentColor = AnnoPalette.Colors[index].ToArgb();
                ColorPicked?.Invoke(_currentColor);
                Render();
            }
        }

        base.OnMouseUp(e);
    }

    protected override void OnVisibleChanged(EventArgs e)
    {
        base.OnVisibleChanged(e);
        if (!Visible)
        {
            _hover = 0;
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            PillDraw.Forget(this);
        }

        base.Dispose(disposing);
    }

    private int Hit(Point point) =>
        PillMetrics.Hit(point.X, point.Y, S(PillMetrics.Shadow), S(PillMetrics.BarHeight), S(5), _widths, _ids);

    protected override void Render()
    {
        if (!IsHandleCreated || _widths.Length == 0)
        {
            return;
        }

        var shadow = S(PillMetrics.Shadow);
        using var bitmap = new Bitmap(Math.Max(1, Width), Math.Max(1, Height), PixelFormat.Format32bppPArgb);
        using (var graphics = Graphics.FromImage(bitmap))
        {
            graphics.Clear(Color.Transparent);
            var body = new Rectangle(shadow, shadow, Width - shadow * 2, S(PillMetrics.BarHeight));
            DrawBar(graphics, body);

            var cursor = body.Left + S(5);
            for (var i = 0; i < _widths.Length; i++)
            {
                var slot = new Rectangle(cursor, body.Top, _widths[i], body.Height);
                var id = _ids[i];
                // Weight ids (300+) also satisfy ">= ColorBase", so the weight range must be tested first.
                if (id >= WeightBase)
                {
                    DrawWeight(graphics, slot, id - WeightBase);
                }
                else if (id >= ColorBase)
                {
                    DrawSwatch(graphics, slot, id - ColorBase);
                }

                cursor += _widths[i];
            }
        }

        PillDraw.Present(this, bitmap);
    }

    private void DrawSwatch(Graphics graphics, Rectangle slot, int index)
    {
        if (index < 0 || index >= AnnoPalette.Colors.Length)
        {
            return;
        }

        var side = S(Swatch);
        var box = new Rectangle(slot.Left + ((slot.Width - side) / 2), slot.Top + ((slot.Height - side) / 2), side, side);
        var color = AnnoPalette.Colors[index];
        var selected = color.ToArgb() == _currentColor;
        var hovered = _hover == ColorBase + index;

        if (hovered && !selected)
        {
            using var halo = new SolidBrush(Color.FromArgb(46, 255, 255, 255));
            using var haloPath = PillDraw.Round(Rectangle.Inflate(box, S(2), S(2)), S(7));
            graphics.FillPath(halo, haloPath);
        }

        using var path = PillDraw.Round(box, S(6));
        using var fill = new SolidBrush(color);
        graphics.FillPath(fill, path);
        // A dark rim keeps white and black swatches readable on the dark capsule.
        using var rim = new Pen(Color.FromArgb(color.GetBrightness() > 0.75f ? 90 : 40, 0, 0, 0));
        graphics.DrawPath(rim, path);
        if (selected)
        {
            // Drawn inside the swatch: an outer ring would spill into the gap and look like a blob.
            var ringColour = color.GetBrightness() > 0.75f
                ? Color.FromArgb(210, 30, 30, 30)
                : Color.FromArgb(240, 255, 255, 255);
            using var ring = new Pen(ringColour, Math.Max(1.6f, S(2)));
            graphics.DrawPath(ring, PillDraw.Round(Rectangle.Inflate(box, -S(3), -S(3)), S(4)));
        }
    }

    private void DrawWeight(Graphics graphics, Rectangle slot, int index)
    {
        if (index < 0 || index >= AnnoPalette.Weights.Length)
        {
            return;
        }

        var weight = AnnoPalette.Weights[index];
        var selected = Math.Abs(weight - _currentWeight) < 0.01f;
        var hovered = _hover == WeightBase + index;
        var lineWidth = Math.Max(1.5f, S(WeightSampleThickness[index]));
        var length = Math.Max(S(20), slot.Width - S(18));
        var y = slot.Top + (slot.Height / 2f);
        var left = slot.Left + ((slot.Width - length) / 2f);

        if (hovered && !selected)
        {
            using var halo = new SolidBrush(Color.FromArgb(34, 255, 255, 255));
            using var haloPath = PillDraw.Round(Rectangle.Inflate(slot, -S(2), -S(5)), S(7));
            graphics.FillPath(halo, haloPath);
        }

        if (selected)
        {
            using var plate = new SolidBrush(Color.FromArgb(30, 255, 255, 255));
            using var platePath = PillDraw.Round(Rectangle.Inflate(slot, -S(2), -S(5)), S(7));
            graphics.FillPath(plate, platePath);
        }

        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using var pen = new Pen(
            selected ? Color.FromArgb(255, 255, 255) : Color.FromArgb(205, 205, 205),
            lineWidth)
        {
            StartCap = LineCap.Round,
            EndCap = LineCap.Round,
        };
        graphics.DrawLine(pen, left, y, left + length, y);
    }
}
