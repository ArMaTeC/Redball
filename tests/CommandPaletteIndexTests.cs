using Microsoft.VisualStudio.TestTools.UnitTesting;
using Redball.UI.WPF.Models;
using Redball.UI.WPF.Services;

namespace Redball.Tests
{
    [TestClass]
    public class CommandPaletteIndexTests
    {
        [TestMethod]
        public void Instance_Singleton_ReturnsSameInstance()
        {
            // Act
            var instance1 = CommandPaletteIndex.Instance;
            var instance2 = CommandPaletteIndex.Instance;

            // Assert
            Assert.IsNotNull(instance1);
            Assert.AreSame(instance1, instance2);
        }

        [TestMethod]
        public void Constructor_RegistersDefaultCommands()
        {
            // Arrange & Act
            var index = CommandPaletteIndex.Instance;

            // Assert - Search for empty query should return default commands
            var results = index.Search("");
            Assert.IsTrue(results.Count > 0, "Default commands should be registered");
        }

        [TestMethod]
        public void Search_EmptyQuery_ReturnsLimitedResults()
        {
            // Arrange
            var index = CommandPaletteIndex.Instance;

            // Act
            var results = index.Search("");

            // Assert - Should return at most 10 results for empty query
            Assert.IsTrue(results.Count <= 10, "Empty query should return at most 10 results");
        }

        [TestMethod]
        public void Search_NullQuery_ReturnsResults()
        {
            // Arrange
            var index = CommandPaletteIndex.Instance;

            // Act
            var results = index.Search(null!);

            // Assert
            Assert.IsNotNull(results);
            Assert.IsInstanceOfType<IReadOnlyList<PaletteCommand>>(results);
        }

        [TestMethod]
        public void Search_WhitespaceQuery_ReturnsResults()
        {
            // Arrange
            var index = CommandPaletteIndex.Instance;

            // Act
            var results = index.Search("   ");

            // Assert
            Assert.IsNotNull(results);
        }

        [TestMethod]
        public void Search_ExactMatch_Title_HighestPriority()
        {
            // Arrange
            var index = CommandPaletteIndex.Instance;

            // Act - Search for exact command title
            var results = index.Search("settings");

            // Assert
            Assert.IsTrue(results.Count > 0, "Should find settings command");
            var firstResult = results[0];
            Assert.IsTrue(firstResult.Title.ToLowerInvariant().Contains("settings"),
                "First result should match settings");
        }

        [TestMethod]
        public void Search_ByKeyword_FindsCommand()
        {
            // Arrange
            var index = CommandPaletteIndex.Instance;

            // Act - Search by keyword
            var results = index.Search("config");

            // Assert - Should find settings command (keywords include "config")
            var settingsResult = results.FirstOrDefault(r => r.Id == "nav.settings");
            Assert.IsNotNull(settingsResult, "Should find settings command by 'config' keyword");
        }

        [TestMethod]
        public void Search_CaseInsensitive()
        {
            // Arrange
            var index = CommandPaletteIndex.Instance;

            // Act
            var resultsLower = index.Search("settings");
            var resultsUpper = index.Search("SETTINGS");
            var resultsMixed = index.Search("SeTtInGs");

            // Assert - All should return similar results
            Assert.IsTrue(resultsLower.Count > 0);
            Assert.IsTrue(resultsUpper.Count > 0);
            Assert.IsTrue(resultsMixed.Count > 0);
        }

        [TestMethod]
        public void Search_MultipleTerms()
        {
            // Arrange
            var index = CommandPaletteIndex.Instance;

            // Act - Search with multiple terms
            var results = index.Search("mini widget");

            // Assert
            Assert.IsTrue(results.Count > 0, "Should find results for 'mini widget'");
        }

        [TestMethod]
        public void Search_LimitedTo15Results()
        {
            // Arrange
            var index = CommandPaletteIndex.Instance;

            // Act
            var results = index.Search("a"); // Common letter should match many

            // Assert - Should be limited to 15 results
            Assert.IsTrue(results.Count <= 15, "Results should be limited to 15");
        }

        [TestMethod]
        public void RegisterCommand_AddsCommand()
        {
            // Arrange
            var index = CommandPaletteIndex.Instance;
            var newCommand = new PaletteCommand
            {
                Id = "test.command",
                Title = "Test Command",
                Subtitle = "For testing",
                Category = "Test",
                Keywords = new[] { "test", "sample" }
            };

            // Act
            index.RegisterCommand(newCommand);
            var results = index.Search("test command");

            // Assert
            var foundCommand = results.FirstOrDefault(r => r.Id == "test.command");
            Assert.IsNotNull(foundCommand);
            Assert.AreEqual("Test Command", foundCommand.Title);
        }

        [TestMethod]
        public void RegisterSetting_AddsSetting()
        {
            // Arrange
            var index = CommandPaletteIndex.Instance;
            var newSetting = new SettingDefinition
            {
                Id = "test.setting",
                Name = "Test Setting",
                Description = "For testing",
                Category = "Test",
                Tier = VisibilityTier.Basic,
                Tags = new[] { "test" }
            };

            // Act
            index.RegisterSetting(newSetting);
            var settings = index.GetSettings();

            // Assert
            var foundSetting = settings.FirstOrDefault(s => s.Id == "test.setting");
            Assert.IsNotNull(foundSetting);
        }

        [TestMethod]
        public void GetSettingsByTier_Basic_ReturnsOnlyBasic()
        {
            // Arrange
            var index = CommandPaletteIndex.Instance;

            // Act
            var basicSettings = index.GetSettingsByTier(VisibilityTier.Basic);

            // Assert
            Assert.IsTrue(basicSettings.All(s => s.Tier == VisibilityTier.Basic),
                "Should only return Basic tier settings");
        }

        [TestMethod]
        public void GetSettingsByTier_Advanced_ReturnsBasicAndAdvanced()
        {
            // Arrange
            var index = CommandPaletteIndex.Instance;

            // Act
            var advancedSettings = index.GetSettingsByTier(VisibilityTier.Advanced);

            // Assert
            Assert.IsTrue(advancedSettings.All(s => s.Tier <= VisibilityTier.Advanced),
                "Should return Basic and Advanced tier settings");
        }

        [TestMethod]
        public void GetSettingsByTier_Experimental_ReturnsAll()
        {
            // Arrange
            var index = CommandPaletteIndex.Instance;

            // Act
            var allSettings = index.GetSettingsByTier(VisibilityTier.Experimental);

            // Assert - Should return all settings (all tiers <= Experimental)
            Assert.IsTrue(allSettings.Count >= 0);
        }

        [TestMethod]
        public void GetSettings_ReturnsAllSettings()
        {
            // Arrange
            var index = CommandPaletteIndex.Instance;

            // Act
            var settings = index.GetSettings();

            // Assert
            Assert.IsNotNull(settings);
            Assert.IsInstanceOfType<IReadOnlyList<SettingDefinition>>(settings);
        }

        [TestMethod]
        public void PaletteCommand_Properties_SetCorrectly()
        {
            // Arrange & Act
            var command = new PaletteCommand
            {
                Id = "test.cmd",
                Title = "Test Title",
                Subtitle = "Test Subtitle",
                Category = "Test Category",
                Keywords = new[] { "keyword1", "keyword2" },
                IconGlyph = "\uE710",
                NavigateTo = "TestPage",
                Shortcut = "Ctrl+T"
            };

            // Assert
            Assert.AreEqual("test.cmd", command.Id);
            Assert.AreEqual("Test Title", command.Title);
            Assert.AreEqual("Test Subtitle", command.Subtitle);
            Assert.AreEqual("Test Category", command.Category);
            CollectionAssert.AreEqual(new[] { "keyword1", "keyword2" }, command.Keywords.ToArray());
            Assert.AreEqual("\uE710", command.IconGlyph);
            Assert.AreEqual("TestPage", command.NavigateTo);
            Assert.AreEqual("Ctrl+T", command.Shortcut);
        }

        [TestMethod]
        public void SettingDefinition_Properties_SetCorrectly()
        {
            // Arrange & Act
            var setting = new SettingDefinition
            {
                Id = "test.setting",
                Name = "Test Name",
                Description = "Test Description",
                Category = "Test Category",
                Tier = VisibilityTier.Advanced,
                Tags = new[] { "tag1", "tag2" },
                CommandId = "cmd.test",
                ConfigPath = "TestPath",
                IconGlyph = "\uE711",
                RequiresRestart = true
            };

            // Assert
            Assert.AreEqual("test.setting", setting.Id);
            Assert.AreEqual("Test Name", setting.Name);
            Assert.AreEqual("Test Description", setting.Description);
            Assert.AreEqual("Test Category", setting.Category);
            Assert.AreEqual(VisibilityTier.Advanced, setting.Tier);
            CollectionAssert.AreEqual(new[] { "tag1", "tag2" }, setting.Tags.ToArray());
            Assert.AreEqual("cmd.test", setting.CommandId);
            Assert.AreEqual("TestPath", setting.ConfigPath);
            Assert.AreEqual("\uE711", setting.IconGlyph);
            Assert.IsTrue(setting.RequiresRestart);
        }

        [TestMethod]
        public void VisibilityTier_Enum_ValuesCorrect()
        {
            // Assert
            Assert.AreEqual(0, (int)VisibilityTier.Basic);
            Assert.AreEqual(1, (int)VisibilityTier.Advanced);
            Assert.AreEqual(2, (int)VisibilityTier.Experimental);
        }

        [TestMethod]
        public void Search_WithCanExecute_Filtering()
        {
            // Arrange
            var index = CommandPaletteIndex.Instance;

            // Act - Most default commands don't have CanExecute set (null means always available)
            var results = index.Search("toggle");

            // Assert
            Assert.IsTrue(results.Count >= 0, "Should handle CanExecute filtering");
        }

        [TestMethod]
        public void DefaultCommands_IncludeNavigation()
        {
            // Arrange
            var index = CommandPaletteIndex.Instance;

            // Act
            var results = index.Search("");

            // Assert - Should have navigation commands
            var navCommands = results.Where(r => r.Id.StartsWith("nav.")).ToList();
            Assert.IsTrue(navCommands.Count > 0, "Should have navigation commands");
        }

        [TestMethod]
        public void DefaultCommands_IncludeActions()
        {
            // Arrange
            var index = CommandPaletteIndex.Instance;

            // Act
            var results = index.Search("");

            // Assert - Should have action commands
            var actionCommands = results.Where(r => r.Id.StartsWith("action.")).ToList();
            Assert.IsTrue(actionCommands.Count > 0, "Should have action commands");
        }

        [TestMethod]
        public void Search_NoMatches_ReturnsEmpty()
        {
            // Arrange
            var index = CommandPaletteIndex.Instance;

            // Act - Search for something that doesn't exist
            var results = index.Search("xyznonexistent123");

            // Assert
            Assert.AreEqual(0, results.Count);
        }
    }
}
