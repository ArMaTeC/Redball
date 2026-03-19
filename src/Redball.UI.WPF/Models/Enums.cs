namespace Redball.UI.Services;

/// <summary>
/// Heartbeat input mode for keep-awake keypress simulation.
/// </summary>
public enum HeartbeatInputMode
{
    Disabled = 0,
    F13 = 1,
    F14 = 2,
    F15 = 3,
    F16 = 4
}

/// <summary>
/// Notification filtering mode for tray balloon notifications.
/// </summary>
public enum NotificationMode
{
    All,
    Important,
    Errors,
    Silent
}
