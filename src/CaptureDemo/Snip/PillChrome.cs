using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using CaptureDemo.Core;

namespace CaptureDemo.Snip;

internal static class SnipModeInfo
{
    public static string Name(SnipMode mode) => mode switch
    {
        SnipMode.Rectangle => "矩形",
        SnipMode.Window => "窗口",
        SnipMode.Fullscreen => "全屏",
        SnipMode.Freeform => "任意形状",
        SnipMode.Long => "长截图",
        SnipMode.Element => "元素截图",
        _ => "截图",
    };

    public static void Draw(SnipMode mode, Graphics graphics, Rectangle bounds, Color color)
    {
        switch (mode)
        {
            case SnipMode.Fullscreen:
                SnipIcons.Fullscreen(graphics, bounds, color);
                break;
            case SnipMode.Window:
                SnipIcons.Window(graphics, bounds, color);
                break;
            case SnipMode.Long:
                SnipIcons.Long(graphics, bounds, color);
                break;
            case SnipMode.Element:
                SnipIcons.Element(graphics, bounds, color);
                break;
            case SnipMode.Freeform:
                SnipIcons.Freeform(graphics, bounds, color);
                break;
            default:
                SnipIcons.Rect(graphics, bounds, color);
                break;
        }
    }
}

internal static class PillDraw
{
    public static GraphicsPath Round(Rectangle bounds, int radius)
    {
        var path = new GraphicsPath();
        var diameter = Math.Max(2, radius * 2);
        if (bounds.Width < diameter || bounds.Height < diameter)
        {
            path.AddRectangle(bounds);
            return path;
        }

        path.AddArc(bounds.Left, bounds.Top, diameter, diameter, 180, 90);
        path.AddArc(bounds.Right - diameter, bounds.Top, diameter, diameter, 270, 90);
        path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0, 90);
        path.AddArc(bounds.Left, bounds.Bottom - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();
        return path;
    }

    public static void Present(Form form, Bitmap bitmap)
    {
        if (!form.IsHandleCreated || form.IsDisposed)
        {
            return;
        }

        LayeredWindow.Update(form.Handle, form.Location, bitmap);
    }
}

internal class PillTipForm : Form
{
    public PillTipForm()
    {
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        TopMost = true;
        ShowInTaskbar = false;
        AutoScaleMode = AutoScaleMode.None;
    }

    protected override bool ShowWithoutActivation => true;

    protected override CreateParams CreateParams
    {
        get
        {
            const int WS_EX_LAYERED = 0x00080000;
            const int WS_EX_TOOLWINDOW = 0x80;
            const int WS_EX_NOACTIVATE = 0x08000000;
            var cp = base.CreateParams;
            cp.ExStyle |= WS_EX_LAYERED | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE;
            return cp;
        }
    }

    public void ShowUnder(Rectangle anchorScreen, string text, int dpi)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            Hide();
            return;
        }

        var scale = Math.Max(1, dpi) / 96f;
        int S(int v) => Math.Max(1, (int)Math.Round(v * scale));
        var measured = TextRenderer.MeasureText(text, UiFonts.Small);
        var width = measured.Width + S(16);
        var height = S(22);
        var shadow = S(6);
        ClientSize = new Size(width + shadow * 2, height + shadow * 2);
        var x = anchorScreen.Left + (anchorScreen.Width - width) / 2 - shadow;
        var y = anchorScreen.Bottom + S(4) - shadow;
        Location = new Point(x, y);
        using var bitmap = new Bitmap(Width, Height, PixelFormat.Format32bppPArgb);
        using (var graphics = Graphics.FromImage(bitmap))
        {
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
            graphics.Clear(Color.Transparent);
            var body = new Rectangle(shadow, shadow, width, height);
            using var path = PillDraw.Round(body, height / 2);
            using var fill = new SolidBrush(Color.FromArgb(236, 28, 28, 28));
            graphics.FillPath(fill, path);
            TextRenderer.DrawText(graphics, text, UiFonts.Small, body, Color.White, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        }

        if (!Visible)
        {
            Show();
        }

        PillDraw.Present(this, bitmap);
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        WindowAffinity.TryExcludeFromCapture(Handle);
    }
}


internal abstract class LayeredChromeForm : Form
{
    private Rectangle _anchorWork;
    private Rectangle? _anchorBelow;
    private bool _placed;

    protected int Dpi { get; private set; } = 96;

    protected LayeredChromeForm()
    {
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        TopMost = true;
        AutoScaleMode = AutoScaleMode.None;
    }

    protected override bool ShowWithoutActivation => true;

    protected override CreateParams CreateParams
    {
        get
        {
            const int WS_EX_LAYERED = 0x00080000;
            const int WS_EX_TOOLWINDOW = 0x80;
            const int WS_EX_NOACTIVATE = 0x08000000;
            var cp = base.CreateParams;
            cp.ExStyle |= WS_EX_LAYERED | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE;
            return cp;
        }
    }

    protected int S(int px) => Math.Max(1, (int)Math.Round(px * Dpi / 96f));

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        Dpi = Math.Max(96, DeviceDpi);
        WindowAffinity.TryExcludeFromCapture(Handle);
        if (_placed)
        {
            Place(_anchorWork, _anchorBelow);
        }
        else
        {
            Relayout();
        }
    }

    protected override void OnDpiChanged(DpiChangedEventArgs e)
    {
        base.OnDpiChanged(e);
        Dpi = Math.Max(96, e.DeviceDpiNew);
        if (_placed)
        {
            Place(_anchorWork, _anchorBelow);
        }
        else
        {
            Relayout();
            Render();
        }
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        Render();
    }

    public void Place(Rectangle work, Rectangle? below)
    {
        _anchorWork = work;
        _anchorBelow = below;
        _placed = true;
        Relayout();
        var shadow = S(PillMetrics.Shadow);
        var visual = new Size(Math.Max(1, Width - shadow * 2), Math.Max(1, Height - shadow * 2));
        if (work.Width < 32 || work.Height < 32)
        {
            work = NativeMethods.VirtualScreen();
        }

        int x;
        int y;
        if (below is Rectangle box)
        {
            x = box.Left + (box.Width - visual.Width) / 2 - shadow;
            y = box.Bottom + S(8) - shadow;
            if (y + Height > work.Bottom - 4)
            {
                y = box.Top - visual.Height - S(8) - shadow;
            }
        }
        else
        {
            x = work.Left + (work.Width - visual.Width) / 2 - shadow;
            y = work.Top + S(10) - shadow;
        }

        var maxX = Math.Max(work.Left, work.Right - Width);
        var maxY = Math.Max(work.Top, work.Bottom - Height);
        Location = new Point(Math.Clamp(x, work.Left, maxX), Math.Clamp(y, work.Top, maxY));
        if (Visible)
        {
            Render();
        }
    }

    protected abstract void Relayout();

    protected abstract void Render();

    protected void DrawBar(Graphics graphics, Rectangle body)
    {
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
        using var shadowPath = PillDraw.Round(new Rectangle(body.X, body.Y + S(2), body.Width, body.Height), body.Height / 2);
        using var shadow = new SolidBrush(Color.FromArgb(54, 0, 0, 0));
        graphics.FillPath(shadow, shadowPath);
        using var path = PillDraw.Round(body, body.Height / 2);
        using var fill = new SolidBrush(Color.FromArgb(242, 32, 32, 32));
        graphics.FillPath(fill, path);
        using var border = new Pen(Color.FromArgb(42, 255, 255, 255));
        graphics.DrawPath(border, path);
    }

    protected static void DrawIcon(Graphics graphics, Action<Graphics, Rectangle, Color> icon, Rectangle slot, Color color)
    {
        var side = Math.Max(10, Math.Min(slot.Width, slot.Height) - 14);
        var box = new Rectangle(slot.Left + (slot.Width - side) / 2, slot.Top + (slot.Height - side) / 2, side, side);
        icon(graphics, box, color);
    }
}

internal sealed class CapturePillForm : LayeredChromeForm
{
    private readonly PillTipForm _tip = new();
    private SnipMode _mode = SnipMode.Rectangle;
    private string _longHotkey = "";
    private string _elementHotkey = "";
    private int _hover;
    private int[] _widths = Array.Empty<int>();
    private int[] _ids = Array.Empty<int>();

    public event Action<SnipMode>? ModeChosen;

    public event Action? SettingsClicked;

    public event Action? CloseClicked;

    public void SetMode(SnipMode mode)
    {
        _mode = mode;
        if (Visible)
        {
            Render();
        }
    }

    public void SetShortcuts(string? longHotkey, string? elementHotkey)
    {
        _longHotkey = longHotkey ?? "";
        _elementHotkey = elementHotkey ?? "";
    }

    protected override void Relayout()
    {
        var button = S(PillMetrics.Button);
        var gap = S(PillMetrics.Gap);
        _widths = new[] { button, button, button, button, button, button, gap, button, button };
        _ids = new[]
        {
            PillMetrics.Fullscreen,
            PillMetrics.Rectangle,
            PillMetrics.Window,
            PillMetrics.Long,
            PillMetrics.Element,
            PillMetrics.Freeform,
            0,
            PillMetrics.Settings,
            PillMetrics.Close,
        };
        var content = PillMetrics.ContentWidth(S(PillMetrics.Pad), _widths);
        var shadow = S(PillMetrics.Shadow);
        ClientSize = new Size(content + shadow * 2, S(PillMetrics.BarHeight) + shadow * 2);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        var hit = Hit(e.Location);
        Cursor = hit == 0 ? Cursors.Default : Cursors.Hand;
        if (hit != _hover)
        {
            _hover = hit;
            Render();
            ShowTip(hit);
        }

        base.OnMouseMove(e);
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        _hover = 0;
        _tip.Hide();
        Render();
        base.OnMouseLeave(e);
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        var hit = Hit(e.Location);
        var mode = ModeFor(hit);
        if (mode is SnipMode chosen)
        {
            ModeChosen?.Invoke(chosen);
        }
        else if (hit == PillMetrics.Settings)
        {
            SettingsClicked?.Invoke();
        }
        else if (hit == PillMetrics.Close)
        {
            CloseClicked?.Invoke();
        }

        base.OnMouseUp(e);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _tip.Dispose();
        }

        base.Dispose(disposing);
    }

    private void ShowTip(int hit)
    {
        var text = Tip(hit);
        if (text.Length == 0)
        {
            _tip.Hide();
            return;
        }

        var slot = SlotBounds(hit);
        if (slot.Width < 2)
        {
            _tip.Hide();
            return;
        }

        _tip.ShowUnder(RectangleToScreen(slot), text, Dpi);
    }

    private string Tip(int hit)
    {
        var mode = ModeFor(hit);
        if (mode is SnipMode chosen)
        {
            var name = SnipModeInfo.Name(chosen);
            var shortcut = chosen switch
            {
                SnipMode.Long => _longHotkey,
                SnipMode.Element => _elementHotkey,
                _ => "",
            };
            return string.IsNullOrWhiteSpace(shortcut) ? name : name + " (" + shortcut + ")";
        }

        return hit switch
        {
            PillMetrics.Settings => "设置",
            PillMetrics.Close => "关闭(Esc)",
            _ => "",
        };
    }

    private static SnipMode? ModeFor(int hit) => hit switch
    {
        PillMetrics.Fullscreen => SnipMode.Fullscreen,
        PillMetrics.Rectangle => SnipMode.Rectangle,
        PillMetrics.Window => SnipMode.Window,
        PillMetrics.Long => SnipMode.Long,
        PillMetrics.Element => SnipMode.Element,
        PillMetrics.Freeform => SnipMode.Freeform,
        _ => null,
    };

    private int Hit(Point point) => PillMetrics.Hit(point.X, point.Y, S(PillMetrics.Shadow), S(PillMetrics.BarHeight), S(PillMetrics.Pad), _widths, _ids);

    private Rectangle SlotBounds(int id)
    {
        var shadow = S(PillMetrics.Shadow);
        var cursor = shadow + S(PillMetrics.Pad);
        var top = shadow;
        var height = S(PillMetrics.BarHeight);
        for (var i = 0; i < _widths.Length && i < _ids.Length; i++)
        {
            if (_ids[i] == id)
            {
                return new Rectangle(cursor, top, _widths[i], height);
            }

            cursor += _widths[i];
        }

        return Rectangle.Empty;
    }

    protected override void Render()
    {
        if (!IsHandleCreated || _widths.Length == 0)
        {
            return;
        }

        var shadow = S(PillMetrics.Shadow);
        using var bitmap = new Bitmap(Math.Max(1, Width), Math.Max(1, Height), PixelFormat.Format32bppPArgb);
        using (var graphics = Graphics.FromImage(bitmap))
        {
            graphics.Clear(Color.Transparent);
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            var body = new Rectangle(shadow, shadow, Width - shadow * 2, S(PillMetrics.BarHeight));
            DrawBar(graphics, body);
            var cursor = body.Left + S(PillMetrics.Pad);
            for (var i = 0; i < _widths.Length; i++)
            {
                var slot = new Rectangle(cursor, body.Top, _widths[i], body.Height);
                var id = _ids[i];
                if (id != 0 && _hover == id)
                {
                    using var hover = new SolidBrush(Color.FromArgb(28, 255, 255, 255));
                    using var hoverPath = PillDraw.Round(Rectangle.Inflate(slot, -S(3), -S(5)), S(6));
                    graphics.FillPath(hover, hoverPath);
                }

                var mode = ModeFor(id);
                if (mode is SnipMode chosen)
                {
                    DrawIcon(graphics, (g, r, c) => SnipModeInfo.Draw(chosen, g, r, c), slot, Theme.SnipInk);
                    if (chosen == _mode)
                    {
                        var line = new Rectangle(slot.Left + (slot.Width - S(14)) / 2, slot.Bottom - S(7), S(14), S(2));
                        using var accent = new SolidBrush(Theme.SnipAccentLine);
                        using var accentPath = PillDraw.Round(line, S(1));
                        graphics.FillPath(accent, accentPath);
                    }
                }
                else if (id == PillMetrics.Settings)
                {
                    DrawIcon(graphics, SnipIcons.Gear, slot, Color.FromArgb(220, 220, 220));
                }
                else if (id == PillMetrics.Close)
                {
                    DrawIcon(graphics, SnipIcons.Discard, slot, Theme.SnipInk);
                }

                cursor += _widths[i];
            }
        }

        PillDraw.Present(this, bitmap);
    }
}

internal sealed class AnnoPillForm : LayeredChromeForm
{
    private const int LongId = 10;
    private const int PenId = 11;
    private const int ArrowId = 12;
    private const int HighlightId = 13;
    private const int MosaicId = 14;
    private const int UndoId = 15;
    private const int SaveId = 16;
    private const int DiscardId = 17;

    private readonly PillTipForm _tip = new();
    private int _hover;
    private AnnoTool _tool = AnnoTool.Pen;
    private int[] _widths = Array.Empty<int>();
    private int[] _ids = Array.Empty<int>();

    public event Action? LongShot;

    public event Action<AnnoTool>? ToolChosen;

    public event Action? UndoClicked;

    public event Action? ConfirmClicked;

    public event Action? DiscardClicked;

    public void SetTool(AnnoTool tool)
    {
        _tool = tool;
        if (Visible)
        {
            Render();
        }
    }

    protected override void Relayout()
    {
        var button = S(PillMetrics.Button);
        var gap = S(PillMetrics.Gap);
        _widths = new[] { button, gap, button, button, button, button, button, gap, button, button };
        _ids = new[] { LongId, 0, PenId, ArrowId, HighlightId, MosaicId, UndoId, 0, SaveId, DiscardId };
        var shadow = S(PillMetrics.Shadow);
        ClientSize = new Size(PillMetrics.ContentWidth(S(PillMetrics.Pad), _widths) + shadow * 2, S(PillMetrics.BarHeight) + shadow * 2);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        var hit = Hit(e.Location);
        Cursor = hit == 0 ? Cursors.Default : Cursors.Hand;
        if (hit != _hover)
        {
            _hover = hit;
            Render();
            ShowTip(hit);
        }

        base.OnMouseMove(e);
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        _hover = 0;
        _tip.Hide();
        Render();
        base.OnMouseLeave(e);
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        switch (Hit(e.Location))
        {
            case LongId:
                LongShot?.Invoke();
                break;
            case PenId:
                Choose(AnnoTool.Pen);
                break;
            case ArrowId:
                Choose(AnnoTool.Arrow);
                break;
            case HighlightId:
                Choose(AnnoTool.Highlight);
                break;
            case MosaicId:
                Choose(AnnoTool.Mosaic);
                break;
            case UndoId:
                UndoClicked?.Invoke();
                break;
            case SaveId:
                ConfirmClicked?.Invoke();
                break;
            case DiscardId:
                DiscardClicked?.Invoke();
                break;
        }

        base.OnMouseUp(e);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _tip.Dispose();
        }

        base.Dispose(disposing);
    }

    private void Choose(AnnoTool tool)
    {
        _tool = tool;
        Render();
        ToolChosen?.Invoke(tool);
    }

    private int Hit(Point point) => PillMetrics.Hit(point.X, point.Y, S(PillMetrics.Shadow), S(PillMetrics.BarHeight), S(PillMetrics.Pad), _widths, _ids);

    private void ShowTip(int hit)
    {
        var text = hit switch
        {
            LongId => "长截图",
            PenId => "画笔",
            ArrowId => "箭头",
            HighlightId => "高亮",
            MosaicId => "马赛克",
            UndoId => "撤销",
            SaveId => "保存",
            DiscardId => "放弃",
            _ => "",
        };
        if (text.Length == 0)
        {
            _tip.Hide();
            return;
        }

        var shadow = S(PillMetrics.Shadow);
        var cursor = shadow + S(PillMetrics.Pad);
        Rectangle slot = Rectangle.Empty;
        for (var i = 0; i < _ids.Length; i++)
        {
            if (_ids[i] == hit)
            {
                slot = new Rectangle(cursor, shadow, _widths[i], S(PillMetrics.BarHeight));
                break;
            }

            cursor += _widths[i];
        }

        if (slot.Width < 2)
        {
            _tip.Hide();
            return;
        }

        _tip.ShowUnder(RectangleToScreen(slot), text, Dpi);
    }

    protected override void Render()
    {
        if (!IsHandleCreated || _widths.Length == 0)
        {
            return;
        }

        var shadow = S(PillMetrics.Shadow);
        using var bitmap = new Bitmap(Math.Max(1, Width), Math.Max(1, Height), PixelFormat.Format32bppPArgb);
        using (var graphics = Graphics.FromImage(bitmap))
        {
            graphics.Clear(Color.Transparent);
            var body = new Rectangle(shadow, shadow, Width - shadow * 2, S(PillMetrics.BarHeight));
            DrawBar(graphics, body);
            var cursor = body.Left + S(PillMetrics.Pad);
            for (var i = 0; i < _widths.Length; i++)
            {
                var slot = new Rectangle(cursor, body.Top, _widths[i], body.Height);
                var id = _ids[i];
                if (id != 0 && _hover == id)
                {
                    using var hover = new SolidBrush(Color.FromArgb(28, 255, 255, 255));
                    using var hoverPath = PillDraw.Round(Rectangle.Inflate(slot, -S(2), -S(4)), S(6));
                    graphics.FillPath(hover, hoverPath);
                }

                Action<Graphics, Rectangle, Color>? icon = id switch
                {
                    LongId => SnipIcons.Long,
                    PenId => SnipIcons.Pen,
                    ArrowId => SnipIcons.Arrow,
                    HighlightId => SnipIcons.Highlight,
                    MosaicId => SnipIcons.Mosaic,
                    UndoId => SnipIcons.Undo,
                    SaveId => SnipIcons.Confirm,
                    DiscardId => SnipIcons.Discard,
                    _ => null,
                };
                if (icon != null)
                {
                    DrawIcon(graphics, icon, slot, Theme.SnipInk);
                }

                if (ToolId(_tool) == id)
                {
                    var line = new Rectangle(slot.Left + (slot.Width - S(14)) / 2, slot.Bottom - S(7), S(14), S(2));
                    using var accent = new SolidBrush(Theme.SnipAccentLine);
                    using var accentPath = PillDraw.Round(line, S(1));
                    graphics.FillPath(accent, accentPath);
                }

                cursor += _widths[i];
            }
        }

        PillDraw.Present(this, bitmap);
    }

    private static int ToolId(AnnoTool tool) => tool switch
    {
        AnnoTool.Arrow => ArrowId,
        AnnoTool.Highlight => HighlightId,
        AnnoTool.Mosaic => MosaicId,
        _ => PenId,
    };
}
