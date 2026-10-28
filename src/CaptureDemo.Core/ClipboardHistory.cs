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

/// <summary>How a load ended. "Really empty" and "could not be read" must not look the same.</summary>
public enum ClipboardIndexStatus
{
    /// <summary>The file was read and parsed. The history reflects what is on disk.</summary>
    Loaded,

    /// <summary>No index on disk yet: first run, or the user deleted it.</summary>
    Missing,

    /// <summary>
    /// Something is there but it could not be read. The history is empty because the read
    /// failed, not because the file was empty — rewriting it now would destroy the only copy.
    /// </summary>
    Failed,
}

/// <summary>What <see cref="ClipboardIndex.LoadWithStatus"/> found.</summary>
public readonly record struct ClipboardIndexLoad(ClipboardHistory History, ClipboardIndexStatus Status)
{
    /// <summary>
    /// False when a write would overwrite an index this process never managed to read.
    /// Callers must skip both the orphan sweep and the next save while this is false.
    /// </summary>
    public bool CanRewrite => Status != ClipboardIndexStatus.Failed;
}

/// <summary>Index persistence. Image bytes are handled by the host (needs System.Drawing).</summary>
public static class ClipboardIndex
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    /// <summary>Convenience overload for callers that only need the entries.</summary>
    public static ClipboardHistory Load(string path, int capacity) =>
        LoadWithStatus(path, capacity).History;

    /// <summary>
    /// Reads the index and reports <em>why</em> it is empty when it is. Callers that act on an
    /// empty history (the orphan sweep, the next save) must check
    /// <see cref="ClipboardIndexLoad.CanRewrite"/> first.
    /// </summary>
    public static ClipboardIndexLoad LoadWithStatus(string path, int capacity)
    {
        var history = new ClipboardHistory { Capacity = capacity };
        if (string.IsNullOrWhiteSpace(path))
        {
            return new ClipboardIndexLoad(history, ClipboardIndexStatus.Missing);
        }

        var status = TryRead(path, history);
        if (status != ClipboardIndexStatus.Failed)
        {
            return new ClipboardIndexLoad(history, status);
        }

        // Unreadable: the copy taken before the last write may still hold everything. If it
        // loads, the index has been recovered and rewriting the main file is a repair.
        return TryRead(BackupPathOf(path), history) == ClipboardIndexStatus.Loaded
            ? new ClipboardIndexLoad(history, ClipboardIndexStatus.Loaded)
            : new ClipboardIndexLoad(history, ClipboardIndexStatus.Failed);
    }

    public static void Save(string path, ClipboardHistory history)
    {
        ArgumentNullException.ThrowIfNull(history);
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var json = JsonSerializer.Serialize(history.Items, Options);
        KeepPrevious(path);
        WriteAtomically(path, json);
    }

    /// <summary>Where the previous index is kept: <c>index.json</c> becomes <c>index.bak.json</c>.</summary>
    public static string BackupPathOf(string path)
    {
        var directory = Path.GetDirectoryName(path) ?? "";
        var name = Path.GetFileNameWithoutExtension(path);
        return Path.Combine(directory, name + ".bak.json");
    }

    /// <summary>
    /// Copies the current index aside before it gets overwritten. Every rewrite goes through
    /// here, and a rewrite is exactly how the file turns into an empty array — keeping the
    /// previous version is the only thing that makes that recoverable.
    /// </summary>
    private static void KeepPrevious(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Copy(path, BackupPathOf(path), overwrite: true);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // A failed backup must not block the write, or the history could never be saved.
        }
    }

    /// <summary>
    /// Writes a sibling file and swaps it in. A process killed mid-write used to leave half a
    /// JSON document behind, which the next start read as "damaged" and therefore as empty.
    /// </summary>
    private static void WriteAtomically(string path, string json)
    {
        var temp = path + ".tmp";
        File.WriteAllText(temp, json);
        try
        {
            if (File.Exists(path))
            {
                File.Replace(temp, path, destinationBackupFileName: null);
                return;
            }

            File.Move(temp, path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or PlatformNotSupportedException)
        {
            // Replace is not available everywhere; an overwriting move still beats a partial write.
            File.Move(temp, path, overwrite: true);
        }
    }

    private static ClipboardIndexStatus TryRead(string path, ClipboardHistory history)
    {
        if (!File.Exists(path))
        {
            return ClipboardIndexStatus.Missing;
        }

        try
        {
            // FileShare.ReadWrite: another instance may be rewriting the index at this very
            // moment. Treating a sharing violation as "damaged" is how the cascade starts.
            using var stream = new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete);
            using var reader = new StreamReader(stream);
            var entries = JsonSerializer.Deserialize<List<ClipboardEntry>>(reader.ReadToEnd(), Options);
            if (entries == null)
            {
                return ClipboardIndexStatus.Failed;
            }

            history.ReplaceAll(entries.Where(entry => !string.IsNullOrEmpty(entry.Id)));
            return ClipboardIndexStatus.Loaded;
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            // A damaged index should not block startup, but it must not be mistaken for an empty one.
            return ClipboardIndexStatus.Failed;
        }
    }
}
