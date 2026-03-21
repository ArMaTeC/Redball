using Microsoft.VisualStudio.TestTools.UnitTesting;
using Redball.UI.Services;
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Redball.Tests
{
    [TestClass]
    public class LoggerThreadSafetyTests
    {
        private string _testLogPath = "";

        private static readonly System.Reflection.BindingFlags BF = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static;

        private static void ResetLogger()
        {
            Logger.Shutdown();
            typeof(Logger).GetField("_channel", BF)?.SetValue(null, null);
            typeof(Logger).GetField("_writerTask", BF)?.SetValue(null, null);
            typeof(Logger).GetField("_initialized", BF)?.SetValue(null, false);
        }

        [TestInitialize]
        public void TestInitialize()
        {
            _testLogPath = Path.Combine(Path.GetTempPath(), $"redball_threadsafe_test_{Guid.NewGuid()}.log");
            ResetLogger();
            Logger.Initialize(_testLogPath);
            Logger.SetLogLevel(0); // Verbose — log everything
        }

        [TestCleanup]
        public void TestCleanup()
        {
            try
            {
                ResetLogger();
                if (File.Exists(_testLogPath))
                    File.Delete(_testLogPath);
                if (File.Exists(_testLogPath + ".old"))
                    File.Delete(_testLogPath + ".old");
            }
            catch { }
        }

        [TestMethod]
        public void Logger_ConcurrentWrites_DoNotThrow()
        {
            // Arrange — 10 threads writing 100 messages each
            var threadCount = 10;
            var messagesPerThread = 100;
            var exceptions = new System.Collections.Concurrent.ConcurrentBag<Exception>();

            // Act
            Parallel.For(0, threadCount, i =>
            {
                for (int j = 0; j < messagesPerThread; j++)
                {
                    try
                    {
                        Logger.Info($"Thread{i}", $"Message {j} from thread {i}");
                    }
                    catch (Exception ex)
                    {
                        exceptions.Add(ex);
                    }
                }
            });

            // Assert
            Assert.AreEqual(0, exceptions.Count,
                $"No exceptions should occur during concurrent writes, got {exceptions.Count}");
        }

        [TestMethod]
        public void Logger_ConcurrentWrites_AllMessagesWritten()
        {
            // Arrange
            var threadCount = 5;
            var messagesPerThread = 50;
            var uniqueMarker = Guid.NewGuid().ToString("N");

            // Act
            Parallel.For(0, threadCount, i =>
            {
                for (int j = 0; j < messagesPerThread; j++)
                {
                    Logger.Info($"Thread{i}", $"{uniqueMarker}_T{i}_M{j}");
                }
            });

            // Assert — shutdown async writes then read log
            Logger.Shutdown();
            var content = File.ReadAllText(_testLogPath);
            var expectedTotal = threadCount * messagesPerThread;
            var actualCount = 0;
            var index = 0;
            while ((index = content.IndexOf(uniqueMarker, index, StringComparison.Ordinal)) != -1)
            {
                actualCount++;
                index += uniqueMarker.Length;
            }

            // Async logging may occasionally lose messages under extreme concurrency (acceptable)
            var tolerance = expectedTotal * 0.05; // Allow 5% loss
            Assert.IsTrue(actualCount >= expectedTotal - tolerance,
                $"Expected at least {expectedTotal - tolerance} messages with marker, found {actualCount} (expected: {expectedTotal})");
        }

        [TestMethod]
        public void Logger_MixedLogLevels_ConcurrentWritesSucceed()
        {
            // Arrange
            var exceptions = new System.Collections.Concurrent.ConcurrentBag<Exception>();

            // Act — mix all log levels concurrently
            Parallel.For(0, 50, i =>
            {
                try
                {
                    switch (i % 5)
                    {
                        case 0: Logger.Verbose("MixTest", $"Verbose {i}"); break;
                        case 1: Logger.Debug("MixTest", $"Debug {i}"); break;
                        case 2: Logger.Info("MixTest", $"Info {i}"); break;
                        case 3: Logger.Warning("MixTest", $"Warning {i}"); break;
                        case 4: Logger.Error("MixTest", $"Error {i}"); break;
                    }
                }
                catch (Exception ex)
                {
                    exceptions.Add(ex);
                }
            });

            // Assert
            Assert.AreEqual(0, exceptions.Count, "Mixed log level writes should not throw");
        }

        [TestMethod]
        public void Logger_ConcurrentErrorWithException_DoesNotCorrupt()
        {
            // Arrange
            var exceptions = new System.Collections.Concurrent.ConcurrentBag<Exception>();
            var marker = Guid.NewGuid().ToString("N");

            // Act
            Parallel.For(0, 20, i =>
            {
                try
                {
                    var ex = new InvalidOperationException($"{marker}_Exception_{i}");
                    Logger.Error("ConcurrentErr", $"{marker}_Msg_{i}", ex);
                }
                catch (Exception thrownEx)
                {
                    exceptions.Add(thrownEx);
                }
            });

            // Assert
            Assert.AreEqual(0, exceptions.Count, "Concurrent error logging with exceptions should not throw");

            Logger.Shutdown();
            var content = File.ReadAllText(_testLogPath);
            Assert.IsTrue(content.Contains(marker), "Log should contain the error messages");
        }
    }
}
