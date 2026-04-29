using System;

namespace Redball.UI.Services;

/// <summary>
/// Interface for analytics tracking.
/// </summary>
public interface IAnalyticsService : IDisposable
{
    void TrackFeature(string featureName, string? context = null);
    void TrackFunnel(string funnelName, string step);
    void TrackRetention(int day);
    void RecordNps(int score, string? feedback = null);
    double GetNpsScore();
    double GetFunnelConversion(string funnelName, string fromStep, string toStep);
    double GetRetentionRate(int day);
    void TrackSessionStart();
    void TrackSessionEnd();
    AnalyticsSummary GetSummary();
    System.Collections.Generic.IReadOnlyList<int> GetFeatureDailyUsage(string featureName, int days);
    string Export();
    void Clear();
}
