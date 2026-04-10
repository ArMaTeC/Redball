using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Win32;
using Redball.UI.Services;

namespace Redball.UI;

/// <summary>
/// Redball v3.0 WPF Modern UI Entry Point
/// Pure C# architecture - all functionality runs natively in WPF.
/// </summary>
public partial class App : Application
{
    private Views.MainWindow? _mainWindow; // Keep reference to prevent GC
    private Services.SingletonService? _singleton;
    private Services.SingleInstanceMessenger? _instanceMessenger;
    private IServiceProvider? _serviceProvider;
    private bool _isStartupTestMode;
    private readonly Stopwatch _startupStopwatch = Stopwatch.StartNew();
    public static TimeSpan StartupDuration { get; private set; }

    public App()
    {
        // Set current directory to base directory to ensure relative asset paths work correctly
        Directory.SetCurrentDirectory(AppDomain.CurrentDomain.BaseDirectory);

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

    private static void TryApplyInstallerHidOption()
    {
        const string subKeyPath = @"Software\Redball\InstallerDefaults";
        const string valueName = "InstallHidDriverNoRestart";

        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(subKeyPath, writable: true);
            if (key == null)
            {
                return;
            }

            var value = key.GetValue(valueName);
            var enabled = value is int intVal && intVal == 1;
            if (!enabled)
            {
                return;
            }

            Services.Logger.Info("App", "Installer HID option is no longer supported. Skipping.");
            key.DeleteValue(valueName, throwOnMissingValue: false);
        }
        catch (Exception ex)
        {
            Services.Logger.Warning("App", $"Failed to apply installer HID option: {ex.Message}");
        }
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
        catch (Exception writeEx)
        {
            Services.Logger.Debug("App", $"Failed to write emergency crash log: {writeEx.Message}");
        }
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
            Services.Logger.Fatal("App", $"XAML parse error: {xamlEx.Message}", xamlEx);
            
            bool isTest = Array.Exists(Environment.GetCommandLineArgs(), arg => arg == "--smoke-test" || arg == "--test-mode");
            if (isTest)
            {
                Services.Logger.Fatal("App", "Fatal XAML error during test mode - exiting with code 1");
                Environment.Exit(1);
            }
        }
        
        e.Handled = true; // Prevent crash dialog, but log it
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        Services.Logger.Info("App", "=== OnStartup Begin ===");
        
        // Handle service installation elevation
        if (e.Args.Length > 0 && e.Args[0] == "--install-service")
        {
            Services.Logger.Info("App", "Running in elevated service installation mode");
            var success = InstallServiceInElevatedMode();
            Environment.Exit(success ? 0 : 1);
            return;
        }

        // Handle service uninstall elevation
        if (e.Args.Length > 0 && e.Args[0] == "--uninstall-service")
        {
            Services.Logger.Info("App", "Running in elevated service uninstall mode");
            var success = UninstallServiceInElevatedMode();
            Environment.Exit(success ? 0 : 1);
            return;
        }

        // Handle service start elevation
        if (e.Args.Length > 0 && e.Args[0] == "--start-service")
        {
            Services.Logger.Info("App", "Running in elevated service start mode");
            var success = StartServiceInElevatedMode();
            Environment.Exit(success ? 0 : 1);
            return;
        }

        // Handle smoke test for build verification
        if (e.Args.Length > 0 && Array.Exists(e.Args, arg => arg == "--smoke-test"))
        {
            Services.Logger.Info("App", "Running in smoke-test mode for build verification");
            // We'll let the rest of the startup run to verify XAML/DI/Config, 
            // then we'll exit after the MainWindow would have been created.
        }

        // Handle test mode for E2E tests
        bool isTestMode = e.Args.Length > 0 && Array.Exists(e.Args, arg => arg == "--test-mode");
        _isStartupTestMode = isTestMode;
        if (isTestMode)
        {
            Services.Logger.Info("App", "Running in test-mode for E2E tests");
        }

        Services.Logger.LogMemoryStats("App");
        
        try
        {
            base.OnStartup(e);
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            Services.Logger.Debug("App", "base.OnStartup completed");

            // Singleton check - prevent multiple instances
            _singleton = new Services.SingletonService();
            if (!isTestMode && !_singleton.TryAcquire())
            {
                Services.Logger.Warning("App", "Another instance is already running. Signaling it to show window.");
                
                // Try to signal the existing instance to show its window
                var signaled = Services.SingleInstanceMessenger.TryShowWindow();
                
                if (!signaled)
                {
                    // Fallback: try to find and activate the window using Win32
                    TryActivateExistingWindow();
                }
                
                Shutdown();
                return;
            }

            // Start listening for messages from other instances
            _instanceMessenger = new Services.SingleInstanceMessenger(OnShowWindowRequested);
            _instanceMessenger.StartListening();

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

            // Apply one-time installer-selected HID option if present.
            // This is set by MSI optional feature and consumed on first launch.
            TryApplyInstallerHidOption();

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
            if (cfg.FirstRun && !isTestMode)
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

                // ALWAYS disable FirstRun after the onboarding window closes,
                // whether completed or dismissed. This prevents the onboarding
                // loop where the app keeps showing it after updates/restarts.
                cfg.FirstRun = false;
                Services.ConfigService.Instance.Save();
                Services.Logger.Info("App", "FirstRun flag permanently disabled and config saved");
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
                
                if (isTestMode)
                {
                    _mainWindow.Title += " (Test Mode)";
                }
            }
            catch (Exception mwEx)
            {
                Services.Logger.Fatal("App", "Failed to create MainWindow", mwEx);
                throw;
            }

            // Handle smoke test exit
            if (e.Args.Length > 0 && Array.Exists(e.Args, arg => arg == "--smoke-test"))
            {
                Services.Logger.Info("App", "Smoke test successful - exiting with code 0");
                _startupStopwatch.Stop();
                Services.Logger.Info("App", $"Startup sequence verified in {_startupStopwatch.Elapsed.TotalSeconds:F2}s");
                
                // Cleanup and exit
                Services.Logger.Shutdown();
                Environment.Exit(0);
                return;
            }
            
            // Ensure window is loaded before moving off-screen
            _mainWindow.Loaded += OnMainWindowLoaded;
            _mainWindow.Unloaded += OnMainWindowUnloaded;
            
            // Subscribe to application events
            _mainWindow.Closed += OnMainWindowClosed;
            
            // Initialize the main window
            Services.Logger.Debug("App", "Initializing MainWindow...");
            
            if (isTestMode)
            {
                // In test mode, we want the window to be visible and findable by automation
                _mainWindow.WindowState = WindowState.Normal;
                _mainWindow.ShowInTaskbar = true;
                _mainWindow.Show();
                Services.Logger.Info("App", "MainWindow shown directly for test-mode");
            }
            else
            {
                // Tray-only mode for normal startup
                _mainWindow.WindowState = WindowState.Minimized;
                _mainWindow.ShowInTaskbar = false;
                _mainWindow.Show();
                _mainWindow.Hide();
                Services.Logger.Debug("App", "MainWindow initialized in tray-only mode");
            }

            if (cfg.MiniWidgetOpenOnStartup)
            {
                _mainWindow.Dispatcher.BeginInvoke(new Action(() =>
                {
                    if (_mainWindow.DataContext is ViewModels.MainViewModel vm)
                    {
                        vm.ShowMiniWidgetCommand.Execute(null);
                        Services.Logger.Info("App", "Mini widget opened on startup from config");
                    }
                }), System.Windows.Threading.DispatcherPriority.Background);
            }

            ShutdownMode = ShutdownMode.OnMainWindowClose;
            _startupStopwatch.Stop();
            StartupDuration = _startupStopwatch.Elapsed;
            Services.Logger.Info("App", $"MainWindow initialized in tray-only mode, startup sequence complete (took {StartupDuration.TotalSeconds:F2}s)");
            if (StartupDuration.TotalSeconds > 2.0)
            {
                Services.Logger.Warning("App", $"Startup exceeded 2-second target: {StartupDuration.TotalSeconds:F2}s");
            }
            Services.Logger.LogMemoryStats("App");

            // Start background update check if enabled (after a short delay to not block startup)
            if (cfg.AutoUpdateCheckEnabled && !isTestMode)
            {
                _ = Task.Run(async () =>
                {
                    try
                    {
                        // Wait a few seconds for startup to complete
                        await Task.Delay(TimeSpan.FromSeconds(10));
                        await PerformStartupUpdateCheckAsync(cfg);
                    }
                    catch (Exception ex)
                    {
                        Services.Logger.Error("App", "Startup update check failed", ex);
                    }
                });
            }
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
            if (_isStartupTestMode)
            {
                Services.Logger.Debug("App", "MainWindow kept visible for test-mode startup");
                return;
            }

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

    #region Elevated Service Installation Helpers

    /// <summary>
    /// Installs the Redball Input Service when running in elevated mode.
    /// Called when the app is relaunched with --install-service argument.
    /// </summary>
    private static bool InstallServiceInElevatedMode()
    {
        try
        {
            var servicePath = GetServiceExecutablePath();
            if (!System.IO.File.Exists(servicePath))
            {
                Services.Logger.Error("App", $"Service executable not found: {servicePath}");
                return false;
            }

            // Check if service already exists
            var queryResult = RunProcessAsAdmin("sc.exe", "query RedballInputService");
            if (queryResult.ExitCode == 0)
            {
                Services.Logger.Info("App", "Service already exists, stopping and removing for clean installation...");
                
                // Stop the service first
                RunProcessAsAdmin("sc.exe", "stop RedballInputService");
                System.Threading.Thread.Sleep(1000); // Give service time to stop
                
                // Delete the service
                var deleteResult = RunProcessAsAdmin("sc.exe", "delete RedballInputService");
                if (deleteResult.ExitCode != 0)
                {
                    Services.Logger.Warning("App", $"Failed to delete existing service: {deleteResult.StdErr}");
                    // Continue anyway - might be permissions issue but create might still work
                }
                else
                {
                    Services.Logger.Info("App", "Existing service removed successfully");
                    System.Threading.Thread.Sleep(500); // Give SCM time to process
                }
            }

            // Create the service fresh
            var createResult = RunProcessAsAdmin("sc.exe", $"create RedballInputService binPath= \"{servicePath}\" start= auto");
            if (createResult.ExitCode != 0)
            {
                Services.Logger.Error("App", $"Failed to create service: {createResult.StdErr} {createResult.StdOut}");
                return false;
            }
            Services.Logger.Info("App", "Service created successfully");

            // Set service description (allows updating of currently installed services)
            var descResult = RunProcessAsAdmin("sc.exe", "description RedballInputService \"Provides secure input injection for Redball keep-alive functionality. Supports automatic updates and can be safely upgraded while running.\"");
            if (descResult.ExitCode != 0)
            {
                Services.Logger.Warning("App", $"Failed to set service description: {descResult.StdErr}");
            }

            // Start the service
            var startResult = RunProcessAsAdmin("sc.exe", "start RedballInputService");
            if (startResult.ExitCode != 0)
            {
                Services.Logger.Warning("App", $"Service created but failed to start: {startResult.StdErr}");
                // Don't fail if service was created but couldn't start - it might need manual start
            }
            else
            {
                Services.Logger.Info("App", "Service started successfully");
            }

            Services.Logger.Info("App", "Redball Input Service installed successfully");
            return true;
        }
        catch (Exception ex)
        {
            Services.Logger.Error("App", "Service installation failed in elevated mode", ex);
            return false;
        }
    }

    /// <summary>
    /// Uninstalls the Redball Input Service when running in elevated mode.
    /// Called when the app is relaunched with --uninstall-service argument.
    /// </summary>
    private static bool UninstallServiceInElevatedMode()
    {
        try
        {
            // Stop the service first
            RunProcessAsAdmin("sc.exe", "stop RedballInputService");

            // Delete the service
            var deleteResult = RunProcessAsAdmin("sc.exe", "delete RedballInputService");
            if (deleteResult.ExitCode != 0 && !deleteResult.StdErr.Contains("does not exist", StringComparison.OrdinalIgnoreCase))
            {
                Services.Logger.Error("App", $"Failed to delete service: {deleteResult.StdErr}");
                return false;
            }

            Services.Logger.Info("App", "Redball Input Service uninstalled successfully");
            return true;
        }
        catch (Exception ex)
        {
            Services.Logger.Error("App", "Service uninstallation failed in elevated mode", ex);
            return false;
        }
    }

    /// <summary>
    /// Starts the Redball Input Service when running in elevated mode.
    /// Called when the app is relaunched with --start-service argument.
    /// </summary>
    private static bool StartServiceInElevatedMode()
    {
        try
        {
            var startResult = RunProcessAsAdmin("sc.exe", "start RedballInputService");
            if (startResult.ExitCode != 0)
            {
                Services.Logger.Error("App", $"Failed to start service: {startResult.StdErr}");
                return false;
            }

            Services.Logger.Info("App", "Redball Input Service started successfully");
            return true;
        }
        catch (Exception ex)
        {
            Services.Logger.Error("App", "Service start failed in elevated mode", ex);
            return false;
        }
    }

    /// <summary>
    /// Gets the path to the service executable.
    /// </summary>
    private static string GetServiceExecutablePath()
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

    /// <summary>
    /// Runs a process with redirected output (for use when already elevated).
    /// </summary>
    private static (int ExitCode, string StdOut, string StdErr) RunProcessAsAdmin(string fileName, string arguments)
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

    #endregion

    #region Startup Update Check

    /// <summary>
    /// Performs an update check on startup if enabled.
    /// Shows the update dialog if an update is available.
    /// </summary>
    private async Task PerformStartupUpdateCheckAsync(RedballConfig cfg)
    {
        try
        {
            if (!cfg.AutoUpdateCheckEnabled)
            {
                Services.Logger.Debug("App", "Auto update check disabled, skipping");
                return;
            }

            if (string.Equals(cfg.UpdateChannel, "Disabled", StringComparison.OrdinalIgnoreCase))
            {
                Services.Logger.Debug("App", "Update channel is disabled, skipping startup check");
                return;
            }

            Services.Logger.Info("App", "Starting update check...");

            var updateService = new Services.UpdateService(
                cfg.UpdateRepoOwner,
                cfg.UpdateRepoName,
                cfg.UpdateChannel ?? "stable",
                cfg.VerifyUpdateSignature);

            var updateInfo = await updateService.CheckForUpdateAsync();

            if (updateInfo == null)
            {
                Services.Logger.Info("App", "No update available (already on latest version)");
                return;
            }

            Services.Logger.Info("App", $"Update available: {updateInfo.LatestVersion}");

            // Show the update dialog on the UI thread
            await Dispatcher.BeginInvoke(new Action(async () =>
            {
                try
                {
                    // Check if user previously skipped this version
                    var skippedVersion = cfg.SkippedUpdateVersion;
                    if (!string.IsNullOrEmpty(skippedVersion) &&
                        string.Equals(skippedVersion, updateInfo.LatestVersion.ToString(), StringComparison.OrdinalIgnoreCase))
                    {
                        Services.Logger.Info("App", $"User previously skipped version {updateInfo.LatestVersion}, not showing dialog");
                        return;
                    }

                    // Get changelogs for the update
                    var changelogs = await updateService.GetChangelogBetweenVersionsAsync(
                        updateInfo.CurrentVersion, 
                        updateInfo.LatestVersion);

                    // Show update available window
                    var updateWindow = new Views.UpdateAvailableWindow(updateInfo, updateService, changelogs);
                    var result = updateWindow.ShowDialog();

                    if (result == true)
                    {
                        // User chose to update - show progress window
                        var progressWindow = new Views.UpdateProgressWindow();
                        progressWindow.Show();
                        
                        // Start the download
                        _ = Task.Run(async () =>
                        {
                            try
                            {
                                var progress = new Progress<Services.UpdateDownloadProgress>(p =>
                                {
                                    Dispatcher.BeginInvoke(() => progressWindow.UpdateProgress(p));
                                });
                                
                                var success = await updateService.DownloadAndInstallAsync(updateInfo, progress);
                                
                                await Dispatcher.BeginInvoke(() =>
                                {
                                    if (success)
                                    {
                                        progressWindow.Close();
                                    }
                                    else
                                    {
                                        System.Windows.MessageBox.Show(
                                            "Update download failed. Please try again later.",
                                            "Update Error",
                                            System.Windows.MessageBoxButton.OK,
                                            System.Windows.MessageBoxImage.Error);
                                    }
                                });
                            }
                            catch (Exception ex)
                            {
                                Services.Logger.Error("App", "Update download failed", ex);
                                await Dispatcher.BeginInvoke(() =>
                                {
                                    System.Windows.MessageBox.Show(
                                        $"Update error: {ex.Message}",
                                        "Update Error",
                                        System.Windows.MessageBoxButton.OK,
                                        System.Windows.MessageBoxImage.Error);
                                });
                            }
                        });
                    }
                    else if (updateWindow.SkipThisVersion)
                    {
                        // User chose to skip this version
                        cfg.SkippedUpdateVersion = updateInfo.LatestVersion.ToString();
                        Services.ConfigService.Instance.Save();
                        Services.Logger.Info("App", $"User skipped update to version {updateInfo.LatestVersion}");
                    }
                }
                catch (Exception ex)
                {
                    Services.Logger.Error("App", "Error showing update dialog", ex);
                }
            }), System.Windows.Threading.DispatcherPriority.Background);
        }
        catch (Exception ex)
        {
            Services.Logger.Error("App", "Update check failed", ex);
        }
    }

    #endregion

    #region Single Instance Window Activation

    /// <summary>
    /// Called when another instance requests this instance to show its main window.
    /// </summary>
    private void OnShowWindowRequested()
    {
        Dispatcher.BeginInvoke(new Action(() =>
        {
            try
            {
                Services.Logger.Info("App", "OnShowWindowRequested invoked");

                if (_mainWindow == null)
                {
                    Services.Logger.Warning("App", "MainWindow is null - attempting to recreate");
                    try
                    {
                        _mainWindow = new Views.MainWindow();
                        _mainWindow.Loaded += OnMainWindowLoaded;
                        _mainWindow.Unloaded += OnMainWindowUnloaded;
                        _mainWindow.Closed += OnMainWindowClosed;
                        Services.Logger.Info("App", "MainWindow recreated successfully");
                    }
                    catch (Exception ex)
                    {
                        Services.Logger.Error("App", "Failed to recreate MainWindow", ex);
                        return;
                    }
                }

                Services.Logger.Info("App", "Showing main window as requested by another instance");

                // Ensure window is visible in taskbar
                _mainWindow.ShowInTaskbar = true;

                // Show the window if it was hidden
                _mainWindow.Show();

                // Restore from minimized state
                if (_mainWindow.WindowState == WindowState.Minimized)
                {
                    _mainWindow.WindowState = WindowState.Normal;
                }

                // Bring to front and activate
                _mainWindow.Activate();
                _mainWindow.Focus();

                // Flash the window to get user's attention
                FlashWindow(_mainWindow);

                Services.Logger.Debug("App", "Main window shown and activated");
            }
            catch (Exception ex)
            {
                Services.Logger.Error("App", "Error showing main window", ex);
            }
        }), System.Windows.Threading.DispatcherPriority.Normal);
    }

    /// <summary>
    /// Fallback method to activate an existing window using Win32 when named pipe fails.
    /// </summary>
    private static void TryActivateExistingWindow()
    {
        try
        {
            // Find the main window by class name and title
            var hwnd = FindWindow(null, "Redball");
            if (hwnd == IntPtr.Zero)
            {
                // Try finding by partial title match
                hwnd = FindWindowByPartialTitle("Redball");
            }

            if (hwnd != IntPtr.Zero)
            {
                // Restore if minimized
                if (IsIconic(hwnd))
                {
                    ShowWindow(hwnd, SW_RESTORE);
                }

                // Bring to front
                SetForegroundWindow(hwnd);
                FlashWindowWin32(hwnd);
                Services.Logger.Info("App", "Activated existing window via Win32");
            }
            else
            {
                Services.Logger.Warning("App", "Could not find existing window to activate");
            }
        }
        catch (Exception ex)
        {
            Services.Logger.Error("App", "Error activating existing window", ex);
        }
    }

    private static void FlashWindow(Window window)
    {
        if (window == null) return;
        var helper = new System.Windows.Interop.WindowInteropHelper(window);
        FlashWindowWin32(helper.Handle);
    }

    private static void FlashWindowWin32(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero) return;
        var fi = new FLASHWINFO
        {
            cbSize = (uint)System.Runtime.InteropServices.Marshal.SizeOf(typeof(FLASHWINFO)),
            hwnd = hwnd,
            dwFlags = FLASHW_ALL | FLASHW_TIMERNOFG,
            uCount = 3,
            dwTimeout = 0
        };
        FlashWindowEx(ref fi);
    }

    private static IntPtr FindWindowByPartialTitle(string title)
    {
        IntPtr found = IntPtr.Zero;
        EnumWindows((hwnd, param) =>
        {
            var len = GetWindowTextLength(hwnd);
            if (len > 0)
            {
                var sb = new System.Text.StringBuilder(len + 1);
                GetWindowText(hwnd, sb, len + 1);
                if (sb.ToString().Contains(title))
                {
                    found = hwnd;
                    return false; // Stop enumerating
                }
            }
            return true; // Continue
        }, IntPtr.Zero);
        return found;
    }

    // Win32 imports
    private const uint FLASHW_STOP = 0;
    private const uint FLASHW_CAPTION = 1;
    private const uint FLASHW_TRAY = 2;
    private const uint FLASHW_ALL = 3;
    private const uint FLASHW_TIMER = 4;
    private const uint FLASHW_TIMERNOFG = 12;
    private const int SW_RESTORE = 9;

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool FlashWindowEx(ref FLASHWINFO pwfi);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool IsIconic(IntPtr hWnd);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Auto)]
    private static extern IntPtr FindWindow(string? lpClassName, string? lpWindowName);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsCallback lpEnumFunc, IntPtr lParam);

    [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Auto)]
    private static extern int GetWindowText(IntPtr hWnd, System.Text.StringBuilder lpString, int nMaxCount);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern int GetWindowTextLength(IntPtr hWnd);

    private delegate bool EnumWindowsCallback(IntPtr hWnd, IntPtr lParam);

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    private struct FLASHWINFO
    {
        public uint cbSize;
        public IntPtr hwnd;
        public uint dwFlags;
        public uint uCount;
        public uint dwTimeout;
    }

    #endregion

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

            // Release singleton mutex and stop listening for messages
            _instanceMessenger?.Dispose();
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
