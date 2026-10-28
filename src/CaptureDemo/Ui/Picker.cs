using System.Drawing.Drawing2D;
using System.Drawing.Text;
using CaptureDemo.Snip;

namespace CaptureDemo.Ui;

/// <summary>
/// Drop-down replacement for <see cref="ComboBox"/>. The native flat combo keeps its
/// own 3D arrow and never adopts the surrounding card style, which read as unfinished.
/// </summary>
internal sealed class Picker : Control
{
    private readonly List<string> _items = new();
    private ContextMenuStrip? _menu;
    private int _selected = -1;
    private bool _hover;
    private bool _pressed;

    public Picker()
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

    public event EventHandler? SelectedIndexChanged;

    public int SelectedIndex
    {
        get => _selected;
        set
        {
            var clamped = _items.Count == 0 ? -1 : Math.Clamp(value, 0, _items.Count - 1);
            if (clamped == _selected)
            {
                return;
            }

            _selected = clamped;
            Invalidate();
            SelectedIndexChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public string SelectedText => _selected >= 0 && _selected < _items.Count ? _items[_selected] : "";

    private float DpiScale => DeviceDpi / 96f;

    private int S(float px) => Math.Max(1, (int)Math.Round(px * DpiScale));

    public void SetItems(IEnumerable<string> items, int selected)
    {
        _items.Clear();
        _items.AddRange(items);
        _selected = _items.Count == 0 ? -1 : Math.Clamp(selected, 0, _items.Count - 1);
        Invalidate();
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
            Invalidate();
            Focus();
            Open();
        }

        base.OnMouseDown(e);
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        _pressed = false;
        Invalidate();
        base.OnMouseUp(e);
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
        if (e.KeyCode is Keys.Space or Keys.Enter or Keys.Down or Keys.F4)
        {
            e.Handled = true;
            Open();
            return;
        }

        if (e.KeyCode is Keys.Up or Keys.Left)
        {
            e.Handled = true;
            SelectedIndex--;
            return;
        }

        if (e.KeyCode is Keys.Right)
        {
            e.Handled = true;
            SelectedIndex++;
            return;
        }

        base.OnKeyDown(e);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _menu?.Dispose();
        }

        base.Dispose(disposing);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;

        var active = _hover || Focused;
        var bounds = new RectangleF(0.5f, 0.5f, Width - 1f, Height - 1f);
        var radius = S(8);
        var fill = _pressed ? UiKit.Mix(Theme.Field, Theme.Text, 0.07) : Theme.Field;
        var border = active ? UiKit.Mix(Theme.Accent, Theme.Field, 0.2) : Theme.CardBorder;
        UiKit.FillAndStroke(g, bounds, radius, fill, border, active ? 1.4f : 1f);

        var glyphSize = S(18);
        var glyphBox = new Rectangle(Width - S(30), (Height - glyphSize) / 2, glyphSize, glyphSize);
        UiKit.DrawGlyph(g, SnipIcons.Chevron, glyphBox, active ? Theme.Accent : Theme.Muted);

        var textRect = new RectangleF(S(12), 0, Math.Max(1, glyphBox.Left - S(16)), Height);
        using var brush = new SolidBrush(Theme.Text);
        using var format = new StringFormat(StringFormatFlags.NoWrap)
        {
            LineAlignment = StringAlignment.Center,
            Trimming = StringTrimming.EllipsisCharacter,
        };
        g.DrawString(SelectedText, Font, brush, textRect, format);
        g.TextRenderingHint = TextRenderingHint.SystemDefault;
    }

    private void Open()
    {
        if (_items.Count == 0)
        {
            return;
        }

        _menu?.Dispose();
        var itemWidth = Math.Max(S(60), Width - S(8));
        var itemHeight = S(30);
        _menu = new ContextMenuStrip
        {
            Renderer = new FluentMenuRenderer(),
            BackColor = Theme.Card,
            ForeColor = Theme.Text,
            Font = UiFonts.Body,
            ShowImageMargin = false,
            ShowCheckMargin = false,
            Padding = new Padding(S(4)),
        };

        for (var i = 0; i < _items.Count; i++)
        {
            var index = i;
            var current = i == _selected;
            var item = new ToolStripMenuItem(_items[i])
            {
                AutoSize = false,
                Size = new Size(itemWidth, itemHeight),
                ForeColor = current ? Theme.Accent : Theme.Text,
                Font = current ? UiFonts.BodyBold : UiFonts.Body,
                TextAlign = ContentAlignment.MiddleLeft,
                Padding = new Padding(S(10), 0, S(8), 0),
            };
            item.Click += (_, _) => SelectedIndex = index;
            _menu.Items.Add(item);
        }

        _menu.Closed += (_, _) =>
        {
            _pressed = false;
            Invalidate();
        };

        _menu.Show(this, new Point(-S(4), Height + S(4)));
    }
}
