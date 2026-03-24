using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using Redball.UI.Services;
using Redball.UI.ViewModels;

namespace Redball.UI.Views;

public partial class QuickSettingsPopup : UserControl
{
    public QuickSettingsPopup()
    {
        InitializeComponent();
        RefreshState();
    }

    private void RefreshState()
    {
        var ka = KeepAwakeService.Instance;
        var config = ConfigService.Instance.Config;
        StatusText.Text = ka.GetStatusText();
        ToggleActiveBtn.Content = ka.IsActive ? "Pause Keep-Awake" : "Resume Keep-Awake";
        ToggleDisplayBtn.Content = ka.PreventDisplaySleep ? "Display Sleep: Prevented" : "Display Sleep: Allowed";
        ToggleHeartbeatBtn.Content = ka.UseHeartbeat ? "Heartbeat: On" : "Heartbeat: Off";
        QuickCustomBtn.Content = $"+{Math.Clamp(config.MiniWidgetCustomQuickMinutes, 1, 720)}m";
    }

    private void ToggleActive_Click(object sender, RoutedEventArgs e)
    {
        KeepAwakeService.Instance.Toggle();
        RefreshState();
    }

    private void ToggleDisplay_Click(object sender, RoutedEventArgs e)
    {
        var newValue = !KeepAwakeService.Instance.PreventDisplaySleep;
        KeepAwakeService.Instance.PreventDisplaySleep = newValue;
        PersistQuickSetting(cfg => cfg.PreventDisplaySleep = newValue, "display sleep preference");
        RefreshState();
    }

    private void ToggleHeartbeat_Click(object sender, RoutedEventArgs e)
    {
        var newValue = !KeepAwakeService.Instance.UseHeartbeat;
        KeepAwakeService.Instance.UseHeartbeat = newValue;
        PersistQuickSetting(cfg =>
        {
            cfg.UseHeartbeatKeypress = newValue;
            cfg.HeartbeatInputMode = newValue ? "F15" : "Disabled";
        }, "heartbeat preference");
        RefreshState();
    }

    private void CheckForUpdates_Click(object sender, RoutedEventArgs e)
    {
        ClosePopup();
        if (Application.Current.MainWindow is MainWindow mw)
        {
            _ = mw.CheckForUpdatesAsync();
        }
    }

    private void OpenSettings_Click(object sender, RoutedEventArgs e)
    {
        ClosePopup();
        // Find MainWindow and show settings
        if (Application.Current.MainWindow is MainWindow mw)
        {
            mw.ShowSettings();
        }
    }

    private void OpenWidget_Click(object sender, RoutedEventArgs e)
    {
        ClosePopup();

        if (Application.Current.MainWindow?.DataContext is MainViewModel vm)
        {
            vm.ShowMiniWidgetCommand.Execute(null);
            return;
        }

        if (Application.Current.MainWindow is MainWindow mw)
        {
            mw.Activate();
        }
    }

    private void Quick15m_Click(object sender, RoutedEventArgs e)
    {
        ExtendSessionByMinutes(15);
    }

    private void Quick60m_Click(object sender, RoutedEventArgs e)
    {
        ExtendSessionByMinutes(60);
    }

    private void QuickCustom_Click(object sender, RoutedEventArgs e)
    {
        var customMinutes = Math.Clamp(ConfigService.Instance.Config.MiniWidgetCustomQuickMinutes, 1, 720);
        ExtendSessionByMinutes(customMinutes);
    }

    private void PresetFocus_Click(object sender, RoutedEventArgs e)
    {
        ApplyMiniWidgetPreset(MiniWidgetPresetService.Focus);
    }

    private void PresetMeeting_Click(object sender, RoutedEventArgs e)
    {
        ApplyMiniWidgetPreset(MiniWidgetPresetService.Meeting);
    }

    private void PresetBatterySafe_Click(object sender, RoutedEventArgs e)
    {
        ApplyMiniWidgetPreset(MiniWidgetPresetService.BatterySafe);
    }

    private void Exit_Click(object sender, RoutedEventArgs e)
    {
        var keepAwakeActive = KeepAwakeService.Instance.IsActive;
        if (keepAwakeActive)
        {
            var confirmExit = NotificationWindow.Show(
                "Exit Redball",
                "Keep-awake is currently active. Exit anyway?",
                "\uE7BA",
                true);

            if (!confirmExit)
            {
                return;
            }
        }

        ClosePopup();
        if (Application.Current.MainWindow is MainWindow mw)
        {
            mw.ExitApplication();
            return;
        }

        Application.Current.Shutdown();
    }

    private void ClosePopup()
    {
        if (Parent is Popup popup)
            popup.IsOpen = false;
    }

    private static void PersistQuickSetting(Action<RedballConfig> apply, string settingName)
    {
        var configService = ConfigService.Instance;
        var config = configService.Config;
        var originalJson = string.Empty;

        try
        {
            originalJson = System.Text.Json.JsonSerializer.Serialize(config);
            apply(config);

            if (!configService.Save())
            {
                RollbackConfig(configService, originalJson);
                NotificationWindow.Show("Settings", $"Failed to save {settingName}.", "\uE783");
            }
        }
        catch (Exception ex)
        {
            RollbackConfig(configService, originalJson);
            Logger.Error("QuickSettingsPopup", $"Failed to persist {settingName}", ex);
            NotificationWindow.Show("Settings", $"Could not save {settingName}.", "\uE783");
        }
    }

    private static void RollbackConfig(ConfigService configService, string originalJson)
    {
        if (string.IsNullOrWhiteSpace(originalJson))
        {
            return;
        }

        try
        {
            var restored = System.Text.Json.JsonSerializer.Deserialize<RedballConfig>(originalJson);
            if (restored != null)
            {
                configService.Config = restored;
            }
        }
        catch (Exception ex)
        {
            Logger.Warning("QuickSettingsPopup", $"Failed to rollback config after save error: {ex.Message}");
        }
    }

    private void ExtendSessionByMinutes(int minutes)
    {
        var keepAwake = KeepAwakeService.Instance;
        var now = DateTime.Now;
        var baseTime = keepAwake.Until.HasValue && keepAwake.Until.Value > now
            ? keepAwake.Until.Value
            : now;

        keepAwake.SetActive(true, baseTime.AddMinutes(Math.Clamp(minutes, 1, 720)));
        RefreshState();
    }

    private void ApplyMiniWidgetPreset(string preset)
    {
        PersistQuickSetting(cfg => MiniWidgetPresetService.ApplyPreset(cfg, preset), "mini widget preset");
        RefreshState();
        NotificationService.Instance.ShowInfo("Mini Widget", $"Applied '{preset}' preset.");
    }
}
