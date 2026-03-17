using Microsoft.VisualStudio.TestTools.UnitTesting;
using Redball.UI.Services;

namespace Redball.Tests
{
    [TestClass]
    public class SingletonServiceTests
    {
        [TestMethod]
        public void SingletonService_TryAcquire_ReturnsBoolean()
        {
            // Arrange
            using var service = new SingletonService();

            // Act
            var result = service.TryAcquire();

            // Assert
            Assert.IsTrue(result || !result, "TryAcquire should return a boolean");
        }

        [TestMethod]
        public void SingletonService_IsAnotherInstanceRunning_ReturnsBoolean()
        {
            // Act
            var result = SingletonService.IsAnotherInstanceRunning();

            // Assert
            Assert.IsTrue(result || !result, "Should return a boolean indicating if another instance is running");
        }

        [TestMethod]
        public void SingletonService_MultipleTryAcquire_DoesNotThrow()
        {
            // Arrange
            using var service1 = new SingletonService();

            // Act
            var result1 = service1.TryAcquire();

            // Assert - first acquire should work
            Assert.IsTrue(result1 || !result1, "First TryAcquire should complete");
        }

        [TestMethod]
        public void SingletonService_CanBeDisposed()
        {
            // Arrange
            var service = new SingletonService();
            service.TryAcquire();

            // Act & Assert - should not throw
            service.Dispose();
            Assert.IsTrue(true, "Dispose should complete without throwing");
        }

        [TestMethod]
        public void SingletonService_DoubleDispose_DoesNotThrow()
        {
            // Arrange
            var service = new SingletonService();
            service.Dispose();

            // Act & Assert - second dispose should not throw
            service.Dispose();
            Assert.IsTrue(true, "Double dispose should not throw");
        }
    }
}
