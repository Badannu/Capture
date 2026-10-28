namespace CaptureDemo.Element;

internal sealed record ElementPick(Rectangle Bounds, string Label);

internal sealed class ElementOverlayForm : ScreenOverlayForm
{
    private const int DragThreshold = 6;

    private readonly ElementPicker _picker;
    private readonly TaskCompletionSource<ElementPick?> _done =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    private IReadOnlyList<ElementSnapshot> _chain = Array.Empty<ElementSnapshot>();
    private int _index = -1;
    private Point? _lastPick;
    private Point? _down;
    private bool _dragging;
    private Rectangle? _dragRect;
    private string _line2 = "屏幕已变暗。高亮区域保持原亮度。";
    private bool _finished;
    private bool _moveQueued;
    private Point _pending;

    public ElementOverlayForm(ElementPicker picker)
    {
        _picker = picker;
    }

    public Task<ElementPick?> PickAsync()
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
        _dragging = false;
        _dragRect = null;
    }

    public void OnLeftUp(Point screen)
    {
        if (InvokeRequired)
        {
            BeginInvoke(() => OnLeftUp(screen));
            return;
        }

        if (_finished)
        {
            return;
        }

        var origin = _down;
        _down = null;
        if (origin is Point start && _dragging)
        {
            _dragging = false;
            var rect = ScreenRects.Normalize(start, screen);
            _dragRect = null;
            if (rect.Width >= 8 && rect.Height >= 8)
            {
                Finish(new ElementPick(rect, "矩形"));
                return;
            }

            _line2 = "矩形太小，请重新拖拽。";
            PaintMask();
            return;
        }

        _dragging = false;
        if (CurrentSnapshot() is ElementSnapshot snapshot)
        {
            Finish(new ElementPick(snapshot.Bounds, snapshot.Caption));
            return;
        }

        _line2 = "没有可用控件。按住左键拖出一个矩形，或换一个窗口再试。";
        PaintMask();
    }

    public void OnWheel(int delta)
    {
        if (InvokeRequired)
        {
            BeginInvoke(() => OnWheel(delta));
            return;
        }

        if (_finished || _dragging || delta == 0 || _chain.Count == 0)
        {
            return;
        }

        var next = FindUseful(_index, delta > 0 ? 1 : -1);
        if (next == _index)
        {
            return;
        }

        _index = next;
        UpdateLine2();
        PaintMask();
    }

    protected override void PaintMask()
    {
        var screen = CurrentScreenRect();
        var caption = _dragging
            ? screen is Rectangle drag ? $"{drag.Width} × {drag.Height}" : null
            : CurrentSnapshot()?.Caption;
        PresentMask(
            screen,
            _dragging ? Theme.DragBorder : Theme.MaskBorder,
            "元素截图 · 左键截取 · 拖拽改为矩形 · 滚轮切换层级 · Esc 取消",
            _line2,
            caption);
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        _done.TrySetResult(null);
        base.OnFormClosed(e);
    }

    private void ProcessMove()
    {
        _moveQueued = false;
        if (_finished || IsDisposed)
        {
            return;
        }

        var screen = _pending;
        HandleMove(screen);
        if (screen != _pending && !_moveQueued && !IsDisposed)
        {
            _moveQueued = true;
            BeginInvoke(ProcessMove);
        }
    }

    private void HandleMove(Point screen)
    {
        if (_down is Point start &&
            (Math.Abs(screen.X - start.X) >= DragThreshold || Math.Abs(screen.Y - start.Y) >= DragThreshold))
        {
            _dragging = true;
            _dragRect = ScreenRects.Normalize(start, screen);
            _line2 = "松开鼠标，按矩形截取。选区保持原亮度。";
            PaintMask();
            return;
        }

        if (_dragging)
        {
            return;
        }

        if (_lastPick is Point last && ScreenRects.DistanceSquared(last, screen) < 16 && _chain.Count > 0)
        {
            return;
        }

        _lastPick = screen;
        _chain = _picker.CaptureChain(screen);
        _index = FindUseful(-1, 1);
        UpdateLine2();
        PaintMask();
    }

    private ElementSnapshot? CurrentSnapshot()
    {
        if (_index < 0 || _index >= _chain.Count || !_chain[_index].IsUseful)
        {
            return null;
        }

        return _chain[_index];
    }

    private Rectangle? CurrentScreenRect()
    {
        if (_dragging && _dragRect is Rectangle drag)
        {
            return drag;
        }

        return CurrentSnapshot()?.Bounds;
    }

    private int FindUseful(int current, int step)
    {
        if (_chain.Count == 0)
        {
            return -1;
        }

        var index = current;
        for (var n = 0; n < _chain.Count; n++)
        {
            if (index < 0)
            {
                index = step > 0 ? 0 : _chain.Count - 1;
            }
            else
            {
                index += step;
                if (index < 0)
                {
                    index = _chain.Count - 1;
                }
                else if (index >= _chain.Count)
                {
                    index = 0;
                }
            }

            if (_chain[index].IsUseful)
            {
                return index;
            }
        }

        return current;
    }

    private void UpdateLine2()
    {
        var snapshot = CurrentSnapshot();
        if (snapshot == null)
        {
            _line2 = "没有可用控件（自绘界面或没有辅助功能树）。可以按住左键拖矩形。";
            return;
        }

        var usefulCount = 0;
        var usefulPos = 0;
        for (var i = 0; i < _chain.Count; i++)
        {
            if (!_chain[i].IsUseful)
            {
                continue;
            }

            usefulCount++;
            if (i == _index)
            {
                usefulPos = usefulCount;
            }
        }

        _line2 = $"当前：{snapshot.Caption} · 第 {usefulPos}/{usefulCount} 层";
    }

    private void Finish(ElementPick? pick)
    {
        if (_finished)
        {
            return;
        }

        _finished = true;
        Hide();
        _done.TrySetResult(pick);
    }
}
