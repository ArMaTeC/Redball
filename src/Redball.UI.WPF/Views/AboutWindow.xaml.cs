using System.Diagnostics;
using System.Windows;

namespace Redball.UI.Views;

public partial class AboutWindow : Window
{
    public AboutWindow()
    {
        InitializeComponent();
        Loaded += OnLoaded;
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

    private void UpdateButton_Click(object sender, RoutedEventArgs e)
    {
        Process.Start(new ProcessStartInfo
        {
            FileName = "https://github.com/ArMaTeC/Redball/releases",
            UseShellExecute = true
        });
    }
}
