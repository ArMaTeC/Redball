using Microsoft.VisualStudio.TestTools.UnitTesting;
using Redball.UI.Services;

namespace Redball.Tests
{
    [TestClass]
    public class SecurityServiceTests
    {
        [TestMethod]
        public void SecurityService_ComputeFileHash_ValidFile_ReturnsHash()
        {
            // Arrange
            var tempFile = Path.GetTempFileName();
            File.WriteAllText(tempFile, "Test content for hashing");

            // Act
            var hash = SecurityService.ComputeFileHash(tempFile);

            // Assert
            Assert.IsFalse(string.IsNullOrEmpty(hash), "Hash should not be empty");
            Assert.AreEqual(64, hash.Length, "SHA256 hash should be 64 hex characters");

            // Cleanup
            File.Delete(tempFile);
        }

        [TestMethod]
        public void SecurityService_ComputeFileHash_NonExistentFile_ReturnsEmpty()
        {
            // Act
            var hash = SecurityService.ComputeFileHash("nonexistent_file_12345.txt");

            // Assert
            Assert.AreEqual(string.Empty, hash, "Should return empty string for non-existent file");
        }

        [TestMethod]
        public void SecurityService_GenerateSBOM_ReturnsValidJson()
        {
            // Act
            var sbom = SecurityService.GenerateSBOM();

            // Assert
            Assert.IsFalse(string.IsNullOrEmpty(sbom), "SBOM should not be empty");
            Assert.IsTrue(sbom.Contains("spdxVersion"), "SBOM should contain spdxVersion");
            Assert.IsTrue(sbom.Contains("Redball"), "SBOM should mention Redball");
            Assert.IsTrue(sbom.Contains("packages"), "SBOM should have packages section");
        }

        [TestMethod]
        public void SecurityService_SaveSBOM_ValidPath_ReturnsTrue()
        {
            // Arrange
            var tempPath = Path.Combine(Path.GetTempPath(), $"test_sbom_{Guid.NewGuid()}.json");

            // Act
            var result = SecurityService.SaveSBOM(tempPath);

            // Assert
            Assert.IsTrue(result, "Should return true on successful save");
            Assert.IsTrue(File.Exists(tempPath), "SBOM file should exist");
            var content = File.ReadAllText(tempPath);
            Assert.IsTrue(content.Contains("Redball"), "Saved file should contain Redball data");

            // Cleanup
            File.Delete(tempPath);
        }

        [TestMethod]
        public void SecurityService_SaveSBOM_InvalidPath_ReturnsFalse()
        {
            // Arrange
            var invalidPath = "Z:\\nonexistent\\directory\\sbom.json";

            // Act
            var result = SecurityService.SaveSBOM(invalidPath);

            // Assert
            Assert.IsFalse(result, "Should return false on failed save");
        }
    }
}
