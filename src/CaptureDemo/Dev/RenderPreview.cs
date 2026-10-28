using System.Drawing.Imaging;

namespace CaptureDemo.Dev;

/// <summary>
/// Dev-only contact sheets. Run with <c>--preview &lt;folder&gt;</c> to dump PNGs so
/// icon weight and spacing can be eyeballed instead of guessed. Never runs during
/// normal startup.
/// </summary>
internal static class RenderPreview
{
    private static readonly (string Name, Action<Graphics, Rectangle, Color> Draw)[] Glyphs =
    {
        ("fullscreen", SnipIcons.Fullscreen),
        ("rect", SnipIcons.Rect),
        ("window", SnipIcons.Window),
        ("long", SnipIcons.Long),
        ("element", SnipIcons.Element),
        ("freeform", SnipIcons.Freeform),
        ("gear", SnipIcons.Gear),
        ("close", SnipIcons.Close),
        ("pen", SnipIcons.Pen),
        ("arrow", SnipIcons.Arrow),
        ("highlight", SnipIcons.Highlight),
        ("mosaic", SnipIcons.Mosaic),
        ("undo", SnipIcons.Undo),
        ("confirm", SnipIcons.Confirm),
        ("discard", SnipIcons.Discard),
        ("clipboard", SnipIcons.Clipboard),
        ("copy", SnipIcons.Copy),
        ("folder", SnipIcons.Folder),
        ("trash", SnipIcons.Trash),
        ("pin", SnipIcons.Pin),
        ("saveas", SnipIcons.SaveAs),
        ("open", SnipIcons.Open),
        ("sliders", SnipIcons.Sliders),
        ("chevron", SnipIcons.Chevron),
    };

    private static readonly int[] Sizes = { 16, 24, 32, 48 };

    public static void Render(string folder)
    {
        Directory.CreateDirectory(folder);
        RenderIcons(Path.Combine(folder, "icons.png"));
    }

    /// <summary>Dev hook: <c>--crop &lt;src&gt; &lt;dst&gt; x y w h scale</c> magnifies a region for review.</summary>
    public static void Crop(string source, string destination, int x, int y, int width, int height, int scale)
    {
        using var image = Image.FromFile(source);
        using var output = new Bitmap(width * scale, height * scale, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(output))
        {
            g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.NearestNeighbor;
            g.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.Half;
            g.DrawImage(
                image,
                new Rectangle(0, 0, output.Width, output.Height),
                new Rectangle(x, y, width, height),
                GraphicsUnit.Pixel);
        }

        output.Save(destination, ImageFormat.Png);
    }

    private static void RenderIcons(string path)
    {
        const int slot = 64;
        const int labelWidth = 96;
        const int cellHeight = 72;
        const int perRow = 3;
        var cellWidth = labelWidth + (Sizes.Length * slot);
        var rows = (int)Math.Ceiling(Glyphs.Length / (double)perRow);
        var log = new List<string>();
        using var bitmap = new Bitmap(cellWidth * perRow, cellHeight * rows + 8, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(bitmap))
        {
            g.Clear(Color.FromArgb(30, 30, 32));
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
            using var muted = new SolidBrush(Color.FromArgb(160, 162, 172));
            for (var i = 0; i < Glyphs.Length; i++)
            {
                var row = i / perRow;
                var column = i % perRow;
                var cellLeft = column * cellWidth;
                var cellTop = (row * cellHeight) + 4;
                g.DrawString(Glyphs[i].Name, UiFonts.Small, muted, cellLeft + 8, cellTop + 20);
                for (var s = 0; s < Sizes.Length; s++)
                {
                    var side = Sizes[s];
                    var box = new Rectangle(
                        cellLeft + labelWidth + (s * slot) + ((slot - side) / 2),
                        cellTop + ((cellHeight - side) / 2),
                        side,
                        side);
                    if (i == 0)
                    {
                        log.Add($"size {side}: x={box.Left} y={box.Top} w={box.Width}");
                    }

                    Glyphs[i].Draw(g, box, Color.FromArgb(240, 240, 245));
                }

                using var guide = new Pen(Color.FromArgb(46, 255, 255, 255));
                g.DrawLine(guide, cellLeft, cellTop, cellLeft + cellWidth, cellTop);
                g.DrawLine(guide, cellLeft, cellTop, cellLeft, cellTop + cellHeight);
            }
        }

        bitmap.Save(path, ImageFormat.Png);
        File.WriteAllText(Path.ChangeExtension(path, ".txt"), string.Join(Environment.NewLine, log) + $"{Environment.NewLine}canvas={bitmap.Width}x{bitmap.Height}");
    }
}
