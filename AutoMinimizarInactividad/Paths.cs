using System.IO;

namespace AutoMinimizarInactividad;

/// <summary>
/// Central place for every writable path the app uses, all rooted under
/// %LOCALAPPDATA%\AutoMinimizarInactividad — never next to the exe.
/// </summary>
internal static class Paths
{
    public static string AppDataDir { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AutoMinimizarInactividad");

    public static string LogsDir
    {
        get
        {
            var dir = Path.Combine(AppDataDir, "logs");
            Directory.CreateDirectory(dir);
            return dir;
        }
    }
}
