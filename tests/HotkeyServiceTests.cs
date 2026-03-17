using Microsoft.VisualStudio.TestTools.UnitTesting;
using Redball.UI.Views;

namespace Redball.Tests
{
    [TestClass]
    public class HotkeyServiceTests
    {
        [TestMethod]
        public void HotkeyService_ParseHotkey_SimpleKey_ReturnsCorrectValues()
        {
            // Arrange
            var hotkey = "A";

            // Act
            var (modifiers, vk) = HotkeyService.ParseHotkey(hotkey);

            // Assert
            Assert.AreEqual(0u, modifiers, "Should have no modifiers");
            Assert.AreEqual(0x41u, vk, "Should return virtual key for 'A'");
        }

        [TestMethod]
        public void HotkeyService_ParseHotkey_WithCtrlModifier()
        {
            // Arrange
            var hotkey = "Ctrl+A";

            // Act
            var (modifiers, vk) = HotkeyService.ParseHotkey(hotkey);

            // Assert
            Assert.AreEqual(HotkeyService.MOD_CONTROL, modifiers & HotkeyService.MOD_CONTROL, "Should have CONTROL modifier");
            Assert.AreEqual(0x41u, vk, "Should return virtual key for 'A'");
        }

        [TestMethod]
        public void HotkeyService_ParseHotkey_WithAltModifier()
        {
            // Arrange
            var hotkey = "Alt+F1";

            // Act
            var (modifiers, vk) = HotkeyService.ParseHotkey(hotkey);

            // Assert
            Assert.AreEqual(HotkeyService.MOD_ALT, modifiers & HotkeyService.MOD_ALT, "Should have ALT modifier");
            Assert.AreEqual(0x70u, vk, "Should return virtual key for F1");
        }

        [TestMethod]
        public void HotkeyService_ParseHotkey_WithMultipleModifiers()
        {
            // Arrange
            var hotkey = "Ctrl+Shift+V";

            // Act
            var (modifiers, vk) = HotkeyService.ParseHotkey(hotkey);

            // Assert
            Assert.AreEqual(HotkeyService.MOD_CONTROL | HotkeyService.MOD_SHIFT, modifiers, "Should have CONTROL and SHIFT modifiers");
            Assert.AreEqual(0x56u, vk, "Should return virtual key for 'V'");
        }

        [TestMethod]
        public void HotkeyService_ParseHotkey_FunctionKey()
        {
            // Arrange
            var hotkey = "F12";

            // Act
            var (modifiers, vk) = HotkeyService.ParseHotkey(hotkey);

            // Assert
            Assert.AreEqual(0u, modifiers, "Should have no modifiers");
            Assert.AreEqual(0x7Bu, vk, "Should return virtual key for F12");
        }

        [TestMethod]
        public void HotkeyService_ParseHotkey_NullOrEmpty()
        {
            // Act
            var (modifiers, vk) = HotkeyService.ParseHotkey(string.Empty);

            // Assert
            Assert.AreEqual(0u, modifiers, "Should have no modifiers");
            Assert.AreEqual(0u, vk, "Should return 0 for empty");
        }

        [TestMethod]
        public void HotkeyService_ParseHotkey_UnknownKey()
        {
            // Arrange
            var hotkey = "@#$"; // Invalid keys

            // Act
            var (modifiers, vk) = HotkeyService.ParseHotkey(hotkey);

            // Assert
            Assert.AreEqual(0u, vk, "Should return 0 for unknown key");
        }

        [TestMethod]
        public void HotkeyService_ParseHotkey_SpecialKeys()
        {
            // Test various special keys
            var keys = new[]
            {
                ("TAB", 0x09u),
                ("SPACE", 0x20u),
                ("UP", 0x26u),
                ("DOWN", 0x28u),
                ("LEFT", 0x25u),
                ("RIGHT", 0x27u),
                ("HOME", 0x24u),
                ("END", 0x23u),
                ("INSERT", 0x2Du),
                ("DELETE", 0x2Eu),
                ("DEL", 0x2Eu),
                ("PAGEUP", 0x21u),
                ("PAGEDOWN", 0x22u),
                ("PAUSE", 0x13u),
                ("BREAK", 0x13u),
            };

            foreach (var (key, expected) in keys)
            {
                // Act
                var (modifiers, vk) = HotkeyService.ParseHotkey(key);

                // Assert
                Assert.AreEqual(expected, vk, $"Should return correct virtual key for '{key}'");
            }
        }

        [TestMethod]
        public void HotkeyService_ParseHotkey_AllModifiers()
        {
            // Arrange
            var hotkey = "Ctrl+Alt+Shift+Win+X";

            // Act
            var (modifiers, vk) = HotkeyService.ParseHotkey(hotkey);

            // Assert
            Assert.AreEqual(HotkeyService.MOD_CONTROL | HotkeyService.MOD_ALT | HotkeyService.MOD_SHIFT | HotkeyService.MOD_WIN,
                modifiers, "Should have all modifiers");
            Assert.AreEqual(0x58u, vk, "Should return virtual key for 'X'");
        }

        [TestMethod]
        public void HotkeyService_ParseHotkey_CaseInsensitive()
        {
            // Arrange
            var hotkey = "ctrl+SHIFT+v";

            // Act
            var (modifiers, vk) = HotkeyService.ParseHotkey(hotkey);

            // Assert
            Assert.AreEqual(HotkeyService.MOD_CONTROL | HotkeyService.MOD_SHIFT, modifiers, "Should parse case insensitively");
            Assert.AreEqual(0x56u, vk, "Should return virtual key for 'v'");
        }

        [TestMethod]
        public void HotkeyService_ConstantValues_AreCorrect()
        {
            // Assert Win32 modifier constants
            Assert.AreEqual(0x0001u, HotkeyService.MOD_ALT, "MOD_ALT should be 0x0001");
            Assert.AreEqual(0x0002u, HotkeyService.MOD_CONTROL, "MOD_CONTROL should be 0x0002");
            Assert.AreEqual(0x0004u, HotkeyService.MOD_SHIFT, "MOD_SHIFT should be 0x0004");
            Assert.AreEqual(0x0008u, HotkeyService.MOD_WIN, "MOD_WIN should be 0x0008");
        }
    }
}
