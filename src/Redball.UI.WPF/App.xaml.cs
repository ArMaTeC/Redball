using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using Microsoft.Extensions.DependencyInjection;

namespace Redball.UI;

/// <summary>
/// Redball v3.0 WPF Modern UI Entry Point
/// Pure C# architecture - all functionality runs natively in WPF.
/// </summary>
public partial class App : Application
{
    private Views.MainWindow? _mainWindow; // Keep reference to prevent GC
    private Services.SingletonService? _singleton;
    private IServiceProvider? _serviceProvider;
    private readonly Stopwatch _startupStopwatch = Stopwatch.StartNew();
    public static TimeSpan StartupDuration { get; private set; }

    public App()
    {
        // Initialize logger FIRST before anything else
        Services.Logger.Initialize();
        Services.Logger.Info("App", "=== Redball.UI.WPF Application Starting ===");
        Services.Logger.Info("App", $"Version: {System.Reflection.Assembly.GetExecutingAssembly().GetName().Version}");

        // Setup early exception handling
        AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        
        Services.Logger.Debug("App", "Exception handlers registered");
    }

    private void OnUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        var ex = e.ExceptionObject as Exception;
        Services.Logger.Fatal("App", "FATAL: Unhandled exception in AppDomain", ex ?? new Exception("Unknown exception"));
        Services.Logger.WriteCrashDump(ex ?? new Exception("Unknown"), "AppDomain.UnhandledException");
        
        // Write emergency crash log
        try
        {
            var crashLog = Path.Combine(AppContext.BaseDirectory, "crash.log");
            File.WriteAllText(crashLog, $"[{DateTime.Now}] FATAL: {e.ExceptionObject}");
        }
        catch { }
    }

    private void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        Services.Logger.Fatal("App", "FATAL: Unobserved task exception", e.Exception);
        Services.Logger.WriteCrashDump(e.Exception, "TaskScheduler.UnobservedTaskException");
        e.SetObserved();
    }

    private void OnDispatcherUnhandledException(object sender, System.Windows.Threading.DispatcherUnhandledExceptionEventArgs e)
    {
        Services.Logger.Fatal("App", "FATAL: Dispatcher exception", e.Exception);
        Services.Logger.WriteCrashDump(e.Exception, "DispatcherUnhandledException");
        
        // Prevent default crash dialog for known recoverable exceptions
        if (e.Exception is System.Windows.Markup.XamlParseException xamlEx)
        {
            Services.Logger.Error("App", $"XAML parse error (recoverable): {xamlEx.Message}");
            // Could show a message box here with the error
        }
        
        e.Handled = true; // Prevent crash, but log it
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        Services.Logger.Info("App", "=== OnStartup Begin ===");
        Services.Logger.LogMemoryStats("App");
        
        try
        {
            base.OnStartup(e);
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            Services.Logger.Debug("App", "base.OnStartup completed");

            // Singleton check - prevent multiple instances
            _singleton = new Services.SingletonService();
            if (!_singleton.TryAcquire())
            {
                Services.Logger.Warning("App", "Another instance is already running. Exiting.");
                Shutdown();
                return;
            }

            // Crash recovery check
            var wasCrash = Services.CrashRecoveryService.CheckAndRecover();
            if (wasCrash)
            {
                Services.Logger.Warning("App", "Recovered from previous crash - using safe defaults");
            }
            Services.CrashRecoveryService.SetCrashFlag();

            // Load configuration
            Services.Logger.Info("App", "Loading configuration...");
            var configLoaded = Services.ConfigService.Instance.Load();
            Services.Logger.Info("App", configLoaded ? "Configuration loaded successfully" : "Using default configuration (file not found)");

            // Log configuration details
            var cfg = Services.ConfigService.Instance.Config;
            Services.Logger.ApplyConfig(cfg);
            Services.Logger.Info("App", $"Config: Heartbeat={cfg.HeartbeatSeconds}s, Duration={cfg.DefaultDuration}min, Theme={cfg.Theme}");
            Services.Logger.Info("App", $"Config: PreventDisplaySleep={cfg.PreventDisplaySleep}, BatteryAware={cfg.BatteryAware}, NetworkAware={cfg.NetworkAware}");

            // Initialize theme resources before showing any themed windows such as onboarding.
            Services.Logger.Info("App", "Initializing ThemeManager...");
            try
            {
                var savedTheme = Services.ConfigService.Instance.Config.Theme;
                Services.Logger.Debug("App", $"Setting theme from config: '{savedTheme}'");
                ThemeManager.SetThemeFromConfig(string.IsNullOrEmpty(savedTheme) ? "System" : savedTheme);

                Services.Logger.Info("App", $"ThemeManager initialized with theme: {ThemeManager.CurrentTheme}");
                ThemeManager.StartWatchingSystemTheme();
            }
            catch (Exception themeEx)
            {
                Services.Logger.Error("App", "ThemeManager initialization failed", themeEx);
                ThemeManager.SetTheme(Theme.Dark);
            }

            // Build DI container now that config is loaded
            _serviceProvider = Services.ServiceLocator.BuildServiceProvider(cfg);
            Services.Logger.Info("App", "DI container built");

            var analytics = _serviceProvider.GetRequiredService<Services.IAnalyticsService>();
            analytics.TrackSessionStart();
            analytics.TrackFeature("app.launch");
            analytics.TrackFunnel("onboarding", "app_started");

            // Validate configuration
            var errors = Services.ConfigService.Instance.Validate();
            if (errors.Count > 0)
            {
                Services.Logger.Warning("App", $"Configuration validation found {errors.Count} errors:");
                foreach (var err in errors)
                {
                    Services.Logger.Warning("App", $"  - {err}");
                }
            }

            // Show onboarding for first-time users
            if (cfg.FirstRun)
            {
                Services.Logger.Info("App", "First run detected - showing onboarding window");
                analytics.TrackFeature("onboarding.shown");
                analytics.TrackFunnel("onboarding", "shown");
                var onboarding = new Views.OnboardingWindow();
                var result = onboarding.ShowDialog();
                Services.Logger.Info("App", result == true ? "Onboarding completed" : "Onboarding cancelled/closed");
                if (result == true)
                {
                    analytics.TrackFeature("onboarding.completed");
                    analytics.TrackFunnel("onboarding", "completed");
                    analytics.TrackRetention(0);
                }
                else
                {
                    analytics.TrackFeature("onboarding.dismissed");
                }
            }

            // Initialize keep-awake engine
            Services.Logger.Info("App", "Initializing KeepAwakeService...");
            Services.KeepAwakeService.Instance.Initialize();

            // Restore previous session state or start fresh
            var sessionState = _serviceProvider.GetRequiredService<Services.ISessionStateService>();
            var restored = sessionState.Restore(Services.KeepAwakeService.Instance);
            if (!restored)
            {
                Services.KeepAwakeService.Instance.SetActive(true);
            }
            Services.KeepAwakeService.Instance.StartMonitoring();
            Services.Logger.Info("App", restored ? "Session state restored" : "KeepAwakeService initialized and active");

            // Create main window but don't show it (tray-only mode)
            Services.Logger.Info("App", "Creating MainWindow...");
            try
            {
                _mainWindow = new Views.MainWindow();
                Services.Logger.Debug("App", "MainWindow instance created");
            }
            catch (Exception mwEx)
            {
                Services.Logger.Fatal("App", "Failed to create MainWindow", mwEx);
                throw;
            }
            
            // Ensure window is loaded before moving off-screen
            _mainWindow.Loaded += OnMainWindowLoaded;
            _mainWindow.Unloaded += OnMainWindowUnloaded;
            
            // Subscribe to application events
            _mainWindow.Closed += OnMainWindowClosed;
            
            // Initialize the main window in hidden tray-only mode
            Services.Logger.Debug("App", "Initializing MainWindow in tray-only mode...");
            _mainWindow.WindowState = WindowState.Minimized;
            _mainWindow.ShowInTaskbar = false;
            _mainWindow.Show();
            _mainWindow.Hide();
            ShutdownMode = ShutdownMode.OnMainWindowClose;
            _startupStopwatch.Stop();
            StartupDuration = _startupStopwatch.Elapsed;
            Services.Logger.Info("App", $"MainWindow initialized in tray-only mode, startup sequence complete (took {StartupDuration.TotalSeconds:F2}s)");
            if (StartupDuration.TotalSeconds > 2.0)
            {
                Services.Logger.Warning("App", $"Startup exceeded 2-second target: {StartupDuration.TotalSeconds:F2}s");
            }
            Services.Logger.LogMemoryStats("App");
        }
        catch (Exception ex)
        {
            Services.Logger.Fatal("App", "OnStartup failed with exception", ex);
            Services.Logger.WriteCrashDump(ex, "OnStartup");
            throw;
        }
    }

    private void OnMainWindowLoaded(object sender, RoutedEventArgs e)
    {
        Services.Logger.Info("App", "MainWindow Loaded event fired");
        try
        {
            if (_mainWindow != null)
            {
                _mainWindow.WindowState = WindowState.Minimized;
                _mainWindow.ShowInTaskbar = false;
                _mainWindow.Hide();
                Services.Logger.Debug("App", "MainWindow kept hidden for tray-only startup");
            }
        }
        catch (Exception ex)
        {
            Services.Logger.Error("App", "Error in MainWindow.Loaded handler", ex);
        }
    }

    private void OnMainWindowUnloaded(object sender, RoutedEventArgs e)
    {
        Services.Logger.Info("App", "MainWindow Unloaded event fired");
    }

    private void OnMainWindowClosed(object? sender, EventArgs e)
    {
        Services.Logger.Info("App", "MainWindow Closed event fired");
    }


    protected override void OnExit(ExitEventArgs e)
    {
        Services.Logger.Info("App", $"=== OnExit Begin (code: {e.ApplicationExitCode}) ===");
        Services.Logger.LogMemoryStats("App");
        
        try
        {
            var analytics = _serviceProvider?.GetService<Services.IAnalyticsService>();
            analytics?.TrackFeature("app.exit");
            analytics?.TrackSessionEnd();
            analytics?.Dispose();

            // Save session state for next launch
            var sessionState = _serviceProvider?.GetService<Services.ISessionStateService>();
            sessionState?.Save(Services.KeepAwakeService.Instance);
            Services.Logger.Debug("App", "Session state saved");

            ThemeManager.StopWatchingSystemTheme();
            Services.KeepAwakeService.Instance.Dispose();
            Services.Logger.Debug("App", "KeepAwakeService disposed");

            // Clear crash flag on clean exit
            Services.CrashRecoveryService.ClearCrashFlag();

            // Release singleton mutex
            _singleton?.Dispose();
        }
        catch (Exception ex)
        {
            Services.Logger.Error("App", "Error during shutdown cleanup", ex);
        }
        
        base.OnExit(e);
        Services.Logger.Info("App", "=== Application Exit Complete ===");
    }
}
