using Xunit;

namespace CaptureDemo.Core.Tests;

public class WheelPaceTests
{
    [Fact]
    public void HalfSpeed_EmitsAboutOneThousandUnitsPerSecond_InSubNotchChunks()
    {
        long debt = 0;
        var emitted = 0;
        var chunks = new int[8];
        var left = 1000;
        while (left > 0)
        {
            var step = Math.Min(WheelPace.TickMilliseconds, left);
            var count = WheelPace.Drain(ref debt, WheelPace.UnitsPerSecond(1), step, chunks);
            Assert.InRange(count, 1, chunks.Length);
            for (var i = 0; i < count; i++)
            {
                Assert.InRange(chunks[i], 1, WheelPace.MaxChunk);
                emitted += chunks[i];
            }

            left -= step;
        }

        Assert.InRange(emitted, 996, 1010);
        Assert.True(debt < 1000);
        Assert.Equal(WheelPace.HalfSpeedUnitsPerSecond, WheelPace.UnitsPerSecond(1));
        Assert.Equal(10 * WheelPace.HalfSpeedUnitsPerSecond, WheelPace.UnitsPerSecond(99));
    }

    [Fact]
    public void FasterSpeed_SplitsATickIntoSubNotchChunks()
    {
        long debt = 0;
        Span<int> chunks = stackalloc int[8];
        var count = WheelPace.Drain(ref debt, WheelPace.UnitsPerSecond(10), 12, chunks);
        Assert.Equal(3, count);
        Assert.Equal(40, chunks[0]);
        Assert.Equal(40, chunks[1]);
        Assert.Equal(40, chunks[2]);
        Assert.True(debt < 1000);
    }

    [Fact]
    public void FourMillisecondTicks_StayWellUnderOneNotch()
    {
        long debt = 0;
        Span<int> chunks = stackalloc int[4];
        var count = WheelPace.Drain(ref debt, WheelPace.UnitsPerSecond(1), WheelPace.TickMilliseconds, chunks);
        Assert.Equal(1, count);
        Assert.Equal(4, chunks[0]);
        Assert.True(chunks[0] < 120);
    }
}
