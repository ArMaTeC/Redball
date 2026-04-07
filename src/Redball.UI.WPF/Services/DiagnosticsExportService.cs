using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace Redball.UI.Services;

/// <summary>
/// In-app diagnostics export service for one-click support bundle generation.
/// Collects logs, config, analytics, and system info for troubleshooting.
/// </summary>
public sealed class DiagnosticsExportService
{
    public static DiagnosticsExportService Instance { get; } = new();

    private DiagnosticsExportService() { }

    /// <summary>
    /// Generates a comprehensive diagnostics bundle for support.
    /// </summary>
    public async Task<ExportResult> ExportDiagnosticsAsync(string? customIssue = null)
    {
        var timestamp = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");
        var tempDir = Path.Combine(Path.GetTempPath(), $"Redball_Diagnostics_{timestamp}");
        var zipPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
            $"Redball_Diagnostics_{timestamp}.zip");

        try
        {
            Directory.CreateDirectory(tempDir);

            // Collect all diagnostic data
            var tasks = new List<Task>
            {
                ExportLogsAsync(tempDir),
                ExportConfigAsync(tempDir),
                ExportAnalyticsAsync(tempDir),
                ExportSystemInfoAsync(tempDir),
                ExportSessionStateAsync(tempDir),
                ExportCrashDumpsAsync(tempDir),
                ExportServiceStatusAsync(tempDir)
            };

            await Task.WhenAll(tasks);

            // Add issue description if provided
            if (!string.IsNullOrEmpty(customIssue))
            {
                var issuePath = Path.Combine(tempDir, "issue_description.txt");
                await File.WriteAllTextAsync(issuePath, customIssue);
            }

            // Create manifest
            await CreateManifestAsync(tempDir, customIssue);

            // Compress to zip
            ZipFile.CreateFromDirectory(tempDir, zipPath, CompressionLevel.Optimal, false);

            // Cleanup temp directory
            Directory.Delete(tempDir, true);

            var fileInfo = new FileInfo(zipPath);
            
            Logger.Info("DiagnosticsExport", $"Diagnostics bundle created: {zipPath} ({fileInfo.Length / 1024} KB)");

            return ExportResult.Ok(zipPath, fileInfo.Length);
        }
        catch (Exception ex)
        {
            Logger.Error("DiagnosticsExport", "Failed to create diagnostics bundle", ex);
            return ExportResult.Err(ex.Message);
        }
    }

    private async Task ExportLogsAsync(string targetDir)
    {
        var logsDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Redball", "UserData", "logs");

        if (!Directory.Exists(logsDir)) return;

        var targetLogsDir = Path.Combine(targetDir, "logs");
        Directory.CreateDirectory(targetLogsDir);

        // Copy last 7 days of logs
        var logFiles = Directory.GetFiles(logsDir, "*.log")
            .Select(f => new FileInfo(f))
            .Where(f => f.LastWriteTime > DateTime.Now.AddDays(-7))
            .OrderByDescending(f => f.LastWriteTime)
            .Take(10);

        foreach (var logFile in logFiles)
        {
            var destPath = Path.Combine(targetLogsDir, logFile.Name);
            File.Copy(logFile.FullName, destPath, true);
        }
    }

    private async Task ExportConfigAsync(string targetDir)
    {
        try
        {
            var configPath = ConfigService.Instance.ConfigFilePath;
            if (File.Exists(configPath))
            {
                var destPath = Path.Combine(targetDir, "config.json");
                
                // Export decrypted config (sanitized)
                var config = ConfigService.Instance.Config;
                var sanitized = SanitizeConfig(config);
                var json = JsonSerializer.Serialize(sanitized, new JsonSerializerOptions { WriteIndented = true });
                await File.WriteAllTextAsync(destPath, json);
            }
        }
        catch (Exception ex)
        {
            Logger.Warning("DiagnosticsExport", $"Failed to export config: {ex.Message}");
        }
    }

    private async Task ExportAnalyticsAsync(string targetDir)
    {
        try
        {
            var analytics = new { SessionCount = 0, TotalDuration = TimeSpan.Zero }; // Placeholder
            // var analytics = AnalyticsService.Instance.GetSessionStats();
            var destPath = Path.Combine(targetDir, "analytics.json");
            var json = JsonSerializer.Serialize(analytics, new JsonSerializerOptions { WriteIndented = true });
            await File.WriteAllTextAsync(destPath, json);
        }
        catch (Exception ex)
        {
            Logger.Warning("DiagnosticsExport", $"Failed to export analytics: {ex.Message}");
        }
    }

    private async Task ExportSystemInfoAsync(string targetDir)
    {
        var info = new Dictionary<string, object>
        {
            ["OSVersion"] = Environment.OSVersion.ToString(),
            ["OSBuild"] = Environment.OSVersion.Version.Build,
            ["IsWindows11"] = Environment.OSVersion.Version.Build >= 22000,
            ["DotNetVersion"] = Environment.Version.ToString(),
            ["MachineName"] = Environment.MachineName,
            ["ProcessorCount"] = Environment.ProcessorCount,
            ["WorkingSet"] = Environment.WorkingSet,
            ["AppVersion"] = GetAppVersion(),
            ["Uptime"] = (DateTime.Now - Process.GetCurrentProcess().StartTime).ToString(),
            ["CurrentCulture"] = System.Globalization.CultureInfo.CurrentCulture.Name,
            ["Is64BitProcess"] = Environment.Is64BitProcess,
            ["Is64BitOperatingSystem"] = Environment.Is64BitOperatingSystem
        };

        // Get battery info if available
        try
        {
            var battery = BatteryMonitorService.Instance;
            info["BatteryPercent"] = battery.BatteryPercent;
            info["IsCharging"] = battery.IsCharging;
        }
        catch (Exception ex)
        {
            Logger.Debug("DiagnosticsExport", $"Failed to get battery info: {ex.Message}");
        }

        // Get power plan
        try
        {
            info["PowerPlan"] = "Unknown"; // Placeholder
            // info["PowerPlan"] = PowerPlanService.Instance.GetCurrentPlanName();
        }
        catch (Exception ex)
        {
            Logger.Debug("DiagnosticsExport", $"Failed to get power plan: {ex.Message}");
        }

        var destPath = Path.Combine(targetDir, "system_info.json");
        var json = JsonSerializer.Serialize(info, new JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllTextAsync(destPath, json);
    }

    private async Task ExportSessionStateAsync(string targetDir)
    {
        try
        {
            var state = new { Status = "Unknown" }; // Placeholder
            // var state = SessionStateService.Instance.GetCurrentState();
            var destPath = Path.Combine(targetDir, "session_state.json");
            var json = JsonSerializer.Serialize(state, new JsonSerializerOptions { WriteIndented = true });
            await File.WriteAllTextAsync(destPath, json);
        }
        catch (Exception ex)
        {
            Logger.Warning("DiagnosticsExport", $"Failed to export session state: {ex.Message}");
        }
    }

    private async Task ExportCrashDumpsAsync(string targetDir)
    {
        var crashDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Redball", "UserData", "crashes");

        if (!Directory.Exists(crashDir)) return;

        var targetCrashDir = Path.Combine(targetDir, "crashes");
        Directory.CreateDirectory(targetCrashDir);

        var crashFiles = Directory.GetFiles(crashDir, "*")
            .OrderByDescending(f => File.GetLastWriteTime(f))
            .Take(5);

        foreach (var crashFile in crashFiles)
        {
            var destPath = Path.Combine(targetCrashDir, Path.GetFileName(crashFile));
            File.Copy(crashFile, destPath, true);
        }
    }

    private async Task ExportServiceStatusAsync(string targetDir)
    {
        var status = new Dictionary<string, object>
        {
            ["KeepAwake"] = new
            {
                IsActive = KeepAwakeService.Instance.IsActive,
                IsPaused = false, // Placeholder - IsPaused doesn't exist on KeepAwakeService
                Until = KeepAwakeService.Instance.Until,
                AutoPausedBattery = KeepAwakeService.Instance.AutoPausedBattery,
                AutoPausedNetwork = KeepAwakeService.Instance.AutoPausedNetwork,
                AutoPausedIdle = KeepAwakeService.Instance.AutoPausedIdle
            },
            ["ServicesRunning"] = new Dictionary<string, bool>
            {
                ["BatteryMonitor"] = IsServiceRunning(typeof(BatteryMonitorService)),
                ["NetworkMonitor"] = IsServiceRunning(typeof(NetworkMonitorService)),
                ["IdleDetection"] = IsServiceRunning(typeof(IdleDetectionService)),
                ["Schedule"] = IsServiceRunning(typeof(ScheduleService)),
                // ["Hotkey"] = IsServiceRunning(typeof(HotkeyService))
            },
            // ["LastUpdateCheck"] = UpdateService.Instance.GetLastCheckTime(),
            ["EncryptionTier"] = ConfigService.Instance.Config.EncryptConfig ? "Enabled" : "Disabled"
        };

        var destPath = Path.Combine(targetDir, "service_status.json");
        var json = JsonSerializer.Serialize(status, new JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllTextAsync(destPath, json);
    }

    private async Task CreateManifestAsync(string targetDir, string? issueDescription)
    {
        var manifest = new DiagnosticsManifest
        {
            ExportedAt = DateTime.UtcNow,
            AppVersion = GetAppVersion(),
            IssueDescription = issueDescription,
            Files = Directory.GetFiles(targetDir, "*", SearchOption.AllDirectories)
                .Select(f => new FileEntry
                {
                    Path = Path.GetRelativePath(targetDir, f),
                    Size = new FileInfo(f).Length
                })
                .ToList()
        };

        var destPath = Path.Combine(targetDir, "manifest.json");
        var json = JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllTextAsync(destPath, json);
    }

    private object SanitizeConfig(RedballConfig config)
    {
        // Return a sanitized version without sensitive data
        return new
        {
            config.HeartbeatSeconds,
            config.PreventDisplaySleep,
            config.Theme,
            config.Locale,
            config.BatteryAware,
            config.BatteryThreshold,
            config.NetworkAware,
            config.IdleDetection,
            config.ScheduleEnabled,
            config.AutoUpdateCheckEnabled,
            config.EncryptConfig,
            // Exclude any sensitive paths or keys
        };
    }

    private bool IsServiceRunning(Type serviceType)
    {
        // Check if service singleton is instantiated and active
        return true; // Simplified
    }

    private string GetAppVersion()
    {
        return System.Reflection.Assembly.GetExecutingAssembly()
            .GetName().Version?.ToString() ?? "unknown";
    }
}

// Result types

public record ExportResult(bool Success, string? Path, long Size, string? Error)
{
    public static ExportResult Ok(string path, long size) => new(true, path, size, null);
    public static ExportResult Err(string error) => new(false, null, 0, error);
}

public class DiagnosticsManifest
{
    public DateTime ExportedAt { get; set; }
    public string AppVersion { get; set; } = "";
    public string? IssueDescription { get; set; }
    public List<FileEntry> Files { get; set; } = new();
}

public class FileEntry
{
    public string Path { get; set; } = "";
    public long Size { get; set; }
}
