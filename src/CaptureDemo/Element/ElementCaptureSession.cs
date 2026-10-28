using CaptureDemo.Core;

namespace CaptureDemo.Element;

internal static class ElementCaptureSession
{
    /// <summary>
    /// Without a preset this runs its own hover overlay. With a preset — the region the
    /// unified capture capsule already framed — it saves that rect and skips the picker,
    /// so the user never picks the same element twice.
    /// </summary>
    public static Task<CaptureOutcome> RunAsync(AppSettings? settings = null, Rectangle? preset = null, string? label = null)
    {
        if (preset is Rectangle framed)
        {
            return SaveAfterSettleAsync(framed, label, settings);
        }

        return PickThenSaveAsync(settings);
    }

    /// <summary>
    /// The snip overlay (dimming layer + capsule) was torn down a moment ago. Give the
    /// compositor a beat so the grab cannot catch the fade, the same reason the overlay
    /// path below waits before it grabs.
    /// </summary>
    private static async Task<CaptureOutcome> SaveAfterSettleAsync(Rectangle bounds, string? label, AppSettings? settings)
    {
        await Task.Delay(90);
        return Save(bounds, label, settings);
    }

    private static CaptureOutcome Save(Rectangle bounds, string? label, AppSettings? settings)
    {
        try
        {
            var visible = Rectangle.Intersect(bounds, NativeMethods.VirtualScreen());
            if (visible.Width < 2 || visible.Height < 2)
            {
                return CaptureOutcome.Failed("元素区域超出屏幕。");
            }

            using var bitmap = ScreenGrabber.Grab(visible);
            settings ??= AppSettings.CreateDefault();
            var saved = CaptureSaver.SavePng(bitmap, "element", settings);
            var clip = CaptureSaver.ClipboardPhrase(settings, saved.CopiedToClipboard);
            var name = string.IsNullOrWhiteSpace(label) ? "元素" : label;
            return CaptureOutcome.Saved(saved.Path, $"已保存元素截图（{name}）。{clip}");
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            return CaptureOutcome.Failed("元素截图失败：" + ex.Message);
        }
    }

    private static async Task<CaptureOutcome> PickThenSaveAsync(AppSettings? settings)
    {
        ElementPicker? picker = null;
        ElementOverlayForm? overlay = null;
        LowLevelInputHook? hook = null;
        try
        {
            try
            {
                picker = new ElementPicker();
            }
            catch (Exception ex)
            {
                return CaptureOutcome.Failed("无法初始化 UI Automation：" + ex.Message);
            }

            overlay = new ElementOverlayForm(picker);
            hook = new LowLevelInputHook
            {
                SuppressLeftButton = true,
                SuppressWheel = true,
                SuppressEscape = true,
            };
            hook.MouseMove += overlay.QueueMouseMove;
            hook.LeftButtonDown += overlay.OnLeftDown;
            hook.LeftButtonUp += overlay.OnLeftUp;
            hook.Wheel += overlay.OnWheel;
            hook.Escape += overlay.Cancel;
            hook.InstallMouseAndKeyboard();

            var pick = await overlay.PickAsync();
            hook.Dispose();
            hook = null;
            overlay.Hide();
            await Task.Delay(120);
            if (pick == null)
            {
                return CaptureOutcome.Cancelled();
            }

            return Save(pick.Bounds, pick.Label, settings);
        }        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            return CaptureOutcome.Failed("元素截图失败：" + ex.Message);
        }
        finally
        {
            hook?.Dispose();
            if (overlay is { IsDisposed: false })
            {
                overlay.Dispose();
            }

            picker?.Dispose();
        }
    }
}
