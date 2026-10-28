using System.Runtime.InteropServices;
using System.Text;

namespace CaptureDemo.Snip;

internal static class WindowHit
{
    public static bool TryHit(Point screen, out Rectangle bounds, out string title)
    {
        bounds = Rectangle.Empty;
        title = "";
        try
        {
            var raw = NativeMethods.WindowFromPoint(new NativeMethods.POINT { X = screen.X, Y = screen.Y });
            if (raw == IntPtr.Zero)
            {
                return false;
            }

            var root = NativeMethods.GetAncestor(raw, NativeMethods.GaRoot);
            var hwnd = root == IntPtr.Zero ? raw : root;
            NativeMethods.GetWindowThreadProcessId(hwnd, out var pid);
            if (pid == (uint)Environment.ProcessId || !NativeMethods.IsWindowVisible(hwnd))
            {
                return false;
            }

            NativeMethods.RECT rect;
            var hr = NativeMethods.DwmGetWindowAttribute(
                hwnd,
                NativeMethods.DwmwaExtendedFrameBounds,
                out rect,
                Marshal.SizeOf<NativeMethods.RECT>());
            if (hr != 0 && !NativeMethods.GetWindowRect(hwnd, out rect))
            {
                return false;
            }

            bounds = Rectangle.Intersect(
                Rectangle.FromLTRB(rect.Left, rect.Top, rect.Right, rect.Bottom),
                NativeMethods.VirtualScreen());
            if (bounds.Width < 16 || bounds.Height < 16)
            {
                return false;
            }

            var buffer = new StringBuilder(512);
            NativeMethods.GetWindowText(hwnd, buffer, buffer.Capacity);
            title = buffer.ToString().Trim();
            if (title.Length == 0)
            {
                title = "窗口";
            }

            return true;
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            return false;
        }
    }
}
