using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using CaptureDemo.Core;

namespace CaptureDemo.Snip;

/// <summary>
/// Annotation bar: pick / draw tools, shape tools, ink colour, stroke weight,
/// then undo and the confirm/discard pair.
/// </summary>
internal sealed class AnnoPillForm : LayeredChromeForm
{
    private const int LongId = 10;
    private const int SelectId = 11;
    private const int PenId = 12;
    private const int LineId = 13;
    private const int ArrowId = 14;
    private const int RectId = 15;
    private const int RoundRectId = 16;
    private const int EllipseId = 17;
    private const int CircleId = 18;
    private const int HighlightId = 19;
    private const int MosaicId = 20;
    private const int ColorId = 21;
    private const int WeightId = 22;
    private const int UndoId = 23;
    private const int SaveId = 24;
    private const int DiscardId = 25;

    private readonly PillTipForm _tip = new();
    private readonly StylePillForm _colors = new();
    private readonly StylePillForm _weights = new();
    private int _hover;
    private AnnoTool _tool = AnnoTool.Pen;
    private int _color = AnnoPalette.Colors[AnnoPalette.Default].ToArgb();
    private float _weight = AnnoPalette.Weights[1];
    private int[] _widths = Array.Empty<int>();
    private int[] _ids = Array.Empty<int>();

    public AnnoPillForm()
    {
        _colors.ColorPicked += argb =>
        {
            _color = argb;
            ColorChosen?.Invoke(argb);
            Render();
        };
        _weights.WeightPicked += weight =>
        {
            _weight = weight;
            WeightChosen?.Invoke(weight);
            Render();
        };
    }

    public event Action? LongShot;

    public event Action<AnnoTool>? ToolChosen;

    public event Action<int>? ColorChosen;

    public event Action<float>? WeightChosen;

    public event Action? UndoClicked;

    public event Action? ConfirmClicked;

    public event Action? DiscardClicked;

    public AnnoTool Tool => _tool;

    /// <summary>True while either style popup is on screen. The session uses this to decide
    /// whether a click elsewhere should dismiss instead of drawing.</summary>
    public bool AnyPopupVisible => IsUp(_colors) || IsUp(_weights);

    public bool PopupContains(Point screen) => Hits(_colors, screen) || Hits(_weights, screen);

    public void SetTool(AnnoTool tool)
    {
        _tool = tool;
        if (Visible)
        {
            Render();
        }
    }

    public void SetColor(int argb)
    {
        _color = argb;
        if (Visible)
        {
            Render();
        }
    }

    public void SetWeight(float weight)
    {
        _weight = weight;
        if (Visible)
        {
            Render();
        }
    }

    protected override void Relayout()
    {
        var button = S(PillMetrics.Button);
        var gap = S(PillMetrics.Gap);
        // [long] | pick pen line arrow | rect round ellipse circle | highlight mosaic | colour weight | undo | save discard
        _widths = new[]
        {
            button,
            gap,
            button, button, button, button,
            gap,
            button, button, button, button,
            gap,
            button, button,
            gap,
            button, button,
            gap,
            button,
            gap,
            button, button,
        };
        _ids = new[]
        {
            LongId,
            0,
            SelectId, PenId, LineId, ArrowId,
            0,
            RectId, RoundRectId, EllipseId, CircleId,
            0,
            HighlightId, MosaicId,
            0,
            ColorId, WeightId,
            0,
            UndoId,
            0,
            SaveId, DiscardId,
        };
        var shadow = S(PillMetrics.Shadow);
        ClientSize = new Size(
            PillMetrics.ContentWidth(S(PillMetrics.Pad), _widths) + shadow * 2,
            S(PillMetrics.BarHeight) + shadow * 2);
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
        // Any bar button other than the two chips dismisses the open popup first, the
        // same way an outside click does on the canvas.
        if (AnyPopupVisible && hit is not (ColorId or WeightId))
        {
            CloseStylePopups();
        }

        switch (hit)
        {
            case LongId:
                LongShot?.Invoke();
                break;
            case SelectId:
                Choose(AnnoTool.Select);
                break;
            case PenId:
                Choose(AnnoTool.Pen);
                break;
            case LineId:
                Choose(AnnoTool.Line);
                break;
            case ArrowId:
                Choose(AnnoTool.Arrow);
                break;
            case RectId:
                Choose(AnnoTool.Rect);
                break;
            case RoundRectId:
                Choose(AnnoTool.RoundRect);
                break;
            case EllipseId:
                Choose(AnnoTool.Ellipse);
                break;
            case CircleId:
                Choose(AnnoTool.Circle);
                break;
            case HighlightId:
                Choose(AnnoTool.Highlight);
                break;
            case MosaicId:
                Choose(AnnoTool.Mosaic);
                break;
            case ColorId:
                ToggleColors();
                break;
            case WeightId:
                ToggleWeights();
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
            PillDraw.Forget(this);
            _tip.Dispose();
            _colors.Dispose();
            _weights.Dispose();
        }

        base.Dispose(disposing);
    }

    private void OpenColors()
    {
        CloseStylePopups();
        _tip.Hide();
        _colors.ShowColors(_color);
        AnchorPopup(_colors, ColorId);
    }

    private void OpenWeights()
    {
        CloseStylePopups();
        _tip.Hide();
        _weights.ShowWeights(_weight);
        AnchorPopup(_weights, WeightId);
    }

    private void ToggleColors()
    {
        if (IsUp(_colors))
        {
            _colors.Hide();
            return;
        }

        OpenColors();
    }

    private void ToggleWeights()
    {
        if (IsUp(_weights))
        {
            _weights.Hide();
            return;
        }

        OpenWeights();
    }

    private void AnchorPopup(LayeredChromeForm popup, int anchorId)
    {
        var slot = SlotBounds(anchorId);
        if (slot.Width < 2)
        {
            return;
        }

        var anchor = RectangleToScreen(slot);
        var work = Screen.FromPoint(new Point(anchor.Left, anchor.Top)).WorkingArea;
        popup.Place(work, anchor);
        if (!popup.Visible)
        {
            popup.Show();
        }
    }

    /// <summary>Dismisses both popups. Called by the session on an outside click and on Esc.</summary>
    public void CloseStylePopups()
    {
        if (IsUp(_colors))
        {
            _colors.Hide();
        }

        if (IsUp(_weights))
        {
            _weights.Hide();
        }
    }

    private static bool IsUp(Form form) => form is { IsDisposed: false, Visible: true };

    private static bool Hits(Form form, Point screen) => IsUp(form) && form.Bounds.Contains(screen);

    private void Choose(AnnoTool tool)
    {
        _tool = tool;
        CloseStylePopups();
        Render();
        ToolChosen?.Invoke(tool);
    }

    private int Hit(Point point) =>
        PillMetrics.Hit(point.X, point.Y, S(PillMetrics.Shadow), S(PillMetrics.BarHeight), S(PillMetrics.Pad), _widths, _ids);

    private Rectangle SlotBounds(int id)
    {
        var shadow = S(PillMetrics.Shadow);
        var cursor = shadow + S(PillMetrics.Pad);
        for (var i = 0; i < _widths.Length && i < _ids.Length; i++)
        {
            if (_ids[i] == id)
            {
                return new Rectangle(cursor, shadow, _widths[i], S(PillMetrics.BarHeight));
            }

            cursor += _widths[i];
        }

        return Rectangle.Empty;
    }

    private void ShowTip(int hit)
    {
        // The popup is anchored right where the tip would sit, so they must not coexist.
        if (AnyPopupVisible)
        {
            _tip.Hide();
            return;
        }

        var text = ToolFor(hit) is AnnoTool tool
            ? ToolName(tool)
            : hit switch
            {
                LongId => "长截图",
                ColorId => "颜色",
                WeightId => "粗细",
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

        var slot = SlotBounds(hit);
        if (slot.Width < 2)
        {
            _tip.Hide();
            return;
        }

        _tip.ShowUnder(RectangleToScreen(slot), text, Dpi);
    }

    private static string ToolName(AnnoTool tool) => tool switch
    {
        AnnoTool.Select => "选择 / 移动",
        AnnoTool.Pen => "画笔",
        AnnoTool.Line => "直线",
        AnnoTool.Arrow => "箭头",
        AnnoTool.Rect => "矩形",
        AnnoTool.RoundRect => "圆角矩形",
        AnnoTool.Ellipse => "椭圆",
        AnnoTool.Circle => "圆",
        AnnoTool.Highlight => "高亮",
        AnnoTool.Mosaic => "马赛克",
        _ => "",
    };

    private static AnnoTool? ToolFor(int hit) => hit switch
    {
        SelectId => AnnoTool.Select,
        PenId => AnnoTool.Pen,
        LineId => AnnoTool.Line,
        ArrowId => AnnoTool.Arrow,
        RectId => AnnoTool.Rect,
        RoundRectId => AnnoTool.RoundRect,
        EllipseId => AnnoTool.Ellipse,
        CircleId => AnnoTool.Circle,
        HighlightId => AnnoTool.Highlight,
        MosaicId => AnnoTool.Mosaic,
        _ => null,
    };

    private static int ToolId(AnnoTool tool) => tool switch
    {
        AnnoTool.Select => SelectId,
        AnnoTool.Pen => PenId,
        AnnoTool.Line => LineId,
        AnnoTool.Arrow => ArrowId,
        AnnoTool.Rect => RectId,
        AnnoTool.RoundRect => RoundRectId,
        AnnoTool.Ellipse => EllipseId,
        AnnoTool.Circle => CircleId,
        AnnoTool.Highlight => HighlightId,
        AnnoTool.Mosaic => MosaicId,
        _ => PenId,
    };

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

                if (id == ColorId)
                {
                    DrawColorChip(graphics, slot);
                }
                else if (id == WeightId)
                {
                    DrawWeightChip(graphics, slot);
                }
                else if (IconFor(id) is { } icon)
                {
                    DrawIcon(graphics, icon, slot, Theme.SnipInk);
                }

                if (id != 0 && ToolId(_tool) == id)
                {
                    var line = new Rectangle(slot.Left + ((slot.Width - S(14)) / 2), slot.Bottom - S(7), S(14), S(2));
                    using var accent = new SolidBrush(Theme.SnipAccentLine);
                    using var accentPath = PillDraw.Round(line, S(1));
                    graphics.FillPath(accent, accentPath);
                }

                cursor += _widths[i];
            }
        }

        PillDraw.Present(this, bitmap);
    }

    private static Action<Graphics, Rectangle, Color>? IconFor(int id) => id switch
    {
        LongId => SnipIcons.Long,
        SelectId => SnipIcons.Select,
        PenId => SnipIcons.Pen,
        LineId => SnipIcons.Line,
        ArrowId => SnipIcons.Arrow,
        RectId => SnipIcons.ShapeRect,
        RoundRectId => SnipIcons.ShapeRoundRect,
        EllipseId => SnipIcons.ShapeEllipse,
        CircleId => SnipIcons.ShapeCircle,
        HighlightId => SnipIcons.Highlight,
        MosaicId => SnipIcons.Mosaic,
        UndoId => SnipIcons.Undo,
        SaveId => SnipIcons.Confirm,
        DiscardId => SnipIcons.Discard,
        _ => null,
    };

    private void DrawColorChip(Graphics graphics, Rectangle slot)
    {
        var side = S(15);
        var box = new Rectangle(slot.Left + ((slot.Width - side) / 2), slot.Top + ((slot.Height - side) / 2), side, side);
        var colour = Color.FromArgb(_color);
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using var brush = new SolidBrush(colour);
        graphics.FillEllipse(brush, box);
        // Light swatches need a dark rim or the chip vanishes against the dark capsule.
        using var rim = new Pen(
            colour.GetBrightness() > 0.8f ? Color.FromArgb(120, 0, 0, 0) : Color.FromArgb(120, 255, 255, 255));
        graphics.DrawEllipse(rim, box);
    }

    private void DrawWeightChip(Graphics graphics, Rectangle slot)
    {
        var index = Math.Clamp(AnnoPalette.IndexOfWeight(_weight), 0, AnnoPalette.Weights.Length - 1);
        var width = 1.6f + (index * 2.1f);
        var length = S(15);
        var y = slot.Top + (slot.Height / 2f);
        var left = slot.Left + ((slot.Width - length) / 2f);
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using var pen = new Pen(Theme.SnipInk, Math.Max(1.4f, width))
        {
            StartCap = LineCap.Round,
            EndCap = LineCap.Round,
        };
        graphics.DrawLine(pen, left, y, left + length, y);
    }
}
