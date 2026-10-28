using Xunit;

namespace CaptureDemo.Core.Tests;

public class SettingsAndAnnotationTests
{
    [Fact]
    public void Hotkey_ParsesCtrlAltS()
    {
        Assert.True(HotkeyChord.TryParse("Ctrl+Alt+S", out var chord));
        Assert.Equal(HotkeyChord.ModControl | HotkeyChord.ModAlt, chord.Modifiers);
        Assert.Equal(0x53u, chord.VirtualKey);
        Assert.False(HotkeyChord.TryParse("Ctrl+Alt", out _));
        Assert.True(HotkeyChord.TryParse("Ctrl + Shift + F5", out var fn));
        Assert.Equal(0x74u, fn.VirtualKey);
    }

    [Fact]
    public void Settings_RoundTrip_KeepsHotkeyAndFolder()
    {
        var path = Path.Combine(Path.GetTempPath(), "capture-demo-settings-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            var settings = AppSettings.CreateDefault();
            settings.AwakenHotkey = "Ctrl+Alt+S";
            settings.SaveDirectory = @"D:\Shots";
            settings.FilenamePattern = "{kind}-{yyyyMMdd}";
            settings.CopyToClipboard = false;
            settings.ScrollSpeed = 2;
            settings.SampleMilliseconds = 80;
            settings.Save(path);

            var loaded = AppSettings.Load(path);
            Assert.Equal("Ctrl+Alt+S", loaded.AwakenHotkey);
            Assert.Equal(@"D:\Shots", loaded.SaveDirectory);
            Assert.Equal("{kind}-{yyyyMMdd}", loaded.FilenamePattern);
            Assert.False(loaded.CopyToClipboard);
            Assert.Equal(2, loaded.ScrollSpeed);
            Assert.Equal(80, loaded.SampleMilliseconds);
            Assert.Equal(@"D:\Shots", loaded.ResolveDirectory());
        }
        finally
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }

    [Fact]
    public void FileName_ReplacesTokensAndStripsIllegalCharacters()
    {
        var time = new DateTime(2026, 10, 27, 15, 4, 5, 7);
        var name = AppSettings.FormatFileName("{kind}-{yyyyMMdd}-{HHmmss}-{fff}", "rect", time);
        Assert.Equal("rect-20261027-150405-007", name);

        var messy = AppSettings.FormatFileName("a/b {kind}", "window", time);
        Assert.DoesNotContain("/", messy);
        Assert.Contains("window", messy);
    }

    [Fact]
    public void Pen_PaintsTheMiddleOfTheStroke()
    {
        var source = Solid(24, 16, 255, 255, 255);
        var stroke = new AnnotateStroke
        {
            Kind = AnnotateKind.Pen,
            Xs = new[] { 2, 20 },
            Ys = new[] { 8, 8 },
        };
        var painted = AnnotationComposer.Apply(source, new[] { stroke });
        var i = (8 * 24 + 10) * 4;
        Assert.Equal(220, painted.Pixels[i + 2]);
        Assert.Equal(255, painted.Pixels[(2 * 24 + 10) * 4]);
    }

    [Fact]
    public void Mosaic_FlattensABlock()
    {
        var source = Solid(36, 36, 0, 0, 0);
        for (var x = 0; x < 12; x++)
        {
            var i = (4 * 36 + x) * 4;
            source.Pixels[i] = 0;
            source.Pixels[i + 1] = 0;
            source.Pixels[i + 2] = (byte)(10 + x * 10);
        }

        var stroke = new AnnotateStroke
        {
            Kind = AnnotateKind.Mosaic,
            Xs = new[] { 0, 24 },
            Ys = new[] { 0, 24 },
        };
        var painted = AnnotationComposer.Apply(source, new[] { stroke });
        var a = (4 * 36 + 1) * 4;
        var b = (4 * 36 + 8) * 4;
        Assert.Equal(painted.Pixels[a + 2], painted.Pixels[b + 2]);
        Assert.True(painted.Pixels[a + 2] > 0);
    }

    [Fact]
    public void PolygonMask_ClearsOutsideAndKeepsTheInside()
    {
        var source = Solid(30, 30, 20, 30, 40);
        int[] xs = { 2, 18, 18, 2 };
        int[] ys = { 2, 2, 16, 16 };
        var masked = AnnotationComposer.MaskOutsidePolygon(source, xs, ys);
        Assert.Equal(40, masked.Pixels[(8 * 30 + 8) * 4 + 2]);
        Assert.Equal(255, masked.Pixels[(8 * 30 + 8) * 4 + 3]);
        Assert.Equal(0, masked.Pixels[(1 * 30 + 1) * 4 + 3]);
        Assert.Equal(0, masked.Pixels[(20 * 30 + 20) * 4 + 3]);
    }

    [Fact]
    public void Highlight_TintsInsideTheRectangleOnly()
    {
        var source = Solid(20, 20, 0, 0, 0);
        var stroke = new AnnotateStroke
        {
            Kind = AnnotateKind.Highlight,
            Xs = new[] { 4, 12 },
            Ys = new[] { 4, 10 },
        };
        var painted = AnnotationComposer.Apply(source, new[] { stroke });
        Assert.True(painted.Pixels[(6 * 20 + 6) * 4 + 2] > 40);
        Assert.Equal(0, painted.Pixels[(1 * 20 + 1) * 4 + 2]);
    }

    private static BgraBuffer Solid(int width, int height, byte b, byte g, byte r)
    {
        var pixels = new byte[width * height * 4];
        for (var i = 0; i < pixels.Length; i += 4)
        {
            pixels[i] = b;
            pixels[i + 1] = g;
            pixels[i + 2] = r;
            pixels[i + 3] = 255;
        }

        return new BgraBuffer(width, height, pixels);
    }
}
