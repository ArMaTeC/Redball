using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
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
    private DispatcherTimer? _trayIconRefreshTimer;
    private uint _taskbarCreatedMsg;

    private Views.SettingsWindow? _settingsWindow;
    private Views.AboutWindow? _aboutWindow;
    private HotkeyService? _hotkeyService;
    private bool _isTyping;

    public MainWindow()
    {
        Logger.Info("MainWindow", "Constructor called");
        InitializeComponent();
        Loaded += OnWindowLoaded;
        Logger.Debug("MainWindow", "Constructor completed");
    }

    private void OnWindowLoaded(object sender, RoutedEventArgs e)
    {
        Logger.Info("MainWindow", "Window loaded event fired");

        // Register for taskbar created message (Explorer restart detection)
        _taskbarCreatedMsg = RegisterWindowMessage("TaskbarCreated");
        Logger.Debug("MainWindow", $"TaskbarCreated message registered: {_taskbarCreatedMsg}");

        // Hook window messages for taskbar recreation
        var hwndSource = PresentationSource.FromVisual(this) as HwndSource;
        if (hwndSource != null)
        {
            hwndSource.AddHook(WndProc);
            Logger.Debug("MainWindow", "Window message hook added for tray icon recovery");
        }

        // Set DataContext here instead of XAML to prevent constructor issues during parsing
        if (DataContext == null)
        {
            Logger.Debug("MainWindow", "Creating new MainViewModel...");
            DataContext = new ViewModels.MainViewModel();
            Logger.Debug("MainWindow", "DataContext set to new MainViewModel");
        }

        _viewModel = DataContext as ViewModels.MainViewModel;
        if (_viewModel == null)
        {
            Logger.Error("MainWindow", "ERROR: DataContext is not MainViewModel");
            return;
        }

        // Connect ViewModel to this window for proper command delegation
        _viewModel.SetMainWindow(this);
        Logger.Debug("MainWindow", "ViewModel connected to window");

        SetupTrayIcon();
        SetupTrayIconRefreshTimer();
        SetupGlobalHotkeys();
        Logger.Info("MainWindow", "Initialization complete");
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        // Check for TaskbarCreated message (Explorer restart)
        if (msg == _taskbarCreatedMsg)
        {
            Logger.Info("MainWindow", "TaskbarCreated message received - Explorer likely restarted, recreating tray icon");
            handled = true;
            // Recreate tray icon with delay to ensure Explorer is ready
            Dispatcher.BeginInvoke(new Action(() =>
            {
                try
                {
                    RecreateTrayIcon();
                }
                catch (Exception ex)
                {
                    Logger.Error("MainWindow", "Failed to recreate tray icon after Explorer restart", ex);
                }
            }), DispatcherPriority.Background, TimeSpan.FromSeconds(2));
        }
        return IntPtr.Zero;
    }

    private void RecreateTrayIcon()
    {
        Logger.Info("MainWindow", "Recreating tray icon...");
        try
        {
            // Dispose existing tray icon
            if (_trayIcon != null)
            {
                _trayIcon.Visibility = Visibility.Collapsed;
                _trayIcon = null;
                Logger.Debug("MainWindow", "Existing tray icon hidden");
            }

            _isTrayIconInitialized = false;

            // Small delay to ensure cleanup
            System.Threading.Thread.Sleep(100);

            // Re-setup tray icon
            SetupTrayIcon();

            if (_trayIcon != null)
            {
                // Force refresh by toggling visibility
                _trayIcon.Visibility = Visibility.Collapsed;
                _trayIcon.Visibility = Visibility.Visible;
                Logger.Info("MainWindow", "Tray icon recreated successfully");
            }
            else
            {
                Logger.Warning("MainWindow", "Tray icon recreation failed - will retry on next timer tick");
            }
        }
        catch (Exception ex)
        {
            Logger.Error("MainWindow", "Error recreating tray icon", ex);
        }
    }

    private void SetupTrayIconRefreshTimer()
    {
        Logger.Info("MainWindow", "Setting up tray icon refresh timer...");
        try
        {
            _trayIconRefreshTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(30) // Check every 30 seconds
            };

            int retryCount = 0;
            const int maxRetries = 3;

            _trayIconRefreshTimer.Tick += (s, e) =>
            {
                try
                {
                    // Check if tray icon needs refreshing
                    if (_trayIcon == null || !_isTrayIconInitialized)
                    {
                        retryCount++;
                        if (retryCount <= maxRetries)
                        {
                            Logger.Warning("MainWindow", $"Tray icon not initialized, attempt {retryCount}/{maxRetries} to recreate...");
                            RecreateTrayIcon();
                        }
                        else
                        {
                            Logger.Error("MainWindow", "Max tray icon retry attempts reached, giving up until next timer cycle");
                            retryCount = 0; // Reset for next cycle
                        }
                    }
                    else
                    {
                        // Icon exists, ensure visibility is set correctly
                        if (_trayIcon.Visibility != Visibility.Visible)
                        {
                            Logger.Warning("MainWindow", "Tray icon visibility was not Visible, correcting...");
                            _trayIcon.Visibility = Visibility.Visible;
                        }
                        retryCount = 0; // Reset counter on success
                    }
                }
                catch (Exception ex)
                {
                    Logger.Error("MainWindow", "Error in tray icon refresh timer", ex);
                }
            };

            _trayIconRefreshTimer.Start();
            Logger.Info("MainWindow", "Tray icon refresh timer started (30s interval)");
        }
        catch (Exception ex)
        {
            Logger.Error("MainWindow", "Failed to setup tray icon refresh timer", ex);
        }
    }

    private void SetupTrayIcon()
    {
        // Prevent duplicate initialization
        if (_isTrayIconInitialized)
        {
            Logger.Debug("MainWindow", "Tray icon already initialized, skipping");
            return;
        }
        
        Logger.Info("MainWindow", "Setting up tray icon...");
        _isTrayIconInitialized = true;
        
        // Tray icon is defined in XAML, ensure it's properly initialized
        _trayIcon = TrayIcon;
        Logger.Debug("MainWindow", $"TrayIcon from XAML: {_trayIcon != null}");
        
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
                Logger.Verbose("MainWindow", $"Checking icon at: {path} - Exists: {exists}");
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
                    Logger.Info("MainWindow", $"Loading icon from: {foundPath}");
                    var icon = new System.Drawing.Icon(foundPath);
                    _trayIcon.Icon = icon;
                    Logger.Info("MainWindow", "Icon loaded successfully");
                }
                catch (Exception ex)
                {
                    Logger.Error("MainWindow", "Failed to load icon", ex);
                }
            }
            else
            {
                Logger.Warning("MainWindow", "Icon file not found in any expected location");
            }
            
            // Ensure visibility is set
            _trayIcon.Visibility = Visibility.Visible;
            
            // Set tooltip with actual version
            var version = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
            _trayIcon.ToolTipText = $"Redball v{version?.Major}.{version?.Minor}.{version?.Build}";
            Logger.Info("MainWindow", $"Tray tooltip set to: {_trayIcon.ToolTipText}");
        }
        else
        {
            Logger.Error("MainWindow", "TrayIcon not found in XAML!");
        }
        
        Logger.Info("MainWindow", "Tray icon setup complete");
    }

    private void SetupGlobalHotkeys()
    {
        Logger.Info("MainWindow", "Setting up global hotkeys...");
        try
        {
            var hwndSource = PresentationSource.FromVisual(this) as HwndSource;
            if (hwndSource == null)
            {
                Logger.Warning("MainWindow", "HwndSource not available for hotkey registration");
                return;
            }
            Logger.Debug("MainWindow", $"HwndSource obtained: {hwndSource.Handle}");

            _hotkeyService = new HotkeyService(hwndSource);

            // Register Ctrl+Alt+Pause to toggle active state
            Logger.Debug("MainWindow", "Registering Ctrl+Alt+Pause hotkey...");
            _hotkeyService.RegisterHotkey(1, HotkeyService.MOD_CONTROL | HotkeyService.MOD_ALT, 0x13 /* VK_PAUSE */, () =>
            {
                Logger.Info("MainWindow", "Hotkey: Ctrl+Alt+Pause - Toggle active");
                _viewModel?.ToggleActiveCommand.Execute(null);
            });

            // Register TypeThing start hotkey from config
            var startHotkey = ConfigService.Instance.Config.TypeThingStartHotkey ?? "Ctrl+Shift+V";
            Logger.Debug("MainWindow", $"TypeThing start hotkey from config: {startHotkey}");
            var (startMods, startKey) = HotkeyService.ParseHotkey(startHotkey);
            if (startKey != 0)
            {
                _hotkeyService.RegisterHotkey(100, startMods, startKey, () =>
                {
                    Logger.Info("MainWindow", $"Hotkey: {startHotkey} - TypeThing start");
                    StartTypeThing();
                });
            }
            else
            {
                Logger.Warning("MainWindow", $"Could not parse TypeThing start hotkey: {startHotkey}");
            }

            // Register TypeThing stop hotkey from config
            var stopHotkey = ConfigService.Instance.Config.TypeThingStopHotkey ?? "Ctrl+Shift+X";
            Logger.Debug("MainWindow", $"TypeThing stop hotkey from config: {stopHotkey}");
            var (stopMods, stopKey) = HotkeyService.ParseHotkey(stopHotkey);
            if (stopKey != 0)
            {
                _hotkeyService.RegisterHotkey(101, stopMods, stopKey, () =>
                {
                    Logger.Info("MainWindow", $"Hotkey: {stopHotkey} - TypeThing stop");
                });
            }

            Logger.Info("MainWindow", "Global hotkeys registered successfully");
        }
        catch (Exception ex)
        {
            Logger.Error("MainWindow", "Failed to register global hotkeys", ex);
        }
    }

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        Logger.Debug("MainWindow", $"OnClosing called, Cancel={e.Cancel}");
        // If closing from tray exit command, allow close
        // If closing from X button, move off-screen instead (tray-only mode)
        if (_trayIcon?.Visibility == Visibility.Visible && e.Cancel == false)
        {
            Logger.Info("MainWindow", "Moving window off-screen instead of closing (tray-only mode)");
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
        Logger.Info("MainWindow", "ShowSettings called");
        Dispatcher.Invoke(() =>
        {
            if (_settingsWindow != null && _settingsWindow.IsLoaded)
            {
                Logger.Debug("MainWindow", "Settings window already open, activating");
                _settingsWindow.Activate();
                _settingsWindow.Focus();
                return;
            }
            Logger.Debug("MainWindow", "Creating new SettingsWindow");
            _settingsWindow = new Views.SettingsWindow(this);
            _settingsWindow.Closed += (s, e) =>
            {
                Logger.Info("MainWindow", "Settings window closed, reloading hotkeys");
                _settingsWindow = null;
                ReloadHotkeys();
            };
            _settingsWindow.Show();
            Logger.Info("MainWindow", "Settings window shown");
        });
    }

    public void ShowAbout()
    {
        Logger.Info("MainWindow", "ShowAbout called");
        Dispatcher.Invoke(() =>
        {
            if (_aboutWindow != null && _aboutWindow.IsLoaded)
            {
                Logger.Debug("MainWindow", "About window already open, activating");
                _aboutWindow.Activate();
                _aboutWindow.Focus();
                return;
            }
            Logger.Debug("MainWindow", "Creating new AboutWindow");
            _aboutWindow = new Views.AboutWindow();
            _aboutWindow.Closed += (s, e) =>
            {
                Logger.Debug("MainWindow", "About window closed");
                _aboutWindow = null;
            };
            _aboutWindow.Show();
            Logger.Info("MainWindow", "About window shown");
        });
    }

    public void StartTypeThing()
    {
        if (_isTyping)
        {
            Logger.Warning("MainWindow", "TypeThing already running, ignoring request");
            if (_trayIcon != null)
            {
                _trayIcon.ShowBalloonTip("TypeThing", "Already typing! Please wait for current operation to complete.",
                    Hardcodet.Wpf.TaskbarNotification.BalloonIcon.Warning);
            }
            return;
        }

        Logger.Info("MainWindow", "StartTypeThing called");
        _isTyping = true;
        Dispatcher.Invoke(() =>
        {
            try
            {
                var clipboardText = System.Windows.Clipboard.GetText();
                if (string.IsNullOrEmpty(clipboardText))
                {
                    Logger.Warning("MainWindow", "TypeThing: Clipboard is empty");
                    if (_trayIcon != null)
                    {
                        _trayIcon.ShowBalloonTip("TypeThing", "Clipboard is empty. Copy some text first.",
                            Hardcodet.Wpf.TaskbarNotification.BalloonIcon.Warning);
                    }
                    return;
                }

                Logger.Info("MainWindow", $"TypeThing: Got {clipboardText.Length} chars from clipboard");

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
                        Logger.Info("MainWindow", "TypeThing: Starting typing");
                        TypeText(clipboardText);
                    }
                    else
                    {
                        Logger.Debug("MainWindow", $"TypeThing: Starting in {countdown}...");
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
                Logger.Error("MainWindow", "TypeThing error", ex);
                _isTyping = false;
            }
        });
    }

    private void TypeText(string text)
    {
        var isRdp = IsRemoteSession();
        Logger.Info("MainWindow", $"TypeThing: Begin typing {text.Length} chars (RDP session: {isRdp})");
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
                _isTyping = false;
                Logger.Info("MainWindow", "TypeThing: Typing complete");
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
                SendKeyPress(0x0D); // VK_RETURN
            }
            else if (ch == '\r')
            {
                // Skip carriage return (handled by \n)
            }
            else if (ch == '\t')
            {
                SendKeyPress(0x09); // VK_TAB
            }
            else
            {
                SendCharacter(ch);
            }
            index++;

            // Randomize interval for human-like typing
            timer.Interval = TimeSpan.FromMilliseconds(30 + new Random().Next(90));
        };
        timer.Start();
    }

    public void ReloadHotkeys()
    {
        Logger.Info("MainWindow", "Reloading hotkeys from config...");
        try
        {
            _hotkeyService?.Dispose();
            SetupGlobalHotkeys();
            Logger.Info("MainWindow", "Hotkeys reloaded successfully");
        }
        catch (Exception ex)
        {
            Logger.Error("MainWindow", "Failed to reload hotkeys", ex);
        }
    }

    public void SuspendHotkeys()
    {
        Logger.Info("MainWindow", "Suspending hotkeys for key capture...");
        try
        {
            _hotkeyService?.Dispose();
            _hotkeyService = null;
            Logger.Info("MainWindow", "Hotkeys suspended");
        }
        catch (Exception ex)
        {
            Logger.Error("MainWindow", "Failed to suspend hotkeys", ex);
        }
    }

    public void ResumeHotkeys()
    {
        Logger.Info("MainWindow", "Resuming hotkeys after key capture...");
        SetupGlobalHotkeys();
    }

    public void ExitApplication()
    {
        Logger.Info("MainWindow", "ExitApplication called");
        _trayIconRefreshTimer?.Stop();
        _trayIconRefreshTimer = null;
        _hotkeyService?.Dispose();
        if (_trayIcon != null)
        {
            _trayIcon.Dispose();
            _trayIcon = null;
        }
        Logger.Info("MainWindow", "Shutting down application");
        Application.Current.Shutdown();
    }

    #region P/Invoke SendInput helpers

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern uint RegisterWindowMessage(string lpString);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

    [DllImport("user32.dll")]
    private static extern bool PostMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int nIndex);

    private const int SM_REMOTESESSION = 0x1000;
    private const uint WM_KEYDOWN = 0x0100;
    private const uint WM_KEYUP = 0x0101;
    private const uint WM_CHAR = 0x0102;
    private const int INPUT_KEYBOARD = 1;
    private const uint KEYEVENTF_KEYUP = 0x0002;
    private const uint KEYEVENTF_UNICODE = 0x0004;

    [StructLayout(LayoutKind.Sequential)]
    private struct INPUT
    {
        public int type;
        public INPUTUNION u;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct INPUTUNION
    {
        [FieldOffset(0)] public MOUSEINPUT mi;
        [FieldOffset(0)] public KEYBDINPUT ki;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MOUSEINPUT
    {
        public int dx;
        public int dy;
        public uint mouseData;
        public uint dwFlags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KEYBDINPUT
    {
        public ushort wVk;
        public ushort wScan;
        public uint dwFlags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    private static bool IsRemoteSession()
    {
        return GetSystemMetrics(SM_REMOTESESSION) != 0;
    }

    private static void SendKeyPress(ushort vk)
    {
        if (IsRemoteSession())
        {
            // Use PostMessage for RDP sessions
            var hwnd = GetForegroundWindow();
            if (hwnd != IntPtr.Zero)
            {
                PostMessage(hwnd, WM_KEYDOWN, (IntPtr)vk, IntPtr.Zero);
                PostMessage(hwnd, WM_KEYUP, (IntPtr)vk, unchecked((nint)0xC0000001));
            }
        }
        else
        {
            // Use SendInput for local sessions (more reliable)
            var inputs = new INPUT[2];
            inputs[0].type = INPUT_KEYBOARD;
            inputs[0].u.ki.wVk = vk;
            inputs[1].type = INPUT_KEYBOARD;
            inputs[1].u.ki.wVk = vk;
            inputs[1].u.ki.dwFlags = KEYEVENTF_KEYUP;
            SendInput(2, inputs, Marshal.SizeOf<INPUT>());
        }
    }

    private static void SendCharacter(char ch)
    {
        if (IsRemoteSession())
        {
            // Use PostMessage for RDP sessions
            var hwnd = GetForegroundWindow();
            if (hwnd != IntPtr.Zero)
            {
                PostMessage(hwnd, WM_CHAR, (IntPtr)ch, IntPtr.Zero);
            }
        }
        else
        {
            // Use SendInput for local sessions
            var inputs = new INPUT[2];
            inputs[0].type = INPUT_KEYBOARD;
            inputs[0].u.ki.wScan = (ushort)ch;
            inputs[0].u.ki.dwFlags = KEYEVENTF_UNICODE;
            inputs[1].type = INPUT_KEYBOARD;
            inputs[1].u.ki.wScan = (ushort)ch;
            inputs[1].u.ki.dwFlags = KEYEVENTF_UNICODE | KEYEVENTF_KEYUP;
            SendInput(2, inputs, Marshal.SizeOf<INPUT>());
        }
    }

    #endregion
}
