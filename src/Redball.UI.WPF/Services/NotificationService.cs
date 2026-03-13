using System;
using Hardcodet.Wpf.TaskbarNotification;

namespace Redball.UI.Services;

/// <summary>
/// Centralized notification service for tray balloon tips.
/// Port of Send-RedballToast and balloon tip logic.
/// Uses the Hardcodet TaskbarIcon for WPF-native notifications.
/// </summary>
public class NotificationService
{
    private static readonly Lazy<NotificationService> _instance = new(() => new NotificationService());
    public static NotificationService Instance => _instance.Value;

    private TaskbarIcon? _trayIcon;

    private NotificationService()
    {
        Logger.Verbose("NotificationService", "Instance created");
    }

    /// <summary>
    /// Sets the TaskbarIcon reference for showing balloon tips.
    /// Must be called after the tray icon is initialized in MainWindow.
    /// </summary>
    public void SetTrayIcon(TaskbarIcon trayIcon)
    {
        _trayIcon = trayIcon;
        Logger.Debug("NotificationService", "TrayIcon reference set");
    }

    /// <summary>
    /// Shows an informational notification.
    /// </summary>
    public void ShowInfo(string title, string message)
    {
        Show(title, message, BalloonIcon.Info);
    }

    /// <summary>
    /// Shows a warning notification.
    /// </summary>
    public void ShowWarning(string title, string message)
    {
        Show(title, message, BalloonIcon.Warning);
    }

    /// <summary>
    /// Shows an error notification.
    /// </summary>
    public void ShowError(string title, string message)
    {
        Show(title, message, BalloonIcon.Error);
    }

    /// <summary>
    /// Shows a balloon tip notification if notifications are enabled.
    /// Respects the NotificationMode config setting.
    /// </summary>
    public void Show(string title, string message, BalloonIcon icon = BalloonIcon.Info)
    {
        var config = ConfigService.Instance.Config;

        if (!config.ShowNotifications)
        {
            Logger.Verbose("NotificationService", $"Notification suppressed (disabled): {title}");
            return;
        }

        // Filter by notification mode
        switch (config.NotificationMode)
        {
            case NotificationMode.Silent:
                return;
            case NotificationMode.Errors when icon != BalloonIcon.Error:
                return;
            case NotificationMode.Important when icon == BalloonIcon.Info:
                return;
        }

        try
        {
            if (_trayIcon != null)
            {
                _trayIcon.ShowBalloonTip(title, message, icon);
                Logger.Verbose("NotificationService", $"Notification shown: [{icon}] {title}: {message}");
            }
            else
            {
                Logger.Debug("NotificationService", $"TrayIcon not set, notification skipped: {title}");
            }
        }
        catch (Exception ex)
        {
            Logger.Debug("NotificationService", $"Balloon tip failed: {ex.Message}");
        }
    }
}
