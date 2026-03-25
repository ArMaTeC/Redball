using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using NUnit.Framework;
using Redball.UI.Services;
using WinForms = System.Windows.Forms;

namespace Redball.E2E.Tests;

[TestFixture]
public class HidTypingE2eTests
{
    private Thread? _uiThread;
    private WinForms.Form? _targetForm;
    private WinForms.TextBox? _sourceTextBox;
    private WinForms.TextBox? _targetTextBox;
    private WinForms.CheckedListBox? _checkList;
    private WinForms.Label? _progressLabel;
    private readonly ManualResetEventSlim _uiReady = new(false);

    [SetUp]
    public void SetUp()
    {
        _uiThread = new Thread(() =>
        {
            _targetForm = new WinForms.Form
            {
                Text = "Redball HID E2E Target",
                Width = 1050,
                Height = 720,
                StartPosition = WinForms.FormStartPosition.CenterScreen,
                TopMost = true
            };

            var root = new WinForms.TableLayoutPanel
            {
                Dock = WinForms.DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 5,
                Padding = new WinForms.Padding(8)
            };
            root.RowStyles.Add(new WinForms.RowStyle(WinForms.SizeType.AutoSize));
            root.RowStyles.Add(new WinForms.RowStyle(WinForms.SizeType.Absolute, 140));
            root.RowStyles.Add(new WinForms.RowStyle(WinForms.SizeType.AutoSize));
            root.RowStyles.Add(new WinForms.RowStyle(WinForms.SizeType.Absolute, 60));
            root.RowStyles.Add(new WinForms.RowStyle(WinForms.SizeType.Percent, 100));

            var sourceLabel = new WinForms.Label
            {
                Dock = WinForms.DockStyle.Fill,
                AutoSize = true,
                Text = "Source Characters (selected + copied to clipboard before typing):"
            };

            _sourceTextBox = new WinForms.TextBox
            {
                Multiline = true,
                ReadOnly = true,
                ScrollBars = WinForms.ScrollBars.Both,
                Dock = WinForms.DockStyle.Fill,
                Font = new System.Drawing.Font("Consolas", 11f),
                Name = "SourceTextBox"
            };

            var targetLabel = new WinForms.Label
            {
                Dock = WinForms.DockStyle.Fill,
                AutoSize = true,
                Text = "Typed Output (HID input target):"
            };

            _targetTextBox = new WinForms.TextBox
            {
                Multiline = true,
                Dock = WinForms.DockStyle.Fill,
                Font = new System.Drawing.Font("Consolas", 12f),
                Name = "TargetTextBox"
            };

            _checkList = new WinForms.CheckedListBox
            {
                Dock = WinForms.DockStyle.Fill,
                CheckOnClick = false,
                Font = new System.Drawing.Font("Consolas", 10f),
                Name = "CharacterChecklist"
            };

            _progressLabel = new WinForms.Label
            {
                Dock = WinForms.DockStyle.Top,
                AutoSize = true,
                Text = "Progress: 0/0"
            };

            root.Controls.Add(sourceLabel, 0, 0);
            root.Controls.Add(_sourceTextBox, 0, 1);
            root.Controls.Add(targetLabel, 0, 2);
            root.Controls.Add(_targetTextBox, 0, 3);
            root.Controls.Add(_checkList, 0, 4);

            _targetForm.Controls.Add(root);
            _targetForm.Controls.Add(_progressLabel);
            _targetForm.Shown += (_, _) =>
            {
                _targetForm.Activate();
                _targetTextBox.Focus();
                _uiReady.Set();
            };

            WinForms.Application.Run(_targetForm);
        });

        _uiThread.SetApartmentState(ApartmentState.STA);
        _uiThread.IsBackground = true;
        _uiThread.Start();

        Assert.That(_uiReady.Wait(TimeSpan.FromSeconds(8)), Is.True, "Failed to initialize textbox target window.");
        Assert.That(_targetForm, Is.Not.Null);
        Assert.That(_targetTextBox, Is.Not.Null);
    }

    [TearDown]
    public void TearDown()
    {
        try
        {
            if (_targetForm != null && !_targetForm.IsDisposed)
            {
                _targetForm.BeginInvoke(new Action(() => _targetForm.Close()));
            }
        }
        catch
        {
        }

        try
        {
            if (_uiThread != null && _uiThread.IsAlive)
            {
                _uiThread.Join(2000);
            }
        }
        catch
        {
        }

        _targetTextBox = null;
        _sourceTextBox = null;
        _checkList = null;
        _progressLabel = null;
        _targetForm = null;
        _uiThread = null;
        _uiReady.Reset();
    }

    [Test]
    public void HidTyping_NotepadPrintableAscii_RoundTripsExactly()
    {
        var interception = InterceptionInputService.Instance;
        if (!interception.IsDriverInstalled)
        {
            if (!interception.Initialize())
            {
                Assert.Ignore("Interception HID driver is not installed/ready on this machine.");
            }
        }
        else if (!interception.IsReady && !interception.Initialize())
        {
            Assert.Ignore("Interception HID driver is installed but not ready.");
        }

        var expected = BuildPrintableAsciiPayload();
        var clipboardPayload = PrepareClipboardFromSelectedSource(expected);

        Assert.That(clipboardPayload, Is.EqualTo(expected),
            "Clipboard payload does not match source payload.");

        BringTextboxToFrontAndFocus();
        Thread.Sleep(250);

        var sendFailures = new List<string>();
        var mismatchFailures = new List<string>();

        for (var i = 0; i < clipboardPayload.Length; i++)
        {
            var ch = clipboardPayload[i];
            var ok = interception.SendCharacter(ch);
            if (!ok)
            {
                sendFailures.Add($"SendCharacter failed for '{Printable(ch)}' (U+{(int)ch:X4})");
                UpdateChecklistStatus(i, ch, false, false);
                continue;
            }

            var reached = WaitForTextLength(i + 1, TimeSpan.FromSeconds(2));
            var current = GetTargetText();
            var matched = reached && current.Length > i && current[i] == ch;
            UpdateChecklistStatus(i, ch, true, matched);

            if (!matched)
            {
                var actualChar = current.Length > i ? Printable(current[i]) : "<missing>";
                mismatchFailures.Add($"Index {i}: expected '{Printable(ch)}' got {actualChar}");
            }

            Thread.Sleep(8);
        }

        if (sendFailures.Count > 0)
        {
            var screenshot = CaptureFailureScreenshot("hid-send-failures");
            Assert.Fail("HID send failures:\n" + string.Join("\n", sendFailures) + "\nScreenshot: " + screenshot);
        }

        if (mismatchFailures.Count > 0)
        {
            var screenshot = CaptureFailureScreenshot("hid-typed-mismatch");
            Assert.Fail("HID typed mismatches:\n" + string.Join("\n", mismatchFailures) + "\nScreenshot: " + screenshot);
        }

        var actual = WaitForText(expected.Length, TimeSpan.FromSeconds(10));
        if (!string.Equals(actual, expected, StringComparison.Ordinal))
        {
            var screenshot = CaptureFailureScreenshot("hid-roundtrip-mismatch");
            Assert.Fail($"Typed text did not match expected text.\nExpected: {expected}\nActual:   {actual}\nScreenshot: {screenshot}");
        }
    }

    private void BringTextboxToFrontAndFocus()
    {
        Assert.That(_targetForm, Is.Not.Null);
        Assert.That(_targetTextBox, Is.Not.Null);

        _targetForm!.Invoke(new Action(() =>
        {
            _targetForm.Activate();
            _targetForm.TopMost = true;
            _targetForm.TopMost = false;
            _targetTextBox!.Clear();
            _targetTextBox.Focus();

            var hwnd = _targetForm.Handle;
            if (hwnd != IntPtr.Zero)
            {
                SetForegroundWindow(hwnd);
            }
        }));
    }

    private string CaptureFailureScreenshot(string reason)
    {
        try
        {
            Assert.That(_targetForm, Is.Not.Null);

            var dir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "Redball-E2E-Screenshots");
            System.IO.Directory.CreateDirectory(dir);
            var file = System.IO.Path.Combine(
                dir,
                $"{DateTime.Now:yyyyMMdd-HHmmss}-{reason}-{Guid.NewGuid():N}.png");

            _targetForm!.Invoke(new Action(() =>
            {
                var width = Math.Max(1, _targetForm.Width);
                var height = Math.Max(1, _targetForm.Height);
                using var bmp = new Bitmap(width, height);
                _targetForm.DrawToBitmap(bmp, new Rectangle(0, 0, width, height));
                bmp.Save(file, ImageFormat.Png);
            }));

            TestContext.Progress.WriteLine($"Failure screenshot: {file}");
            TestContext.AddTestAttachment(file, $"HID E2E failure ({reason})");
            return file;
        }
        catch (Exception ex)
        {
            var fallback = $"<screenshot capture failed: {ex.Message}>";
            TestContext.Progress.WriteLine(fallback);
            return fallback;
        }
    }

    private string PrepareClipboardFromSelectedSource(string payload)
    {
        Assert.That(_targetForm, Is.Not.Null);
        Assert.That(_sourceTextBox, Is.Not.Null);
        Assert.That(_targetTextBox, Is.Not.Null);
        Assert.That(_checkList, Is.Not.Null);

        return (string)(_targetForm!.Invoke(new Func<string>(() =>
        {
            _sourceTextBox!.Text = payload;
            _sourceTextBox.SelectAll();
            _sourceTextBox.Copy();

            _targetTextBox!.Clear();

            _checkList!.Items.Clear();
            for (var i = 0; i < payload.Length; i++)
            {
                _checkList.Items.Add($"[{i:00}] {Printable(payload[i])}  pending", false);
            }

            _progressLabel!.Text = $"Progress: 0/{payload.Length}";
            return WinForms.Clipboard.GetText();
        }))!);
    }

    private string GetTargetText()
    {
        return (string)(_targetTextBox!.Invoke(new Func<string>(() => _targetTextBox.Text ?? string.Empty)) ?? string.Empty)!;
    }

    private bool WaitForTextLength(int minLength, TimeSpan timeout)
    {
        var started = DateTime.UtcNow;
        while ((DateTime.UtcNow - started) < timeout)
        {
            if (GetTargetText().Length >= minLength)
            {
                return true;
            }

            Thread.Sleep(25);
        }

        return GetTargetText().Length >= minLength;
    }

    private void UpdateChecklistStatus(int index, char expectedChar, bool sent, bool matched)
    {
        _targetForm!.Invoke(new Action(() =>
        {
            if (_checkList == null || index < 0 || index >= _checkList.Items.Count)
            {
                return;
            }

            var state = sent
                ? (matched ? "ok" : "mismatch")
                : "send-failed";

            _checkList.Items[index] = $"[{index:00}] {Printable(expectedChar)}  {state}";
            _checkList.SetItemChecked(index, matched);

            var completed = 0;
            for (var i = 0; i < _checkList.Items.Count; i++)
            {
                if (_checkList.GetItemChecked(i))
                {
                    completed++;
                }
            }

            _progressLabel!.Text = $"Progress: {completed}/{_checkList.Items.Count}";
        }));
    }

    private string WaitForText(int minLength, TimeSpan timeout)
    {
        var started = DateTime.UtcNow;
        while ((DateTime.UtcNow - started) < timeout)
        {
            var txt = _targetTextBox!.Invoke(new Func<string>(() => _targetTextBox.Text ?? string.Empty)) as string ?? string.Empty;
            if (txt.Length >= minLength)
            {
                return txt;
            }

            Thread.Sleep(100);
        }

        return _targetTextBox!.Invoke(new Func<string>(() => _targetTextBox.Text ?? string.Empty)) as string ?? string.Empty;
    }

    private static string BuildPrintableAsciiPayload()
    {
        return new string(Enumerable.Range(32, 95).Select(i => (char)i).ToArray());
    }

    private static string Printable(char ch)
    {
        return ch switch
        {
            '\r' => "\\r",
            '\n' => "\\n",
            '\t' => "\\t",
            _ => ch.ToString()
        };
    }

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);
}
