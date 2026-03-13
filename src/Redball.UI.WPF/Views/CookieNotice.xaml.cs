using System.Diagnostics;
using System.Windows;
using System.Windows.Navigation;
using Redball.UI.Services;

namespace Redball.UI.Views;

/// <summary>
/// Cookie/privacy notice dialog for GDPR compliance and user transparency.
/// </summary>
public partial class CookieNotice : Window
{
    public CookieNotice()
    {
        InitializeComponent();
        LoadCurrentSettings();
    }

    private void LoadCurrentSettings()
    {
        var config = ConfigService.Instance.Config;
        
        TelemetryCheck.IsChecked = config.EnableTelemetry;
        UpdateCheck.IsChecked = true; // Always enabled for update checks
        CloudAnalyticsCheck.IsChecked = !string.IsNullOrEmpty(config.UpdateRepoOwner);
    }

    private void AcceptButton_Click(object sender, RoutedEventArgs e)
    {
        // Save user preferences
        var config = ConfigService.Instance.Config;
        
        config.EnableTelemetry = TelemetryCheck.IsChecked ?? false;
        
        // Save "don't show again" preference
        if (DontShowAgainCheck.IsChecked == true)
        {
            config.FirstRun = false; // Reuse FirstRun to track if notice was shown
        }
        
        ConfigService.Instance.Save();
        ConfigService.Instance.IsDirty = false;
        
        Logger.Info("CookieNotice", "Privacy preferences saved");
        
        DialogResult = true;
        Close();
    }

    private void PrivacyLink_Click(object sender, RequestNavigateEventArgs e)
    {
        Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true });
        e.Handled = true;
    }
}
