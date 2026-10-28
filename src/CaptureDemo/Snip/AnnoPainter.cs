using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using CaptureDemo.Core;

namespace CaptureDemo.Snip;

/// <summary>
/// Preset ink colours. A full picker is overkill for a snip; eight readable hues cover it.
/// </summary>
internal static class AnnoPalette
{
    public const int Default = 0;

    public static readonly Color[] Colors =
    {
        Color.FromArgb(0xF2, 0x48, 0x48), // 红
        Color.FromArgb(0xFF, 0x8A, 0x1E), // 橙
        Color.FromArgb(0xFF, 0xC5, 0x3D), // 黄
        Color.FromArgb(0x34, 0xC7, 0x59), // 绿
        Color.FromArgb(0x2E, 0x8B, 0xFF), // 蓝
        Color.FromArgb(0xA8, 0x55, 0xF7), // 紫
        Color.FromArgb(0xFF, 0xFF, 0xFF), // 白
        Color.FromArgb(0x2E, 0x2E, 0x30), // 黑
    };

    public static readonly string[] Names = { "红", "橙", "黄", "绿", "蓝", "紫", "白", "黑" };

    /// <summary>Stroke weights at 96 dpi. Multiplied by the capture scale on use.</summary>
    public static readonly float[] Weights = { 2f, 4f, 6.5f, 10f };

    public static readonly string[] WeightNames = { "细", "中", "粗", "特粗" };

    public static int IndexOfColor(int argb)
    {
        for (var i = 0; i < Colors.Length; i++)
        {
            if (Colors[i].ToArgb() == argb)
            {
                return i;
            }
        }

        return -1;
    }

    public static int IndexOfWeight(float width)
    {
        for (var i = 0; i < Weights.Length; i++)
        {
            if (Math.Abs(Weights[i] - width) < 0.01f)
            {
                return i;
            }
        }

        return -1;
    }
}

/// <summary>
/// GDI+ renderer shared by the live overlay and the final export, so what the user
/// drags is exactly what lands in the PNG.
/// </summary>
internal static class AnnoPainter
{
    /// <summary>Pixel size of a mosaic block in the source image.</summary>
    public const int MosaicBlock = 12;

    /// <summary>Handle side in 96-dpi units, before per-capture scaling.</summary>
    private const float HandleSize = 9f;

    public static void Draw(Graphics g, AnnoItem item, Bitmap? source)
    {
        switch (item.Tool)
        {
            case AnnoTool.Mosaic:
                DrawMosaic(g, item, source);
                return;
            case AnnoTool.Highlight:
                DrawHighlight(g, item);
                return;
        }

        var state = Begin(g);
        try
        {
            using var pen = MakePen(item);
            switch (item.Tool)
            {
                case AnnoTool.Pen:
                    DrawPen(g, pen, item);
                    break;
                case AnnoTool.Line:
                    g.DrawLine(pen, item.Start, item.End);
                    break;
                case AnnoTool.Arrow:
                    DrawArrow(g, pen, item);
                    break;
                case AnnoTool.Rect:
                    g.DrawRectangle(pen, Normalize(item.Bounds));
                    break;
                case AnnoTool.RoundRect:
                    DrawRoundRect(g, pen, item.Bounds);
                    break;
                case AnnoTool.Ellipse:
                case AnnoTool.Circle:
                    var box = Normalize(item.ShapeBox);
                    g.DrawEllipse(pen, box);
                    break;
            }
        }
        finally
        {
            End(g, state);
        }
    }

    /// <summary>Dashed box plus eight grips, drawn in image pixels scaled to the screen.</summary>
    public static void DrawSelection(Graphics g, AnnoItem item, float scale)
    {
        var box = Rectangle.Inflate(item.Bounds, 3, 3);
        var previous = g.SmoothingMode;
        g.SmoothingMode = SmoothingMode.AntiAlias;

        using (var dark = new Pen(Color.FromArgb(150, 0, 0, 0), 1f) { DashStyle = DashStyle.Dash, DashPattern = new[] { 3f, 3f } })
        using (var light = new Pen(Color.FromArgb(220, 255, 255, 255), 1f) { DashStyle = DashStyle.Dash, DashPattern = new[] { 3f, 3f }, DashOffset = 3f })
        {
            g.DrawRectangle(dark, box);
            g.DrawRectangle(light, box);
        }

        var side = Math.Max(5f, HandleSize * scale);
        var half = side / 2f;
        using var fill = new SolidBrush(Color.White);
        using var edge = new Pen(Color.FromArgb(190, 20, 20, 20), Math.Max(1f, scale));
        foreach (var (handle, centre) in AnnoItem.Handles(item.Bounds))
        {
            if (handle == AnnoHandle.None)
            {
                continue;
            }

            var r = new RectangleF(centre.X - half, centre.Y - half, side, side);
            g.FillRectangle(fill, r);
            g.DrawRectangle(edge, r.X, r.Y, r.Width, r.Height);
        }

        g.SmoothingMode = previous;
    }

    private static void DrawPen(Graphics g, Pen pen, AnnoItem item)
    {
        var xs = item.Xs;
        var ys = item.Ys;
        if (xs.Length == 1)
        {
            using var brush = new SolidBrush(pen.Color);
            var radius = pen.Width / 2f;
            g.FillEllipse(brush, xs[0] - radius, ys[0] - radius, pen.Width, pen.Width);
            return;
        }

        var points = new PointF[xs.Length];
        for (var i = 0; i < xs.Length; i++)
        {
            points[i] = new PointF(xs[i], ys[i]);
        }

        g.DrawLines(pen, points);
    }

    private static void DrawArrow(Graphics g, Pen pen, AnnoItem item)
    {
        var from = item.Start;
        var to = item.End;
        g.DrawLine(pen, from, to);

        var dx = to.X - from.X;
        var dy = to.Y - from.Y;
        var length = Math.Sqrt(dx * dx + dy * dy);
        if (length < 1)
        {
            return;
        }

        var ux = dx / length;
        var uy = dy / length;
        var head = Math.Min(Math.Max(pen.Width * 4.2f, 11f), (float)length * 0.55f);
        var backX = to.X - ux * head;
        var backY = to.Y - uy * head;
        var wingX = -uy * head * 0.52f;
        var wingY = ux * head * 0.52f;
        var tip = new PointF(to.X, to.Y);
        var left = new PointF((float)(backX + wingX), (float)(backY + wingY));
        var right = new PointF((float)(backX - wingX), (float)(backY - wingY));
        using var brush = new SolidBrush(pen.Color);
        var state = g.SmoothingMode;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.FillPolygon(brush, new[] { tip, left, right });
        g.SmoothingMode = state;
    }

    private static void DrawRoundRect(Graphics g, Pen pen, Rectangle box)
    {
        box = Normalize(box);
        if (box.Width < 2 || box.Height < 2)
        {
            return;
        }

        var radius = Math.Min(Math.Min(box.Width, box.Height) * 0.22f, pen.Width * 4f + 4f);
        using var path = Rounded(box, radius);
        g.DrawPath(pen, path);
    }

    private static void DrawHighlight(Graphics g, AnnoItem item)
    {
        var width = Math.Max(item.Width * 3.4f, 10f);
        using var pen = new Pen(Color.FromArgb(96, Color.FromArgb(item.ColorArgb)), width)
        {
            StartCap = LineCap.Round,
            EndCap = LineCap.Round,
            LineJoin = LineJoin.Round,
        };
        var preserve = g.CompositingMode;
        g.CompositingMode = CompositingMode.SourceOver;
        var xs = item.Xs;
        var ys = item.Ys;
        if (xs.Length == 1)
        {
            var radius = width / 2f;
            using var brush = new SolidBrush(pen.Color);
            g.FillEllipse(brush, xs[0] - radius, ys[0] - radius, width, width);
        }
        else
        {
            var points = new PointF[xs.Length];
            for (var i = 0; i < xs.Length; i++)
            {
                points[i] = new PointF(xs[i], ys[i]);
            }

            g.DrawLines(pen, points);
        }

        g.CompositingMode = preserve;
    }

    /// <summary>
    /// Downscale the region then blow it back up with nearest-neighbour: cheap, stable,
    /// and it reads as a blur block rather than noise.
    /// </summary>
    private static void DrawMosaic(Graphics g, AnnoItem item, Bitmap? source)
    {
        var box = Rectangle.Intersect(Normalize(item.Bounds), new Rectangle(Point.Empty, source?.Size ?? g.VisibleClipBounds.Size.ToSize()));
        if (source == null || box.Width < 2 || box.Height < 2)
        {
            return;
        }

        var smallWidth = Math.Max(1, box.Width / MosaicBlock);
        var smallHeight = Math.Max(1, box.Height / MosaicBlock);
        using var small = new Bitmap(smallWidth, smallHeight, PixelFormat.Format32bppPArgb);
        using (var sg = Graphics.FromImage(small))
        {
            sg.InterpolationMode = InterpolationMode.HighQualityBilinear;
            sg.PixelOffsetMode = PixelOffsetMode.HighQuality;
            sg.CompositingMode = CompositingMode.SourceCopy;
            sg.DrawImage(source, new Rectangle(0, 0, smallWidth, smallHeight), box, GraphicsUnit.Pixel);
        }

        var interpolation = g.InterpolationMode;
        var offset = g.PixelOffsetMode;
        g.InterpolationMode = InterpolationMode.NearestNeighbor;
        g.PixelOffsetMode = PixelOffsetMode.Half;
        g.DrawImage(small, box);
        g.InterpolationMode = interpolation;
        g.PixelOffsetMode = offset;
    }

    private static Pen MakePen(AnnoItem item)
    {
        var pen = new Pen(Color.FromArgb(item.ColorArgb), Math.Max(1f, item.Width))
        {
            StartCap = LineCap.Round,
            EndCap = LineCap.Round,
            LineJoin = LineJoin.Round,
            MiterLimit = 2f,
        };
        return pen;
    }

    /// <summary>Half-pixel alignment keeps 1-2px strokes from smearing across two rows.</summary>
    private static Rectangle Normalize(Rectangle box)
    {
        if (box.Width <= 0)
        {
            box.Width = 1;
        }

        if (box.Height <= 0)
        {
            box.Height = 1;
        }

        return box;
    }

    private static GraphicsState Begin(Graphics g)
    {
        var state = g.Save();
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        g.CompositingMode = CompositingMode.SourceOver;
        g.CompositingQuality = CompositingQuality.HighSpeed;
        return state;
    }

    private static void End(Graphics g, GraphicsState state) => g.Restore(state);

    private static GraphicsPath Rounded(Rectangle box, float radius)
    {
        var path = new GraphicsPath();
        var d = Math.Max(2f, radius * 2f);
        if (box.Width < d || box.Height < d)
        {
            path.AddRectangle(box);
            return path;
        }

        path.AddArc(box.Left, box.Top, d, d, 180, 90);
        path.AddArc(box.Right - d, box.Top, d, d, 270, 90);
        path.AddArc(box.Right - d, box.Bottom - d, d, d, 0, 90);
        path.AddArc(box.Left, box.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }
}
