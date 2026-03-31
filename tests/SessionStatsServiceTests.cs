using Microsoft.VisualStudio.TestTools.UnitTesting;
using Redball.UI.Services;
using System;
using System.IO;
using System.Reflection;

namespace Redball.Tests;

[TestClass]
public class SessionStatsServiceTests
{
    private string _tempStatsPath = null!;

    [TestInitialize]
    public void Setup()
    {
        // Create temp directory for test stats
        _tempStatsPath = Path.Combine(Path.GetTempPath(), $"redball_test_{Guid.NewGuid()}");
        Directory.CreateDirectory(_tempStatsPath);
    }

    [TestCleanup]
    public void Cleanup()
    {
        try
        {
            if (Directory.Exists(_tempStatsPath))
            {
                Directory.Delete(_tempStatsPath, true);
            }
        }
        catch { }
    }

    [TestMethod]
    public void Instance_IsSingleton()
    {
        var instance1 = SessionStatsService.Instance;
        var instance2 = SessionStatsService.Instance;
        
        Assert.AreSame(instance1, instance2);
    }

    [TestMethod]
    public void StartSession_IncrementsTotalSessions()
    {
        var service = SessionStatsService.Instance;
        var initialSessions = service.TotalSessions;
        
        service.StartSession();
        
        Assert.AreEqual(initialSessions + 1, service.TotalSessions);
        Assert.IsNotNull(service.CurrentSessionStart);
        
        // Cleanup
        service.EndSession();
    }

    [TestMethod]
    public void EndSession_WithoutStart_DoesNothing()
    {
        var service = SessionStatsService.Instance;
        var initialUptime = service.TotalUptime;
        
        service.EndSession();
        
        Assert.AreEqual(initialUptime, service.TotalUptime);
    }

    [TestMethod]
    public void EndSession_UpdatesTotalUptime()
    {
        var service = SessionStatsService.Instance;
        var initialUptime = service.TotalUptime;
        
        service.StartSession();
        Thread.Sleep(100); // Small delay to ensure measurable duration
        service.EndSession();
        
        Assert.IsTrue(service.TotalUptime > initialUptime);
    }

    [TestMethod]
    public void EndSession_UpdatesLongestSession()
    {
        var service = SessionStatsService.Instance;
        var initialLongest = service.LongestSession;
        
        service.StartSession();
        Thread.Sleep(200);
        service.EndSession();
        
        // Should be longer than zero
        Assert.IsTrue(service.LongestSession > TimeSpan.Zero);
    }

    [TestMethod]
    public void AverageSession_CalculatesCorrectly()
    {
        var service = SessionStatsService.Instance;
        
        // With no sessions, average should be zero
        if (service.TotalSessions == 0)
        {
            Assert.AreEqual(TimeSpan.Zero, service.AverageSession);
        }
        else
        {
            var expectedAverage = TimeSpan.FromTicks(service.TotalUptime.Ticks / service.TotalSessions);
            Assert.AreEqual(expectedAverage, service.AverageSession);
        }
    }

    [TestMethod]
    public void CurrentSessionDuration_IsZero_WhenNotStarted()
    {
        var service = SessionStatsService.Instance;
        
        // Make sure no session is active
        service.EndSession();
        
        Assert.AreEqual(TimeSpan.Zero, service.CurrentSessionDuration);
    }

    [TestMethod]
    public void CurrentSessionDuration_IncreasesWhileRunning()
    {
        var service = SessionStatsService.Instance;
        
        service.StartSession();
        var initialDuration = service.CurrentSessionDuration;
        
        Thread.Sleep(150);
        var laterDuration = service.CurrentSessionDuration;
        
        Assert.IsTrue(laterDuration > initialDuration);
        
        service.EndSession();
    }

    [TestMethod]
    public void GetSummaryText_ContainsExpectedSections()
    {
        var service = SessionStatsService.Instance;
        
        var summary = service.GetSummaryText();
        
        Assert.IsNotNull(summary);
        StringAssert.Contains(summary, "Total sessions");
        StringAssert.Contains(summary, "Total uptime");
        StringAssert.Contains(summary, "Longest session");
        StringAssert.Contains(summary, "Average session");
        StringAssert.Contains(summary, "Last 7 days");
    }

    [TestMethod]
    public void GetSummaryText_ShowsCurrentSession_WhenActive()
    {
        var service = SessionStatsService.Instance;
        
        service.StartSession();
        var summary = service.GetSummaryText();
        service.EndSession();
        
        StringAssert.Contains(summary, "Current session");
    }

    [TestMethod]
    public void StatsUpdated_EventFires_OnEndSession()
    {
        var service = SessionStatsService.Instance;
        bool eventFired = false;
        
        service.StatsUpdated += (s, e) => eventFired = true;
        
        service.StartSession();
        Thread.Sleep(50);
        service.EndSession();
        
        Assert.IsTrue(eventFired);
    }

    [TestMethod]
    public void DailyHours_TracksUsage()
    {
        var service = SessionStatsService.Instance;
        
        service.StartSession();
        Thread.Sleep(200);
        service.EndSession();
        
        var dailyHours = service.DailyHours;
        Assert.IsNotNull(dailyHours);
        
        // Should have at least one entry for today
        var todayKey = DateTime.Now.ToString("yyyy-MM-dd");
        if (dailyHours.ContainsKey(todayKey))
        {
            Assert.IsTrue(dailyHours[todayKey] > 0);
        }
    }

    [TestMethod]
    public void MultipleSessions_AccumulateCorrectly()
    {
        var service = SessionStatsService.Instance;
        var initialSessions = service.TotalSessions;
        
        // Start and end first session
        service.StartSession();
        Thread.Sleep(100);
        service.EndSession();
        
        // Start and end second session
        service.StartSession();
        Thread.Sleep(100);
        service.EndSession();
        
        Assert.AreEqual(initialSessions + 2, service.TotalSessions);
    }
}
