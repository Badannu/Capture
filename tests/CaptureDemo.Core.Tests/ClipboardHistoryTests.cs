using CaptureDemo.Core;
using Xunit;

namespace CaptureDemo.Core.Tests;

public sealed class ClipboardHistoryTests
{
    private static ClipboardEntry Text(string value, string? signature = null) => new()
    {
        Id = Guid.NewGuid().ToString("n"),
        Kind = ClipboardKind.Text,
        CreatedUtc = DateTime.UtcNow,
        Text = value,
        Signature = signature ?? "t:" + value,
    };

    [Fact]
    public void NewestEntryGoesToTheTop()
    {
        var history = new ClipboardHistory { Capacity = 5 };
        history.Push(Text("a"));
        history.Push(Text("b"));

        Assert.Equal("b", history.Items[0].Text);
        Assert.Equal("a", history.Items[1].Text);
    }

    [Fact]
    public void RecopyingTheSameContentMovesItUpWithoutDuplicating()
    {
        var history = new ClipboardHistory { Capacity = 5 };
        history.Push(Text("a"));
        history.Push(Text("b"));
        var evicted = history.Push(Text("a"));

        Assert.Equal(2, history.Count);
        Assert.Equal("a", history.Items[0].Text);
        Assert.Single(evicted);
        Assert.Equal("a", evicted[0].Text);
    }

    [Fact]
    public void RepushingKeepsThePin()
    {
        var history = new ClipboardHistory { Capacity = 5 };
        var entry = Text("pinned");
        history.Push(entry);
        Assert.True(history.TogglePin(entry.Id));

        history.Push(Text("pinned"));
        Assert.True(history.Items[0].Pinned);
        Assert.Single(history.Items);
    }

    [Fact]
    public void CapacityTrimsOldestUnpinned()
    {
        var history = new ClipboardHistory { Capacity = 3 };
        history.Push(Text("a"));
        history.Push(Text("b"));
        history.Push(Text("c"));
        var evicted = history.Push(Text("d"));

        Assert.Equal(3, history.Count);
        Assert.Equal("d,c,b", string.Join(",", history.Items.Select(item => item.Text)));
        Assert.Single(evicted);
        Assert.Equal("a", evicted[0].Text);
    }

    [Fact]
    public void PinnedEntriesSurviveTrimming()
    {
        var history = new ClipboardHistory { Capacity = 2 };
        var pinned = Text("keep");
        history.Push(pinned);
        history.TogglePin(pinned.Id);
        history.Push(Text("b"));
        history.Push(Text("c"));
        history.Push(Text("d"));

        Assert.Equal(3, history.Count);
        Assert.Contains(history.Items, item => item.Text == "keep");
    }

    [Fact]
    public void OrderedPutsPinnedFirstThenNewest()
    {
        var history = new ClipboardHistory { Capacity = 6 };
        var first = Text("one");
        history.Push(first);
        history.Push(Text("two"));
        history.Push(Text("three"));
        history.TogglePin(first.Id);

        var ordered = history.Ordered();
        Assert.Equal("one", ordered[0].Text);
        Assert.Equal("three", ordered[1].Text);
        Assert.Equal("two", ordered[2].Text);
    }

    [Fact]
    public void ClearUnpinnedKeepsPinnedRows()
    {
        var history = new ClipboardHistory { Capacity = 6 };
        var keep = Text("keep");
        history.Push(keep);
        history.TogglePin(keep.Id);
        history.Push(Text("drop"));

        var removed = history.ClearUnpinned();

        Assert.Single(removed);
        Assert.Single(history.Items);
        Assert.Equal("keep", history.Items[0].Text);
    }

    [Fact]
    public void RemoveReturnsTheEntry()
    {
        var history = new ClipboardHistory();
        var entry = Text("x");
        history.Push(entry);

        var removed = history.Remove(entry.Id);

        Assert.NotNull(removed);
        Assert.Empty(history.Items);
        Assert.Null(history.Remove("missing"));
    }

    [Fact]
    public void HeadlineCollapsesWhitespaceAndTruncates()
    {
        var entry = new ClipboardEntry
        {
            Kind = ClipboardKind.Text,
            Text = "  first line\r\n\tsecond   line  ",
        };

        Assert.Equal("first line second line", entry.Headline);
    }

    [Fact]
    public void FileHeadlineShowsNameAndCount()
    {
        var single = new ClipboardEntry { Kind = ClipboardKind.Files, Files = new List<string> { @"C:\tmp\shot.png" } };
        var many = new ClipboardEntry
        {
            Kind = ClipboardKind.Files,
            Files = new List<string> { @"C:\tmp\a.png", @"C:\tmp\b.png", @"C:\tmp\c.png" },
        };

        Assert.Equal("shot.png", single.Headline);
        Assert.Equal("a.png 等 3 个文件", many.Headline);
    }

    [Fact]
    public void ImageHeadlineReportsPixelSize()
    {
        var entry = new ClipboardEntry { Kind = ClipboardKind.Image, Width = 1920, Height = 1080 };
        Assert.Equal("1920 × 1080", entry.Headline);
    }

    [Fact]
    public void IndexRoundTripsThroughDisk()
    {
        var folder = Path.Combine(Path.GetTempPath(), "capturedemo-tests", Guid.NewGuid().ToString("n"));
        var path = Path.Combine(folder, "index.json");
        try
        {
            var history = new ClipboardHistory { Capacity = 5 };
            history.Push(new ClipboardEntry
            {
                Id = "a1",
                Kind = ClipboardKind.Image,
                ImageFile = "a1.png",
                ThumbFile = "a1.thumb.png",
                Width = 800,
                Height = 600,
                Signature = "img:1",
                CreatedUtc = new DateTime(2026, 10, 28, 1, 2, 3, DateTimeKind.Utc),
            });
            history.Push(Text("hello"));
            ClipboardIndex.Save(path, history);

            var loaded = ClipboardIndex.Load(path, 5);

            Assert.Equal(2, loaded.Count);
            Assert.Equal(ClipboardKind.Text, loaded.Items[0].Kind);
            Assert.Equal("hello", loaded.Items[0].Text);
            Assert.Equal("a1.png", loaded.Items[1].ImageFile);
            Assert.Equal(800, loaded.Items[1].Width);
        }
        finally
        {
            if (Directory.Exists(folder))
            {
                Directory.Delete(folder, true);
            }
        }
    }

    [Fact]
    public void DamagedIndexFallsBackToEmpty()
    {
        var folder = Path.Combine(Path.GetTempPath(), "capturedemo-tests", Guid.NewGuid().ToString("n"));
        var path = Path.Combine(folder, "index.json");
        try
        {
            Directory.CreateDirectory(folder);
            File.WriteAllText(path, "{ not json");
            var loaded = ClipboardIndex.Load(path, 12);
            Assert.Equal(0, loaded.Count);
            Assert.Equal(12, loaded.Capacity);
        }
        finally
        {
            if (Directory.Exists(folder))
            {
                Directory.Delete(folder, true);
            }
        }
    }
}
