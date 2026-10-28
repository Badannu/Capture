using CaptureDemo.Core;

namespace CaptureDemo.Native;

/// <summary>
/// One running copy per run form: a second launch wakes the running one instead.
/// </summary>
/// <remarks>
/// The scope is intentionally split between the portable and the installed copy. A user who
/// keeps a portable folder next to an installed build has two genuinely different apps with
/// two different config roots, so they must both be allowed to run. Sharing one mutex made the
/// second one exit immediately while its activation broadcast woke up both windows.
/// </remarks>
internal static class SingleInstance
{
    private const int HwndBroadcast = 0xFFFF;

    // Kept alive for the process lifetime: letting the handle be finalized would release the
    // mutex and let a second copy start while this one is still running.
#pragma warning disable IDE0052 // 可删除私有成员：这个字段的用途就是持有句柄，从不读取是设计。
    private static Mutex? _mutex;
#pragma warning restore IDE0052

    /// <summary>
    /// Portable or installed. <see cref="AppSettings.IsPortable"/> only looks at
    /// <see cref="AppContext.BaseDirectory"/> plus one file probe, so evaluating it from the
    /// very top of <c>Main</c> — before any window, settings or theme exists — is safe.
    /// </summary>
    private static string Mode => AppSettings.IsPortable() ? "portable" : "installed";

    private static string MutexName => @"Local\Capture.Instance." + Mode + ".v1";

    private static string ActivateMessageName => "Capture.Activate." + Mode + ".v1";

    /// <summary>Registered activation message id, or 0 when registration failed.</summary>
    public static uint Message { get; private set; }

    /// <summary>
    /// Registers the activation message and takes the per-mode mutex. Returns false when
    /// another instance of the same run form is already running; <see cref="Message"/> is
    /// still set in that case so the caller can <see cref="Signal"/> it.
    /// </summary>
    public static bool TryAcquire()
    {
        Message = NativeMethods.RegisterWindowMessage(ActivateMessageName);

        var mutex = new Mutex(true, MutexName, out var created);
        if (!created)
        {
            // Not the owner: drop our handle right away, the owner keeps the mutex alive.
            mutex.Dispose();
            return false;
        }

        _mutex = mutex;
        return true;
    }

    /// <summary>wParam carries the launch mode so the running instance behaves like the new one.</summary>
    public static void Signal(int mode)
    {
        if (Message != 0)
        {
            // HWND_BROADCAST reaches every top-level window, but only the instance whose own
            // mode is baked into the message name knows the id and reacts to it.
            NativeMethods.PostMessage(new IntPtr(HwndBroadcast), Message, new IntPtr(mode), IntPtr.Zero);
        }
    }
}

/// <summary>HKCU Run entry; no elevation and no scheduled task needed.</summary>
internal static class StartupRegistration
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";

    /// <summary>Value name written by 1.5 and later, matching the Capture.exe file name.</summary>
    private const string ValueName = "Capture";

    /// <summary>Value name written by 1.4.4 and earlier, pointing at the old CaptureDemo.exe.</summary>
    private const string LegacyValueName = "CaptureDemo";

    public static bool IsEnabled()
    {
        try
        {
            using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(RunKey);
            return key?.GetValue(ValueName) is string value && value.Length > 0;
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException)
        {
            return false;
        }
    }

    public static void Set(bool enabled)
    {
        try
        {
            using var key = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(RunKey);
            if (key == null)
            {
                return;
            }

            MigrateLegacyValue(key);

            if (enabled)
            {
                var path = Environment.ProcessPath;
                if (!string.IsNullOrEmpty(path))
                {
                    key.SetValue(ValueName, $"\"{path}\"");
                }
            }
            else if (key.GetValue(ValueName) != null)
            {
                key.DeleteValue(ValueName, false);
            }
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException)
        {
            // Startup is a convenience; failing to write it must not break the app.
        }
    }

    /// <summary>
    /// Brings the Run key in line with the saved setting once per launch: drops the pre-rename
    /// value, refreshes our own path (so a portable folder that was moved still autostarts from
    /// the right place) and removes our value when the setting is off.
    /// </summary>
    /// <remarks>
    /// Unlike <see cref="Set"/>, this never removes a value that points at a different copy of
    /// the app: since 1.5 an installed build and an unzipped portable build can run side by
    /// side, and only one of them can own the single Run entry. Deleting someone else's entry
    /// here would silently turn off autostart for the other flavour.
    /// </remarks>
    public static void Sync(bool enabled)
    {
        try
        {
            using var key = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(RunKey);
            if (key == null)
            {
                return;
            }

            MigrateLegacyValue(key);

            var path = Environment.ProcessPath;
            if (string.IsNullOrEmpty(path))
            {
                return;
            }

            if (enabled)
            {
                key.SetValue(ValueName, $"\"{path}\"");
                return;
            }

            if (key.GetValue(ValueName) is string existing && PointsAt(existing, path))
            {
                key.DeleteValue(ValueName, false);
            }
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException)
        {
            // Same rule as Set: autostart trouble must never stop the app from starting.
        }
    }

    /// <summary>True when a Run value already holds this exact exe path.</summary>
    private static bool PointsAt(string value, string processPath) =>
        value.Contains(processPath, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Removes the pre-rename Run value. It points at <c>CaptureDemo.exe</c>, a file name 1.5
    /// no longer ships, so leaving it behind would make every logon try to start a missing
    /// exe. The user's intent is preserved by the new <c>Capture</c> value, which
    /// <see cref="Sync"/> rewrites from the saved <c>LaunchAtStartup</c> setting on every launch.
    /// </summary>
    private static void MigrateLegacyValue(Microsoft.Win32.RegistryKey key)
    {
        if (key.GetValue(LegacyValueName) == null)
        {
            return;
        }

        key.DeleteValue(LegacyValueName, false);
    }
}
