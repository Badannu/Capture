using CaptureDemo.Core;
using Xunit;

namespace CaptureDemo.Core.Tests;

/// <summary>
/// The portable package is just the exe plus a marker file, so the only thing that can
/// silently break it is the config root: a portable build that still writes to
/// %AppData% leaves files behind after the folder is deleted.
/// </summary>
public class PortableTests
{
    [Fact]
    public void FolderWithoutMarkerKeepsSettingsInAppData()
    {
        var root = Temp("capture-plain-");
        try
        {
            var appData = Path.Combine(root, "AppData");
            var path = AppSettings.SettingsFilePath(Path.Combine(root, "app"), appData);

            Assert.Equal(Path.Combine(appData, AppInfo.Product, "settings.json"), path);
        }
        finally
        {
            Clean(root);
        }
    }

    [Fact]
    public void MarkerNextToTheExeMovesSettingsBesideIt()
    {
        var root = Temp("capture-portable-");
        try
        {
            var app = Path.Combine(root, "app");
            Directory.CreateDirectory(app);
            File.WriteAllText(Path.Combine(app, AppSettings.PortableMarker), "portable\r\n");

            Assert.True(AppSettings.IsPortable(app));
            Assert.Equal(
                Path.Combine(app, "settings.json"),
                AppSettings.SettingsFilePath(app, Path.Combine(root, "AppData")));
        }
        finally
        {
            Clean(root);
        }
    }

    [Fact]
    public void MarkerIsIgnoredWhenItIsNotInTheGivenFolder()
    {
        // Guards against a marker found by walking up: only the exe's own folder counts.
        var root = Temp("capture-marker-parent-");
        try
        {
            var app = Path.Combine(root, "app");
            Directory.CreateDirectory(app);
            File.WriteAllText(Path.Combine(root, AppSettings.PortableMarker), "portable");

            Assert.False(AppSettings.IsPortable(app));
        }
        finally
        {
            Clean(root);
        }
    }

    [Fact]
    public void RunningFromTheBuildOutputIsNotPortable()
    {
        // The test host sits in bin\Debug, which ships no marker; if this ever flips,
        // every developer machine would start writing settings into the build output.
        Assert.False(AppSettings.IsPortable());
        Assert.Contains(
            Path.DirectorySeparatorChar + AppInfo.Product + Path.DirectorySeparatorChar,
            AppSettings.DefaultFilePath());
    }

    [Fact]
    public void ContactDetailsArePresentAndCopyable()
    {
        // The settings footer renders these verbatim and copies them to the clipboard,
        // so a stray space would break a mail client's "paste and send".
        Assert.False(string.IsNullOrWhiteSpace(AppInfo.ContactTelegram));
        Assert.DoesNotContain(" ", AppInfo.ContactTelegram);
        Assert.Equal(AppInfo.ContactEmail, AppInfo.ContactEmail.Trim());
        Assert.Contains("@", AppInfo.ContactEmail);
        Assert.DoesNotContain(" ", AppInfo.ContactEmail);
    }

    private static string Temp(string prefix)
    {
        var root = Path.Combine(Path.GetTempPath(), prefix + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }

    private static void Clean(string root)
    {
        if (Directory.Exists(root))
        {
            Directory.Delete(root, true);
        }
    }
}
