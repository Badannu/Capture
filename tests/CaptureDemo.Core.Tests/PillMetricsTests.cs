using Xunit;

namespace CaptureDemo.Core.Tests;

public class PillMetricsTests
{
    [Fact]
    public void CaptureBar_HitsEachModeThenSettingsAndClose()
    {
        var y = PillMetrics.Shadow + (PillMetrics.CaptureBarHeight / 2);
        var origin = PillMetrics.Shadow + PillMetrics.CapturePad;
        var step = PillMetrics.CaptureButton;
        Assert.Equal(PillMetrics.Fullscreen, PillMetrics.HitCaptureBar(origin + 4, y));
        Assert.Equal(PillMetrics.Rectangle, PillMetrics.HitCaptureBar(origin + step + 4, y));
        Assert.Equal(PillMetrics.Window, PillMetrics.HitCaptureBar(origin + (step * 2) + 4, y));
        Assert.Equal(PillMetrics.Long, PillMetrics.HitCaptureBar(origin + (step * 3) + 4, y));
        Assert.Equal(PillMetrics.Element, PillMetrics.HitCaptureBar(origin + (step * 4) + 4, y));
        Assert.Equal(PillMetrics.Freeform, PillMetrics.HitCaptureBar(origin + (step * 5) + 4, y));
        var afterModes = origin + (step * 6);
        Assert.Equal(0, PillMetrics.HitCaptureBar(afterModes + 2, y));
        var settings = afterModes + PillMetrics.CaptureGap + 4;
        Assert.Equal(PillMetrics.Settings, PillMetrics.HitCaptureBar(settings, y));
        Assert.Equal(PillMetrics.Close, PillMetrics.HitCaptureBar(settings + step, y));
        Assert.Equal(0, PillMetrics.HitCaptureBar(origin + 4, 1));
    }

    [Fact]
    public void CaptureBar_IsTallerThanTheAnnotationBar()
    {
        // The capsule is the first surface a user meets; it was reported as too small
        // next to Windows' own snip bar. The annotation bar keeps its dense footprint.
        Assert.True(PillMetrics.CaptureBarHeight >= 44);
        Assert.True(PillMetrics.CaptureBarHeight > PillMetrics.BarHeight);
        Assert.True(PillMetrics.CaptureButton > PillMetrics.Button);

        // Six modes plus the settings/close pair must still fit a small laptop screen.
        var slots = new List<int>();
        slots.AddRange(Enumerable.Repeat(PillMetrics.CaptureButton, 6));
        slots.Add(PillMetrics.CaptureGap);
        slots.Add(PillMetrics.CaptureButton);
        slots.Add(PillMetrics.CaptureButton);
        var width = PillMetrics.ContentWidth(PillMetrics.CapturePad, slots.ToArray());
        Assert.True(width < 520, $"capsule is {width} px at 100% dpi");
    }
}
