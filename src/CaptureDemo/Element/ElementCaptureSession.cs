using CaptureDemo.Capture;
using CaptureDemo.Core;
using CaptureDemo.Native;

namespace CaptureDemo.Element;

internal static class ElementCaptureSession
{
    public static async Task<CaptureOutcome> RunAsync(AppSettings? settings = null)
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

            var visible = Rectangle.Intersect(pick.Bounds, NativeMethods.VirtualScreen());
            using var bitmap = ScreenGrabber.Grab(visible);
            settings ??= AppSettings.CreateDefault();
            var saved = CaptureSaver.SavePng(bitmap, "element", settings);
            var clip = CaptureSaver.ClipboardPhrase(settings, saved.CopiedToClipboard);
            return CaptureOutcome.Saved(saved.Path, $"已保存元素截图（{pick.Label}）。{clip}");
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
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
