using System.Drawing.Drawing2D;

namespace CaptureDemo.Ui;

internal sealed class ModeButton : Control
{
    private bool _hover;

    public ModeButton()
    {
        SetStyle(
            ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.UserPaint |
            ControlStyles.ResizeRedraw |
            ControlStyles.Selectable,
            true);
        TabStop = true;
        Cursor = Cursors.Hand;
        MinimumSize = new Size(0, 96);
        AccessibleRole = AccessibleRole.PushButton;
    }

    public string Title { get; set; } = "";

    public string Detail { get; set; } = "";

    public string Shortcut { get; set; } = "";

    public Color Accent { get; set; } = Theme.Accent;

    protected override void OnMouseEnter(EventArgs e)
    {
        _hover = true;
        Invalidate();
        base.OnMouseEnter(e);
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        _hover = false;
        Invalidate();
        base.OnMouseLeave(e);
    }

    protected override void OnGotFocus(EventArgs e)
    {
        Invalidate();
        base.OnGotFocus(e);
    }

    protected override void OnLostFocus(EventArgs e)
    {
        Invalidate();
        base.OnLostFocus(e);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.KeyCode is Keys.Enter or Keys.Space)
        {
            e.Handled = true;
            OnClick(EventArgs.Empty);
            return;
        }

        base.OnKeyDown(e);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var graphics = e.Graphics;
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        var bounds = new Rectangle(0, 0, Width - 1, Height - 1);
        using var background = new SolidBrush(_hover || Focused ? Theme.AccentSoft : Theme.Card);
        using var border = new Pen(_hover || Focused ? Accent : Theme.CardBorder);
        using var path = Round(bounds, 10);
        graphics.FillPath(background, path);
        graphics.DrawPath(border, path);
        graphics.SmoothingMode = SmoothingMode.None;
        using var bar = new SolidBrush(Accent);
        graphics.FillRectangle(bar, 0, 10, 4, Math.Max(1, Height - 20));

        TextRenderer.DrawText(
            graphics,
            Title,
            UiFonts.ButtonTitle,
            new Rectangle(20, 14, Math.Max(10, Width - 150), 30),
            Theme.Text,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine);
        TextRenderer.DrawText(
            graphics,
            Detail,
            UiFonts.Small,
            new Rectangle(20, 48, Math.Max(10, Width - 36), Math.Max(20, Height - 58)),
            Theme.Muted,
            TextFormatFlags.Left | TextFormatFlags.WordBreak | TextFormatFlags.Top);

        var chip = "Ctrl+Alt+" + Shortcut;
        var chipSize = TextRenderer.MeasureText(chip, UiFonts.Small);
        var chipRect = new Rectangle(Width - chipSize.Width - 28, 16, chipSize.Width + 12, chipSize.Height + 8);
        using var chipBack = new SolidBrush(Theme.AppBack);
        graphics.FillRectangle(chipBack, chipRect);
        TextRenderer.DrawText(
            graphics,
            chip,
            UiFonts.Small,
            chipRect,
            Accent,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
    }

    private static GraphicsPath Round(Rectangle bounds, int radius)
    {
        var path = new GraphicsPath();
        var diameter = Math.Max(2, radius * 2);
        path.AddArc(bounds.X, bounds.Y, diameter, diameter, 180, 90);
        path.AddArc(bounds.Right - diameter, bounds.Y, diameter, diameter, 270, 90);
        path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0, 90);
        path.AddArc(bounds.X, bounds.Bottom - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();
        return path;
    }
}
