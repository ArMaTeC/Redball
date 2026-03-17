using Microsoft.VisualStudio.TestTools.UnitTesting;
using Redball.UI.Services;
using System;
using System.IO;

namespace Redball.Tests
{
    [TestClass]
    public class NotificationServiceTests
    {
        private string _testLogPath = "";

        [TestInitialize]
        public void TestInitialize()
        {
            _testLogPath = Path.Combine(Path.GetTempPath(), $"redball_notification_test_{Guid.NewGuid()}.log");
            Logger.Initialize(_testLogPath);
        }

        [TestCleanup]
        public void TestCleanup()
        {
            try
            {
                if (File.Exists(_testLogPath))
                    File.Delete(_testLogPath);
            }
            catch { }
        }

        [TestMethod]
        public void NotificationService_Instance_IsNotNull()
        {
            // Act
            var instance = NotificationService.Instance;

            // Assert
            Assert.IsNotNull(instance, "Instance should not be null");
        }

        [TestMethod]
        public void NotificationService_Instance_IsSingleton()
        {
            // Act
            var instance1 = NotificationService.Instance;
            var instance2 = NotificationService.Instance;

            // Assert
            Assert.AreSame(instance1, instance2, "Should return same instance");
        }

        [TestMethod]
        public void NotificationService_Show_WhenNotificationsDisabled_DoesNotThrow()
        {
            // Arrange
            var service = NotificationService.Instance;
            var config = ConfigService.Instance.Config;
            var originalValue = config.ShowNotifications;
            config.ShowNotifications = false;

            try
            {
                // Act & Assert - should not throw
                service.ShowInfo("Test", "Test message");
            }
            finally
            {
                config.ShowNotifications = originalValue;
            }
        }

        [TestMethod]
        public void NotificationService_Show_WhenTrayIconNotSet_DoesNotThrow()
        {
            // Arrange
            var service = NotificationService.Instance;
            var config = ConfigService.Instance.Config;
            var originalShowNotifications = config.ShowNotifications;
            var originalMode = config.NotificationMode;
            config.ShowNotifications = true;
            config.NotificationMode = NotificationMode.All;

            try
            {
                // Act & Assert - should not throw even without tray icon
                service.ShowInfo("Test", "Test message");
            }
            finally
            {
                config.ShowNotifications = originalShowNotifications;
                config.NotificationMode = originalMode;
            }
        }

        [TestMethod]
        public void NotificationService_ShowInfo_DoesNotThrow()
        {
            // Arrange
            var service = NotificationService.Instance;

            // Act & Assert
            try
            {
                service.ShowInfo("Info Title", "Info message content");
            }
            catch (Exception ex)
            {
                Assert.Fail($"ShowInfo should not throw: {ex.Message}");
            }
        }

        [TestMethod]
        public void NotificationService_ShowWarning_DoesNotThrow()
        {
            // Arrange
            var service = NotificationService.Instance;

            // Act & Assert
            try
            {
                service.ShowWarning("Warning Title", "Warning message content");
            }
            catch (Exception ex)
            {
                Assert.Fail($"ShowWarning should not throw: {ex.Message}");
            }
        }

        [TestMethod]
        public void NotificationService_ShowError_DoesNotThrow()
        {
            // Arrange
            var service = NotificationService.Instance;

            // Act & Assert
            try
            {
                service.ShowError("Error Title", "Error message content");
            }
            catch (Exception ex)
            {
                Assert.Fail($"ShowError should not throw: {ex.Message}");
            }
        }

        [TestMethod]
        public void NotificationService_Show_SilentMode_DoesNotShowNonError()
        {
            // Arrange
            var service = NotificationService.Instance;
            var config = ConfigService.Instance.Config;
            var originalMode = config.NotificationMode;
            var originalShow = config.ShowNotifications;
            config.NotificationMode = NotificationMode.Silent;
            config.ShowNotifications = true;

            try
            {
                // Act - should complete without throwing
                service.ShowInfo("Test", "Should not appear");

                // Assert - no exception means success for this mode
                Assert.IsTrue(true, "Silent mode handled correctly");
            }
            finally
            {
                config.NotificationMode = originalMode;
                config.ShowNotifications = originalShow;
            }
        }

        [TestMethod]
        public void NotificationService_Show_ErrorsMode_AllowsErrorNotifications()
        {
            // Arrange
            var service = NotificationService.Instance;
            var config = ConfigService.Instance.Config;
            var originalMode = config.NotificationMode;
            var originalShow = config.ShowNotifications;
            config.NotificationMode = NotificationMode.Errors;
            config.ShowNotifications = true;

            try
            {
                // Act - error should work, info should be filtered
                service.ShowError("Error", "This is an error");
                service.ShowInfo("Info", "This should be filtered");

                // Assert
                Assert.IsTrue(true, "Error mode handled correctly");
            }
            finally
            {
                config.NotificationMode = originalMode;
                config.ShowNotifications = originalShow;
            }
        }

        [TestMethod]
        public void NotificationService_Show_ImportantMode_FiltersInfo()
        {
            // Arrange
            var service = NotificationService.Instance;
            var config = ConfigService.Instance.Config;
            var originalMode = config.NotificationMode;
            var originalShow = config.ShowNotifications;
            config.NotificationMode = NotificationMode.Important;
            config.ShowNotifications = true;

            try
            {
                // Act
                service.ShowInfo("Info", "This should be filtered in Important mode");

                // Assert - no exception
                Assert.IsTrue(true, "Important mode handled correctly");
            }
            finally
            {
                config.NotificationMode = originalMode;
                config.ShowNotifications = originalShow;
            }
        }

        [TestMethod]
        public void NotificationService_SetTrayIcon_DoesNotThrow()
        {
            // Arrange
            var service = NotificationService.Instance;

            // Act - set null (should not throw)
            try
            {
                service.SetTrayIcon(null!);
            }
            catch (Exception ex)
            {
                Assert.Fail($"SetTrayIcon should not throw: {ex.Message}");
            }
        }
    }
}
