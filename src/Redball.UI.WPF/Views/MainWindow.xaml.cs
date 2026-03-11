using System;
using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using Hardcodet.Wpf.TaskbarNotification;

namespace Redball.UI.Views;

/// <summary>
/// Main window for Redball v3.0 WPF UI
/// Primarily a tray-only application with optional window interface
/// </summary>
public partial class MainWindow : Window
{
    private TaskbarIcon? _trayIcon;

    public MainWindow()
    {
        InitializeComponent();
        SetupTrayIcon();
    }

    private void SetupTrayIcon()
    {
        // Tray icon is defined in XAML, load icon from file
        _trayIcon = TrayIcon;
        if (_trayIcon != null)
        {
            // Load icon from file path relative to executable
            var iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "redball.ico");
            if (File.Exists(iconPath))
            {
                _trayIcon.IconSource = new BitmapImage(new Uri(iconPath, UriKind.Absolute));
            }
            _trayIcon.Visibility = Visibility.Visible;
        }
    }

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        // Hide window instead of closing when in tray-only mode
        if (_trayIcon?.Visibility == Visibility.Visible)
        {
            e.Cancel = true;
            Hide();
        }
        base.OnClosing(e);
    }

    public void ShowSettings()
    {
        var settings = new SettingsWindow();
        settings.ShowDialog();
    }

    public void ShowAbout()
    {
        var about = new AboutWindow();
        about.ShowDialog();
    }

    public void ExitApplication()
    {
        if (_trayIcon != null)
        {
            _trayIcon.Dispose();
        }
        Application.Current.Shutdown();
    }
}
