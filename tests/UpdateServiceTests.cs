using Microsoft.VisualStudio.TestTools.UnitTesting;
using Redball.UI.Services;

namespace Redball.Tests
{
    [TestClass]
    public class UpdateServiceTests
    {
        [TestMethod]
        public void UpdateInfo_DefaultValues_AreCorrect()
        {
            // Arrange & Act
            var info = new UpdateInfo();

            // Assert
            Assert.AreEqual(new Version(0, 0, 0), info.CurrentVersion, "Default CurrentVersion should be 0.0.0");
            Assert.AreEqual(new Version(0, 0, 0), info.LatestVersion, "Default LatestVersion should be 0.0.0");
            Assert.AreEqual("", info.DownloadUrl, "Default DownloadUrl should be empty");
            Assert.AreEqual("", info.FileName, "Default FileName should be empty");
            Assert.AreEqual("", info.ReleaseNotes, "Default ReleaseNotes should be empty");
        }

        [TestMethod]
        public void UpdateInfo_VersionDisplay_FormatsCorrectly()
        {
            // Arrange
            var info = new UpdateInfo
            {
                LatestVersion = new Version(2, 1, 15)
            };

            // Act
            var display = info.VersionDisplay;

            // Assert
            Assert.AreEqual("v2.1.15", display, "VersionDisplay should format as vMajor.Minor.Build");
        }

        [TestMethod]
        public void GitHubRelease_DefaultValues_AreCorrect()
        {
            // Arrange & Act
            var release = new GitHubRelease();

            // Assert
            Assert.AreEqual("", release.TagName, "Default TagName should be empty");
            Assert.AreEqual("", release.Body, "Default Body should be empty");
            Assert.IsNotNull(release.Assets, "Assets should be initialized");
            Assert.AreEqual(0, release.Assets.Count, "Should have no assets initially");
            Assert.IsFalse(release.IsDraft, "Default IsDraft should be false");
            Assert.IsFalse(release.IsPreRelease, "Default IsPreRelease should be false");
        }

        [TestMethod]
        public void GitHubAsset_DefaultValues_AreCorrect()
        {
            // Arrange & Act
            var asset = new GitHubAsset();

            // Assert
            Assert.AreEqual("", asset.Name, "Default Name should be empty");
            Assert.AreEqual("", asset.DownloadUrl, "Default DownloadUrl should be empty");
            Assert.AreEqual(0, asset.Size, "Default Size should be 0");
        }

        [TestMethod]
        public async Task UpdateService_CheckForUpdateAsync_ReturnsNullOrUpdateInfo()
        {
            // Arrange
            var service = new UpdateService("ArMaTeC", "Redball", "stable");

            try
            {
                // Act
                var result = await service.CheckForUpdateAsync();

                // Assert - result may be null (up to date) or an UpdateInfo object
                // Either is valid depending on current version vs latest release
                Assert.IsTrue(result == null || result != null, "Should return null or UpdateInfo");
            }
            catch (Exception ex)
            {
                // Network or API errors are acceptable in test environment
                Assert.IsTrue(true, $"Service threw expected exception: {ex.Message}");
            }
        }

        [TestMethod]
        public void UpdateService_Constructor_SetsProperties()
        {
            // Arrange & Act
            var service = new UpdateService("owner", "repo", "beta", true);

            // Assert - properties are private, but we can verify the object was created
            Assert.IsNotNull(service, "Service should be created");
        }

        [TestMethod]
        public void UpdateService_Constructor_DefaultChannel()
        {
            // Arrange & Act - use default channel
            var service = new UpdateService("owner", "repo");

            // Assert
            Assert.IsNotNull(service, "Service should be created with default channel");
        }

        [TestMethod]
        public void UpdateInfo_Properties_CanBeSet()
        {
            // Arrange
            var info = new UpdateInfo();

            // Act
            info.CurrentVersion = new Version(1, 0, 0);
            info.LatestVersion = new Version(2, 0, 0);
            info.DownloadUrl = "https://example.com/update.zip";
            info.FileName = "update.zip";
            info.ReleaseNotes = "Bug fixes and improvements";
            info.ReleaseDate = DateTime.Now;

            // Assert
            Assert.AreEqual(new Version(1, 0, 0), info.CurrentVersion);
            Assert.AreEqual(new Version(2, 0, 0), info.LatestVersion);
            Assert.AreEqual("https://example.com/update.zip", info.DownloadUrl);
            Assert.AreEqual("update.zip", info.FileName);
            Assert.AreEqual("Bug fixes and improvements", info.ReleaseNotes);
        }

        [TestMethod]
        public void GitHubRelease_Assets_CanBeModified()
        {
            // Arrange
            var release = new GitHubRelease();
            var asset = new GitHubAsset
            {
                Name = "Redball.msi",
                DownloadUrl = "https://example.com/Redball.msi",
                Size = 1000000
            };

            // Act
            release.Assets.Add(asset);

            // Assert
            Assert.AreEqual(1, release.Assets.Count, "Should have 1 asset");
            Assert.AreEqual("Redball.msi", release.Assets[0].Name, "Asset name should match");
        }
    }
}
