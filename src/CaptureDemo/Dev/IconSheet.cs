#if DEBUG
// Compiled into Debug builds only; `--icons` is not parsed in a Release build.
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

namespace CaptureDemo.Dev;

/// <summary>
/// Dev-only contact sheet for the "long capture" glyph candidates.
/// Renders every candidate with the same parser, weight and palette as the real pill,
/// so what you pick here is what ships.
/// </summary>
internal static class IconSheet
{
    private sealed record Candidate(string Key, string Title, string[] Paths);

    private static readonly string[] Rect =
    {
        "M4 9V6.5A2.5 2.5 0 0 1 6.5 4H9",
        "M15 4h2.5A2.5 2.5 0 0 1 20 6.5V9",
        "M20 15v2.5a2.5 2.5 0 0 1-2.5 2.5H15",
        "M9 20H6.5A2.5 2.5 0 0 1 4 17.5V15",
    };

    private static readonly string[] Element =
    {
        "M4.5 3.5h7A1 1 0 0 1 12.5 4.5v7a1 1 0 0 1-1 1h-7a1 1 0 0 1-1-1v-7a1 1 0 0 1 1-1z",
        "M11.5 11.5 20.5 14.6l-3.6 1.3-1.3 3.6z",
    };

    private static readonly Candidate[] Candidates =
    {
        new("1", "长条内向下箭头", new[]
        {
            "M7.2 3.4h9.6A1.6 1.6 0 0 1 18.4 5v14a1.6 1.6 0 0 1-1.6 1.6H7.2A1.6 1.6 0 0 1 5.6 19V5a1.6 1.6 0 0 1 1.6-1.6z",
            "M12 7.4v6.6",
            "M9.3 11.4 12 14.1l2.7-2.7",
        }),
        new("2", "窗口加续接横线", new[]
        {
            "M5 3.2h14A1.5 1.5 0 0 1 20.5 4.7v6.6a1.5 1.5 0 0 1-1.5 1.5H5A1.5 1.5 0 0 1 3.5 11.3V4.7A1.5 1.5 0 0 1 5 3.2z",
            "M3.5 15.6h17",
            "M3.5 19.4h17",
        }),
        new("3", "长纸加折叠角", new[]
        {
            "M6.4 3.2h8.2l3.6 3.6v14H6.4z",
            "M14.6 3.2v3.8h3.6",
            "M9 10.6h6",
            "M9 13.8h6",
            "M9 17h3.4",
        }),
        new("4", "双屏续接", new[]
        {
            "M4.8 3.4h14.4a1.4 1.4 0 0 1 1.4 1.4v3.8a1.4 1.4 0 0 1-1.4 1.4H4.8a1.4 1.4 0 0 1-1.4-1.4V4.8A1.4 1.4 0 0 1 4.8 3.4z",
            "M4.8 14h14.4a1.4 1.4 0 0 1 1.4 1.4v3.8a1.4 1.4 0 0 1-1.4 1.4H4.8a1.4 1.4 0 0 1-1.4-1.4v-3.8a1.4 1.4 0 0 1 1.4-1.4z",
            "M8.6 6.7h6.8",
        }),
        new("5", "长页加虚线续接", new[]
        {
            "M7.6 2.6h8.8a1.6 1.6 0 0 1 1.6 1.6v8.4a1.6 1.6 0 0 1-1.6 1.6H7.6A1.6 1.6 0 0 1 6 12.6V4.2a1.6 1.6 0 0 1 1.6-1.6z",
            "M9.4 6.4h5.2",
            "M9.4 9.4h5.2",
            "M6.4 17.6h2.6",
            "M10.7 17.6h2.6",
            "M15 17.6h2.6",
            "M6.4 21h2.6",
            "M10.7 21h2.6",
            "M15 21h2.6",
        }),
        new("6", "细长页面", new[]
        {
            "M7.8 2.4h8.4a1.6 1.6 0 0 1 1.6 1.6v16a1.6 1.6 0 0 1-1.6 1.6H7.8A1.6 1.6 0 0 1 6.2 20V4a1.6 1.6 0 0 1 1.6-1.6z",
            "M9.4 6.4h5.2",
            "M9.4 9.2h5.2",
            "M9.4 12h5.2",
            "M9.4 14.8h3.2",
        }),
        new("E", "现有（像 ♀）", new[]
        {
            "M8.5 3.5h5A1.5 1.5 0 0 1 15 5v7.5a1.5 1.5 0 0 1-1.5 1.5h-5A1.5 1.5 0 0 1 7 12.5V5a1.5 1.5 0 0 1 1.5-1.5z",
            "M11 15.4v5.4",
            "M8.6 18.6 11 21l2.4-2.4",
        }),
    };

    public static void Run(string folder)
    {
        Directory.CreateDirectory(folder);
        const int rowHeight = 100;
        const int width = 1120;
        var height = 56 + (Candidates.Length * rowHeight);

        using var sheet = new Bitmap(width, height, PixelFormat.Format32bppPArgb);
        using (var g = Graphics.FromImage(sheet))
        {
            g.Clear(Color.FromArgb(30, 30, 32));
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

            using var title = new Font("Segoe UI", 11.5f, FontStyle.Bold);
            using var body = new Font("Segoe UI", 9.5f);
            using var tiny = new Font("Segoe UI", 7.5f);
            using var muted = new SolidBrush(Color.FromArgb(150, 150, 155));
            using var ink = new SolidBrush(Color.White);

            Draw(g, title, "长截图图标候选", 18, 14, ink);
            Draw(g, body, "左起：胶囊真实尺寸 26px · 40px · 52px · 16px · 浅底 · 放进胶囊条的样子", 150, 18, muted);

            for (var i = 0; i < Candidates.Length; i++)
            {
                var c = Candidates[i];
                var top = 56 + (i * rowHeight);
                Draw(g, title, c.Key, 20, top + 16, ink);
                Draw(g, body, c.Title, 44, top + 18, muted);

                var tile = new Rectangle(200, top + 14, 46, 46);
                using (var bg = new SolidBrush(Color.FromArgb(26, 26, 28)))
                using (var edge = new Pen(Color.FromArgb(58, 255, 255, 255), 1f))
                {
                    g.FillRectangle(bg, tile);
                    g.DrawRectangle(edge, tile);
                }

                Glyph(g, c.Paths, new Rectangle(tile.X + 10, tile.Y + 10, 26, 26), Color.White);
                Glyph(g, c.Paths, new Rectangle(268, top + 10, 40, 40), Color.White);
                Glyph(g, c.Paths, new Rectangle(324, top + 4, 52, 52), Color.White);
                Glyph(g, c.Paths, new Rectangle(392, top + 30, 16, 16), Color.White);

                using (var light = new SolidBrush(Color.FromArgb(246, 246, 248)))
                {
                    g.FillRectangle(light, new Rectangle(430, top + 16, 56, 40));
                }

                Glyph(g, c.Paths, new Rectangle(445, top + 23, 26, 26), Color.FromArgb(28, 28, 32));

                Pill(g, c.Paths, new Rectangle(510, top + 22, 570, 46), tiny);

                Draw(g, tiny, "真机尺寸", tile.X, top + 64, muted);
            }
        }

        sheet.Save(Path.Combine(folder, "long-icon-candidates.png"), ImageFormat.Png);
        File.WriteAllText(
            Path.Combine(folder, "long-icon-candidates.txt"),
            string.Join(Environment.NewLine, Candidates.Select(c => c.Key + "  " + c.Title)));
    }

    /// <summary>The real capsule: 46 px tall, 40 px slots, blue fill for the active mode.</summary>
    private static void Pill(Graphics g, string[] paths, Rectangle bar, Font label)
    {
        using var bg = new SolidBrush(Color.FromArgb(238, 22, 22, 24));
        using var edge = new Pen(Color.FromArgb(120, 8, 8, 10), 1f);
        using var body = Round(bar, bar.Height / 2);
        g.FillPath(bg, body);
        g.DrawPath(edge, body);

        var slotSize = 40;
        var pad = 7;
        var gap = 10;
        var x = bar.Left + pad;
        var modes = new[] { Rect, paths, Element };
        for (var i = 0; i < modes.Length; i++)
        {
            var slot = new Rectangle(x, bar.Top + ((bar.Height - slotSize) / 2), slotSize, slotSize);
            if (i == 1)
            {
                using var accent = new SolidBrush(Color.FromArgb(0, 120, 212));
                using var rounded = Round(Rectangle.Inflate(slot, -1, -1), 7);
                g.FillPath(accent, rounded);
            }

            Glyph(g, modes[i], new Rectangle(slot.X + 7, slot.Y + 7, 26, 26), Color.White);
            x += slotSize;
            if (i == 2)
            {
                x += gap;
                _ = x;
            }
        }

        Draw(g, label, "矩形 · 长截图（选中）· 元素", bar.Left + 8, bar.Bottom + 4, Brushes.Gray);
    }

    private static GraphicsPath Round(Rectangle r, int radius)
    {
        var path = new GraphicsPath();
        var d = Math.Min(radius * 2, Math.Min(r.Width, r.Height));
        path.AddArc(r.Left, r.Top, d, d, 180, 90);
        path.AddArc(r.Right - d, r.Top, d, d, 270, 90);
        path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        path.AddArc(r.Left, r.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }

    private static void Glyph(Graphics g, string[] paths, Rectangle box, Color color)
    {
        var side = Math.Max(6f, Math.Min(box.Width, box.Height));
        const float grid = 24f;
        var scale = side / grid;
        var ox = box.Left + ((box.Width - side) / 2f);
        var oy = box.Top + ((box.Height - side) / 2f);
        using var pen = new Pen(color, Math.Max(1.05f, 1.75f * scale))
        {
            StartCap = LineCap.Round,
            EndCap = LineCap.Round,
            LineJoin = LineJoin.Round,
        };
        var previous = g.SmoothingMode;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        foreach (var data in paths)
        {
            using var path = SvgPath.Parse(data, scale, ox, oy);
            g.DrawPath(pen, path);
        }

        g.SmoothingMode = previous;
    }

    private static void Draw(Graphics g, Font font, string text, float x, float y, Brush brush)
    {
        g.DrawString(text, font, brush, x, y);
    }
}
#endif
