using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using Redball.UI.Services;

namespace Redball.UI.WPF.Services;

/// <summary>
/// Step in a task funnel.
/// </summary>
public class FunnelStep
{
    /// <summary>
    /// Step identifier.
    /// </summary>
    public string Id { get; set; } = "";

    /// <summary>
    /// Human-readable step name.
    /// </summary>
    public string Name { get; set; } = "";

    /// <summary>
    /// Step description.
    /// </summary>
    public string Description { get; set; } = "";

    /// <summary>
    /// Expected duration for this step.
    /// </summary>
    public TimeSpan TargetDuration { get; set; }

    /// <summary>
    /// UI element or action associated with this step.
    /// </summary>
    public string? UIElementId { get; set; }

    /// <summary>
    /// Step order in the funnel.
    /// </summary>
    public int Order { get; set; }
}

/// <summary>
/// Task funnel definition for a key job-to-be-done.
/// Implements ux-1 from improve_me.txt: End-to-end task funnels for top 5 jobs.
/// </summary>
public class TaskFunnel
{
    /// <summary>
    /// Funnel identifier.
    /// </summary>
    public string Id { get; set; } = "";

    /// <summary>
    /// Funnel name (the job-to-be-done).
    /// </summary>
    public string Name { get; set; } = "";

    /// <summary>
    /// Associated persona ID.
    /// </summary>
    public string PersonaId { get; set; } = "";

    /// <summary>
    /// Total target time for completing the funnel.
    /// </summary>
    public TimeSpan TotalTargetTime { get; set; }

    /// <summary>
    /// Steps in the funnel.
    /// </summary>
    public List<FunnelStep> Steps { get; set; } = new();

    /// <summary>
    /// Success criteria.
    /// </summary>
    public string SuccessCriteria { get; set; } = "";
}

/// <summary>
/// Tracked session of a user in a funnel.
/// </summary>
public class FunnelSession
{
    public string SessionId { get; set; } = Guid.NewGuid().ToString("N")[..8];
    public string FunnelId { get; set; } = "";
    public string UserId { get; set; } = "";
    public DateTime StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public List<FunnelStepCompletion> StepCompletions { get; set; } = new();
    public bool IsCompleted { get; set; }
    public TimeSpan? TotalDuration => CompletedAt.HasValue
        ? CompletedAt.Value - StartedAt
        : null;
    public string? DropOffStepId { get; set; }
    public string? DropOffReason { get; set; }
}

/// <summary>
/// Completion record for a funnel step.
/// </summary>
public class FunnelStepCompletion
{
    public string StepId { get; set; } = "";
    public DateTime StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public TimeSpan? Duration => CompletedAt.HasValue
        ? CompletedAt.Value - StartedAt
        : null;
    public bool IsCompleted { get; set; }
}

/// <summary>
/// Funnel analytics and drop-off analysis.
/// </summary>
public class FunnelAnalytics
{
    public string FunnelId { get; set; } = "";
    public int TotalSessions { get; set; }
    public int CompletedSessions { get; set; }
    public double CompletionRate => TotalSessions > 0
        ? (double)CompletedSessions / TotalSessions * 100
        : 0;
    public TimeSpan AverageTotalTime { get; set; }
    public Dictionary<string, StepAnalytics> StepStats { get; set; } = new();
    public List<DropOffPoint> TopDropOffPoints { get; set; } = new();
}

/// <summary>
/// Analytics for a specific step.
/// </summary>
public class StepAnalytics
{
    public string StepId { get; set; } = "";
    public int EnteredCount { get; set; }
    public int CompletedCount { get; set; }
    public double CompletionRate => EnteredCount > 0
        ? (double)CompletedCount / EnteredCount * 100
        : 0;
    public TimeSpan AverageDuration { get; set; }
    public TimeSpan P95Duration { get; set; }
}

/// <summary>
/// Drop-off point analysis.
/// </summary>
public class DropOffPoint
{
    public string StepId { get; set; } = "";
    public string StepName { get; set; } = "";
    public int DropOffCount { get; set; }
    public double DropOffRate { get; set; }
    public List<string> CommonReasons { get; set; } = new();
}

/// <summary>
/// Service for managing task funnels and UX analytics.
/// Implements ux-1 and ux-6 from improve_me.txt.
/// </summary>
public class TaskFunnelService
{
    private static readonly Lazy<TaskFunnelService> _instance = new(() => new TaskFunnelService());
    public static TaskFunnelService Instance => _instance.Value;

    private readonly List<TaskFunnel> _funnels = new();
    private readonly List<FunnelSession> _sessions = new();
    private readonly object _lock = new();
    private FunnelSession? _activeSession;

    private TaskFunnelService()
    {
        InitializeTop5Funnels();
        Logger.Info("TaskFunnelService", "Task funnel service initialized");
    }

    /// <summary>
    /// Gets all defined funnels.
    /// </summary>
    public IReadOnlyList<TaskFunnel> GetFunnels()
    {
        return _funnels.ToList();
    }

    /// <summary>
    /// Gets a specific funnel.
    /// </summary>
    public TaskFunnel? GetFunnel(string id)
    {
        return _funnels.FirstOrDefault(f => f.Id == id);
    }

    /// <summary>
    /// Starts a new funnel session.
    /// </summary>
    public FunnelSession StartSession(string funnelId, string userId)
    {
        var session = new FunnelSession
        {
            FunnelId = funnelId,
            UserId = userId,
            StartedAt = DateTime.Now
        };

        lock (_lock)
        {
            _sessions.Add(session);
            _activeSession = session;
        }

        Logger.Info("TaskFunnelService", $"Funnel session started: {funnelId} for user {userId}");
        AnalyticsService.Instance.TrackFeature($"funnel.{funnelId}.started");

        return session;
    }

    /// <summary>
    /// Records entering a funnel step.
    /// </summary>
    public void EnterStep(string stepId)
    {
        if (_activeSession == null) return;

        var stepCompletion = new FunnelStepCompletion
        {
            StepId = stepId,
            StartedAt = DateTime.Now
        };

        lock (_lock)
        {
            _activeSession.StepCompletions.Add(stepCompletion);
        }

        Logger.Debug("TaskFunnelService", $"Entered step: {stepId}");
    }

    /// <summary>
    /// Records completing a funnel step.
    /// </summary>
    public void CompleteStep(string stepId)
    {
        if (_activeSession == null) return;

        lock (_lock)
        {
            var step = _activeSession.StepCompletions.LastOrDefault(s => s.StepId == stepId);
            if (step != null)
            {
                step.CompletedAt = DateTime.Now;
                step.IsCompleted = true;

                var duration = step.Duration;
                if (duration.HasValue)
                {
                    Logger.Debug("TaskFunnelService", $"Completed step: {stepId} in {duration.Value.TotalSeconds:F1}s");
                }
            }
        }
    }

    /// <summary>
    /// Records funnel completion.
    /// </summary>
    public void CompleteSession()
    {
        if (_activeSession == null) return;

        lock (_lock)
        {
            _activeSession.CompletedAt = DateTime.Now;
            _activeSession.IsCompleted = true;

            var duration = _activeSession.TotalDuration;
            if (duration.HasValue)
            {
                Logger.Info("TaskFunnelService",
                    $"Funnel completed: {_activeSession.FunnelId} in {duration.Value.TotalSeconds:F1}s");
                AnalyticsService.Instance.TrackFeature($"funnel.{_activeSession.FunnelId}.completed");
            }
        }

        _activeSession = null;
    }

    /// <summary>
    /// Records a drop-off.
    /// </summary>
    public void RecordDropOff(string stepId, string reason)
    {
        if (_activeSession == null) return;

        lock (_lock)
        {
            _activeSession.DropOffStepId = stepId;
            _activeSession.DropOffReason = reason;
        }

        Logger.Warning("TaskFunnelService", $"Drop-off at step {stepId}: {reason}");
        AnalyticsService.Instance.TrackFeature($"funnel.{_activeSession.FunnelId}.dropoff", stepId);

        _activeSession = null;
    }

    /// <summary>
    /// Gets analytics for a funnel.
    /// </summary>
    public FunnelAnalytics GetAnalytics(string funnelId, TimeSpan? timeWindow = null)
    {
        var cutoff = DateTime.Now - (timeWindow ?? TimeSpan.FromDays(7));

        lock (_lock)
        {
            var relevantSessions = _sessions
                .Where(s => s.FunnelId == funnelId && s.StartedAt > cutoff)
                .ToList();

            var funnel = GetFunnel(funnelId);
            if (funnel == null)
                return new FunnelAnalytics { FunnelId = funnelId };

            var analytics = new FunnelAnalytics
            {
                FunnelId = funnelId,
                TotalSessions = relevantSessions.Count,
                CompletedSessions = relevantSessions.Count(s => s.IsCompleted)
            };

            // Calculate average total time
            var completedDurations = relevantSessions
                .Where(s => s.TotalDuration.HasValue)
                .Select(s => s.TotalDuration!.Value)
                .ToList();

            if (completedDurations.Any())
            {
                analytics.AverageTotalTime = TimeSpan.FromMilliseconds(
                    completedDurations.Average(d => d.TotalMilliseconds));
            }

            // Step-level analytics
            foreach (var step in funnel.Steps)
            {
                var stepSessions = relevantSessions
                    .SelectMany(s => s.StepCompletions.Where(sc => sc.StepId == step.Id))
                    .ToList();

                var completedSteps = stepSessions.Where(s => s.IsCompleted).ToList();

                var stepAnalytics = new StepAnalytics
                {
                    StepId = step.Id,
                    EnteredCount = stepSessions.Count,
                    CompletedCount = completedSteps.Count
                };

                if (completedSteps.Any())
                {
                    var durations = completedSteps
                        .Where(s => s.Duration.HasValue)
                        .Select(s => s.Duration!.Value)
                        .ToList();

                    if (durations.Any())
                    {
                        stepAnalytics.AverageDuration = TimeSpan.FromMilliseconds(
                            durations.Average(d => d.TotalMilliseconds));
                        stepAnalytics.P95Duration = TimeSpan.FromMilliseconds(
                            durations.OrderBy(d => d.TotalMilliseconds)
                                .ElementAt((int)(durations.Count * 0.95)).TotalMilliseconds);
                    }
                }

                analytics.StepStats[step.Id] = stepAnalytics;
            }

            // Calculate drop-off points
            var dropOffs = relevantSessions
                .Where(s => !s.IsCompleted && s.DropOffStepId != null)
                .GroupBy(s => s.DropOffStepId!)
                .Select(g => new
                {
                    StepId = g.Key,
                    Count = g.Count(),
                    Reasons = g.Select(s => s.DropOffReason).Where(r => r != null).ToList()
                })
                .OrderByDescending(d => d.Count)
                .Take(3)
                .ToList();

            analytics.TopDropOffPoints = dropOffs.Select(d => new DropOffPoint
            {
                StepId = d.StepId,
                StepName = funnel.Steps.FirstOrDefault(s => s.Id == d.StepId)?.Name ?? d.StepId,
                DropOffCount = d.Count,
                DropOffRate = (double)d.Count / relevantSessions.Count * 100,
                CommonReasons = d.Reasons.Take(3).ToList()!
            }).ToList();

            return analytics;
        }
    }

    /// <summary>
    /// Gets funnel performance summary.
    /// </summary>
    public FunnelPerformanceSummary GetPerformanceSummary()
    {
        var summary = new FunnelPerformanceSummary();

        foreach (var funnel in _funnels)
        {
            var analytics = GetAnalytics(funnel.Id);

            summary.FunnelPerformances.Add(new FunnelPerformance
            {
                FunnelId = funnel.Id,
                FunnelName = funnel.Name,
                TargetTime = funnel.TotalTargetTime,
                ActualAverageTime = analytics.AverageTotalTime,
                CompletionRate = analytics.CompletionRate,
                IsMeetingTarget = analytics.AverageTotalTime <= funnel.TotalTargetTime,
                TopDropOffStep = analytics.TopDropOffPoints.FirstOrDefault()?.StepName
            });
        }

        return summary;
    }

    /// <summary>
    /// Exports funnel data for analysis.
    /// </summary>
    public bool ExportFunnelData(string filePath)
    {
        try
        {
            var export = new
            {
                ExportedAt = DateTime.Now,
                Funnels = _funnels.Select(f => new
                {
                    f.Id,
                    f.Name,
                    f.TotalTargetTime,
                    Steps = f.Steps.Select(s => new
                    {
                        s.Id,
                        s.Name,
                        s.TargetDuration
                    })
                }),
                Analytics = _funnels.Select(f => GetAnalytics(f.Id))
            };

            var json = JsonSerializer.Serialize(export, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(filePath, json);
            return true;
        }
        catch (Exception ex)
        {
            Logger.Error("TaskFunnelService", "Failed to export funnel data", ex);
            return false;
        }
    }

    private void InitializeTop5Funnels()
    {
        // Top 5 Jobs to be Done based on persona research

        // Job 1: Deploy power policy at scale (Enterprise Admin)
        _funnels.Add(new TaskFunnel
        {
            Id = "deploy_policy",
            Name = "Deploy Power Policy at Scale",
            PersonaId = "enterprise_admin",
            TotalTargetTime = TimeSpan.FromMinutes(5),
            Steps = new()
            {
                new() { Id = "open_settings", Name = "Open Settings", Order = 1, TargetDuration = TimeSpan.FromSeconds(5), UIElementId = "SettingsNavButton" },
                new() { Id = "navigate_policies", Name = "Navigate to Policy Section", Order = 2, TargetDuration = TimeSpan.FromSeconds(3), UIElementId = "BehaviorNavButton" },
                new() { Id = "configure_policy", Name = "Configure Policy Rules", Order = 3, TargetDuration = TimeSpan.FromMinutes(2), UIElementId = "PolicyConfigurationPanel" },
                new() { Id = "export_config", Name = "Export Configuration", Order = 4, TargetDuration = TimeSpan.FromSeconds(30), UIElementId = "ExportConfigButton" },
                new() { Id = "deploy_to_endpoints", Name = "Deploy to Endpoints", Order = 5, TargetDuration = TimeSpan.FromMinutes(2), UIElementId = "DeployButton" }
            },
            SuccessCriteria = "Configuration exported and deployment initiated"
        });

        // Job 2: Start focused work session (Power Remote Worker)
        _funnels.Add(new TaskFunnel
        {
            Id = "start_focus_session",
            Name = "Start Focused Work Session",
            PersonaId = "power_remote_worker",
            TotalTargetTime = TimeSpan.FromSeconds(30),
            Steps = new()
            {
                new() { Id = "activate_keepawake", Name = "Activate Keep-Awake", Order = 1, TargetDuration = TimeSpan.FromSeconds(5), UIElementId = "ActivateButton" },
                new() { Id = "configure_duration", Name = "Configure Duration", Order = 2, TargetDuration = TimeSpan.FromSeconds(10), UIElementId = "DurationCombo" },
                new() { Id = "enable_pomodoro", Name = "Enable Pomodoro (Optional)", Order = 3, TargetDuration = TimeSpan.FromSeconds(10), UIElementId = "PomodoroToggle" },
                new() { Id = "minimize_to_tray", Name = "Minimize to Tray", Order = 4, TargetDuration = TimeSpan.FromSeconds(5), UIElementId = "MinimizeButton" }
            },
            SuccessCriteria = "Keep-awake active and user returned to work"
        });

        // Job 3: Troubleshoot sleep issue (Enterprise Admin)
        _funnels.Add(new TaskFunnel
        {
            Id = "troubleshoot_sleep",
            Name = "Troubleshoot Sleep Issue",
            PersonaId = "enterprise_admin",
            TotalTargetTime = TimeSpan.FromMinutes(3),
            Steps = new()
            {
                new() { Id = "open_diagnostics", Name = "Open Diagnostics", Order = 1, TargetDuration = TimeSpan.FromSeconds(5), UIElementId = "DiagnosticsNavButton" },
                new() { Id = "view_logs", Name = "View System Logs", Order = 2, TargetDuration = TimeSpan.FromSeconds(10), UIElementId = "ViewLogsButton" },
                new() { Id = "check_sleep_history", Name = "Check Sleep Prevention History", Order = 3, TargetDuration = TimeSpan.FromSeconds(30), UIElementId = "SleepHistoryPanel" },
                new() { Id = "identify_issue", Name = "Identify Root Cause", Order = 4, TargetDuration = TimeSpan.FromMinutes(1), UIElementId = "AnalysisPanel" },
                new() { Id = "apply_fix", Name = "Apply Recommended Fix", Order = 5, TargetDuration = TimeSpan.FromSeconds(30), UIElementId = "ApplyFixButton" }
            },
            SuccessCriteria = "Issue identified and fix applied"
        });

        // Job 4: Type long content hands-free (Power Remote Worker)
        _funnels.Add(new TaskFunnel
        {
            Id = "typething_content",
            Name = "Type Content with TypeThing",
            PersonaId = "power_remote_worker",
            TotalTargetTime = TimeSpan.FromSeconds(45),
            Steps = new()
            {
                new() { Id = "open_typething", Name = "Open TypeThing", Order = 1, TargetDuration = TimeSpan.FromSeconds(5), UIElementId = "TypeThingNavButton" },
                new() { Id = "paste_content", Name = "Paste Content", Order = 2, TargetDuration = TimeSpan.FromSeconds(5), UIElementId = "ContentTextBox" },
                new() { Id = "configure_speed", Name = "Configure Typing Speed", Order = 3, TargetDuration = TimeSpan.FromSeconds(10), UIElementId = "SpeedSlider" },
                new() { Id = "start_typing", Name = "Start Typing", Order = 4, TargetDuration = TimeSpan.FromSeconds(5), UIElementId = "StartTypingButton" },
                new() { Id = "focus_target_app", Name = "Focus Target Application", Order = 5, TargetDuration = TimeSpan.FromSeconds(20), UIElementId = "TargetWindowSelector" }
            },
            SuccessCriteria = "Content typed successfully in target application"
        });

        // Job 5: Monitor productivity impact (Both personas)
        _funnels.Add(new TaskFunnel
        {
            Id = "monitor_impact",
            Name = "Monitor Productivity Impact",
            PersonaId = "enterprise_admin",
            TotalTargetTime = TimeSpan.FromMinutes(2),
            Steps = new()
            {
                new() { Id = "open_analytics", Name = "Open Analytics Dashboard", Order = 1, TargetDuration = TimeSpan.FromSeconds(5), UIElementId = "AnalyticsNavButton" },
                new() { Id = "select_timeframe", Name = "Select Timeframe", Order = 2, TargetDuration = TimeSpan.FromSeconds(5), UIElementId = "TimeframeSelector" },
                new() { Id = "view_metrics", Name = "View Key Metrics", Order = 3, TargetDuration = TimeSpan.FromSeconds(30), UIElementId = "MetricsPanel" },
                new() { Id = "export_report", Name = "Export Report", Order = 4, TargetDuration = TimeSpan.FromSeconds(10), UIElementId = "ExportReportButton" },
                new() { Id = "share_insights", Name = "Share Insights with Team", Order = 5, TargetDuration = TimeSpan.FromSeconds(10), UIElementId = "ShareButton" }
            },
            SuccessCriteria = "Report generated and insights shared"
        });
    }
}

/// <summary>
/// Summary of funnel performance across all funnels.
/// </summary>
public class FunnelPerformanceSummary
{
    public List<FunnelPerformance> FunnelPerformances { get; set; } = new();
    public int TotalFunnels => FunnelPerformances.Count;
    public int FunnelsMeetingTargets => FunnelPerformances.Count(f => f.IsMeetingTarget);
    public double OverallHealth => TotalFunnels > 0
        ? (double)FunnelsMeetingTargets / TotalFunnels * 100
        : 0;
}

/// <summary>
/// Performance metrics for a single funnel.
/// </summary>
public class FunnelPerformance
{
    public string FunnelId { get; set; } = "";
    public string FunnelName { get; set; } = "";
    public TimeSpan TargetTime { get; set; }
    public TimeSpan ActualAverageTime { get; set; }
    public double CompletionRate { get; set; }
    public bool IsMeetingTarget { get; set; }
    public string? TopDropOffStep { get; set; }
}
