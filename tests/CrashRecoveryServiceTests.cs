using Microsoft.VisualStudio.TestTools.UnitTesting;
using Redball.UI.Services;
using System.IO;

namespace Redball.Tests
{
    [TestClass]
    public class CrashRecoveryServiceTests
    {
        private string _originalFlagPath = string.Empty;
        private string _tempFlagPath = string.Empty;

        [TestInitialize]
        public void Setup()
        {
            _originalFlagPath = CrashRecoveryService.CrashFlagPath;
            _tempFlagPath = Path.Combine(Path.GetTempPath(), "Redball_Tests", "Redball.crash.flag");
            CrashRecoveryService.CrashFlagPath = _tempFlagPath;
            
            // Ensure clean start
            if (File.Exists(_tempFlagPath)) File.Delete(_tempFlagPath);
            var dir = Path.GetDirectoryName(_tempFlagPath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);
        }

        [TestCleanup]
        public void Cleanup()
        {
            if (File.Exists(_tempFlagPath)) File.Delete(_tempFlagPath);
            CrashRecoveryService.CrashFlagPath = _originalFlagPath;
        }

        [TestMethod]
        public void CrashRecoveryService_WasPreviousCrash_NoFlag_ReturnsFalse()
        {
            // Arrange - already cleaned in Setup
            
            // Act
            var result = CrashRecoveryService.WasPreviousCrash();

            // Assert
            Assert.IsFalse(result, "Should return false when no crash flag exists");
        }

        [TestMethod]
        public void CrashRecoveryService_SetCrashFlag_CreatesFlag()
        {
            // Act
            CrashRecoveryService.SetCrashFlag();

            // Assert
            Assert.IsTrue(File.Exists(_tempFlagPath), "Crash flag file should exist after SetCrashFlag");
        }

        [TestMethod]
        public void CrashRecoveryService_ClearCrashFlag_RemovesFlag()
        {
            // Arrange
            CrashRecoveryService.SetCrashFlag();

            // Act
            CrashRecoveryService.ClearCrashFlag();

            // Assert
            Assert.IsFalse(File.Exists(_tempFlagPath), "Crash flag file should not exist after ClearCrashFlag");
        }

        [TestMethod]
        public void CrashRecoveryService_WasPreviousCrash_WithFlag_ReturnsTrue()
        {
            // Arrange
            CrashRecoveryService.SetCrashFlag();

            // Act
            var result = CrashRecoveryService.WasPreviousCrash();

            // Assert
            Assert.IsTrue(result, "Should return true when crash flag exists");
        }

        [TestMethod]
        public void CrashRecoveryService_CheckAndRecover_NoFlag_ReturnsFalse()
        {
            // Act
            var result = CrashRecoveryService.CheckAndRecover();

            // Assert
            Assert.IsFalse(result, "Should return false when no crash to recover from");
        }

        [TestMethod]
        public void CrashRecoveryService_CheckAndRecover_WithFlag_ReturnsTrue()
        {
            // Arrange
            CrashRecoveryService.SetCrashFlag();

            // Act
            var result = CrashRecoveryService.CheckAndRecover();

            // Assert
            Assert.IsTrue(result, "Should return true when recovering from crash");
        }
    }
}
