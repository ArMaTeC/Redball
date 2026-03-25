using Microsoft.VisualStudio.TestTools.UnitTesting;
using Redball.UI.Services;
using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Redball.Tests
{
    /// <summary>
    /// Stress tests for long-running sessions - simulates Redball running for
    /// extended periods to detect memory leaks, timer drift, and resource exhaustion.
    /// </summary>
    [TestClass]
    public class StressTests
    {
        private const int ShortStressMinutes = 2;   // For CI (fast feedback)
        private const int MemorySampleIntervalMs = 5000;
        private const double MemoryGrowthThresholdPercent = 20;  // Max acceptable growth

        [TestMethod]
        [TestCategory("Stress")]
        [Ignore("Long-running stress test - run manually or on scheduled CI")]
        public async Task LongRunningSession_NoMemoryLeak()
        {
            // Arrange
            var service = KeepAwakeService.Instance;
            var initialMemory = GetWorkingSetMB();
            var samples = new System.Collections.Generic.List<double> { initialMemory };
            var cts = new CancellationTokenSource(TimeSpan.FromMinutes(ShortStressMinutes));

            Logger.Info("StressTests", $"Starting memory leak test - initial: {initialMemory:F1} MB");

            // Act - toggle active state repeatedly to simulate real usage
            service.SetActive(true);
            var toggleCount = 0;

            try
            {
                while (!cts.Token.IsCancellationRequested)
                {
                    // Toggle every 10 seconds to simulate user activity
                    await Task.Delay(10000, cts.Token);
                    service.Toggle();
                    toggleCount++;

                    // Sample memory every 5 seconds
                    if (toggleCount % 2 == 0)
                    {
                        var currentMemory = GetWorkingSetMB();
                        samples.Add(currentMemory);
                        GC.Collect(); // Force GC to get true memory pressure
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // Expected when timeout expires
            }

            service.SetActive(false);

            // Assert - check memory growth
            var finalMemory = GetWorkingSetMB();
            var maxMemory = Math.Max(initialMemory, samples.Max());
            var growthPercent = ((maxMemory - initialMemory) / initialMemory) * 100;

            Logger.Info("StressTests", $"Stress test complete - toggles: {toggleCount}, max: {maxMemory:F1} MB, growth: {growthPercent:F1}%");

            Assert.IsTrue(growthPercent < MemoryGrowthThresholdPercent,
                $"Memory grew by {growthPercent:F1}% (threshold: {MemoryGrowthThresholdPercent}%). " +
                $"Initial: {initialMemory:F1} MB, Max: {maxMemory:F1} MB. Possible memory leak.");
        }

        [TestMethod]
        [TestCategory("Stress")]
        public void TimerAccuracy_NoDriftOverShortPeriod()
        {
            // Arrange - use Stopwatch for accurate timing
            var stopwatch = Stopwatch.StartNew();
            var interval = TimeSpan.FromMilliseconds(100);
            var iterations = 50;
            var timer = new System.Timers.Timer(interval.TotalMilliseconds);
            var tickCount = 0;
            var latch = new ManualResetEventSlim(false);

            timer.Elapsed += (s, e) =>
            {
                tickCount++;
                if (tickCount >= iterations)
                    latch.Set();
            };

            // Act
            timer.Start();
            latch.Wait(TimeSpan.FromSeconds(30));
            stopwatch.Stop();
            timer.Stop();
            timer.Dispose();

            // Assert - check timer accuracy
            var expectedDuration = interval * iterations;
            var actualDuration = stopwatch.Elapsed;
            var drift = Math.Abs((actualDuration - expectedDuration).TotalMilliseconds);
            var driftPercent = (drift / expectedDuration.TotalMilliseconds) * 100;

            Logger.Info("StressTests", $"Timer accuracy - expected: {expectedDuration.TotalMilliseconds:F0}ms, actual: {actualDuration.TotalMilliseconds:F0}ms, drift: {drift:F0}ms ({driftPercent:F1}%)");

            // Allow up to 15% drift — Windows timer resolution and OS scheduling
            // can easily cause >5% variance, especially under CI load
            Assert.IsTrue(driftPercent < 15,
                $"Timer drift too high: {driftPercent:F1}% ({drift:F0}ms). Expected: {expectedDuration.TotalMilliseconds:F0}ms, Actual: {actualDuration.TotalMilliseconds:F0}ms");
        }

        [TestMethod]
        [TestCategory("Stress")]
        public void FileHandle_SteadyState_NoLeak()
        {
            // Arrange
            var process = Process.GetCurrentProcess();
            var initialHandles = process.HandleCount;
            var logPath = Path.Combine(Path.GetTempPath(), $"stress_test_{Guid.NewGuid()}.log");

            // Ensure Logger is initialized to a temp file
            Logger.Initialize(logPath);

            try
            {
                // Act - generate log activity
                for (var i = 0; i < 100; i++)
                {
                    Logger.Info("StressTests", $"Stress test log entry {i}");
                    if (i % 10 == 0)
                        Thread.Sleep(10); // Small delay to simulate real usage
                }

                // Force cleanup
                GC.Collect();
                GC.WaitForPendingFinalizers();
                GC.Collect();

                // Assert - check handle count
                process.Refresh();
                var finalHandles = process.HandleCount;
                var handleGrowth = finalHandles - initialHandles;

                Logger.Info("StressTests", $"File handle check - initial: {initialHandles}, final: {finalHandles}, growth: {handleGrowth}");

                // Shouldn't have more than 10 new handles (some variance is normal)
                Assert.IsTrue(handleGrowth < 10,
                    $"Possible file handle leak: handle count grew by {handleGrowth} (initial: {initialHandles}, final: {finalHandles})");
            }
            finally
            {
                try
                {
                    if (File.Exists(logPath))
                        File.Delete(logPath);
                }
                catch { }
            }
        }

        [TestMethod]
        [TestCategory("Stress")]
        public void ConcurrentLogging_NoCorruption()
        {
            // Arrange
            var logPath = Path.Combine(Path.GetTempPath(), $"concurrent_stress_{Guid.NewGuid()}.log");
            Logger.Initialize(logPath);
            var threads = 10;
            var messagesPerThread = 50;
            var tasks = new System.Collections.Generic.List<Task>();

            // Act - concurrent logging from multiple threads
            for (var t = 0; t < threads; t++)
            {
                var threadId = t;
                tasks.Add(Task.Run(() =>
                {
                    for (var i = 0; i < messagesPerThread; i++)
                    {
                        Logger.Info("StressTests", $"Thread {threadId} - Message {i}");
                    }
                }));
            }

            Task.WaitAll(tasks.ToArray());
            Logger.Flush(); // Ensure all async writes complete

            // Wait a bit more for channel writer to flush to disk
            Thread.Sleep(500);

            // Get the actual log path (Logger may have already been initialized)
            var actualLogPath = Logger.LogPath;

            // Assert - verify log file integrity
            try
            {
                var lines = File.ReadAllLines(actualLogPath);
                var expectedMessages = threads * messagesPerThread;
                var actualMessages = lines.Length;

                Logger.Info("StressTests", $"Concurrent logging - expected: {expectedMessages}, actual: {actualMessages}");

                // Check that we got most messages (some loss is acceptable under extreme stress)
                Assert.IsTrue(actualMessages >= expectedMessages * 0.9,
                    $"Too many log messages lost. Expected ~{expectedMessages}, got {actualMessages}");

                // Check for corruption (no lines should be partial)
                foreach (var line in lines)
                {
                    Assert.IsTrue(line.StartsWith("[") || line.Trim().Length == 0,
                        $"Corrupted log line detected: {line.Substring(0, Math.Min(50, line.Length))}");
                }
            }
            finally
            {
                try
                {
                    if (File.Exists(actualLogPath))
                        File.Delete(actualLogPath);
                }
                catch { }
            }
        }

        [TestMethod]
        [TestCategory("Stress")]
        public void ServiceToggle_RapidToggle_NoCrash()
        {
            // Arrange
            var service = KeepAwakeService.Instance;
            var iterations = 100;

            // Act - rapid toggle
            for (var i = 0; i < iterations; i++)
            {
                service.Toggle();
            }

            // Assert - service should still be functional
            var status = service.GetStatusText();
            Assert.IsNotNull(status);
            Assert.IsTrue(status.Contains("Active") || status.Contains("Inactive"),
                $"Service status unexpected after rapid toggles: {status}");
        }

        private static double GetWorkingSetMB()
        {
            using var process = Process.GetCurrentProcess();
            return process.WorkingSet64 / (1024.0 * 1024.0);
        }
    }
}
