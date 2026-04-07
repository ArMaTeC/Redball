using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace Redball.UI.Services;

/// <summary>
/// Smart Home Integration service for IoT device connectivity.
/// Integrates with popular smart home platforms (Philips Hue, SmartThings, Home Assistant).
/// </summary>
public class SmartHomeIntegrationService
{
    private static readonly Lazy<SmartHomeIntegrationService> _instance = new(() => new SmartHomeIntegrationService());
    public static SmartHomeIntegrationService Instance => _instance.Value;

    private readonly HttpClient _httpClient;
    private readonly Dictionary<string, SmartHomePlatform> _connectedPlatforms;
    private readonly List<SmartDevice> _discoveredDevices;

    public event EventHandler<DeviceDiscoveredEventArgs>? DeviceDiscovered;
    public event EventHandler<DeviceStateChangedEventArgs>? DeviceStateChanged;
    public event EventHandler<AutomationTriggeredEventArgs>? AutomationTriggered;

    public bool IsEnabled { get; set; }
    public IReadOnlyList<SmartHomePlatform> ConnectedPlatforms => _connectedPlatforms.Values.ToList();
    public IReadOnlyList<SmartDevice> DiscoveredDevices => _discoveredDevices;

    private SmartHomeIntegrationService()
    {
        _httpClient = new HttpClient();
        _connectedPlatforms = new Dictionary<string, SmartHomePlatform>();
        _discoveredDevices = new List<SmartDevice>();
        
        Logger.Verbose("SmartHomeIntegrationService", "Initialized");
    }

    /// <summary>
    /// Discovers available smart home platforms on the network.
    /// </summary>
    public async Task<List<SmartHomePlatform>> DiscoverPlatformsAsync()
    {
        var platforms = new List<SmartHomePlatform>();

        try
        {
            // Discover Philips Hue bridges
            var hueBridges = await DiscoverHueBridgesAsync();
            platforms.AddRange(hueBridges);

            // Discover Home Assistant instances
            var homeAssistant = await DiscoverHomeAssistantAsync();
            if (homeAssistant != null)
                platforms.Add(homeAssistant);

            // Discover SmartThings hubs
            var smartThings = await DiscoverSmartThingsAsync();
            if (smartThings != null)
                platforms.Add(smartThings);

            Logger.Info("SmartHomeIntegrationService", $"Discovered {platforms.Count} smart home platforms");
        }
        catch (Exception ex)
        {
            Logger.Error("SmartHomeIntegrationService", "Platform discovery failed", ex);
        }

        return platforms;
    }

    /// <summary>
    /// Connects to a smart home platform.
    /// </summary>
    public async Task<bool> ConnectPlatformAsync(SmartHomePlatform platform, string apiKey)
    {
        try
        {
            platform.ApiKey = apiKey;
            platform.IsConnected = await ValidateConnectionAsync(platform);

            if (platform.IsConnected)
            {
                _connectedPlatforms[platform.PlatformId] = platform;
                
                // Discover devices on this platform
                var devices = await DiscoverDevicesAsync(platform);
                _discoveredDevices.AddRange(devices);

                foreach (var device in devices)
                {
                    DeviceDiscovered?.Invoke(this, new DeviceDiscoveredEventArgs
                    {
                        Device = device,
                        Platform = platform
                    });
                }

                Logger.Info("SmartHomeIntegrationService", $"Connected to {platform.Name}");
                return true;
            }
        }
        catch (Exception ex)
        {
            Logger.Error("SmartHomeIntegrationService", $"Failed to connect to {platform.Name}", ex);
        }

        return false;
    }

    /// <summary>
    /// Controls a smart device.
    /// </summary>
    public async Task<bool> ControlDeviceAsync(string deviceId, DeviceCommand command)
    {
        try
        {
            var device = _discoveredDevices.FirstOrDefault(d => d.DeviceId == deviceId);
            if (device == null)
            {
                Logger.Warning("SmartHomeIntegrationService", $"Device {deviceId} not found");
                return false;
            }

            var platform = _connectedPlatforms.Values.FirstOrDefault(p => p.PlatformId == device.PlatformId);
            if (platform == null)
            {
                Logger.Warning("SmartHomeIntegrationService", $"Platform for device {deviceId} not connected");
                return false;
            }

            bool success = false;

            switch (platform.Type)
            {
                case PlatformType.PhilipsHue:
                    success = await ControlHueDeviceAsync(device, command, platform);
                    break;
                    
                case PlatformType.HomeAssistant:
                    success = await ControlHomeAssistantDeviceAsync(device, command, platform);
                    break;
                    
                case PlatformType.SmartThings:
                    success = await ControlSmartThingsDeviceAsync(device, command, platform);
                    break;
            }

            if (success)
            {
                DeviceStateChanged?.Invoke(this, new DeviceStateChangedEventArgs
                {
                    DeviceId = deviceId,
                    NewState = command,
                    ChangedAt = DateTime.UtcNow
                });
            }

            return success;
        }
        catch (Exception ex)
        {
            Logger.Error("SmartHomeIntegrationService", $"Failed to control device {deviceId}", ex);
            return false;
        }
    }

    /// <summary>
    /// Sets up automation rules based on Redball state.
    /// </summary>
    public void SetupRedballAutomations()
    {
        // When keep-awake starts: dim lights, set do-not-disturb
        KeepAwakeService.Instance.ActiveStateChanged += async (s, e) =>
        {
            if (KeepAwakeService.Instance.IsActive)
            {
                await OnKeepAwakeStartedAsync();
            }
            else
            {
                await OnKeepAwakeStoppedAsync();
            }
        };

        // When battery is low: turn off non-essential devices
        // Implementation would subscribe to battery events

        Logger.Info("SmartHomeIntegrationService", "Redball automations configured");
    }

    /// <summary>
    /// Creates a scene for focus mode.
    /// </summary>
    public async Task<bool> ActivateFocusSceneAsync()
    {
        try
        {
            // Dim lights to warm color
            var lights = _discoveredDevices.Where(d => d.DeviceType == DeviceType.Light);
            foreach (var light in lights)
            {
                await ControlDeviceAsync(light.DeviceId, new DeviceCommand
                {
                    Action = "set_state",
                    Parameters = new Dictionary<string, object>
                    {
                        ["on"] = true,
                        ["brightness"] = 30,
                        ["color_temperature"] = 500 // Warm
                    }
                });
            }

            AutomationTriggered?.Invoke(this, new AutomationTriggeredEventArgs
            {
                AutomationName = "Focus Mode",
                TriggeredAt = DateTime.UtcNow
            });

            return true;
        }
        catch (Exception ex)
        {
            Logger.Error("SmartHomeIntegrationService", "Failed to activate focus scene", ex);
            return false;
        }
    }

    // Platform-specific implementations

    private async Task<List<SmartHomePlatform>> DiscoverHueBridgesAsync()
    {
        var bridges = new List<SmartHomePlatform>();

        try
        {
            // Use Philips Hue discovery endpoint
            // https://discovery.meethue.com/
            var response = await _httpClient.GetStringAsync("https://discovery.meethue.com/");
            var hueBridges = JsonSerializer.Deserialize<List<HueBridgeInfo>>(response);

            if (hueBridges != null)
            {
                foreach (var bridge in hueBridges)
                {
                    bridges.Add(new SmartHomePlatform
                    {
                        PlatformId = bridge.Id,
                        Name = $"Philips Hue ({bridge.InternalIpAddress})",
                        Type = PlatformType.PhilipsHue,
                        IpAddress = bridge.InternalIpAddress,
                        Port = 80
                    });
                }
            }
        }
        catch (Exception ex)
        {
            Logger.Debug("SmartHomeIntegrationService", $"Hue discovery failed: {ex.Message}");
        }

        return bridges;
    }

    private async Task<SmartHomePlatform?> DiscoverHomeAssistantAsync()
    {
        try
        {
            // Home Assistant discovery via mDNS or manual entry
            // For now, check common addresses
            var candidates = new[] { "homeassistant.local", "hassio.local", "homeassistant" };
            
            foreach (var host in candidates)
            {
                try
                {
                    var response = await _httpClient.GetAsync($"http://{host}:8123/api/");
                    if (response.IsSuccessStatusCode)
                    {
                        return new SmartHomePlatform
                        {
                            PlatformId = Guid.NewGuid().ToString(),
                            Name = "Home Assistant",
                            Type = PlatformType.HomeAssistant,
                            IpAddress = host,
                            Port = 8123
                        };
                    }
                }
                catch (Exception ex)
                {
                    Logger.Debug("SmartHomeIntegrationService", $"HomeAssistant discovery attempt failed for {host}: {ex.Message}");
                    // Try next potential host
                }
            }
        }
        catch (Exception ex)
        {
            Logger.Debug("SmartHomeIntegrationService", $"Home Assistant discovery failed: {ex.Message}");
        }

        return null;
    }

    private async Task<SmartHomePlatform?> DiscoverSmartThingsAsync()
    {
        // SmartThings requires API token, can't auto-discover
        return null;
    }

    private async Task<bool> ValidateConnectionAsync(SmartHomePlatform platform)
    {
        try
        {
            var baseUrl = $"http://{platform.IpAddress}:{platform.Port}";
            
            switch (platform.Type)
            {
                case PlatformType.PhilipsHue:
                    var hueResponse = await _httpClient.GetAsync($"{baseUrl}/api/{platform.ApiKey}/config");
                    return hueResponse.IsSuccessStatusCode;
                    
                case PlatformType.HomeAssistant:
                    _httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {platform.ApiKey}");
                    var haResponse = await _httpClient.GetAsync($"{baseUrl}/api/");
                    _httpClient.DefaultRequestHeaders.Remove("Authorization");
                    return haResponse.IsSuccessStatusCode;
                    
                default:
                    return false;
            }
        }
        catch (Exception ex)
        {
            Logger.Debug("SmartHomeIntegrationService", $"Connection validation failed for {platform.Name}: {ex.Message}");
            return false;
        }
    }

    private async Task<List<SmartDevice>> DiscoverDevicesAsync(SmartHomePlatform platform)
    {
        var devices = new List<SmartDevice>();

        try
        {
            var baseUrl = $"http://{platform.IpAddress}:{platform.Port}";

            switch (platform.Type)
            {
                case PlatformType.PhilipsHue:
                    var hueResponse = await _httpClient.GetAsync($"{baseUrl}/api/{platform.ApiKey}/lights");
                    if (hueResponse.IsSuccessStatusCode)
                    {
                        var hueJson = await hueResponse.Content.ReadAsStringAsync();
                        var hueLights = JsonSerializer.Deserialize<Dictionary<string, HueLight>>(hueJson);
                        
                        if (hueLights != null)
                        {
                            foreach (var light in hueLights)
                            {
                                devices.Add(new SmartDevice
                                {
                                    DeviceId = $"hue_{light.Key}",
                                    PlatformId = platform.PlatformId,
                                    Name = light.Value.Name,
                                    DeviceType = DeviceType.Light,
                                    IsReachable = light.Value.State?.Reachable ?? false
                                });
                            }
                        }
                    }
                    break;

                case PlatformType.HomeAssistant:
                    _httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {platform.ApiKey}");
                    var haResponse = await _httpClient.GetAsync($"{baseUrl}/api/states");
                    _httpClient.DefaultRequestHeaders.Remove("Authorization");
                    
                    if (haResponse.IsSuccessStatusCode)
                    {
                        var haJson = await haResponse.Content.ReadAsStringAsync();
                        var haStates = JsonSerializer.Deserialize<List<HomeAssistantState>>(haJson);
                        
                        if (haStates != null)
                        {
                            foreach (var state in haStates.Where(s => s.EntityId?.StartsWith("light.") == true || 
                                                                        s.EntityId?.StartsWith("switch.") == true))
                            {
                                if (string.IsNullOrEmpty(state.EntityId)) continue;
                                devices.Add(new SmartDevice
                                {
                                    DeviceId = state.EntityId!,
                                    PlatformId = platform.PlatformId,
                                    Name = state.Attributes?.FriendlyName ?? state.EntityId!,
                                    DeviceType = state.EntityId.StartsWith("light.") ? DeviceType.Light : DeviceType.Switch,
                                    IsReachable = state.State != "unavailable"
                                });
                            }
                        }
                    }
                    break;
            }
        }
        catch (Exception ex)
        {
            Logger.Error("SmartHomeIntegrationService", $"Device discovery failed for {platform.Name}", ex);
        }

        return devices;
    }

    private async Task<bool> ControlHueDeviceAsync(SmartDevice device, DeviceCommand command, SmartHomePlatform platform)
    {
        try
        {
            var lightId = device.DeviceId.Replace("hue_", "");
            var baseUrl = $"http://{platform.IpAddress}:{platform.Port}";
            
            var state = new Dictionary<string, object>();
            
            if (command.Parameters.TryGetValue("on", out var onValue))
                state["on"] = onValue;
            
            if (command.Parameters.TryGetValue("brightness", out var briValue))
                state["bri"] = (int)((double)briValue / 100 * 254);

            var json = JsonSerializer.Serialize(state);
            var content = new StringContent(json, Encoding.UTF8, "application/json");
            
            var response = await _httpClient.PutAsync(
                $"{baseUrl}/api/{platform.ApiKey}/lights/{lightId}/state", 
                content);
            
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            Logger.Error("SmartHomeIntegrationService", $"Hue control failed for {device.Name}", ex);
            return false;
        }
    }

    private async Task<bool> ControlHomeAssistantDeviceAsync(SmartDevice device, DeviceCommand command, SmartHomePlatform platform)
    {
        try
        {
            var baseUrl = $"http://{platform.IpAddress}:{platform.Port}";
            var domain = device.DeviceType == DeviceType.Light ? "light" : "switch";
            var service = command.Parameters.TryGetValue("on", out var onVal) && (bool)onVal ? "turn_on" : "turn_off";

            var haCommand = new
            {
                entity_id = device.DeviceId
            };

            var json = JsonSerializer.Serialize(haCommand);
            var content = new StringContent(json, Encoding.UTF8, "application/json");
            
            _httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {platform.ApiKey}");
            var response = await _httpClient.PostAsync(
                $"{baseUrl}/api/services/{domain}/{service}", 
                content);
            _httpClient.DefaultRequestHeaders.Remove("Authorization");
            
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            Logger.Error("SmartHomeIntegrationService", $"Home Assistant control failed for {device.Name}", ex);
            return false;
        }
    }

    private async Task<bool> ControlSmartThingsDeviceAsync(SmartDevice device, DeviceCommand command, SmartHomePlatform platform)
    {
        // Implementation for SmartThings API
        await Task.CompletedTask;
        return false;
    }

    private async Task OnKeepAwakeStartedAsync()
    {
        // Dim lights, set DND mode
        await ActivateFocusSceneAsync();
        
        Logger.Info("SmartHomeIntegrationService", "Keep-awake started - smart home scene activated");
    }

    private async Task OnKeepAwakeStoppedAsync()
    {
        // Restore normal lighting
        var lights = _discoveredDevices.Where(d => d.DeviceType == DeviceType.Light);
        foreach (var light in lights)
        {
            await ControlDeviceAsync(light.DeviceId, new DeviceCommand
            {
                Action = "set_state",
                Parameters = new Dictionary<string, object>
                {
                    ["on"] = true,
                    ["brightness"] = 100,
                    ["color_temperature"] = 2700 // Daylight
                }
            });
        }
        
        Logger.Info("SmartHomeIntegrationService", "Keep-awake stopped - normal lighting restored");
    }
}

// Platform and device models
public class SmartHomePlatform
{
    public string PlatformId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public PlatformType Type { get; set; }
    public string IpAddress { get; set; } = string.Empty;
    public int Port { get; set; }
    public string? ApiKey { get; set; }
    public bool IsConnected { get; set; }
}

public enum PlatformType
{
    PhilipsHue,
    HomeAssistant,
    SmartThings,
    Custom
}

public class SmartDevice
{
    public string DeviceId { get; set; } = string.Empty;
    public string PlatformId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public DeviceType DeviceType { get; set; }
    public bool IsReachable { get; set; }
    public Dictionary<string, object> State { get; set; } = new();
}

public enum DeviceType
{
    Light,
    Switch,
    Outlet,
    Thermostat,
    Lock,
    Sensor,
    Other
}

public class DeviceCommand
{
    public string Action { get; set; } = string.Empty;
    public Dictionary<string, object> Parameters { get; set; } = new();
}

// Platform-specific models
public class HueBridgeInfo
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;
    
    [JsonPropertyName("internalipaddress")]
    public string InternalIpAddress { get; set; } = string.Empty;
}

public class HueLight
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;
    
    [JsonPropertyName("state")]
    public HueLightState? State { get; set; }
}

public class HueLightState
{
    [JsonPropertyName("on")]
    public bool On { get; set; }
    
    [JsonPropertyName("bri")]
    public int Brightness { get; set; }
    
    [JsonPropertyName("reachable")]
    public bool Reachable { get; set; }
}

public class HomeAssistantState
{
    [JsonPropertyName("entity_id")]
    public string? EntityId { get; set; }
    
    [JsonPropertyName("state")]
    public string State { get; set; } = string.Empty;
    
    [JsonPropertyName("attributes")]
    public HomeAssistantAttributes? Attributes { get; set; }
}

public class HomeAssistantAttributes
{
    [JsonPropertyName("friendly_name")]
    public string? FriendlyName { get; set; }
}

// Event args
public class DeviceDiscoveredEventArgs : EventArgs
{
    public SmartDevice Device { get; set; } = new();
    public SmartHomePlatform Platform { get; set; } = new();
}

public class DeviceStateChangedEventArgs : EventArgs
{
    public string DeviceId { get; set; } = string.Empty;
    public DeviceCommand NewState { get; set; } = new();
    public DateTime ChangedAt { get; set; }
}

public class AutomationTriggeredEventArgs : EventArgs
{
    public string AutomationName { get; set; } = string.Empty;
    public DateTime TriggeredAt { get; set; }
}
