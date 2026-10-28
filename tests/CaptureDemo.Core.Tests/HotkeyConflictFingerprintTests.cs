using System.Text.Json;
using CaptureDemo.Core;
using Xunit;

namespace CaptureDemo.Core.Tests;

/// <summary>
/// A conflict usually comes from a program that is always there, which makes it permanent —
/// so "have we told the user about this one already?" has to be answerable from stable facts
/// alone. These tests pin what counts as stable and what must not.
/// </summary>
public class HotkeyConflictFingerprintTests
{
    private static readonly (string Action, HotkeyConflict Conflict) WeChatHoldsLongShot = (
        "长截图",
        new HotkeyConflict(HotkeyConflictKind.Taken, "Ctrl+Alt+A", "微信", "截图"));

    private static readonly (string Action, HotkeyConflict Conflict) ElementDuplicatesAwaken = (
        "元素截图",
        new HotkeyConflict(HotkeyConflictKind.Duplicate, "Ctrl+Alt+S", "唤醒截图条"));

    private static readonly (string Action, HotkeyConflict Conflict) WindowsOwnsClipboard = (
        "剪贴板历史",
        new HotkeyConflict(HotkeyConflictKind.Reserved, "Win+Shift+S", "Windows", "系统截图"));

    [Fact]
    public void FirstRunHasNoRecordSoItNotifies()
    {
        var current = HotkeyConflictFingerprint.Build(new[] { WeChatHoldsLongShot });

        Assert.NotEmpty(current);
        Assert.True(HotkeyConflictFingerprint.Changed(AppSettings.CreateDefault().HotkeyConflictNotice, current));
    }

    [Fact]
    public void SameConflictsInADifferentOrderAreNotNew()
    {
        var recorded = HotkeyConflictFingerprint.Build(new[] { WeChatHoldsLongShot, ElementDuplicatesAwaken });
        var current = HotkeyConflictFingerprint.Build(new[] { ElementDuplicatesAwaken, WeChatHoldsLongShot });

        Assert.Equal(recorded, current);
        Assert.False(HotkeyConflictFingerprint.Changed(recorded, current));
    }

    [Fact]
    public void OneMoreConflictIsNewAndOneFewerIsNewToo()
    {
        var recorded = HotkeyConflictFingerprint.Build(new[] { WeChatHoldsLongShot });

        var grown = HotkeyConflictFingerprint.Build(new[] { WeChatHoldsLongShot, ElementDuplicatesAwaken });
        var shrunk = HotkeyConflictFingerprint.Build(new[] { WeChatHoldsLongShot, ElementDuplicatesAwaken, WindowsOwnsClipboard });

        Assert.NotEqual(recorded, grown);
        Assert.NotEqual(grown, shrunk);
        Assert.True(HotkeyConflictFingerprint.Changed(recorded, grown));
        Assert.True(HotkeyConflictFingerprint.Changed(grown, shrunk));
    }

    [Fact]
    public void NoConflictsClearsTheRecordSoANewOneCanNotifyAgain()
    {
        var recorded = HotkeyConflictFingerprint.Build(new[] { WeChatHoldsLongShot, ElementDuplicatesAwaken });
        var current = HotkeyConflictFingerprint.Build(Array.Empty<(string, HotkeyConflict)>());

        // Nothing wrong: never pop, and drop what we remembered.
        Assert.Equal(string.Empty, current);
        Assert.False(HotkeyConflictFingerprint.Changed(recorded, current));

        // The same pair coming back later counts as new again instead of staying silent.
        var backAgain = HotkeyConflictFingerprint.Build(new[] { ElementDuplicatesAwaken, WeChatHoldsLongShot });
        Assert.True(HotkeyConflictFingerprint.Changed(current, backAgain));
    }

    [Fact]
    public void TheOwnerGuessIsLeftOutSoARunningProgramCannotMakeItLookNew()
    {
        var identified = new HotkeyConflict(HotkeyConflictKind.Taken, "Ctrl+Alt+A", "微信", "截图") { Note = "错误 1409" };
        var anonymous = new HotkeyConflict(HotkeyConflictKind.Taken, "Ctrl+Alt+A");

        var withGuess = HotkeyConflictFingerprint.Build(new[] { ("长截图", identified) });
        var withoutGuess = HotkeyConflictFingerprint.Build(new[] { ("长截图", anonymous) });

        Assert.Equal(withGuess, withoutGuess);
    }

    [Fact]
    public void HalfTypedChordsCompareEqualToTheirCanonicalForm()
    {
        var sloppy = HotkeyConflictFingerprint.Build(new[] { ("长截图", new HotkeyConflict(HotkeyConflictKind.Taken, "alt+ctrl+a")) });
        var canonical = HotkeyConflictFingerprint.Build(new[] { ("长截图", new HotkeyConflict(HotkeyConflictKind.Taken, "Ctrl+Alt+A")) });

        Assert.True(HotkeyChord.TryParse("alt+ctrl+a", out var chord));
        Assert.Equal("Ctrl+Alt+A", chord.Describe());
        Assert.Equal(canonical, sloppy);
    }

    [Fact]
    public void ASettingsFileWrittenBeforeTheFieldCountAsNeverNotified()
    {
        var legacy = JsonSerializer.Deserialize<AppSettings>("{\"AwakenHotkey\":\"Ctrl+Alt+S\",\"CloseToTray\":false}");

        Assert.NotNull(legacy);
        Assert.Equal(string.Empty, legacy!.HotkeyConflictNotice);

        var current = HotkeyConflictFingerprint.Build(new[] { ElementDuplicatesAwaken });
        Assert.True(HotkeyConflictFingerprint.Changed(legacy.HotkeyConflictNotice, current));
    }

    [Fact]
    public void AStoredRecordSurvivesSaveAndLoad()
    {
        var recorded = HotkeyConflictFingerprint.Build(new[] { WeChatHoldsLongShot, WindowsOwnsClipboard });
        var settings = AppSettings.CreateDefault();
        settings.HotkeyConflictNotice = recorded;

        var json = JsonSerializer.Serialize(settings);
        var reloaded = JsonSerializer.Deserialize<AppSettings>(json);

        Assert.NotNull(reloaded);
        Assert.Equal(recorded, reloaded!.HotkeyConflictNotice);

        var same = HotkeyConflictFingerprint.Build(new[] { WindowsOwnsClipboard, WeChatHoldsLongShot });
        Assert.False(HotkeyConflictFingerprint.Changed(reloaded.HotkeyConflictNotice, same));
    }
}
