using System.Drawing.Imaging;
using CaptureDemo.Native;

namespace CaptureDemo.Capture;

internal static class ScreenRects
{
    public static Rectangle Normalize(Point a, Point b)
    {
        var x = Math.Min(a.X, b.X);
        var y = Math.Min(a.Y, b.Y);
        return new Rectangle(x, y, Math.Abs(a.X - b.X), Math.Abs(a.Y - b.Y));
    }

    public static int DistanceSquared(Point a, Point b)
    {
        var dx = a.X - b.X;
        var dy = a.Y - b.Y;
        return dx * dx + dy * dy;
    }
}

internal static class ScreenGrabber
{
    public const long MaxPixels = 40_000_000;

    public static Bitmap Grab(Rectangle screenRect)
    {
        var rect = Rectangle.Intersect(screenRect, NativeMethods.VirtualScreen());
        if (rect.Width < 1 || rect.Height < 1)
        {
            throw new InvalidOperationException("截取范围不在屏幕内。");
        }

        if ((long)rect.Width * rect.Height > MaxPixels)
        {
            throw new InvalidOperationException("截取范围太大。");
        }

        var bitmap = new Bitmap(rect.Width, rect.Height, PixelFormat.Format32bppArgb);
        try
        {
            using var graphics = Graphics.FromImage(bitmap);
            graphics.CopyFromScreen(rect.Left, rect.Top, 0, 0, rect.Size, CopyPixelOperation.SourceCopy);
            return bitmap;
        }
        catch
        {
            bitmap.Dispose();
            throw;
        }
    }
}
