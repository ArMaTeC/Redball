using Microsoft.VisualStudio.TestTools.UnitTesting;
using Redball.UI.Services;

namespace Redball.Tests
{
    [TestClass]
    public class BatteryMonitorServiceTests
    {
        [TestMethod]
        public void BatteryMonitorService_DefaultValues_AreCorrect()
        {
            // Arrange
            var service = new BatteryMonitorService();

            // Assert
            Assert.IsFalse(service.IsEnabled, "Should be disabled by default");
            Assert.AreEqual(20, service.Threshold, "Default threshold should be 20%");
        }

        [TestMethod]
        public void BatteryMonitorService_Threshold_CanBeSet()
        {
            // Arrange
            var service = new BatteryMonitorService();

            // Act
            service.Threshold = 30;

            // Assert
            Assert.AreEqual(30, service.Threshold, "Threshold should be settable");
        }

        [TestMethod]
        public void BatteryMonitorService_IsEnabled_CanBeToggled()
        {
            // Arrange
            var service = new BatteryMonitorService();

            // Act
            service.IsEnabled = true;

            // Assert
            Assert.IsTrue(service.IsEnabled, "Should be enabled after toggle");
        }

        [TestMethod]
        public void BatteryStatus_DefaultValues_AreCorrect()
        {
            // Arrange
            var status = new BatteryStatus();

            // Assert
            Assert.IsFalse(status.HasBattery, "Should not have battery by default");
            Assert.IsFalse(status.IsOnBattery, "Should not be on battery by default");
            Assert.AreEqual(0, status.ChargePercent, "Charge should be 0% by default");
        }

        [TestMethod]
        public void BatteryStatus_Properties_CanBeSet()
        {
            // Arrange
            var status = new BatteryStatus
            {
                HasBattery = true,
                IsOnBattery = true,
                ChargePercent = 75
            };

            // Assert
            Assert.IsTrue(status.HasBattery, "HasBattery should be settable");
            Assert.IsTrue(status.IsOnBattery, "IsOnBattery should be settable");
            Assert.AreEqual(75, status.ChargePercent, "ChargePercent should be settable");
        }
    }
}
