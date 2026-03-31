using System;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Text.Json;

namespace Redball.UI.Services;

/// <summary>
/// GDPR-style data export: bundles config, analytics, logs, and session state
/// into a single downloadable ZIP archive.
/// </summary>
public static class DataExportService
{
    private static readonly string ExportTempDir = Path.Combine(Path.GetTempPath(), "RedballDataExport");

    /// <summary>
    /// Exports all user data to a ZIP file at the specified path.
    /// Returns true on success, false on failure.
    /// </summary>
    public static bool ExportAll(string destinationZipPath)
    {
        try
        {
            Logger.Info("DataExportService", $"Starting GDPR data export to: {destinationZipPath}");

            // Clean up any stale export temp dir
            if (Directory.Exists(ExportTempDir))
                Directory.Delete(ExportTempDir, true);
            Directory.CreateDirectory(ExportTempDir);

            // Gather all data components
            ExportConfig();
            ExportAnalytics();
            ExportSessionState();
            ExportLogs();
            ExportMetadata();

            // Create ZIP
            if (File.Exists(destinationZipPath))
                File.Delete(destinationZipPath);

            ZipFile.CreateFromDirectory(ExportTempDir, destinationZipPath, CompressionLevel.Optimal, false);

            // Cleanup temp
            Directory.Delete(ExportTempDir, true);

            Logger.Info("DataExportService", $"Export complete: {destinationZipPath}");
            return true;
        }
        catch (Exception ex)
        {
            Logger.Error("DataExportService", "Export failed", ex);
            return false;
        }
    }

    private static void ExportConfig()
    {
        var configPath = ConfigService.Instance.ConfigPath;
        var target = Path.Combine(ExportTempDir, "config.json");
        if (File.Exists(configPath))
        {
            File.Copy(configPath, target, true);
            Logger.Debug("DataExportService", "Exported config");
        }
        else
        {
            // Export current in-memory config as fallback
            var json = JsonSerializer.Serialize(ConfigService.Instance.Config, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(target, json);
        }
    }

    private static void ExportAnalytics()
    {
        var analyticsPath = Path.Combine(AppContext.BaseDirectory, "analytics.json");
        var target = Path.Combine(ExportTempDir, "analytics.json");
        if (File.Exists(analyticsPath))
        {
            File.Copy(analyticsPath, target, true);
            Logger.Debug("DataExportService", "Exported analytics");
        }
    }

    private static void ExportSessionState()
    {
        var statePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Redball", "UserData", "Redball.state.json");
        var target = Path.Combine(ExportTempDir, "session_state.json");
        if (File.Exists(statePath))
        {
            File.Copy(statePath, target, true);
            Logger.Debug("DataExportService", "Exported session state");
        }
    }

    private static void ExportLogs()
    {
        var logDir = Logger.GetLogDirectory();
        var logTargetDir = Path.Combine(ExportTempDir, "logs");
        Directory.CreateDirectory(logTargetDir);

        if (Directory.Exists(logDir))
        {
            foreach (var logFile in Directory.GetFiles(logDir, "*.log"))
            {
                var fileName = Path.GetFileName(logFile);
                var target = Path.Combine(logTargetDir, fileName);
                try
                {
                    File.Copy(logFile, target, true);
                }
                catch (IOException ex)
                {
                    Logger.Debug("DataExportService", $"Could not copy log {fileName}: {ex.Message}");
                }
            }
            Logger.Debug("DataExportService", "Exported logs");
        }
    }

    private static void ExportMetadata()
    {
        var meta = new
        {
            ExportedAt = DateTime.UtcNow,
            Version = GetAppVersion(),
            Machine = Environment.MachineName,
            User = Environment.UserName,
            Framework = Environment.Version.ToString(),
            OS = Environment.OSVersion.ToString()
        };
        var json = JsonSerializer.Serialize(meta, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(Path.Combine(ExportTempDir, "export_metadata.json"), json);
    }

    private static string GetAppVersion()
    {
        try
        {
            return System.Reflection.Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "unknown";
        }
        catch (Exception ex)
        {
            Logger.Debug("DataExportService", $"Failed to get app version: {ex.Message}");
            return "unknown";
        }
    }
}
