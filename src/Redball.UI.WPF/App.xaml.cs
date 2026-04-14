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
    private System.IServiceProvider? _serviceProvider;
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

            bool isTest = Array.Exists(Environment.GetCommandLineArgs(), arg => arg.Equals("--smoke-test", StringComparison.Ordinal) || arg.Equals("--test-mode", StringComparison.Ordinal));
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
        Services.Logger.Info("App", "=== OnStartup Begin (Modern Threaded Startup) ===");

        // SECURITY: Log app launch event
        Services.SecurityAuditService.Instance.LogEvent("Lifecycle", "Application Startup initiated");

        // Handle service installation elevation (synchronous as they exit immediately)
        if (HandleElevatedServiceArgs(e.Args)) return;

        // Start background initialization task IMMEDIATELY
        var backgroundInitTask = Task.Run(() => InitializeBackgroundServices(e.Args));

        // Smoke test/Test mode flags
        bool isTestMode = e.Args.Length > 0 && Array.Exists(e.Args, arg => arg.Equals("--test-mode", StringComparison.Ordinal));
        _isStartupTestMode = isTestMode;

        // Perform fast-path UI thread initialization
        try
        {
            base.OnStartup(e);
            ShutdownMode = ShutdownMode.OnExplicitShutdown;

            // Singleton check - prevent multiple instances (synchronous, very fast)
            _singleton = new Services.SingletonService();
            if (!isTestMode && !_singleton.TryAcquire())
            {
                Services.Logger.Warning("App", "Another instance is already running. Signaling it to show window.");
                Services.SingleInstanceMessenger.TryShowWindow();
                Shutdown();
                return;
            }

            // Start listening for messages while background init proceeds
            _instanceMessenger = new Services.SingleInstanceMessenger(OnShowWindowRequested);
            _instanceMessenger.StartListening();

            // Continue rest of UI startup when background tasks complete
            _ = CompleteStartupAsync(backgroundInitTask, e.Args);
        }
        catch (Exception ex)
        {
            Services.Logger.Fatal("App", "Fast-path OnStartup failed", ex);
            throw;
        }
    }

    private bool HandleElevatedServiceArgs(string[] args)
    {
        if (args.Length == 0) return false;

        if (args[0] == "--install-service")
        {
            Services.Logger.Info("App", "Running in elevated service installation mode");
            var success = InstallServiceInElevatedMode();
            Environment.Exit(success ? 0 : 1);
            return true;
        }

        if (args[0] == "--uninstall-service")
        {
            Services.Logger.Info("App", "Running in elevated service uninstall mode");
            var success = UninstallServiceInElevatedMode();
            Environment.Exit(success ? 0 : 1);
            return true;
        }

        if (args[0] == "--start-service")
        {
            Services.Logger.Info("App", "Running in elevated service start mode");
            var success = StartServiceInElevatedMode();
            Environment.Exit(success ? 0 : 1);
            return true;
        }

        return false;
    }

    private struct BackgroundInitResult
    {
        public RedballConfig Config;
        public IServiceProvider ServiceProvider;
        public bool Restored;
    }

    private BackgroundInitResult InitializeBackgroundServices(string[] args)
    {
        var sw = Stopwatch.StartNew();
        Services.Logger.Info("App", "Background initialization started...");

        // Begin startup optimisation timing and lazy-service registration
        Services.StartupOptimizer.Instance.BeginStartup();

        // SECURITY: Anti-Tamper check (10/10 Hardening Suggestion)
        if (IsDebuggerPresent() && !args.Contains("--test-mode"))
        {
            Services.SecurityAuditService.Instance.LogEvent("Security", "CRITICAL: External Debugger Detected during initialization");
            Services.Logger.Fatal("App", "DEBUGGER DETECTED! Application terminating for security.");
            Environment.Exit(0xDEAD);
        }

        // 1. Crash recovery check
        Services.CrashRecoveryService.CheckAndRecover();
        Services.CrashRecoveryService.SetCrashFlag();

        // 2. Load configuration (Disc IO)
        var configLoaded = Services.ConfigService.Instance.Load();
        var cfg = Services.ConfigService.Instance.Config;

        // 3. One-time installer hidden option
        TryApplyInstallerHidOption();

        // 4. Build DI container (CPU)
        var serviceProvider = Services.ServiceLocator.BuildServiceProvider(cfg);

        // 5. Initialize Core Services (Parallelizable)
        Parallel.Invoke(
            () => Services.KeepAwakeService.Instance.Initialize(),
            () => Services.MemoryOptimizerService.Instance.Initialize(Services.KeepAwakeService.Instance.IdleDetection)
        );

        // 6. Restore session state (Disc IO)
        var sessionState = serviceProvider.GetRequiredService<Services.ISessionStateService>();
        var restored = sessionState.Restore(Services.KeepAwakeService.Instance);
        if (!restored)
        {
            Services.KeepAwakeService.Instance.SetActive(true);
        }
        Services.KeepAwakeService.Instance.StartMonitoring();

        sw.Stop();
        Services.Logger.Info("App", $"Background initialization complete in {sw.ElapsedMilliseconds}ms");

        return new BackgroundInitResult
        {
            Config = cfg,
            ServiceProvider = serviceProvider,
            Restored = restored
        };
    }

    private async Task CompleteStartupAsync(Task<BackgroundInitResult> backgroundTask, string[] args)
    {
        try
        {
            // Wait for critical background services to be ready
            var result = await backgroundTask;
            this._serviceProvider = result.ServiceProvider;
            var cfg = result.Config;
            var isTestMode = Array.Exists(args, arg => arg.Equals("--test-mode", StringComparison.Ordinal));

            // Initialize theme on UI thread
            Services.Logger.Info("App", "Initializing ThemeManager on UI thread...");
            try
            {
                var savedTheme = cfg.Theme;
                ThemeManager.SetThemeFromConfig(string.IsNullOrEmpty(savedTheme) ? "System" : savedTheme);
                ThemeManager.StartWatchingSystemTheme();
            }
            catch (Exception themeEx)
            {
                Services.Logger.Error("App", "ThemeManager initialization failed", themeEx);
                ThemeManager.SetTheme(Theme.Dark);
            }

            // Analytics session start
            var analytics = result.ServiceProvider.GetRequiredService<Services.IAnalyticsService>();
            analytics.TrackSessionStart();
            analytics.TrackFeature("app.launch.threaded");

            // Onboarding logic
            if (cfg.FirstRun && !isTestMode)
            {
                await ShowOnboardingAsync(cfg, analytics);
            }

            // Create and show MainWindow
            Services.Logger.Info("App", "Creating MainWindow...");
            _mainWindow = new Views.MainWindow();

            if (isTestMode)
            {
                _mainWindow.Title += " (Test Mode)";
                _mainWindow.WindowState = WindowState.Normal;
                _mainWindow.ShowInTaskbar = true;
                _mainWindow.Show();
                Services.Logger.Info("App", "MainWindow shown directly for test-mode");
            }
            else
            {
                // Tray-only mode
                _mainWindow.WindowState = WindowState.Minimized;
                _mainWindow.ShowInTaskbar = false;
                _mainWindow.Show();
                _mainWindow.Hide();
                Services.Logger.Debug("App", "MainWindow initialized in tray-only mode");
            }

            // Smoke test exit
            if (Array.Exists(args, arg => arg == "--smoke-test"))
            {
                Services.Logger.Info("App", "Smoke test successful - exiting");
                Services.Logger.Shutdown();
                Environment.Exit(0);
                return;
            }

            _mainWindow.Loaded += OnMainWindowLoaded;
            _mainWindow.Unloaded += OnMainWindowUnloaded;
            _mainWindow.Closed += OnMainWindowClosed;

            // Optional mini-widget auto-open
            if (cfg.MiniWidgetOpenOnStartup)
            {
                if (_mainWindow.DataContext is ViewModels.MainViewModel vm)
                {
                    vm.ShowMiniWidgetCommand.Execute(null);
                }
            }

            ShutdownMode = ShutdownMode.OnMainWindowClose;
            _startupStopwatch.Stop();
            StartupDuration = _startupStopwatch.Elapsed;
            Services.Logger.Info("App", $"Startup complete (took {StartupDuration.TotalSeconds:F2}s)");

            // Mark startup complete — triggers deferred background service initialisation
            Services.StartupOptimizer.Instance.MarkStartupComplete();

            // Intelligent Monitoring start
            StartIntelligentMonitoring(cfg);
        }
        catch (Exception ex)
        {
            Services.Logger.Fatal("App", "CompleteStartupAsync failed", ex);
            Services.Logger.WriteCrashDump(ex, "CompleteStartupAsync");
            throw;
        }
    }

    private async Task ShowOnboardingAsync(RedballConfig cfg, Services.IAnalyticsService analytics)
    {
        Services.Logger.Info("App", "Showing onboarding window");
        analytics.TrackFunnel("onboarding", "shown");
        var onboarding = new Views.OnboardingWindow();
        var result = onboarding.ShowDialog();

        if (result == true)
        {
            analytics.TrackFunnel("onboarding", "completed");
        }

        cfg.FirstRun = false;
        Services.ConfigService.Instance.Save();
        Services.Logger.Info("App", "FirstRun flag cleared");
    }

    private void StartIntelligentMonitoring(RedballConfig cfg)
    {
        Services.Logger.Info("App", "Starting intelligent awareness services...");

        // Webcam Awareness (10/10 Strategy suggestion)
        var webcamTimer = new System.Windows.Threading.DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(5)
        };

        webcamTimer.Tick += (s, e) =>
        {
            bool inUse = Services.WebcamDetectionService.Instance.CheckWebcamStatus();
            if (inUse && Services.KeepAwakeService.Instance.IsActive)
            {
                Services.Logger.Warning("App", "Webcam in use detected while Keep-Awake is active! Pausing for privacy.");
                Services.KeepAwakeService.Instance.SetActive(false);
                Views.HUDWindow.ShowStatus("Privacy Mode", "PAUSED FOR WEBCAM", "📷");
            }
        };
        webcamTimer.Start();
    }

    [System.Runtime.InteropServices.DllImport("kernel32.dll", ExactSpelling = true)]
    private static extern bool IsDebuggerPresent();

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
    private const uint FLASHW_TRAY = 2;
    private const uint FLASHW_ALL = 3;
    private const uint FLASHW_TIMERNOFG = 12;
    private const int SW_RESTORE = 9;

    [System.Runtime.InteropServices.LibraryImport("user32.dll")]
    [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
    private static partial bool FlashWindowEx(ref FLASHWINFO pwfi);

    [System.Runtime.InteropServices.LibraryImport("user32.dll")]
    [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
    private static partial bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [System.Runtime.InteropServices.LibraryImport("user32.dll")]
    [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
    private static partial bool IsIconic(IntPtr hWnd);

    [System.Runtime.InteropServices.LibraryImport("user32.dll")]
    [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
    private static partial bool SetForegroundWindow(IntPtr hWnd);

    [System.Runtime.InteropServices.LibraryImport("user32.dll", EntryPoint = "FindWindowW", StringMarshalling = System.Runtime.InteropServices.StringMarshalling.Utf16)]
    private static partial IntPtr FindWindow(string? lpClassName, string? lpWindowName);

    [System.Runtime.InteropServices.LibraryImport("user32.dll")]
    [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
    private static partial bool EnumWindows(EnumWindowsCallback lpEnumFunc, IntPtr lParam);

#pragma warning disable SYSLIB1054
    [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
    private static extern int GetWindowText(IntPtr hWnd, System.Text.StringBuilder lpString, int nMaxCount);
#pragma warning restore SYSLIB1054

    [System.Runtime.InteropServices.LibraryImport("user32.dll", EntryPoint = "GetWindowTextLengthW")]
    private static partial int GetWindowTextLength(IntPtr hWnd);

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
            var analytics = this._serviceProvider?.GetService<Services.IAnalyticsService>();
            analytics?.TrackFeature("app.exit");
            analytics?.TrackSessionEnd();
            analytics?.Dispose();

            // Save session state for next launch
            var sessionState = this._serviceProvider?.GetService<Services.ISessionStateService>();
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
