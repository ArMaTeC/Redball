using Microsoft.VisualStudio.TestTools.UnitTesting;
using Redball.UI.Services;
using System;
using System.Collections.Generic;

namespace Redball.Tests
{
    [TestClass]
    public class NetworkMonitorServiceTests
    {
        [TestMethod]
        public void NetworkMonitorService_DefaultValues_AreCorrect()
        {
            // Arrange
            var service = new NetworkMonitorService();

            // Assert
            Assert.IsFalse(service.IsEnabled, "Should be disabled by default");
        }

        [TestMethod]
        public void NetworkMonitorService_IsEnabled_CanBeToggled()
        {
            // Arrange
            var service = new NetworkMonitorService();

            // Act
            service.IsEnabled = true;

            // Assert
            Assert.IsTrue(service.IsEnabled, "Should be enabled after toggle");
        }

        [TestMethod]
        public void NetworkMonitorService_IsEnabled_CanBeDisabled()
        {
            // Arrange
            var service = new NetworkMonitorService();
            service.IsEnabled = true;

            // Act
            service.IsEnabled = false;

            // Assert
            Assert.IsFalse(service.IsEnabled, "Should be disabled after toggle off");
        }

        [TestMethod]
        public void NetworkMonitorService_IsConnected_ReturnsBoolean()
        {
            // Arrange
            var service = new NetworkMonitorService();

            // Act
            var result = service.IsConnected();

            // Assert - just verify it returns a boolean without throwing
            // Result depends on actual network state, so we just check type
            Assert.IsInstanceOfType(result, typeof(bool), "Should return a boolean value");
        }

        [TestMethod]
        public void NetworkMonitorService_IsConnected_DoesNotThrow()
        {
            // Arrange
            var service = new NetworkMonitorService();

            // Act & Assert
            try
            {
                var result = service.IsConnected();
                Assert.IsTrue(true, "IsConnected should not throw");
            }
            catch (Exception ex)
            {
                Assert.Fail($"IsConnected threw exception: {ex.Message}");
            }
        }

        [TestMethod]
        public void NetworkMonitorService_CheckAndUpdate_WhenDisabled_DoesNotThrow()
        {
            // Arrange
            var service = new NetworkMonitorService();
            service.IsEnabled = false;
            var keepAwake = KeepAwakeService.Instance;
            var wasActive = keepAwake.IsActive;
            keepAwake.SetActive(false);

            try
            {
                // Act & Assert
                service.CheckAndUpdate(keepAwake);
                Assert.IsTrue(true, "CheckAndUpdate should not throw when disabled");
            }
            finally
            {
                keepAwake.SetActive(wasActive);
            }
        }
    }
}
