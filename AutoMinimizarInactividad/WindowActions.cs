using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace AutoMinimizarInactividad;

public sealed record RunningAppInfo(string ProcessName, string WindowTitle, string? ExePath, int ProcessId);

/// <summary>Win32-backed actions: minimizing specific apps, minimizing everything, and listing open windows.</summary>
internal static class WindowActions
{
    private const int SW_MINIMIZE = 6;
    private const int GWL_EXSTYLE = -20;
    private const int WS_EX_TOOLWINDOW = 0x00000080;
    private const uint GW_OWNER = 4;

    private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool IsIconic(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    private static extern int GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    [DllImport("user32.dll")]
    private static extern int GetWindowTextLength(IntPtr hWnd);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(IntPtr hWnd, System.Text.StringBuilder lpString, int nMaxCount);

    [DllImport("user32.dll")]
    private static extern IntPtr GetWindow(IntPtr hWnd, uint uCmd);

    [DllImport("user32.dll")]
    private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

    /// <summary>Minimizes every visible, non-minimized top-level window belonging to any of the given process names.</summary>
    public static void MinimizeProcesses(IEnumerable<string> processNames)
    {
        var names = new HashSet<string>(processNames, StringComparer.OrdinalIgnoreCase);
        if (names.Count == 0) return;

        EnumWindows((hWnd, _) =>
        {
            if (!IsWindowVisible(hWnd) || IsIconic(hWnd)) return true;

            GetWindowThreadProcessId(hWnd, out var pid);
            try
            {
                using var proc = Process.GetProcessById((int)pid);
                if (names.Contains(proc.ProcessName))
                {
                    ShowWindow(hWnd, SW_MINIMIZE);
                }
            }
            catch
            {
                // Process may have exited between enumeration and lookup — ignore.
            }
            return true;
        }, IntPtr.Zero);
    }

    /// <summary>Shows the desktop by minimizing every window, same as the taskbar's "Show desktop" corner.</summary>
    public static void MinimizeAllWindows()
    {
        var shellType = Type.GetTypeFromProgID("Shell.Application");
        if (shellType is null) return;

        object? shell = null;
        try
        {
            shell = Activator.CreateInstance(shellType);
            shellType.InvokeMember("MinimizeAll", BindingFlags.InvokeMethod, null, shell, null);
        }
        finally
        {
            if (shell is not null) Marshal.ReleaseComObject(shell);
        }
    }

    /// <summary>Launches the screensaver the user has configured in Windows, if any.</summary>
    public static void LaunchScreensaver()
    {
        var path = FindConfiguredScreensaver();
        if (path is null || !File.Exists(path)) return;

        Process.Start(new ProcessStartInfo(path, "/s") { UseShellExecute = true });
    }

    private static string? FindConfiguredScreensaver()
    {
        using var key = Registry.CurrentUser.OpenSubKey(@"Control Panel\Desktop");
        var value = key?.GetValue("SCRNSAVE.EXE") as string;
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }

    private const ushort VK_MEDIA_PLAY_PAUSE = 0xB3;
    private const uint INPUT_KEYBOARD = 1;
    private const uint KEYEVENTF_KEYUP = 0x0002;

    [StructLayout(LayoutKind.Sequential)]
    private struct KEYBDINPUT
    {
        public ushort wVk;
        public ushort wScan;
        public uint dwFlags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct InputUnion
    {
        [FieldOffset(0)] public KEYBDINPUT ki;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct INPUT
    {
        public uint type;
        public InputUnion U;
    }

    [DllImport("user32.dll")]
    private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

    /// <summary>
    /// Sends the system media play/pause key — the same signal a keyboard's
    /// media key sends, routed by Windows to whatever app currently owns
    /// media playback. It's a toggle (there's no OS-wide "pause only"
    /// command without querying each app's own playback state), so this
    /// pauses if something is playing and does nothing meaningful otherwise.
    /// </summary>
    public static void PauseMedia()
    {
        var inputs = new[]
        {
            new INPUT { type = INPUT_KEYBOARD, U = new InputUnion { ki = new KEYBDINPUT { wVk = VK_MEDIA_PLAY_PAUSE } } },
            new INPUT { type = INPUT_KEYBOARD, U = new InputUnion { ki = new KEYBDINPUT { wVk = VK_MEDIA_PLAY_PAUSE, dwFlags = KEYEVENTF_KEYUP } } },
        };
        SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<INPUT>());
    }

    /// <summary>Lists distinct processes that currently own a visible, top-level, non-tool window — candidates to add to the target list.</summary>
    public static List<RunningAppInfo> ListRunningApps()
    {
        var result = new List<RunningAppInfo>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var selfName = Process.GetCurrentProcess().ProcessName;

        EnumWindows((hWnd, _) =>
        {
            if (!IsWindowVisible(hWnd)) return true;
            if (GetWindowTextLength(hWnd) == 0) return true;
            if (GetWindow(hWnd, GW_OWNER) != IntPtr.Zero) return true;
            if ((GetWindowLong(hWnd, GWL_EXSTYLE) & WS_EX_TOOLWINDOW) != 0) return true;

            GetWindowThreadProcessId(hWnd, out var pid);
            try
            {
                using var proc = Process.GetProcessById((int)pid);
                if (proc.ProcessName.Equals(selfName, StringComparison.OrdinalIgnoreCase)) return true;
                if (!seen.Add(proc.ProcessName)) return true;

                var sb = new System.Text.StringBuilder(256);
                GetWindowText(hWnd, sb, sb.Capacity);
                string? exePath = null;
                try { exePath = proc.MainModule?.FileName; } catch { /* access denied for elevated/protected processes */ }

                result.Add(new RunningAppInfo(proc.ProcessName, sb.ToString(), exePath, proc.Id));
            }
            catch
            {
                // Process may have exited, or access is denied — skip it.
            }
            return true;
        }, IntPtr.Zero);

        return result.OrderBy(a => a.WindowTitle, StringComparer.OrdinalIgnoreCase).ToList();
    }
}
