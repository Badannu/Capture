using System.Text.Json;

namespace CaptureDemo.Core;

/// <summary>
/// Local preferences. Stored as JSON under %AppData%\CaptureDemo\settings.json.
/// </summary>
public sealed class AppSettings
{
    public const string DefaultAwakenHotkey = "Ctrl+Alt+S";

    public const string DefaultElementHotkey = "Ctrl+Alt+E";

    public const string DefaultLongHotkey = "Ctrl+Alt+L";

    public const string DefaultClipboardHotkey = "Ctrl+Alt+V";

    public const string DefaultFilenamePattern = "{kind}-{yyyyMMdd}-{HHmmss}-{fff}";

    public string AwakenHotkey { get; set; } = DefaultAwakenHotkey;

    public string ElementHotkey { get; set; } = DefaultElementHotkey;

    public string LongHotkey { get; set; } = DefaultLongHotkey;

    public string ClipboardHotkey { get; set; } = DefaultClipboardHotkey;

    /// <summary>Empty means Pictures\CaptureDemo.</summary>
    public string SaveDirectory { get; set; } = "";

    public string FilenamePattern { get; set; } = DefaultFilenamePattern;

    public bool CopyToClipboard { get; set; } = true;

    /// <summary>Record clipboard items so the history panel has something to show.</summary>
    public bool WatchClipboard { get; set; } = true;

    /// <summary>How many unpinned clipboard entries to keep.</summary>
    public int ClipboardLimit { get; set; } = 12;

    public bool LaunchAtStartup { get; set; }

    /// <summary>System / Light / Dark.</summary>
    public string Appearance { get; set; } = "System";

    public int ScrollSpeed { get; set; } = 1;

    public int SampleMilliseconds { get; set; } = 120;

    public static AppSettings CreateDefault() => new();

    public static string DefaultDirectory() =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "CaptureDemo");

    public static string DefaultFilePath() =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "CaptureDemo", "settings.json");

    public string ResolveDirectory()
    {
        if (string.IsNullOrWhiteSpace(SaveDirectory))
        {
            return DefaultDirectory();
        }

        return Environment.ExpandEnvironmentVariables(SaveDirectory.Trim());
    }

    public void Normalize()
    {
        if (!HotkeyChord.TryParse(AwakenHotkey, out _))
        {
            AwakenHotkey = DefaultAwakenHotkey;
        }

        if (!HotkeyChord.TryParse(ElementHotkey, out _))
        {
            ElementHotkey = DefaultElementHotkey;
        }

        if (!HotkeyChord.TryParse(LongHotkey, out _))
        {
            LongHotkey = DefaultLongHotkey;
        }

        if (!HotkeyChord.TryParse(ClipboardHotkey, out _))
        {
            ClipboardHotkey = DefaultClipboardHotkey;
        }

        ClipboardLimit = Math.Clamp(ClipboardLimit, 5, 30);
        if (Appearance is not ("System" or "Light" or "Dark"))
        {
            Appearance = "System";
        }

        ScrollSpeed = Math.Clamp(ScrollSpeed, 1, 10);
        SampleMilliseconds = Math.Clamp(SampleMilliseconds, 40, 800);
        if (string.IsNullOrWhiteSpace(FilenamePattern))
        {
            FilenamePattern = DefaultFilenamePattern;
        }
    }

    public static AppSettings Load(string path)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            {
                return CreateDefault();
            }

            var loaded = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(path));
            if (loaded == null)
            {
                return CreateDefault();
            }

            loaded.Normalize();
            return loaded;
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            return CreateDefault();
        }
    }

    public void Save(string path)
    {
        Normalize();
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var json = JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(path, json);
    }

    public static string FormatFileName(string? pattern, string kind, DateTime time)
    {
        var safeKind = string.IsNullOrWhiteSpace(kind) ? "shot" : kind.Trim();
        var source = string.IsNullOrWhiteSpace(pattern) ? DefaultFilenamePattern : pattern;
        var text = source
            .Replace("{kind}", safeKind, StringComparison.Ordinal)
            .Replace("{yyyyMMdd}", time.ToString("yyyyMMdd"), StringComparison.Ordinal)
            .Replace("{HHmmss}", time.ToString("HHmmss"), StringComparison.Ordinal)
            .Replace("{yyyy}", time.ToString("yyyy"), StringComparison.Ordinal)
            .Replace("{MM}", time.ToString("MM"), StringComparison.Ordinal)
            .Replace("{dd}", time.ToString("dd"), StringComparison.Ordinal)
            .Replace("{HH}", time.ToString("HH"), StringComparison.Ordinal)
            .Replace("{mm}", time.ToString("mm"), StringComparison.Ordinal)
            .Replace("{ss}", time.ToString("ss"), StringComparison.Ordinal)
            .Replace("{fff}", time.ToString("fff"), StringComparison.Ordinal);

        var invalid = Path.GetInvalidFileNameChars();
        var chars = text.Select(ch => invalid.Contains(ch) ? '-' : ch).ToArray();
        var name = new string(chars).Trim().Trim('.');
        if (name.Length == 0)
        {
            name = safeKind + "-" + time.ToString("yyyyMMdd-HHmmss-fff");
        }

        return name;
    }
}

public readonly record struct HotkeyChord(uint Modifiers, uint VirtualKey)
{
    public const uint ModAlt = 0x0001;

    public const uint ModControl = 0x0002;

    public const uint ModShift = 0x0004;

    public const uint ModWin = 0x0008;

    public static bool TryParse(string? text, out HotkeyChord chord)
    {
        chord = default;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        uint mods = 0;
        uint key = 0;
        var seenKey = false;
        foreach (var raw in text.Split(new[] { '+', ' ' }, StringSplitOptions.RemoveEmptyEntries))
        {
            var token = raw.Trim();
            if (token.Equals("Ctrl", StringComparison.OrdinalIgnoreCase) ||
                token.Equals("Control", StringComparison.OrdinalIgnoreCase))
            {
                mods |= ModControl;
                continue;
            }

            if (token.Equals("Alt", StringComparison.OrdinalIgnoreCase))
            {
                mods |= ModAlt;
                continue;
            }

            if (token.Equals("Shift", StringComparison.OrdinalIgnoreCase))
            {
                mods |= ModShift;
                continue;
            }

            if (token.Equals("Win", StringComparison.OrdinalIgnoreCase) ||
                token.Equals("Windows", StringComparison.OrdinalIgnoreCase))
            {
                mods |= ModWin;
                continue;
            }

            if (seenKey || !TryKey(token, out var vk))
            {
                return false;
            }

            key = vk;
            seenKey = true;
        }

        if (!seenKey)
        {
            return false;
        }

        chord = new HotkeyChord(mods, key);
        return true;
    }

    private static bool TryKey(string token, out uint vk)
    {
        vk = 0;
        if (token.Length == 1)
        {
            var ch = char.ToUpperInvariant(token[0]);
            if (ch is >= 'A' and <= 'Z')
            {
                vk = ch;
                return true;
            }

            if (ch is >= '0' and <= '9')
            {
                vk = ch;
                return true;
            }

            return false;
        }

        if (token.Length is >= 2 and <= 3 && (token[0] == 'F' || token[0] == 'f') && int.TryParse(token.AsSpan(1), out var fn) && fn is >= 1 and <= 12)
        {
            vk = (uint)(0x70 + fn - 1);
            return true;
        }

        return false;
    }
}
