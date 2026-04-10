using Microsoft.VisualStudio.TestTools.UnitTesting;
using Redball.UI.Services;

namespace Redball.Tests
{
    [TestClass]
    public class TemperatureMonitorServiceTests
    {
        [TestCleanup]
        public void TestCleanup()
        {
            // Clean up the service after tests
            TemperatureMonitorService.Instance.Dispose();
        }

        [TestMethod]
        public void Instance_Singleton_ReturnsSameInstance()
        {
            // Act
            var instance1 = TemperatureMonitorService.Instance;
            var instance2 = TemperatureMonitorService.Instance;

            // Assert
            Assert.IsNotNull(instance1);
            Assert.AreSame(instance1, instance2);
        }

        [TestMethod]
        public void Constructor_InitializesProperties()
        {
            // Arrange & Act
            var service = TemperatureMonitorService.Instance;

            // Assert
            Assert.IsNotNull(service);
            Assert.IsNotNull(service.LastError);
            Assert.IsNotNull(service.ActiveSensorName);
        }

        [TestMethod]
        public void CurrentCpuTemp_Property_CanRead()
        {
            // Arrange
            var service = TemperatureMonitorService.Instance;

            // Act
            var temp = service.CurrentCpuTemp;

            // Assert - Can be null (if sensors unavailable) or a valid temperature
            if (temp.HasValue)
            {
                Assert.IsTrue(temp.Value > -50 && temp.Value < 150, "Temperature should be in reasonable range");
            }
        }

        [TestMethod]
        public void IsOverThreshold_Property_CanRead()
        {
            // Arrange
            var service = TemperatureMonitorService.Instance;

            // Act
            var isOver = service.IsOverThreshold;

            // Assert
            Assert.IsInstanceOfType<bool>(isOver);
        }

        [TestMethod]
        public void LastError_Property_IsString()
        {
            // Arrange
            var service = TemperatureMonitorService.Instance;

            // Act
            var error = service.LastError;

            // Assert
            Assert.IsInstanceOfType<string>(error);
        }

        [TestMethod]
        public void ActiveSensorName_Property_IsString()
        {
            // Arrange
            var service = TemperatureMonitorService.Instance;

            // Act
            var sensorName = service.ActiveSensorName;

            // Assert
            Assert.IsInstanceOfType<string>(sensorName);
            Assert.IsFalse(string.IsNullOrWhiteSpace(sensorName));
        }

        [TestMethod]
        public void TemperatureUpdated_Event_CanSubscribeAndUnsubscribe()
        {
            // Arrange
            var service = TemperatureMonitorService.Instance;
            var eventFired = false;
            EventHandler<double> handler = (sender, temp) => { eventFired = true; };

            // Act - Subscribe and unsubscribe
            service.TemperatureUpdated += handler;
            service.TemperatureUpdated -= handler;

            // Assert - no exception
            Assert.IsFalse(eventFired);
        }

        [TestMethod]
        public void TemperatureUpdated_Event_CanHaveMultipleSubscribers()
        {
            // Arrange
            var service = TemperatureMonitorService.Instance;
            var counter1 = 0;
            var counter2 = 0;
            EventHandler<double> handler1 = (sender, temp) => counter1++;
            EventHandler<double> handler2 = (sender, temp) => counter2++;

            // Act
            service.TemperatureUpdated += handler1;
            service.TemperatureUpdated += handler2;
            service.TemperatureUpdated -= handler1;
            service.TemperatureUpdated -= handler2;

            // Assert
            Assert.AreEqual(0, counter1);
            Assert.AreEqual(0, counter2);
        }

        [TestMethod]
        public void Dispose_DoesNotThrow()
        {
            // Arrange
            var service = TemperatureMonitorService.Instance;

            // Act & Assert
            service.Dispose();
        }

        [TestMethod]
        public void Dispose_MultipleCalls_DoesNotThrow()
        {
            // Arrange
            var service = TemperatureMonitorService.Instance;

            // Act & Assert - Multiple disposes should be safe
            service.Dispose();
            service.Dispose();
            service.Dispose();
        }

        [TestMethod]
        public void Service_ConfigIntegration_ThermalThreshold_IsReasonable()
        {
            // Arrange
            var config = ConfigService.Instance.Config;

            // Act
            var threshold = config.ThermalThreshold;

            // Assert
            Assert.IsTrue(threshold > 50 && threshold < 120, "Thermal threshold should be in reasonable range (50-120°C)");
        }

        [TestMethod]
        public void Service_ConfigIntegration_ThermalProtectionEnabled_IsBoolean()
        {
            // Arrange
            var config = ConfigService.Instance.Config;

            // Act
            var enabled = config.ThermalProtectionEnabled;

            // Assert
            Assert.IsInstanceOfType<bool>(enabled);
        }
    }
}
