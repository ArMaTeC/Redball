using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Redball.UI.Services;

/// <summary>
/// Resource budget configuration for a service.
/// </summary>
public class ServiceResourceBudget
{
    /// <summary>
    /// Service identifier.
    /// </summary>
    public string ServiceName { get; set; } = "";

    /// <summary>
    /// Maximum CPU percentage allowed (0-100).
    /// </summary>
    public double MaxCpuPercent { get; set; }

    /// <summary>
    /// Maximum RAM in MB.
    /// </summary>
    public long MaxRamMB { get; set; }

    /// <summary>
    /// Whether this service is critical (cannot be throttled).
    /// </summary>
    public bool IsCritical { get; set; }

    /// <summary>
    /// Action to take when budget is exceeded.
    /// </summary>
    public BudgetAction ActionOnExceed { get; set; } = BudgetAction.Log;
}

/// <summary>
/// Actions to take when a resource budget is exceeded.
/// </summary>
public enum BudgetAction
{
    /// <summary>Log a warning only.</summary>
    Log,
    /// <summary>Log and notify the user.</summary>
    Notify,
    /// <summary>Throttle the service if possible.</summary>
    Throttle,
    /// <summary>Restart the service as last resort.</summary>
    Restart
}

/// <summary>
/// Resource usage snapshot for a service.
/// </summary>
public class ServiceResourceUsage
{
    public string ServiceName { get; set; } = "";
    public double CpuPercent { get; set; }
    public long RamBytes { get; set; }
    public long RamMB => RamBytes / (1024 * 1024);
    public DateTime Timestamp { get; set; }
    public bool IsWithinBudget { get; set; }
    public List<string> Violations { get; } = new();
}

/// <summary>
/// Budget conformance report for all services.
/// </summary>
public class BudgetConformanceReport
{
    public DateTime GeneratedAt { get; set; }
    public TimeSpan CheckDuration { get; set; }
    public List<ServiceResourceUsage> ServiceUsages { get; } = new();
    public int ServicesWithinBudget { get; set; }
    public int ServicesOverBudget { get; set; }
    public List<string> CriticalViolations { get; } = new();

    public bool IsHealthy => ServicesOverBudget == 0 && CriticalViolations.Count == 0;
}

/// <summary>
/// Service for monitoring and enforcing per-service CPU/RAM budgets.
/// Implements perf-2 from improve_me.txt: Per-service CPU/RAM budgets and periodic conformance checks.
/// </summary>
public class ResourceBudgetService
{
    private static readonly Lazy<ResourceBudgetService> _instance = new(() => new ResourceBudgetService());
    public static ResourceBudgetService Instance => _instance.Value;

    private readonly Dictionary<string, ServiceResourceBudget> _budgets = new();
    private readonly Dictionary<string, List<ServiceResourceUsage>> _history = new();
    private readonly object _lock = new();
    private Timer? _monitoringTimer;
    private bool _isMonitoring;
    private readonly TimeSpan _historyRetention = TimeSpan.FromHours(24);

    // Thread-based tracking: service -> list of registered thread IDs
    private readonly Dictionary<string, HashSet<int>> _serviceThreads = new();
    // Thread CPU snapshots: thread ID -> (timestamp, processorTime)
    private readonly Dictionary<int, (DateTime timestamp, TimeSpan processorTime)> _threadSnapshots = new();

    // Default budgets for known services
    // Note: RAM budgets reflect shared process memory - all services share the same process space
    private static readonly Dictionary<string, (double cpu, long ram, bool critical)> DefaultBudgets = new()
    {
        { "KeepAwakeService", (10.0, 400, true) },           // Core functionality - shared process memory
        { "AnalyticsService", (5.0, 400, false) },           // Background telemetry - shared process memory
        { "UpdateService", (20.0, 400, false) },             // Occasional heavy operations - shared process memory
        { "ConfigService", (5.0, 400, true) },               // Config management - shared process memory
        { "NotificationService", (3.0, 400, false) },        // Toast notifications - shared process memory
        { "TrayIconService", (5.0, 400, true) },             // Always running - shared process memory
        { "CommandPaletteIndex", (10.0, 400, false) },       // On-demand - shared process memory
        { "MainWindow", (25.0, 400, false) }                 // UI, variable - shared process memory
    };

    private ResourceBudgetService()
    {
        InitializeDefaultBudgets();
        Logger.Info("ResourceBudgetService", "Resource budget service initialized");
    }

    /// <summary>
    /// Sets a budget for a service.
    /// </summary>
    public void SetBudget(string serviceName, double maxCpuPercent, long maxRamMB, bool isCritical = false, BudgetAction action = BudgetAction.Log)
    {
        lock (_lock)
        {
            _budgets[serviceName] = new ServiceResourceBudget
            {
                ServiceName = serviceName,
                MaxCpuPercent = maxCpuPercent,
                MaxRamMB = maxRamMB,
                IsCritical = isCritical,
                ActionOnExceed = action
            };

            if (!_history.ContainsKey(serviceName))
            {
                _history[serviceName] = new List<ServiceResourceUsage>();
            }

            Logger.Info("ResourceBudgetService", $"Set budget for {serviceName}: {maxCpuPercent}% CPU, {maxRamMB}MB RAM");
        }
    }

    /// <summary>
    /// Gets the budget for a service.
    /// </summary>
    public ServiceResourceBudget? GetBudget(string serviceName)
    {
        lock (_lock)
        {
            return _budgets.TryGetValue(serviceName, out var budget) ? budget : null;
        }
    }

    /// <summary>
    /// Starts periodic monitoring of resource budgets.
    /// </summary>
    public void StartMonitoring(TimeSpan interval)
    {
        if (_isMonitoring)
        {
            Logger.Warning("ResourceBudgetService", "Monitoring already started");
            return;
        }

        _isMonitoring = true;
        _monitoringTimer = new Timer(MonitoringCallback, null, TimeSpan.Zero, interval);

        Logger.Info("ResourceBudgetService", $"Started monitoring with interval: {interval.TotalSeconds}s");
    }

    /// <summary>
    /// Stops resource monitoring.
    /// </summary>
    public void StopMonitoring()
    {
        _isMonitoring = false;
        _monitoringTimer?.Dispose();
        _monitoringTimer = null;

        Logger.Info("ResourceBudgetService", "Stopped monitoring");
    }

    /// <summary>
    /// Registers a thread as belonging to a specific service for CPU tracking.
    /// </summary>
    public void RegisterServiceThread(string serviceName, int threadId)
    {
        lock (_lock)
        {
            if (!_serviceThreads.ContainsKey(serviceName))
                _serviceThreads[serviceName] = new HashSet<int>();

            _serviceThreads[serviceName].Add(threadId);

            // Initialize snapshot for this thread
            if (!_threadSnapshots.ContainsKey(threadId))
            {
                _threadSnapshots[threadId] = (DateTime.Now, TimeSpan.Zero);
            }

            Logger.Debug("ResourceBudgetService", $"Registered thread {threadId} for {serviceName}");
        }
    }

    /// <summary>
    /// Unregisters a thread from a service (e.g., when thread terminates).
    /// </summary>
    public void UnregisterServiceThread(string serviceName, int threadId)
    {
        lock (_lock)
        {
            if (_serviceThreads.TryGetValue(serviceName, out var threads))
            {
                threads.Remove(threadId);
            }
            _threadSnapshots.Remove(threadId);

            Logger.Debug("ResourceBudgetService", $"Unregistered thread {threadId} from {serviceName}");
        }
    }

    /// <summary>
    /// Registers the current thread for a service.
    /// </summary>
    public void RegisterCurrentThread(string serviceName)
    {
        RegisterServiceThread(serviceName, Thread.CurrentThread.ManagedThreadId);
    }

    /// <summary>
    /// Gets current resource usage for a service.
    /// </summary>
    public ServiceResourceUsage? GetCurrentUsage(string serviceName)
    {
        lock (_lock)
        {
            if (!_history.TryGetValue(serviceName, out var history) || history.Count == 0)
                return null;

            return history.Last();
        }
    }

    /// <summary>
    /// Gets resource usage history for a service.
    /// </summary>
    public IReadOnlyList<ServiceResourceUsage> GetHistory(string serviceName, TimeSpan? duration = null)
    {
        var cutoff = DateTime.Now - (duration ?? TimeSpan.FromHours(1));

        lock (_lock)
        {
            if (!_history.TryGetValue(serviceName, out var history))
                return Array.Empty<ServiceResourceUsage>();

            return history.Where(h => h.Timestamp > cutoff).ToList();
        }
    }

    /// <summary>
    /// Generates a budget conformance report.
    /// </summary>
    public BudgetConformanceReport GenerateReport()
    {
        var stopwatch = Stopwatch.StartNew();
        var report = new BudgetConformanceReport { GeneratedAt = DateTime.Now };

        lock (_lock)
        {
            foreach (var (serviceName, budget) in _budgets)
            {
                var usage = MeasureServiceUsage(serviceName);
                if (usage == null) continue;

                // Check against budget
                usage.IsWithinBudget = true;

                if (usage.CpuPercent > budget.MaxCpuPercent)
                {
                    usage.IsWithinBudget = false;
                    usage.Violations.Add($"CPU: {usage.CpuPercent:F1}% > {budget.MaxCpuPercent:F1}%");
                }

                if (usage.RamMB > budget.MaxRamMB)
                {
                    usage.IsWithinBudget = false;
                    usage.Violations.Add($"RAM: {usage.RamMB}MB > {budget.MaxRamMB}MB");
                }

                report.ServiceUsages.Add(usage);

                if (usage.IsWithinBudget)
                    report.ServicesWithinBudget++;
                else
                    report.ServicesOverBudget++;

                // Track critical violations
                if (!usage.IsWithinBudget && budget.IsCritical)
                {
                    report.CriticalViolations.Add($"{serviceName}: {string.Join(", ", usage.Violations)}");
                }

                // Store in history
                if (!_history.ContainsKey(serviceName))
                    _history[serviceName] = new List<ServiceResourceUsage>();

                _history[serviceName].Add(usage);

                // Cleanup old history
                var cutoff = DateTime.Now - _historyRetention;
                _history[serviceName].RemoveAll(h => h.Timestamp < cutoff);
            }
        }

        stopwatch.Stop();
        report.CheckDuration = stopwatch.Elapsed;

        return report;
    }

    /// <summary>
    /// Checks if all services are within their budgets.
    /// </summary>
    public bool IsConformant()
    {
        var report = GenerateReport();
        return report.IsHealthy;
    }

    /// <summary>
    /// Gets a summary of resource usage for all services.
    /// </summary>
    public Dictionary<string, (double avgCpu, long avgRam, bool withinBudget)> GetSummary()
    {
        lock (_lock)
        {
            var summary = new Dictionary<string, (double avgCpu, long avgRam, bool withinBudget)>();

            foreach (var (serviceName, history) in _history)
            {
                if (history.Count == 0) continue;

                var recent = history.TakeLast(10).ToList();
                var avgCpu = recent.Average(h => h.CpuPercent);
                var avgRam = (long)recent.Average(h => h.RamMB);
                var withinBudget = recent.All(h => h.IsWithinBudget);

                summary[serviceName] = (avgCpu, avgRam, withinBudget);
            }

            return summary;
        }
    }

    /// <summary>
    /// Records an action taken when budget was exceeded.
    /// </summary>
    public void RecordBudgetAction(string serviceName, BudgetAction action, string reason)
    {
        Logger.Warning("ResourceBudgetService", $"Budget action for {serviceName}: {action} - {reason}");

        // Could trigger notifications, throttling, or other responses
        switch (action)
        {
            case BudgetAction.Notify:
                NotificationService.Instance.ShowWarning("Resource Budget",
                    $"{serviceName} exceeded resource budget: {reason}");
                break;

            case BudgetAction.Throttle:
                // Signal to service to reduce activity
                ThrottleService(serviceName);
                break;

            case BudgetAction.Restart:
                // Log for manual intervention - automatic restart is risky
                Logger.Error("ResourceBudgetService",
                    $"CRITICAL: {serviceName} requires restart due to resource exhaustion");
                break;
        }
    }

    private void InitializeDefaultBudgets()
    {
        foreach (var (serviceName, (cpu, ram, critical)) in DefaultBudgets)
        {
            SetBudget(serviceName, cpu, ram, critical);
        }
    }

    private void MonitoringCallback(object? state)
    {
        if (!_isMonitoring) return;

        try
        {
            // Clean up dead thread registrations periodically
            CleanupDeadThreads();

            var report = GenerateReport();

            // Log summary
            if (report.ServicesOverBudget > 0)
            {
                Logger.Warning("ResourceBudgetService",
                    $"Budget check: {report.ServicesOverBudget}/{report.ServiceUsages.Count} services over budget");

                foreach (var usage in report.ServiceUsages.Where(u => !u.IsWithinBudget))
                {
                    var budget = GetBudget(usage.ServiceName);
                    if (budget != null && budget.ActionOnExceed != BudgetAction.Log)
                    {
                        RecordBudgetAction(usage.ServiceName, budget.ActionOnExceed,
                            string.Join(", ", usage.Violations));
                    }
                }
            }

            // Log critical violations
            foreach (var violation in report.CriticalViolations)
            {
                Logger.Error("ResourceBudgetService", $"CRITICAL resource violation: {violation}");
            }
        }
        catch (Exception ex)
        {
            Logger.Error("ResourceBudgetService", "Error during monitoring callback", ex);
        }
    }

    /// <summary>
    /// Removes thread registrations for threads that no longer exist.
    /// </summary>
    private void CleanupDeadThreads()
    {
        try
        {
            using var process = Process.GetCurrentProcess();
            var aliveThreadIds = process.Threads.Cast<ProcessThread>().Select(t => t.Id).ToHashSet();

            lock (_lock)
            {
                var deadThreadIds = _threadSnapshots.Keys.Where(id => !aliveThreadIds.Contains(id)).ToList();

                foreach (var threadId in deadThreadIds)
                {
                    _threadSnapshots.Remove(threadId);

                    // Also remove from service registrations
                    foreach (var threads in _serviceThreads.Values)
                    {
                        threads.Remove(threadId);
                    }
                }

                if (deadThreadIds.Count > 0)
                {
                    Logger.Debug("ResourceBudgetService", $"Cleaned up {deadThreadIds.Count} dead thread registrations");
                }
            }
        }
        catch (Exception ex)
        {
            Logger.Debug("ResourceBudgetService", $"Error cleaning up dead threads: {ex.Message}");
        }
    }

    private ServiceResourceUsage? MeasureServiceUsage(string serviceName)
    {
        try
        {
            using var process = Process.GetCurrentProcess();

            double cpuPercent;
            HashSet<int>? serviceThreadIds = null;

            lock (_lock)
            {
                _serviceThreads.TryGetValue(serviceName, out serviceThreadIds);
            }

            if (serviceThreadIds != null && serviceThreadIds.Count > 0)
            {
                // Thread-based measurement: aggregate CPU across all service threads
                cpuPercent = MeasureThreadBasedCpu(serviceThreadIds);
            }
            else
            {
                // Fallback: measure process-wide but scale down by number of active services
                // This prevents false positives when no threads are explicitly registered
                cpuPercent = MeasureProcessCpu() / Math.Max(1, _budgets.Count);
            }

            // Get memory usage (process-wide - shared memory space)
            process.Refresh();
            var ramBytes = process.WorkingSet64;

            return new ServiceResourceUsage
            {
                ServiceName = serviceName,
                CpuPercent = cpuPercent,
                RamBytes = ramBytes,
                Timestamp = DateTime.Now
            };
        }
        catch (Exception ex)
        {
            Logger.Error("ResourceBudgetService", $"Failed to measure usage for {serviceName}", ex);
            return null;
        }
    }

    /// <summary>
    /// Measures CPU usage for a set of threads by aggregating their individual CPU times.
    /// </summary>
    private double MeasureThreadBasedCpu(HashSet<int> threadIds)
    {
        var now = DateTime.Now;
        double totalCpuPercent = 0;
        int validThreads = 0;

        // Get all process threads once
        using var process = Process.GetCurrentProcess();
        var processThreads = process.Threads.Cast<ProcessThread>().ToDictionary(t => t.Id);

        lock (_lock)
        {
            foreach (var threadId in threadIds)
            {
                if (!processThreads.TryGetValue(threadId, out var thread))
                    continue; // Thread no longer exists

                try
                {
                    var currentProcessorTime = thread.TotalProcessorTime;

                    if (_threadSnapshots.TryGetValue(threadId, out var snapshot))
                    {
                        var elapsedTime = (now - snapshot.timestamp).TotalMilliseconds;
                        var cpuUsed = (currentProcessorTime - snapshot.processorTime).TotalMilliseconds;

                        if (elapsedTime > 0)
                        {
                            var threadCpuPercent = (cpuUsed / (Environment.ProcessorCount * elapsedTime)) * 100;
                            totalCpuPercent += threadCpuPercent;
                            validThreads++;
                        }
                    }

                    // Update snapshot for next measurement
                    _threadSnapshots[threadId] = (now, currentProcessorTime);
                }
                catch (InvalidOperationException)
                {
                    // Thread may have exited between check and measurement
                    _threadSnapshots.Remove(threadId);
                }
            }
        }

        return validThreads > 0 ? totalCpuPercent : 0;
    }

    /// <summary>
    /// Measures total process CPU usage (fallback when no threads registered).
    /// </summary>
    private double MeasureProcessCpu()
    {
        using var process = Process.GetCurrentProcess();

        var startTime = DateTime.Now;
        var startCpuUsage = process.TotalProcessorTime;

        Thread.Sleep(50); // Sample over 50ms (shorter since we're called multiple times)

        var endTime = DateTime.Now;
        var endCpuUsage = process.TotalProcessorTime;

        var cpuUsedMs = (endCpuUsage - startCpuUsage).TotalMilliseconds;
        var totalMs = (endTime - startTime).TotalMilliseconds;

        return totalMs > 0 ? (cpuUsedMs / (Environment.ProcessorCount * totalMs)) * 100 : 0;
    }

    private void ThrottleService(string serviceName)
    {
        // This would integrate with specific services to throttle their operations
        // For example, reducing polling frequency, batching operations, etc.
        Logger.Info("ResourceBudgetService", $"Throttling requested for {serviceName}");

        // Could emit an event that services subscribe to
        ServiceThrottled?.Invoke(this, new ServiceThrottledEventArgs { ServiceName = serviceName });
    }

    /// <summary>
    /// Event raised when a service is throttled due to budget exceedance.
    /// </summary>
    public event EventHandler<ServiceThrottledEventArgs>? ServiceThrottled;
}

/// <summary>
/// Event args for service throttling.
/// </summary>
public class ServiceThrottledEventArgs : EventArgs
{
    public string ServiceName { get; set; } = "";
    public DateTime ThrottledAt { get; set; } = DateTime.Now;
}
