using System;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using Redball.UI.Services;

namespace Redball.UI.Views;

public partial class MiniWidgetWindow : Window
{
    private const double ScreenEdgeSnapDistance = 16;
    private const double DefaultWidgetWidth = 232;
    private const double DefaultWidgetHeight = 118;

    private readonly DispatcherTimer _refreshTimer;
    private readonly EventHandler<bool> _activeStateChangedHandler;
    private readonly EventHandler _heartbeatHandler;
    private readonly System.Windows.Media.Animation.Storyboard? _heartbeatStoryboard;

    public MiniWidgetWindow()
    {
        InitializeComponent();
        RestorePosition();

        _heartbeatStoryboard = TryFindResource("HeartbeatStoryboard") as System.Windows.Media.Animation.Storyboard;

        RefreshState();

        _activeStateChangedHandler = (_, _) => Dispatcher.BeginInvoke(RefreshState);
        _heartbeatHandler = (_, _) => Dispatcher.BeginInvoke(PlayHeartbeatEffect);

        KeepAwakeService.Instance.ActiveStateChanged += _activeStateChangedHandler;
        KeepAwakeService.Instance.HeartbeatTick += _heartbeatHandler;

        _refreshTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) }; // Faster refresh for timer
        _refreshTimer.Tick += (_, _) => RefreshState();
        _refreshTimer.Start();

        // Apply entrance animation
        Loaded += OnWindowLoaded;
    }

    private void OnWindowLoaded(object sender, RoutedEventArgs e)
    {
        try
        {
            // Apply scale and fade entrance animation
            var transform = new ScaleTransform(0.9, 0.9);
            RenderTransform = transform;
            RenderTransformOrigin = new Point(0.5, 0.5);
            Opacity = 0;

            var scaleAnimation = new DoubleAnimation
            {
                From = 0.9,
                To = 1.0,
                Duration = TimeSpan.FromMilliseconds(250),
                EasingFunction = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.3 }
            };

            var fadeAnimation = new DoubleAnimation
            {
                From = 0,
                To = 1,
                Duration = TimeSpan.FromMilliseconds(200),
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };

            transform.BeginAnimation(ScaleTransform.ScaleXProperty, scaleAnimation);
            transform.BeginAnimation(ScaleTransform.ScaleYProperty, scaleAnimation);
            BeginAnimation(OpacityProperty, fadeAnimation);
        }
        catch (Exception ex)
        {
            Logger.Warning("MiniWidget", $"Failed to apply entrance animation: {ex.Message}");
        }
    }

    private void PlayHeartbeatEffect()
    {
        _heartbeatStoryboard?.Begin();
    }

    private void RefreshState()
    {
        var ka = KeepAwakeService.Instance;
        var config = ConfigService.Instance.Config;

        Topmost = config.MiniWidgetAlwaysOnTop;
        Opacity = Math.Clamp(config.MiniWidgetOpacityPercent, 35, 100) / 100.0;
        QuickActionsPanel.Visibility = config.MiniWidgetShowQuickActions ? Visibility.Visible : Visibility.Collapsed;
        StatusIconsPanel.Visibility = config.MiniWidgetShowStatusIcons ? Visibility.Visible : Visibility.Collapsed;
        OpenDashboardBtn.Visibility = config.MiniWidgetDoubleClickOpensDashboard ? Visibility.Visible : Visibility.Collapsed;
        LockPositionBtn.Content = config.MiniWidgetLockPosition ? "\uE72F" : "\uE72E";
        LockPositionBtn.ToolTip = config.MiniWidgetLockPosition ? "Unlock widget position" : "Lock widget position";
        UpdatePresetBadge(config.MiniWidgetPreset);

        var customQuickMinutes = Math.Clamp(config.MiniWidgetCustomQuickMinutes, 1, 720);
        QuickAddCustomBtn.Content = $"+{customQuickMinutes}m";
        QuickAddCustomBtn.ToolTip = $"Start or extend timed session by {customQuickMinutes} minutes";
        
        // Status Text & Color
        StatusText.Text = ka.GetStatusText();
        
        if (ka.IsActive)
        {
            StatusDot.Fill = ka.Until.HasValue
                ? new SolidColorBrush(Color.FromRgb(253, 126, 20))   // Orange for timed
                : new SolidColorBrush(Color.FromRgb(76, 175, 80));   // Green for active
            ToggleBtn.Content = "\uE769"; // Pause icon
        }
        else
        {
            StatusDot.Fill = new SolidColorBrush(Color.FromRgb(108, 117, 125)); // Gray for paused
            ToggleBtn.Content = "\uE768"; // Play icon
        }

        // Active Timer / Countdown
        if (ka.IsActive && ka.StartTime.HasValue)
        {
            if (ka.Until.HasValue)
            {
                var remaining = ka.Until.Value - DateTime.Now;
                var total = (ka.Until.Value - ka.StartTime.Value).TotalSeconds;
                var elapsed = (DateTime.Now - ka.StartTime.Value).TotalSeconds;

                if (remaining.TotalSeconds > 0)
                {
                    TimeText.Text = remaining.TotalMinutes >= 1 
                        ? $"{(int)remaining.TotalMinutes}m remaining"
                        : $"{(int)remaining.TotalSeconds}s remaining";
                    UpdateProgressRing(total > 0 ? elapsed / total : 1);
                }
                else
                {
                    TimeText.Text = "Expiring...";
                    UpdateProgressRing(1);
                }
            }
            else
            {
                var duration = DateTime.Now - ka.StartTime.Value;
                TimeText.Text = duration.TotalHours >= 1
                    ? $"{(int)duration.TotalHours}h {(int)duration.Minutes}m"
                    : $"{(int)duration.TotalMinutes}m active";
                ProgressRing.Visibility = Visibility.Collapsed;
            }
        }
        else
        {
            TimeText.Text = "";
            ProgressRing.Visibility = Visibility.Collapsed;
        }

        // Mode Text
        ModeText.Text = config.HeartbeatInputMode;

        // Battery Icon
        var batteryStatus = StaticBatteryMonitor.GetStatus(); // Using a static helper or instance
        if (batteryStatus.HasBattery)
        {
            BatteryIcon.Visibility = Visibility.Visible;
            BatteryIcon.Text = GetBatteryIcon(batteryStatus.ChargePercent, batteryStatus.IsOnBattery);
            BatteryIcon.Foreground = batteryStatus.ChargePercent <= 20 && batteryStatus.IsOnBattery
                ? new SolidColorBrush(Color.FromRgb(220, 53, 69)) // Red for low
                : (SolidColorBrush)FindResource("ForegroundSecondaryBrush");
        }
        else
        {
            BatteryIcon.Visibility = Visibility.Collapsed;
        }

        // Network Icon
        var isVpn = StaticNetworkMonitor.IsVpnConnected();
        var isConnected = StaticNetworkMonitor.IsConnected();
        
        NetworkIcon.Text = isConnected ? "\uE774" : "\uEB55"; // Check or Disconnected
        VpnIcon.Visibility = isVpn ? Visibility.Visible : Visibility.Collapsed;
    }

    private string GetBatteryIcon(int percent, bool onBattery)
    {
        if (!onBattery) return "\uEBA1"; // Charging
        if (percent > 90) return "\uEBAA"; // Full
        if (percent > 70) return "\uEBA8";
        if (percent > 50) return "\uEBA6";
        if (percent > 30) return "\uEBA4";
        return "\uEBA0"; // Low
    }

    private void UpdateProgressRing(double percentage)
    {
        percentage = Math.Clamp(percentage, 0, 0.999);
        ProgressRing.Visibility = Visibility.Visible;
        
        double radius = 15;
        double angle = percentage * 360;
        
        var startPoint = new Point(16, 1);
        var endPoint = new Point(
            16 + radius * Math.Sin(angle * Math.PI / 180),
            16 - radius * Math.Cos(angle * Math.PI / 180));

        bool isLargeArc = angle > 180;
        
        var geometry = new PathGeometry();
        var figure = new PathFigure { StartPoint = startPoint, IsClosed = false };
        figure.Segments.Add(new ArcSegment(endPoint, new Size(radius, radius), 0, isLargeArc, SweepDirection.Clockwise, true));
        geometry.Figures.Add(figure);
        
        ProgressRing.Data = geometry;
    }

    private void RestorePosition()
    {
        var config = ConfigService.Instance.Config;
        var left = config.MiniWidgetLeft;
        var top = config.MiniWidgetTop;

        if (left >= 0 && top >= 0 && IsPositionOnScreen(left, top))
        {
            WindowStartupLocation = WindowStartupLocation.Manual;
            Left = left;
            Top = top;
        }
        else
        {
            SetDefaultPosition();
        }
    }

    private void SetDefaultPosition()
    {
        WindowStartupLocation = WindowStartupLocation.Manual;
        var workArea = SystemParameters.WorkArea;
        Left = workArea.Right - GetWidgetWidth() - ScreenEdgeSnapDistance;
        Top = workArea.Bottom - GetWidgetHeight() - ScreenEdgeSnapDistance;
    }

    private bool IsPositionOnScreen(double left, double top)
    {
        const double margin = 40;
        var width = GetWidgetWidth();
        var height = GetWidgetHeight();
        var virtualLeft = SystemParameters.VirtualScreenLeft - margin;
        var virtualTop = SystemParameters.VirtualScreenTop - margin;
        var virtualRight = SystemParameters.VirtualScreenLeft + SystemParameters.VirtualScreenWidth + margin;
        var virtualBottom = SystemParameters.VirtualScreenTop + SystemParameters.VirtualScreenHeight + margin;

        return left + width > virtualLeft && left < virtualRight &&
               top + height > virtualTop && top < virtualBottom;
    }

    private double GetWidgetWidth() => Width > 0 ? Width : DefaultWidgetWidth;

    private double GetWidgetHeight() => Height > 0 ? Height : DefaultWidgetHeight;

    private void SnapToWorkArea()
    {
        var workArea = SystemParameters.WorkArea;
        var width = GetWidgetWidth();
        var height = GetWidgetHeight();

        Left = Math.Clamp(Left, workArea.Left, workArea.Right - width);
        Top = Math.Clamp(Top, workArea.Top, workArea.Bottom - height);

        if (Math.Abs(Left - workArea.Left) <= ScreenEdgeSnapDistance)
            Left = workArea.Left;
        else if (Math.Abs((workArea.Right - width) - Left) <= ScreenEdgeSnapDistance)
            Left = workArea.Right - width;

        if (Math.Abs(Top - workArea.Top) <= ScreenEdgeSnapDistance)
            Top = workArea.Top;
        else if (Math.Abs((workArea.Bottom - height) - Top) <= ScreenEdgeSnapDistance)
            Top = workArea.Bottom - height;
    }

    private void SavePosition()
    {
        try
        {
            var config = ConfigService.Instance.Config;
            config.MiniWidgetLeft = Left;
            config.MiniWidgetTop = Top;
            ConfigService.Instance.Save();
        }
        catch (Exception ex)
        {
            Logger.Warning("MiniWidget", $"Failed to save position: {ex.Message}");
        }
    }

    public void ResetPosition()
    {
        SetDefaultPosition();
        SavePosition();
        Activate();
    }

    private void ToggleBtn_Click(object sender, RoutedEventArgs e)
    {
        KeepAwakeService.Instance.Toggle();
        RefreshState();
    }

    private void LockPositionBtn_Click(object sender, RoutedEventArgs e)
    {
        var config = ConfigService.Instance.Config;
        config.MiniWidgetLockPosition = !config.MiniWidgetLockPosition;
        config.MiniWidgetPreset = MiniWidgetPresetService.Custom;
        ConfigService.Instance.Save();
        RefreshState();
    }

    private void UpdatePresetBadge(string? preset)
    {
        var normalizedPreset = MiniWidgetPresetService.NormalizePreset(preset);

        switch (normalizedPreset)
        {
            case MiniWidgetPresetService.Focus:
                PresetBadgeText.Text = "FOCUS";
                PresetBadgeBorder.Background = new SolidColorBrush(Color.FromArgb(0x40, 0x0D, 0x6E, 0xFD));
                PresetBadgeBorder.BorderBrush = new SolidColorBrush(Color.FromArgb(0x99, 0x4D, 0x94, 0xFF));
                PresetBadgeText.Foreground = new SolidColorBrush(Color.FromRgb(0xDB, 0xE9, 0xFF));
                break;
            case MiniWidgetPresetService.Meeting:
                PresetBadgeText.Text = "MEETING";
                PresetBadgeBorder.Background = new SolidColorBrush(Color.FromArgb(0x40, 0xFF, 0xA7, 0x00));
                PresetBadgeBorder.BorderBrush = new SolidColorBrush(Color.FromArgb(0x99, 0xFF, 0xC2, 0x4A));
                PresetBadgeText.Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0xF0, 0xCC));
                break;
            case MiniWidgetPresetService.BatterySafe:
                PresetBadgeText.Text = "BATTERY";
                PresetBadgeBorder.Background = new SolidColorBrush(Color.FromArgb(0x40, 0x2E, 0xB8, 0x72));
                PresetBadgeBorder.BorderBrush = new SolidColorBrush(Color.FromArgb(0x99, 0x66, 0xD6, 0x9E));
                PresetBadgeText.Foreground = new SolidColorBrush(Color.FromRgb(0xDA, 0xF8, 0xE8));
                break;
            default:
                PresetBadgeText.Text = "CUSTOM";
                PresetBadgeBorder.Background = new SolidColorBrush(Color.FromArgb(0x30, 0x41, 0x4B, 0x5A));
                PresetBadgeBorder.BorderBrush = new SolidColorBrush(Color.FromArgb(0x80, 0x6B, 0x72, 0x80));
                PresetBadgeText.Foreground = (SolidColorBrush)FindResource("ForegroundSecondaryBrush");
                break;
        }
    }

    private void OpenDashboardBtn_Click(object sender, RoutedEventArgs e)
    {
        OpenDashboard();
    }

    private void QuickAdd15mBtn_Click(object sender, RoutedEventArgs e)
    {
        QuickAddTimedMinutes(15);
    }

    private void QuickAdd60mBtn_Click(object sender, RoutedEventArgs e)
    {
        QuickAddTimedMinutes(60);
    }

    private void QuickAddCustomBtn_Click(object sender, RoutedEventArgs e)
    {
        var minutes = Math.Clamp(ConfigService.Instance.Config.MiniWidgetCustomQuickMinutes, 1, 720);
        QuickAddTimedMinutes(minutes);
    }

    private void QuickResetPositionBtn_Click(object sender, RoutedEventArgs e)
    {
        ResetPosition();
    }

    private void CloseBtn_Click(object sender, RoutedEventArgs e)
    {
        if (!ShouldAllowClose())
        {
            return;
        }

        Close();
    }

    private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left)
        {
            Focus();

            if (ConfigService.Instance.Config.MiniWidgetLockPosition)
            {
                return;
            }

            DragMove();

            if (ConfigService.Instance.Config.MiniWidgetSnapToScreenEdges)
            {
                SnapToWorkArea();
            }

            SavePosition();
        }
    }

    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        var config = ConfigService.Instance.Config;
        if (!config.MiniWidgetEnableKeyboardShortcuts)
        {
            return;
        }

        switch (e.Key)
        {
            case Key.Space:
                KeepAwakeService.Instance.Toggle();
                RefreshState();
                e.Handled = true;
                break;
            case Key.D:
                OpenDashboard();
                e.Handled = true;
                break;
            case Key.D1:
            case Key.NumPad1:
                QuickAddTimedMinutes(15);
                e.Handled = true;
                break;
            case Key.D2:
            case Key.NumPad2:
                QuickAddTimedMinutes(60);
                e.Handled = true;
                break;
            case Key.OemPlus:
            case Key.Add:
                QuickAddTimedMinutes(Math.Clamp(config.MiniWidgetCustomQuickMinutes, 1, 720));
                e.Handled = true;
                break;
            case Key.Escape:
                if (ShouldAllowClose())
                {
                    Close();
                }
                e.Handled = true;
                break;
        }
    }

    private void Window_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left)
        {
            return;
        }

        if (ConfigService.Instance.Config.MiniWidgetDoubleClickOpensDashboard)
        {
            OpenDashboard();
            return;
        }

        KeepAwakeService.Instance.Toggle();
        RefreshState();
    }

    private void QuickAddTimedMinutes(int minutes)
    {
        var keepAwake = KeepAwakeService.Instance;
        var now = DateTime.Now;

        var baseTime = keepAwake.Until.HasValue && keepAwake.Until.Value > now
            ? keepAwake.Until.Value
            : now;

        keepAwake.SetActive(true, baseTime.AddMinutes(minutes));
        RefreshState();
    }

    private void OpenDashboard()
    {
        var mainWindow = Application.Current.Windows.OfType<MainWindow>().FirstOrDefault();
        if (mainWindow == null)
        {
            return;
        }

        if (!mainWindow.IsVisible)
        {
            mainWindow.Show();
        }

        if (mainWindow.WindowState == WindowState.Minimized)
        {
            mainWindow.WindowState = WindowState.Normal;
        }

        mainWindow.Activate();
    }

    private bool ShouldAllowClose()
    {
        var config = ConfigService.Instance.Config;
        if (!config.MiniWidgetConfirmCloseWhenActive || !KeepAwakeService.Instance.IsActive)
        {
            return true;
        }

        return NotificationWindow.Show(
            "Close Mini Widget",
            "Keep-awake is active. Close the mini widget anyway?",
            "\uE7BA",
            true);
    }

    protected override void OnClosed(EventArgs e)
    {
        _refreshTimer.Stop();
        KeepAwakeService.Instance.ActiveStateChanged -= _activeStateChangedHandler;
        KeepAwakeService.Instance.HeartbeatTick -= _heartbeatHandler;
        Loaded -= OnWindowLoaded;
        SavePosition();
        base.OnClosed(e);
    }
}

// Static helpers to avoid creating new service instances or if services are accessible via Instance
internal static class StaticBatteryMonitor 
{
    private static readonly BatteryMonitorService _monitor = new();
    public static BatteryStatus GetStatus() => _monitor.GetStatus();
}

internal static class StaticNetworkMonitor
{
    private static readonly NetworkMonitorService _monitor = new();
    public static bool IsVpnConnected() => _monitor.IsVpnConnected();
    public static bool IsConnected() => _monitor.IsConnected();
}
