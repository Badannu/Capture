namespace CaptureDemo.Core;

/// <summary>
/// Moves clipboard images that the index no longer references out of the way instead of
/// deleting them.
///
/// <para>
/// Why this is not just <c>File.Delete</c>: the panel's index is a small JSON file that
/// anything can truncate — a half-written save, a process killed mid-write, a user opening
/// it in an editor. When it comes back empty, every PNG in the folder looks like an orphan,
/// and sweeping them up quietly destroys screenshots the user cannot get back. A stray file
/// costs a few hundred KB; a wrong deletion is irreversible.
/// </para>
///
/// <para>
/// So this class only ever moves files into <c>orphan\</c>, and refuses to act when the
/// evidence looks more like damage than leftovers.
/// </para>
/// </summary>
public static class ClipboardPruner
{
    /// <summary>Sub-folder that receives quarantined files. Never read back by the panel.</summary>
    public const string QuarantineFolderName = "orphan";

    /// <summary>
    /// Upper bound for a single run. A handful of crash leftovers is normal; dozens means the
    /// index lost track of everything, and in that case the safe answer is to do nothing.
    /// </summary>
    public const int MaxQuarantinePerRun = 8;

    /// <summary>
    /// Moves every PNG in <paramref name="folder"/> that <paramref name="history"/> does not
    /// reference into <c>orphan\&lt;timestamp&gt;-&lt;name&gt;</c>.
    /// </summary>
    /// <param name="history">The loaded index. Its entry file names are what counts as "referenced".</param>
    /// <param name="folder">The clipboard folder. Files are only ever looked at there, not recursively.</param>
    /// <returns>How many files were moved. Zero whenever the sweep was skipped on purpose.</returns>
    public static int Quarantine(ClipboardHistory history, string folder)
    {
        ArgumentNullException.ThrowIfNull(history);

        if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder))
        {
            return 0;
        }

        // Zero records next to a pile of PNGs is far more likely a damaged index than N
        // orphans. Doing nothing is free; guessing wrong costs the user their history.
        if (history.Count == 0)
        {
            return 0;
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

        var orphans = new List<string>();
        foreach (var file in Directory.EnumerateFiles(folder, "*.png", SearchOption.TopDirectoryOnly))
        {
            if (!keep.Contains(Path.GetFileName(file)))
            {
                orphans.Add(file);
            }
        }

        if (orphans.Count == 0 || orphans.Count > MaxQuarantinePerRun)
        {
            return 0;
        }

        var quarantine = Path.Combine(folder, QuarantineFolderName);
        try
        {
            Directory.CreateDirectory(quarantine);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return 0;
        }

        var stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss", System.Globalization.CultureInfo.InvariantCulture);
        var moved = 0;
        foreach (var file in orphans)
        {
            try
            {
                // The stamp keeps repeated sweeps from overwriting each other.
                var target = Path.Combine(quarantine, stamp + "-" + Path.GetFileName(file));
                File.Move(file, target, overwrite: true);
                moved++;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // A locked file is not worth failing the whole sweep over.
            }
        }

        return moved;
    }
}
