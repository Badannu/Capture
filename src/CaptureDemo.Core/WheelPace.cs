namespace CaptureDemo.Core;

/// <summary>
/// Turns a wheel speed into a stream of sub-notch deltas.
/// One classic notch is 120 units. Half of the old "3 notches / 180 ms" pace is 1000 units/second.
/// </summary>
public static class WheelPace
{
    public const int HalfSpeedUnitsPerSecond = 1000;

    /// <summary>Largest single <c>mouse_event</c> delta. A full notch is 120, so this stays under one detent.</summary>
    public const int MaxChunk = 40;

    /// <summary>How often a running glide emits. Small on purpose so the page does not step.</summary>
    public const int TickMilliseconds = 4;

    public static int UnitsPerSecond(int speed) =>
        HalfSpeedUnitsPerSecond * Math.Clamp(speed, 1, 10);

    /// <summary>
    /// Adds <paramref name="unitsPerSecond"/> × <paramref name="elapsedMs"/> to <paramref name="debt"/>
    /// (debt is in thousandths of a wheel unit) and writes downward-positive chunks of 1..<see cref="MaxChunk"/>.
    /// Units that do not fit in <paramref name="chunks"/> stay in the debt for the next call.
    /// </summary>
    public static int Drain(ref long debt, int unitsPerSecond, int elapsedMs, Span<int> chunks)
    {
        if (chunks.IsEmpty)
        {
            return 0;
        }

        if (elapsedMs > 0 && unitsPerSecond > 0)
        {
            debt += (long)unitsPerSecond * elapsedMs;
        }

        var count = 0;
        while (debt >= 1000 && count < chunks.Length)
        {
            var units = (int)Math.Min(debt / 1000, MaxChunk);
            if (units < 1)
            {
                break;
            }

            chunks[count++] = units;
            debt -= (long)units * 1000;
        }

        return count;
    }
}
