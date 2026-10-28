using System.Text.Json;
using System.Text.Json.Serialization;

namespace CaptureDemo.Core;

public enum ClipboardKind
{
    Image,
    Text,
    Files,
}

/// <summary>One clipboard capture. Image bytes live on disk, the index keeps names only.</summary>
public sealed class ClipboardEntry
{
    public string Id { get; set; } = "";

    public ClipboardKind Kind { get; set; }

    public DateTime CreatedUtc { get; set; }

    /// <summary>Content fingerprint used to collapse repeated copies of the same thing.</summary>
    public string Signature { get; set; } = "";

    public string? Text { get; set; }

    public List<string>? Files { get; set; }

    /// <summary>PNG file name inside the clipboard folder (not a full path).</summary>
    public string? ImageFile { get; set; }

    public string? ThumbFile { get; set; }

    public int Width { get; set; }

    public int Height { get; set; }

    public bool Pinned { get; set; }

    [JsonIgnore]
    public bool HasImage => Kind == ClipboardKind.Image && !string.IsNullOrEmpty(ImageFile);

    [JsonIgnore]
    public string Headline => Kind switch
    {
        ClipboardKind.Image => Width > 0 && Height > 0 ? $"{Width} × {Height}" : "图片",
        ClipboardKind.Text => oneLine(Text),
        ClipboardKind.Files => Files is { Count: > 0 }
            ? Files.Count == 1
                ? Path.GetFileName(Files[0])
                : $"{Path.GetFileName(Files[0])} 等 {Files.Count} 个文件"
            : "文件",
        _ => "剪贴板内容",
    };

    [JsonIgnore]
    public string KindLabel => Kind switch
    {
        ClipboardKind.Image => "图片",
        ClipboardKind.Text => "文本",
        _ => "文件",
    };

    private static string oneLine(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return "文本";
        }

        var builder = new System.Text.StringBuilder(80);
        var pendingSpace = false;
        foreach (var ch in text.Trim())
        {
            if (char.IsWhiteSpace(ch))
            {
                pendingSpace = builder.Length > 0;
                continue;
            }

            if (pendingSpace)
            {
                builder.Append(' ');
                pendingSpace = false;
            }

            builder.Append(ch);
            if (builder.Length >= 80)
            {
                break;
            }
        }

        return builder.ToString();
    }
}

/// <summary>
/// Newest-first ring of clipboard entries. Pinned entries survive trimming and are
/// listed before everything else; re-copying an existing item refreshes it to the top
/// instead of creating a duplicate.
/// </summary>
public sealed class ClipboardHistory
{
    private readonly List<ClipboardEntry> _items = new();

    public int Capacity { get; set; } = 12;

    public IReadOnlyList<ClipboardEntry> Items => _items;

    public int Count => _items.Count;

    /// <summary>Inserts at the top. Returns entries that fell out of the ring and should be deleted from disk.</summary>
    public IReadOnlyList<ClipboardEntry> Push(ClipboardEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        var evicted = new List<ClipboardEntry>();
        if (!string.IsNullOrEmpty(entry.Signature))
        {
            var existing = _items.FindIndex(item => item.Signature == entry.Signature);
            if (existing >= 0)
            {
                // Same content copied twice: keep the pin, drop the older row.
                entry.Pinned = _items[existing].Pinned;
                evicted.Add(_items[existing]);
                _items.RemoveAt(existing);
            }
        }

        _items.Insert(0, entry);
        evicted.AddRange(EnforceCapacity());
        return evicted;
    }

    public bool TogglePin(string id)
    {
        var entry = Find(id);
        if (entry == null)
        {
            return false;
        }

        entry.Pinned = !entry.Pinned;
        return true;
    }

    public ClipboardEntry? Find(string id) =>
        string.IsNullOrEmpty(id) ? null : _items.Find(item => item.Id == id);

    public ClipboardEntry? Remove(string id)
    {
        var index = _items.FindIndex(item => item.Id == id);
        if (index < 0)
        {
            return null;
        }

        var entry = _items[index];
        _items.RemoveAt(index);
        return entry;
    }

    /// <summary>Clears everything that is not pinned.</summary>
    public IReadOnlyList<ClipboardEntry> ClearUnpinned()
    {
        var removed = _items.Where(item => !item.Pinned).ToList();
        _items.RemoveAll(item => !item.Pinned);
        return removed;
    }

    /// <summary>Pinned first (newest first), then the rest newest first.</summary>
    public IReadOnlyList<ClipboardEntry> Ordered()
    {
        var ordered = new List<ClipboardEntry>(_items.Count);
        ordered.AddRange(_items.Where(item => item.Pinned));
        ordered.AddRange(_items.Where(item => !item.Pinned));
        return ordered;
    }

    public void ReplaceAll(IEnumerable<ClipboardEntry> entries)
    {
        _items.Clear();
        _items.AddRange(entries);
        EnforceCapacity();
    }

    private IReadOnlyList<ClipboardEntry> EnforceCapacity()
    {
        var evicted = new List<ClipboardEntry>();
        var limit = Math.Max(1, Capacity);
        while (true)
        {
            var unpinned = _items.Count(item => !item.Pinned);
            if (unpinned <= limit)
            {
                break;
            }

            var index = _items.FindLastIndex(item => !item.Pinned);
            if (index < 0)
            {
                break;
            }

            evicted.Add(_items[index]);
            _items.RemoveAt(index);
        }

        return evicted;
    }
}

/// <summary>Index persistence. Image bytes are handled by the host (needs System.Drawing).</summary>
public static class ClipboardIndex
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    public static ClipboardHistory Load(string path, int capacity)
    {
        var history = new ClipboardHistory { Capacity = capacity };
        try
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            {
                return history;
            }

            var entries = JsonSerializer.Deserialize<List<ClipboardEntry>>(File.ReadAllText(path), Options);
            if (entries != null)
            {
                history.ReplaceAll(entries.Where(entry => !string.IsNullOrEmpty(entry.Id)));
            }
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            // A damaged index should not block startup.
        }

        return history;
    }

    public static void Save(string path, ClipboardHistory history)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        File.WriteAllText(path, JsonSerializer.Serialize(history.Items, Options));
    }
}
