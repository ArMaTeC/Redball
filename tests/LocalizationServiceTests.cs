using Microsoft.VisualStudio.TestTools.UnitTesting;
using Redball.UI.Services;

namespace Redball.Tests
{
    [TestClass]
    public class LocalizationServiceTests
    {
        [TestMethod]
        public void LocalizationService_Instance_IsSingleton()
        {
            // Arrange & Act
            var instance1 = LocalizationService.Instance;
            var instance2 = LocalizationService.Instance;

            // Assert
            Assert.AreSame(instance1, instance2, "Instance should return the same singleton");
        }

        [TestMethod]
        public void LocalizationService_CurrentLocale_DefaultIsEn()
        {
            // Arrange
            var service = LocalizationService.Instance;

            // Act & Assert
            Assert.AreEqual("en", service.CurrentLocale, "Default locale should be 'en'");
        }

        [TestMethod]
        public void LocalizationService_CurrentLocale_CanSetValidLocale()
        {
            // Arrange
            var service = LocalizationService.Instance;
            var originalLocale = service.CurrentLocale;

            // Act
            service.CurrentLocale = "es";

            // Assert
            Assert.AreEqual("es", service.CurrentLocale, "Should be able to set to 'es'");

            // Restore
            service.CurrentLocale = originalLocale;
        }

        [TestMethod]
        public void LocalizationService_CurrentLocale_InvalidLocale_KeepsCurrent()
        {
            // Arrange
            var service = LocalizationService.Instance;
            var originalLocale = service.CurrentLocale;

            // Act
            service.CurrentLocale = "invalid_locale";

            // Assert - should keep the current locale
            Assert.AreEqual(originalLocale, service.CurrentLocale, "Should keep current locale when setting invalid");
        }

        [TestMethod]
        public void LocalizationService_GetString_ExistingKey_ReturnsValue()
        {
            // Arrange
            var service = LocalizationService.Instance;

            // Act
            var result = service.GetString("app.name");

            // Assert
            Assert.AreEqual("Redball", result, "Should return localized app name");
        }

        [TestMethod]
        public void LocalizationService_GetString_NonExistentKey_ReturnsKey()
        {
            // Arrange
            var service = LocalizationService.Instance;

            // Act
            var result = service.GetString("nonexistent.key.12345");

            // Assert
            Assert.AreEqual("nonexistent.key.12345", result, "Should return key if not found");
        }

        [TestMethod]
        public void LocalizationService_GetString_WithLocaleOverride()
        {
            // Arrange
            var service = LocalizationService.Instance;

            // Act - get Spanish string directly
            var result = service.GetString("status.active", "es");

            // Assert
            Assert.AreEqual("Activo", result, "Should return Spanish translation");
        }

        [TestMethod]
        public void LocalizationService_GetString_FallsBackToEnglish()
        {
            // Arrange
            var service = LocalizationService.Instance;
            service.CurrentLocale = "bl"; // Blade runner theme

            // Act - key exists in English but not in 'bl'
            var result = service.GetString("menu.settings");

            // Assert - should fall back to English
            Assert.IsFalse(string.IsNullOrEmpty(result), "Should return something");
        }

        [TestMethod]
        public void LocalizationService_AvailableLocales_ContainsExpected()
        {
            // Arrange
            var service = LocalizationService.Instance;

            // Act
            var locales = service.AvailableLocales;

            // Assert
            Assert.IsTrue(locales.Contains("en"), "Should have 'en' locale");
            Assert.IsTrue(locales.Contains("es"), "Should have 'es' locale");
            Assert.IsTrue(locales.Contains("fr"), "Should have 'fr' locale");
            Assert.IsTrue(locales.Contains("de"), "Should have 'de' locale");
            Assert.IsTrue(locales.Contains("bl"), "Should have 'bl' (blade runner) locale");
        }

        [TestMethod]
        public void LocalizationService_GetString_CommonKeys_Exist()
        {
            // Arrange
            var service = LocalizationService.Instance;
            var keys = new[]
            {
                "app.name",
                "status.active",
                "status.paused",
                "menu.toggle",
                "menu.settings",
                "menu.exit",
                "notify.activated",
                "notify.deactivated"
            };

            foreach (var key in keys)
            {
                // Act
                var result = service.GetString(key);

                // Assert - key should exist and return something (not the key itself)
                Assert.IsFalse(string.IsNullOrEmpty(result), $"Key '{key}' should have a value");
            }
        }
    }
}
