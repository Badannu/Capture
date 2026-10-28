using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using CaptureDemo.Clips;
using CaptureDemo.Core;

namespace CaptureDemo.Ui;

/// <summary>
/// The clipboard flyout. Painted as one layered bitmap so the rounded corners and
/// the shadow stay clean; hit testing is done by hand because there are no child
/// controls. Double click activates, right click opens the per-item menu.
/// </summary>
internal sealed class ClipboardForm : Form
{
    private const int Shadow = 14;
    private const int Width96 = 392;
    private const int Header96 = 64;
    private const int Row96 = 64;
    private const int Footer96 = 44;
    private const int MaxRows = 8;

    private readonly ClipboardService _service;
    private readonly ContextMenuStrip _menu;
    private readonly Dictionary<string, Bitmap> _thumbs = new();
    private readonly List<Rectangle> _rowBounds = new();
    private int _scroll;
    private int _hover = -1;
    private bool _menuOpen;
    private bool _closeHover;
    private bool _clearHover;

    public ClipboardForm(ClipboardService service)
    {
        _service = service;
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        TopMost = true;
        StartPosition = FormStartPosition.Manual;
        AutoScaleMode = AutoScaleMode.None;
        KeyPreview = true;
        DoubleBuffered = true;
        Text = "剪贴板";
        _menu = BuildMenu();
        _service.Changed += OnServiceChanged;
    }

    private float DpiScale => Math.Max(96, DeviceDpi) / 96f;

    private int S(int px) => Math.Max(1, (int)Math.Round(px * (double)DpiScale));

    private int CardWidth => S(Width96);

    private int HeaderHeight => S(Header96);

    private int RowHeight => S(Row96);

    private int FooterHeight => S(Footer96);

    private int VisibleRows
    {
        get
        {
            var screen = Screen.FromPoint(Cursor.Position).WorkingArea;
            var available = (screen.Height - S(160)) / RowHeight;
            return Math.Max(3, Math.Min(MaxRows, available));
        }
    }

    private IReadOnlyList<ClipboardEntry> Items => _service.History.Ordered();

    private int VisibleCount => Math.Min(VisibleRows, Items.Count);

    protected override bool ShowWithoutActivation => false;

    protected override CreateParams CreateParams
    {
        get
        {
            const int WsExLayered = 0x00080000;
            const int WsExToolWindow = 0x00000080;
            var cp = base.CreateParams;
            cp.ExStyle |= WsExLayered | WsExToolWindow;
            return cp;
        }
    }

    public void ShowNearCursor()
    {
        var cursor = Cursor.Position;
        var screen = Screen.FromPoint(cursor).WorkingArea;
        var rows = Math.Max(3, VisibleCount);
        var height = (Shadow * 2) + HeaderHeight + (rows * RowHeight) + FooterHeight;
        var width = CardWidth + (Shadow * 2);
        var x = cursor.X - (width / 2);
        var y = cursor.Y - height + S(10);
        if (y < screen.Top + S(8))
        {
            y = cursor.Y + S(12);
        }

        x = Math.Clamp(x, screen.Left + S(8), Math.Max(screen.Left + S(8), screen.Right - width - S(8)));
        y = Math.Clamp(y, screen.Top + S(8), Math.Max(screen.Top + S(8), screen.Bottom - height - S(8)));
        Location = new Point(x, y);
        Show();
        Activate();
        Render();
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        Render();
    }

    protected override void OnDeactivate(EventArgs e)
    {
        base.OnDeactivate(e);
        if (!_menuOpen)
        {
            Hide();
        }
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Escape)
        {
            e.Handled = true;
            Hide();
            return;
        }

        if (e.KeyCode == Keys.Up && _hover > 0)
        {
            _hover--;
            e.Handled = true;
            Render();
            return;
        }

        if (e.KeyCode == Keys.Down && _hover >= 0 && _hover < VisibleCount - 1)
        {
            _hover++;
            e.Handled = true;
            Render();
            return;
        }

        if (e.KeyCode == Keys.Enter && _hover >= 0 && _hover < Items.Count)
        {
            e.Handled = true;
            Activate(Items[_hover]);
            return;
        }

        base.OnKeyDown(e);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        var hover = HitRow(e.Location);
        var closeHover = CloseRect().Contains(e.Location);
        var clearHover = _clearRect.Contains(e.Location);
        if (hover != _hover || closeHover != _closeHover || clearHover != _clearHover)
        {
            _hover = hover;
            _closeHover = closeHover;
            _clearHover = clearHover;
            Cursor = hover >= 0 || closeHover || clearHover ? Cursors.Hand : Cursors.Default;
            Render();
        }

        base.OnMouseMove(e);
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        if (_hover != -1 || _closeHover || _clearHover)
        {
            _hover = -1;
            _closeHover = false;
            _clearHover = false;
            Render();
        }

        base.OnMouseLeave(e);
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Left)
        {
            if (CloseRect().Contains(e.Location))
            {
                Hide();
                return;
            }

            if (_clearRect.Contains(e.Location) && Items.Count > 0)
            {
                _service.ClearAll(this);
                _scroll = 0;
                Render();
                return;
            }
        }

        if (e.Button == MouseButtons.Right)
        {
            var index = HitRow(e.Location);
            if (index >= 0 && index < Items.Count)
            {
                ShowItemMenu(Items[index], e.Location);
            }
        }

        base.OnMouseUp(e);
    }

    protected override void OnMouseDoubleClick(MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Left && !CloseRect().Contains(e.Location))
        {
            var index = HitRow(e.Location);
            if (index >= 0 && index < Items.Count)
            {
                Activate(Items[index]);
                return;
            }
        }

        base.OnMouseDoubleClick(e);
    }

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        var maxScroll = Math.Max(0, Items.Count - VisibleCount);
        if (maxScroll > 0)
        {
            _scroll = Math.Clamp(_scroll - Math.Sign(e.Delta) * 2, 0, maxScroll);
            _hover = -1;
            Render();
        }

        base.OnMouseWheel(e);
    }

    protected override void OnDpiChanged(DpiChangedEventArgs e)
    {
        base.OnDpiChanged(e);
        Render();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _service.Changed -= OnServiceChanged;
            _menu.Dispose();
            foreach (var bitmap in _thumbs.Values)
            {
                bitmap.Dispose();
            }

            _thumbs.Clear();
        }

        base.Dispose(disposing);
    }

    private void OnServiceChanged()
    {
        if (!IsDisposed && Visible)
        {
            Render();
        }
    }

    private Rectangle CardRect() => new(Shadow, Shadow, CardWidth, Height - (Shadow * 2));

    private Rectangle CloseRect()
    {
        var card = CardRect();
        var side = S(28);
        return new Rectangle(card.Right - side - S(10), card.Top + ((HeaderHeight - side) / 2), side, side);
    }

    private Rectangle BodyRect()
    {
        var card = CardRect();
        return new Rectangle(
            card.Left,
            card.Top + HeaderHeight,
            card.Width,
            VisibleCount * RowHeight);
    }

    private int HitRow(Point point)
    {
        var body = BodyRect();
        if (point.X < body.Left || point.X >= body.Right || point.Y < body.Top || point.Y >= body.Bottom)
        {
            return -1;
        }

        var index = ((point.Y - body.Top) / RowHeight) + _scroll;
        return index >= 0 && index < Items.Count ? index : -1;
    }

    private void Activate(ClipboardEntry entry)
    {
        _service.Activate(entry);
    }

    private void Render()
    {
        if (!IsHandleCreated || IsDisposed)
        {
            return;
        }

        var items = Items;
        var rows = Math.Max(3, Math.Min(VisibleRows, Math.Max(items.Count, 1)));
        var height = (Shadow * 2) + HeaderHeight + (rows * RowHeight) + FooterHeight;
        if (ClientSize.Height != height || ClientSize.Width != CardWidth + (Shadow * 2))
        {
            ClientSize = new Size(CardWidth + (Shadow * 2), height);
        }

        using var bitmap = Compose(items);
        LayeredWindow.Update(Handle, Location, bitmap);
    }

    /// <summary>Dev preview: the flyout paints itself, so WM_PRINT would come back empty.</summary>
    internal Bitmap Snapshot()
    {
        var items = Items;
        var rows = Math.Max(3, Math.Min(VisibleRows, Math.Max(items.Count, 1)));
        ClientSize = new Size(CardWidth + (Shadow * 2), (Shadow * 2) + HeaderHeight + (rows * RowHeight) + FooterHeight);
        return Compose(items);
    }

    private Bitmap Compose(IReadOnlyList<ClipboardEntry> items)
    {
        _rowBounds.Clear();
        var bitmap = new Bitmap(Math.Max(1, Width), Math.Max(1, Height), PixelFormat.Format32bppPArgb);
        using var g = Graphics.FromImage(bitmap);
        g.Clear(Color.Transparent);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        // ClearType fringes badly on a premultiplied-alpha layered surface.
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
        Draw(g, items);
        return bitmap;
    }

    private void Draw(Graphics g, IReadOnlyList<ClipboardEntry> items)
    {
        var card = new RectangleF(CardRect().X, CardRect().Y, CardRect().Width, CardRect().Height);
        UiKit.Shadow(g, RectangleF.Inflate(card, -1f, -1f), S(12), S(8), 46);
        UiKit.FillAndStroke(g, RectangleF.Inflate(card, -0.5f, -0.5f), S(12), Theme.Card, Theme.CardBorder);

        PaintHeader(g, card, items.Count);
        PaintRows(g, items);
        PaintFooter(g, card, items.Count);
    }

    private void PaintHeader(Graphics g, RectangleF card, int count)
    {
        var titleRect = new Rectangle(
            (int)card.Left + S(18),
            (int)card.Top + S(11),
            (int)card.Width - S(70),
            S(24));
        DrawText(g, "剪贴板", UiFonts.BodyBold, titleRect, Theme.Text);

        var subtitle = count == 0 ? "暂无记录" : $"{count} 条记录 · 双击打开";
        var subtitleRect = new Rectangle(titleRect.Left, titleRect.Bottom + S(2), titleRect.Width, S(17));
        DrawText(g, subtitle, UiFonts.Caption, subtitleRect, Theme.Muted);

        var close = CloseRect();
        if (_closeHover)
        {
            UiKit.Fill(g, close, S(6), Theme.Hover);
        }

        var glyph = Rectangle.Inflate(close, -S(7), -S(7));
        UiKit.DrawGlyph(g, SnipIcons.Close, glyph, _closeHover ? Theme.Text : Theme.Muted);

        using var divider = new Pen(Theme.Divider, 1f);
        var y = card.Top + HeaderHeight - 0.5f;
        g.DrawLine(divider, card.Left + S(1), y, card.Right - S(1), y);
    }

    private void PaintRows(Graphics g, IReadOnlyList<ClipboardEntry> items)
    {
        var body = BodyRect();
        if (items.Count == 0)
        {
            var empty = new Rectangle(body.Left, body.Top, body.Width, body.Height);
            DrawText(g, "复制点什么，这里就会出现记录", UiFonts.Small, empty, Theme.Muted, StringAlignment.Center);
            return;
        }

        var maxScroll = Math.Max(0, items.Count - VisibleCount);
        _scroll = Math.Clamp(_scroll, 0, maxScroll);
        for (var i = 0; i < VisibleCount; i++)
        {
            var index = _scroll + i;
            if (index >= items.Count)
            {
                break;
            }

            var row = new Rectangle(body.Left + S(6), body.Top + (i * RowHeight), body.Width - S(12), RowHeight);
            _rowBounds.Add(row);
            PaintRow(g, items[index], row, index == _hover);
        }

        if (maxScroll > 0)
        {
            var track = new Rectangle(body.Right - S(6), body.Top + S(6), S(3), body.Height - S(12));
            UiKit.Fill(g, track, S(2), UiKit.Alpha(Theme.Muted, 40));
            var thumbHeight = Math.Max(S(24), (int)(track.Height * (VisibleCount / (float)items.Count)));
            var travel = track.Height - thumbHeight;
            var offset = maxScroll == 0 ? 0 : (int)(travel * (_scroll / (float)maxScroll));
            UiKit.Fill(g, new Rectangle(track.X, track.Y + offset, track.Width, thumbHeight), S(2), UiKit.Alpha(Theme.Muted, 120));
        }
    }

    private void PaintRow(Graphics g, ClipboardEntry entry, Rectangle row, bool hover)
    {
        if (hover)
        {
            UiKit.Fill(g, row, S(8), Theme.Hover);
        }

        var side = S(42);
        var thumb = new Rectangle(row.Left + S(10), row.Top + ((row.Height - side) / 2), side, side);
        PaintThumb(g, entry, thumb);

        var pinWidth = entry.Pinned ? S(26) : 0;
        var textLeft = thumb.Right + S(12);
        var textWidth = row.Right - textLeft - S(10) - pinWidth;

        var title = UiKit.Elide(entry.Headline, UiFonts.Body, textWidth);
        DrawText(g, title, UiFonts.Body, new Rectangle(textLeft, row.Top + S(13), textWidth, S(22)), Theme.Text);

        var meta = entry.KindLabel + " · " + UiKit.RelativeTime(entry.CreatedUtc);
        DrawText(g, meta, UiFonts.Caption, new Rectangle(textLeft, row.Top + S(37), textWidth, S(17)), Theme.Muted);

        if (entry.Pinned)
        {
            var pin = new Rectangle(row.Right - S(28), row.Top + ((row.Height - S(16)) / 2), S(16), S(16));
            UiKit.DrawGlyph(g, SnipIcons.Pin, pin, Theme.Accent);
        }
    }

    private void PaintThumb(Graphics g, ClipboardEntry entry, Rectangle box)
    {
        if (entry.HasImage && LoadThumb(entry) is { } bitmap)
        {
            using var path = UiKit.Rounded(box, S(6));
            var previous = g.Clip;
            g.SetClip(path);
            UiKit.Fill(g, box, S(6), Theme.Field);
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            // Cover, not contain: a 16:9 shot shrunk to fit left a letterboxed sliver that
            // read as a dark smudge instead of a thumbnail.
            var scale = Math.Max(box.Width / (float)bitmap.Width, box.Height / (float)bitmap.Height);
            var width = Math.Max(1, (int)Math.Round(bitmap.Width * scale));
            var height = Math.Max(1, (int)Math.Round(bitmap.Height * scale));
            g.DrawImage(bitmap, new Rectangle(box.Left + ((box.Width - width) / 2), box.Top + ((box.Height - height) / 2), width, height));
            g.Clip = previous;
            UiKit.Stroke(g, box, S(6), UiKit.Alpha(Theme.Text, 26));
            return;
        }

        UiKit.Fill(g, box, S(6), Theme.AccentSoft);
        var glyphBox = Rectangle.Inflate(box, -S(10), -S(10));
        Action<Graphics, Rectangle, Color> glyph = entry.Kind == ClipboardKind.Files ? SnipIcons.Folder : SnipIcons.Clipboard;
        UiKit.DrawGlyph(g, glyph, glyphBox, Theme.Accent);
    }

    private Bitmap? LoadThumb(ClipboardEntry entry)
    {
        if (_thumbs.TryGetValue(entry.Id, out var cached))
        {
            return cached;
        }

        try
        {
            var path = _service.Store.ThumbPath(entry);
            if (!File.Exists(path))
            {
                return null;
            }

            var bitmap = new Bitmap(path);
            _thumbs[entry.Id] = bitmap;
            return bitmap;
        }
        catch (Exception ex) when (ex is IOException or ArgumentException or OutOfMemoryException)
        {
            return null;
        }
    }

    private void PaintFooter(Graphics g, RectangleF card, int count)
    {
        var top = card.Bottom - FooterHeight;
        using (var divider = new Pen(Theme.Divider, 1f))
        {
            g.DrawLine(divider, card.Left + S(1), top + 0.5f, card.Right - S(1), top + 0.5f);
        }

        var clearWidth = TextRenderer.MeasureText("清空历史", UiFonts.Small).Width + S(20);
        _clearRect = new Rectangle((int)card.Left + S(10), (int)top + S(8), clearWidth, FooterHeight - S(16));
        if (count > 0)
        {
            if (_clearHover)
            {
                UiKit.Fill(g, _clearRect, S(6), UiKit.Alpha(Theme.Danger, 22));
            }

            DrawText(g, "清空历史", UiFonts.Small, _clearRect, Theme.Danger, StringAlignment.Center);
        }
        else
        {
            DrawText(g, "清空历史", UiFonts.Small, _clearRect, UiKit.Alpha(Theme.Muted, 110), StringAlignment.Center);
        }

        var hint = new Rectangle((int)card.Right - S(160), (int)top + S(8), S(150), FooterHeight - S(16));
        DrawText(g, "双击打开 · Esc 关闭", UiFonts.Caption, hint, Theme.Muted, StringAlignment.Far);
    }

    /// <summary>
    /// GDI+ text: TextRenderer goes through GDI, whose ClearType fringing looks grubby
    /// on a premultiplied-alpha layered surface.
    /// </summary>
    private static void DrawText(Graphics g, string text, Font font, Rectangle box, Color color, StringAlignment align = StringAlignment.Near)
    {
        using var format = new StringFormat(StringFormat.GenericTypographic)
        {
            Alignment = align,
            LineAlignment = StringAlignment.Center,
            Trimming = StringTrimming.EllipsisCharacter,
            FormatFlags = StringFormatFlags.NoWrap,
        };
        using var brush = new SolidBrush(color);
        g.DrawString(text, font, brush, box, format);
    }

    private Rectangle _clearRect = Rectangle.Empty;

    // ── per-item context menu ───────────────────────────────────────────────
    private ContextMenuStrip BuildMenu()
    {
        var menu = new ContextMenuStrip
        {
            Renderer = new FluentMenuRenderer(),
            BackColor = Theme.Card,
            ForeColor = Theme.Text,
            Font = UiFonts.Body,
            ShowImageMargin = true,
            Padding = new Padding(4),
            DropShadowEnabled = true,
        };
        return menu;
    }

    private void ShowItemMenu(ClipboardEntry entry, Point location)
    {
        FillItemMenu(_menu, entry);
        _menuOpen = true;
        try
        {
            _menu.Show(this, location);
            _menu.Closed += OnMenuClosed;
        }
        catch (InvalidOperationException)
        {
            _menuOpen = false;
        }
    }

    /// <summary>Dev-only: builds the same menu off-screen so it can be captured.</summary>
    internal ContextMenuStrip BuildItemMenuForPreview(ClipboardEntry entry)
    {
        var menu = BuildMenu();
        FillItemMenu(menu, entry);
        return menu;
    }

    private void FillItemMenu(ContextMenuStrip menu, ClipboardEntry entry)
    {
        menu.Items.Clear();
        var primary = entry.Kind == ClipboardKind.Text ? "复制" : "打开";
        Add(menu, entry.Kind == ClipboardKind.Text ? SnipIcons.Copy : SnipIcons.Open, primary, () => Activate(entry), bold: true);

        if (entry.Kind != ClipboardKind.Text)
        {
            Add(menu, SnipIcons.Copy, "复制到剪贴板", () => _service.CopyEntry(entry));
        }

        if (entry.Kind == ClipboardKind.Files)
        {
            Add(menu, SnipIcons.Copy, "复制路径", () => _service.CopyPath(entry));
        }

        Add(menu, SnipIcons.SaveAs, "另存为…", () => _service.SaveAs(entry, this));
        Add(menu, SnipIcons.Folder, entry.Kind == ClipboardKind.Image ? "在文件夹中显示" : "打开所在文件夹",
            () => _service.Reveal(entry));
        menu.Items.Add(new ToolStripSeparator());
        Add(menu, SnipIcons.Pin, entry.Pinned ? "取消置顶" : "置顶", () => _service.TogglePin(entry));
        Add(menu, SnipIcons.Trash, "删除", () => _service.Delete(entry));
        menu.Items.Add(new ToolStripSeparator());
        Add(menu, SnipIcons.Trash, "清空历史", () => _service.ClearAll(this));
    }

    private void OnMenuClosed(object? sender, ToolStripDropDownClosedEventArgs e)
    {
        _menu.Closed -= OnMenuClosed;
        _menuOpen = false;
        if (Visible)
        {
            Activate();
            Render();
        }
    }

    private void Add(
        ContextMenuStrip menu,
        Action<Graphics, Rectangle, Color>? glyph,
        string text,
        Action action,
        bool bold = false)
    {
        var item = new ToolStripMenuItem(text)
        {
            ForeColor = Theme.Text,
            Font = bold ? UiFonts.BodyBold : UiFonts.Body,
            Padding = new Padding(4),
        };
        if (glyph != null)
        {
            item.Image = UiKit.GlyphBitmap(glyph, Theme.Text, S(18));
            item.ImageScaling = ToolStripItemImageScaling.None;
        }

        item.Click += (_, _) => action();
        menu.Items.Add(item);
    }
}

/// <summary>Flat, rounded menu rows that match the app instead of the 1998 toolstrip look.</summary>
internal sealed class FluentMenuRenderer : ToolStripProfessionalRenderer
{
    public FluentMenuRenderer()
        : base(new FluentColorTable())
    {
        RoundedEdges = true;
    }

    protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e)
    {
        using var brush = new SolidBrush(Theme.Card);
        e.Graphics.FillRectangle(brush, e.AffectedBounds);
    }

    protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e)
    {
        using var pen = new Pen(Theme.CardBorder);
        var rect = new Rectangle(0, 0, e.ToolStrip.Width - 1, e.ToolStrip.Height - 1);
        e.Graphics.DrawRectangle(pen, rect);
    }

    protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
    {
        var item = e.Item;
        var bounds = new RectangleF(2, 1, item.Width - 4, item.Height - 2);
        if (item.Selected && item.Enabled)
        {
            UiKit.Fill(e.Graphics, bounds, 6, Theme.IsDark ? Theme.Hover : Theme.Selection);
        }
        else if (!item.Enabled)
        {
            using var brush = new SolidBrush(UiKit.Alpha(Theme.Muted, 120));
            e.Graphics.FillRectangle(brush, bounds);
        }
    }

    protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
    {
        // Shortcut columns must read as secondary information, not as another label.
        var isShortcut = e.Item is ToolStripMenuItem { ShortcutKeyDisplayString: not null } item &&
                         string.Equals(e.Text, item.ShortcutKeyDisplayString, StringComparison.Ordinal);
        e.TextColor = !e.Item.Enabled ? UiKit.Alpha(Theme.Muted, 140) : isShortcut ? Theme.Muted : Theme.Text;
        base.OnRenderItemText(e);
    }

    protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e)
    {
        var y = e.Item.Height / 2;
        using var pen = new Pen(Theme.Divider);
        e.Graphics.DrawLine(pen, 10, y, e.Item.Width - 10, y);
    }

    protected override void OnRenderImageMargin(ToolStripRenderEventArgs e)
    {
        // The icon column must share the card colour; leaving it unpainted showed the
        // window behind through the strip.
        using var brush = new SolidBrush(Theme.Card);
        e.Graphics.FillRectangle(brush, e.AffectedBounds);
    }
}

internal sealed class FluentColorTable : ProfessionalColorTable
{
    public override Color ToolStripDropDownBackground => Theme.Card;

    public override Color ImageMarginGradientBegin => Theme.Card;

    public override Color ImageMarginGradientMiddle => Theme.Card;

    public override Color ImageMarginGradientEnd => Theme.Card;

    public override Color MenuBorder => Theme.CardBorder;

    public override Color MenuItemBorder => Color.Transparent;

    public override Color MenuItemSelected => Theme.Hover;

    public override Color MenuItemSelectedGradientBegin => Theme.Hover;

    public override Color MenuItemSelectedGradientEnd => Theme.Hover;

    public override Color SeparatorDark => Theme.Divider;

    public override Color SeparatorLight => Theme.Divider;
}
