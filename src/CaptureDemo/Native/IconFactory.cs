using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using CaptureDemo.Snip;

namespace CaptureDemo.Native;

/// <summary>
/// Builds a real multi-resolution .ico at runtime instead of handing Windows one
/// 32×32 bitmap to shrink. Classic 32bpp DIB frames are used for the small sizes
/// (widest compatibility) and PNG frames for 128/256 so the file stays small.
/// </summary>
internal static class IconFactory
{
    private static readonly int[] Sizes = { 16, 20, 24, 32, 40, 48, 64, 128, 256 };

    public static Icon TrayIcon() => new(Stream(), SystemInformation.SmallIconSize);

    public static Icon AppIcon() => new(Stream(), SystemInformation.IconSize);

    private static MemoryStream Stream()
    {
        var frames = new List<(int Size, byte[] Data)>();
        foreach (var size in Sizes)
        {
            using var bitmap = DrawTile(size);
            frames.Add((size, size >= 128 ? EncodePng(bitmap) : EncodeDib(bitmap)));
        }

        var buffer = new MemoryStream();
        using var writer = new BinaryWriter(buffer, System.Text.Encoding.UTF8, true);
        writer.Write((ushort)0);
        writer.Write((ushort)1);
        writer.Write((ushort)frames.Count);
        var offset = 6 + (frames.Count * 16);
        foreach (var frame in frames)
        {
            writer.Write((byte)(frame.Size >= 256 ? 0 : frame.Size));
            writer.Write((byte)(frame.Size >= 256 ? 0 : frame.Size));
            writer.Write((byte)0);
            writer.Write((byte)0);
            writer.Write((ushort)1);
            writer.Write((ushort)32);
            writer.Write(frame.Data.Length);
            writer.Write(offset);
            offset += frame.Data.Length;
        }

        foreach (var frame in frames)
        {
            writer.Write(frame.Data);
        }

        writer.Flush();
        buffer.Position = 0;
        return buffer;
    }

    /// <summary>32bpp BGRA DIB plus a zeroed AND mask, the format every Windows version reads.</summary>
    private static byte[] EncodeDib(Bitmap bitmap)
    {
        var width = bitmap.Width;
        var height = bitmap.Height;
        var stride = width * 4;
        var maskStride = (width + 31) / 32 * 4;
        var maskSize = maskStride * height;
        var pixelSize = stride * height;
        var data = new byte[40 + pixelSize + maskSize];
        using var stream = new MemoryStream(data);
        using var writer = new BinaryWriter(stream);
        writer.Write(40);
        writer.Write(width);
        writer.Write(height * 2);
        writer.Write((ushort)1);
        writer.Write((ushort)32);
        writer.Write(0);
        writer.Write(pixelSize);
        writer.Write(0);
        writer.Write(0);
        writer.Write(0);
        writer.Write(0);

        var pixels = new byte[pixelSize];
        var info = new BitmapData();
        bitmap.LockBits(new Rectangle(0, 0, width, height), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb, info);
        try
        {
            for (var y = 0; y < height; y++)
            {
                var source = info.Scan0 + ((height - 1 - y) * info.Stride);
                System.Runtime.InteropServices.Marshal.Copy(source, pixels, y * stride, stride);
            }
        }
        finally
        {
            bitmap.UnlockBits(info);
        }

        writer.Write(pixels);
        writer.Write(new byte[maskSize]);
        writer.Flush();
        return data;
    }

    private static byte[] EncodePng(Bitmap bitmap)
    {
        using var buffer = new MemoryStream();
        bitmap.Save(buffer, ImageFormat.Png);
        return buffer.ToArray();
    }

    private static Bitmap DrawTile(int size)
    {
        var bitmap = new Bitmap(size, size, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(bitmap);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        g.Clear(Color.Transparent);

        var inset = Math.Max(0f, size * 0.03f);
        var tile = new RectangleF(inset, inset, size - (inset * 2), size - (inset * 2));
        var radius = size * 0.26f;
        using (var path = Rounded(tile, radius))
        using (var brush = new LinearGradientBrush(
                   new RectangleF(tile.X, tile.Y, tile.Width, tile.Height + 1),
                   Color.FromArgb(58, 61, 70),
                   Color.FromArgb(20, 22, 26),
                   LinearGradientMode.Vertical))
        {
            g.FillPath(brush, path);
        }

        if (size >= 32)
        {
            using var inner = Rounded(RectangleF.Inflate(tile, -1.2f, -1.2f), radius - 1.2f);
            using var rim = new Pen(Color.FromArgb(58, 255, 255, 255), Math.Max(1f, size / 48f));
            g.DrawPath(rim, inner);
        }

        var mark = new RectangleF(tile.X, tile.Y, tile.Width, tile.Height);
        SnipIcons.CaptureMark(g, mark, Color.FromArgb(245, 247, 250), size <= 20 ? 2.15f : 1.95f);
        return bitmap;
    }

    private static GraphicsPath Rounded(RectangleF bounds, float radius)
    {
        var path = new GraphicsPath();
        var d = Math.Max(1f, radius * 2f);
        if (bounds.Width <= d || bounds.Height <= d)
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
}
