using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Redball.UI.Services;

/// <summary>
/// Types of performance tests.
/// </summary>
public enum PerformanceTestType
{
    Startup,
    Soak,
    MemoryLeak,
    CpuUtilization,
    Responsiveness
}

/// <summary>
/// Result of a performance test run.
/// </summary>
public class PerformanceTestResult
{
    public PerformanceTestType TestType { get; set; }
    public string TestName { get; set; } = "";
    public DateTime StartTime { get; set; }
    public TimeSpan Duration { get; set; }
    public bool Passed { get; set; }
    public List<string> Metrics { get; } = new();
    public List<string> Failures { get; } = new();
    public string? Details { get; set; }

    public void AddMetric(string name, double value, string unit)
    {
        Metrics.Add($"{name}: {value:F2}{unit}");
    }

    public void AddFailure(string message)
    {
        Failures.Add(message);
        Passed = false;
    }
}

/// <summary>
/// Configuration for continuous performance testing.
/// </summary>
public class PerformanceTestConfig
{
    public bool EnableStartupTests { get; set; } = true;
    public bool EnableSoakTests { get; set; } = true;
    public bool EnableLeakDetection { get; set; } = true;
    public TimeSpan StartupTestInterval { get; set; } = TimeSpan.FromMinutes(5);
    public TimeSpan SoakTestDuration { get; set; } = TimeSpan.FromHours(1);
    public TimeSpan LeakTestDuration { get; set; } = TimeSpan.FromMinutes(30);
    public double MaxMemoryGrowthMB { get; set; } = 50; // Max acceptable growth
    public double MaxCpuPercent { get; set; } = 10; // Max acceptable CPU
}

/// <summary>
/// Service for continuous performance testing.
/// Implements perf-6 from improve_me.txt: Continuous performance test suite (soak + startup + leak detection).
/// </summary>
public class PerformanceTestService
{
    private static readonly Lazy<PerformanceTestService> _instance = new(() => new PerformanceTestService());
    public static PerformanceTestService Instance => _instance.Value;

    private readonly PerformanceTestConfig _config = new();
    private readonly List<PerformanceTestResult> _results = new();
    private readonly object _lock = new();
    private Timer? _startupTestTimer;
    private CancellationTokenSource? _soakTestCts;
    private CancellationTokenSource? _leakTestCts;
    private bool _isRunning;

    // Baseline measurements
    private long _baselineMemoryMB;
    private double _baselineCpuPercent;

    public event EventHandler<PerformanceTestResult>? TestCompleted;
    public event EventHandler<string>? TestSuiteStatusChanged;

    private PerformanceTestService()
    {
        Logger.Info("PerformanceTestService", "Performance test service initialized");
    }

    /// <summary>
    /// Starts continuous performance testing.
    /// </summary>
    public void StartTesting(PerformanceTestConfig? config = null)
    {
        if (_isRunning)
        {
            Logger.Warning("PerformanceTestService", "Testing already running");
            return;
        }

        if (config != null)
        {
            _config.EnableStartupTests = config.EnableStartupTests;
            _config.EnableSoakTests = config.EnableSoakTests;
            _config.EnableLeakDetection = config.EnableLeakDetection;
        }

        _isRunning = true;
        CaptureBaseline();

        // Start startup tests
        if (_config.EnableStartupTests)
        {
            _startupTestTimer = new Timer(RunStartupTest, null, TimeSpan.Zero, _config.StartupTestInterval);
            Logger.Info("PerformanceTestService", "Startup testing started");
        }

        // Start soak test
        if (_config.EnableSoakTests)
        {
            _soakTestCts = new CancellationTokenSource();
            _ = RunSoakTestAsync(_soakTestCts.Token);
            Logger.Info("PerformanceTestService", "Soak testing started");
        }

        // Start leak detection
        if (_config.EnableLeakDetection)
        {
            _leakTestCts = new CancellationTokenSource();
            _ = RunLeakDetectionAsync(_leakTestCts.Token);
            Logger.Info("PerformanceTestService", "Leak detection started");
        }

        TestSuiteStatusChanged?.Invoke(this, "Performance testing active");
    }

    /// <summary>
    /// Stops all performance testing.
    /// </summary>
    public void StopTesting()
    {
        _isRunning = false;
        _startupTestTimer?.Dispose();
        _soakTestCts?.Cancel();
        _leakTestCts?.Cancel();

        Logger.Info("PerformanceTestService", "Performance testing stopped");
        TestSuiteStatusChanged?.Invoke(this, "Performance testing stopped");
    }

    /// <summary>
    /// Gets all test results.
    /// </summary>
    public IReadOnlyList<PerformanceTestResult> GetResults()
    {
        lock (_lock)
        {
            return _results.ToList();
        }
    }

    /// <summary>
    /// Gets results for a specific test type.
    /// </summary>
    public IReadOnlyList<PerformanceTestResult> GetResults(PerformanceTestType type)
    {
        lock (_lock)
        {
            return _results.Where(r => r.TestType == type).ToList();
        }
    }

    /// <summary>
    /// Gets the latest result for a test type.
    /// </summary>
    public PerformanceTestResult? GetLatestResult(PerformanceTestType type)
    {
        lock (_lock)
        {
            return _results.Where(r => r.TestType == type).OrderByDescending(r => r.StartTime).FirstOrDefault();
        }
    }

    /// <summary>
    /// Gets overall test suite health.
    /// </summary>
    public bool IsHealthy()
    {
        lock (_lock)
        {
            var recent = _results.Where(r => r.StartTime > DateTime.Now.AddHours(-1)).ToList();
            if (recent.Count == 0) return true;
            return recent.All(r => r.Passed);
        }
    }

    /// <summary>
    /// Exports results to JSON file.
    /// </summary>
    public bool ExportResults(string filePath)
    {
        try
        {
            var export = new
            {
                ExportedAt = DateTime.Now,
                IsHealthy = IsHealthy(),
                Baseline = new { MemoryMB = _baselineMemoryMB, CpuPercent = _baselineCpuPercent },
                Results = GetResults().Select(r => new
                {
                    r.TestType,
                    r.TestName,
                    r.StartTime,
                    r.Duration,
                    r.Passed,
                    r.Metrics,
                    r.Failures,
                    r.Details
                })
            };

            var json = JsonSerializer.Serialize(export, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(filePath, json);
            return true;
        }
        catch (Exception ex)
        {
            Logger.Error("PerformanceTestService", "Failed to export results", ex);
            return false;
        }
    }

    private void CaptureBaseline()
    {
        using var process = Process.GetCurrentProcess();
        _baselineMemoryMB = process.WorkingSet64 / (1024 * 1024);
        _baselineCpuPercent = MeasureCpuUsage();

        Logger.Info("PerformanceTestService",
            $"Baseline captured: {_baselineMemoryMB}MB memory, {_baselineCpuPercent:F1}% CPU");
    }

    private void RunStartupTest(object? state)
    {
        if (!_isRunning) return;

        var sw = Stopwatch.StartNew();
        var result = new PerformanceTestResult
        {
            TestType = PerformanceTestType.Startup,
            TestName = "Runtime Health Check",
            StartTime = DateTime.Now
        };

        try
        {
            // Check current memory vs baseline
            using var process = Process.GetCurrentProcess();
            var currentMemoryMB = process.WorkingSet64 / (1024 * 1024);
            var memoryGrowthMB = currentMemoryMB - _baselineMemoryMB;

            result.AddMetric("MemoryBaseline", _baselineMemoryMB, "MB");
            result.AddMetric("MemoryCurrent", currentMemoryMB, "MB");
            result.AddMetric("MemoryGrowth", memoryGrowthMB, "MB");

            if (memoryGrowthMB > _config.MaxMemoryGrowthMB)
            {
                result.AddFailure($"Memory growth {memoryGrowthMB:F0}MB exceeds threshold {_config.MaxMemoryGrowthMB:F0}MB");
            }

            // Check CPU usage
            var cpuPercent = MeasureCpuUsage();
            result.AddMetric("CpuUsage", cpuPercent, "%");

            if (cpuPercent > _config.MaxCpuPercent)
            {
                result.AddFailure($"CPU usage {cpuPercent:F1}% exceeds threshold {_config.MaxCpuPercent:F1}%");
            }

            // Check GC collections
            var gc0 = GC.CollectionCount(0);
            var gc1 = GC.CollectionCount(1);
            var gc2 = GC.CollectionCount(2);
            result.AddMetric("GCGen0", gc0, " collections");
            result.AddMetric("GCGen1", gc1, " collections");
            result.AddMetric("GCGen2", gc2, " collections");

            // Overall pass if no failures
            if (result.Failures.Count == 0)
            {
                result.Passed = true;
                result.Details = "Runtime health within acceptable bounds";
            }
        }
        catch (Exception ex)
        {
            result.AddFailure($"Test error: {ex.Message}");
            Logger.Error("PerformanceTestService", "Startup test failed", ex);
        }

        sw.Stop();
        result.Duration = sw.Elapsed;

        StoreResult(result);
        TestCompleted?.Invoke(this, result);
    }

    private async Task RunSoakTestAsync(CancellationToken ct)
    {
        var startTime = DateTime.Now;
        var result = new PerformanceTestResult
        {
            TestType = PerformanceTestType.Soak,
            TestName = $"Soak Test ({_config.SoakTestDuration.TotalHours}h)",
            StartTime = startTime
        };

        try
        {
            var measurements = new List<(DateTime time, long memoryMB, double cpuPercent)>();
            var interval = TimeSpan.FromMinutes(1);

            while (!ct.IsCancellationRequested && DateTime.Now - startTime < _config.SoakTestDuration)
            {
                using var process = Process.GetCurrentProcess();
                var memoryMB = process.WorkingSet64 / (1024 * 1024);
                var cpuPercent = MeasureCpuUsage();

                measurements.Add((DateTime.Now, memoryMB, cpuPercent));

                // Check for stability issues
                if (measurements.Count > 10)
                {
                    var recent = measurements.TakeLast(10);
                    var avgMemory = recent.Average(m => m.memoryMB);
                    var avgCpu = recent.Average(m => m.cpuPercent);

                    // Memory should be relatively stable
                    var memoryVariance = recent.Select(m => Math.Abs(m.memoryMB - avgMemory)).Average();
                    if (memoryVariance > 100) // More than 100MB variance
                    {
                        result.AddFailure($"High memory variance: {memoryVariance:F0}MB (unstable)");
                    }

                    // CPU should be reasonable for idle app
                    if (avgCpu > _config.MaxCpuPercent)
                    {
                        result.AddFailure($"Average CPU {avgCpu:F1}% exceeds threshold {_config.MaxCpuPercent:F1}%");
                    }
                }

                await Task.Delay(interval, ct);
            }

            if (result.Failures.Count == 0)
            {
                result.Passed = true;
                result.Details = $"Soak test completed: {measurements.Count} measurements over {(DateTime.Now - startTime).TotalHours:F1} hours";
            }
        }
        catch (OperationCanceledException)
        {
            result.Details = "Soak test cancelled";
        }
        catch (Exception ex)
        {
            result.AddFailure($"Soak test error: {ex.Message}");
            Logger.Error("PerformanceTestService", "Soak test failed", ex);
        }

        result.Duration = DateTime.Now - startTime;
        StoreResult(result);
        TestCompleted?.Invoke(this, result);
    }

    private async Task RunLeakDetectionAsync(CancellationToken ct)
    {
        var startTime = DateTime.Now;
        var result = new PerformanceTestResult
        {
            TestType = PerformanceTestType.MemoryLeak,
            TestName = $"Leak Detection ({_config.LeakTestDuration.TotalMinutes}m)",
            StartTime = startTime
        };

        try
        {
            var samples = new List<long>();
            var interval = TimeSpan.FromMinutes(2);

            while (!ct.IsCancellationRequested && DateTime.Now - startTime < _config.LeakTestDuration)
            {
                // Force GC before measuring
                GC.Collect(GC.MaxGeneration, GCCollectionMode.Aggressive, true, true);
                GC.WaitForPendingFinalizers();
                GC.Collect();

                using var process = Process.GetCurrentProcess();
                var memoryMB = process.WorkingSet64 / (1024 * 1024);
                samples.Add(memoryMB);

                // Check for leak pattern after enough samples
                if (samples.Count >= 5)
                {
                    var trend = CalculateTrend(samples);
                    result.AddMetric($"Sample{samples.Count}", memoryMB, "MB");

                    // Positive trend indicates potential leak
                    if (trend > 5) // More than 5MB per sample growth
                    {
                        result.AddFailure($"Potential memory leak detected: {trend:F1}MB growth per sample");
                    }
                }

                await Task.Delay(interval, ct);
            }

            if (result.Failures.Count == 0)
            {
                result.Passed = true;
                var totalGrowth = samples.Count > 1 ? samples.Last() - samples.First() : 0;
                result.Details = $"No leak detected: {samples.Count} samples, total growth {totalGrowth}MB";
            }
        }
        catch (OperationCanceledException)
        {
            result.Details = "Leak detection cancelled";
        }
        catch (Exception ex)
        {
            result.AddFailure($"Leak detection error: {ex.Message}");
            Logger.Error("PerformanceTestService", "Leak detection failed", ex);
        }

        result.Duration = DateTime.Now - startTime;
        StoreResult(result);
        TestCompleted?.Invoke(this, result);
    }

    private double MeasureCpuUsage()
    {
        try
        {
            using var process = Process.GetCurrentProcess();
            var startTime = DateTime.UtcNow;
            var startCpuUsage = process.TotalProcessorTime;

            Thread.Sleep(500);

            var endTime = DateTime.UtcNow;
            var endCpuUsage = process.TotalProcessorTime;

            var cpuUsedMs = (endCpuUsage - startCpuUsage).TotalMilliseconds;
            var totalMs = (endTime - startTime).TotalMilliseconds;

            return (cpuUsedMs / (Environment.ProcessorCount * totalMs)) * 100;
        }
        catch
        {
            return 0;
        }
    }

    private double CalculateTrend(List<long> values)
    {
        if (values.Count < 2) return 0;

        var n = values.Count;
        var sumX = Enumerable.Range(0, n).Sum(i => (double)i);
        var sumY = values.Sum();
        var sumXY = values.Select((y, i) => (double)i * y).Sum();
        var sumX2 = Enumerable.Range(0, n).Sum(i => (double)i * i);

        var slope = (n * sumXY - sumX * sumY) / (n * sumX2 - sumX * sumX);
        return slope;
    }

    private void StoreResult(PerformanceTestResult result)
    {
        lock (_lock)
        {
            _results.Add(result);

            // Keep only last 100 results per type
            var toRemove = _results
                .Where(r => r.TestType == result.TestType)
                .OrderByDescending(r => r.StartTime)
                .Skip(100)
                .ToList();

            foreach (var r in toRemove)
            {
                _results.Remove(r);
            }
        }

        var status = result.Passed ? "PASSED" : "FAILED";
        Logger.Info("PerformanceTestService",
            $"Test {status}: {result.TestName} ({result.Duration.TotalSeconds:F1}s)");
    }
}
