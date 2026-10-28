using System.Diagnostics;
using CaptureDemo.Core;
using CaptureDemo.Ui;

namespace CaptureDemo.Clips;

/// <summary>
/// Ties the watcher, the index and the on-disk store together, and exposes the
/// actions the history panel and tray menu call into.
/// </summary>
internal sealed class ClipboardService : IDisposable
{
    private static readonly TimeSpan EchoWindow = TimeSpan.FromMilliseconds(500);

    private readonly AppSettings _settings;
    private readonly ClipboardStore _store;
    private readonly ClipboardHistory _history;
    private readonly ClipboardWatcher? _watcher;
    private ClipboardForm? _panel;
    private DateTime _ignoreUntil;

    public ClipboardService(AppSettings settings)
        : this(settings, listen: true, store: null)
    {
    }

    public ClipboardService(AppSettings settings, bool listen, ClipboardStore? store)
    {
        _settings = settings;
        _store = store ?? new ClipboardStore();
        _history = ClipboardIndex.Load(_store.IndexPath, settings.ClipboardLimit);
        if (listen)
        {
            _store.PruneOrphans(_history);
            _watcher = new ClipboardWatcher { Suspended = !settings.WatchClipboard };
            _watcher.Captured += OnCaptured;
        }
    }

    public event Action? Changed;

    public ClipboardHistory History => _history;

    public ClipboardStore Store => _store;

    public void ApplySettings()
    {
        _history.Capacity = _settings.ClipboardLimit;
        if (_watcher != null)
        {
            _watcher.Suspended = !_settings.WatchClipboard;
        }

        Persist();
    }

    public void TogglePanel()
    {
        if (_panel is { Visible: true })
        {
            HidePanel();
            return;
        }

        ShowPanel();
    }

    public void ShowPanel()
    {
        if (_panel is { IsDisposed: false, Visible: true })
        {
            _panel.Activate();
            return;
        }

        _panel?.Dispose();
        _panel = new ClipboardForm(this);
        _panel.FormClosed += (_, _) =>
        {
            _panel = null;
        };
        _panel.ShowNearCursor();
    }

    public void HidePanel()
    {
        if (_panel is { IsDisposed: false })
        {
            _panel.Hide();
        }
    }

    /// <summary>Primary action: images and files open in their default app, text goes back to the clipboard.</summary>
    public void Activate(ClipboardEntry entry)
    {
        if (entry.Kind == ClipboardKind.Text)
        {
            CopyEntry(entry);
            HidePanel();
            return;
        }

        Open(entry);
    }

    public void Open(ClipboardEntry entry)
    {
        try
        {
            switch (entry.Kind)
            {
                case ClipboardKind.Image when entry.HasImage:
                    Shell(_store.ImagePath(entry));
                    break;
                case ClipboardKind.Files when entry.Files != null:
                    foreach (var file in entry.Files.Take(12).Where(File.Exists))
                    {
                        Shell(file);
                    }

                    break;
            }
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or IOException or InvalidOperationException)
        {
            // The association may be missing; nothing useful to show from a popup.
        }
    }

    public void CopyEntry(ClipboardEntry entry)
    {
        _ignoreUntil = DateTime.UtcNow + EchoWindow;
        try
        {
            switch (entry.Kind)
            {
                case ClipboardKind.Image when entry.HasImage:
                {
                    using var bitmap = new Bitmap(_store.ImagePath(entry));
                    System.Windows.Forms.Clipboard.SetImage(bitmap);
                    break;
                }

                case ClipboardKind.Files when entry.Files != null:
                {
                    var files = new System.Collections.Specialized.StringCollection();
                    files.AddRange(entry.Files.Where(File.Exists).ToArray());
                    if (files.Count == 0)
                    {
                        return;
                    }

                    System.Windows.Forms.Clipboard.SetFileDropList(files);
                    break;
                }

                default:
                    System.Windows.Forms.Clipboard.SetText(entry.Text ?? "");
                    break;
            }
        }
        catch (Exception ex) when (ex is System.Runtime.InteropServices.ExternalException or IOException)
        {
            // Clipboard busy: leave it alone rather than throwing at the user.
        }
    }

    /// <summary>Copies just the stored path of a file entry as text.</summary>
    public void CopyPath(ClipboardEntry entry)
    {
        var text = entry.Files == null ? "" : string.Join(Environment.NewLine, entry.Files);
        if (string.IsNullOrEmpty(text))
        {
            return;
        }

        _ignoreUntil = DateTime.UtcNow + EchoWindow;
        try
        {
            System.Windows.Forms.Clipboard.SetText(text);
        }
        catch (System.Runtime.InteropServices.ExternalException)
        {
        }
    }

    public void SaveAs(ClipboardEntry entry, IWin32Window? owner)
    {
        using var dialog = new SaveFileDialog
        {
            Title = "另存为",
            Filter = entry.Kind == ClipboardKind.Image ? "PNG 图片|*.png" : "文本文件|*.txt",
            FileName = AppSettings.FormatFileName(_settings.FilenamePattern, "clip", DateTime.Now)
                + (entry.Kind == ClipboardKind.Image ? ".png" : ".txt"),
            InitialDirectory = _settings.ResolveDirectory(),
            OverwritePrompt = true,
        };

        if (dialog.ShowDialog(owner) != DialogResult.OK)
        {
            return;
        }

        try
        {
            if (entry.Kind == ClipboardKind.Image && entry.HasImage)
            {
                File.Copy(_store.ImagePath(entry), dialog.FileName, true);
            }
            else
            {
                File.WriteAllText(dialog.FileName, entry.Text ?? "");
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            MessageBox.Show(owner, "保存失败：" + ex.Message, "剪贴板");
        }
    }

    public void Reveal(ClipboardEntry entry)
    {
        try
        {
            if (entry.Kind == ClipboardKind.Image && entry.HasImage)
            {
                Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{_store.ImagePath(entry)}\"")
                {
                    UseShellExecute = true,
                });
                return;
            }

            var folder = entry.Files?.FirstOrDefault(File.Exists) is { } file
                ? Path.GetDirectoryName(file)
                : _store.Folder;
            if (!string.IsNullOrEmpty(folder))
            {
                Shell(folder);
            }
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException or IOException)
        {
        }
    }

    public void TogglePin(ClipboardEntry entry)
    {
        _history.TogglePin(entry.Id);
        Persist();
        Changed?.Invoke();
    }

    public void Delete(ClipboardEntry entry)
    {
        _history.Remove(entry.Id);
        _store.Delete(entry);
        Persist();
        Changed?.Invoke();
    }

    public void ClearAll()
    {
        foreach (var item in _history.ClearUnpinned())
        {
            _store.Delete(item);
        }

        Persist();
        Changed?.Invoke();
    }

    public void Dispose()
    {
        _panel?.Dispose();
        _panel = null;
        if (_watcher != null)
        {
            _watcher.Captured -= OnCaptured;
            _watcher.Dispose();
        }
    }

    private void OnCaptured(ClipboardSnapshot snapshot)
    {
        if (DateTime.UtcNow < _ignoreUntil)
        {
            snapshot.Image?.Dispose();
            return;
        }

        ClipboardEntry entry;
        try
        {
            entry = snapshot.Kind switch
            {
                ClipboardKind.Image => CaptureImage(snapshot),
                ClipboardKind.Files => new ClipboardEntry
                {
                    Id = Guid.NewGuid().ToString("n"),
                    Kind = ClipboardKind.Files,
                    CreatedUtc = DateTime.UtcNow,
                    Files = snapshot.Files,
                    Signature = "fs:" + string.Join("|", snapshot.Files ?? new List<string>()),
                },
                _ => new ClipboardEntry
                {
                    Id = Guid.NewGuid().ToString("n"),
                    Kind = ClipboardKind.Text,
                    CreatedUtc = DateTime.UtcNow,
                    Text = snapshot.Text,
                    Signature = "txt:" + snapshot.Text,
                },
            };
        }
        finally
        {
            snapshot.Image?.Dispose();
        }

        var evicted = _history.Push(entry);
        foreach (var old in evicted)
        {
            _store.Delete(old);
        }

        Persist();
        Changed?.Invoke();
    }

    private ClipboardEntry CaptureImage(ClipboardSnapshot snapshot)
    {
        var entry = new ClipboardEntry
        {
            Id = Guid.NewGuid().ToString("n"),
            Kind = ClipboardKind.Image,
            CreatedUtc = DateTime.UtcNow,
        };

        if (snapshot.Image != null)
        {
            entry.Signature = ClipboardStore.Signature(snapshot.Image);
            _store.SaveImage(entry, snapshot.Image);
        }

        return entry;
    }

    private void Persist()
    {
        try
        {
            ClipboardIndex.Save(_store.IndexPath, _history);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Losing the index is tolerable; the history still works this session.
        }
    }

    private static void Shell(string target)
    {
        Process.Start(new ProcessStartInfo(target) { UseShellExecute = true });
    }
}
