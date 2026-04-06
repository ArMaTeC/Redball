using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Redball.UI.Services;
using Redball.UI.Views;

namespace Redball.Tests;

/// <summary>
/// End-to-end tests for TypeThing functionality.
/// Tests actual typing behavior in real Notepad windows.
/// Requires UI interaction permission.
/// </summary>
[TestClass]
[Ignore("E2E tests require UI interaction and should be run manually or in dedicated UI test pipeline")]
public class TypeThingE2ETests
{
    private Process? _notepad;
    private IntPtr _notepadHandle;
    private string _originalClipboard = "";

    [TestInitialize]
    public void Setup()
    {
        // Store original clipboard
        _originalClipboard = System.Windows.Clipboard.ContainsText() 
            ? System.Windows.Clipboard.GetText() 
            : "";
        
        // Start Notepad
        _notepad = Process.Start("notepad.exe");
        _notepad?.WaitForInputIdle(5000);
        Thread.Sleep(500); // Give time for window to appear
        
        if (_notepad != null)
        {
            _notepadHandle = _notepad.MainWindowHandle;
            Assert.AreNotEqual(IntPtr.Zero, _notepadHandle, "Notepad window handle should be valid");
        }
    }

    [TestCleanup]
    public void Cleanup()
    {
        // Restore clipboard
        if (!string.IsNullOrEmpty(_originalClipboard))
        {
            System.Windows.Clipboard.SetText(_originalClipboard);
        }

        // Close Notepad
        if (_notepad != null && !_notepad.HasExited)
        {
            _notepad.Kill();
            _notepad.WaitForExit(5000);
            _notepad.Dispose();
        }
    }

    [TestMethod]
    public async Task TypeThing_TypesSimpleText_ToActiveWindow()
    {
        // Arrange
        var testText = "Hello Redball!";
        System.Windows.Clipboard.SetText(testText);
        
        // Bring Notepad to foreground
        SetForegroundWindow(_notepadHandle);
        await Task.Delay(200);
        
        // Act - Use MainWindow's TypeThing directly
        var mainWindow = new MainWindow();
        mainWindow.StartTypeThingFromText(testText);
        
        // Wait for countdown (3s default) + typing time
        var estimatedTypingTime = testText.Length * 100; // ~100ms per char
        await Task.Delay(3500 + estimatedTypingTime);
        
        // Assert
        var notepadText = GetNotepadText(_notepadHandle);
        Assert.AreEqual(testText, notepadText, "TypeThing should type the exact clipboard content");
    }

    [TestMethod]
    public async Task TypeThing_TypesUnicodeCharacters()
    {
        // Arrange
        var testText = "Hello 世界! Привет мир! 🎉 €£¥";
        System.Windows.Clipboard.SetText(testText);
        
        SetForegroundWindow(_notepadHandle);
        await Task.Delay(200);
        
        // Act
        var mainWindow = new MainWindow();
        mainWindow.StartTypeThingFromText(testText);
        
        var estimatedTypingTime = testText.Length * 100;
        await Task.Delay(3500 + estimatedTypingTime);
        
        // Assert
        var notepadText = GetNotepadText(_notepadHandle);
        Assert.AreEqual(testText, notepadText, "TypeThing should handle Unicode characters correctly");
    }

    [TestMethod]
    public async Task TypeThing_TypesMultilineText()
    {
        // Arrange
        var testText = "Line 1\nLine 2\nLine 3";
        System.Windows.Clipboard.SetText(testText);
        
        SetForegroundWindow(_notepadHandle);
        await Task.Delay(200);
        
        // Act
        var mainWindow = new MainWindow();
        mainWindow.StartTypeThingFromText(testText);
        
        var estimatedTypingTime = testText.Length * 100;
        await Task.Delay(3500 + estimatedTypingTime);
        
        // Assert
        var notepadText = GetNotepadText(_notepadHandle);
        Assert.AreEqual(testText, notepadText, "TypeThing should handle newlines correctly");
    }

    [TestMethod]
    public async Task TypeThing_EmergencyStop_StopsTyping()
    {
        // Arrange
        var longText = new string('A', 100); // Long text to type
        System.Windows.Clipboard.SetText(longText);
        
        SetForegroundWindow(_notepadHandle);
        await Task.Delay(200);
        
        // Act
        var mainWindow = new MainWindow();
        mainWindow.StartTypeThingFromText(longText);
        
        // Wait for countdown then trigger emergency stop
        await Task.Delay(3500);
        
        // Simulate emergency stop hotkey (Ctrl+Shift+X by default)
        SimulateKeyCombo(0x11, 0x10, 0x58); // Ctrl, Shift, X
        
        await Task.Delay(500);
        
        // Assert - Should have typed only a portion
        var notepadText = GetNotepadText(_notepadHandle);
        Assert.IsTrue(notepadText.Length < longText.Length, 
            "Emergency stop should halt typing before completion");
    }

    [TestMethod]
    public async Task TypeThing_EmptyClipboard_ShowsWarning()
    {
        // Arrange
        System.Windows.Clipboard.Clear();
        
        // Act & Assert - Should not throw, should show warning notification
        var mainWindow = new MainWindow();
        
        try
        {
            mainWindow.StartTypeThing();
            Assert.Fail("Should have shown warning for empty clipboard");
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("empty"))
        {
            // Expected behavior
        }
    }

    [TestMethod]
    public async Task TypeThing_LargeContent_ShowsConfirmation()
    {
        // Arrange
        var largeText = new string('X', 15000); // Exceeds warning threshold
        System.Windows.Clipboard.SetText(largeText);
        
        // Act & Assert
        var mainWindow = new MainWindow();
        
        // Should show confirmation dialog - in real test we'd mock or automate the dialog
        // For now, just verify it doesn't crash
        try
        {
            mainWindow.StartTypeThing();
        }
        catch (Exception ex)
        {
            Assert.Fail($"Large content should not crash: {ex.Message}");
        }
    }

    [TestMethod]
    public async Task TypeThing_ConfigurableSpeed_TypesAtExpectedRate()
    {
        // Arrange
        var testText = "Speed test";
        System.Windows.Clipboard.SetText(testText);
        
        // Set fast typing speed
        ConfigService.Instance.Config.TypeThingMinDelayMs = 10;
        ConfigService.Instance.Config.TypeThingMaxDelayMs = 20;
        ConfigService.Instance.Config.TypeThingStartDelaySec = 1;
        ConfigService.Instance.Save();
        
        SetForegroundWindow(_notepadHandle);
        await Task.Delay(200);
        
        // Act
        var stopwatch = Stopwatch.StartNew();
        var mainWindow = new MainWindow();
        mainWindow.StartTypeThing();
        
        // Wait for completion
        var maxExpectedTime = 1000 + (testText.Length * 20) + 500;
        await Task.Delay(maxExpectedTime);
        stopwatch.Stop();
        
        // Assert
        var notepadText = GetNotepadText(_notepadHandle);
        Assert.AreEqual(testText, notepadText);
        
        // Verify timing is within expected range (accounting for overhead)
        var minExpectedTime = 1000 + (testText.Length * 10);
        Assert.IsTrue(stopwatch.ElapsedMilliseconds >= minExpectedTime - 500, 
            "Typing should respect minimum delay settings");
    }

    #region P/Invoke Helpers

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    private const int SW_RESTORE = 9;

    private void SimulateKeyCombo(params byte[] keyCodes)
    {
        // Send key down for each modifier
        foreach (var key in keyCodes)
        {
            keybd_event(key, 0, 0, UIntPtr.Zero);
        }
        
        // Send key up in reverse order
        foreach (var key in keyCodes.Reverse())
        {
            keybd_event(key, 0, 2, UIntPtr.Zero); // KEYEVENTF_KEYUP = 2
        }
    }

    [DllImport("user32.dll")]
    private static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);

    private string GetNotepadText(IntPtr hWnd)
    {
        // Simplified approach - in real implementation, use UI Automation or SendMessage
        // For testing, we'll use a temporary file approach
        
        try
        {
            // Send Ctrl+A to select all
            keybd_event(0x11, 0, 0, UIntPtr.Zero); // Ctrl down
            keybd_event(0x41, 0, 0, UIntPtr.Zero); // A down
            keybd_event(0x41, 0, 2, UIntPtr.Zero); // A up
            keybd_event(0x11, 0, 2, UIntPtr.Zero); // Ctrl up
            
            Thread.Sleep(100);
            
            // Send Ctrl+C to copy
            keybd_event(0x11, 0, 0, UIntPtr.Zero); // Ctrl down
            keybd_event(0x43, 0, 0, UIntPtr.Zero); // C down
            keybd_event(0x43, 0, 2, UIntPtr.Zero); // C up
            keybd_event(0x11, 0, 2, UIntPtr.Zero); // Ctrl up
            
            Thread.Sleep(100);
            
            return System.Windows.Clipboard.GetText();
        }
        catch (Exception ex)
        {
            Assert.Fail($"Failed to get Notepad text: {ex.Message}");
            return "";
        }
    }

    #endregion
}
