using System;
using Microsoft.Win32;

namespace Redball.UI.Services;

/// <summary>
/// Detects Windows session lock/unlock events and optionally pauses keep-awake when locked.
/// </summary>
public class SessionLockService
{
    private static readonly Lazy<SessionLockService> _instance = new(() => new SessionLockService());
    public static SessionLockService Instance => _instance.Value;

    private bool _enabled;
    private bool _wasActiveBeforeLock;

    public bool IsLocked { get; private set; }
    public bool IsEnabled => _enabled;

    public event EventHandler<bool>? SessionLockChanged;

    private SessionLockService()
    {
        Logger.Verbose("SessionLockService", "Instance created");
    }

    public void Start()
    {
        if (_enabled) return;
        _enabled = true;
        SystemEvents.SessionSwitch += SystemEvents_SessionSwitch;
        Logger.Info("SessionLockService", "Session lock detection started");
    }

    public void Stop()
    {
        if (!_enabled) return;
        _enabled = false;
        SystemEvents.SessionSwitch -= SystemEvents_SessionSwitch;
        Logger.Info("SessionLockService", "Session lock detection stopped");
    }

    private void SystemEvents_SessionSwitch(object sender, SessionSwitchEventArgs e)
    {
        switch (e.Reason)
        {
            case SessionSwitchReason.SessionLock:
                IsLocked = true;
                Logger.Info("SessionLockService", "Session locked");

                var config = ConfigService.Instance.Config;
                if (config.PauseOnScreenLock)
                {
                    _wasActiveBeforeLock = KeepAwakeService.Instance.IsActive;
                    if (_wasActiveBeforeLock)
                    {
                        KeepAwakeService.Instance.SetActive(false);
                        Logger.Info("SessionLockService", "Keep-awake paused due to screen lock");
                    }
                }

                SessionLockChanged?.Invoke(this, true);
                break;

            case SessionSwitchReason.SessionUnlock:
                IsLocked = false;
                Logger.Info("SessionLockService", "Session unlocked");

                if (_wasActiveBeforeLock && ConfigService.Instance.Config.PauseOnScreenLock)
                {
                    KeepAwakeService.Instance.SetActive(true);
                    Logger.Info("SessionLockService", "Keep-awake resumed after screen unlock");
                    _wasActiveBeforeLock = false;
                }

                SessionLockChanged?.Invoke(this, false);
                break;
        }
    }
}
