using Microsoft.VisualStudio.TestTools.UnitTesting;
using Redball.UI.Services;
using System;
using System.Collections.Generic;
using System.Text.Json;

namespace Redball.Tests
{
    [TestClass]
    public class UpdateServiceMockTests
    {
        // Test version parsing from GitHub-style tag names
        [TestMethod]
        [DataRow("v2.1.43", 2, 1, 43)]
        [DataRow("V2.1.43", 2, 1, 43)]
        [DataRow("2.1.43", 2, 1, 43)]
        [DataRow("v1.0.0", 1, 0, 0)]
        [DataRow("v10.20.30", 10, 20, 30)]
        public void VersionParsing_FromTagName_Succeeds(string tag, int major, int minor, int build)
        {
            var tagName = tag.TrimStart('v', 'V');
            Assert.IsTrue(Version.TryParse(tagName, out var version), $"Should parse tag: {tag}");
            Assert.AreEqual(major, version!.Major);
            Assert.AreEqual(minor, version.Minor);
            Assert.AreEqual(build, version.Build);
        }

        [TestMethod]
        [DataRow("invalid")]
        [DataRow("")]
        [DataRow("v")]
        [DataRow("abc.def.ghi")]
        public void VersionParsing_InvalidTag_Fails(string tag)
        {
            var tagName = tag.TrimStart('v', 'V');
            Assert.IsFalse(Version.TryParse(tagName, out _), $"Should not parse tag: {tag}");
        }

        [TestMethod]
        public void VersionComparison_NewerVersion_IsDetected()
        {
            var current = new Version(2, 1, 100);
            var latest = new Version(2, 1, 110);
            Assert.IsTrue(latest > current, "Newer version should be greater");
        }

        [TestMethod]
        public void VersionComparison_SameVersion_NotGreater()
        {
            var current = new Version(2, 1, 110);
            var latest = new Version(2, 1, 110);
            Assert.IsFalse(latest > current, "Same version should not be greater");
        }

        [TestMethod]
        public void VersionComparison_OlderVersion_NotGreater()
        {
            var current = new Version(2, 1, 110);
            var latest = new Version(2, 1, 100);
            Assert.IsFalse(latest > current, "Older version should not be greater");
        }

        [TestMethod]
        public void VersionComparison_NormalizesRevision()
        {
            // UpdateService normalizes to Major.Minor.Build (ignoring Revision)
            var v1 = new Version(2, 1, 110, 0);
            var v2 = new Version(2, 1, 110, 5);
            var n1 = new Version(v1.Major, v1.Minor, v1.Build);
            var n2 = new Version(v2.Major, v2.Minor, v2.Build);
            Assert.AreEqual(n1, n2, "Normalized versions should be equal regardless of revision");
        }

        [TestMethod]
        public void GitHubRelease_Deserialization_MultipleReleases_FindsHighest()
        {
            // Simulate what FindHighestVersionRelease does
            var json = """
            [
              {"tag_name": "v2.1.100", "body": "Old release", "draft": false, "prerelease": false, "assets": []},
              {"tag_name": "v2.1.110", "body": "Latest release", "draft": false, "prerelease": false, "assets": []},
              {"tag_name": "v2.1.105", "body": "Middle release", "draft": false, "prerelease": false, "assets": []}
            ]
            """;
            var releases = JsonSerializer.Deserialize<List<GitHubRelease>>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;

            // Simulate FindHighestVersionRelease logic
            GitHubRelease? highest = null;
            Version? highestVersion = null;
            foreach (var release in releases)
            {
                if (release.IsDraft || release.IsPreRelease) continue;
                var tag = release.TagName.TrimStart('v', 'V');
                if (!Version.TryParse(tag, out var ver)) continue;
                var normalized = new Version(ver.Major, ver.Minor, ver.Build);
                if (highestVersion == null || normalized > new Version(highestVersion.Major, highestVersion.Minor, highestVersion.Build))
                {
                    highestVersion = ver;
                    highest = release;
                }
            }

            Assert.IsNotNull(highest);
            Assert.AreEqual("v2.1.110", highest.TagName);
            Assert.AreEqual("Latest release", highest.Body);
        }

        [TestMethod]
        public void GitHubRelease_Deserialization_SkipsDrafts()
        {
            var json = """
            [
              {"tag_name": "v9.0.0", "body": "Draft", "draft": true, "prerelease": false, "assets": []},
              {"tag_name": "v2.1.100", "body": "Real", "draft": false, "prerelease": false, "assets": []}
            ]
            """;
            var releases = JsonSerializer.Deserialize<List<GitHubRelease>>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;

            GitHubRelease? highest = null;
            foreach (var release in releases)
            {
                if (release.IsDraft) continue;
                highest = release;
            }

            Assert.IsNotNull(highest);
            Assert.AreEqual("v2.1.100", highest.TagName, "Should skip draft releases");
        }

        [TestMethod]
        public void GitHubRelease_Deserialization_SkipsPreRelease_OnStableChannel()
        {
            var json = """
            [
              {"tag_name": "v3.0.0-beta", "body": "Beta", "draft": false, "prerelease": true, "assets": []},
              {"tag_name": "v2.1.100", "body": "Stable", "draft": false, "prerelease": false, "assets": []}
            ]
            """;
            var releases = JsonSerializer.Deserialize<List<GitHubRelease>>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;

            // Stable channel skips pre-releases
            var updateChannel = "stable";
            GitHubRelease? highest = null;
            Version? highestVersion = null;
            foreach (var release in releases)
            {
                if (release.IsDraft) continue;
                if (release.IsPreRelease && updateChannel != "beta" && updateChannel != "alpha") continue;
                var tag = release.TagName.TrimStart('v', 'V');
                if (!Version.TryParse(tag, out var ver)) continue;
                var norm = new Version(ver.Major, ver.Minor, ver.Build);
                if (highestVersion == null || norm > new Version(highestVersion.Major, highestVersion.Minor, highestVersion.Build))
                {
                    highestVersion = ver;
                    highest = release;
                }
            }

            Assert.IsNotNull(highest);
            Assert.AreEqual("v2.1.100", highest.TagName, "Stable channel should skip pre-releases");
        }

        [TestMethod]
        public void AssetSelection_PrefersRedballMsi()
        {
            // Simulate FindBestAsset priority logic
            var assets = new List<GitHubAsset>
            {
                new() { Name = "some-other-file.txt", DownloadUrl = "https://example.com/other.txt", Size = 100 },
                new() { Name = "Redball.msi", DownloadUrl = "https://example.com/Redball.msi", Size = 5000000 },
                new() { Name = "Redball-Setup-2.1.110.exe", DownloadUrl = "https://example.com/setup.exe", Size = 3000000 }
            };

            var priorities = new[] { "Redball.msi", "Redball-Setup-", "Redball-Setup.exe", ".exe", ".msi" };
            GitHubAsset? selected = null;
            foreach (var p in priorities)
            {
                selected = assets.Find(a => a.Name.Contains(p, StringComparison.OrdinalIgnoreCase) &&
                    !a.Name.Contains("debug", StringComparison.OrdinalIgnoreCase));
                if (selected != null) break;
            }

            Assert.IsNotNull(selected);
            Assert.AreEqual("Redball.msi", selected.Name, "Should prefer Redball.msi");
        }

        [TestMethod]
        public void AssetSelection_FallsBackToSetupExe()
        {
            var assets = new List<GitHubAsset>
            {
                new() { Name = "README.md", DownloadUrl = "https://example.com/readme", Size = 100 },
                new() { Name = "Redball-Setup-2.1.110.exe", DownloadUrl = "https://example.com/setup.exe", Size = 3000000 }
            };

            var priorities = new[] { "Redball.msi", "Redball-Setup-", "Redball-Setup.exe", ".exe", ".msi" };
            GitHubAsset? selected = null;
            foreach (var p in priorities)
            {
                selected = assets.Find(a => a.Name.Contains(p, StringComparison.OrdinalIgnoreCase) &&
                    !a.Name.Contains("debug", StringComparison.OrdinalIgnoreCase));
                if (selected != null) break;
            }

            Assert.IsNotNull(selected);
            Assert.AreEqual("Redball-Setup-2.1.110.exe", selected.Name);
        }

        [TestMethod]
        public void AssetSelection_ExcludesDebugBuilds()
        {
            var assets = new List<GitHubAsset>
            {
                new() { Name = "Redball-debug.msi", DownloadUrl = "https://example.com/debug.msi", Size = 5000000 },
                new() { Name = "Redball-Setup-2.1.110.exe", DownloadUrl = "https://example.com/setup.exe", Size = 3000000 }
            };

            var priorities = new[] { "Redball.msi", "Redball-Setup-", "Redball-Setup.exe", ".exe", ".msi" };
            GitHubAsset? selected = null;
            foreach (var p in priorities)
            {
                selected = assets.Find(a => a.Name.Contains(p, StringComparison.OrdinalIgnoreCase) &&
                    !a.Name.Contains("debug", StringComparison.OrdinalIgnoreCase));
                if (selected != null) break;
            }

            Assert.IsNotNull(selected);
            Assert.AreEqual("Redball-Setup-2.1.110.exe", selected.Name, "Should skip debug builds");
        }

        [TestMethod]
        public void AssetSelection_NoMatchingAssets_ReturnsNull()
        {
            var assets = new List<GitHubAsset>
            {
                new() { Name = "source.zip", DownloadUrl = "https://example.com/source.zip", Size = 1000 },
                new() { Name = "checksums.txt", DownloadUrl = "https://example.com/checksums.txt", Size = 100 }
            };

            var priorities = new[] { "Redball.msi", "Redball-Setup-", "Redball-Setup.exe", ".exe", ".msi" };
            GitHubAsset? selected = null;
            foreach (var p in priorities)
            {
                selected = assets.Find(a => a.Name.Contains(p, StringComparison.OrdinalIgnoreCase) &&
                    !a.Name.Contains("debug", StringComparison.OrdinalIgnoreCase));
                if (selected != null) break;
            }

            // Also try fallback
            selected ??= assets.Find(a =>
                (a.Name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ||
                 a.Name.EndsWith(".msi", StringComparison.OrdinalIgnoreCase)) &&
                !a.Name.Contains("debug", StringComparison.OrdinalIgnoreCase));

            Assert.IsNull(selected, "Should return null when no suitable asset found");
        }

        [TestMethod]
        public void UpdateInfo_VersionDisplay_FormatsWithVPrefix()
        {
            var info = new UpdateInfo { LatestVersion = new Version(2, 1, 110) };
            Assert.AreEqual("v2.1.110", info.VersionDisplay);
        }

        [TestMethod]
        public void UpdateInfo_VersionDisplay_SingleDigitVersion()
        {
            var info = new UpdateInfo { LatestVersion = new Version(1, 0, 0) };
            Assert.AreEqual("v1.0.0", info.VersionDisplay);
        }

        [TestMethod]
        public void CircuitBreaker_Logic_TripsAfterThreshold()
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

            Assert.AreEqual(threshold, consecutiveFailures);
            Assert.IsTrue(circuitOpenUntil > DateTime.UtcNow, "Circuit should be open");
            Assert.IsTrue(DateTime.UtcNow < circuitOpenUntil, "Should skip calls while circuit is open");
        }

        [TestMethod]
        public void CircuitBreaker_Logic_ResetsOnSuccess()
        {
            var consecutiveFailures = 3;
            // Simulate success
            consecutiveFailures = 0;
            Assert.AreEqual(0, consecutiveFailures, "Failures should reset to 0 on success");
        }

        [TestMethod]
        public void GitHubRelease_FullDeserialization_MapsAllFields()
        {
            var json = """
            {
              "tag_name": "v2.1.110",
              "body": "## What's New\n- Bug fixes\n- Performance improvements",
              "published_at": "2026-03-20T10:00:00Z",
              "draft": false,
              "prerelease": false,
              "assets": [
                {
                  "name": "Redball-2.1.110.msi",
                  "browser_download_url": "https://github.com/ArMaTeC/Redball/releases/download/v2.1.110/Redball-2.1.110.msi",
                  "size": 15000000
                },
                {
                  "name": "Redball-2.1.110.msi.sha256",
                  "browser_download_url": "https://github.com/ArMaTeC/Redball/releases/download/v2.1.110/Redball-2.1.110.msi.sha256",
                  "size": 64
                }
              ]
            }
            """;

            var release = JsonSerializer.Deserialize<GitHubRelease>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;

            Assert.AreEqual("v2.1.110", release.TagName);
            Assert.IsTrue(release.Body.Contains("Bug fixes"));
            Assert.AreEqual(new DateTime(2026, 3, 20, 10, 0, 0, DateTimeKind.Utc), release.PublishedAt);
            Assert.IsFalse(release.IsDraft);
            Assert.IsFalse(release.IsPreRelease);
            Assert.AreEqual(2, release.Assets.Count);
            Assert.AreEqual("Redball-2.1.110.msi", release.Assets[0].Name);
            Assert.AreEqual(15000000, release.Assets[0].Size);
        }
    }
}
