using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls.Primitives;
using Microsoft.Win32;
using Redball.UI.Services;

namespace Redball.UI.Views;

/// <summary>
/// Partial class: Navigation, section switching, dashboard content, and Show* methods.
/// </summary>
public partial class MainWindow
{
    private void LoadEmbeddedDashboardContent()
    {
        try
        {
            var summary = _analytics.GetSummary();
            var config = ConfigService.Instance.Config;
            var validationErrors = ConfigService.Instance.Validate();
            var keepAwake = KeepAwakeService.Instance;
            var logPath = Logger.LogPath;
            var analyticsPath = Path.Combine(AppContext.BaseDirectory, "analytics.json");
            var topAreas = summary.TopCategories.Take(5).ToList();

            AnalyticsSessionsText.Text = summary.TotalSessions.ToString();
            AnalyticsUsageText.Text = $"{summary.TotalUsageTime.TotalHours:F1}h";
            AnalyticsEventsText.Text = summary.TotalFeatureEvents.ToString();
            AnalyticsTopAreasText.Text = topAreas.Count == 0
                ? "No activity recorded yet."
                : string.Join(Environment.NewLine, topAreas.Select(area => $"{area.Name}: {area.Count} events"));
            AnalyticsTrendText.Text = $"Recent 7 days: {summary.RecentSessions} sessions | Prior 7 days: {summary.PriorSessions} | Trend: {summary.SessionTrendPercent:+0.0;-0.0;0.0}%";

            MetricsAdoptionText.Text = $"{summary.FeatureAdoptionRate:F0}%";
            MetricsRetentionText.Text = $"7d {summary.RetentionDay7:F0}% | 30d {summary.RetentionDay30:F0}%";
            MetricsTypeThingText.Text = $"Success rate: {summary.TypeThingSuccessRate:F0}%{Environment.NewLine}Completed: {summary.TypeThingCompletions} / {summary.TypeThingAttempts}";
            MetricsSettingsText.Text = $"Save success: {summary.SettingsSaveSuccessRate:F0}%{Environment.NewLine}Saved: {summary.SettingsSaves} / {summary.SettingsSaveAttempts}";
            MetricsUpdatesText.Text = $"Update success: {summary.UpdateSuccessRate:F0}%{Environment.NewLine}Succeeded: {summary.UpdateSuccesses} / {summary.UpdateAttempts}";
            MetricsOnboardingText.Text = $"Completion rate: {summary.OnboardingCompletionRate:F0}%{Environment.NewLine}Completed: {summary.OnboardingCompletions} / {summary.OnboardingStarts}";

            DiagnosticsConfigText.Text = $"Config Path: {ConfigService.Instance.ConfigPath}{Environment.NewLine}Validation: {(validationErrors.Count == 0 ? "OK" : $"{validationErrors.Count} issue(s)")}{Environment.NewLine}Update Channel: {config.UpdateChannel}";
            DiagnosticsRuntimeText.Text = $"Keep Awake: {keepAwake.GetStatusText()}{Environment.NewLine}Heartbeat Seconds: {config.HeartbeatSeconds}{Environment.NewLine}Battery Aware: {config.BatteryAware} | Network Aware: {config.NetworkAware}";
            DiagnosticsLoggingText.Text = $"Log File: {logPath}{Environment.NewLine}Directory: {Logger.GetLogDirectory()}{Environment.NewLine}Level: {Logger.CurrentLogLevel}";
            DiagnosticsAnalyticsText.Text = $"Analytics Enabled: {config.EnableTelemetry}{Environment.NewLine}Analytics File: {(File.Exists(analyticsPath) ? analyticsPath : "Not found")}{Environment.NewLine}Diagnostics Opens: {summary.DiagnosticsOpens} | Exports: {summary.DiagnosticsExports}";
            SessionStatsText.Text = SessionStatsService.Instance.GetSummaryText();
            DiagnosticsTempText.Text = TemperatureMonitorService.Instance.GetStatusText();

            if (File.Exists(logPath))
            {
                DiagnosticsRecentLogText.Text = string.Join(Environment.NewLine, File.ReadLines(logPath).TakeLast(30));
            }
            else
            {
                DiagnosticsRecentLogText.Text = "No log file found.";
            }

            LoadEmbeddedSettings();
        }
        catch (Exception ex)
        {
            Logger.Error("MainWindow", "Failed to load embedded dashboard content", ex);
            AnalyticsTopAreasText.Text = "Unable to load analytics summary.";
            AnalyticsTrendText.Text = "Unable to load session trend.";
            MetricsTypeThingText.Text = "Unable to load product metrics.";
            DiagnosticsRecentLogText.Text = "Unable to load diagnostics.";
        }
    }

    private string GetCurrentVersionText()
    {
        var version = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
        return $"Current Version: v{version?.Major}.{version?.Minor}.{version?.Build}";
    }

    private async void MainCheckForUpdatesButton_Click(object sender, RoutedEventArgs e)
    {
        await CheckForUpdatesAsync();
    }

    public async Task CheckForUpdatesAsync()
    {
        EnsureUpdateService();

        if (_updateService == null)
        {
            return;
        }

        _analytics.TrackFeature("update.manual_check");

        var updateInfo = await _updateService.CheckForUpdateAsync();
        if (updateInfo == null)
        {
            NotificationWindow.Show("Up to Date", "You're running the latest version of Redball.", "\uE73E"); 
            return;
        }

        // Show the full changelog dialog instead of jumping straight to download
        await ShowUpdateAvailableDialogAsync(updateInfo);
    }

    private void MainOpenLogFolderButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = Logger.GetLogDirectory(),
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            Logger.Error("MainWindow", "Failed to open log folder", ex);
            MessageBox.Show(this, $"Could not open log folder: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void MainExportDiagnosticsButton_Click(object sender, RoutedEventArgs e)
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
                MessageBox.Show(this, $"Diagnostics exported to:{Environment.NewLine}{path}", "Export Complete", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }
        catch (Exception ex)
        {
            Logger.Error("MainWindow", "Failed to export diagnostics", ex);
            NotificationWindow.Show("Error", $"Export failed: {ex.Message}", "\uE783");
        }
    }

    private void ShowSection(string section)
    {
        if (HomePanel == null ||
            AnalyticsPanel == null ||
            MetricsPanel == null ||
            DiagnosticsPanel == null ||
            SettingsPanel == null ||
            BehaviorPanel == null ||
            SmartFeaturesPanel == null ||
            TypeThingPanel == null ||
            PomodoroPanel == null ||
            UpdatesPanel == null)
        {
            return;
        }

        HomePanel.Visibility = section == "Home" ? Visibility.Visible : Visibility.Collapsed;
        AnalyticsPanel.Visibility = section == "Analytics" ? Visibility.Visible : Visibility.Collapsed;
        MetricsPanel.Visibility = section == "Metrics" ? Visibility.Visible : Visibility.Collapsed;
        DiagnosticsPanel.Visibility = section == "Diagnostics" ? Visibility.Visible : Visibility.Collapsed;
        SettingsPanel.Visibility = section == "Settings" ? Visibility.Visible : Visibility.Collapsed;
        BehaviorPanel.Visibility = section == "Behavior" ? Visibility.Visible : Visibility.Collapsed;
        SmartFeaturesPanel.Visibility = section == "SmartFeatures" ? Visibility.Visible : Visibility.Collapsed;
        TypeThingPanel.Visibility = section == "TypeThing" ? Visibility.Visible : Visibility.Collapsed;
        PomodoroPanel.Visibility = section == "Pomodoro" ? Visibility.Visible : Visibility.Collapsed;
        UpdatesPanel.Visibility = section == "Updates" ? Visibility.Visible : Visibility.Collapsed;

        if (HomeNavButton != null && section == "Home" && HomeNavButton.IsChecked != true) HomeNavButton.IsChecked = true;
        if (AnalyticsNavButton != null && section == "Analytics" && AnalyticsNavButton.IsChecked != true) AnalyticsNavButton.IsChecked = true;
        if (MetricsNavButton != null && section == "Metrics" && MetricsNavButton.IsChecked != true) MetricsNavButton.IsChecked = true;
        if (DiagnosticsNavButton != null && section == "Diagnostics" && DiagnosticsNavButton.IsChecked != true) DiagnosticsNavButton.IsChecked = true;
        if (SettingsNavButton != null && section == "Settings" && SettingsNavButton.IsChecked != true) SettingsNavButton.IsChecked = true;
        if (BehaviorNavButton != null && section == "Behavior" && BehaviorNavButton.IsChecked != true) BehaviorNavButton.IsChecked = true;
        if (SmartFeaturesNavButton != null && section == "SmartFeatures" && SmartFeaturesNavButton.IsChecked != true) SmartFeaturesNavButton.IsChecked = true;
        if (TypeThingNavButton != null && section == "TypeThing" && TypeThingNavButton.IsChecked != true) TypeThingNavButton.IsChecked = true;
        if (PomodoroNavButton != null && section == "Pomodoro" && PomodoroNavButton.IsChecked != true) PomodoroNavButton.IsChecked = true;
        if (UpdatesNavButton != null && section == "Updates" && UpdatesNavButton.IsChecked != true) UpdatesNavButton.IsChecked = true;
    }

    private void NavButton_Checked(object sender, RoutedEventArgs e)
    {
        if (HomeNavButton == null ||
            AnalyticsNavButton == null ||
            MetricsNavButton == null ||
            DiagnosticsNavButton == null ||
            SettingsNavButton == null ||
            BehaviorNavButton == null ||
            SmartFeaturesNavButton == null ||
            TypeThingNavButton == null ||
            PomodoroNavButton == null ||
            UpdatesNavButton == null)
        {
            return;
        }

        if (HomeNavButton.IsChecked == true) { ShowSection("Home"); return; }
        if (AnalyticsNavButton.IsChecked == true) { ShowSection("Analytics"); return; }
        if (MetricsNavButton.IsChecked == true) { ShowSection("Metrics"); return; }
        if (DiagnosticsNavButton.IsChecked == true) { ShowSection("Diagnostics"); return; }
        if (SettingsNavButton.IsChecked == true) { ShowSection("Settings"); return; }
        if (BehaviorNavButton.IsChecked == true) { ShowSection("Behavior"); return; }
        if (SmartFeaturesNavButton.IsChecked == true) { ShowSection("SmartFeatures"); return; }
        if (TypeThingNavButton.IsChecked == true) { ShowSection("TypeThing"); return; }
        if (PomodoroNavButton.IsChecked == true) { ShowSection("Pomodoro"); return; }
        if (UpdatesNavButton.IsChecked == true) { ShowSection("Updates"); }
    }

    private void AnalyticsExportCsv_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var dialog = new Microsoft.Win32.SaveFileDialog
            {
                Filter = "CSV files (*.csv)|*.csv|All files (*.*)|*.*",
                FileName = $"redball_analytics_{DateTime.Now:yyyyMMdd_HHmmss}.csv",
                Title = "Export Analytics as CSV"
            };
            if (dialog.ShowDialog() == true)
            {
                System.IO.File.WriteAllText(dialog.FileName, _analytics.ExportToCsv());
                _analytics.TrackFeature("analytics.exported_csv");
                MessageBox.Show(this, $"Analytics exported to:\n{dialog.FileName}", "Export Complete", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }
        catch (Exception ex)
        {
            Logger.Error("MainWindow", "Failed to export analytics CSV", ex);
            NotificationWindow.Show("Error", $"Export failed: {ex.Message}", "\uE783");
        }
    }

    private void AnalyticsExportJson_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var dialog = new Microsoft.Win32.SaveFileDialog
            {
                Filter = "JSON files (*.json)|*.json|All files (*.*)|*.*",
                FileName = $"redball_analytics_{DateTime.Now:yyyyMMdd_HHmmss}.json",
                Title = "Export Analytics as JSON"
            };
            if (dialog.ShowDialog() == true)
            {
                System.IO.File.WriteAllText(dialog.FileName, _analytics.Export());
                _analytics.TrackFeature("analytics.exported_json");
                MessageBox.Show(this, $"Analytics exported to:\n{dialog.FileName}", "Export Complete", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }
        catch (Exception ex)
        {
            Logger.Error("MainWindow", "Failed to export analytics JSON", ex);
            NotificationWindow.Show("Error", $"Export failed: {ex.Message}", "\uE783");
        }
    }

    private void ShowAnalyticsButton_Click(object sender, RoutedEventArgs e) => ShowAnalytics();
    private void ShowMetricsButton_Click(object sender, RoutedEventArgs e) => ShowMetrics();
    private void ShowDiagnosticsButton_Click(object sender, RoutedEventArgs e) => ShowDiagnostics();
    private void ShowSettingsButton_Click(object sender, RoutedEventArgs e) => ShowSettings();
    private void ShowBehaviorButton_Click(object sender, RoutedEventArgs e) => ShowBehavior();
    private void ShowSmartFeaturesButton_Click(object sender, RoutedEventArgs e) => ShowSmartFeatures();
    private void ShowTypeThingButton_Click(object sender, RoutedEventArgs e) => ShowTypeThing();
    private void ShowUpdatesButton_Click(object sender, RoutedEventArgs e) => ShowUpdates();

    public void ShowMainWindow()
    {
        Logger.Info("MainWindow", "ShowMainWindow called");
        Dispatcher.Invoke(() =>
        {
            ShowInTaskbar = true;
            if (!IsVisible) Show();
            WindowState = WindowState.Normal;
            Activate();
            Focus();
        });
    }

    public void ShowSettings()
    {
        Logger.Info("MainWindow", "ShowSettings called");
        _analytics.TrackFeature("settings.opened");
        Dispatcher.Invoke(() =>
        {
            ShowInTaskbar = true;
            if (!IsVisible) Show();
            WindowState = WindowState.Normal;
            LoadEmbeddedSettings();
            ShowSection("Settings");
            Activate();
            Focus();
        });
    }

    public void ShowBehavior()
    {
        Logger.Info("MainWindow", "ShowBehavior called");
        _analytics.TrackFeature("behavior.opened");
        Dispatcher.Invoke(() =>
        {
            ShowInTaskbar = true;
            if (!IsVisible) Show();
            WindowState = WindowState.Normal;
            LoadEmbeddedSettings();
            ShowSection("Behavior");
            Activate();
            Focus();
        });
    }

    public void ShowSmartFeatures()
    {
        Logger.Info("MainWindow", "ShowSmartFeatures called");
        _analytics.TrackFeature("smart_features.opened");
        Dispatcher.Invoke(() =>
        {
            ShowInTaskbar = true;
            if (!IsVisible) Show();
            WindowState = WindowState.Normal;
            LoadEmbeddedSettings();
            ShowSection("SmartFeatures");
            Activate();
            Focus();
        });
    }

    public void ShowTypeThing()
    {
        Logger.Info("MainWindow", "ShowTypeThing called");
        _analytics.TrackFeature("typething.opened");
        Dispatcher.Invoke(() =>
        {
            ShowInTaskbar = true;
            if (!IsVisible) Show();
            WindowState = WindowState.Normal;
            LoadEmbeddedSettings();
            ShowSection("TypeThing");
            Activate();
            Focus();
        });
    }

    public void ShowUpdates()
    {
        Logger.Info("MainWindow", "ShowUpdates called");
        _analytics.TrackFeature("updates.opened");
        Dispatcher.Invoke(() =>
        {
            ShowInTaskbar = true;
            if (!IsVisible) Show();
            WindowState = WindowState.Normal;
            LoadEmbeddedSettings();
            ShowSection("Updates");
            Activate();
            Focus();
        });
    }

    public void ShowPomodoro()
    {
        Logger.Info("MainWindow", "ShowPomodoro called");
        _analytics.TrackFeature("pomodoro.opened");
        Dispatcher.Invoke(() =>
        {
            ShowInTaskbar = true;
            if (!IsVisible) Show();
            WindowState = WindowState.Normal;
            ShowSection("Pomodoro");
            Activate();
            Focus();
        });
    }

    public void ShowAbout()
    {
        Logger.Info("MainWindow", "ShowAbout called");
        _analytics.TrackFeature("about.opened");
        Dispatcher.Invoke(() =>
        {
            if (_aboutWindow != null && _aboutWindow.IsLoaded)
            {
                Logger.Debug("MainWindow", "About window already open, activating");
                _aboutWindow.Activate();
                _aboutWindow.Focus();
                return;
            }
            Logger.Debug("MainWindow", "Creating new AboutWindow");
            _aboutWindow = new Views.AboutWindow();
            _aboutWindow.Closed += (s, e) =>
            {
                Logger.Debug("MainWindow", "About window closed");
                _aboutWindow = null;
            };
            _aboutWindow.Show();
            Logger.Info("MainWindow", "About window shown");
        });
    }

    public void ShowAnalytics()
    {
        Logger.Info("MainWindow", "ShowAnalytics called");
        _analytics.TrackFeature("analytics.opened");
        Dispatcher.Invoke(() =>
        {
            ShowInTaskbar = true;
            if (!IsVisible) Show();
            WindowState = WindowState.Normal;
            LoadEmbeddedDashboardContent();
            ShowSection("Analytics");
            Activate();
            Focus();
        });
    }

    public void ShowMetrics()
    {
        Logger.Info("MainWindow", "ShowMetrics called");
        _analytics.TrackFeature("metrics.opened");
        Dispatcher.Invoke(() =>
        {
            ShowInTaskbar = true;
            if (!IsVisible) Show();
            WindowState = WindowState.Normal;
            LoadEmbeddedDashboardContent();
            ShowSection("Metrics");
            Activate();
            Focus();
        });
    }

    public void ShowDiagnostics()
    {
        Logger.Info("MainWindow", "ShowDiagnostics called");
        _analytics.TrackFeature("diagnostics.opened");
        Dispatcher.Invoke(() =>
        {
            ShowInTaskbar = true;
            if (!IsVisible) Show();
            WindowState = WindowState.Normal;
            LoadEmbeddedDashboardContent();
            ShowSection("Diagnostics");
            Activate();
            Focus();
        });
    }

    public void OpenLogs()
    {
        Logger.Info("MainWindow", "OpenLogs called");
        _analytics.TrackFeature("logs.opened");

        try
        {
            var logPath = Logger.LogPath;
            var targetPath = File.Exists(logPath)
                ? logPath
                : (Path.GetDirectoryName(logPath) ?? AppContext.BaseDirectory);

            Process.Start(new ProcessStartInfo
            {
                FileName = targetPath,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            Logger.Error("MainWindow", "Failed to open logs", ex);
            NotificationService.Instance.ShowError("Redball", "Could not open logs.");
        }
    }
}
