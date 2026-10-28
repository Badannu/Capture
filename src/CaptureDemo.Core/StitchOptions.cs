namespace CaptureDemo.Core;

/// <summary>
/// Knobs for row-overlap search. Immutable so a session can reuse <see cref="Default"/>
/// and override <see cref="PreviousNewRows"/> with a record copy.
/// </summary>
public sealed record StitchOptions
{
    public static StitchOptions Default { get; } = new();

    /// <summary>Smallest overlap, in rows, that the search will consider.</summary>
    public int MinOverlap { get; init; } = 8;

    /// <summary>
    /// How many rows of the accumulated image's bottom band are used as the template.
    /// </summary>
    public int StripRows { get; init; } = 64;

    public int ColumnStep { get; init; } = 4;

    public int RowStep { get; init; } = 2;

    /// <summary>Columns ignored on the right (scrollbars). Falls back to the full width when too few columns remain.</summary>
    public int RightIgnore { get; init; } = 18;

    /// <summary>Mean per-channel absolute difference above this is a weak match.</summary>
    public double LowConfidenceMeanDiff { get; init; } = 12;

    /// <summary>New rows accepted on the previous successful append, if any.</summary>
    public int? PreviousNewRows { get; init; }

    /// <summary>
    /// How far around the previous scroll distance the first search may look, as a multiple.
    /// 2.2 means "between 1/2.2 and 2.2 times the last step". Values at or below 1 disable the window.
    /// </summary>
    public double SearchWindowFactor { get; init; } = 2.2;
}

public sealed record AssemblerOptions
{
    public static AssemblerOptions Default { get; } = new();

    public int MaxHeight { get; init; } = 20_000;

    /// <summary>
    /// How many identical consecutive pairs end the capture.
    /// 1 matches "stop when two consecutive frames are nearly the same".
    /// </summary>
    public int IdenticalPairLimit { get; init; } = 1;

    public double IdenticalMeanDiff { get; init; } = 2.0;

    /// <summary>
    /// A row counts as changed when its mean channel difference exceeds this.
    /// Used so a mostly-static full screen does not look "identical" just because the page is sparse.
    /// </summary>
    public double IdenticalRowThreshold { get; init; } = 3.0;

    /// <summary>
    /// Frames that may stitch nothing before the capture gives up.
    /// A fixed footer with a live page counter can make a single frame look unmovable;
    /// one such frame must never end a sixty-page document.
    /// </summary>
    public int ZeroAppendLimit { get; init; } = 3;

    public StitchOptions Stitch { get; init; } = StitchOptions.Default;

    /// <summary>How fixed header/footer bands are found before a frame is stitched.</summary>
    public StickyDetectOptions Sticky { get; init; } = StickyDetectOptions.Default;
}

public static class StopReasons
{
    public const string Identical = "identical";

    /// <summary>The frame moved, but no new rows could be aligned.</summary>
    public const string Stalled = "stalled";

    public const string MaxHeight = "max-height";
}
