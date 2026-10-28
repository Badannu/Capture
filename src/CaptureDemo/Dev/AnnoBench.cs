#if DEBUG
// Compiled into Debug builds only: the shipped exe must not carry a benchmark harness, and
// `--bench` is not even parsed there (see Program.Main).
using System.Diagnostics;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using CaptureDemo.Core;

namespace CaptureDemo.Dev;

/// <summary>
/// Dev-only timing harness: compares the old per-move path (re-composite the whole capture
/// on the CPU, convert it to a bitmap, push a full-screen layered window) against the current
/// one (paint the dirty rectangle, copy the dirty rows, present once). Run with <c>--bench</c>.
/// </summary>
internal static class AnnoBench
{
    private const int Width = 1920;
    private const int Height = 1080;

    public static void Run(string reportPath)
    {
        var lines = new List<string>
        {
            $"画布 {Width}x{Height}（相当于 1080p 框选后的标注编辑）",
            "",
        };

        using var background = MakeBackground();
        var oldPen = MeasureOldPath(background);
        var oldRect = MeasureOldPath(background, AnnotateKind.Mosaic);
        var (newPen, newRect, newSelect) = MeasureNewPath(background);
        var oldPresent = MeasureFullScreenPresent(background);

        lines.Add("旧路径（每次鼠标移动都重合成整幅 + 全屏提交）");
        lines.Add($"  画笔：CPU 重合成 {oldPen,6:F2} ms + 全屏提交 {oldPresent,6:F2} ms = {oldPen + oldPresent,6:F2} ms/帧");
        lines.Add($"  马赛克：CPU 重合成 {oldRect,6:F2} ms + 全屏提交 {oldPresent,6:F2} ms = {oldRect + oldPresent,6:F2} ms/帧");
        lines.Add("");
        lines.Add("新路径（只重画脏矩形 + 只拷脏行）");
        lines.Add($"  画笔拖动      {newPen,7:F2} ms/帧   → 约 {1000 / Math.Max(0.01, newPen),4:F0} fps 上限");
        lines.Add($"  矩形拖动      {newRect,7:F2} ms/帧   → 约 {1000 / Math.Max(0.01, newRect),4:F0} fps 上限");
        lines.Add($"  选中拖动      {newSelect,7:F2} ms/帧   → 约 {1000 / Math.Max(0.01, newSelect),4:F0} fps 上限");
        lines.Add("");
        lines.Add($"画笔提升 {(oldPen + oldPresent) / Math.Max(0.01, newPen):F1}x，马赛克提升 {(oldRect + oldPresent) / Math.Max(0.01, newRect):F1}x");
        lines.Add("");
        lines.Add("注：旧路径的全屏提交每次都重建 DC + DIB 段，这是当时的另一个主要开销。");

        File.WriteAllText(reportPath, string.Join(Environment.NewLine, lines));

        var proof = Path.Combine(Path.GetDirectoryName(reportPath) ?? ".", "layer-shot.png");
        ProveVisible(proof);
    }

    /// <summary>Cost of the old full-window submit: new DC + DIB section, full copy, one present.</summary>
    private static double MeasureFullScreenPresent(Bitmap background)
    {
        using var layer = new AnnoLayerForm();
        layer.Attach(background, new Rectangle(0, 0, Width, Height));
        layer.Show();
        Application.DoEvents();

        var full = new Bitmap(Width, Height, PixelFormat.Format32bppPArgb);
        using (var g = Graphics.FromImage(full))
        {
            g.DrawImage(background, 0, 0);
        }

        var repeats = 12;
        var stopwatch = Stopwatch.StartNew();
        for (var i = 0; i < repeats; i++)
        {
            LayeredWindow.Update(layer.Handle, Point.Empty, full);
        }

        stopwatch.Stop();
        layer.Hide();
        return stopwatch.Elapsed.TotalMilliseconds / repeats;
    }

    private static Bitmap MakeBackground()
    {
        var bitmap = new Bitmap(Width, Height, PixelFormat.Format32bppPArgb);
        using var g = Graphics.FromImage(bitmap);
        using var brush = new LinearGradientBrush(
            new Rectangle(0, 0, Width, Height),
            Color.FromArgb(46, 62, 92),
            Color.FromArgb(120, 92, 138),
            LinearGradientMode.ForwardDiagonal);
        g.FillRectangle(brush, 0, 0, Width, Height);
        using var light = new SolidBrush(Color.FromArgb(40, 255, 255, 255));
        for (var i = 0; i < 40; i++)
        {
            g.FillRectangle(light, 40 + (i % 8 * 220), 60 + (i / 8 * 200), 150, 90);
        }

        return bitmap;
    }

    /// <summary>Reproduces the work the old <c>SnipSession.Repaint</c> did on every pointer move.</summary>
    private static double MeasureOldPath(Bitmap background, AnnotateKind kind = AnnotateKind.Pen)
    {
        var buffer = BgraConvert.FromBitmap(background);
        var xs = new int[400];
        var ys = new int[400];
        var repeats = 12;
        var stopwatch = Stopwatch.StartNew();
        for (var run = 0; run < repeats; run++)
        {
            // The old code rebuilt the point arrays from the draft on every frame.
            for (var i = 0; i < xs.Length; i++)
            {
                xs[i] = 40 + (i * 4);
                ys[i] = 120 + (int)(180 * Math.Sin(i / 14.0));
            }

            var stroke = new AnnotateStroke { Kind = kind, Xs = xs, Ys = ys };
            var composed = AnnotationComposer.Apply(buffer, new[] { stroke });
            using var bitmap = BgraConvert.ToBitmap(composed);
        }

        stopwatch.Stop();
        return stopwatch.Elapsed.TotalMilliseconds / repeats;
    }

    private static (double Pen, double Rect, double Select) MeasureNewPath(Bitmap background)
    {
        using var layer = new AnnoLayerForm { Coalesce = false };
        layer.Attach((Bitmap)background.Clone(), new Rectangle(0, 0, Width, Height));
        layer.Show();
        Application.DoEvents();

        // Freehand: append a sample and repaint only the segment box.
        var pen = new AnnoItem(AnnoTool.Pen, new[] { 40 }, new[] { 120 }) { Width = 6f };
        layer.Draft = pen;
        var stopwatch = Stopwatch.StartNew();
        for (var i = 1; i < 400; i++)
        {
            var x = 40 + (i * 4);
            var y = 120 + (int)(180 * Math.Sin(i / 14.0));
            var previous = new Point(pen.Xs[^1], pen.Ys[^1]);
            pen.Append(x, y);
            var segment = Rectangle.Union(new Rectangle(previous.X, previous.Y, 1, 1), new Rectangle(x, y, 1, 1));
            layer.Redraw(Rectangle.Inflate(segment, 8, 8));
        }

        var penMs = stopwatch.Elapsed.TotalMilliseconds / 399;
        layer.Draft = null;

        // Shape: the dirty area is the union of the previous and the new bounding box.
        var shape = AnnoItem.FromDrag(AnnoTool.Rect, new Point(200, 200), new Point(210, 210));
        shape.Width = 6f;
        layer.Draft = shape;
        stopwatch.Restart();
        for (var i = 1; i <= 200; i++)
        {
            var before = shape.InkBounds;
            shape.SetEnd(200 + (i * 7), 200 + (i * 4));
            layer.Redraw(Rectangle.Union(before, shape.InkBounds));
        }

        var rectMs = stopwatch.Elapsed.TotalMilliseconds / 200;
        layer.CommitDraft(shape);

        // Selection drag: move a committed item and repaint both frames.
        layer.Selected = shape;
        stopwatch.Restart();
        for (var i = 1; i <= 200; i++)
        {
            var before = Rectangle.Inflate(shape.Bounds, 24, 24);
            shape.Move(3, 2);
            layer.Redraw(Rectangle.Union(before, Rectangle.Inflate(shape.Bounds, 24, 24)));
        }

        var selectMs = stopwatch.Elapsed.TotalMilliseconds / 200;
        layer.Hide();
        return (penMs, rectMs, selectMs);
    }

    /// <summary>
    /// Proves the layer actually reaches the screen. The benchmark timings would look fine even
    /// if <c>UpdateLayeredWindowIndirect</c> failed, so this grabs real pixels and writes a PNG.
    /// </summary>
    public static void ProveVisible(string path)
    {
        AnnoLayerForm.CapturableForDev = true;
        using var layer = new AnnoLayerForm();
        using var background = new Bitmap(720, 420, PixelFormat.Format32bppPArgb);
        using (var g = Graphics.FromImage(background))
        {
            g.Clear(Color.FromArgb(238, 240, 244));
            using var band = new SolidBrush(Color.FromArgb(30, 34, 42));
            g.FillRectangle(band, 0, 0, 720, 46);
            using var block = new SolidBrush(Color.FromArgb(200, 206, 216));
            for (var i = 0; i < 4; i++)
            {
                g.FillRectangle(block, 32, 84 + (i * 46), 300 - (i * 40), 14);
            }
        }

        layer.Attach((Bitmap)background.Clone(), new Rectangle(140, 140, 720, 420));
        layer.Show();
        layer.TopMost = true;
        Application.DoEvents();
        Thread.Sleep(220);
        Application.DoEvents();

        var arrow = AnnoItem.FromDrag(AnnoTool.Arrow, new Point(120, 340), new Point(430, 130));
        arrow.ColorArgb = AnnoPalette.Colors[0].ToArgb();
        arrow.Width = 6f;
        layer.CommitDraft(arrow);

        var ellipse = AnnoItem.FromDrag(AnnoTool.Ellipse, new Point(40, 60), new Point(300, 170));
        ellipse.ColorArgb = AnnoPalette.Colors[1].ToArgb();
        ellipse.Width = 4f;
        layer.CommitDraft(ellipse);

        var circle = AnnoItem.FromDrag(AnnoTool.Circle, new Point(470, 250), new Point(600, 350));
        circle.ColorArgb = AnnoPalette.Colors[3].ToArgb();
        circle.Width = 5f;
        layer.CommitDraft(circle);
        layer.Selected = circle;

        Application.DoEvents();
        Thread.Sleep(320);
        Application.DoEvents();

        var bounds = layer.Bounds;
        using var shot = new Bitmap(bounds.Width, bounds.Height, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(shot))
        {
            g.CopyFromScreen(bounds.Location, Point.Empty, bounds.Size);
        }

        shot.Save(path, ImageFormat.Png);
        layer.Hide();
        AnnoLayerForm.CapturableForDev = false;
    }
}
#endif
