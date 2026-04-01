using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Threading;
using Microsoft.Win32;
using Redball.UI.Services;

namespace Redball.UI.Views;

public partial class DiagnosticsWindow : Window
{
    private static readonly string AnalyticsPath = Path.Combine(AppContext.BaseDirectory, "analytics.json");
    private readonly AnalyticsService _analytics = AnalyticsService.Instance;

    // Memory leak detection: rolling window of working-set samples
    private static readonly List<(DateTime Time, long WorkingSetBytes)> _memorySamples = new();
    private const int MaxMemorySamples = 60;
    private DispatcherTimer? _memoryTimer;

    public DiagnosticsWindow()
    {
        InitializeComponent();
        Loaded += DiagnosticsWindow_Loaded;
        Closed += (_, _) => _memoryTimer?.Stop();
    }

    private void DiagnosticsWindow_Loaded(object sender, RoutedEventArgs e)
    {
        _analytics.TrackFeature("diagnostics.opened");
        TakeMemorySample();
        LoadDiagnostics();

        // Sample memory every 10 seconds while the window is open
        _memoryTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(10) };
        _memoryTimer.Tick += (_, _) => { TakeMemorySample(); UpdateMemoryHealth(); };
        _memoryTimer.Start();
    }

    private static void TakeMemorySample()
    {
        using var proc = Process.GetCurrentProcess();
        _memorySamples.Add((DateTime.Now, proc.WorkingSet64));
        if (_memorySamples.Count > MaxMemorySamples)
            _memorySamples.RemoveAt(0);
    }

    private void LoadDiagnostics()
    {
        var configService = ConfigService.Instance;
        var config = configService.Config;
        var validationErrors = configService.Validate();
        var keepAwake = KeepAwakeService.Instance;
        var analytics = AnalyticsService.Instance;
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

        UpdateMemoryHealth();
    }

    private void UpdateMemoryHealth()
    {
        using var proc = Process.GetCurrentProcess();
        var workingSetMb = proc.WorkingSet64 / 1024.0 / 1024.0;
        var gcTotalMb = GC.GetTotalMemory(false) / 1024.0 / 1024.0;

        WorkingSetText.Text = $"Working Set: {workingSetMb:F1} MB";
        GcTotalText.Text = $"GC Allocated: {gcTotalMb:F1} MB";
        GcGen0Text.Text = $"GC Gen 0 Collections: {GC.CollectionCount(0)}";
        GcGen1Text.Text = $"GC Gen 1 Collections: {GC.CollectionCount(1)}";
        GcGen2Text.Text = $"GC Gen 2 Collections: {GC.CollectionCount(2)}";

        // Detect memory growth trend from samples
        if (_memorySamples.Count >= 5)
        {
            var recent = _memorySamples.Skip(_memorySamples.Count - 5).Select(s => s.WorkingSetBytes).ToList();
            var isGrowing = true;
            for (int i = 1; i < recent.Count; i++)
            {
                if (recent[i] <= recent[i - 1]) { isGrowing = false; break; }
            }

            var firstMb = _memorySamples[0].WorkingSetBytes / 1024.0 / 1024.0;
            var lastMb = _memorySamples[^1].WorkingSetBytes / 1024.0 / 1024.0;
            var deltaPercent = firstMb > 0 ? ((lastMb - firstMb) / firstMb) * 100 : 0;

            if (isGrowing && deltaPercent > 10)
            {
                MemoryTrendText.Text = $"\u26A0 Memory growing: +{deltaPercent:F0}% ({firstMb:F1} \u2192 {lastMb:F1} MB)";
                MemoryTrendText.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(220, 50, 50));
            }
            else if (isGrowing)
            {
                MemoryTrendText.Text = $"Memory slightly growing: +{deltaPercent:F1}%";
                MemoryTrendText.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(200, 150, 0));
            }
            else
            {
                MemoryTrendText.Text = $"Memory stable ({deltaPercent:+0.0;-0.0}%)";
                MemoryTrendText.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0, 160, 0));
            }
        }
        else
        {
            MemoryTrendText.Text = $"Collecting samples ({_memorySamples.Count}/5)...";
            MemoryTrendText.Foreground = (System.Windows.Media.Brush)FindResource("ForegroundBrush");
        }

        // Display sample history
        var sampleLines = _memorySamples
            .Select(s => $"{s.Time:HH:mm:ss}  {s.WorkingSetBytes / 1024.0 / 1024.0:F1} MB")
            .TakeLast(15);
        MemorySamplesText.Text = string.Join(Environment.NewLine, sampleLines);
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
            NotificationWindow.Show("Error", $"Could not open log folder: {ex.Message}", "\uE783");
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
            NotificationWindow.Show("Error", $"Could not open config file: {ex.Message}", "\uE783");
        }
    }

    private void OpenAnalyticsButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (!File.Exists(AnalyticsPath))
            {
                _analytics.TrackFeature("analytics.opened_file_missing");
                NotificationWindow.Show("Analytics", "Analytics file not found yet.", "\uE946");
                return;
            }

            _analytics.TrackFeature("analytics.opened_file");
            OpenPath(AnalyticsPath);
        }
        catch (Exception ex)
        {
            Logger.Error("DiagnosticsWindow", "Failed to open analytics file", ex);
            NotificationWindow.Show("Error", $"Could not open analytics file: {ex.Message}", "\uE783");
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
                NotificationWindow.Show(
                    success ? "Export Complete" : "Export Failed",
                    success ? $"Config exported to:\n{dialog.FileName}" : "Config export failed.",
                    success ? "\uE73E" : "\uE783");
            }
        }
        catch (Exception ex)
        {
            Logger.Error("DiagnosticsWindow", "Failed to export config", ex);
            NotificationWindow.Show("Error", $"Config export failed: {ex.Message}", "\uE783");
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
                NotificationWindow.Show("Export Complete", $"Diagnostics exported to:\n{path}", "\uE73E");
            }
        }
        catch (Exception ex)
        {
            Logger.Error("DiagnosticsWindow", "Failed to export diagnostics", ex);
            NotificationWindow.Show("Error", $"Export failed: {ex.Message}", "\uE783");
        }
    }

    private void CopyAllButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var text = string.Join(Environment.NewLine, new[]
            {
                "=== Redball Diagnostics ===",
                $"Generated: {DateTime.Now:yyyy-MM-dd HH:mm:ss}",
                "",
                "--- Configuration ---",
                ConfigPathText.Text,
                ValidationStatusText.Text,
                DirtyStateText.Text,
                UpdateStatusText.Text,
                "",
                "--- Logging ---",
                LogPathText.Text,
                LogDirectoryText.Text,
                LogLevelText.Text,
                LogSizeText.Text,
                "",
                "--- Runtime ---",
                KeepAwakeStatusText.Text,
                HeartbeatText.Text,
                MonitorStatusText.Text,
                "",
                "--- Analytics ---",
                AnalyticsEnabledText.Text,
                AnalyticsSessionsText.Text,
                AnalyticsFeaturesText.Text,
                AnalyticsPathText.Text,
                "",
                "--- Update Health ---",
                UpdateChannelText.Text,
                UpdateRepoText.Text,
                UpdateVerificationText.Text,
                "",
                "--- Validation Issues ---",
                ValidationIssuesText.Text,
                "",
                "--- Recent Log Output ---",
                RecentLogText.Text
            });

            Clipboard.SetText(text);
            _analytics.TrackFeature("diagnostics.copied_all");
            Logger.Info("DiagnosticsWindow", "All diagnostics copied to clipboard");
        }
        catch (Exception ex)
        {
            Logger.Error("DiagnosticsWindow", "Failed to copy diagnostics to clipboard", ex);
            NotificationWindow.Show("Error", $"Failed to copy: {ex.Message}", "\uE783");
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
