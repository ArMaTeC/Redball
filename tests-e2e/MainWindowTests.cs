using FlaUI.Core;
using FlaUI.Core.AutomationElements;
using FlaUI.UIA3;
using NUnit.Framework;

namespace Redball.E2E.Tests;

[TestFixture]
public class MainWindowTests
{
    private Application? _app;
    private UIA3Automation? _automation;
    private Window? _mainWindow;

    [SetUp]
    public void SetUp()
    {
        var appPath = Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..", "..", "..",
            "src", "Redball.UI.WPF", "bin", "Release", "net8.0-windows",
            "Redball.UI.WPF.exe"
        );
        
        if (!File.Exists(appPath))
        {
            appPath = Path.Combine(
                AppContext.BaseDirectory,
                "..", "..", "..", "..", "..",
                "src", "Redball.UI.WPF", "bin", "Debug", "net8.0-windows",
                "Redball.UI.WPF.exe"
            );
        }

        if (!File.Exists(appPath))
        {
            throw new FileNotFoundException("Redball.UI.WPF.exe not found. Build the project first.");
        }

        _automation = new UIA3Automation();
        _app = Application.Launch(appPath);
        _mainWindow = _app.GetMainWindow(_automation);
    }

    [TearDown]
    public void TearDown()
    {
        _mainWindow?.Close();
        _app?.Close();
        _automation?.Dispose();
    }

    [Test]
    public void MainWindow_ShouldBeVisible()
    {
        Assert.That(_mainWindow, Is.Not.Null);
        Assert.That(_mainWindow.IsEnabled, Is.True);
    }

    [Test]
    public void MainWindow_ShouldHaveTitle()
    {
        Assert.That(_mainWindow?.Title, Does.Contain("Redball"));
    }

    [Test]
    public void ToggleButton_ShouldExist()
    {
        var toggleButton = _mainWindow?.FindFirstDescendant(cf => cf.ByAutomationId("ToggleButton"))?.AsButton();
        
        Assert.That(toggleButton, Is.Not.Null, "Toggle button not found");
        Assert.That(toggleButton.IsEnabled, Is.True);
    }

    [Test]
    public void ToggleButton_Click_ShouldChangeState()
    {
        var toggleButton = _mainWindow?.FindFirstDescendant(cf => cf.ByAutomationId("ToggleButton"))?.AsButton();
        var statusText = _mainWindow?.FindFirstDescendant(cf => cf.ByAutomationId("StatusTextBlock"))?.AsLabel();

        Assert.That(toggleButton, Is.Not.Null);
        Assert.That(statusText, Is.Not.Null);

        var initialStatus = statusText.Text;
        
        toggleButton.Click();
        Thread.Sleep(500);

        var newStatus = statusText.Text;
        Assert.That(newStatus, Is.Not.EqualTo(initialStatus), "Status should change after toggle");
    }

    [Test]
    public void SettingsButton_ShouldOpenSettings()
    {
        var settingsButton = _mainWindow?.FindFirstDescendant(cf => cf.ByAutomationId("SettingsButton"))?.AsButton();
        Assert.That(settingsButton, Is.Not.Null, "Settings button not found");

        settingsButton.Click();
        Thread.Sleep(1000);

        var settingsWindow = _automation?.GetDesktop().FindFirstDescendant(cf => cf.ByName("Settings"))?.AsWindow();
        Assert.That(settingsWindow, Is.Not.Null, "Settings window should open");
        
        settingsWindow.Close();
    }

    [Test]
    public void AboutButton_ShouldOpenAboutDialog()
    {
        var aboutButton = _mainWindow?.FindFirstDescendant(cf => cf.ByAutomationId("AboutButton"))?.AsButton();
        Assert.That(aboutButton, Is.Not.Null, "About button not found");

        aboutButton.Click();
        Thread.Sleep(1000);

        var aboutWindow = _automation?.GetDesktop().FindFirstDescendant(cf => cf.ByName("About Redball"))?.AsWindow();
        Assert.That(aboutWindow, Is.Not.Null, "About window should open");
        
        aboutWindow.Close();
    }
}
