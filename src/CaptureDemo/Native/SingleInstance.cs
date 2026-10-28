using System.Runtime.InteropServices;

namespace CaptureDemo.Native;

/// <summary>One instance per session: a second launch wakes the running one instead.</summary>
internal static class SingleInstance
{
    private const string MutexName = @"Local\CaptureDemo.SingleInstance.v1";
    private const int HwndBroadcast = 0xFFFF;

    private static Mutex? _mutex;

    public static uint Message { get; private set; }

    public static bool TryAcquire()
    {
        Message = RegisterWindowMessage("CaptureDemo.Activate.v1");
        _mutex = new Mutex(true, MutexName, out var created);
        if (!created)
        {
            return false;
        }

        return true;
    }

    /// <summary>wParam carries the launch mode so the running instance behaves like the new one.</summary>
    public static void Signal(int mode)
    {
        if (Message != 0)
        {
            PostMessage(new IntPtr(HwndBroadcast), Message, new IntPtr(mode), IntPtr.Zero);
        }
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern uint RegisterWindowMessage(string message);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool PostMessage(IntPtr hWnd, uint message, IntPtr wParam, IntPtr lParam);
}

/// <summary>HKCU Run entry; no elevation and no scheduled task needed.</summary>
internal static class StartupRegistration
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "CaptureDemo";

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
}
