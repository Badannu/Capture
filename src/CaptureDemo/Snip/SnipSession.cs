using System.Drawing.Imaging;
using CaptureDemo.Core;
using CaptureDemo.Element;

namespace CaptureDemo.Snip;

internal sealed class SnipSession
{
    private readonly AppSettings _settings;
    private readonly TaskCompletionSource<SnipResult> _done = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private SnipOverlayForm? _overlay;
    private CapturePillForm? _toolbar;
    private AnnoPillForm? _anno;
    private AnnoLayerForm? _layer;
    private LowLevelInputHook? _hook;
    private ElementHover? _element;
    private readonly System.Windows.Forms.Timer _elementPump = new() { Interval = 70 };
    private Point _elementPoint;
    private BgraBuffer? _base;
    private Point? _down;
    private readonly List<Point> _lasso = new();
    private SnipMode _mode = SnipMode.Rectangle;
    private AnnoTool _tool = AnnoTool.Pen;
    private AnnoItem? _ink;
    private AnnoItem? _selected;
    private AnnoHandle _dragHandle;
    private Point _dragOrigin;
    private int[] _dragXs = Array.Empty<int>();
    private int[] _dragYs = Array.Empty<int>();
    private Rectangle _dragBounds;
    private int _color = AnnoPalette.Colors[AnnoPalette.Default].ToArgb();
    private float _weight = AnnoPalette.Weights[1];
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
        _hook.Wheel += OnWheel;
        _hook.Escape += OnEscape;
        _hook.InstallMouseAndKeyboard();
        _elementPump.Tick += (_, _) => PumpElement();
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
        bar.ColorChosen += ApplyColor;
        bar.WeightChosen += ApplyWeight;
        bar.UndoClicked += Undo;
        bar.ConfirmClicked += Confirm;
        bar.DiscardClicked += () => Finish(CaptureOutcome.Cancelled("已放弃，没有保存。"), SnipFollow.None, null);
        bar.SetColor(_color);
        bar.SetWeight(_weight);
        _anno = bar;
    }

    private void SetTool(AnnoTool tool)
    {
        _tool = tool;
        _anno?.SetTool(tool);
        // Leaving a drawing tool mid-drag would strand a half-finished shape.
        if (_ink != null)
        {
            EndDraw();
        }

        if (tool != AnnoTool.Select && _layer != null)
        {
            _layer.Selected = null;
            _selected = null;
        }
    }

    private void ApplyColor(int argb)
    {
        _color = argb;
        if (_selected != null)
        {
            _selected.ColorArgb = argb;
            _layer?.Redraw(Rectangle.Inflate(_selected.Bounds, 24, 24));
        }
    }

    private void ApplyWeight(float weight)
    {
        _weight = weight;
        if (_selected != null)
        {
            _selected.Width = weight * Scale;
            _layer?.Redraw(Rectangle.Inflate(_selected.Bounds, 24, 24));
        }
    }

    private void SelectMode(SnipMode mode)
    {
        if (_finished)
        {
            return;
        }

        _elementPump.Stop();

        // Long capture no longer tears the capsule down on click. The mode is just a
        // highlighted state until a region is framed, then the session hands the rect
        // to the scrolling pipeline. Leaving the editor is the one exception: the
        // region is already there, so hand it over straight away.
        if (mode == SnipMode.Long)
        {
            if (_editing)
            {
                Finish(CaptureOutcome.Cancelled(), SnipFollow.Long, _selection);
                return;
            }

            LeaveEditor();
            _mode = mode;
            _toolbar?.SetMode(mode);
            _down = null;
            _lasso.Clear();
            PaintSelecting(HintFor(mode));
            return;
        }

        if (mode == SnipMode.Element)
        {
            LeaveEditor();
            _mode = mode;
            _toolbar?.SetMode(mode);
            _down = null;
            _lasso.Clear();
            EnterElementHover();
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

    /// <summary>
    /// Element mode lives in this session too, so the capsule keeps showing the picked
    /// mode while the pointer walks the UI tree. The wheel climbs to the parent element.
    /// </summary>
    private void EnterElementHover()
    {
        if (_element == null)
        {
            try
            {
                _element = new ElementHover();
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                // Locked-down machine or a broken automation provider: fall back rather
                // than leave the capsule parked on a mode that cannot do anything.
                SelectMode(SnipMode.Rectangle);
                PaintSelecting("元素截图不可用：" + ex.Message);
                return;
            }
        }

        ShowElement(_cursor);
        // UIA hit tests can take tens of milliseconds on Chromium and Electron windows.
        // Running one inside the mouse hook would drag the system pointer, so the hook
        // only records the position and this timer does the probing.
        _elementPoint = _cursor;
        _elementPump.Start();
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
        _ink = null;
        _selected = null;
        _dragHandle = AnnoHandle.None;
        _editing = true;
        _down = null;

        // Freeze the capture once. Everything after this point is incremental ink on top of it.
        // The bitmap's ownership passes to the layer, which disposes it with the window.
        var frozen = BgraConvert.ToBitmap(buffer);
        _overlay?.ShowFrozen(screen, frozen);

        EnsureAnno();
        SetTool(AnnoTool.Pen);
        _layer ??= new AnnoLayerForm();
        _layer.Attach(frozen, screen);

        // The annotation layer covers the whole selection, so the bar has to be lifted above it.
        _anno!.Place(_work, screen);
        if (!_anno.Visible)
        {
            _anno.Show();
        }

        _anno.BringToFront();
    }

    private void LeaveEditor()
    {
        _editing = false;
        _ink = null;
        _selected = null;
        _dragHandle = AnnoHandle.None;
        _base = null;
        _anno?.CloseStylePopups();
        if (_layer is { Visible: true })
        {
            _layer.Hide();
        }

        if (_anno is { Visible: true })
        {
            _anno.Hide();
        }
    }

    private float Scale => _layer?.ScreenScale ?? 1f;

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
            if (_tool == AnnoTool.Select)
            {
                MoveSelect(ToImage(screen));
                return;
            }

            if (_ink == null)
            {
                return;
            }

            ExtendDraw(ToImage(screen));
            return;
        }

        if (_mode == SnipMode.Element)
        {
            _elementPoint = screen;
            return;
        }

        if (_mode is SnipMode.Rectangle or SnipMode.Long && _down is Point anchor)
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
            // Clicks that land on the annotation bar or a style popup belong to them.
            return;
        }

        if (_anno is { AnyPopupVisible: true } open)
        {
            // Anywhere else dismisses the popup, and that click does not also draw.
            open.CloseStylePopups();
            return;
        }

        _cursor = screen;
        if (_editing)
        {
            if (!_selection.Contains(screen) || _layer == null)
            {
                return;
            }

            var image = ToImage(screen);
            _down = screen;
            if (_tool == AnnoTool.Select)
            {
                StartSelect(image);
                return;
            }

            StartDraw(image);
            return;
        }

        if (_mode is SnipMode.Rectangle or SnipMode.Long or SnipMode.Freeform)
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
            if (_tool == AnnoTool.Select)
            {
                EndSelect();
            }
            else
            {
                EndDraw();
            }

            _down = null;
            return;
        }

        if (IsChrome(screen))
        {
            return;
        }

        if (_mode == SnipMode.Element)
        {
            var found = _element?.Current;
            if (found is { } element && element.Bounds.Width >= 2 && element.Bounds.Height >= 2)
            {
                Finish(CaptureOutcome.Cancelled(), SnipFollow.Element, element.Bounds, element.Caption);
            }
            else
            {
                PaintSelecting("这里没有可识别的控件，换一处试试");
            }

            return;
        }

        if (_mode is SnipMode.Rectangle or SnipMode.Long && _down is Point anchor)
        {
            _down = null;
            var rect = ScreenRects.Normalize(anchor, screen);
            if (rect.Width < 8 || rect.Height < 8)
            {
                PaintSelecting("再大一点");
                return;
            }

            if (_mode == SnipMode.Long)
            {
                Finish(CaptureOutcome.Cancelled(), SnipFollow.Long, rect);
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

    private void StartDraw(Point image)
    {
        _ink = _tool switch
        {
            AnnoTool.Pen or AnnoTool.Highlight or AnnoTool.Mosaic => new AnnoItem(_tool, new[] { image.X }, new[] { image.Y }),
            _ => AnnoItem.FromDrag(_tool, image, image),
        };
        _ink.ColorArgb = _color;
        _ink.Width = _weight * Scale;
        _layer!.Draft = _ink;
    }

    private void ExtendDraw(Point image)
    {
        var last = new Point(_ink!.Xs[^1], _ink.Ys[^1]);
        if (ScreenRects.DistanceSquared(last, image) < 4)
        {
            return;
        }

        if (_ink.IsFreehand)
        {
            _ink.Append(image.X, image.Y);
            // Repaint only the segment just laid down, not the whole stroke.
            var segment = Rectangle.Union(
                new Rectangle(last.X, last.Y, 1, 1),
                new Rectangle(image.X, image.Y, 1, 1));
            var pad = (int)Math.Ceiling(Math.Max(_ink.Width * (_tool == AnnoTool.Highlight ? 3.4f : 1f), 6f) / 2f) + 3;
            _layer!.Redraw(Rectangle.Inflate(segment, pad, pad));
            return;
        }

        var before = _ink.InkBounds;
        _ink.SetEnd(image.X, image.Y);
        _layer!.Redraw(Rectangle.Union(before, _ink.InkBounds));
    }

    private void EndDraw()
    {
        if (_ink == null)
        {
            return;
        }

        var item = _ink;
        _ink = null;
        if (item.IsDegenerate)
        {
            _layer!.Draft = null;
            return;
        }

        _layer!.CommitDraft(item);
    }

    private void StartSelect(Point image)
    {
        var grip = (int)Math.Ceiling(6 * Scale);
        if (_selected != null)
        {
            var handle = _selected.HandleAt(image, grip);
            if (handle != AnnoHandle.None)
            {
                BeginDrag(_selected, handle, image);
                return;
            }
        }

        var hit = _layer!.HitTest(image, grip);
        _layer.Selected = hit;
        _selected = hit;
        if (hit == null)
        {
            _dragHandle = AnnoHandle.None;
            return;
        }

        BeginDrag(hit, AnnoHandle.Body, image);
    }

    private void BeginDrag(AnnoItem item, AnnoHandle handle, Point image)
    {
        _dragHandle = handle;
        _dragOrigin = image;
        _dragBounds = item.Bounds;
        _dragXs = (int[])item.Xs.Clone();
        _dragYs = (int[])item.Ys.Clone();
        // The dashed frame sits outside the ink, so the old frame needs clearing too.
        _layer?.Redraw(Rectangle.Inflate(_dragBounds, 24, 24));
    }

    private void MoveSelect(Point image)
    {
        if (_dragHandle == AnnoHandle.None || _selected == null)
        {
            return;
        }

        var dx = image.X - _dragOrigin.X;
        var dy = image.Y - _dragOrigin.Y;
        var before = Rectangle.Inflate(_selected.Bounds, 24, 24);

        // Rebuild from the drag-start snapshot: repeated relative edits on a freehand path
        // accumulate rounding error and the stroke slowly drifts away from the pointer.
        _selected.SetPoints(_dragXs, _dragYs);
        if (_dragHandle == AnnoHandle.Body)
        {
            _selected.Move(dx, dy);
        }
        else
        {
            _selected.DragHandle(_dragHandle, new Point(dx, dy));
        }

        _layer?.Redraw(Rectangle.Union(before, Rectangle.Inflate(_selected.Bounds, 24, 24)));
    }

    private void EndSelect()
    {
        if (_dragHandle == AnnoHandle.None)
        {
            return;
        }

        _dragHandle = AnnoHandle.None;
        _layer?.Redraw(_selected == null ? null : Rectangle.Inflate(_selected.Bounds, 24, 24));
    }

    private void Undo()
    {
        if (_ink != null)
        {
            _ink = null;
            _layer!.Draft = null;
            return;
        }

        if (_layer != null && _layer.Selected != null)
        {
            _layer.Remove(_layer.Selected);
            _selected = null;
            return;
        }

        _layer?.RemoveLast();
    }

    private void Confirm()
    {
        if (!_editing || _layer == null)
        {
            return;
        }

        EndDraw();
        using var bitmap = new Bitmap(_selection.Width, _selection.Height, PixelFormat.Format32bppPArgb);
        using (var g = Graphics.FromImage(bitmap))
        {
            _layer.RenderTo(g);
        }

        var saved = CaptureSaver.SavePng(bitmap, _kind, _settings);
        var clip = CaptureSaver.ClipboardPhrase(_settings, saved.CopiedToClipboard);
        Finish(CaptureOutcome.Saved(saved.Path, "已保存。" + clip), SnipFollow.None, null);
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

        if (_mode == SnipMode.Element && _element != null)
        {
            ShowElement(_cursor);
            return;
        }

        _overlay?.ShowDim(_work, hint);
    }

    private void ShowElement(Point screen) => ShowSnapshot(_element?.Update(screen));

    /// <summary>Timer-driven element probe. Repaints only when the hit element changes.</summary>
    private void PumpElement()
    {
        if (_finished || _editing || _mode != SnipMode.Element || _element == null)
        {
            return;
        }

        var before = _element.Current;
        var next = _element.Update(_elementPoint);
        if (ReferenceEquals(before, next))
        {
            return;
        }

        ShowSnapshot(next);
    }

    private void ShowSnapshot(ElementSnapshot? snapshot)
    {
        if (snapshot is { } found)
        {
            var hole = Rectangle.Intersect(found.Bounds, NativeMethods.VirtualScreen());
            if (hole.Width > 1 && hole.Height > 1)
            {
                var hint = string.IsNullOrWhiteSpace(found.Caption)
                    ? "单击截取 · 滚轮换层"
                    : found.Caption + " · 单击截取";
                _overlay?.ShowHole(_work, hole, hint);
                return;
            }
        }

        _overlay?.ShowDim(_work, "这里没有可识别的控件，换一处试试");
    }

    private void OnWheel(int delta)
    {
        if (_finished || _editing || _mode != SnipMode.Element || _element == null)
        {
            return;
        }

        ShowSnapshot(_element.Cycle(delta));
    }

    private void OnEscape()
    {
        if (_finished)
        {
            return;
        }

        // Esc closes an open swatch popup before it gives up on the whole capture.
        if (_anno is { AnyPopupVisible: true } bar)
        {
            bar.CloseStylePopups();
            return;
        }

        Finish(CaptureOutcome.Cancelled(), SnipFollow.None, null);
    }

    private static string HintFor(SnipMode mode) => mode switch
    {
        SnipMode.Rectangle => "拖出矩形",
        SnipMode.Window => "指向一个窗口",
        SnipMode.Freeform => "按住绘制",
        SnipMode.Long => "拖出要拼接的区域",
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
        // Style popups are separate windows, so a click on them must also reach them
        // instead of being eaten by the capture hook.
        return Contains(_toolbar, screen) ||
               Contains(_anno, screen) ||
               (_anno?.PopupContains(screen) ?? false);
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

    private static Point CursorOrZero()
    {
        if (NativeMethods.GetCursorPos(out var point))
        {
            return new Point(point.X, point.Y);
        }

        return Point.Empty;
    }

    private void Finish(CaptureOutcome outcome, SnipFollow follow, Rectangle? preset, string? note = null)
    {
        if (_finished)
        {
            return;
        }

        _finished = true;
        _done.TrySetResult(new SnipResult(outcome, follow, preset, note));
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
        _elementPump.Stop();
        _elementPump.Dispose();
        _element?.Dispose();
        _element = null;
        if (_layer is { IsDisposed: false })
        {
            _layer.Dispose();
        }

        _layer = null;
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
