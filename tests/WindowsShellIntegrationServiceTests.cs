using Microsoft.VisualStudio.TestTools.UnitTesting;
using Redball.UI.WPF.Services;

namespace Redball.Tests
{
    [TestClass]
    public class WindowsShellIntegrationServiceTests
    {
        [TestMethod]
        public void Instance_Singleton_ReturnsSameInstance()
        {
            // Act
            var instance1 = WindowsShellIntegrationService.Instance;
            var instance2 = WindowsShellIntegrationService.Instance;

            // Assert
            Assert.IsNotNull(instance1);
            Assert.AreSame(instance1, instance2);
        }

        [TestMethod]
        public void AppId_ReturnsExpectedValue()
        {
            // Arrange
            var service = WindowsShellIntegrationService.Instance;

            // Act
            var appId = service.AppId;

            // Assert
            Assert.AreEqual("ArMaTeC.Redball", appId);
        }

        [TestMethod]
        public void RegisterAll_DoesNotThrow()
        {
            // Arrange
            var service = WindowsShellIntegrationService.Instance;

            // Act & Assert - Should not throw (may fail on non-Windows or without permissions)
            var result = service.RegisterAll();
            Assert.IsInstanceOfType<bool>(result);
        }

        [TestMethod]
        public void UnregisterAll_DoesNotThrow()
        {
            // Arrange
            var service = WindowsShellIntegrationService.Instance;

            // Act & Assert - Should not throw
            var result = service.UnregisterAll();
            Assert.IsInstanceOfType<bool>(result);
        }

        [TestMethod]
        public void RegisterStartupTask_DoesNotThrow()
        {
            // Arrange
            var service = WindowsShellIntegrationService.Instance;

            // Act & Assert - Should not throw
            service.RegisterStartupTask();
        }

        [TestMethod]
        public void UnregisterStartupTask_DoesNotThrow()
        {
            // Arrange
            var service = WindowsShellIntegrationService.Instance;

            // Act & Assert - Should not throw
            service.UnregisterStartupTask();
        }

        [TestMethod]
        public void IsStartupEnabled_ReturnsBoolean()
        {
            // Arrange
            var service = WindowsShellIntegrationService.Instance;

            // Act
            var result = service.IsStartupEnabled();

            // Assert
            Assert.IsInstanceOfType<bool>(result);
        }

        [TestMethod]
        public void RegisterJumpList_DoesNotThrow()
        {
            // Arrange
            var service = WindowsShellIntegrationService.Instance;

            // Act & Assert - Should not throw
            service.RegisterJumpList();
        }

        [TestMethod]
        public void UnregisterJumpList_DoesNotThrow()
        {
            // Arrange
            var service = WindowsShellIntegrationService.Instance;

            // Act & Assert - Should not throw
            service.UnregisterJumpList();
        }

        [TestMethod]
        public void GetDefaultJumpListTasks_ReturnsTasks()
        {
            // Arrange
            var service = WindowsShellIntegrationService.Instance;

            // Act
            var tasks = service.GetDefaultJumpListTasks();

            // Assert
            Assert.IsNotNull(tasks);
            Assert.IsTrue(tasks.Count > 0, "Should have default jump list tasks");

            foreach (var task in tasks)
            {
                Assert.IsFalse(string.IsNullOrWhiteSpace(task.Title));
                Assert.IsFalse(string.IsNullOrWhiteSpace(task.Description));
                Assert.IsFalse(string.IsNullOrWhiteSpace(task.Arguments));
            }
        }

        [TestMethod]
        public void JumpListTask_Properties_SetCorrectly()
        {
            // Arrange & Act
            var task = new JumpListTask
            {
                Title = "Test Task",
                Description = "Test Description",
                Arguments = "--test",
                IconPath = @"C:\Test\icon.ico",
                IconIndex = 0
            };

            // Assert
            Assert.AreEqual("Test Task", task.Title);
            Assert.AreEqual("Test Description", task.Description);
            Assert.AreEqual("--test", task.Arguments);
            Assert.AreEqual(@"C:\Test\icon.ico", task.IconPath);
            Assert.AreEqual(0, task.IconIndex);
        }

        [TestMethod]
        public void UriProtocolConfig_Properties_SetCorrectly()
        {
            // Arrange & Act
            var config = new UriProtocolConfig
            {
                Scheme = "myapp",
                DisplayName = "My Application",
                ExecutablePath = @"C:\Program Files\MyApp\app.exe"
            };

            // Assert
            Assert.AreEqual("myapp", config.Scheme);
            Assert.AreEqual("My Application", config.DisplayName);
            Assert.AreEqual(@"C:\Program Files\MyApp\app.exe", config.ExecutablePath);
        }

        [TestMethod]
        public void UriProtocolConfig_DefaultValues()
        {
            // Arrange & Act
            var config = new UriProtocolConfig();

            // Assert - Check defaults
            Assert.AreEqual("redball", config.Scheme);
            Assert.AreEqual("Redball Application", config.DisplayName);
        }

        [TestMethod]
        public void RegisterUriProtocol_DoesNotThrow()
        {
            // Arrange
            var service = WindowsShellIntegrationService.Instance;

            // Act & Assert - Should not throw
            service.RegisterUriProtocol();
        }

        [TestMethod]
        public void UnregisterUriProtocol_DoesNotThrow()
        {
            // Arrange
            var service = WindowsShellIntegrationService.Instance;

            // Act & Assert - Should not throw
            service.UnregisterUriProtocol();
        }

        [TestMethod]
        public void RegisterToastActivator_DoesNotThrow()
        {
            // Arrange
            var service = WindowsShellIntegrationService.Instance;

            // Act & Assert - Should not throw
            service.RegisterToastActivator();
        }
    }
}
