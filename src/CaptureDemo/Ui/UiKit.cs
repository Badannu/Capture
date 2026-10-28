using System.Drawing.Drawing2D;

namespace CaptureDemo.Ui;

/// <summary>Shared drawing primitives so every surface shares one corner radius language.</summary>
internal static class UiKit
{
    public static GraphicsPath Rounded(RectangleF bounds, float radius)
    {
        var path = new GraphicsPath();
        var d = Math.Max(1f, Math.Min(radius * 2f, Math.Min(bounds.Width, bounds.Height)));
        if (bounds.Width < d || bounds.Height < d)
        {
            path.AddRectangle(bounds);
            return path;
        }

        path.AddArc(bounds.Left, bounds.Top, d, d, 180, 90);
        path.AddArc(bounds.Right - d, bounds.Top, d, d, 270, 90);
        path.AddArc(bounds.Right - d, bounds.Bottom - d, d, d, 0, 90);
        path.AddArc(bounds.Left, bounds.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }

    public static void Fill(Graphics g, RectangleF bounds, float radius, Color color)
    {
        using var path = Rounded(bounds, radius);
        using var brush = new SolidBrush(color);
        g.FillPath(brush, path);
    }

    public static void Stroke(Graphics g, RectangleF bounds, float radius, Color color, float width = 1f)
    {
        using var path = Rounded(Deflate(bounds, width / 2f), radius);
        using var pen = new Pen(color, width);
        g.DrawPath(pen, path);
    }

    public static void FillAndStroke(Graphics g, RectangleF bounds, float radius, Color fill, Color border, float width = 1f)
    {
        using var path = Rounded(bounds, radius);
        using var brush = new SolidBrush(fill);
        g.FillPath(brush, path);
        using var pen = new Pen(border, width);
        g.DrawPath(pen, path);
    }

    /// <summary>Cheap soft shadow: stacked translucent rounded rects, no GDI+ blur available.</summary>
    public static void Shadow(Graphics g, RectangleF bounds, float radius, int spread, byte alpha)
    {
        if (spread <= 0 || alpha == 0)
        {
            return;
        }

        for (var i = spread; i >= 1; i--)
        {
            var step = i / (float)spread;
            var a = (int)(alpha * (1f - step) * (1f - step));
            if (a <= 0)
            {
                continue;
            }

            var box = RectangleF.Inflate(bounds, i, i);
            box.Offset(0f, i * 0.35f);
            using var path = Rounded(box, radius + i);
            using var brush = new SolidBrush(Color.FromArgb(a, 0, 0, 0));
            g.FillPath(brush, path);
        }
    }

    public static RectangleF Deflate(RectangleF bounds, float amount) =>
        RectangleF.Inflate(bounds, -amount, -amount);

    public static Color Mix(Color from, Color to, double amount)
    {
        var t = Math.Clamp(amount, 0d, 1d);
        return Color.FromArgb(
            (int)Math.Round(from.A + ((to.A - from.A) * t)),
            (int)Math.Round(from.R + ((to.R - from.R) * t)),
            (int)Math.Round(from.G + ((to.G - from.G) * t)),
            (int)Math.Round(from.B + ((to.B - from.B) * t)));
    }

    public static Color Alpha(Color color, int alpha) => Color.FromArgb(Math.Clamp(alpha, 0, 255), color);

    public static void DrawGlyph(Graphics g, Action<Graphics, Rectangle, Color> glyph, Rectangle box, Color color)
    {
        var previous = g.SmoothingMode;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        glyph(g, box, color);
        g.SmoothingMode = previous;
    }

    /// <summary>
    /// Menu items take an Image, so glyphs must be rasterised. Size is passed in device
    /// pixels: a fixed 18 px bitmap looked undersized next to DPI-scaled menu text.
    /// </summary>
    public static Bitmap GlyphBitmap(Action<Graphics, Rectangle, Color> glyph, Color color, int side)
    {
        var bitmap = new Bitmap(side, side, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(bitmap);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        var inset = Math.Max(1, side / 12);
        glyph(g, new Rectangle(inset, inset, side - (inset * 2), side - (inset * 2)), color);
        return bitmap;
    }

    public static string Elide(string text, Font font, int width)
    {
        if (string.IsNullOrEmpty(text) || width <= 0)
        {
            return text;
        }

        if (TextRenderer.MeasureText(text, font).Width <= width)
        {
            return text;
        }

        const string Ellipsis = "…";
        var lo = 0;
        var hi = text.Length;
        while (lo < hi)
        {
            var mid = (lo + hi + 1) / 2;
            if (TextRenderer.MeasureText(text[..mid] + Ellipsis, font).Width <= width)
            {
                lo = mid;
            }
            else
            {
                hi = mid - 1;
            }
        }

        return lo <= 0 ? Ellipsis : text[..lo] + Ellipsis;
    }

    public static string RelativeTime(DateTime utc)
    {
        var span = DateTime.UtcNow - utc;
        if (span.TotalSeconds < 45)
        {
            return "刚刚";
        }

        if (span.TotalMinutes < 60)
        {
            return $"{(int)span.TotalMinutes} 分钟前";
        }

        if (span.TotalHours < 24)
        {
            return $"{(int)span.TotalHours} 小时前";
        }

        if (span.TotalDays < 7)
        {
            return $"{(int)span.TotalDays} 天前";
        }

        return utc.ToLocalTime().ToString("MM-dd HH:mm");
    }
}
