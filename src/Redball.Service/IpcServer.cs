namespace Redball.Service;

using Microsoft.Extensions.Logging;
using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text.Json;
using System.Text.Json.Serialization;

/// <summary>
/// Named pipe server for IPC between the UI application and the service.
/// </summary>
public class IpcServer : IDisposable
{
    private const int DefaultPipeBufferSize = 4096; // 4KB buffer for IPC messages
    private const int MaxMessageSize = 1024 * 1024; // 1MB max message size for DoS protection
    private const int MaxMessagesPerMinute = 60; // Rate limit: 60 messages per minute per client

    private readonly ILogger<IpcServer> _logger;
    private readonly InputInjectionEngine _engine;
    private CancellationTokenSource? _cts;
    private Task? _listenerTask;
    private bool _disposed;

    // SECURITY: Rate limiting tracking per client process
    private readonly Dictionary<int, ClientRateLimit> _clientRateLimits = new();
    private readonly object _rateLimitLock = new();

    private class ClientRateLimit
    {
        public DateTime WindowStart { get; set; } = DateTime.UtcNow;
        public int MessageCount { get; set; } = 0;
    }

    public const string PipeName = "RedballInputService";

    // STRICT: Serializer options with strict type constraints
    private static readonly JsonSerializerOptions _strictJsonOptions = new()
    {
        PropertyNameCaseInsensitive = false,
        PropertyNamingPolicy = null, // Use exact property names
        NumberHandling = JsonNumberHandling.Strict,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
    };

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
        // Create security descriptor with specific group permissions
        var pipeSecurity = new PipeSecurity();

        // Allow Administrators full access
        var adminRule = new PipeAccessRule(
            new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null),
            PipeAccessRights.FullControl,
            AccessControlType.Allow);
        pipeSecurity.AddAccessRule(adminRule);

        // Allow specific RedballUsers group (create if doesn't exist, or use fallback)
        try
        {
            var redballUsersGroup = new NTAccount("RedballUsers");
            var redballSid = (SecurityIdentifier)redballUsersGroup.Translate(typeof(SecurityIdentifier));
            var redballRule = new PipeAccessRule(
                redballSid,
                PipeAccessRights.ReadWrite,
                AccessControlType.Allow);
            pipeSecurity.AddAccessRule(redballRule);
        }
        catch (IdentityNotMappedException)
        {
            // RedballUsers group doesn't exist - fall back to Interactive Users only
            _logger.LogWarning("RedballUsers group not found. Falling back to Interactive Users for IPC access.");
            var interactiveRule = new PipeAccessRule(
                new SecurityIdentifier(WellKnownSidType.InteractiveSid, null),
                PipeAccessRights.ReadWrite,
                AccessControlType.Allow);
            pipeSecurity.AddAccessRule(interactiveRule);
        }

        // Deny access to anonymous users for security
        var denyAnonymousRule = new PipeAccessRule(
            new SecurityIdentifier(WellKnownSidType.AnonymousSid, null),
            PipeAccessRights.ReadWrite,
            AccessControlType.Deny);
        pipeSecurity.AddAccessRule(denyAnonymousRule);

        var pipe = NamedPipeServerStreamAcl.Create(
            PipeName,
            PipeDirection.InOut,
            NamedPipeServerStream.MaxAllowedServerInstances,
            PipeTransmissionMode.Message,
            PipeOptions.Asynchronous,
            DefaultPipeBufferSize,
            DefaultPipeBufferSize,
            pipeSecurity);

        return pipe;
    }

    private async Task HandleClientAsync(NamedPipeServerStream pipe, CancellationToken cancellationToken)
    {
        // SECURITY: Get client process ID for rate limiting
        int clientProcessId = -1;
        try
        {
            clientProcessId = pipe.GetClientProcessId();
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Could not identify client process: {Message}", ex.Message);
        }

        try
        {
            using var reader = new StreamReader(pipe);
            await using var writer = new StreamWriter(pipe) { AutoFlush = true };

            while (pipe.IsConnected && !cancellationToken.IsCancellationRequested)
            {
                var message = await reader.ReadLineAsync(cancellationToken);
                if (message == null) break;

                // SECURITY: Validate message size to prevent memory exhaustion
                if (message.Length > MaxMessageSize)
                {
                    _logger.LogWarning("Rejected oversized message from client {ClientId}: {Size} bytes", clientProcessId, message.Length);
                    await writer.WriteLineAsync(JsonSerializer.Serialize(new IpcResponse
                    {
                        Success = false,
                        Error = "Message exceeds maximum size"
                    }, _strictJsonOptions));
                    continue;
                }

                // SECURITY: Rate limiting check
                if (clientProcessId > 0 && !CheckRateLimit(clientProcessId))
                {
                    _logger.LogWarning("Rate limit exceeded for client {ClientId}", clientProcessId);
                    await writer.WriteLineAsync(JsonSerializer.Serialize(new IpcResponse
                    {
                        Success = false,
                        Error = "Rate limit exceeded"
                    }, _strictJsonOptions));
                    break;
                }

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
        finally
        {
            // Clean up rate limit entry for this client
            if (clientProcessId > 0)
            {
                lock (_rateLimitLock)
                {
                    _clientRateLimits.Remove(clientProcessId);
                }
            }
        }
    }

    // SECURITY: Rate limiting implementation
    private bool CheckRateLimit(int clientProcessId)
    {
        lock (_rateLimitLock)
        {
            var now = DateTime.UtcNow;

            if (!_clientRateLimits.TryGetValue(clientProcessId, out var limit))
            {
                limit = new ClientRateLimit { WindowStart = now, MessageCount = 1 };
                _clientRateLimits[clientProcessId] = limit;
                return true;
            }

            // Reset window if minute has passed
            if ((now - limit.WindowStart).TotalMinutes >= 1)
            {
                limit.WindowStart = now;
                limit.MessageCount = 1;
                return true;
            }

            // Check limit
            if (limit.MessageCount >= MaxMessagesPerMinute)
            {
                return false;
            }

            limit.MessageCount++;
            return true;
        }
    }

    private string ProcessMessage(string message)
    {
        try
        {
            // STRICT: Use strict deserialization with type validation
            var request = JsonSerializer.Deserialize<IpcRequest>(message, _strictJsonOptions);
            if (request == null || string.IsNullOrWhiteSpace(request.Command))
            {
                return JsonSerializer.Serialize(new IpcResponse { Success = false, Error = "Invalid request: missing command" }, _strictJsonOptions);
            }

            return request.Command switch
            {
                "ping" => JsonSerializer.Serialize(new IpcResponse { Success = true, Data = "pong" }, _strictJsonOptions),

                "inject_keyboard" => HandleKeyboardInjection(request),

                "inject_mouse" => HandleMouseInjection(request),

                "get_session" => JsonSerializer.Serialize(new IpcResponse
                {
                    Success = true,
                    Data = System.Diagnostics.Process.GetCurrentProcess().SessionId.ToString()
                }, _strictJsonOptions),

                _ => JsonSerializer.Serialize(new IpcResponse { Success = false, Error = "Unknown command" }, _strictJsonOptions)
            };
        }
        catch (JsonException ex)
        {
            _logger.LogWarning("Invalid IPC message format: {Message}", ex.Message);
            return JsonSerializer.Serialize(new IpcResponse { Success = false, Error = "Invalid message format" }, _strictJsonOptions);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing IPC message");
            return JsonSerializer.Serialize(new IpcResponse { Success = false, Error = "Internal error" }, _strictJsonOptions);
        }
    }

    private string HandleKeyboardInjection(IpcRequest request)
    {
        try
        {
            // STRICT: Use strict deserialization with validation schema
            var data = JsonSerializer.Deserialize<KeyboardInjectionData>(request.Data, _strictJsonOptions);
            if (data == null)
            {
                return JsonSerializer.Serialize(new IpcResponse { Success = false, Error = "Invalid keyboard data" }, _strictJsonOptions);
            }

            var success = _engine.InjectKeyboardInput(
                data.SessionId,
                data.KeyCode,
                data.KeyUp,
                data.Extended);

            return JsonSerializer.Serialize(new IpcResponse { Success = success }, _strictJsonOptions);
        }
        catch (JsonException ex)
        {
            _logger.LogWarning("Invalid keyboard injection data: {Message}", ex.Message);
            return JsonSerializer.Serialize(new IpcResponse { Success = false, Error = "Invalid keyboard data format" }, _strictJsonOptions);
        }
    }

    private string HandleMouseInjection(IpcRequest request)
    {
        try
        {
            // STRICT: Use strict deserialization with validation schema
            var data = JsonSerializer.Deserialize<MouseInjectionData>(request.Data, _strictJsonOptions);
            if (data == null)
            {
                return JsonSerializer.Serialize(new IpcResponse { Success = false, Error = "Invalid mouse data" }, _strictJsonOptions);
            }

            var mouseInput = new InputInjectionEngine.MouseInputData
            {
                X = data.X,
                Y = data.Y,
                MouseData = data.MouseData,
                Flags = data.Flags
            };

            var success = _engine.InjectMouseInput(data.SessionId, mouseInput);
            return JsonSerializer.Serialize(new IpcResponse { Success = success }, _strictJsonOptions);
        }
        catch (JsonException ex)
        {
            _logger.LogWarning("Invalid mouse injection data: {Message}", ex.Message);
            return JsonSerializer.Serialize(new IpcResponse { Success = false, Error = "Invalid mouse data format" }, _strictJsonOptions);
        }
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
