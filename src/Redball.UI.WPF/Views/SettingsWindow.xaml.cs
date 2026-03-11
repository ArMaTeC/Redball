using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;

namespace Redball.UI.Views;

public partial class SettingsWindow : Window
{
    public SettingsWindow()
    {
        InitializeComponent();
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        // TODO: Save settings to config file
        // TODO: Send IPC message to PowerShell core to reload config
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

    private void Tab_Checked(object sender, RoutedEventArgs e)
    {
        // Hide all panels
        GeneralPanel.Visibility = Visibility.Collapsed;
        BehaviorPanel.Visibility = Visibility.Collapsed;
        FeaturesPanel.Visibility = Visibility.Collapsed;
        TypeThingPanel.Visibility = Visibility.Collapsed;
        UpdatesPanel.Visibility = Visibility.Collapsed;
        AboutPanel.Visibility = Visibility.Collapsed;

        // Show selected panel
        if (sender == GeneralTab) GeneralPanel.Visibility = Visibility.Visible;
        else if (sender == BehaviorTab) BehaviorPanel.Visibility = Visibility.Visible;
        else if (sender == FeaturesTab) FeaturesPanel.Visibility = Visibility.Visible;
        else if (sender == TypeThingTab) TypeThingPanel.Visibility = Visibility.Visible;
        else if (sender == UpdatesTab) UpdatesPanel.Visibility = Visibility.Visible;
        else if (sender == AboutTab) AboutPanel.Visibility = Visibility.Visible;
    }
}
