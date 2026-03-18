using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Threading;
using Hardcodet.Wpf.TaskbarNotification;
using Microsoft.Win32;
using Redball.UI.Services;

namespace Redball.UI.Views;

/// <summary>
/// Main window for Redball v3.0 WPF UI
/// Primarily a tray-only application with optional window interface
/// </summary>
public partial class MainWindow : Window
{
    private ViewModels.MainViewModel? _viewModel;
    private TaskbarIcon? _trayIcon;
    private bool _isTrayIconInitialized;
    private DispatcherTimer? _trayIconRefreshTimer;
    private uint _taskbarCreatedMsg;
    private readonly AnalyticsService _analytics = new(ConfigService.Instance.Config.EnableTelemetry);
    private readonly Random _random = new();
    private UpdateService? _updateService;

    private Views.SettingsWindow? _settingsWindow;
    private Views.AboutWindow? _aboutWindow;
    private Views.AnalyticsDashboard? _analyticsWindow;
    private Views.ProductMetricsDashboard? _metricsWindow;
    private Views.DiagnosticsWindow? _diagnosticsWindow;
    private HotkeyService? _hotkeyService;
    private bool _isTyping;
    private bool _isLoadingSettings;
    private DispatcherTimer? _typeThingCountdownTimer;
    private DispatcherTimer? _typeThingTimer;

    public MainWindow()
    {
        Logger.Info("MainWindow", "Constructor called");
        InitializeComponent();
        _taskbarCreatedMsg = RegisterWindowMessage("TaskbarCreated");
        Logger.Debug("MainWindow", $"TaskbarCreated message ID: {_taskbarCreatedMsg}");
        Loaded += OnWindowLoaded;
        Logger.Debug("MainWindow", "Constructor completed");
    }

    private void OnWindowLoaded(object sender, RoutedEventArgs e)
    {
        Logger.Info("MainWindow", "Window loaded, initializing services...");
        SyncWindowChromeButtons();
        try
        {
            // Hook window messages for taskbar recreation
            var hwndSource = PresentationSource.FromVisual(this) as HwndSource;
            if (hwndSource != null)
            {
                hwndSource.AddHook(WndProc);
                Logger.Debug("MainWindow", "Window message hook added for tray icon recovery");
            }

            // Set DataContext here instead of XAML to prevent constructor issues during parsing
            if (DataContext == null)
            {
                Logger.Debug("MainWindow", "Creating new MainViewModel...");
                DataContext = new ViewModels.MainViewModel();
                Logger.Debug("MainWindow", "DataContext set to new MainViewModel");
            }

            _viewModel = DataContext as ViewModels.MainViewModel;
            if (_viewModel == null)
            {
                Logger.Error("MainWindow", "ERROR: DataContext is not MainViewModel");
                return;
            }

            // Connect ViewModel to this window for proper command delegation
            _viewModel.SetMainWindow(this);
            Logger.Debug("MainWindow", "ViewModel connected to window");

            SetupTrayIcon();
            SetupTrayIconRefreshTimer();
            SetupGlobalHotkeys();
            LoadEmbeddedDashboardContent();
            Logger.Info("MainWindow", "Initialization complete");
        }
        catch (Exception ex)
        {
            Logger.Error("MainWindow", "Failed during window initialization", ex);
        }
    }

    protected override void OnStateChanged(EventArgs e)
    {
        base.OnStateChanged(e);
        SyncWindowChromeButtons();
    }

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
        if (_updateService == null)
        {
            LoadEmbeddedSettings();
        }

        if (_updateService == null)
        {
            return;
        }

        var checkingDialog = MessageBox.Show("Checking for updates...", "Update", MessageBoxButton.OKCancel, MessageBoxImage.Information);
        if (checkingDialog != MessageBoxResult.OK)
        {
            return;
        }

        var updateInfo = await _updateService.CheckForUpdateAsync();
        if (updateInfo == null)
        {
            MessageBox.Show(this, "You're running the latest version.", "No Updates", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var progressWindow = new UpdateProgressWindow();
        progressWindow.Show();
        var progress = new Progress<int>(percent => progressWindow.SetProgress(percent));
        var success = await _updateService.DownloadAndInstallAsync(updateInfo, progress);
        progressWindow.Close();

        if (success)
        {
            Application.Current.Shutdown();
            return;
        }

        MessageBox.Show(this, "Failed to download or install the update. Please try again later or download manually from GitHub.", "Update Failed", MessageBoxButton.OK, MessageBoxImage.Error);
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
            MessageBox.Show(this, $"Export failed: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void MainShowNotificationsCheck_Checked(object sender, RoutedEventArgs e)
    {
        MainNotificationModeLabel.Visibility = Visibility.Visible;
        MainNotificationModeCombo.Visibility = Visibility.Visible;
        AutoApplySettings();
    }

    private void MainShowNotificationsCheck_Unchecked(object sender, RoutedEventArgs e)
    {
        MainNotificationModeLabel.Visibility = Visibility.Collapsed;
        MainNotificationModeCombo.Visibility = Visibility.Collapsed;
        AutoApplySettings();
    }

    private void MainBatteryThresholdSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (MainBatteryThresholdText != null)
        {
            MainBatteryThresholdText.Text = $"Threshold: {(int)e.NewValue}%";
        }
        AutoApplySettings();
    }

    private void MainIdleThresholdSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (MainIdleThresholdText != null)
        {
            MainIdleThresholdText.Text = $"Threshold: {(int)e.NewValue} minutes";
        }
        AutoApplySettings();
    }

    private void MainDurationSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (MainDurationText != null)
        {
            MainDurationText.Text = $"Duration: {(int)e.NewValue} minutes";
        }
        AutoApplySettings();
    }

    private void MainTypingSpeedSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (MainTypingSpeedText != null)
        {
            MainTypingSpeedText.Text = $"Speed: {(int)e.NewValue} ms per character";
        }
        AutoApplySettings();
    }

    private void MainMaxLogSizeSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (MainMaxLogSizeText != null)
        {
            MainMaxLogSizeText.Text = $"Max log size: {(int)e.NewValue} MB";
        }
        AutoApplySettings();
    }

    private void MainSettingChanged(object sender, RoutedEventArgs e)
    {
        AutoApplySettings();
    }

    private void MainComboSettingChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        AutoApplySettings();
    }

    private void MainHotkeyBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        e.Handled = true;
        if (sender is not System.Windows.Controls.TextBox textBox)
        {
            return;
        }

        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (key == Key.LeftCtrl || key == Key.RightCtrl ||
            key == Key.LeftAlt || key == Key.RightAlt ||
            key == Key.LeftShift || key == Key.RightShift ||
            key == Key.LWin || key == Key.RWin)
        {
            return;
        }

        var hotkey = string.Empty;
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control)) hotkey += "Ctrl+";
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Alt)) hotkey += "Alt+";
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift)) hotkey += "Shift+";
        hotkey += key;
        textBox.Text = hotkey;
    }

    private void MainHotkeyBox_GotFocus(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.TextBox textBox)
        {
            textBox.Text = "Press a key combination...";
        }

        SuspendHotkeys();
    }

    private void MainHotkeyBox_LostFocus(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.TextBox textBox && textBox.Text == "Press a key combination...")
        {
            textBox.Text = textBox.Name == "MainStartHotkeyBox" ? "Ctrl+Shift+V" : "Ctrl+Shift+X";
        }

        ResumeHotkeys();
    }

    private void LoadEmbeddedSettings()
    {
        _isLoadingSettings = true;
        var config = ConfigService.Instance.Config;

        MainMinimizeToTrayCheck.IsChecked = config.MinimizeToTray;
        MainShowNotificationsCheck.IsChecked = config.ShowNotifications;
        MainNotificationModeCombo.SelectedIndex = (int)config.NotificationMode;
        MainVerboseLoggingCheck.IsChecked = config.VerboseLogging;
        MainMaxLogSizeSlider.Value = config.MaxLogSizeMB;
        MainMaxLogSizeText.Text = $"Max log size: {config.MaxLogSizeMB} MB";
        MainPreventDisplaySleepCheck.IsChecked = config.PreventDisplaySleep;
        MainHeartbeatInputModeCombo.SelectedIndex = config.HeartbeatInputMode?.ToUpperInvariant() switch
        {
            "DISABLED" => 0,
            "F13" => 1,
            "F14" => 2,
            "F16" => 4,
            _ => 3
        };
        MainDurationSlider.Value = config.DefaultDuration;
        MainDurationText.Text = $"Duration: {config.DefaultDuration} minutes";
        MainAutoExitOnCompleteCheck.IsChecked = config.AutoExitOnComplete;

        MainBatteryAwareCheck.IsChecked = config.BatteryAware;
        MainBatteryThresholdSlider.Value = config.BatteryThreshold;
        MainBatteryThresholdText.Text = $"Threshold: {config.BatteryThreshold}%";
        MainNetworkAwareCheck.IsChecked = config.NetworkAware;
        MainIdleDetectionCheck.IsChecked = config.IdleDetection;
        MainIdleThresholdSlider.Value = config.IdleThreshold;
        MainIdleThresholdText.Text = $"Threshold: {config.IdleThreshold} minutes";
        MainPresentationModeCheck.IsChecked = config.PresentationMode;
        MainScheduledOperationCheck.IsChecked = config.ScheduledOperation;

        MainEnableTypeThingCheck.IsChecked = config.TypeThingEnabled;
        MainStartHotkeyBox.Text = config.TypeThingStartHotkey;
        MainStopHotkeyBox.Text = config.TypeThingStopHotkey;
        MainTypingSpeedSlider.Value = config.TypeThingMaxDelayMs;
        MainTypingSpeedText.Text = $"Speed: {config.TypeThingMaxDelayMs} ms per character";
        MainAddRandomPausesCheck.IsChecked = config.TypeThingAddRandomPauses;
        MainTypeNewlinesCheck.IsChecked = config.TypeThingTypeNewlines;

        MainThemeCombo.SelectedIndex = config.Theme switch
        {
            "System" => 0,
            "Light" => 2,
            "MidnightBlue" => 3,
            "ForestGreen" => 4,
            "OceanBlue" => 5,
            "SunsetOrange" => 6,
            "RoyalPurple" => 7,
            "SlateGray" => 8,
            "RoseGold" => 9,
            "Cyberpunk" => 10,
            "Coffee" => 11,
            "ArcticFrost" => 12,
            _ => 1
        };

        MainUpdateChannelCombo.SelectedIndex = config.UpdateChannel?.ToLowerInvariant() switch
        {
            "beta" => 1,
            "disabled" => 2,
            _ => 0
        };

        MainVerifyUpdateSignatureCheck.IsChecked = config.VerifyUpdateSignature;
        MainCurrentVersionText.Text = GetCurrentVersionText();

        MainNotificationModeLabel.Visibility = config.ShowNotifications ? Visibility.Visible : Visibility.Collapsed;
        MainNotificationModeCombo.Visibility = config.ShowNotifications ? Visibility.Visible : Visibility.Collapsed;

        _updateService = new UpdateService(
            config.UpdateRepoOwner,
            config.UpdateRepoName,
            config.UpdateChannel,
            config.VerifyUpdateSignature);
        _isLoadingSettings = false;
    }

    private void AutoApplySettings()
    {
        if (_isLoadingSettings) return;
        try
        {
            var config = ConfigService.Instance.Config;
            config.MinimizeToTray = MainMinimizeToTrayCheck.IsChecked ?? false;
            config.ShowNotifications = MainShowNotificationsCheck.IsChecked ?? true;
            config.NotificationMode = (NotificationMode)MainNotificationModeCombo.SelectedIndex;
            config.VerboseLogging = MainVerboseLoggingCheck.IsChecked ?? false;
            config.MaxLogSizeMB = (int)MainMaxLogSizeSlider.Value;
            config.PreventDisplaySleep = MainPreventDisplaySleepCheck.IsChecked ?? true;
            config.HeartbeatInputMode = MainHeartbeatInputModeCombo.SelectedIndex switch
            {
                0 => "Disabled",
                1 => "F13",
                2 => "F14",
                4 => "F16",
                _ => "F15"
            };
            config.UseHeartbeatKeypress = MainHeartbeatInputModeCombo.SelectedIndex != 0;
            config.DefaultDuration = (int)MainDurationSlider.Value;
            config.AutoExitOnComplete = MainAutoExitOnCompleteCheck.IsChecked ?? false;

            config.BatteryAware = MainBatteryAwareCheck.IsChecked ?? false;
            config.BatteryThreshold = (int)MainBatteryThresholdSlider.Value;
            config.NetworkAware = MainNetworkAwareCheck.IsChecked ?? false;
            config.IdleDetection = MainIdleDetectionCheck.IsChecked ?? false;
            config.IdleThreshold = (int)MainIdleThresholdSlider.Value;
            config.PresentationMode = MainPresentationModeCheck.IsChecked ?? false;
            config.ScheduledOperation = MainScheduledOperationCheck.IsChecked ?? false;

            config.TypeThingEnabled = MainEnableTypeThingCheck.IsChecked ?? true;
            config.TypeThingStartHotkey = MainStartHotkeyBox.Text;
            config.TypeThingStopHotkey = MainStopHotkeyBox.Text;
            config.TypeThingMaxDelayMs = (int)MainTypingSpeedSlider.Value;
            config.TypeThingMinDelayMs = Math.Max(1, Math.Min(config.TypeThingMinDelayMs, config.TypeThingMaxDelayMs));
            config.TypeThingAddRandomPauses = MainAddRandomPausesCheck.IsChecked ?? true;
            config.TypeThingTypeNewlines = MainTypeNewlinesCheck.IsChecked ?? true;

            config.Theme = MainThemeCombo.SelectedIndex switch
            {
                0 => "System",
                3 => "MidnightBlue",
                4 => "ForestGreen",
                5 => "OceanBlue",
                6 => "SunsetOrange",
                7 => "RoyalPurple",
                8 => "SlateGray",
                9 => "RoseGold",
                10 => "Cyberpunk",
                11 => "Coffee",
                12 => "ArcticFrost",
                2 => "Light",
                _ => "Dark"
            };
            config.UpdateChannel = MainUpdateChannelCombo.SelectedIndex switch
            {
                1 => "beta",
                2 => "disabled",
                _ => "stable"
            };
            config.VerifyUpdateSignature = MainVerifyUpdateSignatureCheck.IsChecked ?? false;

            ConfigService.Instance.Save();
            ThemeManager.SetTheme(ThemeManager.ThemeFromString(config.Theme));
            Logger.ApplyConfig(config);
            KeepAwakeService.Instance.ReloadConfig();
            KeepAwakeService.Instance.PreventDisplaySleep = config.PreventDisplaySleep;
            KeepAwakeService.Instance.UseHeartbeat = config.UseHeartbeatKeypress;
            KeepAwakeService.Instance.HeartbeatInputMode = Enum.TryParse<HeartbeatInputMode>(config.HeartbeatInputMode, true, out var heartbeatMode)
                ? heartbeatMode
                : HeartbeatInputMode.F15;
            Logger.Debug("MainWindow", "Settings auto-applied");
        }
        catch (Exception ex)
        {
            Logger.Error("MainWindow", "Failed to auto-apply settings", ex);
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
        UpdatesPanel.Visibility = section == "Updates" ? Visibility.Visible : Visibility.Collapsed;

        if (HomeNavButton != null && section == "Home" && HomeNavButton.IsChecked != true) HomeNavButton.IsChecked = true;
        if (AnalyticsNavButton != null && section == "Analytics" && AnalyticsNavButton.IsChecked != true) AnalyticsNavButton.IsChecked = true;
        if (MetricsNavButton != null && section == "Metrics" && MetricsNavButton.IsChecked != true) MetricsNavButton.IsChecked = true;
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
            MetricsNavButton == null ||
            DiagnosticsNavButton == null ||
            SettingsNavButton == null ||
            BehaviorNavButton == null ||
            SmartFeaturesNavButton == null ||
            TypeThingNavButton == null ||
            UpdatesNavButton == null)
        {
            return;
        }

        if (HomeNavButton.IsChecked == true)
        {
            ShowSection("Home");
            return;
        }

        if (AnalyticsNavButton.IsChecked == true)
        {
            ShowSection("Analytics");
            return;
        }

        if (MetricsNavButton.IsChecked == true)
        {
            ShowSection("Metrics");
            return;
        }

        if (DiagnosticsNavButton.IsChecked == true)
        {
            ShowSection("Diagnostics");
            return;
        }

        if (SettingsNavButton.IsChecked == true)
        {
            ShowSection("Settings");
            return;
        }

        if (BehaviorNavButton.IsChecked == true)
        {
            ShowSection("Behavior");
            return;
        }

        if (SmartFeaturesNavButton.IsChecked == true)
        {
            ShowSection("SmartFeatures");
            return;
        }

        if (TypeThingNavButton.IsChecked == true)
        {
            ShowSection("TypeThing");
            return;
        }

        if (UpdatesNavButton.IsChecked == true)
        {
            ShowSection("Updates");
        }
    }

    private void ShowAnalyticsButton_Click(object sender, RoutedEventArgs e)
    {
        ShowAnalytics();
    }

    private void ShowMetricsButton_Click(object sender, RoutedEventArgs e)
    {
        ShowMetrics();
    }

    private void ShowDiagnosticsButton_Click(object sender, RoutedEventArgs e)
    {
        ShowDiagnostics();
    }

    private void ShowSettingsButton_Click(object sender, RoutedEventArgs e)
    {
        ShowSettings();
    }

    private void ShowBehaviorButton_Click(object sender, RoutedEventArgs e)
    {
        ShowBehavior();
    }

    private void ShowSmartFeaturesButton_Click(object sender, RoutedEventArgs e)
    {
        ShowSmartFeatures();
    }

    private void ShowTypeThingButton_Click(object sender, RoutedEventArgs e)
    {
        ShowTypeThing();
    }

    private void ShowUpdatesButton_Click(object sender, RoutedEventArgs e)
    {
        ShowUpdates();
    }

    private void ApplyMainSettingsButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var config = ConfigService.Instance.Config;
            config.MinimizeToTray = MainMinimizeToTrayCheck.IsChecked ?? false;
            config.ShowNotifications = MainShowNotificationsCheck.IsChecked ?? true;
            config.NotificationMode = (NotificationMode)MainNotificationModeCombo.SelectedIndex;
            config.VerboseLogging = MainVerboseLoggingCheck.IsChecked ?? false;
            config.MaxLogSizeMB = (int)MainMaxLogSizeSlider.Value;
            config.PreventDisplaySleep = MainPreventDisplaySleepCheck.IsChecked ?? true;
            config.HeartbeatInputMode = MainHeartbeatInputModeCombo.SelectedIndex switch
            {
                0 => "Disabled",
                1 => "F13",
                2 => "F14",
                4 => "F16",
                _ => "F15"
            };
            config.UseHeartbeatKeypress = MainHeartbeatInputModeCombo.SelectedIndex != 0;
            config.DefaultDuration = (int)MainDurationSlider.Value;
            config.AutoExitOnComplete = MainAutoExitOnCompleteCheck.IsChecked ?? false;

            config.BatteryAware = MainBatteryAwareCheck.IsChecked ?? false;
            config.BatteryThreshold = (int)MainBatteryThresholdSlider.Value;
            config.NetworkAware = MainNetworkAwareCheck.IsChecked ?? false;
            config.IdleDetection = MainIdleDetectionCheck.IsChecked ?? false;
            config.IdleThreshold = (int)MainIdleThresholdSlider.Value;
            config.PresentationMode = MainPresentationModeCheck.IsChecked ?? false;
            config.ScheduledOperation = MainScheduledOperationCheck.IsChecked ?? false;

            config.TypeThingEnabled = MainEnableTypeThingCheck.IsChecked ?? true;
            config.TypeThingStartHotkey = MainStartHotkeyBox.Text;
            config.TypeThingStopHotkey = MainStopHotkeyBox.Text;
            config.TypeThingMaxDelayMs = (int)MainTypingSpeedSlider.Value;
            config.TypeThingMinDelayMs = Math.Max(1, Math.Min(config.TypeThingMinDelayMs, config.TypeThingMaxDelayMs));
            config.TypeThingAddRandomPauses = MainAddRandomPausesCheck.IsChecked ?? true;
            config.TypeThingTypeNewlines = MainTypeNewlinesCheck.IsChecked ?? true;

            config.Theme = MainThemeCombo.SelectedIndex switch
            {
                0 => "System",
                3 => "MidnightBlue",
                4 => "ForestGreen",
                5 => "OceanBlue",
                6 => "SunsetOrange",
                7 => "RoyalPurple",
                8 => "SlateGray",
                9 => "RoseGold",
                10 => "Cyberpunk",
                11 => "Coffee",
                12 => "ArcticFrost",
                2 => "Light",
                _ => "Dark"
            };
            config.UpdateChannel = MainUpdateChannelCombo.SelectedIndex switch
            {
                1 => "beta",
                2 => "disabled",
                _ => "stable"
            };
            config.VerifyUpdateSignature = MainVerifyUpdateSignatureCheck.IsChecked ?? false;

            var validationErrors = ConfigService.Instance.Validate();
            if (validationErrors.Count > 0)
            {
                _analytics.TrackFeature("settings.save_validation_failed");
                MessageBox.Show(this, string.Join(Environment.NewLine, validationErrors), "Validation Errors", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (!ConfigService.Instance.Save())
            {
                throw new InvalidOperationException("Failed to save configuration.");
            }

            ThemeManager.SetTheme(ThemeManager.ThemeFromString(config.Theme));
            Logger.ApplyConfig(config);
            KeepAwakeService.Instance.ReloadConfig();
            KeepAwakeService.Instance.PreventDisplaySleep = config.PreventDisplaySleep;
            KeepAwakeService.Instance.UseHeartbeat = config.UseHeartbeatKeypress;
            KeepAwakeService.Instance.HeartbeatInputMode = Enum.TryParse<HeartbeatInputMode>(config.HeartbeatInputMode, true, out var heartbeatMode)
                ? heartbeatMode
                : HeartbeatInputMode.F15;
            _analytics.TrackFeature("settings.saved");
            LoadEmbeddedDashboardContent();
            ReloadHotkeys();
            MessageBox.Show(this, "Settings applied.", "Redball", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            Logger.Error("MainWindow", "Failed to apply embedded settings", ex);
            _analytics.TrackFeature("settings.save_failed");
            MessageBox.Show(this, $"Could not apply settings: {ex.Message}", "Redball", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        // Check for TaskbarCreated message (Explorer restart)
        if (msg == _taskbarCreatedMsg)
        {
            Logger.Info("MainWindow", "TaskbarCreated message received - Explorer likely restarted, recreating tray icon");
            handled = true;
            // Recreate tray icon with delay to ensure Explorer is ready
            var delayTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
            delayTimer.Tick += (_, _) =>
            {
                delayTimer.Stop();
                try
                {
                    RecreateTrayIcon();
                }
                catch (Exception ex)
                {
                    Logger.Error("MainWindow", "Failed to recreate tray icon after Explorer restart", ex);
                }
            };
            delayTimer.Start();
        }
        return IntPtr.Zero;
    }

    private void RecreateTrayIcon()
    {
        Logger.Info("MainWindow", "Recreating tray icon...");
        try
        {
            // Dispose existing tray icon
            if (_trayIcon != null)
            {
                _trayIcon.Visibility = Visibility.Collapsed;
                _trayIcon = null;
                Logger.Debug("MainWindow", "Existing tray icon hidden");
            }

            _isTrayIconInitialized = false;

            // Small delay to ensure cleanup
            System.Threading.Thread.Sleep(100);

            // Re-setup tray icon
            SetupTrayIcon();

            if (_trayIcon != null)
            {
                // Force refresh by toggling visibility
                _trayIcon.Visibility = Visibility.Collapsed;
                _trayIcon.Visibility = Visibility.Visible;
                Logger.Info("MainWindow", "Tray icon recreated successfully");
            }
            else
            {
                Logger.Warning("MainWindow", "Tray icon recreation failed - will retry on next timer tick");
            }
        }
        catch (Exception ex)
        {
            Logger.Error("MainWindow", "Error recreating tray icon", ex);
        }
    }

    private void SetupTrayIconRefreshTimer()
    {
        Logger.Info("MainWindow", "Setting up tray icon refresh timer...");
        try
        {
            _trayIconRefreshTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(30) // Check every 30 seconds
            };

            int retryCount = 0;
            const int maxRetries = 3;

            _trayIconRefreshTimer.Tick += (s, e) =>
            {
                try
                {
                    // Check if tray icon needs refreshing
                    if (_trayIcon == null || !_isTrayIconInitialized)
                    {
                        retryCount++;
                        if (retryCount <= maxRetries)
                        {
                            Logger.Warning("MainWindow", $"Tray icon not initialized, attempt {retryCount}/{maxRetries} to recreate...");
                            RecreateTrayIcon();
                        }
                        else
                        {
                            Logger.Error("MainWindow", "Max tray icon retry attempts reached, giving up until next timer cycle");
                            retryCount = 0; // Reset for next cycle
                        }
                    }
                    else
                    {
                        // Icon exists, ensure visibility is set correctly
                        if (_trayIcon.Visibility != Visibility.Visible)
                        {
                            Logger.Warning("MainWindow", "Tray icon visibility was not Visible, correcting...");
                            _trayIcon.Visibility = Visibility.Visible;
                        }
                        retryCount = 0; // Reset counter on success
                    }
                }
                catch (Exception ex)
                {
                    Logger.Error("MainWindow", "Error in tray icon refresh timer", ex);
                }
            };

            _trayIconRefreshTimer.Start();
            Logger.Info("MainWindow", "Tray icon refresh timer started (30s interval)");
        }
        catch (Exception ex)
        {
            Logger.Error("MainWindow", "Failed to setup tray icon refresh timer", ex);
        }
    }

    private void SetupTrayIcon()
    {
        // Prevent duplicate initialization
        if (_isTrayIconInitialized)
        {
            Logger.Debug("MainWindow", "Tray icon already initialized, skipping");
            return;
        }
        
        Logger.Info("MainWindow", "Setting up tray icon...");
        _isTrayIconInitialized = true;
        
        // Tray icon is defined in XAML, ensure it's properly initialized
        _trayIcon = TrayIcon;
        Logger.Debug("MainWindow", $"TrayIcon from XAML: {_trayIcon != null}");
        
        if (_trayIcon != null)
        {
            // Load icon from multiple possible locations
            var iconPaths = new[]
            {
                Path.Combine(AppContext.BaseDirectory, "Assets", "redball.ico"),
                Path.Combine(AppContext.BaseDirectory, "redball.ico"),
                Path.Combine(Environment.CurrentDirectory, "Assets", "redball.ico"),
                Path.Combine(Environment.CurrentDirectory, "redball.ico")
            };
            
            string? foundPath = null;
            foreach (var path in iconPaths)
            {
                bool exists = File.Exists(path);
                Logger.Verbose("MainWindow", $"Checking icon at: {path} - Exists: {exists}");
                if (exists)
                {
                    foundPath = path;
                    break;
                }
            }
            
            if (foundPath != null)
            {
                try
                {
                    Logger.Info("MainWindow", $"Loading icon from: {foundPath}");
                    var icon = new System.Drawing.Icon(foundPath);
                    _trayIcon.Icon = icon;
                    Logger.Info("MainWindow", "Icon loaded successfully");
                }
                catch (Exception ex)
                {
                    Logger.Error("MainWindow", "Failed to load icon", ex);
                }
            }
            else
            {
                Logger.Warning("MainWindow", "Icon file not found in any expected location");
            }
            
            // Ensure visibility is set
            _trayIcon.Visibility = Visibility.Visible;
            NotificationService.Instance.SetTrayIcon(_trayIcon);
            
            // Set tooltip with actual version
            var version = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
            _trayIcon.ToolTipText = $"Redball v{version?.Major}.{version?.Minor}.{version?.Build}";
            Logger.Info("MainWindow", $"Tray tooltip set to: {_trayIcon.ToolTipText}");
        }
        else
        {
            Logger.Error("MainWindow", "TrayIcon not found in XAML!");
        }
        
        Logger.Info("MainWindow", "Tray icon setup complete");
    }

    private void SetupGlobalHotkeys()
    {
        Logger.Info("MainWindow", "Setting up global hotkeys...");
        try
        {
            var hwndSource = PresentationSource.FromVisual(this) as HwndSource;
            if (hwndSource == null)
            {
                Logger.Warning("MainWindow", "HwndSource not available for hotkey registration");
                return;
            }
            Logger.Debug("MainWindow", $"HwndSource obtained: {hwndSource.Handle}");

            _hotkeyService = new HotkeyService(hwndSource);

            // Register Ctrl+Alt+Pause to toggle active state
            Logger.Debug("MainWindow", "Registering Ctrl+Alt+Pause hotkey...");
            _hotkeyService.RegisterHotkey(1, HotkeyService.MOD_CONTROL | HotkeyService.MOD_ALT, 0x13 /* VK_PAUSE */, () =>
            {
                Logger.Info("MainWindow", "Hotkey: Ctrl+Alt+Pause - Toggle active");
                _viewModel?.ToggleActiveCommand.Execute(null);
            });

            // Register TypeThing start hotkey from config
            var startHotkey = ConfigService.Instance.Config.TypeThingStartHotkey ?? "Ctrl+Shift+V";
            Logger.Debug("MainWindow", $"TypeThing start hotkey from config: {startHotkey}");
            var (startMods, startKey) = HotkeyService.ParseHotkey(startHotkey);
            if (startKey != 0)
            {
                _hotkeyService.RegisterHotkey(100, startMods, startKey, () =>
                {
                    Logger.Info("MainWindow", $"Hotkey: {startHotkey} - TypeThing start");
                    StartTypeThing();
                });
            }
            else
            {
                Logger.Warning("MainWindow", $"Could not parse TypeThing start hotkey: {startHotkey}");
            }

            // Register TypeThing stop hotkey from config
            var stopHotkey = ConfigService.Instance.Config.TypeThingStopHotkey ?? "Ctrl+Shift+X";
            Logger.Debug("MainWindow", $"TypeThing stop hotkey from config: {stopHotkey}");
            var (stopMods, stopKey) = HotkeyService.ParseHotkey(stopHotkey);
            if (stopKey != 0)
            {
                _hotkeyService.RegisterHotkey(101, stopMods, stopKey, () =>
                {
                    Logger.Info("MainWindow", $"Hotkey: {stopHotkey} - TypeThing stop");
                    StopTypeThing();
                });
            }

            Logger.Info("MainWindow", "Global hotkeys registered successfully");
        }
        catch (Exception ex)
        {
            Logger.Error("MainWindow", "Failed to register global hotkeys", ex);
        }
    }

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        Logger.Debug("MainWindow", $"OnClosing called, Cancel={e.Cancel}");
        // If closing from tray exit command, allow close
        // If closing from X button, move off-screen instead (tray-only mode)
        if (_trayIcon?.Visibility == Visibility.Visible && e.Cancel == false)
        {
            Logger.Info("MainWindow", "Moving window off-screen instead of closing (tray-only mode)");
            ShowInTaskbar = false;
            WindowState = WindowState.Minimized;
            Hide();
            e.Cancel = true;
        }
        base.OnClosing(e);
    }

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2)
        {
            ToggleWindowState();
            return;
        }

        try
        {
            DragMove();
        }
        catch (InvalidOperationException)
        {
        }
    }

    private void MinimizeWindowButton_Click(object sender, RoutedEventArgs e)
    {
        WindowState = WindowState.Minimized;
    }

    private void MaximizeWindowButton_Click(object sender, RoutedEventArgs e)
    {
        ToggleWindowState();
    }

    private void CloseWindowButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void ToggleWindowState()
    {
        WindowState = WindowState == WindowState.Maximized
            ? WindowState.Normal
            : WindowState.Maximized;
    }

    private void SyncWindowChromeButtons()
    {
        if (MaximizeWindowButton == null)
        {
            return;
        }

        MaximizeWindowButton.Content = WindowState == WindowState.Maximized ? "❐" : "□";
    }

    public void ShowSettings()
    {
        Logger.Info("MainWindow", "ShowSettings called");
        _analytics.TrackFeature("settings.opened");
        Dispatcher.Invoke(() =>
        {
            ShowInTaskbar = true;
            if (!IsVisible)
            {
                Show();
            }

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
            if (!IsVisible)
            {
                Show();
            }

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
            if (!IsVisible)
            {
                Show();
            }

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
            if (!IsVisible)
            {
                Show();
            }

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
            if (!IsVisible)
            {
                Show();
            }

            WindowState = WindowState.Normal;
            LoadEmbeddedSettings();
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

            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
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

    public void StartTypeThing()
    {
        if (_isTyping)
        {
            Logger.Warning("MainWindow", "TypeThing already running, ignoring request");
            _analytics.TrackFeature("typething.rejected_busy");
            NotificationService.Instance.ShowWarning("TypeThing", "Already typing! Please wait for current operation to complete.");
            return;
        }

        Logger.Info("MainWindow", "StartTypeThing called");
        _analytics.TrackFeature("typething.started");
        _isTyping = true;
        Dispatcher.Invoke(() =>
        {
            try
            {
                var config = ConfigService.Instance.Config;
                if (!config.TypeThingEnabled)
                {
                    _analytics.TrackFeature("typething.blocked_disabled");
                    NotificationService.Instance.ShowWarning("TypeThing", "TypeThing is disabled in settings.");
                    _isTyping = false;
                    return;
                }

                var clipboardText = System.Windows.Clipboard.GetText();
                if (string.IsNullOrEmpty(clipboardText))
                {
                    Logger.Warning("MainWindow", "TypeThing: Clipboard is empty");
                    _analytics.TrackFeature("typething.failed_empty_clipboard");
                    NotificationService.Instance.ShowWarning("TypeThing", "Clipboard is empty. Copy some text first.");
                    _isTyping = false;
                    return;
                }

                Logger.Info("MainWindow", $"TypeThing: Got {clipboardText.Length} chars from clipboard");

                // Start typing after a short delay so user can switch to target window
                var countdown = Math.Max(1, config.TypeThingStartDelaySec);
                _typeThingCountdownTimer?.Stop();
                _typeThingCountdownTimer = new System.Windows.Threading.DispatcherTimer
                {
                    Interval = TimeSpan.FromSeconds(1)
                };
                _typeThingCountdownTimer.Tick += (s, e) =>
                {
                    countdown--;
                    if (countdown <= 0)
                    {
                        _typeThingCountdownTimer?.Stop();
                        _typeThingCountdownTimer = null;
                        Logger.Info("MainWindow", "TypeThing: Starting typing");
                        TypeText(clipboardText);
                    }
                    else
                    {
                        Logger.Debug("MainWindow", $"TypeThing: Starting in {countdown}...");
                    }
                };

                NotificationService.Instance.ShowInfo("TypeThing", $"Typing {clipboardText.Length} characters in {countdown} seconds... Switch to target window now!");
                _typeThingCountdownTimer.Start();
            }
            catch (Exception ex)
            {
                Logger.Error("MainWindow", "TypeThing error", ex);
                _analytics.TrackFeature("typething.failed_exception");
                _isTyping = false;
            }
        });
    }

    private void TypeText(string text)
    {
        var isRdp = IsRemoteSession();
        var config = ConfigService.Instance.Config;
        Logger.Info("MainWindow", $"TypeThing: Begin typing {text.Length} chars (RDP session: {isRdp})");
        var index = 0;
        var minDelay = Math.Max(1, config.TypeThingMinDelayMs);
        var maxDelay = Math.Max(minDelay, config.TypeThingMaxDelayMs);
        _typeThingTimer?.Stop();
        _typeThingTimer = new System.Windows.Threading.DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(_random.Next(minDelay, maxDelay + 1))
        };
        _typeThingTimer.Tick += (s, e) =>
        {
            if (!_isTyping)
            {
                _typeThingTimer?.Stop();
                _typeThingTimer = null;
                Logger.Info("MainWindow", "TypeThing: Typing stopped by user");
                _analytics.TrackFeature("typething.stopped");
                NotificationService.Instance.ShowInfo("TypeThing", "Typing stopped.");
                return;
            }

            if (index >= text.Length)
            {
                _typeThingTimer?.Stop();
                _typeThingTimer = null;
                _isTyping = false;
                Logger.Info("MainWindow", "TypeThing: Typing complete");
                _analytics.TrackFeature("typething.completed");
                NotificationService.Instance.ShowInfo("TypeThing", $"Done! Typed {text.Length} characters.");
                return;
            }

            var ch = text[index];
            if (ch == '\n' && config.TypeThingTypeNewlines)
            {
                SendKeyPress(0x0D); // VK_RETURN
            }
            else if (ch == '\r')
            {
                // Skip carriage return (handled by \n)
            }
            else if (ch == '\t')
            {
                SendKeyPress(0x09); // VK_TAB
            }
            else
            {
                SendCharacter(ch);
            }
            index++;

            // Randomize interval for human-like typing
            if (config.TypeThingAddRandomPauses && _random.Next(1, 101) <= Math.Max(0, config.TypeThingRandomPauseChance))
            {
                var extraPause = _random.Next(50, Math.Max(51, config.TypeThingRandomPauseMaxMs + 1));
                _typeThingTimer.Interval = TimeSpan.FromMilliseconds(Math.Min(maxDelay + extraPause, maxDelay + config.TypeThingRandomPauseMaxMs));
            }
            else
            {
                _typeThingTimer.Interval = TimeSpan.FromMilliseconds(_random.Next(minDelay, maxDelay + 1));
            }
        };
        _typeThingTimer.Start();
    }

    public void StopTypeThing()
    {
        Logger.Info("MainWindow", "StopTypeThing called");
        Dispatcher.Invoke(() =>
        {
            if (!_isTyping && _typeThingCountdownTimer == null && _typeThingTimer == null)
            {
                Logger.Debug("MainWindow", "TypeThing is not active; nothing to stop");
                return;
            }

            _typeThingCountdownTimer?.Stop();
            _typeThingCountdownTimer = null;
            _typeThingTimer?.Stop();
            _typeThingTimer = null;
            _isTyping = false;
            _analytics.TrackFeature("typething.stop_requested");
            NotificationService.Instance.ShowInfo("TypeThing", "TypeThing stopped.");
        });
    }

    public void ReloadHotkeys()
    {
        Logger.Info("MainWindow", "Reloading hotkeys from config...");
        try
        {
            _hotkeyService?.Dispose();
            SetupGlobalHotkeys();
            Logger.Info("MainWindow", "Hotkeys reloaded successfully");
        }
        catch (Exception ex)
        {
            Logger.Error("MainWindow", "Failed to reload hotkeys", ex);
        }
    }

    public void SuspendHotkeys()
    {
        Logger.Info("MainWindow", "Suspending hotkeys for key capture...");
        try
        {
            _hotkeyService?.Dispose();
            _hotkeyService = null;
            Logger.Info("MainWindow", "Hotkeys suspended");
        }
        catch (Exception ex)
        {
            Logger.Error("MainWindow", "Failed to suspend hotkeys", ex);
        }
    }

    public void ResumeHotkeys()
    {
        Logger.Info("MainWindow", "Resuming hotkeys after key capture...");
        SetupGlobalHotkeys();
    }

    public void ExitApplication()
    {
        Logger.Info("MainWindow", "ExitApplication called");
        _trayIconRefreshTimer?.Stop();
        _trayIconRefreshTimer = null;
        _hotkeyService?.Dispose();
        if (_trayIcon != null)
        {
            _trayIcon.Dispose();
            _trayIcon = null;
        }
        Logger.Info("MainWindow", "Shutting down application");
        Application.Current.Shutdown();
    }

    #region P/Invoke SendInput helpers

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern uint RegisterWindowMessage(string lpString);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

    [DllImport("user32.dll")]
    private static extern bool PostMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int nIndex);

    private const int SM_REMOTESESSION = 0x1000;
    private const uint WM_KEYDOWN = 0x0100;
    private const uint WM_KEYUP = 0x0101;
    private const uint WM_CHAR = 0x0102;
    private const int INPUT_KEYBOARD = 1;
    private const uint KEYEVENTF_KEYUP = 0x0002;
    private const uint KEYEVENTF_UNICODE = 0x0004;

    [StructLayout(LayoutKind.Sequential)]
    private struct INPUT
    {
        public int type;
        public INPUTUNION u;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct INPUTUNION
    {
        [FieldOffset(0)] public MOUSEINPUT mi;
        [FieldOffset(0)] public KEYBDINPUT ki;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MOUSEINPUT
    {
        public int dx;
        public int dy;
        public uint mouseData;
        public uint dwFlags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KEYBDINPUT
    {
        public ushort wVk;
        public ushort wScan;
        public uint dwFlags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    private static bool IsRemoteSession()
    {
        return GetSystemMetrics(SM_REMOTESESSION) != 0;
    }

    private static void SendKeyPress(ushort vk)
    {
        if (IsRemoteSession())
        {
            // Use PostMessage for RDP sessions
            var hwnd = GetForegroundWindow();
            if (hwnd != IntPtr.Zero)
            {
                PostMessage(hwnd, WM_KEYDOWN, (IntPtr)vk, IntPtr.Zero);
                PostMessage(hwnd, WM_KEYUP, (IntPtr)vk, unchecked((nint)0xC0000001));
            }
        }
        else
        {
            // Use SendInput for local sessions (more reliable)
            var inputs = new INPUT[2];
            inputs[0].type = INPUT_KEYBOARD;
            inputs[0].u.ki.wVk = vk;
            inputs[1].type = INPUT_KEYBOARD;
            inputs[1].u.ki.wVk = vk;
            inputs[1].u.ki.dwFlags = KEYEVENTF_KEYUP;
            SendInput(2, inputs, Marshal.SizeOf<INPUT>());
        }
    }

    private static void SendCharacter(char ch)
    {
        if (IsRemoteSession())
        {
            // Use PostMessage for RDP sessions
            var hwnd = GetForegroundWindow();
            if (hwnd != IntPtr.Zero)
            {
                PostMessage(hwnd, WM_CHAR, (IntPtr)ch, IntPtr.Zero);
            }
        }
        else
        {
            // Use SendInput for local sessions
            var inputs = new INPUT[2];
            inputs[0].type = INPUT_KEYBOARD;
            inputs[0].u.ki.wScan = (ushort)ch;
            inputs[0].u.ki.dwFlags = KEYEVENTF_UNICODE;
            inputs[1].type = INPUT_KEYBOARD;
            inputs[1].u.ki.wScan = (ushort)ch;
            inputs[1].u.ki.dwFlags = KEYEVENTF_UNICODE | KEYEVENTF_KEYUP;
            SendInput(2, inputs, Marshal.SizeOf<INPUT>());
        }
    }

    #endregion
}
