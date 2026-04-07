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
    private CommandPaletteViewModel? _palette;
    
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
        
        InitializePalette();
        
        Logger.Info("MainViewModel", "Commands initialized");
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
        if (result)
        {
            NotificationService.Instance.ShowInfo("Service Installation", "Redball Input Service installed successfully. No restart required.");
        }
        else
        {
            NotificationService.Instance.ShowError("Service Installation", "Failed to install Redball Input Service. Ensure the application is running as Administrator.");
        }
    }

    private async System.Threading.Tasks.Task<bool> InstallServiceAsync()
    {
        return await System.Threading.Tasks.Task.Run(() =>
        {
            try
            {
                var servicePath = ResolveServiceExecutablePath();
                if (!System.IO.File.Exists(servicePath))
                {
                    Logger.Error("MainViewModel", $"Service executable not found: {servicePath}");
                    return false;
                }

                // Check admin rights
                if (!Interop.NativeMethods.IsUserAnAdmin())
                {
                    Logger.Warning("MainViewModel", "Service installation requires admin rights; requesting UAC elevation.");
                    var elevatedCreate = RunProcessElevated("sc.exe", $"create RedballInputService binPath= \"{servicePath}\" start= auto");
                    if (!elevatedCreate.Success || elevatedCreate.ExitCode != 0)
                    {
                        Logger.Error("MainViewModel", $"Elevated create service failed: {elevatedCreate.Error}");
                        return false;
                    }

                    var elevatedStart = RunProcessElevated("sc.exe", "start RedballInputService");
                    if (!elevatedStart.Success || elevatedStart.ExitCode != 0)
                    {
                        Logger.Warning("MainViewModel", $"Service created but failed to start (elevated): {elevatedStart.Error}");
                    }

                    Logger.Info("MainViewModel", "Redball Input Service installed successfully via elevated command");
                    return true;
                }

                // Check if service already exists
                try
                {
                    using var sc = new System.ServiceProcess.ServiceController("RedballInputService");
                    Logger.Info("MainViewModel", "Redball Input Service already installed");
                    return true;
                }
                catch (Exception ex)
                {
                    Logger.Debug("MainViewModel", $"Service check failed (service not installed): {ex.Message}");
                    // Service doesn't exist, proceed with installation
                }

                var createResult = RunProcess("sc.exe", $"create RedballInputService binPath= \"{servicePath}\" start= auto");
                if (createResult.ExitCode != 0)
                {
                    Logger.Error("MainViewModel", $"Failed to create service: {createResult.StdErr}");
                    return false;
                }

                var startResult = RunProcess("sc.exe", "start RedballInputService");
                if (startResult.ExitCode != 0)
                {
                    Logger.Warning("MainViewModel", $"Service created but failed to start: {startResult.StdErr}");
                    // Don't fail - service is installed but needs manual start
                }

                Logger.Info("MainViewModel", "Redball Input Service installed successfully");
                return true;
            }
            catch (Exception ex)
            {
                Logger.Error("MainViewModel", "Service installation failed", ex);
                return false;
            }
        });
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

    private static (bool Success, int ExitCode, string Error) RunProcessElevated(string fileName, string arguments)
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
                return (false, -1, "Failed to start elevated process.");
            }

            process.WaitForExit();
            return (true, process.ExitCode, string.Empty);
        }
        catch (Win32Exception ex)
        {
            return (false, -1, ex.Message);
        }
        catch (Exception ex)
        {
            return (false, -1, ex.Message);
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
