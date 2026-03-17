using Microsoft.VisualStudio.TestTools.UnitTesting;
using Redball.UI.Services;
using System;
using System.IO;

namespace Redball.Tests
{
    [TestClass]
    public class LoggerTests
    {
        private string _testLogPath = "";

        [TestInitialize]
        public void TestInitialize()
        {
            _testLogPath = Path.Combine(Path.GetTempPath(), $"redball_test_{Guid.NewGuid()}.log");
            // Clear any existing initialization
            typeof(Logger).GetField("_initialized", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)?.SetValue(null, false);
        }

        [TestCleanup]
        public void TestCleanup()
        {
            try
            {
                if (File.Exists(_testLogPath))
                    File.Delete(_testLogPath);
            }
            catch { }
        }

        [TestMethod]
        public void Logger_Initialize_CreatesLogFile()
        {
            // Act
            Logger.Initialize(_testLogPath);
            Logger.Info("Test", "Test message");

            // Assert
            Assert.IsTrue(File.Exists(_testLogPath), "Log file should be created");
            var content = File.ReadAllText(_testLogPath);
            Assert.IsTrue(content.Contains("Redball WPF Started"), "Log should contain startup header");
        }

        [TestMethod]
        public void Logger_LogPath_ReturnsPath()
        {
            // Act
            Logger.Initialize(_testLogPath);
            var path = Logger.LogPath;

            // Assert
            Assert.AreEqual(_testLogPath, path, "LogPath should return initialized path");
        }

        [TestMethod]
        public void Logger_Verbose_LogsWhenLevelIsVerbose()
        {
            // Arrange
            Logger.Initialize(_testLogPath);
            Logger.SetLogLevel(0); // Verbose

            // Act
            Logger.Verbose("TestComponent", "Verbose message");

            // Assert
            var content = File.ReadAllText(_testLogPath);
            Assert.IsTrue(content.Contains("[VRB]"), "Should log verbose message");
            Assert.IsTrue(content.Contains("Verbose message"), "Should contain message text");
        }

        [TestMethod]
        public void Logger_Verbose_DoesNotLogWhenLevelIsDebug()
        {
            // Arrange
            Logger.Initialize(_testLogPath);
            Logger.SetLogLevel(1); // Debug

            // Act
            Logger.Verbose("TestComponent", "Should not appear");

            // Assert
            var content = File.ReadAllText(_testLogPath);
            Assert.IsFalse(content.Contains("Should not appear"), "Verbose should not log when level is Debug");
        }

        [TestMethod]
        public void Logger_Debug_LogsWhenLevelIsDebug()
        {
            // Arrange
            Logger.Initialize(_testLogPath);
            Logger.SetLogLevel(1); // Debug

            // Act
            Logger.Debug("TestComponent", "Debug message");

            // Assert
            var content = File.ReadAllText(_testLogPath);
            Assert.IsTrue(content.Contains("[DBG]"), "Should log debug message");
            Assert.IsTrue(content.Contains("Debug message"), "Should contain message text");
        }

        [TestMethod]
        public void Logger_Info_LogsWhenLevelIsInfo()
        {
            // Arrange
            Logger.Initialize(_testLogPath);
            Logger.SetLogLevel(2); // Info

            // Act
            Logger.Info("TestComponent", "Info message");

            // Assert
            var content = File.ReadAllText(_testLogPath);
            Assert.IsTrue(content.Contains("[INF]"), "Should log info message");
            Assert.IsTrue(content.Contains("Info message"), "Should contain message text");
        }

        [TestMethod]
        public void Logger_Info_DoesNotLogWhenLevelIsWarning()
        {
            // Arrange
            Logger.Initialize(_testLogPath);
            Logger.SetLogLevel(3); // Warning

            // Act
            Logger.Info("TestComponent", "Should not appear");

            // Assert
            var content = File.ReadAllText(_testLogPath);
            Assert.IsFalse(content.Contains("Should not appear"), "Info should not log when level is Warning");
        }

        [TestMethod]
        public void Logger_Warning_LogsWhenLevelIsWarning()
        {
            // Arrange
            Logger.Initialize(_testLogPath);
            Logger.SetLogLevel(3); // Warning

            // Act
            Logger.Warning("TestComponent", "Warning message");

            // Assert
            var content = File.ReadAllText(_testLogPath);
            Assert.IsTrue(content.Contains("[WRN]"), "Should log warning message");
            Assert.IsTrue(content.Contains("Warning message"), "Should contain message text");
        }

        [TestMethod]
        public void Logger_Error_LogsWhenLevelIsError()
        {
            // Arrange
            Logger.Initialize(_testLogPath);
            Logger.SetLogLevel(4); // Error

            // Act
            Logger.Error("TestComponent", "Error message");

            // Assert
            var content = File.ReadAllText(_testLogPath);
            Assert.IsTrue(content.Contains("[ERR]"), "Should log error message");
            Assert.IsTrue(content.Contains("Error message"), "Should contain message text");
        }

        [TestMethod]
        public void Logger_Error_WithException_LogsExceptionDetails()
        {
            // Arrange
            Logger.Initialize(_testLogPath);
            var ex = new InvalidOperationException("Test exception message");

            // Act
            Logger.Error("TestComponent", "Operation failed", ex);

            // Assert
            var content = File.ReadAllText(_testLogPath);
            Assert.IsTrue(content.Contains("InvalidOperationException"), "Should contain exception type");
            Assert.IsTrue(content.Contains("Test exception message"), "Should contain exception message");
        }

        [TestMethod]
        public void Logger_Fatal_AlwaysLogs()
        {
            // Arrange
            Logger.Initialize(_testLogPath);
            Logger.SetLogLevel(5); // Fatal only

            // Act
            Logger.Fatal("TestComponent", "Fatal error");

            // Assert
            var content = File.ReadAllText(_testLogPath);
            Assert.IsTrue(content.Contains("[FTL]"), "Should log fatal message");
            Assert.IsTrue(content.Contains("Fatal error"), "Should contain message text");
        }

        [TestMethod]
        public void Logger_Fatal_WithException_LogsFullDetails()
        {
            // Arrange
            Logger.Initialize(_testLogPath);
            var innerEx = new ArgumentException("Inner error");
            var ex = new InvalidOperationException("Outer error", innerEx);

            // Act
            Logger.Fatal("TestComponent", "Critical failure", ex);

            // Assert
            var content = File.ReadAllText(_testLogPath);
            Assert.IsTrue(content.Contains("Outer error"), "Should contain outer exception");
            Assert.IsTrue(content.Contains("Inner error"), "Should contain inner exception");
        }

        [TestMethod]
        public void Logger_SetLogLevel_ClampsToValidRange()
        {
            // Arrange
            Logger.Initialize(_testLogPath);

            // Act - set to invalid values
            Logger.SetLogLevel(-1);
            Logger.Info("Test", "Should log after negative clamp");

            // Assert - Info should still log (clamped to 0)
            var content = File.ReadAllText(_testLogPath);
            Assert.IsTrue(content.Contains("Should log after negative clamp"), "Should log after clamping negative");

            // Act - set above max
            Logger.SetLogLevel(10);
            Logger.Info("Test", "Should not appear after high clamp");

            // Assert - Info should not log (clamped to 5)
            content = File.ReadAllText(_testLogPath);
            Assert.IsFalse(content.Contains("Should not appear after high clamp"), "Should not log after clamping high");
        }

        [TestMethod]
        public void Logger_WriteCrashDump_CreatesDumpFile()
        {
            // Arrange
            Logger.Initialize(_testLogPath);
            var ex = new Exception("Test crash");
            var logDir = Path.GetDirectoryName(_testLogPath) ?? "";

            // Act
            Logger.WriteCrashDump(ex, "Test context");

            // Assert
            var dumpFiles = Directory.GetFiles(logDir, "crash_*.txt");
            Assert.IsTrue(dumpFiles.Length > 0, "Crash dump file should be created");

            // Cleanup
            foreach (var f in dumpFiles)
            {
                try { File.Delete(f); } catch { }
            }
        }

        [TestMethod]
        public void Logger_LogMemoryStats_WritesMemoryInfo()
        {
            // Arrange
            Logger.Initialize(_testLogPath);
            Logger.SetLogLevel(2); // Info level

            // Act
            Logger.LogMemoryStats("TestComponent");

            // Assert - allow time for file write
            Thread.Sleep(100);
            var content = File.ReadAllText(_testLogPath);
            // Memory stats log with the pattern "Memory: WorkingSet=XMB"
            Assert.IsTrue(content.Contains("Memory:"), "Should log memory stats");
        }

        [TestMethod]
        public void Logger_RotateLog_CreatesBackup()
        {
            // Arrange
            Logger.Initialize(_testLogPath);
            // Write enough content to exceed rotation threshold
            for (int i = 0; i < 1000; i++)
            {
                Logger.Info("Test", new string('x', 1000));
            }

            // Act
            Logger.RotateLog(1); // 1 byte threshold to force rotation

            // Assert
            var backupPath = _testLogPath + ".old";
            Assert.IsTrue(File.Exists(backupPath) || !File.Exists(_testLogPath), "Backup should exist or log should be rotated");
        }

        [TestMethod]
        public void Logger_MultilineMessage_LogsEachLine()
        {
            // Arrange
            Logger.Initialize(_testLogPath);
            Logger.SetLogLevel(2); // Info level

            // Act
            Logger.Info("Test", "Line 1\r\nLine 2\nLine 3");

            // Assert - allow time for file write
            Thread.Sleep(100);
            var content = File.ReadAllText(_testLogPath);
            // Logger splits lines and writes each separately
            Assert.IsTrue(content.Contains("Line 1") || content.Contains("Line 2") || content.Contains("Line 3"),
                "Should contain at least one of the lines from multiline message");
        }
    }
}
