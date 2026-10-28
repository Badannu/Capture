using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

namespace CaptureDemo.Native;

/// <summary>
/// Builds a real multi-resolution .ico at runtime instead of handing Windows one
/// 32×32 bitmap to shrink. Classic 32bpp DIB frames are used for the small sizes
/// (widest compatibility) and PNG frames for 128/256 so the file stays small.
/// </summary>
/// <remarks>
/// The same drawing code also produces the static <c>Assets\Capture.ico</c> that ships as the
/// exe icon; <c>Dev/IconExport.cs</c> (DEBUG only) drives that with the tuned small-size
/// metrics in <see cref="TileMetrics"/>. Nothing here changes at runtime: the tray and window
/// icons keep using <see cref="TileMetrics.Default"/>, because they are drawn per theme.
/// </remarks>
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

        return Build(frames);
    }

    /// <summary>Wraps already-encoded frames into an ICO container.</summary>
    internal static MemoryStream Build(List<(int Size, byte[] Data)> frames)
    {
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
    internal static byte[] EncodeDib(Bitmap bitmap)
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

    internal static byte[] EncodePng(Bitmap bitmap)
    {
        using var buffer = new MemoryStream();
        bitmap.Save(buffer, ImageFormat.Png);
        return buffer.ToArray();
    }

    private static Bitmap DrawTile(int size) => DrawTile(size, TileMetrics.Default(size));

    /// <summary>
    /// Paints one tile: rounded gradient background, optional 1 px inner rim, and the
    /// four-corner capture mark. <paramref name="metrics"/> carries every number that has to
    /// change with size, so small frames can be re-rendered instead of downscaled.
    /// </summary>
    internal static Bitmap DrawTile(int size, TileMetrics metrics)
    {
        var bitmap = new Bitmap(size, size, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(bitmap);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        g.Clear(Color.Transparent);

        var inset = Math.Max(0f, metrics.Inset);
        var tile = new RectangleF(inset, inset, size - (inset * 2), size - (inset * 2));
        using (var path = Rounded(tile, metrics.Radius))
        using (var brush = new LinearGradientBrush(
                   new RectangleF(tile.X, tile.Y, tile.Width, tile.Height + 1),
                   Color.FromArgb(58, 61, 70),
                   Color.FromArgb(20, 22, 26),
                   LinearGradientMode.Vertical))
        {
            g.FillPath(brush, path);
        }

        if (metrics.RimWidth > 0f)
        {
            var rimInset = Math.Max(0.5f, metrics.RimInset);
            using var inner = Rounded(
                RectangleF.Inflate(tile, -rimInset, -rimInset),
                Math.Max(0f, metrics.Radius - rimInset));
            using var rim = new Pen(Color.FromArgb(58, 255, 255, 255), metrics.RimWidth);
            g.DrawPath(rim, inner);
        }

        var mark = metrics.MarkInset > 0f
            ? RectangleF.Inflate(tile, -metrics.MarkInset, -metrics.MarkInset)
            : tile;
        if (metrics.Crisp)
        {
            // Hinting for the 16 px frame: a 1 px bracket drawn with anti-aliasing turns into a
            // 60 %-grey smear. Turning AA off for the mark (the tile keeps its AA corners) is how
            // classic 16 px icons stay readable. Nothing else changes: same geometry, same color.
            g.SmoothingMode = SmoothingMode.None;
            g.PixelOffsetMode = PixelOffsetMode.Half;
        }

        SnipIcons.CaptureMark(g, mark, Color.FromArgb(245, 247, 250), metrics.MarkWeight);
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

/// <summary>
/// Every size-dependent number <see cref="IconFactory.DrawTile(int, TileMetrics)"/> needs.
/// </summary>
/// <param name="Inset">Margin around the rounded tile, in pixels.</param>
/// <param name="Radius">Corner radius of the tile, in pixels.</param>
/// <param name="RimWidth">Inner highlight stroke width; 0 disables the rim.</param>
/// <param name="RimInset">How far the rim sits inside the tile edge, in pixels.</param>
/// <param name="MarkInset">Extra margin between tile edge and the capture mark, in pixels.</param>
/// <param name="MarkWeight">Capture-mark stroke weight in SVG grid units (24 = full box).</param>
/// <param name="Crisp">
/// Draw the capture mark without anti-aliasing. Only the 16 px frame uses it; see
/// <see cref="IconFactory.DrawTile(int, TileMetrics)"/>.
/// </param>
internal readonly record struct TileMetrics(
    float Inset,
    float Radius,
    float RimWidth,
    float RimInset,
    float MarkInset,
    float MarkWeight,
    bool Crisp = false)
{
    /// <summary>
    /// The runtime values, unchanged since 1.4: what the tray and window icons are drawn with.
    /// Keep this exactly as it is — the tray icon is theme-aware and tuned against these numbers.
    /// </summary>
    public static TileMetrics Default(int size) => new(
        Math.Max(0f, size * 0.03f),
        size * 0.26f,
        size >= 32 ? Math.Max(1f, size / 48f) : 0f,
        1.2f,
        0f,
        size <= 20 ? 2.15f : 1.95f);

    /// <summary>
    /// Static-icon values. 16 px is re-rendered rather than scaled: no rim (a hairlin at that
    /// size is just fog), a slightly tighter tile and a hinted 1 px bracket. 32 px keeps the
    /// default proportions because the rim still reads there. 48 and up are proportional.
    /// </summary>
    public static TileMetrics ForStaticIcon(int size) => size switch
    {
        16 => new TileMetrics(0.5f, 3.2f, 0f, 0f, 2.1f, 2.2f, Crisp: true),
        _ => Default(size),
    };
}
