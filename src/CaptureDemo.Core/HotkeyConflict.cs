namespace CaptureDemo.Core;

/// <summary>Why a global hotkey could not be registered.</summary>
public enum HotkeyConflictKind
{
    /// <summary>The chord parsed and Windows accepted it.</summary>
    None,

    /// <summary>The text in the settings file is not a usable chord.</summary>
    Invalid,

    /// <summary>Another action of this same app already uses the chord.</summary>
    Duplicate,

    /// <summary>Windows itself owns the chord; no program can register it.</summary>
    Reserved,

    /// <summary>Some other program holds the chord.</summary>
    Taken,
}

/// <summary>
/// One hotkey that could not be registered, plus the best guess at who holds it.
/// <para>
/// Windows reports only <c>ERROR_HOTKEY_ALREADY_REGISTERED</c> — there is no API that
/// answers "which process owns this hotkey". So <see cref="Owner"/> is a guess taken
/// from <see cref="KnownHotkeys"/> (well-known default shortcuts of running programs),
/// and the wording says so instead of pretending to know.
/// </para>
/// </summary>
public sealed record HotkeyConflict(
    HotkeyConflictKind Kind,
    string Chord = "",
    string Owner = "",
    string Feature = "",
    bool OwnerRunning = true)
{
    /// <summary>Nothing wrong — the hotkey registered.</summary>
    public static HotkeyConflict Ok { get; } = new(HotkeyConflictKind.None);

    /// <summary>
    /// Extra sentence appended to <see cref="Detail"/>, e.g. the Win32 error code when
    /// the failure was not "already registered". Empty means nothing to add.
    /// </summary>
    public string Note { get; init; } = string.Empty;

    public bool IsConflict => Kind != HotkeyConflictKind.None;

    /// <summary>Popup headline.</summary>
    public string Headline => Kind switch
    {
        HotkeyConflictKind.Invalid => Chord.Length == 0
            ? "还没有设置组合键"
            : $"「{Chord}」不是有效的组合键",
        HotkeyConflictKind.Duplicate => $"「{Chord}」和「{Owner}」重复了",
        HotkeyConflictKind.Reserved => $"「{Chord}」归 Windows 使用",
        HotkeyConflictKind.Taken => $"「{Chord}」已被占用",
        _ => "",
    };

    /// <summary>Popup body: what is going on, in plain language.</summary>
    public string Detail
    {
        get
        {
            var body = Body;
            return Note.Length == 0 ? body : body + " " + Note;
        }
    }

    private string Body => Kind switch
    {
        HotkeyConflictKind.Invalid => "需要至少一个修饰键加一个主键，例如 Ctrl+Alt+S。",
        HotkeyConflictKind.Duplicate =>
            $"本程序的「{Owner}」也用它。同一个组合键只能有一个生效，后注册的会被系统拒绝。",
        HotkeyConflictKind.Reserved => Feature.Length == 0
            ? "这个组合键由系统自己处理，任何程序都注册不上。"
            : $"这个组合键由系统自己的「{Feature}」占用，任何程序都注册不上。",
        HotkeyConflictKind.Taken => Owner.Length == 0
            ? "有别的程序占了它，但 Windows 不提供占用者信息，所以查不到是谁。"
            : $"大概率是{Owner}{(Feature.Length == 0 ? "" : "的「" + Feature + "」")}" +
              (OwnerRunning ? "（该程序正在运行）。" : "（该程序当前没在运行，也可能是别的东西占用）。") +
              "Windows 不提供占用者信息，这是按常见默认快捷键推断的。",
        _ => "",
    };

    /// <summary>Popup footer: what the user can do about it.</summary>
    public string Hint => Kind switch
    {
        HotkeyConflictKind.Invalid => "在输入框里直接按一次组合键，功能键 F1–F12 也可以。",
        HotkeyConflictKind.Duplicate => "给其中一个换一个组合键。",
        HotkeyConflictKind.Reserved => "换一个组合键。",
        HotkeyConflictKind.Taken => Owner.Length == 0 || !OwnerRunning
            ? "换一个组合键。"
            : $"关掉{Owner}，或换一个组合键。",
        _ => "",
    };

    /// <summary>One compact line for the main window status bar.</summary>
    public string Short(string action) => Kind switch
    {
        HotkeyConflictKind.Invalid => $"{action}的组合键无效",
        HotkeyConflictKind.Duplicate => $"{action} {Chord} 与「{Owner}」重复",
        HotkeyConflictKind.Reserved => $"{action} {Chord} 归 Windows 使用",
        HotkeyConflictKind.Taken => Owner.Length == 0
            ? Note.Length == 0 ? $"{action} {Chord} 被占用" : $"{action} {Chord} 注册失败"
            : $"{action} {Chord} 被占用（疑似{Owner}{(Feature.Length == 0 ? "" : " " + Feature)}）",
        _ => "",
    };
}

/// <summary>
/// Decides why a chord is unusable. The actual "can Windows register it" question is
/// passed in as a delegate: <c>RegisterHotKey</c> lives in the WinForms host, the
/// decision order lives here so it can be tested without a desktop.
/// </summary>
public static class HotkeyAudit
{
    /// <param name="text">Chord as typed in the settings box.</param>
    /// <param name="action">Label of the row being checked, skipped when comparing siblings.</param>
    /// <param name="others">Every other hotkey row currently in the dialog.</param>
    /// <param name="runningProcesses">Process names, used only to guess the owner.</param>
    /// <param name="canRegister">True when the OS accepted the chord right now.</param>
    public static HotkeyConflict Check(
        string text,
        string action,
        IReadOnlyList<(string Action, string Text)> others,
        IReadOnlyCollection<string>? runningProcesses,
        Func<HotkeyChord, bool> canRegister)
    {
        if (!HotkeyChord.TryParse(text, out var chord))
        {
            return new HotkeyConflict(HotkeyConflictKind.Invalid, (text ?? string.Empty).Trim());
        }

        // Two rows of the same dialog fighting over one chord: the OS would happily give
        // it to whichever registers first, so say it here instead of hiding one silently.
        foreach (var (label, sibling) in others)
        {
            if (string.Equals(label, action, StringComparison.Ordinal))
            {
                continue;
            }

            if (HotkeyChord.TryParse(sibling, out var other) && other == chord)
            {
                return new HotkeyConflict(HotkeyConflictKind.Duplicate, chord.Describe(), label);
            }
        }

        if (canRegister(chord))
        {
            return HotkeyConflict.Ok;
        }

        return Held(chord, runningProcesses);
    }

    /// <summary>
    /// The chord is known to be unusable — Windows already refused it (or the caller
    /// knows it will). Explains who probably holds it.
    /// </summary>
    public static HotkeyConflict Held(
        HotkeyChord chord,
        IReadOnlyCollection<string>? runningProcesses,
        string note = "")
    {
        var reserved = KnownHotkeys.ReservedFeature(chord);
        if (reserved != null)
        {
            return new HotkeyConflict(HotkeyConflictKind.Reserved, chord.Describe(), "Windows", reserved)
            {
                Note = note,
            };
        }

        var matches = KnownHotkeys.Match(chord, runningProcesses);
        if (matches.Count > 0)
        {
            return new HotkeyConflict(
                HotkeyConflictKind.Taken,
                chord.Describe(),
                matches[0].Application,
                matches[0].Feature)
            {
                Note = note,
            };
        }

        return new HotkeyConflict(HotkeyConflictKind.Taken, chord.Describe()) { Note = note };
    }
}

/// <summary>
/// Default global shortcuts shipped by programs a Chinese desktop user is likely to run.
/// Used only to turn "someone holds Shift+Alt+A" into "probably WeChat's screenshot".
/// </summary>
public static class KnownHotkeys
{
    /// <summary>Chord + the app that ships it + the feature it triggers + process names.</summary>
    public sealed record Entry(string Chord, string Application, string Feature, string[] Processes);

    private static readonly Entry[] Table =
    {
        new("Alt+A", "微信", "截图", new[] { "WeChat", "Weixin" }),
        new("Ctrl+Alt+A", "QQ", "截图", new[] { "QQ" }),
        new("Ctrl+Alt+A", "企业微信", "截图", new[] { "WXWork" }),
        new("Ctrl+Shift+A", "钉钉", "截图", new[] { "DingTalk" }),
        new("Ctrl+Shift+A", "飞书", "截图", new[] { "Feishu", "Lark" }),
        new("F1", "Snipaste", "截图", new[] { "Snipaste" }),
        new("Ctrl+Alt+Z", "微信", "打开/收起", new[] { "WeChat", "Weixin" }),
        new("Alt+Space", "PowerToys", "快速启动", new[] { "PowerToys", "PowerToys.PowerLauncher" }),
        new("Alt+Z", "NVIDIA GeForce Experience", "游戏内覆盖", new[] { "NVIDIA Overlay", "NVIDIA Share" }),
        new("F9", "Bandicam", "开始/停止录制", new[] { "bdcam" }),
    };

    /// <summary>
    /// Chords Windows reserves for itself. Consulted only after registration actually
    /// failed, so a wrong entry can never produce a false alarm — it can only explain a
    /// failure that happened anyway.
    /// </summary>
    private static readonly (HotkeyChord Chord, string Feature)[] Reserved =
    {
        (Chord("Win+L"), "锁定屏幕"),
        (Chord("Win+D"), "显示桌面"),
        (Chord("Win+E"), "打开资源管理器"),
        (Chord("Win+R"), "运行对话框"),
        (Chord("Win+G"), "游戏栏"),
        (Chord("Win+Shift+S"), "系统截图"),
        (Chord("Win+Alt+R"), "屏幕录制"),
        (Chord("Alt+F4"), "关闭当前窗口"),
    };

    public static IReadOnlyList<Entry> All => Table;

    /// <summary>Windows-owned chord? Null when the chord is not a known system shortcut.</summary>
    public static string? ReservedFeature(HotkeyChord chord)
    {
        foreach (var (reserved, feature) in Reserved)
        {
            if (reserved == chord)
            {
                return feature;
            }
        }

        return null;
    }

    /// <summary>
    /// Programs that ship this exact chord and are running right now. Empty is a valid
    /// answer: it means the owner is unknown, not that the hotkey is free.
    /// </summary>
    public static IReadOnlyList<Entry> Match(HotkeyChord chord, IReadOnlyCollection<string>? runningProcesses)
    {
        var matches = new List<Entry>();
        if (runningProcesses == null || runningProcesses.Count == 0)
        {
            return matches;
        }

        foreach (var entry in Table)
        {
            if (!HotkeyChord.TryParse(entry.Chord, out var known) || known != chord)
            {
                continue;
            }

            foreach (var process in entry.Processes)
            {
                if (Contains(runningProcesses, process))
                {
                    matches.Add(entry);
                    break;
                }
            }
        }

        return matches;
    }

    private static bool Contains(IReadOnlyCollection<string> names, string name)
    {
        foreach (var candidate in names)
        {
            if (string.Equals(candidate, name, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static HotkeyChord Chord(string text)
    {
        HotkeyChord.TryParse(text, out var chord);
        return chord;
    }
}
