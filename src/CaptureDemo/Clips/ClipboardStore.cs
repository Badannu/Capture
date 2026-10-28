using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using CaptureDemo.Core;

namespace CaptureDemo.Clips;

/// <summary>Owns the on-disk side of the clipboard history: PNGs, thumbnails and cleanup.</summary>
internal sealed class ClipboardStore
{
    private const int ThumbSide = 128;
    private const int MaxEdge = 4096;

    public ClipboardStore()
        : this(DefaultFolder())
    {
    }

    /// <summary>
    /// History sits next to the settings file, so the rename to Capture moved it too.
    /// Move the legacy folder across once instead of starting with an empty panel.
    /// Portable builds skip this: moving the installed app's history into an unzipped
    /// folder would empty the panel of the copy the user already runs.
    /// </summary>
    private static string DefaultFolder()
    {
        var current = Path.GetDirectoryName(AppSettings.DefaultFilePath()) ?? AppContext.BaseDirectory;
        var folder = Path.Combine(current, "clipboard");
        if (Directory.Exists(folder) || AppSettings.IsPortable())
        {
            return folder;
        }

        var legacyRoot = Path.GetDirectoryName(AppSettings.LegacyFilePath());
        var legacy = string.IsNullOrEmpty(legacyRoot) ? null : Path.Combine(legacyRoot, "clipboard");
        if (legacy == null || !Directory.Exists(legacy))
        {
            return folder;
        }

        try
        {
            Directory.CreateDirectory(current);
            Directory.Move(legacy, folder);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Locked or on another volume: keep using a fresh folder rather than fail.
        }

        return folder;
    }

    public ClipboardStore(string folder)
    {
        Folder = folder;
        IndexPath = Path.Combine(Folder, "index.json");
    }

    public string Folder { get; }

    public string IndexPath { get; }

    public string ImagePath(ClipboardEntry entry) => Path.Combine(Folder, entry.ImageFile ?? "");

    public string ThumbPath(ClipboardEntry entry) => Path.Combine(Folder, entry.ThumbFile ?? "");

    public (int Width, int Height) SaveImage(ClipboardEntry entry, Bitmap source)
    {
        Directory.CreateDirectory(Folder);
        using var bounded = Bounded(source, MaxEdge);
        entry.ImageFile = entry.Id + ".png";
        entry.ThumbFile = entry.Id + ".thumb.png";
        entry.Width = bounded.Width;
        entry.Height = bounded.Height;
        bounded.Save(Path.Combine(Folder, entry.ImageFile), ImageFormat.Png);
        using var thumb = Thumbnail(bounded, ThumbSide, cover: true);
        thumb.Save(Path.Combine(Folder, entry.ThumbFile), ImageFormat.Png);
        return (bounded.Width, bounded.Height);
    }

    /// <summary>Cheap fingerprint: 32×32 average-free pixel hash, stable for identical bitmaps.</summary>
    public static string Signature(Bitmap bitmap)
    {
        using var small = Thumbnail(bitmap, 32);
        var info = new BitmapData();
        small.LockBits(new Rectangle(0, 0, small.Width, small.Height), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb, info);
        try
        {
            var bytes = new byte[info.Stride * small.Height];
            System.Runtime.InteropServices.Marshal.Copy(info.Scan0, bytes, 0, bytes.Length);
            return "img:" + Convert.ToHexString(System.Security.Cryptography.SHA1.HashData(bytes));
        }
        finally
        {
            small.UnlockBits(info);
        }
    }

    public void Delete(ClipboardEntry entry)
    {
        TryDelete(entry.ImageFile);
        TryDelete(entry.ThumbFile);
    }

    /// <summary>Removes PNGs that the index no longer references (crash leftovers).</summary>
    public void PruneOrphans(ClipboardHistory history)
    {
        try
        {
            if (!Directory.Exists(Folder))
            {
                return;
            }

            var keep = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var entry in history.Items)
            {
                if (!string.IsNullOrEmpty(entry.ImageFile))
                {
                    keep.Add(entry.ImageFile);
                }

                if (!string.IsNullOrEmpty(entry.ThumbFile))
                {
                    keep.Add(entry.ThumbFile);
                }
            }

            foreach (var file in Directory.EnumerateFiles(Folder, "*.png"))
            {
                if (!keep.Contains(Path.GetFileName(file)))
                {
                    File.Delete(file);
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Housekeeping is best-effort.
        }
    }

    private void TryDelete(string? name)
    {
        if (string.IsNullOrEmpty(name))
        {
            return;
        }

        try
        {
            var path = Path.Combine(Folder, name);
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // A locked file is not worth failing the whole operation.
        }
    }

    private static Bitmap Bounded(Bitmap source, int maxEdge)
    {
        var longest = Math.Max(source.Width, source.Height);
        if (longest <= maxEdge)
        {
            var copy = new Bitmap(source.Width, source.Height, PixelFormat.Format32bppArgb);
            using var g = Graphics.FromImage(copy);
            g.DrawImage(source, 0, 0, source.Width, source.Height);
            return copy;
        }

        var scale = maxEdge / (float)longest;
        var width = Math.Max(1, (int)Math.Round(source.Width * scale));
        var height = Math.Max(1, (int)Math.Round(source.Height * scale));
        var resized = new Bitmap(width, height, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(resized))
        {
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.DrawImage(source, 0, 0, width, height);
        }

        return resized;
    }

    /// <summary>
    /// Square preview. <paramref name="cover"/> crops instead of letterboxing: a padded
    /// 16:9 shot inside a square tile showed as a thin band surrounded by empty space.
    /// </summary>
    private static Bitmap Thumbnail(Bitmap source, int side, bool cover = false)
    {
        var scale = cover
            ? Math.Max(side / (float)source.Width, side / (float)source.Height)
            : Math.Min(side / (float)source.Width, side / (float)source.Height);
        var width = Math.Max(1, (int)Math.Round(source.Width * scale));
        var height = Math.Max(1, (int)Math.Round(source.Height * scale));
        var thumb = new Bitmap(side, side, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(thumb);
        g.Clear(Color.Transparent);
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        g.DrawImage(source, (side - width) / 2, (side - height) / 2, width, height);
        return thumb;
    }
}
