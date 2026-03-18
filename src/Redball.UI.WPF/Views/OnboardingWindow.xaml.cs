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
    private readonly AnalyticsService _analytics = new(ConfigService.Instance.Config.EnableTelemetry);

    public OnboardingWindow()
    {
        InitializeComponent();
        LoadCurrentSettings();
    }

    private bool _isLoading;

    private void LoadCurrentSettings()
    {
        _isLoading = true;
        var config = ConfigService.Instance.Config;
        
        PreventDisplaySleepCheck.IsChecked = config.PreventDisplaySleep;
        UseHeartbeatCheck.IsChecked = config.UseHeartbeatKeypress;
        StartWithWindowsCheck.IsChecked = false;
        BatteryAwareCheck.IsChecked = config.BatteryAware;
        NetworkAwareCheck.IsChecked = config.NetworkAware;
        ShowOnStartupCheck.IsChecked = false;
        
        ThemeCombo.SelectedIndex = config.Theme switch
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
        _isLoading = false;
    }

    private string GetSelectedTheme()
    {
        return ThemeCombo.SelectedIndex switch
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
    }

    private void SaveCurrentSettings()
    {
        if (_isLoading) return;
        var config = ConfigService.Instance.Config;
        config.PreventDisplaySleep = PreventDisplaySleepCheck.IsChecked ?? true;
        config.UseHeartbeatKeypress = UseHeartbeatCheck.IsChecked ?? true;
        config.BatteryAware = BatteryAwareCheck.IsChecked ?? false;
        config.NetworkAware = NetworkAwareCheck.IsChecked ?? false;
        config.FirstRun = ShowOnStartupCheck.IsChecked ?? false;
        config.Theme = GetSelectedTheme();

        if (StartWithWindowsCheck.IsChecked == true)
            StartupService.Install();

        ThemeManager.SetTheme(ThemeManager.ThemeFromString(config.Theme));
        ConfigService.Instance.Save();
        Logger.ApplyConfig(config);
    }

    private void OnSettingChanged(object sender, RoutedEventArgs e)
    {
        SaveCurrentSettings();
    }

    private void ThemeCombo_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        SaveCurrentSettings();
    }

    private void GetStartedButton_Click(object sender, RoutedEventArgs e)
    {
        SaveCurrentSettings();
        ConfigService.Instance.IsDirty = false;

        _analytics.TrackFeature("onboarding.preferences_saved");
        _analytics.TrackFunnel("onboarding", "preferences_saved");
        if (StartWithWindowsCheck.IsChecked == true)
        {
            _analytics.TrackFeature("startup.enabled");
        }
        
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
