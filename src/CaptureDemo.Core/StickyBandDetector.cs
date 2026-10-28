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

    /// <summary>
    /// A row whose differences sit inside one narrow column window still counts as static.
    /// A status bar keeps its page counter, word count, and zoom percentage live while the page
    /// scrolls; without this only the blank tail of the bar is recognised, the rest is stitched
    /// as content, and the capture stalls.
    /// </summary>
    public double LocalChangeFraction { get; init; } = 0.25;

    /// <summary>A local change is only forgiven while the whole row stays under this mean difference.</summary>
    public double LocalChangeMeanDiff { get; init; } = 45;

    /// <summary>
    /// How far the bottom band may grow past its strictly static part to swallow rows that only
    /// changed in a narrow column window. Scrolling body text is never this narrow.
    /// </summary>
    public int MaxLocalRun { get; init; } = 64;

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

/// <summary>One row's difference: how much moved, and over which columns.</summary>
public readonly record struct RowStat(double Mean, int FirstChanged, int LastChanged)
{
    public int ChangedSpan => FirstChanged < 0 ? 0 : LastChanged - FirstChanged;

    public bool AnyChanged => FirstChanged >= 0;
}

/// <summary>
/// Finds sticky header/footer strips by comparing the same row in two consecutive viewports.
/// A fixed footer sits on the same rows forever, so it must not be the overlap template and must not be appended again.
/// </summary>
public static class StickyBandDetector
{
    /// <summary>B/G/R difference that marks a column as changed rather than anti-aliasing noise.</summary>
    private const int ColumnChangeThreshold = 45;

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
        var stats = RowStats(previous, current, options, out var sampleWidth);
        var minScroll = Math.Max(1, options.MinScrollRows);

        var top = RunFromTop(stats, options);
        var bottom = RunFromBottom(stats, options, top);
        // An unmoved frame is static from top to bottom. Do this before the fraction
        // extension, or a cap would turn "nothing scrolled" into a fake header.
        if (top + bottom >= height - minScroll)
        {
            return default;
        }

        top = ExtendTop(stats, top, bottom, height, minScroll, options);
        bottom = ExtendBottom(stats, bottom, top, height, minScroll, options);
        bottom = ExtendOverLiveRows(stats, bottom, top, height, sampleWidth, options);

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

    /// <summary>True when the row moved, but everything that moved fits in one narrow column window.</summary>
    public static bool IsLocalRow(RowStat stat, int sampleWidth, StickyDetectOptions? options = null)
    {
        options ??= StickyDetectOptions.Default;
        if (!stat.AnyChanged || stat.Mean > options.LocalChangeMeanDiff)
        {
            return false;
        }

        var allowance = Math.Max(8, (int)Math.Round(Math.Max(1, sampleWidth) * Clamp01(options.LocalChangeFraction, 0.45)));
        return stat.ChangedSpan <= allowance;
    }

    /// <summary>
    /// Grows the bottom band over rows whose only difference is a live readout. Purely static rows
    /// are handled by the normal run; this only bridges the readout rows that sit inside a fixed bar.
    /// </summary>
    private static int ExtendOverLiveRows(RowStat[] stats, int bottom, int top, int height, int sampleWidth, StickyDetectOptions options)
    {
        if (bottom <= 0 || bottom >= height - top)
        {
            return bottom;
        }

        var limit = Math.Min(height - top, Math.Max(bottom, options.MaxLocalRun));
        var grown = bottom;
        while (grown < limit)
        {
            var stat = stats[height - 1 - grown];
            if (stat.Mean > options.StaticMeanDiff && !IsLocalRow(stat, sampleWidth, options))
            {
                break;
            }

            grown++;
        }

        return grown;
    }

    private static RowStat[] RowStats(BgraBuffer previous, BgraBuffer current, StickyDetectOptions options, out int sampleWidth)
    {
        var height = previous.Height;
        var stats = new RowStat[height];
        var columnStep = Math.Max(1, options.ColumnStep);
        var (x0, x1) = FrameMetrics.SampleRange(previous.Width, options.RightIgnore);
        sampleWidth = Math.Max(1, x1 - x0);
        for (var y = 0; y < height; y++)
        {
            stats[y] = RowStatAt(previous, current, y, x0, x1, columnStep);
        }

        return stats;
    }

    private static RowStat RowStatAt(BgraBuffer left, BgraBuffer right, int y, int x0, int x1, int columnStep)
    {
        long sad = 0;
        var samples = 0;
        var first = -1;
        var last = -1;
        var rowLeft = y * left.Width * 4;
        var rowRight = y * right.Width * 4;
        for (var x = x0; x < x1; x += columnStep)
        {
            var ia = rowLeft + x * 4;
            var ib = rowRight + x * 4;
            var delta = FrameMetrics.Abs(left.Pixels[ia], right.Pixels[ib])
                + FrameMetrics.Abs(left.Pixels[ia + 1], right.Pixels[ib + 1])
                + FrameMetrics.Abs(left.Pixels[ia + 2], right.Pixels[ib + 2]);
            sad += delta;
            samples++;
            if (delta > ColumnChangeThreshold)
            {
                if (first < 0)
                {
                    first = x;
                }

                last = x;
            }
        }

        if (samples == 0)
        {
            return new RowStat(double.PositiveInfinity, -1, -1);
        }

        return new RowStat(sad / (samples * 3.0), first, last);
    }

    private static int RunFromTop(RowStat[] stats, StickyDetectOptions options)
    {
        var top = 0;
        while (top < stats.Length && stats[top].Mean <= options.StaticMeanDiff)
        {
            top++;
        }

        return top;
    }

    private static int RunFromBottom(RowStat[] stats, StickyDetectOptions options, int top)
    {
        var bottom = 0;
        while (bottom < stats.Length - top && stats[stats.Length - 1 - bottom].Mean <= options.StaticMeanDiff)
        {
            bottom++;
        }

        return bottom;
    }

    private static int ExtendTop(RowStat[] stats, int top, int bottom, int height, int minScroll, StickyDetectOptions options)
    {
        var want = EdgeWant(height, options.TopIgnoreFraction, options.MaxEdgePixels);
        if (want <= top || want <= 0 || want + bottom >= height - minScroll)
        {
            return top;
        }

        return Mean(stats, 0, want) <= options.StaticMeanDiff ? want : top;
    }

    private static int ExtendBottom(RowStat[] stats, int bottom, int top, int height, int minScroll, StickyDetectOptions options)
    {
        var want = EdgeWant(height, options.BottomIgnoreFraction, options.MaxEdgePixels);
        if (want <= bottom || want <= 0 || top + want >= height - minScroll)
        {
            return bottom;
        }

        return Mean(stats, height - want, want) <= options.StaticMeanDiff ? want : bottom;
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

    private static double Mean(RowStat[] stats, int start, int count)
    {
        if (count <= 0)
        {
            return double.PositiveInfinity;
        }

        double sum = 0;
        for (var i = 0; i < count; i++)
        {
            sum += stats[start + i].Mean;
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
