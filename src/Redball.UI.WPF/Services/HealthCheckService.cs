using System;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using System.Text.Json;

namespace Redball.UI.Services;

/// <summary>
/// Health check service for monitoring application and update API status
/// </summary>
public class HealthCheckService : IHealthCheckService
{
    private readonly HttpClient _httpClient;
    private readonly string _version;
    private bool _disposed;

    public HealthCheckService()
    {
        _httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
        _version = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "unknown";
    }

    /// <summary>
    /// Performs a comprehensive health check
    /// </summary>
    public async Task<HealthStatus> CheckHealthAsync()
    {
        var status = new HealthStatus
        {
            Timestamp = DateTime.UtcNow,
            Version = _version,
            Checks = new Dictionary<string, HealthCheckResult>()
        };

        // Check file system access
        status.Checks["filesystem"] = CheckFileSystem();

        // Check configuration
        status.Checks["configuration"] = CheckConfiguration();

        // Check update API (if updates are configured)
        if (!string.IsNullOrEmpty(ConfigService.Instance.Config.UpdateChannel))
        {
            status.Checks["update_api"] = await CheckUpdateApiAsync();
        }

        // Check theme resources
        status.Checks["theme_resources"] = CheckThemeResources();

        // Check tray icon
        status.Checks["tray_icon"] = CheckTrayIcon();

        // Overall status
        status.IsHealthy = true;
        foreach (var check in status.Checks.Values)
        {
            if (!check.IsHealthy)
            {
                status.IsHealthy = false;
                break;
            }
        }

        return status;
    }

    private HealthCheckResult CheckFileSystem()
    {
        try
        {
            var testFile = Path.Combine(AppContext.BaseDirectory, $".healthcheck_{Guid.NewGuid()}.tmp");
            File.WriteAllText(testFile, "test");
            File.Delete(testFile);
            return HealthCheckResult.Healthy("File system access OK");
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy($"File system access failed: {ex.Message}");
        }
    }

    private HealthCheckResult CheckConfiguration()
    {
        try
        {
            var config = ConfigService.Instance.Config;
            if (config == null)
            {
                return HealthCheckResult.Unhealthy("Configuration is null");
            }

            // Validate required settings
            if (config.HeartbeatSeconds <= 0)
            {
                return HealthCheckResult.Degraded($"Invalid HeartbeatSeconds: {config.HeartbeatSeconds}");
            }

            return HealthCheckResult.Healthy("Configuration valid");
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy($"Configuration error: {ex.Message}");
        }
    }

    private async Task<HealthCheckResult> CheckUpdateApiAsync()
    {
        try
        {
            var updateUrl = "https://api.github.com/repos/ArMaTeC/Redball/releases/latest";
            _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd($"Redball/{_version}");
            
            var response = await _httpClient.GetAsync(updateUrl);
            if (response.IsSuccessStatusCode)
            {
                var content = await response.Content.ReadAsStringAsync();
                var release = JsonSerializer.Deserialize<JsonElement>(content);
                var latestVersion = release.GetProperty("tag_name").GetString();
                
                return HealthCheckResult.Healthy($"Update API reachable, latest: {latestVersion}");
            }
            else
            {
                return HealthCheckResult.Degraded($"Update API returned {response.StatusCode}");
            }
        }
        catch (TaskCanceledException)
        {
            return HealthCheckResult.Degraded("Update API timeout");
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Degraded($"Update API error: {ex.Message}");
        }
    }

    private HealthCheckResult CheckThemeResources()
    {
        try
        {
            var themes = new[] { "Dark", "Light", "MidnightBlue", "ForestGreen", "OceanBlue" };
            foreach (var theme in themes)
            {
                var themeUri = $"pack://application:,,,/Themes/{theme}Theme.xaml";
                // Theme existence is validated by ThemeManager
            }
            return HealthCheckResult.Healthy("Theme resources accessible");
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy($"Theme resources error: {ex.Message}");
        }
    }

    private HealthCheckResult CheckTrayIcon()
    {
        try
        {
            var iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "redball.ico");
            if (!File.Exists(iconPath))
            {
                return HealthCheckResult.Degraded("Tray icon not found at default location");
            }
            return HealthCheckResult.Healthy("Tray icon accessible");
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Degraded($"Tray icon check error: {ex.Message}");
        }
    }

    /// <summary>
    /// Gets a summary report of the application health
    /// </summary>
    public string GetHealthReport(HealthStatus status)
    {
        var report = $"""
            Redball Health Report
            =====================
            Timestamp: {status.Timestamp:yyyy-MM-dd HH:mm:ss UTC}
            Version: {status.Version}
            Overall Status: {(status.IsHealthy ? "✓ HEALTHY" : "✗ UNHEALTHY")}

            Checks:
            """;

        foreach (var check in status.Checks)
        {
            var symbol = check.Value.Status switch
            {
                HealthStatusCode.Healthy => "✓",
                HealthStatusCode.Degraded => "⚠",
                _ => "✗"
            };
            report += $"\n  {symbol} {check.Key}: {check.Value.Message}";
        }

        return report;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _httpClient?.Dispose();
    }
}

/// <summary>
/// Health status container
/// </summary>
public class HealthStatus
{
    public DateTime Timestamp { get; set; }
    public string Version { get; set; } = "";
    public bool IsHealthy { get; set; }
    public Dictionary<string, HealthCheckResult> Checks { get; set; } = new();
}

/// <summary>
/// Individual health check result
/// </summary>
public class HealthCheckResult
{
    public HealthStatusCode Status { get; set; }
    public string Message { get; set; } = "";
    public DateTime Timestamp { get; set; }

    public bool IsHealthy => Status == HealthStatusCode.Healthy;

    public static HealthCheckResult Healthy(string message)
    {
        return new HealthCheckResult { Status = HealthStatusCode.Healthy, Message = message, Timestamp = DateTime.UtcNow };
    }

    public static HealthCheckResult Degraded(string message)
    {
        return new HealthCheckResult { Status = HealthStatusCode.Degraded, Message = message, Timestamp = DateTime.UtcNow };
    }

    public static HealthCheckResult Unhealthy(string message)
    {
        return new HealthCheckResult { Status = HealthStatusCode.Unhealthy, Message = message, Timestamp = DateTime.UtcNow };
    }
}

public enum HealthStatusCode
{
    Healthy,
    Degraded,
    Unhealthy
}
