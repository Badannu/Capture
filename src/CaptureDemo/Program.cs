namespace CaptureDemo;

internal enum LaunchMode
{
    Menu,
    Element,
    Long,
    Snip,
}

internal static class LaunchModeParser
{
    public static LaunchMode Parse(string[] args)
    {
        foreach (var raw in args)
        {
            var token = raw.Trim().TrimStart('-', '/').ToLowerInvariant();
            switch (token)
            {
                case "element":
                case "e":
                    return LaunchMode.Element;
                case "long":
                case "scroll":
                case "l":
                    return LaunchMode.Long;
                case "snip":
                case "s":
                    return LaunchMode.Snip;
            }
        }

        return LaunchMode.Menu;
    }
}

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        ApplicationConfiguration.Initialize();
        Application.Run(new MainForm(LaunchModeParser.Parse(args)));
    }
}
