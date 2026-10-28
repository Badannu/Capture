using CaptureDemo.Core;
using Xunit;

namespace CaptureDemo.Core.Tests;

/// <summary>
/// The index is the only record of which PNGs belong to which history row. If it silently
/// turns into <c>[]</c> the panel looks empty and every file becomes a leftover, so these
/// tests pin down that the previous version survives and that a damaged file cannot make
/// the app treat the folder as garbage.
/// </summary>
public sealed class ClipboardIndexBackupTests
{
    [Fact]
    public void SaveKeepsThePreviousIndexAsBackup()
    {
        using var folder = new TempFolder();
        var path = Path.Combine(folder.Dir, "index.json");

        var first = new ClipboardHistory { Capacity = 12 };
        first.Push(Image("a1"));
        first.Push(Image("b2"));
        ClipboardIndex.Save(path, first);

        // Second write with nothing in it: this is the shape that wiped a real history.
        ClipboardIndex.Save(path, new ClipboardHistory { Capacity = 12 });

        Assert.Equal("[]", File.ReadAllText(path).Replace("\r", "").Replace("\n", "").Replace(" ", ""));
        var backup = ClipboardIndex.BackupPathOf(path);
        Assert.Equal(Path.Combine(folder.Dir, "index.bak.json"), backup);
        Assert.True(File.Exists(backup));
    }

    [Fact]
    public void BackupHoldsEveryEntryThatWasInTheIndexBeforeTheWipe()
    {
        using var folder = new TempFolder();
        var path = Path.Combine(folder.Dir, "index.json");

        var history = new ClipboardHistory { Capacity = 12 };
        history.Push(Image("a1"));
        history.Push(Image("b2"));
        ClipboardIndex.Save(path, history);
        ClipboardIndex.Save(path, new ClipboardHistory { Capacity = 12 });

        var rescued = ClipboardIndex.Load(ClipboardIndex.BackupPathOf(path), 12);

        Assert.Equal(2, rescued.Count);
        Assert.Contains(rescued.Items, entry => entry.ImageFile == "a1.png");
        Assert.Contains(rescued.Items, entry => entry.ImageFile == "b2.png");
    }

    [Fact]
    public void CorruptIndexLoadsFromTheBackupInsteadOfLookingEmpty()
    {
        using var folder = new TempFolder();
        var path = Path.Combine(folder.Dir, "index.json");

        var history = new ClipboardHistory { Capacity = 12 };
        history.Push(Image("a1"));
        ClipboardIndex.Save(path, history);
        ClipboardIndex.Save(path, history); // second write leaves the first one as the backup
        File.WriteAllText(path, "{ not json");

        var loaded = ClipboardIndex.Load(path, 12);

        Assert.Equal(1, loaded.Count);
        Assert.Equal("a1.png", loaded.Items[0].ImageFile);
    }

    [Fact]
    public void CorruptIndexWithoutBackupStillLeavesEveryFileAlone()
    {
        using var folder = new TempFolder();
        var path = Path.Combine(folder.Dir, "index.json");
        File.WriteAllText(path, "{ not json");
        folder.Write("a1.png");
        folder.Write("a1.thumb.png");
        folder.Write("b2.png");

        var loaded = ClipboardIndex.Load(path, 12);
        var moved = ClipboardPruner.Quarantine(loaded, folder.Dir);

        Assert.Equal(0, loaded.Count);
        Assert.Equal(0, moved);
        Assert.Equal(3, folder.Count("*.png"));
    }

    [Fact]
    public void ClearedIndexStaysClearedInsteadOfResurrectingFromBackup()
    {
        using var folder = new TempFolder();
        var path = Path.Combine(folder.Dir, "index.json");

        var history = new ClipboardHistory { Capacity = 12 };
        history.Push(Image("a1"));
        ClipboardIndex.Save(path, history);
        ClipboardIndex.Save(path, new ClipboardHistory { Capacity = 12 });

        // The user asked for this: an empty array is a valid state, not a damaged file.
        var loaded = ClipboardIndex.Load(path, 12);

        Assert.Equal(0, loaded.Count);
    }

    [Fact]
    public void LockedIndexIsReportedAsFailedNotEmpty()
    {
        using var folder = new TempFolder();
        var path = Path.Combine(folder.Dir, "index.json");
        var history = new ClipboardHistory { Capacity = 12 };
        history.Push(Image("a1"));
        ClipboardIndex.Save(path, history);

        ClipboardIndexLoad load;
        using (new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            load = ClipboardIndex.LoadWithStatus(path, 12);
        }

        Assert.Equal(ClipboardIndexStatus.Failed, load.Status);
        Assert.Equal(0, load.History.Count);
        Assert.False(load.CanRewrite);

        // "Could not read" must never be treated as "the folder is full of leftovers".
        folder.Write("a1.png");
        folder.Write("a1.thumb.png");
        Assert.Equal(0, ClipboardPruner.Quarantine(load.History, folder.Dir));
        Assert.Equal(2, folder.Count("*.png"));
    }

    [Fact]
    public void MissingIndexIsNotAFailure()
    {
        using var folder = new TempFolder();
        var path = Path.Combine(folder.Dir, "index.json");

        var load = ClipboardIndex.LoadWithStatus(path, 7);

        Assert.Equal(ClipboardIndexStatus.Missing, load.Status);
        Assert.Equal(0, load.History.Count);
        Assert.Equal(7, load.History.Capacity);
        Assert.True(load.CanRewrite);
    }

    [Fact]
    public void SaveSwapsInANewFileWithoutLeavingTemporaries()
    {
        using var folder = new TempFolder();
        var path = Path.Combine(folder.Dir, "index.json");
        var history = new ClipboardHistory { Capacity = 12 };
        history.Push(Image("a1"));

        ClipboardIndex.Save(path, history);
        ClipboardIndex.Save(path, history);

        Assert.False(File.Exists(path + ".tmp"));
        Assert.Equal(1, ClipboardIndex.Load(path, 12).Count);
        Assert.Equal(ClipboardIndexStatus.Loaded, ClipboardIndex.LoadWithStatus(path, 12).Status);
    }

    private static ClipboardEntry Image(string id) => new()
    {
        Id = id,
        Kind = ClipboardKind.Image,
        CreatedUtc = DateTime.UtcNow,
        Signature = "img:" + id,
        ImageFile = id + ".png",
        ThumbFile = id + ".thumb.png",
        Width = 640,
        Height = 480,
    };

    private sealed class TempFolder : IDisposable
    {
        public TempFolder()
        {
            Dir = Path.Combine(Path.GetTempPath(), "capturedemo-tests", Guid.NewGuid().ToString("n"));
            Directory.CreateDirectory(Dir);
        }

        public string Dir { get; }

        public void Write(string name, string content = "x")
        {
            File.WriteAllText(Path.Combine(Dir, name), content);
        }

        public int Count(string pattern) =>
            Directory.EnumerateFiles(Dir, pattern, SearchOption.TopDirectoryOnly).Count();

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(Dir))
                {
                    Directory.Delete(Dir, true);
                }
            }
            catch (IOException)
            {
                // A locked temp file is not worth failing a test over.
            }
        }
    }
}
