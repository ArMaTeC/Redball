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
        try
        {
            using var client = new NamedPipeClientStream(
                ".",
                PipeName,
                PipeDirection.InOut,
                PipeOptions.Asynchronous);

            // Connect with timeout
            await client.ConnectAsync(2000);

            using var writer = new StreamWriter(client) { AutoFlush = true };
            using var reader = new StreamReader(client);

            // Create message
            var message = new IpcClientMessage
            {
                Action = action,
                Data = data,
                Timestamp = DateTime.UtcNow
            };

            var json = JsonSerializer.Serialize(message);
            await writer.WriteLineAsync(json);

            // Read response
            var responseJson = await reader.ReadLineAsync();
            if (responseJson != null)
            {
                var response = JsonSerializer.Deserialize<IpcClientResponse>(responseJson);
                return response?.Success ?? false;
            }

            return true;
        }
        catch (TimeoutException)
        {
            // PowerShell core not running or not listening
            return false;
        }
        catch (Exception ex)
        {
            Log($"IPC send failed: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Synchronous wrapper for sending messages.
    /// </summary>
    public static bool SendToPowerShell(string action, object? data = null)
    {
        return SendToPowerShellAsync(action, data).GetAwaiter().GetResult();
    }

    private static void Log(string message)
    {
        var logPath = System.IO.Path.Combine(AppContext.BaseDirectory, "Redball.UI.log");
        var line = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [IpcClient] {message}{Environment.NewLine}";
        System.IO.File.AppendAllText(logPath, line);
    }
}

/// <summary>
/// IPC message from WPF to PowerShell.
/// </summary>
public class IpcClientMessage
{
    public string Action { get; set; } = "";
    public object? Data { get; set; }
    public DateTime Timestamp { get; set; }
}

/// <summary>
/// IPC response from PowerShell.
/// </summary>
public class IpcClientResponse
{
    public bool Success { get; set; }
    public string? Error { get; set; }
    public object? Data { get; set; }
}
