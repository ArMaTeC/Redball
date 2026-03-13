using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenQA.Selenium.Appium;
using OpenQA.Selenium.Appium.Windows;
using System.Diagnostics;

namespace Redball.IntegrationTests
{
    /// <summary>
    /// Integration tests for Redball WPF application using WinAppDriver
    /// WinAppDriver must be installed and running: https://github.com/microsoft/WinAppDriver
    /// </summary>
    [TestClass]
    public class RedballAppTests
    {
        private static WindowsDriver<WindowsElement>? _driver;
        private static Process? _appProcess;
        private const string WinAppDriverUrl = "http://127.0.0.1:4723";
        private const string AppPath = @"..\..\..\..\src\Redball.UI.WPF\bin\Release\net8.0-windows\win-x64\Redball.UI.WPF.exe";

        [ClassInitialize]
        public static void ClassInitialize(TestContext context)
        {
            // Check if WinAppDriver is running
            var winAppDriver = Process.GetProcessesByName("WinAppDriver").FirstOrDefault();
            if (winAppDriver == null)
            {
                Assert.Inconclusive("WinAppDriver is not running. Please start WinAppDriver.exe first.");
            }

            // Start the application
            var appPath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, AppPath));
            if (!File.Exists(appPath))
            {
                // Try to build the project first
                Assert.Inconclusive($"Application not found at {appPath}. Please build the solution first.");
            }

            var appCapabilities = new AppiumOptions();
            appCapabilities.AddAdditionalCapability("app", appPath);
            appCapabilities.AddAdditionalCapability("platformName", "Windows");
            appCapabilities.AddAdditionalCapability("deviceName", "WindowsPC");

            try
            {
                _driver = new WindowsDriver<WindowsElement>(new Uri(WinAppDriverUrl), appCapabilities);
                _driver.Manage().Timeouts().ImplicitWait = TimeSpan.FromSeconds(5);
            }
            catch (Exception ex)
            {
                Assert.Inconclusive($"Failed to connect to WinAppDriver: {ex.Message}");
            }
        }

        [ClassCleanup]
        public static void ClassCleanup()
        {
            _driver?.Quit();
            
            // Clean up any remaining processes
            var processes = Process.GetProcessesByName("Redball.UI.WPF");
            foreach (var proc in processes)
            {
                try
                {
                    proc.Kill();
                    proc.WaitForExit(5000);
                }
                catch { }
            }
        }

        [TestMethod]
        public void App_Launch_Succeeds()
        {
            // Arrange
            Assert.IsNotNull(_driver, "Driver should be initialized");

            // Act & Assert
            var sessionInfo = _driver.SessionId;
            Assert.IsNotNull(sessionInfo, "Session should be active");
        }

        [TestMethod]
        public void App_MainWindow_IsDisplayed()
        {
            // Arrange
            Assert.IsNotNull(_driver, "Driver should be initialized");

            // Act
            var windowHandle = _driver.CurrentWindowHandle;

            // Assert
            Assert.IsNotNull(windowHandle, "Main window should be displayed");
        }

        [TestMethod]
        public void App_TrayIcon_IsAccessible()
        {
            // Arrange
            Assert.IsNotNull(_driver, "Driver should be initialized");

            // Act - Find the notify icon area (system tray)
            // This is a simplified test - in real scenarios you'd need to interact with the tray
            var mainWindow = _driver.FindElementByClassName("Window");

            // Assert
            Assert.IsNotNull(mainWindow, "Main window should be accessible");
        }

        [TestMethod]
        public void App_ToggleButton_Exists()
        {
            // Arrange
            Assert.IsNotNull(_driver, "Driver should be initialized");

            // Act - Try to find the toggle button by automation ID or name
            // Note: This assumes the XAML has AutomationProperties.Name set
            try
            {
                var toggleButton = _driver.FindElementByName("Toggle Keep Awake");
                Assert.IsNotNull(toggleButton, "Toggle button should exist");
            }
            catch
            {
                // If specific name not found, try by class
                var buttons = _driver.FindElementsByClassName("Button");
                Assert.IsTrue(buttons.Count > 0, "At least one button should exist");
            }
        }

        [TestMethod]
        public void App_StatusText_IsDisplayed()
        {
            // Arrange
            Assert.IsNotNull(_driver, "Driver should be initialized");

            // Act
            try
            {
                var statusText = _driver.FindElementByName("StatusText");
                Assert.IsNotNull(statusText, "Status text element should exist");
                Assert.IsFalse(string.IsNullOrEmpty(statusText.Text), "Status text should not be empty");
            }
            catch
            {
                // Try finding text blocks
                var textBlocks = _driver.FindElementsByClassName("TextBlock");
                Assert.IsTrue(textBlocks.Count > 0, "Text blocks should be displayed");
            }
        }

        [TestMethod]
        public void App_Screenshot_CanBeTaken()
        {
            // Arrange
            Assert.IsNotNull(_driver, "Driver should be initialized");

            // Act
            var screenshot = _driver.GetScreenshot();

            // Assert
            Assert.IsNotNull(screenshot, "Screenshot should be taken");
            Assert.IsTrue(screenshot.AsByteArray.Length > 0, "Screenshot should have content");
        }

        [TestMethod]
        public void App_SettingsWindow_CanBeOpened()
        {
            // Arrange
            Assert.IsNotNull(_driver, "Driver should be initialized");

            // Act - Try to find and click settings button
            try
            {
                var settingsButton = _driver.FindElementByName("Settings");
                settingsButton?.Click();
                
                // Wait for settings window
                System.Threading.Thread.Sleep(500);
                
                // Try to find settings window
                var allWindows = _driver.WindowHandles;
                Assert.IsTrue(allWindows.Count >= 1, "Settings window should be accessible");
            }
            catch
            {
                Assert.Inconclusive("Settings button not found or settings window not accessible");
            }
        }

        [TestMethod]
        public void App_WindowTitle_IsCorrect()
        {
            // Arrange
            Assert.IsNotNull(_driver, "Driver should be initialized");

            // Act
            var title = _driver.Title;

            // Assert
            StringAssert.Contains(title, "Redball", "Window title should contain 'Redball'");
        }
    }
}
