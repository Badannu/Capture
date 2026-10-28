namespace CaptureDemo.Core;

/// <summary>
/// Unscaled geometry for the two capsules. Y is measured on the window bitmap,
/// which includes a shadow margin.
/// </summary>
public static class PillMetrics
{
    public const int Shadow = 8;

    // ── annotation bar (dense: 21 slots, must not outgrow a 1080p screen) ────
    public const int BarHeight = 36;

    public const int Pad = 5;

    public const int Button = 32;

    public const int Gap = 8;

    // ── capture mode capsule (first surface the user meets) ─────────────────
    // Deliberately a size up from the annotation bar and close to the Windows
    // snip bar (48 px at 100%), so it reads as a launcher rather than a strip.
    public const int CaptureBarHeight = 46;

    public const int CaptureButton = 40;

    public const int CapturePad = 7;

    public const int CaptureGap = 10;

    public const int Fullscreen = 1;

    public const int Rectangle = 2;

    public const int Window = 3;

    public const int Long = 4;

    public const int Element = 5;

    public const int Freeform = 6;

    public const int Settings = 7;

    public const int Close = 8;

    private static readonly int[] CaptureWidths =
    {
        CaptureButton, CaptureButton, CaptureButton, CaptureButton, CaptureButton, CaptureButton,
        CaptureGap,
        CaptureButton, CaptureButton,
    };

    private static readonly int[] CaptureIds =
    {
        Fullscreen, Rectangle, Window, Long, Element, Freeform, 0, Settings, Close,
    };

    public static int HitCaptureBar(int x, int y) =>
        Hit(x, y, Shadow, CaptureBarHeight, CapturePad, CaptureWidths, CaptureIds);

    public static int ContentWidth(int pad, ReadOnlySpan<int> widths)
    {
        var width = pad * 2;
        foreach (var slot in widths)
        {
            width += slot;
        }

        return width;
    }

    public static int Hit(int x, int y, int shadow, int barHeight, int pad, ReadOnlySpan<int> widths, ReadOnlySpan<int> ids)
    {
        if (widths.Length != ids.Length || y < shadow || y >= shadow + barHeight)
        {
            return 0;
        }

        var local = x - shadow;
        if (local < pad)
        {
            return 0;
        }

        var cursor = pad;
        for (var i = 0; i < widths.Length; i++)
        {
            var next = cursor + widths[i];
            if (local >= cursor && local < next)
            {
                return ids[i];
            }

            cursor = next;
        }

        return 0;
    }
}
