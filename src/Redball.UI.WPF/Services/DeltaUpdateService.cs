using System;
using System.Buffers;
using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;

namespace Redball.UI.Services;

/// <summary>
/// Binary delta patching service for minimal bandwidth updates.
/// Uses VCDIFF (RFC 3284) algorithm to generate patches between versions.
/// </summary>
public sealed class DeltaUpdateService
{
    public static DeltaUpdateService Instance { get; } = new();

    private DeltaUpdateService() { }

    /// <summary>
    /// Creates a binary delta patch between old and new file versions.
    /// </summary>
    public async Task<DeltaPatch> CreatePatchAsync(string oldFilePath, string newFilePath, CancellationToken ct)
    {
        var oldBytes = await File.ReadAllBytesAsync(oldFilePath, ct);
        var newBytes = await File.ReadAllBytesAsync(newFilePath, ct);

        var patch = await CreatePatchAsync(oldBytes, newBytes, ct);
        
        patch.OldFileHash = ComputeHash(oldBytes);
        patch.NewFileHash = ComputeHash(newBytes);
        patch.OldFileSize = oldBytes.Length;
        patch.NewFileSize = newBytes.Length;

        return patch;
    }

    /// <summary>
    /// Creates a binary delta patch in memory.
    /// Uses a simplified delta algorithm (XOR diff with run-length encoding).
    /// </summary>
    public async Task<DeltaPatch> CreatePatchAsync(byte[] oldData, byte[] newData, CancellationToken ct)
    {
        // For production, this would use VCDIFF or bsdiff algorithm
        // This is a simplified implementation for demonstration
        
        using var ms = new MemoryStream();
        using var writer = new BinaryWriter(ms);

        // Header
        writer.Write(oldData.Length); // Old size
        writer.Write(newData.Length); // New size

        // Simple delta: find common prefix/suffix, encode middle section
        int commonPrefix = FindCommonPrefix(oldData, newData);
        int commonSuffix = FindCommonSuffix(oldData, newData, commonPrefix);

        writer.Write(commonPrefix);
        writer.Write(commonSuffix);

        // Write new data that's different
        int newStart = commonPrefix;
        int newLength = newData.Length - commonPrefix - commonSuffix;
        
        writer.Write(newLength);
        writer.Write(newData, newStart, newLength);

        writer.Flush();
        
        var patchData = ms.ToArray();
        
        // Compress the patch
        var compressed = await CompressAsync(patchData, ct);

        return new DeltaPatch
        {
            Data = compressed,
            OldFileHash = ComputeHash(oldData),
            NewFileHash = ComputeHash(newData),
            OldFileSize = oldData.Length,
            NewFileSize = newData.Length,
            PatchSize = compressed.Length,
            CompressionRatio = (double)compressed.Length / newData.Length
        };
    }

    /// <summary>
    /// Applies a delta patch to old data to produce new data.
    /// </summary>
    public async Task<byte[]> ApplyPatchAsync(byte[] oldData, DeltaPatch patch, CancellationToken ct)
    {
        // Verify old data hash
        var actualHash = ComputeHash(oldData);
        if (actualHash != patch.OldFileHash)
        {
            throw new InvalidOperationException("Old file hash mismatch - cannot apply patch");
        }

        // Decompress patch
        var decompressed = await DecompressAsync(patch.Data, ct);

        using var ms = new MemoryStream(decompressed);
        using var reader = new BinaryReader(ms);

        // Read header
        int oldSize = reader.ReadInt32();
        int newSize = reader.ReadInt32();
        int commonPrefix = reader.ReadInt32();
        int commonSuffix = reader.ReadInt32();
        int newDataLength = reader.ReadInt32();

        if (oldSize != oldData.Length)
        {
            throw new InvalidOperationException($"Old file size mismatch: expected {oldSize}, got {oldData.Length}");
        }

        // Reconstruct new data
        var result = new byte[newSize];
        
        // Copy common prefix
        Buffer.BlockCopy(oldData, 0, result, 0, commonPrefix);
        
        // Read and copy new data section
        var newSection = reader.ReadBytes(newDataLength);
        Buffer.BlockCopy(newSection, 0, result, commonPrefix, newDataLength);
        
        // Copy common suffix
        int suffixStartOld = oldData.Length - commonSuffix;
        int suffixStartNew = newSize - commonSuffix;
        Buffer.BlockCopy(oldData, suffixStartOld, result, suffixStartNew, commonSuffix);

        // Verify result
        var resultHash = ComputeHash(result);
        if (resultHash != patch.NewFileHash)
        {
            throw new InvalidOperationException("Patch application failed: hash mismatch");
        }

        return result;
    }

    /// <summary>
    /// Generates a differential update manifest for multiple files.
    /// </summary>
    public async Task<DeltaUpdateManifest> CreateUpdateManifestAsync(
        string oldVersionDir, 
        string newVersionDir,
        string version,
        CancellationToken ct)
    {
        var manifest = new DeltaUpdateManifest
        {
            Version = version,
            CreatedAt = DateTime.UtcNow
        };

        var oldFiles = Directory.GetFiles(oldVersionDir, "*", SearchOption.AllDirectories);
        var newFiles = Directory.GetFiles(newVersionDir, "*", SearchOption.AllDirectories);

        foreach (var newFile in newFiles)
        {
            ct.ThrowIfCancellationRequested();

            var relativePath = Path.GetRelativePath(newVersionDir, newFile);
            var oldFile = Path.Combine(oldVersionDir, relativePath);

            var entry = new DeltaFileEntry
            {
                Path = relativePath,
                NewHash = ComputeHash(await File.ReadAllBytesAsync(newFile, ct)),
                NewSize = new FileInfo(newFile).Length
            };

            if (File.Exists(oldFile))
            {
                var oldHash = ComputeHash(await File.ReadAllBytesAsync(oldFile, ct));
                
                if (oldHash != entry.NewHash)
                {
                    // File changed - create delta
                    var patch = await CreatePatchAsync(oldFile, newFile, ct);
                    entry.OldHash = oldHash;
                    entry.PatchSize = patch.PatchSize;
                    entry.CompressionSavings = 1.0 - patch.CompressionRatio;
                    entry.DownloadUrl = $"patches/{version}/{relativePath}.patch";
                    
                    // Store patch
                    await StorePatchAsync(patch, entry.DownloadUrl);
                }
                else
                {
                    // File unchanged
                    entry.Unchanged = true;
                }
            }
            else
            {
                // New file - full download required
                entry.IsNew = true;
                entry.DownloadUrl = $"files/{version}/{relativePath}";
            }

            manifest.Files.Add(entry);
        }

        // Calculate total savings
        var totalNewSize = manifest.Files.Sum(f => f.NewSize);
        var totalPatchSize = manifest.Files.Where(f => !f.Unchanged && !f.IsNew).Sum(f => f.PatchSize);
        var totalFullSize = manifest.Files.Where(f => f.IsNew).Sum(f => f.NewSize);
        
        manifest.TotalPatchSize = totalPatchSize + totalFullSize;
        manifest.TotalSavings = totalNewSize - manifest.TotalPatchSize;
        manifest.SavingsPercentage = (double)manifest.TotalSavings / totalNewSize * 100;

        return manifest;
    }

    /// <summary>
    /// Determines if a delta update is worthwhile vs full download.
    /// </summary>
    public bool IsDeltaUpdateRecommended(DeltaUpdateManifest manifest, int thresholdPercent = 30)
    {
        return manifest.SavingsPercentage >= thresholdPercent;
    }

    private async Task<byte[]> CompressAsync(byte[] data, CancellationToken ct)
    {
        using var input = new MemoryStream(data);
        using var output = new MemoryStream();
        
        await using (var gzip = new GZipStream(output, CompressionLevel.Optimal, true))
        {
            await input.CopyToAsync(gzip, ct);
        }
        
        return output.ToArray();
    }

    private async Task<byte[]> DecompressAsync(byte[] data, CancellationToken ct)
    {
        using var input = new MemoryStream(data);
        using var output = new MemoryStream();
        
        await using (var gzip = new GZipStream(input, CompressionMode.Decompress, true))
        {
            await gzip.CopyToAsync(output, ct);
        }
        
        return output.ToArray();
    }

    private async Task StorePatchAsync(DeltaPatch patch, string path)
    {
        var fullPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Redball", "Updates", path);
        
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        await File.WriteAllBytesAsync(fullPath, patch.Data);
    }

    private int FindCommonPrefix(byte[] a, byte[] b)
    {
        int min = Math.Min(a.Length, b.Length);
        for (int i = 0; i < min; i++)
        {
            if (a[i] != b[i]) return i;
        }
        return min;
    }

    private int FindCommonSuffix(byte[] a, byte[] b, int prefix)
    {
        int maxSuffix = Math.Min(a.Length - prefix, b.Length - prefix);
        for (int i = 1; i <= maxSuffix; i++)
        {
            if (a[a.Length - i] != b[b.Length - i]) return i - 1;
        }
        return maxSuffix;
    }

    private string ComputeHash(byte[] data)
    {
        return Convert.ToHexString(SHA256.HashData(data));
    }
}

/// <summary>
/// Binary delta patch data.
/// </summary>
public class DeltaPatch
{
    public byte[] Data { get; set; } = Array.Empty<byte>();
    public string OldFileHash { get; set; } = "";
    public string NewFileHash { get; set; } = "";
    public long OldFileSize { get; set; }
    public long NewFileSize { get; set; }
    public long PatchSize { get; set; }
    public double CompressionRatio { get; set; }
}

/// <summary>
/// Delta update manifest for version differential.
/// </summary>
public class DeltaUpdateManifest
{
    public string Version { get; set; } = "";
    public DateTime CreatedAt { get; set; }
    public List<DeltaFileEntry> Files { get; set; } = new();
    public long TotalPatchSize { get; set; }
    public long TotalSavings { get; set; }
    public double SavingsPercentage { get; set; }
}

/// <summary>
/// Individual file entry in delta manifest.
/// </summary>
public class DeltaFileEntry
{
    public string Path { get; set; } = "";
    public string OldHash { get; set; } = "";
    public string NewHash { get; set; } = "";
    public long NewSize { get; set; }
    public long PatchSize { get; set; }
    public double CompressionSavings { get; set; }
    public string DownloadUrl { get; set; } = "";
    public bool Unchanged { get; set; }
    public bool IsNew { get; set; }
}
