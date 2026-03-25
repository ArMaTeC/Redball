using FlaUI.Core;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using FlaUI.UIA3;
using System.Diagnostics;
using System.IO;

namespace Redball.UIAutomation;

/// <summary>
/// Helper class for UI automation testing of Redball WPF application.
/// Uses FlaUI to interact with the application UI.
/// </summary>
public class RedballUIAutomation : IDisposable
{
    private Application? _app;
    private UIA3Automation? _automation;
    private Window? _mainWindow;
    private bool _disposed;

    public Window MainWindow => _mainWindow ?? throw new InvalidOperationException("Application not started");

    /// <summary>
    /// Launches the Redball application from the build output.
    /// </summary>
    public void LaunchApplication()
    {
        var exePath = FindExecutablePath();

        if (!File.Exists(exePath))
        {
            throw new FileNotFoundException("Redball.UI.WPF.exe not found. Please build the project first.", exePath);
        }

        _automation = new UIA3Automation();
        
        try
        {
            _app = Application.Launch(exePath);
            
            // Give the app time to start - tray-only mode takes longer to initialize
            Thread.Sleep(3000);
            
            // For tray-only apps, GetMainWindow fails because window is hidden.
            // Get window by process ID instead, then show it.
            var processId = _app.ProcessId;
            _mainWindow = GetWindowByProcessId(processId, TimeSpan.FromSeconds(10));
            
            if (_mainWindow == null)
            {
                throw new InvalidOperationException("Could not find main window. Application may have crashed or failed to start.");
            }
            
            // Show the window for UI automation testing using WindowPattern
            var windowPattern = _mainWindow!.Patterns.Window.Pattern;
            windowPattern.SetWindowVisualState(WindowVisualState.Normal);
            _mainWindow.Focus();
            Thread.Sleep(500);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Failed to launch application: {ex.Message}", ex);
        }
    }

    private Window? GetWindowByProcessId(int processId, TimeSpan timeout)
    {
        var start = DateTime.Now;
        while (DateTime.Now - start < timeout)
        {
            try
            {
                // Find desktop and search for window by process ID
                var desktop = _automation?.GetDesktop();
                var windows = desktop?.FindAllChildren(cf => cf.ByControlType(ControlType.Window));
                
                if (windows != null)
                {
                    foreach (var window in windows)
                    {
                        try
                        {
                            // Check if this window belongs to our process
                            var windowPattern = window.Patterns.Window.PatternOrDefault;
                            if (windowPattern != null)
                            {
                                // Get the process ID through the window handle
                                var hwnd = window.Properties.NativeWindowHandle;
                                if (hwnd != IntPtr.Zero)
                                {
                                    try
                                    {
                                        var windowProcess = System.Diagnostics.Process.GetProcessById(processId);
                                        if (windowProcess.MainWindowHandle == hwnd || 
                                            window.Name.Contains("Redball", StringComparison.OrdinalIgnoreCase))
                                        {
                                            return window.AsWindow();
                                        }
                                    }
                                    catch { }
                                }
                            }
                        }
                        catch { }
                    }
                }
            }
            catch { }
            
            Thread.Sleep(100);
        }
        
        return null;
    }

    private static string FindExecutablePath()
    {
        // Try multiple possible locations
        var candidates = new[]
        {
            Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "src", "Redball.UI.WPF",
                "bin", "Release", "net8.0-windows", "win-x64", "Redball.UI.WPF.exe"),
            Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "src", "Redball.UI.WPF",
                "bin", "Debug", "net8.0-windows", "win-x64", "Redball.UI.WPF.exe"),
            Path.Combine(AppContext.BaseDirectory, "Redball.UI.WPF.exe"),
            Path.Combine(Environment.CurrentDirectory, "Redball.UI.WPF.exe"),
        };

        foreach (var candidate in candidates)
        {
            var fullPath = Path.GetFullPath(candidate);
            if (File.Exists(fullPath))
            {
                return fullPath;
            }
        }

        return candidates[0]; // Return first candidate even if not found
    }

    /// <summary>
    /// Attaches to an already running Redball instance.
    /// </summary>
    public void AttachToRunningApplication()
    {
        _automation = new UIA3Automation();
        _app = Application.Attach("Redball.UI.WPF");
        _mainWindow = _app.GetMainWindow(_automation);
    }

    /// <summary>
    /// Clicks a button by its automation ID.
    /// </summary>
    public void ClickButton(string automationId)
    {
        var button = FindElementByAutomationId(automationId);
        button?.Click();
    }

    /// <summary>
    /// Clicks a button by its text.
    /// </summary>
    public void ClickButtonByText(string text)
    {
        var button = _mainWindow?.FindFirstDescendant(
            cf => cf.ByControlType(ControlType.Button).And(cf.ByName(text)))?.AsButton();
        button?.Click();
    }

    /// <summary>
    /// Toggles a checkbox by its automation ID.
    /// </summary>
    public void ToggleCheckBox(string automationId, bool checkedState)
    {
        var checkbox = FindElementByAutomationId(automationId)?.AsCheckBox();
        if (checkbox != null && checkedState != checkbox.IsChecked)
        {
            checkbox.Toggle();
        }
    }

    /// <summary>
    /// Sets the value of a text box.
    /// </summary>
    public void SetTextBoxValue(string automationId, string value)
    {
        var textBox = FindElementByAutomationId(automationId)?.AsTextBox();
        textBox?.Enter(value);
    }

    /// <summary>
    /// Gets the value of a text box.
    /// </summary>
    public string GetTextBoxValue(string automationId)
    {
        var textBox = FindElementByAutomationId(automationId)?.AsTextBox();
        return textBox?.Text ?? string.Empty;
    }

    /// <summary>
    /// Selects an item in a combo box.
    /// </summary>
    public void SelectComboBoxItem(string automationId, string itemText)
    {
        var comboBox = FindElementByAutomationId(automationId)?.AsComboBox();
        comboBox?.Select(itemText);
    }

    /// <summary>
    /// Clicks a menu item by its path (e.g., "File|Exit").
    /// </summary>
    public void ClickMenu(string menuPath)
    {
        var parts = menuPath.Split('|');
        var menu = _mainWindow?.FindFirstDescendant(cf => cf.ByControlType(ControlType.MenuBar));

        foreach (var part in parts)
        {
            if (menu == null) return;

            var item = menu.FindFirstDescendant(
                cf => cf.ByName(part).And(cf.ByControlType(ControlType.MenuItem)));

            if (item == null)
            {
                item = menu.FindFirstDescendant(cf => cf.ByControlType(ControlType.MenuItem));
            }

            item?.Click();
            menu = item?.FindFirstDescendant(cf => cf.ByControlType(ControlType.Menu));
        }
    }

    /// <summary>
    /// Gets the status bar text.
    /// </summary>
    public string GetStatusText()
    {
        var statusBar = _mainWindow?.FindFirstDescendant(cf => cf.ByControlType(ControlType.StatusBar));
        return statusBar?.Name ?? string.Empty;
    }

    /// <summary>
    /// Waits for a specific element to appear.
    /// </summary>
    public bool WaitForElement(string automationId, TimeSpan timeout)
    {
        var start = DateTime.Now;
        while (DateTime.Now - start < timeout)
        {
            var element = FindElementByAutomationId(automationId);
            if (element != null) return true;
            Thread.Sleep(100);
        }
        return false;
    }

    /// <summary>
    /// Takes a screenshot of the main window.
    /// </summary>
    public void TakeScreenshot(string filePath)
    {
        // Screenshot functionality disabled for now - requires additional implementation
    }

    /// <summary>
    /// Checks if an element exists.
    /// </summary>
    public bool ElementExists(string automationId)
    {
        return FindElementByAutomationId(automationId) != null;
    }

    /// <summary>
    /// Finds an element by its automation ID.
    /// </summary>
    public AutomationElement? FindElementByAutomationId(string automationId)
    {
        return _mainWindow?.FindFirstDescendant(cf => cf.ByAutomationId(automationId));
    }

    /// <summary>
    /// Finds an element by its name.
    /// </summary>
    public AutomationElement? FindElementByName(string name)
    {
        return _mainWindow?.FindFirstDescendant(cf => cf.ByName(name));
    }

    /// <summary>
    /// Sends keyboard input to the application.
    /// </summary>
    public void SendKeys(string keys)
    {
        // Keyboard input disabled for now - requires additional implementation
    }

    /// <summary>
    /// Sends a keyboard shortcut (e.g., "Ctrl+T").
    /// </summary>
    public void SendShortcut(string shortcut)
    {
        // Keyboard shortcuts disabled for now - requires additional implementation
    }

    /// <summary>
    /// Checks if the application is still running.
    /// </summary>
    public bool IsApplicationRunning()
    {
        try
        {
            return _app?.HasExited == false;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Closes the application gracefully.
    /// </summary>
    public void CloseApplication()
    {
        try
        {
            _mainWindow?.Close();
            _app?.WaitWhileMainHandleIsMissing(TimeSpan.FromSeconds(5));
        }
        catch
        {
            // Force kill if graceful close fails
            try
            {
                _app?.Kill();
            }
            catch { }
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        CloseApplication();
        _automation?.Dispose();
    }
}
