namespace CaptureDemo.LongShot;

/// <summary>
/// Picks a point that should receive the wheel. A full-desktop selection's geometric center
/// can land on the taskbar or a ribbon; the work area and any detected motion band avoid that.
/// </summary>
internal static class ScrollAim
{
    private static readonly double[] HeightFractions = { 0.42, 0.30, 0.62, 0.50 };

    public static Point Next(Rectangle region, Rectangle work, int attempt, Rectangle? motion)
    {
        Point raw;
        if (motion is Rectangle band && band.Width >= 24 && band.Height >= 48)
        {
            raw = new Point(band.X + band.Width / 2, band.Y + band.Height / 2);
        }
        else
        {
            var fraction = HeightFractions[Math.Abs(attempt) % HeightFractions.Length];
            raw = new Point(
                region.X + region.Width / 2,
                region.Y + (int)Math.Round(region.Height * fraction));
        }

        var box = Rectangle.Intersect(region, work);
        if (box.Width < 24 || box.Height < 24)
        {
            box = region;
        }

        var inset = 8;
        if (box.Width <= inset * 2 || box.Height <= inset * 2)
        {
            return new Point(box.X + box.Width / 2, box.Y + box.Height / 2);
        }

        var x = Math.Clamp(raw.X, box.Left + inset, box.Right - inset - 1);
        var y = Math.Clamp(raw.Y, box.Top + inset, box.Bottom - inset - 1);
        return new Point(x, y);
    }
}
