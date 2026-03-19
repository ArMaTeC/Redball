using Hardcodet.Wpf.TaskbarNotification;

namespace Redball.UI.Services;

/// <summary>
/// Interface for centralized notification service.
/// </summary>
public interface INotificationService
{
    void SetTrayIcon(TaskbarIcon trayIcon);
    void ShowInfo(string title, string message);
    void ShowWarning(string title, string message);
    void ShowError(string title, string message);
    void Show(string title, string message, BalloonIcon icon = BalloonIcon.Info);
}
