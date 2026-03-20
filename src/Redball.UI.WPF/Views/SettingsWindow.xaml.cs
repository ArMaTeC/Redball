using System.Diagnostics;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Animation;
using Microsoft.Win32;
using Redball.UI.Services;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;

namespace Redball.UI.Views;

public partial class SettingsWindow : Window
{
    private bool _isDirty;
    private UpdateService? _updateService;
    private MainWindow? _mainWindow;
    private readonly AnalyticsService _analytics = new(ConfigService.Instance.Config.EnableTelemetry);

    public SettingsWindow(MainWindow? mainWindow = null)
    {
        _mainWindow = mainWindow;
        InitializeComponent();
        Loaded += SettingsWindow_Loaded;
    }

    private void SettingsWindow_Loaded(object sender, RoutedEventArgs e)
    {
        LoadConfigIntoUI();
        SetVersionText();
        _isDirty = false;
        
        // Initialize update service with config
        var cfg = ConfigService.Instance.Config;
        _updateService = new UpdateService(
            cfg.UpdateRepoOwner,
            cfg.UpdateRepoName,
            cfg.UpdateChannel,
            cfg.VerifyUpdateSignature);
    }

    private void SetVersionText()
    {
        var version = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
        var versionString = $"v{version?.Major}.{version?.Minor}.{version?.Build}";
        if (CurrentVersionText != null)
            CurrentVersionText.Text = $"Current Version: {versionString}";
        if (AboutVersionText != null)
            AboutVersionText.Text = $"Redball {versionString}";
    }

    private async void CheckForUpdatesButton_Click(object sender, RoutedEventArgs e)
    {
        if (_updateService == null) return;

        // Show checking dialog
        var checkingDialog = MessageBox.Show("Checking for updates...", "Update", MessageBoxButton.OKCancel, MessageBoxImage.Information);
        if (checkingDialog != MessageBoxResult.OK) return;

        // Check for updates
        var updateInfo = await _updateService.CheckForUpdateAsync();
        
        if (updateInfo == null)
        {
            MessageBox.Show("You're running the latest version.", "No Updates", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        // Download and install
        var progressWindow = new UpdateProgressWindow();
        progressWindow.Show();

        var progress = new Progress<int>(percent => progressWindow.SetProgress(percent));
        
        bool success = await _updateService.DownloadAndInstallAsync(updateInfo, progress);
        
        progressWindow.Close();

        if (success)
        {
            if (Application.Current.MainWindow is MainWindow mw)
            {
                mw.ExitApplication();
                return;
            }

            Application.Current.Shutdown();
        }
        else
        {
            MessageBox.Show(
                "Failed to download or install the update. Please try again later or download manually from GitHub.",
                "Update Failed",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private void LoadConfigIntoUI()
    {
        var cfg = ConfigService.Instance.Config;

        // Theme
        if (ThemeCombo != null)
        {
            ThemeCombo.SelectionChanged -= ThemeCombo_SelectionChanged;
            ThemeCombo.SelectedIndex = ThemeManager.CurrentTheme switch
            {
                Theme.Dark => 1,
                Theme.Light => 2,
                Theme.MidnightBlue => 3,
                Theme.ForestGreen => 4,
                Theme.OceanBlue => 5,
                Theme.SunsetOrange => 6,
                Theme.RoyalPurple => 7,
                Theme.SlateGray => 8,
                Theme.RoseGold => 9,
                Theme.Cyberpunk => 10,
                Theme.Coffee => 11,
                Theme.ArcticFrost => 12,
                _ => 1
            };
            ThemeCombo.SelectionChanged += ThemeCombo_SelectionChanged;
        }

        // TypeThing hotkeys
        if (StartHotkeyBox != null) StartHotkeyBox.Text = cfg.TypeThingStartHotkey;
        if (StopHotkeyBox != null) StopHotkeyBox.Text = cfg.TypeThingStopHotkey;

        // General settings
        if (MinimizeToTrayCheck != null) MinimizeToTrayCheck.IsChecked = cfg.MinimizeToTray;
        if (ShowNotificationsCheck != null) ShowNotificationsCheck.IsChecked = cfg.ShowNotifications;
        if (NotificationModeCombo != null) NotificationModeCombo.SelectedIndex = (int)cfg.NotificationMode;
        if (VerboseLoggingCheck != null) VerboseLoggingCheck.IsChecked = cfg.VerboseLogging;
        if (MaxLogSizeSlider != null) MaxLogSizeSlider.Value = cfg.MaxLogSizeMB;
        if (MaxLogSizeText != null) MaxLogSizeText.Text = $"Max log size: {cfg.MaxLogSizeMB} MB";

        // Behavior settings
        if (PreventDisplaySleepCheck != null) PreventDisplaySleepCheck.IsChecked = cfg.PreventDisplaySleep;
        if (UseHeartbeatCheck != null) UseHeartbeatCheck.IsChecked = cfg.UseHeartbeatKeypress;
        if (DurationSlider != null) DurationSlider.Value = cfg.DefaultDuration;
        if (DurationText != null) DurationText.Text = $"Duration: {cfg.DefaultDuration} minutes";
        if (AutoExitOnCompleteCheck != null) AutoExitOnCompleteCheck.IsChecked = cfg.AutoExitOnComplete;

        // Features settings
        if (BatteryAwareCheck != null) BatteryAwareCheck.IsChecked = cfg.BatteryAware;
        if (BatteryThresholdSlider != null) BatteryThresholdSlider.Value = cfg.BatteryThreshold;
        if (BatteryThresholdText != null) BatteryThresholdText.Text = $"Threshold: {cfg.BatteryThreshold}%";
        if (NetworkAwareCheck != null) NetworkAwareCheck.IsChecked = cfg.NetworkAware;
        if (IdleDetectionCheck != null) IdleDetectionCheck.IsChecked = cfg.IdleDetection;
        if (IdleThresholdSlider != null) IdleThresholdSlider.Value = cfg.IdleThreshold;
        if (IdleThresholdText != null) IdleThresholdText.Text = $"Threshold: {cfg.IdleThreshold} minutes";
        if (PresentationModeCheck != null) PresentationModeCheck.IsChecked = cfg.PresentationMode;
        if (ScheduledOperationCheck != null) ScheduledOperationCheck.IsChecked = cfg.ScheduledOperation;

        // TypeThing settings
        if (EnableTypeThingCheck != null) EnableTypeThingCheck.IsChecked = cfg.TypeThingEnabled;
        if (TypingSpeedSlider != null) TypingSpeedSlider.Value = cfg.TypeThingMaxDelayMs;
        if (TypingSpeedText != null) TypingSpeedText.Text = $"Speed: {cfg.TypeThingMaxDelayMs} ms per character";
        if (AddRandomPausesCheck != null) AddRandomPausesCheck.IsChecked = cfg.TypeThingAddRandomPauses;
        if (TypeNewlinesCheck != null) TypeNewlinesCheck.IsChecked = cfg.TypeThingTypeNewlines;

        // Update settings
        if (UpdateChannelCombo != null)
        {
            UpdateChannelCombo.SelectedIndex = cfg.UpdateChannel?.ToLowerInvariant() switch
            {
                "stable" => 0,
                "beta" => 1,
                "disabled" => 2,
                _ => 0
            };
        }
        if (VerifyUpdateSignatureCheck != null) VerifyUpdateSignatureCheck.IsChecked = cfg.VerifyUpdateSignature;

        // Privacy settings
        if (EncryptConfigCheck != null) EncryptConfigCheck.IsChecked = cfg.EncryptConfig;
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        if (_isDirty && PromptUnsavedChanges() == MessageBoxResult.Cancel)
            return;
        Close();
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        if (_isDirty && PromptUnsavedChanges() == MessageBoxResult.Cancel)
            return;
        Close();
    }

    private void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        SaveUIToConfig();
        var svc = ConfigService.Instance;
        var errors = svc.Validate();
        if (errors.Count > 0)
        {
            _analytics.TrackFeature("settings.save_validation_failed");
            MessageBox.Show(string.Join("\n", errors), "Validation Errors",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        if (svc.Save())
        {
            _analytics.TrackFeature("settings.saved");
            _analytics.TrackFunnel("settings", "saved");
            Logger.ApplyConfig(svc.Config);
            // Reload keep-awake engine with updated config
            KeepAwakeService.Instance.ReloadConfig();
            _isDirty = false;
            Close();
        }
        else
        {
            _analytics.TrackFeature("settings.save_failed");
            MessageBox.Show("Failed to save configuration.", "Error",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void SaveUIToConfig()
    {
        var cfg = ConfigService.Instance.Config;
        if (StartHotkeyBox != null) cfg.TypeThingStartHotkey = StartHotkeyBox.Text;
        if (StopHotkeyBox != null) cfg.TypeThingStopHotkey = StopHotkeyBox.Text;
        if (ThemeCombo != null)
        {
            cfg.Theme = ThemeCombo.SelectedIndex switch
            {
                0 => "System",
                1 => "Dark",
                2 => "Light",
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
                _ => "Dark"
            };
        }

        // General settings
        if (MinimizeToTrayCheck != null) cfg.MinimizeToTray = MinimizeToTrayCheck.IsChecked ?? false;
        if (ShowNotificationsCheck != null) cfg.ShowNotifications = ShowNotificationsCheck.IsChecked ?? true;
        if (NotificationModeCombo != null) cfg.NotificationMode = (NotificationMode)NotificationModeCombo.SelectedIndex;
        if (VerboseLoggingCheck != null) cfg.VerboseLogging = VerboseLoggingCheck.IsChecked ?? false;
        if (MaxLogSizeSlider != null) cfg.MaxLogSizeMB = (int)MaxLogSizeSlider.Value;

        // Behavior settings
        if (PreventDisplaySleepCheck != null) cfg.PreventDisplaySleep = PreventDisplaySleepCheck.IsChecked ?? true;
        if (UseHeartbeatCheck != null) cfg.UseHeartbeatKeypress = UseHeartbeatCheck.IsChecked ?? true;
        if (DurationSlider != null) cfg.DefaultDuration = (int)DurationSlider.Value;
        if (AutoExitOnCompleteCheck != null) cfg.AutoExitOnComplete = AutoExitOnCompleteCheck.IsChecked ?? false;

        // Features settings
        if (BatteryAwareCheck != null) cfg.BatteryAware = BatteryAwareCheck.IsChecked ?? false;
        if (BatteryThresholdSlider != null) cfg.BatteryThreshold = (int)BatteryThresholdSlider.Value;
        if (NetworkAwareCheck != null) cfg.NetworkAware = NetworkAwareCheck.IsChecked ?? false;
        if (IdleDetectionCheck != null) cfg.IdleDetection = IdleDetectionCheck.IsChecked ?? false;
        if (IdleThresholdSlider != null) cfg.IdleThreshold = (int)IdleThresholdSlider.Value;
        if (PresentationModeCheck != null) cfg.PresentationMode = PresentationModeCheck.IsChecked ?? false;
        if (ScheduledOperationCheck != null) cfg.ScheduledOperation = ScheduledOperationCheck.IsChecked ?? false;

        // TypeThing settings
        if (EnableTypeThingCheck != null) cfg.TypeThingEnabled = EnableTypeThingCheck.IsChecked ?? true;
        if (TypingSpeedSlider != null)
        {
            cfg.TypeThingMaxDelayMs = (int)TypingSpeedSlider.Value;
            cfg.TypeThingMinDelayMs = Math.Max(1, Math.Min(cfg.TypeThingMinDelayMs, cfg.TypeThingMaxDelayMs));
        }
        if (AddRandomPausesCheck != null) cfg.TypeThingAddRandomPauses = AddRandomPausesCheck.IsChecked ?? true;
        if (TypeNewlinesCheck != null) cfg.TypeThingTypeNewlines = TypeNewlinesCheck.IsChecked ?? true;

        // Privacy settings
        if (EncryptConfigCheck != null) cfg.EncryptConfig = EncryptConfigCheck.IsChecked ?? false;

        // Update settings
        if (UpdateChannelCombo != null)
        {
            cfg.UpdateChannel = UpdateChannelCombo.SelectedIndex switch
            {
                0 => "stable",
                1 => "beta",
                2 => "disabled",
                _ => "stable"
            };
        }
        if (VerifyUpdateSignatureCheck != null) cfg.VerifyUpdateSignature = VerifyUpdateSignatureCheck.IsChecked ?? false;
    }

    private MessageBoxResult PromptUnsavedChanges()
    {
        return MessageBox.Show("You have unsaved changes. Close without saving?",
            "Unsaved Changes", MessageBoxButton.OKCancel, MessageBoxImage.Question);
    }

    private void GitHubButton_Click(object sender, RoutedEventArgs e)
    {
        Process.Start(new ProcessStartInfo
        {
            FileName = "https://github.com/ArMaTeC/Redball",
            UseShellExecute = true
        });
    }

    private void ThemeCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ThemeCombo is null || ThemeCombo.SelectedIndex < 0) return;

        var theme = ThemeCombo.SelectedIndex switch
        {
            0 => ThemeManager.IsSystemDarkMode() ? Theme.Dark : Theme.Light,
            1 => Theme.Dark,
            2 => Theme.Light,
            3 => Theme.MidnightBlue,
            4 => Theme.ForestGreen,
            5 => Theme.OceanBlue,
            6 => Theme.SunsetOrange,
            7 => Theme.RoyalPurple,
            8 => Theme.SlateGray,
            9 => Theme.RoseGold,
            10 => Theme.Cyberpunk,
            11 => Theme.Coffee,
            12 => Theme.ArcticFrost,
            _ => Theme.Dark
        };
        ThemeManager.SetTheme(theme);
        _isDirty = true;
    }

    private void OpenLogFolderButton_Click(object sender, RoutedEventArgs e)
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
            Logger.Error("SettingsWindow", "Failed to open log folder", ex);
            MessageBox.Show($"Could not open log folder: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
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
                MessageBox.Show($"Diagnostics exported to:\n{path}", "Export Complete", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }
        catch (Exception ex)
        {
            Logger.Error("SettingsWindow", "Failed to export diagnostics", ex);
            MessageBox.Show($"Export failed: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void HotkeyBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        e.Handled = true;
        var textBox = sender as TextBox;
        if (textBox == null) return;

        var key = e.Key == Key.System ? e.SystemKey : e.Key;

        // Ignore modifier-only presses
        if (key == Key.LeftCtrl || key == Key.RightCtrl ||
            key == Key.LeftAlt || key == Key.RightAlt ||
            key == Key.LeftShift || key == Key.RightShift ||
            key == Key.LWin || key == Key.RWin)
            return;

        var sb = new StringBuilder();
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control)) sb.Append("Ctrl+");
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Alt)) sb.Append("Alt+");
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift)) sb.Append("Shift+");
        sb.Append(key.ToString());

        textBox.Text = sb.ToString();
        _isDirty = true;
    }

    private void HotkeyBox_GotFocus(object sender, RoutedEventArgs e)
    {
        if (sender is TextBox tb)
            tb.Text = "Press a key combination...";

        // Suspend global hotkeys so we can capture the key combination
        _mainWindow?.SuspendHotkeys();
    }

    private void HotkeyBox_LostFocus(object sender, RoutedEventArgs e)
    {
        if (sender is TextBox tb && tb.Text == "Press a key combination...")
            tb.Text = tb.Name == "StartHotkeyBox" ? "Ctrl+Shift+V" : "Ctrl+Shift+X";

        // Resume global hotkeys after capture
        _mainWindow?.ResumeHotkeys();
    }

    private void Tab_Checked(object sender, RoutedEventArgs e)
    {
        // Hide all panels (null checks needed during XAML initialization)
        var panels = new[] { GeneralPanel, BehaviorPanel, FeaturesPanel, TypeThingPanel, UpdatesPanel, PrivacyPanel, AboutPanel };
        foreach (var p in panels)
        {
            if (p != null) p.Visibility = Visibility.Collapsed;
        }

        // Determine which panel to show
        StackPanel? target = null;
        if (sender == GeneralTab) target = GeneralPanel;
        else if (sender == BehaviorTab) target = BehaviorPanel;
        else if (sender == FeaturesTab) target = FeaturesPanel;
        else if (sender == TypeThingTab) target = TypeThingPanel;
        else if (sender == UpdatesTab) target = UpdatesPanel;
        else if (sender == PrivacyTab) target = PrivacyPanel;
        else if (sender == AboutTab) target = AboutPanel;

        // Show with fade-in animation
        if (target != null)
        {
            target.Opacity = 0;
            target.Visibility = Visibility.Visible;
            var fadeIn = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(200))
            {
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
            };
            target.BeginAnimation(OpacityProperty, fadeIn);
        }
    }

    private void BatteryThresholdSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (BatteryThresholdText != null)
            BatteryThresholdText.Text = $"Threshold: {(int)e.NewValue}%";
        _isDirty = true;
    }

    private void IdleThresholdSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (IdleThresholdText != null)
            IdleThresholdText.Text = $"Threshold: {(int)e.NewValue} minutes";
        _isDirty = true;
    }

    private void DurationSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (DurationText != null)
            DurationText.Text = $"Duration: {(int)e.NewValue} minutes";
        _isDirty = true;
    }

    private void TypingSpeedSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (TypingSpeedText != null)
            TypingSpeedText.Text = $"Speed: {(int)e.NewValue} ms per character";
        _isDirty = true;
    }

    private void MaxLogSizeSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (MaxLogSizeText != null)
            MaxLogSizeText.Text = $"Max log size: {(int)e.NewValue} MB";
        _isDirty = true;
    }

    private void ShowNotificationsCheck_Checked(object sender, RoutedEventArgs e)
    {
        if (NotificationModeLabel != null) NotificationModeLabel.Visibility = Visibility.Visible;
        if (NotificationModeCombo != null) NotificationModeCombo.Visibility = Visibility.Visible;
        _isDirty = true;
    }

    private void ShowNotificationsCheck_Unchecked(object sender, RoutedEventArgs e)
    {
        if (NotificationModeLabel != null) NotificationModeLabel.Visibility = Visibility.Collapsed;
        if (NotificationModeCombo != null) NotificationModeCombo.Visibility = Visibility.Collapsed;
        _isDirty = true;
    }

    private void ExportAllDataButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var dialog = new SaveFileDialog
            {
                Filter = "ZIP files (*.zip)|*.zip|All files (*.*)|*.*",
                FileName = $"redball_data_export_{DateTime.Now:yyyyMMdd_HHmmss}.zip",
                Title = "Export All My Data (GDPR)"
            };

            if (dialog.ShowDialog() == true)
            {
                var success = DataExportService.ExportAll(dialog.FileName);
                if (success)
                {
                    _analytics.TrackFeature("privacy.data_exported");
                    MessageBox.Show($"Your data has been exported to:\n{dialog.FileName}", "Export Complete", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                else
                {
                    MessageBox.Show("Failed to export data. Check the log for details.", "Export Failed", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }
        catch (Exception ex)
        {
            Logger.Error("SettingsWindow", "Failed to export all data", ex);
            MessageBox.Show($"Export failed: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
}
