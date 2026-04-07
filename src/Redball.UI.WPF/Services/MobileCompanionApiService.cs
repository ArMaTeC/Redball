using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Redball.UI.Services;

/// <summary>
/// Mobile Companion API service for iOS/Android remote control.
/// Provides HTTP endpoints for mobile apps to control Redball remotely.
/// </summary>
public class MobileCompanionApiService
{
    private static readonly Lazy<MobileCompanionApiService> _instance = new(() => new MobileCompanionApiService());
    public static MobileCompanionApiService Instance => _instance.Value;

    private HttpListener? _listener;
    private CancellationTokenSource? _cts;
    private bool _isRunning;
    private readonly int _port = 5000;
    private readonly Dictionary<string, PairedDevice> _pairedDevices;
    private readonly Dictionary<string, DateTime> _apiKeys;

    public event EventHandler<DevicePairedEventArgs>? DevicePaired;
    public event EventHandler<DeviceUnpairedEventArgs>? DeviceUnpaired;
#pragma warning disable CS0067 // Event is never used - reserved for future remote command handling
    public event EventHandler<RemoteCommandEventArgs>? RemoteCommandReceived;
#pragma warning restore CS0067

    public bool IsEnabled { get; set; }
    public bool IsRunning => _isRunning;
    public IReadOnlyDictionary<string, PairedDevice> PairedDevices => _pairedDevices;

    private MobileCompanionApiService()
    {
        _pairedDevices = new Dictionary<string, PairedDevice>();
        _apiKeys = new Dictionary<string, DateTime>();
        
        Logger.Verbose("MobileCompanionApiService", "Initialized");
    }

    /// <summary>
    /// Starts the mobile companion API server.
    /// </summary>
    public async Task StartAsync()
    {
        if (_isRunning || !IsEnabled)
            return;

        try
        {
            _cts = new CancellationTokenSource();
            _listener = new HttpListener();
            _listener.Prefixes.Add($"http://+:{_port}/");
            _listener.Start();

            _isRunning = true;
            _ = Task.Run(() => ListenLoopAsync(_cts.Token));

            Logger.Info("MobileCompanionApiService", $"API server started on port {_port}");
        }
        catch (Exception ex)
        {
            Logger.Warning("MobileCompanionApiService", $"Failed to start API server: {ex.Message}");
            ReleaseResources("Failed to start API server");
        }
    }

    private void ReleaseResources(string context)
    {
        try
        {
            _cts?.Cancel();
            _listener?.Stop();
            _listener?.Close();
            _isRunning = false;
            Logger.Debug("MobileCompanionApiService", $"Resources released: {context}");
        }
        catch (Exception ex)
        {
            Logger.Debug("MobileCompanionApiService", $"Error releasing resources: {ex.Message}");
        }
    }

    private async Task ListenLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested && _listener != null)
        {
            try
            {
                var context = await _listener.GetContextAsync();
                _ = Task.Run(() => HandleRequestAsync(context), ct);
            }
            catch (HttpListenerException) when (ct.IsCancellationRequested) { break; }
            catch (Exception ex) { Logger.Debug("MobileCompanionApiService", ex.Message); }
        }
    }

    public async Task<string?> RegisterDeviceAsync(ApiDeviceInfo deviceInfo)
    {
        try
        {
            var apiKey = Guid.NewGuid().ToString("N");
            var pairedDevice = new PairedDevice
            {
                DeviceId = deviceInfo.DeviceId ?? Guid.NewGuid().ToString("N"),
                DeviceName = deviceInfo.Name,
                Platform = deviceInfo.Platform,
                PairedAt = DateTime.UtcNow,
                ApiKey = apiKey
            };
            _pairedDevices[apiKey] = pairedDevice;
            return await Task.FromResult(apiKey);
        }
        catch (Exception ex)
        {
            Logger.Debug("MobileCompanionApiService", $"Failed to pair device: {ex.Message}");
            return null;
        }
    }

    private async Task HandleRequestAsync(HttpListenerContext context)
    {
        var req = context.Request;
        var resp = context.Response;
        resp.Headers.Add("Access-Control-Allow-Origin", "*");

        try
        {
            var path = req.Url?.LocalPath ?? "";
            var method = req.HttpMethod;
            object? result = null;
            int statusCode = 200;

            if (path == "/api/health" && method == "GET")
            {
                result = new { status = "healthy", timestamp = DateTime.UtcNow };
            }
            else if (path == "/api/status" && method == "GET")
            {
                result = new
                {
                    isActive = KeepAwakeService.Instance.IsActive,
                    sessionDuration = KeepAwakeService.Instance.CurrentSessionDuration,
                    batteryLevel = BatteryMonitorService.Instance.CurrentLevel,
                    isCharging = BatteryMonitorService.Instance.IsCharging,
                    timestamp = DateTime.UtcNow
                };
            }
            else if (path == "/api/toggle" && method == "POST")
            {
                if (!ValidateApiKey(req)) { statusCode = 401; result = new { error = "Unauthorized" }; }
                else { KeepAwakeService.Instance.Toggle(); result = new { success = true, isActive = KeepAwakeService.Instance.IsActive }; }
            }
            else if (path == "/api/start" && method == "POST")
            {
                if (!ValidateApiKey(req)) { statusCode = 401; result = new { error = "Unauthorized" }; }
                else { KeepAwakeService.Instance.SetActive(true); result = new { success = true }; }
            }
            else if (path == "/api/stop" && method == "POST")
            {
                if (!ValidateApiKey(req)) { statusCode = 401; result = new { error = "Unauthorized" }; }
                else { KeepAwakeService.Instance.SetActive(false); result = new { success = true }; }
            }
            else if (path == "/api/timed" && method == "POST")
            {
                if (!ValidateApiKey(req)) { statusCode = 401; result = new { error = "Unauthorized" }; }
                else
                {
                    using var sr = new StreamReader(req.InputStream);
                    var body = await sr.ReadToEndAsync();
                    var tr = JsonSerializer.Deserialize<TimedSessionRequest>(body);
                    if (tr?.Minutes > 0) { KeepAwakeService.Instance.StartTimed(tr.Minutes); result = new { success = true, duration = tr.Minutes }; }
                    else { statusCode = 400; result = new { error = "Invalid duration" }; }
                }
            }
            else if (path == "/api/pair/request" && method == "POST")
            {
                using var sr = new StreamReader(req.InputStream);
                var body = await sr.ReadToEndAsync();
                var di = JsonSerializer.Deserialize<ApiDeviceInfo>(body);
                var code = GeneratePairingCode();
                Logger.Info("MobileCompanionApiService", $"Pairing request from {di?.Name ?? "Unknown"} - Code: {code}");
                result = new { pairingCode = code, expiresAt = DateTime.UtcNow.AddMinutes(5) };
            }
            else if (path == "/api/pair/complete" && method == "POST")
            {
                using var sr = new StreamReader(req.InputStream);
                var body = await sr.ReadToEndAsync();
                var pr = JsonSerializer.Deserialize<PairingCompleteRequest>(body);
                if (string.IsNullOrEmpty(pr?.PairingCode)) { statusCode = 400; result = new { error = "Pairing code required" }; }
                else
                {
                    var apiKey = GenerateApiKey();
                    var device = new PairedDevice { DeviceId = Guid.NewGuid().ToString("N"), ApiKey = apiKey, DeviceName = "Mobile Device", Platform = "unknown", PairedAt = DateTime.UtcNow, LastConnectedAt = DateTime.UtcNow };
                    _pairedDevices[device.DeviceId] = device;
                    _apiKeys[apiKey] = DateTime.UtcNow;
                    DevicePaired?.Invoke(this, new DevicePairedEventArgs { Device = device });
                    Logger.Info("MobileCompanionApiService", $"Device paired: {device.DeviceName}");
                    result = new { success = true, deviceId = device.DeviceId, apiKey };
                }
            }
            else if (path == "/api/pair/unpair" && method == "POST")
            {
                if (!ValidateApiKey(req)) { statusCode = 401; result = new { error = "Unauthorized" }; }
                else
                {
                    using var sr = new StreamReader(req.InputStream);
                    var body = await sr.ReadToEndAsync();
                    var ur = JsonSerializer.Deserialize<UnpairRequest>(body);
                    if (_pairedDevices.TryGetValue(ur?.DeviceId ?? "", out var d))
                    {
                        _apiKeys.Remove(d.ApiKey);
                        _pairedDevices.Remove(ur!.DeviceId);
                        DeviceUnpaired?.Invoke(this, new DeviceUnpairedEventArgs { DeviceId = ur.DeviceId });
                        result = new { success = true };
                    }
                    else { statusCode = 404; result = new { error = "Device not found" }; }
                }
            }
            else if (path == "/api/devices" && method == "GET")
            {
                if (!ValidateApiKey(req)) { statusCode = 401; result = new { error = "Unauthorized" }; }
                else
                {
                    var devices = new List<object>();
                    foreach (var d in _pairedDevices.Values)
                        devices.Add(new { deviceId = d.DeviceId, deviceName = d.DeviceName, platform = d.Platform, pairedAt = d.PairedAt, lastConnectedAt = d.LastConnectedAt });
                    result = new { devices };
                }
            }
            else { statusCode = 404; result = new { error = "Not found" }; }

            resp.StatusCode = statusCode;
            var json = JsonSerializer.Serialize(result);
            var bytes = Encoding.UTF8.GetBytes(json);
            resp.ContentType = "application/json";
            resp.ContentLength64 = bytes.Length;
            await resp.OutputStream.WriteAsync(bytes, 0, bytes.Length);
        }
        catch (Exception ex)
        {
            Logger.Error("MobileCompanionApiService", "Request error", ex);
            resp.StatusCode = 500;
        }
        finally { resp.Close(); }
    }

    /// <summary>
    /// Stops the mobile companion API server.
    /// </summary>
    public async Task StopAsync()
    {
        if (!_isRunning || _listener == null)
            return;

        try
        {
            _cts?.Cancel();
            _listener.Stop();
            _listener.Close();
            _isRunning = false;
            Logger.Info("MobileCompanionApiService", "API server stopped");
        }
        catch (Exception ex)
        {
            Logger.Warning("MobileCompanionApiService", $"Error stopping API server: {ex.Message}");
        }
    }

    /// <summary>
    /// Generates a QR code data for pairing.
    /// </summary>
    public string GeneratePairingQRCodeData()
    {
        var pairingInfo = new
        {
            type = "redball_pairing",
            version = 1,
            host = GetLocalIpAddress(),
            port = _port,
            timestamp = DateTime.UtcNow
        };

        return JsonSerializer.Serialize(pairingInfo);
    }

    /// <summary>
    /// Unpairs all devices.
    /// </summary>
    public void UnpairAllDevices()
    {
        foreach (var deviceId in _pairedDevices.Keys.ToList())
        {
            if (_pairedDevices.TryGetValue(deviceId, out var device))
            {
                _apiKeys.Remove(device.ApiKey);
            }
        }
        
        _pairedDevices.Clear();
        
        Logger.Info("MobileCompanionApiService", "All devices unpaired");
    }

    private bool ValidateApiKey(HttpListenerRequest request)
    {
        var apiKey = request.Headers["X-API-Key"];
        if (string.IsNullOrEmpty(apiKey)) return false;
        if (!_apiKeys.ContainsKey(apiKey)) return false;
        foreach (var device in _pairedDevices.Values)
        {
            if (device.ApiKey == apiKey) { device.LastConnectedAt = DateTime.UtcNow; break; }
        }
        return true;
    }

    private string GeneratePairingCode()
    {
        // Generate 6-digit numeric code using cryptographically secure RNG
        const string chars = "0123456789";
        using var rng = System.Security.Cryptography.RandomNumberGenerator.Create();
        var bytes = new byte[6];
        rng.GetBytes(bytes);
        var result = new char[6];
        for (int i = 0; i < 6; i++)
        {
            result[i] = chars[bytes[i] % chars.Length];
        }
        return new string(result);
    }

    private string GenerateApiKey()
    {
        return Guid.NewGuid().ToString("N") + Guid.NewGuid().ToString("N");
    }

    private string GetLocalIpAddress()
    {
        try
        {
            var host = Dns.GetHostEntry(Dns.GetHostName());
            foreach (var ip in host.AddressList)
            {
                if (ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
                {
                    return ip.ToString();
                }
            }
        }
        catch (Exception ex)
        {
            Logger.Warning("MobileCompanionApiService", $"Failed to get local IP address: {ex.Message}");
        }
        
        return "localhost";
    }

    private void StorePendingPairing(string code, PendingDevice device)
    {
        // Store in memory or temp storage
        // Implementation would use a concurrent dictionary or similar
    }

    private PendingDevice? GetPendingPairing(string code)
    {
        // Retrieve from storage
        return null; // Placeholder
    }

    private void RemovePendingPairing(string code)
    {
        // Remove from storage
    }

    private void ShowPairingDialog(string code, PendingDevice device)
    {
        // Show system notification or dialog for user to confirm pairing
        Logger.Info("MobileCompanionApiService", 
            $"Pairing request from {device.DeviceName} ({device.Platform}) - Code: {code}");
        
        // Could show a WPF dialog here
    }
}

// Request/Response models
public class TimedSessionRequest
{
    public int Minutes { get; set; }
}

public class ApiDeviceInfo
{
    public string Name { get; set; } = string.Empty;
    public string Platform { get; set; } = string.Empty;
    public string? DeviceId { get; set; }
}

public class PairingCompleteRequest
{
    public string PairingCode { get; set; } = string.Empty;
}

public class UnpairRequest
{
    public string DeviceId { get; set; } = string.Empty;
}

// Domain models
public class PairedDevice
{
    public string DeviceId { get; set; } = string.Empty;
    public string ApiKey { get; set; } = string.Empty;
    public string DeviceName { get; set; } = string.Empty;
    public string Platform { get; set; } = string.Empty;
    public DateTime PairedAt { get; set; }
    public DateTime LastConnectedAt { get; set; }
}

public class PendingDevice
{
    public string PairingCode { get; set; } = string.Empty;
    public string DeviceName { get; set; } = string.Empty;
    public string Platform { get; set; } = string.Empty;
    public DateTime RequestedAt { get; set; }
    public DateTime ExpiresAt { get; set; }
}

// Event args
public class DevicePairedEventArgs : EventArgs
{
    public PairedDevice Device { get; set; } = new();
}

public class DeviceUnpairedEventArgs : EventArgs
{
    public string DeviceId { get; set; } = string.Empty;
}

public class RemoteCommandEventArgs : EventArgs
{
    public string Command { get; set; } = string.Empty;
    public string DeviceId { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; }
}
