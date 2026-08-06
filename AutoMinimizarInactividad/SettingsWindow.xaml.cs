using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using DrawingIcon = System.Drawing.Icon;

namespace AutoMinimizarInactividad;

internal sealed class AppListItem
{
    /// <summary>Usually one process name; WhatsApp is grouped so this can hold several (see SettingsWindow.WhatsAppProcessNames).</summary>
    public List<string> ProcessNames { get; set; } = new();
    public string DisplayName { get; set; } = "";
    public bool IsRunning { get; set; }
    public bool IsChecked { get; set; }
    public ImageSource? Icon { get; set; }
    public string ProcessNamesLabel => string.Join(", ", ProcessNames);

    /// <summary>The synthetic "Minimizar todo" row, rather than a real process.</summary>
    public bool IsSpecialMinimizeAll { get; set; }

    /// <summary>False while "Minimizar todo" is checked — greys the row out but never touches its IsChecked value.</summary>
    public bool IsEnabled { get; set; } = true;

    /// <summary>Overrides the small gray line under the name — used by the "Minimizar todo" row.</summary>
    public string? SubtitleOverride { get; set; }
    public string SubtitleText => SubtitleOverride ?? ProcessNamesLabel;

    /// <summary>Shown on hover so a truncated (ellipsized) row never hides information.</summary>
    public string TooltipText => $"{DisplayName}\n{SubtitleText}";
}

public partial class SettingsWindow : Window
{
    public event EventHandler? SettingsChanged;

    private readonly AppSettings _settings;
    private List<AppListItem> _appListItems = new();
    private bool _syncingSecondsControls;

    public SettingsWindow(AppSettings settings)
    {
        _settings = settings;
        InitializeComponent();

        SecondsTextBox.Text = _settings.InactivitySeconds.ToString();
        SecondsSlider.Value = Math.Clamp(_settings.InactivitySeconds, SecondsSlider.Minimum, SecondsSlider.Maximum);
        ScreensaverCheckBox.IsChecked = _settings.AlsoOpenScreensaver;
        StartWithWindowsCheckBox.IsChecked = _settings.StartWithWindows;

        RefreshAppList();
    }

    private void RefreshButton_Click(object sender, RoutedEventArgs e) => RefreshAppList();

    /// <summary>
    /// WhatsApp Desktop's Store package shows up as two separate top-level
    /// windows/processes ("WhatsApp" and "WhatsApp.Root") — this app is
    /// built specifically around WhatsApp, so both are merged into one
    /// "WhatsApp" row instead of confusing the user with two entries.
    /// </summary>
    private static readonly string[] WhatsAppProcessNames = { "WhatsApp", "WhatsApp.Root" };

    private static bool IsWhatsAppProcess(string processName) =>
        WhatsAppProcessNames.Contains(processName, StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Toggling "Minimizar todo" needs to grey out (but not clear) the
    /// individually-selected apps, live, without waiting for Save — this
    /// reads whatever the checkbox currently shows (falling back to the
    /// saved setting on first load) before the list gets rebuilt below.
    /// </summary>
    private bool CurrentMinimizeAllChecked() =>
        _appListItems.FirstOrDefault(i => i.IsSpecialMinimizeAll)?.IsChecked ?? _settings.MinimizeAllInstead;

    private void RefreshAppList()
    {
        var minimizeAllChecked = CurrentMinimizeAllChecked();

        CommitCheckedAppsFromCurrentList();

        var running = WindowActions.ListRunningApps();
        var items = new List<AppListItem>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var whatsAppRunning = false;
        string? whatsAppExePath = null;
        int? whatsAppPid = null;

        foreach (var app in running)
        {
            seen.Add(app.ProcessName);

            if (IsWhatsAppProcess(app.ProcessName))
            {
                whatsAppRunning = true;
                whatsAppExePath ??= app.ExePath;
                whatsAppPid ??= app.ProcessId;
                continue;
            }

            items.Add(new AppListItem
            {
                ProcessNames = new List<string> { app.ProcessName },
                DisplayName = string.IsNullOrWhiteSpace(app.WindowTitle) ? app.ProcessName : app.WindowTitle,
                IsRunning = true,
                IsChecked = _settings.TargetApps.Any(t => t.ProcessName.Equals(app.ProcessName, StringComparison.OrdinalIgnoreCase)),
                IsEnabled = !minimizeAllChecked,
                Icon = TryLoadIcon(app.ExePath),
            });
        }

        // Apps already configured but not currently running still need a
        // row — otherwise closing an app would silently drop it from the
        // target list the next time this window refreshes.
        foreach (var target in _settings.TargetApps)
        {
            if (IsWhatsAppProcess(target.ProcessName)) continue; // folded into the synthetic WhatsApp row below
            if (seen.Contains(target.ProcessName)) continue;
            items.Add(new AppListItem
            {
                ProcessNames = new List<string> { target.ProcessName },
                DisplayName = string.IsNullOrWhiteSpace(target.DisplayName) ? target.ProcessName : target.DisplayName,
                IsRunning = false,
                IsChecked = true,
                IsEnabled = !minimizeAllChecked,
            });
        }

        items.Add(new AppListItem
        {
            ProcessNames = WhatsAppProcessNames.ToList(),
            DisplayName = "WhatsApp",
            IsRunning = whatsAppRunning,
            IsChecked = _settings.TargetApps.Any(t => IsWhatsAppProcess(t.ProcessName)),
            IsEnabled = !minimizeAllChecked,
            Icon = ResolveWhatsAppIcon(whatsAppPid, whatsAppExePath),
        });

        items = items
            .OrderByDescending(i => i.IsChecked)
            .ThenBy(i => i.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        // Pinned at the top, always enabled — it's the switch that controls
        // whether the rest of the rows below are usable.
        items.Insert(0, new AppListItem
        {
            IsSpecialMinimizeAll = true,
            DisplayName = "Minimizar todo",
            SubtitleOverride = "Se mostrará el Escritorio",
            IsChecked = minimizeAllChecked,
            IsEnabled = true,
            Icon = TryGetDesktopIcon(),
        });

        _appListItems = items;
        AppsList.ItemsSource = _appListItems;
    }

    private void AppCheckBox_CheckedChanged(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: AppListItem item } || !item.IsSpecialMinimizeAll) return;
        RefreshAppList();
    }

    /// <summary>
    /// WhatsApp Desktop's real icon lives as an asset inside its Microsoft
    /// Store package, not as a classic exe resource, so it's resolved
    /// through the shell's packaged-app icon lookup (see PackagedAppIcon) —
    /// reading it live from the user's own installed copy rather than
    /// bundling a static copy of Meta's artwork in this repo. Falls back to
    /// plain extraction (covers a non-Store install) and finally to this
    /// app's own bubble glyph if WhatsApp isn't running at all.
    /// </summary>
    private static ImageSource? ResolveWhatsAppIcon(int? processId, string? exePath)
    {
        if (processId is not null)
        {
            var packagedIcon = PackagedAppIcon.TryGetIcon(processId.Value);
            if (packagedIcon is not null) return packagedIcon;
        }

        var classicIcon = TryLoadIcon(exePath);
        if (classicIcon is not null) return classicIcon;

        using var icon = IconFactory.BuildPlainBubbleIcon(32);
        var src = Imaging.CreateBitmapSourceFromHIcon(icon.Handle, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
        src.Freeze();
        return src;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SHFILEINFO
    {
        public IntPtr hIcon;
        public int iIcon;
        public uint dwAttributes;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        public string szDisplayName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)]
        public string szTypeName;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr SHGetFileInfo(string pszPath, uint dwFileAttributes, ref SHFILEINFO psfi, uint cbFileInfo, uint uFlags);

    [DllImport("user32.dll")]
    private static extern bool DestroyIcon(IntPtr hIcon);

    private const uint SHGFI_ICON = 0x100;
    private const uint SHGFI_LARGEICON = 0x0;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct SHSTOCKICONINFO
    {
        public uint cbSize;
        public IntPtr hIcon;
        public int iSysImageIndex;
        public int iIcon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        public string szPath;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHGetStockIconInfo(uint siid, uint uFlags, ref SHSTOCKICONINFO psii);

    private const uint SIID_DESKTOP = 0x36;
    private const uint SHGSI_ICON = 0x100;

    private static ImageSource? _desktopIconCache;

    /// <summary>The Windows "Show Desktop" glyph, via the shell's stock icon table — same icon Explorer itself uses.</summary>
    private static ImageSource? TryGetDesktopIcon()
    {
        if (_desktopIconCache is not null) return _desktopIconCache;

        try
        {
            var info = new SHSTOCKICONINFO { cbSize = (uint)Marshal.SizeOf<SHSTOCKICONINFO>() };
            var hr = SHGetStockIconInfo(SIID_DESKTOP, SHGSI_ICON, ref info);
            if (hr != 0 || info.hIcon == IntPtr.Zero) return null;

            try
            {
                var src = Imaging.CreateBitmapSourceFromHIcon(info.hIcon, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
                src.Freeze();
                _desktopIconCache = src;
                return src;
            }
            finally
            {
                DestroyIcon(info.hIcon);
            }
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Packaged (Microsoft Store) apps like WhatsApp Desktop live under
    /// %ProgramFiles%\WindowsApps, whose ACL denies direct file reads to
    /// normal user processes — <see cref="DrawingIcon.ExtractAssociatedIcon"/>
    /// throws for those paths, so this falls back to the shell's own icon
    /// resolver, which resolves them through the same path Explorer uses.
    /// </summary>
    private static ImageSource? TryLoadIcon(string? exePath)
    {
        if (string.IsNullOrWhiteSpace(exePath)) return null;

        try
        {
            using var icon = DrawingIcon.ExtractAssociatedIcon(exePath);
            if (icon is not null)
            {
                var src = Imaging.CreateBitmapSourceFromHIcon(icon.Handle, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
                src.Freeze();
                return src;
            }
        }
        catch
        {
            // Fall through to the shell-based fallback below.
        }

        try
        {
            var info = new SHFILEINFO();
            var handle = SHGetFileInfo(exePath, 0, ref info, (uint)Marshal.SizeOf<SHFILEINFO>(), SHGFI_ICON | SHGFI_LARGEICON);
            if (handle == IntPtr.Zero || info.hIcon == IntPtr.Zero) return null;

            try
            {
                var src = Imaging.CreateBitmapSourceFromHIcon(info.hIcon, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
                src.Freeze();
                return src;
            }
            finally
            {
                DestroyIcon(info.hIcon);
            }
        }
        catch
        {
            return null;
        }
    }

    private void SecondsTextBox_PreviewTextInput(object sender, System.Windows.Input.TextCompositionEventArgs e)
    {
        e.Handled = !Regex.IsMatch(e.Text, "^[0-9]+$");
    }

    private void SecondsTextBox_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        if (_syncingSecondsControls) return;
        if (!int.TryParse(SecondsTextBox.Text, out var seconds)) return;

        _syncingSecondsControls = true;
        SecondsSlider.Value = Math.Clamp(seconds, SecondsSlider.Minimum, SecondsSlider.Maximum);
        _syncingSecondsControls = false;
    }

    private void SecondsSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_syncingSecondsControls) return;

        _syncingSecondsControls = true;
        SecondsTextBox.Text = ((int)e.NewValue).ToString();
        _syncingSecondsControls = false;
    }

    private void CommitCheckedAppsFromCurrentList()
    {
        if (_appListItems.Count == 0) return;

        _settings.TargetApps = _appListItems
            .Where(i => i.IsChecked)
            .SelectMany(i => i.ProcessNames.Select(pn => new TargetApp { ProcessName = pn, DisplayName = i.DisplayName }))
            .ToList();
    }

    private void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        var seconds = int.TryParse(SecondsTextBox.Text, out var parsed) ? parsed : _settings.InactivitySeconds;
        _settings.InactivitySeconds = Math.Max(5, seconds);
        _settings.MinimizeAllInstead = CurrentMinimizeAllChecked();
        _settings.AlsoOpenScreensaver = ScreensaverCheckBox.IsChecked == true;
        _settings.StartWithWindows = StartWithWindowsCheckBox.IsChecked == true;

        CommitCheckedAppsFromCurrentList();
        _settings.Save();
        AutoStartHelper.Sync(_settings.StartWithWindows);

        SettingsChanged?.Invoke(this, EventArgs.Empty);
        Close();
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e) => Close();
}
