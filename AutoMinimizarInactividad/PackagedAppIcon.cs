using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace AutoMinimizarInactividad;

/// <summary>
/// Resolves the real tile icon for a Microsoft Store (MSIX/UWP) app, given
/// the process id of one of its running processes — e.g. WhatsApp Desktop.
///
/// Packaged apps don't carry a classic Win32 icon resource in their exe (the
/// icon lives as a separate asset referenced by the package manifest), so
/// <c>Icon.ExtractAssociatedIcon</c>/<c>SHGetFileInfo</c> on the exe path
/// come up empty. This instead asks the shell to resolve the app through
/// its Application User Model ID via the same "shell:AppsFolder\..." path
/// Explorer itself uses for Start menu tiles, so it returns the actual
/// installed icon rather than a stand-in — no bundled copy of anyone's
/// artwork needed in this repo.
/// </summary>
internal static class PackagedAppIcon
{
    private const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(uint dwDesiredAccess, bool bInheritHandle, int dwProcessId);

    [DllImport("kernel32.dll")]
    private static extern bool CloseHandle(IntPtr hObject);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetApplicationUserModelId(IntPtr hProcess, ref uint applicationUserModelIdLength, StringBuilder? applicationUserModelId);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteObject(IntPtr hObject);

    [StructLayout(LayoutKind.Sequential)]
    private struct SIZE
    {
        public int cx;
        public int cy;
    }

    [Flags]
    private enum SIIGBF
    {
        ResizeToFit = 0x00,
        BiggerSizeOk = 0x01,
        IconOnly = 0x04,
    }

    [ComImport, Guid("43826D1E-E718-42EE-BC55-A1E261C37BFE"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellItem
    {
    }

    [ComImport, Guid("BCC18B79-BA16-442F-80C4-8A59C30C463B"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellItemImageFactory
    {
        void GetImage(SIZE size, SIIGBF flags, out IntPtr phbm);
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = false)]
    private static extern void SHCreateItemFromParsingName(
        [MarshalAs(UnmanagedType.LPWStr)] string path,
        IntPtr pbc,
        [MarshalAs(UnmanagedType.LPStruct)] Guid riid,
        [MarshalAs(UnmanagedType.Interface)] out IShellItem shellItem);

    /// <summary>Returns null if the process isn't a packaged app, or the icon can't be resolved for any reason.</summary>
    public static ImageSource? TryGetIcon(int processId, int size = 48)
    {
        var aumid = TryGetAumid(processId);
        if (string.IsNullOrEmpty(aumid)) return null;

        try
        {
            SHCreateItemFromParsingName($@"shell:AppsFolder\{aumid}", IntPtr.Zero, typeof(IShellItemImageFactory).GUID, out var shellItem);
            var factory = (IShellItemImageFactory)shellItem;
            factory.GetImage(new SIZE { cx = size, cy = size }, SIIGBF.ResizeToFit | SIIGBF.BiggerSizeOk, out var hBitmap);
            if (hBitmap == IntPtr.Zero) return null;

            try
            {
                var src = Imaging.CreateBitmapSourceFromHBitmap(hBitmap, IntPtr.Zero, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
                src.Freeze();
                return src;
            }
            finally
            {
                DeleteObject(hBitmap);
            }
        }
        catch
        {
            return null;
        }
    }

    private static string? TryGetAumid(int processId)
    {
        var hProcess = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, processId);
        if (hProcess == IntPtr.Zero) return null;

        try
        {
            uint length = 0;
            GetApplicationUserModelId(hProcess, ref length, null);
            if (length == 0) return null;

            var sb = new StringBuilder((int)length);
            var result = GetApplicationUserModelId(hProcess, ref length, sb);
            return result == 0 ? sb.ToString() : null; // 0 == ERROR_SUCCESS
        }
        finally
        {
            CloseHandle(hProcess);
        }
    }
}
