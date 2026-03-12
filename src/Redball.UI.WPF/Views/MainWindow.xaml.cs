using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media.Imaging;
using Hardcodet.Wpf.TaskbarNotification;
using Redball.UI.Services;

namespace Redball.UI.Views;

/// <summary>
/// Main window for Redball v3.0 WPF UI
/// Primarily a tray-only application with optional window interface
/// </summary>
public partial class MainWindow : Window
{
    private ViewModels.MainViewModel? _viewModel;
    private TaskbarIcon? _trayIcon;
    private bool _isTrayIconInitialized;
    private static readonly string LogPath = Path.Combine(AppContext.BaseDirectory, "Redball.UI.log");

    private Views.SettingsWindow? _settingsWindow;
    private Views.AboutWindow? _aboutWindow;
    private HotkeyService? _hotkeyService;

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
        Log("Window loaded");

        // Set DataContext here instead of XAML to prevent constructor issues during parsing
        if (DataContext == null)
        {
            DataContext = new ViewModels.MainViewModel();
            Log("DataContext set to new MainViewModel");
        }

        _viewModel = DataContext as ViewModels.MainViewModel;
        if (_viewModel == null)
        {
            Log("ERROR: DataContext is not MainViewModel");
            return;
        }

        // Connect ViewModel to this window for proper command delegation
        _viewModel.SetMainWindow(this);

        SetupTrayIcon();
        SetupGlobalHotkeys();
        Log("MainWindow initialization complete");
    }

    private void SetupGlobalHotkeys()
    {
        try
        {
            var hwndSource = PresentationSource.FromVisual(this) as HwndSource;
            if (hwndSource == null)
            {
                Log("WARNING: HwndSource not available for hotkey registration");
                return;
            }

            _hotkeyService = new HotkeyService(hwndSource);

            // Register Ctrl+Alt+Pause to toggle active state
            _hotkeyService.RegisterHotkey(1, HotkeyService.MOD_CONTROL | HotkeyService.MOD_ALT, 0x13 /* VK_PAUSE */, () =>
            {
                Log("Hotkey: Ctrl+Alt+Pause - Toggle active");
                _viewModel?.ToggleActiveCommand.Execute(null);
            });

            // Register TypeThing start hotkey from config
            var startHotkey = ConfigService.Instance.Config.TypeThingStartHotkey ?? "Ctrl+Shift+V";
            var (startMods, startKey) = HotkeyService.ParseHotkey(startHotkey);
            if (startKey != 0)
            {
                _hotkeyService.RegisterHotkey(100, startMods, startKey, () =>
                {
                    Log($"Hotkey: {startHotkey} - TypeThing start");
                    StartTypeThing();
                });
                Log($"Registered TypeThing start hotkey: {startHotkey} (mods={startMods}, key={startKey})");
            }
            else
            {
                Log($"WARNING: Could not parse TypeThing start hotkey: {startHotkey}");
            }

            // Register TypeThing stop hotkey from config
            var stopHotkey = ConfigService.Instance.Config.TypeThingStopHotkey ?? "Ctrl+Shift+X";
            var (stopMods, stopKey) = HotkeyService.ParseHotkey(stopHotkey);
            if (stopKey != 0)
            {
                _hotkeyService.RegisterHotkey(101, stopMods, stopKey, () =>
                {
                    Log($"Hotkey: {stopHotkey} - TypeThing stop");
                });
                Log($"Registered TypeThing stop hotkey: {stopHotkey} (mods={stopMods}, key={stopKey})");
            }

            Log("Global hotkeys registered successfully");
        }
        catch (Exception ex)
        {
            Log($"Failed to register global hotkeys: {ex.Message}");
        }
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

                    // Use System.Drawing.Icon directly - avoids BitmapImage URI issues
                    var icon = new System.Drawing.Icon(foundPath);
                    _trayIcon.Icon = icon;
                    Log("Icon loaded successfully via System.Drawing.Icon");
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
            
            // Set tooltip with actual version
            var version = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
            _trayIcon.ToolTipText = $"Redball v{version?.Major}.{version?.Minor}.{version?.Build}";
            Log($"Tray tooltip set to: {_trayIcon.ToolTipText}");
            
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
        // If closing from tray exit command, allow close
        // If closing from X button, move off-screen instead (tray-only mode)
        if (_trayIcon?.Visibility == Visibility.Visible && e.Cancel == false)
        {
            // Move off-screen instead of closing to keep hotkeys working
            WindowStyle = WindowStyle.ToolWindow;
            ShowInTaskbar = false;
            Left = -10000;
            Top = -10000;
            Width = 1;
            Height = 1;
            e.Cancel = true;
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
            _settingsWindow.Closed += (s, e) =>
            {
                _settingsWindow = null;
                ReloadHotkeys(); // Reload hotkeys after settings change
            };
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

    public void StartTypeThing()
    {
        Dispatcher.Invoke(() =>
        {
            try
            {
                Log("TypeThing: Starting paste-as-typing");
                var clipboardText = System.Windows.Clipboard.GetText();
                if (string.IsNullOrEmpty(clipboardText))
                {
                    Log("TypeThing: Clipboard is empty, nothing to type");
                    if (_trayIcon != null)
                    {
                        _trayIcon.ShowBalloonTip("TypeThing", "Clipboard is empty. Copy some text first.",
                            Hardcodet.Wpf.TaskbarNotification.BalloonIcon.Warning);
                    }
                    return;
                }

                Log($"TypeThing: Got {clipboardText.Length} chars from clipboard");

                // Start typing after a short delay so user can switch to target window
                var countdown = 3;
                var countdownTimer = new System.Windows.Threading.DispatcherTimer
                {
                    Interval = TimeSpan.FromSeconds(1)
                };
                countdownTimer.Tick += (s, e) =>
                {
                    countdown--;
                    if (countdown <= 0)
                    {
                        countdownTimer.Stop();
                        TypeText(clipboardText);
                    }
                    else
                    {
                        Log($"TypeThing: Starting in {countdown}...");
                    }
                };

                if (_trayIcon != null)
                {
                    _trayIcon.ShowBalloonTip("TypeThing", $"Typing {clipboardText.Length} characters in 3 seconds...\nSwitch to target window now!",
                        Hardcodet.Wpf.TaskbarNotification.BalloonIcon.Info);
                }
                countdownTimer.Start();
            }
            catch (Exception ex)
            {
                Log($"TypeThing error: {ex.Message}");
            }
        });
    }

    private void TypeText(string text)
    {
        Log($"TypeThing: Begin typing {text.Length} chars");
        var index = 0;
        var timer = new System.Windows.Threading.DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(30 + new Random().Next(90))
        };
        timer.Tick += (s, e) =>
        {
            if (index >= text.Length)
            {
                timer.Stop();
                Log("TypeThing: Typing complete");
                if (_trayIcon != null)
                {
                    _trayIcon.ShowBalloonTip("TypeThing", $"Done! Typed {text.Length} characters.",
                        Hardcodet.Wpf.TaskbarNotification.BalloonIcon.Info);
                }
                return;
            }

            var ch = text[index];
            if (ch == '\n')
            {
                System.Windows.Forms.SendKeys.SendWait("{ENTER}");
            }
            else if (ch == '\r')
            {
                // Skip carriage return (handled by \n)
            }
            else if (ch == '\t')
            {
                System.Windows.Forms.SendKeys.SendWait("{TAB}");
            }
            else
            {
                // Escape special SendKeys characters
                var escaped = ch switch
                {
                    '+' => "{+}",
                    '^' => "{^}",
                    '%' => "{%}",
                    '~' => "{~}",
                    '(' => "{(}",
                    ')' => "{)}",
                    '{' => "{{}",
                    '}' => "{}}",
                    '[' => "{[}",
                    ']' => "{]}",
                    _ => ch.ToString()
                };
                System.Windows.Forms.SendKeys.SendWait(escaped);
            }
            index++;

            // Randomize interval for human-like typing
            timer.Interval = TimeSpan.FromMilliseconds(30 + new Random().Next(90));
        };
        timer.Start();
    }

    public void ReloadHotkeys()
    {
        try
        {
            Log("Reloading hotkeys from config...");
            _hotkeyService?.Dispose();
            SetupGlobalHotkeys();
            Log("Hotkeys reloaded successfully");
        }
        catch (Exception ex)
        {
            Log($"Failed to reload hotkeys: {ex.Message}");
        }
    }

    public void ExitApplication()
    {
        _hotkeyService?.Dispose();
        if (_trayIcon != null)
        {
            _trayIcon.Dispose();
            _trayIcon = null;
        }
        Application.Current.Shutdown();
    }
}
