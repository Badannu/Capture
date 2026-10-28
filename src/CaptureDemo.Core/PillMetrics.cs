namespace CaptureDemo.Core;

/// <summary>
/// Unscaled geometry for the capture capsule: six mode icons, a gap, settings, close.
/// Y is measured on the window bitmap, which includes a shadow margin.
/// </summary>
public static class PillMetrics
{
    public const int Shadow = 8;

    public const int BarHeight = 36;

    public const int Pad = 5;

    public const int Button = 32;

    public const int Gap = 8;

    public const int Fullscreen = 1;

    public const int Rectangle = 2;

    public const int Window = 3;

    public const int Long = 4;

    public const int Element = 5;

    public const int Freeform = 6;

    public const int Settings = 7;

    public const int Close = 8;

    public static int HitCaptureBar(int x, int y)
    {
        int[] widths = { Button, Button, Button, Button, Button, Button, Gap, Button, Button };
        int[] ids = { Fullscreen, Rectangle, Window, Long, Element, Freeform, 0, Settings, Close };
        return Hit(x, y, Shadow, BarHeight, Pad, widths, ids);
    }

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
