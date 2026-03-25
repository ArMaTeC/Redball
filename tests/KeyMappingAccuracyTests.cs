using Microsoft.VisualStudio.TestTools.UnitTesting;
using Redball.UI.Services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;

namespace Redball.Tests;

[TestClass]
public class KeyMappingAccuracyTests
{
    private static MethodInfo? _virtualKeyToKeyCodeMethod;

    [ClassInitialize]
    public static void ClassInitialize(TestContext _)
    {
        _virtualKeyToKeyCodeMethod = typeof(InterceptionInputService)
            .GetMethod("VirtualKeyToKeyCode", BindingFlags.NonPublic | BindingFlags.Static);

        Assert.IsNotNull(_virtualKeyToKeyCodeMethod, "Could not locate InterceptionInputService.VirtualKeyToKeyCode via reflection.");
    }

    [TestMethod]
    public void VkMappings_ProblemString_AllCharsResolvable()
    {
        const string input = "nU~djjei$33345";
        var failures = new List<string>();

        foreach (var ch in input)
        {
            var vkResult = VkKeyScanW(ch);
            if (vkResult == -1)
            {
                continue;
            }

            var vk = (ushort)(vkResult & 0xFF);
            if (!HasInterceptionMapping(vk))
            {
                failures.Add($"'{ch}' mapped to VK 0x{vk:X2}, but no Interception key mapping exists.");
            }
        }

        Assert.AreEqual(0, failures.Count, string.Join(Environment.NewLine, failures));
    }

    [TestMethod]
    public void VkMappings_TildeAndAt_AreDistinctAndMapped()
    {
        var tilde = VkKeyScanW('~');
        var at = VkKeyScanW('@');

        Assert.AreNotEqual(-1, tilde, "'~' did not resolve via VkKeyScanW.");
        Assert.AreNotEqual(-1, at, "'@' did not resolve via VkKeyScanW.");

        var tildeVk = (ushort)(tilde & 0xFF);
        var atVk = (ushort)(at & 0xFF);
        var tildeShift = (byte)((tilde >> 8) & 0xFF);
        var atShift = (byte)((at >> 8) & 0xFF);

        Assert.IsTrue(tildeVk != atVk || tildeShift != atShift,
            "'~' and '@' resolved to the same VK + shift combination.");

        Assert.IsTrue(HasInterceptionMapping(tildeVk), $"Missing Interception mapping for '~' VK 0x{tildeVk:X2}.");
        Assert.IsTrue(HasInterceptionMapping(atVk), $"Missing Interception mapping for '@' VK 0x{atVk:X2}.");
    }

    [TestMethod]
    public void VkMappings_PrintableAscii_NoUnexpectedUnmappedVirtualKeys()
    {
        var unmapped = new Dictionary<ushort, List<char>>();

        for (var code = 32; code <= 126; code++)
        {
            var ch = (char)code;
            var vkResult = VkKeyScanW(ch);
            if (vkResult == -1)
            {
                continue;
            }

            var vk = (ushort)(vkResult & 0xFF);
            if (HasInterceptionMapping(vk))
            {
                continue;
            }

            if (!unmapped.TryGetValue(vk, out var chars))
            {
                chars = new List<char>();
                unmapped[vk] = chars;
            }

            chars.Add(ch);
        }

        var allowedUnmappedVks = new HashSet<ushort>();

        var unexpected = unmapped
            .Where(kvp => !allowedUnmappedVks.Contains(kvp.Key))
            .Select(kvp => $"VK 0x{kvp.Key:X2} ({new string(kvp.Value.Distinct().ToArray())})")
            .ToList();

        Assert.AreEqual(0, unexpected.Count,
            "Unexpected unmapped VKs found: " + string.Join(", ", unexpected));
    }

    [TestMethod]
    public void VkMappings_ShiftedChars_ResolveWithShiftState()
    {
        var chars = new[] { '~', '!', '@', '$', '%', '^', '&', '*', '(', ')', '_', '+', '{', '}', '|', ':', '"', '<', '>', '?' };

        foreach (var ch in chars)
        {
            var vkResult = VkKeyScanW(ch);
            Assert.AreNotEqual(-1, vkResult, $"'{ch}' did not resolve via VkKeyScanW.");

            var shiftState = (byte)((vkResult >> 8) & 0xFF);
            Assert.IsTrue((shiftState & 0x01) != 0,
                $"'{ch}' resolved without Shift modifier on this layout (shiftState={shiftState}).");
        }
    }

    [TestMethod]
    public void VkMappings_ExtendedTypeThingCharset_LayoutMappingsAreCovered()
    {
        const string charset = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789!@#$%^&*()-_=+[]{}|;:'\",.<>/?`~ \\\u00E9\u00E0\u00EE\u00F8\u00E7\u00F1\u00DF\u00A1\u00BF\u20AC\u00A3\u00A5\u00A9\u00AE\u2122\u03C0\u03A9\u221A\u221E\u2206\u2248\u2260\u2264\u2265";
        var failures = new List<string>();

        foreach (var ch in charset)
        {
            var vkResult = VkKeyScanW(ch);
            var isDeadKeyResult = (vkResult & unchecked((short)0x8000)) != 0;

            // Characters with no direct VK mapping (or dead-key mappings) are expected
            // to use Unicode fallback in TypeThing.
            if (vkResult == -1 || isDeadKeyResult)
            {
                continue;
            }

            var vk = (ushort)(vkResult & 0xFF);
            if (!HasInterceptionMapping(vk))
            {
                failures.Add($"'{ch}' mapped to VK 0x{vk:X2}, but no Interception key mapping exists.");
            }
        }

        Assert.AreEqual(0, failures.Count, string.Join(Environment.NewLine, failures));
    }

    private static bool HasInterceptionMapping(ushort vk)
    {
        var result = _virtualKeyToKeyCodeMethod!.Invoke(null, new object[] { vk });
        return result != null;
    }

    [DllImport("user32.dll")]
    private static extern short VkKeyScanW(char ch);
}
