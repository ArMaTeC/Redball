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
    private bool _isTrayIconInitialized;
    private static readonly string LogPath = Path.Combine(AppContext.BaseDirectory, "Redball.UI.log");

    private Views.SettingsWindow? _settingsWindow;
    private Views.AboutWindow? _aboutWindow;

    private static void Log(string message)
    {
        try
        {
            var timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            File.AppendAllText(LogPath, $"[{timestamp}] {message}{Environment.NewLine}");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Log failed: {ex.Message}");
        }
    }

    public MainWindow()
    {
        InitializeComponent();
        Loaded += OnWindowLoaded;
    }

    private void OnWindowLoaded(object sender, RoutedEventArgs e)
    {
        // Connect ViewModel to this window for proper command delegation
        if (DataContext is ViewModels.MainViewModel vm)
        {
            vm.SetMainWindow(this);
        }
        SetupTrayIcon();
    }

    private void SetupTrayIcon()
    {
        // Prevent duplicate initialization
        if (_isTrayIconInitialized)
        {
            Log("SetupTrayIcon already initialized, skipping");
            return;
        }
        
        Log("=== SetupTrayIcon called ===");
        _isTrayIconInitialized = true;
        
        // Tray icon is defined in XAML, ensure it's properly initialized
        _trayIcon = TrayIcon;
        Log($"TrayIcon from XAML: {_trayIcon != null}");
        
        if (_trayIcon != null)
        {
            // Load icon from multiple possible locations
            var iconPaths = new[]
            {
                Path.Combine(AppContext.BaseDirectory, "Assets", "redball.ico"),
                Path.Combine(AppContext.BaseDirectory, "redball.ico"),
                Path.Combine(Environment.CurrentDirectory, "Assets", "redball.ico"),
                Path.Combine(Environment.CurrentDirectory, "redball.ico")
            };
            
            string? foundPath = null;
            foreach (var path in iconPaths)
            {
                bool exists = File.Exists(path);
                Log($"Checking icon at: {path} - Exists: {exists}");
                if (exists)
                {
                    foundPath = path;
                    break;
                }
            }
            
            if (foundPath != null)
            {
                try
                {
                    Log($"Loading icon from: {foundPath}");
                    
                    // Use file:// URI scheme for reliable loading
                    var uriString = $"file:///{foundPath.Replace('\\', '/')}";
                    var uri = new Uri(uriString, UriKind.Absolute);
                    var bitmap = new BitmapImage();
                    bitmap.BeginInit();
                    bitmap.UriSource = uri;
                    bitmap.CacheOption = BitmapCacheOption.OnLoad;
                    bitmap.EndInit();
                    bitmap.Freeze();
                    
                    _trayIcon.IconSource = bitmap;
                    Log("Icon loaded successfully via file:// URI");
                }
                catch (Exception ex)
                {
                    Log($"ERROR loading icon: {ex.GetType().Name}: {ex.Message}");
                }
            }
            else
            {
                Log("WARNING: Icon file not found in any expected location");
            }
            
            // Ensure visibility is set
            _trayIcon.Visibility = Visibility.Visible;
            Log("Tray icon visibility set to Visible");
            
            // Verify icon is set
            Log($"IconSource is null: {_trayIcon.IconSource == null}");
        }
        else
        {
            Log("ERROR: TrayIcon not found in XAML!");
        }
        
        Log("=== SetupTrayIcon complete ===");
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
        Dispatcher.Invoke(() =>
        {
            if (_settingsWindow != null && _settingsWindow.IsLoaded)
            {
                _settingsWindow.Activate();
                _settingsWindow.Focus();
                return;
            }
            _settingsWindow = new Views.SettingsWindow();
            _settingsWindow.Closed += (s, e) => _settingsWindow = null;
            _settingsWindow.Show();
        });
    }

    public void ShowAbout()
    {
        Dispatcher.Invoke(() =>
        {
            if (_aboutWindow != null && _aboutWindow.IsLoaded)
            {
                _aboutWindow.Activate();
                _aboutWindow.Focus();
                return;
            }
            _aboutWindow = new Views.AboutWindow();
            _aboutWindow.Closed += (s, e) => _aboutWindow = null;
            _aboutWindow.Show();
        });
    }

    public void ExitApplication()
    {
        if (_trayIcon != null)
        {
            _trayIcon.Dispose();
            _trayIcon = null;
        }
        Application.Current.Shutdown();
    }
}
