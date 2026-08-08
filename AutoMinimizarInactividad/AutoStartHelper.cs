using Microsoft.Win32;

namespace AutoMinimizarInactividad;

/// <summary>
/// Registers/unregisters the app under the per-user Run key so it launches
/// at sign-in.
///
/// A Scheduled Task (trigger: at log on) was tried instead, since it keeps
/// an inspectable run history the Run key doesn't — but `schtasks /create`
/// returned "Acceso denegado" for a non-admin user on the machine this was
/// tested on, which (together with Microsoft Defender for Endpoint and
/// Cloudflare WARP being installed there) points to a centrally-managed
/// security policy blocking task creation for standard users. The Run key
/// write itself never failed, so this reverts to it — if autostart still
/// doesn't fire on a machine like that, the same policy is the likely
/// suspect, not this code; see README's autostart troubleshooting note.
/// </summary>
internal static class AutoStartHelper
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "AutoMinimizarInactividad";

    public static void Sync(bool enabled)
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true);
        if (key is null) return;

        if (!enabled)
        {
            key.DeleteValue(ValueName, throwOnMissingValue: false);
            return;
        }

        var exePath = Environment.ProcessPath;
        if (string.IsNullOrEmpty(exePath)) return;
        key.SetValue(ValueName, $"\"{exePath}\"");
    }
}
