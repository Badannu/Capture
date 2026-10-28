namespace CaptureDemo.Core;

public enum AnnotateKind
{
    Pen,
    Arrow,
    Highlight,
    Mosaic,
}

public sealed class AnnotateStroke
{
    public required AnnotateKind Kind { get; init; }

    public required int[] Xs { get; init; }

    public required int[] Ys { get; init; }
}

/// <summary>
/// Draws a short list of strokes onto a straight-alpha BGRA buffer. No Win32.
/// </summary>
public static class AnnotationComposer
{
    public const int PenRadius = 2;

    public const int MosaicBlock = 12;

    public static BgraBuffer Apply(BgraBuffer source, IReadOnlyList<AnnotateStroke> strokes)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(strokes);
        var pixels = new byte[source.Pixels.Length];
        Buffer.BlockCopy(source.Pixels, 0, pixels, 0, pixels.Length);
        var image = new BgraBuffer(source.Width, source.Height, pixels);
        foreach (var stroke in strokes)
        {
            if (stroke == null)
            {
                continue;
            }

            Draw(image, stroke);
        }

        return image;
    }

    /// <summary>Sets alpha (and color) to 0 for pixels outside the polygon. The inside is unchanged.</summary>
    public static BgraBuffer MaskOutsidePolygon(BgraBuffer source, ReadOnlySpan<int> xs, ReadOnlySpan<int> ys)
    {
        ArgumentNullException.ThrowIfNull(source);
        var pixels = new byte[source.Pixels.Length];
        Buffer.BlockCopy(source.Pixels, 0, pixels, 0, pixels.Length);
        var image = new BgraBuffer(source.Width, source.Height, pixels);
        var count = Math.Min(xs.Length, ys.Length);
        if (count < 3)
        {
            return image;
        }

        Span<int> hits = count <= 256 ? stackalloc int[count] : new int[count];
        for (var y = 0; y < image.Height; y++)
        {
            var n = 0;
            for (var i = 0; i < count; i++)
            {
                var j = (i + 1) % count;
                var y0 = ys[i];
                var y1 = ys[j];
                if (y0 == y1)
                {
                    continue;
                }

                var top = Math.Min(y0, y1);
                var bottom = Math.Max(y0, y1);
                if (y < top || y >= bottom)
                {
                    continue;
                }

                var t = (y + 0.5 - y0) / (y1 - y0);
                hits[n++] = (int)Math.Round(xs[i] + (xs[j] - xs[i]) * t);
            }

            if (n < 2)
            {
                ClearSpan(image, y, 0, image.Width);
                continue;
            }

            hits.Slice(0, n).Sort();
            var cursor = 0;
            for (var k = 0; k + 1 < n; k += 2)
            {
                var left = Math.Clamp(hits[k], 0, image.Width);
                var right = Math.Clamp(hits[k + 1], 0, image.Width);
                if (left > cursor)
                {
                    ClearSpan(image, y, cursor, left);
                }

                cursor = Math.Max(cursor, right);
            }

            if (cursor < image.Width)
            {
                ClearSpan(image, y, cursor, image.Width);
            }
        }

        return image;
    }

    private static void Draw(BgraBuffer image, AnnotateStroke stroke)
    {
        var n = Math.Min(stroke.Xs.Length, stroke.Ys.Length);
        if (n <= 0)
        {
            return;
        }

        switch (stroke.Kind)
        {
            case AnnotateKind.Pen:
                if (n == 1)
                {
                    Stamp(image, stroke.Xs[0], stroke.Ys[0], PenRadius, 40, 40, 220);
                    return;
                }

                for (var i = 1; i < n; i++)
                {
                    Stroke(image, stroke.Xs[i - 1], stroke.Ys[i - 1], stroke.Xs[i], stroke.Ys[i], PenRadius, 40, 40, 220);
                }

                break;
            case AnnotateKind.Arrow:
                if (n < 2)
                {
                    return;
                }

                var x0 = stroke.Xs[0];
                var y0 = stroke.Ys[0];
                var x1 = stroke.Xs[n - 1];
                var y1 = stroke.Ys[n - 1];
                Stroke(image, x0, y0, x1, y1, PenRadius, 40, 40, 220);
                DrawHead(image, x0, y0, x1, y1);
                break;
            case AnnotateKind.Highlight:
                BlendRect(image, Bounds(stroke, n), 10, 214, 255, 110);
                break;
            case AnnotateKind.Mosaic:
                Pixelate(image, Bounds(stroke, n), MosaicBlock);
                break;
        }
    }

    private static (int Left, int Top, int Right, int Bottom) Bounds(AnnotateStroke stroke, int n)
    {
        var left = stroke.Xs[0];
        var right = left;
        var top = stroke.Ys[0];
        var bottom = top;
        for (var i = 1; i < n; i++)
        {
            left = Math.Min(left, stroke.Xs[i]);
            right = Math.Max(right, stroke.Xs[i]);
            top = Math.Min(top, stroke.Ys[i]);
            bottom = Math.Max(bottom, stroke.Ys[i]);
        }

        return (left, top, right, bottom);
    }

    private static void DrawHead(BgraBuffer image, int x0, int y0, int x1, int y1)
    {
        var dx = x1 - x0;
        var dy = y1 - y0;
        var length = Math.Sqrt(dx * dx + dy * dy);
        if (length < 8)
        {
            return;
        }

        var ux = dx / length;
        var uy = dy / length;
        var head = Math.Min(18, length * 0.45);
        var bx = x1 - ux * head;
        var by = y1 - uy * head;
        var px = -uy * (head * 0.55);
        var py = ux * (head * 0.55);
        Stroke(image, x1, y1, (int)Math.Round(bx + px), (int)Math.Round(by + py), PenRadius, 40, 40, 220);
        Stroke(image, x1, y1, (int)Math.Round(bx - px), (int)Math.Round(by - py), PenRadius, 40, 40, 220);
    }

    private static void Stroke(BgraBuffer image, int x0, int y0, int x1, int y1, int radius, byte b, byte g, byte r)
    {
        var dx = Math.Abs(x1 - x0);
        var dy = Math.Abs(y1 - y0);
        var sx = x0 < x1 ? 1 : -1;
        var sy = y0 < y1 ? 1 : -1;
        var err = dx - dy;
        while (true)
        {
            Stamp(image, x0, y0, radius, b, g, r);
            if (x0 == x1 && y0 == y1)
            {
                break;
            }

            var e2 = 2 * err;
            if (e2 > -dy)
            {
                err -= dy;
                x0 += sx;
            }

            if (e2 < dx)
            {
                err += dx;
                y0 += sy;
            }
        }
    }

    private static void Stamp(BgraBuffer image, int cx, int cy, int radius, byte b, byte g, byte r)
    {
        for (var y = cy - radius; y <= cy + radius; y++)
        {
            if (y < 0 || y >= image.Height)
            {
                continue;
            }

            for (var x = cx - radius; x <= cx + radius; x++)
            {
                if (x < 0 || x >= image.Width)
                {
                    continue;
                }

                if ((x - cx) * (x - cx) + (y - cy) * (y - cy) > radius * radius + radius)
                {
                    continue;
                }

                var i = (y * image.Width + x) * 4;
                if (image.Pixels[i + 3] == 0)
                {
                    continue;
                }

                image.Pixels[i] = b;
                image.Pixels[i + 1] = g;
                image.Pixels[i + 2] = r;
                image.Pixels[i + 3] = 255;
            }
        }
    }

    private static void BlendRect(BgraBuffer image, (int Left, int Top, int Right, int Bottom) box, byte b, byte g, byte r, int alpha)
    {
        var left = Math.Clamp(Math.Min(box.Left, box.Right), 0, image.Width);
        var right = Math.Clamp(Math.Max(box.Left, box.Right), 0, image.Width);
        var top = Math.Clamp(Math.Min(box.Top, box.Bottom), 0, image.Height);
        var bottom = Math.Clamp(Math.Max(box.Top, box.Bottom), 0, image.Height);
        if (right - left < 1 || bottom - top < 1)
        {
            return;
        }

        var keep = 255 - alpha;
        for (var y = top; y < bottom; y++)
        {
            for (var x = left; x < right; x++)
            {
                var i = (y * image.Width + x) * 4;
                if (image.Pixels[i + 3] == 0)
                {
                    continue;
                }

                image.Pixels[i] = (byte)((image.Pixels[i] * keep + b * alpha) / 255);
                image.Pixels[i + 1] = (byte)((image.Pixels[i + 1] * keep + g * alpha) / 255);
                image.Pixels[i + 2] = (byte)((image.Pixels[i + 2] * keep + r * alpha) / 255);
                image.Pixels[i + 3] = 255;
            }
        }
    }

    private static void Pixelate(BgraBuffer image, (int Left, int Top, int Right, int Bottom) box, int block)
    {
        var left = Math.Clamp(Math.Min(box.Left, box.Right), 0, image.Width - 1);
        var right = Math.Clamp(Math.Max(box.Left, box.Right), 0, image.Width - 1);
        var top = Math.Clamp(Math.Min(box.Top, box.Bottom), 0, image.Height - 1);
        var bottom = Math.Clamp(Math.Max(box.Top, box.Bottom), 0, image.Height - 1);
        if (right - left < block || bottom - top < block)
        {
            return;
        }

        for (var y0 = top; y0 <= bottom; y0 += block)
        {
            var y1 = Math.Min(bottom, y0 + block - 1);
            for (var x0 = left; x0 <= right; x0 += block)
            {
                var x1 = Math.Min(right, x0 + block - 1);
                long sb = 0;
                long sg = 0;
                long sr = 0;
                var n = 0;
                for (var y = y0; y <= y1; y++)
                {
                    for (var x = x0; x <= x1; x++)
                    {
                        var i = (y * image.Width + x) * 4;
                        if (image.Pixels[i + 3] == 0)
                        {
                            continue;
                        }

                        sb += image.Pixels[i];
                        sg += image.Pixels[i + 1];
                        sr += image.Pixels[i + 2];
                        n++;
                    }
                }

                if (n == 0)
                {
                    continue;
                }

                var bb = (byte)(sb / n);
                var gg = (byte)(sg / n);
                var rr = (byte)(sr / n);
                for (var y = y0; y <= y1; y++)
                {
                    for (var x = x0; x <= x1; x++)
                    {
                        var i = (y * image.Width + x) * 4;
                        if (image.Pixels[i + 3] == 0)
                        {
                            continue;
                        }

                        image.Pixels[i] = bb;
                        image.Pixels[i + 1] = gg;
                        image.Pixels[i + 2] = rr;
                        image.Pixels[i + 3] = 255;
                    }
                }
            }
        }
    }

    private static void ClearSpan(BgraBuffer image, int y, int x0, int x1)
    {
        if (x1 <= x0)
        {
            return;
        }

        var start = (y * image.Width + x0) * 4;
        Array.Clear(image.Pixels, start, (x1 - x0) * 4);
    }
}
