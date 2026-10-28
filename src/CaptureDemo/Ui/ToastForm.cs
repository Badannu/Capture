namespace CaptureDemo.Ui;

internal sealed class ToastForm : Form
{
    private readonly string? _path;
    private readonly System.Windows.Forms.Timer _timer;

    public ToastForm(string message, string? path)
    {
        _path = path;
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.Manual;
        ShowInTaskbar = false;
        TopMost = true;
        BackColor = Color.White;
        ForeColor = Theme.Text;
        Font = UiFonts.Body;
        ClientSize = new Size(440, message.Length > 70 ? 156 : 120);
        var label = new Label
        {
            Dock = DockStyle.Fill,
            Text = message,
            Padding = new Padding(16, 14, 16, 8),
            ForeColor = Theme.Text,
        };
        var hint = new Label
        {
            Dock = DockStyle.Bottom,
            Height = 28,
            Text = path == null ? "点击关闭" : "点击打开文件位置",
            ForeColor = Theme.Muted,
            TextAlign = ContentAlignment.MiddleLeft,
            Padding = new Padding(16, 0, 0, 6),
        };
        Controls.Add(label);
        Controls.Add(hint);

        _timer = new System.Windows.Forms.Timer { Interval = 4600 };
        _timer.Tick += (_, _) =>
        {
            _timer.Stop();
            Close();
        };
        _timer.Start();

        if (path != null)
        {
            Cursor = Cursors.Hand;
            Click += (_, _) => Open();
            label.Click += (_, _) => Open();
            hint.Click += (_, _) => Open();
        }
    }

    protected override bool ShowWithoutActivation => true;

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _timer.Stop();
            _timer.Dispose();
        }

        base.Dispose(disposing);
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        var area = Screen.PrimaryScreen?.WorkingArea ?? Screen.FromPoint(Cursor.Position).WorkingArea;
        Location = new Point(area.Right - Width - 20, area.Bottom - Height - 20);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        using var pen = new Pen(Theme.Accent, 2);
        e.Graphics.DrawRectangle(pen, 1, 1, Width - 3, Height - 3);
    }

    private void Open()
    {
        try
        {
            CaptureSaver.Reveal(_path);
        }
        catch
        {
            // Explorer can fail if the shell is unavailable; the path is still in the main window.
        }

        Close();
    }
}
