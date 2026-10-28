using Xunit;

namespace CaptureDemo.Core.Tests;

public class PillMetricsTests
{
    [Fact]
    public void CaptureBar_HitsEachModeThenSettingsAndClose()
    {
        var y = PillMetrics.Shadow + PillMetrics.BarHeight / 2;
        var origin = PillMetrics.Shadow + PillMetrics.Pad;
        Assert.Equal(PillMetrics.Fullscreen, PillMetrics.HitCaptureBar(origin + 4, y));
        Assert.Equal(PillMetrics.Rectangle, PillMetrics.HitCaptureBar(origin + PillMetrics.Button + 4, y));
        Assert.Equal(PillMetrics.Window, PillMetrics.HitCaptureBar(origin + PillMetrics.Button * 2 + 4, y));
        Assert.Equal(PillMetrics.Long, PillMetrics.HitCaptureBar(origin + PillMetrics.Button * 3 + 4, y));
        Assert.Equal(PillMetrics.Element, PillMetrics.HitCaptureBar(origin + PillMetrics.Button * 4 + 4, y));
        Assert.Equal(PillMetrics.Freeform, PillMetrics.HitCaptureBar(origin + PillMetrics.Button * 5 + 4, y));
        var afterModes = origin + PillMetrics.Button * 6;
        Assert.Equal(0, PillMetrics.HitCaptureBar(afterModes + 2, y));
        var settings = afterModes + PillMetrics.Gap + 4;
        Assert.Equal(PillMetrics.Settings, PillMetrics.HitCaptureBar(settings, y));
        Assert.Equal(PillMetrics.Close, PillMetrics.HitCaptureBar(settings + PillMetrics.Button, y));
        Assert.Equal(0, PillMetrics.HitCaptureBar(origin + 4, 1));
    }
}
