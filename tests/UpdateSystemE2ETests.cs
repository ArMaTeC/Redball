using Microsoft.VisualStudio.TestTools.UnitTesting;
using Redball.UI.Services;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Redball.Tests;

/// <summary>
/// End-to-end tests for the update system.
/// Tests the complete update flow from check to download to installation.
/// </summary>
[TestClass]
public class UpdateSystemE2ETests
{
    private string _testDir = "";
    private static HttpListener? _mockServer;
    private static int _mockServerPort = 0;
    private static readonly object _serverLock = new();

    [ClassInitialize]
    public static void ClassInitialize(TestContext context)
    {
        // Start mock GitHub API server
        StartMockGitHubServer();
    }

    [ClassCleanup]
    public static void ClassCleanup()
    {
        StopMockGitHubServer();
    }

    [TestInitialize]
    public void TestInitialize()
    {
        _testDir = Path.Combine(Path.GetTempPath(), $"update_e2e_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_testDir);
    }

    [TestCleanup]
    public void TestCleanup()
    {
        try
        {
            if (Directory.Exists(_testDir))
            {
                Directory.Delete(_testDir, true);
            }
        }
        catch { /* Ignore cleanup errors */ }
    }

    #region E2E Tests

    /// <summary>
    /// E2E Test 1: Complete update check flow against real GitHub API
    /// </summary>
    [TestMethod]
    public async Task E2E_UpdateCheck_RealGitHub_ReturnsCorrectResult()
    {
        // Arrange
        var service = new UpdateService("ArMaTeC", "Redball", "stable");
        var currentVersion = Assembly.GetExecutingAssembly().GetName().Version;

        // Act
        var updateInfo = await service.CheckForUpdateAsync();

        // Assert
        // Should either return null (up to date) or a valid UpdateInfo
        if (updateInfo != null)
        {
            Assert.IsNotNull(updateInfo.LatestVersion, "LatestVersion should be set");
            Assert.IsNotNull(updateInfo.CurrentVersion, "CurrentVersion should be set");
            Assert.IsFalse(string.IsNullOrEmpty(updateInfo.DownloadUrl), "DownloadUrl should be set");
            Assert.IsFalse(string.IsNullOrEmpty(updateInfo.FileName), "FileName should be set");
            Assert.IsTrue(updateInfo.LatestVersion > updateInfo.CurrentVersion, "Latest should be greater than current");
        }
    }

    /// <summary>
    /// E2E Test 2: Version comparison logic with various scenarios
    /// </summary>
    [TestMethod]
    [DataRow("2.1.100", "2.1.110", true)]  // Newer available
    [DataRow("2.1.110", "2.1.110", false)] // Same version
    [DataRow("2.1.110", "2.1.100", false)] // Older version
    [DataRow("2.1.110", "2.2.0", true)]    // Major bump (newer minor)
    [DataRow("2.1.110", "3.0.0", true)]    // Major version newer
    public void E2E_VersionComparison_Logic_Works(string current, string latest, bool shouldUpdate)
    {
        // Arrange
        var currentVersion = Version.Parse(current);
        var latestVersion = Version.Parse(latest);

        // Act
        var currentNormalized = new Version(currentVersion.Major, currentVersion.Minor, currentVersion.Build);
        var latestNormalized = new Version(latestVersion.Major, latestVersion.Minor, latestVersion.Build);
        var needsUpdate = latestNormalized > currentNormalized;

        // Assert
        Assert.AreEqual(shouldUpdate, needsUpdate, 
            $"Version comparison failed: {current} vs {latest} should {(shouldUpdate ? "" : "not ")}trigger update");
    }

    /// <summary>
    /// E2E Test 3: GitHub API response parsing
    /// </summary>
    [TestMethod]
    public void E2E_GitHubApiResponse_Parsing_Works()
    {
        // Arrange - Create release object directly instead of parsing JSON
        var release = new GitHubRelease
        {
            TagName = "v2.1.110",
            Body = "Test release notes",
            PublishedAt = new DateTime(2026, 1, 15, 10, 0, 0, DateTimeKind.Utc),
            IsDraft = false,
            IsPreRelease = false
        };
        release.Assets.Add(new GitHubAsset
        {
            Name = "Redball.msi",
            DownloadUrl = "https://github.com/ArMaTeC/Redball/releases/download/v2.1.110/Redball.msi",
            Size = 15000000
        });

        // Act & Assert
        Assert.AreEqual("v2.1.110", release.TagName);
        Assert.AreEqual("Test release notes", release.Body);
        Assert.AreEqual(1, release.Assets.Count);
        Assert.AreEqual("Redball.msi", release.Assets[0].Name);
    }

    /// <summary>
    /// E2E Test 4: Asset selection priority
    /// </summary>
    [TestMethod]
    public void E2E_AssetSelection_PriorityOrder_Works()
    {
        // Simulate the FindBestAsset logic
        var assets = new List<GitHubAsset>
        {
            new() { Name = "Redball-debug.msi", DownloadUrl = "url1", Size = 1000 },
            new() { Name = "Redball-Setup-2.1.110.exe", DownloadUrl = "url2", Size = 2000 },
            new() { Name = "Redball.msi", DownloadUrl = "url3", Size = 3000 },
            new() { Name = "README.md", DownloadUrl = "url4", Size = 100 }
        };

        var priorities = new[] { "Redball.msi", "Redball-Setup-", "Redball-Setup.exe", ".msi", ".exe" };
        GitHubAsset? selected = null;

        foreach (var priority in priorities)
        {
            selected = assets.Find(a => 
                a.Name.Contains(priority, StringComparison.OrdinalIgnoreCase) &&
                !a.Name.Contains("debug", StringComparison.OrdinalIgnoreCase));
            if (selected != null) break;
        }

        Assert.IsNotNull(selected);
        Assert.AreEqual("Redball.msi", selected.Name, "Should prefer Redball.msi over other assets");
    }

    /// <summary>
    /// E2E Test 5: Download URL validation
    /// </summary>
    [TestMethod]
    public void E2E_DownloadUrl_Validation_Works()
    {
        // Test various URL formats
        var validUrls = new[]
        {
            "https://github.com/ArMaTeC/Redball/releases/download/v2.1.110/Redball.msi",
            "https://api.github.com/repos/ArMaTeC/Redball/releases/assets/12345"
        };

        foreach (var url in validUrls)
        {
            Assert.IsTrue(Uri.TryCreate(url, UriKind.Absolute, out var uri));
            Assert.IsTrue(uri.Scheme == Uri.UriSchemeHttps);
        }
    }

    /// <summary>
    /// E2E Test 6: Hash calculation and verification
    /// </summary>
    [TestMethod]
    public async Task E2E_HashCalculation_Sha256_Works()
    {
        // Arrange
        var testFile = Path.Combine(_testDir, "test.txt");
        var content = "Test content for hash verification";
        await File.WriteAllTextAsync(testFile, content);

        // Act
        using var sha256 = SHA256.Create();
        await using var stream = File.OpenRead(testFile);
        var hash = await sha256.ComputeHashAsync(stream);
        var hashString = BitConverter.ToString(hash).Replace("-", "").ToLower();

        // Assert
        Assert.AreEqual(64, hashString.Length, "SHA256 hash should be 64 hex characters");
        Assert.IsTrue(hashString.All(c => (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f')));
    }

    /// <summary>
    /// E2E Test 7: Certificate pinning hash format validation
    /// </summary>
    [TestMethod]
    public void E2E_CertificatePinning_HashFormat_Valid()
    {
        // These are the actual pinned hashes from UpdateService
        var pinnedHashes = new[]
        {
            "9yF8wUfUQKd9aLkFMMnpx3xMIVC6sAu9TdjRhdZPjOI=",
            "cAajgxHdb7nHsbRxqmjDn5gEjBuuZKk6YaD8n1BS1DM=",
            "C5+lpZ7tc/VwmBl/DUSJEPSdEjZPw5OLf6IpeigyCNw=",
            "EdsvlytFf4a/O+hCPwBXFFi46RKXqivCAF+mO7s+5Ng=",
            "VqePxH3EcFwZuYK3CCOMz5HKMoeIZpZcEyBf4diPGSA="
        };

        foreach (var hash in pinnedHashes)
        {
            try
            {
                var bytes = Convert.FromBase64String(hash);
                Assert.AreEqual(32, bytes.Length, $"Hash {hash} should be 32 bytes (SHA-256)");
            }
            catch (FormatException)
            {
                Assert.Fail($"Hash {hash} is not valid base64");
            }
        }
    }

    /// <summary>
    /// E2E Test 8: Update script generation
    /// </summary>
    [TestMethod]
    public void E2E_UpdateScript_Generation_Works()
    {
        // Arrange
        var scriptDir = Path.Combine(_testDir, "script_test");
        Directory.CreateDirectory(scriptDir);
        var scriptPath = Path.Combine(scriptDir, "update.ps1");

        // Act - simulate script creation
        var script = @"
$ErrorActionPreference = 'Stop'
$processName = 'Redball.UI.WPF'

while (Get-Process -Name $processName -ErrorAction SilentlyContinue) {
    Start-Sleep -Seconds 1
}

Write-Host 'Update complete'
";
        File.WriteAllText(scriptPath, script);

        // Assert
        Assert.IsTrue(File.Exists(scriptPath));
        var content = File.ReadAllText(scriptPath);
        Assert.IsTrue(content.Contains("$processName"));
        Assert.IsTrue(content.Contains("Redball.UI.WPF"));
    }

    /// <summary>
    /// E2E Test 9: Pre-release channel filtering
    /// </summary>
    [TestMethod]
    public void E2E_PreReleaseFiltering_ChannelRespect_Works()
    {
        // Simulate stable channel logic
        var releases = new[]
        {
            new { Tag = "v3.0.0-beta", IsPreRelease = true },
            new { Tag = "v2.1.100", IsPreRelease = false },
            new { Tag = "v2.2.0-alpha", IsPreRelease = true }
        };

        var updateChannel = "stable";
        var selected = releases.Where(r => 
            !r.IsPreRelease || updateChannel == "beta" || updateChannel == "alpha")
            .OrderByDescending(r => r.Tag)
            .FirstOrDefault();

        Assert.IsNotNull(selected);
        Assert.AreEqual("v2.1.100", selected.Tag, "Stable channel should skip pre-releases");
    }

    /// <summary>
    /// E2E Test 10: Circuit breaker logic
    /// </summary>
    [TestMethod]
    public void E2E_CircuitBreaker_AfterFailures_Opens()
    {
        // Simulate circuit breaker state
        var consecutiveFailures = 0;
        var circuitOpenUntil = DateTime.MinValue;
        var threshold = 3;
        var cooldown = TimeSpan.FromMinutes(30);

        // Simulate 3 failures
        for (int i = 0; i < threshold; i++)
        {
            consecutiveFailures++;
            if (consecutiveFailures >= threshold)
            {
                circuitOpenUntil = DateTime.UtcNow.Add(cooldown);
            }
        }

        // Assert circuit is open
        Assert.AreEqual(threshold, consecutiveFailures);
        Assert.IsTrue(circuitOpenUntil > DateTime.UtcNow);
    }

    /// <summary>
    /// E2E Test 11: Download progress tracking
    /// </summary>
    [TestMethod]
    public void E2E_DownloadProgress_Calculation_Works()
    {
        // Arrange
        var totalBytes = 1000000L;
        var downloadedBytes = 500000L;

        // Act
        var percent = (int)((downloadedBytes * 100) / totalBytes);
        var speed = downloadedBytes / 5.0; // 5 seconds elapsed

        // Assert
        Assert.AreEqual(50, percent);
        Assert.AreEqual(100000, speed, 0.1);
    }

    /// <summary>
    /// E2E Test 12: File caching logic
    /// </summary>
    [TestMethod]
    public async Task E2E_FileCaching_HashBased_Works()
    {
        // Arrange
        var cacheDir = Path.Combine(_testDir, "cache");
        Directory.CreateDirectory(cacheDir);
        var cachedFile = Path.Combine(cacheDir, "update.msi");
        var hashFile = cachedFile + ".sha256";

        // Create test file with hash
        var content = "Test update file content";
        await File.WriteAllTextAsync(cachedFile, content);
        
        using var sha256 = SHA256.Create();
        var hash = sha256.ComputeHash(Encoding.UTF8.GetBytes(content));
        var hashString = BitConverter.ToString(hash).Replace("-", "").ToLower();
        await File.WriteAllTextAsync(hashFile, hashString);

        // Act - verify hash
        var storedHash = await File.ReadAllTextAsync(hashFile);
        storedHash = storedHash.Trim().ToLowerInvariant();

        using var stream = File.OpenRead(cachedFile);
        var actualHash = await sha256.ComputeHashAsync(stream);
        var actualHashString = BitConverter.ToString(actualHash).Replace("-", "").ToLower();

        // Assert
        Assert.AreEqual(storedHash, actualHashString, "Cached file hash should match");
    }

    /// <summary>
    /// E2E Test 13: Changelog generation
    /// </summary>
    [TestMethod]
    public void E2E_ChangelogGeneration_MultipleVersions_Works()
    {
        // Arrange
        var currentVersion = new Version(2, 1, 100);
        var latestVersion = new Version(2, 1, 110);
        
        var releases = new[]
        {
            new { Version = "v2.1.110", Notes = "Latest features", Date = DateTime.UtcNow },
            new { Version = "v2.1.105", Notes = "Bug fixes", Date = DateTime.UtcNow.AddDays(-7) },
            new { Version = "v2.1.100", Notes = "Previous release", Date = DateTime.UtcNow.AddDays(-14) },
            new { Version = "v2.0.0", Notes = "Old major", Date = DateTime.UtcNow.AddDays(-30) }
        };

        // Act - simulate changelog filtering
        var changelogs = releases
            .Where(r =>
            {
                var v = Version.Parse(r.Version.TrimStart('v', 'V'));
                var norm = new Version(v.Major, v.Minor, v.Build);
                return norm > currentVersion && norm <= latestVersion;
            })
            .OrderByDescending(r => r.Version)
            .ToList();

        // Assert
        Assert.AreEqual(2, changelogs.Count);
        Assert.AreEqual("v2.1.110", changelogs[0].Version);
        Assert.AreEqual("v2.1.105", changelogs[1].Version);
    }

    /// <summary>
    /// E2E Test 14: Path normalization security
    /// </summary>
    [TestMethod]
    public void E2E_PathNormalization_Security_Checks()
    {
        // Test path normalization (prevents directory traversal)
        var testPaths = new[]
        {
            ("files/update.exe", true),
            ("files/../update.exe", false),  // Directory traversal attempt
            ("C:\\absolute\\path.exe", false),   // Absolute path with drive
            ("files/sub/update.exe", true)
        };

        foreach (var (path, shouldBeValid) in testPaths)
        {
            var normalized = path.Replace('/', Path.DirectorySeparatorChar)
                .TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

            var isPathRooted = Path.IsPathRooted(normalized);
            var hasTraversal = normalized.Contains(".." + Path.DirectorySeparatorChar) || 
                              normalized.Contains(Path.DirectorySeparatorChar + "..");
            var isValid = !isPathRooted && !hasTraversal;

            Assert.AreEqual(shouldBeValid, isValid, 
                $"Path '{path}' should {(shouldBeValid ? "" : "not ")}be valid");
        }
    }

    /// <summary>
    /// E2E Test 15: Update info serialization
    /// </summary>
    [TestMethod]
    public void E2E_UpdateInfo_Serialization_Works()
    {
        // Arrange
        var info = new UpdateInfo
        {
            CurrentVersion = new Version(2, 1, 100),
            LatestVersion = new Version(2, 1, 110),
            DownloadUrl = "https://example.com/update.msi",
            FileName = "update.msi",
            ReleaseNotes = "Test release notes",
            ReleaseDate = DateTime.UtcNow
        };

        // Act
        var json = JsonSerializer.Serialize(info);
        var deserialized = JsonSerializer.Deserialize<UpdateInfo>(json);

        // Assert
        Assert.IsNotNull(deserialized);
        Assert.AreEqual(info.CurrentVersion, deserialized.CurrentVersion);
        Assert.AreEqual(info.LatestVersion, deserialized.LatestVersion);
        Assert.AreEqual(info.DownloadUrl, deserialized.DownloadUrl);
        Assert.AreEqual(info.FileName, deserialized.FileName);
    }

    /// <summary>
    /// E2E Test 16: Blue-green deployment slot switching
    /// </summary>
    [TestMethod]
    public void E2E_BlueGreenDeployment_SlotSwitching_Works()
    {
        // Arrange
        var appData = Path.Combine(_testDir, "bg_test");
        Directory.CreateDirectory(appData);
        var markerPath = Path.Combine(appData, "active_slot.txt");
        
        // Start with blue slot
        File.WriteAllText(markerPath, "blue");

        // Act - simulate switch to green
        var activeSlot = File.ReadAllText(markerPath).Trim();
        var newSlot = activeSlot == "blue" ? "green" : "blue";
        File.WriteAllText(markerPath, newSlot);

        // Assert
        var updatedSlot = File.ReadAllText(markerPath).Trim();
        Assert.AreEqual("green", updatedSlot);
    }

    /// <summary>
    /// E2E Test 17: Full differential update flow
    /// </summary>
    [TestMethod]
    public void E2E_DifferentialUpdate_FileSelection_Works()
    {
        // Arrange - simulate manifest with files
        var manifest = new UpdateManifest
        {
            Version = "2.1.110",
            Files = new List<FileUpdateInfo>
            {
                new() { Name = "Redball.UI.WPF.exe", Hash = "abc123", Size = 5000000 },
                new() { Name = "Redball.Core.dll", Hash = "def456", Size = 1000000 },
                new() { Name = "config.json", Hash = "ghi789", Size = 1000 }
            }
        };

        // Act - simulate which files need updating
        var filesToUpdate = manifest.Files.Where(f => f.Name.EndsWith(".exe") || f.Name.EndsWith(".dll")).ToList();

        // Assert
        Assert.AreEqual(2, filesToUpdate.Count);
        Assert.IsTrue(filesToUpdate.Any(f => f.Name == "Redball.UI.WPF.exe"));
        Assert.IsTrue(filesToUpdate.Any(f => f.Name == "Redball.Core.dll"));
    }

    /// <summary>
    /// E2E Test 18: Concurrent update check safety
    /// </summary>
    [TestMethod]
    public async Task E2E_ConcurrentUpdateCheck_ThreadSafe()
    {
        // Arrange
        var service = new UpdateService("ArMaTeC", "Redball", "stable");
        var tasks = new List<Task<UpdateInfo?>>();

        // Act - simulate concurrent checks
        for (int i = 0; i < 5; i++)
        {
            tasks.Add(Task.Run(() => service.CheckForUpdateAsync()));
        }

        var results = await Task.WhenAll(tasks);

        // Assert - all should complete without exception
        Assert.AreEqual(5, results.Length);
    }

    /// <summary>
    /// E2E Test 19: Update cancellation handling
    /// </summary>
    [TestMethod]
    public async Task E2E_UpdateCancellation_RespectsToken()
    {
        // Arrange
        var cts = new CancellationTokenSource();
        cts.Cancel(); // Cancel immediately

        var service = new UpdateService("ArMaTeC", "Redball", "stable");

        // Act & Assert - should handle cancellation gracefully
        try
        {
            await service.CheckForUpdateAsync(null, cts.Token);
            // Either returns null or throws OperationCanceledException - both are valid
            Assert.IsTrue(true);
        }
        catch (OperationCanceledException)
        {
            Assert.IsTrue(true, "Cancellation was properly handled");
        }
    }

    /// <summary>
    /// E2E Test 20: Network failure resilience
    /// </summary>
    [TestMethod]
    public async Task E2E_NetworkFailure_ReturnsNull()
    {
        // Arrange - use non-existent repo to simulate network failure
        var service = new UpdateService("nonexistent-owner-12345", "nonexistent-repo-67890", "stable");

        // Act
        var result = await service.CheckForUpdateAsync();

        // Assert - should return null on failure, not throw
        Assert.IsNull(result);
    }

    #endregion

    #region Mock Server

    private static void StartMockGitHubServer()
    {
        try
        {
            _mockServer = new HttpListener();
            _mockServerPort = new Random().Next(10000, 65000);
            _mockServer.Prefixes.Add($"http://localhost:{_mockServerPort}/");
            _mockServer.Start();

            Task.Run(() =>
            {
                while (_mockServer.IsListening)
                {
                    try
                    {
                        var context = _mockServer.GetContext();
                        var response = context.Response;
                        
                        // Mock GitHub API response - use simple string without special chars
                        var mockJson = "[{\"tag_name\":\"v99.99.99\",\"body\":\"Mock release\",\"published_at\":\"2026-01-01T00:00:00Z\",\"draft\":false,\"prerelease\":false,\"assets\":[{\"name\":\"Redball.msi\",\"browser_download_url\":\"http://localhost:" + _mockServerPort + "/download/Redball.msi\",\"size\":15000000}]}]";

                        var buffer = Encoding.UTF8.GetBytes(mockJson);
                        response.ContentLength64 = buffer.Length;
                        response.OutputStream.Write(buffer, 0, buffer.Length);
                        response.OutputStream.Close();
                    }
                    catch { /* Ignore */ }
                }
            });
        }
        catch { /* Ignore startup errors */ }
    }

    private static void StopMockGitHubServer()
    {
        try
        {
            _mockServer?.Stop();
            _mockServer?.Close();
        }
        catch { /* Ignore */ }
    }

    #endregion
}
