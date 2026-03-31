using FlaUI.Core;
using FlaUI.Core.AutomationElements;
using FlaUI.UIA3;
using NUnit.Framework;
using System.IO;
using System.Threading;
using FlaApplication = FlaUI.Core.Application;

namespace Redball.E2E.Tests;

[TestFixture]
public class MainWindowTests
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
            throw new FileNotFoundException("Redball.UI.WPF.exe not found. Build the project first. Searched in common locations.");
        }

        _automation = new UIA3Automation();
        _app = FlaApplication.Launch(appPath, "--test-mode");
        
        // Wait for the window with a timeout
        _mainWindow = _app.GetMainWindow(_automation, TimeSpan.FromSeconds(15));
        
        if (_mainWindow == null)
        {
            throw new Exception("Main window not found within 15 seconds. Check if the app is hanging.");
        }
        
        _mainWindow.SetForeground();
    }

    private string? FindAppPath()
    {
        // 1. Check same directory
        var path = Path.Combine(AppContext.BaseDirectory, "Redball.UI.WPF.exe");
        if (File.Exists(path)) return path;

        // 2. Check win-x64 runtime subfolder (where WPF app builds with RuntimeIdentifier)
        path = Path.Combine(AppContext.BaseDirectory, "win-x64", "Redball.UI.WPF.exe");
        if (File.Exists(path)) return path;

        // 3. Check sibling publish folder (dist/wpf-publish)
        path = Path.Combine(AppContext.BaseDirectory, "..", "wpf-publish", "Redball.UI.WPF.exe");
        if (File.Exists(path)) return Path.GetFullPath(path);

        // 4. Check dev paths (3-5 levels up from bin/Release/net8.0-windows/...)
        var levels = new[]
        {
            Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "src", "Redball.UI.WPF", "bin", "Release", "net10.0-windows", "win-x64", "Redball.UI.WPF.exe"),
            Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "src", "Redball.UI.WPF", "bin", "Debug", "net10.0-windows", "win-x64", "Redball.UI.WPF.exe"),
            Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "Redball.UI.WPF", "bin", "Release", "net10.0-windows", "win-x64", "Redball.UI.WPF.exe"),
            Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "Redball.UI.WPF", "bin", "Debug", "net10.0-windows", "win-x64", "Redball.UI.WPF.exe")
        };

        foreach (var p in levels)
        {
            if (File.Exists(p)) return Path.GetFullPath(p);
        }

        return null;
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
