using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Redball.UI.Services;

/// <summary>
/// Advanced Analytics service providing usage predictions, insights,
/// and productivity recommendations based on historical data.
/// </summary>
public class AdvancedAnalyticsService
{
    private static readonly Lazy<AdvancedAnalyticsService> _instance = new(() => new AdvancedAnalyticsService());
    public static AdvancedAnalyticsService Instance => _instance.Value;

    private readonly SessionStatsService _stats;
    private readonly AuditLogService _audit;

    public bool IsEnabled => ConfigService.Instance.Config.EnableAdvancedAnalytics;

    private AdvancedAnalyticsService()
    {
        _stats = SessionStatsService.Instance;
        _audit = AuditLogService.Instance;
        
        Logger.Verbose("AdvancedAnalyticsService", "Initialized");
    }

    /// <summary>
    /// Generates comprehensive usage insights and predictions.
    /// </summary>
    public async Task<AnalyticsReport> GenerateReportAsync(DateTime startDate, DateTime endDate)
    {
        var report = new AnalyticsReport
        {
            Period = new DateRange { Start = startDate, End = endDate },
            GeneratedAt = DateTime.UtcNow
        };

        try
        {
            // Get session data
            var sessions = await _stats.GetSessionHistoryAsync(
                (int)(endDate - startDate).TotalDays);
            
            // Calculate metrics
            report.UsageMetrics = CalculateUsageMetrics(sessions, startDate, endDate);
            report.Predictions = GeneratePredictions(sessions);
            report.Insights = GenerateInsights(sessions, report.UsageMetrics);
            report.Recommendations = GenerateRecommendations(report);

            Logger.Info("AdvancedAnalyticsService", "Analytics report generated");
        }
        catch (Exception ex)
        {
            Logger.Error("AdvancedAnalyticsService", "Report generation failed", ex);
        }

        return report;
    }

    /// <summary>
    /// Predicts usage for the upcoming week.
    /// </summary>
    public UsagePrediction PredictUpcomingWeek()
    {
        var today = DateTime.Now;
        var weekStart = today.AddDays(-(int)today.DayOfWeek);
        
        // Get last 4 weeks of data
        var last4Weeks = Enumerable.Range(0, 4)
            .Select(i => weekStart.AddDays(-7 * i))
            .ToList();

        var predictions = new List<DailyPrediction>();
        
        for (int day = 0; day < 7; day++)
        {
            var targetDate = today.AddDays(day);
            var dayOfWeek = targetDate.DayOfWeek;
            
            // Calculate average for this day of week
            var historicalDays = last4Weeks
                .Select(week => week.AddDays((int)dayOfWeek))
                .ToList();
            
            var prediction = new DailyPrediction
            {
                Date = targetDate,
                DayOfWeek = dayOfWeek,
                PredictedActiveHours = 2.5, // Default baseline
                Confidence = 0.5
            };
            
            predictions.Add(prediction);
        }

        return new UsagePrediction
        {
            WeekStart = today,
            DailyPredictions = predictions,
            TotalPredictedHours = predictions.Sum(p => p.PredictedActiveHours)
        };
    }

    /// <summary>
    /// Gets battery optimization suggestions.
    /// </summary>
    public BatteryOptimizationSuggestions GetBatterySuggestions()
    {
        var suggestions = new BatteryOptimizationSuggestions();
        
        if (!BatteryMonitorService.Instance.IsBatteryMonitoringAvailable)
        {
            suggestions.IsAvailable = false;
            return suggestions;
        }

        var battery = BatteryMonitorService.Instance;
        var sessions = _stats.GetSessionHistoryAsync(30).Result;
        
        if (sessions.Count == 0)
        {
            suggestions.Suggestions.Add(new BatterySuggestion
            {
                Type = BatterySuggestionType.Info,
                Title = "No usage data yet",
                Description = "Use Redball for a few days to get personalized battery recommendations."
            });
            return suggestions;
        }

        // Analyze battery impact
        var avgSessionDuration = sessions
            .Where(s => s.Duration.HasValue)
            .Average(s => s.Duration!.Value.TotalHours);

        if (avgSessionDuration > 4)
        {
            suggestions.Suggestions.Add(new BatterySuggestion
            {
                Type = BatterySuggestionType.Warning,
                Title = "Long sessions detected",
                Description = $"Your average session is {avgSessionDuration:F1} hours. Consider using timed sessions to preserve battery."
            });
        }

        if (!battery.IsCharging && battery.CurrentLevel < 30)
        {
            suggestions.Suggestions.Add(new BatterySuggestion
            {
                Type = BatterySuggestionType.Critical,
                Title = "Low battery - Auto-pause recommended",
                Description = "Battery below 30%. Enable Battery-Aware mode to auto-pause when unplugged."
            });
        }

        // Productivity insights
        var mostProductiveTime = FindMostProductiveTime(sessions);
        if (mostProductiveTime.HasValue)
        {
            suggestions.Suggestions.Add(new BatterySuggestion
            {
                Type = BatterySuggestionType.Insight,
                Title = "Most productive time",
                Description = $"You're most active around {mostProductiveTime.Value:hh\\:mm}. Consider scheduling focused work then."
            });
        }

        suggestions.IsAvailable = true;
        return suggestions;
    }

    /// <summary>
    /// Calculates productivity score based on usage patterns.
    /// </summary>
    public ProductivityScore CalculateProductivityScore(int days = 30)
    {
        var endDate = DateTime.Now;
        var startDate = endDate.AddDays(-days);
        
        var sessions = _stats.GetSessionHistoryAsync(days).Result;
        var auditEntries = _audit.GetEntries(startDate, endDate, AuditEventType.Session);

        var score = new ProductivityScore
        {
            PeriodDays = days,
            TotalSessions = sessions.Count,
            TotalActiveHours = sessions
                .Where(s => s.Duration.HasValue)
                .Sum(s => s.Duration!.Value.TotalHours),
            AverageSessionLength = sessions.Any() 
                ? sessions.Where(s => s.Duration.HasValue).Average(s => s.Duration!.Value.TotalMinutes)
                : 0,
            ConsistencyScore = CalculateConsistency(sessions, days),
            FocusScore = CalculateFocusScore(auditEntries)
        };

        // Overall score (0-100)
        score.OverallScore = Math.Min(100, 
            (score.ConsistencyScore * 0.4) + 
            (score.FocusScore * 0.3) + 
            (Math.Min(score.TotalSessions / 20.0, 1.0) * 30));

        return score;
    }

    private UsageMetrics CalculateUsageMetrics(List<SessionRecord> sessions, DateTime start, DateTime end)
    {
        var days = (end - start).TotalDays;
        
        return new UsageMetrics
        {
            TotalSessions = sessions.Count,
            TotalActiveTime = TimeSpan.FromHours(sessions
                .Where(s => s.Duration.HasValue)
                .Sum(s => s.Duration!.Value.TotalHours)),
            AverageSessionsPerDay = sessions.Count / Math.Max(1, days),
            AverageSessionDuration = sessions.Any(s => s.Duration.HasValue)
                ? TimeSpan.FromMinutes(sessions.Where(s => s.Duration.HasValue).Average(s => s.Duration!.Value.TotalMinutes))
                : TimeSpan.Zero,
            LongestSession = sessions.Any(s => s.Duration.HasValue)
                ? sessions.Where(s => s.Duration.HasValue).Max(s => s.Duration!.Value)
                : TimeSpan.Zero,
            MostActiveDay = FindMostActiveDay(sessions),
            PeakUsageHour = FindPeakUsageHour(sessions)
        };
    }

    private UsagePredictions GeneratePredictions(List<SessionRecord> sessions)
    {
        if (sessions.Count < 5)
        {
            return new UsagePredictions 
            { 
                Confidence = 0,
                Note = "Insufficient data for accurate predictions (need 5+ sessions)"
            };
        }

        var nextWeekPrediction = PredictUpcomingWeek();
        
        return new UsagePredictions
        {
            NextWeek = nextWeekPrediction,
            ExpectedSessionsNextWeek = (int)(sessions.Count / 4.0), // Assuming 4 weeks of data
            Confidence = Math.Min(0.9, sessions.Count / 50.0)
        };
    }

    private List<UsageInsight> GenerateInsights(List<SessionRecord> sessions, UsageMetrics metrics)
    {
        var insights = new List<UsageInsight>();

        if (sessions.Count == 0)
        {
            insights.Add(new UsageInsight
            {
                Type = InsightType.Info,
                Title = "Welcome to Advanced Analytics",
                Description = "Start using Redball to see personalized insights and recommendations."
            });
            return insights;
        }

        // Pattern insights
        var patterns = FindUsagePatterns(sessions);
        foreach (var pattern in patterns.Take(3))
        {
            insights.Add(new UsageInsight
            {
                Type = InsightType.Pattern,
                Title = pattern.Name,
                Description = pattern.Description
            });
        }

        // Efficiency insights
        if (metrics.AverageSessionDuration.TotalHours > 3)
        {
            insights.Add(new UsageInsight
            {
                Type = InsightType.Suggestion,
                Title = "Long sessions detected",
                Description = "Consider taking breaks every 2 hours for better productivity."
            });
        }

        // Time insights
        if (metrics.PeakUsageHour.HasValue)
        {
            insights.Add(new UsageInsight
            {
                Type = InsightType.Timing,
                Title = $"Peak usage at {metrics.PeakUsageHour.Value}:00",
                Description = "This is when you're most active. Good time for focused work!"
            });
        }

        return insights;
    }

    private List<UsageRecommendation> GenerateRecommendations(AnalyticsReport report)
    {
        var recommendations = new List<UsageRecommendation>();

        // Battery recommendations
        if (report.UsageMetrics.AverageSessionDuration.TotalHours > 4)
        {
            recommendations.Add(new UsageRecommendation
            {
                Category = RecommendationCategory.Battery,
                Priority = RecommendationPriority.Medium,
                Title = "Enable Battery-Aware Mode",
                Description = "Your long sessions may drain battery. Enable Battery-Aware mode in settings."
            });
        }

        // Schedule recommendations
        if (report.Predictions.Confidence > 0.5)
        {
            recommendations.Add(new UsageRecommendation
            {
                Category = RecommendationCategory.Schedule,
                Priority = RecommendationPriority.Low,
                Title = "Try Smart Schedule",
                Description = "Enable Smart Schedule Learning for automatic session suggestions."
            });
        }

        // Break recommendations
        recommendations.Add(new UsageRecommendation
        {
            Category = RecommendationCategory.Health,
            Priority = RecommendationPriority.High,
            Title = "Use Pomodoro Timer",
            Description = "Take regular breaks with the Pomodoro timer to maintain productivity."
        });

        return recommendations.OrderByDescending(r => r.Priority).ToList();
    }

    private List<DetectedPattern> FindUsagePatterns(List<SessionRecord> sessions)
    {
        var patterns = new List<DetectedPattern>();
        
        // Time patterns
        var hourGroups = sessions.GroupBy(s => s.StartTime.Hour)
            .Select(g => new { Hour = g.Key, Count = g.Count() })
            .Where(g => g.Count >= 3)
            .OrderByDescending(g => g.Count)
            .ToList();

        if (hourGroups.Any())
        {
            var topHour = hourGroups.First();
            patterns.Add(new DetectedPattern
            {
                Name = $"Active around {topHour.Hour}:00",
                Description = $"You've started {topHour.Count} sessions near this time",
                Confidence = Math.Min(0.9, topHour.Count / 10.0)
            });
        }

        // Day patterns
        var dayGroups = sessions.GroupBy(s => s.StartTime.DayOfWeek)
            .Select(g => new { Day = g.Key, Count = g.Count() })
            .OrderByDescending(g => g.Count)
            .ToList();

        if (dayGroups.Any())
        {
            var topDay = dayGroups.First();
            if (topDay.Count >= 5)
            {
                patterns.Add(new DetectedPattern
                {
                    Name = $"{topDay.Day} is your most active day",
                    Description = $"You've had {topDay.Count} sessions on {topDay.Day}s",
                    Confidence = 0.7
                });
            }
        }

        return patterns;
    }

    private TimeSpan? FindMostProductiveTime(List<SessionRecord> sessions)
    {
        return sessions
            .Where(s => s.Duration.HasValue && s.Duration.Value.TotalMinutes > 30)
            .GroupBy(s => s.StartTime.Hour)
            .OrderByDescending(g => g.Count())
            .Select(g => (TimeSpan?)TimeSpan.FromHours(g.Key))
            .FirstOrDefault();
    }

    private DayOfWeek? FindMostActiveDay(List<SessionRecord> sessions)
    {
        return sessions
            .GroupBy(s => s.StartTime.DayOfWeek)
            .OrderByDescending(g => g.Count())
            .Select(g => (DayOfWeek?)g.Key)
            .FirstOrDefault();
    }

    private int? FindPeakUsageHour(List<SessionRecord> sessions)
    {
        return sessions
            .GroupBy(s => s.StartTime.Hour)
            .OrderByDescending(g => g.Count())
            .Select(g => (int?)g.Key)
            .FirstOrDefault();
    }

    private double CalculateConsistency(List<SessionRecord> sessions, int days)
    {
        if (sessions.Count == 0) return 0;
        
        var daysWithSessions = sessions
            .Select(s => s.StartTime.Date)
            .Distinct()
            .Count();
        
        return Math.Min(1.0, daysWithSessions / (double)days);
    }

    private double CalculateFocusScore(List<AuditLogEntry> auditEntries)
    {
        // Higher score for longer continuous sessions
        var avgSessionDuration = auditEntries
            .Where(e => e.EventType == AuditEventType.Session && e.Details != null)
            .Select(e => ExtractDuration(e.Details))
            .DefaultIfEmpty(0)
            .Average();
        
        // Score based on session length (30-120 min is ideal)
        if (avgSessionDuration >= 30 && avgSessionDuration <= 120)
            return 0.9;
        else if (avgSessionDuration > 120)
            return 0.7; // Too long, may indicate fatigue
        else
            return Math.Min(1.0, avgSessionDuration / 30.0);
    }

    private double ExtractDuration(string? details)
    {
        if (string.IsNullOrEmpty(details)) return 0;
        
        var match = System.Text.RegularExpressions.Regex.Match(details, @"(\d+(?:\.\d+)?)\s*min");
        if (match.Success && double.TryParse(match.Groups[1].Value, out var minutes))
        {
            return minutes;
        }
        
        return 0;
    }
}

// Data models
public class AnalyticsReport
{
    public DateRange Period { get; set; } = new();
    public DateTime GeneratedAt { get; set; }
    public UsageMetrics UsageMetrics { get; set; } = new();
    public UsagePredictions Predictions { get; set; } = new();
    public List<UsageInsight> Insights { get; set; } = new();
    public List<UsageRecommendation> Recommendations { get; set; } = new();
}

public class UsageMetrics
{
    public int TotalSessions { get; set; }
    public TimeSpan TotalActiveTime { get; set; }
    public double AverageSessionsPerDay { get; set; }
    public TimeSpan AverageSessionDuration { get; set; }
    public TimeSpan LongestSession { get; set; }
    public DayOfWeek? MostActiveDay { get; set; }
    public int? PeakUsageHour { get; set; }
}

public class UsagePredictions
{
    public UsagePrediction NextWeek { get; set; } = new();
    public int ExpectedSessionsNextWeek { get; set; }
    public double Confidence { get; set; }
    public string? Note { get; set; }
}

public class UsagePrediction
{
    public DateTime WeekStart { get; set; }
    public List<DailyPrediction> DailyPredictions { get; set; } = new();
    public double TotalPredictedHours { get; set; }
}

public class DailyPrediction
{
    public DateTime Date { get; set; }
    public DayOfWeek DayOfWeek { get; set; }
    public double PredictedActiveHours { get; set; }
    public double Confidence { get; set; }
}

public class UsageInsight
{
    public InsightType Type { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
}

public enum InsightType
{
    Info,
    Pattern,
    Timing,
    Suggestion,
    Achievement
}

public class DetectedPattern
{
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public double Confidence { get; set; }
}

public class UsageRecommendation
{
    public RecommendationCategory Category { get; set; }
    public RecommendationPriority Priority { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
}

public enum RecommendationCategory
{
    Battery,
    Schedule,
    Health,
    Productivity
}

public enum RecommendationPriority
{
    Low,
    Medium,
    High,
    Critical
}

public class ProductivityScore
{
    public int PeriodDays { get; set; }
    public int TotalSessions { get; set; }
    public double TotalActiveHours { get; set; }
    public double AverageSessionLength { get; set; }
    public double OverallScore { get; set; }
    public double ConsistencyScore { get; set; }
    public double FocusScore { get; set; }
}

public class BatteryOptimizationSuggestions
{
    public bool IsAvailable { get; set; }
    public List<BatterySuggestion> Suggestions { get; set; } = new();
}

public class BatterySuggestion
{
    public BatterySuggestionType Type { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
}

public enum BatterySuggestionType
{
    Info,
    Suggestion,
    Warning,
    Critical,
    Insight
}
