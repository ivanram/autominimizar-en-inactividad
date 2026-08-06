using Microsoft.Win32;

namespace AutoMinimizarInactividad;

/// <summary>Registers/unregisters the app under the per-user Run key so it launches at sign-in.</summary>
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
