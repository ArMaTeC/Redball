using System;
using Hardcodet.Wpf.TaskbarNotification;

namespace Redball.UI.Services;

/// <summary>
/// Centralized notification service for tray balloon tips.
/// Port of Send-RedballToast and balloon tip logic.
/// Uses the Hardcodet TaskbarIcon for WPF-native notifications.
/// </summary>
public class NotificationService : INotificationService
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

        // Respect Windows Focus Assist / Do Not Disturb
        var focusAssist = Interop.NativeMethods.GetFocusAssistStatus();
        if (focusAssist > 0)
        {
            Logger.Verbose("NotificationService", $"Notification suppressed (Focus Assist mode {focusAssist}): {title}");
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

        // Play system sound if enabled
        if (config.SoundNotifications)
        {
            try
            {
                var sound = icon switch
                {
                    BalloonIcon.Error => System.Media.SystemSounds.Hand,
                    BalloonIcon.Warning => System.Media.SystemSounds.Exclamation,
                    _ => System.Media.SystemSounds.Asterisk
                };
                sound.Play();
            }
            catch (Exception ex)
            {
                Logger.Debug("NotificationService", $"Failed to play notification sound: {ex.Message}");
            }
        }

        try
        {
            if (_trayIcon != null)
            {
                // Prefer themed custom toast on Win10/11
                var toastType = icon switch
                {
                    BalloonIcon.Warning => Views.ToastType.Warning,
                    BalloonIcon.Error => Views.ToastType.Error,
                    _ => Views.ToastType.Info
                };
                ShowCustomToast(title, message, toastType);
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

    /// <summary>
    /// Shows a themed custom toast notification via the tray icon.
    /// Falls back to standard balloon tip if custom display fails.
    /// </summary>
    public void ShowCustomToast(string title, string message, Views.ToastType type = Views.ToastType.Info, int autoCloseSec = 5)
    {
        if (_trayIcon == null) return;

        try
        {
            var toast = new Views.ToastNotification(title, message, type, autoCloseSec);
            _trayIcon.ShowCustomBalloon(toast, System.Windows.Controls.Primitives.PopupAnimation.Slide, autoCloseSec * 1000);
        }
        catch (Exception ex)
        {
            Logger.Debug("NotificationService", $"Custom toast failed, falling back to balloon: {ex.Message}");
            var icon = type switch
            {
                Views.ToastType.Warning => BalloonIcon.Warning,
                Views.ToastType.Error => BalloonIcon.Error,
                _ => BalloonIcon.Info
            };
            _trayIcon.ShowBalloonTip(title, message, icon);
        }
    }
}
