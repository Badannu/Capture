using CaptureDemo.Core;
using Xunit;

namespace CaptureDemo.Core.Tests;

/// <summary>
/// Conflict detection is mostly wording, and wording is what users act on: claiming
/// "another program has it" when the box is simply empty sends them hunting for a
/// process that does not exist.
/// </summary>
public class HotkeyConflictTests
{
    private static readonly string[] WeChatRunning = { "WeChat", "explorer", "Code" };

    [Theory]
    [InlineData("alt+ctrl+a", "Ctrl+Alt+A")]
    [InlineData("CTRL+SHIFT+F12", "Ctrl+Shift+F12")]
    [InlineData("Win+Alt+R", "Win+Alt+R")]
    [InlineData("9", "9")]
    public void DescribeNormalisesModifierOrderAndCase(string input, string expected)
    {
        Assert.True(HotkeyChord.TryParse(input, out var chord));
        Assert.Equal(expected, chord.Describe());
    }

    [Fact]
    public void EmptyBoxIsInvalidNotTaken()
    {
        var conflict = HotkeyAudit.Check("  ", "长截图", Array.Empty<(string, string)>(), null, _ => true);

        Assert.Equal(HotkeyConflictKind.Invalid, conflict.Kind);
        Assert.Equal("还没有设置组合键", conflict.Headline);
        Assert.DoesNotContain("占用", conflict.Detail);
    }

    [Fact]
    public void MissingMainKeyIsInvalid()
    {
        var conflict = HotkeyAudit.Check("Ctrl+Alt", "长截图", Array.Empty<(string, string)>(), null, _ => true);

        Assert.Equal(HotkeyConflictKind.Invalid, conflict.Kind);
        Assert.Contains("Ctrl+Alt", conflict.Headline);
    }

    [Fact]
    public void SameChordInTwoRowsIsReportedAsDuplicateBeforeAskingWindows()
    {
        var asked = false;
        var conflict = HotkeyAudit.Check(
            "ctrl+alt+l",
            "长截图",
            new[] { ("唤醒截图条", "Ctrl+Alt+S"), ("元素截图", "Ctrl+Alt+L") },
            null,
            _ =>
            {
                asked = true;
                return true;
            });

        Assert.Equal(HotkeyConflictKind.Duplicate, conflict.Kind);
        Assert.Equal("元素截图", conflict.Owner);
        Assert.Equal("Ctrl+Alt+L", conflict.Chord);
        // A duplicate inside the dialog is decided without touching the OS: the other
        // row has not been saved yet, so RegisterHotKey would happily say "free".
        Assert.False(asked);
    }

    [Fact]
    public void ReservedChordBlamesWindowsNotAnApp()
    {
        var conflict = HotkeyAudit.Check(
            "Win+L",
            "唤醒截图条",
            Array.Empty<(string, string)>(),
            WeChatRunning,
            _ => false);

        Assert.Equal(HotkeyConflictKind.Reserved, conflict.Kind);
        Assert.Equal("Windows", conflict.Owner);
        Assert.Contains("锁定屏幕", conflict.Detail);
    }

    [Fact]
    public void RegisteredChordIsFineEvenIfAWellKnownAppWouldUseIt()
    {
        // WeChat is running and Alt+A is its screenshot key, but the OS accepted our
        // registration, so there is nothing to warn about.
        var conflict = HotkeyAudit.Check("Alt+A", "唤醒截图条", Array.Empty<(string, string)>(), WeChatRunning, _ => true);

        Assert.False(conflict.IsConflict);
        Assert.Equal(HotkeyConflictKind.None, conflict.Kind);
    }

    [Fact]
    public void TakenChordNamesTheRunningAppThatShipsIt()
    {
        var conflict = HotkeyAudit.Check("Alt+A", "唤醒截图条", Array.Empty<(string, string)>(), WeChatRunning, _ => false);

        Assert.Equal(HotkeyConflictKind.Taken, conflict.Kind);
        Assert.Equal("微信", conflict.Owner);
        Assert.Equal("截图", conflict.Feature);
        Assert.Contains("微信", conflict.Detail);
        Assert.Contains("正在运行", conflict.Detail);
    }

    [Fact]
    public void TakenChordWithoutAKnownOwnerDoesNotInventOne()
    {
        var conflict = HotkeyAudit.Check("Ctrl+Alt+F9", "唤醒截图条", Array.Empty<(string, string)>(), WeChatRunning, _ => false);

        Assert.Equal(HotkeyConflictKind.Taken, conflict.Kind);
        Assert.Equal(string.Empty, conflict.Owner);
        Assert.Contains("查不到", conflict.Detail);
        Assert.Equal("唤醒截图条 Ctrl+Alt+F9 被占用", conflict.Short("唤醒截图条"));
    }

    [Fact]
    public void KnownAppThatIsNotRunningIsNotBlamed()
    {
        var conflict = HotkeyAudit.Check("Alt+A", "唤醒截图条", Array.Empty<(string, string)>(), new[] { "explorer" }, _ => false);

        Assert.Equal(HotkeyConflictKind.Taken, conflict.Kind);
        Assert.Equal(string.Empty, conflict.Owner);
    }

    [Fact]
    public void KnownTableMatchesProcessNamesCaseInsensitively()
    {
        HotkeyChord.TryParse("Alt+A", out var chord);
        var matches = KnownHotkeys.Match(chord, new[] { "wechat" });

        Assert.Single(matches);
        Assert.Equal("微信", matches[0].Application);
    }

    [Fact]
    public void SeveralAppsCanShareOneChord()
    {
        HotkeyChord.TryParse("Ctrl+Shift+A", out var chord);
        var matches = KnownHotkeys.Match(chord, new[] { "DingTalk", "Feishu" });

        Assert.Equal(2, matches.Count);
    }

    [Fact]
    public void ShortLinesPointAtTheActionAndTheReason()
    {
        var duplicate = new HotkeyConflict(HotkeyConflictKind.Duplicate, "Ctrl+Alt+L", "元素截图");
        Assert.Equal("长截图 Ctrl+Alt+L 与「元素截图」重复", duplicate.Short("长截图"));

        var reserved = new HotkeyConflict(HotkeyConflictKind.Reserved, "Win+L", "Windows", "锁定屏幕");
        Assert.Equal("长截图 Win+L 归 Windows 使用", reserved.Short("长截图"));

        var taken = new HotkeyConflict(HotkeyConflictKind.Taken, "Alt+A", "微信", "截图");
        Assert.Equal("长截图 Alt+A 被占用（疑似微信 截图）", taken.Short("长截图"));
    }

    [Fact]
    public void ShippedDefaultsAreDistinctAndParseable()
    {
        var defaults = new[]
        {
            AppSettings.DefaultAwakenHotkey,
            AppSettings.DefaultElementHotkey,
            AppSettings.DefaultLongHotkey,
            AppSettings.DefaultClipboardHotkey,
        };

        var parsed = new List<HotkeyChord>();
        foreach (var text in defaults)
        {
            Assert.True(HotkeyChord.TryParse(text, out var chord), text);
            parsed.Add(chord);
        }

        Assert.Equal(parsed.Count, parsed.Distinct().Count());
    }
}
