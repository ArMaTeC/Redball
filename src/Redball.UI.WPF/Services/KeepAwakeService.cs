using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;
using Redball.UI.Interop;

namespace Redball.UI.Services;

/// <summary>
/// Core keep-awake engine. Uses SetThreadExecutionState to prevent Windows sleep
/// and optionally sends F15 heartbeat keypresses to prevent idle detection.
/// Replaces the PowerShell keep-awake logic entirely.
/// </summary>
public class KeepAwakeService : IKeepAwakeService
{
    private static readonly Lazy<KeepAwakeService> _instance = new(() => new KeepAwakeService());
    public static KeepAwakeService Instance => _instance.Value;

    private Timer? _heartbeatTimer;
    private Timer? _durationTimer;
    private bool _disposed;
    private int _monitorTickCount;
    private Stopwatch? _sessionStopwatch;
    private TimeSpan _timedDuration;

    // State
    private bool _isActive;
    private bool _preventDisplaySleep = true;
    private bool _useHeartbeat = true;
    private HeartbeatInputMode _heartbeatInputMode = HeartbeatInputMode.F15;
    private DateTime? _until;
    private DateTime? _startTime;

    // Auto-pause tracking
    private bool _autoPausedBattery;
    private bool _autoPausedNetwork;
    private bool _autoPausedIdle;
    private bool _autoPausedSchedule;
    private bool _activeBeforeAutoPause;

    // Monitoring services
    private readonly BatteryMonitorService _batteryMonitor = new();
    private readonly NetworkMonitorService _networkMonitor = new();
    private readonly IdleDetectionService _idleDetection = new();
    private readonly ScheduleService _schedule = new();
    private readonly PresentationModeService _presentationMode = new();

    public event EventHandler<bool>? ActiveStateChanged;
    public event EventHandler? TimedAwakeExpired;
    public event EventHandler? HeartbeatTick;

    private KeepAwakeService()
    {
        Logger.Verbose("KeepAwakeService", "Instance created");
    }

    // --- Properties ---

    public bool IsActive
    {
        get => _isActive;
        private set
        {
            if (_isActive != value)
            {
                _isActive = value;
                Logger.Info("KeepAwakeService", $"IsActive changed to: {value}");
                ActiveStateChanged?.Invoke(this, value);
            }
        }
    }

    public bool PreventDisplaySleep
    {
        get => _preventDisplaySleep;
        set
        {
            if (_preventDisplaySleep != value)
            {
                _preventDisplaySleep = value;
                Logger.Info("KeepAwakeService", $"PreventDisplaySleep changed to: {value}");
                if (IsActive)
                {
                    ApplyExecutionState(true);
                }
            }
        }
    }

    public bool UseHeartbeat
    {
        get => _useHeartbeat;
        set
        {
            _useHeartbeat = value;
            Logger.Info("KeepAwakeService", $"UseHeartbeat changed to: {value}");
        }
    }

    public HeartbeatInputMode HeartbeatInputMode
    {
        get => _heartbeatInputMode;
        set
        {
            _heartbeatInputMode = value;
            _useHeartbeat = value != HeartbeatInputMode.Disabled;
            Logger.Info("KeepAwakeService", $"HeartbeatInputMode changed to: {value}");
        }
    }

    public DateTime? Until => _until;
    public DateTime? StartTime => _startTime;

    public bool AutoPausedBattery => _autoPausedBattery;
    public bool AutoPausedNetwork => _autoPausedNetwork;
    public bool AutoPausedIdle => _autoPausedIdle;
    public bool AutoPausedSchedule => _autoPausedSchedule;

    private int _heartbeatIntervalMs = 59000;

    // --- Public Methods ---

    /// <summary>
    /// Initializes timers and starts the keep-awake engine.
    /// Call once after config is loaded.
    /// </summary>
    public void Initialize()
    {
        Logger.Info("KeepAwakeService", "Initializing...");

        var config = ConfigService.Instance.Config;
        _preventDisplaySleep = config.PreventDisplaySleep;
        _useHeartbeat = config.UseHeartbeatKeypress;
        _heartbeatInputMode = ParseHeartbeatInputMode(config.HeartbeatInputMode);

        // Configure monitoring services from config
        _batteryMonitor.IsEnabled = config.BatteryAware;
        _batteryMonitor.Threshold = config.BatteryThreshold;
        _networkMonitor.IsEnabled = config.NetworkAware;
        _idleDetection.IsEnabled = config.IdleDetection;
        _idleDetection.ThresholdMinutes = config.IdleThreshold;
        _schedule.IsEnabled = config.ScheduleEnabled;
        _schedule.StartTime = config.ScheduleStartTime;
        _schedule.StopTime = config.ScheduleStopTime;
        _schedule.Days = config.ScheduleDays;
        _presentationMode.IsEnabled = config.PresentationModeDetection;

        // Heartbeat timer - calls SetThreadExecutionState + optional F15
        var heartbeatInterval = Math.Max(10, config.HeartbeatSeconds);
        _heartbeatTimer = new Timer(OnHeartbeatTick, null, Timeout.Infinite, Timeout.Infinite);
        _heartbeatIntervalMs = heartbeatInterval * 1000;

        // Duration timer - 1-second tick for timed expiry and monitoring checks
        _durationTimer = new Timer(OnDurationTick, null, Timeout.Infinite, Timeout.Infinite);

        Logger.Info("KeepAwakeService", $"Timers created (heartbeat: {heartbeatInterval}s)");
        Logger.Info("KeepAwakeService", $"Monitors: Battery={_batteryMonitor.IsEnabled}, Network={_networkMonitor.IsEnabled}, Idle={_idleDetection.IsEnabled}, Schedule={_schedule.IsEnabled}, Presentation={_presentationMode.IsEnabled}");
    }

    /// <summary>
    /// Reloads monitor settings from current config. Call after settings change.
    /// </summary>
    public void ReloadConfig()
    {
        var config = ConfigService.Instance.Config;
        _preventDisplaySleep = config.PreventDisplaySleep;
        _useHeartbeat = config.UseHeartbeatKeypress;
        _heartbeatInputMode = ParseHeartbeatInputMode(config.HeartbeatInputMode);
        _batteryMonitor.IsEnabled = config.BatteryAware;
        _batteryMonitor.Threshold = config.BatteryThreshold;
        _networkMonitor.IsEnabled = config.NetworkAware;
        _idleDetection.IsEnabled = config.IdleDetection;
        _idleDetection.ThresholdMinutes = config.IdleThreshold;
        _schedule.IsEnabled = config.ScheduleEnabled;
        _schedule.StartTime = config.ScheduleStartTime;
        _schedule.StopTime = config.ScheduleStopTime;
        _schedule.Days = config.ScheduleDays;
        _presentationMode.IsEnabled = config.PresentationModeDetection;

        if (IsActive) ApplyExecutionState(true);
        Logger.Info("KeepAwakeService", "Config reloaded");
    }

    /// <summary>
    /// Activates or deactivates keep-awake.
    /// </summary>
    public void SetActive(bool active, DateTime? until = null)
    {
        Logger.Info("KeepAwakeService", $"SetActive called: active={active}, until={until}");

        _until = active ? until : null;
        IsActive = active;

        ApplyExecutionState(active);

        if (active)
        {
            _startTime ??= DateTime.Now;
            // Start monotonic stopwatch for timed sessions (immune to clock changes)
            if (until.HasValue)
            {
                _timedDuration = until.Value - DateTime.Now;
                _sessionStopwatch = Stopwatch.StartNew();
            }
            _heartbeatTimer?.Change(_heartbeatIntervalMs, _heartbeatIntervalMs);
            _durationTimer?.Change(1000, 1000);
            Logger.Info("KeepAwakeService", "Timers started");
        }
        else
        {
            _heartbeatTimer?.Change(Timeout.Infinite, Timeout.Infinite);
            _sessionStopwatch?.Stop();
            _sessionStopwatch = null;
            // Keep duration timer running for monitoring even when paused
            Logger.Info("KeepAwakeService", "Heartbeat timer stopped");
        }
    }

    /// <summary>
    /// Toggles keep-awake on/off.
    /// </summary>
    public void Toggle()
    {
        SetActive(!IsActive);
    }

    /// <summary>
    /// Starts a timed keep-awake session.
    /// </summary>
    public void StartTimed(int minutes)
    {
        if (minutes < 1 || minutes > 720)
        {
            Logger.Warning("KeepAwakeService", $"Invalid timed duration: {minutes}");
            return;
        }
        Logger.Debug("KeepAwakeService", $"Starting timed session: {minutes} min (monotonic Stopwatch)");
        SetActive(true, DateTime.Now.AddMinutes(minutes));
    }

    /// <summary>
    /// Auto-pause due to external condition (battery, network, idle, schedule).
    /// Remembers prior state for auto-resume.
    /// </summary>
    public void AutoPause(string reason)
    {
        if (!IsActive) return;

        Logger.Info("KeepAwakeService", $"Auto-pausing: {reason}");
        _activeBeforeAutoPause = true;

        switch (reason)
        {
            case "Battery": _autoPausedBattery = true; break;
            case "Network": _autoPausedNetwork = true; break;
            case "Idle": _autoPausedIdle = true; break;
            case "Schedule": _autoPausedSchedule = true; break;
        }

        SetActive(false);
    }

    /// <summary>
    /// Auto-resume from a specific auto-pause condition.
    /// </summary>
    public void AutoResume(string reason)
    {
        Logger.Info("KeepAwakeService", $"Auto-resuming: {reason}");

        switch (reason)
        {
            case "Battery": _autoPausedBattery = false; break;
            case "Network": _autoPausedNetwork = false; break;
            case "Idle": _autoPausedIdle = false; break;
            case "Schedule": _autoPausedSchedule = false; break;
        }

        if (_activeBeforeAutoPause && !_autoPausedBattery && !_autoPausedNetwork &&
            !_autoPausedIdle && !_autoPausedSchedule)
        {
            _activeBeforeAutoPause = false;
            SetActive(true);
        }
    }

    /// <summary>
    /// Gets a human-readable status string.
    /// </summary>
    public string GetStatusText()
    {
        if (IsActive)
        {
            var display = PreventDisplaySleep ? "Display On" : "Display Normal";
            var heartbeat = UseHeartbeat && _heartbeatInputMode != HeartbeatInputMode.Disabled
                ? $"{_heartbeatInputMode} On"
                : "Heartbeat Off";
            var status = $"Active | {display} | {heartbeat}";

            if (_until.HasValue && _sessionStopwatch != null)
            {
                var remaining = _timedDuration - _sessionStopwatch.Elapsed;
                var minsLeft = Math.Max(0, (int)remaining.TotalMinutes);
                status += $" | {minsLeft} min left";
            }
            else if (_until.HasValue)
            {
                var minsLeft = Math.Max(0, (int)(_until.Value - DateTime.Now).TotalMinutes);
                status += $" | {minsLeft} min left";
            }

            return status;
        }
        else
        {
            return "Paused | Display Normal | Heartbeat Off";
        }
    }

    /// <summary>
    /// Starts the duration timer for monitoring even when paused.
    /// Call after Initialize.
    /// </summary>
    public void StartMonitoring()
    {
        _durationTimer?.Change(1000, 1000);
        Logger.Info("KeepAwakeService", "Duration timer started for monitoring");
    }

    // --- Private Methods ---

    private void ApplyExecutionState(bool enable)
    {
        try
        {
            uint flags;
            if (enable)
            {
                flags = NativeMethods.ES_CONTINUOUS | NativeMethods.ES_SYSTEM_REQUIRED;
                if (_preventDisplaySleep)
                {
                    flags |= NativeMethods.ES_DISPLAY_REQUIRED;
                }
            }
            else
            {
                flags = NativeMethods.ES_CONTINUOUS; // Release all flags
            }

            NativeMethods.SetThreadExecutionState(flags);
            Logger.Verbose("KeepAwakeService", $"SetThreadExecutionState(0x{flags:X8})");
        }
        catch (Exception ex)
        {
            Logger.Error("KeepAwakeService", "Failed to set execution state", ex);
        }
    }

    private static HeartbeatInputMode ParseHeartbeatInputMode(string? inputMode)
    {
        if (Enum.TryParse<HeartbeatInputMode>(inputMode, true, out var mode))
        {
            return mode;
        }

        return HeartbeatInputMode.F15;
    }

    private ushort GetHeartbeatVirtualKey()
    {
        return _heartbeatInputMode switch
        {
            HeartbeatInputMode.F13 => NativeMethods.VK_F13,
            HeartbeatInputMode.F14 => NativeMethods.VK_F14,
            HeartbeatInputMode.F16 => NativeMethods.VK_F16,
            _ => NativeMethods.VK_F15
        };
    }

    private void SendHeartbeat()
    {
        try
        {
            if (!_useHeartbeat || _heartbeatInputMode == HeartbeatInputMode.Disabled)
            {
                return;
            }

            var virtualKey = GetHeartbeatVirtualKey();
            var inputs = new NativeMethods.INPUT[2];

            inputs[0].type = NativeMethods.INPUT_KEYBOARD;
            inputs[0].u.ki.wVk = virtualKey;
            inputs[0].u.ki.dwFlags = 0;

            inputs[1].type = NativeMethods.INPUT_KEYBOARD;
            inputs[1].u.ki.wVk = virtualKey;
            inputs[1].u.ki.dwFlags = NativeMethods.KEYEVENTF_KEYUP;

            NativeMethods.SendInput(2, inputs, Marshal.SizeOf<NativeMethods.INPUT>());
            Logger.Verbose("KeepAwakeService", $"{_heartbeatInputMode} heartbeat sent");
        }
        catch (Exception ex)
        {
            Logger.Debug("KeepAwakeService", $"Heartbeat failed: {ex.Message}");
        }
    }

    private void OnHeartbeatTick(object? state)
    {
        if (!IsActive) return;

        try
        {
            // Re-assert execution state on each heartbeat (P/Invoke, thread-safe)
            ApplyExecutionState(true);

            // SendInput must be called from a thread with a message pump,
            // so marshal heartbeat keypress to the UI thread.
            if (_useHeartbeat)
            {
                Application.Current?.Dispatcher.BeginInvoke(SendHeartbeat);
            }

            HeartbeatTick?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            Logger.Debug("KeepAwakeService", $"Heartbeat tick error: {ex.Message}");
        }
    }

    private void OnDurationTick(object? state)
    {
        try
        {
            // Check timed expiry using monotonic Stopwatch (immune to DST/NTP/clock changes)
            if (IsActive && _until.HasValue && _sessionStopwatch != null &&
                _sessionStopwatch.Elapsed >= _timedDuration)
            {
                Logger.Info("KeepAwakeService", "Timed awake expired");
                _until = null;
                // SetActive raises events that may touch UI, marshal to dispatcher
                Application.Current?.Dispatcher.BeginInvoke(() =>
                {
                    SetActive(false);
                    TimedAwakeExpired?.Invoke(this, EventArgs.Empty);
                });
                return;
            }

            _monitorTickCount++;

            // Run idle detection every second (cheap P/Invoke call)
            _idleDetection.CheckAndUpdate(this);

            // Run expensive monitors every 10 seconds
            if (_monitorTickCount % 10 == 0)
            {
                _batteryMonitor.CheckAndUpdate(this);
                _networkMonitor.CheckAndUpdate(this);
                _presentationMode.CheckAndUpdate(this);
            }

            // Run schedule check every 30 seconds
            if (_monitorTickCount % 30 == 0)
            {
                _schedule.CheckAndUpdate(this);
            }
        }
        catch (Exception ex)
        {
            Logger.Debug("KeepAwakeService", $"Duration tick error: {ex.Message}");
        }
    }

    // --- IDisposable ---

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        Logger.Info("KeepAwakeService", "Disposing...");

        _heartbeatTimer?.Change(Timeout.Infinite, Timeout.Infinite);
        _durationTimer?.Change(Timeout.Infinite, Timeout.Infinite);
        _heartbeatTimer?.Dispose();
        _durationTimer?.Dispose();

        // Release execution state
        ApplyExecutionState(false);

        Logger.Info("KeepAwakeService", "Disposed");
    }
}
