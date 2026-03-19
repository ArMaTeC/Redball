using System;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Threading;
using Hardcodet.Wpf.TaskbarNotification;
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
    private readonly AnalyticsService _analytics = new(ConfigService.Instance.Config.EnableTelemetry);
    private readonly Random _random = new();
    private UpdateService? _updateService;

    private Views.AboutWindow? _aboutWindow;
    private HotkeyService? _hotkeyService;
    private bool _isTyping;
    private bool _isLoadingSettings;
    private DispatcherTimer? _typeThingCountdownTimer;
    private DispatcherTimer? _typeThingTimer;

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
            LoadEmbeddedDashboardContent();
            InitializePomodoro();
            RefreshTemplateCombo();
            Logger.Info("MainWindow", "Initialization complete");
        }
        catch (Exception ex)
        {
            Logger.Error("MainWindow", "Failed during window initialization", ex);
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
        if (_trayIcon?.Visibility == Visibility.Visible && e.Cancel == false)
        {
            Logger.Info("MainWindow", "Moving window off-screen instead of closing (tray-only mode)");
            ShowInTaskbar = false;
            WindowState = WindowState.Minimized;
            Hide();
            e.Cancel = true;
        }
        base.OnClosing(e);
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

    private void ToggleWindowState()
    {
        WindowState = WindowState == WindowState.Maximized
            ? WindowState.Normal
            : WindowState.Maximized;
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
