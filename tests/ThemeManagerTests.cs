using Microsoft.VisualStudio.TestTools.UnitTesting;
using Redball.UI;

namespace Redball.Tests
{
    [TestClass]
    public class ThemeManagerTests
    {
        [TestMethod]
        public void ThemeFromString_Dark_ReturnsDark()
        {
            var result = ThemeManager.ThemeFromString("Dark");
            Assert.AreEqual(Theme.Dark, result);
        }

        [TestMethod]
        public void ThemeFromString_Light_ReturnsLight()
        {
            var result = ThemeManager.ThemeFromString("Light");
            Assert.AreEqual(Theme.Light, result);
        }

        [TestMethod]
        public void ThemeFromString_MidnightBlue_ReturnsMidnightBlue()
        {
            var result = ThemeManager.ThemeFromString("MidnightBlue");
            Assert.AreEqual(Theme.MidnightBlue, result);
        }

        [TestMethod]
        public void ThemeFromString_ForestGreen_ReturnsForestGreen()
        {
            var result = ThemeManager.ThemeFromString("ForestGreen");
            Assert.AreEqual(Theme.ForestGreen, result);
        }

        [TestMethod]
        public void ThemeFromString_OceanBlue_ReturnsOceanBlue()
        {
            var result = ThemeManager.ThemeFromString("OceanBlue");
            Assert.AreEqual(Theme.OceanBlue, result);
        }

        [TestMethod]
        public void ThemeFromString_SunsetOrange_ReturnsSunsetOrange()
        {
            var result = ThemeManager.ThemeFromString("SunsetOrange");
            Assert.AreEqual(Theme.SunsetOrange, result);
        }

        [TestMethod]
        public void ThemeFromString_RoyalPurple_ReturnsRoyalPurple()
        {
            var result = ThemeManager.ThemeFromString("RoyalPurple");
            Assert.AreEqual(Theme.RoyalPurple, result);
        }

        [TestMethod]
        public void ThemeFromString_SlateGray_ReturnsSlateGray()
        {
            var result = ThemeManager.ThemeFromString("SlateGray");
            Assert.AreEqual(Theme.SlateGray, result);
        }

        [TestMethod]
        public void ThemeFromString_RoseGold_ReturnsRoseGold()
        {
            var result = ThemeManager.ThemeFromString("RoseGold");
            Assert.AreEqual(Theme.RoseGold, result);
        }

        [TestMethod]
        public void ThemeFromString_Cyberpunk_ReturnsCyberpunk()
        {
            var result = ThemeManager.ThemeFromString("Cyberpunk");
            Assert.AreEqual(Theme.Cyberpunk, result);
        }

        [TestMethod]
        public void ThemeFromString_Coffee_ReturnsCoffee()
        {
            var result = ThemeManager.ThemeFromString("Coffee");
            Assert.AreEqual(Theme.Coffee, result);
        }

        [TestMethod]
        public void ThemeFromString_ArcticFrost_ReturnsArcticFrost()
        {
            var result = ThemeManager.ThemeFromString("ArcticFrost");
            Assert.AreEqual(Theme.ArcticFrost, result);
        }

        [TestMethod]
        public void ThemeFromString_HighContrast_ReturnsHighContrast()
        {
            var result = ThemeManager.ThemeFromString("HighContrast");
            Assert.AreEqual(Theme.HighContrast, result);
        }

        [TestMethod]
        public void ThemeFromString_Unknown_ReturnsDarkDefault()
        {
            var result = ThemeManager.ThemeFromString("NonExistentTheme");
            Assert.AreEqual(Theme.Dark, result, "Unknown theme names should default to Dark");
        }

        [TestMethod]
        public void ThemeFromString_Empty_ReturnsDarkDefault()
        {
            var result = ThemeManager.ThemeFromString("");
            Assert.AreEqual(Theme.Dark, result, "Empty string should default to Dark");
        }

        [TestMethod]
        public void ThemeFromString_System_ReturnsDarkOrLight()
        {
            // "System" resolves via registry — result must be Dark or Light
            var result = ThemeManager.ThemeFromString("System");
            Assert.IsTrue(result == Theme.Dark || result == Theme.Light,
                $"System theme should resolve to Dark or Light, got: {result}");
        }

        [TestMethod]
        public void ThemeFromString_System_SetsIsFollowingSystemTheme()
        {
            // Calling ThemeFromString("System") should enable following
            _ = ThemeManager.ThemeFromString("System");
            Assert.IsTrue(ThemeManager.IsFollowingSystemTheme,
                "ThemeFromString(\"System\") should set IsFollowingSystemTheme to true");
        }

        [TestMethod]
        public void IsSystemDarkMode_ReturnsBool()
        {
            // Should not throw regardless of registry state
            var result = ThemeManager.IsSystemDarkMode();
            Assert.IsTrue(result || !result, "IsSystemDarkMode should return without throwing");
        }

        [TestMethod]
        public void Theme_Enum_HasExpectedValues()
        {
            var names = Enum.GetNames(typeof(Theme));
            Assert.AreEqual(13, names.Length, "Theme enum should have 13 values");
            Assert.IsTrue(names.Contains("Light"));
            Assert.IsTrue(names.Contains("Dark"));
            Assert.IsTrue(names.Contains("MidnightBlue"));
            Assert.IsTrue(names.Contains("ForestGreen"));
            Assert.IsTrue(names.Contains("OceanBlue"));
            Assert.IsTrue(names.Contains("SunsetOrange"));
            Assert.IsTrue(names.Contains("RoyalPurple"));
            Assert.IsTrue(names.Contains("SlateGray"));
            Assert.IsTrue(names.Contains("RoseGold"));
            Assert.IsTrue(names.Contains("Cyberpunk"));
            Assert.IsTrue(names.Contains("Coffee"));
            Assert.IsTrue(names.Contains("ArcticFrost"));
            Assert.IsTrue(names.Contains("HighContrast"));
        }

        [TestMethod]
        public void ThemeColors_ActiveRed_IsCorrect()
        {
            var color = ThemeManager.Colors.ActiveRed;
            Assert.AreEqual(220, color.R);
            Assert.AreEqual(53, color.G);
            Assert.AreEqual(69, color.B);
        }

        [TestMethod]
        public void ThemeColors_TimedOrange_IsCorrect()
        {
            var color = ThemeManager.Colors.TimedOrange;
            Assert.AreEqual(253, color.R);
            Assert.AreEqual(126, color.G);
            Assert.AreEqual(20, color.B);
        }

        [TestMethod]
        public void ThemeColors_PausedGray_IsCorrect()
        {
            var color = ThemeManager.Colors.PausedGray;
            Assert.AreEqual(108, color.R);
            Assert.AreEqual(117, color.G);
            Assert.AreEqual(125, color.B);
        }
    }
}
