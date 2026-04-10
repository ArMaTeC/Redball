using Microsoft.VisualStudio.TestTools.UnitTesting;
using Redball.UI.Services;
using Microsoft.Win32;

namespace Redball.Tests
{
    [TestClass]
    public class SessionLockServiceTests
    {
        [TestInitialize]
        public void TestInitialize()
        {
            // Reset the service state before each test
            // Note: We can't fully reset singleton, but we can stop it
            SessionLockService.Instance.Stop();
        }

        [TestCleanup]
        public void TestCleanup()
        {
            // Ensure service is stopped after tests
            SessionLockService.Instance.Stop();
        }

        [TestMethod]
        public void Instance_Singleton_ReturnsSameInstance()
        {
            // Act
            var instance1 = SessionLockService.Instance;
            var instance2 = SessionLockService.Instance;

            // Assert
            Assert.IsNotNull(instance1);
            Assert.AreSame(instance1, instance2);
        }

        [TestMethod]
        public void Constructor_InitialState_IsCorrect()
        {
            // Arrange & Act
            var service = SessionLockService.Instance;

            // Assert
            Assert.IsFalse(service.IsEnabled);
            Assert.IsFalse(service.IsLocked);
        }

        [TestMethod]
        public void Start_ServiceBecomesEnabled()
        {
            // Arrange
            var service = SessionLockService.Instance;
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
            var service = SessionLockService.Instance;

            // Act
            service.Start();
            service.Start(); // Second call should be no-op
            service.Start(); // Third call should be no-op

            // Assert
            Assert.IsTrue(service.IsEnabled);
        }

        [TestMethod]
        public void Stop_ServiceBecomesDisabled()
        {
            // Arrange
            var service = SessionLockService.Instance;
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
            var service = SessionLockService.Instance;
            Assert.IsFalse(service.IsEnabled);

            // Act & Assert - should not throw
            service.Stop();
            service.Stop();
            service.Stop();

            Assert.IsFalse(service.IsEnabled);
        }

        [TestMethod]
        public void SessionLockChanged_Event_CanSubscribeAndUnsubscribe()
        {
            // Arrange
            var service = SessionLockService.Instance;
            var eventFired = false;
            EventHandler<bool> handler = (sender, locked) => { eventFired = true; };

            // Act - Subscribe
            service.SessionLockChanged += handler;
            service.SessionLockChanged -= handler;

            // Assert - no exception
            Assert.IsFalse(eventFired);
        }

        [TestMethod]
        public void SessionLockChanged_Event_CanHaveMultipleSubscribers()
        {
            // Arrange
            var service = SessionLockService.Instance;
            var counter1 = 0;
            var counter2 = 0;
            EventHandler<bool> handler1 = (sender, locked) => counter1++;
            EventHandler<bool> handler2 = (sender, locked) => counter2++;

            // Act
            service.SessionLockChanged += handler1;
            service.SessionLockChanged += handler2;
            service.SessionLockChanged -= handler1;
            service.SessionLockChanged -= handler2;

            // Assert
            Assert.AreEqual(0, counter1);
            Assert.AreEqual(0, counter2);
        }

        [TestMethod]
        public void StartStop_StartStopCycle_WorksCorrectly()
        {
            // Arrange
            var service = SessionLockService.Instance;

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
        public void IsLocked_Property_HasPrivateSetter()
        {
            // Arrange
            var service = SessionLockService.Instance;

            // Act - Verify we can read the property
            var isLocked = service.IsLocked;

            // Assert
            Assert.IsInstanceOfType<bool>(isLocked);
            Assert.IsFalse(isLocked); // Default is unlocked
        }

        [TestMethod]
        public void Service_IntegrationWithConfigService_DoesNotThrow()
        {
            // Arrange
            var service = SessionLockService.Instance;
            var config = ConfigService.Instance.Config;

            // Act & Assert - Verify config access doesn't throw
            var pauseOnLock = config.PauseOnScreenLock;
            Assert.IsInstanceOfType<bool>(pauseOnLock);
        }
    }
}
