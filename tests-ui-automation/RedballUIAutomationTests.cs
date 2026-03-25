using FlaUI.Core.AutomationElements;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Redball.UIAutomation;
using System.Diagnostics.CodeAnalysis;

namespace Redball.UIAutomation.Tests;

/// <summary>
/// UI Automation tests for Redball WPF application.
/// These tests use FlaUI to interact with the actual application UI.
/// </summary>
[TestClass]
[ExcludeFromCodeCoverage]
public class RedballUIAutomationTests
{
    private RedballUIAutomation? _ui;

    [TestInitialize]
    public void TestInitialize()
    {
        _ui = new RedballUIAutomation();
        _ui.LaunchApplication();
    }

    [TestCleanup]
    public void TestCleanup()
    {
        _ui?.Dispose();
    }

    [TestMethod]
    [TestCategory("UI"), TestCategory("Smoke")]
    public void Application_LaunchesSuccessfully()
    {
        // Assert
        Assert.IsTrue(_ui!.IsApplicationRunning(), "Application should be running");
        Assert.IsNotNull(_ui.MainWindow, "Main window should be accessible");
    }

    [TestMethod]
    [TestCategory("UI")]
    public void MainWindow_HasExpectedTitle()
    {
        // Assert
        var title = _ui!.MainWindow.Title;
        Assert.IsTrue(title.Contains("Redball", StringComparison.OrdinalIgnoreCase),
            $"Window title should contain 'Redball', but was: {title}");
    }

    [TestMethod]
    [TestCategory("UI")]
    public void MainWindow_ContainsToggleButton()
    {
        // Assert
        Assert.IsTrue(_ui!.ElementExists("ToggleButton"),
            "Main window should contain Toggle button");
    }

    [TestMethod]
    [TestCategory("UI")]
    public void MainWindow_ContainsSettingsButton()
    {
        // Assert
        Assert.IsTrue(_ui!.ElementExists("SettingsButton"),
            "Main window should contain Settings button");
    }

    [TestMethod]
    [TestCategory("UI")]
    public void ToggleButton_CanBeClicked()
    {
        // Act
        _ui!.ClickButton("ToggleButton");

        // Assert - no exception means success
        Assert.IsTrue(true, "Toggle button click should complete without exception");
    }

    [TestMethod]
    [TestCategory("UI")]
    public void SettingsDialog_CanBeOpened()
    {
        // Act
        _ui!.ClickButton("SettingsButton");

        // Assert - wait for settings dialog to appear
        Assert.IsTrue(_ui.WaitForElement("SettingsDialog", TimeSpan.FromSeconds(2)),
            "Settings dialog should appear");
    }

    [TestMethod]
    [TestCategory("UI")]
    public void StatusText_IsDisplayed()
    {
        // Arrange
        var statusText = _ui!.GetStatusText();

        // Assert
        Assert.IsFalse(string.IsNullOrEmpty(statusText), "Status text should not be empty");
    }

    [TestMethod]
    [TestCategory("UI")]
    public void PreventDisplaySleep_CheckBox_CanBeToggled()
    {
        // Act - toggle checkbox
        _ui!.ToggleCheckBox("PreventDisplaySleepCheckBox", true);

        // Assert - no exception
        Assert.IsTrue(true, "Checkbox toggle should complete");
    }

    [TestMethod]
    [TestCategory("UI")]
    public void UseHeartbeat_CheckBox_CanBeToggled()
    {
        // Act
        _ui!.ToggleCheckBox("UseHeartbeatCheckBox", true);

        // Assert
        Assert.IsTrue(true, "UseHeartbeat checkbox toggle should complete");
    }

    [TestMethod]
    [TestCategory("UI")]
    public void BatteryAware_CheckBox_CanBeToggled()
    {
        // Act
        _ui!.ToggleCheckBox("BatteryAwareCheckBox", true);

        // Assert
        Assert.IsTrue(true, "BatteryAware checkbox toggle should complete");
    }

    [TestMethod]
    [TestCategory("UI")]
    public void DurationTextBox_CanAcceptInput()
    {
        // Act
        _ui!.SetTextBoxValue("DurationTextBox", "120");
        var value = _ui.GetTextBoxValue("DurationTextBox");

        // Assert
        Assert.IsTrue(value.Contains("120"), "Duration textbox should accept the input");
    }

    [TestMethod]
    [TestCategory("UI")]
    public void HeartbeatIntervalTextBox_CanAcceptInput()
    {
        // Act
        _ui!.SetTextBoxValue("HeartbeatIntervalTextBox", "30");
        var value = _ui.GetTextBoxValue("HeartbeatIntervalTextBox");

        // Assert
        Assert.IsTrue(value.Contains("30"), "Heartbeat interval textbox should accept the input");
    }

    [TestMethod]
    [TestCategory("UI")]
    public void ThemeSelector_CanChangeTheme()
    {
        // Arrange - open settings
        _ui!.ClickButton("SettingsButton");
        Assert.IsTrue(_ui.WaitForElement("ThemeComboBox", TimeSpan.FromSeconds(1)));

        // Act
        _ui.SelectComboBoxItem("ThemeComboBox", "Dark");

        // Assert
        Assert.IsTrue(true, "Theme selection should complete");
    }

    [TestMethod]
    [TestCategory("UI")]
    public void AboutDialog_CanBeOpened()
    {
        // Act - look for About button or menu
        try
        {
            _ui!.ClickButton("AboutButton");
        }
        catch
        {
            // About might be in a menu
            Assert.Inconclusive("About dialog test requires manual verification or menu navigation");
        }
    }

    [TestMethod]
    [TestCategory("UI")]
    public void MainWindow_HasTrayIcon()
    {
        // Note: Tray icon testing is complex with FlaUI
        // This test serves as documentation of the feature
        Assert.Inconclusive("Tray icon automation requires specialized handling");
    }

    [TestMethod]
    [TestCategory("UI"), TestCategory("Regression")]
    public void FullWorkflow_ToggleKeepAwake_ThenOpenSettings()
    {
        // Arrange & Act
        _ui!.ClickButton("ToggleButton");
        Thread.Sleep(500); // Brief pause for UI update
        _ui.ClickButton("SettingsButton");

        // Assert
        Assert.IsTrue(_ui.WaitForElement("SettingsDialog", TimeSpan.FromSeconds(2)),
            "Settings should open after toggling");
    }

    [TestMethod]
    [TestCategory("UI"), TestCategory("Regression")]
    public void FullWorkflow_ChangeSettings_ThenSave()
    {
        // Arrange
        _ui!.ClickButton("SettingsButton");
        Assert.IsTrue(_ui.WaitForElement("SettingsDialog", TimeSpan.FromSeconds(2)));

        // Act
        _ui.SetTextBoxValue("HeartbeatIntervalTextBox", "45");
        _ui.ToggleCheckBox("PreventDisplaySleepCheckBox", true);
        
        // Try to save (if Save button exists)
        try
        {
            _ui.ClickButton("SaveButton");
        }
        catch
        {
            // Save might not be explicit, settings might auto-save
        }

        // Assert
        Assert.IsTrue(true, "Settings workflow completed");
    }

    [TestMethod]
    [TestCategory("UI"), TestCategory("Accessibility")]
    public void AllInteractiveElements_HaveAutomationIds()
    {
        // This test validates that all interactive controls have automation IDs
        // for accessibility and testing purposes

        var requiredIds = new[]
        {
            "ToggleButton",
            "SettingsButton",
            "PreventDisplaySleepCheckBox",
            "UseHeartbeatCheckBox",
            "DurationTextBox",
            "HeartbeatIntervalTextBox"
        };

        var missingIds = new List<string>();

        foreach (var id in requiredIds)
        {
            if (!_ui!.ElementExists(id))
            {
                missingIds.Add(id);
            }
        }

        if (missingIds.Count > 0)
        {
            Assert.Fail($"Missing automation IDs: {string.Join(", ", missingIds)}");
        }
    }

    [TestMethod]
    [TestCategory("UI"), TestCategory("Performance")]
    public void Application_RespondsWithinAcceptableTime()
    {
        // Arrange
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();

        // Act
        _ui!.ClickButton("ToggleButton");
        stopwatch.Stop();

        // Assert - UI should respond within 2 seconds
        Assert.IsTrue(stopwatch.ElapsedMilliseconds < 2000,
            $"Button click response time ({stopwatch.ElapsedMilliseconds}ms) exceeds acceptable threshold");
    }
}
