using CaptureDemo.Capture;

namespace CaptureDemo.LongShot;

internal sealed class RegionOverlayForm : ScreenOverlayForm
{
    private readonly TaskCompletionSource<Rectangle?> _done =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    private Point? _down;
    private Rectangle? _rect;
    private string _line1 = "长截图 · 松开鼠标开始滚动 · Esc 取消";
    private string _line2 = "按住左键框选。选区保持原亮度，外面变暗。尽量避开固定顶栏和底栏。";
    private bool _finished;
    private bool _capturing;
    private bool _moveQueued;
    private Point _pending;

    public Task<Rectangle?> SelectAsync()
    {
        Show();
        return _done.Task;
    }

    public void Cancel()
    {
        if (InvokeRequired)
        {
            BeginInvoke(Cancel);
            return;
        }

        Finish(null);
    }

    public void QueueMouseMove(Point screen)
    {
        _pending = screen;
        if (_moveQueued)
        {
            return;
        }

        _moveQueued = true;
        try
        {
            BeginInvoke(ProcessMove);
        }
        catch (InvalidOperationException)
        {
            _moveQueued = false;
        }
    }

    public void OnLeftDown(Point screen)
    {
        if (InvokeRequired)
        {
            BeginInvoke(() => OnLeftDown(screen));
            return;
        }

        if (_finished)
        {
            return;
        }

        _down = screen;
        _rect = new Rectangle(screen.X, screen.Y, 0, 0);
        PaintMask();
    }

    public void OnLeftUp(Point screen)
    {
        if (InvokeRequired)
        {
            BeginInvoke(() => OnLeftUp(screen));
            return;
        }

        if (_finished || _down is not Point start)
        {
            return;
        }

        _down = null;
        var rect = ScreenRects.Normalize(start, screen);
        if (rect.Width < 24 || rect.Height < 24)
        {
            _rect = null;
            _line2 = "选区太小。请框住一块可滚动内容（至少约 24×24）。";
            PaintMask();
            return;
        }

        Finish(rect);
    }

    /// <summary>Keep the dim mask up. The hole is the live region being stitched.</summary>
    public void BeginCapture(Rectangle hole)
    {
        SetCapture(hole, "洞里是正在截的画面。滚一下改为手动 · Esc 停止");
    }

    public void SetCapture(Rectangle hole, string status)
    {
        if (InvokeRequired)
        {
            BeginInvoke(() => SetCapture(hole, status));
            return;
        }

        if (IsDisposed)
        {
            return;
        }

        _finished = true;
        _down = null;
        // The confirmed drag is the hole for the whole session. Later status updates must not
        // reshape it to a motion crop or any other detected blob.
        if (!_capturing)
        {
            _rect = hole;
        }

        _capturing = true;
        _line1 = "长截图";
        _line2 = status;
        if (!Visible)
        {
            Show();
        }
        else
        {
            PaintMask();
        }
    }

    protected override void PaintMask()
    {
        var hole = _rect is Rectangle screen && screen.Width > 1 && screen.Height > 1 ? screen : (Rectangle?)null;
        string? caption = null;
        if (!_capturing && hole is Rectangle rect)
        {
            caption = $"{rect.Width} × {rect.Height}";
        }

        PresentMask(hole, Theme.LongMaskBorder, _line1, _line2, caption);
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        _done.TrySetResult(null);
        base.OnFormClosed(e);
    }

    private void ProcessMove()
    {
        _moveQueued = false;
        if (_finished || IsDisposed || _down is not Point start)
        {
            return;
        }

        var screen = _pending;
        _rect = ScreenRects.Normalize(start, screen);
        _line2 = $"选区 {_rect.Value.Width} × {_rect.Value.Height}。松开后连续下滚，这个框保持不动；滚动滚轮可改为手动。";
        PaintMask();
        if (screen != _pending && !_moveQueued && !IsDisposed)
        {
            _moveQueued = true;
            BeginInvoke(ProcessMove);
        }
    }

    private void Finish(Rectangle? rect)
    {
        if (_finished)
        {
            return;
        }

        _finished = true;
        if (rect == null)
        {
            Hide();
        }

        _done.TrySetResult(rect);
    }
}
