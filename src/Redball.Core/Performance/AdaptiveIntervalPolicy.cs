namespace Redball.Core.Performance;

using System;
using System.Diagnostics;
using System.Runtime.InteropServices;

/// <summary>
/// Adaptive monitoring interval policy that adjusts frequency based on system state.
/// Implements improve_me.txt item E: adaptive scheduling.
/// </summary>
public sealed class AdaptiveIntervalPolicy
{
    // Base intervals
    private readonly TimeSpan _defaultInterval;
    private readonly TimeSpan _batterySaverInterval;
    private readonly TimeSpan _highCpuInterval;
    private readonly TimeSpan _userIdleInterval;

    private DateTime _lastCpuCheck = DateTime.MinValue;
    private float _lastCpuLoad;

    // P/Invoke for power status
    [DllImport("kernel32.dll")]
    private static extern bool GetSystemPowerStatus(out SYSTEM_POWER_STATUS lpSystemPowerStatus);

    [StructLayout(LayoutKind.Sequential)]
    private struct SYSTEM_POWER_STATUS
    {
        public byte ACLineStatus;
        public byte BatteryFlag;
        public byte BatteryLifePercent;
        public byte Reserved1;
        public int BatteryLifeTime;
        public int BatteryFullLifeTime;
    }

    private const byte AC_LINE_OFFLINE = 0;
    private const byte AC_LINE_ONLINE = 1;

    public AdaptiveIntervalPolicy(
        TimeSpan? defaultInterval = null,
        TimeSpan? batterySaverInterval = null,
        TimeSpan? highCpuInterval = null,
        TimeSpan? userIdleInterval = null)
    {
        _defaultInterval = defaultInterval ?? TimeSpan.FromSeconds(5);
        _batterySaverInterval = batterySaverInterval ?? TimeSpan.FromSeconds(20);
        _highCpuInterval = highCpuInterval ?? TimeSpan.FromSeconds(15);
        _userIdleInterval = userIdleInterval ?? TimeSpan.FromSeconds(12);
    }

    /// <summary>
    /// Calculates the optimal monitor interval based on current system state.
    /// </summary>
    public TimeSpan GetMonitorInterval(
        bool onBatterySaver,
        double cpuLoad,
        bool userIdle,
        bool onBatteryPower = false)
    {
        // Battery saver mode: maximum interval
        if (onBatterySaver)
        {
            return _batterySaverInterval;
        }

        // On battery power (not saver): increase interval
        if (onBatteryPower)
        {
            return TimeSpan.FromSeconds(10);
        }

        // High CPU load: increase interval to reduce system load
        if (cpuLoad > 0.80)
        {
            return _highCpuInterval;
        }

        // User idle: can afford longer intervals
        if (userIdle)
        {
            return _userIdleInterval;
        }

        return _defaultInterval;
    }

    /// <summary>
    /// Gets current CPU load (0.0 - 1.0).
    /// Caches result for 2 seconds to reduce overhead.
    /// </summary>
    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    public double GetCurrentCpuLoad()
    {
        if (DateTime.UtcNow - _lastCpuCheck < TimeSpan.FromSeconds(2))
        {
            return _lastCpuLoad;
        }

        try
        {
            // Use PerformanceCounter for accurate system-wide CPU usage
            // First call returns 0, subsequent calls return actual percentage
            using var cpuCounter = new PerformanceCounter("Processor", "% Processor Time", "_Total");
            cpuCounter.NextValue(); // Initialize
            System.Threading.Thread.Sleep(100); // Short wait for accurate reading
            var cpuPercent = cpuCounter.NextValue();
            _lastCpuLoad = Math.Clamp(cpuPercent / 100f, 0f, 1f);
        }
        catch (Exception ex)
        {
            // Fallback to process-specific CPU usage if PerformanceCounter fails
            try
            {
                using var proc = Process.GetCurrentProcess();
                proc.Refresh();
                // Calculate process CPU percentage based on processor time
                var currentTime = DateTime.UtcNow;
                var procTime = proc.TotalProcessorTime;
                
                if (_lastCpuCheck != DateTime.MinValue)
                {
                    var timeDelta = (currentTime - _lastCpuCheck).TotalSeconds;
                    var procTimeDelta = (procTime.TotalMilliseconds / 1000.0) / Environment.ProcessorCount;
                    var cpuPercent = (procTimeDelta / timeDelta) * 100;
                    _lastCpuLoad = Math.Clamp((float)(cpuPercent / 100.0), 0f, 1f);
                }
                else
                {
                    _lastCpuLoad = 0.05f; // Low default on first call
                }
            }
            catch
            {
                _lastCpuLoad = 0;
            }
            
            Logger.Debug("AdaptiveIntervalPolicy", $"PerformanceCounter failed, using fallback: {ex.Message}");
        }

        _lastCpuCheck = DateTime.UtcNow;
        return _lastCpuLoad;
    }

    /// <summary>
    /// Determines if the system is on battery power.
    /// </summary>
    public static bool IsOnBatteryPower()
    {
        try
        {
            if (GetSystemPowerStatus(out var status))
            {
                return status.ACLineStatus == AC_LINE_OFFLINE;
            }
            return false;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Determines if battery saver mode is active.
    /// </summary>
    public static bool IsBatterySaverActive()
    {
        try
        {
            if (!OperatingSystem.IsWindows())
            {
                return false;
            }

            // Check Windows 10/11 battery saver status via registry
            using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(
                @"SYSTEM\CurrentControlSet\Control\Power\PowerThrottling");
            var value = key?.GetValue("PowerThrottlingOff");
            return value is int i && i == 0;
        }
        catch
        {
            return false;
        }
    }
}

/// <summary>
/// Shared scheduler that coalesces monitor ticks to reduce timer churn.
/// Implements improve_me.txt: shared scheduler for performance.
/// </summary>
public sealed class SharedScheduler : IDisposable
{
    private readonly System.Threading.Timer _timer;
    private readonly List<ScheduledTask> _tasks = new();
    private readonly object _lock = new();

    public SharedScheduler(TimeSpan tickInterval)
    {
        _timer = new System.Threading.Timer(OnTick, null, tickInterval, tickInterval);
    }

    /// <summary>
    /// Registers a task to be executed on each tick.
    /// </summary>
    public void RegisterTask(string name, Action action, TimeSpan minInterval)
    {
        lock (_lock)
        {
            _tasks.Add(new ScheduledTask
            {
                Name = name,
                Action = action,
                MinInterval = minInterval,
                LastExecuted = DateTime.UtcNow // Start from now to respect minInterval on first execution
            });
        }
    }

    /// <summary>
    /// Unregisters a task.
    /// </summary>
    public void UnregisterTask(string name)
    {
        lock (_lock)
        {
            _tasks.RemoveAll(t => t.Name == name);
        }
    }

    private void OnTick(object? state)
    {
        var now = DateTime.UtcNow;

        lock (_lock)
        {
            foreach (var task in _tasks)
            {
                if (now - task.LastExecuted >= task.MinInterval)
                {
                    try
                    {
                        task.Action();
                        task.LastExecuted = now;
                    }
                    catch (Exception ex)
                    {
                        Logger.Error("SharedScheduler", $"Task '{task.Name}' failed", ex);
                    }
                }
            }
        }
    }

    public void Dispose()
    {
        _timer?.Dispose();
    }

    private class ScheduledTask
    {
        public string Name { get; init; } = string.Empty;
        public Action Action { get; init; } = () => { };
        public TimeSpan MinInterval { get; init; }
        public DateTime LastExecuted { get; set; }
    }
}
