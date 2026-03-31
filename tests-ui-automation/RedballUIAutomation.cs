using FlaUI.Core;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using FlaUI.UIA3;
using System.Diagnostics;
using System.IO;
using System.Linq;

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
            var startInfo = new ProcessStartInfo
            {
                FileName = exePath,
                Arguments = "--test-mode",
                WorkingDirectory = Path.GetDirectoryName(exePath) ?? Environment.CurrentDirectory,
                UseShellExecute = false
            };

            _app = Application.Launch(startInfo);

            // Give the app time to initialize WPF + tray services
            Thread.Sleep(1500);

            _mainWindow = GetWindowByProcessId(_app.ProcessId, TimeSpan.FromSeconds(30));
            
            if (_mainWindow == null)
            {
                throw new InvalidOperationException("Could not find main window. Application may have crashed or failed to start.");
            }
            
            EnsureWindowIsVisible(_mainWindow);
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
                if (_app != null && _automation != null)
                {
                    // Primary strategy: enumerate this process's top-level windows.
                    var topLevel = _app.GetAllTopLevelWindows(_automation);
                    foreach (var window in topLevel)
                    {
                        var title = window.Title;
                        if (!string.IsNullOrWhiteSpace(title) && title.Contains("Redball", StringComparison.OrdinalIgnoreCase))
                        {
                            return window;
                        }
                    }
                }

                // Fallback strategy: desktop-wide search by process id.
                var desktop = _automation?.GetDesktop();
                var windows = desktop?.FindAllChildren(cf => cf.ByControlType(ControlType.Window));
                if (windows != null)
                {
                    foreach (var window in windows)
                    {
                        var pid = window.Properties.ProcessId.ValueOrDefault;
                        if (pid != processId)
                        {
                            continue;
                        }

                        var title = window.Name;
                        if (!string.IsNullOrWhiteSpace(title) && title.Contains("Redball", StringComparison.OrdinalIgnoreCase))
                        {
                            return window.AsWindow();
                        }
                    }
                }
            }
            catch
            {
                // Keep polling until timeout.
            }
            
            Thread.Sleep(200);
        }
        
        return null;
    }

    private static string FindExecutablePath()
    {
        // Try multiple possible locations
        var candidates = new[]
        {
            Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "src", "Redball.UI.WPF",
                "bin", "Release", "net10.0-windows", "win-x64", "Redball.UI.WPF.exe"),
            Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "src", "Redball.UI.WPF",
                "bin", "Debug", "net10.0-windows", "win-x64", "Redball.UI.WPF.exe"),
            Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "dist", "wpf-publish", "Redball.UI.WPF.exe"),
            Path.Combine(AppContext.BaseDirectory, "Redball.UI.WPF.exe"),
            Path.Combine(Environment.CurrentDirectory, "Redball.UI.WPF.exe"),
        };

        var resolvedCandidates = candidates
            .Select(Path.GetFullPath)
            .Where(File.Exists)
            .Select(path => new FileInfo(path))
            .OrderByDescending(info => info.LastWriteTimeUtc)
            .ToList();

        if (resolvedCandidates.Count > 0)
        {
            return resolvedCandidates[0].FullName;
        }

        return Path.GetFullPath(candidates[0]); // Return first candidate even if not found
    }

    /// <summary>
    /// Attaches to an already running Redball instance.
    /// </summary>
    public void AttachToRunningApplication()
    {
        _automation = new UIA3Automation();

        var processes = Process.GetProcessesByName("Redball.UI.WPF")
            .Where(p => !p.HasExited)
            .OrderByDescending(p => p.StartTime)
            .ToList();

        if (processes.Count == 0)
        {
            throw new InvalidOperationException("No running Redball.UI.WPF process found.");
        }

        _app = Application.Attach(processes[0]);
        _mainWindow = GetWindowByProcessId(_app.ProcessId, TimeSpan.FromSeconds(10));

        if (_mainWindow == null)
        {
            throw new InvalidOperationException("Attached to process but failed to resolve main window.");
        }

        EnsureWindowIsVisible(_mainWindow);
    }

    private static void EnsureWindowIsVisible(Window window)
    {
        var windowPattern = window.Patterns.Window.PatternOrDefault;
        if (windowPattern != null)
        {
            windowPattern.SetWindowVisualState(WindowVisualState.Normal);
        }

        window.Focus();
        Thread.Sleep(350);
    }

    /// <summary>
    /// Clicks a button by its automation ID.
    /// </summary>
    public void ClickButton(string automationId)
    {
        var button = FindElementByAutomationId(automationId);
        if (button == null)
        {
            throw new InvalidOperationException($"Element with AutomationId '{automationId}' was not found.");
        }

        button.Click();
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
    /// Selects a radio button by automation ID.
    /// </summary>
    public void SelectRadioButton(string automationId)
    {
        var radioButton = FindElementByAutomationId(automationId)?.AsRadioButton();
        if (radioButton == null)
        {
            throw new InvalidOperationException($"RadioButton '{automationId}' was not found.");
        }

        if (!radioButton.IsChecked)
        {
            radioButton.IsChecked = true;
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
        if (comboBox == null)
        {
            throw new InvalidOperationException($"ComboBox '{automationId}' was not found.");
        }

        comboBox.Select(itemText);
    }

    /// <summary>
    /// Selects an item in a combo box by zero-based index.
    /// </summary>
    public void SelectComboBoxItemByIndex(string automationId, int index)
    {
        var comboBox = FindElementByAutomationId(automationId)?.AsComboBox();
        if (comboBox == null)
        {
            throw new InvalidOperationException($"ComboBox '{automationId}' was not found.");
        }

        comboBox.Expand();
        var items = comboBox.Items;
        if (index < 0 || index >= items.Length)
        {
            throw new ArgumentOutOfRangeException(nameof(index), $"Index {index} is out of range for ComboBox '{automationId}'.");
        }

        items[index].Select();
    }

    /// <summary>
    /// Returns the currently selected combo box item text.
    /// </summary>
    public string GetComboBoxSelectedText(string automationId)
    {
        var comboBox = FindElementByAutomationId(automationId)?.AsComboBox();
        if (comboBox == null)
        {
            throw new InvalidOperationException($"ComboBox '{automationId}' was not found.");
        }

        return comboBox.SelectedItem?.Text ?? string.Empty;
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
    /// Waits for an element to appear and be visible (not off-screen).
    /// </summary>
    public bool WaitForVisibleElement(string automationId, TimeSpan timeout)
    {
        var start = DateTime.Now;
        while (DateTime.Now - start < timeout)
        {
            if (IsElementVisible(automationId))
            {
                return true;
            }

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
    /// Checks if an element exists and is visible to UI automation.
    /// </summary>
    public bool IsElementVisible(string automationId)
    {
        var element = FindElementByAutomationId(automationId);
        if (element == null)
        {
            return false;
        }

        return !element.Properties.IsOffscreen.ValueOrDefault;
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

            if (_app != null)
            {
                Process? process = null;
                try
                {
                    process = Process.GetProcessById(_app.ProcessId);
                }
                catch
                {
                    process = null;
                }

                if (process != null && !process.HasExited)
                {
                    process.WaitForExit(2000);
                }

                if (process != null && !process.HasExited)
                {
                    _app.Kill();
                }
            }
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
