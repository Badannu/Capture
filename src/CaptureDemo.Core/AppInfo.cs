using System.Reflection;

namespace CaptureDemo.Core;

/// <summary>
/// Product identity read from assembly metadata, so the number shown in the UI is
/// the same one stamped on the binary. Bump <c>Version</c> in Directory.Build.props
/// and every surface follows.
/// </summary>
public static class AppInfo
{
    /// <summary>User-facing product name. Internal namespaces and the exe file name
    /// stay CaptureDemo, so existing shortcuts and autostart entries keep working.</summary>
    public const string Product = "Capture";

    /// <summary>Marketing version, e.g. <c>1.2.0</c>. Never carries build metadata.</summary>
    public static string Version { get; } = Read();

    /// <summary>Ready-to-render label, e.g. <c>CaptureDemo v1.2.0</c>.</summary>
    public static string Label => Product + " v" + Version;

    private static string Read()
    {
        var assembly = typeof(AppInfo).Assembly;

        // .NET SDK auto-generates InformationalVersion from <Version>; SourceLink may
        // append "+<commit hash>", which is noise in a settings footer.
        var informational = assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion;
        if (!string.IsNullOrWhiteSpace(informational))
        {
            var plus = informational.IndexOf('+');
            return plus > 0 ? informational[..plus] : informational;
        }

        var version = assembly.GetName().Version;
        return version == null ? "0.0.0" : $"{version.Major}.{version.Minor}.{version.Build}";
    }
}
