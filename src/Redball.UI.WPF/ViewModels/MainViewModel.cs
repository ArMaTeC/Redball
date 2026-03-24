using System.Diagnostics;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.Input;
using Redball.UI.Services;
using Redball.UI.Views;

namespace Redball.UI.ViewModels;

/// <summary>
/// Main ViewModel for Redball v3.0 WPF UI
/// Handles state management and command binding
/// </summary>
public class MainViewModel : ViewModelBase
{
    private bool _isActive = true;
    private string _statusText = "Active | Display On | F15 On";
    private bool _isDarkMode = true;
    private string _memoryUsageText = "";
    private string _batteryText = "";
    private string _uptimeText = "";
    private WeakReference<MainWindow>? _mainWindowRef;
    private readonly KeepAwakeService _keepAwake;
    private readonly DispatcherTimer _statusBarTimer;
    private readonly BatteryMonitorService _batteryMonitor = new();

    public bool PreventDisplaySleep
    {
        get => _keepAwake.PreventDisplaySleep;
        set
        {
            if (_keepAwake.PreventDisplaySleep == value)
            {
                return;
            }

            _keepAwake.PreventDisplaySleep = value;
            OnPropertyChanged();
            UpdateStatusText();
        }
    }

    private void EmergencyHidRelease()
    {
        Logger.Warning("MainViewModel", "Emergency HID release command invoked from tray");

        if (_mainWindowRef != null && _mainWindowRef.TryGetTarget(out var mainWindow))
        {
            mainWindow.EmergencyReleaseHid("Tray menu", true);
            return;
        }

        var fallbackMainWindow = Application.Current.MainWindow as MainWindow;
        if (fallbackMainWindow != null)
        {
            fallbackMainWindow.EmergencyReleaseHid("Tray menu fallback", true);
            return;
        }

        InterceptionInputService.Instance.ReleaseResources("Tray emergency release (no window reference)");
        NotificationService.Instance.ShowWarning("HID Emergency Release", "HID resources released from tray action.");
    }

    public bool UseHeartbeat
    {
        get => _keepAwake.UseHeartbeat;
        set
        {
            if (_keepAwake.UseHeartbeat == value)
            {
                return;
            }

            _keepAwake.UseHeartbeat = value;
            OnPropertyChanged();
            UpdateStatusText();
        }
    }

    public MainViewModel()
    {
        Logger.Info("MainViewModel", "Constructor called");
        
        _keepAwake = KeepAwakeService.Instance;
        _keepAwake.ActiveStateChanged += OnKeepAwakeStateChanged;
        _keepAwake.TimedAwakeExpired += OnTimedAwakeExpired;
        
        ToggleActiveCommand = new RelayCommand(ToggleActive);
        PauseKeepAwakeCommand = new RelayCommand(ToggleActive);
        OpenSettingsCommand = new RelayCommand(OpenSettings);
        ExitCommand = new RelayCommand(ExitApplication);

        TypeThingCommand = new RelayCommand(StartTypeThing);
        ToggleDisplaySleepCommand = new RelayCommand(ToggleDisplaySleep);
        ToggleHeartbeatCommand = new RelayCommand(ToggleHeartbeat);
        OpenAnalyticsCommand = new RelayCommand(OpenAnalytics);
        OpenMetricsCommand = new RelayCommand(OpenMetrics);
        OpenDiagnosticsCommand = new RelayCommand(OpenDiagnostics);
        OpenLogsCommand = new RelayCommand(OpenLogs);
        OpenBehaviorCommand = new RelayCommand(OpenBehavior);
        OpenSmartFeaturesCommand = new RelayCommand(OpenSmartFeatures);
        OpenTypeThingCommand = new RelayCommand(OpenTypeThing);
        OpenPomodoroCommand = new RelayCommand(OpenPomodoro);
        OpenUpdatesCommand = new RelayCommand(OpenUpdates);
        OpenAboutCommand = new RelayCommand(() => ShowAbout());
        ShowWindowCommand = new RelayCommand(ShowWindow);
        ShowQuickSettingsCommand = new RelayCommand(ShowQuickSettings);
        EmergencyHidReleaseCommand = new RelayCommand(EmergencyHidRelease);
        ShowMiniWidgetCommand = new RelayCommand(ShowMiniWidget);
        ResetMiniWidgetPositionCommand = new RelayCommand(ResetMiniWidgetPosition);
        CheckForUpdatesCommand = new RelayCommand(CheckForUpdates);

        // Sync initial state
        _isActive = _keepAwake.IsActive;
        UpdateStatusText();
        UpdateStatusBar();

        _statusBarTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
        _statusBarTimer.Tick += (_, _) => UpdateStatusBar();
        _statusBarTimer.Start();
        
        Logger.Info("MainViewModel", "Commands initialized");
    }

    /// <summary>
    /// Sets the reference to the MainWindow for delegating window operations
    /// </summary>
    public void SetMainWindow(MainWindow window)
    {
        Logger.Debug("MainViewModel", "Setting MainWindow reference");
        _mainWindowRef = new WeakReference<MainWindow>(window);
    }

    public bool IsActive
    {
        get => _isActive;
        set
        {
            if (SetProperty(ref _isActive, value))
            {
                Logger.Info("MainViewModel", $"IsActive changed to: {value}");
                UpdateStatusText();
            }
        }
    }

    public string StatusText
    {
        get => _statusText;
        set
        {
            if (SetProperty(ref _statusText, value))
            {
                Logger.Verbose("MainViewModel", $"StatusText changed to: '{value}'");
            }
        }
    }

    public bool IsDarkMode
    {
        get => _isDarkMode;
        set
        {
            if (SetProperty(ref _isDarkMode, value))
            {
                Logger.Info("MainViewModel", $"IsDarkMode changed to: {value}");
                ThemeManager.SetTheme(value ? Theme.Dark : Theme.Light);
            }
        }
    }

    public ICommand ToggleActiveCommand { get; }
    public ICommand PauseKeepAwakeCommand { get; }
    public ICommand OpenSettingsCommand { get; }
    public ICommand ExitCommand { get; }
    
    public ICommand TypeThingCommand { get; }
    public ICommand ToggleDisplaySleepCommand { get; }
    public ICommand ToggleHeartbeatCommand { get; }
    public ICommand OpenAnalyticsCommand { get; }
    public ICommand OpenMetricsCommand { get; }
    public ICommand OpenDiagnosticsCommand { get; }
    public ICommand OpenLogsCommand { get; }
    public ICommand OpenBehaviorCommand { get; }
    public ICommand OpenSmartFeaturesCommand { get; }
    public ICommand OpenTypeThingCommand { get; }
    public ICommand OpenPomodoroCommand { get; }
    public ICommand OpenUpdatesCommand { get; }
    public ICommand OpenAboutCommand { get; }
    public ICommand ShowWindowCommand { get; }
    public ICommand ShowQuickSettingsCommand { get; }
    public ICommand EmergencyHidReleaseCommand { get; }
    public ICommand ShowMiniWidgetCommand { get; }
    public ICommand ResetMiniWidgetPositionCommand { get; }
    public ICommand CheckForUpdatesCommand { get; }

    public bool IsMiniWidgetVisible => _miniWidget != null && _miniWidget.IsVisible;

    public string MemoryUsageText
    {
        get => _memoryUsageText;
        set => SetProperty(ref _memoryUsageText, value);
    }

    public string BatteryText
    {
        get => _batteryText;
        set => SetProperty(ref _batteryText, value);
    }

    public string UptimeText
    {
        get => _uptimeText;
        set => SetProperty(ref _uptimeText, value);
    }

    private void ToggleActive()
    {
        Logger.Info("MainViewModel", "ToggleActive called");
        _keepAwake.Toggle();
        try
        {
            var analytics = ServiceLocator.Get<IAnalyticsService>();
            analytics?.TrackFeature(_keepAwake.IsActive ? "keepawake.enabled" : "keepawake.disabled");
        }
        catch { /* Analytics is non-critical */ }
    }

    private void ToggleDisplaySleep()
    {
        PreventDisplaySleep = !PreventDisplaySleep;
    }

    private void ToggleHeartbeat()
    {
        UseHeartbeat = !UseHeartbeat;
    }

    private void OnKeepAwakeStateChanged(object? sender, bool isActive)
    {
        // Update on UI thread
        System.Windows.Application.Current?.Dispatcher.Invoke(() =>
        {
            IsActive = isActive;
        });
    }

    private void OnTimedAwakeExpired(object? sender, EventArgs e)
    {
        Logger.Info("MainViewModel", "Timed awake expired");
        System.Windows.Application.Current?.Dispatcher.Invoke(UpdateStatusText);
    }

    private void OpenSettings()
    {
        Logger.Info("MainViewModel", "OpenSettings called");
        
        // Delegate to MainWindow to show settings properly
        if (_mainWindowRef != null && _mainWindowRef.TryGetTarget(out var mainWindow))
        {
            Logger.Debug("MainViewModel", "Delegating to MainWindow");
            mainWindow.ShowSettings();
        }
        else
        {
            var fallbackMainWindow = Application.Current.MainWindow as MainWindow;
            if (fallbackMainWindow != null)
            {
                Logger.Warning("MainViewModel", "MainWindow reference not available, using Application.Current.MainWindow");
                fallbackMainWindow.ShowSettings();
            }
            else
            {
                Logger.Warning("MainViewModel", "MainWindow reference not available, cannot open embedded settings");
            }
        }
    }

    private void ShowWindow()
    {
        if (_mainWindowRef != null && _mainWindowRef.TryGetTarget(out var mainWindow))
        {
            mainWindow.ShowMainWindow();
        }
        else
        {
            var fallbackMainWindow = Application.Current.MainWindow as MainWindow;
            if (fallbackMainWindow != null)
            {
                fallbackMainWindow.ShowMainWindow();
            }
        }
    }

    private void StartTypeThing()
    {
        Logger.Info("MainViewModel", "StartTypeThing called");
        
        // Delegate to MainWindow for TypeThing paste-as-typing
        if (_mainWindowRef != null && _mainWindowRef.TryGetTarget(out var mainWindow))
        {
            Logger.Debug("MainViewModel", "Delegating TypeThing to MainWindow");
            mainWindow.StartTypeThing();
        }
        else
        {
            Logger.Warning("MainViewModel", "MainWindow reference not available, cannot start TypeThing");
        }
    }

    private void ShowAbout()
    {
        Logger.Info("MainViewModel", "ShowAbout called");
        
        // Delegate to MainWindow to show about properly
        if (_mainWindowRef != null && _mainWindowRef.TryGetTarget(out var mainWindow))
        {
            Logger.Debug("MainViewModel", "Delegating to MainWindow");
            mainWindow.ShowAbout();
        }
        else
        {
            var fallbackMainWindow = Application.Current.MainWindow as MainWindow;
            if (fallbackMainWindow != null)
            {
                Logger.Warning("MainViewModel", "MainWindow reference not available, using Application.Current.MainWindow");
                fallbackMainWindow.ShowAbout();
            }
            else
            {
                Logger.Warning("MainViewModel", "MainWindow reference not available, cannot show About window");
            }
        }
    }

    private void OpenAnalytics()
    {
        if (_mainWindowRef != null && _mainWindowRef.TryGetTarget(out var mainWindow))
        {
            mainWindow.ShowAnalytics();
        }
    }

    private void OpenMetrics()
    {
        if (_mainWindowRef != null && _mainWindowRef.TryGetTarget(out var mainWindow))
        {
            mainWindow.ShowMetrics();
        }
    }

    private void OpenDiagnostics()
    {
        if (_mainWindowRef != null && _mainWindowRef.TryGetTarget(out var mainWindow))
        {
            mainWindow.ShowDiagnostics();
        }
    }

    private void OpenLogs()
    {
        if (_mainWindowRef != null && _mainWindowRef.TryGetTarget(out var mainWindow))
        {
            mainWindow.OpenLogs();
        }
    }

    private void OpenBehavior()
    {
        if (_mainWindowRef != null && _mainWindowRef.TryGetTarget(out var mainWindow))
        {
            mainWindow.ShowBehavior();
        }
    }

    private void OpenSmartFeatures()
    {
        if (_mainWindowRef != null && _mainWindowRef.TryGetTarget(out var mainWindow))
        {
            mainWindow.ShowSmartFeatures();
        }
    }

    private void OpenTypeThing()
    {
        if (_mainWindowRef != null && _mainWindowRef.TryGetTarget(out var mainWindow))
        {
            mainWindow.ShowTypeThing();
        }
    }

    private void OpenPomodoro()
    {
        if (_mainWindowRef != null && _mainWindowRef.TryGetTarget(out var mainWindow))
        {
            mainWindow.ShowPomodoro();
        }
    }

    private void OpenUpdates()
    {
        if (_mainWindowRef != null && _mainWindowRef.TryGetTarget(out var mainWindow))
        {
            mainWindow.ShowUpdates();
        }
    }

    private void ExitApplication()
    {
        Logger.Info("MainViewModel", "ExitApplication called");
        
        // Confirm exit when configured and keep-awake is active
        var confirmOnExit = ConfigService.Instance.Config.ConfirmOnExit;
        if (confirmOnExit && _isActive)
        {
            Logger.Debug("MainViewModel", "Showing exit confirmation dialog");
            
            // Get the currently active window to use as owner (handles About/Settings windows being open)
            Window? ownerWindow = null;
            if (Application.Current.Windows.Count > 0)
            {
                // Find the active window or fall back to main window
                foreach (Window window in Application.Current.Windows)
                {
                    if (window.IsActive && window.IsVisible)
                    {
                        ownerWindow = window;
                        break;
                    }
                }
            }
            // Fall back to main window if no active window found
            if (ownerWindow == null)
            {
                ownerWindow = _mainWindowRef != null && _mainWindowRef.TryGetTarget(out var mw) 
                    ? mw 
                    : Application.Current.MainWindow;
            }

            var result = NotificationWindow.Show(
                "Confirm Exit",
                "Redball is currently keeping your system awake. Are you sure you want to exit?",
                "\uE7E8", // Power/Exit icon
                true);
                
            if (!result)
            {
                Logger.Info("MainViewModel", "User cancelled exit");
                return;
            }
            
            Logger.Info("MainViewModel", "User confirmed exit");
        }

        // Dispose tray icon before shutdown if we have a reference
        if (_mainWindowRef != null && _mainWindowRef.TryGetTarget(out var mainWindow))
        {
            Logger.Debug("MainViewModel", "Delegating exit to MainWindow");
            mainWindow.ExitApplication();
        }
        else
        {
            Logger.Info("MainViewModel", "Shutting down application directly");
            System.Windows.Application.Current.Shutdown();
        }
    }

    private void UpdateStatusText()
    {
        StatusText = _keepAwake.GetStatusText();
        OnPropertyChanged(nameof(PreventDisplaySleep));
        OnPropertyChanged(nameof(UseHeartbeat));
        Logger.Debug("MainViewModel", $"StatusText updated to: {StatusText}");
    }

    private void UpdateStatusBar()
    {
        var proc = Process.GetCurrentProcess();
        MemoryUsageText = $"Mem: {proc.WorkingSet64 / 1024 / 1024} MB";

        try
        {
            var status = _batteryMonitor.GetStatus();
            if (!status.HasBattery)
                BatteryText = "AC Power";
            else
                BatteryText = $"Battery: {status.ChargePercent}%{(status.IsOnBattery ? " (discharging)" : "")}";
        }
        catch
        {
            BatteryText = "";
        }

        var uptime = TimeSpan.FromMilliseconds(Environment.TickCount64);
        UptimeText = $"System uptime: {(int)uptime.TotalHours}h {uptime.Minutes}m";
    }

    /// <summary>
    /// Public method to force refresh status text from KeepAwakeService.
    /// Call this when heartbeat settings change to update the UI immediately.
    /// </summary>
    public void RefreshStatus()
    {
        UpdateStatusText();
    }

    private Views.MiniWidgetWindow? _miniWidget;

    private void ShowMiniWidget()
    {
        if (_miniWidget != null && _miniWidget.IsVisible)
        {
            _miniWidget.Activate();
            return;
        }

        _miniWidget = new Views.MiniWidgetWindow();
        _miniWidget.Closed += (_, _) =>
        {
            _miniWidget = null;
            OnPropertyChanged(nameof(IsMiniWidgetVisible));
        };
        _miniWidget.Show();
        OnPropertyChanged(nameof(IsMiniWidgetVisible));
        Logger.Info("MainViewModel", "Mini widget opened");
    }

    private void ResetMiniWidgetPosition()
    {
        if (_miniWidget != null && _miniWidget.IsVisible)
        {
            _miniWidget.ResetPosition();
            Logger.Info("MainViewModel", "Mini widget position reset");
        }
    }

    private void CheckForUpdates()
    {
        if (_mainWindowRef != null && _mainWindowRef.TryGetTarget(out var window))
        {
            _ = window.CheckForUpdatesAsync();
        }
    }

    private void ShowQuickSettings()
    {
        if (_mainWindowRef != null && _mainWindowRef.TryGetTarget(out var window))
        {
            var trayIcon = window.TrayIcon;
            if (trayIcon != null)
            {
                var popup = new Views.QuickSettingsPopup();
                trayIcon.ShowCustomBalloon(popup, System.Windows.Controls.Primitives.PopupAnimation.Slide, null);
            }
        }
    }

}
