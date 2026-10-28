#if DEBUG
// Dev-only: bakes the runtime-drawn tile into the static .ico that ships as the exe icon.
// Not compiled into Release — the .ico is a committed asset, nothing has to generate it at
// run time. Regenerate after any change to the tile or the capture mark with:
//
//   dotnet run --project src\CaptureDemo -c Debug -- --make-icon src\CaptureDemo\Assets\Capture.ico
//
// `--icon-sheet <folder>` writes two review sheets (candidate matrix + 256/16 comparison).
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

namespace CaptureDemo.Dev;

internal static class IconExport
{
    /// <summary>Frame sizes that go into the static icon, as required by Windows shell surfaces.</summary>
    private static readonly int[] FrameSizes = { 16, 32, 48, 64, 128, 256 };

    /// <summary>Metric variants compared on the review sheet; index 2 is the shipped one.</summary>
    private static readonly (string Name, Func<int, TileMetrics> Make)[] Candidates =
    {
        ("runtime default (scaled)", TileMetrics.Default),
        ("16: crisp 1px, box 10.8", _ => new TileMetrics(0.5f, 3.2f, 0f, 0f, 2.1f, 2.2f, Crisp: true)),
        ("16: crisp 1px, full tile", _ => new TileMetrics(0f, 3.0f, 0f, 0f, 2.4f, 2.14f, Crisp: true)),
        ("16: crisp 2px", _ => new TileMetrics(0.5f, 3.2f, 0f, 0f, 1.5f, 3.3f, Crisp: true)),
        ("16: AA 1.4px", _ => new TileMetrics(0.5f, 3.2f, 0f, 0f, 1.7f, 2.85f)),
    };

    /// <summary>Writes the committed exe icon: 16…256, PNG only for 256, DIB for the rest.</summary>
    public static void Run(string icoPath)
    {
        var full = Path.GetFullPath(icoPath);
        var directory = Path.GetDirectoryName(full);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var frames = new List<(int Size, byte[] Data)>(FrameSizes.Length);
        foreach (var size in FrameSizes)
        {
            using var bitmap = IconFactory.DrawTile(size, TileMetrics.ForStaticIcon(size));
            // A 256×256 DIB frame alone is 256 KB; PNG keeps the file small. Every smaller size
            // stays DIB because some legacy shell paths still refuse PNG frames below 128.
            frames.Add((size, size >= 256 ? IconFactory.EncodePng(bitmap) : IconFactory.EncodeDib(bitmap)));
        }

        using var stream = IconFactory.Build(frames);
        File.WriteAllBytes(full, stream.ToArray());
    }

    /// <summary>Writes <c>candidates.png</c> and <c>review.png</c> into <paramref name="folder"/>.</summary>
    public static void WriteSheets(string folder)
    {
        Directory.CreateDirectory(folder);
        WriteCandidates(Path.Combine(folder, "icon-candidates.png"));
        WriteReview(Path.Combine(folder, "icon-review.png"));
    }

    /// <summary>
    /// Decodes the committed .ico straight from its own bytes — no redraw — and renders every
    /// frame plus the parsed directory table. Evidence about the shipped file, not about the
    /// drawing code: if the file were truncated or a frame were mis-encoded, this shows it.
    /// </summary>
    public static void Check(string icoPath, string sheetPath)
    {
        var bytes = File.ReadAllBytes(icoPath);
        var count = BitConverter.ToUInt16(bytes, 4);
        var log = new List<string>
        {
            $"file = {icoPath}",
            $"size = {bytes.Length} bytes, frames = {count}",
            "",
        };

        var frames = new List<(int Size, string Format, int Length)>(count);
        for (var i = 0; i < count; i++)
        {
            var entry = 6 + (i * 16);
            var size = bytes[entry] == 0 ? 256 : bytes[entry];
            var length = (int)BitConverter.ToUInt32(bytes, entry + 8);
            var offset = (int)BitConverter.ToUInt32(bytes, entry + 12);
            var isPng = bytes[offset] == 0x89 && bytes[offset + 1] == 0x50 && bytes[offset + 2] == 0x4E;
            var format = isPng ? "PNG" : "DIB";
            frames.Add((size, format, length));
            log.Add($"{size,3} px  {format}  {length,6} bytes  @ {offset}");
        }

        const int cell = 132;
        const int padding = 20;
        using var sheet = new Bitmap(
            (padding * 2) + (cell * 6),
            padding + 26 + ((cell + 20) * 2) + 30,
            PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(sheet);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
        g.Clear(Color.FromArgb(24, 25, 28));
        using var muted = new SolidBrush(Color.FromArgb(150, 152, 160));
        using var ink = new SolidBrush(Color.FromArgb(232, 234, 240));
        Color[] backgrounds = { Color.FromArgb(246, 246, 248), Color.FromArgb(32, 32, 36) };

        for (var i = 0; i < frames.Count; i++)
        {
            var left = padding + (i * cell);
            g.DrawString($"{frames[i].Size} px", SystemFonts.DefaultFont, ink, left, padding - 2);
            g.DrawString(
                $"{frames[i].Format} {frames[i].Length / 1024.0:F1} KB",
                SystemFonts.DefaultFont,
                muted,
                left,
                padding + 14);

            // Exact size, so System.Drawing picks the real frame instead of rescaling another one.
            using var icon = new Icon(icoPath, frames[i].Size, frames[i].Size);
            using var bitmap = icon.ToBitmap();
            for (var row = 0; row < backgrounds.Length; row++)
            {
                var top = padding + 34 + (row * (cell + 4));
                using (var backdrop = new SolidBrush(backgrounds[row]))
                {
                    g.FillRectangle(backdrop, left, top, cell - 8, cell - 8);
                }

                var mode = frames[i].Size <= 48 ? InterpolationMode.NearestNeighbor : InterpolationMode.HighQualityBicubic;
                using var scaled = Resize(bitmap, cell - 8, mode);
                g.DrawImageUnscaled(scaled, left, top);
                g.DrawRectangle(Pens.DimGray, left, top, cell - 9, cell - 9);
            }
        }

        g.DrawString(
            "上一行：浅色底　下一行：深色底　｜　每格按原帧解码后缩放（≤48px 用最近邻，保留像素）",
            SystemFonts.DefaultFont,
            muted,
            padding,
            sheet.Height - 24);

        sheet.Save(sheetPath, ImageFormat.Png);
        File.WriteAllText(Path.ChangeExtension(sheetPath, ".txt"), string.Join(Environment.NewLine, log));
    }

    /// <summary>One row per candidate, one column per size, every cell blown up to 128 px.</summary>
    private static void WriteCandidates(string path)
    {
        const int cell = 140;
        const int labelWidth = 190;
        var width = labelWidth + (cell * 4);
        var height = 28 + (Candidates.Length * cell);

        using var sheet = new Bitmap(width, height, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(sheet);
        g.Clear(Color.FromArgb(24, 25, 28));
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
        using var muted = new SolidBrush(Color.FromArgb(168, 170, 178));
        string[] captions = { "16 px → ×8", "32 px → ×4", "48 px → ×2.7", "256 px → ×0.5" };
        int[] sizes = { 16, 32, 48, 256 };

        for (var c = 0; c < sizes.Length; c++)
        {
            g.DrawString(captions[c], SystemFonts.DefaultFont, muted, labelWidth + (c * cell) + 10, 6);
        }

        for (var row = 0; row < Candidates.Length; row++)
        {
            var top = 28 + (row * cell);
            g.DrawString(Candidates[row].Name, SystemFonts.DefaultFont, muted, 10, top + 60);
            for (var c = 0; c < sizes.Length; c++)
            {
                var size = sizes[c];
                using var frame = IconFactory.DrawTile(size, Candidates[row].Make(size));
                var box = CellBox(sizes[c], labelWidth + (c * cell), top, cell);
                g.DrawImage(frame, box, new Rectangle(0, 0, size, size), GraphicsUnit.Pixel);
            }
        }

        sheet.Save(path, ImageFormat.Png);
    }

    /// <summary>The comparison the reviewer asked for: 256 and 16 of the shipped icon, on both themes.</summary>
    private static void WriteReview(string path)
    {
        const int zoomed = 128;
        const int padding = 26;
        const int gap = 18;
        var panels = new (string Caption, int Size, Func<int, TileMetrics> Make)[]
        {
            ("256px → 128", 256, TileMetrics.ForStaticIcon),
            ("16px ×8 优化后", 16, TileMetrics.ForStaticIcon),
            ("16px ×8 基线", 16, TileMetrics.Default),
        };

        var width = padding + ((zoomed + gap) * panels.Length) + 150;
        var height = padding + 22 + ((zoomed + gap) * 2) + 34;

        using var sheet = new Bitmap(width, height, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(sheet);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
        using var ink = new SolidBrush(Color.FromArgb(230, 232, 238));
        using var muted = new SolidBrush(Color.FromArgb(150, 152, 160));
        g.Clear(Color.FromArgb(24, 25, 28));

        Color[] backgrounds = { Color.FromArgb(246, 246, 248), Color.FromArgb(32, 32, 36) };

        for (var column = 0; column < panels.Length; column++)
        {
            var left = padding + (column * (zoomed + gap));
            g.DrawString(panels[column].Caption, SystemFonts.DefaultFont, muted, left, padding - 20);
            for (var row = 0; row < backgrounds.Length; row++)
            {
                var top = padding + 12 + (row * (zoomed + gap));
                using (var backdrop = new SolidBrush(backgrounds[row]))
                {
                    g.FillRectangle(backdrop, left, top, zoomed, zoomed);
                }

                using var frame = IconFactory.DrawTile(panels[column].Size, panels[column].Make(panels[column].Size));
                var mode = panels[column].Size <= 48
                    ? InterpolationMode.NearestNeighbor
                    : InterpolationMode.HighQualityBicubic;
                using var scaled = Resize(frame, zoomed, mode);
                g.DrawImageUnscaled(scaled, left, top);
                g.DrawRectangle(Pens.DimGray, left, top, zoomed - 1, zoomed - 1);
                g.DrawString(
                    row == 0 ? "浅色底" : "深色底",
                    SystemFonts.DefaultFont,
                    muted,
                    left,
                    top + zoomed + 2);
            }
        }

        // Actual-size strip, so the magnified panels are not the only evidence.
        var stripLeft = padding + (panels.Length * (zoomed + gap));
        g.DrawString("实际尺寸", SystemFonts.DefaultFont, muted, stripLeft, padding - 20);
        using (var backdrop = new SolidBrush(backgrounds[0]))
        {
            g.FillRectangle(backdrop, stripLeft, padding + 12, 84, 56);
        }

        var x = stripLeft + 6;
        foreach (var size in new[] { 16, 32, 48 })
        {
            using var frame = IconFactory.DrawTile(size, TileMetrics.ForStaticIcon(size));
            g.DrawImageUnscaled(frame, x, padding + 20);
            using var caption = new SolidBrush(Color.FromArgb(90, 92, 100));
            g.DrawString(size.ToString(), SystemFonts.DefaultFont, caption, x, padding + 12 + 56);
            x += size + 10;
        }

        g.DrawString(
            "Capture.ico 帧：" + string.Join(" / ", FrameSizes) + " px ｜ 256 为 PNG 条目、其余 32bpp DIB ｜ 16px 按尺寸重绘（无内描边、关闭抗锯齿）",
            SystemFonts.DefaultFont,
            ink,
            padding,
            height - 24);

        sheet.Save(path, ImageFormat.Png);
    }

    private static Bitmap Resize(Bitmap source, int side, InterpolationMode mode)
    {
        var target = new Bitmap(side, side, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(target);
        g.InterpolationMode = mode;
        g.PixelOffsetMode = mode == InterpolationMode.NearestNeighbor
            ? PixelOffsetMode.Half
            : PixelOffsetMode.HighQuality;
        g.DrawImage(source, new Rectangle(0, 0, side, side), new Rectangle(0, 0, source.Width, source.Height), GraphicsUnit.Pixel);
        return target;
    }

    private static Rectangle CellBox(int size, int cellLeft, int cellTop, int cell)
    {
        // Integer zoom for 16/32 so the pixel grid stays honest; 48 and 256 use the common box.
        var side = size switch
        {
            16 => 16 * 8,
            32 => 32 * 4,
            _ => cell - 12,
        };
        var offset = (cell - side) / 2;
        return new Rectangle(cellLeft + offset, cellTop + offset, side, side);
    }
}
#endif
