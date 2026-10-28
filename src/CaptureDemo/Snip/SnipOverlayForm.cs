using System.Drawing.Drawing2D;

namespace CaptureDemo.Snip;

internal sealed class SnipOverlayForm : ScreenOverlayForm
{
    protected override void PaintMask()
    {
        PresentScene((_, _) => { });
    }

    public void ShowDim(Rectangle work, string hint)
    {
        var origin = Bounds.Location;
        PresentScene((surface, graphics) => DrawHint(graphics, surface.Size, work, origin, hint));
    }

    public void ShowHole(Rectangle work, Rectangle holeScreen, string hint)
    {
        var origin = Bounds.Location;
        PresentScene((surface, graphics) =>
        {
            var hole = ToClient(holeScreen, origin, surface.Size);
            if (hole.Width > 1 && hole.Height > 1)
            {
                ClearRect(surface, hole);
                DrawOutsideBorder(graphics, hole, Theme.MaskBorder, 3);
            }

            DrawHint(graphics, surface.Size, work, origin, hint);
        });
    }

    public void ShowLasso(Rectangle work, IReadOnlyList<Point> screenPoints, string hint)
    {
        var origin = Bounds.Location;
        PresentScene((surface, graphics) =>
        {
            if (screenPoints.Count >= 2)
            {
                var local = new Point[screenPoints.Count];
                for (var i = 0; i < screenPoints.Count; i++)
                {
                    local[i] = new Point(screenPoints[i].X - origin.X, screenPoints[i].Y - origin.Y);
                }

                using var pen = new Pen(Theme.LongMaskBorder, 2.2f)
                {
                    StartCap = LineCap.Round,
                    EndCap = LineCap.Round,
                    LineJoin = LineJoin.Round,
                };
                graphics.DrawLines(pen, local);
            }

            DrawHint(graphics, surface.Size, work, origin, hint);
        });
    }

    public void ShowFrozen(Rectangle holeScreen, Bitmap image)
    {
        var origin = Bounds.Location;
        PresentScene((surface, graphics) =>
        {
            var hole = ToClient(holeScreen, origin, surface.Size);
            if (hole.Width > 1 && hole.Height > 1)
            {
                graphics.InterpolationMode = InterpolationMode.NearestNeighbor;
                graphics.DrawImage(image, hole);
                DrawOutsideBorder(graphics, hole, Theme.MaskBorder, 3);
            }
        });
    }

    private void DrawHint(Graphics graphics, Size canvas, Rectangle work, Point origin, string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        var localWork = ToClient(work, origin, canvas);
        if (localWork.Width < 40)
        {
            localWork = new Rectangle(0, 0, canvas.Width, canvas.Height);
        }

        var measured = TextRenderer.MeasureText(text, Font);
        var width = Math.Min(Math.Max(96, localWork.Width - 48), measured.Width + 28);
        const int height = 28;
        var rect = new Rectangle(localWork.Left + (localWork.Width - width) / 2, localWork.Bottom - 64, width, height);
        if (rect.Y < localWork.Top + 64)
        {
            rect.Y = Math.Min(localWork.Bottom - 40, localWork.Top + 64);
        }

        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using var path = PillDraw.Round(rect, height / 2);
        using var brush = new SolidBrush(Color.FromArgb(230, 32, 32, 32));
        graphics.FillPath(brush, path);
        TextRenderer.DrawText(graphics, text, Font, rect, Color.White, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
    }

    private static Rectangle ToClient(Rectangle screen, Point origin, Size canvas)
    {
        var local = new Rectangle(screen.X - origin.X, screen.Y - origin.Y, screen.Width, screen.Height);
        return Rectangle.Intersect(local, new Rectangle(Point.Empty, canvas));
    }
}
