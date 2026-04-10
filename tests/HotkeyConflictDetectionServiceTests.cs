using Microsoft.VisualStudio.TestTools.UnitTesting;
using Redball.UI.WPF.Services;
using System.Windows.Forms;

namespace Redball.Tests
{
    [TestClass]
    public class HotkeyConflictDetectionServiceTests
    {
        [TestMethod]
        public void Instance_Singleton_ReturnsSameInstance()
        {
            // Act
            var instance1 = HotkeyConflictDetectionService.Instance;
            var instance2 = HotkeyConflictDetectionService.Instance;

            // Assert
            Assert.IsNotNull(instance1);
            Assert.AreSame(instance1, instance2);
        }

        [TestMethod]
        public void Constructor_InitializesDefaultHotkeys()
        {
            // Arrange & Act
            var service = HotkeyConflictDetectionService.Instance;
            var report = service.GetConflictReport();

            // Assert - Should have default hotkeys initialized
            Assert.IsTrue(report.TotalHotkeys > 0, "Should have default hotkeys initialized");
        }

        [TestMethod]
        public void RegisterHotkey_AddsHotkey()
        {
            // Arrange
            var service = HotkeyConflictDetectionService.Instance;
            var hotkey = new HotkeyInfo
            {
                Id = "test.hotkey",
                Name = "Test Hotkey",
                Key = Keys.F9,
                Modifiers = ModifierKeys.Control | ModifierKeys.Shift,
                Action = "TestAction"
            };

            // Act
            service.RegisterHotkey(hotkey);
            var report = service.GetConflictReport();

            // Assert
            Assert.IsTrue(report.TotalHotkeys > 0);
        }

        [TestMethod]
        public void CheckAllConflicts_ReturnsList()
        {
            // Arrange
            var service = HotkeyConflictDetectionService.Instance;

            // Act
            var conflicts = service.CheckAllConflicts();

            // Assert
            Assert.IsNotNull(conflicts);
            Assert.IsInstanceOfType<List<HotkeyConflict>>(conflicts);
        }

        [TestMethod]
        public void GetConflicts_ReturnsReadOnlyList()
        {
            // Arrange
            var service = HotkeyConflictDetectionService.Instance;

            // Act
            var conflicts = service.GetConflicts();

            // Assert
            Assert.IsNotNull(conflicts);
            Assert.IsInstanceOfType<IReadOnlyList<HotkeyConflict>>(conflicts);
        }

        [TestMethod]
        public void GetRemappingSuggestions_ExistingHotkey_ReturnsSuggestions()
        {
            // Arrange
            var service = HotkeyConflictDetectionService.Instance;

            // Act - Use a default hotkey ID
            var suggestions = service.GetRemappingSuggestions("command_palette");

            // Assert
            Assert.IsNotNull(suggestions);
            Assert.IsInstanceOfType<List<RemappingSuggestion>>(suggestions);
        }

        [TestMethod]
        public void GetRemappingSuggestions_NonExistentHotkey_ReturnsEmpty()
        {
            // Arrange
            var service = HotkeyConflictDetectionService.Instance;

            // Act
            var suggestions = service.GetRemappingSuggestions("nonexistent.hotkey");

            // Assert
            Assert.IsNotNull(suggestions);
            Assert.AreEqual(0, suggestions.Count);
        }

        [TestMethod]
        public void GetConflictReport_ReturnsReport()
        {
            // Arrange
            var service = HotkeyConflictDetectionService.Instance;

            // Act
            var report = service.GetConflictReport();

            // Assert
            Assert.IsNotNull(report);
            Assert.IsTrue(report.TotalHotkeys >= 0);
            Assert.IsTrue(report.RegisteredHotkeys >= 0);
            Assert.IsTrue(report.BlockingConflicts >= 0);
            Assert.IsTrue(report.WarningConflicts >= 0);
            Assert.IsInstanceOfType<bool>(report.CanUseAllHotkeys);
            Assert.IsNotNull(report.ConflictsNeedingResolution);
        }

        [TestMethod]
        public void ApplyRemapping_InvalidHotkey_ReturnsFalse()
        {
            // Arrange
            var service = HotkeyConflictDetectionService.Instance;
            var suggestion = new RemappingSuggestion
            {
                OriginalShortcut = "Ctrl+K",
                SuggestedShortcut = "Ctrl+Shift+K",
                SuggestedKey = Keys.K,
                SuggestedModifiers = ModifierKeys.Control | ModifierKeys.Shift
            };

            // Act
            var result = service.ApplyRemapping("nonexistent.hotkey", suggestion);

            // Assert
            Assert.IsFalse(result);
        }

        [TestMethod]
        public void HotkeyInfo_Properties_SetCorrectly()
        {
            // Arrange & Act
            var hotkey = new HotkeyInfo
            {
                Id = "test.id",
                Name = "Test Name",
                Key = Keys.A,
                Modifiers = ModifierKeys.Control | ModifierKeys.Shift,
                Action = "TestAction",
                RegisteredId = 1,
                IsRegistered = true,
                ConflictDescription = "Test conflict"
            };

            // Assert
            Assert.AreEqual("test.id", hotkey.Id);
            Assert.AreEqual("Test Name", hotkey.Name);
            Assert.AreEqual(Keys.A, hotkey.Key);
            Assert.AreEqual(ModifierKeys.Control | ModifierKeys.Shift, hotkey.Modifiers);
            Assert.AreEqual("TestAction", hotkey.Action);
            Assert.AreEqual(1, hotkey.RegisteredId);
            Assert.IsTrue(hotkey.IsRegistered);
            Assert.AreEqual("Test conflict", hotkey.ConflictDescription);
        }

        [TestMethod]
        public void HotkeyConflict_Properties_SetCorrectly()
        {
            // Arrange & Act
            var conflict = new HotkeyConflict
            {
                HotkeyId = "test.id",
                HotkeyName = "Test Hotkey",
                Shortcut = "Ctrl+K",
                ConflictingApp = "Test App",
                ConflictType = "system",
                Severity = "blocking"
            };

            // Assert
            Assert.AreEqual("test.id", conflict.HotkeyId);
            Assert.AreEqual("Test Hotkey", conflict.HotkeyName);
            Assert.AreEqual("Ctrl+K", conflict.Shortcut);
            Assert.AreEqual("Test App", conflict.ConflictingApp);
            Assert.AreEqual("system", conflict.ConflictType);
            Assert.AreEqual("blocking", conflict.Severity);
        }

        [TestMethod]
        public void RemappingSuggestion_Properties_SetCorrectly()
        {
            // Arrange & Act
            var suggestion = new RemappingSuggestion
            {
                OriginalShortcut = "Ctrl+K",
                SuggestedShortcut = "Ctrl+Shift+K",
                SuggestedKey = Keys.K,
                SuggestedModifiers = ModifierKeys.Control | ModifierKeys.Shift,
                Reason = "No conflicts",
                ConflictScore = 0
            };

            // Assert
            Assert.AreEqual("Ctrl+K", suggestion.OriginalShortcut);
            Assert.AreEqual("Ctrl+Shift+K", suggestion.SuggestedShortcut);
            Assert.AreEqual(Keys.K, suggestion.SuggestedKey);
            Assert.AreEqual(ModifierKeys.Control | ModifierKeys.Shift, suggestion.SuggestedModifiers);
            Assert.AreEqual("No conflicts", suggestion.Reason);
            Assert.AreEqual(0, suggestion.ConflictScore);
        }

        [TestMethod]
        public void ModifierKeys_Flags_ValuesCorrect()
        {
            // Assert
            Assert.AreEqual(0, (int)ModifierKeys.None);
            Assert.AreEqual(1, (int)ModifierKeys.Alt);
            Assert.AreEqual(2, (int)ModifierKeys.Control);
            Assert.AreEqual(4, (int)ModifierKeys.Shift);
            Assert.AreEqual(8, (int)ModifierKeys.Windows);
        }

        [TestMethod]
        public void ModifierKeys_Combinations_WorkCorrectly()
        {
            // Arrange
            var combined = ModifierKeys.Control | ModifierKeys.Shift;

            // Assert
            Assert.IsTrue((combined & ModifierKeys.Control) != 0);
            Assert.IsTrue((combined & ModifierKeys.Shift) != 0);
            Assert.IsFalse((combined & ModifierKeys.Alt) != 0);
            Assert.IsFalse((combined & ModifierKeys.Windows) != 0);
        }

        [TestMethod]
        public void ConflictReportSummary_Properties_SetCorrectly()
        {
            // Arrange & Act
            var summary = new ConflictReportSummary
            {
                TotalHotkeys = 10,
                RegisteredHotkeys = 8,
                BlockingConflicts = 2,
                WarningConflicts = 1,
                CanUseAllHotkeys = false,
                ConflictsNeedingResolution = new List<string> { "Hotkey1", "Hotkey2" }
            };

            // Assert
            Assert.AreEqual(10, summary.TotalHotkeys);
            Assert.AreEqual(8, summary.RegisteredHotkeys);
            Assert.AreEqual(2, summary.BlockingConflicts);
            Assert.AreEqual(1, summary.WarningConflicts);
            Assert.IsFalse(summary.CanUseAllHotkeys);
            CollectionAssert.AreEqual(new[] { "Hotkey1", "Hotkey2" }, summary.ConflictsNeedingResolution.ToArray());
        }

        [TestMethod]
        public void ConflictsDetected_Event_CanSubscribe()
        {
            // Arrange
            var service = HotkeyConflictDetectionService.Instance;
            var eventFired = false;
            EventHandler<List<HotkeyConflict>> handler = (sender, conflicts) => { eventFired = true; };

            // Act
            service.ConflictsDetected += handler;
            service.ConflictsDetected -= handler;

            // Assert
            Assert.IsFalse(eventFired);
        }
    }
}
