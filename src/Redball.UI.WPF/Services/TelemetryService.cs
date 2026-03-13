using System;

namespace Redball.UI.Services;

/// <summary>
/// Opt-in telemetry event logging.
/// Port of Write-RedballTelemetryEvent.
/// Currently logs events locally; future versions may send to analytics endpoint.
/// </summary>
public static class TelemetryService
{
    /// <summary>
    /// Logs a telemetry event if telemetry is enabled in config.
    /// </summary>
    public static void LogEvent(string eventName, object? data = null)
    {
        if (!ConfigService.Instance.Config.EnableTelemetry) return;

        try
        {
            var dataStr = data != null ? System.Text.Json.JsonSerializer.Serialize(data) : "";
            Logger.Info("Telemetry", $"Event: {eventName} {dataStr}");
        }
        catch (Exception ex)
        {
            Logger.Debug("Telemetry", $"Telemetry event failed: {ex.Message}");
        }
    }
}
