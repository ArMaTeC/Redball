using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Redball.UI.Services;

namespace Redball.Tests;

/// <summary>
/// Tests for BlueGreenUpdateService package extraction functionality.
/// </summary>
[TestClass]
public class BlueGreenUpdateServiceTests
{
    private string _testDir = "";

    [TestInitialize]
    public void TestInitialize()
    {
        _testDir = Path.Combine(Path.GetTempPath(), $"bg_update_test_{Guid.NewGuid():N}");
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

    /// <summary>
    /// Tests UpdatePackage model properties.
    /// </summary>
    [TestMethod]
    public void UpdatePackage_Properties_Work()
    {
        var package = new UpdatePackage
        {
            Version = "2.1.0",
            DownloadUrl = "https://example.com/update.zip",
            Hash = "abc123"
        };

        Assert.AreEqual("2.1.0", package.Version);
        Assert.AreEqual("https://example.com/update.zip", package.DownloadUrl);
        Assert.AreEqual("abc123", package.Hash);
    }

    /// <summary>
    /// Tests result types creation.
    /// </summary>
    [TestMethod]
    public void UpdateStageResult_Ok_ReturnsSuccess()
    {
        var result = UpdateStageResult.Ok("/path/to/stage");
        
        Assert.IsTrue(result.Success);
        Assert.AreEqual("/path/to/stage", result.Path);
        Assert.AreEqual(0, result.Errors.Count);
    }

    [TestMethod]
    public void UpdateStageResult_Err_ReturnsFailure()
    {
        var errors = new[] { "Error 1", "Error 2" };
        var result = UpdateStageResult.Err(errors);
        
        Assert.IsFalse(result.Success);
        Assert.IsNull(result.Path);
        Assert.AreEqual(2, result.Errors.Count);
    }

    [TestMethod]
    public void UpdateSwitchResult_Ok_ReturnsSuccess()
    {
        var result = UpdateSwitchResult.Ok(1234);
        
        Assert.IsTrue(result.Success);
        Assert.AreEqual(1234, result.ProcessId);
        Assert.IsNull(result.Error);
    }

    [TestMethod]
    public void UpdateSwitchResult_Err_ReturnsFailure()
    {
        var result = UpdateSwitchResult.Err("Switch failed");
        
        Assert.IsFalse(result.Success);
        Assert.IsNull(result.ProcessId);
        Assert.AreEqual("Switch failed", result.Error);
    }

    [TestMethod]
    public void UpdateRollbackResult_Ok_ReturnsSuccess()
    {
        var result = UpdateRollbackResult.Ok("/previous/path");
        
        Assert.IsTrue(result.Success);
        Assert.AreEqual("/previous/path", result.Path);
        Assert.IsNull(result.Error);
    }

    [TestMethod]
    public void UpdateRollbackResult_Err_ReturnsFailure()
    {
        var result = UpdateRollbackResult.Err("Rollback failed");
        
        Assert.IsFalse(result.Success);
        Assert.IsNull(result.Path);
        Assert.AreEqual("Rollback failed", result.Error);
    }

    [TestMethod]
    public void VerificationResult_Ok_ReturnsSuccess()
    {
        var result = VerificationResult.Ok();
        
        Assert.IsTrue(result.Success);
        Assert.AreEqual(0, result.Errors.Count);
    }

    [TestMethod]
    public void VerificationResult_Err_ReturnsFailure()
    {
        var errors = new[] { "Verification failed" };
        var result = VerificationResult.Err(errors);
        
        Assert.IsFalse(result.Success);
        Assert.AreEqual(1, result.Errors.Count);
    }

    /// <summary>
    /// Tests ZIP extraction functionality.
    /// </summary>
    [TestMethod]
    public async Task ExtractZipAsync_ExtractsFiles()
    {
        // Create test ZIP
        var testZip = Path.Combine(Path.GetTempPath(), $"test_{Guid.NewGuid():N}.zip");
        var extractDir = Path.Combine(_testDir, "extract");
        
        try
        {
            // Create a test ZIP file
            Directory.CreateDirectory(Path.Combine(_testDir, "source"));
            File.WriteAllText(Path.Combine(_testDir, "source", "test.txt"), "Hello World");
            
            ZipFile.CreateFromDirectory(
                Path.Combine(_testDir, "source"), 
                testZip,
                CompressionLevel.Optimal,
                false);

            var package = new UpdatePackage
            {
                DownloadUrl = $"file://{testZip}",
                Version = "1.0.0"
            };

            var progressValues = new List<double>();
            var progress = new Progress<double>(p => progressValues.Add(p));

            // Extract using reflection to test private method
            var service = new BlueGreenUpdateService();
            var method = typeof(BlueGreenUpdateService).GetMethod("ExtractZipAsync", 
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            
            await (method?.Invoke(service, new object[] { package, extractDir, progress }) as Task ?? Task.CompletedTask);

            // Verify extraction
            var extractedFile = Path.Combine(extractDir, "test.txt");
            Assert.IsTrue(File.Exists(extractedFile), "Extracted file should exist");
            Assert.AreEqual("Hello World", File.ReadAllText(extractedFile));
            
            // Verify progress was reported
            Assert.IsTrue(progressValues.Count > 0, "Progress should have been reported");
        }
        finally
        {
            if (File.Exists(testZip)) File.Delete(testZip);
        }
    }

    /// <summary>
    /// Tests hash verification.
    /// </summary>
    [TestMethod]
    public async Task VerifyPackageHashAsync_ValidHash_ReturnsOk()
    {
        // Create test file
        var testFile = Path.Combine(_testDir, "Redball.UI.WPF.exe");
        File.WriteAllText(testFile, "test content");

        // Calculate expected hash
        using var sha256 = SHA256.Create();
        await using var stream = File.OpenRead(testFile);
        var hash = await sha256.ComputeHashAsync(stream);
        var expectedHash = BitConverter.ToString(hash).Replace("-", "").ToLower();

        // Verify using reflection
        var service = new BlueGreenUpdateService();
        var method = typeof(BlueGreenUpdateService).GetMethod("VerifyPackageHashAsync", 
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        
        var result = await (method?.Invoke(service, new object[] { _testDir, expectedHash }) as Task<VerificationResult> ?? Task.FromResult(VerificationResult.Err(new[] { "Failed" })));

        Assert.IsTrue(result.Success, "Hash verification should succeed with correct hash");
    }

    [TestMethod]
    public async Task VerifyPackageHashAsync_InvalidHash_ReturnsError()
    {
        // Create test file
        var testFile = Path.Combine(_testDir, "Redball.UI.WPF.exe");
        File.WriteAllText(testFile, "test content");

        // Verify using reflection with wrong hash
        var service = new BlueGreenUpdateService();
        var method = typeof(BlueGreenUpdateService).GetMethod("VerifyPackageHashAsync", 
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        
        var result = await (method?.Invoke(service, new object[] { _testDir, "wronghash123" }) as Task<VerificationResult> ?? Task.FromResult(VerificationResult.Err(new[] { "Failed" })));

        Assert.IsFalse(result.Success, "Hash verification should fail with incorrect hash");
    }

    /// <summary>
    /// Tests service disposal doesn't throw.
    /// </summary>
    [TestMethod]
    public void BlueGreenUpdateService_Dispose_DoesNotThrow()
    {
        var service = new BlueGreenUpdateService();
        service.Dispose();
        // Should not throw
    }
}
