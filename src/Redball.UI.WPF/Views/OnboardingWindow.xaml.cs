using System.Diagnostics;
using System.Windows;
using System.Windows.Navigation;
using Redball.UI.Services;

namespace Redball.UI.Views;

/// <summary>
/// First-run onboarding window with multi-step animated flow.
/// </summary>
public partial class OnboardingWindow : Window
{
    private readonly AnalyticsService _analytics = new(ConfigService.Instance.Config.EnableTelemetry);
    private int _currentStep = 1;
    private const int TotalSteps = 4;

    public OnboardingWindow()
    {
        InitializeComponent();
        LoadCurrentSettings();
        UpdateStepUI();
    }

    private bool _isLoading = true;

    private void LoadCurrentSettings()
    {
        _isLoading = true;
        var config = ConfigService.Instance.Config;
        
        PreventDisplaySleepCheck.IsChecked = config.PreventDisplaySleep;
        UseHeartbeatCheck.IsChecked = config.UseHeartbeatKeypress;
        StartWithWindowsCheck.IsChecked = StartupService.IsInstalledAtStartup();
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
        // FirstRun should only be true initially; once adjusted here, we consider onboarding engaged.
        // We'll explicitly set it to false on completion in GetStartedButton_Click.
        config.Theme = GetSelectedTheme();

        var shouldStartWithWindows = StartWithWindowsCheck.IsChecked == true;
        if (shouldStartWithWindows)
            StartupService.Install();
        else
            StartupService.Uninstall();

        ThemeManager.SetTheme(ThemeManager.ThemeFromString(config.Theme));
        ConfigService.Instance.Save();
        Logger.ApplyConfig(config);
    }

    private void UpdateStepUI()
    {
        Step1Panel.Visibility = _currentStep == 1 ? Visibility.Visible : Visibility.Collapsed;
        Step2Panel.Visibility = _currentStep == 2 ? Visibility.Visible : Visibility.Collapsed;
        Step3Panel.Visibility = _currentStep == 3 ? Visibility.Visible : Visibility.Collapsed;
        Step4Panel.Visibility = _currentStep == 4 ? Visibility.Visible : Visibility.Collapsed;

        // Progress dots
        Dot1.Opacity = _currentStep >= 1 ? 1.0 : 0.4;
        Dot2.Opacity = _currentStep >= 2 ? 1.0 : 0.4;
        Dot3.Opacity = _currentStep >= 3 ? 1.0 : 0.4;
        Dot4.Opacity = _currentStep >= 4 ? 1.0 : 0.4;

        // Header text
        HeaderSubtitle.Text = _currentStep switch
        {
            1 => "Keep your Windows PC awake — intelligently",
            2 => "Step 2 of 4 — Discover what Redball can do",
            3 => "Step 3 of 4 — Set your preferences",
            4 => "Setup complete!",
            _ => ""
        };

        // Button text & visibility
        BackButton.Visibility = _currentStep > 1 ? Visibility.Visible : Visibility.Collapsed;
        GetStartedButton.Content = _currentStep == TotalSteps ? "Get Started" : "Next";

        _analytics.TrackFunnel("onboarding", $"step_{_currentStep}");
    }

    private void OnSettingChanged(object sender, RoutedEventArgs e)
    {
        SaveCurrentSettings();
    }

    private void ThemeCombo_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        SaveCurrentSettings();
    }

    private void BackButton_Click(object sender, RoutedEventArgs e)
    {
        if (_currentStep > 1)
        {
            _currentStep--;
            UpdateStepUI();
        }
    }

    private void GetStartedButton_Click(object sender, RoutedEventArgs e)
    {
        if (_currentStep < TotalSteps)
        {
            _currentStep++;
            UpdateStepUI();
            return;
        }

        // Final step — save and close
        SaveCurrentSettings();
        ConfigService.Instance.IsDirty = false;

        _analytics.TrackFeature("onboarding.preferences_saved");
        _analytics.TrackFunnel("onboarding", "preferences_saved");
        if (StartWithWindowsCheck.IsChecked == true)
        {
            _analytics.TrackFeature("startup.enabled");
        }
        
        Logger.Info("OnboardingWindow", "First-run onboarding completed, settings saved");
        
        // Ensure the FirstRun flag is permanently disabled after successful completion
        ConfigService.Instance.Config.FirstRun = false;
        ConfigService.Instance.Save();
        
        DialogResult = true;
        Close();
    }

    private void Hyperlink_RequestNavigate(object sender, RequestNavigateEventArgs e)
    {
        Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true });
        e.Handled = true;
    }
}
