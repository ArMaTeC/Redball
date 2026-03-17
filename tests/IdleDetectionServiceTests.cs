using Microsoft.VisualStudio.TestTools.UnitTesting;
using Redball.UI.Services;

namespace Redball.Tests
{
    [TestClass]
    public class IdleDetectionServiceTests
    {
        [TestMethod]
        public void IdleDetectionService_DefaultValues_AreCorrect()
        {
            // Arrange
            var service = new IdleDetectionService();

            // Assert
            Assert.IsFalse(service.IsEnabled, "Should be disabled by default");
            Assert.AreEqual(30, service.ThresholdMinutes, "Default threshold should be 30 minutes");
        }

        [TestMethod]
        public void IdleDetectionService_ThresholdMinutes_CanBeSet()
        {
            // Arrange
            var service = new IdleDetectionService();

            // Act
            service.ThresholdMinutes = 15;

            // Assert
            Assert.AreEqual(15, service.ThresholdMinutes, "Threshold should be settable");
        }

        [TestMethod]
        public void IdleDetectionService_ThresholdMinutes_ZeroIsValid()
        {
            // Arrange
            var service = new IdleDetectionService();

            // Act
            service.ThresholdMinutes = 0;

            // Assert
            Assert.AreEqual(0, service.ThresholdMinutes, "Zero should be valid threshold");
        }

        [TestMethod]
        public void IdleDetectionService_IsEnabled_CanBeToggled()
        {
            // Arrange
            var service = new IdleDetectionService();

            // Act
            service.IsEnabled = true;

            // Assert
            Assert.IsTrue(service.IsEnabled, "Should be enabled after toggle");
        }

        [TestMethod]
        public void IdleDetectionService_IsEnabled_CanBeDisabled()
        {
            // Arrange
            var service = new IdleDetectionService();
            service.IsEnabled = true;

            // Act
            service.IsEnabled = false;

            // Assert
            Assert.IsFalse(service.IsEnabled, "Should be disabled after toggle off");
        }

        [TestMethod]
        public void IdleDetectionService_GetIdleMinutes_ReturnsNonNegativeValue()
        {
            // Arrange
            var service = new IdleDetectionService();

            // Act
            var result = service.GetIdleMinutes();

            // Assert - idle time should be >= 0
            Assert.IsTrue(result >= 0, "Idle minutes should be non-negative");
        }

        [TestMethod]
        public void IdleDetectionService_GetIdleMinutes_WhenCalledMultipleTimes_ReturnsConsistentResults()
        {
            // Arrange
            var service = new IdleDetectionService();

            // Act
            var result1 = service.GetIdleMinutes();
            var result2 = service.GetIdleMinutes();

            // Assert - results should be close (within 1 second of each other)
            var difference = Math.Abs(result1 - result2);
            Assert.IsTrue(difference < 0.1, "Consecutive calls should return similar values");
        }

        [TestMethod]
        public void IdleDetectionService_CheckAndUpdate_WhenDisabled_DoesNotThrow()
        {
            // Arrange
            var service = new IdleDetectionService();
            service.IsEnabled = false;
            var keepAwake = KeepAwakeService.Instance;
            var wasActive = keepAwake.IsActive;
            keepAwake.SetActive(false);

            try
            {
                // Act & Assert - should not throw
                service.CheckAndUpdate(keepAwake);
                Assert.IsTrue(true, "CheckAndUpdate should not throw when disabled");
            }
            finally
            {
                keepAwake.SetActive(wasActive);
            }
        }

        [TestMethod]
        public void IdleDetectionService_CheckAndUpdate_WhenEnabledAndNotIdle_DoesNotThrow()
        {
            // Arrange
            var service = new IdleDetectionService();
            service.IsEnabled = true;
            service.ThresholdMinutes = 60; // High threshold so we're not idle
            var keepAwake = KeepAwakeService.Instance;
            var wasActive = keepAwake.IsActive;
            keepAwake.SetActive(false);

            try
            {
                // Act & Assert - should not throw
                service.CheckAndUpdate(keepAwake);
                Assert.IsTrue(true, "CheckAndUpdate should not throw");
            }
            finally
            {
                keepAwake.SetActive(wasActive);
            }
        }
    }
}
