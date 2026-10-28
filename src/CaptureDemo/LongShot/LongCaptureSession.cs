using CaptureDemo.Capture;
using CaptureDemo.Core;
using CaptureDemo.Native;

namespace CaptureDemo.LongShot;

internal sealed record LongShotRequest(int ScrollSpeed, int SampleMilliseconds);

internal sealed class LoopResult
{
    public BgraBuffer? Image { get; init; }

    public string? StopReason { get; init; }

    public bool Cancelled { get; init; }

    public bool LowConfidence { get; init; }

    public int Frames { get; init; }

    public int StickyTop { get; init; }

    public int StickyBottom { get; init; }

    public bool Manual { get; init; }

    public bool Trimmed { get; init; }

    /// <summary>Height of the rectangle actually grabbed at the end, after any auto-trim.</summary>
    public int ViewportHeight { get; init; }

    public Exception? Error { get; init; }
}

internal sealed class ScrollPace
{
    public int Manual;

    public int WheelGeneration;
}

internal static class LongCaptureSession
{
    public const int MaxFrames = 150;

    /// <summary>Identical frames tolerated before the page has actually started moving.</summary>
    private const int GraceIdenticalPairs = 8;

    public static async Task<CaptureOutcome> RunAsync(AppSettings settings, Rectangle? preset = null)
    {
        settings.Normalize();
        var request = new LongShotRequest(settings.ScrollSpeed, settings.SampleMilliseconds);
        RegionOverlayForm? overlay = null;
        LowLevelInputHook? selectHook = null;
        try
        {
            overlay = new RegionOverlayForm();
            Rectangle region;
            if (preset is Rectangle given)
            {
                region = Rectangle.Intersect(given, NativeMethods.VirtualScreen());
                overlay.BeginCapture(region);
            }
            else
            {
                selectHook = new LowLevelInputHook
                {
                    SuppressLeftButton = true,
                    SuppressWheel = true,
                    SuppressEscape = true,
                };
                selectHook.MouseMove += overlay.QueueMouseMove;
                selectHook.LeftButtonDown += overlay.OnLeftDown;
                selectHook.LeftButtonUp += overlay.OnLeftUp;
                selectHook.Escape += overlay.Cancel;
                selectHook.InstallMouseAndKeyboard();

                var selected = await overlay.SelectAsync();
                selectHook.Dispose();
                selectHook = null;
                if (selected == null)
                {
                    overlay.Hide();
                    return CaptureOutcome.Cancelled();
                }

                region = Rectangle.Intersect(selected.Value, NativeMethods.VirtualScreen());
                overlay.BeginCapture(region);
            }

            return await CaptureRegionAsync(region, request, overlay, settings);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            return CaptureOutcome.Failed("长截图失败：" + ex.Message);
        }
        finally
        {
            selectHook?.Dispose();
            if (overlay is { IsDisposed: false })
            {
                overlay.Dispose();
            }
        }
    }

    private static async Task<CaptureOutcome> CaptureRegionAsync(Rectangle region, LongShotRequest request, RegionOverlayForm overlay, AppSettings settings)
    {
        if (region.Width < 24 || region.Height < 24)
        {
            return CaptureOutcome.Failed("选区不在屏幕内，或小于 24×24。");
        }

        var speed = Math.Clamp(request.ScrollSpeed, 1, 10);
        var sampleMs = Math.Clamp(request.SampleMilliseconds, 40, 800);
        using var cts = new CancellationTokenSource();
        var pace = new ScrollPace();
        using var hook = new LowLevelInputHook { SuppressEscape = true };
        hook.Escape += () => cts.Cancel();
        hook.Wheel += _ =>
        {
            // Physical wheel only. Injected auto-scroll is ignored by the hook.
            // The first user wheel latches manual mode for the rest of the capture.
            Interlocked.Exchange(ref pace.Manual, 1);
            Interlocked.Increment(ref pace.WheelGeneration);
        };
        hook.InstallMouseAndKeyboard();

        using var cursor = CursorRestorer.Capture();
        var loop = await Task.Run(() => RunLoop(region, speed, sampleMs, overlay, pace, cts.Token));
        if (!overlay.IsDisposed)
        {
            overlay.Hide();
        }

        if (loop.Image == null)
        {
            return loop.Error == null
                ? CaptureOutcome.Cancelled()
                : CaptureOutcome.Failed("长截图失败：" + loop.Error.Message);
        }

        using var bitmap = BgraConvert.ToBitmap(loop.Image);
        var saved = CaptureSaver.SavePng(bitmap, "long", settings);
        var message = BuildMessage(loop, region, CaptureSaver.ClipboardPhrase(settings, saved.CopiedToClipboard));
        return CaptureOutcome.Saved(saved.Path, message);
    }

    private static LoopResult RunLoop(
        Rectangle selection,
        int speed,
        int sampleMs,
        RegionOverlayForm overlay,
        ScrollPace pace,
        CancellationToken token)
    {
        BgraBuffer? accumulated = null;
        BgraBuffer? previous = null;
        var streak = 0;
        int? lastNew = null;
        var lowConfidence = false;
        string? stopReason = null;
        var frames = 0;
        var stickyTop = 0;
        var stickyBottom = 0;
        var baseline = 0;
        var trimmed = false;
        var aimAttempt = 0;
        Rectangle? motionScreen = null;
        var grab = selection;
        WheelScroll.Streamer? streamer = null;
        try
        {
            while (!token.IsCancellationRequested && frames < MaxFrames)
            {
                var grabStarted = Environment.TickCount64;
                frames++;
                var generationBeforeGrab = Volatile.Read(ref pace.WheelGeneration);
                using var bitmap = ScreenGrabber.Grab(grab);
                var frame = BgraConvert.FromBitmap(bitmap);
                var grown = accumulated != null && baseline > 0 && accumulated.Height > baseline + 4;
                var options = AssemblerOptions.Default with
                {
                    // Two identical pairs, never one: a fixed footer that updates its page counter
                    // must not end a long document on a single unlucky sample.
                    IdenticalPairLimit = grown ? 2 : GraceIdenticalPairs,
                };

                AssemblyStep step;
                if (!trimmed && previous != null && TryFocusMotion(previous, frame, ref grab, ref motionScreen, out var rebased))
                {
                    trimmed = true;
                    step = rebased;
                    baseline = step.Accumulated?.Height ?? baseline;
                }
                else
                {
                    step = ScrollAssembler.AddFrame(
                        accumulated,
                        previous,
                        frame,
                        streak,
                        lastNew,
                        options,
                        stickyTop,
                        stickyBottom,
                        detectSticky: true);
                    if (baseline == 0 && step.Accumulated != null)
                    {
                        baseline = step.Accumulated.Height;
                    }
                }

                accumulated = step.Accumulated;
                previous = step.PreviousFrame;
                streak = step.IdenticalStreak;
                lastNew = step.LastNewRows;
                stickyTop = step.StickyTop;
                stickyBottom = step.StickyBottom;
                if (step.LowConfidence)
                {
                    lowConfidence = true;
                }

                stopReason = step.StopReason;
                var manual = Volatile.Read(ref pace.Manual) != 0;
                Report(overlay, selection, Describe(accumulated?.Height ?? 0, frames, manual, stopReason));
                if (step.Stop || token.IsCancellationRequested)
                {
                    break;
                }

                if (step.AppendedRows <= 0 && motionScreen == null)
                {
                    aimAttempt++;
                }

                var scrolledDuringGrab = Volatile.Read(ref pace.WheelGeneration) != generationBeforeGrab;
                if (Volatile.Read(ref pace.Manual) != 0)
                {
                    StopAuto(ref streamer);
                    if (!WaitForNextFrame(pace, sampleMs, token, requireNewWheel: !scrolledDuringGrab))
                    {
                        break;
                    }

                    continue;
                }

                var work = NativeMethods.WorkArea(new Point(grab.X + grab.Width / 2, grab.Y + grab.Height / 2));
                var aim = ScrollAim.Next(grab, work, aimAttempt, motionScreen ?? (trimmed ? grab : null));
                EnsureAuto(ref streamer, aim, WheelPace.UnitsPerSecond(speed), token, pace);
                var spent = (int)Math.Min(int.MaxValue, Environment.TickCount64 - grabStarted);
                var remain = sampleMs - spent;
                if (remain > 0 && !WaitRemaining(pace, remain, token))
                {
                    break;
                }

                if (Volatile.Read(ref pace.Manual) != 0)
                {
                    StopAuto(ref streamer);
                    if (!WaitForNextFrame(pace, sampleMs, token, requireNewWheel: false))
                    {
                        break;
                    }
                }
            }

            var cancelled = token.IsCancellationRequested;
            if (frames >= MaxFrames && stopReason == null && !cancelled)
            {
                stopReason = "max-frames";
            }

            return new LoopResult
            {
                Image = accumulated,
                StopReason = stopReason,
                Cancelled = cancelled,
                LowConfidence = lowConfidence,
                Frames = frames,
                StickyTop = stickyTop,
                StickyBottom = stickyBottom,
                Manual = Volatile.Read(ref pace.Manual) != 0,
                Trimmed = trimmed,
                ViewportHeight = grab.Height,
            };
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            return new LoopResult
            {
                Image = accumulated,
                StopReason = stopReason,
                Cancelled = token.IsCancellationRequested,
                LowConfidence = lowConfidence,
                Frames = frames,
                StickyTop = stickyTop,
                StickyBottom = stickyBottom,
                Manual = Volatile.Read(ref pace.Manual) != 0,
                Trimmed = trimmed,
                ViewportHeight = grab.Height,
                Error = ex,
            };
        }
        finally
        {
            StopAuto(ref streamer);
        }
    }

    private static void EnsureAuto(
        ref WheelScroll.Streamer? streamer,
        Point aim,
        int unitsPerSecond,
        CancellationToken token,
        ScrollPace pace)
    {
        if (streamer == null)
        {
            streamer = WheelScroll.Streamer.Start(
                aim,
                unitsPerSecond,
                token,
                () => Volatile.Read(ref pace.Manual) != 0);
            return;
        }

        streamer.SetAim(aim);
    }

    private static void StopAuto(ref WheelScroll.Streamer? streamer)
    {
        streamer?.Dispose();
        streamer = null;
    }

    /// <summary>
    /// When a large selection contains a smaller scrolling page, crop the grabbed frames
    /// and restart the stitch so ribbon, taskbar, and gutters are not the overlap template.
    /// The on-screen hole stays the user's drag rectangle.
    /// </summary>
    private static bool TryFocusMotion(
        BgraBuffer previous,
        BgraBuffer frame,
        ref Rectangle grab,
        ref Rectangle? motionScreen,
        out AssemblyStep step)
    {
        step = null!;
        var span = MotionBand.Measure(previous, frame);
        if (span.RowSpan >= 24 && span.ColumnSpan >= 8)
        {
            var left = grab.X + span.Left;
            var top = grab.Y + span.Top;
            motionScreen = new Rectangle(left, top, Math.Max(1, span.ColumnSpan), span.RowSpan);
        }

        // Rows that never move are chrome, not content. Knowing where they are keeps the crop from
        // shaving the status bar down to a few padding rows: it is kept whole and stitched once.
        var bands = StickyBandDetector.Detect(previous, frame);

        var crop = MotionBand.SuggestCrop(frame.Width, frame.Height, span, bands.Top, bands.Bottom);
        if (crop is not FrameCrop boxed)
        {
            return false;
        }

        var screen = Rectangle.Intersect(
            new Rectangle(grab.X + boxed.X, grab.Y + boxed.Y, boxed.Width, boxed.Height),
            grab);
        if (screen.Width < 48 || screen.Height < 48)
        {
            return false;
        }

        var prevC = previous.Crop(boxed.X, boxed.Y, boxed.Width, boxed.Height);
        var frameC = frame.Crop(boxed.X, boxed.Y, boxed.Width, boxed.Height);
        var first = ScrollAssembler.AddFrame(null, null, prevC, 0, null, AssemblerOptions.Default);
        step = ScrollAssembler.AddFrame(
            first.Accumulated,
            first.PreviousFrame,
            frameC,
            first.IdenticalStreak,
            first.LastNewRows,
            AssemblerOptions.Default,
            bands.Top,
            bands.Bottom,
            detectSticky: true);
        grab = screen;
        motionScreen = screen;
        return true;
    }

    /// <summary>
    /// Auto mode waits out the rest of the sample interval while the wheel stream keeps running.
    /// Returns false when Esc cancels. A physical wheel returns true so the caller can coalesce.
    /// </summary>
    private static bool WaitRemaining(ScrollPace pace, int milliseconds, CancellationToken token)
    {
        var startGen = Volatile.Read(ref pace.WheelGeneration);
        var waited = 0;
        while (waited < milliseconds)
        {
            var slice = Math.Min(8, milliseconds - waited);
            if (token.WaitHandle.WaitOne(Math.Max(1, slice)))
            {
                return false;
            }

            waited += slice;
            if (Volatile.Read(ref pace.WheelGeneration) != startGen)
            {
                return true;
            }
        }

        return !token.IsCancellationRequested;
    }

    /// <summary>
    /// Manual mode waits for a wheel, then for <paramref name="settle"/> milliseconds of quiet so a fling
    /// becomes one frame. Returns false when Esc cancels.
    /// </summary>
    private static bool WaitForNextFrame(ScrollPace pace, int settle, CancellationToken token, bool requireNewWheel)
    {
        var startGen = Volatile.Read(ref pace.WheelGeneration);
        if (requireNewWheel)
        {
            while (Volatile.Read(ref pace.WheelGeneration) == startGen)
            {
                if (token.WaitHandle.WaitOne(25))
                {
                    return false;
                }
            }
        }
        else
        {
            var waited = 0;
            while (waited < settle)
            {
                var slice = Math.Min(25, settle - waited);
                if (token.WaitHandle.WaitOne(slice))
                {
                    return false;
                }

                waited += slice;
                if (Volatile.Read(ref pace.WheelGeneration) != startGen)
                {
                    break;
                }
            }

            if (Volatile.Read(ref pace.WheelGeneration) == startGen)
            {
                return !token.IsCancellationRequested;
            }
        }

        while (!token.IsCancellationRequested)
        {
            var seen = Volatile.Read(ref pace.WheelGeneration);
            if (token.WaitHandle.WaitOne(Math.Max(40, settle)))
            {
                return false;
            }

            if (Volatile.Read(ref pace.WheelGeneration) == seen)
            {
                return true;
            }
        }

        return false;
    }

    private static string Describe(int height, int frames, bool manual, string? stopReason)
    {
        var state = stopReason switch
        {
            StopReasons.Identical => "画面不再变化",
            StopReasons.Stalled => "连续几帧都没拼上新内容",
            StopReasons.MaxHeight => "已达高度上限",
            "max-frames" => "已达帧数上限",
            _ => manual ? "手动" : "连续慢滚",
        };
        var hint = manual ? "滚轮调速" : "滚轮改为手动";
        return $"{state} · 高度 {height} px · {frames} 帧 · {hint} · Esc 停止";
    }

    private static void Report(RegionOverlayForm overlay, Rectangle hole, string text)
    {
        try
        {
            if (overlay.IsDisposed)
            {
                return;
            }

            overlay.SetCapture(hole, text);
        }
        catch (ObjectDisposedException)
        {
        }
        catch (InvalidOperationException)
        {
        }
    }

    private static string BuildMessage(LoopResult loop, Rectangle region, string clip)
    {
        var height = loop.Image?.Height ?? 0;
        var lead = loop.Cancelled
            ? "已停止。"
            : loop.StopReason switch
            {
                StopReasons.Identical => "滚动已结束。",
                StopReasons.Stalled => "画面不再往下增长，已保存到这一页。",
                StopReasons.MaxHeight => "已达到 20000 像素高度上限。",
                "max-frames" => $"已达到 {MaxFrames} 帧上限。",
                _ => "长截图完成。",
            };

        var body = $"高度 {height} px，{loop.Frames} 帧。";
        if (loop.Error != null)
        {
            body = "中途出错：" + loop.Error.Message + " 已保存已拼接的部分。" + body;
        }

        var viewport = loop.ViewportHeight > 0 ? loop.ViewportHeight : region.Height;
        var hint = height <= viewport
            ? "画面几乎没有往下增长。滚轮会避开任务栏，改点到选区里真正在动的位置；若页面本身不吃滚轮，高度就不会增加。"
            : "";
        var trim = loop.Trimmed ? "屏幕上的选区框没有改。拼接只用了里面真正在滚动的那一块。" : "";
        var sticky = loop.StickyTop > 0 || loop.StickyBottom > 0
            ? $"已把不动的边缘只保留一份（上 {loop.StickyTop} px，下 {loop.StickyBottom} px）。"
            : "";
        var mode = loop.Manual ? "本次在滚轮后保持手动，没有再自动滚。" : "";
        var confidence = loop.LowConfidence ? "有的帧重叠匹配不够稳定，接缝可能不齐。" : "";
        return lead + body + hint + trim + sticky + mode + confidence + clip;
    }
}
