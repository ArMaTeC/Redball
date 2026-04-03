using System;
using System.Threading;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Redball.Core.Performance;

namespace Redball.Tests;

/// <summary>
/// Tests for AdaptiveIntervalPolicy CPU monitoring functionality.
/// </summary>
[TestClass]
public class AdaptiveIntervalPolicyTests
{
    /// <summary>
    /// Tests that GetCurrentCpuLoad returns a value within valid range.
    /// </summary>
    [TestMethod]
    public void GetCurrentCpuLoad_ReturnsValidRange()
    {
        var policy = new AdaptiveIntervalPolicy();
        
        // First call may return cached default or initialize
        var load1 = policy.GetCurrentCpuLoad();
        
        // Wait for cache to expire and get fresh reading
        Thread.Sleep(2100);
        var load2 = policy.GetCurrentCpuLoad();
        
        // CPU load should be between 0 and 1
        Assert.IsTrue(load2 >= 0.0, $"CPU load {load2} should be >= 0");
        Assert.IsTrue(load2 <= 1.0, $"CPU load {load2} should be <= 1");
    }

    /// <summary>
    /// Tests that CPU load values are cached for 2 seconds.
    /// </summary>
    [TestMethod]
    public void GetCurrentCpuLoad_CachesResultFor2Seconds()
    {
        var policy = new AdaptiveIntervalPolicy();
        
        var load1 = policy.GetCurrentCpuLoad();
        
        // Immediate second call should return same cached value
        var load2 = policy.GetCurrentCpuLoad();
        
        Assert.AreEqual(load1, load2, "Cached values should be identical");
    }

    /// <summary>
    /// Tests battery power detection doesn't throw.
    /// </summary>
    [TestMethod]
    public void IsOnBatteryPower_DoesNotThrow()
    {
        try
        {
            var result = AdaptiveIntervalPolicy.IsOnBatteryPower();
            // Result depends on actual system state, just ensure no exception
            Assert.IsTrue(result == true || result == false);
        }
        catch (Exception ex)
        {
            Assert.Fail($"IsOnBatteryPower should not throw: {ex.Message}");
        }
    }

    /// <summary>
    /// Tests battery saver detection doesn't throw.
    /// </summary>
    [TestMethod]
    public void IsBatterySaverActive_DoesNotThrow()
    {
        try
        {
            var result = AdaptiveIntervalPolicy.IsBatterySaverActive();
            // Result depends on actual system state, just ensure no exception
            Assert.IsTrue(result == true || result == false);
        }
        catch (Exception ex)
        {
            Assert.Fail($"IsBatterySaverActive should not throw: {ex.Message}");
        }
    }

    /// <summary>
    /// Tests that monitor interval increases under battery saver conditions.
    /// </summary>
    [TestMethod]
    public void GetMonitorInterval_BatterySaver_ReturnsLongerInterval()
    {
        var policy = new AdaptiveIntervalPolicy(
            defaultInterval: TimeSpan.FromSeconds(5),
            batterySaverInterval: TimeSpan.FromSeconds(20)
        );

        var interval = policy.GetMonitorInterval(
            onBatterySaver: true,
            cpuLoad: 0.5,
            userIdle: false
        );

        Assert.AreEqual(TimeSpan.FromSeconds(20), interval);
    }

    /// <summary>
    /// Tests that monitor interval increases under high CPU load.
    /// </summary>
    [TestMethod]
    public void GetMonitorInterval_HighCpu_ReturnsLongerInterval()
    {
        var policy = new AdaptiveIntervalPolicy(
            defaultInterval: TimeSpan.FromSeconds(5),
            highCpuInterval: TimeSpan.FromSeconds(15)
        );

        var interval = policy.GetMonitorInterval(
            onBatterySaver: false,
            cpuLoad: 0.85, // Above 80% threshold
            userIdle: false
        );

        Assert.AreEqual(TimeSpan.FromSeconds(15), interval);
    }

    /// <summary>
    /// Tests that monitor interval increases when user is idle.
    /// </summary>
    [TestMethod]
    public void GetMonitorInterval_UserIdle_ReturnsLongerInterval()
    {
        var policy = new AdaptiveIntervalPolicy(
            defaultInterval: TimeSpan.FromSeconds(5),
            userIdleInterval: TimeSpan.FromSeconds(12)
        );

        var interval = policy.GetMonitorInterval(
            onBatterySaver: false,
            cpuLoad: 0.5,
            userIdle: true
        );

        Assert.AreEqual(TimeSpan.FromSeconds(12), interval);
    }

    /// <summary>
    /// Tests that default interval is returned under normal conditions.
    /// </summary>
    [TestMethod]
    public void GetMonitorInterval_NormalConditions_ReturnsDefaultInterval()
    {
        var policy = new AdaptiveIntervalPolicy(
            defaultInterval: TimeSpan.FromSeconds(5)
        );

        var interval = policy.GetMonitorInterval(
            onBatterySaver: false,
            cpuLoad: 0.5,
            userIdle: false
        );

        Assert.AreEqual(TimeSpan.FromSeconds(5), interval);
    }

    /// <summary>
    /// Tests SharedScheduler basic registration and execution.
    /// </summary>
    [TestMethod]
    public void SharedScheduler_RegistersAndExecutesTask()
    {
        var executed = false;
        using var scheduler = new SharedScheduler(TimeSpan.FromMilliseconds(100));
        
        scheduler.RegisterTask("test", () => executed = true, TimeSpan.FromMilliseconds(50));
        
        // Wait for task to execute
        Thread.Sleep(200);
        
        Assert.IsTrue(executed, "Task should have been executed");
    }

    /// <summary>
    /// Tests SharedScheduler respects minimum interval.
    /// </summary>
    [TestMethod]
    public void SharedScheduler_RespectsMinInterval()
    {
        var executionCount = 0;
        using var scheduler = new SharedScheduler(TimeSpan.FromMilliseconds(50));
        
        // Set a long minimum interval
        scheduler.RegisterTask("test", () => executionCount++, TimeSpan.FromSeconds(10));
        
        // Wait longer to ensure scheduler has started and ticked at least once
        // The scheduler tick interval is 50ms, so wait for multiple ticks
        Thread.Sleep(500);
        
        Assert.AreEqual(0, executionCount, "Task should not execute within min interval");
    }

    /// <summary>
    /// Tests SharedScheduler unregisters tasks correctly.
    /// </summary>
    [TestMethod]
    public void SharedScheduler_UnregisterTask_StopsExecution()
    {
        var executionCount = 0;
        using var scheduler = new SharedScheduler(TimeSpan.FromMilliseconds(50));
        
        scheduler.RegisterTask("test", () => executionCount++, TimeSpan.FromMilliseconds(25));
        Thread.Sleep(100);
        
        scheduler.UnregisterTask("test");
        Thread.Sleep(100);
        
        var countAfterUnregister = executionCount;
        Thread.Sleep(100);
        
        Assert.AreEqual(countAfterUnregister, executionCount, "Task should not execute after unregister");
    }
}
