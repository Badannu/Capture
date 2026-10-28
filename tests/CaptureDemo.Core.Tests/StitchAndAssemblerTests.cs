using Xunit;

namespace CaptureDemo.Core.Tests;

public class StitchAndAssemblerTests
{
    [Fact]
    public void ShiftedFrame_AppendsOnlyNewRows_AndMatchesSource()
    {
        var accumulated = Rows(32, 50, 0);
        var next = Rows(32, 50, 17);

        var match = OverlapStitcher.FindOverlap(accumulated, next);
        Assert.Equal(33, match.OverlapRows);
        Assert.False(match.LowConfidence);
        Assert.True(match.MeanChannelDiff < 0.5);

        var appended = OverlapStitcher.Append(accumulated, next, match.OverlapRows, 20_000);
        Assert.Equal(17, appended.AppendedRows);
        AssertSame(Rows(32, 67, 0), appended.Image);
    }

    [Fact]
    public void StickyHeader_StillAlignsOnTheBottomBand()
    {
        // Taller than the default 64-row template, so the template sits in the scrolling body
        // and not in the fixed header. Real captures should still keep headers out of the rect.
        var accumulated = HeaderThenBody(width: 24, header: 8, bodyStart: 0, bodyCount: 82);
        var next = HeaderThenBody(width: 24, header: 8, bodyStart: 15, bodyCount: 82);

        var match = OverlapStitcher.FindOverlap(accumulated, next);
        Assert.Equal(75, match.OverlapRows);
        Assert.False(match.LowConfidence);

        var appended = OverlapStitcher.Append(accumulated, next, match.OverlapRows, 20_000);
        Assert.Equal(15, appended.AppendedRows);
        AssertSame(HeaderThenBody(24, 8, 0, 97), appended.Image);
    }

    [Fact]
    public void PeriodicContent_PrefersPreviousScrollDistance()
    {
        var frame = Periodic(12, 40, period: 10);
        var withHistory = OverlapStitcher.FindOverlap(frame, frame, StitchOptions.Default with { PreviousNewRows = 10 });
        Assert.Equal(30, withHistory.OverlapRows);
        Assert.False(withHistory.LowConfidence);

        var first = OverlapStitcher.FindOverlap(frame, frame);
        Assert.Equal(40, first.OverlapRows);
    }

    [Fact]
    public void UnrelatedFrames_DoNotPretendTheyFullyOverlap()
    {
        var red = Solid(20, 40, 0, 0, 255);
        var blue = Solid(20, 40, 255, 0, 0);
        var match = OverlapStitcher.FindOverlap(red, blue);
        Assert.True(match.LowConfidence);
        Assert.Equal(StitchOptions.Default.MinOverlap, match.OverlapRows);

        var appended = OverlapStitcher.Append(red, blue, match.OverlapRows, 20_000);
        Assert.Equal(40 - StitchOptions.Default.MinOverlap, appended.AppendedRows);
    }

    [Fact]
    public void DifferentWidths_AreCroppedToTheNarrowerLeftSide()
    {
        var wide = Rows(40, 30, 0);
        var narrow = Rows(28, 30, 12);
        var match = OverlapStitcher.FindOverlap(wide, narrow);
        Assert.Equal(18, match.OverlapRows);
        var appended = OverlapStitcher.Append(wide, narrow, match.OverlapRows, 20_000);
        Assert.Equal(28, appended.Image.Width);
        Assert.Equal(42, appended.Image.Height);
        AssertSame(Rows(28, 42, 0), appended.Image);
    }

    [Fact]
    public void Assembler_StopsWhenTheNextFrameRepeats()
    {
        var options = AssemblerOptions.Default;
        var first = Rows(16, 20, 0);
        var step1 = ScrollAssembler.AddFrame(null, null, first, 0, null, options);
        Assert.False(step1.Stop);
        Assert.Equal(20, step1.Accumulated!.Height);

        var step2 = ScrollAssembler.AddFrame(step1.Accumulated, step1.PreviousFrame, Rows(16, 20, 0), step1.IdenticalStreak, step1.LastNewRows, options);
        Assert.True(step2.Stop);
        Assert.Equal(StopReasons.Identical, step2.StopReason);
        Assert.Equal(20, step2.Accumulated!.Height);
    }

    [Fact]
    public void Assembler_IdenticalPairLimitTwo_WaitsForASecondRepeat()
    {
        var options = AssemblerOptions.Default with { IdenticalPairLimit = 2 };
        var frame = Rows(16, 20, 0);
        var step = ScrollAssembler.AddFrame(null, null, frame, 0, null, options);
        step = ScrollAssembler.AddFrame(step.Accumulated, step.PreviousFrame, Rows(16, 20, 0), step.IdenticalStreak, step.LastNewRows, options);
        Assert.False(step.Stop);
        Assert.Equal(1, step.IdenticalStreak);

        step = ScrollAssembler.AddFrame(step.Accumulated, step.PreviousFrame, Rows(16, 20, 0), step.IdenticalStreak, step.LastNewRows, options);
        Assert.True(step.Stop);
        Assert.Equal(StopReasons.Identical, step.StopReason);
    }

    [Fact]
    public void Assembler_ThreeScrolls_GrowByTheShift_AndHonorMaxHeight()
    {
        var options = AssemblerOptions.Default with { MaxHeight = 60 };
        AssemblyStep? step = null;
        BgraBuffer? acc = null;
        BgraBuffer? prev = null;
        var streak = 0;
        int? lastNew = null;
        foreach (var offset in new[] { 0, 17 })
        {
            step = ScrollAssembler.AddFrame(acc, prev, Rows(32, 50, offset), streak, lastNew, options);
            acc = step.Accumulated;
            prev = step.PreviousFrame;
            streak = step.IdenticalStreak;
            lastNew = step.LastNewRows;
        }

        Assert.NotNull(step);
        Assert.True(step!.Stop);
        Assert.Equal(StopReasons.MaxHeight, step.StopReason);
        AssertSame(Rows(32, 60, 0), step.Accumulated!);
    }

    [Fact]
    public void Assembler_ScrollSequence_MatchesTheSourceDocument()
    {
        var options = AssemblerOptions.Default;
        AssemblyStep? step = null;
        BgraBuffer? acc = null;
        BgraBuffer? prev = null;
        var streak = 0;
        int? lastNew = null;
        foreach (var offset in new[] { 0, 17, 34 })
        {
            step = ScrollAssembler.AddFrame(acc, prev, Rows(32, 50, offset), streak, lastNew, options);
            acc = step.Accumulated;
            prev = step.PreviousFrame;
            streak = step.IdenticalStreak;
            lastNew = step.LastNewRows;
            Assert.False(step.Stop);
        }

        AssertSame(Rows(32, 84, 0), step!.Accumulated!);
        Assert.Equal(17, step.LastNewRows);
    }

    [Fact]
    public void MeanChannelDiff_IgnoresARightHandScrollbarColumn()
    {
        var left = Rows(40, 24, 0);
        var right = Rows(40, 24, 0);
        for (var y = 0; y < right.Height; y++)
        {
            var i = (y * right.Width + 39) * 4;
            right.Pixels[i] = 255;
            right.Pixels[i + 1] = 0;
            right.Pixels[i + 2] = 128;
        }

        var ignored = FrameMetrics.MeanChannelDiff(left, right, columnStep: 1, rowStep: 1, rightIgnore: 18);
        var counted = FrameMetrics.MeanChannelDiff(left, right, columnStep: 1, rowStep: 1, rightIgnore: 0);
        Assert.Equal(0, ignored);
        Assert.True(counted > 0);
    }

    [Fact]
    public void SparseFullFrame_DoesNotStopJustBecauseTheMeanIsTiny()
    {
        // Chrome and white page dominate a near-fullscreen grab. One wheel notch moves only a
        // narrow text column, so the frame mean stays under the old "identical" threshold.
        var first = SparseDesktop(bodyStart: 0);
        var second = SparseDesktop(bodyStart: 16);
        var third = SparseDesktop(bodyStart: 32);
        var mean = FrameMetrics.MeanChannelDiff(first, second, columnStep: 4, rowStep: 2, rightIgnore: 18);
        Assert.True(mean < AssemblerOptions.Default.IdenticalMeanDiff, $"mean was {mean}");

        var options = AssemblerOptions.Default;
        var step = ScrollAssembler.AddFrame(null, null, first, 0, null, options, detectSticky: true);
        step = ScrollAssembler.AddFrame(step.Accumulated, step.PreviousFrame, second, step.IdenticalStreak, step.LastNewRows, options, step.StickyTop, step.StickyBottom, detectSticky: true);
        Assert.False(step.Stop, $"reason={step.StopReason} appended={step.AppendedRows} sticky={step.StickyTop}/{step.StickyBottom}");
        Assert.True(step.AppendedRows > 0);
        step = ScrollAssembler.AddFrame(step.Accumulated, step.PreviousFrame, third, step.IdenticalStreak, step.LastNewRows, options, step.StickyTop, step.StickyBottom, detectSticky: true);
        Assert.False(step.Stop);
        Assert.True(step.Accumulated!.Height > first.Height);
        Assert.InRange(CountColorRows(step.Accumulated, 1, 2, 3), 1, 130);
    }

    [Fact]
    public void MotionBand_TrimsChromeAroundTheScrollingPage()
    {
        var previous = SparseDesktop(bodyStart: 0);
        var current = SparseDesktop(bodyStart: 16);
        var span = MotionBand.Measure(previous, current);
        var crop = MotionBand.SuggestCrop(previous.Width, previous.Height, span);
        Assert.NotNull(crop);
        Assert.True(crop.Value.Y >= 60, "top chrome should be outside the crop");
        Assert.True(crop.Value.Y + crop.Value.Height <= previous.Height - 60, "bottom chrome should be outside the crop");
        Assert.True(crop.Value.Height < previous.Height - 40);
        Assert.Equal(previous.Width, crop.Value.Width);
    }

    [Fact]
    public void MotionBand_TrimsSideGuttersWhenThePageIsNarrow()
    {
        var previous = SparseDesktop(bodyStart: 0, textColumn: 80, textWidth: 64);
        var current = SparseDesktop(bodyStart: 20, textColumn: 80, textWidth: 64);
        var crop = MotionBand.SuggestCrop(previous.Width, previous.Height, MotionBand.Measure(previous, current));
        Assert.NotNull(crop);
        Assert.True(crop.Value.X > 40);
        Assert.True(crop.Value.X + crop.Value.Width < previous.Width - 40);
    }

    [Fact]
    public void MotionBand_DoesNotTrimAFrameThatScrollsEdgeToEdge()
    {
        var crop = MotionBand.SuggestCrop(80, 120, MotionBand.Measure(Rows(80, 120, 0), Rows(80, 120, 15)));
        Assert.Null(crop);
    }

    [Fact]
    public void Buffer_RejectsAMismatchedPixelArray()
    {
        Assert.Throws<ArgumentException>(() => new BgraBuffer(2, 2, new byte[8]));
    }

    [Fact]
    public void FixedBottomStrip_IsNotRepeatedDownTheStitch()
    {
        // Width 20 forces the right-ignore fallback onto the full row, so the footer
        // cannot hide in the ignored scrollbar columns.
        var options = AssemblerOptions.Default;
        AssemblyStep? step = null;
        BgraBuffer? acc = null;
        BgraBuffer? prev = null;
        var streak = 0;
        int? lastNew = null;
        var top = 0;
        var bottom = 0;
        foreach (var offset in new[] { 0, 16, 32 })
        {
            var frame = BodyThenFooter(width: 20, bodyStart: offset, bodyCount: 48, footer: 12);
            step = ScrollAssembler.AddFrame(acc, prev, frame, streak, lastNew, options, top, bottom, detectSticky: true);
            acc = step.Accumulated;
            prev = step.PreviousFrame;
            streak = step.IdenticalStreak;
            lastNew = step.LastNewRows;
            top = step.StickyTop;
            bottom = step.StickyBottom;
            Assert.False(step.Stop);
        }

        Assert.NotNull(step);
        Assert.Equal(0, step!.StickyTop);
        Assert.Equal(12, step.StickyBottom);
        Assert.Equal(92, step.Accumulated!.Height);
        AssertSame(BodyThenFooter(20, 0, 80, 12), step.Accumulated);
        Assert.Equal(12, CountFooterRows(step.Accumulated));
    }

    [Fact]
    public void FixedHeader_IsKeptOnceWhenTheAssemblerDetectsIt()
    {
        var options = AssemblerOptions.Default;
        AssemblyStep? step = null;
        BgraBuffer? acc = null;
        BgraBuffer? prev = null;
        var streak = 0;
        int? lastNew = null;
        var top = 0;
        var bottom = 0;
        foreach (var offset in new[] { 0, 15, 30 })
        {
            var frame = HeaderThenBody(width: 24, header: 8, bodyStart: offset, bodyCount: 82);
            step = ScrollAssembler.AddFrame(acc, prev, frame, streak, lastNew, options, top, bottom, detectSticky: true);
            acc = step.Accumulated;
            prev = step.PreviousFrame;
            streak = step.IdenticalStreak;
            lastNew = step.LastNewRows;
            top = step.StickyTop;
            bottom = step.StickyBottom;
            Assert.False(step.Stop);
        }

        Assert.Equal(8, step!.StickyTop);
        Assert.Equal(0, step.StickyBottom);
        AssertSame(HeaderThenBody(24, 8, 0, 112), step.Accumulated!);
    }

    [Fact]
    public void ScrollingFrame_DoesNotMarkAStickyEdge()
    {
        var detected = StickyBandDetector.Detect(Rows(20, 80, 0), Rows(20, 80, 20));
        Assert.Equal(0, detected.Top);
        Assert.Equal(0, detected.Bottom);

        var options = AssemblerOptions.Default;
        AssemblyStep? step = null;
        BgraBuffer? acc = null;
        BgraBuffer? prev = null;
        var streak = 0;
        int? lastNew = null;
        foreach (var offset in new[] { 0, 16, 32 })
        {
            step = ScrollAssembler.AddFrame(
                acc,
                prev,
                Rows(20, 60, offset),
                streak,
                lastNew,
                options,
                step?.StickyTop ?? 0,
                step?.StickyBottom ?? 0,
                detectSticky: true);
            acc = step.Accumulated;
            prev = step.PreviousFrame;
            streak = step.IdenticalStreak;
            lastNew = step.LastNewRows;
        }

        Assert.Equal(0, step!.StickyTop);
        Assert.Equal(0, step.StickyBottom);
        AssertSame(Rows(20, 92, 0), step.Accumulated!);
    }

    [Fact]
    public void BottomIgnoreFraction_DoesNotDropRowsThatMoved()
    {
        var moving = StickyBandDetector.Detect(Rows(32, 100, 0), Rows(32, 100, 24));
        Assert.Equal(0, moving.Bottom);

        var stuck = StickyBandDetector.Detect(
            BodyThenFooter(32, 0, 88, 12),
            BodyThenFooter(32, 20, 88, 12));
        Assert.Equal(12, stuck.Bottom);
        Assert.Equal(0, stuck.Top);
    }

    [Fact]
    public void ContinuousSmallSteps_StillReconstructTheDocument()
    {
        const int width = 24;
        const int view = 84;
        const int step = 6;
        const int frames = 20;
        var source = Rows(width, view + step * (frames - 1), 0);
        BgraBuffer? acc = null;
        BgraBuffer? prev = null;
        var streak = 0;
        int? lastNew = null;
        for (var i = 0; i < frames; i++)
        {
            var frame = Rows(width, view, i * step);
            var result = ScrollAssembler.AddFrame(acc, prev, frame, streak, lastNew, detectSticky: false);
            Assert.False(result.Stop);
            Assert.Equal(i == 0 ? view : step, result.AppendedRows);
            acc = result.Accumulated;
            prev = result.PreviousFrame;
            streak = result.IdenticalStreak;
            lastNew = result.LastNewRows;
        }

        AssertSame(source, acc!);
    }

    [Fact]
    public void TinyContinuousShifts_StillOverlapEnoughToStitch()
    {
        const int width = 20;
        const int view = 90;
        const int step = 3;
        const int frames = 24;
        var source = Rows(width, view + step * (frames - 1), 0);
        BgraBuffer? acc = null;
        BgraBuffer? prev = null;
        var streak = 0;
        int? lastNew = null;
        for (var i = 0; i < frames; i++)
        {
            var frame = Rows(width, view, i * step);
            var result = ScrollAssembler.AddFrame(acc, prev, frame, streak, lastNew, detectSticky: false);
            Assert.False(result.Stop);
            Assert.Equal(i == 0 ? view : step, result.AppendedRows);
            acc = result.Accumulated;
            prev = result.PreviousFrame;
            streak = result.IdenticalStreak;
            lastNew = result.LastNewRows;
        }

        AssertSame(source, acc!);
    }

    [Fact]
    public void LiveStatusBar_DoesNotEndTheCaptureAndIsKeptOnce()
    {
        // A status bar with a live page counter is not "static" row by row: its readout changes
        // every time the page number changes. Recognition must still treat the whole bar as chrome,
        // or the bar is stitched as content and the capture stalls after a few frames.
        const int width = 320;
        const int height = 420;
        const int bar = 40;
        const int step = 18;
        const int frames = 20;
        BgraBuffer? acc = null;
        BgraBuffer? prev = null;
        var streak = 0;
        int? lastNew = null;
        var top = 0;
        var bottom = 0;
        for (var i = 0; i < frames; i++)
        {
            var frame = LiveStatusFrame(width, height, bar, i * step);
            var grown = acc != null && acc.Height > height + 4;
            var options = AssemblerOptions.Default with { IdenticalPairLimit = grown ? 2 : 8 };
            var step2 = ScrollAssembler.AddFrame(acc, prev, frame, streak, lastNew, options, top, bottom, detectSticky: true);
            acc = step2.Accumulated;
            prev = step2.PreviousFrame;
            streak = step2.IdenticalStreak;
            lastNew = step2.LastNewRows;
            top = step2.StickyTop;
            bottom = step2.StickyBottom;
            Assert.False(step2.Stop, $"frame {i} stopped early: {step2.StopReason}");
            Assert.Equal(i == 0 ? height : step, step2.AppendedRows);
            if (i > 0)
            {
                Assert.True(bottom >= bar, $"frame {i} only recognised {bottom} of {bar} status rows");
            }
        }

        Assert.Equal(height + step * (frames - 1), acc!.Height);
        Assert.Equal(bar, CountRowsMatching(acc, 236, 236, 236));
    }

    [Fact]
    public void StalledFrame_IsToleratedUntilTheLimit()
    {
        // One frame that cannot be aligned must not end the capture: a sparse page or a live
        // footer can produce it. The default limit gives the next frames a chance.
        var options = AssemblerOptions.Default;
        var first = Solid(16, 40, 200, 100, 50);
        var step = ScrollAssembler.AddFrame(null, null, first, 0, null, options);
        Assert.Equal(40, step.Accumulated!.Height);

        // Nearly flat frames that differ just enough not to be "identical": every overlap scores the
        // same, so the stitcher cannot place them and appends nothing.
        step = ScrollAssembler.AddFrame(step.Accumulated, step.PreviousFrame, Solid(16, 40, 210, 100, 50), step.IdenticalStreak, step.LastNewRows, options);
        Assert.False(step.Stop, "a single stalled frame ended the capture");
        Assert.Equal(0, step.AppendedRows);
        Assert.Null(step.StopReason);

        step = ScrollAssembler.AddFrame(step.Accumulated, step.PreviousFrame, Solid(16, 40, 218, 100, 50), step.IdenticalStreak, step.LastNewRows, options);
        Assert.False(step.Stop);

        step = ScrollAssembler.AddFrame(step.Accumulated, step.PreviousFrame, Solid(16, 40, 226, 100, 50), step.IdenticalStreak, step.LastNewRows, options);
        Assert.True(step.Stop);
        Assert.Equal(StopReasons.Stalled, step.StopReason);
    }

    [Fact]
    public void PreviousScrollDistance_KeepsABlankBandFromClaimingTheWholeFrame()
    {
        // The bottom band of a mostly blank page matches itself at every offset, so a full search
        // can pick "everything already seen" and append nothing. The last known distance wins.
        var blank = BlankPage(24, 300, textTop: 60, textRows: 30);
        var next = BlankPage(24, 300, textTop: 42, textRows: 30);
        var options = StitchOptions.Default with { PreviousNewRows = 18 };
        var match = OverlapStitcher.FindOverlap(blank, next, options);
        Assert.Equal(282, match.OverlapRows);
        Assert.False(match.LowConfidence);
    }

    private static BgraBuffer BlankPage(int width, int height, int textTop, int textRows)
    {
        var pixels = new byte[width * height * 4];
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var i = (y * width + x) * 4;
                var inked = y >= textTop && y < textTop + textRows;
                pixels[i] = inked ? (byte)(30 + (y * 7) % 200) : (byte)250;
                pixels[i + 1] = inked ? (byte)(40 + (y * 13) % 190) : (byte)250;
                pixels[i + 2] = inked ? (byte)(50 + (y * 23) % 180) : (byte)250;
                pixels[i + 3] = 255;
            }
        }

        return new BgraBuffer(width, height, pixels);
    }

    private static BgraBuffer LiveStatusFrame(int width, int height, int bar, int offset)
    {
        var bodyHeight = height - bar;
        var pixels = new byte[width * height * 4];
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var i = (y * width + x) * 4;
                byte b;
                byte g;
                byte r;
                if (y >= bodyHeight)
                {
                    b = 236;
                    g = 236;
                    r = 236;
                    var row = y - bodyHeight;
                    if (x >= 20 && x < 90 && row >= 10 && row < 26)
                    {
                        // Live page counter: same columns, new glyphs.
                        var glyph = ((offset / 18) + (x / 7)) % 5;
                        var v = (byte)(60 + glyph * 35);
                        b = v;
                        g = v;
                        r = v;
                    }
                }
                else if (x >= 90 && x < 230 && (y + offset) % 46 < 11)
                {
                    var src = y + offset;
                    b = (byte)(src & 0xFF);
                    g = (byte)((src >> 8) & 0x03);
                    r = (byte)((src % 7) * 30 + 20);
                }
                else
                {
                    b = 252;
                    g = 252;
                    r = 252;
                }

                pixels[i] = b;
                pixels[i + 1] = g;
                pixels[i + 2] = r;
                pixels[i + 3] = 255;
            }
        }

        return new BgraBuffer(width, height, pixels);
    }

    private static int CountRowsMatching(BgraBuffer image, byte b, byte g, byte r)
    {
        var count = 0;
        for (var y = 0; y < image.Height; y++)
        {
            var leftOk = image.Pixels[(y * image.Width) * 4] == b
                && image.Pixels[(y * image.Width) * 4 + 1] == g
                && image.Pixels[(y * image.Width) * 4 + 2] == r;
            var rightIndex = (y * image.Width + image.Width - 1) * 4;
            var rightOk = image.Pixels[rightIndex] == b
                && image.Pixels[rightIndex + 1] == g
                && image.Pixels[rightIndex + 2] == r;
            if (leftOk && rightOk)
            {
                count++;
            }
        }

        return count;
    }

    private static void AssertSame(BgraBuffer expected, BgraBuffer actual)
    {
        Assert.Equal(expected.Width, actual.Width);
        Assert.Equal(expected.Height, actual.Height);
        Assert.Equal(expected.Pixels, actual.Pixels);
    }

    /// <summary>Each source row has a unique color, and columns vary so a flat row cannot alias.</summary>
    private static BgraBuffer Rows(int width, int height, int yOffset)
    {
        var pixels = new byte[width * height * 4];
        for (var y = 0; y < height; y++)
        {
            var src = y + yOffset;
            for (var x = 0; x < width; x++)
            {
                var i = (y * width + x) * 4;
                pixels[i] = (byte)(src * 3 + x);
                pixels[i + 1] = (byte)(20 + src * 5);
                pixels[i + 2] = (byte)(src * 13 + 7);
                pixels[i + 3] = 255;
            }
        }

        return new BgraBuffer(width, height, pixels);
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

    private static BgraBuffer Periodic(int width, int height, int period)
    {
        var pixels = new byte[width * height * 4];
        for (var y = 0; y < height; y++)
        {
            var phase = y % period;
            for (var x = 0; x < width; x++)
            {
                var i = (y * width + x) * 4;
                pixels[i] = (byte)(phase * 20);
                pixels[i + 1] = (byte)(30 + phase * 4 + x);
                pixels[i + 2] = (byte)(15 + phase * 12);
                pixels[i + 3] = 255;
            }
        }

        return new BgraBuffer(width, height, pixels);
    }

    private static BgraBuffer HeaderThenBody(int width, int header, int bodyStart, int bodyCount)
    {
        var height = header + bodyCount;
        var pixels = new byte[width * height * 4];
        for (var y = 0; y < header; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var i = (y * width + x) * 4;
                pixels[i] = 20;
                pixels[i + 1] = 20;
                pixels[i + 2] = 20;
                pixels[i + 3] = 255;
            }
        }

        for (var b = 0; b < bodyCount; b++)
        {
            var src = bodyStart + b;
            var y = header + b;
            for (var x = 0; x < width; x++)
            {
                var i = (y * width + x) * 4;
                pixels[i] = (byte)(src * 3 + x);
                pixels[i + 1] = (byte)(80 + src * 5);
                pixels[i + 2] = (byte)(src * 11 + 4);
                pixels[i + 3] = 255;
            }
        }

        return new BgraBuffer(width, height, pixels);
    }

    private static BgraBuffer BodyThenFooter(int width, int bodyStart, int bodyCount, int footer)
    {
        var height = bodyCount + footer;
        var pixels = new byte[width * height * 4];
        for (var b = 0; b < bodyCount; b++)
        {
            var src = bodyStart + b;
            var y = b;
            for (var x = 0; x < width; x++)
            {
                var i = (y * width + x) * 4;
                pixels[i] = (byte)(30 + src * 3 + x);
                pixels[i + 1] = (byte)(40 + src * 5);
                pixels[i + 2] = (byte)(src * 9 + 11);
                pixels[i + 3] = 255;
            }
        }

        for (var y = bodyCount; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var i = (y * width + x) * 4;
                pixels[i] = 1;
                pixels[i + 1] = 2;
                pixels[i + 2] = 3;
                pixels[i + 3] = 255;
            }
        }

        return new BgraBuffer(width, height, pixels);
    }

    /// <summary>
    /// 360×400 grab: modest ribbon and taskbar, white page, narrow ink column.
    /// The chrome is under the 30% sticky cap so it can be removed in one piece.
    /// </summary>
    private static BgraBuffer SparseDesktop(int bodyStart, int textColumn = 40, int textWidth = 20)
    {
        const int width = 360;
        const int height = 400;
        const int top = 100;
        const int bottom = 100;
        var pixels = new byte[width * height * 4];
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var i = (y * width + x) * 4;
                if (y < top)
                {
                    pixels[i] = 20;
                    pixels[i + 1] = 30;
                    pixels[i + 2] = 40;
                }
                else if (y >= height - bottom)
                {
                    pixels[i] = 1;
                    pixels[i + 1] = 2;
                    pixels[i + 2] = 3;
                }
                else if (x >= textColumn && x < textColumn + textWidth)
                {
                    var src = bodyStart + (y - top);
                    pixels[i] = (byte)(40 + src * 3);
                    pixels[i + 1] = (byte)(40 + src * 2);
                    pixels[i + 2] = (byte)(40 + src * 3);
                }
                else
                {
                    pixels[i] = 255;
                    pixels[i + 1] = 255;
                    pixels[i + 2] = 255;
                }

                pixels[i + 3] = 255;
            }
        }

        return new BgraBuffer(width, height, pixels);
    }

    private static int CountColorRows(BgraBuffer image, byte b, byte g, byte r)
    {
        var count = 0;
        for (var y = 0; y < image.Height; y++)
        {
            var match = true;
            for (var x = 0; x < image.Width; x++)
            {
                var i = (y * image.Width + x) * 4;
                if (image.Pixels[i] != b || image.Pixels[i + 1] != g || image.Pixels[i + 2] != r)
                {
                    match = false;
                    break;
                }
            }

            if (match)
            {
                count++;
            }
        }

        return count;
    }

    private static int CountFooterRows(BgraBuffer image)
    {
        var count = 0;
        for (var y = 0; y < image.Height; y++)
        {
            var footer = true;
            for (var x = 0; x < image.Width; x++)
            {
                var i = (y * image.Width + x) * 4;
                if (image.Pixels[i] != 1 || image.Pixels[i + 1] != 2 || image.Pixels[i + 2] != 3)
                {
                    footer = false;
                    break;
                }
            }

            if (footer)
            {
                count++;
            }
        }

        return count;
    }
}
