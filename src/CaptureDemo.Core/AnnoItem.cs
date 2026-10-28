using System.Drawing;

namespace CaptureDemo.Core;

/// <summary>Tools shown on the annotation bar. Order matches the bar layout.</summary>
public enum AnnoTool
{
    Select,
    Pen,
    Line,
    Arrow,
    Rect,
    RoundRect,
    Ellipse,
    Circle,
    Highlight,
    Mosaic,
}

public enum AnnoHandle
{
    None,
    Left,
    TopLeft,
    Top,
    TopRight,
    Right,
    BottomRight,
    Bottom,
    BottomLeft,
    Body,
}

/// <summary>
/// One annotation on the snip, in image pixel coordinates. Two-point tools keep
/// exactly two points (the diagonal corners), freehand tools keep the sampled path.
/// Geometry only — no drawing, so this stays testable without a display.
/// </summary>
public sealed class AnnoItem
{
    /// <summary>Minimum drag before a shape is worth keeping.</summary>
    public const int MinExtent = 3;

    private int[] _xs;
    private int[] _ys;

    public AnnoItem(AnnoTool tool, int[] xs, int[] ys)
    {
        if (xs.Length != ys.Length)
        {
            throw new ArgumentException("坐标数组长度必须一致。", nameof(ys));
        }

        if (xs.Length == 0)
        {
            throw new ArgumentException("至少需要一个点。", nameof(xs));
        }

        Tool = tool;
        _xs = xs;
        _ys = ys;
    }

    public AnnoTool Tool { get; }

    public int[] Xs => _xs;

    public int[] Ys => _ys;

    public int ColorArgb { get; set; } = unchecked((int)0xFFF24848);

    /// <summary>Stroke width in image pixels already scaled for the capture DPI.</summary>
    public float Width { get; set; } = 4f;

    public bool IsShape => Tool is AnnoTool.Line or AnnoTool.Arrow or AnnoTool.Rect
        or AnnoTool.RoundRect or AnnoTool.Ellipse or AnnoTool.Circle;

    public bool IsFreehand => Tool is AnnoTool.Pen or AnnoTool.Highlight or AnnoTool.Mosaic;

    public Point Start => new(_xs[0], _ys[0]);

    /// <summary>Far corner for shapes, last sample for freehand.</summary>
    public Point End => new(_xs[^1], _ys[^1]);

    /// <summary>Axis-aligned box the item occupies. Shapes use both corners.</summary>
    public Rectangle Bounds
    {
        get
        {
            var left = _xs[0];
            var right = left;
            var top = _ys[0];
            var bottom = top;
            for (var i = 1; i < _xs.Length; i++)
            {
                if (_xs[i] < left)
                {
                    left = _xs[i];
                }
                else if (_xs[i] > right)
                {
                    right = _xs[i];
                }

                if (_ys[i] < top)
                {
                    top = _ys[i];
                }
                else if (_ys[i] > bottom)
                {
                    bottom = _ys[i];
                }
            }

            return Rectangle.FromLTRB(left, top, right, bottom);
        }
    }

    /// <summary>Bounds grown by half the painted stroke plus a couple of pixels for the cap.</summary>
    public Rectangle InkBounds
    {
        get
        {
            // Highlight paints at 3.4x the nominal weight, so its footprint is wider than Width/2.
            var painted = Tool switch
            {
                AnnoTool.Highlight => Math.Max(Width * 3.4f, 10f),
                AnnoTool.Mosaic => 0f,
                _ => Width,
            };
            var pad = (int)Math.Ceiling(painted / 2f) + 2;
            return Rectangle.Inflate(Bounds, pad, pad);
        }
    }

    /// <summary>Snapshot of the points, so a drag can be recomputed from the origin every frame.</summary>
    public void SetPoints(int[] xs, int[] ys)
    {
        if (xs.Length != ys.Length || xs.Length == 0)
        {
            return;
        }

        _xs = (int[])xs.Clone();
        _ys = (int[])ys.Clone();
    }

    /// <summary>Moves the far end of a two-point tool. Circles stay square around the anchor.</summary>
    public void SetEnd(int x, int y)
    {
        if (IsShape)
        {
            if (Tool == AnnoTool.Circle)
            {
                var side = Math.Max(Math.Abs(x - _xs[0]), Math.Abs(y - _ys[0]));
                x = _xs[0] + (x >= _xs[0] ? side : -side);
                y = _ys[0] + (y >= _ys[0] ? side : -side);
            }

            _xs[^1] = x;
            _ys[^1] = y;
            return;
        }

        Append(x, y);
    }

    /// <summary>Adds a sample to a freehand path.</summary>
    public void Append(int x, int y)
    {
        var next = _xs.Length;
        Array.Resize(ref _xs, next + 1);
        Array.Resize(ref _ys, next + 1);
        _xs[next] = x;
        _ys[next] = y;
    }

    public AnnoItem Clone() => new(Tool, (int[])_xs.Clone(), (int[])_ys.Clone())
    {
        ColorArgb = ColorArgb,
        Width = Width,
    };

    public void Move(int dx, int dy)
    {
        if (dx == 0 && dy == 0)
        {
            return;
        }

        for (var i = 0; i < _xs.Length; i++)
        {
            _xs[i] += dx;
            _ys[i] += dy;
        }
    }

    /// <summary>
    /// Stretch the item so its bounding box becomes <paramref name="box"/>.
    /// Stroke width is deliberately left alone so a resized arrow keeps the same weight.
    /// </summary>
    public void SetBounds(Rectangle box)
    {
        var old = Bounds;
        var oldWidth = Math.Max(1, old.Width);
        var oldHeight = Math.Max(1, old.Height);
        var newWidth = Math.Max(0, box.Width);
        var newHeight = Math.Max(0, box.Height);
        for (var i = 0; i < _xs.Length; i++)
        {
            var tx = (_xs[i] - old.Left) / (double)oldWidth;
            var ty = (_ys[i] - old.Top) / (double)oldHeight;
            _xs[i] = box.Left + (int)Math.Round(tx * newWidth);
            _ys[i] = box.Top + (int)Math.Round(ty * newHeight);
        }
    }

    /// <summary>Squared-aspect helper: shrink a box to the largest centred square inside it.</summary>
    public static Rectangle Squarify(Rectangle box)
    {
        var side = Math.Min(box.Width, box.Height);
        var left = box.Left + (box.Width - side) / 2;
        var top = box.Top + (box.Height - side) / 2;
        return new Rectangle(left, top, side, side);
    }

    /// <summary>The circle/ellipse actually painted inside <see cref="Bounds"/>.</summary>
    public Rectangle ShapeBox
    {
        get
        {
            var box = Bounds;
            return Tool == AnnoTool.Circle ? Squarify(box) : box;
        }
    }

    /// <summary>True when the point is close enough to grab the item.</summary>
    public bool HitTest(Point point, int tolerance)
    {
        var slack = Math.Max(tolerance, (int)Math.Ceiling(Width / 2f) + 1);
        var box = Bounds;

        if (Tool == AnnoTool.Mosaic)
        {
            return Rectangle.Inflate(box, slack, slack).Contains(point);
        }

        if (!Rectangle.Inflate(box, slack, slack).Contains(point))
        {
            return false;
        }

        switch (Tool)
        {
            case AnnoTool.Rect:
            case AnnoTool.RoundRect:
                return NearBorder(box, point, slack);
            case AnnoTool.Ellipse:
            case AnnoTool.Circle:
                return NearEllipse(ShapeBox, point, slack);
            default:
                return NearPolyline(point, slack);
        }
    }

    /// <summary>Which resize handle, if any, sits under the point.</summary>
    public AnnoHandle HandleAt(Point point, int grip)
    {
        var box = Bounds;
        foreach (var (handle, centre) in Handles(box))
        {
            if (Math.Abs(point.X - centre.X) <= grip && Math.Abs(point.Y - centre.Y) <= grip)
            {
                return handle;
            }
        }

        return AnnoHandle.None;
    }

    /// <summary>Handle centres in item coordinates. Order is stable so callers can rely on it.</summary>
    public static IEnumerable<(AnnoHandle Handle, Point Centre)> Handles(Rectangle box)
    {
        var midX = box.Left + (box.Width / 2);
        var midY = box.Top + (box.Height / 2);
        yield return (AnnoHandle.TopLeft, new Point(box.Left, box.Top));
        yield return (AnnoHandle.Top, new Point(midX, box.Top));
        yield return (AnnoHandle.TopRight, new Point(box.Right, box.Top));
        yield return (AnnoHandle.Right, new Point(box.Right, midY));
        yield return (AnnoHandle.BottomRight, new Point(box.Right, box.Bottom));
        yield return (AnnoHandle.Bottom, new Point(midX, box.Bottom));
        yield return (AnnoHandle.BottomLeft, new Point(box.Left, box.Bottom));
        yield return (AnnoHandle.Left, new Point(box.Left, midY));
    }

    /// <summary>Applies a handle drag: <paramref name="delta"/> is in item coordinates.</summary>
    public void DragHandle(AnnoHandle handle, Point delta)
    {
        var box = Bounds;
        int left = box.Left;
        int top = box.Top;
        int right = box.Right;
        int bottom = box.Bottom;
        switch (handle)
        {
            case AnnoHandle.Left:
            case AnnoHandle.TopLeft:
            case AnnoHandle.BottomLeft:
                left += delta.X;
                break;
            case AnnoHandle.Right:
            case AnnoHandle.TopRight:
            case AnnoHandle.BottomRight:
                right += delta.X;
                break;
        }

        switch (handle)
        {
            case AnnoHandle.Top:
            case AnnoHandle.TopLeft:
            case AnnoHandle.TopRight:
                top += delta.Y;
                break;
            case AnnoHandle.Bottom:
            case AnnoHandle.BottomLeft:
            case AnnoHandle.BottomRight:
                bottom += delta.Y;
                break;
        }

        if (handle == AnnoHandle.None || handle == AnnoHandle.Body)
        {
            return;
        }

        // Never let a drag invert the box; a zero-extent box would make SetBounds divide by ~0.
        if (right - left < MinExtent)
        {
            if (handle is AnnoHandle.Left or AnnoHandle.TopLeft or AnnoHandle.BottomLeft)
            {
                left = right - MinExtent;
            }
            else
            {
                right = left + MinExtent;
            }
        }

        if (bottom - top < MinExtent)
        {
            if (handle is AnnoHandle.Top or AnnoHandle.TopLeft or AnnoHandle.TopRight)
            {
                top = bottom - MinExtent;
            }
            else
            {
                bottom = top + MinExtent;
            }
        }

        SetBounds(Rectangle.FromLTRB(left, top, right, bottom));
    }

    /// <summary>Normalises a shape built from two drag corners, forcing a square for circles.</summary>
    public static AnnoItem FromDrag(AnnoTool tool, Point from, Point to)
    {
        if (tool == AnnoTool.Circle)
        {
            var side = Math.Max(Math.Abs(to.X - from.X), Math.Abs(to.Y - from.Y));
            var right = from.X + (to.X >= from.X ? side : -side);
            var bottom = from.Y + (to.Y >= from.Y ? side : -side);
            to = new Point(right, bottom);
        }

        return new AnnoItem(tool, new[] { from.X, to.X }, new[] { from.Y, to.Y });
    }

    /// <summary>Point count below which a freehand stroke is just a stray click.</summary>
    public bool IsDegenerate
    {
        get
        {
            if (IsShape)
            {
                var box = Bounds;
                return box.Width < MinExtent || box.Height < MinExtent;
            }

            if (_xs.Length < 2)
            {
                return true;
            }

            var bounds = Bounds;
            return bounds.Width < MinExtent && bounds.Height < MinExtent;
        }
    }

    private bool NearBorder(Rectangle box, Point point, int slack)
    {
        var insideX = point.X >= box.Left - slack && point.X <= box.Right + slack;
        var insideY = point.Y >= box.Top - slack && point.Y <= box.Bottom + slack;
        if (!insideX || !insideY)
        {
            return false;
        }

        var nearVertical = Math.Abs(point.X - box.Left) <= slack || Math.Abs(point.X - box.Right) <= slack;
        var nearHorizontal = Math.Abs(point.Y - box.Top) <= slack || Math.Abs(point.Y - box.Bottom) <= slack;
        return nearVertical || nearHorizontal;
    }

    private static bool NearEllipse(Rectangle box, Point point, int slack)
    {
        var radiusX = box.Width / 2.0;
        var radiusY = box.Height / 2.0;
        if (radiusX < 1 || radiusY < 1)
        {
            return false;
        }

        var centreX = box.Left + radiusX;
        var centreY = box.Top + radiusY;
        var nx = (point.X - centreX) / radiusX;
        var ny = (point.Y - centreY) / radiusY;
        var distance = Math.Sqrt(nx * nx + ny * ny);
        return Math.Abs(distance - 1.0) * Math.Min(radiusX, radiusY) <= slack;
    }

    private bool NearPolyline(Point point, int slack)
    {
        if (_xs.Length == 1)
        {
            return Squared(_xs[0] - point.X, _ys[0] - point.Y) <= slack * slack;
        }

        for (var i = 1; i < _xs.Length; i++)
        {
            if (DistanceToSegment(point.X, point.Y, _xs[i - 1], _ys[i - 1], _xs[i], _ys[i]) <= slack)
            {
                return true;
            }
        }

        return false;
    }

    private static double DistanceToSegment(int px, int py, int x0, int y0, int x1, int y1)
    {
        var dx = x1 - x0;
        var dy = y1 - y0;
        var lengthSquared = dx * dx + dy * dy;
        if (lengthSquared == 0)
        {
            return Math.Sqrt(Squared(px - x0, py - y0));
        }

        var t = ((px - x0) * dx + (py - y0) * dy) / (double)lengthSquared;
        t = Math.Clamp(t, 0.0, 1.0);
        var nearestX = x0 + t * dx;
        var nearestY = y0 + t * dy;
        return Math.Sqrt(Squared(px - nearestX, py - nearestY));
    }

    private static double Squared(double dx, double dy) => dx * dx + dy * dy;
}
