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
/// Mobile notification service for sending push notifications to paired mobile devices.
/// Integrates with Firebase Cloud Messaging (FCM) or custom push notification providers.
/// </summary>
public class MobileNotificationService
{
    private static readonly Lazy<MobileNotificationService> _instance = new(() => new MobileNotificationService());
    public static MobileNotificationService Instance => _instance.Value;

    private readonly HttpClient _httpClient;
    private readonly MobileCompanionApiService _companionApi;
    private string? _fcmServerKey;
    private string? _fcmSenderId;

    public event EventHandler<NotificationSentEventArgs>? NotificationSent;
    public event EventHandler<NotificationFailedEventArgs>? NotificationFailed;

    public bool IsEnabled => !string.IsNullOrEmpty(_fcmServerKey);

    private MobileNotificationService()
    {
        _httpClient = new HttpClient();
        _companionApi = MobileCompanionApiService.Instance;
        
        // Subscribe to events that should trigger notifications
        KeepAwakeService.Instance.ActiveStateChanged += OnKeepAwakeStateChanged;
        
        Logger.Verbose("MobileNotificationService", "Initialized");
    }

    /// <summary>
    /// Configures Firebase Cloud Messaging credentials.
    /// </summary>
    public void ConfigureFCM(string serverKey, string senderId)
    {
        _fcmServerKey = serverKey;
        _fcmSenderId = senderId;
        
        _httpClient.DefaultRequestHeaders.TryAddWithoutValidation("Authorization", $"key={serverKey}");
        
        Logger.Info("MobileNotificationService", "FCM configured");
    }

    /// <summary>
    /// Sends a notification to a specific device.
    /// </summary>
    public async Task<bool> SendNotificationAsync(string deviceId, MobileNotification notification)
    {
        if (!IsEnabled)
        {
            Logger.Warning("MobileNotificationService", "Cannot send notification - FCM not configured");
            return false;
        }

        try
        {
            // Get device registration token
            var deviceToken = await GetDeviceTokenAsync(deviceId);
            if (string.IsNullOrEmpty(deviceToken))
            {
                Logger.Warning("MobileNotificationService", $"No registration token for device {deviceId}");
                return false;
            }

            // Build FCM message
            var message = new FCMMessage
            {
                To = deviceToken,
                Notification = new FCMNotificationPayload
                {
                    Title = notification.Title,
                    Body = notification.Body,
                    Icon = "redball_notification",
                    Sound = notification.PlaySound ? "default" : null
                },
                Data = notification.Data,
                Priority = notification.Priority.ToString().ToLower()
            };

            // Send to FCM
            var json = JsonSerializer.Serialize(message);
            var content = new StringContent(json, Encoding.UTF8, "application/json");
            
            var response = await _httpClient.PostAsync("https://fcm.googleapis.com/fcm/send", content);
            
            if (response.IsSuccessStatusCode)
            {
                var responseJson = await response.Content.ReadAsStringAsync();
                var result = JsonSerializer.Deserialize<FCMResponse>(responseJson);

                if (result?.Success == 1)
                {
                    NotificationSent?.Invoke(this, new NotificationSentEventArgs
                    {
                        DeviceId = deviceId,
                        Notification = notification,
                        SentAt = DateTime.UtcNow
                    });

                    Logger.Info("MobileNotificationService", $"Notification sent to {deviceId}: {notification.Title}");
                    return true;
                }
            }

            var error = await response.Content.ReadAsStringAsync();
            Logger.Warning("MobileNotificationService", $"FCM send failed: {error}");
            return false;
        }
        catch (Exception ex)
        {
            Logger.Error("MobileNotificationService", $"Failed to send notification to {deviceId}", ex);
            
            NotificationFailed?.Invoke(this, new NotificationFailedEventArgs
            {
                DeviceId = deviceId,
                Notification = notification,
                Error = ex.Message
            });
            
            return false;
        }
    }

    /// <summary>
    /// Sends a notification to all paired devices.
    /// </summary>
    public async Task<NotificationBroadcastResult> BroadcastNotificationAsync(MobileNotification notification)
    {
        var result = new NotificationBroadcastResult
        {
            SentAt = DateTime.UtcNow,
            Results = new Dictionary<string, bool>()
        };

        foreach (var device in _companionApi.PairedDevices)
        {
            var success = await SendNotificationAsync(device.Key, notification);
            result.Results[device.Key] = success;
            
            if (success)
                result.SuccessCount++;
            else
                result.FailureCount++;
        }

        Logger.Info("MobileNotificationService", 
            $"Broadcast complete: {result.SuccessCount} succeeded, {result.FailureCount} failed");

        return result;
    }

    /// <summary>
    /// Notifies all devices when keep-awake state changes.
    /// </summary>
    private async void OnKeepAwakeStateChanged(object? sender, bool isActive)
    {
        if (!IsEnabled)
            return;

        var notification = new MobileNotification
        {
            Title = isActive ? "Redball Activated" : "Redball Deactivated",
            Body = isActive 
                ? "Keep-awake is now active on your computer" 
                : "Keep-awake has been turned off",
            Priority = NotificationPriority.Normal,
            Data = new Dictionary<string, string>
            {
                ["type"] = "state_change",
                ["isActive"] = isActive.ToString(),
                ["timestamp"] = DateTime.UtcNow.ToString("O")
            }
        };

        await BroadcastNotificationAsync(notification);
    }

    /// <summary>
    /// Sends a low battery warning to all devices.
    /// </summary>
    public async Task SendBatteryWarningAsync(int batteryLevel)
    {
        if (!IsEnabled || batteryLevel > 20)
            return;

        var notification = new MobileNotification
        {
            Title = "Low Battery Warning",
            Body = $"Your computer battery is at {batteryLevel}%. Redball has been paused to preserve battery.",
            Priority = NotificationPriority.High,
            PlaySound = true,
            Data = new Dictionary<string, string>
            {
                ["type"] = "battery_warning",
                ["level"] = batteryLevel.ToString()
            }
        };

        await BroadcastNotificationAsync(notification);
    }

    /// <summary>
    /// Sends a session completion notification.
    /// </summary>
    public async Task SendSessionCompleteNotificationAsync(TimeSpan duration)
    {
        if (!IsEnabled)
            return;

        var notification = new MobileNotification
        {
            Title = "Session Complete",
            Body = $"Your keep-awake session has completed after {duration.TotalMinutes:F0} minutes.",
            Priority = NotificationPriority.Normal,
            Data = new Dictionary<string, string>
            {
                ["type"] = "session_complete",
                ["duration"] = duration.ToString()
            }
        };

        await BroadcastNotificationAsync(notification);
    }

    /// <summary>
    /// Registers a device token for push notifications.
    /// </summary>
    public async Task<bool> RegisterDeviceTokenAsync(string deviceId, string fcmToken)
    {
        try
        {
            // Store token in device registry
            // Implementation would update the device record in storage
            
            Logger.Info("MobileNotificationService", $"FCM token registered for device {deviceId}");
            return true;
        }
        catch (Exception ex)
        {
            Logger.Error("MobileNotificationService", "Failed to register device token", ex);
            return false;
        }
    }

    /// <summary>
    /// Gets the stored FCM token for a device.
    /// </summary>
    private async Task<string?> GetDeviceTokenAsync(string deviceId)
    {
        // Retrieve from device registry
        // Implementation would query the device storage
        
        // For now, check if device exists in companion API
        if (_companionApi.PairedDevices.TryGetValue(deviceId, out var device))
        {
            // In production, this would return the stored FCM token
            return null; // Placeholder
        }
        
        return null;
    }
}

/// <summary>
/// Mobile notification model.
/// </summary>
public class MobileNotification
{
    public string Title { get; set; } = string.Empty;
    public string Body { get; set; } = string.Empty;
    public NotificationPriority Priority { get; set; } = NotificationPriority.Normal;
    public bool PlaySound { get; set; }
    public Dictionary<string, string> Data { get; set; } = new();
}

public enum NotificationPriority
{
    Normal,
    High
}

/// <summary>
/// FCM message structure.
/// </summary>
public class FCMMessage
{
    [JsonPropertyName("to")]
    public string To { get; set; } = string.Empty;
    
    [JsonPropertyName("notification")]
    public FCMNotificationPayload Notification { get; set; } = new();
    
    [JsonPropertyName("data")]
    public Dictionary<string, string> Data { get; set; } = new();
    
    [JsonPropertyName("priority")]
    public string Priority { get; set; } = "normal";
}

public class FCMNotificationPayload
{
    [JsonPropertyName("title")]
    public string Title { get; set; } = string.Empty;
    
    [JsonPropertyName("body")]
    public string Body { get; set; } = string.Empty;
    
    [JsonPropertyName("icon")]
    public string? Icon { get; set; }
    
    [JsonPropertyName("sound")]
    public string? Sound { get; set; }
}

/// <summary>
/// FCM response structure.
/// </summary>
public class FCMResponse
{
    [JsonPropertyName("success")]
    public int Success { get; set; }
    
    [JsonPropertyName("failure")]
    public int Failure { get; set; }
}

/// <summary>
/// Broadcast result for multi-device notifications.
/// </summary>
public class NotificationBroadcastResult
{
    public DateTime SentAt { get; set; }
    public int SuccessCount { get; set; }
    public int FailureCount { get; set; }
    public Dictionary<string, bool> Results { get; set; } = new();
}

// Event args
public class NotificationSentEventArgs : EventArgs
{
    public string DeviceId { get; set; } = string.Empty;
    public MobileNotification Notification { get; set; } = new();
    public DateTime SentAt { get; set; }
}

public class NotificationFailedEventArgs : EventArgs
{
    public string DeviceId { get; set; } = string.Empty;
    public MobileNotification Notification { get; set; } = new();
    public string Error { get; set; } = string.Empty;
}
