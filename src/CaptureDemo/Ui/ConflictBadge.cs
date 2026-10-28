using System.Drawing.Drawing2D;
using CaptureDemo.Core;

namespace CaptureDemo.Ui;

/// <summary>
/// The red circled exclamation that sits after a hotkey box. Kept as its own control so
/// the row can reserve the slot permanently: a badge that appears and disappears would
/// resize the text box under the cursor and shift everything the user is reading.
/// </summary>
internal sealed class ConflictBadge : Control
{
    private readonly float _scale;
    private bool _hot;
    private HotkeyConflict? _conflict;

    public ConflictBadge(float scale)
    {
        _scale = scale <= 0f ? 1f : scale;
        SetStyle(
            ControlStyles.UserPaint |
            ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.ResizeRedraw,
            true);
        Size = new Size(S(18), S(18));
        BackColor = Theme.Card;
        Cursor = Cursors.Default;
        Visible = false;
    }

    /// <summary>Null clears the badge and makes the slot click-through again.</summary>
    public void SetConflict(HotkeyConflict? conflict)
    {
        var wasVisible = Visible;
        _conflict = conflict is { IsConflict: true } ? conflict : null;
        Visible = _conflict != null;
        Cursor = _conflict == null ? Cursors.Default : Cursors.Hand;
        if (_conflict == null)
        {
            _hot = false;
        }

        if (Visible != wasVisible || Visible)
        {
            Invalidate();
        }
    }

    public HotkeyConflict? Conflict => _conflict;

    /// <summary>Where a popup should point: this control's bottom-right, in screen pixels.</summary>
    public Point AnchorScreen => PointToScreen(new Point(Width, Height));

    protected override void OnMouseEnter(EventArgs e)
    {
        base.OnMouseEnter(e);
        if (_conflict != null)
        {
            _hot = true;
            Invalidate();
        }
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        if (_hot)
        {
            _hot = false;
            Invalidate();
        }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        if (_conflict == null)
        {
            using var back = new SolidBrush(BackColor);
            e.Graphics.FillRectangle(back, ClientRectangle);
            return;
        }

        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;

        using (var back = new SolidBrush(BackColor))
        {
            g.FillRectangle(back, ClientRectangle);
        }

        var side = Math.Min(Width, Height);
        var left = (Width - side) / 2f;
        var top = (Height - side) / 2f;
        var stroke = Math.Max(1.2f, 1.5f * _scale);
        var ring = new RectangleF(left + (stroke / 2f), top + (stroke / 2f), side - stroke, side - stroke);

        if (_hot)
        {
            var halo = RectangleF.Inflate(ring, stroke * 1.6f, stroke * 1.6f);
            using var path = new GraphicsPath();
            path.AddEllipse(halo);
            using var brush = new SolidBrush(UiKit.Alpha(Theme.Danger, 34));
            g.FillPath(brush, path);
        }

        using (var pen = new Pen(Theme.Danger, stroke))
        {
            g.DrawEllipse(pen, ring);
        }

        // Drawn, not typed: the "!" of a 9 pt font is one pixel wide at this size and
        // lands off-centre against the ring.
        var cx = left + (side / 2f);
        var barTop = top + (side * 0.30f);
        var barBottom = top + (side * 0.55f);
        var dot = Math.Max(1.6f, side * 0.11f);
        using (var pen = new Pen(Theme.Danger, stroke * 1.15f)
               {
                   StartCap = LineCap.Round,
                   EndCap = LineCap.Round,
               })
        {
            g.DrawLine(pen, cx, barTop, cx, barBottom);
        }

        using (var brush = new SolidBrush(Theme.Danger))
        {
            g.FillEllipse(brush, cx - (dot / 2f), top + (side * 0.68f), dot, dot);
        }
    }

    private int S(int px) => Math.Max(1, (int)Math.Round(px * _scale));
}
