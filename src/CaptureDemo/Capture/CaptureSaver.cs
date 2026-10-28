using System.Diagnostics;
using System.Drawing.Imaging;
using CaptureDemo.Core;

namespace CaptureDemo.Capture;

internal readonly record struct SaveResult(string Path, bool CopiedToClipboard);

internal static class CaptureSaver
{
    public static SaveResult SavePng(Bitmap bitmap, string kind, AppSettings? settings = null)
    {
        settings ??= AppSettings.CreateDefault();
        settings.Normalize();
        var directory = settings.ResolveDirectory();
        Directory.CreateDirectory(directory);
        var name = AppSettings.FormatFileName(settings.FilenamePattern, kind, DateTime.Now);
        if (!name.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
        {
            name += ".png";
        }

        var path = UniquePath(directory, name);
        bitmap.Save(path, ImageFormat.Png);
        var copied = settings.CopyToClipboard && TryCopyClipboard(bitmap);
        return new SaveResult(path, copied);
    }

    public static string ClipboardPhrase(AppSettings settings, bool copied)
    {
        if (!settings.CopyToClipboard)
        {
            return "";
        }

        return copied ? "已复制到剪贴板。" : "文件已保存，剪贴板未能写入。";
    }

    public static void Reveal(string? filePath, AppSettings? settings = null)
    {
        settings ??= AppSettings.CreateDefault();
        var directory = settings.ResolveDirectory();
        Directory.CreateDirectory(directory);
        if (!string.IsNullOrEmpty(filePath) && File.Exists(filePath))
        {
            Process.Start(new ProcessStartInfo("explorer.exe", "/select,\"" + filePath + "\"")
            {
                UseShellExecute = true,
            });
            return;
        }

        Process.Start(new ProcessStartInfo("explorer.exe", "\"" + directory + "\"")
        {
            UseShellExecute = true,
        });
    }

    private static string UniquePath(string directory, string name)
    {
        var path = Path.Combine(directory, name);
        if (!File.Exists(path))
        {
            return path;
        }

        var stem = Path.GetFileNameWithoutExtension(name);
        var ext = Path.GetExtension(name);
        for (var i = 2; i < 1000; i++)
        {
            path = Path.Combine(directory, stem + "-" + i + ext);
            if (!File.Exists(path))
            {
                return path;
            }
        }

        return Path.Combine(directory, stem + "-" + Guid.NewGuid().ToString("N") + ext);
    }

    private static bool TryCopyClipboard(Bitmap bitmap)
    {
        try
        {
            Clipboard.SetDataObject(bitmap, copy: true);
            return true;
        }
        catch
        {
            return false;
        }
    }
}
