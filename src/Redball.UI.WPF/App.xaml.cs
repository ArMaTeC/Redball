using System;
using System.IO.Pipes;
using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;

namespace Redball.UI;

/// <summary>
/// Redball v3.0 WPF Modern UI Entry Point
/// Hybrid architecture: WPF UI + PowerShell Core via Named Pipes
/// </summary>
public partial class App : Application
{
    private NamedPipeServerStream? _pipeServer;
    private StreamReader? _pipeReader;
    private StreamWriter? _pipeWriter;
    private Views.MainWindow? _mainWindow; // Keep reference to prevent GC

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
            Services.Logger.Debug("App", "base.OnStartup completed");

            // Load configuration
            Services.Logger.Info("App", "Loading configuration...");
            var configLoaded = Services.ConfigService.Instance.Load();
            Services.Logger.Info("App", configLoaded ? "Configuration loaded successfully" : "Using default configuration (file not found)");

            // Log configuration details
            var cfg = Services.ConfigService.Instance.Config;
            Services.Logger.Info("App", $"Config: Heartbeat={cfg.HeartbeatSeconds}s, Duration={cfg.DefaultDuration}min, Theme={cfg.Theme}");
            Services.Logger.Info("App", $"Config: PreventDisplaySleep={cfg.PreventDisplaySleep}, BatteryAware={cfg.BatteryAware}, NetworkAware={cfg.NetworkAware}");

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

            // Initialize modern theme from saved config
            Services.Logger.Info("App", "Initializing ThemeManager...");
            try
            {
                var savedTheme = Services.ConfigService.Instance.Config.Theme;
                if (!string.IsNullOrEmpty(savedTheme) && savedTheme != "System")
                {
                    var themeEnum = ThemeManager.ThemeFromString(savedTheme);
                    Services.Logger.Debug("App", $"Setting theme to: {themeEnum} (from config)");
                    ThemeManager.SetTheme(themeEnum);
                }
                else
                {
                    Services.Logger.Debug("App", "Auto-detecting system theme");
                    ThemeManager.Initialize();
                }
                Services.Logger.Info("App", $"ThemeManager initialized with theme: {ThemeManager.CurrentTheme}");
            }
            catch (Exception themeEx)
            {
                Services.Logger.Error("App", "ThemeManager initialization failed", themeEx);
                // Fallback to dark theme
                ThemeManager.SetTheme(Theme.Dark);
            }

            // Start IPC server for PowerShell communication
            Services.Logger.Info("App", "Starting IPC server...");
            _ = StartIpcServerAsync();

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
            
            // Show the window to ensure proper initialization
            Services.Logger.Debug("App", "Showing MainWindow for initialization...");
            _mainWindow.Show();
            Services.Logger.Info("App", "MainWindow shown, startup sequence complete");
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
            // Move window off-screen for tray-only mode
            if (_mainWindow != null)
            {
                _mainWindow.WindowStyle = WindowStyle.None;
                _mainWindow.ShowInTaskbar = false;
                _mainWindow.Left = -10000;
                _mainWindow.Top = -10000;
                _mainWindow.Width = 1;
                _mainWindow.Height = 1;
                Services.Logger.Debug("App", "MainWindow moved off-screen, tray-only mode active");
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

    private async Task StartIpcServerAsync()
    {
        Services.Logger.Info("IPC", "IPC server loop starting");
        int connectionAttempts = 0;
        
        while (true)
        {
            try
            {
                _pipeServer = new NamedPipeServerStream(
                    "RedballUI",
                    PipeDirection.InOut,
                    1,
                    PipeTransmissionMode.Message,
                    PipeOptions.Asynchronous);

                Services.Logger.Debug("IPC", "Waiting for pipe connection...");
                await _pipeServer.WaitForConnectionAsync();
                connectionAttempts = 0;
                Services.Logger.Info("IPC", "Pipe client connected");

                _pipeReader = new StreamReader(_pipeServer);
                _pipeWriter = new StreamWriter(_pipeServer) { AutoFlush = true };

                // Handle incoming messages
                await HandleIpcMessagesAsync();
            }
            catch (IOException ioEx)
            {
                connectionAttempts++;
                Services.Logger.Warning("IPC", $"Pipe IO error (attempt {connectionAttempts}): {ioEx.Message}");
            }
            catch (Exception ex)
            {
                connectionAttempts++;
                Services.Logger.Error("IPC", $"Pipe error (attempt {connectionAttempts})", ex);
            }
            
            if (connectionAttempts > 0)
            {
                var delayMs = Math.Min(1000 * connectionAttempts, 30000); // Max 30s delay
                Services.Logger.Debug("IPC", $"Waiting {delayMs}ms before retry...");
                await Task.Delay(delayMs);
            }
        }
    }

    private async Task HandleIpcMessagesAsync()
    {
        if (_pipeReader == null) return;

        int messageCount = 0;
        while (_pipeServer?.IsConnected == true)
        {
            string? message = null;
            try
            {
                message = await _pipeReader.ReadLineAsync();
                if (message == null) break;
                messageCount++;
            }
            catch (IOException ioEx)
            {
                Services.Logger.Debug("IPC", $"ReadLine IO error: {ioEx.Message}");
                break;
            }
            catch (Exception readEx)
            {
                Services.Logger.Error("IPC", "Error reading from pipe", readEx);
                break;
            }

            try
            {
                Services.Logger.Verbose("IPC", $"Message #{messageCount}: {message}");
                var request = JsonSerializer.Deserialize<IpcRequest>(message);
                if (request != null)
                {
                    var response = ProcessRequest(request);
                    if (_pipeWriter != null)
                    {
                        var responseJson = JsonSerializer.Serialize(response);
                        await _pipeWriter.WriteLineAsync(responseJson);
                        Services.Logger.Verbose("IPC", $"Response: {responseJson}");
                    }
                }
                else
                {
                    Services.Logger.Warning("IPC", "Received null deserialized request");
                }
            }
            catch (JsonException jsonEx)
            {
                Services.Logger.Error("IPC", $"JSON parse error for message: {message}", jsonEx);
            }
            catch (Exception ex)
            {
                Services.Logger.Error("IPC", "Message handling error", ex);
            }
        }
        
        Services.Logger.Info("IPC", $"Message loop ended after {messageCount} messages");
    }

    private IpcResponse ProcessRequest(IpcRequest request)
    {
        Services.Logger.Debug("IPC", $"Processing request: {request.Action}");
        try
        {
            return request.Action switch
            {
                "GetStatus" => new IpcResponse { Success = true, Data = GetStatus() },
                "SetActive" => new IpcResponse { Success = true, Data = SetActive(request.Data) },
                "ShowSettings" => new IpcResponse { Success = ShowSettingsDialog() },
                _ => new IpcResponse { Success = false, Error = $"Unknown action: {request.Action}" }
            };
        }
        catch (Exception ex)
        {
            Services.Logger.Error("IPC", $"Error processing request {request.Action}", ex);
            return new IpcResponse { Success = false, Error = ex.Message };
        }
    }

    private static object GetStatus()
    {
        var version = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
        var status = new { 
            Active = true, 
            Version = $"{version?.Major}.{version?.Minor}.{version?.Build}",
            Timestamp = DateTime.Now
        };
        Services.Logger.Verbose("IPC", $"GetStatus: {status}");
        return status;
    }

    private static object SetActive(object? data)
    {
        Services.Logger.Info("IPC", $"SetActive: {data}");
        return new { Active = data };
    }

    private bool ShowSettingsDialog()
    {
        Services.Logger.Info("App", "Showing settings dialog via IPC request");
        try
        {
            Dispatcher.Invoke(() =>
            {
                if (_mainWindow != null)
                {
                    _mainWindow.ShowSettings();
                    Services.Logger.Debug("App", "Settings dialog shown via MainWindow");
                }
                else
                {
                    Services.Logger.Warning("App", "MainWindow was null, creating standalone SettingsWindow");
                    var settingsWindow = new Views.SettingsWindow();
                    settingsWindow.Show();
                }
            });
            return true;
        }
        catch (Exception ex)
        {
            Services.Logger.Error("App", "Failed to show settings dialog", ex);
            return false;
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        Services.Logger.Info("App", $"=== OnExit Begin (code: {e.ApplicationExitCode}) ===");
        Services.Logger.LogMemoryStats("App");
        
        try
        {
            _pipeReader?.Dispose();
            _pipeWriter?.Dispose();
            _pipeServer?.Dispose();
            Services.Logger.Debug("App", "Pipe resources disposed");
        }
        catch (Exception ex)
        {
            Services.Logger.Error("App", "Error disposing pipe resources", ex);
        }
        
        base.OnExit(e);
        Services.Logger.Info("App", "=== Application Exit Complete ===");
    }
}

public class IpcRequest
{
    public string Action { get; set; } = "";
    public object? Data { get; set; }
}

public class IpcResponse
{
    public bool Success { get; set; }
    public object? Data { get; set; }
    public string? Error { get; set; }
}
