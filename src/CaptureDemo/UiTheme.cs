using System.Drawing.Text;
using Microsoft.Win32;

namespace CaptureDemo;

internal enum Appearance
{
    System,
    Light,
    Dark,
}

/// <summary>
/// One palette, resolved once at startup from the user's choice (or the OS
/// light/dark preference). Capture chrome stays dark on purpose: it sits on top
/// of the dimmed desktop, not on the app background.
/// </summary>
internal static class Theme
{
    private static bool _dark;

    public static bool IsDark => _dark;

    public static void Apply(Appearance appearance)
    {
        _dark = appearance switch
        {
            Appearance.Dark => true,
            Appearance.Light => false,
            _ => SystemPrefersDark(),
        };
    }

    public static Color AppBack => _dark ? Rgb(24, 25, 28) : Rgb(244, 245, 247);

    public static Color Card => _dark ? Rgb(38, 39, 43) : Color.White;

    public static Color CardBorder => _dark ? Rgb(58, 59, 64) : Rgb(225, 227, 232);

    public static Color Text => _dark ? Rgb(240, 241, 245) : Rgb(23, 24, 27);

    public static Color Muted => _dark ? Rgb(150, 152, 160) : Rgb(104, 106, 116);

    public static Color Accent => _dark ? Rgb(76, 152, 255) : Rgb(21, 101, 192);

    public static Color AccentText => _dark ? Rgb(12, 22, 38) : Color.White;

    public static Color AccentSoft => _dark ? Rgb(30, 48, 76) : Rgb(227, 240, 254);

    public static Color Hover => _dark ? Rgb(52, 53, 58) : Rgb(240, 242, 246);

    public static Color Pressed => _dark ? Rgb(62, 64, 70) : Rgb(230, 233, 238);

    public static Color Divider => _dark ? Rgb(52, 53, 58) : Rgb(233, 235, 240);

    public static Color Field => _dark ? Rgb(30, 31, 35) : Rgb(250, 251, 252);

    public static Color Danger => _dark ? Rgb(255, 110, 110) : Rgb(197, 40, 40);

    public static Color Selection => _dark ? Rgb(40, 62, 96) : Rgb(219, 234, 254);

    // Capture chrome is always dark: it floats over the dimmed desktop.
    public static Color FluentBack => AppBack;

    public static Color LongAccent => Rgb(14, 116, 144);

    public static Color Banner => Rgb(28, 32, 38);

    public static Color BannerHint => Rgb(253, 224, 71);

    public static Color DragBorder => Rgb(234, 88, 12);

    public static Color MaskBorder => Rgb(96, 165, 250);

    public static Color LongMaskBorder => Rgb(103, 232, 249);

    public static Color SnipInk => Rgb(245, 245, 245);

    public static Color SnipAccentLine => Rgb(96, 205, 255);

    public static Color SnipBar => Rgb(32, 32, 32);

    private static Color Rgb(int r, int g, int b) => Color.FromArgb(r, g, b);

    private static bool SystemPrefersDark()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return key?.GetValue("AppsUseLightTheme") is int value && value == 0;
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException)
        {
            return false;
        }
    }
}

internal static class UiFonts
{
    public static Font Title { get; } = Create(17f, FontStyle.Bold);

    public static Font Body { get; } = Create(10.5f, FontStyle.Regular);

    public static Font BodyBold { get; } = Create(10.5f, FontStyle.Bold);

    public static Font Small { get; } = Create(9f, FontStyle.Regular);

    public static Font ButtonTitle { get; } = Create(14f, FontStyle.Bold);

    public static Font Section { get; } = Create(9.5f, FontStyle.Bold);

    public static Font Caption { get; } = Create(8.5f, FontStyle.Regular);

    private static Font Create(float size, FontStyle style)
    {
        foreach (var name in new[] { "Microsoft YaHei UI", "Microsoft YaHei", "Segoe UI", "Tahoma" })
        {
            try
            {
                if (!IsInstalled(name))
                {
                    continue;
                }

                return new Font(name, size, style, GraphicsUnit.Point);
            }
            catch
            {
                // Try the next installed family.
            }
        }

        return new Font(FontFamily.GenericSansSerif, size, style, GraphicsUnit.Point);
    }

    private static bool IsInstalled(string name)
    {
        using var installed = new InstalledFontCollection();
        foreach (var family in installed.Families)
        {
            if (family.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}
