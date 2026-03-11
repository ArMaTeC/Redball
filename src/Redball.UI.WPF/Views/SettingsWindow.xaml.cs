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
        // Hide all panels (null checks needed during XAML initialization)
        if (GeneralPanel != null) GeneralPanel.Visibility = Visibility.Collapsed;
        if (BehaviorPanel != null) BehaviorPanel.Visibility = Visibility.Collapsed;
        if (FeaturesPanel != null) FeaturesPanel.Visibility = Visibility.Collapsed;
        if (TypeThingPanel != null) TypeThingPanel.Visibility = Visibility.Collapsed;
        if (UpdatesPanel != null) UpdatesPanel.Visibility = Visibility.Collapsed;
        if (AboutPanel != null) AboutPanel.Visibility = Visibility.Collapsed;

        // Show selected panel
        if (sender == GeneralTab && GeneralPanel != null) GeneralPanel.Visibility = Visibility.Visible;
        else if (sender == BehaviorTab && BehaviorPanel != null) BehaviorPanel.Visibility = Visibility.Visible;
        else if (sender == FeaturesTab && FeaturesPanel != null) FeaturesPanel.Visibility = Visibility.Visible;
        else if (sender == TypeThingTab && TypeThingPanel != null) TypeThingPanel.Visibility = Visibility.Visible;
        else if (sender == UpdatesTab && UpdatesPanel != null) UpdatesPanel.Visibility = Visibility.Visible;
        else if (sender == AboutTab && AboutPanel != null) AboutPanel.Visibility = Visibility.Visible;
    }
}
