using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using H.NotifyIcon;

namespace AutoMinimizarInactividad;

public sealed class TrayOrchestrator : IDisposable
{
    private static readonly Guid TrayIconGuid = new("b6b6e9b0-6b7b-4a7a-9c6a-6f2a2f8b9a41");

    [DllImport("user32.dll")]
    private static extern uint GetDoubleClickTime();

    private TaskbarIcon _trayIcon = null!;
    private readonly DispatcherTimer _idleTimer = new() { Interval = TimeSpan.FromSeconds(1) };
    private readonly DispatcherTimer _clickTimer = new();
    private bool _pendingSingleClick;
    private bool _triggered;
    private double _lastIdleMs;
    private readonly AppSettings _settings = AppSettings.Load();
    private SettingsWindow? _settingsWindow;
    private MenuItem _toggleMenuItem = null!;

    public void Start()
    {
        AutoStartHelper.Sync(_settings.StartWithWindows);

        _trayIcon = new TaskbarIcon
        {
            Id = TrayIconGuid,
            Icon = IconFactory.BuildTrayIcon(32, _settings.IsPaused),
            ToolTipText = BuildTooltip(),
            ContextMenu = BuildContextMenu(),
        };
        _trayIcon.TrayLeftMouseUp += (s, e) => OnTrayLeftClick();
        _trayIcon.TrayMouseDoubleClick += (s, e) => OnTrayDoubleClick();

        // TaskbarIcon normally creates its native icon from its own Loaded
        // event, which only fires once it's parented into a visual tree.
        // We construct it standalone in code, so force creation explicitly.
        _trayIcon.ForceCreate(enablesEfficiencyMode: false);

        var doubleClickMs = GetDoubleClickTime();
        _clickTimer.Interval = TimeSpan.FromMilliseconds(doubleClickMs > 0 ? doubleClickMs : 500);
        _clickTimer.Tick += (s, e) =>
        {
            _clickTimer.Stop();
            if (!_pendingSingleClick) return;
            _pendingSingleClick = false;
            ToggleActive();
        };

        _idleTimer.Tick += (s, e) => OnIdleTick();
        _idleTimer.Start();
    }

    /// <summary>
    /// A single left click toggles active/paused, a double click opens
    /// settings — but the OS delivers a double click as two separate clicks
    /// in quick succession, so the first click's action has to wait out the
    /// system double-click interval before firing, in case a second click
    /// turns it into "open settings" instead.
    /// </summary>
    private void OnTrayLeftClick()
    {
        _pendingSingleClick = true;
        _clickTimer.Stop();
        _clickTimer.Start();
    }

    private void OnTrayDoubleClick()
    {
        _clickTimer.Stop();
        _pendingSingleClick = false;
        OpenSettings();
    }

    private void ToggleActive()
    {
        _settings.IsPaused = !_settings.IsPaused;
        _settings.Save();
        _triggered = false;
        RefreshTrayVisuals();
    }

    private void OnIdleTick()
    {
        var idle = IdleMonitor.GetIdleTime();
        var idleMs = idle.TotalMilliseconds;

        // The OS idle clock only ever resets to (near) zero on real input,
        // so a drop from the previous reading means the user just acted —
        // re-arm the trigger for the next idle stretch.
        if (idleMs < _lastIdleMs) _triggered = false;
        _lastIdleMs = idleMs;

        if (_settings.IsPaused || _triggered) return;

        var thresholdSeconds = Math.Max(5, _settings.InactivitySeconds);
        if (idle.TotalSeconds < thresholdSeconds) return;

        Trigger();
        _triggered = true;
    }

    private void Trigger()
    {
        if (_settings.MinimizeAllInstead)
            WindowActions.MinimizeAllWindows();
        else
            WindowActions.MinimizeProcesses(_settings.TargetApps.Select(a => a.ProcessName));

        if (_settings.AlsoOpenScreensaver)
            WindowActions.LaunchScreensaver();
    }

    private void OpenSettings()
    {
        if (_settingsWindow is { IsVisible: true })
        {
            _settingsWindow.Activate();
            return;
        }

        _settingsWindow = new SettingsWindow(_settings);
        _settingsWindow.SettingsChanged += (s, e) => RefreshTrayVisuals();
        _settingsWindow.Closed += (s, e) => _settingsWindow = null;
        _settingsWindow.Show();
        _settingsWindow.Activate();
    }

    private ContextMenu BuildContextMenu()
    {
        var menu = new ContextMenu();

        _toggleMenuItem = new MenuItem();
        _toggleMenuItem.Click += (s, e) => ToggleActive();
        menu.Items.Add(_toggleMenuItem);

        var settingsItem = new MenuItem { Header = "_Ajustes..." };
        settingsItem.Click += (s, e) => OpenSettings();
        menu.Items.Add(settingsItem);

        menu.Items.Add(new Separator());

        var exitItem = new MenuItem { Header = "_Salir" };
        exitItem.Click += (s, e) => Application.Current.Shutdown();
        menu.Items.Add(exitItem);

        UpdateToggleMenuText();
        return menu;
    }

    private void UpdateToggleMenuText()
    {
        _toggleMenuItem.Header = _settings.IsPaused ? "_Activar" : "_Pausar";
    }

    private void RefreshTrayVisuals()
    {
        _trayIcon.Icon = IconFactory.BuildTrayIcon(32, _settings.IsPaused);
        _trayIcon.ToolTipText = BuildTooltip();
        UpdateToggleMenuText();
    }

    private string BuildTooltip()
    {
        var state = _settings.IsPaused ? "Pausado" : $"Activo ({_settings.InactivitySeconds}s)";
        return $"Autominimizar en inactividad — {state}";
    }

    public void Dispose()
    {
        _idleTimer.Stop();
        _clickTimer.Stop();
        _trayIcon.Dispose();
    }
}
