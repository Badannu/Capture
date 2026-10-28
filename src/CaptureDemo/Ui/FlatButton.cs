namespace CaptureDemo.Ui;

internal enum ButtonVariant
{
    Primary,
    Secondary,
    Ghost,
    Danger,
}

/// <summary>
/// Fully owner-drawn button. Native <see cref="Button"/> with FlatStyle.Flat has no
/// hover state and paints its own hard-edged rectangle, which is what made the old
/// shell look unfinished.
/// </summary>
internal sealed class FlatButton : Control
{
    private bool _hover;
    private bool _pressed;

    public FlatButton()
    {
        SetStyle(
            ControlStyles.UserPaint |
            ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.ResizeRedraw |
            ControlStyles.SupportsTransparentBackColor,
            true);
        BackColor = Color.Transparent;
        Font = UiFonts.Body;
        Cursor = Cursors.Hand;
        TabStop = true;
    }

    public ButtonVariant Variant { get; set; } = ButtonVariant.Secondary;

    public Action<Graphics, Rectangle, Color>? Glyph { get; set; }

    public int GlyphSize { get; set; } = 18;

    public int CornerRadius { get; set; } = 8;

    public int GlyphGap { get; set; } = 8;

    public bool Bold { get; set; }

    public event EventHandler? Clicked;

    public void PerformClick()
    {
        if (!Enabled)
        {
            return;
        }

        Clicked?.Invoke(this, EventArgs.Empty);
        OnClick(EventArgs.Empty);
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
        _pressed = false;
        Invalidate();
        base.OnMouseLeave(e);
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Left)
        {
            _pressed = true;
            Focus();
            Invalidate();
        }

        base.OnMouseDown(e);
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        if (_pressed && e.Button == MouseButtons.Left)
        {
            _pressed = false;
            Invalidate();
            if (ClientRectangle.Contains(e.Location))
            {
                PerformClick();
            }
        }

        base.OnMouseUp(e);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.KeyCode is Keys.Space or Keys.Enter)
        {
            e.Handled = true;
            PerformClick();
            return;
        }

        base.OnKeyDown(e);
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

    protected override void OnEnabledChanged(EventArgs e)
    {
        Invalidate();
        base.OnEnabledChanged(e);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

        var bounds = new RectangleF(0, 0, Width, Height);
        var radius = CornerRadius;
        var (fill, border, ink) = Palette();

        if (fill.A > 0)
        {
            UiKit.Fill(g, bounds, radius, fill);
        }

        if (border.A > 0)
        {
            UiKit.Stroke(g, bounds, radius, border);
        }

        if (Focused && ShowFocusCues)
        {
            UiKit.Stroke(g, RectangleF.Inflate(bounds, -1.5f, -1.5f), radius - 1f, UiKit.Alpha(Theme.Accent, 170));
        }

        var font = Bold ? UiFonts.ButtonTitle : Font;
        var hasText = !string.IsNullOrEmpty(Text);
        var iconSide = Glyph != null ? GlyphSize : 0;
        var textWidth = hasText ? TextRenderer.MeasureText(Text, font).Width : 0;
        var gap = Glyph != null && hasText ? GlyphGap : 0;
        var total = iconSide + gap + textWidth;
        var startX = (Width - total) / 2;
        var middle = Height / 2;

        if (Glyph != null)
        {
            var box = new Rectangle(startX, middle - (iconSide / 2), iconSide, iconSide);
            UiKit.DrawGlyph(g, Glyph, box, ink);
            startX += iconSide + gap;
        }

        if (hasText)
        {
            var textBox = new Rectangle(startX, 0, Math.Max(1, Width - startX - 4), Height);
            TextRenderer.DrawText(
                g,
                Text,
                font,
                textBox,
                ink,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPadding);
        }
    }

    private (Color Fill, Color Border, Color Ink) Palette()
    {
        if (!Enabled)
        {
            return Variant switch
            {
                ButtonVariant.Primary => (UiKit.Alpha(Theme.Accent, 70), Color.Transparent, UiKit.Alpha(Theme.Muted, 170)),
                ButtonVariant.Ghost => (Color.Transparent, Color.Transparent, UiKit.Alpha(Theme.Muted, 140)),
                _ => (Theme.Card, UiKit.Alpha(Theme.CardBorder, 150), UiKit.Alpha(Theme.Muted, 150)),
            };
        }

        return Variant switch
        {
            ButtonVariant.Primary => (
                _pressed
                    ? UiKit.Mix(Theme.Accent, Color.Black, 0.14)
                    : _hover
                        ? UiKit.Mix(Theme.Accent, Color.White, 0.12)
                        : Theme.Accent,
                Color.Transparent,
                Theme.AccentText),
            ButtonVariant.Ghost => (
                _pressed ? Theme.Pressed : _hover ? Theme.Hover : Color.Transparent,
                Color.Transparent,
                _hover ? Theme.Text : Theme.Muted),
            ButtonVariant.Danger => (
                _pressed ? UiKit.Alpha(Theme.Danger, 26) : _hover ? UiKit.Alpha(Theme.Danger, 20) : Color.Transparent,
                Color.Transparent,
                Theme.Danger),
            _ => (
                _pressed ? Theme.Pressed : _hover ? Theme.Hover : Theme.Card,
                _hover ? UiKit.Mix(Theme.CardBorder, Theme.Muted, 0.35) : Theme.CardBorder,
                Theme.Text),
        };
    }

    protected override void OnTextChanged(EventArgs e)
    {
        Invalidate();
        base.OnTextChanged(e);
    }

    protected override void OnFontChanged(EventArgs e)
    {
        Invalidate();
        base.OnFontChanged(e);
    }
}
