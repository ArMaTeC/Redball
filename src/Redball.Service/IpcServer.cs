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
public partial class IpcServer : IDisposable
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

    private sealed class ClientRateLimit
    {
        public DateTime WindowStart { get; set; } = DateTime.UtcNow;
        public int MessageCount { get; set; }
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
        Log.Starting(_logger);
    }

    public void Stop()
    {
        _cts?.Cancel();
        _listenerTask?.Wait(TimeSpan.FromSeconds(5));
        Log.Stopped(_logger);
    }

    private async Task ListenAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await using var pipe = CreatePipe();
                Log.WaitingForConnection(_logger);

                await pipe.WaitForConnectionAsync(cancellationToken);
                Log.ClientConnected(_logger);

                await HandleClientAsync(pipe, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                Log.ErrorInListener(_logger, ex);
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
            // RedballUsers group doesn't exist on first run - this is expected, fall back to Interactive Users
            Log.GroupNotFound(_logger);
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
        // SECURITY: Get client process ID for rate limiting (Windows only)
        int clientProcessId = -1;
        try
        {
            // GetClientProcessId is Windows-only - use reflection to avoid build errors on Linux
            var method = typeof(NamedPipeServerStream).GetMethod("GetClientProcessId");
            if (method != null)
            {
                clientProcessId = (int)method.Invoke(pipe, null)!;
            }
            else
            {
                // Fallback: generate a hash from the pipe handle for non-Windows platforms
                clientProcessId = pipe.GetHashCode();
            }
        }
        catch (Exception ex)
        {
            Log.CouldNotIdentifyClient(_logger, ex.Message);
            clientProcessId = pipe.GetHashCode(); // Fallback identifier
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
                    Log.OversizedMessage(_logger, clientProcessId, message.Length);
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
                    Log.RateLimitExceeded(_logger, clientProcessId);
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
            Log.ClientDisconnected(_logger);
        }
        catch (Exception ex)
        {
            Log.ErrorHandlingClient(_logger, ex);
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
                    Data = System.Diagnostics.Process.GetCurrentProcess().SessionId.ToString(System.Globalization.CultureInfo.InvariantCulture)
                }, _strictJsonOptions),

                _ => JsonSerializer.Serialize(new IpcResponse { Success = false, Error = "Unknown command" }, _strictJsonOptions)
            };
        }
        catch (JsonException ex)
        {
            Log.InvalidIpcFormat(_logger, ex.Message);
            return JsonSerializer.Serialize(new IpcResponse { Success = false, Error = "Invalid message format" }, _strictJsonOptions);
        }
        catch (Exception ex)
        {
            Log.ErrorProcessingIpc(_logger, ex);
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
            Log.InvalidKeyboardInjectionData(_logger, ex.Message);
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
            Log.InvalidMouseInjectionData(_logger, ex.Message);
            return JsonSerializer.Serialize(new IpcResponse { Success = false, Error = "Invalid mouse data format" }, _strictJsonOptions);
        }
    }

    public void Dispose()
    {
        if (_disposed) return;

        Stop();
        _cts?.Dispose();
        _disposed = true;
        GC.SuppressFinalize(this);
    }

    #region IPC Data Types
 
    private sealed class IpcRequest
    {
        public string Command { get; set; } = string.Empty;
        public string Data { get; set; } = string.Empty;
    }
 
    private sealed class IpcResponse
    {
        public bool Success { get; set; }
        public string? Data { get; set; }
        public string? Error { get; set; }
    }
 
    private sealed class KeyboardInjectionData
    {
        public uint SessionId { get; set; }
        public ushort KeyCode { get; set; }
        public bool KeyUp { get; set; }
        public bool Extended { get; set; }
    }
 
    private sealed class MouseInjectionData
    {
        public uint SessionId { get; set; }
        public int X { get; set; }
        public int Y { get; set; }
        public uint MouseData { get; set; }
        public uint Flags { get; set; }
    }
 
    #endregion
 
    private static partial class Log
    {
        [LoggerMessage(Level = LogLevel.Information, Message = "IPC server starting")]
        public static partial void Starting(ILogger logger);
 
        [LoggerMessage(Level = LogLevel.Information, Message = "IPC server stopped")]
        public static partial void Stopped(ILogger logger);
 
        [LoggerMessage(Level = LogLevel.Debug, Message = "Waiting for client connection...")]
        public static partial void WaitingForConnection(ILogger logger);
 
        [LoggerMessage(Level = LogLevel.Debug, Message = "Client connected")]
        public static partial void ClientConnected(ILogger logger);
 
        [LoggerMessage(Level = LogLevel.Error, Message = "Error in IPC listener")]
        public static partial void ErrorInListener(ILogger logger, Exception ex);
 
        [LoggerMessage(Level = LogLevel.Information, Message = "RedballUsers group not found (expected on first run). Using Interactive Users for IPC access.")]
        public static partial void GroupNotFound(ILogger logger);
 
        [LoggerMessage(Level = LogLevel.Warning, Message = "Could not identify client process: {Message}")]
        public static partial void CouldNotIdentifyClient(ILogger logger, string message);
 
        [LoggerMessage(Level = LogLevel.Warning, Message = "Rejected oversized message from client {ClientId}: {Size} bytes")]
        public static partial void OversizedMessage(ILogger logger, int clientId, int size);
 
        [LoggerMessage(Level = LogLevel.Warning, Message = "Rate limit exceeded for client {ClientId}")]
        public static partial void RateLimitExceeded(ILogger logger, int clientId);
 
        [LoggerMessage(Level = LogLevel.Debug, Message = "Client disconnected")]
        public static partial void ClientDisconnected(ILogger logger);
 
        [LoggerMessage(Level = LogLevel.Error, Message = "Error handling client")]
        public static partial void ErrorHandlingClient(ILogger logger, Exception ex);
 
        [LoggerMessage(Level = LogLevel.Warning, Message = "Invalid IPC message format: {Message}")]
        public static partial void InvalidIpcFormat(ILogger logger, string message);
 
        [LoggerMessage(Level = LogLevel.Error, Message = "Error processing IPC message")]
        public static partial void ErrorProcessingIpc(ILogger logger, Exception ex);
 
        [LoggerMessage(Level = LogLevel.Warning, Message = "Invalid keyboard injection data: {Message}")]
        public static partial void InvalidKeyboardInjectionData(ILogger logger, string message);
 
        [LoggerMessage(Level = LogLevel.Warning, Message = "Invalid mouse injection data: {Message}")]
        public static partial void InvalidMouseInjectionData(ILogger logger, string message);
    }
}
