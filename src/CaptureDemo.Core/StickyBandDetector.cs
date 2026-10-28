namespace CaptureDemo.Core;

/// <summary>Rows at the top or bottom of a viewport that did not move between two frames.</summary>
public readonly record struct StickyBands(int Top, int Bottom);

/// <summary>
/// Knobs for <see cref="StickyBandDetector"/>.
/// <see cref="TopIgnoreFraction"/> and <see cref="BottomIgnoreFraction"/> are not a blind crop:
/// that edge is excluded only when its rows actually stayed still.
/// </summary>
public sealed record StickyDetectOptions
{
    public static StickyDetectOptions Default { get; } = new();

    /// <summary>A row is treated as static when its mean channel difference is at or below this.</summary>
    public double StaticMeanDiff { get; init; } = 2.5;

    /// <summary>Shorter static runs are ignored so a one-pixel border is not treated as chrome.</summary>
    public int MinRun { get; init; } = 3;

    /// <summary>Neither edge may claim more than this share of the frame.</summary>
    public double MaxFraction { get; init; } = 0.30;

    /// <summary>Default 0. A static top band can still be detected from the pixels.</summary>
    public double TopIgnoreFraction { get; init; }

    /// <summary>Default 12% of the region height, used only when that bottom band did not move.</summary>
    public double BottomIgnoreFraction { get; init; } = 0.12;

    public int ColumnStep { get; init; } = 4;

    public int RightIgnore { get; init; } = 18;

    /// <summary>If the static edges leave fewer scrolling rows than this, the frame is treated as not scrolled.</summary>
    public int MinScrollRows { get; init; } = 8;

    /// <summary>
    /// Fractional ignore bands stop growing at this many pixels.
    /// 12% of a full desktop would otherwise include document margin, not only a taskbar.
    /// A contiguous static run is not limited here.
    /// </summary>
    public int MaxEdgePixels { get; init; } = 96;
}

/// <summary>
/// Finds sticky header/footer strips by comparing the same row in two consecutive viewports.
/// A fixed footer sits on the same rows forever, so it must not be the overlap template and must not be appended again.
/// </summary>
public static class StickyBandDetector
{
    public static StickyBands Detect(BgraBuffer previous, BgraBuffer current, StickyDetectOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(previous);
        ArgumentNullException.ThrowIfNull(current);
        options ??= StickyDetectOptions.Default;
        if (previous.Width != current.Width || previous.Height != current.Height || previous.Height <= 0)
        {
            return default;
        }

        var height = previous.Height;
        var diffs = RowDiffs(previous, current, options);
        var threshold = options.StaticMeanDiff;
        var minScroll = Math.Max(1, options.MinScrollRows);

        var top = RunFromTop(diffs, threshold);
        var bottom = RunFromBottom(diffs, threshold, top);
        // An unmoved frame is static from top to bottom. Do this before the fraction
        // extension, or a cap would turn "nothing scrolled" into a fake header.
        if (top + bottom >= height - minScroll)
        {
            return default;
        }

        top = ExtendTop(diffs, top, bottom, height, minScroll, threshold, options.TopIgnoreFraction, options.MaxEdgePixels);
        bottom = ExtendBottom(diffs, top, bottom, height, minScroll, threshold, options.BottomIgnoreFraction, options.MaxEdgePixels);

        var cap = (int)Math.Floor(height * Clamp01(options.MaxFraction, 0.45));
        if (top > cap)
        {
            top = cap;
        }

        if (bottom > cap)
        {
            bottom = cap;
        }

        var minRun = Math.Max(1, options.MinRun);
        if (top < minRun)
        {
            top = 0;
        }

        if (bottom < minRun)
        {
            bottom = 0;
        }

        if (top + bottom >= height - minScroll)
        {
            return default;
        }

        return new StickyBands(top, bottom);
    }

    /// <summary>Keeps the first non-zero edge. Later frames may shrink a noisy bar; that must not bring it back into the stitch.</summary>
    public static StickyBands Lock(StickyBands locked, StickyBands detected)
    {
        return new StickyBands(
            locked.Top > 0 ? locked.Top : Math.Max(0, detected.Top),
            locked.Bottom > 0 ? locked.Bottom : Math.Max(0, detected.Bottom));
    }

    private static double[] RowDiffs(BgraBuffer previous, BgraBuffer current, StickyDetectOptions options)
    {
        var height = previous.Height;
        var diffs = new double[height];
        var columnStep = Math.Max(1, options.ColumnStep);
        var (x0, x1) = FrameMetrics.SampleRange(previous.Width, options.RightIgnore);
        for (var y = 0; y < height; y++)
        {
            diffs[y] = RowDiff(previous, current, y, x0, x1, columnStep);
        }

        return diffs;
    }

    private static double RowDiff(BgraBuffer left, BgraBuffer right, int y, int x0, int x1, int columnStep)
    {
        long sad = 0;
        var samples = 0;
        var rowLeft = y * left.Width * 4;
        var rowRight = y * right.Width * 4;
        for (var x = x0; x < x1; x += columnStep)
        {
            var ia = rowLeft + x * 4;
            var ib = rowRight + x * 4;
            sad += FrameMetrics.Abs(left.Pixels[ia], right.Pixels[ib]);
            sad += FrameMetrics.Abs(left.Pixels[ia + 1], right.Pixels[ib + 1]);
            sad += FrameMetrics.Abs(left.Pixels[ia + 2], right.Pixels[ib + 2]);
            samples++;
        }

        if (samples == 0)
        {
            return double.PositiveInfinity;
        }

        return sad / (samples * 3.0);
    }

    private static int RunFromTop(double[] diffs, double threshold)
    {
        var top = 0;
        while (top < diffs.Length && diffs[top] <= threshold)
        {
            top++;
        }

        return top;
    }

    private static int RunFromBottom(double[] diffs, double threshold, int top)
    {
        var bottom = 0;
        while (bottom < diffs.Length - top && diffs[diffs.Length - 1 - bottom] <= threshold)
        {
            bottom++;
        }

        return bottom;
    }

    private static int ExtendTop(
        double[] diffs,
        int top,
        int bottom,
        int height,
        int minScroll,
        double threshold,
        double fraction,
        int maxEdgePixels)
    {
        var want = EdgeWant(height, fraction, maxEdgePixels);
        if (want <= top || want <= 0 || want + bottom >= height - minScroll)
        {
            return top;
        }

        return Mean(diffs, 0, want) <= threshold ? want : top;
    }

    private static int ExtendBottom(
        double[] diffs,
        int top,
        int bottom,
        int height,
        int minScroll,
        double threshold,
        double fraction,
        int maxEdgePixels)
    {
        var want = EdgeWant(height, fraction, maxEdgePixels);
        if (want <= bottom || want <= 0 || top + want >= height - minScroll)
        {
            return bottom;
        }

        return Mean(diffs, height - want, want) <= threshold ? want : bottom;
    }

    private static int EdgeWant(int height, double fraction, int maxEdgePixels)
    {
        var want = (int)Math.Floor(height * Clamp01(fraction, 0.45));
        if (maxEdgePixels > 0 && want > maxEdgePixels)
        {
            want = maxEdgePixels;
        }

        return want;
    }

    private static double Mean(double[] diffs, int start, int count)
    {
        if (count <= 0)
        {
            return double.PositiveInfinity;
        }

        double sum = 0;
        for (var i = 0; i < count; i++)
        {
            sum += diffs[start + i];
        }

        return sum / count;
    }

    private static double Clamp01(double value, double max)
    {
        if (value < 0 || double.IsNaN(value))
        {
            return 0;
        }

        return value > max ? max : value;
    }
}
