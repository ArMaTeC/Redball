namespace Redball.UI.Services;

/// <summary>
/// Driver selection for HID input mode.
/// </summary>
public enum DriverSelection
{
    /// <summary>No driver selected.</summary>
    None = 0,
    /// <summary>Standard Interception driver.</summary>
    Interception = 1,
    /// <summary>Redball KMDF filter driver (requires test signing).</summary>
    RedballKMDF = 2,
    /// <summary>Auto-select based on system state (Recommended).</summary>
    Auto = 3
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
    /// <summary>Driver-level HID keyboard emulation via Interception driver (works over RDP/remote).</summary>
    HID = 1,
    /// <summary>Windows Service-based input injection (works over RDP, no driver signing required).</summary>
    Service = 2
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
