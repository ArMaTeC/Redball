using System.Diagnostics;
using System.Windows;

namespace Redball.UI.Views;

public partial class AboutWindow : Window
{
    public AboutWindow()
    {
        InitializeComponent();
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

    private void UpdateButton_Click(object sender, RoutedEventArgs e)
    {
        // TODO: Trigger update check via IPC to PowerShell core
        MessageBox.Show("Update check would be performed here via IPC communication with PowerShell core.",
                       "Check for Updates",
                       MessageBoxButton.OK,
                       MessageBoxImage.Information);
    }
}
