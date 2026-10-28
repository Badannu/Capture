using Xunit;

namespace CaptureDemo.Core.Tests;

/// <summary>
/// Renaming the product touched storage paths, and the new tray-residency switch
/// decides whether closing the window quits. Both are easy to break silently.
/// </summary>
public class BrandingAndTrayTests
{
    [Fact]
    public void DefaultLocationsUseTheProductName()
    {
        Assert.Equal(AppInfo.Product, Path.GetFileName(AppSettings.DefaultDirectory()));
        Assert.Equal("settings.json", Path.GetFileName(AppSettings.DefaultFilePath()));
        Assert.Contains(
            Path.DirectorySeparatorChar + AppInfo.Product + Path.DirectorySeparatorChar,
            AppSettings.DefaultFilePath());
    }

    [Fact]
    public void LegacyFilePointsAtTheOldProductFolder()
    {
        var legacy = AppSettings.LegacyFilePath();
        Assert.Equal("settings.json", Path.GetFileName(legacy));
        Assert.Equal("CaptureDemo", Path.GetFileName(Path.GetDirectoryName(legacy)));
        Assert.NotEqual(AppSettings.DefaultFilePath(), legacy);
    }

    [Fact]
    public void ClosingTheWindowStaysInTheTrayByDefault()
    {
        // Users who dislike it can switch it off; the default keeps the hotkeys alive,
        // which is what an earlier version promised in the header text.
        Assert.True(AppSettings.CreateDefault().CloseToTray);
    }

    [Fact]
    public void SettingsCarryOverFromTheLegacyFolderOnce()
    {
        var root = Path.Combine(Path.GetTempPath(), "capture-migrate-" + Guid.NewGuid().ToString("N"));
        var legacy = Path.Combine(root, "CaptureDemo", "settings.json");
        var current = Path.Combine(root, AppInfo.Product, "settings.json");
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(legacy)!);
            var old = AppSettings.CreateDefault();
            old.AwakenHotkey = "Ctrl+Shift+F9";
            old.CloseToTray = false;
            old.Save(legacy);

            var migrated = AppSettings.LoadForApp(current, legacy);
            Assert.Equal("Ctrl+Shift+F9", migrated.AwakenHotkey);
            Assert.False(migrated.CloseToTray);
            Assert.True(File.Exists(current), "the migrated settings were not written to the new path");

            // The next launch reads the new file, so later edits are not fought over.
            File.Delete(legacy);
            var again = AppSettings.LoadForApp(current, legacy);
            Assert.Equal("Ctrl+Shift+F9", again.AwakenHotkey);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, true);
            }
        }
    }

    [Fact]
    public void MissingFilesFallBackToDefaults()
    {
        var root = Path.Combine(Path.GetTempPath(), "capture-empty-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var loaded = AppSettings.LoadForApp(
                Path.Combine(root, "settings.json"),
                Path.Combine(root, "legacy.json"));
            Assert.Equal(AppSettings.DefaultAwakenHotkey, loaded.AwakenHotkey);
            Assert.False(File.Exists(Path.Combine(root, "settings.json")));
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }
}
