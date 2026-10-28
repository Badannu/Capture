namespace CaptureDemo.Core;

/// <summary>Inclusive-exclusive bounds of pixels that changed between two viewports.</summary>
public readonly record struct MotionSpan(int Top, int Bottom, int Left, int Right)
{
    public int RowSpan => Math.Max(0, Bottom - Top);

    public int ColumnSpan => Math.Max(0, Right - Left);

    public bool IsEmpty => RowSpan <= 0 || ColumnSpan <= 0;
}

/// <summary>A crop of a captured frame, in that frame's pixel coordinates.</summary>
public readonly record struct FrameCrop(int X, int Y, int Width, int Height);

/// <summary>
/// Finds the band that actually scrolled inside a large selection (document body inside a
/// full-desktop rect) so the capture can follow that band instead of stopping on static chrome.
/// </summary>
public static class MotionBand
{
    public const double ChangeThreshold = 3.0;

    public const int MinSpan = 48;

    public const int MinMargin = 16;

    public const int Pad = 8;

    public static MotionSpan Measure(BgraBuffer previous, BgraBuffer current, int rightIgnore = 18)
    {
        ArgumentNullException.ThrowIfNull(previous);
        ArgumentNullException.ThrowIfNull(current);
        var rows = FrameMetrics.ChangedRowSpan(previous, current, ChangeThreshold, columnStep: 4, rightIgnore);
        var columns = FrameMetrics.ChangedColumnSpan(previous, current, ChangeThreshold, rowStep: 2, rightIgnore);
        return new MotionSpan(rows.Top, rows.Bottom, columns.Left, columns.Right);
    }

    /// <summary>
    /// Returns a crop when a static margin is large enough to be chrome (ribbon, taskbar, page gutter).
    /// A whole-frame scroll, or a caret-sized flicker, returns null.
    /// </summary>
    public static FrameCrop? SuggestCrop(int width, int height, MotionSpan span)
    {
        if (width < MinSpan || height < MinSpan || span.RowSpan < MinSpan)
        {
            return null;
        }

        var y0 = Math.Max(0, span.Top - Pad);
        var y1 = Math.Min(height, span.Bottom + Pad);
        var x0 = 0;
        var x1 = width;
        if (span.ColumnSpan >= MinSpan)
        {
            x0 = Math.Max(0, span.Left - Pad);
            x1 = Math.Min(width, span.Right + Pad);
        }

        var trimTop = y0 >= MinMargin;
        var trimBottom = height - y1 >= MinMargin;
        var trimLeft = x0 >= MinMargin;
        var trimRight = width - x1 >= MinMargin;
        if (!trimTop && !trimBottom && !trimLeft && !trimRight)
        {
            return null;
        }

        if (!trimTop)
        {
            y0 = 0;
        }

        if (!trimBottom)
        {
            y1 = height;
        }

        if (!trimLeft)
        {
            x0 = 0;
        }

        if (!trimRight)
        {
            x1 = width;
        }

        var cropWidth = x1 - x0;
        var cropHeight = y1 - y0;
        if (cropWidth < MinSpan || cropHeight < MinSpan)
        {
            return null;
        }

        if (x0 == 0 && y0 == 0 && cropWidth == width && cropHeight == height)
        {
            return null;
        }

        return new FrameCrop(x0, y0, cropWidth, cropHeight);
    }
}
