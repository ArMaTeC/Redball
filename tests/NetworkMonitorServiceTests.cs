using Microsoft.VisualStudio.TestTools.UnitTesting;
using Redball.UI.Services;

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
    }
}
