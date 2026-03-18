using System;
using System.IO;
using System.Linq;
using System.Windows;
using Microsoft.Win32;
using Redball.UI.Services;

namespace Redball.UI.Views;

public partial class DiagnosticsWindow : Window
{
    private static readonly string AnalyticsPath = Path.Combine(AppContext.BaseDirectory, "analytics.json");
    private readonly AnalyticsService _analytics = new(ConfigService.Instance.Config.EnableTelemetry);

    public DiagnosticsWindow()
    {
        InitializeComponent();
        Loaded += DiagnosticsWindow_Loaded;
    }

    private void DiagnosticsWindow_Loaded(object sender, RoutedEventArgs e)
    {
        _analytics.TrackFeature("diagnostics.opened");
        LoadDiagnostics();
    }

    private void LoadDiagnostics()
    {
        var configService = ConfigService.Instance;
        var config = configService.Config;
        var validationErrors = configService.Validate();
        var keepAwake = KeepAwakeService.Instance;
        var analytics = new AnalyticsService(true);
        var analyticsSummary = analytics.GetSummary();
        var logPath = Logger.LogPath;
        var logDirectory = Logger.GetLogDirectory();
        var logSize = File.Exists(logPath) ? new FileInfo(logPath).Length : 0;
        var hasAnalyticsFile = File.Exists(AnalyticsPath);

        ConfigPathText.Text = $"Config Path: {configService.ConfigPath}";
        ValidationStatusText.Text = validationErrors.Count == 0
            ? "Validation: OK"
            : $"Validation: {validationErrors.Count} issue(s)";
        DirtyStateText.Text = $"Unsaved Changes: {configService.IsDirty}";
        UpdateStatusText.Text = $"Update Checks: Channel={config.UpdateChannel}, Repo={config.UpdateRepoOwner}/{config.UpdateRepoName}";

        LogPathText.Text = $"Log File: {logPath}";
        LogDirectoryText.Text = $"Log Directory: {logDirectory}";
        LogLevelText.Text = $"Log Level: {Logger.CurrentLogLevel}";
        LogSizeText.Text = $"Log Size: {logSize / 1024.0:F1} KB";

        KeepAwakeStatusText.Text = $"Keep Awake: {keepAwake.GetStatusText()}";
        HeartbeatText.Text = $"Heartbeat Seconds: {config.HeartbeatSeconds}";
        MonitorStatusText.Text = $"Monitors: Battery={config.BatteryAware}, Network={config.NetworkAware}, Idle={config.IdleDetection}, Schedule={config.ScheduleEnabled}";

        AnalyticsEnabledText.Text = $"Analytics Enabled: {config.EnableTelemetry}";
        AnalyticsSessionsText.Text = $"Total Sessions: {analyticsSummary.TotalSessions}";
        AnalyticsFeaturesText.Text = $"Tracked Features: {analyticsSummary.TotalFeatureEvents}";
        AnalyticsPathText.Text = $"Analytics File: {(hasAnalyticsFile ? AnalyticsPath : "Not found")}";

        ValidationIssuesText.Text = validationErrors.Count == 0
            ? "No validation issues detected."
            : string.Join(Environment.NewLine, validationErrors.Select((error, index) => $"{index + 1}. {error}"));

        UpdateChannelText.Text = $"Channel: {config.UpdateChannel}";
        UpdateRepoText.Text = $"Repository: {config.UpdateRepoOwner}/{config.UpdateRepoName}";
        UpdateVerificationText.Text = $"Signature Verification: {config.VerifyUpdateSignature}";

        if (File.Exists(logPath))
        {
            var recentLog = string.Join(Environment.NewLine, File.ReadLines(logPath).TakeLast(40));
            RecentLogText.Text = recentLog;
        }
        else
        {
            RecentLogText.Text = "No log file found.";
        }
    }

    private void RefreshButton_Click(object sender, RoutedEventArgs e)
    {
        _analytics.TrackFeature("diagnostics.refreshed");
        LoadDiagnostics();
    }

    private void OpenLogFolderButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            _analytics.TrackFeature("logs.opened");
            OpenPath(Logger.GetLogDirectory());
        }
        catch (Exception ex)
        {
            Logger.Error("DiagnosticsWindow", "Failed to open log folder", ex);
            MessageBox.Show($"Could not open log folder: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void OpenConfigButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            _analytics.TrackFeature("config.opened");
            OpenPath(ConfigService.Instance.ConfigPath);
        }
        catch (Exception ex)
        {
            Logger.Error("DiagnosticsWindow", "Failed to open config file", ex);
            MessageBox.Show($"Could not open config file: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void OpenAnalyticsButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (!File.Exists(AnalyticsPath))
            {
                _analytics.TrackFeature("analytics.opened_file_missing");
                MessageBox.Show("Analytics file not found yet.", "Analytics", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            _analytics.TrackFeature("analytics.opened_file");
            OpenPath(AnalyticsPath);
        }
        catch (Exception ex)
        {
            Logger.Error("DiagnosticsWindow", "Failed to open analytics file", ex);
            MessageBox.Show($"Could not open analytics file: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void ExportConfigButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var dialog = new SaveFileDialog
            {
                Filter = "JSON files (*.json)|*.json|All files (*.*)|*.*",
                FileName = $"redball_config_backup_{DateTime.Now:yyyyMMdd_HHmmss}.json",
                Title = "Export Configuration"
            };

            if (dialog.ShowDialog() == true)
            {
                var success = ConfigService.Instance.Export(dialog.FileName);
                _analytics.TrackFeature(success ? "config.exported" : "config.export_failed");
                MessageBox.Show(success ? $"Config exported to:\n{dialog.FileName}" : "Config export failed.", success ? "Export Complete" : "Export Failed", MessageBoxButton.OK, success ? MessageBoxImage.Information : MessageBoxImage.Error);
            }
        }
        catch (Exception ex)
        {
            Logger.Error("DiagnosticsWindow", "Failed to export config", ex);
            MessageBox.Show($"Config export failed: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void ExportDiagnosticsButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var dialog = new SaveFileDialog
            {
                Filter = "Text files (*.txt)|*.txt|All files (*.*)|*.*",
                FileName = $"redball_diagnostics_{DateTime.Now:yyyyMMdd_HHmmss}.txt",
                Title = "Export Diagnostics"
            };

            if (dialog.ShowDialog() == true)
            {
                var path = Logger.ExportDiagnostics(dialog.FileName);
                _analytics.TrackFeature("diagnostics.exported");
                MessageBox.Show($"Diagnostics exported to:\n{path}", "Export Complete", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }
        catch (Exception ex)
        {
            Logger.Error("DiagnosticsWindow", "Failed to export diagnostics", ex);
            MessageBox.Show($"Export failed: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private static void OpenPath(string path)
    {
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
        {
            FileName = path,
            UseShellExecute = true
        });
    }
}
