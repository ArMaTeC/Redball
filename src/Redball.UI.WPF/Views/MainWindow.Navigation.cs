using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using Microsoft.Win32;
using Redball.UI.Services;

namespace Redball.UI.Views;

/// <summary>
/// Partial class: Navigation, section switching, dashboard content, and Show* methods.
/// </summary>
public partial class MainWindow
{
    // Track the currently visible section for animations
    private string? _currentSection;

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
        // Use the embedded UpdatesSectionView for integrated progress UI
        if (UpdatesPanel is UpdatesSectionView updatesSection)
        {
            await updatesSection.StartUpdateCheckFromExternalAsync();
        }
        else
        {
            await CheckForUpdatesAsync();
        }
    }

    public async Task CheckForUpdatesAsync()
    {
        Logger.Info("MainWindow", "CheckForUpdatesAsync manual trigger");
        
        // Switch to Updates section first to provide integrated experience
        ShowUpdates();

        // Small delay to allow UI to transition before heavy API call
        await Task.Delay(100);

        // If we have the embedded UI, use it to start the check
        if (UpdatesPanel is UpdatesSectionView updatesSection)
        {
            Logger.Debug("MainWindow", "Starting update check via UpdatesSectionView");
            await updatesSection.StartUpdateCheckFromExternalAsync();
        }
        else
        {
            Logger.Warning("MainWindow", "UpdatesPanel is NOT UpdatesSectionView or is null, cannot start check");
        }
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
            NotificationWindow.Show("Error", $"Could not open log folder: {ex.Message}", "\uE783");
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
                NotificationWindow.Show("Export Complete", $"Diagnostics exported to:\n{path}", "\uE73E");
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
            DiagnosticsPanel == null ||
            SettingsPanel == null ||
            BehaviorPanel == null ||
            SmartFeaturesPanel == null ||
            TypeThingPanel == null ||
            UpdatesPanel == null)
        {
            return;
        }

        // Don't animate if we're already on this section
        if (_currentSection == section)
        {
            Logger.Debug("MainWindow", $"Already in section {section}, skipping animation");
            return;
        }

        Logger.Info("MainWindow", $"Switching UI section to: {section}");

        // Get all panels
        var panels = new (string name, UIElement panel)[]
        {
            ("Home", HomePanel),
            ("Analytics", AnalyticsPanel),
            ("Diagnostics", DiagnosticsPanel),
            ("Settings", SettingsPanel),
            ("Behavior", BehaviorPanel),
            ("SmartFeatures", SmartFeaturesPanel),
            ("TypeThing", TypeThingPanel),
            ("Updates", UpdatesPanel)
        };

        // Get the target panel to show
        var targetPanel = panels.FirstOrDefault(p => p.name == section).panel;
        if (targetPanel == null) return;

        // Hide all panels with fade out animation
        foreach (var item in panels)
        {
            var panel = item.panel;
            if (panel.Visibility == Visibility.Visible)
            {
                // Apply fade out animation
                var fadeOut = new DoubleAnimation
                {
                    From = 1,
                    To = 0,
                    Duration = TimeSpan.FromMilliseconds(100),
                    EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseIn }
                };
                
                fadeOut.Completed += (s, e) =>
                {
                    panel.Visibility = Visibility.Collapsed;
                    panel.Opacity = 0;
                };
                
                panel.BeginAnimation(UIElement.OpacityProperty, fadeOut);
            }
            else
            {
                panel.Visibility = Visibility.Collapsed;
            }
        }

        // Show the target panel with fade in and slide animation
        targetPanel.Visibility = Visibility.Visible;
        targetPanel.Opacity = 0;

        // Apply transform for slide animation
        var transform = new System.Windows.Media.TranslateTransform(0, 15);
        targetPanel.RenderTransform = transform;

        // Fade in animation
        var fadeIn = new DoubleAnimation
        {
            From = 0,
            To = 1,
            Duration = TimeSpan.FromMilliseconds(250),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };

        // Slide in animation
        var slideIn = new DoubleAnimation
        {
            From = 15,
            To = 0,
            Duration = TimeSpan.FromMilliseconds(300),
            EasingFunction = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.2 }
        };

        // Apply animations
        targetPanel.BeginAnimation(UIElement.OpacityProperty, fadeIn);
        transform.BeginAnimation(System.Windows.Media.TranslateTransform.YProperty, slideIn);

        _currentSection = section;

        if (HomeNavButton != null && section == "Home" && HomeNavButton.IsChecked != true) HomeNavButton.IsChecked = true;
        if (AnalyticsNavButton != null && section == "Analytics" && AnalyticsNavButton.IsChecked != true) AnalyticsNavButton.IsChecked = true;
        if (DiagnosticsNavButton != null && section == "Diagnostics" && DiagnosticsNavButton.IsChecked != true) DiagnosticsNavButton.IsChecked = true;
        if (SettingsNavButton != null && section == "Settings" && SettingsNavButton.IsChecked != true) SettingsNavButton.IsChecked = true;
        if (BehaviorNavButton != null && section == "Behavior" && BehaviorNavButton.IsChecked != true) BehaviorNavButton.IsChecked = true;
        if (SmartFeaturesNavButton != null && section == "SmartFeatures" && SmartFeaturesNavButton.IsChecked != true) SmartFeaturesNavButton.IsChecked = true;
        if (TypeThingNavButton != null && section == "TypeThing" && TypeThingNavButton.IsChecked != true) TypeThingNavButton.IsChecked = true;
        if (UpdatesNavButton != null && section == "Updates" && UpdatesNavButton.IsChecked != true) UpdatesNavButton.IsChecked = true;
    }

    private void NavButton_Checked(object sender, RoutedEventArgs e)
    {
        if (HomeNavButton == null ||
            AnalyticsNavButton == null ||
            DiagnosticsNavButton == null ||
            SettingsNavButton == null ||
            BehaviorNavButton == null ||
            SmartFeaturesNavButton == null ||
            TypeThingNavButton == null ||
            UpdatesNavButton == null)
        {
            return;
        }

        if (sender == HomeNavButton) { ShowSection("Home"); return; }
        if (sender == AnalyticsNavButton) { LoadEmbeddedDashboardContent(); ShowSection("Analytics"); return; }
        if (sender == DiagnosticsNavButton) { LoadEmbeddedDashboardContent(); ShowSection("Diagnostics"); return; }
        if (sender == SettingsNavButton) { ShowSection("Settings"); return; }
        if (sender == BehaviorNavButton) { ShowSection("Behavior"); return; }
        if (sender == SmartFeaturesNavButton) { ShowSection("SmartFeatures"); return; }
        if (sender == TypeThingNavButton) { ShowSection("TypeThing"); return; }
        if (sender == UpdatesNavButton) { ShowSection("Updates"); }
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
                NotificationWindow.Show("Export Complete", $"Analytics exported to:\n{dialog.FileName}", "\uE73E");
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
                NotificationWindow.Show("Export Complete", $"Analytics exported to:\n{dialog.FileName}", "\uE73E");
            }
        }
        catch (Exception ex)
        {
            Logger.Error("MainWindow", "Failed to export analytics JSON", ex);
            NotificationWindow.Show("Error", $"Export failed: {ex.Message}", "\uE783");
        }
    }

    private void ShowAnalyticsButton_Click(object sender, RoutedEventArgs e) => ShowAnalytics();
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

    public void ShowMetrics()
    {
        ShowAnalytics();
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

            // Ensure UpdatesPanel is ready before showing
            if (UpdatesPanel is Views.UpdatesSectionView updatesSection)
            {
                updatesSection.Visibility = Visibility.Visible;
            }

            ShowSection("Updates");
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

    private void OpenLogsFolderButton_Click(object sender, RoutedEventArgs e)
    {
        OpenLogs();
    }

    /// <summary>
    /// Navigates to a specific section from the command palette.
    /// </summary>
    public void NavigateToSection(string sectionName)
    {
        Logger.Info("MainWindow", $"NavigateToSection called: {sectionName}");
        _analytics.TrackFeature($"navigate.{sectionName.ToLowerInvariant()}");

        Dispatcher.Invoke(() =>
        {
            ShowInTaskbar = true;
            if (!IsVisible) Show();
            WindowState = WindowState.Normal;
            Activate();

            switch (sectionName)
            {
                case "Dashboard":
                case "Home":
                    ShowSection("Home");
                    if (HomeNavButton != null) HomeNavButton.IsChecked = true;
                    break;

                case "Settings":
                    ShowSettings();
                    break;

                case "MiniWidgetSettings":
                    ShowSettings();
                    // Could scroll to mini widget section if needed
                    break;

                case "Analytics":
                    ShowAnalytics();
                    break;

                case "Diagnostics":
                    ShowDiagnostics();
                    break;

                case "Behavior":
                    ShowBehavior();
                    break;

                case "SmartFeatures":
                    ShowSmartFeatures();
                    break;

                case "TypeThing":
                    ShowTypeThing();
                    break;

                case "Updates":
                    ShowUpdates();
                    break;

                case "SyncHealth":
                    ShowDiagnostics();
                    // Could navigate to specific sync health tab if implemented
                    break;

                case "CrashTelemetry":
                    ShowDiagnostics();
                    // Could navigate to specific telemetry tab if implemented
                    break;

                default:
                    Logger.Warning("MainWindow", $"Unknown section: {sectionName}");
                    ShowSection("Home");
                    break;
            }

            Focus();
        });
    }
}
