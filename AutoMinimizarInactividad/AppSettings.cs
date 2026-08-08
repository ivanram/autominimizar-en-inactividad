using System.IO;
using System.Text.Json;

namespace AutoMinimizarInactividad;

/// <summary>A process to watch and auto-minimize, identified by executable name (without ".exe").</summary>
public sealed class TargetApp
{
    public string ProcessName { get; set; } = "";
    public string DisplayName { get; set; } = "";
}

public sealed class AppSettings
{
    public int InactivitySeconds { get; set; } = 30;
    public bool IsPaused { get; set; }

    public List<TargetApp> TargetApps { get; set; } = new()
    {
        new TargetApp { ProcessName = "WhatsApp", DisplayName = "WhatsApp" },
    };

    /// <summary>When true, ignores TargetApps and shows the desktop (minimizes every window) instead.</summary>
    public bool MinimizeAllInstead { get; set; }

    /// <summary>When true, additionally launches the configured Windows screensaver after minimizing.</summary>
    public bool AlsoOpenScreensaver { get; set; }

    public bool StartWithWindows { get; set; } = true;

    /// <summary>When true, additionally sends a media play/pause command after minimizing.</summary>
    public bool AlsoPauseMedia { get; set; }

    private static string FilePath => Path.Combine(Paths.AppDataDir, "settings.json");

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                var json = File.ReadAllText(FilePath);
                var loaded = JsonSerializer.Deserialize<AppSettings>(json);
                if (loaded is not null) return loaded;
            }
        }
        catch
        {
            // Ignore corrupt settings file, fall back to defaults.
        }
        return new AppSettings();
    }

    public void Save()
    {
        Directory.CreateDirectory(Paths.AppDataDir);
        // Write-to-temp-then-replace so a crash or force-kill mid-write can
        // never leave settings.json truncated or corrupt.
        var tmpPath = FilePath + ".tmp";
        File.WriteAllText(tmpPath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        File.Move(tmpPath, FilePath, overwrite: true);
    }
}
