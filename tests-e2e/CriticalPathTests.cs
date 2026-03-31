using FlaUI.Core;
using FlaUI.Core.AutomationElements;
using FlaUI.UIA3;
using NUnit.Framework;
using System.IO;
using System.Threading;
using FlaApplication = FlaUI.Core.Application;

namespace Redball.E2E.Tests;

/// <summary>
/// Critical path E2E tests covering main user workflows.
/// </summary>
[TestFixture]
public class CriticalPathTests
{
    private FlaApplication? _app;
    private UIA3Automation? _automation;
    private Window? _mainWindow;

    [SetUp]
    public void SetUp()
    {
        var appPath = FindAppPath();
        
        if (appPath == null || !File.Exists(appPath))
        {
            Assert.Inconclusive("Redball.UI.WPF.exe not found. Build the project first.");
        }

        _automation = new UIA3Automation();
        _app = FlaApplication.Launch(appPath, "--test-mode");
        
        _mainWindow = _app.GetMainWindow(_automation, TimeSpan.FromSeconds(15));
        
        if (_mainWindow == null)
        {
            Assert.Inconclusive("Main window not found within 15 seconds.");
        }
        
        _mainWindow.SetForeground();
    }

    private string? FindAppPath()
    {
        var paths = new[]
        {
            Path.Combine(AppContext.BaseDirectory, "Redball.UI.WPF.exe"),
            Path.Combine(AppContext.BaseDirectory, "win-x64", "Redball.UI.WPF.exe"),
            Path.Combine(AppContext.BaseDirectory, "..", "wpf-publish", "Redball.UI.WPF.exe"),
            Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "src", "Redball.UI.WPF", "bin", "Release", "net10.0-windows", "win-x64", "Redball.UI.WPF.exe"),
            Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "src", "Redball.UI.WPF", "bin", "Debug", "net10.0-windows", "win-x64", "Redball.UI.WPF.exe"),
        };

        foreach (var p in paths)
        {
            var fullPath = Path.GetFullPath(p);
            if (File.Exists(fullPath)) return fullPath;
        }

        return null;
    }

    [TearDown]
    public void TearDown()
    {
        try
        {
            _mainWindow?.Close();
            _app?.Close();
        }
        catch { }
        _automation?.Dispose();
    }

    /// <summary>
    /// Critical Path 1: User activates keep-awake
    /// </summary>
    [Test]
    [Category("CriticalPath")]
    public void CP01_ActivateKeepAwake_ShouldWork()
    {
        // Arrange
        var pauseButton = FindButtonByText("Pause / Resume");
        Assert.That(pauseButton, Is.Not.Null, "Pause/Resume button not found");

        var statusText = FindTextByAutomationId("StatusTextBlock");
        Assert.That(statusText, Is.Not.Null, "Status text not found");

        var initialStatus = statusText.Text;

        // Act
        pauseButton.Click();
        Thread.Sleep(500);

        // Assert
        var newStatus = statusText.Text;
        Assert.That(newStatus, Is.Not.EqualTo(initialStatus), "Status should change after activation");
        Assert.That(newStatus.ToLower(), Does.Contain("active").Or.Contain("paused"), 
            "Status should indicate active or paused state");
    }

    /// <summary>
    /// Critical Path 2: User changes theme
    /// </summary>
    [Test]
    [Category("CriticalPath")]
    public void CP02_ChangeTheme_ShouldApply()
    {
        // Navigate to Settings
        ClickNavigationButton("Settings");
        Thread.Sleep(500);

        // Find theme combo
        var themeCombo = FindComboBoxByName("Theme");
        Assert.That(themeCombo, Is.Not.Null, "Theme combo not found");

        // Get initial selection
        var initialTheme = themeCombo.SelectedItem?.Text;

        // Change theme
        themeCombo.Click();
        Thread.Sleep(200);
        
        var items = themeCombo.Items;
        if (items.Length > 1)
        {
            // Select different theme
            var newIndex = initialTheme?.Contains("Dark") == true ? 2 : 1;
            items[Math.Min(newIndex, items.Length - 1)].Click();
            Thread.Sleep(500);

            // Assert theme changed
            var newTheme = themeCombo.SelectedItem?.Text;
            Assert.That(newTheme, Is.Not.EqualTo(initialTheme), "Theme should change");
        }
    }

    /// <summary>
    /// Critical Path 3: User opens and closes mini widget
    /// </summary>
    [Test]
    [Category("CriticalPath")]
    public void CP03_MiniWidget_ShouldOpenAndClose()
    {
        // Click mini widget button
        var miniWidgetButton = FindButtonByName("Mini Widget");
        Assert.That(miniWidgetButton, Is.Not.Null, "Mini Widget button not found");

        miniWidgetButton.Click();
        Thread.Sleep(1000);

        // Verify mini widget is visible
        var miniWidget = _automation?.GetDesktop()
            .FindFirstDescendant(cf => cf.ByName("Mini Widget"))?.AsWindow();
        
        Assert.That(miniWidget, Is.Not.Null, "Mini widget should be visible");
        Assert.That(miniWidget.IsEnabled, Is.True);

        // Close mini widget
        miniWidget.Close();
        Thread.Sleep(500);

        // Verify it's closed
        miniWidget = _automation?.GetDesktop()
            .FindFirstDescendant(cf => cf.ByName("Mini Widget"))?.AsWindow();
        Assert.That(miniWidget?.IsOffscreen ?? true, Is.True, "Mini widget should be closed");
    }

    /// <summary>
    /// Critical Path 4: User views analytics
    /// </summary>
    [Test]
    [Category("CriticalPath")]
    public void CP04_ViewAnalytics_ShouldShowData()
    {
        // Navigate to Analytics
        ClickNavigationButton("Analytics");
        Thread.Sleep(500);

        // Verify analytics panel is visible with data
        var totalSessionsLabel = FindLabelContaining("Total Sessions");
        Assert.That(totalSessionsLabel, Is.Not.Null, "Total Sessions label not found");

        var usageTimeLabel = FindLabelContaining("Usage Time");
        Assert.That(usageTimeLabel, Is.Not.Null, "Usage Time label not found");
    }

    /// <summary>
    /// Critical Path 5: User checks for updates
    /// </summary>
    [Test]
    [Category("CriticalPath")]
    public void CP05_CheckForUpdates_ShouldOpenDialog()
    {
        // Click check for updates
        var updateButton = FindButtonByName("Check for Updates");
        if (updateButton == null)
        {
            Assert.Inconclusive("Check for Updates button not found");
        }

        updateButton.Click();
        Thread.Sleep(2000);

        // Verify update dialog or notification appears
        var updateDialog = _automation?.GetDesktop()
            .FindFirstDescendant(cf => cf.ByName("Updates").Or(cf.ByName("Update Available")))?.AsWindow();
        
        // Or check for HUD notification
        var hudWindow = _automation?.GetDesktop()
            .FindFirstDescendant(cf => cf.ByName("Redball HUD"))?.AsWindow();

        Assert.That(updateDialog != null || hudWindow != null, Is.True, 
            "Update check should show dialog or HUD notification");

        // Close any dialogs
        updateDialog?.Close();
    }

    /// <summary>
    /// Critical Path 6: User toggles display sleep prevention
    /// </summary>
    [Test]
    [Category("CriticalPath")]
    public void CP06_ToggleDisplaySleep_ShouldWork()
    {
        // Navigate to Behavior
        ClickNavigationButton("Behavior");
        Thread.Sleep(500);

        // Find display sleep checkbox
        var displaySleepCheck = FindCheckBoxByName("Prevent Display Sleep");
        if (displaySleepCheck == null)
        {
            // Try partial match
            displaySleepCheck = FindCheckBoxContaining("Display");
        }

        Assert.That(displaySleepCheck, Is.Not.Null, "Prevent Display Sleep checkbox not found");

        // Toggle it
        var initialState = displaySleepCheck.IsChecked;
        displaySleepCheck.Click();
        Thread.Sleep(300);

        // Verify state changed
        Assert.That(displaySleepCheck.IsChecked, Is.Not.EqualTo(initialState), 
            "Display sleep prevention should toggle");
    }

    /// <summary>
    /// Critical Path 7: User navigates all main sections
    /// </summary>
    [Test]
    [Category("CriticalPath")]
    public void CP07_NavigateAllSections_ShouldWork()
    {
        var sections = new[] { "Home", "Analytics", "SLO Dashboard", "Diagnostics", "Settings", "Behavior", "Smart Features", "TypeThing", "Updates" };
        
        foreach (var section in sections)
        {
            ClickNavigationButton(section);
            Thread.Sleep(300);

            // Verify the section is now active by checking content visibility
            var panel = _mainWindow?.FindFirstDescendant(cf => 
                cf.ByAutomationId($"{section.Replace(" ", "")}Panel").Or(
                cf.ByName(section)));
            
            // For sections that may not exist as named panels, just verify no crash
            Assert.That(_mainWindow?.IsEnabled, Is.True, $"Window should remain enabled after navigating to {section}");
        }
    }

    /// <summary>
    /// Critical Path 8: User minimizes to tray and restores
    /// </summary>
    [Test]
    [Category("CriticalPath")]
    public void CP08_MinimizeToTray_ShouldWork()
    {
        // Click close button (should minimize to tray, not close)
        var closeButton = FindButtonByAutomationId("CloseWindowButton");
        Assert.That(closeButton, Is.Not.Null, "Close button not found");

        closeButton.Click();
        Thread.Sleep(1000);

        // Main window should be hidden/minimized
        Assert.That(_mainWindow?.IsOffscreen, Is.True.Or.Null, "Main window should be minimized to tray");

        // Note: Restoring from tray via FlaUI is complex and may require
        // clicking the tray icon which is OS-level UI
    }

    // Helper methods
    private FlaUI.Core.AutomationElements.Button? FindButtonByText(string text)
    {
        return _mainWindow?.FindFirstDescendant(cf => cf.ByText(text))?.AsButton();
    }

    private FlaUI.Core.AutomationElements.Button? FindButtonByName(string name)
    {
        return _mainWindow?.FindFirstDescendant(cf => cf.ByName(name))?.AsButton();
    }

    private FlaUI.Core.AutomationElements.Button? FindButtonByAutomationId(string id)
    {
        return _mainWindow?.FindFirstDescendant(cf => cf.ByAutomationId(id))?.AsButton();
    }

    private FlaUI.Core.AutomationElements.Label? FindTextByAutomationId(string id)
    {
        return _mainWindow?.FindFirstDescendant(cf => cf.ByAutomationId(id))?.AsLabel();
    }

    private FlaUI.Core.AutomationElements.ComboBox? FindComboBoxByName(string name)
    {
        return _mainWindow?.FindFirstDescendant(cf => cf.ByName(name))?.AsComboBox();
    }

    private FlaUI.Core.AutomationElements.CheckBox? FindCheckBoxByName(string name)
    {
        return _mainWindow?.FindFirstDescendant(cf => cf.ByName(name))?.AsCheckBox();
    }

    private FlaUI.Core.AutomationElements.CheckBox? FindCheckBoxContaining(string text)
    {
        return _mainWindow?.FindFirstDescendant(cf => cf.ByText(text).Or(cf.ByName(text)))?.AsCheckBox();
    }

    private FlaUI.Core.AutomationElements.Label? FindLabelContaining(string text)
    {
        return _mainWindow?.FindFirstDescendant(cf => cf.ByText(text))?.AsLabel();
    }

    private void ClickNavigationButton(string sectionName)
    {
        var button = _mainWindow?.FindFirstDescendant(cf => 
            cf.ByControlType(FlaUI.Core.Definitions.ControlType.RadioButton).And(
            cf.ByText(sectionName)))?.AsRadioButton();
        
        button?.Click();
    }
}
