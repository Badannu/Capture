using System.Drawing.Drawing2D;
using CaptureDemo.Core;

namespace CaptureDemo.Ui;

/// <summary>
/// Small explanation window anchored to a <see cref="ConflictBadge"/>. A MessageBox would
/// steal the whole screen for three lines of text, and a tooltip cannot be read for long
/// enough to act on. Closes on any click, on deactivate and on Escape.
/// </summary>
internal sealed class ConflictPopup : Form
{
    private const int BaseWidth = 348;
    private const int BasePad = 16;

    private const TextFormatFlags Wrap =
        TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix | TextFormatFlags.Left | TextFormatFlags.Top;

    private readonly float _scale;
    private readonly string _head;
    private readonly string _body;
    private readonly string _hint;
    private readonly Rectangle _headRect;
    private readonly Rectangle _bodyRect;
    private readonly Rectangle _hintRect;
    private bool _autoClose = true;

    private ConflictPopup(HotkeyConflict conflict, float scale)
    {
        _scale = scale <= 0f ? 1f : scale;
        _head = conflict.Headline;
        _body = conflict.Detail;
        _hint = conflict.Hint;

        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.Manual;
        ShowInTaskbar = false;
        TopMost = true;
        DoubleBuffered = true;
        BackColor = Theme.Card;
        ForeColor = Theme.Text;
        Font = UiFonts.Body;

        var width = S(BaseWidth);
        var inner = width - (S(BasePad) * 2);
        var headHeight = Measure(_head, UiFonts.BodyBold, inner);
        var bodyHeight = Measure(_body, UiFonts.Small, inner);
        var hintHeight = Measure(_hint, UiFonts.Small, inner);

        var x = S(BasePad);
        var y = S(14);
        _headRect = new Rectangle(x, y, inner, headHeight);
        y += headHeight + S(7);
        _bodyRect = new Rectangle(x, y, inner, bodyHeight);
        y += bodyHeight + S(9);
        _hintRect = new Rectangle(x, y, inner, hintHeight);
        y += hintHeight + S(14);

        ClientSize = new Size(width, y);
        Region = new Region(UiKit.Rounded(new RectangleF(0, 0, Width, Height), S(12)));
    }

    /// <summary>Places the popup under the badge, flipping above it when the screen ends.</summary>
    public static void Open(Control anchor, HotkeyConflict conflict)
    {
        var scale = anchor.DeviceDpi / 96f;
        var popup = new ConflictPopup(conflict, scale);
        var screen = Screen.FromControl(anchor).WorkingArea;
        var x = anchor.PointToScreen(new Point(anchor.Width, anchor.Height)).X - popup.Width;
        var under = anchor.PointToScreen(new Point(0, anchor.Height)).Y + (int)Math.Round(6 * scale);

        if (under + popup.Height > screen.Bottom)
        {
            under = anchor.PointToScreen(Point.Empty).Y - popup.Height - (int)Math.Round(8 * scale);
        }

        popup.Location = new Point(
            Math.Max(screen.Left + 8, Math.Min(x, screen.Right - popup.Width - 8)),
            Math.Max(screen.Top + 8, under));
        popup.Show(anchor.FindForm());
    }

    /// <summary>Dev-only: render without an owner, so --ui-demo can screenshot it.</summary>
    internal static ConflictPopup CreateForPreview(HotkeyConflict conflict, float scale)
    {
        var popup = new ConflictPopup(conflict, scale);
        // The screenshot helper activates other windows while it works; a popup that
        // closes on deactivate would be gone before its pixels are read.
        popup._autoClose = false;
        return popup;
    }

    protected override bool ShowWithoutActivation => false;

    protected override void OnDeactivate(EventArgs e)
    {
        base.OnDeactivate(e);
        if (_autoClose)
        {
            Close();
        }
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        Close();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Escape)
        {
            Close();
            return;
        }

        base.OnKeyDown(e);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        var bounds = new RectangleF(0.5f, 0.5f, Width - 1f, Height - 1f);
        using (var path = UiKit.Rounded(bounds, S(12)))
        using (var brush = new SolidBrush(Theme.Card))
        {
            g.FillPath(brush, path);
        }

        UiKit.Stroke(g, bounds, S(12), Theme.CardBorder, Math.Max(1f, _scale));

        TextRenderer.DrawText(g, _head, UiFonts.BodyBold, _headRect, Theme.Text, Wrap);
        TextRenderer.DrawText(g, _body, UiFonts.Small, _bodyRect, Theme.Muted, Wrap);
        TextRenderer.DrawText(g, _hint, UiFonts.Small, _hintRect, Theme.Accent, Wrap);

        // No icon: the badge that was just clicked already carries the exclamation, and a
        // second one next to the headline only competes with it.
    }

    private static int Measure(string text, Font font, int width) =>
        TextRenderer.MeasureText(text, font, new Size(Math.Max(1, width), int.MaxValue), Wrap).Height;

    private int S(int px) => Math.Max(1, (int)Math.Round(px * _scale));
}
