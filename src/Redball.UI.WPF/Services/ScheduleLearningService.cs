using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Redball.UI.Services;

/// <summary>
/// Smart Schedule Learning service that analyzes user patterns
/// to predict optimal keep-awake times and auto-start sessions.
/// </summary>
public class ScheduleLearningService
{
    private static readonly Lazy<ScheduleLearningService> _instance = new(() => new ScheduleLearningService());
    public static ScheduleLearningService Instance => _instance.Value;

    private readonly SessionStatsService _stats;
    private readonly List<UsagePattern> _patterns;
    private bool _isLearningEnabled;

    public event EventHandler<ScheduleSuggestionEventArgs>? ScheduleSuggested;
    public event EventHandler<PatternDetectedEventArgs>? PatternDetected;

    public bool IsLearningEnabled
    {
        get => _isLearningEnabled;
        set
        {
            _isLearningEnabled = value;
            if (value) StartLearning();
            else StopLearning();
        }
    }

    public IReadOnlyList<UsagePattern> DetectedPatterns => _patterns.AsReadOnly();

    private ScheduleLearningService()
    {
        _stats = SessionStatsService.Instance;
        _patterns = new List<UsagePattern>();
        _isLearningEnabled = ConfigService.Instance.Config.EnableSmartSchedule;
        
        Logger.Verbose("ScheduleLearningService", "Initialized");
    }

    /// <summary>
    /// Analyzes historical usage to detect patterns.
    /// </summary>
    public async Task AnalyzePatternsAsync()
    {
        try
        {
            var sessions = await _stats.GetSessionHistoryAsync(days: 30);
            
            if (sessions.Count < 5)
            {
                Logger.Info("ScheduleLearningService", "Insufficient data for pattern detection (need 5+ sessions)");
                return;
            }

            // Clear old patterns
            _patterns.Clear();

            // Detect time-based patterns
            DetectTimePatterns(sessions);
            
            // Detect day-of-week patterns
            DetectDayPatterns(sessions);
            
            // Detect duration patterns
            DetectDurationPatterns(sessions);

            // Notify about new patterns
            foreach (var pattern in _patterns.Where(p => p.Confidence > 0.7))
            {
                PatternDetected?.Invoke(this, new PatternDetectedEventArgs
                {
                    Pattern = pattern,
                    DetectedAt = DateTime.UtcNow
                });
            }

            Logger.Info("ScheduleLearningService", $"Detected {_patterns.Count} usage patterns");
        }
        catch (Exception ex)
        {
            Logger.Error("ScheduleLearningService", "Pattern analysis failed", ex);
        }
    }

    /// <summary>
    /// Gets schedule suggestions for the current time.
    /// </summary>
    public List<ScheduleSuggestion> GetSuggestionsForTime(DateTime? time = null)
    {
        var targetTime = time ?? DateTime.Now;
        var suggestions = new List<ScheduleSuggestion>();

        foreach (var pattern in _patterns.Where(p => p.IsActive && p.Confidence > 0.6))
        {
            var suggestion = pattern.GenerateSuggestion(targetTime);
            if (suggestion != null && suggestion.Probability > 0.5)
            {
                suggestions.Add(suggestion);
            }
        }

        return suggestions.OrderByDescending(s => s.Probability).ToList();
    }

    /// <summary>
    /// Predicts the next likely session time.
    /// </summary>
    public DateTime? PredictNextSession()
    {
        if (_patterns.Count == 0) return null;

        var now = DateTime.Now;
        var bestPrediction = _patterns
            .Where(p => p.IsActive && p.Type == PatternType.TimeBased)
            .Select(p => new
            {
                Pattern = p,
                NextOccurrence = p.GetNextOccurrence(now),
                Probability = p.Confidence
            })
            .Where(x => x.NextOccurrence.HasValue)
            .OrderByDescending(x => x.Probability)
            .FirstOrDefault();

        return bestPrediction?.NextOccurrence;
    }

    /// <summary>
    /// Auto-starts keep-awake if a pattern suggests it's time.
    /// </summary>
    public async Task<bool> TryAutoStartAsync()
    {
        if (!IsLearningEnabled) return false;

        var suggestions = GetSuggestionsForTime();
        var bestSuggestion = suggestions.FirstOrDefault();

        if (bestSuggestion?.Probability > 0.8 && bestSuggestion.Action == SuggestedAction.Start)
        {
            // Suggest auto-start
            ScheduleSuggested?.Invoke(this, new ScheduleSuggestionEventArgs
            {
                Suggestion = bestSuggestion,
                AutoStart = true
            });

            // Actually start if auto-start is enabled
            if (ConfigService.Instance.Config.AutoStartBasedOnPatterns)
            {
                KeepAwakeService.Instance.SetActive(true);
                if (bestSuggestion.RecommendedDuration.HasValue)
                {
                    // Start with suggested duration
                    KeepAwakeService.Instance.StartTimed(bestSuggestion.RecommendedDuration.Value);
                }
                
                Logger.Info("ScheduleLearningService", $"Auto-started based on pattern (confidence: {bestSuggestion.Probability:P})");
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Provides battery-aware predictions.
    /// </summary>
    public BatteryPrediction GetBatteryPrediction()
    {
        if (!BatteryMonitorService.Instance.IsBatteryMonitoringAvailable)
        {
            return new BatteryPrediction { IsAvailable = false };
        }

        var battery = BatteryMonitorService.Instance;
        var prediction = new BatteryPrediction
        {
            IsAvailable = true,
            CurrentLevel = battery.CurrentLevel,
            IsCharging = battery.IsCharging,
            EstimatedRemainingMinutes = battery.EstimatedTimeRemaining?.TotalMinutes
        };

        // Predict if session is viable based on patterns
        var patterns = _patterns.Where(p => p.IsActive).ToList();
        if (patterns.Any())
        {
            var avgDuration = patterns.Average(p => p.AverageDurationMinutes);
            var requiredLevel = Math.Min(20, avgDuration / 60.0 * 10); // Rough estimate: 10% per hour

            prediction.CanCompleteTypicalSession = battery.CurrentLevel > requiredLevel;
            prediction.RecommendedMaxDuration = (int)(battery.CurrentLevel / 10.0 * 60); // ~10% per hour
        }

        return prediction;
    }

    private void DetectTimePatterns(IReadOnlyList<SessionStatsService.SessionRecord> sessions)
    {
        // Group sessions by hour of day
        var hourGroups = sessions
            .GroupBy(s => s.StartTime.Hour)
            .Select(g => new { Hour = g.Key, Count = g.Count(), Sessions = g.ToList() })
            .Where(g => g.Count >= 3)
            .OrderByDescending(g => g.Count)
            .ToList();

        foreach (var group in hourGroups.Take(3))
        {
            var pattern = new UsagePattern
            {
                Type = PatternType.TimeBased,
                Name = $"Daily at {group.Hour}:00",
                Description = $"Sessions typically start around {group.Hour}:00",
                PreferredStartTime = TimeSpan.FromHours(group.Hour),
                AverageDurationMinutes = (int)group.Sessions.Average(s => s.Duration?.TotalMinutes ?? 60),
                Frequency = group.Count,
                Confidence = Math.Min(0.95, group.Count / 10.0),
                IsActive = true
            };
            
            _patterns.Add(pattern);
        }
    }

    private void DetectDayPatterns(IReadOnlyList<SessionStatsService.SessionRecord> sessions)
    {
        // Group by day of week
        var dayGroups = sessions
            .GroupBy(s => s.StartTime.DayOfWeek)
            .Select(g => new { Day = g.Key, Count = g.Count() })
            .Where(g => g.Count >= 5)
            .OrderByDescending(g => g.Count);

        foreach (var group in dayGroups)
        {
            var pattern = new UsagePattern
            {
                Type = PatternType.DayBased,
                Name = $"{group.Day} sessions",
                Description = $"Frequent usage on {group.Day}s",
                PreferredDays = new[] { group.Day },
                Frequency = group.Count,
                Confidence = Math.Min(0.9, group.Count / 15.0),
                IsActive = true
            };
            
            _patterns.Add(pattern);
        }
    }

    private void DetectDurationPatterns(IReadOnlyList<SessionStatsService.SessionRecord> sessions)
    {
        // Find common duration patterns
        var durations = sessions
            .Where(s => s.Duration.HasValue)
            .Select(s => s.Duration.GetValueOrDefault().TotalMinutes)
            .ToList();

        if (durations.Count < 5) return;

        var avgDuration = durations.Average();
        var stdDev = Math.Sqrt(durations.Average(d => Math.Pow(d - avgDuration, 2)));

        if (stdDev < avgDuration * 0.3) // Low variance = consistent duration
        {
            var pattern = new UsagePattern
            {
                Type = PatternType.DurationBased,
                Name = $"~{(int)avgDuration} minute sessions",
                Description = $"Consistent session length of about {(int)avgDuration} minutes",
                AverageDurationMinutes = (int)avgDuration,
                Frequency = durations.Count,
                Confidence = 0.7 + (1 - stdDev / avgDuration) * 0.2,
                IsActive = true
            };
            
            _patterns.Add(pattern);
        }
    }

    private void StartLearning()
    {
        // Schedule periodic analysis
        Logger.Info("ScheduleLearningService", "Learning mode activated");
        
        // Initial analysis
        _ = AnalyzePatternsAsync();
    }

    private void StopLearning()
    {
        Logger.Info("ScheduleLearningService", "Learning mode deactivated");
    }
}

public class UsagePattern
{
    public PatternType Type { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    
    // Time-based
    public TimeSpan? PreferredStartTime { get; set; }
    public TimeSpan? TimeWindow { get; set; } = TimeSpan.FromMinutes(30);
    
    // Day-based
    public DayOfWeek[]? PreferredDays { get; set; }
    
    // Duration-based
    public int AverageDurationMinutes { get; set; }
    public int? MaxDurationMinutes { get; set; }
    
    // Statistics
    public int Frequency { get; set; }
    public double Confidence { get; set; } // 0.0 - 1.0
    public DateTime LastDetected { get; set; } = DateTime.UtcNow;
    
    // State
    public bool IsActive { get; set; } = true;

    public ScheduleSuggestion? GenerateSuggestion(DateTime targetTime)
    {
        var probability = CalculateProbability(targetTime);
        if (probability < 0.3) return null;

        return new ScheduleSuggestion
        {
            Pattern = this,
            TargetTime = targetTime,
            Probability = probability,
            Action = probability > 0.7 ? SuggestedAction.Start : SuggestedAction.Suggest,
            RecommendedDuration = AverageDurationMinutes > 0 ? AverageDurationMinutes : null,
            Reason = $"Based on your {Frequency} previous sessions"
        };
    }

    private double CalculateProbability(DateTime targetTime)
    {
        var score = Confidence * 0.5; // Base confidence

        if (Type == PatternType.TimeBased && PreferredStartTime.HasValue)
        {
            var targetTimeOfDay = targetTime.TimeOfDay;
            var diff = Math.Abs((targetTimeOfDay - PreferredStartTime.Value).TotalMinutes);
            if (diff < TimeWindow?.TotalMinutes)
            {
                score += 0.5 * (1 - diff / TimeWindow.Value.TotalMinutes);
            }
        }

        if (Type == PatternType.DayBased && PreferredDays != null)
        {
            if (PreferredDays.Contains(targetTime.DayOfWeek))
            {
                score += 0.3;
            }
        }

        return Math.Min(0.95, score);
    }

    public DateTime? GetNextOccurrence(DateTime from)
    {
        if (Type != PatternType.TimeBased || !PreferredStartTime.HasValue)
            return null;

        var next = from.Date + PreferredStartTime.Value;
        if (next <= from)
            next = next.AddDays(1);

        return next;
    }
}

public enum PatternType
{
    TimeBased,
    DayBased,
    DurationBased,
    CalendarBased
}

public class ScheduleSuggestion
{
    public UsagePattern Pattern { get; set; } = null!;
    public DateTime TargetTime { get; set; }
    public double Probability { get; set; }
    public SuggestedAction Action { get; set; }
    public int? RecommendedDuration { get; set; }
    public string Reason { get; set; } = string.Empty;
}

public enum SuggestedAction
{
    Suggest,
    Start,
    Remind,
    Skip
}

public class BatteryPrediction
{
    public bool IsAvailable { get; set; }
    public int CurrentLevel { get; set; }
    public bool IsCharging { get; set; }
    public double? EstimatedRemainingMinutes { get; set; }
    public bool CanCompleteTypicalSession { get; set; }
    public int? RecommendedMaxDuration { get; set; }
}

// Event args
public class PatternDetectedEventArgs : EventArgs
{
    public UsagePattern Pattern { get; set; } = null!;
    public DateTime DetectedAt { get; set; }
}

public class ScheduleSuggestionEventArgs : EventArgs
{
    public ScheduleSuggestion Suggestion { get; set; } = null!;
    public bool AutoStart { get; set; }
}

/// <summary>
/// Represents a recorded keep-awake session for pattern analysis.
/// </summary>
public class SessionRecord
{
    public DateTime StartTime { get; set; }
    public DateTime? EndTime { get; set; }
    public TimeSpan? Duration => EndTime.HasValue ? EndTime.Value - StartTime : null;
    public bool WasCompleted { get; set; }
    public int? BatteryLevelAtStart { get; set; }
    public int? BatteryLevelAtEnd { get; set; }
}
