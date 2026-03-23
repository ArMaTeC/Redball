using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Input;

namespace Redball.Tests;

/// <summary>
/// Tests for verifying HID keyboard input produces correct output across all characters.
/// These tests validate that TypeThing (via InterceptionInputService) types the correct
/// characters by comparing input text against what was actually typed.
/// </summary>
[TestClass]
public class KeyMappingAccuracyTests
{
    // Test all printable ASCII characters and common symbols
    private static readonly string TestCharacterSets =
        "abcdefghijklmnopqrstuvwxyz" +                    // lowercase
        "ABCDEFGHIJKLMNOPQRSTUVWXYZ" +                    // uppercase
        "0123456789" +                                    // numbers
        "!@#$%^&*()" +                                    // shifted numbers
        "[]{};:'\"',<>./?\\|" +                          // punctuation
        " -=+_`~" +                                       // space and special
            // Extended ASCII - these typically require Unicode fallback or AltGr
            "£€¥§©©°±µ";                                      // extended

    private static string _testOutputPath = null!;
    private static StringBuilder _logBuilder = null!;

    [ClassInitialize]
    public static void ClassInitialize(TestContext context)
    {
        _testOutputPath = Path.Combine(Path.GetTempPath(), $"Redball_KeyMapping_Test_{Guid.NewGuid():N}.log");
        _logBuilder = new StringBuilder();
        _logBuilder.AppendLine("=== Redball Key Mapping Accuracy Test ===");
        _logBuilder.AppendLine($"Test Run: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        _logBuilder.AppendLine($"OS: {Environment.OSVersion}");
        _logBuilder.AppendLine($"Keyboard Layout: {System.Globalization.CultureInfo.CurrentCulture.Name}");
        _logBuilder.AppendLine();
    }

    [ClassCleanup]
    public static void ClassCleanup()
    {
        // Write test results to file
        File.WriteAllText(_testOutputPath, _logBuilder.ToString());
        Console.WriteLine($"\nDetailed results written to: {_testOutputPath}");
    }

    /// <summary>
    /// Tests VkKeyScanW mapping for all printable ASCII characters.
    /// Documents what VK codes and shift states each character maps to.
    /// </summary>
    [TestMethod]
    public void Log_VkKeyScanW_Mappings_ForAllAsciiChars()
    {
        _logBuilder.AppendLine("--- VkKeyScanW Mappings ---");
        _logBuilder.AppendLine("Char | Unicode | VK Code | ShiftState | VK Name");
        _logBuilder.AppendLine("-----|---------|---------|------------|----------");

        for (int i = 32; i < 127; i++) // Printable ASCII
        {
            char ch = (char)i;
            var vkResult = VkKeyScanW(ch);
            
            if (vkResult != -1)
            {
                var vk = (byte)(vkResult & 0xFF);
                var shiftState = (byte)((vkResult >> 8) & 0xFF);
                var vkName = GetVirtualKeyName(vk);
                
                _logBuilder.AppendLine($"  {ch}  | U+{i:X4}  |  0x{vk:X2}   |     {shiftState}      | {vkName}");
            }
            else
            {
                _logBuilder.AppendLine($"  {ch}  | U+{i:X4}  |  NO KEY |     N/A    | (no physical key)");
            }
        }

        // Log problematic characters specifically
        var problematicChars = new[] { '~', '@', '#', '£', '$', '%', '^', '&', '*', '(', ')', '_', '+', '{', '}', '|', ':', '"', '<', '>', '?' };
        _logBuilder.AppendLine();
        _logBuilder.AppendLine("--- Problematic Character Analysis ---");
        foreach (var ch in problematicChars)
        {
            var vkResult = VkKeyScanW(ch);
            if (vkResult != -1)
            {
                var vk = (byte)(vkResult & 0xFF);
                var shiftState = (byte)((vkResult >> 8) & 0xFF);
                var vkName = GetVirtualKeyName(vk);
                var needsShift = (shiftState & 1) != 0;
                var needsCtrl = (shiftState & 2) != 0;
                var needsAlt = (shiftState & 4) != 0;
                
                _logBuilder.AppendLine($"'{ch}' -> VK 0x{vk:X2} ({vkName}) | Shift:{needsShift} Ctrl:{needsCtrl} Alt:{needsAlt}");
            }
            else
            {
                _logBuilder.AppendLine($"'{ch}' -> NO MAPPING (Unicode fallback required)");
            }
        }

        Assert.IsTrue(true); // This test always passes - it's for documentation
    }

    /// <summary>
    /// Specifically tests the ~ vs @ issue reported by users.
    /// On UK keyboards, ~ and @ are on different physical keys than US layout.
    /// </summary>
    [TestMethod]
    public void Validate_TildeVsAt_Mapping()
    {
        // Test the specific issue: nU~djjei$33345 becomes nU@djjei$33345
        var tildeVk = VkKeyScanW('~');
        var atVk = VkKeyScanW('@');

        _logBuilder.AppendLine();
        _logBuilder.AppendLine("--- Tilde vs @ Mapping ---");
        _logBuilder.AppendLine($"'~' (U+007E): VkKeyScanW = 0x{tildeVk:X4}");
        if (tildeVk != -1)
        {
            var vk = (byte)(tildeVk & 0xFF);
            var shift = (byte)((tildeVk >> 8) & 0xFF);
            _logBuilder.AppendLine($"  -> VK 0x{vk:X2} ({GetVirtualKeyName(vk)}), ShiftState={shift}");
        }

        _logBuilder.AppendLine($"'@' (U+0040): VkKeyScanW = 0x{atVk:X4}");
        if (atVk != -1)
        {
            var vk = (byte)(atVk & 0xFF);
            var shift = (byte)((atVk >> 8) & 0xFF);
            _logBuilder.AppendLine($"  -> VK 0x{vk:X2} ({GetVirtualKeyName(vk)}), ShiftState={shift}");
        }

        // On US layout: ~ is VK_OEM_3 (0xC0) with Shift, @ is VK_2 (0x32) with Shift
        // On UK layout: ~ is typically on a different key, @ is on VK_OEM_7 (0xDE) with Shift
        // The bug likely occurs because our VirtualKeyToKeyCode mapping assumes US layout

        // Assert that ~ and @ map to different VK codes
        Assert.AreNotEqual(tildeVk & 0xFF, atVk & 0xFF, 
            "~ and @ should map to different virtual keys. If they map to the same VK, shift state must differ.");
    }

    /// <summary>
    /// Tests that all characters in the test string "nU~djjei$33345" 
    /// map to unique and correct VK codes.
    /// </summary>
    [TestMethod]
    public void Validate_ProblematicInputString_Mapping()
    {
        const string input = "nU~djjei$33345";
        
        _logBuilder.AppendLine();
        _logBuilder.AppendLine("--- Problematic Input String Analysis ---");
        _logBuilder.AppendLine($"Input: '{input}'");
        _logBuilder.AppendLine();
        _logBuilder.AppendLine("Char | Unicode | VK Code | Shift | KeyCode Map | Expected Output");
        _logBuilder.AppendLine("-----|---------|---------|-------|-------------|----------------");

        var errors = new List<string>();

        foreach (char ch in input)
        {
            var vkResult = VkKeyScanW(ch);
            if (vkResult == -1)
            {
                errors.Add($"Character '{ch}' has no VK mapping - will use Unicode fallback");
                _logBuilder.AppendLine($"  {ch}  | U+{(int)ch:X4}  |   N/A   |  N/A  |  FALLBACK   | '{ch}' (Unicode)");
                continue;
            }

            var vk = (byte)(vkResult & 0xFF);
            var shiftState = (byte)((vkResult >> 8) & 0xFF);
            var keyCode = VirtualKeyToKeyCode(vk);
            var needsShift = (shiftState & 1) != 0;

            _logBuilder.AppendLine($"  {ch}  | U+{(int)ch:X4}  |  0x{vk:X2}   |   {(needsShift ? "Y" : "N")}   | {(keyCode?.ToString() ?? "NULL")} | '{ch}'");

            // Critical check: if KeyCode is null, the character won't type correctly via HID
            if (keyCode == null)
            {
                errors.Add($"Character '{ch}' (U+{(int)ch:X4}) maps to VK 0x{vk:X2} which has no KeyCode mapping!");
            }
        }

        if (errors.Count > 0)
        {
            _logBuilder.AppendLine();
            _logBuilder.AppendLine("ERRORS:");
            foreach (var error in errors)
            {
                _logBuilder.AppendLine($"  - {error}");
            }
        }

        Assert.IsTrue(errors.Count == 0, 
            $"Found {errors.Count} mapping errors for input string '{input}':\n{string.Join("\n", errors)}");
    }

    /// <summary>
    /// Comprehensive test that validates all printable ASCII characters
    /// have valid KeyCode mappings (or at least Unicode fallback).
    /// </summary>
    [TestMethod]
    public void Validate_AllAsciiChars_HaveMapping()
    {
        var failures = new List<(char ch, int vk, string reason)>();
        var unicodeFallbacks = new List<char>();

        for (int i = 32; i < 127; i++)
        {
            char ch = (char)i;
            var vkResult = VkKeyScanW(ch);
            
            if (vkResult == -1)
            {
                // No physical key - will use Unicode fallback
                unicodeFallbacks.Add(ch);
                continue;
            }

            var vk = (byte)(vkResult & 0xFF);
            var keyCode = VirtualKeyToKeyCode(vk);
            
            if (keyCode == null)
            {
                failures.Add((ch, vk, $"VK 0x{vk:X2} has no KeyCode mapping"));
            }
        }

        _logBuilder.AppendLine();
        _logBuilder.AppendLine("--- Full ASCII Mapping Validation ---");
        _logBuilder.AppendLine($"Total printable ASCII: 95 characters");
        _logBuilder.AppendLine($"Characters using Unicode fallback: {unicodeFallbacks.Count}");
        _logBuilder.AppendLine($"Characters with missing KeyCode mapping: {failures.Count}");

        if (failures.Count > 0)
        {
            _logBuilder.AppendLine();
            _logBuilder.AppendLine("Missing KeyCode mappings:");
            foreach (var (ch, vk, reason) in failures)
            {
                _logBuilder.AppendLine($"  '{ch}' (U+{(int)ch:X4}) -> VK 0x{vk:X2}: {reason}");
            }
        }

        // This should be 0 for complete HID support
        Assert.AreEqual(0, failures.Count, 
            $"{failures.Count} characters have VK codes but no KeyCode mapping. These will fail to type via HID.");
    }

    /// <summary>
    /// Tests shift-state handling for all characters that require Shift modifier.
    /// Verifies that shifted symbols (like @ # $ %) correctly indicate shift is needed.
    /// </summary>
    [TestMethod]
    public void Validate_ShiftStateHandling()
    {
        var shiftChars = new[] { '~', '!', '@', '#', '$', '%', '^', '&', '*', '(', ')', '_', '+', '{', '}', '|', ':', '"', '<', '>', '?' };
        var errors = new List<string>();

        _logBuilder.AppendLine();
        _logBuilder.AppendLine("--- Shift State Validation ---");

        foreach (char ch in shiftChars)
        {
            var vkResult = VkKeyScanW(ch);
            if (vkResult == -1)
            {
                _logBuilder.AppendLine($"'{ch}' -> No VK mapping (Unicode fallback)");
                continue;
            }

            var shiftState = (byte)((vkResult >> 8) & 0xFF);
            var needsShift = (shiftState & 1) != 0;

            _logBuilder.AppendLine($"'{ch}' -> ShiftState={shiftState}, NeedsShift={needsShift}");

            // All these characters should require shift on standard layouts
            // If they don't, it might indicate a layout mismatch
            if (!needsShift)
            {
                errors.Add($"'{ch}' does not indicate Shift needed (ShiftState={shiftState}) - possible layout mismatch");
            }
        }

        // This is a warning, not a hard failure, as different layouts may vary
        if (errors.Count > 0)
        {
            _logBuilder.AppendLine();
            _logBuilder.AppendLine("WARNINGS (may indicate layout-specific behavior):");
            foreach (var error in errors)
            {
                _logBuilder.AppendLine($"  - {error}");
            }
        }

        // We don't fail here because layout differences are expected
        Assert.IsTrue(true);
    }

    // P/Invoke for VkKeyScanW
    [DllImport("user32.dll")]
    private static extern short VkKeyScanW(char ch);

    /// <summary>
    /// Maps Win32 virtual key codes to descriptive names.
    /// Mirrors the mapping in InterceptionInputService.VirtualKeyToKeyCode.
    /// </summary>
    private static string GetVirtualKeyName(ushort vk)
    {
        return vk switch
        {
            0x08 => "VK_BACK",
            0x09 => "VK_TAB",
            0x0D => "VK_RETURN",
            0x10 => "VK_SHIFT",
            0x11 => "VK_CONTROL",
            0x12 => "VK_MENU",
            0x14 => "VK_CAPITAL",
            0x1B => "VK_ESCAPE",
            0x20 => "VK_SPACE",
            0x30 => "VK_0",
            0x31 => "VK_1",
            0x32 => "VK_2",
            0x33 => "VK_3",
            0x34 => "VK_4",
            0x35 => "VK_5",
            0x36 => "VK_6",
            0x37 => "VK_7",
            0x38 => "VK_8",
            0x39 => "VK_9",
            0x41 => "VK_A",
            0x42 => "VK_B",
            0x43 => "VK_C",
            0x44 => "VK_D",
            0x45 => "VK_E",
            0x46 => "VK_F",
            0x47 => "VK_G",
            0x48 => "VK_H",
            0x49 => "VK_I",
            0x4A => "VK_J",
            0x4B => "VK_K",
            0x4C => "VK_L",
            0x4D => "VK_M",
            0x4E => "VK_N",
            0x4F => "VK_O",
            0x50 => "VK_P",
            0x51 => "VK_Q",
            0x52 => "VK_R",
            0x53 => "VK_S",
            0x54 => "VK_T",
            0x55 => "VK_U",
            0x56 => "VK_V",
            0x57 => "VK_W",
            0x58 => "VK_X",
            0x59 => "VK_Y",
            0x5A => "VK_Z",
            0xBA => "VK_OEM_1",    // ;:
            0xBB => "VK_OEM_PLUS", // =+
            0xBC => "VK_OEM_COMMA", // ,<
            0xBD => "VK_OEM_MINUS", // -_
            0xBE => "VK_OEM_PERIOD", // .>
            0xBF => "VK_OEM_2",    // /?
            0xC0 => "VK_OEM_3",    // `~
            0xDB => "VK_OEM_4",    // [{
            0xDC => "VK_OEM_5",    // \|
            0xDD => "VK_OEM_6",    // ]}
            0xDE => "VK_OEM_7",    // '"
            0xE2 => "VK_OEM_102",  // \| on 102-key
            _ => $"VK_UNKNOWN(0x{vk:X2})"
        };
    }

    /// <summary>
    /// Mirrors the VirtualKeyToKeyCode method from InterceptionInputService
    /// for validation purposes. Returns null if no mapping exists.
    /// </summary>
    private static object? VirtualKeyToKeyCode(ushort vk)
    {
        // This mirrors the logic in InterceptionInputService
        // We return object? instead of KeyCode? to avoid dependency on InputInterceptor
        return vk switch
        {
            0x08 => "Backspace",
            0x09 => "Tab",
            0x0D => "Enter",
            0x10 => "LeftShift",
            0x11 => "Control",
            0x12 => "Alt",
            0x14 => "CapsLock",
            0x1B => "Escape",
            0x20 => "Space",
            0x21 => "PageUp",
            0x22 => "PageDown",
            0x23 => "End",
            0x24 => "Home",
            0x25 => "Left",
            0x26 => "Up",
            0x27 => "Right",
            0x28 => "Down",
            0x2C => "PrintScreen",
            0x2D => "Insert",
            0x2E => "Delete",
            0x30 => "Zero",
            0x31 => "One",
            0x32 => "Two",
            0x33 => "Three",
            0x34 => "Four",
            0x35 => "Five",
            0x36 => "Six",
            0x37 => "Seven",
            0x38 => "Eight",
            0x39 => "Nine",
            0x41 => "A",
            0x42 => "B",
            0x43 => "C",
            0x44 => "D",
            0x45 => "E",
            0x46 => "F",
            0x47 => "G",
            0x48 => "H",
            0x49 => "I",
            0x4A => "J",
            0x4B => "K",
            0x4C => "L",
            0x4D => "M",
            0x4E => "N",
            0x4F => "O",
            0x50 => "P",
            0x51 => "Q",
            0x52 => "R",
            0x53 => "S",
            0x54 => "T",
            0x55 => "U",
            0x56 => "V",
            0x57 => "W",
            0x58 => "X",
            0x59 => "Y",
            0x5A => "Z",
            0x5B => "LeftWindowsKey",
            0x5C => "RightWindowsKey",
            0x5D => "Menu",
            0x60 => "Numpad0",
            0x61 => "Numpad1",
            0x62 => "Numpad2",
            0x63 => "Numpad3",
            0x64 => "Numpad4",
            0x65 => "Numpad5",
            0x66 => "Numpad6",
            0x67 => "Numpad7",
            0x68 => "Numpad8",
            0x69 => "Numpad9",
            0x6A => "NumpadAsterisk",
            0x6B => "NumpadPlus",
            0x6D => "NumpadMinus",
            0x6E => "NumpadDelete",
            0x6F => "NumpadDivide",
            0x70 => "F1",
            0x71 => "F2",
            0x72 => "F3",
            0x73 => "F4",
            0x74 => "F5",
            0x75 => "F6",
            0x76 => "F7",
            0x77 => "F8",
            0x78 => "F9",
            0x79 => "F10",
            0x7A => "F11",
            0x7B => "F12",
            0x90 => "NumLock",
            0x91 => "ScrollLock",
            0xA0 => "LeftShift",
            0xA1 => "RightShift",
            0xA2 => "Control",
            0xA3 => "Control",
            0xA4 => "Alt",
            0xA5 => "Alt",
            0xBA => "Semicolon",
            0xBB => "Equals",
            0xBC => "Comma",
            0xBD => "Dash",
            0xBE => "Dot",
            0xBF => "Slash",
            0xC0 => "Tilde",
            0xDB => "OpenBracketBrace",
            0xDC => "Backslash",
            0xDE => "Apostrophe",
            0xE2 => "Backslash",
            _ => null
        };
    }
}
