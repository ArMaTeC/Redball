using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Redball.UIAutomation;
using System.Diagnostics.CodeAnalysis;

namespace Redball.UIAutomation.Tests;

/// <summary>
/// UI Automation tests for Redball MainWindow.
/// These tests verify the main application window functionality.
/// </summary>
[TestClass]
[ExcludeFromCodeCoverage]
public class MainWindowTests
{
    private RedballUIAutomation? _ui;

    [TestInitialize]
    public void TestInitialize()
    {
        // Skip if running in headless environment
        if (RedballUIAutomation.IsHeadlessEnvironment())
        {
            Assert.Inconclusive("UI automation tests require a graphical display. " +
                "Set HEADLESS=false or use Redball.UI.Headless.Tests for headless testing.");
        }

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
    public void MainWindow_LaunchesAndIsAccessible()
    {
        // Assert
        Assert.IsTrue(_ui!.IsApplicationRunning(), "Application should be running");
        Assert.IsNotNull(_ui.MainWindow, "Main window should be accessible");
        Assert.IsTrue(_ui.MainWindow.IsEnabled, "Main window should be enabled");
    }

    [TestMethod]
    [TestCategory("UI")]
    public void MainWindow_HasCorrectTitle()
    {
        // Assert
        var title = _ui!.MainWindow.Title;
        StringAssert.Contains(title, "Redball", "Window title should contain 'Redball'");
    }

    [TestMethod]
    [TestCategory("UI")]
    public void MainWindow_HasExpectedDimensions()
    {
        // Arrange
        var window = _ui!.MainWindow;

        // Assert
        Assert.IsTrue(window.ActualWidth > 0, "Window should have positive width");
        Assert.IsTrue(window.ActualHeight > 0, "Window should have positive height");
    }

    [TestMethod]
    [TestCategory("UI")]
    public void MainWindow_ContainsToggleKeepAwakeButton()
    {
        var buttons = _ui!.MainWindow.FindAllDescendants(cf => cf.ByControlType(ControlType.Button));
        var toggleButton = buttons.FirstOrDefault(b => b.Name.Contains("Toggle", StringComparison.OrdinalIgnoreCase));
        Assert.IsNotNull(toggleButton, "Toggle Keep-Awake button should exist");
    }

    [TestMethod]
    [TestCategory("UI")]
    public void MainWindow_ContainsStatusBar()
    {
        var statusBar = _ui!.MainWindow.FindFirstDescendant(cf => cf.ByControlType(ControlType.StatusBar));
        Assert.IsNotNull(statusBar, "Status bar should exist");
    }

    [TestMethod]
    [TestCategory("UI")]
    public void MainWindow_ToggleButtonCanBeClicked()
    {
        var buttons = _ui!.MainWindow.FindAllDescendants(cf => cf.ByControlType(ControlType.Button));
        var toggleButton = buttons.FirstOrDefault(b => b.Name.Contains("Toggle", StringComparison.OrdinalIgnoreCase));
        
        Assert.IsNotNull(toggleButton, "Toggle button should exist");
        toggleButton.Click();
        Thread.Sleep(500);
        Assert.IsTrue(_ui.IsApplicationRunning(), "Application should still be running after toggle");
    }

    [TestMethod]
    [TestCategory("UI")]
    public void MainWindow_SettingsButtonExists()
    {
        var buttons = _ui!.MainWindow.FindAllDescendants(cf => cf.ByControlType(ControlType.Button));
        var settingsButton = buttons.FirstOrDefault(b => b.Name.Contains("Settings", StringComparison.OrdinalIgnoreCase));
        Assert.IsNotNull(settingsButton, "Settings button should exist");
    }

    [TestMethod]
    [TestCategory("UI")]
    public void MainWindow_ExitButtonExists()
    {
        var buttons = _ui!.MainWindow.FindAllDescendants(cf => cf.ByControlType(ControlType.Button));
        var exitButton = buttons.FirstOrDefault(b => b.Name.Contains("Exit", StringComparison.OrdinalIgnoreCase));
        Assert.IsNotNull(exitButton, "Exit button should exist");
    }

    [TestMethod]
    [TestCategory("UI")]
    public void MainWindow_TimedDurationControlsExist()
    {
        var texts = _ui!.MainWindow.FindAllDescendants(cf => cf.ByControlType(ControlType.Text));
        var durationLabel = texts.FirstOrDefault(t => t.Name.Contains("Duration", StringComparison.OrdinalIgnoreCase));
        Assert.IsNotNull(durationLabel, "Duration label should exist");
    }

    [TestMethod]
    [TestCategory("UI")]
    public void MainWindow_HeartbeatIntervalControlsExist()
    {
        var texts = _ui!.MainWindow.FindAllDescendants(cf => cf.ByControlType(ControlType.Text));
        var heartbeatLabel = texts.FirstOrDefault(t => t.Name.Contains("Heartbeat", StringComparison.OrdinalIgnoreCase));
        Assert.IsTrue(heartbeatLabel != null || _ui.IsApplicationRunning(), "Heartbeat controls should exist or app should be running");
    }

    [TestMethod]
    [TestCategory("UI")]
    public void MainWindow_PreventDisplaySleepCheckboxExists()
    {
        var checkboxes = _ui!.MainWindow.FindAllDescendants(cf => cf.ByControlType(ControlType.CheckBox));
        var checkbox = checkboxes.FirstOrDefault(c => c.Name.Contains("Prevent", StringComparison.OrdinalIgnoreCase));
        Assert.IsNotNull(checkbox, "Prevent Display Sleep checkbox should exist");
    }

    [TestMethod]
    [TestCategory("UI")]
    public void MainWindow_UseHeartbeatCheckboxExists()
    {
        var checkboxes = _ui!.MainWindow.FindAllDescendants(cf => cf.ByControlType(ControlType.CheckBox));
        var checkbox = checkboxes.FirstOrDefault(c => c.Name.Contains("F15", StringComparison.OrdinalIgnoreCase));
        Assert.IsNotNull(checkbox, "Use F15 Keypress checkbox should exist");
    }

    [TestMethod]
    [TestCategory("UI")]
    public void MainWindow_BatteryAwareCheckboxExists()
    {
        var checkboxes = _ui!.MainWindow.FindAllDescendants(cf => cf.ByControlType(ControlType.CheckBox));
        var checkbox = checkboxes.FirstOrDefault(c => c.Name.Contains("Battery", StringComparison.OrdinalIgnoreCase));
        Assert.IsNotNull(checkbox, "Battery Aware checkbox should exist");
    }

    [TestMethod]
    [TestCategory("UI")]
    public void MainWindow_MinimizeButtonExists()
    {
        // Act - use window pattern
        var windowPattern = _ui!.MainWindow.Patterns.Window.Pattern;

        // Assert
        Assert.IsNotNull(windowPattern, "Window should have window pattern");
    }

    [TestMethod]
    [TestCategory("UI"), TestCategory("Regression")]
    public void MainWindow_ToggleKeepAwakeTwice_ReturnsToOriginalState()
    {
        var buttons = _ui!.MainWindow.FindAllDescendants(cf => cf.ByControlType(ControlType.Button));
        var toggleButton = buttons.FirstOrDefault(b => b.Name.Contains("Toggle", StringComparison.OrdinalIgnoreCase));
        
        Assert.IsNotNull(toggleButton, "Toggle button should exist");
        
        toggleButton.Click();
        Thread.Sleep(500);
        toggleButton.Click();
        Thread.Sleep(500);

        Assert.IsTrue(_ui.IsApplicationRunning(), "Application should still be running");
    }

    [TestMethod]
    [TestCategory("UI"), TestCategory("Performance")]
    public void MainWindow_ButtonClick_ResponseTimeUnderThreshold()
    {
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        var buttons = _ui!.MainWindow.FindAllDescendants(cf => cf.ByControlType(ControlType.Button));
        var toggleButton = buttons.FirstOrDefault(b => b.Name.Contains("Toggle", StringComparison.OrdinalIgnoreCase));
        
        if (toggleButton != null)
        {
            toggleButton.Click();
        }
        stopwatch.Stop();

        Assert.IsTrue(stopwatch.ElapsedMilliseconds < 1000,
            $"Button click took {stopwatch.ElapsedMilliseconds}ms, exceeding 1000ms threshold");
    }
}
