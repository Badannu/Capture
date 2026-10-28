using System.Diagnostics;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using CaptureDemo.Core;

namespace CaptureDemo.Snip;

/// <summary>
/// The annotation layer. It covers exactly the selection (not the whole virtual desktop) and
/// keeps its own premultiplied frame buffer, so a pointer move only repaints the rectangle the
/// stroke actually touched. The old path re-composited the entire capture on every mouse event,
/// which is what made drawing feel sticky on large selections.
/// </summary>
internal sealed class AnnoLayerForm : Form
{
    private readonly LayeredSurface _surface = new();
    private readonly List<AnnoItem> _items = new();
    private readonly System.Windows.Forms.Timer _pump;
    private Bitmap? _background;
    private Bitmap? _frame;
    private AnnoItem? _draft;
    private AnnoItem? _selected;
    private Rectangle _pending;
    private long _lastPresent;
    private bool _presented;

    public AnnoLayerForm()
    {
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        TopMost = true;
        BackColor = Color.Black;
        AutoScaleMode = AutoScaleMode.None;
        DoubleBuffered = false;

        _pump = new System.Windows.Forms.Timer { Interval = 8 };
        _pump.Tick += (_, _) =>
        {
            if (!_pending.IsEmpty)
            {
                Present();
            }
        };
    }

    public IReadOnlyList<AnnoItem> Items => _items;

    /// <summary>
    /// Collapses bursts of pointer moves into one present per frame. Turned off by the
    /// dev benchmark, which wants to time a single frame rather than a coalesced run.
    /// </summary>
    public bool Coalesce { get; set; } = true;

    /// <summary>Handle size and dash pattern are expressed in screen pixels, so DPI matters.</summary>
    public float ScreenScale => Math.Max(1f, DeviceDpi / 96f);

    public AnnoItem? Selected
    {
        get => _selected;
        set
        {
            if (ReferenceEquals(_selected, value))
            {
                return;
            }

            var dirty = _selected == null ? Rectangle.Empty : Rectangle.Inflate(_selected.Bounds, 24, 24);
            _selected = value;
            if (value != null)
            {
                dirty = dirty.IsEmpty ? Rectangle.Inflate(value.Bounds, 24, 24) : Rectangle.Union(dirty, Rectangle.Inflate(value.Bounds, 24, 24));
            }

            Redraw(dirty);
        }
    }

    public AnnoItem? Draft
    {
        get => _draft;
        set
        {
            var dirty = _draft == null ? Rectangle.Empty : _draft.InkBounds;
            _draft = value;
            if (value != null)
            {
                dirty = dirty.IsEmpty ? value.InkBounds : Rectangle.Union(dirty, value.InkBounds);
            }

            Redraw(dirty);
        }
    }

    /// <summary>Attaches the frozen capture and sizes the window to the selection.</summary>
    public void Attach(Bitmap background, Rectangle screenBounds)
    {
        _items.Clear();
        _draft = null;
        _selected = null;
        _pending = Rectangle.Empty;
        Bounds = screenBounds;
        _background?.Dispose();
        _background = background;
        _frame?.Dispose();
        _frame = new Bitmap(Math.Max(1, background.Width), Math.Max(1, background.Height), PixelFormat.Format32bppPArgb);
        using (var g = Graphics.FromImage(_frame))
        {
            g.CompositingMode = CompositingMode.SourceCopy;
            g.DrawImage(background, new Rectangle(0, 0, _frame.Width, _frame.Height));
        }

        if (!Visible)
        {
            Show();
        }

        Present();
    }

    public void Add(AnnoItem item)
    {
        _items.Add(item);
        Redraw(item.InkBounds);
    }

    /// <summary>
    /// Promotes the in-progress item to a committed one in a single repaint, so a
    /// translucent highlight is not painted twice into the same frame.
    /// </summary>
    public void CommitDraft(AnnoItem item)
    {
        var dirty = _draft == null ? item.InkBounds : Rectangle.Union(_draft.InkBounds, item.InkBounds);
        _draft = null;
        _items.Add(item);
        Redraw(dirty);
    }

    /// <summary>Drops the newest item and returns the area that needs repainting.</summary>
    public AnnoItem? RemoveLast()
    {
        if (_items.Count == 0)
        {
            return null;
        }

        var item = _items[^1];
        _items.RemoveAt(_items.Count - 1);
        if (ReferenceEquals(_selected, item))
        {
            _selected = null;
        }

        Redraw(Rectangle.Inflate(item.Bounds, 24, 24));
        return item;
    }

    public bool Remove(AnnoItem item)
    {
        if (!_items.Remove(item))
        {
            return false;
        }

        if (ReferenceEquals(_selected, item))
        {
            _selected = null;
        }

        Redraw(Rectangle.Inflate(item.Bounds, 24, 24));
        return true;
    }

    public void ClearItems()
    {
        if (_items.Count == 0 && _draft == null && _selected == null)
        {
            return;
        }

        _items.Clear();
        _draft = null;
        _selected = null;
        Redraw(null);
    }

    /// <summary>Topmost item under the point, honouring stack order.</summary>
    public AnnoItem? HitTest(Point point, int tolerance)
    {
        for (var i = _items.Count - 1; i >= 0; i--)
        {
            if (_items[i].HitTest(point, tolerance))
            {
                return _items[i];
            }
        }

        return null;
    }

    /// <summary>Queues a repaint. <paramref name="dirty"/> is in image pixels; null means everything.</summary>
    public void Redraw(Rectangle? dirty)
    {
        if (_frame == null)
        {
            return;
        }

        var clip = new Rectangle(0, 0, _frame.Width, _frame.Height);
        var region = dirty is Rectangle r ? Rectangle.Intersect(r, clip) : clip;
        if (region.Width <= 0 || region.Height <= 0)
        {
            return;
        }

        _pending = _pending.IsEmpty ? region : Rectangle.Union(_pending, region);

        // Coalesce bursts of pointer moves into one present per frame; the timer above makes
        // sure a trailing dirty region still reaches the screen after the pointer stops.
        var now = Stopwatch.GetTimestamp();
        var elapsed = (now - _lastPresent) * 1000.0 / Stopwatch.Frequency;
        if (Coalesce && _presented && elapsed < 4.0)
        {
            return;
        }

        Present();
    }

    /// <summary>Paints every item into an export bitmap, matching exactly what the overlay shows.</summary>
    public void RenderTo(Graphics g)
    {
        if (_background == null)
        {
            return;
        }

        g.CompositingMode = CompositingMode.SourceCopy;
        g.InterpolationMode = InterpolationMode.NearestNeighbor;
        g.PixelOffsetMode = PixelOffsetMode.Half;
        g.DrawImage(_background, new Rectangle(0, 0, _background.Width, _background.Height));
        g.CompositingMode = CompositingMode.SourceOver;
        foreach (var item in _items)
        {
            AnnoPainter.Draw(g, item, _background);
        }
    }

    protected override bool ShowWithoutActivation => true;

    protected override CreateParams CreateParams
    {
        get
        {
            const int WS_EX_LAYERED = 0x00080000;
            const int WS_EX_TRANSPARENT = 0x20;
            const int WS_EX_TOOLWINDOW = 0x80;
            const int WS_EX_NOACTIVATE = 0x08000000;
            var cp = base.CreateParams;
            cp.ExStyle |= WS_EX_LAYERED | WS_EX_TRANSPARENT | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE;
            return cp;
        }
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        // Normally the layer must stay out of screen captures; the dev proof shot needs it visible.
        if (!CapturableForDev)
        {
            WindowAffinity.TryExcludeFromCapture(Handle);
        }
    }

    /// <summary>Dev-only: allow a screen grab of this window so the layer can be verified end to end.</summary>
    public static bool CapturableForDev { get; set; }

    protected override void OnPaint(PaintEventArgs e)
    {
    }

    protected override void OnPaintBackground(PaintEventArgs e)
    {
    }

    protected override void OnVisibleChanged(EventArgs e)
    {
        base.OnVisibleChanged(e);
        // The pump only exists to flush a trailing dirty region after the pointer stops.
        if (Visible)
        {
            _pump.Start();
        }
        else
        {
            _pump.Stop();
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _pump.Stop();
            _pump.Dispose();
            _surface.Dispose();
            _background?.Dispose();
            _background = null;
            _frame?.Dispose();
            _frame = null;
        }

        base.Dispose(disposing);
    }

    private void Present()
    {
        if (_frame == null || _background == null)
        {
            return;
        }

        var region = _pending.IsEmpty ? new Rectangle(0, 0, _frame.Width, _frame.Height) : _pending;
        _pending = Rectangle.Empty;
        PaintRegion(region);
        _surface.Present(Handle, Bounds.Location, _frame, region);
        _lastPresent = Stopwatch.GetTimestamp();
        _presented = true;
    }

    private void PaintRegion(Rectangle region)
    {
        using var g = Graphics.FromImage(_frame!);
        g.SetClip(region);
        g.CompositingMode = CompositingMode.SourceCopy;
        g.InterpolationMode = InterpolationMode.NearestNeighbor;
        g.PixelOffsetMode = PixelOffsetMode.Half;
        g.DrawImage(_background!, region, region, GraphicsUnit.Pixel);

        g.CompositingMode = CompositingMode.SourceOver;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        var source = _background;
        foreach (var item in _items)
        {
            if (item.InkBounds.IntersectsWith(region))
            {
                AnnoPainter.Draw(g, item, source);
            }
        }

        if (_draft != null && _draft.InkBounds.IntersectsWith(region))
        {
            AnnoPainter.Draw(g, _draft, source);
        }

        if (_selected != null && Rectangle.Inflate(_selected.Bounds, 24, 24).IntersectsWith(region))
        {
            AnnoPainter.DrawSelection(g, _selected, ScreenScale);
        }
    }
}
