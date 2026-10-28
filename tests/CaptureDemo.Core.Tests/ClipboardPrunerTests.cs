using CaptureDemo.Core;
using Xunit;

namespace CaptureDemo.Core.Tests;

/// <summary>
/// Locks down the rule that matters most in this app: housekeeping may tidy up, never
/// destroy. Every case here is a variation of "the index says nothing about these files",
/// which used to mean "delete them all".
/// </summary>
public sealed class ClipboardPrunerTests
{
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

    [Fact]
    public void EmptyIndexLeavesEveryPngAlone()
    {
        using var folder = new TempFolder();
        folder.Write("a.png");
        folder.Write("b.png");
        folder.Write("c.png");

        var moved = ClipboardPruner.Quarantine(new ClipboardHistory(), folder.Dir);

        Assert.Equal(0, moved);
        Assert.Equal(3, folder.Count("*.png"));
        Assert.False(Directory.Exists(Path.Combine(folder.Dir, ClipboardPruner.QuarantineFolderName)));
    }

    [Fact]
    public void ReferencedFilesStayAndOnlyOrphansAreQuarantined()
    {
        using var folder = new TempFolder();
        var history = new ClipboardHistory { Capacity = 12 };
        history.Push(Image("a1"));
        history.Push(Image("b2"));

        folder.Write("a1.png");
        folder.Write("a1.thumb.png");
        folder.Write("b2.png");
        folder.Write("b2.thumb.png");
        folder.Write("c3.png");
        folder.Write("c3.thumb.png");

        var moved = ClipboardPruner.Quarantine(history, folder.Dir);

        Assert.Equal(2, moved);
        Assert.Equal(4, folder.Count("*.png"));
        Assert.True(File.Exists(Path.Combine(folder.Dir, "a1.png")));
        Assert.True(File.Exists(Path.Combine(folder.Dir, "b2.thumb.png")));

        var quarantine = Path.Combine(folder.Dir, ClipboardPruner.QuarantineFolderName);
        Assert.Equal(2, Directory.EnumerateFiles(quarantine, "*.png").Count());
        Assert.Contains(Directory.EnumerateFiles(quarantine), file => file.EndsWith("c3.png", StringComparison.Ordinal));
    }

    [Fact]
    public void QuarantinedFileKeepsItsBytes()
    {
        using var folder = new TempFolder();
        var history = new ClipboardHistory { Capacity = 12 };
        history.Push(Image("a1"));
        folder.Write("a1.png");
        folder.Write("a1.thumb.png");
        folder.Write("orphan.png", "the bytes the user cared about");

        var moved = ClipboardPruner.Quarantine(history, folder.Dir);

        Assert.Equal(1, moved);
        var quarantine = Path.Combine(folder.Dir, ClipboardPruner.QuarantineFolderName);
        var rescued = Directory.EnumerateFiles(quarantine, "*orphan.png").Single();
        Assert.Equal("the bytes the user cared about", File.ReadAllText(rescued));
    }

    [Fact]
    public void TooManyOrphansIsTreatedAsDamage()
    {
        using var folder = new TempFolder();
        var history = new ClipboardHistory { Capacity = 12 };
        history.Push(Image("a1"));
        folder.Write("a1.png");

        for (var i = 0; i < ClipboardPruner.MaxQuarantinePerRun + 1; i++)
        {
            folder.Write($"stray{i}.png");
        }

        var moved = ClipboardPruner.Quarantine(history, folder.Dir);

        Assert.Equal(0, moved);
        Assert.Equal(ClipboardPruner.MaxQuarantinePerRun + 2, folder.Count("*.png"));
        Assert.False(Directory.Exists(Path.Combine(folder.Dir, ClipboardPruner.QuarantineFolderName)));
    }

    [Fact]
    public void MissingFolderIsIgnored()
    {
        var history = new ClipboardHistory { Capacity = 12 };
        history.Push(Image("a1"));

        var missing = Path.Combine(Path.GetTempPath(), "capturedemo-tests", Guid.NewGuid().ToString("n"));

        Assert.Equal(0, ClipboardPruner.Quarantine(history, missing));
        Assert.False(Directory.Exists(missing));
    }

    [Fact]
    public void TextOnlyIndexDoesNotProtectLeftoverImagesButStaysWithinTheGate()
    {
        using var folder = new TempFolder();
        var history = new ClipboardHistory { Capacity = 12 };
        history.Push(new ClipboardEntry { Id = "t1", Kind = ClipboardKind.Text, Text = "hello" });
        folder.Write("leftover.png");

        var moved = ClipboardPruner.Quarantine(history, folder.Dir);

        Assert.Equal(1, moved);
        Assert.Equal(0, folder.Count("*.png"));
    }

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
