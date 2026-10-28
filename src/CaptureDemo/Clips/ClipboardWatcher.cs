using CaptureDemo.Core;

namespace CaptureDemo.Clips;

internal sealed class ClipboardSnapshot
{
    public ClipboardKind Kind { get; init; }

    public Bitmap? Image { get; init; }

    public string? Text { get; init; }

    public List<string>? Files { get; init; }
}

/// <summary>
/// Listens for WM_CLIPBOARDUPDATE on a message-only window. Reading the clipboard can
/// fail while another process holds it, so every read is retried before giving up.
/// Updates are coalesced: Office and Excel fire several updates for one copy.
/// </summary>
internal sealed class ClipboardWatcher : NativeWindow, IDisposable
{
    private readonly System.Windows.Forms.Timer _debounce;
    private bool _disposed;

    public ClipboardWatcher()
    {
        _debounce = new System.Windows.Forms.Timer { Interval = 130 };
        _debounce.Tick += OnDebounce;
        CreateHandle(new CreateParams
        {
            Caption = "CaptureDemo.ClipboardSink",
            Parent = new IntPtr(-3),
        });
        NativeMethods.AddClipboardFormatListener(Handle);
    }

    public event Action<ClipboardSnapshot>? Captured;

    public bool Suspended { get; set; }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == NativeMethods.WmClipboardUpdate && !Suspended)
        {
            _debounce.Stop();
            _debounce.Start();
        }

        base.WndProc(ref m);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _debounce.Stop();
        _debounce.Dispose();
        if (Handle != IntPtr.Zero)
        {
            NativeMethods.RemoveClipboardFormatListener(Handle);
            DestroyHandle();
        }
    }

    private void OnDebounce(object? sender, EventArgs e)
    {
        _debounce.Stop();
        var snapshot = Read();
        if (snapshot != null)
        {
            Captured?.Invoke(snapshot);
        }
    }

    private static ClipboardSnapshot? Read()
    {
        for (var attempt = 0; attempt < 6; attempt++)
        {
            try
            {
                if (System.Windows.Forms.Clipboard.ContainsImage())
                {
                    if (System.Windows.Forms.Clipboard.GetImage() is Bitmap image && image.Width > 0 && image.Height > 0)
                    {
                        return new ClipboardSnapshot { Kind = ClipboardKind.Image, Image = image };
                    }
                }

                if (System.Windows.Forms.Clipboard.ContainsFileDropList())
                {
                    var files = System.Windows.Forms.Clipboard.GetFileDropList();
                    var list = new List<string>();
                    foreach (var file in files)
                    {
                        if (!string.IsNullOrWhiteSpace(file))
                        {
                            list.Add(file);
                        }
                    }

                    if (list.Count > 0)
                    {
                        return new ClipboardSnapshot { Kind = ClipboardKind.Files, Files = list };
                    }
                }

                if (System.Windows.Forms.Clipboard.ContainsText())
                {
                    var text = System.Windows.Forms.Clipboard.GetText();
                    if (!string.IsNullOrWhiteSpace(text))
                    {
                        return new ClipboardSnapshot { Kind = ClipboardKind.Text, Text = text };
                    }
                }

                return null;
            }
            catch (System.Runtime.InteropServices.ExternalException)
            {
                Thread.Sleep(30);
            }
            catch (Exception ex) when (ex is ThreadStateException or ArgumentException)
            {
                return null;
            }
        }

        return null;
    }
}
