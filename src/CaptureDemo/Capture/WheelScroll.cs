using CaptureDemo.Core;

namespace CaptureDemo.Capture;

internal static class WheelScroll
{
    /// <summary>
    /// Injects sub-notch wheel deltas until stopped. Grabbing a frame must not pause this,
    /// or the page steps once per sample the way a notched wheel does.
    /// </summary>
    internal sealed class Streamer : IDisposable
    {
        private readonly object _aimGate = new();
        private readonly Thread _thread;
        private readonly ManualResetEventSlim _stop = new(false);
        private Point _aim;
        private bool _aimDirty;
        private int _disposed;

        private Streamer(Point aim, int unitsPerSecond, CancellationToken token, Func<bool> userTookOver)
        {
            _aim = aim;
            _aimDirty = true;
            _thread = new Thread(() => Run(unitsPerSecond, token, userTookOver))
            {
                IsBackground = true,
                Name = "CaptureDemo.Wheel",
            };
            _thread.Start();
        }

        public static Streamer Start(Point aim, int unitsPerSecond, CancellationToken token, Func<bool> userTookOver)
        {
            ArgumentNullException.ThrowIfNull(userTookOver);
            return new Streamer(aim, unitsPerSecond, token, userTookOver);
        }

        public void SetAim(Point aim)
        {
            lock (_aimGate)
            {
                if (_aim == aim)
                {
                    return;
                }

                _aim = aim;
                _aimDirty = true;
            }
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
            {
                return;
            }

            _stop.Set();
            _thread.Join(TimeSpan.FromSeconds(2));
            if (!_thread.IsAlive)
            {
                _stop.Dispose();
            }
        }

        private void Run(int unitsPerSecond, CancellationToken token, Func<bool> userTookOver)
        {
            NativeMethods.timeBeginPeriod(1);
            try
            {
                long debt = 0;
                var last = Environment.TickCount64;
                while (!_stop.IsSet && !token.IsCancellationRequested && !userTookOver())
                {
                    if (TryTakeRetarget(out var aim))
                    {
                        NativeMethods.SetCursorPos(aim.X, aim.Y);
                        TryForegroundAt(aim);
                        if (token.WaitHandle.WaitOne(15) || _stop.IsSet || userTookOver())
                        {
                            return;
                        }

                        // The activation wait is not scroll time. Do not dump it as one jump.
                        last = Environment.TickCount64;
                        debt = 0;
                    }

                    var now = Environment.TickCount64;
                    var elapsed = (int)Math.Min(Math.Max(0, now - last), 32);
                    last = now;
                    if (elapsed > 0 && unitsPerSecond > 0)
                    {
                        EmitDebt(ref debt, unitsPerSecond, elapsed);
                    }

                    if (token.WaitHandle.WaitOne(WheelPace.TickMilliseconds) || _stop.IsSet)
                    {
                        return;
                    }
                }
            }
            finally
            {
                NativeMethods.timeEndPeriod(1);
            }
        }

        private bool TryTakeRetarget(out Point aim)
        {
            lock (_aimGate)
            {
                aim = _aim;
                if (!_aimDirty)
                {
                    return false;
                }

                _aimDirty = false;
                return true;
            }
        }
    }

    private static void EmitDebt(ref long debt, int unitsPerSecond, int elapsedMs)
    {
        Span<int> chunks = stackalloc int[16];
        while (true)
        {
            var count = WheelPace.Drain(ref debt, unitsPerSecond, elapsedMs, chunks);
            for (var i = 0; i < count; i++)
            {
                EmitDown(chunks[i]);
            }

            if (count < chunks.Length)
            {
                return;
            }

            elapsedMs = 0;
        }
    }

    private static void EmitDown(int units)
    {
        if (units <= 0)
        {
            return;
        }

        NativeMethods.mouse_event(
            NativeMethods.MouseeventfWheel,
            0,
            0,
            unchecked((uint)(-units)),
            UIntPtr.Zero);
    }

    private static void TryForegroundAt(Point screen)
    {
        var raw = NativeMethods.WindowFromPoint(new NativeMethods.POINT { X = screen.X, Y = screen.Y });
        if (raw == IntPtr.Zero)
        {
            return;
        }

        var root = NativeMethods.GetAncestor(raw, NativeMethods.GaRoot);
        var hwnd = root == IntPtr.Zero ? raw : root;
        var foreground = NativeMethods.GetForegroundWindow();
        var thisThread = NativeMethods.GetCurrentThreadId();
        var fgThread = foreground == IntPtr.Zero ? 0 : NativeMethods.GetWindowThreadProcessId(foreground, out _);
        var targetThread = NativeMethods.GetWindowThreadProcessId(hwnd, out _);
        var attachedFg = false;
        var attachedTarget = false;
        try
        {
            if (fgThread != 0 && fgThread != thisThread)
            {
                attachedFg = NativeMethods.AttachThreadInput(thisThread, fgThread, true);
            }

            if (targetThread != 0 && targetThread != thisThread && targetThread != fgThread)
            {
                attachedTarget = NativeMethods.AttachThreadInput(thisThread, targetThread, true);
            }

            NativeMethods.BringWindowToTop(hwnd);
            NativeMethods.SetForegroundWindow(hwnd);
        }
        finally
        {
            if (attachedTarget)
            {
                NativeMethods.AttachThreadInput(thisThread, targetThread, false);
            }

            if (attachedFg)
            {
                NativeMethods.AttachThreadInput(thisThread, fgThread, false);
            }
        }
    }
}

internal readonly struct CursorRestorer : IDisposable
{
    private readonly bool _ok;
    private readonly int _x;
    private readonly int _y;

    private CursorRestorer(bool ok, int x, int y)
    {
        _ok = ok;
        _x = x;
        _y = y;
    }

    public static CursorRestorer Capture()
    {
        if (!NativeMethods.GetCursorPos(out var point))
        {
            return new CursorRestorer(false, 0, 0);
        }

        return new CursorRestorer(true, point.X, point.Y);
    }

    public void Dispose()
    {
        if (_ok)
        {
            NativeMethods.SetCursorPos(_x, _y);
        }
    }
}
