using System.Diagnostics;
using System.Windows;
using System.Windows.Navigation;
using Redball.UI.Services;

namespace Redball.UI.Views;

/// <summary>
/// First-run onboarding window to help new users get started with Redball.
/// </summary>
public partial class OnboardingWindow : Window
{
    public OnboardingWindow()
    {
        InitializeComponent();
        LoadCurrentSettings();
    }

    private void LoadCurrentSettings()
    {
        var config = ConfigService.Instance.Config;
        
        PreventDisplaySleepCheck.IsChecked = config.PreventDisplaySleep;
        UseHeartbeatCheck.IsChecked = config.UseHeartbeatKeypress;
        StartWithWindowsCheck.IsChecked = false; // Will be set via StartupService
        BatteryAwareCheck.IsChecked = config.BatteryAware;
        NetworkAwareCheck.IsChecked = config.NetworkAware;
        ShowOnStartupCheck.IsChecked = false;
        
        // Set theme selection
        ThemeCombo.SelectedIndex = config.Theme switch
        {
            "Light" => 1,
            "System" => 2,
            _ => 0
        };
    }

    private void GetStartedButton_Click(object sender, RoutedEventArgs e)
    {
        // Save user preferences
        var config = ConfigService.Instance.Config;
        
        config.PreventDisplaySleep = PreventDisplaySleepCheck.IsChecked ?? true;
        config.UseHeartbeatKeypress = UseHeartbeatCheck.IsChecked ?? true;
        config.BatteryAware = BatteryAwareCheck.IsChecked ?? false;
        config.NetworkAware = NetworkAwareCheck.IsChecked ?? false;
        config.FirstRun = ShowOnStartupCheck.IsChecked ?? false;
        
        // Save theme selection
        config.Theme = ThemeCombo.SelectedIndex switch
        {
            1 => "Light",
            2 => "System",
            _ => "Dark"
        };
        
        // Handle startup registration
        if (StartWithWindowsCheck.IsChecked == true)
        {
            StartupService.Install();
        }
        
        // Apply theme immediately
        ThemeManager.SetTheme(ThemeManager.ThemeFromString(config.Theme));
        
        // Save config
        ConfigService.Instance.Save();
        ConfigService.Instance.IsDirty = false;
        
        Logger.Info("OnboardingWindow", "First-run onboarding completed, settings saved");
        
        DialogResult = true;
        Close();
    }

    private void Hyperlink_RequestNavigate(object sender, RequestNavigateEventArgs e)
    {
        Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true });
        e.Handled = true;
    }
}
