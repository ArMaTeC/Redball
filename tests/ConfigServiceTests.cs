using Microsoft.VisualStudio.TestTools.UnitTesting;
using Redball.UI.Services;

namespace Redball.Tests
{
    [TestClass]
    public class ConfigServiceTests
    {
        [TestMethod]
        public void ConfigService_Validate_ValidConfig_ReturnsEmpty()
        {
            // Arrange
            var service = ConfigService.Instance;
            service.Config.HeartbeatSeconds = 59;
            service.Config.DefaultDuration = 60;
            service.Config.BatteryThreshold = 20;

            // Act
            var errors = service.Validate();

            // Assert
            Assert.AreEqual(0, errors.Count, "Valid config should have no validation errors");
        }

        [TestMethod]
        public void ConfigService_Validate_InvalidHeartbeat_ReturnsError()
        {
            // Arrange
            var service = ConfigService.Instance;
            service.Config.HeartbeatSeconds = 5; // Too low

            // Act
            var errors = service.Validate();

            // Assert
            Assert.IsTrue(errors.Count > 0, "Should detect invalid heartbeat");
            Assert.IsTrue(errors[0].Contains("HeartbeatSeconds"), "Error should mention HeartbeatSeconds");
        }

        [TestMethod]
        public void ConfigService_Validate_InvalidDuration_ReturnsError()
        {
            // Arrange
            var service = ConfigService.Instance;
            service.Config.DefaultDuration = 0; // Too low

            // Act
            var errors = service.Validate();

            // Assert
            Assert.IsTrue(errors.Count > 0, "Should detect invalid duration");
        }

        [TestMethod]
        public void ConfigService_Validate_InvalidBatteryThreshold_ReturnsError()
        {
            // Arrange
            var service = ConfigService.Instance;
            service.Config.BatteryThreshold = 100; // Too high

            // Act
            var errors = service.Validate();

            // Assert
            Assert.IsTrue(errors.Count > 0, "Should detect invalid battery threshold");
        }

        [TestMethod]
        public void ConfigService_Validate_InvalidTypeThingDelays_ReturnsError()
        {
            // Arrange
            var service = ConfigService.Instance;
            service.Config.TypeThingMinDelayMs = 100;
            service.Config.TypeThingMaxDelayMs = 50; // Less than min

            // Act
            var errors = service.Validate();

            // Assert
            Assert.IsTrue(errors.Count > 0, "Should detect invalid delay configuration");
        }
    }
}
