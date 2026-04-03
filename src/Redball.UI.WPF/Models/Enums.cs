namespace Redball.UI.Services;

/// <summary>
/// Driver selection for input mode.
/// </summary>
public enum DriverSelection
{
    /// <summary>No driver/service selected.</summary>
    None = 0,
    /// <summary>Windows Service-based input (no driver signing required).</summary>
    Service = 1
}

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
    SendInput = 0,
    /// <summary>Windows Service-based input injection (works over RDP, no driver signing required).</summary>
    Service = 1
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
