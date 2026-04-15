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
/// Input method for TypeThing keystroke simulation.
/// </summary>
public enum TypeThingInputMode
{
    /// <summary>Standard Win32 SendInput with scan codes (default, works locally).</summary>
    SendInput = 0
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
