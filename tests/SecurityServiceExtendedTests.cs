using Microsoft.VisualStudio.TestTools.UnitTesting;
using Redball.UI.Services;
using System;
using System.IO;

namespace Redball.Tests
{
    [TestClass]
    public class SecurityServiceExtendedTests
    {
        [TestMethod]
        public void ComputeFileHash_KnownContent_ReturnsExpectedHash()
        {
            // Arrange — "hello" has a well-known SHA256 hash
            var tempFile = Path.Combine(Path.GetTempPath(), $"hash_test_{Guid.NewGuid()}.txt");
            File.WriteAllBytes(tempFile, System.Text.Encoding.UTF8.GetBytes("hello"));

            // Act
            var hash = SecurityService.ComputeFileHash(tempFile);

            // Assert — SHA256("hello") = 2cf24dba5fb0a30e26e83b2ac5b9e29e1b161e5c1fa7425e73043362938b9824
            Assert.AreEqual("2cf24dba5fb0a30e26e83b2ac5b9e29e1b161e5c1fa7425e73043362938b9824", hash,
                "Hash of 'hello' should match known SHA256");

            File.Delete(tempFile);
        }

        [TestMethod]
        public void ComputeFileHash_EmptyFile_ReturnsEmptyFileHash()
        {
            // Arrange
            var tempFile = Path.Combine(Path.GetTempPath(), $"empty_hash_{Guid.NewGuid()}.txt");
            File.WriteAllBytes(tempFile, Array.Empty<byte>());

            // Act
            var hash = SecurityService.ComputeFileHash(tempFile);

            // Assert — SHA256 of empty input = e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855
            Assert.AreEqual("e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855", hash,
                "Hash of empty file should match known SHA256 of empty input");

            File.Delete(tempFile);
        }

        [TestMethod]
        public void ComputeFileHash_SameContent_ReturnsSameHash()
        {
            // Arrange
            var content = "Redball test determinism " + DateTime.UtcNow.Ticks;
            var file1 = Path.Combine(Path.GetTempPath(), $"hash_same1_{Guid.NewGuid()}.txt");
            var file2 = Path.Combine(Path.GetTempPath(), $"hash_same2_{Guid.NewGuid()}.txt");
            File.WriteAllText(file1, content);
            File.WriteAllText(file2, content);

            // Act
            var hash1 = SecurityService.ComputeFileHash(file1);
            var hash2 = SecurityService.ComputeFileHash(file2);

            // Assert
            Assert.AreEqual(hash1, hash2, "Same content should produce same hash");

            File.Delete(file1);
            File.Delete(file2);
        }

        [TestMethod]
        public void ComputeFileHash_DifferentContent_ReturnsDifferentHash()
        {
            // Arrange
            var file1 = Path.Combine(Path.GetTempPath(), $"hash_diff1_{Guid.NewGuid()}.txt");
            var file2 = Path.Combine(Path.GetTempPath(), $"hash_diff2_{Guid.NewGuid()}.txt");
            File.WriteAllText(file1, "content A");
            File.WriteAllText(file2, "content B");

            // Act
            var hash1 = SecurityService.ComputeFileHash(file1);
            var hash2 = SecurityService.ComputeFileHash(file2);

            // Assert
            Assert.AreNotEqual(hash1, hash2, "Different content should produce different hashes");

            File.Delete(file1);
            File.Delete(file2);
        }

        [TestMethod]
        public void ComputeFileHash_HashIsLowercase()
        {
            // Arrange
            var tempFile = Path.Combine(Path.GetTempPath(), $"hash_case_{Guid.NewGuid()}.txt");
            File.WriteAllText(tempFile, "test case sensitivity");

            // Act
            var hash = SecurityService.ComputeFileHash(tempFile);

            // Assert
            Assert.AreEqual(hash, hash.ToLowerInvariant(), "Hash should be lowercase hex");

            File.Delete(tempFile);
        }

        [TestMethod]
        public void VerifyAuthenticodeSignature_NonExistentFile_ReturnsFalse()
        {
            var result = SecurityService.VerifyAuthenticodeSignature("Z:\\does_not_exist_12345.exe");
            Assert.IsFalse(result, "Non-existent file should return false");
        }

        [TestMethod]
        public void VerifyAuthenticodeSignature_UnsignedFile_ReturnsFalse()
        {
            // Arrange — create a plain text file (not signed)
            var tempFile = Path.Combine(Path.GetTempPath(), $"unsigned_{Guid.NewGuid()}.txt");
            File.WriteAllText(tempFile, "This is not a signed executable");

            // Act
            var result = SecurityService.VerifyAuthenticodeSignature(tempFile);

            // Assert
            Assert.IsFalse(result, "Unsigned file should return false");

            File.Delete(tempFile);
        }

        [TestMethod]
        public void GenerateSBOM_ContainsSPDXVersion()
        {
            var sbom = SecurityService.GenerateSBOM();
            Assert.IsTrue(sbom.Contains("SPDX-2.3"), "SBOM should contain SPDX version 2.3");
        }

        [TestMethod]
        public void GenerateSBOM_ContainsAllDependencies()
        {
            var sbom = SecurityService.GenerateSBOM();
            Assert.IsTrue(sbom.Contains(".NET-Runtime"), "SBOM should list .NET Runtime");
            Assert.IsTrue(sbom.Contains("Hardcodet.NotifyIcon.Wpf"), "SBOM should list Hardcodet.NotifyIcon");
            Assert.IsTrue(sbom.Contains("Microsoft.Xaml.Behaviors.Wpf"), "SBOM should list Xaml Behaviors");
            Assert.IsTrue(sbom.Contains("System.Management"), "SBOM should list System.Management");
            Assert.IsTrue(sbom.Contains("System.Text.Json"), "SBOM should list System.Text.Json");
        }

        [TestMethod]
        public void GenerateSBOM_ContainsRelationships()
        {
            var sbom = SecurityService.GenerateSBOM();
            Assert.IsTrue(sbom.Contains("DESCRIBES"), "SBOM should contain DESCRIBES relationship");
            Assert.IsTrue(sbom.Contains("DEPENDS_ON"), "SBOM should contain DEPENDS_ON relationships");
        }

        [TestMethod]
        public void GenerateSBOM_ContainsMITLicense()
        {
            var sbom = SecurityService.GenerateSBOM();
            Assert.IsTrue(sbom.Contains("MIT"), "SBOM should reference MIT license");
        }

        [TestMethod]
        public void SaveSBOM_OverwritesExistingFile()
        {
            // Arrange
            var tempPath = Path.Combine(Path.GetTempPath(), $"sbom_overwrite_{Guid.NewGuid()}.json");
            File.WriteAllText(tempPath, "old content");

            // Act
            var result = SecurityService.SaveSBOM(tempPath);

            // Assert
            Assert.IsTrue(result, "Save should succeed");
            var content = File.ReadAllText(tempPath);
            Assert.IsTrue(content.Contains("spdxVersion"), "File should contain new SBOM data");
            Assert.IsFalse(content.Contains("old content"), "Old content should be overwritten");

            File.Delete(tempPath);
        }
    }
}
