namespace CaptureDemo.Capture;

internal static class OverlayPainter
{
    public static void DrawBanner(Graphics graphics, Size canvas, Font font, string line1, string? line2, Rectangle? avoid = null)
    {
        var hasSecond = !string.IsNullOrWhiteSpace(line2);
        var width = Math.Min(Math.Max(120, canvas.Width - 32), 840);
        var height = hasSecond ? 68 : 42;
        var rect = BannerRect(canvas, width, height, avoid);
        using var brush = new SolidBrush(Theme.Banner);
        graphics.FillRectangle(brush, rect);
        using var accent = new SolidBrush(Theme.Accent);
        graphics.FillRectangle(accent, rect.X, rect.Y, 4, rect.Height);

        using var format = new StringFormat
        {
            Alignment = StringAlignment.Near,
            LineAlignment = StringAlignment.Center,
            Trimming = StringTrimming.EllipsisCharacter,
            FormatFlags = StringFormatFlags.NoWrap,
        };
        using var white = new SolidBrush(Color.White);
        var first = new RectangleF(rect.X + 16, rect.Y + 6, rect.Width - 24, hasSecond ? 26 : rect.Height - 12);
        graphics.DrawString(line1, font, white, first, format);
        if (!hasSecond || line2 == null)
        {
            return;
        }

        using var hint = new SolidBrush(Theme.BannerHint);
        var second = new RectangleF(rect.X + 16, rect.Y + 34, rect.Width - 24, 28);
        graphics.DrawString(line2, font, hint, second, format);
    }

    private static Rectangle BannerRect(Size canvas, int width, int height, Rectangle? avoid)
    {
        var top = new Rectangle(Math.Max(8, (canvas.Width - width) / 2), 12, width, height);
        if (avoid is not Rectangle hole || hole.Width < 2 || hole.Height < 2 || !top.IntersectsWith(hole))
        {
            return top;
        }

        var bottom = new Rectangle(top.X, Math.Max(8, canvas.Height - height - 12), width, height);
        if (!bottom.IntersectsWith(hole))
        {
            return bottom;
        }

        if (hole.X >= width + 24)
        {
            var left = new Rectangle(12, Math.Max(12, hole.Y), Math.Min(width, hole.X - 20), height);
            if (left.Width >= 160 && !left.IntersectsWith(hole))
            {
                return left;
            }
        }

        var rightX = hole.Right + 12;
        if (canvas.Width - rightX >= 180)
        {
            var right = new Rectangle(rightX, Math.Max(12, hole.Y), Math.Min(width, canvas.Width - rightX - 12), height);
            if (right.Width >= 160 && !right.IntersectsWith(hole))
            {
                return right;
            }
        }

        // Full-screen selection: keep one thin status line on the dimmed pixels that still exist,
        // otherwise along the top edge. It is excluded from the capture.
        return top;
    }

    public static void DrawCaption(Graphics graphics, Rectangle hole, Size canvas, Color border, Font font, string? caption)
    {
        if (string.IsNullOrWhiteSpace(caption))
        {
            return;
        }

        var measure = graphics.MeasureString(caption, font, 640);
        var boxWidth = (int)Math.Min(Math.Max(hole.Width, 80), measure.Width + 16);
        var boxHeight = (int)Math.Ceiling(measure.Height) + 8;
        var y = hole.Y - boxHeight - 6;
        if (y < 8)
        {
            y = hole.Bottom + 6;
        }

        if (y + boxHeight > canvas.Height - 4)
        {
            y = hole.Y + 6;
        }

        var x = hole.X;
        if (x + boxWidth > canvas.Width - 4)
        {
            x = Math.Max(4, canvas.Width - boxWidth - 4);
        }

        var box = new Rectangle(x, y, boxWidth, boxHeight);
        using var brush = new SolidBrush(border);
        graphics.FillRectangle(brush, box);
        using var white = new SolidBrush(Color.White);
        using var format = new StringFormat
        {
            Alignment = StringAlignment.Center,
            LineAlignment = StringAlignment.Center,
            Trimming = StringTrimming.EllipsisCharacter,
            FormatFlags = StringFormatFlags.NoWrap,
        };
        graphics.DrawString(caption, font, white, box, format);
    }
}
