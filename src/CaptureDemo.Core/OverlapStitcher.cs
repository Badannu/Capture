namespace CaptureDemo.Core;

public readonly record struct OverlapMatch(int OverlapRows, double MeanChannelDiff, bool LowConfidence);

public readonly record struct AppendResult(BgraBuffer Image, int AppendedRows);

/// <summary>
/// Independent row-overlap stitcher.
/// The template is the bottom band of the accumulated image. For each candidate overlap
/// it is compared with the aligned band inside the new frame (the part that should already
/// have been seen). The winner is the minimum mean absolute channel difference.
/// This file is not derived from ShareX, jietuba, or Snow Shot source.
/// </summary>
public static class OverlapStitcher
{
    public static OverlapMatch FindOverlap(BgraBuffer accumulated, BgraBuffer next, StitchOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(accumulated);
        ArgumentNullException.ThrowIfNull(next);
        options ??= StitchOptions.Default;

        var maxOverlap = Math.Min(accumulated.Height, next.Height);
        if (maxOverlap <= 0)
        {
            throw new ArgumentException("Images must have a positive height.");
        }

        var minOverlap = Math.Min(Math.Max(1, options.MinOverlap), maxOverlap);
        var columnStep = Math.Max(1, options.ColumnStep);
        var rowStep = Math.Max(1, options.RowStep);
        var width = Math.Min(accumulated.Width, next.Width);
        var (x0, x1) = FrameMetrics.SampleRange(width, options.RightIgnore);

        var count = maxOverlap - minOverlap + 1;
        var scores = new double[count];
        var best = double.PositiveInfinity;
        for (var overlap = minOverlap; overlap <= maxOverlap; overlap++)
        {
            var score = Score(accumulated, next, overlap, options.StripRows, x0, x1, columnStep, rowStep);
            scores[overlap - minOverlap] = score;
            if (score < best)
            {
                best = score;
            }
        }

        var chosen = Choose(scores, minOverlap, best, options, next.Height);
        var chosenScore = scores[chosen - minOverlap];
        return new OverlapMatch(chosen, chosenScore, chosenScore > options.LowConfidenceMeanDiff);
    }

    public static AppendResult Append(BgraBuffer accumulated, BgraBuffer next, int overlapRows, int maxHeight)
    {
        ArgumentNullException.ThrowIfNull(accumulated);
        ArgumentNullException.ThrowIfNull(next);
        if (maxHeight <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxHeight));
        }

        if (overlapRows < 0 || overlapRows > next.Height || overlapRows > accumulated.Height)
        {
            throw new ArgumentOutOfRangeException(nameof(overlapRows));
        }

        var width = Math.Min(accumulated.Width, next.Width);
        var accRows = Math.Min(accumulated.Height, maxHeight);
        var room = maxHeight - accRows;
        if (room < 0)
        {
            room = 0;
        }

        var newRows = next.Height - overlapRows;
        if (newRows > room)
        {
            newRows = room;
        }

        if (newRows == 0 && accumulated.Width == width && accumulated.Height == accRows)
        {
            return new AppendResult(accumulated, 0);
        }

        var height = accRows + newRows;
        var pixels = new byte[width * height * 4];
        CopyRows(accumulated, pixels, width, 0, 0, accRows);
        CopyRows(next, pixels, width, overlapRows, accRows, newRows);
        return new AppendResult(new BgraBuffer(width, height, pixels), newRows);
    }

    private static int Choose(double[] scores, int minOverlap, double best, StitchOptions options, int nextHeight)
    {
        var maxOverlap = minOverlap + scores.Length - 1;
        var confident = best <= options.LowConfidenceMeanDiff;
        if (!confident)
        {
            // A weak match must not pretend the new frame was already fully contained,
            // or the scroller stops as if it had reached the bottom.
            if (TryPreferred(options, nextHeight, minOverlap, maxOverlap, out var preferred))
            {
                return preferred;
            }

            return minOverlap;
        }

        var limit = Math.Max(best + 0.35, best * 1.12);
        int? preferredOverlap = null;
        if (TryPreferred(options, nextHeight, minOverlap, maxOverlap, out var pref))
        {
            preferredOverlap = pref;
        }

        var chosen = -1;
        for (var i = 0; i < scores.Length; i++)
        {
            if (scores[i] > limit)
            {
                continue;
            }

            var overlap = minOverlap + i;
            if (chosen < 0)
            {
                chosen = overlap;
                continue;
            }

            if (preferredOverlap is int target)
            {
                var newDistance = Math.Abs(overlap - target);
                var oldDistance = Math.Abs(chosen - target);
                if (newDistance < oldDistance || (newDistance == oldDistance && overlap > chosen))
                {
                    chosen = overlap;
                }
            }
            else if (overlap > chosen)
            {
                chosen = overlap;
            }
        }

        return chosen < 0 ? minOverlap : chosen;
    }

    private static bool TryPreferred(StitchOptions options, int nextHeight, int minOverlap, int maxOverlap, out int preferred)
    {
        preferred = 0;
        if (options.PreviousNewRows is not int rows || rows < 0)
        {
            return false;
        }

        preferred = nextHeight - rows;
        return preferred >= minOverlap && preferred <= maxOverlap;
    }

    private static double Score(
        BgraBuffer accumulated,
        BgraBuffer next,
        int overlap,
        int stripRows,
        int x0,
        int x1,
        int columnStep,
        int rowStep)
    {
        var rows = Math.Min(Math.Max(1, stripRows), overlap);
        var accY = accumulated.Height - rows;
        var nextY = overlap - rows;
        long sad = 0;
        var samples = 0;
        for (var y = 0; y < rows; y += rowStep)
        {
            var rowAcc = (accY + y) * accumulated.Width * 4;
            var rowNext = (nextY + y) * next.Width * 4;
            for (var x = x0; x < x1; x += columnStep)
            {
                var ia = rowAcc + x * 4;
                var ib = rowNext + x * 4;
                sad += FrameMetrics.Abs(accumulated.Pixels[ia], next.Pixels[ib]);
                sad += FrameMetrics.Abs(accumulated.Pixels[ia + 1], next.Pixels[ib + 1]);
                sad += FrameMetrics.Abs(accumulated.Pixels[ia + 2], next.Pixels[ib + 2]);
                samples++;
            }
        }

        if (samples == 0)
        {
            return double.PositiveInfinity;
        }

        return sad / (samples * 3.0);
    }

    private static void CopyRows(BgraBuffer source, byte[] destination, int width, int srcY, int dstY, int rowCount)
    {
        if (rowCount <= 0)
        {
            return;
        }

        var srcStride = source.Width * 4;
        var dstStride = width * 4;
        var copy = width * 4;
        for (var i = 0; i < rowCount; i++)
        {
            Buffer.BlockCopy(source.Pixels, (srcY + i) * srcStride, destination, (dstY + i) * dstStride, copy);
        }
    }
}
