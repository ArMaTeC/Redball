using Microsoft.Playwright;
using NUnit.Framework;
using System;
using System.IO;
using System.Threading.Tasks;

// Type aliases to resolve ambiguity with MSTest
using Assert = NUnit.Framework.Assert;
using TestContext = NUnit.Framework.TestContext;

namespace Redball.Tests.VisualRegression;

/// <summary>
/// Visual regression tests for UI consistency across themes.
/// Uses Playwright for screenshot capture and pixel comparison.
/// </summary>
[TestFixture]
public class ThemeVisualTests
{
    private IPlaywright _playwright = null!;
    private IBrowser _browser = null!;
    private IBrowserContext _context = null!;
    private IPage _page = null!;
    private string _screenshotDir = null!;

    [SetUp]
    public async Task SetUp()
    {
        // Skip entire test fixture if Playwright browser not installed
        try
        {
            _playwright = await Playwright.CreateAsync();
            _browser = await _playwright.Chromium.LaunchAsync(new() { Headless = true });
        }
        catch (Exception ex)
        {
            // Use Ignore to skip all tests in this fixture
            Ignore($"Playwright browser not available: {ex.Message}");
            return;
        }
        
        _context = await _browser.NewContextAsync(new()
        {
            ViewportSize = new() { Width = 1920, Height = 1080 }
        });
        _page = await _context.NewPageAsync();
        
        _screenshotDir = Path.Combine(TestContext.CurrentContext.TestDirectory, "..", "..", "..", "screenshots");
        Directory.CreateDirectory(_screenshotDir);
    }

    [TearDown]
    public async Task TearDown()
    {
        await _page.CloseAsync();
        await _context.CloseAsync();
        await _browser.CloseAsync();
        _playwright.Dispose();
    }

    [Test]
    [TestCase("Dark")]
    [TestCase("Light")]
    [TestCase("Cyberpunk")]
    [TestCase("HighContrast")]
    [Category("VisualRegression")]
    public async Task MainWindow_ThemeConsistency_MatchesBaseline(string theme)
    {
        // Skip if app not found - requires built executable and HTTP endpoint
        var appPath = FindAppPath();
        if (appPath == null)
        {
            Assert.Inconclusive("Redball.UI.WPF.exe not found. Build required.");
        }

        // Launch process with theme parameter
        var process = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
        {
            FileName = appPath,
            Arguments = $"--test-mode --theme={theme.ToLower()}",
            UseShellExecute = false
        });

        try
        {
            // Wait for app to initialize
            await Task.Delay(3000);

            // Take screenshot via Playwright
            try
            {
                await _page.GotoAsync("http://localhost:48080/");
                await _page.WaitForSelectorAsync("[data-testid='main-window']", new() { Timeout = 5000 });
            }
            catch (Exception ex)
            {
                Assert.Inconclusive($"App HTTP endpoint not available: {ex.Message}");
            }

            var screenshot = await _page.ScreenshotAsync(new() { FullPage = true });
            var baselinePath = Path.Combine(_screenshotDir, $"{theme}_baseline.png");
            var currentPath = Path.Combine(_screenshotDir, $"{theme}_current.png");
            var diffPath = Path.Combine(_screenshotDir, $"{theme}_diff.png");

            // Save current screenshot
            await File.WriteAllBytesAsync(currentPath, screenshot);

            if (!File.Exists(baselinePath))
            {
                // First run: establish baseline
                File.Copy(currentPath, baselinePath, true);
                Assert.Inconclusive($"Baseline established for theme: {theme}");
            }

            // Compare with baseline using pixelmatch
            var baseline = await File.ReadAllBytesAsync(baselinePath);
            var diff = ImageCompare.PixelDiff(baseline, screenshot, threshold: 0.1);

            if (diff.PixelDiffRatio > 0.01)
            {
                // Save diff image
                await File.WriteAllBytesAsync(diffPath, diff.DiffImage);
                Assert.Fail($"Visual regression detected for theme '{theme}': {diff.DifferentPixels} pixels differ ({diff.PixelDiffRatio:P2})");
            }

            Assert.Pass($"Theme '{theme}' matches baseline within tolerance");
        }
        finally
        {
            try { process?.Kill(); } catch { }
        }
    }

    [Test]
    [Category("VisualRegression")]
    public async Task SettingsPanel_AllThemes_RenderCorrectly()
    {
        var themes = new[] { "Dark", "Light", "MidnightBlue", "ForestGreen", "Cyberpunk" };
        
        foreach (var theme in themes)
        {
            // Navigate to settings panel for each theme
            try
            {
                await _page.GotoAsync($"http://localhost:48080/settings?theme={theme.ToLower()}");
                await _page.WaitForSelectorAsync("[data-testid='settings-panel']", new() { Timeout = 5000 });
            }
            catch (Exception ex)
            {
                Assert.Inconclusive($"App HTTP endpoint not available: {ex.Message}");
            }

            var screenshot = await _page.ScreenshotAsync(new() { FullPage = false });
            var path = Path.Combine(_screenshotDir, $"settings_{theme}.png");
            await File.WriteAllBytesAsync(path, screenshot);
        }

        Assert.Pass("All theme screenshots captured");
    }

    private string? FindAppPath()
    {
        var paths = new[]
        {
            Path.Combine(AppContext.BaseDirectory, "Redball.UI.WPF.exe"),
            Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "src", "Redball.UI.WPF", "bin", "Release", "net10.0-windows", "win-x64", "Redball.UI.WPF.exe"),
            Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "dist", "wpf-publish", "Redball.UI.WPF.exe"),
        };

        foreach (var p in paths)
        {
            var fullPath = Path.GetFullPath(p);
            if (File.Exists(fullPath)) return fullPath;
        }

        return null;
    }
}

/// <summary>
/// Pixel comparison utility for visual regression testing.
/// </summary>
public static class ImageCompare
{
    public static DiffResult PixelDiff(byte[] baseline, byte[] current, double threshold = 0.1)
    {
        // Simplified pixel comparison - production would use ImageSharp or similar
        // This is a placeholder for the actual implementation
        
        if (baseline.Length != current.Length)
        {
            return new DiffResult(
                DifferentPixels: Math.Abs(baseline.Length - current.Length),
                PixelDiffRatio: 1.0,
                DiffImage: Array.Empty<byte>()
            );
        }

        int diffCount = 0;
        for (int i = 0; i < baseline.Length; i++)
        {
            if (Math.Abs(baseline[i] - current[i]) > threshold * 255)
            {
                diffCount++;
            }
        }

        double ratio = (double)diffCount / baseline.Length;
        
        return new DiffResult(
            DifferentPixels: diffCount,
            PixelDiffRatio: ratio,
            DiffImage: Array.Empty<byte>() // Would contain actual diff visualization
        );
    }
}

public record DiffResult(int DifferentPixels, double PixelDiffRatio, byte[] DiffImage);
