namespace Redball.Service;

using Microsoft.Extensions.Logging;
using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text.Json;

/// <summary>
/// Named pipe server for IPC between the UI application and the service.
/// </summary>
public class IpcServer : IDisposable
{
    private readonly ILogger<IpcServer> _logger;
    private readonly InputInjectionEngine _engine;
    private CancellationTokenSource? _cts;
    private Task? _listenerTask;
    private bool _disposed;

    public const string PipeName = "RedballInputService";

    public IpcServer(ILogger<IpcServer> logger, InputInjectionEngine engine)
    {
        _logger = logger;
        _engine = engine;
    }

    public void Start()
    {
        if (_cts != null) return;

        _cts = new CancellationTokenSource();
        _listenerTask = Task.Run(() => ListenAsync(_cts.Token));
        _logger.LogInformation("IPC server starting");
    }

    public void Stop()
    {
        _cts?.Cancel();
        _listenerTask?.Wait(TimeSpan.FromSeconds(5));
        _logger.LogInformation("IPC server stopped");
    }

    private async Task ListenAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await using var pipe = CreatePipe();
                _logger.LogDebug("Waiting for client connection...");

                await pipe.WaitForConnectionAsync(cancellationToken);
                _logger.LogDebug("Client connected");

                await HandleClientAsync(pipe, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in IPC listener");
                await Task.Delay(1000, cancellationToken);
            }
        }
    }

    private NamedPipeServerStream CreatePipe()
    {
        // Create security descriptor allowing authenticated users to connect
        var pipeSecurity = new PipeSecurity();
        var usersRule = new PipeAccessRule(
            new SecurityIdentifier(WellKnownSidType.BuiltinUsersSid, null),
            PipeAccessRights.ReadWrite,
            AccessControlType.Allow);
        pipeSecurity.AddAccessRule(usersRule);

        var pipe = NamedPipeServerStreamAcl.Create(
            PipeName,
            PipeDirection.InOut,
            NamedPipeServerStream.MaxAllowedServerInstances,
            PipeTransmissionMode.Message,
            PipeOptions.Asynchronous,
            4096,
            4096,
            pipeSecurity);

        return pipe;
    }

    private async Task HandleClientAsync(NamedPipeServerStream pipe, CancellationToken cancellationToken)
    {
        try
        {
            using var reader = new StreamReader(pipe);
            await using var writer = new StreamWriter(pipe) { AutoFlush = true };

            while (pipe.IsConnected && !cancellationToken.IsCancellationRequested)
            {
                var message = await reader.ReadLineAsync(cancellationToken);
                if (message == null) break;

                var response = ProcessMessage(message);
                await writer.WriteLineAsync(response);
            }
        }
        catch (IOException ex) when (ex.Message.Contains("broken", StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogDebug("Client disconnected");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error handling client");
        }
    }

    private string ProcessMessage(string message)
    {
        try
        {
            var request = JsonSerializer.Deserialize<IpcRequest>(message);
            if (request == null)
            {
                return JsonSerializer.Serialize(new IpcResponse { Success = false, Error = "Invalid request" });
            }

            return request.Command switch
            {
                "ping" => JsonSerializer.Serialize(new IpcResponse { Success = true, Data = "pong" }),

                "inject_keyboard" => HandleKeyboardInjection(request),

                "inject_mouse" => HandleMouseInjection(request),

                "get_session" => JsonSerializer.Serialize(new IpcResponse
                {
                    Success = true,
                    Data = System.Diagnostics.Process.GetCurrentProcess().SessionId.ToString()
                }),

                _ => JsonSerializer.Serialize(new IpcResponse { Success = false, Error = "Unknown command" })
            };
        }
        catch (Exception ex)
        {
            return JsonSerializer.Serialize(new IpcResponse { Success = false, Error = ex.Message });
        }
    }

    private string HandleKeyboardInjection(IpcRequest request)
    {
        var data = JsonSerializer.Deserialize<KeyboardInjectionData>(request.Data);
        if (data == null)
        {
            return JsonSerializer.Serialize(new IpcResponse { Success = false, Error = "Invalid keyboard data" });
        }

        var success = _engine.InjectKeyboardInput(
            data.SessionId,
            data.KeyCode,
            data.KeyUp,
            data.Extended);

        return JsonSerializer.Serialize(new IpcResponse { Success = success });
    }

    private string HandleMouseInjection(IpcRequest request)
    {
        var data = JsonSerializer.Deserialize<MouseInjectionData>(request.Data);
        if (data == null)
        {
            return JsonSerializer.Serialize(new IpcResponse { Success = false, Error = "Invalid mouse data" });
        }

        var mouseInput = new InputInjectionEngine.MouseInputData
        {
            X = data.X,
            Y = data.Y,
            MouseData = data.MouseData,
            Flags = data.Flags
        };

        var success = _engine.InjectMouseInput(data.SessionId, mouseInput);
        return JsonSerializer.Serialize(new IpcResponse { Success = success });
    }

    public void Dispose()
    {
        if (_disposed) return;

        Stop();
        _cts?.Dispose();
        _disposed = true;
    }

    #region IPC Data Types

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
