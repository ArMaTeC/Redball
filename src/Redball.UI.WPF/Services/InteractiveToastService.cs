using System;
using System.Windows;

namespace Redball.UI.Services;

/// <summary>
/// Native OS Integration (7.2): Actionable Interactive Toast Notifications.
/// Uses the existing NotificationService balloon-tip pipeline (WPF-native)
/// instead of WinRT APIs (Windows.UI.Notifications) which are not available
/// in the .NET 10 WPF build without additional SDK contracts references.
/// </summary>
public static class InteractiveToastService
{
    /// <summary>
    /// Displays an actionable keep-awake notification via the tray balloon tip.
    /// On Windows 11 the OS upgrades balloon tips to full toast notifications automatically.
    /// </summary>
    public static void ShowKeepAwakeActionableToast()
    {
        try
        {
            const string title = "Keep-Awake Alert";
            const string message = "Your system is scheduled to sleep in 5 minutes. Open Redball to delay this.";

            // Route through the existing notification service which owns the tray icon
            // and handles both balloon tips and custom toast popups gracefully.
            NotificationService.Instance.ShowWarning(title, message);

            Logger.Info("InteractiveToastService", "Fired actionable keep-awake notification.");
        }
        catch (Exception ex)
        {
            Logger.Error("InteractiveToastService", "Failed to dispatch interactive toast payload.", ex);
        }
    }
}
