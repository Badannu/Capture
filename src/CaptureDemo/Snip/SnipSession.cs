using CaptureDemo.Core;

namespace CaptureDemo.Snip;

internal sealed class SnipSession
{
    private readonly AppSettings _settings;
    private readonly TaskCompletionSource<SnipResult> _done = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly List<AnnotateStroke> _strokes = new();
    private SnipOverlayForm? _overlay;
    private CapturePillForm? _toolbar;
    private AnnoPillForm? _anno;
    private LowLevelInputHook? _hook;
    private BgraBuffer? _base;
    private Bitmap? _view;
    private List<Point>? _draft;
    private Point? _down;
    private readonly List<Point> _lasso = new();
    private SnipMode _mode = SnipMode.Rectangle;
    private AnnoTool _tool = AnnoTool.Pen;
    private Rectangle _work;
    private Rectangle _selection;
    private string _kind = "rect";
    private bool _editing;
    private bool _finished;
    private Point _cursor;

    private SnipSession(AppSettings settings)
    {
        _settings = settings;
    }

    public static Task<SnipResult> RunAsync(AppSettings settings, SnipMode initial)
    {
        var session = new SnipSession(settings);
        return session.RunAsync(initial);
    }

    private async Task<SnipResult> RunAsync(SnipMode initial)
    {
        try
        {
            Setup(initial);
            return await _done.Task;
        }
        finally
        {
            Cleanup();
        }
    }

    private void Setup(SnipMode initial)
    {
        _cursor = CursorOrZero();
        _work = NativeMethods.WorkArea(_cursor);
        _overlay = new SnipOverlayForm();
        _overlay.Show();
        _toolbar = BuildToolbar();
        _toolbar.Place(_work, null);
        _toolbar.Show();
        _hook = new LowLevelInputHook
        {
            SuppressLeftButton = true,
            SuppressEscape = true,
        };
        _hook.PassThroughLeftButton = IsChrome;
        _hook.MouseMove += OnMove;
        _hook.LeftButtonDown += OnDown;
        _hook.LeftButtonUp += OnUp;
        _hook.Escape += () => Finish(CaptureOutcome.Cancelled(), SnipFollow.None, null);
        _hook.InstallMouseAndKeyboard();
        SelectMode(initial);
    }

    private CapturePillForm BuildToolbar()
    {
        var bar = new CapturePillForm();
        bar.SetShortcuts(_settings.LongHotkey, _settings.ElementHotkey);
        bar.ModeChosen += SelectMode;
        bar.SettingsClicked += () => Finish(CaptureOutcome.OpenSettings(), SnipFollow.Settings, null);
        bar.CloseClicked += () => Finish(CaptureOutcome.Cancelled(), SnipFollow.None, null);
        bar.SetMode(_mode);
        return bar;
    }

    private void EnsureAnno()
    {
        if (_anno != null)
        {
            return;
        }

        var bar = new AnnoPillForm();
        bar.LongShot += () => Finish(CaptureOutcome.Cancelled(), SnipFollow.Long, _selection);
        bar.ToolChosen += SetTool;
        bar.UndoClicked += Undo;
        bar.ConfirmClicked += Confirm;
        bar.DiscardClicked += () => Finish(CaptureOutcome.Cancelled("已放弃，没有保存。"), SnipFollow.None, null);
        _anno = bar;
    }

    private void SetTool(AnnoTool tool)
    {
        _tool = tool;
        _anno?.SetTool(tool);
    }

    private void SelectMode(SnipMode mode)
    {
        if (_finished)
        {
            return;
        }

        if (mode == SnipMode.Long)
        {
            Finish(CaptureOutcome.Cancelled(), SnipFollow.Long, _editing ? _selection : null);
            return;
        }

        if (mode == SnipMode.Element)
        {
            Finish(CaptureOutcome.Cancelled(), SnipFollow.Element, null);
            return;
        }

        LeaveEditor();
        _mode = mode;
        _toolbar?.SetMode(mode);

        if (mode == SnipMode.Fullscreen)
        {
            EnterFullscreen();
            return;
        }

        _down = null;
        _lasso.Clear();
        PaintSelecting(HintFor(mode));
    }

    private void EnterFullscreen()
    {
        var monitor = NativeMethods.MonitorBounds(_cursor);
        try
        {
            BeginEditor(monitor, "screen");
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            PaintSelecting("全屏截取失败：" + ex.Message);
        }
    }

    private void BeginEditor(Rectangle screen, string kind)
    {
        screen = Rectangle.Intersect(screen, NativeMethods.VirtualScreen());
        if (screen.Width < 8 || screen.Height < 8)
        {
            PaintSelecting("选区太小。");
            return;
        }

        using var grabbed = ScreenGrabber.Grab(screen);
        var buffer = BgraConvert.FromBitmap(grabbed);
        if (kind == "freeform" && _lasso.Count >= 3)
        {
            var xs = new int[_lasso.Count];
            var ys = new int[_lasso.Count];
            for (var i = 0; i < _lasso.Count; i++)
            {
                xs[i] = _lasso[i].X - screen.X;
                ys[i] = _lasso[i].Y - screen.Y;
            }

            buffer = AnnotationComposer.MaskOutsidePolygon(buffer, xs, ys);
        }

        _selection = screen;
        _kind = kind;
        _base = buffer;
        _strokes.Clear();
        _draft = null;
        _editing = true;
        _down = null;
        EnsureAnno();
        SetTool(AnnoTool.Pen);
        _anno!.Place(_work, screen);
        if (!_anno.Visible)
        {
            _anno.Show();
        }

        Repaint();
    }

    private void LeaveEditor()
    {
        _editing = false;
        _draft = null;
        _strokes.Clear();
        _base = null;
        _view?.Dispose();
        _view = null;
        if (_anno is { Visible: true })
        {
            _anno.Hide();
        }
    }

    private void OnMove(Point screen)
    {
        if (_finished || IsChrome(screen))
        {
            return;
        }

        _cursor = screen;
        var work = NativeMethods.WorkArea(screen);
        if (work != _work)
        {
            _work = work;
            if (!_editing)
            {
                _toolbar?.Place(_work, null);
            }
        }

        if (_editing)
        {
            if (_draft == null || _down == null)
            {
                return;
            }

            var image = ToImage(screen);
            if (_tool == AnnoTool.Pen)
            {
                var last = _draft[^1];
                if (ScreenRects.DistanceSquared(last, image) >= 4)
                {
                    _draft.Add(image);
                    Repaint();
                }
            }
            else
            {
                if (_draft.Count == 1)
                {
                    _draft.Add(image);
                }
                else
                {
                    _draft[^1] = image;
                }

                Repaint();
            }

            return;
        }

        if (_mode == SnipMode.Rectangle && _down is Point anchor)
        {
                _overlay?.ShowHole(_work, ScreenRects.Normalize(anchor, screen), "");
            return;
        }

        if (_mode == SnipMode.Freeform && _down != null)
        {
            if (_lasso.Count == 0 || ScreenRects.DistanceSquared(_lasso[^1], screen) >= 4)
            {
                _lasso.Add(screen);
                _overlay?.ShowLasso(_work, _lasso, "");
            }

            return;
        }

        if (_mode == SnipMode.Window)
        {
            if (WindowHit.TryHit(screen, out var bounds, out var title))
            {
                _overlay?.ShowHole(_work, bounds, title);
            }
            else
            {
                _overlay?.ShowDim(_work, "指向一个窗口");
            }
        }
    }

    private void OnDown(Point screen)
    {
        if (_finished)
        {
            return;
        }

        if (IsChrome(screen))
        {
            return;
        }

        _cursor = screen;
        if (_editing)
        {
            if (!_selection.Contains(screen) || _base == null)
            {
                return;
            }

            _down = screen;
            _draft = new List<Point> { ToImage(screen) };
            return;
        }

        if (_mode is SnipMode.Rectangle or SnipMode.Freeform)
        {
            _down = screen;
            _lasso.Clear();
            if (_mode == SnipMode.Freeform)
            {
                _lasso.Add(screen);
            }
        }
    }

    private void OnUp(Point screen)
    {
        if (_finished)
        {
            return;
        }

        if (_editing)
        {
            CommitDraft();
            _down = null;
            return;
        }

        if (IsChrome(screen))
        {
            return;
        }

        if (_mode == SnipMode.Rectangle && _down is Point anchor)
        {
            _down = null;
            var rect = ScreenRects.Normalize(anchor, screen);
            if (rect.Width < 8 || rect.Height < 8)
            {
                PaintSelecting("再大一点");
                return;
            }

            try
            {
                BeginEditor(rect, "rect");
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                PaintSelecting("截取失败：" + ex.Message);
            }

            return;
        }

        if (_mode == SnipMode.Freeform && _down != null)
        {
            _down = null;
            if (_lasso.Count < 3)
            {
                PaintSelecting("再画长一点");
                return;
            }

            var rect = BoundsOf(_lasso);
            if (rect.Width < 8 || rect.Height < 8)
            {
                PaintSelecting("再大一点");
                return;
            }

            try
            {
                BeginEditor(rect, "freeform");
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                PaintSelecting("截取失败：" + ex.Message);
            }

            return;
        }

        if (_mode == SnipMode.Window && WindowHit.TryHit(screen, out var bounds, out _))
        {
            try
            {
                BeginEditor(bounds, "window");
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                PaintSelecting("截取失败：" + ex.Message);
            }
        }
    }

    private void CommitDraft()
    {
        if (_draft == null || _draft.Count == 0)
        {
            _draft = null;
            return;
        }

        var keep = _tool switch
        {
            AnnoTool.Pen => true,
            AnnoTool.Arrow => _draft.Count >= 2 && ScreenRects.DistanceSquared(_draft[0], _draft[^1]) >= 16,
            _ => _draft.Count >= 2 && SpanAtLeast(_draft, 4),
        };
        if (keep)
        {
            _strokes.Add(new AnnotateStroke
            {
                Kind = _tool switch
                {
                    AnnoTool.Arrow => AnnotateKind.Arrow,
                    AnnoTool.Highlight => AnnotateKind.Highlight,
                    AnnoTool.Mosaic => AnnotateKind.Mosaic,
                    _ => AnnotateKind.Pen,
                },
                Xs = _draft.Select(p => p.X).ToArray(),
                Ys = _draft.Select(p => p.Y).ToArray(),
            });
        }

        _draft = null;
        Repaint();
    }

    private void Undo()
    {
        if (_draft != null)
        {
            _draft = null;
            _down = null;
            Repaint();
            return;
        }

        if (_strokes.Count > 0)
        {
            _strokes.RemoveAt(_strokes.Count - 1);
            Repaint();
        }
    }

    private void Confirm()
    {
        if (_base == null)
        {
            return;
        }

        CommitDraft();
        var composed = AnnotationComposer.Apply(_base, _strokes);
        using var bitmap = BgraConvert.ToBitmap(composed);
        var saved = CaptureSaver.SavePng(bitmap, _kind, _settings);
        var clip = CaptureSaver.ClipboardPhrase(_settings, saved.CopiedToClipboard);
        Finish(CaptureOutcome.Saved(saved.Path, "已保存。" + clip), SnipFollow.None, null);
    }

    private void Repaint()
    {
        if (_base == null || _overlay == null)
        {
            return;
        }

        IReadOnlyList<AnnotateStroke> strokes = _strokes;
        if (_draft is { Count: > 0 })
        {
            var extra = new AnnotateStroke
            {
                Kind = _tool switch
                {
                    AnnoTool.Arrow => AnnotateKind.Arrow,
                    AnnoTool.Highlight => AnnotateKind.Highlight,
                    AnnoTool.Mosaic => AnnotateKind.Mosaic,
                    _ => AnnotateKind.Pen,
                },
                Xs = _draft.Select(p => p.X).ToArray(),
                Ys = _draft.Select(p => p.Y).ToArray(),
            };
            var both = new List<AnnotateStroke>(_strokes.Count + 1);
            both.AddRange(_strokes);
            both.Add(extra);
            strokes = both;
        }

        var composed = AnnotationComposer.Apply(_base, strokes);
        var next = BgraConvert.ToBitmap(composed);
        var previous = _view;
        _view = next;
        _overlay.ShowFrozen(_selection, next);
        previous?.Dispose();
    }

    private void PaintSelecting(string hint)
    {
        if (_mode == SnipMode.Window)
        {
            if (WindowHit.TryHit(_cursor, out var bounds, out var title))
            {
                _overlay?.ShowHole(_work, bounds, title);
                return;
            }
        }

        _overlay?.ShowDim(_work, hint);
    }

    private static string HintFor(SnipMode mode) => mode switch
    {
        SnipMode.Rectangle => "拖出矩形",
        SnipMode.Window => "指向一个窗口",
        SnipMode.Freeform => "按住绘制",
        _ => "",
    };

    private Point ToImage(Point screen)
    {
        var x = Math.Clamp(screen.X - _selection.X, 0, Math.Max(0, _selection.Width - 1));
        var y = Math.Clamp(screen.Y - _selection.Y, 0, Math.Max(0, _selection.Height - 1));
        return new Point(x, y);
    }

    private bool IsChrome(Point screen)
    {
        return Contains(_toolbar, screen) || Contains(_anno, screen);
    }

    private static bool Contains(Form? form, Point screen) =>
        form is { IsDisposed: false, Visible: true } && form.Bounds.Contains(screen);

    private static Rectangle BoundsOf(List<Point> points)
    {
        var left = points[0].X;
        var right = left;
        var top = points[0].Y;
        var bottom = top;
        foreach (var point in points)
        {
            left = Math.Min(left, point.X);
            right = Math.Max(right, point.X);
            top = Math.Min(top, point.Y);
            bottom = Math.Max(bottom, point.Y);
        }

        return Rectangle.FromLTRB(left, top, right + 1, bottom + 1);
    }

    private static bool SpanAtLeast(List<Point> points, int min)
    {
        var rect = BoundsOf(points);
        return rect.Width >= min && rect.Height >= min;
    }

    private static Point CursorOrZero()
    {
        if (NativeMethods.GetCursorPos(out var point))
        {
            return new Point(point.X, point.Y);
        }

        return Point.Empty;
    }

    private void Finish(CaptureOutcome outcome, SnipFollow follow, Rectangle? preset)
    {
        if (_finished)
        {
            return;
        }

        _finished = true;
        _done.TrySetResult(new SnipResult(outcome, follow, preset));
    }

    private void Cleanup()
    {
        if (_overlay is { IsDisposed: false, InvokeRequired: true })
        {
            try
            {
                _overlay.Invoke(Cleanup);
            }
            catch (Exception ex) when (ex is ObjectDisposedException or InvalidOperationException)
            {
            }

            return;
        }

        _hook?.Dispose();
        _hook = null;
        _view?.Dispose();
        _view = null;
        if (_anno is { IsDisposed: false })
        {
            _anno.Dispose();
        }

        _anno = null;
        if (_toolbar is { IsDisposed: false })
        {
            _toolbar.Dispose();
        }

        _toolbar = null;
        if (_overlay is { IsDisposed: false })
        {
            _overlay.Dispose();
        }

        _overlay = null;
    }
}
