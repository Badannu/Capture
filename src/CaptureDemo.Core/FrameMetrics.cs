namespace CaptureDemo.Core;

public static class FrameMetrics
{
    /// <summary>
    /// Mean absolute difference per B/G/R channel. Images of different size return positive infinity.
    /// </summary>
    public static double MeanChannelDiff(BgraBuffer left, BgraBuffer right, int columnStep, int rowStep, int rightIgnore)
    {
        ArgumentNullException.ThrowIfNull(left);
        ArgumentNullException.ThrowIfNull(right);
        if (left.Width != right.Width || left.Height != right.Height)
        {
            return double.PositiveInfinity;
        }

        columnStep = Math.Max(1, columnStep);
        rowStep = Math.Max(1, rowStep);
        var (x0, x1) = SampleRange(left.Width, rightIgnore);

        long sad = 0;
        var samples = 0;
        for (var y = 0; y < left.Height; y += rowStep)
        {
            var rowLeft = y * left.Width * 4;
            var rowRight = y * right.Width * 4;
            for (var x = x0; x < x1; x += columnStep)
            {
                var ia = rowLeft + x * 4;
                var ib = rowRight + x * 4;
                sad += Abs(left.Pixels[ia], right.Pixels[ib]);
                sad += Abs(left.Pixels[ia + 1], right.Pixels[ib + 1]);
                sad += Abs(left.Pixels[ia + 2], right.Pixels[ib + 2]);
                samples++;
            }
        }

        if (samples == 0)
        {
            return double.PositiveInfinity;
        }

        return sad / (samples * 3.0);
    }

    /// <summary>
    /// How many rows differ by more than <paramref name="threshold"/>.
    /// A full-screen mean can stay tiny when only a document column scrolls; the row count does not.
    /// </summary>
    public static int CountRowsAbove(BgraBuffer left, BgraBuffer right, double threshold, int columnStep, int rightIgnore)
    {
        ArgumentNullException.ThrowIfNull(left);
        ArgumentNullException.ThrowIfNull(right);
        if (left.Width != right.Width || left.Height != right.Height)
        {
            return int.MaxValue;
        }

        columnStep = Math.Max(1, columnStep);
        var (x0, x1) = SampleRange(left.Width, rightIgnore);
        var count = 0;
        for (var y = 0; y < left.Height; y++)
        {
            if (RowMean(left, right, y, x0, x1, columnStep) > threshold)
            {
                count++;
            }
        }

        return count;
    }

    /// <summary>First and last row (exclusive) whose mean difference exceeds <paramref name="threshold"/>.</summary>
    public static (int Top, int Bottom) ChangedRowSpan(BgraBuffer left, BgraBuffer right, double threshold, int columnStep, int rightIgnore)
    {
        ArgumentNullException.ThrowIfNull(left);
        ArgumentNullException.ThrowIfNull(right);
        if (left.Width != right.Width || left.Height != right.Height || left.Height == 0)
        {
            return (0, 0);
        }

        columnStep = Math.Max(1, columnStep);
        var (x0, x1) = SampleRange(left.Width, rightIgnore);
        var top = -1;
        var bottom = -1;
        for (var y = 0; y < left.Height; y++)
        {
            if (RowMean(left, right, y, x0, x1, columnStep) <= threshold)
            {
                continue;
            }

            if (top < 0)
            {
                top = y;
            }

            bottom = y + 1;
        }

        return top < 0 ? (0, 0) : (top, bottom);
    }

    public static int CountColumnsAbove(BgraBuffer left, BgraBuffer right, double threshold, int rowStep, int rightIgnore)
    {
        ArgumentNullException.ThrowIfNull(left);
        ArgumentNullException.ThrowIfNull(right);
        if (left.Width != right.Width || left.Height != right.Height)
        {
            return int.MaxValue;
        }

        rowStep = Math.Max(1, rowStep);
        var limit = SampleRange(left.Width, rightIgnore).Right;
        var count = 0;
        for (var x = 0; x < limit; x++)
        {
            if (ColumnMean(left, right, x, rowStep) > threshold)
            {
                count++;
            }
        }

        return count;
    }

    /// <summary>First and last column (exclusive) whose mean difference exceeds <paramref name="threshold"/>.</summary>
    public static (int Left, int Right) ChangedColumnSpan(BgraBuffer left, BgraBuffer right, double threshold, int rowStep, int rightIgnore)
    {
        ArgumentNullException.ThrowIfNull(left);
        ArgumentNullException.ThrowIfNull(right);
        if (left.Width != right.Width || left.Height != right.Height || left.Width == 0)
        {
            return (0, 0);
        }

        rowStep = Math.Max(1, rowStep);
        var limit = SampleRange(left.Width, rightIgnore).Right;
        var first = -1;
        var last = -1;
        for (var x = 0; x < limit; x++)
        {
            if (ColumnMean(left, right, x, rowStep) <= threshold)
            {
                continue;
            }

            if (first < 0)
            {
                first = x;
            }

            last = x + 1;
        }

        return first < 0 ? (0, 0) : (first, last);
    }

    internal static double RowMean(BgraBuffer left, BgraBuffer right, int y, int x0, int x1, int columnStep)
    {
        long sad = 0;
        var samples = 0;
        var rowLeft = y * left.Width * 4;
        var rowRight = y * right.Width * 4;
        for (var x = x0; x < x1; x += columnStep)
        {
            var ia = rowLeft + x * 4;
            var ib = rowRight + x * 4;
            sad += Abs(left.Pixels[ia], right.Pixels[ib]);
            sad += Abs(left.Pixels[ia + 1], right.Pixels[ib + 1]);
            sad += Abs(left.Pixels[ia + 2], right.Pixels[ib + 2]);
            samples++;
        }

        if (samples == 0)
        {
            return double.PositiveInfinity;
        }

        return sad / (samples * 3.0);
    }

    private static double ColumnMean(BgraBuffer left, BgraBuffer right, int x, int rowStep)
    {
        long sad = 0;
        var samples = 0;
        for (var y = 0; y < left.Height; y += rowStep)
        {
            var ia = (y * left.Width + x) * 4;
            var ib = (y * right.Width + x) * 4;
            sad += Abs(left.Pixels[ia], right.Pixels[ib]);
            sad += Abs(left.Pixels[ia + 1], right.Pixels[ib + 1]);
            sad += Abs(left.Pixels[ia + 2], right.Pixels[ib + 2]);
            samples++;
        }

        if (samples == 0)
        {
            return double.PositiveInfinity;
        }

        return sad / (samples * 3.0);
    }

    internal static (int Left, int Right) SampleRange(int width, int rightIgnore)
    {
        var right = width - Math.Max(0, rightIgnore);
        if (right - 0 < 8 || right > width)
        {
            return (0, width);
        }

        return (0, right);
    }

    internal static int Abs(byte a, byte b) => a >= b ? a - b : b - a;
}
