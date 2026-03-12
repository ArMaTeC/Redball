using System;
using System.IO;
using System.IO.Pipes;
using System.Text.Json;
using System.Threading.Tasks;

namespace Redball.UI.Services;

/// <summary>
/// Service for sending IPC messages from WPF UI to PowerShell core.
/// </summary>
public static class IpcClientService
{
    private const string PipeName = "RedballPS";

    /// <summary>
    /// Sends a message to the PowerShell core.
    /// </summary>
    /// <param name="action">The action to perform.</param>
    /// <param name="data">Optional data to send.</param>
    /// <returns>True if message was sent successfully.</returns>
    public static async Task<bool> SendToPowerShellAsync(string action, object? data = null)
    {
        Logger.Verbose("IpcClient", $"Sending action '{action}' to PowerShell...");
        
        try
        {
            using var client = new NamedPipeClientStream(
                ".",
                PipeName,
                PipeDirection.InOut,
                PipeOptions.Asynchronous);

            Logger.Debug("IpcClient", "Connecting to pipe (timeout: 2000ms)...");
            await client.ConnectAsync(2000);
            Logger.Debug("IpcClient", "Connected to pipe successfully");

            using var writer = new StreamWriter(client) { AutoFlush = true };
            using var reader = new StreamReader(client);

            var message = new IpcClientMessage
            {
                Action = action,
                Data = data,
                Timestamp = DateTime.UtcNow
            };

            var json = JsonSerializer.Serialize(message);
            Logger.Verbose("IpcClient", $"Sending JSON: {json}");
            await writer.WriteLineAsync(json);
            Logger.Verbose("IpcClient", "Message sent, waiting for response...");

            var responseJson = await reader.ReadLineAsync();
            if (responseJson != null)
            {
                Logger.Verbose("IpcClient", $"Response received: {responseJson}");
                var response = JsonSerializer.Deserialize<IpcClientResponse>(responseJson);
                var success = response?.Success ?? false;
                Logger.Debug("IpcClient", $"Action '{action}' completed: Success={success}");
                return success;
            }

            Logger.Warning("IpcClient", "No response received from PowerShell");
            return true;
        }
        catch (TimeoutException)
        {
            Logger.Debug("IpcClient", "Connection timed out - PowerShell core may not be running");
            return false;
        }
        catch (IOException ioEx)
        {
            Logger.Error("IpcClient", "IO error communicating with PowerShell", ioEx);
            return false;
        }
        catch (Exception ex)
        {
            Logger.Error("IpcClient", "Failed to send IPC message", ex);
            return false;
        }
    }

    public static bool SendToPowerShell(string action, object? data = null)
    {
        Logger.Verbose("IpcClient", $"Synchronous send: '{action}'");
        return SendToPowerShellAsync(action, data).GetAwaiter().GetResult();
    }
}

public class IpcClientMessage
{
    public string Action { get; set; } = "";
    public object? Data { get; set; }
    public DateTime Timestamp { get; set; }
}

public class IpcClientResponse
{
    public bool Success { get; set; }
    public string? Error { get; set; }
    public object? Data { get; set; }
}