using System;

namespace Redball.UI.Services;

/// <summary>
/// Interface for the core keep-awake engine.
/// </summary>
public interface IKeepAwakeService : IDisposable
{
    bool IsActive { get; }
    bool PreventDisplaySleep { get; set; }
    bool UseHeartbeat { get; set; }
    HeartbeatInputMode HeartbeatInputMode { get; set; }
    DateTime? Until { get; }
    DateTime? StartTime { get; }
    bool AutoPausedBattery { get; }
    bool AutoPausedNetwork { get; }
    bool AutoPausedIdle { get; }
    bool AutoPausedSchedule { get; }

    event EventHandler<bool>? ActiveStateChanged;
    event EventHandler? TimedAwakeExpired;
    event EventHandler? HeartbeatTick;

    void Initialize();
    void ReloadConfig();
    void SetActive(bool active, DateTime? until = null);
    void Toggle();
    void StartTimed(int minutes);
    void AutoPause(string reason);
    void AutoResume(string reason);
    string GetStatusText();
    void StartMonitoring();
}
