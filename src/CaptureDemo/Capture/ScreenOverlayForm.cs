using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Runtime.InteropServices;
using CaptureDemo.Native;

namespace CaptureDemo.Capture;

/// <summary>
/// Full-virtual-screen click-through overlay. The desktop is dimmed and the
/// selection is a fully transparent hole, so the chosen pixels stay at their real brightness.
/// A color key cannot do both: it would also punch out the dim.
/// </summary>
internal abstract class ScreenOverlayForm : Form
{
    /// <summary>About 60% black. The hole is alpha 0, so the contrast matches a dimmed desktop with a bright cutout.</summary>
    public const byte DimAlpha = 153;

    private Bitmap? _surface;

    protected ScreenOverlayForm()
    {
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        TopMost = true;
        BackColor = Color.Black;
        AutoScaleMode = AutoScaleMode.None;
        DoubleBuffered = false;
        Font = UiFonts.Body;
        Bounds = NativeMethods.VirtualScreen();
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

    protected abstract void PaintMask();

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        WindowAffinity.TryExcludeFromCapture(Handle);
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        Bounds = NativeMethods.VirtualScreen();
        TopMost = true;
        PaintMask();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
    }

    protected override void OnPaintBackground(PaintEventArgs e)
    {
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _surface?.Dispose();
            _surface = null;
        }

        base.Dispose(disposing);
    }

    protected void PresentMask(Rectangle? holeScreen, Color border, string line1, string? line2, string? caption)
    {
        if (IsDisposed)
        {
            return;
        }

        if (InvokeRequired)
        {
            BeginInvoke(() => PresentMask(holeScreen, border, line1, line2, caption));
            return;
        }

        if (!IsHandleCreated)
        {
            return;
        }

        var bounds = Bounds;
        if (bounds.Width < 2 || bounds.Height < 2)
        {
            return;
        }

        EnsureSurface(bounds.Width, bounds.Height);
        var surface = _surface!;
        Fill(surface, 0, 0, 0, DimAlpha);
        var hole = holeScreen is Rectangle screen && screen.Width > 1 && screen.Height > 1
            ? Rectangle.Intersect(
                new Rectangle(screen.X - bounds.X, screen.Y - bounds.Y, screen.Width, screen.Height),
                new Rectangle(0, 0, surface.Width, surface.Height))
            : Rectangle.Empty;
        if (hole.Width > 1 && hole.Height > 1)
        {
            ClearRect(surface, hole);
        }

        using (var graphics = Graphics.FromImage(surface))
        {
            graphics.SmoothingMode = SmoothingMode.None;
            graphics.PixelOffsetMode = PixelOffsetMode.Half;
            graphics.CompositingMode = CompositingMode.SourceOver;
            graphics.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            if (hole.Width > 1 && hole.Height > 1)
            {
                DrawOutsideBorder(graphics, hole, border, 3);
                OverlayPainter.DrawCaption(graphics, hole, surface.Size, border, Font, caption);
            }

            OverlayPainter.DrawBanner(graphics, surface.Size, Font, line1, line2, hole.Width > 1 ? hole : null);
        }

        LayeredWindow.Update(Handle, bounds.Location, surface);
    }

    /// <summary>Dim the desktop, then let the caller punch a hole, blit a frozen image, or draw a lasso.</summary>
    protected void PresentScene(Action<Bitmap, Graphics> paint)
    {
        if (IsDisposed)
        {
            return;
        }

        if (InvokeRequired)
        {
            BeginInvoke(() => PresentScene(paint));
            return;
        }

        if (!IsHandleCreated)
        {
            return;
        }

        var bounds = Bounds;
        if (bounds.Width < 2 || bounds.Height < 2)
        {
            return;
        }

        EnsureSurface(bounds.Width, bounds.Height);
        var surface = _surface!;
        Fill(surface, 0, 0, 0, DimAlpha);
        using (var graphics = Graphics.FromImage(surface))
        {
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            graphics.PixelOffsetMode = PixelOffsetMode.Half;
            graphics.CompositingMode = CompositingMode.SourceOver;
            graphics.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            paint(surface, graphics);
        }

        LayeredWindow.Update(Handle, bounds.Location, surface);
    }

    private void EnsureSurface(int width, int height)
    {
        if (_surface != null && _surface.Width == width && _surface.Height == height)
        {
            return;
        }

        _surface?.Dispose();
        _surface = new Bitmap(width, height, PixelFormat.Format32bppPArgb);
    }

    private static void Fill(Bitmap bitmap, byte blue, byte green, byte red, byte alpha)
    {
        var preBlue = (byte)(blue * alpha / 255);
        var preGreen = (byte)(green * alpha / 255);
        var preRed = (byte)(red * alpha / 255);
        var row = new byte[bitmap.Width * 4];
        for (var x = 0; x < bitmap.Width; x++)
        {
            var i = x * 4;
            row[i] = preBlue;
            row[i + 1] = preGreen;
            row[i + 2] = preRed;
            row[i + 3] = alpha;
        }

        BlitRows(bitmap, new Rectangle(0, 0, bitmap.Width, bitmap.Height), row, bitmap.Width, ImageLockMode.WriteOnly);
    }

    protected static void ClearRect(Bitmap bitmap, Rectangle rect)
    {
        var zeros = new byte[rect.Width * 4];
        BlitRows(bitmap, rect, zeros, rect.Width, ImageLockMode.ReadWrite);
    }

    private static void BlitRows(Bitmap bitmap, Rectangle rect, byte[] rowTemplate, int templateWidth, ImageLockMode mode)
    {
        var data = bitmap.LockBits(new Rectangle(0, 0, bitmap.Width, bitmap.Height), mode, PixelFormat.Format32bppPArgb);
        try
        {
            var copy = templateWidth * 4;
            var stride = data.Stride;
            for (var y = 0; y < rect.Height; y++)
            {
                var rowIndex = rect.Y + y;
                var rowStart = stride >= 0
                    ? data.Scan0 + rowIndex * stride
                    : data.Scan0 + (bitmap.Height - 1 - rowIndex) * stride;
                Marshal.Copy(rowTemplate, 0, rowStart + rect.X * 4, copy);
            }
        }
        finally
        {
            bitmap.UnlockBits(data);
        }
    }

    protected static void DrawOutsideBorder(Graphics graphics, Rectangle hole, Color color, int thickness)
    {
        using var brush = new SolidBrush(color);
        graphics.FillRectangle(brush, hole.X - thickness, hole.Y - thickness, hole.Width + thickness * 2, thickness);
        graphics.FillRectangle(brush, hole.X - thickness, hole.Bottom, hole.Width + thickness * 2, thickness);
        graphics.FillRectangle(brush, hole.X - thickness, hole.Y, thickness, hole.Height);
        graphics.FillRectangle(brush, hole.Right, hole.Y, thickness, hole.Height);
    }
}
