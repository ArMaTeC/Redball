using System;
using System.Diagnostics;
using System.ComponentModel;
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
    private readonly SessionStatsService _sessionStats = SessionStatsService.Instance;
    private readonly DispatcherTimer _homeStatsTimer;
    private CommandPaletteViewModel? _palette;

    // Home tab real stats
    private int _typeThingSessionsToday;
    private int _charsTypedToday;
    private double _avgCharsPerMinute = 50; // Calculated from typing speed settings
    private int _keepAwakeSessionsToday;
    private TimeSpan _keepAwakeTimeToday;
    
    public DriverSelection TypeThingDriverSelection
    {
        get => ConfigService.Instance.Config.TypeThingDriverSelection;
        set
        {
            if (ConfigService.Instance.Config.TypeThingDriverSelection == value) return;
            ConfigService.Instance.Config.TypeThingDriverSelection = value;
            OnPropertyChanged();
            ConfigService.Instance.Save();
            Logger.Info("MainViewModel", $"Driver selection changed to {value}");
        }
    }

    public System.Collections.Generic.IEnumerable<DriverSelection> DriverSelectionOptions => System.Enum.GetValues<DriverSelection>();

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
        OpenUpdatesCommand = new RelayCommand(OpenUpdates);
        OpenAboutCommand = new RelayCommand(() => ShowAbout());
        ShowWindowCommand = new RelayCommand(ShowWindow);
        ShowQuickSettingsCommand = new RelayCommand(ShowQuickSettings);
        ShowMiniWidgetCommand = new RelayCommand(ShowMiniWidget);
        ResetMiniWidgetPositionCommand = new RelayCommand(ResetMiniWidgetPosition);
        CheckForUpdatesCommand = new RelayCommand(CheckForUpdates);
        InstallDriverCommand = new RelayCommand(async () => await InstallDriverAsync());

        // Sync initial state
        _isActive = _keepAwake.IsActive;
        UpdateStatusText();
        UpdateStatusBar();

        _statusBarTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
        _statusBarTimer.Tick += (_, _) => UpdateStatusBar();
        _statusBarTimer.Start();

        // Home stats timer - updates every 30 seconds
        _homeStatsTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(30) };
        _homeStatsTimer.Tick += (_, _) => UpdateHomeStats();
        _homeStatsTimer.Start();
        UpdateHomeStats(); // Initial load
        
        InitializePalette();
        
        Logger.Info("MainViewModel", "Commands initialized");
    }

    /// <summary>
    /// Updates the home tab statistics from SessionStatsService and calculates TypeThing stats.
    /// </summary>
    private void UpdateHomeStats()
    {
        try
        {
            // KeepAwake stats from SessionStatsService
            KeepAwakeSessionsToday = _sessionStats.TotalSessions;
            
            var today = DateTime.Now.ToString("yyyy-MM-dd");
            if (_sessionStats.DailyHours.TryGetValue(today, out var hours))
            {
                KeepAwakeTimeToday = TimeSpan.FromHours(hours);
            }
            else
            {
                KeepAwakeTimeToday = TimeSpan.Zero;
            }

            // TypeThing stats - calculate from typing speed settings
            // Chars/min = 60000ms / avg delay per char
            var config = ConfigService.Instance.Config;
            var avgDelayMs = (config.TypeThingMinDelayMs + config.TypeThingMaxDelayMs) / 2.0;
            if (avgDelayMs > 0)
            {
                AvgCharsPerMinute = 60000.0 / avgDelayMs;
            }

            // For now, TypeThing session count is tracked via analytics
            // We could add a proper TypeThingStatsService in the future
            // For now, estimate based on clipboard history size if available
            // or just show placeholder that updates occasionally
            if (TypeThingSessionsToday == 0)
            {
                // Will be populated from MainWindow.TypeThingHistory when available
                TypeThingSessionsToday = 0; // Placeholder until real tracking added
            }
            if (CharsTypedToday == 0)
            {
                CharsTypedToday = 0; // Placeholder until real tracking added
            }

            // Notify property changes for formatted display strings
            OnPropertyChanged(nameof(TypeThingSessionsTodayText));
            OnPropertyChanged(nameof(CharsTypedTodayText));
            OnPropertyChanged(nameof(AvgCharsPerMinuteText));
            OnPropertyChanged(nameof(KeepAwakeSessionsTodayText));
            OnPropertyChanged(nameof(KeepAwakeTimeTodayText));
        }
        catch (Exception ex)
        {
            Logger.Debug("MainViewModel", $"Failed to update home stats: {ex.Message}");
        }
    }

    /// <summary>
    /// Called by MainWindow to report TypeThing usage for stats tracking.
    /// </summary>
    public void ReportTypeThingUsage(int charsTyped)
    {
        TypeThingSessionsToday++;
        CharsTypedToday += charsTyped;
        UpdateHomeStats();
    }

    public CommandPaletteViewModel? Palette
    {
        get => _palette;
        set => SetProperty(ref _palette, value);
    }

    private void InitializePalette()
    {
        var commands = new List<PaletteCommand>
        {
            new PaletteCommand { Name = "Toggle Keep Awake", Description = "Turn keep-awake ON or OFF", Icon = "\uE708", Action = () => ToggleActive() },
            new PaletteCommand { Name = "Enable/Disable Display Sleep", Description = "Allow or prevent monitor sleep", Icon = "\uE7F4", Action = () => ToggleDisplaySleep() },
            new PaletteCommand { Name = "Toggle Heartbeat (F15)", Description = "Simulate F15 keypresses", Icon = "\uE945", Action = () => ToggleHeartbeat() },
            new PaletteCommand { Name = "Open Settings", Description = "View and modify Redball settings", Icon = "\uE713", Action = () => OpenSettings() },
            new PaletteCommand { Name = "Open Diagnostics", Description = "System diagnostics", Icon = "\uEBE1", Action = () => OpenDiagnostics() },
            new PaletteCommand { Name = "Exit Application", Description = "Close Redball completely", Icon = "\uE711", Action = () => ExitApplication() }
        };

        // Load Custom Commands from Config
        foreach (var cmd in ConfigService.Instance.Config.CustomCommands)
        {
            commands.Add(new PaletteCommand 
            { 
                Name = cmd.Name, 
                Description = cmd.Description, 
                Icon = cmd.Icon, 
                Action = () => ExecuteCustomCommand(cmd) 
            });
        }

        Palette = new CommandPaletteViewModel(commands);
    }

    private void ExecuteCustomCommand(CustomCommandMetadata cmd)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(cmd.Command)) return;

            Logger.Info("MainViewModel", $"Executing custom command: {cmd.Name} ({cmd.Command})");
            Process.Start(new ProcessStartInfo
            {
                FileName = cmd.Command,
                Arguments = cmd.Arguments,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            Logger.Error("MainViewModel", $"Failed to execute custom command {cmd.Name}", ex);
            NotificationService.Instance.ShowError("Command Failed", $"Could not execute '{cmd.Name}': {ex.Message}");
        }
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
    public ICommand InstallDriverCommand { get; }
    public ICommand ToggleDisplaySleepCommand { get; }
    public ICommand ToggleHeartbeatCommand { get; }
    public ICommand OpenAnalyticsCommand { get; }
    public ICommand OpenMetricsCommand { get; }
    public ICommand OpenDiagnosticsCommand { get; }
    public ICommand OpenLogsCommand { get; }
    public ICommand OpenBehaviorCommand { get; }
    public ICommand OpenSmartFeaturesCommand { get; }
    public ICommand OpenTypeThingCommand { get; }
    public ICommand OpenUpdatesCommand { get; }
    public ICommand OpenAboutCommand { get; }
    public ICommand ShowWindowCommand { get; }
    public ICommand ShowQuickSettingsCommand { get; }
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

    // Home tab statistics (real data)
    public int TypeThingSessionsToday
    {
        get => _typeThingSessionsToday;
        set => SetProperty(ref _typeThingSessionsToday, value);
    }

    public int CharsTypedToday
    {
        get => _charsTypedToday;
        set => SetProperty(ref _charsTypedToday, value);
    }

    public double AvgCharsPerMinute
    {
        get => _avgCharsPerMinute;
        set => SetProperty(ref _avgCharsPerMinute, value);
    }

    public int KeepAwakeSessionsToday
    {
        get => _keepAwakeSessionsToday;
        set => SetProperty(ref _keepAwakeSessionsToday, value);
    }

    public TimeSpan KeepAwakeTimeToday
    {
        get => _keepAwakeTimeToday;
        set => SetProperty(ref _keepAwakeTimeToday, value);
    }

    // Formatted display strings for XAML binding
    public string TypeThingSessionsTodayText => $"{TypeThingSessionsToday}";
    public string CharsTypedTodayText => CharsTypedToday.ToString("N0");
    public string AvgCharsPerMinuteText => $"{AvgCharsPerMinute:F0}";
    public string KeepAwakeSessionsTodayText => $"{KeepAwakeSessionsToday}";
    public string KeepAwakeTimeTodayText => $"{KeepAwakeTimeToday.TotalHours:F1}h";

    private void ToggleActive()
    {
        Logger.Info("MainViewModel", "ToggleActive called");
        _keepAwake.Toggle();
        try
        {
            var analytics = ServiceLocator.Get<IAnalyticsService>();
            analytics?.TrackFeature(_keepAwake.IsActive ? "keepawake.enabled" : "keepawake.disabled");
        }
        catch (Exception ex)
        {
            Logger.Debug("MainViewModel", $"Analytics tracking failed: {ex.Message}");
        }
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
        catch (Exception ex)
        {
            Logger.Debug("MainViewModel", $"Failed to get battery status: {ex.Message}");
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

    private async System.Threading.Tasks.Task InstallDriverAsync()
    {
        Logger.Info("MainViewModel", "Install driver command invoked for Service mode");

        // Service mode - install Windows Service
        var result = await InstallServiceAsync();
        if (result.Success)
        {
            NotificationService.Instance.ShowInfo("Service Installation", "Redball Input Service installed successfully. No restart required.");
        }
        else if (result.UserCancelled)
        {
            // User cancelled UAC - no error message needed, just log it
            Logger.Info("MainViewModel", "Service installation cancelled by user at UAC prompt");
        }
        else
        {
            NotificationService.Instance.ShowError("Service Installation", $"Failed to install Redball Input Service: {result.ErrorMessage}");
        }
    }

    /// <summary>
    /// Result of a service operation with user cancellation detection.
    /// </summary>
    private record ServiceOperationResult(bool Success, bool UserCancelled, string ErrorMessage);

    private async System.Threading.Tasks.Task<ServiceOperationResult> InstallServiceAsync()
    {
        return await System.Threading.Tasks.Task.Run(() =>
        {
            try
            {
                var servicePath = ResolveServiceExecutablePath();
                if (!System.IO.File.Exists(servicePath))
                {
                    var msg = $"Service executable not found: {servicePath}";
                    Logger.Error("MainViewModel", msg);
                    return new ServiceOperationResult(false, false, msg);
                }

                // Check admin rights - if not admin, relaunch app with elevation
                if (!Interop.NativeMethods.IsUserAnAdmin())
                {
                    Logger.Warning("MainViewModel", "Service installation requires admin rights; relaunching app with UAC elevation.");
                    return InstallServiceWithElevation();
                }

                // Already admin - install directly
                // Check if service already exists
                try
                {
                    using var sc = new System.ServiceProcess.ServiceController("RedballInputService");
                    Logger.Info("MainViewModel", "Redball Input Service already installed");
                    return new ServiceOperationResult(true, false, string.Empty);
                }
                catch (Exception ex)
                {
                    Logger.Debug("MainViewModel", $"Service check failed (service not installed): {ex.Message}");
                }

                var createResult = RunProcess("sc.exe", $"create RedballInputService binPath= \"{servicePath}\" start= auto");
                if (createResult.ExitCode != 0)
                {
                    if (createResult.StdErr.Contains("already exists", StringComparison.OrdinalIgnoreCase))
                    {
                        Logger.Info("MainViewModel", "Service already exists, attempting to start...");
                    }
                    else
                    {
                        var err = $"Failed to create service: {createResult.StdErr}";
                        Logger.Error("MainViewModel", err);
                        return new ServiceOperationResult(false, false, err);
                    }
                }

                var startResult = RunProcess("sc.exe", "start RedballInputService");
                if (startResult.ExitCode != 0)
                {
                    Logger.Warning("MainViewModel", $"Service created but failed to start: {startResult.StdErr}");
                }

                Logger.Info("MainViewModel", "Redball Input Service installed successfully");
                return new ServiceOperationResult(true, false, string.Empty);
            }
            catch (Exception ex)
            {
                Logger.Error("MainViewModel", "Service installation failed", ex);
                return new ServiceOperationResult(false, false, $"Unexpected error: {ex.Message}");
            }
        });
    }

    /// <summary>
    /// Relaunches the application with elevation to install the service.
    /// This is more reliable than trying to elevate sc.exe directly.
    /// </summary>
    private ServiceOperationResult InstallServiceWithElevation()
    {
        try
        {
            var processInfo = new ProcessStartInfo
            {
                FileName = Process.GetCurrentProcess().MainModule?.FileName ?? "Redball.UI.WPF.exe",
                Arguments = "--install-service",
                UseShellExecute = true,
                Verb = "runas"
            };

            try
            {
                using var process = Process.Start(processInfo);
                if (process == null)
                {
                    var err = "Failed to start elevated process.";
                    Logger.Error("MainViewModel", err);
                    return new ServiceOperationResult(false, false, err);
                }

                // Wait with 60-second timeout to prevent hanging
                if (!process.WaitForExit(60000))
                {
                    Logger.Warning("MainViewModel", "Elevated service install process timed out after 60 seconds");
                    try { process.Kill(); }
                    catch (Exception ex)
                    {
                        Logger.Debug("MainViewModel", $"Failed to kill timed out install process: {ex.Message}");
                    }
                    return new ServiceOperationResult(false, false, "Installation timed out. The elevated process did not complete in time.");
                }

                var exitCode = process.ExitCode;
                if (exitCode == 0)
                {
                    return new ServiceOperationResult(true, false, string.Empty);
                }
                else
                {
                    var err = $"Installation failed with exit code {exitCode}. Check logs for details.";
                    Logger.Error("MainViewModel", err);
                    return new ServiceOperationResult(false, false, err);
                }
            }
            catch (Win32Exception ex) when (ex.NativeErrorCode == 1223) // ERROR_CANCELLED
            {
                Logger.Info("MainViewModel", "User cancelled UAC elevation for service install");
                return new ServiceOperationResult(false, true, string.Empty);
            }
            catch (Win32Exception ex)
            {
                Logger.Error("MainViewModel", "Win32Exception during elevated service install", ex);
                return new ServiceOperationResult(false, false, $"Elevation failed: {ex.Message}");
            }
        }
        catch (Exception ex)
        {
            Logger.Error("MainViewModel", "Service elevation installation failed", ex);
            return new ServiceOperationResult(false, false, $"Unexpected error: {ex.Message}");
        }
    }

    private static string ResolveServiceExecutablePath()
    {
        var candidates = new[]
        {
            System.IO.Path.Combine(AppContext.BaseDirectory, "Redball.Service.exe"),
            System.IO.Path.Combine(AppContext.BaseDirectory, "Redball.Input.Service.exe")
        };

        foreach (var candidate in candidates)
        {
            if (System.IO.File.Exists(candidate))
            {
                return candidate;
            }
        }

        return candidates[0];
    }

    private static (bool Success, int ExitCode, string Error, bool UserCancelled) RunProcessElevated(string fileName, string arguments)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = arguments,
                UseShellExecute = true,
                Verb = "runas",
                WindowStyle = ProcessWindowStyle.Hidden
            };

            using var process = Process.Start(psi);
            if (process == null)
            {
                return (false, -1, "Failed to start elevated process.", false);
            }

            process.WaitForExit();
            return (true, process.ExitCode, string.Empty, false);
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 1223) // ERROR_CANCELLED - user cancelled UAC
        {
            Logger.Info("MainViewModel", "User cancelled UAC elevation");
            return (false, -1, ex.Message, true);
        }
        catch (Win32Exception ex)
        {
            Logger.Error("MainViewModel", $"Win32Exception during elevated process: {ex.Message}", ex);
            return (false, -1, ex.Message, false);
        }
        catch (Exception ex)
        {
            Logger.Error("MainViewModel", $"Exception during elevated process: {ex.Message}", ex);
            return (false, -1, ex.Message, false);
        }
    }

    private static (int ExitCode, string StdOut, string StdErr) RunProcess(string fileName, string arguments)
    {
        var psi = new System.Diagnostics.ProcessStartInfo
        {
            FileName = fileName,
            Arguments = arguments,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };

        using var process = System.Diagnostics.Process.Start(psi)!;
        var stdout = process.StandardOutput.ReadToEnd();
        var stderr = process.StandardError.ReadToEnd();
        process.WaitForExit();
        return (process.ExitCode, stdout, stderr);
    }
}
