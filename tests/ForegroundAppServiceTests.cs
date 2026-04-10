using Microsoft.VisualStudio.TestTools.UnitTesting;
using Redball.UI.Services;

namespace Redball.Tests
{
    [TestClass]
    public class ForegroundAppServiceTests
    {
        [TestCleanup]
        public void TestCleanup()
        {
            // Ensure service is stopped after each test
            ForegroundAppService.Instance.Stop();
        }

        [TestMethod]
        public void Instance_Singleton_ReturnsSameInstance()
        {
            // Act
            var instance1 = ForegroundAppService.Instance;
            var instance2 = ForegroundAppService.Instance;

            // Assert
            Assert.IsNotNull(instance1);
            Assert.AreSame(instance1, instance2);
        }

        [TestMethod]
        public void Constructor_InitializesNotEnabled()
        {
            // Arrange & Act
            var service = ForegroundAppService.Instance;

            // Assert
            Assert.IsFalse(service.IsEnabled);
        }

        [TestMethod]
        public void Start_ServiceBecomesEnabled()
        {
            // Arrange
            var service = ForegroundAppService.Instance;
            Assert.IsFalse(service.IsEnabled);

            // Act
            service.Start();

            // Assert
            Assert.IsTrue(service.IsEnabled);
        }

        [TestMethod]
        public void Start_MultipleCalls_DoesNotThrow()
        {
            // Arrange
            var service = ForegroundAppService.Instance;

            // Act
            service.Start();
            service.Start();
            service.Start();

            // Assert
            Assert.IsTrue(service.IsEnabled);
        }

        [TestMethod]
        public void Stop_ServiceBecomesDisabled()
        {
            // Arrange
            var service = ForegroundAppService.Instance;
            service.Start();
            Assert.IsTrue(service.IsEnabled);

            // Act
            service.Stop();

            // Assert
            Assert.IsFalse(service.IsEnabled);
        }

        [TestMethod]
        public void Stop_WhenNotStarted_DoesNotThrow()
        {
            // Arrange
            var service = ForegroundAppService.Instance;
            Assert.IsFalse(service.IsEnabled);

            // Act & Assert
            service.Stop();
            Assert.IsFalse(service.IsEnabled);
        }

        [TestMethod]
        public void CurrentForegroundApp_InitiallyEmpty()
        {
            // Arrange
            var service = ForegroundAppService.Instance;

            // Act
            var app = service.CurrentForegroundApp;

            // Assert
            Assert.AreEqual("", app);
        }

        [TestMethod]
        public void CurrentForegroundApp_AfterStop_IsCleared()
        {
            // Arrange
            var service = ForegroundAppService.Instance;
            service.Start();

            // Act
            service.Stop();
            var app = service.CurrentForegroundApp;

            // Assert
            Assert.AreEqual("", app);
        }

        [TestMethod]
        public void ForegroundAppChanged_Event_CanSubscribe()
        {
            // Arrange
            var service = ForegroundAppService.Instance;
            var eventFired = false;
            EventHandler<string> handler = (sender, app) => { eventFired = true; };

            // Act
            service.ForegroundAppChanged += handler;
            service.ForegroundAppChanged -= handler;

            // Assert
            Assert.IsFalse(eventFired);
        }

        [TestMethod]
        public void StartStop_Cycle_WorksCorrectly()
        {
            // Arrange
            var service = ForegroundAppService.Instance;

            // Act & Assert - Multiple cycles
            for (int i = 0; i < 3; i++)
            {
                Assert.IsFalse(service.IsEnabled);
                service.Start();
                Assert.IsTrue(service.IsEnabled);
                service.Stop();
                Assert.IsFalse(service.IsEnabled);
            }
        }

        [TestMethod]
        public void Service_ConfigIntegration_KeepAwakeApps_IsString()
        {
            // Arrange
            var config = ConfigService.Instance.Config;

            // Act
            var apps = config.KeepAwakeApps;

            // Assert
            Assert.IsInstanceOfType<string>(apps);
        }

        [TestMethod]
        public void Service_ConfigIntegration_PauseApps_IsString()
        {
            // Arrange
            var config = ConfigService.Instance.Config;

            // Act
            var apps = config.PauseApps;

            // Assert
            Assert.IsInstanceOfType<string>(apps);
        }
    }
}
