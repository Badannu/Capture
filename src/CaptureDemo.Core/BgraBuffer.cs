namespace CaptureDemo.Core;

/// <summary>
/// Tightly packed 32-bit BGRA pixels. Row stride is <c>Width * 4</c>.
/// </summary>
public sealed class BgraBuffer
{
    public BgraBuffer(int width, int height, byte[] pixels)
    {
        if (width <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(width));
        }

        if (height <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(height));
        }

        ArgumentNullException.ThrowIfNull(pixels);
        if (pixels.Length != checked(width * height * 4))
        {
            throw new ArgumentException("Pixel length must be width * height * 4.", nameof(pixels));
        }

        Width = width;
        Height = height;
        Pixels = pixels;
    }

    public int Width { get; }

    public int Height { get; }

    public byte[] Pixels { get; }

    public BgraBuffer CropHeight(int height)
    {
        if (height <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(height));
        }

        if (height >= Height)
        {
            return this;
        }

        var pixels = new byte[Width * height * 4];
        Buffer.BlockCopy(Pixels, 0, pixels, 0, pixels.Length);
        return new BgraBuffer(Width, height, pixels);
    }

    /// <summary>A contiguous horizontal band. Returns this instance when the band is the whole image.</summary>
    public BgraBuffer SliceRows(int startRow, int rowCount)
    {
        if (startRow < 0 || rowCount <= 0 || startRow > Height - rowCount)
        {
            throw new ArgumentOutOfRangeException(nameof(startRow));
        }

        if (startRow == 0 && rowCount == Height)
        {
            return this;
        }

        var pixels = new byte[Width * rowCount * 4];
        Buffer.BlockCopy(Pixels, startRow * Width * 4, pixels, 0, pixels.Length);
        return new BgraBuffer(Width, rowCount, pixels);
    }

    public BgraBuffer Crop(int x, int y, int width, int height)
    {
        if (x < 0 || y < 0 || width <= 0 || height <= 0 || x > Width - width || y > Height - height)
        {
            throw new ArgumentOutOfRangeException(nameof(width));
        }

        if (x == 0 && y == 0 && width == Width && height == Height)
        {
            return this;
        }

        var pixels = new byte[width * height * 4];
        var dstStride = width * 4;
        for (var row = 0; row < height; row++)
        {
            Buffer.BlockCopy(Pixels, ((y + row) * Width + x) * 4, pixels, row * dstStride, dstStride);
        }

        return new BgraBuffer(width, height, pixels);
    }

    /// <summary>Stacks images top to bottom. Wider images are cropped from the left to the narrowest width.</summary>
    public static BgraBuffer ConcatVertical(IReadOnlyList<BgraBuffer> parts)
    {
        ArgumentNullException.ThrowIfNull(parts);
        if (parts.Count == 0)
        {
            throw new ArgumentException("Need at least one image.", nameof(parts));
        }

        var width = int.MaxValue;
        var height = 0;
        foreach (var part in parts)
        {
            ArgumentNullException.ThrowIfNull(part);
            if (part.Width <= 0 || part.Height <= 0)
            {
                throw new ArgumentException("Every part must have a positive size.", nameof(parts));
            }

            if (part.Width < width)
            {
                width = part.Width;
            }

            height = checked(height + part.Height);
        }

        var pixels = new byte[checked(width * height * 4)];
        var y = 0;
        foreach (var part in parts)
        {
            var srcStride = part.Width * 4;
            var dstStride = width * 4;
            var copy = width * 4;
            for (var row = 0; row < part.Height; row++)
            {
                Buffer.BlockCopy(part.Pixels, row * srcStride, pixels, (y + row) * dstStride, copy);
            }

            y += part.Height;
        }

        return new BgraBuffer(width, height, pixels);
    }
}
