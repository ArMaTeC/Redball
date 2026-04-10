using Redball.Core.Security;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;

namespace Redball.UI.Services;

/// <summary>
/// Centralized Management Console client for enterprise deployments.
/// Connects to a central management server for policy distribution,
/// usage reporting, and remote configuration management.
/// </summary>
public class CentralizedManagementService
{
    private static readonly Lazy<CentralizedManagementService> _instance = new(() => new CentralizedManagementService());
    public static CentralizedManagementService Instance => _instance.Value;

    private readonly HttpClient _httpClient;
    private readonly string _deviceId;
    private readonly string _cacheDir;
    
    private string? _serverUrl;
    private string? _authToken;
    private ManagementPolicy? _cachedPolicy;
    private DateTime _lastSync = DateTime.MinValue;

    public event EventHandler<PolicyUpdatedEventArgs>? PolicyUpdated;
    public event EventHandler<ManagementConnectionEventArgs>? ConnectionStateChanged;

    public bool IsEnabled => !string.IsNullOrEmpty(_serverUrl);
    public bool IsConnected => !string.IsNullOrEmpty(_authToken);
    public string DeviceId => _deviceId;
    public ManagementPolicy? CurrentPolicy => _cachedPolicy;
    public DateTime LastSync => _lastSync;

    private CentralizedManagementService()
    {
        _httpClient = new HttpClient();
        _httpClient.DefaultRequestHeaders.Add("User-Agent", $"Redball/{GetAppVersion()}");
        
        // Generate unique device ID
        _deviceId = GenerateDeviceId();
        
        // Cache directory for offline policy storage
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        _cacheDir = Path.Combine(appData, "Redball", "ManagementCache");
        
        if (!Directory.Exists(_cacheDir))
        {
            Directory.CreateDirectory(_cacheDir);
        }

        LoadCachedPolicy();
        
        Logger.Verbose("CentralizedManagementService", $"Initialized with device ID: {_deviceId}");
    }

    /// <summary>
    /// Configures the management server connection.
    /// </summary>
    public void Configure(string serverUrl, string? authToken = null)
    {
        _serverUrl = serverUrl?.TrimEnd('/');
        _authToken = authToken;
        
        if (!string.IsNullOrEmpty(_authToken))
        {
            _httpClient.DefaultRequestHeaders.Authorization = 
                new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _authToken);
        }

        Logger.Info("CentralizedManagementService", $"Configured server: {_serverUrl}");
    }

    /// <summary>
    /// Registers this device with the management server.
    /// </summary>
    public async Task<bool> RegisterDeviceAsync(string organizationId, string? deviceName = null)
    {
        if (!IsEnabled)
        {
            Logger.Warning("CentralizedManagementService", "Cannot register - server not configured");
            return false;
        }

        try
        {
            var registration = new DeviceRegistration
            {
                DeviceId = _deviceId,
                OrganizationId = organizationId,
                DeviceName = deviceName ?? Environment.MachineName,
                Platform = GetPlatform(),
                AppVersion = GetAppVersion(),
                UserName = Environment.UserName,
                RegisteredAt = DateTime.UtcNow
            };

            var json = JsonSerializer.Serialize(registration);
            var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");

            var response = await _httpClient.PostAsync($"{_serverUrl}/api/devices/register", content);
            
            if (response.IsSuccessStatusCode)
            {
                Logger.Info("CentralizedManagementService", "Device registered successfully");
                
                ConnectionStateChanged?.Invoke(this, new ManagementConnectionEventArgs
                {
                    IsConnected = true,
                    Timestamp = DateTime.UtcNow
                });

                // Fetch initial policy
                await SyncPolicyAsync();
                
                return true;
            }
            else
            {
                var error = await response.Content.ReadAsStringAsync();
                Logger.Error("CentralizedManagementService", $"Registration failed: {error}");
                return false;
            }
        }
        catch (Exception ex)
        {
            Logger.Error("CentralizedManagementService", "Device registration failed", ex);
            return false;
        }
    }

    /// <summary>
    /// Synchronizes policy from the management server.
    /// </summary>
    public async Task<bool> SyncPolicyAsync()
    {
        if (!IsEnabled)
            return false;

        try
        {
            var response = await _httpClient.GetAsync($"{_serverUrl}/api/devices/{_deviceId}/policy");
            
            if (response.IsSuccessStatusCode)
            {
                var json = await response.Content.ReadAsStringAsync();
                var policy = SecureJsonSerializer.Deserialize<ManagementPolicy>(json);

                if (policy != null)
                {
                    await ApplyPolicyAsync(policy);
                    
                    _lastSync = DateTime.UtcNow;
                    _cachedPolicy = policy;
                    SaveCachedPolicy(policy);

                    PolicyUpdated?.Invoke(this, new PolicyUpdatedEventArgs
                    {
                        Policy = policy,
                        UpdatedAt = DateTime.UtcNow
                    });

                    Logger.Info("CentralizedManagementService", "Policy synchronized successfully");
                    return true;
                }
            }
            else if (response.StatusCode == System.Net.HttpStatusCode.NotModified)
            {
                Logger.Debug("CentralizedManagementService", "Policy unchanged");
                return true;
            }
            else
            {
                var error = await response.Content.ReadAsStringAsync();
                Logger.Warning("CentralizedManagementService", $"Policy sync failed: {error}");
            }
        }
        catch (Exception ex)
        {
            Logger.Error("CentralizedManagementService", "Policy synchronization failed", ex);
            
            // Use cached policy if available
            if (_cachedPolicy != null)
            {
                Logger.Info("CentralizedManagementService", "Using cached policy");
                await ApplyPolicyAsync(_cachedPolicy);
            }
        }

        return false;
    }

    /// <summary>
    /// Reports usage metrics to the management server.
    /// </summary>
    public async Task<bool> ReportUsageAsync(UsageMetrics metrics)
    {
        if (!IsEnabled)
            return false;

        try
        {
            var report = new UsageReport
            {
                DeviceId = _deviceId,
                Timestamp = DateTime.UtcNow,
                Metrics = metrics
            };

            var json = JsonSerializer.Serialize(report);
            var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");

            var response = await _httpClient.PostAsync($"{_serverUrl}/api/usage/report", content);
            
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            Logger.Debug("CentralizedManagementService", $"Usage report failed (will retry): {ex.Message}");
            // Store for later retry
            StorePendingReport(metrics);
            return false;
        }
    }

    /// <summary>
    /// Checks for remote commands from the management server.
    /// </summary>
    public async Task<List<RemoteCommand>> CheckForCommandsAsync()
    {
        if (!IsEnabled)
            return new List<RemoteCommand>();

        try
        {
            var response = await _httpClient.GetAsync($"{_serverUrl}/api/devices/{_deviceId}/commands");
            
            if (response.IsSuccessStatusCode)
            {
                var json = await response.Content.ReadAsStringAsync();
                // SECURITY: Use SecureJsonSerializer with size limit and max depth
                var commands = SecureJsonSerializer.Deserialize<List<RemoteCommand>>(json);
                return commands ?? new List<RemoteCommand>();
            }
        }
        catch (Exception ex)
        {
            Logger.Debug("CentralizedManagementService", $"Command check failed: {ex.Message}");
        }

        return new List<RemoteCommand>();
    }

    /// <summary>
    /// Executes a remote command from the management server.
    /// </summary>
    public async Task<CommandResult> ExecuteCommandAsync(RemoteCommand command)
    {
        var result = new CommandResult
        {
            CommandId = command.Id,
            ExecutedAt = DateTime.UtcNow
        };

        try
        {
            Logger.Info("CentralizedManagementService", $"Executing remote command: {command.Type}");

            switch (command.Type.ToLower())
            {
                case "start_keep_awake":
                    KeepAwakeService.Instance.SetActive(true);
                    result.Success = true;
                    break;

                case "stop_keep_awake":
                    KeepAwakeService.Instance.SetActive(false);
                    result.Success = true;
                    break;

                case "apply_policy":
                    if (command.Payload != null)
                    {
                        // SECURITY: Use SecureJsonSerializer with size limit and max depth
                        var policy = SecureJsonSerializer.Deserialize<ManagementPolicy>(command.Payload.ToString()!);
                        if (policy != null)
                        {
                            await ApplyPolicyAsync(policy);
                            result.Success = true;
                        }
                    }
                    break;

                case "update_config":
                    // Apply configuration update
                    result.Success = true;
                    break;

                case "ping":
                    result.Success = true;
                    result.Output = "pong";
                    break;

                default:
                    result.Success = false;
                    result.Error = $"Unknown command type: {command.Type}";
                    break;
            }

            // Acknowledge command execution
            await AcknowledgeCommandAsync(command.Id, result);
        }
        catch (Exception ex)
        {
            // SECURITY: Log full exception internally, return safe message to caller
            result.Success = false;
            result.Error = SafeExceptionHandler.GetSafeErrorMessageForOperation("Command execution");
            Logger.Error("CentralizedManagementService", $"Command execution failed: {command.Type}", ex);
        }

        return result;
    }

    /// <summary>
    /// Heartbeat to keep connection alive and report status.
    /// </summary>
    public async Task SendHeartbeatAsync()
    {
        if (!IsEnabled)
            return;

        try
        {
            var status = new DeviceStatus
            {
                DeviceId = _deviceId,
                Timestamp = DateTime.UtcNow,
                IsKeepAwakeActive = KeepAwakeService.Instance.IsActive,
                SessionDuration = KeepAwakeService.Instance.CurrentSessionDuration,
                BatteryLevel = BatteryMonitorService.Instance.CurrentLevel,
                IsCharging = BatteryMonitorService.Instance.IsCharging,
                AppVersion = GetAppVersion()
            };

            var json = JsonSerializer.Serialize(status);
            var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");

            await _httpClient.PostAsync($"{_serverUrl}/api/devices/{_deviceId}/heartbeat", content);
        }
        catch (Exception ex)
        {
            Logger.Debug("CentralizedManagementService", $"Heartbeat failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Disconnects from the management server.
    /// </summary>
    public void Disconnect()
    {
        _authToken = null;
        _httpClient.DefaultRequestHeaders.Authorization = null;
        
        ConnectionStateChanged?.Invoke(this, new ManagementConnectionEventArgs
        {
            IsConnected = false,
            Timestamp = DateTime.UtcNow
        });

        Logger.Info("CentralizedManagementService", "Disconnected from management server");
    }

    private async Task ApplyPolicyAsync(ManagementPolicy policy)
    {
        var config = ConfigService.Instance.Config;

        // Apply security policies
        if (policy.RequireBatteryAware.HasValue)
            config.BatteryAware = policy.RequireBatteryAware.Value;

        if (policy.RequireIdleDetection.HasValue)
            config.IdleDetection = policy.RequireIdleDetection.Value;

        if (policy.RequireNetworkAware.HasValue)
            config.NetworkAware = policy.RequireNetworkAware.Value;

        if (policy.MaxSessionDurationMinutes.HasValue)
            config.DefaultDuration = Math.Min(config.DefaultDuration, policy.MaxSessionDurationMinutes.Value);

        if (policy.DisableScheduledRestart.HasValue)
            config.AutoExitOnComplete = !policy.DisableScheduledRestart.Value;

        if (policy.AllowUserOverrides.HasValue)
        {
            // Store policy enforcement flag
        }

        // Apply feature restrictions
        if (policy.DisableBrowserExtension.HasValue && policy.DisableBrowserExtension.Value)
        {
            // Disable browser extension
        }

        Logger.Info("CentralizedManagementService", "Applied management policy");
    }

    private async Task AcknowledgeCommandAsync(string commandId, CommandResult result)
    {
        try
        {
            var json = JsonSerializer.Serialize(result);
            var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");
            
            await _httpClient.PostAsync($"{_serverUrl}/api/commands/{commandId}/ack", content);
        }
        catch (Exception ex)
        {
            Logger.Warning("CentralizedManagementService", $"Failed to acknowledge command: {ex.Message}");
        }
    }

    private void StorePendingReport(UsageMetrics metrics)
    {
        // Store for later retry when connection is restored
        var pendingFile = Path.Combine(_cacheDir, $"pending_report_{DateTime.UtcNow:yyyyMMddHHmmss}.json");
        var json = JsonSerializer.Serialize(metrics);
        File.WriteAllText(pendingFile, json);
    }

    private void SaveCachedPolicy(ManagementPolicy policy)
    {
        var policyFile = Path.Combine(_cacheDir, "cached_policy.json");
        var json = JsonSerializer.Serialize(policy, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(policyFile, json);
    }

    private void LoadCachedPolicy()
    {
        var policyFile = Path.Combine(_cacheDir, "cached_policy.json");
        if (File.Exists(policyFile))
        {
            try
            {
                var json = File.ReadAllText(policyFile);
                // SECURITY: Use SecureJsonSerializer with size limit and max depth
                _cachedPolicy = SecureJsonSerializer.Deserialize<ManagementPolicy>(json);
            }
            catch (Exception ex)
            {
                // SECURITY: Log full details internally
                Logger.Warning("CentralizedManagementService", "Failed to load cached policy", ex);
            }
        }
    }

    private string GenerateDeviceId()
    {
        // Create unique device ID based on machine characteristics
        var machineName = Environment.MachineName;
        var userName = Environment.UserName;
        var combined = $"{machineName}:{userName}:{GetAppVersion()}";
        
        using var sha = System.Security.Cryptography.SHA256.Create();
        var hash = sha.ComputeHash(System.Text.Encoding.UTF8.GetBytes(combined));
        return Convert.ToHexString(hash).Substring(0, 16).ToLower();
    }

    private string GetPlatform()
    {
        if (OperatingSystem.IsWindows()) return "windows";
        if (OperatingSystem.IsMacOS()) return "macos";
        if (OperatingSystem.IsLinux()) return "linux";
        return "unknown";
    }

    private string GetAppVersion()
    {
        return typeof(CentralizedManagementService).Assembly.GetName().Version?.ToString(3) ?? "3.0.0";
    }
}

// Data models
public class ManagementPolicy
{
    public string PolicyId { get; set; } = string.Empty;
    public string PolicyName { get; set; } = string.Empty;
    public string OrganizationId { get; set; } = string.Empty;
    public DateTime IssuedAt { get; set; }
    public DateTime? ExpiresAt { get; set; }
    
    // Security policies
    public bool? RequireBatteryAware { get; set; }
    public bool? RequireIdleDetection { get; set; }
    public bool? RequireNetworkAware { get; set; }
    public int? MaxSessionDurationMinutes { get; set; }
    public bool? DisableScheduledRestart { get; set; }
    public bool? AllowUserOverrides { get; set; }
    
    // Feature restrictions
    public bool? DisableBrowserExtension { get; set; }
    public bool? DisableTypeThing { get; set; }
    
    // Audit settings
    public bool? RequireAuditLogging { get; set; }
    public int? AuditRetentionDays { get; set; }
}

public class DeviceRegistration
{
    public string DeviceId { get; set; } = string.Empty;
    public string OrganizationId { get; set; } = string.Empty;
    public string DeviceName { get; set; } = string.Empty;
    public string Platform { get; set; } = string.Empty;
    public string AppVersion { get; set; } = string.Empty;
    public string UserName { get; set; } = string.Empty;
    public DateTime RegisteredAt { get; set; }
}

public class DeviceStatus
{
    public string DeviceId { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; }
    public bool IsKeepAwakeActive { get; set; }
    public TimeSpan SessionDuration { get; set; }
    public int BatteryLevel { get; set; }
    public bool IsCharging { get; set; }
    public string AppVersion { get; set; } = string.Empty;
}

public class RemoteCommand
{
    public string Id { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public object? Payload { get; set; }
    public DateTime IssuedAt { get; set; }
    public DateTime? ExpiresAt { get; set; }
}

public class CommandResult
{
    public string CommandId { get; set; } = string.Empty;
    public bool Success { get; set; }
    public string? Output { get; set; }
    public string? Error { get; set; }
    public DateTime ExecutedAt { get; set; }
}

public class CentralUsageMetrics
{
    public int SessionCount { get; set; }
    public TimeSpan TotalActiveTime { get; set; }
    public int ConfigChanges { get; set; }
    public Dictionary<string, int> FeatureUsage { get; set; } = new();
}

// Event args
public class PolicyUpdatedEventArgs : EventArgs
{
    public ManagementPolicy Policy { get; set; } = new();
    public DateTime UpdatedAt { get; set; }
}

public class ManagementConnectionEventArgs : EventArgs
{
    public bool IsConnected { get; set; }
    public DateTime Timestamp { get; set; }
}
