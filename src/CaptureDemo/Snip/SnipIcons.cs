using System.Drawing.Drawing2D;

namespace CaptureDemo.Snip;

internal static class SnipIcons
{
    public static void Fullscreen(Graphics g, Rectangle r, Color color)
    {
        var box = Inset(r, 1);
        using var pen = Stroke(color, r);
        var screen = new RectangleF(box.Left, box.Top, box.Width, box.Height * 0.72f);
        using var path = Round(screen, Radius(r));
        g.DrawPath(pen, path);
        var mid = box.Left + box.Width / 2f;
        var standTop = screen.Bottom;
        g.DrawLine(pen, mid, standTop, mid, box.Bottom - 0.5f);
        g.DrawLine(pen, mid - box.Width * 0.22f, box.Bottom - 0.5f, mid + box.Width * 0.22f, box.Bottom - 0.5f);
    }

    public static void Rect(Graphics g, Rectangle r, Color color)
    {
        var box = Inset(r, 2);
        using var pen = Stroke(color, r);
        using var path = Round(box, Radius(r) + 1f);
        g.DrawPath(pen, path);
    }

    public static void Window(Graphics g, Rectangle r, Color color)
    {
        var box = Inset(r, 1);
        using var pen = Stroke(color, r);
        using var path = Round(box, Radius(r));
        g.DrawPath(pen, path);
        var y = box.Top + box.Height * 0.34f;
        g.DrawLine(pen, box.Left + 1.2f, y, box.Right - 1.2f, y);
    }

    public static void Long(Graphics g, Rectangle r, Color color)
    {
        var box = Inset(r, 1);
        using var pen = Stroke(color, r);
        var page = new RectangleF(box.Left + box.Width * 0.18f, box.Top, box.Width * 0.64f, box.Height * 0.62f);
        using (var path = Round(page, Radius(r)))
        {
            g.DrawPath(pen, path);
        }

        var mid = box.Left + box.Width / 2f;
        var tail = box.Bottom - 0.4f;
        g.DrawLine(pen, mid, page.Bottom + 0.6f, mid, tail - box.Height * 0.16f);
        g.DrawLine(pen, mid - box.Width * 0.16f, tail - box.Height * 0.2f, mid, tail);
        g.DrawLine(pen, mid + box.Width * 0.16f, tail - box.Height * 0.2f, mid, tail);
    }

    public static void Element(Graphics g, Rectangle r, Color color)
    {
        var box = Inset(r, 1);
        using var pen = Stroke(color, r);
        var tile = box.Width * 0.46f;
        using (var path = Round(new RectangleF(box.Left, box.Top, tile, tile), Radius(r)))
        {
            g.DrawPath(pen, path);
        }

        var x0 = box.Left + tile * 0.72f;
        var y0 = box.Top + tile * 0.72f;
        g.DrawLine(pen, x0, y0, box.Right - 0.4f, box.Bottom - 0.4f);
    }

    public static void Freeform(Graphics g, Rectangle r, Color color)
    {
        var box = Inset(r, 1);
        using var pen = Stroke(color, r);
        g.DrawBezier(
            pen,
            box.Left + box.Width * 0.08f,
            box.Bottom - box.Height * 0.18f,
            box.Left + box.Width * 0.05f,
            box.Top + box.Height * 0.05f,
            box.Left + box.Width * 0.55f,
            box.Top,
            box.Right - box.Width * 0.08f,
            box.Top + box.Height * 0.28f);
        g.DrawBezier(
            pen,
            box.Right - box.Width * 0.08f,
            box.Top + box.Height * 0.28f,
            box.Right,
            box.Top + box.Height * 0.62f,
            box.Left + box.Width * 0.62f,
            box.Bottom,
            box.Left + box.Width * 0.22f,
            box.Bottom - box.Height * 0.08f);
    }

    public static void Gear(Graphics g, Rectangle r, Color color)
    {
        var box = Inset(r, 1);
        using var pen = Stroke(color, r);
        var c = new PointF(box.Left + box.Width / 2f, box.Top + box.Height / 2f);
        var outer = box.Width * 0.22f;
        g.DrawEllipse(pen, c.X - outer, c.Y - outer, outer * 2f, outer * 2f);
        for (var i = 0; i < 8; i++)
        {
            var a = i * Math.PI / 4;
            var inner = box.Width * 0.30f;
            var reach = box.Width * 0.46f;
            g.DrawLine(
                pen,
                c.X + (float)Math.Cos(a) * inner,
                c.Y + (float)Math.Sin(a) * inner,
                c.X + (float)Math.Cos(a) * reach,
                c.Y + (float)Math.Sin(a) * reach);
        }
    }

    public static void Pen(Graphics g, Rectangle r, Color color)
    {
        var box = Inset(r, 1);
        using var pen = Stroke(color, r);
        g.DrawLine(pen, box.Left + box.Width * 0.18f, box.Bottom - box.Height * 0.16f, box.Right - box.Width * 0.12f, box.Top + box.Height * 0.14f);
    }

    public static void Arrow(Graphics g, Rectangle r, Color color)
    {
        var box = Inset(r, 1);
        using var pen = Stroke(color, r);
        var x1 = box.Right - box.Width * 0.16f;
        var y1 = box.Top + box.Height * 0.18f;
        g.DrawLine(pen, box.Left + box.Width * 0.16f, box.Bottom - box.Height * 0.16f, x1, y1);
        g.DrawLine(pen, x1 - box.Width * 0.28f, y1, x1, y1);
        g.DrawLine(pen, x1, y1, x1, y1 + box.Height * 0.28f);
    }

    public static void Highlight(Graphics g, Rectangle r, Color color)
    {
        var box = Inset(r, 2);
        using var fill = new SolidBrush(Color.FromArgb(150, 250, 204, 21));
        using var path = Round(box, Radius(r) + 1f);
        g.FillPath(fill, path);
        using var pen = Stroke(color, r);
        g.DrawPath(pen, path);
    }

    public static void Mosaic(Graphics g, Rectangle r, Color color)
    {
        var box = Inset(r, 1);
        using var brush = new SolidBrush(color);
        var cell = box.Width / 3.4f;
        var gap = cell * 0.22f;
        for (var y = 0; y < 3; y++)
        {
            for (var x = 0; x < 3; x++)
            {
                if (((x + y) & 1) != 0)
                {
                    continue;
                }

                var cellRect = new RectangleF(box.Left + x * cell, box.Top + y * cell, cell - gap, cell - gap);
                using var path = Round(cellRect, Math.Max(1f, cell * 0.22f));
                g.FillPath(brush, path);
            }
        }
    }

    public static void Undo(Graphics g, Rectangle r, Color color)
    {
        var box = Inset(r, 1);
        using var pen = Stroke(color, r);
        var arc = new RectangleF(box.Left + box.Width * 0.16f, box.Top + box.Height * 0.18f, box.Width * 0.68f, box.Height * 0.68f);
        g.DrawArc(pen, arc.X, arc.Y, arc.Width, arc.Height, 200, 230);
        var tipX = arc.Left + arc.Width * 0.18f;
        var tipY = arc.Top + arc.Height * 0.42f;
        g.DrawLine(pen, tipX, tipY, tipX + box.Width * 0.16f, tipY - box.Height * 0.16f);
        g.DrawLine(pen, tipX, tipY, tipX + box.Width * 0.02f, tipY + box.Height * 0.2f);
    }

    public static void Confirm(Graphics g, Rectangle r, Color color)
    {
        var box = Inset(r, 2);
        using var pen = Stroke(color, r);
        g.DrawLines(pen, new[]
        {
            new PointF(box.Left + box.Width * 0.12f, box.Top + box.Height * 0.52f),
            new PointF(box.Left + box.Width * 0.40f, box.Bottom - box.Height * 0.16f),
            new PointF(box.Right - box.Width * 0.08f, box.Top + box.Height * 0.18f),
        });
    }

    public static void Discard(Graphics g, Rectangle r, Color color)
    {
        var box = Inset(r, 3);
        using var pen = Stroke(color, r);
        g.DrawLine(pen, box.Left, box.Top, box.Right, box.Bottom);
        g.DrawLine(pen, box.Right, box.Top, box.Left, box.Bottom);
    }

    private static Pen Stroke(Color color, Rectangle r)
    {
        var width = Math.Max(1.35f, Math.Min(r.Width, r.Height) / 9f);
        return new Pen(color, width)
        {
            StartCap = LineCap.Round,
            EndCap = LineCap.Round,
            LineJoin = LineJoin.Round,
        };
    }

    private static GraphicsPath Round(RectangleF bounds, float radius)
    {
        var path = new GraphicsPath();
        var diameter = Math.Max(2f, radius * 2f);
        if (bounds.Width <= diameter || bounds.Height <= diameter)
        {
            path.AddRectangle(bounds);
            return path;
        }

        path.AddArc(bounds.Left, bounds.Top, diameter, diameter, 180, 90);
        path.AddArc(bounds.Right - diameter, bounds.Top, diameter, diameter, 270, 90);
        path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0, 90);
        path.AddArc(bounds.Left, bounds.Bottom - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();
        return path;
    }

    private static RectangleF Inset(Rectangle r, int n) => new(r.Left + n, r.Top + n, Math.Max(4, r.Width - n * 2), Math.Max(4, r.Height - n * 2));

    private static float Radius(Rectangle r) => Math.Max(1.5f, Math.Min(r.Width, r.Height) / 7f);
}
