namespace Redball.Core.Input;

using Redball.Core.Security;
using System.IO;
using System.IO.Pipes;
using System.Text.Json;

/// <summary>
/// Client for communicating with the Redball Input Service via named pipes.
/// </summary>
public class InputServiceClient : IDisposable
{
    private readonly string _pipeName;
    private NamedPipeClientStream? _pipe;
    private StreamReader? _reader;
    private StreamWriter? _writer;
    private readonly object _lock = new();

    public const string DefaultPipeName = "RedballInputService";

    public event EventHandler<string>? OnError;

    public InputServiceClient(string pipeName = DefaultPipeName)
    {
        _pipeName = pipeName;
    }

    /// <summary>
    /// Connects to the input service. Returns true if successful.
    /// </summary>
    public async Task<bool> ConnectAsync(int timeoutMs = 5000, CancellationToken cancellationToken = default)
    {
        try
        {
            _pipe = new NamedPipeClientStream(".", _pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
            await _pipe.ConnectAsync(timeoutMs, cancellationToken);

            _reader = new StreamReader(_pipe);
            _writer = new StreamWriter(_pipe) { AutoFlush = true };

            return true;
        }
        catch (Exception ex)
        {
            OnError?.Invoke(this, $"Failed to connect: {ex.Message}");
            Dispose();
            return false;
        }
    }

    /// <summary>
    /// Checks if the service is available (can connect).
    /// </summary>
    public async Task<bool> IsAvailableAsync(int timeoutMs = 1000)
    {
        try
        {
            using var pipe = new NamedPipeClientStream(".", _pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
            await pipe.ConnectAsync(timeoutMs);
            return true;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"InputServiceClient: Failed to check availability: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Pings the service to check connectivity.
    /// </summary>
    public async Task<bool> PingAsync(CancellationToken cancellationToken = default)
    {
        var response = await SendCommandAsync(new IpcRequest { Command = "ping" }, cancellationToken);
        return response.Success && response.Data == "pong";
    }

    /// <summary>
    /// Injects keyboard input into the specified session (0 for current session).
    /// </summary>
    public async Task<bool> InjectKeyboardAsync(
        uint sessionId,
        ushort keyCode,
        bool keyUp = false,
        bool extended = false,
        CancellationToken cancellationToken = default)
    {
        var data = JsonSerializer.Serialize(new KeyboardInjectionData
        {
            SessionId = sessionId,
            KeyCode = keyCode,
            KeyUp = keyUp,
            Extended = extended
        });

        var response = await SendCommandAsync(new IpcRequest
        {
            Command = "inject_keyboard",
            Data = data
        }, cancellationToken);

        return response.Success;
    }

    /// <summary>
    /// Injects mouse input into the specified session (0 for current session).
    /// </summary>
    public async Task<bool> InjectMouseAsync(
        uint sessionId,
        int x,
        int y,
        uint mouseData = 0,
        uint flags = 0,
        CancellationToken cancellationToken = default)
    {
        var data = JsonSerializer.Serialize(new MouseInjectionData
        {
            SessionId = sessionId,
            X = x,
            Y = y,
            MouseData = mouseData,
            Flags = flags
        });

        var response = await SendCommandAsync(new IpcRequest
        {
            Command = "inject_mouse",
            Data = data
        }, cancellationToken);

        return response.Success;
    }

    /// <summary>
    /// Gets the session ID the service is running in.
    /// </summary>
    public async Task<int> GetServiceSessionIdAsync(CancellationToken cancellationToken = default)
    {
        var response = await SendCommandAsync(new IpcRequest { Command = "get_session" }, cancellationToken);
        if (response.Success && int.TryParse(response.Data, out var sessionId))
        {
            return sessionId;
        }
        return -1;
    }

    private async Task<IpcResponse> SendCommandAsync(IpcRequest request, CancellationToken cancellationToken)
    {
        lock (_lock)
        {
            if (_pipe?.IsConnected != true)
            {
                return new IpcResponse { Success = false, Error = "Not connected to service" };
            }
        }

        try
        {
            var json = JsonSerializer.Serialize(request);

            lock (_lock)
            {
                _writer?.WriteLine(json);
            }

            var reader = _reader;
            if (reader == null)
            {
                return new IpcResponse { Success = false, Error = "Not connected" };
            }
            
            var responseJson = await reader.ReadLineAsync(cancellationToken);
            if (responseJson == null)
            {
                return new IpcResponse { Success = false, Error = "No response from service" };
            }

            return JsonSerializer.Deserialize<IpcResponse>(responseJson)
                ?? new IpcResponse { Success = false, Error = "Invalid response" };
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            // SECURITY: Log full details internally, return generic message to user
            Logger.Error("InputServiceClient", "IPC command failed", ex);
            OnError?.Invoke(this, "Command failed");
            return new IpcResponse { Success = false, Error = "Service communication failed" };
        }
    }

    public void Dispose()
    {
        _writer?.Dispose();
        _reader?.Dispose();
        _pipe?.Dispose();
    }

    #region IPC Types

    private class IpcRequest
    {
        public string Command { get; set; } = string.Empty;
        public string Data { get; set; } = string.Empty;
    }

    private class IpcResponse
    {
        public bool Success { get; set; }
        public string? Data { get; set; }
        public string? Error { get; set; }
    }

    private class KeyboardInjectionData
    {
        public uint SessionId { get; set; }
        public ushort KeyCode { get; set; }
        public bool KeyUp { get; set; }
        public bool Extended { get; set; }
    }

    private class MouseInjectionData
    {
        public uint SessionId { get; set; }
        public int X { get; set; }
        public int Y { get; set; }
        public uint MouseData { get; set; }
        public uint Flags { get; set; }
    }

    #endregion
}
