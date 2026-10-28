namespace CaptureDemo.Core;

public sealed class AssemblyStep
{
    public required BgraBuffer? Accumulated { get; init; }

    public required BgraBuffer PreviousFrame { get; init; }

    public required int IdenticalStreak { get; init; }

    public required int? LastNewRows { get; init; }

    public required bool Stop { get; init; }

    public required string? StopReason { get; init; }

    public required bool LowConfidence { get; init; }

    public required int AppendedRows { get; init; }

    public required int OverlapRows { get; init; }

    public required double MatchMeanDiff { get; init; }

    /// <summary>Header rows kept once, from the first frame, and left out of later overlap matches.</summary>
    public required int StickyTop { get; init; }

    /// <summary>Footer rows kept once, from the first frame, and left out of later overlap matches.</summary>
    public required int StickyBottom { get; init; }
}

/// <summary>
/// Pure "what do we do with the next captured frame" step used by the Windows scroll loop.
/// </summary>
public static class ScrollAssembler
{
    public static AssemblyStep AddFrame(
        BgraBuffer? accumulated,
        BgraBuffer? previousFrame,
        BgraBuffer frame,
        int identicalStreak,
        int? lastNewRows,
        AssemblerOptions? options = null,
        int stickyTop = 0,
        int stickyBottom = 0,
        bool detectSticky = false)
    {
        ArgumentNullException.ThrowIfNull(frame);
        options ??= AssemblerOptions.Default;
        if (identicalStreak < 0)
        {
            identicalStreak = 0;
        }

        if (detectSticky && previousFrame != null)
        {
            var detected = StickyBandDetector.Detect(previousFrame, frame, options.Sticky);
            var locked = StickyBandDetector.Lock(new StickyBands(stickyTop, stickyBottom), detected);
            stickyTop = locked.Top;
            stickyBottom = locked.Bottom;
        }

        var bands = Sanitize(stickyTop, stickyBottom, frame, accumulated);

        if (previousFrame != null && FramesAreUnchanged(previousFrame, frame, options))
        {
            var streak = identicalStreak + 1;
            var stop = streak >= Math.Max(1, options.IdenticalPairLimit);
            return Step(
                accumulated ?? frame,
                frame,
                streak,
                lastNewRows,
                stop,
                stop ? StopReasons.Identical : null,
                lowConfidence: false,
                appendedRows: 0,
                overlapRows: frame.Height,
                matchMeanDiff: 0,
                bands);
        }

        if (accumulated == null)
        {
            var first = frame.Height > options.MaxHeight ? frame.CropHeight(options.MaxHeight) : frame;
            var capped = first.Height >= options.MaxHeight;
            return Step(
                first,
                frame,
                identicalStreak: 0,
                lastNewRows: null,
                stop: capped,
                stopReason: capped ? StopReasons.MaxHeight : null,
                lowConfidence: false,
                appendedRows: first.Height,
                overlapRows: 0,
                matchMeanDiff: 0,
                bands);
        }

        if (bands.Top == 0 && bands.Bottom == 0)
        {
            return StitchFull(accumulated, frame, identicalStreak, lastNewRows, options, bands);
        }

        return StitchWithoutChrome(accumulated, frame, identicalStreak, lastNewRows, options, bands);
    }

    private static AssemblyStep StitchFull(
        BgraBuffer accumulated,
        BgraBuffer frame,
        int identicalStreak,
        int? lastNewRows,
        AssemblerOptions options,
        StickyBands bands)
    {
        var stitchOptions = options.Stitch with { PreviousNewRows = lastNewRows };
        var match = OverlapStitcher.FindOverlap(accumulated, frame, stitchOptions);
        var append = OverlapStitcher.Append(accumulated, frame, match.OverlapRows, options.MaxHeight);
        return FromAppend(
            frame,
            identicalStreak,
            lastNewRows,
            options,
            bands,
            match,
            append.Image,
            append.AppendedRows);
    }

    private static AssemblyStep StitchWithoutChrome(
        BgraBuffer accumulated,
        BgraBuffer frame,
        int identicalStreak,
        int? lastNewRows,
        AssemblerOptions options,
        StickyBands bands)
    {
        var top = Math.Min(bands.Top, Math.Min(accumulated.Height, frame.Height));
        var bottom = Math.Min(bands.Bottom, Math.Min(accumulated.Height, frame.Height) - top);
        if (bottom < 0)
        {
            bottom = 0;
        }

        var accMiddleCount = accumulated.Height - top - bottom;
        var nextMiddleCount = frame.Height - top - bottom;
        if (accMiddleCount < 1 || nextMiddleCount < 1)
        {
            return StitchFull(accumulated, frame, identicalStreak, lastNewRows, options, new StickyBands(0, 0));
        }

        var chrome = top + bottom;
        var middleMax = options.MaxHeight - chrome;
        if (middleMax < 1)
        {
            return Step(
                accumulated,
                frame,
                identicalStreak,
                lastNewRows,
                stop: true,
                stopReason: StopReasons.MaxHeight,
                lowConfidence: false,
                appendedRows: 0,
                overlapRows: 0,
                matchMeanDiff: 0,
                new StickyBands(top, bottom));
        }

        var accMiddle = accumulated.SliceRows(top, accMiddleCount);
        var nextMiddle = frame.SliceRows(top, nextMiddleCount);
        var stitchOptions = options.Stitch with { PreviousNewRows = lastNewRows };
        var match = OverlapStitcher.FindOverlap(accMiddle, nextMiddle, stitchOptions);
        var append = OverlapStitcher.Append(accMiddle, nextMiddle, match.OverlapRows, middleMax);
        BgraBuffer image;
        if (append.AppendedRows <= 0 && append.Image.Width == accMiddle.Width && append.Image.Height == accMiddle.Height)
        {
            image = accumulated;
        }
        else
        {
            var parts = new List<BgraBuffer>(3);
            if (top > 0)
            {
                parts.Add(accumulated.SliceRows(0, top));
            }

            parts.Add(append.Image);
            if (bottom > 0)
            {
                parts.Add(accumulated.SliceRows(accumulated.Height - bottom, bottom));
            }

            image = BgraBuffer.ConcatVertical(parts);
        }

        return FromAppend(
            frame,
            identicalStreak,
            lastNewRows,
            options,
            new StickyBands(top, bottom),
            match,
            image,
            append.AppendedRows);
    }

    private static AssemblyStep FromAppend(
        BgraBuffer frame,
        int identicalStreak,
        int? lastNewRows,
        AssemblerOptions options,
        StickyBands bands,
        OverlapMatch match,
        BgraBuffer image,
        int appendedRows)
    {
        if (appendedRows <= 0)
        {
            var streak = identicalStreak + 1;
            var stop = streak >= Math.Max(1, options.IdenticalPairLimit);
            return Step(
                image,
                frame,
                streak,
                lastNewRows,
                stop,
                stop ? StopReasons.Identical : null,
                match.LowConfidence,
                appendedRows: 0,
                match.OverlapRows,
                match.MeanChannelDiff,
                bands);
        }

        var hitMax = image.Height >= options.MaxHeight;
        return Step(
            image,
            frame,
            identicalStreak: 0,
            lastNewRows: appendedRows,
            stop: hitMax,
            stopReason: hitMax ? StopReasons.MaxHeight : null,
            match.LowConfidence,
            appendedRows,
            match.OverlapRows,
            match.MeanChannelDiff,
            bands);
    }

    private static bool FramesAreUnchanged(BgraBuffer previous, BgraBuffer frame, AssemblerOptions options)
    {
        var mean = FrameMetrics.MeanChannelDiff(
            previous,
            frame,
            options.Stitch.ColumnStep,
            options.Stitch.RowStep,
            options.Stitch.RightIgnore);
        if (mean > options.IdenticalMeanDiff)
        {
            // The average already moved. A full-screen mean can also stay under the
            // threshold while a sparse document column scrolls, so a low mean is not enough to stop.
            return false;
        }

        var changedRows = FrameMetrics.CountRowsAbove(
            previous,
            frame,
            options.IdenticalRowThreshold,
            options.Stitch.ColumnStep,
            options.Stitch.RightIgnore);
        var changedColumns = FrameMetrics.CountColumnsAbove(
            previous,
            frame,
            options.IdenticalRowThreshold,
            options.Stitch.RowStep,
            options.Stitch.RightIgnore);
        var rowAllowance = Math.Max(2, frame.Height / 80);
        var columnAllowance = Math.Max(4, frame.Width / 80);
        return changedRows <= rowAllowance && changedColumns <= columnAllowance;
    }

    private static StickyBands Sanitize(int stickyTop, int stickyBottom, BgraBuffer frame, BgraBuffer? accumulated)
    {
        var top = Math.Max(0, stickyTop);
        var bottom = Math.Max(0, stickyBottom);
        if (top >= frame.Height || bottom >= frame.Height || top + bottom >= frame.Height - 8)
        {
            return default;
        }

        if (accumulated != null && (top >= accumulated.Height || bottom >= accumulated.Height || top + bottom >= accumulated.Height - 8))
        {
            return default;
        }

        return new StickyBands(top, bottom);
    }

    private static AssemblyStep Step(
        BgraBuffer? accumulated,
        BgraBuffer previousFrame,
        int identicalStreak,
        int? lastNewRows,
        bool stop,
        string? stopReason,
        bool lowConfidence,
        int appendedRows,
        int overlapRows,
        double matchMeanDiff,
        StickyBands bands)
    {
        return new AssemblyStep
        {
            Accumulated = accumulated,
            PreviousFrame = previousFrame,
            IdenticalStreak = identicalStreak,
            LastNewRows = lastNewRows,
            Stop = stop,
            StopReason = stopReason,
            LowConfidence = lowConfidence,
            AppendedRows = appendedRows,
            OverlapRows = overlapRows,
            MatchMeanDiff = matchMeanDiff,
            StickyTop = bands.Top,
            StickyBottom = bands.Bottom,
        };
    }
}
