using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using CaptureDemo.Core;

namespace CaptureDemo.Capture;

internal static class BgraConvert
{
    public static BgraBuffer FromBitmap(Bitmap bitmap)
    {
        var rect = new Rectangle(0, 0, bitmap.Width, bitmap.Height);
        var data = bitmap.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        try
        {
            var pixels = new byte[bitmap.Width * bitmap.Height * 4];
            var rowBytes = bitmap.Width * 4;
            for (var y = 0; y < bitmap.Height; y++)
            {
                Marshal.Copy(data.Scan0 + y * data.Stride, pixels, y * rowBytes, rowBytes);
            }

            return new BgraBuffer(bitmap.Width, bitmap.Height, pixels);
        }
        finally
        {
            bitmap.UnlockBits(data);
        }
    }

    public static Bitmap ToBitmap(BgraBuffer buffer)
    {
        var bitmap = new Bitmap(buffer.Width, buffer.Height, PixelFormat.Format32bppArgb);
        var rect = new Rectangle(0, 0, buffer.Width, buffer.Height);
        var data = bitmap.LockBits(rect, ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
        try
        {
            var rowBytes = buffer.Width * 4;
            for (var y = 0; y < buffer.Height; y++)
            {
                Marshal.Copy(buffer.Pixels, y * rowBytes, data.Scan0 + y * data.Stride, rowBytes);
            }
        }
        catch
        {
            bitmap.UnlockBits(data);
            bitmap.Dispose();
            throw;
        }

        bitmap.UnlockBits(data);
        return bitmap;
    }
}
