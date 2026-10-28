using System.Runtime.InteropServices;

namespace CaptureDemo.Ui;

/// <summary>
/// Window shell: DPI-scaled helpers, rounded corners and a title bar that follows
/// the app palette instead of the default white caption.
/// </summary>
internal class ShellForm : Form
{
    private const int DwmwaUseImmersiveDarkMode = 20;
    private const int DwmwaWindowCornerPreference = 33;
    private const int DwmwaCaptionColor = 35;
    private const int DwmwaTextColor = 36;
    private const int DwmwaBorderColor = 34;

    public ShellForm()
    {
        AutoScaleMode = AutoScaleMode.None;
        Font = UiFonts.Body;
        BackColor = Theme.AppBack;
        ForeColor = Theme.Text;
        DoubleBuffered = true;
        StartPosition = FormStartPosition.CenterScreen;
    }

    protected float DpiScale => DeviceDpi / 96f;

    protected int S(int px) => Math.Max(1, (int)Math.Round(px * DpiScale));

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        ApplyChrome(Handle);
    }

    protected static void ApplyChrome(IntPtr handle)
    {
        if (handle == IntPtr.Zero)
        {
            return;
        }

        var round = 2;
        Dwm(handle, DwmwaWindowCornerPreference, ref round);
        var dark = Theme.IsDark ? 1 : 0;
        Dwm(handle, DwmwaUseImmersiveDarkMode, ref dark);
        var caption = ToColorRef(Theme.AppBack);
        Dwm(handle, DwmwaCaptionColor, ref caption);
        var text = ToColorRef(Theme.Text);
        Dwm(handle, DwmwaTextColor, ref text);
        var border = ToColorRef(Theme.IsDark ? Color.FromArgb(58, 59, 64) : Color.FromArgb(219, 221, 226));
        Dwm(handle, DwmwaBorderColor, ref border);
    }

    private static int ToColorRef(Color color) => color.R | (color.G << 8) | (color.B << 16);

    private static void Dwm(IntPtr handle, int attribute, ref int value)
    {
        try
        {
            DwmSetWindowAttribute(handle, attribute, ref value, sizeof(int));
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            // Older Windows builds: keep the default frame.
        }
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
}
