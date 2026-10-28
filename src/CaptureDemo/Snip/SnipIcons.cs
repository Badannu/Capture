using System.Drawing.Drawing2D;

namespace CaptureDemo.Snip;

/// <summary>
/// Every glyph is authored once on a 24×24 grid and stroked with one optical
/// weight (1.75 grid units, round caps and joins). Scaling is uniform, so the
/// pill bar, the annotation bar and the popup menus stay visually identical.
/// </summary>
internal static class SnipIcons
{
    private const float Weight = 1.75f;

    // ── capture modes ──────────────────────────────────────────────────────
    private static readonly string[] FullscreenPaths =
    {
        "M4.5 4h15A1.5 1.5 0 0 1 21 5.5v9a1.5 1.5 0 0 1-1.5 1.5h-15A1.5 1.5 0 0 1 3 14.5v-9A1.5 1.5 0 0 1 4.5 4z",
        "M12 16.2v3.3",
        "M8.6 19.5h6.8",
    };

    private static readonly string[] RectPaths =
    {
        "M4 9V6.5A2.5 2.5 0 0 1 6.5 4H9",
        "M15 4h2.5A2.5 2.5 0 0 1 20 6.5V9",
        "M20 15v2.5a2.5 2.5 0 0 1-2.5 2.5H15",
        "M9 20H6.5A2.5 2.5 0 0 1 4 17.5V15",
    };

    private static readonly string[] WindowPaths =
    {
        "M4.5 4.5h15A1.5 1.5 0 0 1 21 6v12a1.5 1.5 0 0 1-1.5 1.5h-15A1.5 1.5 0 0 1 3 18V6a1.5 1.5 0 0 1 1.5-1.5z",
        "M3 9h18",
    };

    private static readonly string[] LongPaths =
    {
        "M8.5 3.5h5A1.5 1.5 0 0 1 15 5v7.5a1.5 1.5 0 0 1-1.5 1.5h-5A1.5 1.5 0 0 1 7 12.5V5a1.5 1.5 0 0 1 1.5-1.5z",
        "M11 15.4v5.4",
        "M8.6 18.6 11 21l2.4-2.4",
    };

    private static readonly string[] ElementPaths =
    {
        "M4.5 3.5h7A1 1 0 0 1 12.5 4.5v7a1 1 0 0 1-1 1h-7a1 1 0 0 1-1-1v-7a1 1 0 0 1 1-1z",
        "M11.5 11.5 20.5 14.6l-3.6 1.3-1.3 3.6z",
    };

    private static readonly string[] FreeformPaths =
    {
        // A freehand scribble: the clearest "draw it yourself" metaphor at 16 px.
        "M3.6 17.4c2.2-6 4.6-9.6 6.6-9.6 1.5 0 1.1 3.2-.4 6.2-1.2 2.4-2.4 4.2-1.5 4.9.9.7 4.3-1.8 7.3-5 2.5-2.7 4.3-4 4.6-2.9",
    };

    // ── chrome ─────────────────────────────────────────────────────────────
    private static readonly string[] ClosePaths = { "M6.2 6.2 17.8 17.8", "M17.8 6.2 6.2 17.8" };

    private static readonly string[] PenPaths =
    {
        "M5 19l.9-3.9L16.4 4.6a2.3 2.3 0 0 1 3.2 0l.8.8a2.3 2.3 0 0 1 0 3.2L9.9 19.1 6 20z",
        "M14.4 6.6 17.9 10.1",
    };

    private static readonly string[] ArrowPaths = { "M4.5 19.5 19 5", "M19 5h-6.8", "M19 5v6.8" };

    private static readonly string[] HighlightPaths =
    {
        "M8.8 16.2 16.6 8.4a1.9 1.9 0 0 1 2.7 0l.7.7a1.9 1.9 0 0 1 0 2.7l-7.8 7.8H8.8z",
        "M4.5 21.2h15",
    };

    private static readonly string[] MosaicPaths =
    {
        "M4 4h6v6H4z",
        "M14 4h6v6h-6z",
        "M4 14h6v6H4z",
        "M14 14h6v6h-6z",
    };

    private static readonly string[] UndoPaths =
    {
        "M8.9 3.9 4.2 8.6 8.9 13.3",
        "M4.2 8.6h9.6a5.2 5.2 0 0 1 0 10.4h-4.2",
    };

    private static readonly string[] ConfirmPaths = { "M5 12.6 9.8 17.4 19.4 6.6" };

    private static readonly string[] DiscardPaths = { "M6.6 6.6 17.4 17.4", "M17.4 6.6 6.6 17.4" };

    // ── clipboard / menus ──────────────────────────────────────────────────
    private static readonly string[] ClipboardPaths =
    {
        "M8.6 4.6H6.4A1.9 1.9 0 0 0 4.5 6.5v13.1a1.9 1.9 0 0 0 1.9 1.9h11.2a1.9 1.9 0 0 0 1.9-1.9V6.5a1.9 1.9 0 0 0-1.9-1.9h-2.2",
        "M9 3.4h6a1.2 1.2 0 0 1 1.2 1.2v1.6A1.2 1.2 0 0 1 15 7.4H9a1.2 1.2 0 0 1-1.2-1.2V4.6A1.2 1.2 0 0 1 9 3.4z",
        "M8.6 11.4h6.8",
        "M8.6 15.2h6.8",
    };

    private static readonly string[] CopyPaths =
    {
        "M9.4 8.6h8.9a1.8 1.8 0 0 1 1.8 1.8v8.9a1.8 1.8 0 0 1-1.8 1.8H9.4a1.8 1.8 0 0 1-1.8-1.8v-8.9a1.8 1.8 0 0 1 1.8-1.8z",
        "M15.6 5.6V4.4a1.8 1.8 0 0 0-1.8-1.8H4.6a1.8 1.8 0 0 0-1.8 1.8v9.2a1.8 1.8 0 0 0 1.8 1.8h1.4",
    };

    private static readonly string[] FolderPaths =
    {
        "M3.6 7.4a2 2 0 0 1 2-2h3.3l2 2.4h7.5a2 2 0 0 1 2 2v8.4a2 2 0 0 1-2 2H5.6a2 2 0 0 1-2-2z",
    };

    private static readonly string[] TrashPaths =
    {
        "M4.4 7h15.2",
        "M9.4 7V4.6h5.2V7",
        "M6.6 7l.9 12.4a1.6 1.6 0 0 0 1.6 1.5h5.8a1.6 1.6 0 0 0 1.6-1.5L17.4 7",
        "M10.4 10.6v6.4",
        "M13.6 10.6v6.4",
    };

    private static readonly string[] PinPaths =
    {
        "M9.4 3.5h5.2l-.8 4.6 3.1 3v1.3H7.1v-1.3l3.1-3z",
        "M12 12.4v8.1",
    };

    private static readonly string[] SavePaths =
    {
        "M12 3.6v10.2",
        "M7.9 9.9 12 14l4.1-4.1",
        "M4.4 15.6v2.8a2.2 2.2 0 0 0 2.2 2.2h10.8a2.2 2.2 0 0 0 2.2-2.2v-2.8",
    };

    private static readonly string[] OpenPaths =
    {
        "M13.6 4.4H19.6V10.4",
        "M19.6 4.4 11.4 12.6",
        "M17 14.4v4.2a1.9 1.9 0 0 1-1.9 1.9H5.9A1.9 1.9 0 0 1 4 18.6V9.5a1.9 1.9 0 0 1 1.9-1.9h4.2",
    };

    private static readonly string[] SliderPaths =
    {
        "M4.2 7.2h5.6",
        "M16.6 7.2h3.2",
        "M4.2 16.8h2.8",
        "M13.4 16.8h6.4",
    };

    /// <summary>Sliders with real knobs; the knobs carry the meaning at 16 px.</summary>
    public static void Sliders(Graphics g, Rectangle r, Color color)
    {
        Stroke(g, r, color, SliderPaths);
        var side = Math.Max(6f, Math.Min(r.Width, r.Height));
        var scale = side / SvgPath.Grid;
        var offsetX = r.Left + ((r.Width - side) / 2f);
        var offsetY = r.Top + ((r.Height - side) / 2f);
        using var pen = MakePen(color, scale);
        var knob = 2.5f * scale;
        g.DrawEllipse(pen, offsetX + (12.1f * scale) - knob, offsetY + (7.2f * scale) - knob, knob * 2f, knob * 2f);
        g.DrawEllipse(pen, offsetX + (9.1f * scale) - knob, offsetY + (16.8f * scale) - knob, knob * 2f, knob * 2f);
    }

    public static void Chevron(Graphics g, Rectangle r, Color color) => Stroke(g, r, color, ChevronPaths);

    private static readonly string[] ChevronPaths = { "M7.5 10 12 14.5 16.5 10" };

    public static void Fullscreen(Graphics g, Rectangle r, Color color) => Stroke(g, r, color, FullscreenPaths);

    public static void Rect(Graphics g, Rectangle r, Color color) => Stroke(g, r, color, RectPaths);

    public static void Window(Graphics g, Rectangle r, Color color) => Stroke(g, r, color, WindowPaths);

    public static void Long(Graphics g, Rectangle r, Color color) => Stroke(g, r, color, LongPaths);

    public static void Element(Graphics g, Rectangle r, Color color) => Stroke(g, r, color, ElementPaths);

    public static void Freeform(Graphics g, Rectangle r, Color color) => Stroke(g, r, color, FreeformPaths);

    public static void Pen(Graphics g, Rectangle r, Color color) => Stroke(g, r, color, PenPaths);

    public static void Arrow(Graphics g, Rectangle r, Color color) => Stroke(g, r, color, ArrowPaths);

    public static void Highlight(Graphics g, Rectangle r, Color color) => Stroke(g, r, color, HighlightPaths);

    public static void Mosaic(Graphics g, Rectangle r, Color color) => Fill(g, r, color, MosaicPaths);

    public static void Undo(Graphics g, Rectangle r, Color color) => Stroke(g, r, color, UndoPaths);

    public static void Confirm(Graphics g, Rectangle r, Color color) => Stroke(g, r, color, ConfirmPaths);

    public static void Discard(Graphics g, Rectangle r, Color color) => Stroke(g, r, color, DiscardPaths);

    public static void Close(Graphics g, Rectangle r, Color color) => Stroke(g, r, color, ClosePaths);

    public static void Clipboard(Graphics g, Rectangle r, Color color) => Stroke(g, r, color, ClipboardPaths);

    public static void Copy(Graphics g, Rectangle r, Color color) => Stroke(g, r, color, CopyPaths);

    public static void Folder(Graphics g, Rectangle r, Color color) => Stroke(g, r, color, FolderPaths);

    public static void Trash(Graphics g, Rectangle r, Color color) => Stroke(g, r, color, TrashPaths);

    public static void Pin(Graphics g, Rectangle r, Color color) => Stroke(g, r, color, PinPaths);

    public static void SaveAs(Graphics g, Rectangle r, Color color) => Stroke(g, r, color, SavePaths);

    public static void Open(Graphics g, Rectangle r, Color color) => Stroke(g, r, color, OpenPaths);

    /// <summary>
    /// A gear built from a real tooth polygon (alternating radii, round joins),
    /// so it stays round instead of turning into a sun at 16 px.
    /// </summary>
    public static void Gear(Graphics g, Rectangle r, Color color)
    {
        var side = Math.Max(6f, Math.Min(r.Width, r.Height));
        var scale = side / SvgPath.Grid;
        var cx = r.Left + (r.Width / 2f);
        var cy = r.Top + (r.Height / 2f);
        using var pen = MakePen(color, scale);

        const int teeth = 8;
        var outer = 10.2f * scale;
        var inner = 7.4f * scale;
        var points = new PointF[teeth * 4];
        for (var i = 0; i < points.Length; i++)
        {
            var step = i % 4;
            var radius = step is 1 or 2 ? outer : inner;
            var angle = (i * Math.PI) / (teeth * 2);
            points[i] = new PointF(
                cx + (float)(Math.Cos(angle) * radius),
                cy + (float)(Math.Sin(angle) * radius));
        }

        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.DrawPolygon(pen, points);
        var hub = 3.2f * scale;
        g.DrawEllipse(pen, cx - hub, cy - hub, hub * 2f, hub * 2f);
    }

    /// <summary>Four corner brackets; the app and tray identity mark.</summary>
    public static void CaptureMark(Graphics g, RectangleF box, Color color, float weight)
    {
        var scale = Math.Min(box.Width, box.Height) / SvgPath.Grid;
        using var pen = new Pen(color, Math.Max(1f, weight * scale))
        {
            StartCap = LineCap.Round,
            EndCap = LineCap.Round,
            LineJoin = LineJoin.Round,
        };
        const float inset = 5.2f;
        const float arm = 4.8f;
        var l = box.Left + (inset * scale);
        var t = box.Top + (inset * scale);
        var right = box.Right - (inset * scale);
        var b = box.Bottom - (inset * scale);
        var a = arm * scale;
        g.DrawLine(pen, l, t + a, l, t);
        g.DrawLine(pen, l, t, l + a, t);
        g.DrawLine(pen, right - a, t, right, t);
        g.DrawLine(pen, right, t, right, t + a);
        g.DrawLine(pen, right, b - a, right, b);
        g.DrawLine(pen, right, b, right - a, b);
        g.DrawLine(pen, l + a, b, l, b);
        g.DrawLine(pen, l, b, l, b - a);
    }

    private static void Stroke(Graphics g, Rectangle r, Color color, string[] paths)
    {
        var side = Math.Max(6f, Math.Min(r.Width, r.Height));
        var scale = side / SvgPath.Grid;
        var offsetX = r.Left + ((r.Width - side) / 2f);
        var offsetY = r.Top + ((r.Height - side) / 2f);
        using var pen = MakePen(color, scale);
        var previous = g.SmoothingMode;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        foreach (var data in paths)
        {
            using var path = SvgPath.Parse(data, scale, offsetX, offsetY);
            g.DrawPath(pen, path);
        }

        g.SmoothingMode = previous;
    }

    /// <summary>Filled glyphs stay legible when the stroke would collapse below one pixel.</summary>
    private static void Fill(Graphics g, Rectangle r, Color color, string[] paths)
    {
        var side = Math.Max(6f, Math.Min(r.Width, r.Height));
        var scale = side / SvgPath.Grid;
        var offsetX = r.Left + ((r.Width - side) / 2f);
        var offsetY = r.Top + ((r.Height - side) / 2f);
        using var brush = new SolidBrush(color);
        var previous = g.SmoothingMode;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        foreach (var data in paths)
        {
            using var path = SvgPath.Parse(data, scale, offsetX, offsetY);
            g.FillPath(brush, path);
        }

        g.SmoothingMode = previous;
    }

    private static Pen MakePen(Color color, float scale)
    {
        return new Pen(color, Math.Max(1.05f, Weight * scale))
        {
            StartCap = LineCap.Round,
            EndCap = LineCap.Round,
            LineJoin = LineJoin.Round,
        };
    }
}
