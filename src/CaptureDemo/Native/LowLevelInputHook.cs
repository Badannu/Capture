using System.Runtime.InteropServices;

namespace CaptureDemo.Native;

/// <summary>
/// Low-level mouse and keyboard hook so a click-through overlay can still see the pointer.
/// Injected events (our own wheel) are never swallowed.
/// </summary>
internal sealed class LowLevelInputHook : IDisposable
{
    private readonly HookProc _mouseProc;
    private readonly HookProc _keyProc;
    private IntPtr _mouse;
    private IntPtr _key;
    private bool _disposed;

    public LowLevelInputHook()
    {
        _mouseProc = MouseHook;
        _keyProc = KeyHook;
    }

    public bool SuppressLeftButton { get; set; }

    /// <summary>When this returns true, a left click is delivered to the window under the cursor (our toolbars).</summary>
    public Func<Point, bool>? PassThroughLeftButton { get; set; }

    public bool SuppressWheel { get; set; }

    public bool SuppressEscape { get; set; } = true;

    public event Action<Point>? MouseMove;

    public event Action<Point>? LeftButtonDown;

    public event Action<Point>? LeftButtonUp;

    public event Action<int>? Wheel;

    public event Action? Escape;

    public void InstallMouseAndKeyboard()
    {
        Install(NativeMethods.WhMouseLl, _mouseProc, ref _mouse, "鼠标");
        Install(NativeMethods.WhKeyboardLl, _keyProc, ref _key, "键盘");
    }

    public void InstallKeyboard()
    {
        Install(NativeMethods.WhKeyboardLl, _keyProc, ref _key, "键盘");
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        if (_mouse != IntPtr.Zero)
        {
            NativeMethods.UnhookWindowsHookEx(_mouse);
            _mouse = IntPtr.Zero;
        }

        if (_key != IntPtr.Zero)
        {
            NativeMethods.UnhookWindowsHookEx(_key);
            _key = IntPtr.Zero;
        }
    }

    private static void Install(int id, HookProc proc, ref IntPtr slot, string name)
    {
        if (slot != IntPtr.Zero)
        {
            return;
        }

        slot = NativeMethods.SetWindowsHookEx(id, proc, NativeMethods.GetModuleHandle(null), 0);
        if (slot == IntPtr.Zero)
        {
            throw new InvalidOperationException($"安装{name}钩子失败，错误码 {Marshal.GetLastWin32Error()}。");
        }
    }

    private IntPtr MouseHook(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0)
        {
            try
            {
                var info = Marshal.PtrToStructure<NativeMethods.MSLLHOOKSTRUCT>(lParam);
                if ((info.flags & NativeMethods.LlmhfInjected) == 0)
                {
                    var message = (int)wParam;
                    var point = new Point(info.pt.X, info.pt.Y);
                    switch (message)
                    {
                        case NativeMethods.WmMouseMove:
                            MouseMove?.Invoke(point);
                            break;
                        case NativeMethods.WmLButtonDown:
                            LeftButtonDown?.Invoke(point);
                            if (SuppressLeftButton && PassThroughLeftButton?.Invoke(point) != true)
                            {
                                return (IntPtr)1;
                            }

                            break;
                        case NativeMethods.WmLButtonUp:
                            LeftButtonUp?.Invoke(point);
                            if (SuppressLeftButton && PassThroughLeftButton?.Invoke(point) != true)
                            {
                                return (IntPtr)1;
                            }

                            break;
                        case NativeMethods.WmMouseWheel:
                            var delta = (short)((info.mouseData >> 16) & 0xffff);
                            Wheel?.Invoke(delta);
                            if (SuppressWheel)
                            {
                                return (IntPtr)1;
                            }

                            break;
                    }
                }
            }
            catch
            {
                // A throw inside a low-level hook makes Windows detach it.
            }
        }

        return NativeMethods.CallNextHookEx(IntPtr.Zero, nCode, wParam, lParam);
    }

    private IntPtr KeyHook(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0 && SuppressEscape)
        {
            try
            {
                var info = Marshal.PtrToStructure<NativeMethods.KBDLLHOOKSTRUCT>(lParam);
                var message = (int)wParam;
                if ((message == NativeMethods.WmKeyDown || message == NativeMethods.WmSysKeyDown) &&
                    info.vkCode == NativeMethods.VkEscape)
                {
                    Escape?.Invoke();
                    return (IntPtr)1;
                }
            }
            catch
            {
                // Keep the hook installed.
            }
        }

        return NativeMethods.CallNextHookEx(IntPtr.Zero, nCode, wParam, lParam);
    }
}
