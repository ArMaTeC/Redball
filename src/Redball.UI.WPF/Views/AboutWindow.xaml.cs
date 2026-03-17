using System.Diagnostics;
using System.Windows;
using Redball.UI.Services;

namespace Redball.UI.Views;

public partial class AboutWindow : Window
{
    private UpdateService? _updateService;

    public AboutWindow()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        
        // Initialize update service
        var cfg = Services.ConfigService.Instance.Config;
        _updateService = new UpdateService(
            cfg.UpdateRepoOwner,
            cfg.UpdateRepoName,
            cfg.UpdateChannel,
            cfg.VerifyUpdateSignature);
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        var version = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
        VersionText.Text = $"Redball v{version?.Major}.{version?.Minor}.{version?.Build}";
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void GitHubButton_Click(object sender, RoutedEventArgs e)
    {
        Process.Start(new ProcessStartInfo
        {
            FileName = "https://github.com/ArMaTeC/Redball",
            UseShellExecute = true
        });
    }

    private async void UpdateButton_Click(object sender, RoutedEventArgs e)
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
            MessageBox.Show(
                "Update downloaded successfully. The application will now restart to apply the update.",
                "Update Ready",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
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
}
