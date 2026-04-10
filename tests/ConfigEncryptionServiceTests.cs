using Microsoft.VisualStudio.TestTools.UnitTesting;
using Redball.UI.Services;
using System.Text;

namespace Redball.Tests
{
    [TestClass]
    public class ConfigEncryptionServiceTests
    {
        [TestMethod]
        public void Instance_Singleton_ReturnsSameInstance()
        {
            // Act
            var instance1 = ConfigEncryptionService.Instance;
            var instance2 = ConfigEncryptionService.Instance;

            // Assert
            Assert.IsNotNull(instance1);
            Assert.AreSame(instance1, instance2);
        }

        [TestMethod]
        public void IsTpmAvailable_ReturnsBoolean()
        {
            // Act
            var result = ConfigEncryptionService.IsTpmAvailable();

            // Assert
            // Result depends on hardware, but should not throw
            Assert.IsInstanceOfType<bool>(result);
        }

        [TestMethod]
        public void IsDpapiNgAvailable_ReturnsBoolean()
        {
            // Act
            var result = ConfigEncryptionService.IsDpapiNgAvailable();

            // Assert
            // Result depends on Windows version, but should not throw
            Assert.IsInstanceOfType<bool>(result);
        }

        [TestMethod]
        public void EncryptDecryptConfig_RoundTrip_PreservesConfig()
        {
            // Arrange
            var service = ConfigEncryptionService.Instance;
            var originalConfig = new RedballConfig
            {
                TypeThingEnabled = true,
                HeartbeatSeconds = 59,
                Theme = "Dark",
                PauseOnScreenLock = true,
                KeepAwakeApps = "chrome,firefox"
            };

            // Act
            var encrypted = service.EncryptConfig(originalConfig);
            var decrypted = service.DecryptConfig(encrypted);

            // Assert
            Assert.IsNotNull(decrypted);
            Assert.AreEqual(originalConfig.TypeThingEnabled, decrypted.TypeThingEnabled);
            Assert.AreEqual(originalConfig.HeartbeatSeconds, decrypted.HeartbeatSeconds);
            Assert.AreEqual(originalConfig.Theme, decrypted.Theme);
        }

        [TestMethod]
        public void EncryptConfig_MinimumTier_DoesNotThrow()
        {
            // Arrange
            var service = ConfigEncryptionService.Instance;
            var config = new RedballConfig { Theme = "Light" };

            // Act & Assert - EncryptionTier.Standard as minimum viable tier
            var encrypted = service.EncryptConfig(config, EncryptionTier.Standard);
            Assert.IsFalse(string.IsNullOrEmpty(encrypted));
        }

        [TestMethod]
        public void EncryptConfig_HighTier_DoesNotThrow()
        {
            // Arrange
            var service = ConfigEncryptionService.Instance;
            var config = new RedballConfig { Theme = "Dark" };

            // Act & Assert
            var encrypted = service.EncryptConfig(config, EncryptionTier.High);
            Assert.IsFalse(string.IsNullOrEmpty(encrypted));
        }

        [TestMethod]
        public void EncryptConfig_MaximumTier_DoesNotThrow()
        {
            // Arrange
            var service = ConfigEncryptionService.Instance;
            var config = new RedballConfig { Theme = "Auto" };

            // Act & Assert
            var encrypted = service.EncryptConfig(config, EncryptionTier.Maximum);
            Assert.IsFalse(string.IsNullOrEmpty(encrypted));
        }

        [TestMethod]
        public void DecryptConfig_InvalidData_ReturnsNull()
        {
            // Arrange
            var service = ConfigEncryptionService.Instance;
            var invalidData = "INVALID:NOT:ENCRYPTED:DATA";

            // Act
            var result = service.DecryptConfig(invalidData);

            // Assert
            Assert.IsNull(result);
        }

        [TestMethod]
        public void DecryptConfig_NullOrEmpty_ReturnsNull()
        {
            // Arrange
            var service = ConfigEncryptionService.Instance;

            // Act & Assert
            Assert.IsNull(service.DecryptConfig(null));
            Assert.IsNull(service.DecryptConfig(""));
            Assert.IsNull(service.DecryptConfig("   "));
        }

        [TestMethod]
        public void IsEncrypted_TrueForEncryptedPayload()
        {
            // Arrange
            var service = ConfigEncryptionService.Instance;
            var config = new RedballConfig { Theme = "Test" };
            var encrypted = service.EncryptConfig(config);

            // Act - check tier to verify encryption
            var tier = service.GetCurrentTier(encrypted);

            // Assert
            Assert.AreNotEqual(EncryptionTier.None, tier);
        }

        [TestMethod]
        public void GetCurrentTier_UnencryptedData_ReturnsNone()
        {
            // Arrange
            var service = ConfigEncryptionService.Instance;
            var plainPayload = "Plain text data";

            // Act
            var tier = service.GetCurrentTier(plainPayload);

            // Assert
            Assert.AreEqual(EncryptionTier.None, tier);
        }

        [TestMethod]
        public void GetCurrentTier_NullOrEmpty_ReturnsNone()
        {
            // Arrange
            var service = ConfigEncryptionService.Instance;

            // Act & Assert
            Assert.AreEqual(EncryptionTier.None, service.GetCurrentTier(null));
            Assert.AreEqual(EncryptionTier.None, service.GetCurrentTier(""));
            Assert.AreEqual(EncryptionTier.None, service.GetCurrentTier("   "));
        }

        [TestMethod]
        public void EncryptConfig_StandardTier_ProducesEncryptedPayload()
        {
            // Arrange
            var service = ConfigEncryptionService.Instance;
            var config = new RedballConfig { Theme = "Dark", TypeThingEnabled = true };

            // Act
            var encrypted = service.EncryptConfig(config, EncryptionTier.Standard);
            var tier = service.GetCurrentTier(encrypted);

            // Assert
            Assert.AreEqual(EncryptionTier.Standard, tier);

            // Verify round-trip
            var decrypted = service.DecryptConfig(encrypted);
            Assert.IsNotNull(decrypted);
            Assert.AreEqual(config.Theme, decrypted.Theme);
        }

        [TestMethod]
        public void Dispose_DoesNotThrow()
        {
            // Arrange
            var service = ConfigEncryptionService.Instance;

            // Act & Assert - multiple disposes should be safe
            service.Dispose();
            service.Dispose();
        }
    }
}
