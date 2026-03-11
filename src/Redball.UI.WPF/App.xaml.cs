using System;
using System.Windows;
using System.IO.Pipes;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;

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

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Initialize modern theme
        ThemeManager.Initialize();

        // Start IPC server for PowerShell communication
        _ = StartIpcServerAsync();

        // Create and show main window (tray-only mode)
        var mainWindow = new MainWindow();
        mainWindow.Show();
    }

    private async Task StartIpcServerAsync()
    {
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

                await _pipeServer.WaitForConnectionAsync();

                _pipeReader = new StreamReader(_pipeServer);
                _pipeWriter = new StreamWriter(_pipeServer) { AutoFlush = true };

                // Handle incoming messages from PowerShell core
                await HandleIpcMessagesAsync();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"IPC Error: {ex.Message}");
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
                var request = JsonSerializer.Deserialize<IpcRequest>(message);
                if (request != null)
                {
                    var response = ProcessRequest(request);
                    if (_pipeWriter != null)
                    {
                        await _pipeWriter.WriteLineAsync(
                            JsonSerializer.Serialize(response));
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Message handling error: {ex.Message}");
            }
        }
    }

    private IpcResponse ProcessRequest(IpcRequest request)
    {
        return request.Action switch
        {
            "GetStatus" => new IpcResponse { Success = true, Data = GetStatus() },
            "SetActive" => new IpcResponse { Success = true, Data = SetActive(request.Data) },
            "ShowSettings" => new IpcResponse { Success = ShowSettingsDialog() },
            _ => new IpcResponse { Success = false, Error = "Unknown action" }
        };
    }

    private static object GetStatus() => new { Active = true, Version = "3.0.0" };
    private static object SetActive(object? data) => new { Active = data };

    private bool ShowSettingsDialog()
    {
        Dispatcher.Invoke(() =>
        {
            var settings = new Views.SettingsWindow();
            settings.ShowDialog();
        });
        return true;
    }

    protected override void OnExit(ExitEventArgs e)
    {
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
