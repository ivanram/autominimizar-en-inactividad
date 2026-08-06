using System.Runtime.InteropServices;

namespace AutoMinimizarInactividad;

/// <summary>Reads system-wide keyboard/mouse idle time via the Win32 last-input timestamp.</summary>
internal static class IdleMonitor
{
    [StructLayout(LayoutKind.Sequential)]
    private struct LASTINPUTINFO
    {
        public uint cbSize;
        public uint dwTime;
    }

    [DllImport("user32.dll")]
    private static extern bool GetLastInputInfo(ref LASTINPUTINFO plii);

    public static TimeSpan GetIdleTime()
    {
        var lii = new LASTINPUTINFO { cbSize = (uint)Marshal.SizeOf<LASTINPUTINFO>() };
        if (!GetLastInputInfo(ref lii)) return TimeSpan.Zero;

        // Both operands are uint, so this wraps correctly (modulo 2^32) even
        // when Environment.TickCount itself has wrapped negative after ~24.9 days.
        var idleMs = unchecked((uint)Environment.TickCount - lii.dwTime);
        return TimeSpan.FromMilliseconds(idleMs);
    }
}
