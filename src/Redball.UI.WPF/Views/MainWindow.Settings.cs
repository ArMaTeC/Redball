using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Security.Principal;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using Redball.UI.Services;
using Redball.UI.ViewModels;

namespace Redball.UI.Views;

/// <summary>
/// Partial class: Settings loading, auto-apply, slider/hotkey event handlers.
/// </summary>
public partial class MainWindow
{
    private readonly Stack<string> _settingsUndoStack = new(20);
    private bool _suppressVerifySignaturePrompt;
    private bool _isSettingsDirty;

    private void LoadEmbeddedSettings()
    {
        _isLoadingSettings = true;
        var config = ConfigService.Instance.Config;

        MainMinimizeToTrayCheck.IsChecked = config.MinimizeToTray;
        MainStartWithWindowsCheck.IsChecked = StartupService.IsInstalledAtStartup();
        UpdateMainStartWithWindowsStatusText();
        MainShowNotificationsCheck.IsChecked = config.ShowNotifications;
        MainSoundNotificationsCheck.IsChecked = config.SoundNotifications;
        MainNotificationModeCombo.SelectedIndex = (int)config.NotificationMode;
        MainVerboseLoggingCheck.IsChecked = config.VerboseLogging;
        MainConfirmOnExitCheck.IsChecked = config.ConfirmOnExit;
        MainEncryptConfigCheck.IsChecked = config.EncryptConfig;
        MainEnableTelemetryCheck.IsChecked = config.EnableTelemetry;
        MainEnablePerformanceMetricsCheck.IsChecked = config.EnablePerformanceMetrics;
        MainProcessIsolationCheck.IsChecked = config.ProcessIsolation;
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
        MainDurationCustomBox.Text = config.DefaultDuration.ToString();
        MainAutoExitOnCompleteCheck.IsChecked = config.AutoExitOnComplete;

        MainBatteryAwareCheck.IsChecked = config.BatteryAware;
        MainBatteryThresholdSlider.Value = config.BatteryThreshold;
        MainBatteryThresholdText.Text = $"Threshold: {config.BatteryThreshold}%";
        MainNetworkAwareCheck.IsChecked = config.NetworkAware;
        MainIdleDetectionCheck.IsChecked = config.IdleDetection;
        MainIdleThresholdSlider.Value = config.IdleThreshold;
        MainIdleThresholdText.Text = $"Threshold: {config.IdleThreshold} minutes";
        MainPresentationModeCheck.IsChecked = config.PresentationMode;
        MainGamingModeEnabledCheck.IsChecked = config.GamingModeEnabled;
        MainMeetingAwareCheck.IsChecked = config.MeetingAware;
        MainThermalProtectionEnabledCheck.IsChecked = config.ThermalProtectionEnabled;
        MainThermalThresholdSlider.Value = config.ThermalThreshold;
        MainThermalThresholdText.Text = $"Threshold: {config.ThermalThreshold}C";
        MainScheduledOperationCheck.IsChecked = config.ScheduledOperation;
        MainScheduleStartTimeBox.Text = config.ScheduleStartTime;
        MainScheduleStopTimeBox.Text = config.ScheduleStopTime;
        MainScheduleMonCheck.IsChecked = config.ScheduleDays?.Contains("Monday") ?? true;
        MainScheduleTueCheck.IsChecked = config.ScheduleDays?.Contains("Tuesday") ?? true;
        MainScheduleWedCheck.IsChecked = config.ScheduleDays?.Contains("Wednesday") ?? true;
        MainScheduleThuCheck.IsChecked = config.ScheduleDays?.Contains("Thursday") ?? true;
        MainScheduleFriCheck.IsChecked = config.ScheduleDays?.Contains("Friday") ?? true;
        MainScheduleSatCheck.IsChecked = config.ScheduleDays?.Contains("Saturday") ?? false;
        MainScheduleSunCheck.IsChecked = config.ScheduleDays?.Contains("Sunday") ?? false;
        MainScheduleDetailsPanel.Visibility = config.ScheduledOperation ? Visibility.Visible : Visibility.Collapsed;
        MainPauseOnScreenLockCheck.IsChecked = config.PauseOnScreenLock;
        MainVpnAutoKeepAwakeCheck.IsChecked = config.VpnAutoKeepAwake;
        MainAppRulesEnabledCheck.IsChecked = config.AppRulesEnabled;
        MainKeepAwakeAppsBox.Text = config.KeepAwakeApps;
        MainPauseAppsBox.Text = config.PauseApps;
        MainPowerPlanAutoSwitchCheck.IsChecked = config.PowerPlanAutoSwitch;
        MainPowerPlanText.Text = $"Current plan: {PowerPlanService.Instance.ActivePlanName ?? "Unknown"}";
        MainWifiProfileSwitchCheck.IsChecked = config.WifiProfileSwitchEnabled;
        MainWifiProfileMappingsBox.Text = config.WifiProfileMappings;
        MainRestartReminderCheck.IsChecked = config.RestartReminderEnabled;
        MainRestartReminderDaysSlider.Value = config.RestartReminderDays;
        MainRestartReminderDaysText.Text = $"Remind after: {config.RestartReminderDays} days";
        MainAutoRestartCheck.IsChecked = config.AutoRestartEnabled;
        MainUptimeStatusText.Text = ScheduledRestartService.Instance.GetStatusText();

        if (config.AppRulesEnabled)
            ForegroundAppService.Instance.Start();
        else
            ForegroundAppService.Instance.Stop();

        // Start/stop session lock service based on config
        if (config.PauseOnScreenLock)
            SessionLockService.Instance.Start();
        else
            SessionLockService.Instance.Stop();

        MainEnableTypeThingCheck.IsChecked = config.TypeThingEnabled;
        MainUseLowLevelHotkeyCheck.IsChecked = config.UseLowLevelHotkey;
        MainTypeThingInputModeCombo.SelectedIndex = config.TypeThingInputMode?.ToUpperInvariant() switch
        {
            "SERVICE" => 1,
            _ => 0
        };
        RefreshServiceInstallVisibility();
        MainStartHotkeyBox.Text = config.TypeThingStartHotkey;
        MainStopHotkeyBox.Text = config.TypeThingStopHotkey;
        MainTypingSpeedSlider.Value = config.TypeThingMaxDelayMs;
        MainTypingSpeedText.Text = $"Speed: {config.TypeThingMaxDelayMs} ms per character";
        MainAddRandomPausesCheck.IsChecked = config.TypeThingAddRandomPauses;
        MainTypeNewlinesCheck.IsChecked = config.TypeThingTypeNewlines;
        MainTypeThingNotificationsCheck.IsChecked = config.TypeThingNotifications;
        MainTypeThingTtsCheck.IsChecked = config.TypeThingTtsEnabled;
        TextToSpeechService.Instance.IsEnabled = config.TypeThingTtsEnabled;
        MainTypeThingMinDelaySlider.Value = config.TypeThingMinDelayMs;
        MainTypeThingMinDelayText.Text = $"Min: {config.TypeThingMinDelayMs} ms";
        MainTypeThingStartDelaySlider.Value = config.TypeThingStartDelaySec;
        MainTypeThingStartDelayText.Text = $"Countdown: {config.TypeThingStartDelaySec} seconds";
        MainTypeThingPauseChanceSlider.Value = config.TypeThingRandomPauseChance;
        MainTypeThingPauseChanceText.Text = $"Chance: {config.TypeThingRandomPauseChance}%";
        MainTypeThingPauseMaxSlider.Value = config.TypeThingRandomPauseMaxMs;
        MainTypeThingPauseMaxText.Text = $"Max pause: {config.TypeThingRandomPauseMaxMs} ms";

        MainMiniWidgetAlwaysOnTopCheck.IsChecked = config.MiniWidgetAlwaysOnTop;
        MainMiniWidgetOpacitySlider.Value = Math.Clamp(config.MiniWidgetOpacityPercent, 35, 100);
        MainMiniWidgetOpacityText.Text = $"Opacity: {(int)MainMiniWidgetOpacitySlider.Value}%";
        MainMiniWidgetShowQuickActionsCheck.IsChecked = config.MiniWidgetShowQuickActions;
        MainMiniWidgetShowStatusIconsCheck.IsChecked = config.MiniWidgetShowStatusIcons;
        MainMiniWidgetDoubleClickDashboardCheck.IsChecked = config.MiniWidgetDoubleClickOpensDashboard;
        MainMiniWidgetOpenOnStartupCheck.IsChecked = config.MiniWidgetOpenOnStartup;
        MainMiniWidgetLockPositionCheck.IsChecked = config.MiniWidgetLockPosition;
        MainMiniWidgetSnapToEdgesCheck.IsChecked = config.MiniWidgetSnapToScreenEdges;
        MainMiniWidgetKeyboardShortcutsCheck.IsChecked = config.MiniWidgetEnableKeyboardShortcuts;
        MainMiniWidgetCustomQuickMinutesSlider.Value = Math.Clamp(config.MiniWidgetCustomQuickMinutes, 1, 180);
        MainMiniWidgetCustomQuickMinutesText.Text = $"Custom quick action: +{(int)MainMiniWidgetCustomQuickMinutesSlider.Value}m";
        MainMiniWidgetConfirmCloseWhenActiveCheck.IsChecked = config.MiniWidgetConfirmCloseWhenActive;
        SetMiniWidgetPresetSelection(config.MiniWidgetPreset);

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
        MainAutoUpdateCheckEnabledCheck.IsChecked = config.AutoUpdateCheckEnabled;
        MainAutoUpdateIntervalSlider.Value = Math.Max(30, config.AutoUpdateCheckIntervalMinutes);
        UpdateAutoUpdateIntervalText((int)MainAutoUpdateIntervalSlider.Value);
        MainCurrentVersionText.Text = GetCurrentVersionText();

        MainNotificationModeLabel.Visibility = config.ShowNotifications ? Visibility.Visible : Visibility.Collapsed;
        MainNotificationModeCombo.Visibility = config.ShowNotifications ? Visibility.Visible : Visibility.Collapsed;

        _updateService = new UpdateService(
            config.UpdateRepoOwner,
            config.UpdateRepoName,
            config.UpdateChannel ?? "stable",
            config.VerifyUpdateSignature,
            "https://redball.certrunnerx.com/");
        _isLoadingSettings = false;
        RefreshProfileCombo();
    }

    private void AutoApplySettings()
    {
        if (_isLoadingSettings) return;
        try
        {
            // Snapshot current config for undo before applying changes
            var snapshot = JsonSerializer.Serialize(ConfigService.Instance.Config);
            if (_settingsUndoStack.Count == 0 || _settingsUndoStack.Peek() != snapshot)
                _settingsUndoStack.Push(snapshot);

            var config = ConfigService.Instance.Config;
            config.MinimizeToTray = MainMinimizeToTrayCheck.IsChecked ?? false;
            var startWithWindows = MainStartWithWindowsCheck.IsChecked ?? false;
            var startupInstalled = StartupService.IsInstalledAtStartup();
            if (startWithWindows && !startupInstalled)
            {
                StartupService.Install();
            }
            else if (!startWithWindows && startupInstalled)
            {
                StartupService.Uninstall();
            }
            UpdateMainStartWithWindowsStatusText();
            config.ShowNotifications = MainShowNotificationsCheck.IsChecked ?? true;
            config.SoundNotifications = MainSoundNotificationsCheck.IsChecked ?? false;
            config.NotificationMode = (NotificationMode)MainNotificationModeCombo.SelectedIndex;
            config.VerboseLogging = MainVerboseLoggingCheck.IsChecked ?? false;
            config.ConfirmOnExit = MainConfirmOnExitCheck.IsChecked ?? true;
            config.EncryptConfig = MainEncryptConfigCheck.IsChecked ?? true;
            config.EnableTelemetry = MainEnableTelemetryCheck.IsChecked ?? false;
            config.EnablePerformanceMetrics = MainEnablePerformanceMetricsCheck.IsChecked ?? false;
            config.ProcessIsolation = MainProcessIsolationCheck.IsChecked ?? false;
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
            config.GamingModeEnabled = MainGamingModeEnabledCheck.IsChecked ?? false;
            config.MeetingAware = MainMeetingAwareCheck.IsChecked ?? false;
            config.ThermalProtectionEnabled = MainThermalProtectionEnabledCheck.IsChecked ?? false;
            config.ThermalThreshold = (int)MainThermalThresholdSlider.Value;
            config.ScheduledOperation = MainScheduledOperationCheck.IsChecked ?? false;
            config.PauseOnScreenLock = MainPauseOnScreenLockCheck.IsChecked ?? false;
            config.VpnAutoKeepAwake = MainVpnAutoKeepAwakeCheck.IsChecked ?? false;
            config.AppRulesEnabled = MainAppRulesEnabledCheck.IsChecked ?? false;
            config.KeepAwakeApps = MainKeepAwakeAppsBox.Text ?? "";
            config.PauseApps = MainPauseAppsBox.Text ?? "";
            config.PowerPlanAutoSwitch = MainPowerPlanAutoSwitchCheck.IsChecked ?? false;
            config.WifiProfileSwitchEnabled = MainWifiProfileSwitchCheck.IsChecked ?? false;
            config.WifiProfileMappings = MainWifiProfileMappingsBox.Text ?? "";
            config.RestartReminderEnabled = MainRestartReminderCheck.IsChecked ?? false;
            config.RestartReminderDays = (int)MainRestartReminderDaysSlider.Value;
            config.AutoRestartEnabled = MainAutoRestartCheck.IsChecked ?? false;
            if (config.AppRulesEnabled)
                ForegroundAppService.Instance.Start();
            else
                ForegroundAppService.Instance.Stop();
            if (config.PauseOnScreenLock)
                SessionLockService.Instance.Start();
            else
                SessionLockService.Instance.Stop();
            config.ScheduleStartTime = MainScheduleStartTimeBox.Text ?? "09:00";
            config.ScheduleStopTime = MainScheduleStopTimeBox.Text ?? "18:00";
            config.ScheduleDays = new List<string>();
            if (MainScheduleMonCheck.IsChecked == true) config.ScheduleDays.Add("Monday");
            if (MainScheduleTueCheck.IsChecked == true) config.ScheduleDays.Add("Tuesday");
            if (MainScheduleWedCheck.IsChecked == true) config.ScheduleDays.Add("Wednesday");
            if (MainScheduleThuCheck.IsChecked == true) config.ScheduleDays.Add("Thursday");
            if (MainScheduleFriCheck.IsChecked == true) config.ScheduleDays.Add("Friday");
            if (MainScheduleSatCheck.IsChecked == true) config.ScheduleDays.Add("Saturday");
            if (MainScheduleSunCheck.IsChecked == true) config.ScheduleDays.Add("Sunday");

            config.TypeThingEnabled = MainEnableTypeThingCheck.IsChecked ?? true;
            config.UseLowLevelHotkey = MainUseLowLevelHotkeyCheck.IsChecked ?? false;
            config.TypeThingInputMode = MainTypeThingInputModeCombo.SelectedIndex switch
            {
                1 => "Service",
                _ => "SendInput"
            };

            RefreshServiceInstallVisibility();
            config.TypeThingStartHotkey = MainStartHotkeyBox.Text;
            config.TypeThingStopHotkey = MainStopHotkeyBox.Text;
            config.TypeThingMaxDelayMs = (int)MainTypingSpeedSlider.Value;
            config.TypeThingMinDelayMs = Math.Max(1, Math.Min(config.TypeThingMinDelayMs, config.TypeThingMaxDelayMs));
            config.TypeThingAddRandomPauses = MainAddRandomPausesCheck.IsChecked ?? true;
            config.TypeThingTypeNewlines = MainTypeNewlinesCheck.IsChecked ?? true;
            config.TypeThingNotifications = MainTypeThingNotificationsCheck.IsChecked ?? true;
            config.TypeThingTtsEnabled = MainTypeThingTtsCheck.IsChecked ?? false;
            TextToSpeechService.Instance.IsEnabled = config.TypeThingTtsEnabled;
            config.TypeThingMinDelayMs = (int)MainTypeThingMinDelaySlider.Value;
            config.TypeThingStartDelaySec = (int)MainTypeThingStartDelaySlider.Value;
            config.TypeThingRandomPauseChance = (int)MainTypeThingPauseChanceSlider.Value;
            config.TypeThingRandomPauseMaxMs = (int)MainTypeThingPauseMaxSlider.Value;

            config.MiniWidgetAlwaysOnTop = MainMiniWidgetAlwaysOnTopCheck.IsChecked ?? true;
            config.MiniWidgetOpacityPercent = (int)Math.Round(MainMiniWidgetOpacitySlider.Value);
            config.MiniWidgetShowQuickActions = MainMiniWidgetShowQuickActionsCheck.IsChecked ?? true;
            config.MiniWidgetShowStatusIcons = MainMiniWidgetShowStatusIconsCheck.IsChecked ?? true;
            config.MiniWidgetDoubleClickOpensDashboard = MainMiniWidgetDoubleClickDashboardCheck.IsChecked ?? true;
            config.MiniWidgetOpenOnStartup = MainMiniWidgetOpenOnStartupCheck.IsChecked ?? false;
            config.MiniWidgetLockPosition = MainMiniWidgetLockPositionCheck.IsChecked ?? false;
            config.MiniWidgetSnapToScreenEdges = MainMiniWidgetSnapToEdgesCheck.IsChecked ?? true;
            config.MiniWidgetEnableKeyboardShortcuts = MainMiniWidgetKeyboardShortcutsCheck.IsChecked ?? true;
            config.MiniWidgetCustomQuickMinutes = (int)Math.Round(MainMiniWidgetCustomQuickMinutesSlider.Value);
            config.MiniWidgetConfirmCloseWhenActive = MainMiniWidgetConfirmCloseWhenActiveCheck.IsChecked ?? true;
            config.MiniWidgetPreset = GetSelectedMiniWidgetPreset();

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
            config.AutoUpdateCheckEnabled = MainAutoUpdateCheckEnabledCheck.IsChecked ?? true;
            config.AutoUpdateCheckIntervalMinutes = (int)MainAutoUpdateIntervalSlider.Value;

            ConfigService.Instance.Save();
            ThemeManager.SetThemeFromConfig(config.Theme);
            Logger.ApplyConfig(config);
            KeepAwakeService.Instance.ReloadConfig();
            KeepAwakeService.Instance.PreventDisplaySleep = config.PreventDisplaySleep;
            KeepAwakeService.Instance.UseHeartbeat = config.UseHeartbeatKeypress;
            KeepAwakeService.Instance.HeartbeatInputMode = Enum.TryParse<HeartbeatInputMode>(config.HeartbeatInputMode, true, out var heartbeatMode)
                ? heartbeatMode
                : HeartbeatInputMode.F15;
            _viewModel?.RefreshStatus();
            _analytics.TrackFeature("settings.saved");
            ReloadHotkeys();
            Logger.Debug("MainWindow", "Settings auto-applied");
        }
        catch (Exception ex)
        {
            Logger.Error("MainWindow", "Failed to auto-apply settings", ex);
        }
    }

    private void MainShowNotificationsCheck_Checked(object sender, RoutedEventArgs e)
    {
        MainNotificationModeLabel.Visibility = Visibility.Visible;
        MainNotificationModeCombo.Visibility = Visibility.Visible;
        AutoApplySettings();
    }

    private string GetSelectedMiniWidgetPreset()
    {
        return MainMiniWidgetPresetCombo?.SelectedIndex switch
        {
            1 => MiniWidgetPresetService.Focus,
            2 => MiniWidgetPresetService.Meeting,
            3 => MiniWidgetPresetService.BatterySafe,
            _ => MiniWidgetPresetService.Custom
        };
    }

    private void SetMiniWidgetPresetSelection(string? preset)
    {
        if (MainMiniWidgetPresetCombo == null)
        {
            return;
        }

        MainMiniWidgetPresetCombo.SelectedIndex = MiniWidgetPresetService.NormalizePreset(preset) switch
        {
            MiniWidgetPresetService.Focus => 1,
            MiniWidgetPresetService.Meeting => 2,
            MiniWidgetPresetService.BatterySafe => 3,
            _ => 0
        };
    }

    private void ApplyMiniWidgetControlsFromPreset(string preset)
    {
        var presetConfig = new RedballConfig();
        MiniWidgetPresetService.ApplyPreset(presetConfig, preset);

        MainMiniWidgetAlwaysOnTopCheck.IsChecked = presetConfig.MiniWidgetAlwaysOnTop;
        MainMiniWidgetOpacitySlider.Value = presetConfig.MiniWidgetOpacityPercent;
        MainMiniWidgetShowQuickActionsCheck.IsChecked = presetConfig.MiniWidgetShowQuickActions;
        MainMiniWidgetShowStatusIconsCheck.IsChecked = presetConfig.MiniWidgetShowStatusIcons;
        MainMiniWidgetDoubleClickDashboardCheck.IsChecked = presetConfig.MiniWidgetDoubleClickOpensDashboard;
        MainMiniWidgetOpenOnStartupCheck.IsChecked = presetConfig.MiniWidgetOpenOnStartup;
        MainMiniWidgetLockPositionCheck.IsChecked = presetConfig.MiniWidgetLockPosition;
        MainMiniWidgetSnapToEdgesCheck.IsChecked = presetConfig.MiniWidgetSnapToScreenEdges;
        MainMiniWidgetKeyboardShortcutsCheck.IsChecked = presetConfig.MiniWidgetEnableKeyboardShortcuts;
        MainMiniWidgetCustomQuickMinutesSlider.Value = Math.Clamp(presetConfig.MiniWidgetCustomQuickMinutes, 1, 180);
        MainMiniWidgetConfirmCloseWhenActiveCheck.IsChecked = presetConfig.MiniWidgetConfirmCloseWhenActive;
    }

    private void MainMiniWidgetApplyPresetButton_Click(object sender, RoutedEventArgs e)
    {
        if (_isLoadingSettings)
        {
            return;
        }

        var preset = GetSelectedMiniWidgetPreset();

        _isLoadingSettings = true;
        try
        {
            ApplyMiniWidgetControlsFromPreset(preset);
            SetMiniWidgetPresetSelection(preset);
        }
        finally
        {
            _isLoadingSettings = false;
        }

        AutoApplySettings();
        NotificationService.Instance.ShowInfo("Mini Widget", $"Applied '{preset}' preset.");
    }

    private void MainMiniWidgetCustomQuickMinutesSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (MainMiniWidgetCustomQuickMinutesText != null)
        {
            MainMiniWidgetCustomQuickMinutesText.Text = $"Custom quick action: +{(int)e.NewValue}m";
        }

        MarkMiniWidgetPresetAsCustomFromManualChange(sender);
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
        if (MainDurationCustomBox != null && !MainDurationCustomBox.IsFocused)
        {
            MainDurationCustomBox.Text = ((int)e.NewValue).ToString();
        }
        AutoApplySettings();
    }

    private void MainDurationCustomBox_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        if (_isLoadingSettings) return;
        if (int.TryParse(MainDurationCustomBox.Text, out var mins) && mins >= 1 && mins <= 1440)
        {
            if (mins <= MainDurationSlider.Maximum)
                MainDurationSlider.Value = mins;
            else
                MainDurationSlider.Value = MainDurationSlider.Maximum;
            ConfigService.Instance.Config.DefaultDuration = mins;
        }
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

    private void MainAutoUpdateIntervalSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        UpdateAutoUpdateIntervalText((int)e.NewValue);
        AutoApplySettings();
    }

    private void MainMiniWidgetOpacitySlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (MainMiniWidgetOpacityText != null)
        {
            MainMiniWidgetOpacityText.Text = $"Opacity: {(int)e.NewValue}%";
        }

        MarkMiniWidgetPresetAsCustomFromManualChange(sender);
        AutoApplySettings();
    }

    private bool IsMiniWidgetPresetManagedControl(object sender)
    {
        return ReferenceEquals(sender, MainMiniWidgetAlwaysOnTopCheck)
            || ReferenceEquals(sender, MainMiniWidgetShowQuickActionsCheck)
            || ReferenceEquals(sender, MainMiniWidgetShowStatusIconsCheck)
            || ReferenceEquals(sender, MainMiniWidgetDoubleClickDashboardCheck)
            || ReferenceEquals(sender, MainMiniWidgetOpenOnStartupCheck)
            || ReferenceEquals(sender, MainMiniWidgetLockPositionCheck)
            || ReferenceEquals(sender, MainMiniWidgetSnapToEdgesCheck)
            || ReferenceEquals(sender, MainMiniWidgetKeyboardShortcutsCheck)
            || ReferenceEquals(sender, MainMiniWidgetConfirmCloseWhenActiveCheck)
            || ReferenceEquals(sender, MainMiniWidgetOpacitySlider)
            || ReferenceEquals(sender, MainMiniWidgetCustomQuickMinutesSlider);
    }

    private void MarkMiniWidgetPresetAsCustomFromManualChange(object sender)
    {
        if (_isLoadingSettings || !IsMiniWidgetPresetManagedControl(sender))
        {
            return;
        }

        if (GetSelectedMiniWidgetPreset() == MiniWidgetPresetService.Custom)
        {
            return;
        }

        SetMiniWidgetPresetSelection(MiniWidgetPresetService.Custom);
    }

    private void UpdateAutoUpdateIntervalText(int minutes)
    {
        if (MainAutoUpdateIntervalText == null) return;
        if (minutes >= 60 && minutes % 60 == 0)
            MainAutoUpdateIntervalText.Text = minutes == 60 ? "Interval: every hour" : $"Interval: every {minutes / 60} hours";
        else if (minutes > 60)
            MainAutoUpdateIntervalText.Text = $"Interval: every {minutes / 60}h {minutes % 60}m";
        else
            MainAutoUpdateIntervalText.Text = $"Interval: every {minutes} minutes";
    }

    private void MainSettingChanged(object sender, RoutedEventArgs e)
    {
        // Track dirty state for Updates panel settings that require explicit save
        if (IsUpdatesPanelSetting(sender))
        {
            _isSettingsDirty = true;
            if (MainSaveSettingsButton != null)
                MainSaveSettingsButton.IsEnabled = true;
            return; // Don't auto-apply; wait for explicit save
        }

        if (ReferenceEquals(sender, MainVerifyUpdateSignatureCheck)
            && !_isLoadingSettings
            && !_suppressVerifySignaturePrompt
            && MainVerifyUpdateSignatureCheck.IsChecked != true
            && ConfigService.Instance.Config.VerifyUpdateSignature)
        {
            var confirmDisable = NotificationWindow.Show(
                "Security Warning",
                "Disabling update signature verification reduces security and can allow untrusted updates. Continue?",
                "\uE7BA",
                true);

            if (!confirmDisable)
            {
                _suppressVerifySignaturePrompt = true;
                MainVerifyUpdateSignatureCheck.IsChecked = true;
                _suppressVerifySignaturePrompt = false;
                return;
            }
        }

        if (ReferenceEquals(sender, MainStartWithWindowsCheck))
        {
            UpdateMainStartWithWindowsStatusText();
        }

        MarkMiniWidgetPresetAsCustomFromManualChange(sender);

        AutoApplySettings();
    }

    /// <summary>
    /// Determines if a setting is part of the Updates panel and requires explicit save.
    /// </summary>
    private bool IsUpdatesPanelSetting(object sender)
    {
        return ReferenceEquals(sender, MainVerifyUpdateSignatureCheck)
            || ReferenceEquals(sender, MainAutoUpdateCheckEnabledCheck)
            || ReferenceEquals(sender, MainUpdateChannelCombo);
    }

    /// <summary>
    /// Saves settings from the Updates panel with confirmation dialogs for security-critical changes.
    /// Matches the behavior of SettingsWindow save flow.
    /// </summary>
    private void MainSaveSettingsButton_Click(object sender, RoutedEventArgs e)
    {
        if (!_isSettingsDirty)
            return;

        var config = ConfigService.Instance.Config;
        var wasVerifySignatureEnabled = config.VerifyUpdateSignature;
        var wasEncryptConfigEnabled = config.EncryptConfig;

        // Capture new values from UI
        var newVerifySignature = MainVerifyUpdateSignatureCheck.IsChecked ?? false;
        var newUpdateChannel = MainUpdateChannelCombo.SelectedIndex switch
        {
            1 => "beta",
            2 => "disabled",
            _ => "stable"
        };
        var newAutoUpdateCheck = MainAutoUpdateCheckEnabledCheck.IsChecked ?? true;
        var newAutoUpdateInterval = (int)MainAutoUpdateIntervalSlider.Value;

        // Security confirmation: VerifyUpdateSignature being disabled
        if (wasVerifySignatureEnabled && !newVerifySignature)
        {
            var confirmDisable = NotificationWindow.Show(
                "Security Warning",
                "Disabling update signature verification reduces security and can allow untrusted updates. Continue?",
                "\uE7BA",
                true);

            if (!confirmDisable)
            {
                MainVerifyUpdateSignatureCheck.IsChecked = true;
                return;
            }
        }

        // Apply the settings
        config.VerifyUpdateSignature = newVerifySignature;
        config.UpdateChannel = newUpdateChannel;
        config.AutoUpdateCheckEnabled = newAutoUpdateCheck;
        config.AutoUpdateCheckIntervalMinutes = newAutoUpdateInterval;

        // Reinitialize update service with new settings
        _updateService = new UpdateService(
            config.UpdateRepoOwner,
            config.UpdateRepoName,
            config.UpdateChannel ?? "stable",
            config.VerifyUpdateSignature,
            "https://redball.certrunnerx.com/");

        ConfigService.Instance.Save();
        _isSettingsDirty = false;
        if (MainSaveSettingsButton != null)
            MainSaveSettingsButton.IsEnabled = false;

        NotificationService.Instance.ShowInfo("Settings Saved", "Update settings saved successfully.");
        Logger.Info("MainWindow", "Update settings saved via explicit Save button");
    }

    private void UpdateMainStartWithWindowsStatusText()
    {
        if (MainStartWithWindowsStatusText == null) return;
        var installed = StartupService.IsInstalledAtStartup();
        MainStartWithWindowsStatusText.Text = installed
            ? "Startup status: Enabled"
            : "Startup status: Disabled";
    }

    private void MainComboSettingChanged(object sender, SelectionChangedEventArgs e)
    {
        // Track dirty state for Updates panel settings that require explicit save
        if (IsUpdatesPanelSetting(sender))
        {
            _isSettingsDirty = true;
            if (MainSaveSettingsButton != null)
                MainSaveSettingsButton.IsEnabled = true;
            return; // Don't auto-apply; wait for explicit save
        }

        if (ReferenceEquals(sender, MainTypeThingInputModeCombo))
        {
            RefreshServiceInstallVisibility();
        }
        AutoApplySettings();
    }

    private void MainThermalThresholdSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (MainThermalThresholdText != null)
        {
            MainThermalThresholdText.Text = $"Threshold: {(int)e.NewValue}C";
        }
        AutoApplySettings();
    }

    private void MainScheduledOperationCheck_Checked(object sender, RoutedEventArgs e)
    {
        if (MainScheduleDetailsPanel != null)
            MainScheduleDetailsPanel.Visibility = Visibility.Visible;
        AutoApplySettings();
    }

    private void MainScheduledOperationCheck_Unchecked(object sender, RoutedEventArgs e)
    {
        if (MainScheduleDetailsPanel != null)
            MainScheduleDetailsPanel.Visibility = Visibility.Collapsed;
        AutoApplySettings();
    }

    private void MainTypeThingMinDelaySlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (MainTypeThingMinDelayText != null)
        {
            MainTypeThingMinDelayText.Text = $"Min: {(int)e.NewValue} ms";
        }
        AutoApplySettings();
    }

    private void MainTypeThingStartDelaySlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (MainTypeThingStartDelayText != null)
        {
            MainTypeThingStartDelayText.Text = $"Countdown: {(int)e.NewValue} seconds";
        }
        AutoApplySettings();
    }

    private void MainTypeThingPauseChanceSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (MainTypeThingPauseChanceText != null)
        {
            MainTypeThingPauseChanceText.Text = $"Chance: {(int)e.NewValue}%";
        }
        AutoApplySettings();
    }

    private void MainTypeThingPauseMaxSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (MainTypeThingPauseMaxText != null)
        {
            MainTypeThingPauseMaxText.Text = $"Max pause: {(int)e.NewValue} ms";
        }
        AutoApplySettings();
    }

    private void SettingsUndoButton_Click(object sender, RoutedEventArgs e)
    {
        if (_settingsUndoStack.Count == 0)
        {
            NotificationService.Instance.ShowInfo("Settings", "Nothing to undo.");
            return;
        }

        try
        {
            var json = _settingsUndoStack.Pop();
            var restored = JsonSerializer.Deserialize<RedballConfig>(json);
            if (restored != null)
            {
                ConfigService.Instance.Config = restored;
                LoadEmbeddedSettings();
                ConfigService.Instance.Save();
                Logger.Info("MainWindow", "Settings undone");
            }
        }
        catch (Exception ex)
        {
            Logger.Error("MainWindow", "Settings undo failed", ex);
        }
    }

    private void SettingsResetButton_Click(object sender, RoutedEventArgs e)
    {
        var result = NotificationWindow.Show(
            "Reset Settings",
            "Reset all settings to their default values? This action cannot be easily undone.",
            "\uE7BA", // Warning icon
            true);

        if (!result) return;

        // Snapshot current for undo
        _settingsUndoStack.Push(JsonSerializer.Serialize(ConfigService.Instance.Config));

        ConfigService.Instance.Config = new RedballConfig();
        LoadEmbeddedSettings();
        ConfigService.Instance.Save();
        Logger.Info("MainWindow", "Settings reset to defaults");
        _analytics.TrackFeature("settings.reset_defaults");
    }

    private void RefreshProfileCombo()
    {
        var profiles = ProfileService.Instance.GetProfileNames();
        ProfileCombo.Items.Clear();
        foreach (var p in profiles)
            ProfileCombo.Items.Add(new System.Windows.Controls.ComboBoxItem { Content = p });

        var active = ProfileService.Instance.ActiveProfileName;
        ProfileActiveText.Text = string.IsNullOrEmpty(active) ? "" : $"Active profile: {active}";
    }

    private void ProfileLoad_Click(object sender, RoutedEventArgs e)
    {
        if (ProfileCombo.SelectedItem is System.Windows.Controls.ComboBoxItem item && item.Content is string name)
        {
            _settingsUndoStack.Push(JsonSerializer.Serialize(ConfigService.Instance.Config));
            if (ProfileService.Instance.LoadProfile(name))
            {
                LoadEmbeddedSettings();
                RefreshProfileCombo();
                NotificationService.Instance.ShowInfo("Profiles", $"Profile \"{name}\" loaded.");
                _analytics.TrackFeature("profile.loaded");
            }
        }
    }

    private void ProfileSave_Click(object sender, RoutedEventArgs e)
    {
        var name = ShowInputDialog("Save Profile", "Enter a name for the profile:", ProfileService.Instance.ActiveProfileName);
        if (string.IsNullOrWhiteSpace(name)) return;

        if (ProfileService.Instance.SaveProfile(name))
        {
            RefreshProfileCombo();
            NotificationService.Instance.ShowInfo("Profiles", $"Profile \"{name}\" saved.");
            _analytics.TrackFeature("profile.saved");
        }
    }

    private string? ShowInputDialog(string title, string prompt, string defaultValue = "")
    {
        var win = new Window
        {
            Title = title,
            Width = 350, Height = 160,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Owner = this,
            ResizeMode = ResizeMode.NoResize,
            WindowStyle = WindowStyle.ToolWindow,
            Background = (System.Windows.Media.Brush)FindResource("BackgroundBrush"),
            Foreground = (System.Windows.Media.Brush)FindResource("ForegroundBrush")
        };
        var sp = new StackPanel { Margin = new Thickness(16) };
        sp.Children.Add(new TextBlock { Text = prompt, Margin = new Thickness(0, 0, 0, 8) });
        var tb = new TextBox { Text = defaultValue, Margin = new Thickness(0, 0, 0, 12) };
        sp.Children.Add(tb);
        var btnPanel = new StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var okBtn = new Button { Content = "OK", Width = 70, Margin = new Thickness(0, 0, 8, 0), IsDefault = true };
        var cancelBtn = new Button { Content = "Cancel", Width = 70, IsCancel = true };
        okBtn.Click += (_, _) => { win.DialogResult = true; win.Close(); };
        btnPanel.Children.Add(okBtn);
        btnPanel.Children.Add(cancelBtn);
        sp.Children.Add(btnPanel);
        win.Content = sp;
        tb.Focus();
        tb.SelectAll();
        return win.ShowDialog() == true ? tb.Text : null;
    }

    private void ProfileDelete_Click(object sender, RoutedEventArgs e)
    {
        if (ProfileCombo.SelectedItem is System.Windows.Controls.ComboBoxItem item && item.Content is string name)
        {
            var result = NotificationWindow.Show("Delete Profile", $"Delete profile \"{name}\"?", "\uE74D", true);
            if (!result) return;

            if (ProfileService.Instance.DeleteProfile(name))
            {
                RefreshProfileCombo();
                NotificationService.Instance.ShowInfo("Profiles", $"Profile \"{name}\" deleted.");
                _analytics.TrackFeature("profile.deleted");
            }
        }
    }

    private void ProcessWatcherStart_Click(object sender, RoutedEventArgs e)
    {
        var target = ProcessWatcherTargetBox.Text?.Trim();
        if (string.IsNullOrWhiteSpace(target))
        {
            ProcessWatcherStatusText.Text = "Enter a process name (e.g., OBS64, chrome, teams).";
            return;
        }

        ProcessWatcherService.Instance.Start(target);
        ProcessWatcherStatusText.Text = $"Watching for: {target}";

        var config = ConfigService.Instance.Config;
        config.ProcessWatcherEnabled = true;
        config.ProcessWatcherTarget = target;
        ConfigService.Instance.Save();
        _analytics.TrackFeature("processwatcher.started");
    }

    private void ProcessWatcherStop_Click(object sender, RoutedEventArgs e)
    {
        ProcessWatcherService.Instance.Stop();
        ProcessWatcherStatusText.Text = "Process watching stopped.";

        var config = ConfigService.Instance.Config;
        config.ProcessWatcherEnabled = false;
        ConfigService.Instance.Save();
        _analytics.TrackFeature("processwatcher.stopped");
    }

    private void MainAppRulesBox_LostFocus(object sender, RoutedEventArgs e)
    {
        if (_isLoadingSettings) return;
        AutoApplySettings();
    }

    private void MainRestartReminderDaysSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (MainRestartReminderDaysText != null)
            MainRestartReminderDaysText.Text = $"Remind after: {(int)e.NewValue} days";
        if (!_isLoadingSettings)
            AutoApplySettings();
    }

    private void SettingsSearchBox_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        var query = SettingsSearchBox.Text?.Trim() ?? "";
        var isEmpty = string.IsNullOrEmpty(query);

        // Iterate through SettingsPanel children, show/hide Border (card) elements
        foreach (var child in SettingsPanel.Children)
        {
            if (child is System.Windows.Controls.Border card)
            {
                if (isEmpty)
                {
                    card.Visibility = Visibility.Visible;
                    continue;
                }

                // Search all text content within the card
                var text = GetAllText(card);
                card.Visibility = text.Contains(query, StringComparison.OrdinalIgnoreCase)
                    ? Visibility.Visible
                    : Visibility.Collapsed;
            }
        }
    }

    private static string GetAllText(DependencyObject parent)
    {
        var sb = new System.Text.StringBuilder();
        var count = System.Windows.Media.VisualTreeHelper.GetChildrenCount(parent);
        for (int i = 0; i < count; i++)
        {
            var child = System.Windows.Media.VisualTreeHelper.GetChild(parent, i);
            if (child is System.Windows.Controls.TextBlock tb)
                sb.Append(tb.Text).Append(' ');
            else if (child is System.Windows.Controls.CheckBox cb)
                sb.Append(cb.Content?.ToString()).Append(' ');
            else if (child is System.Windows.Controls.ContentControl cc)
                sb.Append(cc.Content?.ToString()).Append(' ');
            sb.Append(GetAllText(child));
        }
        return sb.ToString();
    }

    private void MainKeepAwakeUntilSet_Click(object sender, RoutedEventArgs e)
    {
        var input = MainKeepAwakeUntilBox.Text?.Trim();
        if (string.IsNullOrEmpty(input))
        {
            MainKeepAwakeUntilStatus.Text = "Please enter a time in HH:mm format.";
            return;
        }

        if (!TimeSpan.TryParse(input, out var time))
        {
            MainKeepAwakeUntilStatus.Text = "Invalid format. Use HH:mm (e.g. 17:30).";
            return;
        }

        var target = DateTime.Today.Add(time);
        if (target <= DateTime.Now)
            target = target.AddDays(1); // If the time already passed today, set for tomorrow

        KeepAwakeService.Instance.SetActive(true, target);
        MainKeepAwakeUntilStatus.Text = $"Keeping awake until {target:HH:mm} ({(target - DateTime.Now).TotalMinutes:F0} min)";
        _viewModel?.RefreshStatus();
        _analytics.TrackFeature("keepawake.until_time_set");
        Logger.Info("MainWindow", $"Keep awake until set to {target:yyyy-MM-dd HH:mm}");
    }

    private void MainKeepAwakeUntilClear_Click(object sender, RoutedEventArgs e)
    {
        KeepAwakeService.Instance.SetActive(true); // Switch to indefinite
        MainKeepAwakeUntilBox.Text = "";
        MainKeepAwakeUntilStatus.Text = "Timed session cleared. Keep-awake is now indefinite.";
        _viewModel?.RefreshStatus();
        _analytics.TrackFeature("keepawake.until_time_cleared");
        Logger.Info("MainWindow", "Keep awake until cleared");
    }

    private void MainStopHotkeyBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        // Delegate to shared handler
        MainHotkeyBox_PreviewKeyDown(sender, e);
    }

    private void MainHotkeyBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        e.Handled = true;
        if (sender is not TextBox textBox)
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
        if (sender is TextBox textBox)
        {
            textBox.Text = "Press a key combination...";
        }

        SuspendHotkeys();
    }

    private void MainHotkeyBox_LostFocus(object sender, RoutedEventArgs e)
    {
        if (sender is TextBox textBox && textBox.Text == "Press a key combination...")
        {
            textBox.Text = textBox.Name == "MainStartHotkeyBox" ? "Ctrl+Shift+V" : "Ctrl+Shift+X";
        }

        ResumeHotkeys();
        AutoApplySettings();
    }

    private void MainStartHotkeyBox_GotFocus(object sender, RoutedEventArgs e)
    {
        if (sender is TextBox textBox)
        {
            textBox.Text = "Press a key combination...";
        }
        SuspendHotkeys();
    }

    private void MainStopHotkeyBox_GotFocus(object sender, RoutedEventArgs e)
    {
        if (sender is TextBox textBox)
        {
            textBox.Text = "Press a key combination...";
        }
        SuspendHotkeys();
    }

    private void RefreshServiceInstallVisibility()
    {
        var inputMode = MainTypeThingInputModeCombo.SelectedIndex;
        var serviceSelected = inputMode == 1; // Service mode (index 1)
        var serviceInstalled = ServiceInputProvider.Instance.RefreshServiceInstalledState();
        var serviceRunning = ServiceInputProvider.Instance.IsReady || 
                             ServiceInputProvider.Instance.GetDetailedServiceState().Status == ServiceInputProvider.ServiceStatus.Healthy;

        // Update button text and visibility based on selected mode
        if (serviceSelected)
        {
            MainInstallServiceBtn.Content = serviceInstalled
                ? (serviceRunning ? "Uninstall Service" : "Start Service")
                : "Install Service";
            MainInstallServiceBtn.Visibility = Visibility.Visible;
            MainUninstallServiceBtn.Visibility = Visibility.Collapsed;
            if (MainServiceAdminHintText != null)
            {
                MainServiceAdminHintText.Visibility = Visibility.Visible;
                MainServiceAdminHintText.Text = "Administrator approval (UAC) is required to install or uninstall the Input Service.";
            }
            
            MainInstallServiceBtn.ToolTip = serviceInstalled
                ? (serviceRunning 
                    ? "Uninstall the Redball Input Service."
                    : "Start the Redball Input Service (admin required).")
                : "Install the Redball Input Service.";
        }
        else
        {
            // SendInput mode (index 0)
            MainInstallServiceBtn.Visibility = Visibility.Collapsed;
            MainUninstallServiceBtn.Visibility = Visibility.Collapsed;
            if (MainServiceAdminHintText != null) MainServiceAdminHintText.Visibility = Visibility.Collapsed;
        }
    }

    private void MainInstallServiceBtn_Click(object sender, RoutedEventArgs e)
    {
        HandleServiceInstallUninstall();
    }

    private void MainUninstallServiceBtn_Click(object sender, RoutedEventArgs e)
    {
        HandleServiceInstallUninstall();
    }

    private void HandleServiceInstallUninstall()
    {
        var serviceInstalled = ServiceInputProvider.Instance.RefreshServiceInstalledState();
        var serviceRunning = ServiceInputProvider.Instance.IsReady || 
                             ServiceInputProvider.Instance.GetDetailedServiceState().Status == ServiceInputProvider.ServiceStatus.Healthy;

        if (!serviceInstalled)
        {
            // Service doesn't exist - install it
            var confirmInstall = NotificationWindow.Show(
                "Install Input Service",
                "Install the Redball Input Service now? This doesn't require driver signing and works over RDP. Admin approval may be required.",
                "\uE7BA",
                true);

            if (!confirmInstall)
            {
                return;
            }

            var installResult = InstallServiceDirect();
            if (installResult.Success)
            {
                NotificationService.Instance.ShowInfo("Service Installed", "Redball Input Service installed successfully. No restart required.");
            }
            else if (installResult.UserCancelled)
            {
                // User cancelled UAC - no error message needed
                Logger.Info("MainWindow", "Service installation cancelled by user at UAC prompt");
            }
            else
            {
                NotificationService.Instance.ShowError("Install Failed", $"Failed to install Redball Input Service: {installResult.ErrorMessage}");
            }
        }
        else if (!serviceRunning)
        {
            // Service exists but not running - start it (requires admin)
            var confirmStart = NotificationWindow.Show(
                "Start Input Service",
                "The Redball Input Service is installed but not running. Start it now? Admin approval may be required.",
                "\uE768",
                true);

            if (!confirmStart)
            {
                return;
            }

            var startResult = StartServiceDirect();
            if (startResult.Success)
            {
                NotificationService.Instance.ShowInfo("Service Started", "Redball Input Service started successfully.");
            }
            else if (startResult.UserCancelled)
            {
                Logger.Info("MainWindow", "Service start cancelled by user at UAC prompt");
            }
            else
            {
                NotificationService.Instance.ShowError("Start Failed", $"Failed to start Redball Input Service: {startResult.ErrorMessage}");
            }
        }
        else
        {
            // Service is installed and running - offer to uninstall
            var confirmUninstall = NotificationWindow.Show(
                "Uninstall Input Service",
                "This will uninstall the Redball Input Service and disable service-based typing until reinstalled. Continue?",
                "\uE74D",
                true);

            if (!confirmUninstall)
            {
                return;
            }

            var uninstallResult = UninstallServiceDirect();
            if (uninstallResult.Success)
            {
                NotificationService.Instance.ShowInfo("Service Uninstalled", "Redball Input Service uninstalled successfully.");
            }
            else if (uninstallResult.UserCancelled)
            {
                // User cancelled UAC - no error message needed
                Logger.Info("MainWindow", "Service uninstallation cancelled by user at UAC prompt");
            }
            else
            {
                NotificationService.Instance.ShowError("Uninstall Failed", $"Failed to uninstall Redball Input Service: {uninstallResult.ErrorMessage}");
            }
        }

        RefreshServiceInstallVisibility();
    }

    private (bool Success, bool UserCancelled, string ErrorMessage) InstallServiceDirect()
    {
        try
        {
            var servicePath = ResolveServiceExecutablePath();
            if (!System.IO.File.Exists(servicePath))
            {
                var msg = $"Service executable not found: {servicePath}";
                Logger.Error("MainWindow", msg);
                return (false, false, msg);
            }

            // Check admin rights - if not admin, relaunch app with elevation
            if (!IsCurrentUserAdministrator())
            {
                Logger.Warning("MainWindow", "Service installation requires admin rights; relaunching app with UAC elevation.");
                
                var processInfo = new ProcessStartInfo
                {
                    FileName = Process.GetCurrentProcess().MainModule?.FileName ?? "Redball.UI.WPF.exe",
                    Arguments = "--install-service",
                    UseShellExecute = true,
                    Verb = "runas"
                };

                try
                {
                    using var process = Process.Start(processInfo);
                    if (process == null)
                    {
                        var err = "Failed to start elevated process.";
                        Logger.Error("MainWindow", err);
                        return (false, false, err);
                    }
                    
                    // Wait with 60-second timeout to prevent hanging
                    if (!process.WaitForExit(60000))
                    {
                        Logger.Warning("MainWindow", "Elevated service install process timed out after 60 seconds");
                        try { process.Kill(); } catch { }
                        return (false, false, "Installation timed out. The elevated process did not complete in time.");
                    }
                    
                    var exitCode = process.ExitCode;
                    if (exitCode == 0)
                    {
                        return (true, false, string.Empty);
                    }
                    else
                    {
                        var err = $"Installation failed with exit code {exitCode}. Check logs for details.";
                        Logger.Error("MainWindow", err);
                        return (false, false, err);
                    }
                }
                catch (Win32Exception ex) when (ex.NativeErrorCode == 1223) // ERROR_CANCELLED
                {
                    Logger.Info("MainWindow", "User cancelled UAC elevation for service install");
                    return (false, true, string.Empty);
                }
                catch (Win32Exception ex)
                {
                    Logger.Error("MainWindow", "Win32Exception during elevated service install", ex);
                    return (false, false, $"Elevation failed: {ex.Message}");
                }
            }

            // Already admin - install directly
            var createResult = RunProcess("sc.exe", $"create RedballInputService binPath= \"{servicePath}\" start= auto");
            if (createResult.ExitCode != 0)
            {
                // Check if service already exists
                if (createResult.StdErr.Contains("already exists", StringComparison.OrdinalIgnoreCase))
                {
                    Logger.Info("MainWindow", "Service already exists, attempting to start...");
                }
                else
                {
                    var err = $"Failed to create service: {createResult.StdErr}";
                    Logger.Error("MainWindow", err);
                    return (false, false, err);
                }
            }

            var startResult = RunProcess("sc.exe", "start RedballInputService");
            if (startResult.ExitCode != 0)
            {
                Logger.Warning("MainWindow", $"Service created but failed to start: {startResult.StdErr}");
                // Don't fail if service was created but couldn't start - it might already be running
            }

            Logger.Info("MainWindow", "Redball Input Service installed successfully");
            return (true, false, string.Empty);
        }
        catch (Exception ex)
        {
            Logger.Error("MainWindow", "Service installation failed", ex);
            return (false, false, $"Unexpected error: {ex.Message}");
        }
    }

    private (bool Success, bool UserCancelled, string ErrorMessage) StartServiceDirect()
    {
        try
        {
            // Check admin rights - if not admin, relaunch app with elevation
            if (!IsCurrentUserAdministrator())
            {
                Logger.Warning("MainWindow", "Service start requires admin rights; relaunching app with UAC elevation.");
                
                var processInfo = new ProcessStartInfo
                {
                    FileName = Process.GetCurrentProcess().MainModule?.FileName ?? "Redball.UI.WPF.exe",
                    Arguments = "--start-service",
                    UseShellExecute = true,
                    Verb = "runas"
                };

                try
                {
                    using var process = Process.Start(processInfo);
                    if (process == null)
                    {
                        var err = "Failed to start elevated process.";
                        Logger.Error("MainWindow", err);
                        return (false, false, err);
                    }
                    
                    // Wait with 30-second timeout for service start
                    if (!process.WaitForExit(30000))
                    {
                        Logger.Warning("MainWindow", "Elevated service start process timed out after 30 seconds");
                        try { process.Kill(); } catch { }
                        return (false, false, "Start timed out. The elevated process did not complete in time.");
                    }
                    
                    var exitCode = process.ExitCode;
                    if (exitCode == 0)
                    {
                        return (true, false, string.Empty);
                    }
                    else
                    {
                        var err = $"Service start failed with exit code {exitCode}. Check logs for details.";
                        Logger.Error("MainWindow", err);
                        return (false, false, err);
                    }
                }
                catch (Win32Exception ex) when (ex.NativeErrorCode == 1223) // ERROR_CANCELLED
                {
                    Logger.Info("MainWindow", "User cancelled UAC elevation for service start");
                    return (false, true, string.Empty);
                }
                catch (Win32Exception ex)
                {
                    Logger.Error("MainWindow", "Win32Exception during elevated service start", ex);
                    return (false, false, $"Elevation failed: {ex.Message}");
                }
            }

            // Already admin - start directly
            var startResult = RunProcess("sc.exe", "start RedballInputService");
            if (startResult.ExitCode != 0)
            {
                var err = $"Failed to start service: {startResult.StdErr}";
                Logger.Error("MainWindow", err);
                return (false, false, err);
            }

            Logger.Info("MainWindow", "Redball Input Service started successfully");
            return (true, false, string.Empty);
        }
        catch (Exception ex)
        {
            Logger.Error("MainWindow", "Service start failed", ex);
            return (false, false, $"Unexpected error: {ex.Message}");
        }
    }

    private (bool Success, bool UserCancelled, string ErrorMessage) UninstallServiceDirect()
    {
        try
        {
            // Check admin rights - if not admin, relaunch app with elevation
            if (!IsCurrentUserAdministrator())
            {
                Logger.Warning("MainWindow", "Service uninstallation requires admin rights; relaunching app with UAC elevation.");
                
                var processInfo = new ProcessStartInfo
                {
                    FileName = Process.GetCurrentProcess().MainModule?.FileName ?? "Redball.UI.WPF.exe",
                    Arguments = "--uninstall-service",
                    UseShellExecute = true,
                    Verb = "runas"
                };

                try
                {
                    using var process = Process.Start(processInfo);
                    if (process == null)
                    {
                        var err = "Failed to start elevated process.";
                        Logger.Error("MainWindow", err);
                        return (false, false, err);
                    }
                    
                    // Wait with 60-second timeout to prevent hanging
                    if (!process.WaitForExit(60000))
                    {
                        Logger.Warning("MainWindow", "Elevated service uninstall process timed out after 60 seconds");
                        try { process.Kill(); } catch { }
                        return (false, false, "Uninstallation timed out. The elevated process did not complete in time.");
                    }
                    
                    var exitCode = process.ExitCode;
                    if (exitCode == 0)
                    {
                        return (true, false, string.Empty);
                    }
                    else
                    {
                        var err = $"Uninstallation failed with exit code {exitCode}. Check logs for details.";
                        Logger.Error("MainWindow", err);
                        return (false, false, err);
                    }
                }
                catch (Win32Exception ex) when (ex.NativeErrorCode == 1223) // ERROR_CANCELLED
                {
                    Logger.Info("MainWindow", "User cancelled UAC elevation for service uninstall");
                    return (false, true, string.Empty);
                }
                catch (Win32Exception ex)
                {
                    Logger.Error("MainWindow", "Win32Exception during elevated service uninstall", ex);
                    return (false, false, $"Elevation failed: {ex.Message}");
                }
            }

            // Already admin - uninstall directly
            // Stop the service first
            RunProcess("sc.exe", "stop RedballInputService");

            // Delete the service
            var deleteResult = RunProcess("sc.exe", "delete RedballInputService");
            if (deleteResult.ExitCode != 0 && !deleteResult.StdErr.Contains("does not exist", StringComparison.OrdinalIgnoreCase))
            {
                var err = $"Failed to delete service: {deleteResult.StdErr}";
                Logger.Error("MainWindow", err);
                return (false, false, err);
            }

            Logger.Info("MainWindow", "Redball Input Service uninstalled successfully");
            return (true, false, string.Empty);
        }
        catch (Exception ex)
        {
            Logger.Error("MainWindow", "Service uninstallation failed", ex);
            return (false, false, $"Unexpected error: {ex.Message}");
        }
    }

    private static string ResolveServiceExecutablePath()
    {
        var candidates = new[]
        {
            System.IO.Path.Combine(AppContext.BaseDirectory, "Redball.Service.exe"),
            System.IO.Path.Combine(AppContext.BaseDirectory, "Redball.Input.Service.exe")
        };

        foreach (var candidate in candidates)
        {
            if (System.IO.File.Exists(candidate))
            {
                return candidate;
            }
        }

        return candidates[0];
    }

    private static (bool Success, int ExitCode, string Error) RunProcessElevated(string fileName, string arguments)
    {
        try
        {
            var psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName = fileName,
                Arguments = arguments,
                UseShellExecute = true,
                Verb = "runas",
                WindowStyle = System.Diagnostics.ProcessWindowStyle.Hidden
            };

            using var process = System.Diagnostics.Process.Start(psi);
            if (process == null)
            {
                return (false, -1, "Failed to start elevated process.");
            }

            process.WaitForExit();
            return (true, process.ExitCode, string.Empty);
        }
        catch (Win32Exception ex)
        {
            return (false, -1, ex.Message);
        }
        catch (Exception ex)
        {
            return (false, -1, ex.Message);
        }
    }

    private static (int ExitCode, string StdOut, string StdErr) RunProcess(string fileName, string arguments)
    {
        var psi = new System.Diagnostics.ProcessStartInfo
        {
            FileName = fileName,
            Arguments = arguments,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };

        using var process = System.Diagnostics.Process.Start(psi)!;
        var stdout = process.StandardOutput.ReadToEnd();
        var stderr = process.StandardError.ReadToEnd();
        process.WaitForExit();
        return (process.ExitCode, stdout, stderr);
    }

    private static bool IsCurrentUserAdministrator()
    {
        try
        {
            using var identity = WindowsIdentity.GetCurrent();
            if (identity == null)
            {
                return false;
            }

            var principal = new WindowsPrincipal(identity);
            return principal.IsInRole(WindowsBuiltInRole.Administrator);
        }
        catch (Exception ex)
        {
            Logger.Debug("MainWindow", $"Failed to check administrator status: {ex.Message}");
            return false;
        }
    }
}
