namespace Redball.Core.Performance;

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

/// <summary>
/// Tracks startup timing spans for SLO monitoring and regression detection.
/// Implements improve_me.txt item E: Performance SLO instrumentation.
/// </summary>
public sealed class StartupTimingService
{
    private static readonly Lazy<StartupTimingService> _instance = new(() => new StartupTimingService());
    public static StartupTimingService Instance => _instance.Value;

    private readonly Stopwatch _stopwatch;
    private readonly Dictionary<string, (long timestamp, TimeSpan elapsed)> _spans = new();
    private readonly List<StartupSnapshot> _history = new();

    // SLO Targets
    public const double ColdStartSloSeconds = 1.5;
    public const double WarmStartSloSeconds = 0.8;
    public const int MaxHistoryEntries = 50;

    private StartupTimingService()
    {
        _stopwatch = Stopwatch.StartNew();
    }

    /// <summary>
    /// Records a timing span checkpoint.
    /// </summary>
    public void RecordSpan(string spanName)
    {
        var elapsed = _stopwatch.Elapsed;
        _spans[spanName] = (Stopwatch.GetTimestamp(), elapsed);
        Logger.Debug("StartupTiming", $"Span '{spanName}': {elapsed.TotalMilliseconds:F0}ms");
    }

    /// <summary>
    /// Completes startup timing and saves snapshot to history.
    /// </summary>
    public void CompleteStartup(bool isColdStart)
    {
        var totalTime = _stopwatch.Elapsed;
        var sloTarget = isColdStart ? TimeSpan.FromSeconds(ColdStartSloSeconds) : TimeSpan.FromSeconds(WarmStartSloSeconds);
        var passed = totalTime <= sloTarget;

        var snapshot = new StartupSnapshot
        {
            Timestamp = DateTime.UtcNow,
            IsColdStart = isColdStart,
            TotalTime = totalTime,
            SloTarget = sloTarget,
            SloPassed = passed,
            Spans = _spans.ToDictionary(kvp => kvp.Key, kvp => kvp.Value.elapsed)
        };

        _history.Add(snapshot);

        // Trim history if needed
        if (_history.Count > MaxHistoryEntries)
        {
            _history.RemoveAt(0);
        }

        var status = passed ? "PASSED" : "FAILED";
        Logger.Info("StartupTiming", 
            $"Startup {status}: {totalTime.TotalSeconds:F2}s (SLO: {sloTarget.TotalSeconds:F1}s, Cold: {isColdStart})");
    }

    /// <summary>
    /// Gets the current spans for live monitoring.
    /// </summary>
    public IReadOnlyDictionary<string, TimeSpan> GetCurrentSpans()
    {
        return _spans.ToDictionary(kvp => kvp.Key, kvp => kvp.Value.elapsed);
    }

    /// <summary>
    /// Gets historical startup snapshots.
    /// </summary>
    public IReadOnlyList<StartupSnapshot> GetHistory()
    {
        return _history.ToList();
    }

    /// <summary>
    /// Calculates SLO compliance statistics.
    /// </summary>
    public SloStatistics GetSloStatistics(TimeSpan window)
    {
        var cutoff = DateTime.UtcNow - window;
        var relevant = _history.Where(h => h.Timestamp >= cutoff).ToList();

        if (!relevant.Any())
        {
            return SloStatistics.Empty;
        }

        var coldStarts = relevant.Where(h => h.IsColdStart).ToList();
        var warmStarts = relevant.Where(h => !h.IsColdStart).ToList();

        return new SloStatistics
        {
            TotalStarts = relevant.Count,
            ColdStarts = coldStarts.Count,
            WarmStarts = warmStarts.Count,
            ColdStartPassRate = coldStarts.Any() ? (double)coldStarts.Count(h => h.SloPassed) / coldStarts.Count : 0,
            WarmStartPassRate = warmStarts.Any() ? (double)warmStarts.Count(h => h.SloPassed) / warmStarts.Count : 0,
            AvgColdStartSeconds = coldStarts.Any() ? coldStarts.Average(h => h.TotalTime.TotalSeconds) : 0,
            AvgWarmStartSeconds = warmStarts.Any() ? warmStarts.Average(h => h.TotalTime.TotalSeconds) : 0,
            P95ColdStartSeconds = coldStarts.Any() ? Percentile(coldStarts.Select(h => h.TotalTime.TotalSeconds), 0.95) : 0,
            P95WarmStartSeconds = warmStarts.Any() ? Percentile(warmStarts.Select(h => h.TotalTime.TotalSeconds), 0.95) : 0
        };
    }

    /// <summary>
    /// Detects if startup time is regressing compared to baseline.
    /// </summary>
    public bool IsRegression(TimeSpan window, double thresholdPercent = 20)
    {
        var stats = GetSloStatistics(window);
        if (stats.TotalStarts < 5) return false;

        var baseline = stats.IsColdStartRecent 
            ? stats.AvgColdStartSeconds 
            : stats.AvgWarmStartSeconds;

        var current = _spans.ContainsKey("tray_ready") 
            ? _spans["tray_ready"].elapsed.TotalSeconds 
            : 0;

        if (baseline > 0 && current > 0)
        {
            var increase = (current - baseline) / baseline * 100;
            return increase > thresholdPercent;
        }

        return false;
    }

    private static double Percentile(IEnumerable<double> values, double percentile)
    {
        var sorted = values.OrderBy(v => v).ToList();
        var index = (int)Math.Ceiling(sorted.Count * percentile) - 1;
        return sorted[Math.Max(0, Math.Min(index, sorted.Count - 1))];
    }
}

/// <summary>
/// A single startup timing snapshot.
/// </summary>
public sealed class StartupSnapshot
{
    public DateTime Timestamp { get; init; }
    public bool IsColdStart { get; init; }
    public TimeSpan TotalTime { get; init; }
    public TimeSpan SloTarget { get; init; }
    public bool SloPassed { get; init; }
    public Dictionary<string, TimeSpan> Spans { get; init; } = new();
}

/// <summary>
/// SLO compliance statistics.
/// </summary>
public sealed class SloStatistics
{
    public int TotalStarts { get; init; }
    public int ColdStarts { get; init; }
    public int WarmStarts { get; init; }
    public double ColdStartPassRate { get; init; }
    public double WarmStartPassRate { get; init; }
    public double AvgColdStartSeconds { get; init; }
    public double AvgWarmStartSeconds { get; init; }
    public double P95ColdStartSeconds { get; init; }
    public double P95WarmStartSeconds { get; init; }
    public bool IsColdStartRecent { get; init; }

    public bool IsHealthy => ColdStartPassRate >= 0.95 && WarmStartPassRate >= 0.99;

    public static SloStatistics Empty => new()
    {
        TotalStarts = 0,
        ColdStartPassRate = 1.0,
        WarmStartPassRate = 1.0
    };
}
