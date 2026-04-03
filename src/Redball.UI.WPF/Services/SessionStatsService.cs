using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace Redball.UI.Services;

/// <summary>
/// Tracks per-session and historical statistics: session count, total uptime,
/// longest session, average session, daily usage history.
/// </summary>
public class SessionStatsService
{
    private static readonly Lazy<SessionStatsService> _instance = new(() => new SessionStatsService());
    public static SessionStatsService Instance => _instance.Value;

    private readonly string _statsFile;
    private SessionStatsData _data = new();
    private DateTime? _currentSessionStart;

    public event EventHandler? StatsUpdated;

    private SessionStatsService()
    {
        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Redball");
        if (!Directory.Exists(dir))
            Directory.CreateDirectory(dir);

        _statsFile = Path.Combine(dir, "session_stats.json");
        Load();
        Logger.Verbose("SessionStatsService", $"Stats file: {_statsFile}");
    }

    public int TotalSessions => _data.TotalSessions;
    public TimeSpan TotalUptime => _data.TotalUptime;
    public TimeSpan LongestSession => _data.LongestSession;
    public TimeSpan AverageSession => _data.TotalSessions > 0
        ? TimeSpan.FromTicks(_data.TotalUptime.Ticks / _data.TotalSessions)
        : TimeSpan.Zero;
    public DateTime? CurrentSessionStart => _currentSessionStart;
    public TimeSpan CurrentSessionDuration => _currentSessionStart.HasValue
        ? DateTime.Now - _currentSessionStart.Value
        : TimeSpan.Zero;
    public IReadOnlyDictionary<string, double> DailyHours => _data.DailyHours;

    public void StartSession()
    {
        _currentSessionStart = DateTime.Now;
        _data.TotalSessions++;
        Logger.Info("SessionStatsService", $"Session #{_data.TotalSessions} started");
    }

    public void EndSession()
    {
        if (!_currentSessionStart.HasValue) return;

        var duration = DateTime.Now - _currentSessionStart.Value;
        _data.TotalUptime += duration;

        if (duration > _data.LongestSession)
            _data.LongestSession = duration;

        // Track daily hours
        var dayKey = DateTime.Now.ToString("yyyy-MM-dd");
        _data.DailyHours.TryGetValue(dayKey, out var existing);
        _data.DailyHours[dayKey] = existing + duration.TotalHours;

        _currentSessionStart = null;
        Save();
        StatsUpdated?.Invoke(this, EventArgs.Empty);
        Logger.Info("SessionStatsService", $"Session ended: {duration.TotalMinutes:F1} min");
    }

    public string GetSummaryText()
    {
        var lines = new List<string>
        {
            $"Total sessions: {TotalSessions}",
            $"Total uptime: {FormatDuration(TotalUptime)}",
            $"Longest session: {FormatDuration(LongestSession)}",
            $"Average session: {FormatDuration(AverageSession)}"
        };

        if (_currentSessionStart.HasValue)
            lines.Add($"Current session: {FormatDuration(CurrentSessionDuration)}");

        // Last 7 days
        lines.Add("");
        lines.Add("Last 7 days:");
        for (int i = 0; i < 7; i++)
        {
            var day = DateTime.Now.AddDays(-i).ToString("yyyy-MM-dd");
            var dayName = DateTime.Now.AddDays(-i).ToString("ddd MM/dd");
            _data.DailyHours.TryGetValue(day, out var hours);
            var bar = new string('#', Math.Min((int)(hours * 4), 40));
            lines.Add($"  {dayName}: {hours:F1}h {bar}");
        }

        return string.Join("\n", lines);
    }

    public async Task<IReadOnlyList<SessionRecord>> GetSessionHistoryAsync(int days)
    {
        var end = DateTime.Now;
        var start = end.AddDays(-days);
        return await GetSessionHistoryAsync(start, end);
    }

    public async Task<IReadOnlyList<SessionRecord>> GetSessionHistoryAsync(DateTime start, DateTime end)
    {
        return await Task.FromResult(_data.DailyHours
            .Where(d => DateTime.TryParse(d.Key, out var date) && date >= start && date <= end)
            .Select(d => new SessionRecord
            {
                Date = d.Key,
                Hours = d.Value
            })
            .ToList());
    }

    public class SessionRecord
    {
        public string Date { get; set; } = string.Empty;
        public double Hours { get; set; }
        
        // For ScheduleLearningService and AdvancedAnalyticsService compatibility
        public DateTime StartTime { get; set; }
        public DateTime? EndTime { get; set; }
        public TimeSpan? Duration => EndTime.HasValue ? EndTime.Value - StartTime : null;
    }

    private static string FormatDuration(TimeSpan ts)
    {
        if (ts.TotalDays >= 1)
            return $"{(int)ts.TotalDays}d {ts.Hours}h {ts.Minutes}m";
        if (ts.TotalHours >= 1)
            return $"{(int)ts.TotalHours}h {ts.Minutes}m";
        return $"{(int)ts.TotalMinutes}m";
    }

    private void Load()
    {
        try
        {
            if (File.Exists(_statsFile))
            {
                var json = File.ReadAllText(_statsFile);
                _data = JsonSerializer.Deserialize<SessionStatsData>(json) ?? new();
            }
        }
        catch (Exception ex)
        {
            Logger.Error("SessionStatsService", "Failed to load stats", ex);
            _data = new();
        }
    }

    private void Save()
    {
        try
        {
            var json = JsonSerializer.Serialize(_data, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(_statsFile, json);
        }
        catch (Exception ex)
        {
            Logger.Error("SessionStatsService", "Failed to save stats", ex);
        }
    }
}

public class SessionStatsData
{
    public int TotalSessions { get; set; }
    public TimeSpan TotalUptime { get; set; }
    public TimeSpan LongestSession { get; set; }
    public Dictionary<string, double> DailyHours { get; set; } = new();
}
