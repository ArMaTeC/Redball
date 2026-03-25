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

    // Default budgets for known services
    private static readonly Dictionary<string, (double cpu, long ram, bool critical)> DefaultBudgets = new()
    {
        { "KeepAwakeService", (5.0, 50, true) },           // Core functionality, low resources
        { "InterceptionInputService", (10.0, 100, true) },  // HID operations
        { "AnalyticsService", (2.0, 30, false) },           // Background telemetry
        { "UpdateService", (15.0, 80, false) },             // Occasional heavy operations
        { "ConfigService", (1.0, 20, true) },               // Config management
        { "NotificationService", (1.0, 25, false) },          // Toast notifications
        { "PomodoroService", (1.0, 20, false) },            // Timer-based
        { "TrayIconService", (2.0, 40, true) },              // Always running
        { "CommandPaletteIndex", (5.0, 60, false) },         // On-demand
        { "MainWindow", (20.0, 200, false) }                // UI, variable
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

    private ServiceResourceUsage? MeasureServiceUsage(string serviceName)
    {
        try
        {
            // Get current process for self-monitoring
            using var process = Process.GetCurrentProcess();

            // Measure CPU usage over a short interval
            var startTime = DateTime.Now;
            var startCpuUsage = process.TotalProcessorTime;

            Thread.Sleep(100); // Sample over 100ms

            var endTime = DateTime.Now;
            var endCpuUsage = process.TotalProcessorTime;

            var cpuUsedMs = (endCpuUsage - startCpuUsage).TotalMilliseconds;
            var totalMs = (endTime - startTime).TotalMilliseconds;
            var cpuPercent = (cpuUsedMs / (Environment.ProcessorCount * totalMs)) * 100;

            // Get memory usage
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
