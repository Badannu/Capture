using System.Diagnostics;
using System.Runtime.InteropServices;
using CaptureDemo.Core;

namespace CaptureDemo.Native;

/// <summary>
/// Asks Windows whether a chord is available right now, without keeping it.
/// <para>
/// The only honest test is the real thing: <c>RegisterHotKey</c> on a message-only
/// window. There is no API that lists the hotkeys other processes own, so a refusal
/// plus <c>ERROR_HOTKEY_ALREADY_REGISTERED</c> is as close as we get to "someone else
/// has it". The probe releases the chord immediately afterwards — the caller must not
/// hold the same chord at the same time, or the probe will accuse itself.
/// </para>
/// </summary>
internal sealed class HotkeyProbe : NativeWindow, IDisposable
{
    private const int ProbeId = 0xC0F9;
    private static readonly IntPtr HwndMessage = new(-3);

    private bool _held;

    public HotkeyProbe()
    {
        CreateHandle(new CreateParams
        {
            Caption = "CaptureHotkeyProbe",
            Parent = HwndMessage,
        });
    }

    /// <summary>Win32 code from the last refused registration; 0 when it succeeded.</summary>
    public int LastError { get; private set; }

    public bool IsAlreadyRegistered => LastError == ErrorHotkeyAlreadyRegistered;

    /// <summary>Registers, remembers, and answers whether the OS said yes.</summary>
    public bool CanRegister(HotkeyChord chord)
    {
        Release();
        var ok = NativeMethods.RegisterHotKey(Handle, ProbeId, chord.Modifiers | NativeMethods.ModNoRepeat, chord.VirtualKey);
        LastError = ok ? 0 : Marshal.GetLastWin32Error();
        _held = ok;
        return ok;
    }

    public void Release()
    {
        if (!_held)
        {
            return;
        }

        NativeMethods.UnregisterHotKey(Handle, ProbeId);
        _held = false;
    }

    public void Dispose()
    {
        Release();
        if (Handle != IntPtr.Zero)
        {
            DestroyHandle();
        }
    }

    /// <summary>ERROR_HOTKEY_ALREADY_REGISTERED.</summary>
    public const int ErrorHotkeyAlreadyRegistered = 1409;

    protected override void WndProc(ref Message m)
    {
        // A key could be pressed in the microsecond the probe holds a chord. Swallow the
        // message: acting on it would start a capture the user never asked for.
        if (m.Msg == NativeMethods.WmHotkey)
        {
            return;
        }

        base.WndProc(ref m);
    }
}

/// <summary>
/// Ties the probe, the running-process list and the Core decision order together.
/// One instance per dialog is enough: creating a probe window per keystroke would leak.
/// </summary>
internal sealed class HotkeyAuditor : IDisposable
{
    private static readonly TimeSpan CacheFor = TimeSpan.FromSeconds(4);

    private readonly HotkeyProbe _probe = new();
    private string[] _processes = Array.Empty<string>();
    private DateTime _refreshed = DateTime.MinValue;

    /// <summary>Checks one row; siblings are the other rows currently on screen.</summary>
    public HotkeyConflict Check(string text, string action, IReadOnlyList<(string Action, string Text)> others) =>
        HotkeyAudit.Check(text, action, others, RunningProcesses(), _probe.CanRegister);

    /// <summary>Explains a registration that already failed (the main window path).</summary>
    public HotkeyConflict Explain(HotkeyChord chord, int error) =>
        HotkeyAudit.Held(chord, RunningProcesses(), error == HotkeyProbe.ErrorHotkeyAlreadyRegistered
            ? string.Empty
            : $"（系统返回错误码 {error}）");

    /// <summary>
    /// Process names are only a hint for "who might own this", so a stale list for a few
    /// seconds is harmless — and re-enumerating on every keystroke is not free.
    /// </summary>
    private IReadOnlyCollection<string> RunningProcesses()
    {
        if (DateTime.UtcNow - _refreshed < CacheFor)
        {
            return _processes;
        }

        try
        {
            var names = new List<string>(256);
            foreach (var process in Process.GetProcesses())
            {
                try
                {
                    names.Add(process.ProcessName);
                }
                catch (InvalidOperationException)
                {
                    // The process exited between enumeration and reading its name.
                }
                finally
                {
                    process.Dispose();
                }
            }

            _processes = names.ToArray();
            _refreshed = DateTime.UtcNow;
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            // Enumeration can fail on a locked-down machine; an empty list only means
            // "owner unknown", never "hotkey free".
            _processes = Array.Empty<string>();
        }

        return _processes;
    }

    public void Dispose() => _probe.Dispose();
}
