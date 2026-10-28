using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using CaptureDemo.Native;

namespace CaptureDemo.Capture;

/// <summary>
/// A reusable <c>WS_EX_LAYERED</c> back buffer. <see cref="LayeredWindow.Update"/> builds and
/// tears down a DC plus a DIB section on every call, which is fine for a 500px toolbar but far
/// too expensive for a full-selection annotation layer at pointer-move rate.
/// This keeps the DC, the DIB section and its pixel pointer alive across frames, and copies
/// only the dirty rows before presenting.
/// </summary>
internal sealed class LayeredSurface : IDisposable
{
    /// <summary>POINT pptDst, SIZE psize, POINT pptSrc, BLENDFUNCTION pblend, RECT prcDirty.</summary>
    private const int ScratchBytes = 64;

    private const int OffsetDst = 0;
    private const int OffsetSize = 8;
    private const int OffsetSrc = 16;
    private const int OffsetBlend = 24;
    private const int OffsetDirty = 32;

    private IntPtr _screenDc;
    private IntPtr _memDc;
    private IntPtr _dib;
    private IntPtr _previous;
    private IntPtr _bits;
    private IntPtr _scratch;
    private int _width;
    private int _height;
    private bool _disposed;

    /// <summary>Size of the buffer currently allocated, so callers can avoid rebuilding bitmaps.</summary>
    public Size Size => new(_width, _height);

    public bool IsUsable(Size size) => !_disposed && _dib != IntPtr.Zero && _width == size.Width && _height == size.Height;

    /// <summary>
    /// Copies <paramref name="bitmap"/> (premultiplied ARGB) into the back buffer and shows it at
    /// <paramref name="origin"/>. Pass <paramref name="dirty"/> to move only that region to the screen.
    /// </summary>
    public void Present(IntPtr hwnd, Point origin, Bitmap bitmap, Rectangle? dirty)
    {
        if (_disposed || hwnd == IntPtr.Zero)
        {
            return;
        }

        Ensure(bitmap.Width, bitmap.Height);
        if (_dib == IntPtr.Zero)
        {
            return;
        }

        var region = dirty is Rectangle r
            ? Rectangle.Intersect(r, new Rectangle(0, 0, _width, _height))
            : new Rectangle(0, 0, _width, _height);
        if (region.Width <= 0 || region.Height <= 0)
        {
            return;
        }

        CopyRows(bitmap, region);
        Push(hwnd, origin, region);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        if (_previous != IntPtr.Zero && _memDc != IntPtr.Zero)
        {
            NativeMethods.SelectObject(_memDc, _previous);
            _previous = IntPtr.Zero;
        }

        if (_dib != IntPtr.Zero)
        {
            NativeMethods.DeleteObject(_dib);
            _dib = IntPtr.Zero;
            _bits = IntPtr.Zero;
        }

        if (_memDc != IntPtr.Zero)
        {
            NativeMethods.DeleteDC(_memDc);
            _memDc = IntPtr.Zero;
        }

        if (_screenDc != IntPtr.Zero)
        {
            NativeMethods.ReleaseDC(IntPtr.Zero, _screenDc);
            _screenDc = IntPtr.Zero;
        }

        if (_scratch != IntPtr.Zero)
        {
            Marshal.FreeHGlobal(_scratch);
            _scratch = IntPtr.Zero;
        }
    }

    private void Ensure(int width, int height)
    {
        if (_dib != IntPtr.Zero && _width == width && _height == height)
        {
            return;
        }

        Release();

        _screenDc = NativeMethods.GetDC(IntPtr.Zero);
        if (_screenDc == IntPtr.Zero)
        {
            return;
        }

        _memDc = NativeMethods.CreateCompatibleDC(_screenDc);
        if (_memDc == IntPtr.Zero)
        {
            return;
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
        _dib = NativeMethods.CreateDIBSection(_screenDc, ref info, 0, out _bits, IntPtr.Zero, 0);
        if (_dib == IntPtr.Zero || _bits == IntPtr.Zero)
        {
            return;
        }

        _previous = NativeMethods.SelectObject(_memDc, _dib);
        _width = width;
        _height = height;
        if (_scratch == IntPtr.Zero)
        {
            _scratch = Marshal.AllocHGlobal(ScratchBytes);
            Marshal.WriteInt32(_scratch, OffsetBlend, 0);
        }
    }

    private void Release()
    {
        if (_previous != IntPtr.Zero && _memDc != IntPtr.Zero)
        {
            NativeMethods.SelectObject(_memDc, _previous);
            _previous = IntPtr.Zero;
        }

        if (_dib != IntPtr.Zero)
        {
            NativeMethods.DeleteObject(_dib);
            _dib = IntPtr.Zero;
        }

        _bits = IntPtr.Zero;
        if (_memDc != IntPtr.Zero)
        {
            NativeMethods.DeleteDC(_memDc);
            _memDc = IntPtr.Zero;
        }

        if (_screenDc != IntPtr.Zero)
        {
            NativeMethods.ReleaseDC(IntPtr.Zero, _screenDc);
            _screenDc = IntPtr.Zero;
        }

        _width = 0;
        _height = 0;
    }

    private void CopyRows(Bitmap bitmap, Rectangle region)
    {
        var data = bitmap.LockBits(
            new Rectangle(0, 0, bitmap.Width, bitmap.Height),
            ImageLockMode.ReadOnly,
            PixelFormat.Format32bppPArgb);
        try
        {
            var stride = data.Stride;
            var absStride = Math.Abs(stride);
            var flipped = stride < 0;
            var rowBytes = _width * 4;
            var xOffset = region.Left * 4;
            var length = region.Width * 4;

            if (flipped)
            {
                for (var y = region.Top; y < region.Bottom; y++)
                {
                    var src = data.Scan0 + (_height - 1 - y) * absStride + xOffset;
                    NativeMethods.MoveMemory(_bits + y * rowBytes + xOffset, src, (UIntPtr)(uint)length);
                }

                return;
            }

            // Full-width rows (the common case) are one contiguous block, so one memcpy beats N.
            if (region.Left == 0 && length == rowBytes && absStride == rowBytes)
            {
                var src = data.Scan0 + region.Top * absStride;
                NativeMethods.MoveMemory(
                    _bits + region.Top * rowBytes,
                    src,
                    (UIntPtr)(uint)(rowBytes * region.Height));
                return;
            }

            for (var y = region.Top; y < region.Bottom; y++)
            {
                var src = data.Scan0 + y * absStride + xOffset;
                NativeMethods.MoveMemory(_bits + y * rowBytes + xOffset, src, (UIntPtr)(uint)length);
            }
        }
        finally
        {
            bitmap.UnlockBits(data);
        }
    }

    private void Push(IntPtr hwnd, Point origin, Rectangle dirty)
    {
        var scratch = _scratch;
        if (scratch == IntPtr.Zero)
        {
            return;
        }

        Marshal.WriteInt32(scratch, OffsetDst, origin.X);
        Marshal.WriteInt32(scratch, OffsetDst + 4, origin.Y);
        Marshal.WriteInt32(scratch, OffsetSize, _width);
        Marshal.WriteInt32(scratch, OffsetSize + 4, _height);
        Marshal.WriteInt32(scratch, OffsetSrc, 0);
        Marshal.WriteInt32(scratch, OffsetSrc + 4, 0);
        // BLENDFUNCTION: BlendOp 0, BlendFlags 0, SourceConstantAlpha 255, AlphaFormat 1.
        Marshal.WriteByte(scratch, OffsetBlend, 0);
        Marshal.WriteByte(scratch, OffsetBlend + 1, 0);
        Marshal.WriteByte(scratch, OffsetBlend + 2, 255);
        Marshal.WriteByte(scratch, OffsetBlend + 3, 1);
        Marshal.WriteInt32(scratch, OffsetDirty, dirty.Left);
        Marshal.WriteInt32(scratch, OffsetDirty + 4, dirty.Top);
        Marshal.WriteInt32(scratch, OffsetDirty + 8, dirty.Right);
        Marshal.WriteInt32(scratch, OffsetDirty + 12, dirty.Bottom);

        var info = new NativeMethods.UPDATELAYEREDWINDOWINFO
        {
            cbSize = (uint)Marshal.SizeOf<NativeMethods.UPDATELAYEREDWINDOWINFO>(),
            hdcDst = _screenDc,
            pptDst = scratch + OffsetDst,
            psize = scratch + OffsetSize,
            hdcSrc = _memDc,
            pptSrc = scratch + OffsetSrc,
            crKey = 0,
            pblend = scratch + OffsetBlend,
            dwFlags = 2,
            prcDirty = scratch + OffsetDirty,
        };

        if (!NativeMethods.UpdateLayeredWindowIndirect(hwnd, ref info))
        {
            // A failed present must not kill the capture session; the next frame retries.
            _ = Marshal.GetLastWin32Error();
        }
    }
}
