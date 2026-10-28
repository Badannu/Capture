using System.Drawing.Text;

namespace CaptureDemo;

internal static class Theme
{
    public static readonly Color AppBack = Color.FromArgb(243, 244, 246);
    public static readonly Color Card = Color.White;
    public static readonly Color CardBorder = Color.FromArgb(218, 220, 224);
    public static readonly Color Text = Color.FromArgb(24, 24, 27);
    public static readonly Color Muted = Color.FromArgb(82, 82, 91);
    public static readonly Color Accent = Color.FromArgb(21, 101, 192);
    public static readonly Color AccentSoft = Color.FromArgb(227, 242, 253);
    public static readonly Color LongAccent = Color.FromArgb(14, 116, 144);
    public static readonly Color Banner = Color.FromArgb(28, 32, 38);
    public static readonly Color BannerHint = Color.FromArgb(253, 224, 71);
    public static readonly Color DragBorder = Color.FromArgb(234, 88, 12);
    public static readonly Color MaskBorder = Color.FromArgb(96, 165, 250);
    public static readonly Color LongMaskBorder = Color.FromArgb(103, 232, 249);
    public static readonly Color FluentBack = Color.FromArgb(243, 243, 243);
    public static readonly Color SnipInk = Color.FromArgb(245, 245, 245);
    public static readonly Color SnipAccentLine = Color.FromArgb(96, 205, 255);
    public static readonly Color SnipBar = Color.FromArgb(32, 32, 32);
}

internal static class UiFonts
{
    public static Font Title { get; } = Create(18f, FontStyle.Bold);

    public static Font Body { get; } = Create(10.5f, FontStyle.Regular);

    public static Font Small { get; } = Create(9f, FontStyle.Regular);

    public static Font ButtonTitle { get; } = Create(14f, FontStyle.Bold);

    public static Font Section { get; } = Create(12f, FontStyle.Bold);

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
