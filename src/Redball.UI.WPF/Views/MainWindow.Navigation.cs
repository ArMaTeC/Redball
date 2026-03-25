using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media.Animation;
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
            MetricsPanel == null ||
            DiagnosticsPanel == null ||
            SettingsPanel == null ||
            BehaviorPanel == null ||
            SmartFeaturesPanel == null ||
            TypeThingPanel == null ||
            PomodoroPanel == null ||
            UpdatesPanel == null ||
            SecurityPanel == null)
        {
            return;
        }

        // Don't animate if we're already on this section
        if (_currentSection == section)
            return;

        // Get all panels
        var panels = new (string name, UIElement panel)[]
        {
            ("Home", HomePanel),
            ("Analytics", AnalyticsPanel),
            ("Metrics", MetricsPanel),
            ("Diagnostics", DiagnosticsPanel),
            ("Settings", SettingsPanel),
            ("Behavior", BehaviorPanel),
            ("SmartFeatures", SmartFeaturesPanel),
            ("TypeThing", TypeThingPanel),
            ("Pomodoro", PomodoroPanel),
            ("Updates", UpdatesPanel),
            ("Security", SecurityPanel)
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
        if (MetricsNavButton != null && section == "Metrics" && MetricsNavButton.IsChecked != true) MetricsNavButton.IsChecked = true;
        if (DiagnosticsNavButton != null && section == "Diagnostics" && DiagnosticsNavButton.IsChecked != true) DiagnosticsNavButton.IsChecked = true;
        if (SettingsNavButton != null && section == "Settings" && SettingsNavButton.IsChecked != true) SettingsNavButton.IsChecked = true;
        if (BehaviorNavButton != null && section == "Behavior" && BehaviorNavButton.IsChecked != true) BehaviorNavButton.IsChecked = true;
        if (SmartFeaturesNavButton != null && section == "SmartFeatures" && SmartFeaturesNavButton.IsChecked != true) SmartFeaturesNavButton.IsChecked = true;
        if (TypeThingNavButton != null && section == "TypeThing" && TypeThingNavButton.IsChecked != true) TypeThingNavButton.IsChecked = true;
        if (PomodoroNavButton != null && section == "Pomodoro" && PomodoroNavButton.IsChecked != true) PomodoroNavButton.IsChecked = true;
        if (UpdatesNavButton != null && section == "Updates" && UpdatesNavButton.IsChecked != true) UpdatesNavButton.IsChecked = true;
        if (SecurityNavButton != null && section == "Security" && SecurityNavButton.IsChecked != true) SecurityNavButton.IsChecked = true;
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
            UpdatesNavButton == null ||
            SecurityNavButton == null)
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
        if (UpdatesNavButton.IsChecked == true) { ShowSection("Updates"); return; }
        if (SecurityNavButton.IsChecked == true) { ShowSection("Security"); }
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
    private void ShowMetricsButton_Click(object sender, RoutedEventArgs e) => ShowMetrics();
    private void ShowDiagnosticsButton_Click(object sender, RoutedEventArgs e) => ShowDiagnostics();
    private void ShowSettingsButton_Click(object sender, RoutedEventArgs e) => ShowSettings();
    private void ShowBehaviorButton_Click(object sender, RoutedEventArgs e) => ShowBehavior();
    private void ShowSmartFeaturesButton_Click(object sender, RoutedEventArgs e) => ShowSmartFeatures();
    private void ShowTypeThingButton_Click(object sender, RoutedEventArgs e) => ShowTypeThing();
    private void ShowUpdatesButton_Click(object sender, RoutedEventArgs e) => ShowUpdates();
    private void ShowSecurityButton_Click(object sender, RoutedEventArgs e) => ShowSecurity();

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

    public void ShowSecurity()
    {
        Logger.Info("MainWindow", "ShowSecurity called");
        _analytics.TrackFeature("security.opened");
        Dispatcher.Invoke(() =>
        {
            ShowInTaskbar = true;
            if (!IsVisible) Show();
            WindowState = WindowState.Normal;
            LoadEmbeddedSettings();
            ShowSection("Security");
            UpdateSecurityStatus();
            Activate();
            Focus();
        });
    }

    private async void UpdateSecurityStatus()
    {
        try
        {
            var secrets = await SecretManagerService.Instance.ListSecretsAsync();
            SecuritySecretsStatusText.Text = secrets.Length == 0 
                ? "No secrets configured" 
                : $"{secrets.Length} secret(s) stored";
            
            var health = SecretManagerService.Instance.GetHealth();
            SecurityProviderStatusText.Text = health.PrimaryAvailable ? "Available" : "Unavailable";
            SecurityProviderStatusText.Foreground = health.PrimaryAvailable 
                ? System.Windows.Media.Brushes.Green 
                : System.Windows.Media.Brushes.Red;
            
            var endpointExists = await SecretManagerService.Instance.SecretExistsAsync(
                SecretManagerService.KnownSecrets.CloudAnalyticsEndpoint);
            SecurityAnalyticsStatusText.Text = endpointExists ? "Configured" : "Not configured";

            // Update tamper detection status
            var tamperCount = TamperPolicyService.Instance.GetUnresolvedCount();
            if (tamperCount == 0)
            {
                SecurityTamperCountText.Text = "No active tamper events";
                SecurityTamperCountText.Foreground = System.Windows.Media.Brushes.Green;
            }
            else
            {
                SecurityTamperCountText.Text = $"{tamperCount} tamper event(s) detected";
                SecurityTamperCountText.Foreground = System.Windows.Media.Brushes.Orange;
            }
            
            var policies = $"Config: {TamperPolicyService.Instance.ConfigTamperPolicy}, " +
                          $"Updates: {TamperPolicyService.Instance.UpdateSignaturePolicy}, " +
                          $"Cert: {TamperPolicyService.Instance.CertificatePinPolicy}";
            SecurityTamperPolicyText.Text = policies;

            // Update threat model status
            var threatSummary = ThreatModelService.Instance.GetSummary();
            SecurityThreatCountText.Text = $"{threatSummary.TotalThreats} threats ({threatSummary.MitigatedCount} mitigated)";
            
            if (threatSummary.UnmitigatedCount == 0)
            {
                SecurityThreatStatusText.Text = "All threats mitigated ✓";
                SecurityThreatStatusText.Foreground = System.Windows.Media.Brushes.Green;
            }
            else
            {
                var riskText = threatSummary.CriticalRiskCount > 0 ? $"{threatSummary.CriticalRiskCount} critical" :
                              threatSummary.HighRiskCount > 0 ? $"{threatSummary.HighRiskCount} high risk" :
                              $"{threatSummary.UnmitigatedCount} pending";
                SecurityThreatStatusText.Text = $"{riskText} unmitigated";
                SecurityThreatStatusText.Foreground = threatSummary.CriticalRiskCount > 0 ? System.Windows.Media.Brushes.Red :
                                                     threatSummary.HighRiskCount > 0 ? System.Windows.Media.Brushes.Orange :
                                                     System.Windows.Media.Brushes.Yellow;
            }
        }
        catch (Exception ex)
        {
            Logger.Error("MainWindow", "Failed to update security status", ex);
        }
    }

    private void SecurityExportThreatModel_Click(object sender, RoutedEventArgs e)
    {
        _analytics.TrackFeature("security.export_threat_model");
        
        try
        {
            var dialog = new Microsoft.Win32.SaveFileDialog
            {
                Filter = "Markdown files (*.md)|*.md|JSON files (*.json)|*.json|All files (*.*)|*.*",
                FileName = $"Redball_ThreatModel_{DateTime.Now:yyyyMMdd}.md",
                Title = "Export Threat Model Document"
            };

            if (dialog.ShowDialog() == true)
            {
                var ext = System.IO.Path.GetExtension(dialog.FileName).ToLowerInvariant();
                bool success = ext == ".json" 
                    ? ThreatModelService.Instance.ExportToJson(dialog.FileName)
                    : ThreatModelService.Instance.SaveMarkdownDocument(dialog.FileName);

                if (success)
                {
                    NotificationWindow.Show("Export Complete", $"Threat model exported to:\n{dialog.FileName}", "\uE73E");
                }
                else
                {
                    NotificationWindow.Show("Export Failed", "Could not export threat model document.", "\uE783");
                }
            }
        }
        catch (Exception ex)
        {
            Logger.Error("MainWindow", "Failed to export threat model", ex);
            NotificationWindow.Show("Export Failed", $"Error: {ex.Message}", "\uE783");
        }
    }

    private void SecurityViewTamperEvents_Click(object sender, RoutedEventArgs e)
    {
        _analytics.TrackFeature("security.view_tamper_events");
        
        var events = TamperPolicyService.Instance.GetTamperEvents(true);
        if (events.Count == 0)
        {
            NotificationWindow.Show("Tamper Detection", "No tamper events have been recorded.");
            return;
        }

        var message = string.Join("\n\n", events.Select(ev => 
            $"[{ev.DetectedAt:yyyy-MM-dd HH:mm}] {ev.Type}\n" +
            $"Status: {(ev.IsResolved ? "Resolved" : "Active")}\n" +
            $"{(ev.IsResolved ? $"Resolved: {ev.ResolutionAction}" : ev.Description)}"));

        NotificationWindow.Show("Tamper Events", message.Length > 500 ? message[..500] + "..." : message);
    }

    private void SecurityManageSecrets_Click(object sender, RoutedEventArgs e)
    {
        _analytics.TrackFeature("security.manage_secrets");
        var window = new SecretManagementWindow { Owner = this };
        window.ShowDialog();
        _ = UpdateSecurityStatus();
    }

    private async void SecurityTestAnalytics_Click(object sender, RoutedEventArgs e)
    {
        _analytics.TrackFeature("security.test_analytics");
        try
        {
            var endpoint = await SecretManagerService.Instance.GetSecretAsync(
                SecretManagerService.KnownSecrets.CloudAnalyticsEndpoint);
            if (string.IsNullOrEmpty(endpoint))
            {
                NotificationWindow.Show("Not Configured", "Cloud analytics endpoint is not configured in secure storage.", "\uE783");
                return;
            }
            NotificationWindow.Show("Configuration Found", $"Endpoint: {endpoint}", "\uE73E");
        }
        catch (Exception ex)
        {
            NotificationWindow.Show("Error", $"Failed to test connection: {ex.Message}", "\uE783");
        }
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

    private async void SecurityRunCIGates_Click(object sender, RoutedEventArgs e)
    {
        _analytics.TrackFeature("security.run_ci_gates");
        
        try
        {
            SecurityCIGatesStatusText.Text = "Running...";
            SecurityCIGatesStatusText.Foreground = System.Windows.Media.Brushes.Yellow;
            
            var sourceDir = AppContext.BaseDirectory;
            var results = await SecurityCIGatesService.Instance.RunAllGatesAsync(sourceDir);
            
            var passed = results.Count(r => r.Passed);
            var failed = results.Count(r => !r.Passed);
            var errors = results.Sum(r => r.Errors.Count);
            
            if (failed == 0)
            {
                SecurityCIGatesStatusText.Text = $"All gates passed ({passed}/{results.Count})";
                SecurityCIGatesStatusText.Foreground = System.Windows.Media.Brushes.Green;
                NotificationWindow.Show("CI Gates Complete", $"All {results.Count} security gates passed successfully!");
            }
            else
            {
                SecurityCIGatesStatusText.Text = $"{failed} gates failed, {errors} errors";
                SecurityCIGatesStatusText.Foreground = System.Windows.Media.Brushes.Red;
                NotificationWindow.Show("CI Gates Failed", $"{failed} gates failed with {errors} errors. Check logs for details.");
            }
            
            SecurityCIGatesDetailsText.Text = string.Join(" | ", results.Select(r => 
                $"{r.GateName}: {(r.Passed ? "✓" : "✗")}"));
        }
        catch (Exception ex)
        {
            Logger.Error("MainWindow", "Failed to run CI gates", ex);
            SecurityCIGatesStatusText.Text = "Error - see logs";
            SecurityCIGatesStatusText.Foreground = System.Windows.Media.Brushes.Red;
            NotificationWindow.Show("CI Gates Error", $"Error running gates: {ex.Message}");
        }
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

                case "Metrics":
                    ShowMetrics();
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

                case "Pomodoro":
                    ShowPomodoro();
                    break;

                case "Updates":
                    ShowUpdates();
                    break;

                case "Security":
                    ShowSecurity();
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
