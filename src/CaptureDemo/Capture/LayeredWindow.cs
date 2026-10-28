using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace CaptureDemo.Capture;

/// <summary>
/// Pushes a premultiplied 32-bit bitmap to a <c>WS_EX_LAYERED</c> window.
/// <c>Bitmap.GetHbitmap</c> drops the alpha channel, so the pixels go through a DIB section.
/// </summary>
internal static class LayeredWindow
{
    public static void Update(IntPtr hwnd, Point origin, Bitmap bitmap)
    {
        if (hwnd == IntPtr.Zero)
        {
            throw new InvalidOperationException("覆盖层窗口还没有句柄。");
        }

        var width = bitmap.Width;
        var height = bitmap.Height;
        var screenDc = NativeMethods.GetDC(IntPtr.Zero);
        var memDc = IntPtr.Zero;
        var dib = IntPtr.Zero;
        var previous = IntPtr.Zero;
        try
        {
            if (screenDc == IntPtr.Zero)
            {
                throw new InvalidOperationException("无法取得屏幕 DC。");
            }

            memDc = NativeMethods.CreateCompatibleDC(screenDc);
            if (memDc == IntPtr.Zero)
            {
                throw new InvalidOperationException("无法创建兼容 DC。");
            }

            var info = new NativeMethods.BITMAPINFO
            {
                bmiHeader = new NativeMethods.BITMAPINFOHEADER
                {
                    biSize = (uint)Marshal.SizeOf<NativeMethods.BITMAPINFOHEADER>(),
                    biWidth = width,
                    biHeight = -height,
                    biPlanes = 1,
                    biBitCount = 32,
                    biCompression = 0,
                },
            };
            dib = NativeMethods.CreateDIBSection(screenDc, ref info, 0, out var bits, IntPtr.Zero, 0);
            if (dib == IntPtr.Zero || bits == IntPtr.Zero)
            {
                throw new InvalidOperationException($"创建蒙版位图失败，错误码 {Marshal.GetLastWin32Error()}。");
            }

            CopyPremultiplied(bitmap, bits, width, height);
            previous = NativeMethods.SelectObject(memDc, dib);
            var destination = new NativeMethods.POINT { X = origin.X, Y = origin.Y };
            var source = new NativeMethods.POINT { X = 0, Y = 0 };
            var size = new NativeMethods.SIZE { CX = width, CY = height };
            var blend = new NativeMethods.BLENDFUNCTION
            {
                BlendOp = 0,
                BlendFlags = 0,
                SourceConstantAlpha = 255,
                AlphaFormat = 1,
            };
            if (!NativeMethods.UpdateLayeredWindow(hwnd, screenDc, ref destination, ref size, memDc, ref source, 0, ref blend, 2))
            {
                throw new InvalidOperationException($"更新蒙版窗口失败，错误码 {Marshal.GetLastWin32Error()}。");
            }
        }
        finally
        {
            if (previous != IntPtr.Zero && memDc != IntPtr.Zero)
            {
                NativeMethods.SelectObject(memDc, previous);
            }

            if (dib != IntPtr.Zero)
            {
                NativeMethods.DeleteObject(dib);
            }

            if (memDc != IntPtr.Zero)
            {
                NativeMethods.DeleteDC(memDc);
            }

            if (screenDc != IntPtr.Zero)
            {
                NativeMethods.ReleaseDC(IntPtr.Zero, screenDc);
            }
        }
    }

    /// <summary>
    /// Copies the bitmap into a DIB section's pixel memory. One memcpy when the stride is
    /// tight (32bpp normally is), otherwise one memcpy per row — never a per-row allocation.
    /// </summary>
    private static void CopyPremultiplied(Bitmap bitmap, IntPtr bits, int width, int height)
    {
        var rect = new Rectangle(0, 0, width, height);
        var data = bitmap.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format32bppPArgb);
        try
        {
            var rowBytes = width * 4;
            var stride = data.Stride;
            var flipped = stride < 0;
            var absStride = Math.Abs(stride);
            if (!flipped && absStride == rowBytes)
            {
                NativeMethods.MoveMemory(bits, data.Scan0, (UIntPtr)(uint)(rowBytes * height));
                return;
            }

            for (var y = 0; y < height; y++)
            {
                var src = flipped
                    ? data.Scan0 + (height - 1 - y) * absStride
                    : data.Scan0 + y * absStride;
                NativeMethods.MoveMemory(bits + y * rowBytes, src, (UIntPtr)(uint)rowBytes);
            }
        }
        finally
        {
            bitmap.UnlockBits(data);
        }
    }
}
