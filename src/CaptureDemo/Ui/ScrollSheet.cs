using System.Drawing.Drawing2D;

namespace CaptureDemo.Ui;

/// <summary>
/// Fixed-height viewport for a taller settings sheet.
/// <para>
/// Content lives on an inner panel that is translated as a whole, so cards are never
/// re-laid out while scrolling — one <c>SetWindowPos</c> plus a thumb repaint per frame.
/// The native scrollbar is avoided on purpose: 17 px of system grey next to rounded
/// cards looks bolted on, and it cannot be themed.
/// </para>
/// </summary>
internal sealed class ScrollSheet : Control
{
    private readonly Panel _sheet;
    private readonly ThumbBar _bar;
    private readonly System.Windows.Forms.Timer _slide;
    private readonly WheelRouter _router;

    private int _contentHeight;
    private double _offset;
    private double _target;

    public ScrollSheet()
    {
        SetStyle(
            ControlStyles.UserPaint |
            ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.ResizeRedraw,
            true);
        BackColor = Theme.AppBack;

        _sheet = new BufferedPanel { BackColor = Theme.AppBack };
        _sheet.ControlAdded += (_, e) => Watch(e.Control);
        Controls.Add(_sheet);

        _bar = new ThumbBar(this);
        Controls.Add(_bar);
        // Controls.Add appends to the *bottom* of the Z-order, and the sheet covers the
        // whole viewport, so without this the thumb would be painted and then hidden.
        _bar.BringToFront();

        _slide = new System.Windows.Forms.Timer { Interval = 15 };
        _slide.Tick += (_, _) => Animate();

        _router = new WheelRouter(this);
    }

    /// <summary>Host for cards; children keep their own coordinates, the panel moves instead.</summary>
    public Panel Sheet => _sheet;

    /// <summary>True when the sheet is taller than the viewport.</summary>
    public bool CanScroll => MaxOffset > 0;

    private int MaxOffset => Math.Max(0, _contentHeight - ClientSize.Height);

    private int WheelStep => S(54);

    private int S(int px) => Math.Max(1, (int)Math.Round(px * (DeviceDpi / 96f)));

    /// <summary>Declares how tall the content is; the viewport itself never resizes.</summary>
    public void SetContentHeight(int height)
    {
        _contentHeight = Math.Max(0, height);
        LayoutSheet();
        ClampOffset();
        UpdateBar();
    }

    /// <summary>Jumps (still animated) so a specific row is on screen — used by Tab focus.</summary>
    public void EnsureVisible(Control target)
    {
        if (!target.IsHandleCreated || target.IsDisposed)
        {
            return;
        }

        var top = 0;
        for (var p = (Control?)target; p != null && p != _sheet; p = p.Parent)
        {
            top += p.Top;
        }

        var bottom = top + target.Height;
        var view = ClientSize.Height;
        if (top < _offset)
        {
            SetTarget(top - S(14));
        }
        else if (bottom > _offset + view)
        {
            SetTarget(bottom - view + S(14));
        }
    }

    public void ScrollToEnd() => SetTarget(MaxOffset, animate: false);

    internal string Describe() =>
        $"viewport={ClientSize.Height} content={_contentHeight} offset={_offset:F0} max={MaxOffset} " +
        $"bar={_bar.Visible}@{_bar.Bounds} thumb={ThumbRect(_bar)}";

    /// <summary>Instant position used while dragging the thumb: no easing, it must track the pointer.</summary>
    internal void SnapTo(double value)
    {
        var clamped = Math.Clamp(value, 0, MaxOffset);
        _offset = clamped;
        _target = clamped;
        _slide.Stop();
        Apply();
    }

    internal void ScrollByStep(double steps) => SetTarget(_target + (steps * WheelStep));

    /// <summary>Maps the thumb's top edge back to a scroll offset.</summary>
    internal Rectangle ThumbRect(Control bar)
    {
        var pad = S(2);
        var track = Math.Max(1, bar.Height - (pad * 2));
        var view = Math.Max(1, ClientSize.Height);
        var content = Math.Max(view, _contentHeight);
        var height = Math.Max(S(28), (int)Math.Round(track * (view / (double)content)));
        height = Math.Min(track, height);
        var travel = track - height;
        var max = MaxOffset;
        var ratio = max <= 0 ? 0d : Math.Clamp(_offset / max, 0d, 1d);
        var top = pad + (int)Math.Round(travel * ratio);
        var width = Math.Max(S(6), bar.Width - S(6));
        return new Rectangle((bar.Width - width) / 2, top, width, height);
    }

    internal void DragThumbTo(Control bar, int thumbTop)
    {
        var pad = S(2);
        var track = Math.Max(1, bar.Height - (pad * 2));
        var thumb = ThumbRect(bar);
        var travel = track - thumb.Height;
        if (travel <= 0 || MaxOffset <= 0)
        {
            return;
        }

        var ratio = Math.Clamp((thumbTop - pad) / (double)travel, 0d, 1d);
        SnapTo(MaxOffset * ratio);
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        LayoutSheet();
        _bar.SetBounds(Width - S(19), S(10), S(14), Math.Max(1, Height - S(20)));
        ClampOffset();
        UpdateBar();
    }

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        ScrollByStep(-e.Delta / 120d);
        base.OnMouseWheel(e);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        // The sheet covers the viewport, so only the strip it does not reach needs paint.
        e.Graphics.Clear(Theme.AppBack);
        base.OnPaint(e);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _router.Detach();
            _slide.Stop();
            _slide.Dispose();
        }

        base.Dispose(disposing);
    }

    private void LayoutSheet()
    {
        var height = Math.Max(ClientSize.Height, _contentHeight);
        _sheet.SetBounds(0, -((int)Math.Round(_offset)), Math.Max(1, ClientSize.Width), Math.Max(1, height));
    }

    private void ClampOffset()
    {
        var max = MaxOffset;
        if (_offset > max)
        {
            _offset = max;
        }

        if (_target > max)
        {
            _target = max;
        }
    }

    private void UpdateBar()
    {
        var needed = CanScroll;
        if (_bar.Visible != needed)
        {
            _bar.Visible = needed;
        }

        if (needed)
        {
            _bar.Invalidate();
        }
    }

    private void SetTarget(double value, bool animate = true)
    {
        var clamped = Math.Clamp(value, 0, MaxOffset);
        _target = clamped;
        if (!animate || Math.Abs(_target - _offset) < 0.5)
        {
            _offset = clamped;
            _slide.Stop();
            Apply();
            return;
        }

        if (!_slide.Enabled)
        {
            _slide.Start();
        }
    }

    private void Animate()
    {
        var delta = _target - _offset;
        if (Math.Abs(delta) < 0.7)
        {
            _offset = _target;
            _slide.Stop();
        }
        else
        {
            _offset += delta * 0.34;
        }

        Apply();
    }

    private void Apply()
    {
        var top = -((int)Math.Round(_offset));
        if (_sheet.Top != top)
        {
            _sheet.Top = top;
        }

        if (_bar.Visible)
        {
            _bar.Invalidate();
        }
    }

    /// <summary>Keyboard users tab into off-screen rows; follow them instead of hiding the focus.</summary>
    private void Watch(Control? control)
    {
        if (control == null)
        {
            return;
        }

        control.Enter += (_, _) => EnsureVisible(control);
        control.ControlAdded += (_, e) => Watch(e.Control);
        foreach (Control child in control.Controls)
        {
            Watch(child);
        }
    }

    /// <summary>
    /// A single-row text box swallows WM_MOUSEWHEEL, so hovering a field stopped the page
    /// from scrolling. Intercept the message before it reaches the control under the cursor.
    /// </summary>
    private sealed class WheelRouter : IMessageFilter
    {
        private const int WmMouseWheel = 0x020A;

        private readonly ScrollSheet _owner;
        private bool _attached;

        public WheelRouter(ScrollSheet owner)
        {
            _owner = owner;
            Application.AddMessageFilter(this);
            _attached = true;
        }

        public void Detach()
        {
            if (!_attached)
            {
                return;
            }

            Application.RemoveMessageFilter(this);
            _attached = false;
        }

        public bool PreFilterMessage(ref Message m)
        {
            if (m.Msg != WmMouseWheel)
            {
                return false;
            }

            if (!_owner.IsHandleCreated || !_owner.Visible || !_owner.CanScroll)
            {
                return false;
            }

            var l = m.LParam.ToInt64();
            var screen = new Point((short)(l & 0xFFFF), (short)((l >> 16) & 0xFFFF));
            if (!_owner.RectangleToScreen(_owner.ClientRectangle).Contains(screen))
            {
                return false;
            }

            var delta = (short)((m.WParam.ToInt64() >> 16) & 0xFFFF);
            if (delta == 0)
            {
                return false;
            }

            _owner.ScrollByStep(-delta / 120d);
            return true;
        }
    }
}

/// <summary>Thin Fluent-style scrollbar: 6 px pill in a 12 px hit strip.</summary>
internal sealed class ThumbBar : Control
{
    private readonly ScrollSheet _owner;
    private bool _hover;
    private bool _dragging;
    private int _grab;

    public ThumbBar(ScrollSheet owner)
    {
        _owner = owner;
        SetStyle(
            ControlStyles.UserPaint |
            ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.ResizeRedraw,
            true);
        BackColor = Theme.AppBack;
        Visible = false;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(BackColor);
        var thumb = _owner.ThumbRect(this);
        if (thumb.Height <= 0)
        {
            return;
        }

        g.SmoothingMode = SmoothingMode.AntiAlias;
        // Light text on a dark surface reads louder, so the dark palette gets a lower base.
        var idle = Theme.IsDark ? 78 : 104;
        var alpha = _dragging ? idle + 62 : _hover ? idle + 30 : idle;
        UiKit.Fill(g, thumb, thumb.Width / 2f, UiKit.Alpha(Theme.Text, alpha));
    }

    protected override void OnMouseEnter(EventArgs e)
    {
        _hover = true;
        Invalidate();
        base.OnMouseEnter(e);
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        _hover = false;
        if (!_dragging)
        {
            Invalidate();
        }

        base.OnMouseLeave(e);
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button != MouseButtons.Left)
        {
            return;
        }

        var thumb = _owner.ThumbRect(this);
        if (thumb.Contains(e.Location))
        {
            _dragging = true;
            _grab = e.Y - thumb.Top;
            Capture = true;
        }
        else
        {
            // Clicking the track pages towards the pointer, the Windows convention.
            _owner.ScrollByStep(e.Y < thumb.Top ? -6 : 6);
        }

        Invalidate();
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (_dragging)
        {
            _owner.DragThumbTo(this, e.Y - _grab);
        }
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        if (!_dragging)
        {
            return;
        }

        _dragging = false;
        Capture = false;
        Invalidate();
    }
}
