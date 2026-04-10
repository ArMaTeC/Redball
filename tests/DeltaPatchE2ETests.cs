using Microsoft.VisualStudio.TestTools.UnitTesting;
using Redball.UI.Services;
using System;
using System.Buffers;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Redball.Tests;

/// <summary>
/// End-to-end tests for the delta patching system.
/// Tests patch generation, application, and verification with the Node.js update server.
/// </summary>
[TestClass]
public class DeltaPatchE2ETests
{
    private string _testDir = "";
    private DeltaUpdateService _deltaService = null!;

    [TestInitialize]
    public void TestInitialize()
    {
        _testDir = Path.Combine(Path.GetTempPath(), $"delta_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_testDir);
        _deltaService = DeltaUpdateService.Instance;
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

    #region Core Patch Tests

    /// <summary>
    /// E2E Test 1: Create and apply a simple delta patch
    /// </summary>
    [TestMethod]
    public async Task E2E_SimpleDeltaPatch_CreateAndApply_Success()
    {
        // Arrange
        var oldData = Encoding.UTF8.GetBytes("Hello World! This is version 1.");
        var newData = Encoding.UTF8.GetBytes("Hello World! This is version 2.");

        // Act - Create patch
        var patch = await _deltaService.CreatePatchAsync(oldData, newData, CancellationToken.None);

        // Assert - Patch properties
        Assert.IsNotNull(patch);
        Assert.IsNotNull(patch.Data);
        Assert.AreEqual(ComputeHash(oldData), patch.OldFileHash);
        Assert.AreEqual(ComputeHash(newData), patch.NewFileHash);
        Assert.AreEqual(oldData.Length, patch.OldFileSize);
        Assert.AreEqual(newData.Length, patch.NewFileSize);
        Assert.IsTrue(patch.PatchSize > 0);

        // Act - Apply patch
        var result = await _deltaService.ApplyPatchAsync(oldData, patch, CancellationToken.None);

        // Assert - Result matches new data
        CollectionAssert.AreEqual(newData, result);
    }

    /// <summary>
    /// E2E Test 2: Patch large file (simulating real DLL/EXE)
    /// </summary>
    [TestMethod]
    public async Task E2E_LargeFileDeltaPatch_CreateAndApply_Success()
    {
        // Arrange - Create simulated binary files (5MB each, 10% different)
        var oldData = new byte[5 * 1024 * 1024];
        var newData = new byte[5 * 1024 * 1024];
        
        // Fill with pseudo-random data
        var random = new Random(42); // Fixed seed for reproducibility
        random.NextBytes(oldData);
        oldData.CopyTo(newData, 0);

        // Change 10% in the middle
        var changeStart = oldData.Length / 2;
        var changeLength = oldData.Length / 10;
        for (int i = 0; i < changeLength; i++)
        {
            newData[changeStart + i] = (byte)(newData[changeStart + i] ^ 0xFF);
        }

        // Act
        var patch = await _deltaService.CreatePatchAsync(oldData, newData, CancellationToken.None);
        var result = await _deltaService.ApplyPatchAsync(oldData, patch, CancellationToken.None);

        // Assert
        CollectionAssert.AreEqual(newData, result);
        
        // Verify compression savings
        var savingsPercent = (1.0 - (double)patch.PatchSize / newData.Length) * 100;
        Console.WriteLine($"Large file patch savings: {savingsPercent:F1}%");
        Assert.IsTrue(savingsPercent > 80, "Should achieve >80% savings for minor changes");
    }

    /// <summary>
    /// E2E Test 3: Patch binary file with common header/footer
    /// </summary>
    [TestMethod]
    public async Task E2E_BinaryFile_CommonPrefixSuffix_Success()
    {
        // Arrange - Simulate PE/DLL with common header
        var header = new byte[1024];
        Array.Fill(header, (byte)0x4D); // 'M' for MZ header
        
        var oldBody = Encoding.UTF8.GetBytes("OLD_VERSION_DATA" + new string('x', 10000));
        var newBody = Encoding.UTF8.GetBytes("NEW_VERSION_DATA" + new string('y', 10000));
        
        var footer = new byte[512];
        Array.Fill(footer, (byte)0x00);

        var oldData = header.Concat(oldBody).Concat(footer).ToArray();
        var newData = header.Concat(newBody).Concat(footer).ToArray();

        // Act
        var patch = await _deltaService.CreatePatchAsync(oldData, newData, CancellationToken.None);
        var result = await _deltaService.ApplyPatchAsync(oldData, patch, CancellationToken.None);

        // Assert
        CollectionAssert.AreEqual(newData, result);
        
        // Should have significant savings due to common prefix/suffix
        var savings = (1.0 - (double)patch.PatchSize / newData.Length) * 100;
        Console.WriteLine($"Binary patch savings: {savings:F1}%");
        Assert.IsTrue(savings > 60, "Should detect and exploit common header/footer");
    }

    /// <summary>
    /// E2E Test 4: Patch verification with hash mismatch detection
    /// </summary>
    [TestMethod]
    public async Task E2E_PatchVerification_HashMismatch_Throws()
    {
        // Arrange
        var oldData = Encoding.UTF8.GetBytes("Original content");
        var newData = Encoding.UTF8.GetBytes("Modified content");
        var patch = await _deltaService.CreatePatchAsync(oldData, newData, CancellationToken.None);

        // Corrupt the old data
        var corruptedOldData = oldData.ToArray();
        corruptedOldData[0] = (byte)(corruptedOldData[0] ^ 0xFF);

        // Act & Assert - Should throw due to hash mismatch
        try
        {
            await _deltaService.ApplyPatchAsync(corruptedOldData, patch, CancellationToken.None);
            Assert.Fail("Expected InvalidOperationException was not thrown");
        }
        catch (InvalidOperationException)
        {
            // Expected
        }
    }

    /// <summary>
    /// E2E Test 5: Patch file size mismatch detection
    /// </summary>
    [TestMethod]
    public async Task E2E_PatchSizeMismatch_WrongOldFile_Throws()
    {
        // Arrange
        var oldData = Encoding.UTF8.GetBytes("Short content");
        var newData = Encoding.UTF8.GetBytes("Much longer content that is different");
        var patch = await _deltaService.CreatePatchAsync(oldData, newData, CancellationToken.None);

        // Wrong old data (different size)
        var wrongOldData = Encoding.UTF8.GetBytes("Wrong size");

        // Act & Assert - Should throw due to size mismatch
        try
        {
            await _deltaService.ApplyPatchAsync(wrongOldData, patch, CancellationToken.None);
            Assert.Fail("Expected InvalidOperationException was not thrown");
        }
        catch (InvalidOperationException)
        {
            // Expected
        }
    }

    /// <summary>
    /// E2E Test 6: Round-trip patch creation and application from files
    /// </summary>
    [TestMethod]
    public async Task E2E_FileBasedPatch_RoundTrip_Success()
    {
        // Arrange
        var oldFile = Path.Combine(_testDir, "old.txt");
        var newFile = Path.Combine(_testDir, "new.txt");
        var patchFile = Path.Combine(_testDir, "patch.bin");
        var resultFile = Path.Combine(_testDir, "result.txt");

        var oldContent = "Line 1\nLine 2\nLine 3\n" + string.Join("\n", Enumerable.Range(4, 100).Select(i => $"Line {i}"));
        var newContent = "Line 1\nLine 2 MODIFIED\nLine 3\n" + string.Join("\n", Enumerable.Range(4, 100).Select(i => $"Line {i}"));

        await File.WriteAllTextAsync(oldFile, oldContent);
        await File.WriteAllTextAsync(newFile, newContent);

        // Act - Create patch from files
        var patch = await _deltaService.CreatePatchAsync(oldFile, newFile, CancellationToken.None);
        await File.WriteAllBytesAsync(patchFile, patch.Data);

        // Act - Apply patch
        var loadedPatch = new DeltaPatch
        {
            Data = await File.ReadAllBytesAsync(patchFile),
            OldFileHash = patch.OldFileHash,
            NewFileHash = patch.NewFileHash,
            NewFileSize = patch.NewFileSize
        };

        var oldData = await File.ReadAllBytesAsync(oldFile);
        var result = await _deltaService.ApplyPatchAsync(oldData, loadedPatch, CancellationToken.None);
        await File.WriteAllBytesAsync(resultFile, result);

        // Assert
        var resultContent = await File.ReadAllTextAsync(resultFile);
        Assert.AreEqual(newContent, resultContent);
    }

    #endregion

    #region Update Manifest Tests

    /// <summary>
    /// E2E Test 7: Generate update manifest for multiple files
    /// </summary>
    [TestMethod]
    public async Task E2E_UpdateManifest_MultipleFiles_GeneratesCorrectly()
    {
        // Arrange
        var oldDir = Path.Combine(_testDir, "v1");
        var newDir = Path.Combine(_testDir, "v2");
        Directory.CreateDirectory(oldDir);
        Directory.CreateDirectory(newDir);

        // Create file structure
        await File.WriteAllTextAsync(Path.Combine(oldDir, "app.exe"), "APP_VERSION_1");
        await File.WriteAllTextAsync(Path.Combine(oldDir, "core.dll"), "DLL_VERSION_1");
        await File.WriteAllTextAsync(Path.Combine(oldDir, "config.json"), "{\"version\":1}");

        await File.WriteAllTextAsync(Path.Combine(newDir, "app.exe"), "APP_VERSION_2");
        await File.WriteAllTextAsync(Path.Combine(newDir, "core.dll"), "DLL_VERSION_2");
        await File.WriteAllTextAsync(Path.Combine(newDir, "config.json"), "{\"version\":2}");
        await File.WriteAllTextAsync(Path.Combine(newDir, "new.dll"), "NEW_DLL"); // New file

        // Act
        var manifest = await _deltaService.CreateUpdateManifestAsync(oldDir, newDir, "2.0.0", CancellationToken.None);

        // Assert
        Assert.IsNotNull(manifest);
        Assert.AreEqual("2.0.0", manifest.Version);
        Assert.AreEqual(4, manifest.Files.Count);

        // Check app.exe
        var appEntry = manifest.Files.First(f => f.Path == "app.exe");
        Assert.IsFalse(appEntry.Unchanged);
        Assert.IsFalse(appEntry.IsNew);
        Assert.IsTrue(appEntry.PatchSize > 0);

        // Check new.dll
        var newEntry = manifest.Files.First(f => f.Path == "new.dll");
        Assert.IsTrue(newEntry.IsNew);

        // Verify total savings
        Console.WriteLine($"Update manifest: {manifest.SavingsPercentage:F1}% savings");
    }

    /// <summary>
    /// E2E Test 8: Detect unchanged files and skip patching
    /// </summary>
    [TestMethod]
    public async Task E2E_UnchangedFiles_DetectedAndSkipped()
    {
        // Arrange
        var oldDir = Path.Combine(_testDir, "v1");
        var newDir = Path.Combine(_testDir, "v2");
        Directory.CreateDirectory(oldDir);
        Directory.CreateDirectory(newDir);

        var unchangedContent = "This file never changes";
        await File.WriteAllTextAsync(Path.Combine(oldDir, "stable.dll"), unchangedContent);
        await File.WriteAllTextAsync(Path.Combine(newDir, "stable.dll"), unchangedContent);

        await File.WriteAllTextAsync(Path.Combine(oldDir, "changed.dll"), "Old version");
        await File.WriteAllTextAsync(Path.Combine(newDir, "changed.dll"), "New version");

        // Act
        var manifest = await _deltaService.CreateUpdateManifestAsync(oldDir, newDir, "2.0.0", CancellationToken.None);

        // Assert
        var unchangedEntry = manifest.Files.First(f => f.Path == "stable.dll");
        Assert.IsTrue(unchangedEntry.Unchanged);

        var changedEntry = manifest.Files.First(f => f.Path == "changed.dll");
        Assert.IsFalse(changedEntry.Unchanged);
    }

    /// <summary>
    /// E2E Test 9: Delta update recommendation based on savings
    /// </summary>
    [TestMethod]
    public void E2E_DeltaUpdateRecommendation_ThresholdWorks()
    {
        // Arrange - Good savings manifest
        var goodManifest = new DeltaUpdateManifest
        {
            TotalPatchSize = 100,
            SavingsPercentage = 50
        };

        // Arrange - Poor savings manifest
        var poorManifest = new DeltaUpdateManifest
        {
            TotalPatchSize = 950,
            SavingsPercentage = 5
        };

        // Act & Assert
        Assert.IsTrue(_deltaService.IsDeltaUpdateRecommended(goodManifest, 30));
        Assert.IsFalse(_deltaService.IsDeltaUpdateRecommended(poorManifest, 30));
        Assert.IsTrue(_deltaService.IsDeltaUpdateRecommended(poorManifest, 0)); // No threshold
    }

    #endregion

    #region Patch Format Compatibility Tests

    /// <summary>
    /// E2E Test 10: C# patch format matches Node.js expectations
    /// Tests the binary format is compatible with update-server/lib/delta-patches.js
    /// </summary>
    [TestMethod]
    public async Task E2E_PatchFormat_NodeJsCompatible()
    {
        // Arrange
        var oldData = Encoding.UTF8.GetBytes("HEADER_OLD_DATA_FOOTER");
        var newData = Encoding.UTF8.GetBytes("HEADER_NEW_DATA_FOOTER");

        // Act
        var patch = await _deltaService.CreatePatchAsync(oldData, newData, CancellationToken.None);

        // Assert - Decompress and verify header format matches Node.js expectations
        using var input = new MemoryStream(patch.Data);
        using var gzip = new GZipStream(input, CompressionMode.Decompress);
        using var output = new MemoryStream();
        await gzip.CopyToAsync(output);
        var decompressed = output.ToArray();

        // Verify header structure (20 bytes = 5 x int32)
        Assert.IsTrue(decompressed.Length >= 20, "Decompressed patch must have at least 20 byte header");

        // Read header (little-endian, matching Node.js Buffer.readInt32LE)
        var header = decompressed.Take(20).ToArray();
        var oldSize = BitConverter.ToInt32(header, 0);
        var newSize = BitConverter.ToInt32(header, 4);
        var commonPrefix = BitConverter.ToInt32(header, 8);
        var commonSuffix = BitConverter.ToInt32(header, 12);
        var newDataLength = BitConverter.ToInt32(header, 16);

        // Verify header values are reasonable
        Assert.AreEqual(oldData.Length, oldSize, "Old size should match");
        Assert.AreEqual(newData.Length, newSize, "New size should match");
        Assert.IsTrue(commonPrefix >= 0, "Common prefix should be non-negative");
        Assert.IsTrue(commonSuffix >= 0, "Common suffix should be non-negative");
        Assert.IsTrue(newDataLength >= 0, "New data length should be non-negative");

        // Verify data section
        var dataSection = decompressed.Skip(20).ToArray();
        Assert.AreEqual(newDataLength, dataSection.Length, "Data section length should match header");
    }

    /// <summary>
    /// E2E Test 11: Patch with identical files produces empty/micro patch
    /// </summary>
    [TestMethod]
    public async Task E2E_IdenticalFiles_MicroPatch()
    {
        // Arrange
        var data = Encoding.UTF8.GetBytes("Identical content");

        // Act
        var patch = await _deltaService.CreatePatchAsync(data, data, CancellationToken.None);

        // Assert - Should have perfect savings (patch is just compressed header)
        var savingsPercent = (1.0 - (double)patch.PatchSize / data.Length) * 100;
        Console.WriteLine($"Identical file patch size: {patch.PatchSize} bytes ({savingsPercent:F1}% savings)");
        Assert.IsTrue(savingsPercent > 80, "Identical files should produce tiny patch");
    }

    /// <summary>
    /// E2E Test 12: Binary patch integrity verification
    /// </summary>
    [TestMethod]
    public async Task E2E_BinaryPatchIntegrity_AllZeros_Success()
    {
        // Arrange - Edge case: all zeros (common in binary files for padding)
        var oldData = new byte[1024]; // All zeros
        var newData = new byte[1024];
        newData[500] = 0xFF; // Single byte change

        // Act
        var patch = await _deltaService.CreatePatchAsync(oldData, newData, CancellationToken.None);
        var result = await _deltaService.ApplyPatchAsync(oldData, patch, CancellationToken.None);

        // Assert
        CollectionAssert.AreEqual(newData, result);
    }

    #endregion

    #region Error Handling Tests

    /// <summary>
    /// E2E Test 13: Empty file handling
    /// </summary>
    [TestMethod]
    public async Task E2E_EmptyFilePatch_Success()
    {
        // Arrange
        var oldData = Array.Empty<byte>();
        var newData = Encoding.UTF8.GetBytes("New content");

        // Act
        var patch = await _deltaService.CreatePatchAsync(oldData, newData, CancellationToken.None);
        var result = await _deltaService.ApplyPatchAsync(oldData, patch, CancellationToken.None);

        // Assert
        CollectionAssert.AreEqual(newData, result);
    }

    /// <summary>
    /// E2E Test 14: Corrupted patch data detection
    /// </summary>
    [TestMethod]
    public async Task E2E_CorruptedPatchData_Throws()
    {
        // Arrange
        var oldData = Encoding.UTF8.GetBytes("Original");
        var newData = Encoding.UTF8.GetBytes("Modified");
        var patch = await _deltaService.CreatePatchAsync(oldData, newData, CancellationToken.None);

        // Corrupt the patch data
        var corruptedPatch = new DeltaPatch
        {
            Data = patch.Data.Take(10).ToArray(), // Truncate
            OldFileHash = patch.OldFileHash,
            NewFileHash = patch.NewFileHash,
            NewFileSize = patch.NewFileSize
        };

        // Act & Assert - Should throw during decompression or application
        try
        {
            await _deltaService.ApplyPatchAsync(oldData, corruptedPatch, CancellationToken.None);
            Assert.Fail("Expected InvalidOperationException was not thrown");
        }
        catch (InvalidOperationException)
        {
            // Expected
        }
    }

    /// <summary>
    /// E2E Test 15: Cancellation during patch creation
    /// </summary>
    [TestMethod]
    public async Task E2E_CancellationDuringPatchCreation_Throws()
    {
        // Arrange
        var cts = new CancellationTokenSource();
        var oldData = new byte[10 * 1024 * 1024]; // 10MB
        var newData = new byte[10 * 1024 * 1024];
        new Random().NextBytes(oldData);
        new Random().NextBytes(newData);

        // Cancel immediately
        cts.Cancel();

        // Act & Assert - Should throw OperationCanceledException
        try
        {
            await _deltaService.CreatePatchAsync(oldData, newData, cts.Token);
            Assert.Fail("Expected OperationCanceledException was not thrown");
        }
        catch (OperationCanceledException)
        {
            // Expected
        }
    }

    #endregion

    #region Bandwidth Savings Tests

    /// <summary>
    /// E2E Test 16: Calculate bandwidth savings for realistic update scenario
    /// </summary>
    [TestMethod]
    public async Task E2E_BandwidthSavings_RealisticScenario()
    {
        // Arrange - Simulate a realistic app update
        // Old version: 50MB of data
        // Changes: 2MB of new code, 500KB of modified resources
        
        var oldData = new byte[50 * 1024 * 1024];
        new Random(123).NextBytes(oldData);

        var newData = oldData.ToArray();
        
        // Modify 500KB in the middle (code change)
        var changeStart = 10 * 1024 * 1024;
        for (int i = 0; i < 500 * 1024; i++)
        {
            newData[changeStart + i] = (byte)(newData[changeStart + i] ^ 0xAA);
        }

        // Add 2MB at the end (new features)
        var newFeatures = new byte[2 * 1024 * 1024];
        new Random(456).NextBytes(newFeatures);
        newData = newData.Concat(newFeatures).ToArray();

        // Act
        var patch = await _deltaService.CreatePatchAsync(oldData, newData, CancellationToken.None);

        // Assert
        var fullDownloadSize = newData.Length;
        var patchSize = patch.PatchSize;
        var savingsBytes = fullDownloadSize - patchSize;
        var savingsPercent = (savingsBytes / (double)fullDownloadSize) * 100;

        Console.WriteLine($"Realistic update scenario:");
        Console.WriteLine($"  Full download: {FormatBytes(fullDownloadSize)}");
        Console.WriteLine($"  Patch size: {FormatBytes(patchSize)}");
        Console.WriteLine($"  Savings: {FormatBytes(savingsBytes)} ({savingsPercent:F1}%)");

        // Should achieve significant savings
        Assert.IsTrue(savingsPercent > 85, "Should save >85% bandwidth for minor update");
    }

    /// <summary>
    /// E2E Test 17: Multiple sequential patches cumulative test
    /// </summary>
    [TestMethod]
    public async Task E2E_SequentialPatches_CumulativeSuccess()
    {
        // Arrange - Chain of updates: v1 -> v2 -> v3
        var v1 = Encoding.UTF8.GetBytes("VERSION_1_DATA");
        var v2 = Encoding.UTF8.GetBytes("VERSION_2_DATA");
        var v3 = Encoding.UTF8.GetBytes("VERSION_3_DATA");

        // Act
        var patch1to2 = await _deltaService.CreatePatchAsync(v1, v2, CancellationToken.None);
        var patch2to3 = await _deltaService.CreatePatchAsync(v2, v3, CancellationToken.None);

        // Apply sequentially: v1 -> patch1to2 = v2 -> patch2to3 = v3
        var afterPatch1 = await _deltaService.ApplyPatchAsync(v1, patch1to2, CancellationToken.None);
        CollectionAssert.AreEqual(v2, afterPatch1);

        var afterPatch2 = await _deltaService.ApplyPatchAsync(afterPatch1, patch2to3, CancellationToken.None);
        CollectionAssert.AreEqual(v3, afterPatch2);
    }

    #endregion

    #region Helpers

    private static string ComputeHash(byte[] data)
    {
        return Convert.ToHexString(SHA256.HashData(data));
    }

    private static string FormatBytes(long bytes)
    {
        if (bytes == 0) return "0 B";
        var sizes = new[] { "B", "KB", "MB", "GB" };
        var i = (int)Math.Floor(Math.Log(bytes) / Math.Log(1024));
        return $"{bytes / Math.Pow(1024, i):F1} {sizes[i]}";
    }

    #endregion
}
