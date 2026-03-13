using Microsoft.VisualStudio.TestTools.UnitTesting;
using Redball.UI.Services;
using System.IO;

namespace Redball.Tests
{
    [TestClass]
    public class CrashRecoveryServiceTests
    {
        [TestMethod]
        public void CrashRecoveryService_WasPreviousCrash_NoFlag_ReturnsFalse()
        {
            // Arrange - ensure no flag exists
            var flagPath = Path.Combine(AppContext.BaseDirectory, "Redball.crash.flag");
            if (File.Exists(flagPath))
            {
                File.Delete(flagPath);
            }

            // Act
            var result = CrashRecoveryService.WasPreviousCrash();

            // Assert
            Assert.IsFalse(result, "Should return false when no crash flag exists");
        }

        [TestMethod]
        public void CrashRecoveryService_SetCrashFlag_CreatesFlag()
        {
            // Arrange - clean up first
            var flagPath = Path.Combine(AppContext.BaseDirectory, "Redball.crash.flag");
            if (File.Exists(flagPath))
            {
                File.Delete(flagPath);
            }

            // Act
            CrashRecoveryService.SetCrashFlag();

            // Assert
            Assert.IsTrue(File.Exists(flagPath), "Crash flag file should exist after SetCrashFlag");

            // Cleanup
            CrashRecoveryService.ClearCrashFlag();
        }

        [TestMethod]
        public void CrashRecoveryService_ClearCrashFlag_RemovesFlag()
        {
            // Arrange - set flag first
            CrashRecoveryService.SetCrashFlag();
            var flagPath = Path.Combine(AppContext.BaseDirectory, "Redball.crash.flag");

            // Act
            CrashRecoveryService.ClearCrashFlag();

            // Assert
            Assert.IsFalse(File.Exists(flagPath), "Crash flag file should not exist after ClearCrashFlag");
        }

        [TestMethod]
        public void CrashRecoveryService_WasPreviousCrash_WithFlag_ReturnsTrue()
        {
            // Arrange - set flag
            CrashRecoveryService.SetCrashFlag();

            // Act
            var result = CrashRecoveryService.WasPreviousCrash();

            // Assert
            Assert.IsTrue(result, "Should return true when crash flag exists");

            // Cleanup
            CrashRecoveryService.ClearCrashFlag();
        }

        [TestMethod]
        public void CrashRecoveryService_CheckAndRecover_NoFlag_ReturnsFalse()
        {
            // Arrange - ensure no flag
            CrashRecoveryService.ClearCrashFlag();

            // Act
            var result = CrashRecoveryService.CheckAndRecover();

            // Assert
            Assert.IsFalse(result, "Should return false when no crash to recover from");
        }

        [TestMethod]
        public void CrashRecoveryService_CheckAndRecover_WithFlag_ReturnsTrue()
        {
            // Arrange - set flag
            CrashRecoveryService.SetCrashFlag();

            // Act
            var result = CrashRecoveryService.CheckAndRecover();

            // Assert
            Assert.IsTrue(result, "Should return true when recovering from crash");
        }
    }
}
