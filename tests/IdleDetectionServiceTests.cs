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
        public void IdleDetectionService_GetIdleMinutes_ReturnsNonNegativeValue()
        {
            // Arrange
            var service = new IdleDetectionService();

            // Act
            var result = service.GetIdleMinutes();

            // Assert - idle time should be >= 0
            Assert.IsTrue(result >= 0, "Idle minutes should be non-negative");
        }
    }
}
