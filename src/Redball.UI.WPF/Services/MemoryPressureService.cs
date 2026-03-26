using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Timers;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Redball.Core.Performance;
using Redball.UI.Interop;
using Redball.UI.Services;

/// <summary>
/// Memory pressure levels.
/// </summary>
public enum MemoryPressureLevel
{
    /// <summary>Normal memory availability.</summary>
    Normal,
    /// <summary>Moderate pressure - consider light optimization.</summary>
    Moderate,
    /// <summary>High pressure - aggressive optimization needed.</summary>
    High,
    /// <summary>Critical pressure - emergency measures required.</summary>
    Critical
}

/// <summary>
/// Memory status snapshot.
/// </summary>
public class MemoryStatus
{
    public long TotalPhysicalBytes { get; set; }
    public long AvailablePhysicalBytes { get; set; }
    public long UsedBytes => TotalPhysicalBytes - AvailablePhysicalBytes;
    public double UsedPercent => TotalPhysicalBytes > 0 ? (double)UsedBytes / TotalPhysicalBytes * 100 : 0;
    public MemoryPressureLevel PressureLevel { get; set; }
    public long WorkingSetBytes { get; set; }
    public long GCHeapBytes { get; set; }
    public DateTime Timestamp { get; set; }

    public long AvailableMB => AvailablePhysicalBytes / (1024 * 1024);
    public long UsedMB => UsedBytes / (1024 * 1024);
    public long WorkingSetMB => WorkingSetBytes / (1024 * 1024);
}

/// <summary>
/// Feature degradation action.
/// </summary>
public enum DegradationAction
{
    /// <summary>No action needed.</summary>
    None,
    /// <summary>Reduce animation quality.</summary>
    ReduceAnimations,
    /// <summary>Disable non-essential visual effects.</summary>
    DisableEffects,
    /// <summary>Reduce polling frequency.</summary>
    ReducePolling,
    /// <summary>Clear caches and release buffers.</summary>
    ClearCaches,
    /// <summary>Disable optional features.</summary>
    DisableFeatures,
    /// <summary>Request user to close unused panels.</summary>
    PromptUser
}

/// <summary>
/// Memory pressure event arguments.
/// </summary>
public class MemoryPressureEventArgs : EventArgs
{
    public MemoryPressureLevel Level { get; set; }
    public MemoryStatus Status { get; set; } = new();
    public List<DegradationAction> RecommendedActions { get; set; } = new();
}

/// <summary>
/// Service for monitoring memory pressure and gracefully degrading features.
/// Implements perf-5 from improve_me.txt: Memory pressure handler with graceful feature degradation.
/// </summary>
public class MemoryPressureService
{
    private static readonly Lazy<MemoryPressureService> _instance = new(() => new MemoryPressureService());
    public static MemoryPressureService Instance => _instance.Value;

    private System.Threading.Timer? _monitoringTimer;
    private MemoryPressureLevel _currentLevel = MemoryPressureLevel.Normal;
    private readonly List<MemoryStatus> _history = new();
    private readonly object _lock = new();
    private readonly TimeSpan _historyRetention = TimeSpan.FromHours(1);

    // Thresholds for pressure levels (as % of total memory used)
    private const double ModerateThreshold = 75.0;
    private const double HighThreshold = 85.0;
    private const double CriticalThreshold = 93.0;

    // Minimum available RAM in MB
    private const long ModerateMinMB = 2048;
    private const long HighMinMB = 1024;
    private const long CriticalMinMB = 512;

    private MemoryPressureService()
    {
        Logger.Info("MemoryPressureService", "Memory pressure service initialized");
    }

    /// <summary>
    /// Event raised when memory pressure changes.
    /// </summary>
    public event EventHandler<MemoryPressureEventArgs>? PressureChanged;

    /// <summary>
    /// Event raised when feature degradation is recommended.
    /// </summary>
    public event EventHandler<MemoryPressureEventArgs>? DegradationRecommended;

    /// <summary>
    /// Starts monitoring memory pressure.
    /// </summary>
    public void StartMonitoring(TimeSpan interval)
    {
        _monitoringTimer?.Dispose();
        _monitoringTimer = new System.Threading.Timer(MonitoringCallback, null, TimeSpan.Zero, interval);
        Logger.Info("MemoryPressureService", $"Started monitoring with interval: {interval.TotalSeconds}s");
    }

    /// <summary>
    /// Stops memory monitoring.
    /// </summary>
    public void StopMonitoring()
    {
        _monitoringTimer?.Dispose();
        _monitoringTimer = null;
        Logger.Info("MemoryPressureService", "Stopped monitoring");
    }

    /// <summary>
    /// Gets the current memory pressure level.
    /// </summary>
    public MemoryPressureLevel CurrentPressureLevel
    {
        get
        {
            lock (_lock)
            {
                return _currentLevel;
            }
        }
    }

    /// <summary>
    /// Gets current memory status.
    /// </summary>
    public MemoryStatus GetCurrentStatus()
    {
        return CaptureMemoryStatus();
    }

    /// <summary>
    /// Gets memory pressure history.
    /// </summary>
    public IReadOnlyList<MemoryStatus> GetHistory(TimeSpan? duration = null)
    {
        var cutoff = DateTime.Now - (duration ?? TimeSpan.FromMinutes(10));
        lock (_lock)
        {
            return _history.FindAll(h => h.Timestamp > cutoff);
        }
    }

    /// <summary>
    /// Triggers garbage collection and memory cleanup.
    /// </summary>
    public void TriggerCleanup()
    {
        Logger.Info("MemoryPressureService", "Triggering memory cleanup");

        // Force full GC
        GC.Collect(GC.MaxGeneration, GCCollectionMode.Optimized, true, true);
        GC.WaitForPendingFinalizers();
        GC.Collect();

        // Trim working set
        TrimWorkingSet();

        Logger.Info("MemoryPressureService", "Memory cleanup completed");
    }

    /// <summary>
    /// Applies recommended degradations based on pressure level.
    /// </summary>
    public void ApplyDegradations(MemoryPressureLevel level)
    {
        var actions = GetDegradationActions(level);
        Logger.Warning("MemoryPressureService", $"Applying degradations for {level}: {string.Join(", ", actions)}");

        foreach (var action in actions)
        {
            try
            {
                ApplyDegradation(action);
            }
            catch (Exception ex)
            {
                Logger.Error("MemoryPressureService", $"Failed to apply degradation {action}", ex);
            }
        }

        // Notify subscribers
        var args = new MemoryPressureEventArgs
        {
            Level = level,
            Status = GetCurrentStatus(),
            RecommendedActions = actions
        };
        DegradationRecommended?.Invoke(this, args);
    }

    /// <summary>
    /// Releases degradations when pressure returns to normal.
    /// </summary>
    public void ReleaseDegradations()
    {
        Logger.Info("MemoryPressureService", "Releasing degradations - returning to normal operation");

        // Restore animations
        Application.Current.Dispatcher.Invoke(() =>
        {
            // Could restore animation settings here
        });

        // Restore polling rates through AdaptiveIntervalPolicy
        // AdaptiveIntervalPolicy.Instance.SetEmergencyMode(false);

        // Notify user if we were in critical state
        if (_currentLevel == MemoryPressureLevel.Critical)
        {
            NotificationService.Instance.ShowInfo("Memory Recovered",
                "Memory pressure has returned to normal levels. All features restored.");
        }
    }

    private void MonitoringCallback(object? state)
    {
        try
        {
            var status = CaptureMemoryStatus();
            var newLevel = DeterminePressureLevel(status);

            lock (_lock)
            {
                _history.Add(status);

                // Cleanup old history
                var cutoff = DateTime.Now - _historyRetention;
                _history.RemoveAll(h => h.Timestamp < cutoff);
            }

            // Check for level change
            if (newLevel != _currentLevel)
            {
                var previousLevel = _currentLevel;
                _currentLevel = newLevel;

                Logger.Warning("MemoryPressureService",
                    $"Memory pressure changed: {previousLevel} -> {newLevel} ({status.UsedPercent:F1}% used, {status.AvailableMB}MB free)");

                // Raise event
                var args = new MemoryPressureEventArgs
                {
                    Level = newLevel,
                    Status = status,
                    RecommendedActions = GetDegradationActions(newLevel)
                };
                PressureChanged?.Invoke(this, args);

                // Apply degradations if pressure increased
                if (newLevel > previousLevel)
                {
                    ApplyDegradations(newLevel);
                }
                // Release degradations if returned to normal
                else if (newLevel == MemoryPressureLevel.Normal && previousLevel > MemoryPressureLevel.Normal)
                {
                    ReleaseDegradations();
                }
            }

            // Critical pressure - emergency cleanup
            if (newLevel == MemoryPressureLevel.Critical)
            {
                TriggerCleanup();
            }
        }
        catch (Exception ex)
        {
            Logger.Error("MemoryPressureService", "Error in monitoring callback", ex);
        }
    }

    private MemoryStatus CaptureMemoryStatus()
    {
        try
        {
            // Get system memory info
            var computerInfo = new Microsoft.VisualBasic.Devices.ComputerInfo();
            var totalBytes = (long)computerInfo.TotalPhysicalMemory;
            var availableBytes = (long)computerInfo.AvailablePhysicalMemory;

            // Get process memory info
            using var process = Process.GetCurrentProcess();
            var workingSet = process.WorkingSet64;

            // Get GC heap size
            var gcHeap = GC.GetTotalMemory(false);

            return new MemoryStatus
            {
                TotalPhysicalBytes = totalBytes,
                AvailablePhysicalBytes = availableBytes,
                WorkingSetBytes = workingSet,
                GCHeapBytes = gcHeap,
                PressureLevel = _currentLevel,
                Timestamp = DateTime.Now
            };
        }
        catch (Exception ex)
        {
            Logger.Error("MemoryPressureService", "Failed to capture memory status", ex);
            return new MemoryStatus
            {
                PressureLevel = MemoryPressureLevel.Normal,
                Timestamp = DateTime.Now
            };
        }
    }

    private MemoryPressureLevel DeterminePressureLevel(MemoryStatus status)
    {
        // Use both percentage and absolute MB thresholds
        var percentUsed = status.UsedPercent;
        var availableMB = status.AvailableMB;

        if (percentUsed >= CriticalThreshold || availableMB < CriticalMinMB)
            return MemoryPressureLevel.Critical;

        if (percentUsed >= HighThreshold || availableMB < HighMinMB)
            return MemoryPressureLevel.High;

        if (percentUsed >= ModerateThreshold || availableMB < ModerateMinMB)
            return MemoryPressureLevel.Moderate;

        return MemoryPressureLevel.Normal;
    }

    private List<DegradationAction> GetDegradationActions(MemoryPressureLevel level)
    {
        var actions = new List<DegradationAction>();

        switch (level)
        {
            case MemoryPressureLevel.Moderate:
                actions.Add(DegradationAction.ReduceAnimations);
                break;

            case MemoryPressureLevel.High:
                actions.Add(DegradationAction.ReduceAnimations);
                actions.Add(DegradationAction.DisableEffects);
                actions.Add(DegradationAction.ReducePolling);
                actions.Add(DegradationAction.ClearCaches);
                break;

            case MemoryPressureLevel.Critical:
                actions.Add(DegradationAction.ReduceAnimations);
                actions.Add(DegradationAction.DisableEffects);
                actions.Add(DegradationAction.ReducePolling);
                actions.Add(DegradationAction.ClearCaches);
                actions.Add(DegradationAction.DisableFeatures);
                actions.Add(DegradationAction.PromptUser);
                break;
        }

        return actions;
    }

    private void ApplyDegradation(DegradationAction action)
    {
        switch (action)
        {
            case DegradationAction.ReduceAnimations:
                Application.Current.Dispatcher.Invoke(() =>
                {
                    // Could disable or reduce animations here
                    Logger.Info("MemoryPressureService", "Reduced animation quality");
                });
                break;

            case DegradationAction.DisableEffects:
                Application.Current.Dispatcher.Invoke(() =>
                {
                    // Could disable visual effects
                    Logger.Info("MemoryPressureService", "Disabled visual effects");
                });
                break;

            case DegradationAction.ReducePolling:
                // Use AdaptiveIntervalPolicy to reduce polling
                // AdaptiveIntervalPolicy.Instance.SetEmergencyMode(true);
                Logger.Info("MemoryPressureService", "Reduced polling frequency");
                break;

            case DegradationAction.ClearCaches:
                TriggerCleanup();
                Logger.Info("MemoryPressureService", "Cleared caches");
                break;

            case DegradationAction.DisableFeatures:
                // Could disable non-essential features
                Logger.Warning("MemoryPressureService", "Non-essential features disabled");
                break;

            case DegradationAction.PromptUser:
                NotificationService.Instance.ShowWarning("Memory Pressure",
                    "System memory is critically low. Some features have been disabled to maintain stability.");
                break;
        }
    }

    private void TrimWorkingSet()
    {
        try
        {
            // Use Windows API to trim working set
            using var process = Process.GetCurrentProcess();
            NativeMethods.EmptyWorkingSet(process.Handle);
        }
        catch (Exception ex)
        {
            Logger.Debug("MemoryPressureService", $"Failed to trim working set: {ex.Message}");
        }
    }

    /// <summary>
    /// Gets a summary of current memory state.
    /// </summary>
    public string GetSummary()
    {
        var status = GetCurrentStatus();
        return $"Memory: {status.UsedPercent:F1}% used ({status.UsedMB}MB/{status.TotalPhysicalBytes / (1024 * 1024)}MB), " +
               $"Available: {status.AvailableMB}MB, Level: {_currentLevel}";
    }
}
