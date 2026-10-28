using System.Drawing.Drawing2D;

namespace CaptureDemo.Ui;

/// <summary>Rounded surface that groups related rows, the Win11 "card" idiom.</summary>
internal sealed class CardPanel : Panel
{
    public CardPanel()
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
    }

    public int Radius { get; set; } = 10;

    public bool ShowBorder { get; set; } = true;

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        var bounds = new RectangleF(0.5f, 0.5f, Width - 1f, Height - 1f);
        using (var path = UiKit.Rounded(bounds, Radius))
        using (var brush = new SolidBrush(Theme.Card))
        {
            g.FillPath(brush, path);
        }

        if (ShowBorder)
        {
            UiKit.Stroke(g, bounds, Radius, Theme.CardBorder);
        }

        base.OnPaint(e);
    }
}

/// <summary>Scroll container that does not flicker while cards are repositioned.</summary>
internal sealed class BufferedPanel : Panel
{
    public BufferedPanel()
    {
        SetStyle(
            ControlStyles.UserPaint |
            ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.ResizeRedraw,
            true);
    }
}

/// <summary>Win11 style switch. Animates the knob instead of snapping.</summary>
internal sealed class ToggleSwitch : Control
{
    private readonly System.Windows.Forms.Timer _timer;
    private float _position;
    private bool _checked;

    public ToggleSwitch()
    {
        SetStyle(
            ControlStyles.UserPaint |
            ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.ResizeRedraw |
            ControlStyles.SupportsTransparentBackColor,
            true);
        BackColor = Color.Transparent;
        Cursor = Cursors.Hand;
        Size = new Size(44, 24);
        _timer = new System.Windows.Forms.Timer { Interval = 15 };
        _timer.Tick += OnTick;
    }

    public event EventHandler? CheckedChanged;

    public bool Checked
    {
        get => _checked;
        set
        {
            if (_checked == value)
            {
                return;
            }

            _checked = value;
            _timer.Start();
            CheckedChanged?.Invoke(this, EventArgs.Empty);
            Invalidate();
        }
    }

    public void SetCheckedSilently(bool value)
    {
        _checked = value;
        _position = value ? 1f : 0f;
        _timer.Stop();
        Invalidate();
    }

    protected override void OnClick(EventArgs e)
    {
        Checked = !Checked;
        base.OnClick(e);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _timer.Stop();
            _timer.Dispose();
        }

        base.Dispose(disposing);
    }

    private void OnTick(object? sender, EventArgs e)
    {
        var target = _checked ? 1f : 0f;
        var delta = target - _position;
        if (Math.Abs(delta) < 0.02f)
        {
            _position = target;
            _timer.Stop();
        }
        else
        {
            _position += delta * 0.35f;
        }

        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        var height = Math.Min(Height, 24);
        var width = Math.Min(Width, 44);
        var track = new RectangleF((Width - width) / 2f, (Height - height) / 2f, width, height);
        var radius = height / 2f;
        var eased = _position * _position * (3f - (2f * _position));

        var off = Theme.IsDark ? Color.FromArgb(84, 86, 92) : Color.FromArgb(206, 208, 214);
        var fill = UiKit.Mix(off, Theme.Accent, eased);
        if (!Enabled)
        {
            fill = UiKit.Mix(off, Theme.AppBack, 0.45);
        }

        using (var path = UiKit.Rounded(track, radius))
        using (var brush = new SolidBrush(fill))
        {
            g.FillPath(brush, path);
        }

        var padding = 2.5f;
        var knob = (height - (padding * 2)) * (1f - (0.12f * Math.Abs((2f * eased) - 1f)));
        var travel = width - (padding * 2) - knob;
        var knobX = track.Left + padding + (travel * eased);
        var knobRect = new RectangleF(knobX, track.Top + padding, knob, knob);
        var knobFill = Theme.IsDark ? Color.FromArgb(32, 33, 37) : Color.White;
        using (var brush = new SolidBrush(knobFill))
        {
            g.FillEllipse(brush, knobRect);
        }
    }
}

/// <summary>Rounded input: the border belongs to the panel, the text box stays borderless.</summary>
internal sealed class FieldBox : Panel
{
    private readonly TextBox _box = new();

    public FieldBox()
    {
        SetStyle(
            ControlStyles.UserPaint |
            ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.ResizeRedraw |
            ControlStyles.SupportsTransparentBackColor,
            true);
        BackColor = Color.Transparent;
        Padding = new Padding(0);
        _box.BorderStyle = BorderStyle.None;
        _box.BackColor = Theme.Field;
        _box.ForeColor = Theme.Text;
        _box.Font = UiFonts.Body;
        _box.GotFocus += (_, _) => Invalidate();
        _box.LostFocus += (_, _) => Invalidate();
        _box.TextChanged += (_, _) => TextChanged?.Invoke(this, EventArgs.Empty);
        Controls.Add(_box);
    }

    public new event EventHandler? TextChanged;

    [System.Diagnostics.CodeAnalysis.AllowNull]
    public override string Text
    {
        get => _box.Text;
        set => _box.Text = value;
    }

    public string Placeholder
    {
        get => _box.PlaceholderText;
        set => _box.PlaceholderText = value;
    }

    public bool ReadOnly
    {
        get => _box.ReadOnly;
        set => _box.ReadOnly = value;
    }

    public TextBox Inner => _box;

    public int Radius { get; set; } = 8;

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        var pad = Math.Max(6, (int)Math.Round(10 * (DeviceDpi / 96f)));
        var textHeight = _box.PreferredHeight;
        _box.SetBounds(pad, Math.Max(0, (Height - textHeight) / 2), Math.Max(10, Width - (pad * 2)), textHeight);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        var bounds = new RectangleF(0.5f, 0.5f, Width - 1f, Height - 1f);
        using (var path = UiKit.Rounded(bounds, Radius))
        using (var brush = new SolidBrush(Theme.Field))
        {
            g.FillPath(brush, path);
        }

        var border = _box.Focused
            ? UiKit.Mix(Theme.Accent, Theme.Field, 0.15)
            : Theme.CardBorder;
        UiKit.Stroke(g, bounds, Radius, border, _box.Focused ? 1.4f : 1f);
        base.OnPaint(e);
    }
}
