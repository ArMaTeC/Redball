using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;

namespace Redball.Core.Cryptography;

/// <summary>
/// Centralized hash computation utilities to eliminate code duplication.
/// Provides consistent SHA256 hashing for strings, bytes, and files.
/// </summary>
public static class HashUtility
{
    /// <summary>
    /// Computes SHA256 hash of a string and returns hex string.
    /// </summary>
    /// <param name="input">Input string to hash</param>
    /// <param name="truncateLength">Optional length to truncate hash (default: full hash)</param>
    /// <returns>Hex-encoded hash string</returns>
    public static string ComputeStringHash(string input, int? truncateLength = null)
    {
        var bytes = Encoding.UTF8.GetBytes(input);
        var hash = SHA256.HashData(bytes);
        var hexHash = Convert.ToHexString(hash);
        
        return truncateLength.HasValue 
            ? hexHash[..Math.Min(truncateLength.Value, hexHash.Length)]
            : hexHash;
    }

    /// <summary>
    /// Computes SHA256 hash of a byte array and returns hex string.
    /// </summary>
    /// <param name="data">Input bytes to hash</param>
    /// <param name="truncateLength">Optional length to truncate hash (default: full hash)</param>
    /// <returns>Hex-encoded hash string</returns>
    public static string ComputeBytesHash(byte[] data, int? truncateLength = null)
    {
        var hash = SHA256.HashData(data);
        var hexHash = Convert.ToHexString(hash);
        
        return truncateLength.HasValue 
            ? hexHash[..Math.Min(truncateLength.Value, hexHash.Length)]
            : hexHash;
    }

    /// <summary>
    /// Computes SHA256 hash of a file synchronously.
    /// </summary>
    /// <param name="filePath">Path to file</param>
    /// <param name="lowercase">Whether to return lowercase hex (default: false)</param>
    /// <returns>Hex-encoded hash string</returns>
    public static string ComputeFileHash(string filePath, bool lowercase = false)
    {
        using var stream = File.OpenRead(filePath);
        var hash = SHA256.HashData(stream);
        var hexHash = Convert.ToHexString(hash);
        
        return lowercase ? hexHash.ToLowerInvariant() : hexHash;
    }

    /// <summary>
    /// Computes SHA256 hash of a file asynchronously.
    /// </summary>
    /// <param name="filePath">Path to file</param>
    /// <param name="lowercase">Whether to return lowercase hex (default: false)</param>
    /// <returns>Hex-encoded hash string</returns>
    public static async Task<string> ComputeFileHashAsync(string filePath, bool lowercase = false)
    {
        await using var stream = File.OpenRead(filePath);
        var hash = await SHA256.HashDataAsync(stream);
        var hexHash = Convert.ToHexString(hash);
        
        return lowercase ? hexHash.ToLowerInvariant() : hexHash;
    }

    /// <summary>
    /// Computes SHA256 hash of a stream asynchronously.
    /// </summary>
    /// <param name="stream">Input stream</param>
    /// <param name="lowercase">Whether to return lowercase hex (default: false)</param>
    /// <returns>Hex-encoded hash string</returns>
    public static async Task<string> ComputeStreamHashAsync(Stream stream, bool lowercase = false)
    {
        var hash = await SHA256.HashDataAsync(stream);
        var hexHash = Convert.ToHexString(hash);
        
        return lowercase ? hexHash.ToLowerInvariant() : hexHash;
    }

    /// <summary>
    /// Generates a machine-specific anonymous user ID based on machine name and username.
    /// Consistent per device but not personally identifiable.
    /// </summary>
    /// <param name="truncateLength">Length to truncate hash (default: 16)</param>
    /// <returns>Anonymous user ID</returns>
    public static string GenerateAnonymousUserId(int truncateLength = 16)
    {
        var machineName = Environment.MachineName;
        var userName = Environment.UserName;
        var combined = $"{machineName}:{userName}";
        
        return ComputeStringHash(combined, truncateLength).ToLowerInvariant();
    }
}
