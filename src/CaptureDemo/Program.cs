using CaptureDemo.Core;

namespace CaptureDemo;

internal enum LaunchMode
{
    Menu,
    Element,
    Long,
    Snip,
    Clipboard,
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
                case "clipboard":
                case "clip":
                case "v":
                    return LaunchMode.Clipboard;
            }
        }

        return LaunchMode.Menu;
    }

    /// <summary>Dev hook: <c>--preview &lt;folder&gt;</c> renders contact sheets and exits.</summary>
    public static string? PreviewFolder(string[] args)
    {
        for (var i = 0; i < args.Length; i++)
        {
            var token = args[i].Trim().TrimStart('-', '/').ToLowerInvariant();
            if (token == "preview" && i + 1 < args.Length)
            {
                return args[i + 1];
            }
        }

        return null;
    }

    /// <summary>Dev hook: <c>--ui-demo &lt;folder&gt;</c> renders the real forms and exits.</summary>
    public static string? UiDemoFolder(string[] args)
    {
        for (var i = 0; i < args.Length; i++)
        {
            var token = args[i].Trim().TrimStart('-', '/').ToLowerInvariant();
            if (token == "ui-demo" && i + 1 < args.Length)
            {
                return args[i + 1];
            }
        }

        return null;
    }
}

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        var preview = LaunchModeParser.PreviewFolder(args);
        if (preview != null)
        {
            Dev.RenderPreview.Render(preview);
            return;
        }

        var demo = LaunchModeParser.UiDemoFolder(args);
        if (demo != null)
        {
            ApplicationConfiguration.Initialize();
            Dev.UiDemo.Run(demo);
            return;
        }

        if (args.Length >= 2 && args[0].TrimStart('-', '/').Equals("icons", StringComparison.OrdinalIgnoreCase))
        {
            ApplicationConfiguration.Initialize();
            Dev.IconSheet.Run(args[1]);
            return;
        }

        if (args.Length >= 1 && args[0].TrimStart('-', '/').Equals("bench", StringComparison.OrdinalIgnoreCase))
        {
            ApplicationConfiguration.Initialize();
            Dev.AnnoBench.Run(args.Length > 1 ? args[1] : "bench.txt");
            return;
        }

        if (args.Length >= 8 && args[0].TrimStart('-', '/').Equals("crop", StringComparison.OrdinalIgnoreCase))
        {
            Dev.RenderPreview.Crop(
                args[1],
                args[2],
                int.Parse(args[3]),
                int.Parse(args[4]),
                int.Parse(args[5]),
                int.Parse(args[6]),
                int.Parse(args[7]));
            return;
        }

        if (args.Length >= 1 && args[0].TrimStart('-', '/').Equals("version", StringComparison.OrdinalIgnoreCase))
        {
            ApplicationConfiguration.Initialize();
            // WinExe has no console, so a dialog is the only way to answer on a double-click.
            MessageBox.Show(
                AppInfo.Label + Environment.NewLine + "保存目录：" + AppSettings.DefaultDirectory(),
                "关于",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            return;
        }

        var mode = LaunchModeParser.Parse(args);
        if (!Native.SingleInstance.TryAcquire())
        {
            Native.SingleInstance.Signal((int)mode);
            return;
        }

        ApplicationConfiguration.Initialize();
        Application.Run(new MainForm(mode));
    }
}
