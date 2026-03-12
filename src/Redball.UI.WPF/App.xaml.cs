using System;
using System.IO.Pipes;
using System.IO;
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
    private string _logPath = "";
    private Views.MainWindow? _mainWindow; // Keep reference to prevent GC

    public App()
    {
        // Setup early exception handling before logging is ready
        AppDomain.CurrentDomain.UnhandledException += (s, e) =>
        {
            var crashLog = Path.Combine(AppContext.BaseDirectory, "crash.log");
            File.WriteAllText(crashLog, $"[{DateTime.Now}] FATAL: {e.ExceptionObject}");
        };
        TaskScheduler.UnobservedTaskException += (s, e) =>
        {
            var crashLog = Path.Combine(AppContext.BaseDirectory, "crash.log");
            File.AppendAllText(crashLog, $"[{DateTime.Now}] TASK ERROR: {e.Exception}\n");
        };

        // Setup crash logging before anything else
        var appRoot = AppContext.BaseDirectory;
        _logPath = Path.Combine(appRoot, "Redball.UI.log");
        
        AppDomain.CurrentDomain.UnhandledException += (sender, e) => 
        {
            Log($"FATAL: Unhandled exception - {e.ExceptionObject}");
        };
        
        DispatcherUnhandledException += (sender, e) => 
        {
            Log($"FATAL: Dispatcher exception - {e.Exception}");
            e.Handled = true;
        };
        
        TaskScheduler.UnobservedTaskException += (sender, e) => 
        {
            Log($"FATAL: Task exception - {e.Exception}");
            e.SetObserved();
        };
    }

    private void Log(string message)
    {
        try
        {
            var timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            File.AppendAllText(_logPath, $"[{timestamp}] {message}{Environment.NewLine}");
        }
        catch { /* Silent fail */ }
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        Log("=== Redball.UI.WPF Starting ===");
        
        try
        {
            base.OnStartup(e);
            Log("OnStartup called");

            // Load configuration
            Log("Loading configuration...");
            var configLoaded = Services.ConfigService.Instance.Load();
            Log(configLoaded ? "Configuration loaded successfully" : "Using default configuration");

            // Initialize modern theme
            Log("Initializing ThemeManager...");
            ThemeManager.Initialize();
            Log("ThemeManager initialized");

            // Start IPC server for PowerShell communication
            Log("Starting IPC server...");
            _ = StartIpcServerAsync();
            Log("IPC server started");

            // Create main window but don't show it (tray-only mode)
            Log("Creating MainWindow...");
            _mainWindow = new Views.MainWindow();
            
            // Ensure window is loaded before moving off-screen (important for tray icon and hotkeys)
            _mainWindow.Loaded += (s, e) =>
            {
                Log("MainWindow loaded, moving off-screen for tray-only mode...");
                // Move window off-screen instead of hiding to keep message pump running for hotkeys
                // Must use WindowStyle.None when AllowsTransparency is true
                _mainWindow.WindowStyle = WindowStyle.None;
                _mainWindow.ShowInTaskbar = false;
                _mainWindow.Left = -10000;
                _mainWindow.Top = -10000;
                _mainWindow.Width = 1;
                _mainWindow.Height = 1;
                Log("MainWindow moved off-screen, tray-only mode active");
            };
            
            // Show the window to ensure proper initialization and hotkey registration
            _mainWindow.Show();
            Log("MainWindow shown for initialization");
        }
        catch (Exception ex)
        {
            Log($"FATAL: OnStartup failed - {ex.GetType().Name}: {ex.Message}");
            Log($"Stack trace: {ex.StackTrace}");
            throw;
        }
    }

    private async Task StartIpcServerAsync()
    {
        Log("IPC server loop starting");
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

                Log("Waiting for pipe connection...");
                await _pipeServer.WaitForConnectionAsync();
                Log("Pipe connected");

                _pipeReader = new StreamReader(_pipeServer);
                _pipeWriter = new StreamWriter(_pipeServer) { AutoFlush = true };

                // Handle incoming messages from PowerShell core
                await HandleIpcMessagesAsync();
            }
            catch (Exception ex)
            {
                Log($"IPC Error: {ex.GetType().Name}: {ex.Message}");
                await Task.Delay(1000);
            }
        }
    }

    private async Task HandleIpcMessagesAsync()
    {
        if (_pipeReader == null) return;

        while (_pipeServer?.IsConnected == true)
        {
            var message = await _pipeReader.ReadLineAsync();
            if (message == null) break;

            try
            {
                Log($"IPC Message received: {message}");
                var request = JsonSerializer.Deserialize<IpcRequest>(message);
                if (request != null)
                {
                    var response = ProcessRequest(request);
                    if (_pipeWriter != null)
                    {
                        var responseJson = JsonSerializer.Serialize(response);
                        await _pipeWriter.WriteLineAsync(responseJson);
                        Log($"IPC Response sent: {responseJson}");
                    }
                }
            }
            catch (Exception ex)
            {
                Log($"Message handling error: {ex.GetType().Name}: {ex.Message}");
            }
        }
    }

    private IpcResponse ProcessRequest(IpcRequest request)
    {
        Log($"Processing request: {request.Action}");
        return request.Action switch
        {
            "GetStatus" => new IpcResponse { Success = true, Data = GetStatus() },
            "SetActive" => new IpcResponse { Success = true, Data = SetActive(request.Data) },
            "ShowSettings" => new IpcResponse { Success = ShowSettingsDialog() },
            _ => new IpcResponse { Success = false, Error = "Unknown action" }
        };
    }

    private static object GetStatus()
    {
        var version = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
        return new { Active = true, Version = $"{version?.Major}.{version?.Minor}.{version?.Build}" };
    }
    private static object SetActive(object? data) => new { Active = data };

    private bool ShowSettingsDialog()
    {
        Log("Showing settings dialog");
        Dispatcher.Invoke(() =>
        {
            if (_mainWindow != null)
            {
                _mainWindow.ShowSettings();
            }
            else
            {
                // Fallback if MainWindow not available
                var settingsWindow = new Views.SettingsWindow();
                settingsWindow.Show();
            }
        });
        return true;
    }

    protected override void OnExit(ExitEventArgs e)
    {
        Log($"=== Redball.UI.WPF Exiting (code: {e.ApplicationExitCode}) ===");
        _pipeReader?.Dispose();
        _pipeWriter?.Dispose();
        _pipeServer?.Dispose();
        base.OnExit(e);
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
