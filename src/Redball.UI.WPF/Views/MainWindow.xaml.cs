using System;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using Hardcodet.Wpf.TaskbarNotification;
using Redball.UI.Interop;
using Redball.UI.Services;

namespace Redball.UI.Views;

/// <summary>
/// Main window for Redball v3.0 WPF UI.
/// Primarily a tray-only application with optional window interface.
/// Split into partial classes: TrayIcon, Settings, Navigation, TypeThing.
/// </summary>
public partial class MainWindow : Window
{
    private ViewModels.MainViewModel? _viewModel;
    private TaskbarIcon? _trayIcon;
    private bool _isTrayIconInitialized;
    private DispatcherTimer? _trayIconRefreshTimer;
    private uint _taskbarCreatedMsg;
    private HwndSource? _windowHwndSource;
    private readonly AnalyticsService _analytics = AnalyticsService.Instance;
    private readonly Random _random = new();
    private UpdateService? _updateService;

    private Views.AboutWindow? _aboutWindow;
    private HotkeyService? _hotkeyService;
    private bool _isTyping;
    private bool _isLoadingSettings = true;
    private bool _isExiting;
    private DispatcherTimer? _typeThingCountdownTimer;
    private DispatcherTimer? _typeThingTimer;
    private DispatcherTimer? _sloDashboardRefreshTimer;

    public MainWindow()
    {
        Logger.Info("MainWindow", "Constructor called");
        InitializeComponent();
        _taskbarCreatedMsg = RegisterWindowMessage("TaskbarCreated");
        Logger.Debug("MainWindow", $"TaskbarCreated message ID: {_taskbarCreatedMsg}");
        Loaded += OnWindowLoaded;
        Logger.Debug("MainWindow", "Constructor completed");
    }

    private void OnWindowLoaded(object sender, RoutedEventArgs e)
    {
        Logger.Info("MainWindow", "Window loaded, initializing services...");
        SyncWindowChromeButtons();
        try
        {
            // Hook window messages for taskbar recreation
            _windowHwndSource = PresentationSource.FromVisual(this) as HwndSource;
            if (_windowHwndSource != null)
            {
                _windowHwndSource.AddHook(WndProc);
                Logger.Debug("MainWindow", "Window message hook added for tray icon recovery");
                
                // --- MICA BACKDROP SUPPORT ---
                try
                {
                    var hwnd = _windowHwndSource.Handle;
                    var micaValue = (int)NativeMethods.DWM_SYSTEMBACKDROP_TYPE.DWMSBT_MAINWINDOW;
                    var darkMode = ConfigService.Instance.Config.Theme == "Dark" ? 1 : 0;
                    
                    // Set Immersive Dark Mode attribute for title bar
                    NativeMethods.DwmSetWindowAttribute(hwnd, NativeMethods.DWMWA_USE_IMMERSIVE_DARK_MODE, ref darkMode, sizeof(int));
                    
                    // Set Mica background (Windows 11 22H2+)
                    NativeMethods.DwmSetWindowAttribute(hwnd, NativeMethods.DWMWA_SYSTEMBACKDROP_TYPE, ref micaValue, sizeof(int));
                    
                    Logger.Info("MainWindow", "DWM Mica backdrop and dark mode attributes applied");
                }
                catch (Exception dwmEx)
                {
                    Logger.Debug("MainWindow", $"Failed to apply DWM attributes: {dwmEx.Message}");
                }
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
            LoadEmbeddedDashboardContent();
            RefreshTemplateCombo();
            StartAutoUpdateCheck();
            InitializeResourceBudgetMonitoring();
            InitializeMemoryPressureMonitoring();
            Logger.Info("MainWindow", "Initialization complete");

            // Apply window entrance animation
            ApplyWindowEntranceAnimation();
        }
        catch (Exception ex)
        {
            Logger.Error("MainWindow", "Failed during window initialization", ex);
        }
    }

    private void ApplyWindowEntranceAnimation()
    {
        try
        {
            // Find the main content border for animation
            if (Content is FrameworkElement rootElement)
            {
                // Set initial transform state
                var transform = new ScaleTransform(0.95, 0.95);
                rootElement.RenderTransform = transform;
                rootElement.RenderTransformOrigin = new Point(0.5, 0.5);
                rootElement.Opacity = 0;

                // Create scale animation
                var scaleAnimation = new DoubleAnimation
                {
                    From = 0.95,
                    To = 1.0,
                    Duration = TimeSpan.FromMilliseconds(300),
                    EasingFunction = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.3 }
                };

                // Create fade animation
                var fadeAnimation = new DoubleAnimation
                {
                    From = 0,
                    To = 1,
                    Duration = TimeSpan.FromMilliseconds(250),
                    EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
                };

                // Apply animations
                transform.BeginAnimation(ScaleTransform.ScaleXProperty, scaleAnimation);
                transform.BeginAnimation(ScaleTransform.ScaleYProperty, scaleAnimation);
                rootElement.BeginAnimation(OpacityProperty, fadeAnimation);
            }
        }
        catch (Exception ex)
        {
            Logger.Warning("MainWindow", $"Failed to apply entrance animation: {ex.Message}");
        }
    }

    protected override void OnStateChanged(EventArgs e)
    {
        base.OnStateChanged(e);
        SyncWindowChromeButtons();
    }

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        Logger.Debug("MainWindow", $"OnClosing called, Cancel={e.Cancel}");
        // If closing from tray exit command, allow close
        // If closing from X button, move off-screen instead (tray-only mode)
        if (!_isExiting && _trayIcon?.Visibility == Visibility.Visible && e.Cancel == false)
        {
            Logger.Info("MainWindow", "Moving window off-screen instead of closing (tray-only mode)");
            ShowInTaskbar = false;
            WindowState = WindowState.Minimized;
            Hide();
            e.Cancel = true;
        }
        base.OnClosing(e);
    }

    protected override void OnClosed(EventArgs e)
    {
        Logger.Info("MainWindow", "OnClosed called, cleaning up window resources");

        try
        {
            Loaded -= OnWindowLoaded;

            StopAutoUpdateCheck();

            _trayIconRefreshTimer?.Stop();
            _trayIconRefreshTimer = null;

            _sloDashboardRefreshTimer?.Stop();
            _sloDashboardRefreshTimer = null;

            if (_windowHwndSource != null)
            {
                try
                {
                    _windowHwndSource.RemoveHook(WndProc);
                    Logger.Debug("MainWindow", "Window message hook removed");
                }
                catch (Exception ex)
                {
                    Logger.Warning("MainWindow", $"Failed to remove window hook: {ex.Message}");
                }

                _windowHwndSource = null;
            }

            KeepAwakeService.Instance.ActiveStateChanged -= OnKeepAwakeStateChanged;

            _hotkeyService?.Dispose();
            _hotkeyService = null;

            DisposeTrayIcons();

            if (_trayIcon != null)
            {
                _trayIcon.Visibility = Visibility.Collapsed;
                _trayIcon.Dispose();
                _trayIcon = null;
            }
        }
        catch (Exception ex)
        {
            Logger.Error("MainWindow", "Error during MainWindow cleanup", ex);
        }

        base.OnClosed(e);
    }

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2)
        {
            ToggleWindowState();
            return;
        }

        try
        {
            DragMove();
        }
        catch (InvalidOperationException)
        {
        }
    }

    private void MinimizeWindowButton_Click(object sender, RoutedEventArgs e)
    {
        WindowState = WindowState.Minimized;
    }

    private void MaximizeWindowButton_Click(object sender, RoutedEventArgs e)
    {
        ToggleWindowState();
    }

    private void CloseWindowButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);

        // Global Command Palette Shortcuts
        if (e.Key == Key.P && 
            (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control && 
            (Keyboard.Modifiers & ModifierKeys.Shift) == ModifierKeys.Shift)
        {
            ShowCommandPalette();
            e.Handled = true;
        }
        // Ctrl+K shortcut for Command Palette
        else if (e.Key == Key.K && 
            (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
        {
            ShowCommandPalette();
            e.Handled = true;
        }
    }

    protected override void OnActivated(EventArgs e)
    {
        base.OnActivated(e);
    }

    private void ToggleWindowState()
    {
        WindowState = WindowState == WindowState.Maximized
            ? WindowState.Normal
            : WindowState.Maximized;
    }

    private void ShowCommandPalette()
    {
        try
        {
            _analytics.TrackFeature("command_palette.opened");
            var palette = new Redball.UI.WPF.Views.CommandPaletteWindow();
            palette.ShowDialog();
        }
        catch (Exception ex)
        {
            Logger.Error("MainWindow", "Failed to show command palette", ex);
        }
    }

    private void InitializeMemoryPressureMonitoring()
    {
        try
        {
            // Start memory pressure monitoring every 10 seconds
            MemoryPressureService.Instance.StartMonitoring(TimeSpan.FromSeconds(10));

            // Subscribe to pressure change events
            MemoryPressureService.Instance.PressureChanged += OnMemoryPressureChanged;

            Logger.Info("MainWindow", "Memory pressure monitoring started");
        }
        catch (Exception ex)
        {
            Logger.Warning("MainWindow", $"Failed to start memory pressure monitoring: {ex.Message}");
        }
    }

    private void OnMemoryPressureChanged(object? sender, MemoryPressureEventArgs e)
    {
        Logger.Info("MainWindow", $"Memory pressure event: {e.Level} - {MemoryPressureService.Instance.GetSummary()}");

        // Log degradations that were applied
        if (e.RecommendedActions.Count > 0)
        {
            Logger.Warning("MainWindow", $"Memory degradations applied: {string.Join(", ", e.RecommendedActions)}");
        }
    }

    private void InitializeResourceBudgetMonitoring()
    {
        try
        {
            // Start resource budget monitoring every 30 seconds
            ResourceBudgetService.Instance.StartMonitoring(TimeSpan.FromSeconds(30));
            Logger.Info("MainWindow", "Resource budget monitoring started");
        }
        catch (Exception ex)
        {
            Logger.Warning("MainWindow", $"Failed to start resource budget monitoring: {ex.Message}");
        }
    }

    private void SyncWindowChromeButtons()
    {
        if (MaximizeWindowButton == null)
        {
            return;
        }

        MaximizeWindowButton.Content = WindowState == WindowState.Maximized ? "❐" : "□";
    }
}
