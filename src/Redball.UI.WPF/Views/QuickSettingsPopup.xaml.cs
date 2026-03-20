using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using Redball.UI.Services;

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
        StatusText.Text = ka.GetStatusText();
        ToggleActiveBtn.Content = ka.IsActive ? "Pause Keep-Awake" : "Resume Keep-Awake";
        ToggleDisplayBtn.Content = ka.PreventDisplaySleep ? "Display Sleep: Prevented" : "Display Sleep: Allowed";
        ToggleHeartbeatBtn.Content = ka.UseHeartbeat ? "Heartbeat: On" : "Heartbeat: Off";
    }

    private void ToggleActive_Click(object sender, RoutedEventArgs e)
    {
        KeepAwakeService.Instance.Toggle();
        RefreshState();
    }

    private void ToggleDisplay_Click(object sender, RoutedEventArgs e)
    {
        KeepAwakeService.Instance.PreventDisplaySleep = !KeepAwakeService.Instance.PreventDisplaySleep;
        RefreshState();
    }

    private void ToggleHeartbeat_Click(object sender, RoutedEventArgs e)
    {
        KeepAwakeService.Instance.UseHeartbeat = !KeepAwakeService.Instance.UseHeartbeat;
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

    private void Exit_Click(object sender, RoutedEventArgs e)
    {
        ClosePopup();
        Application.Current.Shutdown();
    }

    private void ClosePopup()
    {
        if (Parent is Popup popup)
            popup.IsOpen = false;
    }
}
