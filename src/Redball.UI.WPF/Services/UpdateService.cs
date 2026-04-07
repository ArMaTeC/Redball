using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;

namespace Redball.UI.Services;

/// <summary>
/// Cache for file hashes to avoid re-hashing unchanged files during update checks.
/// </summary>
public class FileHashCache
{
    private readonly string _cacheFilePath;
    private Dictionary<string, FileHashEntry> _cache = new();
    private readonly object _lock = new();

    public FileHashCache()
    {
        var cacheDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Redball", "UpdateCache");
        _cacheFilePath = Path.Combine(cacheDir, "filehashcache.json");
        
        // Ensure directory exists before trying to load cache (fixes first-run race condition)
        try
        {
            if (!Directory.Exists(cacheDir))
            {
                Directory.CreateDirectory(cacheDir);
            }
        }
        catch (Exception ex)
        {
            Logger.Warning("UpdateService", $"Failed to create cache directory: {ex.Message}");
        }
        
        LoadCache();
    }

    private void LoadCache()
    {
        try
        {
            if (File.Exists(_cacheFilePath))
            {
                var json = File.ReadAllText(_cacheFilePath);
                _cache = JsonSerializer.Deserialize<Dictionary<string, FileHashEntry>>(json) ?? new();
            }
        }
        catch
        {
            _cache = new();
        }
    }

    private void SaveCache()
    {
        try
        {
            var dir = Path.GetDirectoryName(_cacheFilePath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);
            var json = JsonSerializer.Serialize(_cache, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(_cacheFilePath, json);
        }
        catch (Exception ex)
        {
            Logger.Debug("FileHashCache", $"Failed to save cache: {ex.Message}");
        }
    }

    /// <summary>
    /// Gets cached hash if file hasn't changed, or null if re-hash is needed.
    /// </summary>
    public string? GetCachedHash(string filePath)
    {
        var key = filePath.ToLowerInvariant();
        if (_cache.TryGetValue(key, out var entry))
        {
            var info = new FileInfo(filePath);
            if (info.Exists && 
                entry.Size == info.Length && 
                entry.LastWriteTimeUtc == info.LastWriteTimeUtc.ToString("O"))
            {
                return entry.Hash;
            }
        }
        return null;
    }

    /// <summary>
    /// Stores hash in cache.
    /// </summary>
    public void StoreHash(string filePath, string hash)
    {
        var key = filePath.ToLowerInvariant();
        var info = new FileInfo(filePath);
        lock (_lock)
        {
            _cache[key] = new FileHashEntry
            {
                Hash = hash,
                Size = info.Length,
                LastWriteTimeUtc = info.LastWriteTimeUtc.ToString("O")
            };
        }
        SaveCache();
    }

    private class FileHashEntry
    {
        public string Hash { get; set; } = "";
        public long Size { get; set; }
        public string LastWriteTimeUtc { get; set; } = "";
    }
}

/// <summary>
/// Service for checking, downloading, and installing updates from GitHub releases.
/// </summary>
public class UpdateService : IUpdateService
{
    private static readonly HttpClient _httpClient;
    private static DateTime _lastApiCall = DateTime.MinValue;
    private static readonly TimeSpan MinTimeBetweenApiCalls = TimeSpan.FromSeconds(5);
    private static List<GitHubRelease>? _cachedReleases;
    private static DateTime _cacheExpiry = DateTime.MinValue;
    private static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(10);
    private readonly string _repoOwner;
    private readonly string _repoName;
    private readonly string _updateChannel;
    private readonly bool _verifySignature;
    private readonly string? _updateServerUrl;

    // Circuit breaker: stop calling GitHub API after consecutive failures
    private static int _consecutiveFailures;
    private static DateTime _circuitOpenUntil = DateTime.MinValue;
    private const int CircuitBreakerThreshold = 3;
    private static readonly TimeSpan CircuitBreakerCooldown = TimeSpan.FromMinutes(30);

    static UpdateService()
    {
        var handler = CreatePinnedHandler();
        _httpClient = new HttpClient(handler);
        _httpClient.DefaultRequestHeaders.UserAgent.Add(
            new ProductInfoHeaderValue("Redball-Updater", 
                Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "0.0.0"));
    }

    /// <summary>
    /// Creates an HttpClientHandler with certificate pinning for GitHub API.
    /// Protects against MITM attacks by validating the certificate chain
    /// and pinning to known-good public key hashes.
    /// </summary>
    private static HttpClientHandler CreatePinnedHandler()
    {
        var handler = new HttpClientHandler();
        
        // GitHub API certificate pins (SHA-256 hashes of SPKI)
        // These are the expected public key hashes for GitHub's TLS certificates
        // Pins verified against DigiCert, Sectigo, and Let's Encrypt root CAs
        var pinnedHashes = new[]
        {
            // DigiCert High Assurance EV Root CA (legacy GitHub root)
            "9yF8wUfUQKd9aLkFMMnpx3xMIVC6sAu9TdjRhdZPjOI=",
            // DigiCert Global Root G2 (backup/fallback)
            "cAajgxHdb7nHsbRxqmjDn5gEjBuuZKk6YaD8n1BS1DM=",
            // Let's Encrypt ISRG Root X1 (community builds) - CORRECTED HASH
            "9Fk6HgfMnM7/vtnBHcUhg1b3gU2bIpSd50XmKZkMbGA=",
            // Let's Encrypt R12 Intermediate (for *.github.io)
            "ALUp8i2ObzHom0yteD763OkM0dJPVdAf0tkMO9fxm2U=",
            // GitHub.io wildcard certificate (release-assets.githubusercontent.com)
            "FgUf9sJof4ufBKwJ2tXxyWz4UnlW8a2leYfUVTuIguA=",
            // Sectigo Public Server Authentication Root E46 (GitHub's current root as of 2026)
            "EdsvlytFf4a/O+hCPwBXFFi46RKXqivCAF+mO7s+5Ng=",
            // Sectigo Public Server Authentication CA DV E36 (intermediate)
            "VqePxH3EcFwZuYK3CCOMz5HKMoeIZpZcEyBf4diPGSA=",
        };

        handler.ServerCertificateCustomValidationCallback = (request, cert, chain, errors) =>
        {
            if (cert == null || chain == null)
            {
                Logger.Error("UpdateService", "Certificate pinning failed: null certificate or chain");
                return false;
            }

            // First, verify the standard chain
            var chainValid = errors == System.Net.Security.SslPolicyErrors.None;
            
            // Get the certificate's public key hash
            var publicKey = cert.GetPublicKey();
            var publicKeyHash = Convert.ToBase64String(SHA256.HashData(publicKey));
            
            // Check if the public key hash matches any pinned hash
            var isPinned = pinnedHashes.Contains(publicKeyHash, StringComparer.OrdinalIgnoreCase);
            
            // Also check intermediate certificates for pinning
            var chainPinned = false;
            foreach (var element in chain.ChainElements)
            {
                var elementKey = element.Certificate.GetPublicKey();
                var elementHash = Convert.ToBase64String(SHA256.HashData(elementKey));
                if (pinnedHashes.Contains(elementHash, StringComparer.OrdinalIgnoreCase))
                {
                    chainPinned = true;
                    break;
                }
            }

            if (!chainValid)
            {
                Logger.Warning("UpdateService", $"Certificate chain validation failed for {request.RequestUri?.Host}: {errors}");
            }

            if (!isPinned && !chainPinned)
            {
                // Log all chain certificate hashes for debugging
                var chainHashes = string.Join(", ", chain.ChainElements.Select(e => {
                    var key = e.Certificate.GetPublicKey();
                    var hash = Convert.ToBase64String(SHA256.HashData(key));
                    return $"{e.Certificate.Subject}={hash}";
                }));
                
                Logger.Error("UpdateService", 
                    $"Certificate pinning FAILED for {request.RequestUri?.Host}. " +
                    $"Public key hash {publicKeyHash} not in pinned set. " +
                    $"Chain hashes: [{chainHashes}]" +
                    "Possible MITM attack detected!");
                
                // Log security event
                Debug.WriteLine($"[SECURITY] Certificate pinning failure for {request.RequestUri?.Host}: Unexpected hash {publicKeyHash}");
                
                return false;
            }

            if (isPinned || chainPinned)
            {
                Logger.Debug("UpdateService", 
                    $"Certificate pinning validated for {request.RequestUri?.Host}");
            }

            return chainValid;
        };

        return handler;
    }

    private static string NormalizeRelativeUpdatePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new InvalidOperationException("Update manifest contained an empty file path.");
        }

        var normalized = path.Replace('/', Path.DirectorySeparatorChar)
            .TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        if (Path.IsPathRooted(normalized))
        {
            throw new InvalidOperationException($"Update manifest contained a rooted path: {path}");
        }

        var fullPath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, normalized));
        var appBase = Path.GetFullPath(AppContext.BaseDirectory);
        if (!fullPath.StartsWith(appBase, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"Update manifest path escapes app directory: {path}");
        }

        return normalized;
    }

    public UpdateService(string repoOwner, string repoName, string updateChannel = "stable", bool verifySignature = false, string? updateServerUrl = null)
    {
        _repoOwner = repoOwner;
        _repoName = repoName;
        _updateChannel = updateChannel;
        _verifySignature = verifySignature;
        _updateServerUrl = updateServerUrl;
    }

    /// <summary>
    /// Checks for updates by comparing current version with latest GitHub release.
    /// Includes automatic retry logic for transient failures.
    /// </summary>
    /// <returns>Update info if an update is available, null if up to date or error.</returns>
    public async Task<UpdateInfo?> CheckForUpdateAsync(CancellationToken cancellationToken = default)
    {
        // Circuit breaker: skip if too many recent failures
        if (_consecutiveFailures >= CircuitBreakerThreshold && DateTime.UtcNow < _circuitOpenUntil)
        {
            Logger.Warning("UpdateService", $"Circuit breaker open — skipping update check until {_circuitOpenUntil:HH:mm:ss UTC}");
            return null;
        }

        // Retry logic for transient failures
        int maxRetries = 3;
        for (int attempt = 0; attempt <= maxRetries; attempt++)
        {
            try
            {
                var result = await CheckForUpdateInternalAsync(cancellationToken);
                
                if (result != null || attempt == maxRetries)
                {
                    return result;
                }
                
                // If result is null but no exception, check if we should retry
                if (attempt < maxRetries)
                {
                    var delay = TimeSpan.FromSeconds(Math.Pow(2, attempt)); // 1s, 2s, 4s
                    Logger.Debug("UpdateService", $"Update check returned null, retrying in {delay.TotalSeconds}s (attempt {attempt + 1}/{maxRetries})");
                    await Task.Delay(delay, cancellationToken);
                }
            }
            catch (HttpRequestException ex) when (ex.Message.Contains("rate limit", StringComparison.OrdinalIgnoreCase))
            {
                // Don't retry on rate limit
                Logger.Warning("UpdateService", "GitHub API rate limit hit - not retrying");
                return null;
            }
            catch (Exception ex) when (attempt < maxRetries && IsTransientError(ex))
            {
                var delay = TimeSpan.FromSeconds(Math.Pow(2, attempt)); // 1s, 2s, 4s
                Logger.Warning("UpdateService", $"Update check failed (attempt {attempt + 1}/{maxRetries + 1}): {ex.Message}. Retrying in {delay.TotalSeconds}s...");
                await Task.Delay(delay, cancellationToken);
            }
            catch (Exception ex)
            {
                _consecutiveFailures++;
                Logger.Error("UpdateService", $"Update check failed after {attempt + 1} attempts", ex);
                return null;
            }
        }

        return null;
    }

    /// <summary>
    /// Internal implementation of update check (extracted for retry logic).
    /// </summary>
    private async Task<UpdateInfo?> CheckForUpdateInternalAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var currentVersion = Assembly.GetExecutingAssembly().GetName().Version;
            Logger.Debug("UpdateService", $"Current assembly version: {currentVersion}");
            
            if (currentVersion == null)
            {
                Logger.Warning("UpdateService", "Could not get current assembly version");
                return null;
            }
            
            // Get all releases and find the highest version
            var allReleases = await GetAllReleasesAsync(cancellationToken);
            var latestRelease = FindHighestVersionRelease(allReleases);
            
            if (latestRelease == null)
            {
                Logger.Warning("UpdateService", "Could not find any valid release");
                return null;
            }

            // Parse version from tag
            var tagName = latestRelease.TagName.TrimStart('v', 'V');
            if (!Version.TryParse(tagName, out var latestVersion))
            {
                Logger.Error("UpdateService", $"Failed to parse version from tag: {tagName}");
                return null;
            }
            
            // Normalize versions
            var currentNormalized = new Version(currentVersion.Major, currentVersion.Minor, currentVersion.Build);
            var latestNormalized = new Version(latestVersion.Major, latestVersion.Minor, latestVersion.Build);
            
            if (latestNormalized <= currentNormalized)
            {
                Logger.Info("UpdateService", $"Up to date (current: {currentNormalized}, latest: {latestNormalized})");
                return null;
            }

            // Check for manifest.json for differential updates
            var manifestAsset = latestRelease.Assets.Find(a => a.Name.Equals("manifest.json", StringComparison.OrdinalIgnoreCase));
            if (manifestAsset != null)
            {
                Logger.Info("UpdateService", "Found update manifest, checking for differential updates...");
                var manifestJson = await _httpClient.GetStringAsync(manifestAsset.DownloadUrl, cancellationToken);
                var manifest = JsonSerializer.Deserialize<UpdateManifest>(manifestJson);
                
                if (manifest != null)
                {
                    var filesToUpdate = new List<FileUpdateInfo>();
                    var appDir = AppDomain.CurrentDomain.BaseDirectory;
                    var hashCache = new FileHashCache();
                    int cachedHashesUsed = 0;
                    int filesHashed = 0;
                    
                    foreach (var file in manifest.Files)
                    {
                        var normalizedName = NormalizeRelativeUpdatePath(file.Name);
                        var localPath = Path.Combine(appDir, normalizedName);
                        bool needsUpdate = true;
                        
                        if (File.Exists(localPath))
                        {
                            string? localHash = null;
                            
                            // Try to get cached hash first
                            var cachedHash = hashCache.GetCachedHash(localPath);
                            if (cachedHash != null)
                            {
                                localHash = cachedHash.ToUpper();
                                cachedHashesUsed++;
                            }
                            else
                            {
                                // Calculate hash and cache it
                                localHash = (await CalculateHashAsync(localPath)).ToUpper();
                                hashCache.StoreHash(localPath, localHash);
                                filesHashed++;
                            }
                            
                            if (localHash == file.Hash.ToUpper())
                            {
                                needsUpdate = false;
                            }
                        }
                        
                        if (needsUpdate)
                        {
                            var asset = latestRelease.Assets.Find(a => a.Name.Equals(Path.GetFileName(file.Name), StringComparison.OrdinalIgnoreCase));
                            if (asset != null)
                            {
                                // --- SECURITY: Signature Verification ---
                                if (_verifySignature && !string.IsNullOrEmpty(file.Signature))
                                {
                                    Logger.Debug("UpdateService", $"Signature available for {normalizedName}, verification will happen after download.");
                                }
                                else if (_verifySignature)
                                {
                                    Logger.Warning("UpdateService", $"MISSING core signature for {normalizedName}! Skipping differential update for security.");
                                    filesToUpdate.Clear();
                                    break;
                                }

                                // Check if binary delta patch is available (preferred for bandwidth)
                                var patchAsset = latestRelease.Assets.Find(a => 
                                    a.Name.Equals(Path.GetFileName(file.Name) + ".patch", StringComparison.OrdinalIgnoreCase));
                                
                                if (patchAsset != null && patchAsset.Size > 0 && patchAsset.Size < file.Size)
                                {
                                    // Use delta patch - much smaller download
                                    filesToUpdate.Add(new FileUpdateInfo
                                    {
                                        Name = normalizedName,
                                        DownloadUrl = asset.DownloadUrl, // Fallback full file
                                        PatchUrl = patchAsset.DownloadUrl,
                                        PatchSize = patchAsset.Size,
                                        Hash = file.Hash,
                                        Size = file.Size,
                                        Signature = file.Signature
                                    });
                                    Logger.Debug("UpdateService", $"Using delta patch for {normalizedName}: {patchAsset.Size} bytes vs {file.Size} bytes full file");
                                }
                                else
                                {
                                    // Use full file download
                                    filesToUpdate.Add(new FileUpdateInfo
                                    {
                                        Name = normalizedName,
                                        DownloadUrl = asset.DownloadUrl,
                                        Hash = file.Hash,
                                        Size = file.Size,
                                        Signature = file.Signature
                                    });
                                }
                            }
                        }
                    }
                    
                    Logger.Info("UpdateService", $"Differential check complete: {filesHashed} files hashed, {cachedHashesUsed} cached hashes used");
                    
                    if (filesToUpdate.Count > 0)
                    {
                        Logger.Info("UpdateService", $"Differential update available: {filesToUpdate.Count} files need updating.");
                        _consecutiveFailures = 0;
                        return new UpdateInfo
                        {
                            CurrentVersion = currentNormalized,
                            LatestVersion = latestNormalized,
                            FilesToUpdate = filesToUpdate,
                            ReleaseNotes = latestRelease.Body,
                            ReleaseDate = latestRelease.PublishedAt
                        };
                    }
                }
            }

            // Fallback to full asset (MSI/ZIP)
            var bestAsset = FindBestAsset(latestRelease);
            if (bestAsset == null)
                return null;

            _consecutiveFailures = 0;
            return new UpdateInfo
            {
                CurrentVersion = currentNormalized,
                LatestVersion = latestNormalized,
                DownloadUrl = bestAsset.DownloadUrl,
                FileName = bestAsset.Name,
                ReleaseNotes = latestRelease.Body,
                ReleaseDate = latestRelease.PublishedAt
            };
        }
        catch (Exception)
        {
            // Re-throw for retry logic to handle
            throw;
        }
    }

    /// <summary>
    /// Determines if an exception is transient and should be retried.
    /// </summary>
    private bool IsTransientError(Exception ex)
    {
        return ex is HttpRequestException ||
               ex is TimeoutException ||
               ex is TaskCanceledException ||
               (ex is IOException ioEx && 
                (ioEx.Message.Contains("being used by another process") ||
                 ioEx.Message.Contains("access is denied") ||
                 ioEx.Message.Contains("cannot access")));
    }

    /// <summary>
    /// Downloads and installs the update.
    /// </summary>
    /// <param name="updateInfo">Update information from CheckForUpdateAsync.</param>
    /// <param name="progress">Callback for download progress (0-100).</param>
    /// <returns>True if update was downloaded and prepared successfully.</returns>
    /// <summary>
    /// Downloads and installs the update.
    /// </summary>
    /// <param name="updateInfo">Update information from CheckForUpdateAsync.</param>
    /// <param name="progress">Callback for download progress.</param>
    /// <returns>True if update was downloaded and prepared successfully.</returns>
    public async Task<bool> DownloadAndInstallAsync(UpdateInfo updateInfo, IProgress<UpdateDownloadProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        try
        {
            var tempDir = Path.Combine(Path.GetTempPath(), "RedballUpdate");
            
            // Retry temp directory cleanup (handles race conditions with AV scanning)
            for (int i = 0; i < 3; i++)
            {
                try
                {
                    if (Directory.Exists(tempDir))
                    {
                        Directory.Delete(tempDir, true);
                    }
                    break;
                }
                catch (IOException) when (i < 2)
                {
                    Logger.Warning("UpdateService", $"Temp directory cleanup attempt {i + 1} failed, retrying...");
                    await Task.Delay(500, cancellationToken);
                }
            }
            
            Directory.CreateDirectory(tempDir);
            var stagingDir = Path.Combine(tempDir, "staging");
            Directory.CreateDirectory(stagingDir);

            if (updateInfo.FilesToUpdate != null && updateInfo.FilesToUpdate.Count > 0)
            {
                Logger.Info("UpdateService", $"Starting differential download of {updateInfo.FilesToUpdate.Count} files...");
                int completed = 0;
                long totalBytesSaved = 0;
                var appDir = AppDomain.CurrentDomain.BaseDirectory;
                
                foreach (var file in updateInfo.FilesToUpdate)
                {
                    var normalizedName = NormalizeRelativeUpdatePath(file.Name);
                    var destPath = Path.Combine(stagingDir, normalizedName);
                    var destDir = Path.GetDirectoryName(destPath);
                    if (destDir != null && !Directory.Exists(destDir)) Directory.CreateDirectory(destDir);
                    
                    // Try binary delta patch first if available
                    if (!string.IsNullOrEmpty(file.PatchUrl) && file.PatchSize > 0)
                    {
                        try
                        {
                            var localFilePath = Path.Combine(appDir, normalizedName);
                            if (File.Exists(localFilePath))
                            {
                                Logger.Info("UpdateService", $"Downloading binary delta patch for {normalizedName}...");
                                var patchPath = Path.Combine(tempDir, normalizedName + ".patch");
                                
                                if (await DownloadFileAsync(file.PatchUrl, patchPath, progress, cancellationToken))
                                {
                                    // Apply the patch using DeltaUpdateService
                                    var oldData = await File.ReadAllBytesAsync(localFilePath, cancellationToken);
                                    var patchData = await File.ReadAllBytesAsync(patchPath, cancellationToken);
                                    
                                    var patch = new DeltaPatch
                                    {
                                        Data = patchData,
                                        NewFileHash = file.Hash,
                                        NewFileSize = file.Size
                                    };
                                    
                                    var newData = await DeltaUpdateService.Instance.ApplyPatchAsync(oldData, patch, cancellationToken);
                                    await File.WriteAllBytesAsync(destPath, newData, cancellationToken);
                                    
                                    // Verify hash
                                    var actualHash = await CalculateHashAsync(destPath);
                                    if (!actualHash.Equals(file.Hash, StringComparison.OrdinalIgnoreCase))
                                    {
                                        Logger.Warning("UpdateService", $"Patch result hash mismatch for {normalizedName}, falling back to full download");
                                        File.Delete(destPath);
                                        throw new InvalidOperationException("Patch hash mismatch");
                                    }
                                    
                                    totalBytesSaved += (file.Size - file.PatchSize);
                                    Logger.Info("UpdateService", $"Successfully applied delta patch for {normalizedName} (saved {file.Size - file.PatchSize} bytes)");
                                    
                                    // Clean up patch file
                                    TryDeleteFile(patchPath);
                                    completed++;
                                    progress?.Report(new UpdateDownloadProgress 
                                    { 
                                        Percentage = completed * 100 / updateInfo.FilesToUpdate.Count,
                                        BytesReceived = completed,
                                        TotalBytes = updateInfo.FilesToUpdate.Count,
                                        StatusText = $"Patched {completed} of {updateInfo.FilesToUpdate.Count}: {normalizedName}"
                                    });
                                    continue; // Skip full download
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            Logger.Warning("UpdateService", $"Delta patch failed for {normalizedName}, falling back to full download: {ex.Message}");
                            // Fall through to full download
                        }
                    }
                    
                    // Full file download (fallback or no patch available)
                    if (!await DownloadFileAsync(file.DownloadUrl, destPath, progress, cancellationToken))
                        return false;
                    
                    // --- SECURITY: Hash/Signature Verification ---
                    if (_verifySignature && !string.IsNullOrEmpty(file.Signature))
                    {
                        var actualHash = await CalculateHashAsync(destPath);
                        if (!actualHash.Equals(file.Hash, StringComparison.OrdinalIgnoreCase))
                        {
                            Logger.Error("UpdateService", $"SECURITY ALERT: Integrity check failed for {normalizedName}! Expected {file.Hash}, got {actualHash}");
                            return false;
                        }
                        Logger.Info("UpdateService", $"Integrity verified for {normalizedName}");
                    }

                    completed++;
                    progress?.Report(new UpdateDownloadProgress 
                    { 
                        Percentage = completed * 100 / updateInfo.FilesToUpdate.Count,
                        BytesReceived = completed,
                        TotalBytes = updateInfo.FilesToUpdate.Count,
                        StatusText = $"File {completed} of {updateInfo.FilesToUpdate.Count}: {normalizedName}"
                    });
                }
                
                if (totalBytesSaved > 0)
                {
                    Logger.Info("UpdateService", $"Delta patching saved {totalBytesSaved} bytes total");
                }
                
                var scriptPath = CreateUpdateScript(stagingDir);
                if (scriptPath == null) return false;
                
                Process.Start(new ProcessStartInfo
                {
                    FileName = "powershell.exe",
                    Arguments = $"-ExecutionPolicy Bypass -File \"{scriptPath}\"",
                    UseShellExecute = true,
                    CreateNoWindow = false
                });
                return true;
            }

            // Check for cached update file with hash verification
            var downloadPath = Path.Combine(tempDir, updateInfo.FileName);
            var cachedFile = await GetCachedUpdateFileAsync(updateInfo, cancellationToken);
            if (cachedFile != null)
            {
                Logger.Info("UpdateService", "Using cached update file (hash verified)");
                downloadPath = cachedFile;
            }
            else
            {
                // Download the file
                Logger.Info("UpdateService", "Downloading update file...");
                if (!await DownloadFileAsync(updateInfo.DownloadUrl, downloadPath, progress, cancellationToken))
                    return false;

                // Cache the downloaded file with its hash
                await CacheUpdateFileAsync(updateInfo, downloadPath);
            }

            // --- SECURITY: Trust Chain Validation (sec-3) ---
            if (_verifySignature)
            {
                Logger.Info("UpdateService", "Performing trust chain validation on installer...");
                progress?.Report(new UpdateDownloadProgress
                {
                    Percentage = 99,
                    StatusText = "Verifying package trust chain..."
                });

                var trustResult = SecurityService.ValidateUpdatePackage(downloadPath, updateInfo.ManifestHash);
                
                if (!trustResult.IsTrusted)
                {
                    Logger.Error("UpdateService", $"SECURITY ALERT: Update package failed trust validation! {trustResult.Summary}");
                    Logger.Error("UpdateService", $"Failures: {string.Join(", ", trustResult.Failures)}");
                    
                    // Log warnings but don't necessarily block - depends on strict mode
                    foreach (var warning in trustResult.Warnings)
                    {
                        Logger.Warning("UpdateService", $"Trust validation warning: {warning}");
                    }

                    // If Authenticode is invalid or manifest hash doesn't match, always block
                    if (!trustResult.AuthenticodeValid || 
                        (!string.IsNullOrEmpty(updateInfo.ManifestHash) && !trustResult.ManifestHashValid))
                    {
                        Logger.Error("UpdateService", "Update blocked due to failed trust validation");
                        return false;
                    }
                }
                else
                {
                    Logger.Info("UpdateService", $"Update package trust validated: {trustResult.Summary}");
                    if (!string.IsNullOrEmpty(trustResult.Thumbprint))
                    {
                        Logger.Debug("UpdateService", $"Certificate thumbprint: {trustResult.Thumbprint}");
                    }
                }

                // Store trust result in updateInfo for UI display
                updateInfo.TrustValidation = trustResult;
            }

            if (updateInfo.FileName.EndsWith(".msi", StringComparison.OrdinalIgnoreCase) ||
                updateInfo.FileName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            {
                var installerScriptPath = CreateInstallerLaunchScript(downloadPath, updateInfo.FileName);
                if (installerScriptPath == null) return false;

                Process.Start(new ProcessStartInfo
                {
                    FileName = "powershell.exe",
                    Arguments = $"-ExecutionPolicy Bypass -File \"{installerScriptPath}\"",
                    UseShellExecute = true,
                    CreateNoWindow = false
                });
                return true;
            }

            // ZIP extraction fallback
            if (updateInfo.FileName.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            {
                if (!ExtractZip(downloadPath, stagingDir)) return false;
                var scriptPath = CreateUpdateScript(stagingDir);
                if (scriptPath == null) return false;
                Process.Start(new ProcessStartInfo
                {
                    FileName = "powershell.exe",
                    Arguments = $"-ExecutionPolicy Bypass -File \"{scriptPath}\"",
                    UseShellExecute = true,
                    CreateNoWindow = false
                });
                return true;
            }

            return false;
        }
        catch (Exception ex)
        {
            Logger.Error("UpdateService", "Update download/install failed", ex);
            return false;
        }
    }

    /// <summary>
    /// Gets the cache directory for update files.
    /// </summary>
    private static string GetUpdateCacheDirectory()
    {
        var cacheDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Redball",
            "UpdateCache");
        
        if (!Directory.Exists(cacheDir))
        {
            Directory.CreateDirectory(cacheDir);
        }
        
        return cacheDir;
    }

    /// <summary>
    /// Tries to get a cached update file if it exists and hash matches.
    /// Returns the cached file path if valid, null otherwise.
    /// </summary>
    private async Task<string?> GetCachedUpdateFileAsync(UpdateInfo updateInfo, CancellationToken cancellationToken)
    {
        try
        {
            if (string.IsNullOrEmpty(updateInfo.FileName) || 
                string.IsNullOrEmpty(updateInfo.DownloadUrl))
            {
                return null;
            }

            var cacheDir = GetUpdateCacheDirectory();
            var cachedFile = Path.Combine(cacheDir, updateInfo.FileName);
            var hashFile = cachedFile + ".sha256";

            // Check if cached file exists
            if (!File.Exists(cachedFile) || !File.Exists(hashFile))
            {
                Logger.Debug("UpdateService", "No cached update file found");
                return null;
            }

            // Read stored hash
            var storedHash = await File.ReadAllTextAsync(hashFile, cancellationToken);
            storedHash = storedHash.Trim().ToLowerInvariant();

            if (string.IsNullOrEmpty(storedHash))
            {
                Logger.Warning("UpdateService", "Cached hash file is empty, deleting cache");
                TryDeleteFile(cachedFile);
                TryDeleteFile(hashFile);
                return null;
            }

            // Calculate actual hash of cached file
            var actualHash = await CalculateHashAsync(cachedFile);
            
            if (!actualHash.Equals(storedHash, StringComparison.OrdinalIgnoreCase))
            {
                Logger.Warning("UpdateService", "Cached file hash mismatch, deleting cache");
                TryDeleteFile(cachedFile);
                TryDeleteFile(hashFile);
                return null;
            }

            // Also check file size if available
            var fileInfo = new FileInfo(cachedFile);
            if (updateInfo.FilesToUpdate?.Count > 0)
            {
                var totalSize = updateInfo.FilesToUpdate.Sum(f => f.Size);
                if (fileInfo.Length != totalSize)
                {
                    Logger.Warning("UpdateService", $"Cached file size mismatch (expected: {totalSize}, actual: {fileInfo.Length})");
                    TryDeleteFile(cachedFile);
                    TryDeleteFile(hashFile);
                    return null;
                }
            }

            Logger.Info("UpdateService", $"Cached update file verified (hash: {actualHash[..16]}...)");
            return cachedFile;
        }
        catch (Exception ex)
        {
            Logger.Warning("UpdateService", $"Error checking cached file: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Caches an update file with its hash for future use.
    /// </summary>
    private async Task CacheUpdateFileAsync(UpdateInfo updateInfo, string filePath)
    {
        try
        {
            if (string.IsNullOrEmpty(updateInfo.FileName))
            {
                return;
            }

            var cacheDir = GetUpdateCacheDirectory();
            var cachedFile = Path.Combine(cacheDir, updateInfo.FileName);
            var hashFile = cachedFile + ".sha256";

            // Clean up old cache files first (keep only last 3 versions)
            CleanupOldCacheFiles(cacheDir, updateInfo.FileName);

            // Copy file to cache
            File.Copy(filePath, cachedFile, true);

            // Calculate and store hash
            var hash = await CalculateHashAsync(cachedFile);
            await File.WriteAllTextAsync(hashFile, hash);

            Logger.Info("UpdateService", $"Update file cached (hash: {hash[..16]}...)");
        }
        catch (Exception ex)
        {
            Logger.Warning("UpdateService", $"Failed to cache update file: {ex.Message}");
            // Non-critical error, don't fail the update
        }
    }

    /// <summary>
    /// Cleans up old cached update files, keeping only the most recent ones.
    /// </summary>
    private void CleanupOldCacheFiles(string cacheDir, string currentFileName)
    {
        try
        {
            var cacheFiles = Directory.GetFiles(cacheDir, "*.msi")
                .Concat(Directory.GetFiles(cacheDir, "*.exe"))
                .Select(f => new FileInfo(f))
                .Where(f => f.Name != currentFileName)
                .OrderByDescending(f => f.LastWriteTime)
                .Skip(2) // Keep 2 most recent
                .ToList();

            foreach (var file in cacheFiles)
            {
                try
                {
                    TryDeleteFile(file.FullName);
                    TryDeleteFile(file.FullName + ".sha256");
                    Logger.Debug("UpdateService", $"Cleaned up old cache file: {file.Name}");
                }
                catch (Exception ex)
                {
                    Logger.Debug("UpdateService", $"Failed to cleanup cache file {file.Name}: {ex.Message}");
                }
            }
        }
        catch (Exception ex)
        {
            Logger.Debug("UpdateService", $"Cache cleanup error: {ex.Message}");
        }
    }

    /// <summary>
    /// Safely tries to delete a file, ignoring errors.
    /// </summary>
    private static void TryDeleteFile(string filePath)
    {
        try
        {
            if (File.Exists(filePath))
            {
                File.Delete(filePath);
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[UpdateService] Failed to delete file {filePath}: {ex.Message}");
        }
    }

    private async Task<string> CalculateHashAsync(string filePath)
    {
        using var sha256 = SHA256.Create();
        using var stream = File.OpenRead(filePath);
        var hashBytes = await sha256.ComputeHashAsync(stream);
        return BitConverter.ToString(hashBytes).Replace("-", "");
    }



    private async Task<List<GitHubRelease>> GetAllReleasesAsync(CancellationToken cancellationToken = default)
    {
        // Check cache first
        if (_cachedReleases != null && DateTime.UtcNow < _cacheExpiry)
        {
            Logger.Debug("UpdateService", "Using cached releases (valid for another " + 
                (_cacheExpiry - DateTime.UtcNow).TotalMinutes.ToString("F1") + " min)");
            return _cachedReleases;
        }

        // Rate limiting: ensure minimum time between API calls
        var timeSinceLastCall = DateTime.UtcNow - _lastApiCall;
        if (timeSinceLastCall < MinTimeBetweenApiCalls)
        {
            var delay = MinTimeBetweenApiCalls - timeSinceLastCall;
            Logger.Debug("UpdateService", $"Rate limiting: waiting {delay.TotalSeconds:F1}s before API call");
            await Task.Delay(delay, cancellationToken);
        }

        // Try update-server first if configured (for delta patches)
        if (!string.IsNullOrEmpty(_updateServerUrl))
        {
            try
            {
                var updateServerUrl = $"{_updateServerUrl.TrimEnd('/')}/api/github/releases";
                Logger.Debug("UpdateService", $"Trying update-server: {updateServerUrl}");
                
                _lastApiCall = DateTime.UtcNow;
                var response = await _httpClient.GetAsync(updateServerUrl, cancellationToken);
                
                if (response.IsSuccessStatusCode)
                {
                    var content = await response.Content.ReadAsStringAsync(cancellationToken);
                    var releases = JsonSerializer.Deserialize<List<GitHubRelease>>(content, new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true
                    });
                    
                    if (releases != null && releases.Count > 0)
                    {
                        _cachedReleases = releases;
                        _cacheExpiry = DateTime.UtcNow + CacheDuration;
                        Logger.Info("UpdateService", $"Using update-server with {releases.Count} releases (includes delta patches)");
                        return releases;
                    }
                }
                
                Logger.Warning("UpdateService", "Update-server unavailable, falling back to GitHub");
            }
            catch (Exception ex)
            {
                Logger.Warning("UpdateService", $"Update-server failed: {ex.Message}, falling back to GitHub");
            }
        }

        // Fallback to GitHub API
        var url = $"https://api.github.com/repos/{_repoOwner}/{_repoName}/releases";
        Logger.Debug("UpdateService", $"Fetching releases from GitHub: {url}");
        
        try
        {
            _lastApiCall = DateTime.UtcNow;
            var response = await _httpClient.GetAsync(url, cancellationToken);
            
            // Handle rate limiting specifically
            if (response.StatusCode == System.Net.HttpStatusCode.Forbidden)
            {
                var retryAfter = response.Headers.RetryAfter?.Delta ?? TimeSpan.FromMinutes(5);
                Logger.Warning("UpdateService", $"GitHub API rate limit exceeded. Retry after: {retryAfter.TotalMinutes:F1} min");
                
                // Open circuit breaker to prevent further calls
                _consecutiveFailures = CircuitBreakerThreshold;
                _circuitOpenUntil = DateTime.UtcNow + retryAfter;
                
                // Return cached data if available, even if expired
                if (_cachedReleases != null)
                {
                    Logger.Info("UpdateService", "Returning stale cached releases due to rate limit");
                    return _cachedReleases;
                }
                
                throw new HttpRequestException($"GitHub API rate limit exceeded. Please try again after {retryAfter.TotalMinutes:F0} minutes.");
            }
            
            response.EnsureSuccessStatusCode();
            var content = await response.Content.ReadAsStringAsync(cancellationToken);
            Logger.Debug("UpdateService", $"API response length: {content.Length} chars");
            
            var releases = JsonSerializer.Deserialize<List<GitHubRelease>>(content, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });
            
            _cachedReleases = releases ?? new List<GitHubRelease>();
            _cacheExpiry = DateTime.UtcNow + CacheDuration;
            
            Logger.Info("UpdateService", $"Cached {releases?.Count ?? 0} releases for {CacheDuration.TotalMinutes:F0} minutes");
            return _cachedReleases;
        }
        catch (HttpRequestException ex) when (ex.Message.Contains("rate limit"))
        {
            throw;
        }
        catch (HttpRequestException ex)
        {
            Logger.Error("UpdateService", $"HTTP error fetching releases: {ex.Message}");
            throw;
        }
    }

    /// <summary>
    /// Gets changelogs for all releases between the current version and latest version (inclusive of latest).
    /// Returns them sorted newest-first so the user sees the most recent changes at the top.
    /// </summary>
    public async Task<List<VersionChangelog>> GetChangelogBetweenVersionsAsync(Version currentVersion, Version latestVersion, CancellationToken cancellationToken = default)
    {
        try
        {
            var allReleases = await GetAllReleasesAsync(cancellationToken);
            var currentNormalized = new Version(currentVersion.Major, currentVersion.Minor, currentVersion.Build);
            var latestNormalized = new Version(latestVersion.Major, latestVersion.Minor, latestVersion.Build);

            var changelogs = new List<VersionChangelog>();

            foreach (var release in allReleases)
            {
                if (release.IsDraft) continue;
                if (release.IsPreRelease && _updateChannel != "beta" && _updateChannel != "alpha") continue;

                var tagName = release.TagName.TrimStart('v', 'V');
                if (!Version.TryParse(tagName, out var version)) continue;

                var vNorm = new Version(version.Major, version.Minor, version.Build);

                // Include versions that are newer than current and up to (including) latest
                if (vNorm > currentNormalized && vNorm <= latestNormalized)
                {
                    changelogs.Add(new VersionChangelog
                    {
                        Version = vNorm,
                        TagName = release.TagName,
                        ReleaseNotes = release.Body ?? "",
                        ReleaseDate = release.PublishedAt,
                        IsPreRelease = release.IsPreRelease
                    });
                }
            }

            // Sort newest first
            changelogs.Sort((a, b) => b.Version.CompareTo(a.Version));
            Logger.Info("UpdateService", $"Found {changelogs.Count} changelog entries between {currentNormalized} and {latestNormalized}");
            return changelogs;
        }
        catch (Exception ex)
        {
            Logger.Error("UpdateService", "Failed to get changelogs", ex);
            return new List<VersionChangelog>();
        }
    }

    private GitHubRelease? FindHighestVersionRelease(List<GitHubRelease> releases)
    {
        Logger.Debug("UpdateService", $"Checking {releases.Count} releases for highest version");
        
        GitHubRelease? highestRelease = null;
        Version? highestVersion = null;
        
        foreach (var release in releases)
        {
            // Skip drafts and pre-releases if not on a pre-release channel
            if (release.IsDraft) continue;
            if (release.IsPreRelease && _updateChannel != "beta" && _updateChannel != "alpha") continue;
            
            // Parse version from tag (remove 'v' prefix if present)
            var tagName = release.TagName.TrimStart('v', 'V');
            if (!Version.TryParse(tagName, out var version))
            {
                Logger.Warning("UpdateService", $"Could not parse version from tag: {release.TagName}");
                continue;
            }
            
            Logger.Verbose("UpdateService", $"Release {release.TagName} -> version {version}");
            
            // Compare versions (only compare Major.Minor.Build, ignore Revision)
            var versionToCompare = new Version(version.Major, version.Minor, version.Build);
            var currentHighest = highestVersion == null ? null : new Version(highestVersion.Major, highestVersion.Minor, highestVersion.Build);
            
            if (highestVersion == null || versionToCompare > currentHighest!)
            {
                highestVersion = version;
                highestRelease = release;
                Logger.Debug("UpdateService", $"New highest version found: {version} from {release.TagName}");
            }
        }
        
        if (highestRelease != null)
        {
            Logger.Info("UpdateService", $"Highest version release: {highestRelease.TagName} ({highestVersion})");
        }
        else
        {
            Logger.Warning("UpdateService", "No valid version found in releases");
        }
        
        return highestRelease;
    }

    private GitHubAsset? FindBestAsset(GitHubRelease release)
    {
        // Priority: full installer bundle > MSI installer > any exe/msi
        var priorities = new[]
        {
            "Redball.msi",
            "Redball-Setup-",
            "Redball-Setup.exe",
            ".msi",
            ".exe"
        };

        foreach (var priority in priorities)
        {
            var asset = release.Assets.Find(a => 
                a.Name.Contains(priority, StringComparison.OrdinalIgnoreCase) &&
                !a.Name.Contains("debug", StringComparison.OrdinalIgnoreCase));
            
            if (asset != null)
            {
                Logger.Debug("UpdateService", $"Found matching asset: {asset.Name}");
                return asset;
            }
        }

        // Fallback to first .exe or .msi file
        var fallbackAsset = release.Assets.Find(a => 
            (a.Name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ||
             a.Name.EndsWith(".msi", StringComparison.OrdinalIgnoreCase)) &&
            !a.Name.Contains("debug", StringComparison.OrdinalIgnoreCase));
        
        if (fallbackAsset != null)
        {
            Logger.Debug("UpdateService", $"Using fallback asset: {fallbackAsset.Name}");
        }
        else
        {
            Logger.Warning("UpdateService", "No suitable asset found in release");
        }
        
        return fallbackAsset;
    }

    private async Task<bool> DownloadFileAsync(string url, string destinationPath, IProgress<UpdateDownloadProgress>? progress, CancellationToken cancellationToken = default)
    {
        Logger.Info("UpdateService", $"Downloading update from: {url}");
        using var response = await _httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();

        var totalBytes = response.Content.Headers.ContentLength ?? -1L;
        var downloadedBytes = 0L;

        await using var contentStream = await response.Content.ReadAsStreamAsync(cancellationToken);
        // Use FileShare.Read instead of FileShare.None to avoid conflicts with AV/real-time scanning
        await using var fileStream = new FileStream(destinationPath, FileMode.Create, FileAccess.Write, FileShare.Read);

        var buffer = new byte[16384]; // Larger buffer for speed
        int read;
        var stopwatch = Stopwatch.StartNew();
        var lastReport = Stopwatch.StartNew();
        
        while ((read = await contentStream.ReadAsync(buffer, cancellationToken)) > 0)
        {
            await fileStream.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
            downloadedBytes += read;

            if (lastReport.ElapsedMilliseconds > 250 || downloadedBytes == totalBytes)
            {
                if (totalBytes > 0 && progress != null)
                {
                    var percent = (int)((downloadedBytes * 100) / totalBytes);
                    var speed = downloadedBytes / (stopwatch.Elapsed.TotalSeconds + 0.001);
                    
                    progress.Report(new UpdateDownloadProgress
                    {
                        Percentage = percent,
                        BytesReceived = downloadedBytes,
                        TotalBytes = totalBytes,
                        BytesPerSecond = speed,
                        StatusText = $"Downloading {Path.GetFileName(destinationPath)}..."
                    });
                }
                lastReport.Restart();
            }
        }

        return true;
    }

    private bool ExtractZip(string zipPath, string extractPath)
    {
        try
        {
            if (Directory.Exists(extractPath))
                Directory.Delete(extractPath, true);
            
            Directory.CreateDirectory(extractPath);
            
            // SECURITY: Extract with ZipSlip protection
            using var archive = System.IO.Compression.ZipFile.OpenRead(zipPath);
            var fullExtractPath = Path.GetFullPath(extractPath);
            
            foreach (var entry in archive.Entries)
            {
                // Validate entry path to prevent directory traversal
                var entryName = entry.FullName.Replace('/', Path.DirectorySeparatorChar);
                var entryPath = Path.GetFullPath(Path.Combine(extractPath, entryName));
                
                if (!entryPath.StartsWith(fullExtractPath, StringComparison.OrdinalIgnoreCase))
                {
                    Logger.Error("UpdateService", $"Security: ZIP entry escapes target directory: {entry.FullName}");
                    return false;
                }
                
                var entryDir = Path.GetDirectoryName(entryPath);
                if (!string.IsNullOrEmpty(entryDir))
                {
                    Directory.CreateDirectory(entryDir);
                }
                
                if (!entry.FullName.EndsWith("/"))
                {
                    entry.ExtractToFile(entryPath, overwrite: true);
                }
            }
            
            return true;
        }
        catch (Exception ex)
        {
            Logger.Error("UpdateService", "Failed to extract zip", ex);
            return false;
        }
    }

    private async Task<string?> DownloadHashFileAsync(string hashUrl, string hashFileName, CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await _httpClient.GetAsync(hashUrl, cancellationToken);
            if (!response.IsSuccessStatusCode)
                return null;

            var content = await response.Content.ReadAsStringAsync();
            // SHA256 hash files typically have format: <hash> <filename>
            // Extract just the hash (first 64 hex characters)
            var lines = content.Split('\n', StringSplitOptions.RemoveEmptyEntries);
            foreach (var line in lines)
            {
                var trimmed = line.Trim();
                if (trimmed.Contains(hashFileName) || trimmed.Length >= 64)
                {
                    // Extract hash - first 64 hex characters
                    var hash = System.Text.RegularExpressions.Regex.Match(trimmed, "^[a-fA-F0-9]{64}").Value;
                    if (!string.IsNullOrEmpty(hash))
                        return hash.ToLowerInvariant();
                }
            }
            return null;
        }
        catch (Exception ex)
        {
            Logger.Debug("UpdateService", $"DownloadHashFileAsync failed for {hashUrl}: {ex.Message}");
            return null;
        }
    }

    private bool VerifyFileHash(string filePath, string expectedHash)
    {
        try
        {
            using var stream = File.OpenRead(filePath);
            using var sha256 = SHA256.Create();
            var hash = sha256.ComputeHash(stream);
            var actualHash = BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
            return actualHash.Equals(expectedHash, StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex)
        {
            Logger.Error("UpdateService", "Hash verification error", ex);
            return false;
        }
    }

    private string? CreateUpdateScript(string sourceDir)
    {
        try
        {
            var appDir = AppContext.BaseDirectory;
            var scriptDir = Path.Combine(Path.GetTempPath(), "RedballUpdate");
            var scriptPath = Path.Combine(scriptDir, "update.ps1");
            
            // Ensure directory exists before writing script
            Directory.CreateDirectory(scriptDir);
            
            // Build PowerShell script using StringBuilder to avoid escaping hell
            var sb = new StringBuilder();
            sb.AppendLine("# Redball Auto-Update Script");
            sb.AppendLine("$ErrorActionPreference = 'Stop'");
            sb.AppendLine($"$sourceDir = '{sourceDir.Replace("'", "''")}'");
            sb.AppendLine($"$targetDir = '{appDir.Replace("'", "''")}'");
            sb.AppendLine("$processName = 'Redball.UI.WPF'");
            sb.AppendLine("$logRoot = Join-Path $env:TEMP 'RedballUpdate'");
            sb.AppendLine("if (-not (Test-Path $logRoot)) {");
            sb.AppendLine("    New-Item -ItemType Directory -Path $logRoot -Force | Out-Null");
            sb.AppendLine("}");
            sb.AppendLine("$logFile = Join-Path $logRoot 'update-error.log'");
            sb.AppendLine();
            sb.AppendLine("# UserData backup/restore helpers");
            sb.AppendLine("$backupDir = Join-Path $env:TEMP ('RedballBackup_' + (Get-Date -Format 'yyyyMMdd_HHmmss'))");
            sb.AppendLine("$protectedUserDataDir = Join-Path $targetDir 'UserData'");
            sb.AppendLine();
            sb.AppendLine("function Backup-UserData {");
            sb.AppendLine("    if (Test-Path $protectedUserDataDir) {");
            sb.AppendLine("        $backupUserData = Join-Path $backupDir 'UserData'");
            sb.AppendLine("        Write-Host ('Backing up UserData to: ' + $backupUserData)");
            sb.AppendLine("        New-Item -ItemType Directory -Path $backupUserData -Force | Out-Null");
            sb.AppendLine("        robocopy $protectedUserDataDir $backupUserData /E /COPY:DAT /R:2 /W:1 /NFL /NDL /NJH /NJS | Out-Null");
            sb.AppendLine("        if ($LASTEXITCODE -le 7) {");
            sb.AppendLine("            Write-Host 'UserData backup complete'");
            sb.AppendLine("            return $true");
            sb.AppendLine("        } else {");
            sb.AppendLine("            Write-Warning ('UserData backup may have issues (robocopy exit code: ' + $LASTEXITCODE + ')')");
            sb.AppendLine("            return $true");
            sb.AppendLine("        }");
            sb.AppendLine("    }");
            sb.AppendLine("    return $false");
            sb.AppendLine("}");
            sb.AppendLine();
            sb.AppendLine("function Restore-UserData {");
            sb.AppendLine("    $backupUserData = Join-Path $backupDir 'UserData'");
            sb.AppendLine("    if (Test-Path $backupUserData) {");
            sb.AppendLine("        Write-Host 'Restoring UserData from backup...' -ForegroundColor Yellow");
            sb.AppendLine("        if (-not (Test-Path $protectedUserDataDir)) {");
            sb.AppendLine("            New-Item -ItemType Directory -Path $protectedUserDataDir -Force | Out-Null");
            sb.AppendLine("        }");
            sb.AppendLine("        robocopy $backupUserData $protectedUserDataDir /E /COPY:DAT /R:2 /W:1 /NFL /NDL /NJH /NJS | Out-Null");
            sb.AppendLine("        if ($LASTEXITCODE -le 7) {");
            sb.AppendLine("            Write-Host 'UserData restore complete' -ForegroundColor Green");
            sb.AppendLine("        } else {");
            sb.AppendLine("            Write-Warning ('UserData restore completed with warnings (robocopy exit code: ' + $LASTEXITCODE + ')')");
            sb.AppendLine("        }");
            sb.AppendLine("    }");
            sb.AppendLine("}");
            sb.AppendLine();
            sb.AppendLine("function Remove-Backup {");
            sb.AppendLine("    if (Test-Path $backupDir) {");
            sb.AppendLine("        Remove-Item -Path $backupDir -Recurse -Force -ErrorAction SilentlyContinue");
            sb.AppendLine("    }");
            sb.AppendLine("}");
            sb.AppendLine();
            sb.AppendLine("try {");
            sb.AppendLine("    $hasBackup = $false");
            sb.AppendLine("    Write-Host 'Waiting for Redball to close...'");
            sb.AppendLine("    while (Get-Process -Name $processName -ErrorAction SilentlyContinue) {");
            sb.AppendLine("        Start-Sleep -Seconds 1");
            sb.AppendLine("    }");
            sb.AppendLine();
            sb.AppendLine("    Write-Host 'Creating backup of UserData...'");
            sb.AppendLine("    $hasBackup = Backup-UserData");
            sb.AppendLine();
            sb.AppendLine("    Write-Host 'Cleaning up orphaned files from previous installs...'");
            sb.AppendLine("    $orphanedFiles = @(");
            sb.AppendLine("        'analytics.json', 'engine_toggle.json', 'ram_usage.json',");
            sb.AppendLine("        'Redball.state.json', 'templates.json', 'typething_launch.json'");
            sb.AppendLine("    )");
            sb.AppendLine("    $removedCount = 0;");
            sb.AppendLine("    foreach ($file in $orphanedFiles) {");
            sb.AppendLine("        $filePath = Join-Path $targetDir $file;");
            sb.AppendLine("        if (Test-Path $filePath) {");
            sb.AppendLine("            try {");
            sb.AppendLine("                Remove-Item $filePath -Force -ErrorAction Stop;");
            sb.AppendLine("                Write-Host ('  Removed orphaned file: ' + $file);");
            sb.AppendLine("                $removedCount++;");
            sb.AppendLine("            } catch {");
            sb.AppendLine("                Write-Warning ('Could not remove orphaned file: ' + $file);");
            sb.AppendLine("            }");
            sb.AppendLine("        }");
            sb.AppendLine("    }");
            sb.AppendLine("    ");
            sb.AppendLine("    # Clean up old DLLs from root folder (they should now be in dll/ subfolder)");
            sb.AppendLine("    $orphanedRootDlls = @(");
            sb.AppendLine("        'CommunityToolkit.Mvvm.dll', 'e_sqlite3.dll', 'Hardcodet.NotifyIcon.Wpf.dll',");
            sb.AppendLine("        'libSkiaSharp.dll', 'LottieSharp.dll', 'Microsoft.Data.Sqlite.dll',");
            sb.AppendLine("        'Microsoft.Extensions.Configuration.Abstractions.dll', 'Microsoft.Extensions.Configuration.Binder.dll',");
            sb.AppendLine("        'Microsoft.Extensions.Configuration.CommandLine.dll', 'Microsoft.Extensions.Configuration.dll',");
            sb.AppendLine("        'Microsoft.Extensions.Configuration.EnvironmentVariables.dll', 'Microsoft.Extensions.Configuration.FileExtensions.dll',");
            sb.AppendLine("        'Microsoft.Extensions.Configuration.Json.dll', 'Microsoft.Extensions.Configuration.UserSecrets.dll',");
            sb.AppendLine("        'Microsoft.Extensions.DependencyInjection.Abstractions.dll', 'Microsoft.Extensions.DependencyInjection.dll',");
            sb.AppendLine("        'Microsoft.Extensions.Diagnostics.Abstractions.dll', 'Microsoft.Extensions.Diagnostics.dll',");
            sb.AppendLine("        'Microsoft.Extensions.FileProviders.Abstractions.dll', 'Microsoft.Extensions.FileProviders.Physical.dll',");
            sb.AppendLine("        'Microsoft.Extensions.FileSystemGlobbing.dll', 'Microsoft.Extensions.Hosting.Abstractions.dll',");
            sb.AppendLine("        'Microsoft.Extensions.Hosting.dll', 'Microsoft.Extensions.Http.dll',");
            sb.AppendLine("        'Microsoft.Extensions.Logging.Abstractions.dll', 'Microsoft.Extensions.Logging.Configuration.dll',");
            sb.AppendLine("        'Microsoft.Extensions.Logging.Console.dll', 'Microsoft.Extensions.Logging.Debug.dll',");
            sb.AppendLine("        'Microsoft.Extensions.Logging.dll', 'Microsoft.Extensions.Logging.EventLog.dll',");
            sb.AppendLine("        'Microsoft.Extensions.Logging.EventSource.dll', 'Microsoft.Extensions.Options.ConfigurationExtensions.dll',");
            sb.AppendLine("        'Microsoft.Extensions.Options.dll', 'Microsoft.Extensions.Primitives.dll',");
            sb.AppendLine("        'Microsoft.Xaml.Behaviors.dll', 'Redball.Core.dll', 'Redball.Core.pdb',");
            sb.AppendLine("        'SkiaSharp.dll', 'SkiaSharp.SceneGraph.dll', 'SkiaSharp.Skottie.dll',");
            sb.AppendLine("        'SkiaSharp.Views.Desktop.Common.dll', 'SkiaSharp.Views.WPF.dll',");
            sb.AppendLine("        'SQLitePCLRaw.batteries_v2.dll', 'SQLitePCLRaw.core.dll', 'SQLitePCLRaw.provider.e_sqlite3.dll',");
            sb.AppendLine("        'System.Diagnostics.EventLog.dll', 'System.Management.dll',");
            sb.AppendLine("        'System.ServiceProcess.ServiceController.dll', 'System.Speech.dll',");
            sb.AppendLine("        'InputInterceptor.dll'");
            sb.AppendLine("    )");
            sb.AppendLine("    foreach ($file in $orphanedRootDlls) {");
            sb.AppendLine("        $filePath = Join-Path $targetDir $file;");
            sb.AppendLine("        if (Test-Path $filePath) {");
            sb.AppendLine("            try {");
            sb.AppendLine("                Remove-Item $filePath -Force -ErrorAction Stop;");
            sb.AppendLine("                Write-Host ('  Removed orphaned root DLL: ' + $file);");
            sb.AppendLine("                $removedCount++;");
            sb.AppendLine("            } catch {");
            sb.AppendLine("                Write-Warning ('Could not remove orphaned root DLL: ' + $file);");
            sb.AppendLine("            }");
            sb.AppendLine("        }");
            sb.AppendLine("    }");
            sb.AppendLine("    ");
            sb.AppendLine("    # Clean up old DLLs from dll folder that are no longer needed");
            sb.AppendLine("    $dllDir = Join-Path $targetDir 'dll'");
            sb.AppendLine("    if (Test-Path $dllDir) {");
            sb.AppendLine("        $stagingDllDir = Join-Path $sourceDir 'dll'");
            sb.AppendLine("        if (Test-Path $stagingDllDir) {");
            sb.AppendLine("            $currentDlls = Get-ChildItem -Path $stagingDllDir -Filter '*.dll' | Select-Object -ExpandProperty Name");
            sb.AppendLine("            $existingDlls = Get-ChildItem -Path $dllDir -Filter '*.dll' -ErrorAction SilentlyContinue");
            sb.AppendLine("            foreach ($dll in $existingDlls) {");
            sb.AppendLine("                if ($currentDlls -notcontains $dll.Name) {");
            sb.AppendLine("                    try {");
            sb.AppendLine("                        Remove-Item $dll.FullName -Force -ErrorAction Stop;");
            sb.AppendLine("                        Write-Host ('  Removed obsolete DLL: dll\\' + $dll.Name);");
            sb.AppendLine("                        $removedCount++;");
            sb.AppendLine("                    } catch {");
            sb.AppendLine("                        Write-Warning ('Could not remove obsolete DLL: ' + $dll.Name);");
            sb.AppendLine("                    }");
            sb.AppendLine("                }");
            sb.AppendLine("            }");
            sb.AppendLine("        }");
            sb.AppendLine("    }");
            sb.AppendLine("    if ($removedCount -gt 0) {");
            sb.AppendLine("        Write-Host ('Cleaned up ' + $removedCount + ' orphaned file(s).');");
            sb.AppendLine("    }");
            sb.AppendLine();
            sb.AppendLine("    Write-Host 'Installing update...'");
            sb.AppendLine("    Start-Sleep -Seconds 2");
            sb.AppendLine();
            sb.AppendLine("    $sourceRoot = [System.IO.Path]::GetFullPath($sourceDir)");
            sb.AppendLine("    if (-not $sourceRoot.EndsWith([System.IO.Path]::DirectorySeparatorChar.ToString())) {");
            sb.AppendLine("        $sourceRoot += [System.IO.Path]::DirectorySeparatorChar");
            sb.AppendLine("    }");
            sb.AppendLine();
            sb.AppendLine("    Get-ChildItem -Path $sourceDir -File -Recurse | ForEach-Object {");
            sb.AppendLine("        $fullPath = [System.IO.Path]::GetFullPath($_.FullName)");
            sb.AppendLine("        if (-not $fullPath.StartsWith($sourceRoot, [System.StringComparison]::OrdinalIgnoreCase)) {");
            sb.AppendLine("            return");
            sb.AppendLine("        }");
            sb.AppendLine();
            sb.AppendLine("        $relativePath = $fullPath.Substring($sourceRoot.Length)");
            sb.AppendLine("        if ([string]::IsNullOrWhiteSpace($relativePath) -or $relativePath.StartsWith('..')) {");
            sb.AppendLine("            return");
            sb.AppendLine("        }");
            sb.AppendLine();
            sb.AppendLine("        if ($relativePath.StartsWith('UserData\\', [System.StringComparison]::OrdinalIgnoreCase) -or");
            sb.AppendLine("            $relativePath.StartsWith('UserData/', [System.StringComparison]::OrdinalIgnoreCase)) {");
            sb.AppendLine("            Write-Host ('Skipping protected path: ' + $relativePath)");
            sb.AppendLine("            return");
            sb.AppendLine("        }");
            sb.AppendLine();
            sb.AppendLine("        $targetPath = Join-Path $targetDir $relativePath");
            sb.AppendLine("        $parentDir = Split-Path -Parent $targetPath");
            sb.AppendLine();
            sb.AppendLine("        if (-not (Test-Path $parentDir)) {");
            sb.AppendLine("            New-Item -ItemType Directory -Path $parentDir -Force | Out-Null");
            sb.AppendLine("        }");
            sb.AppendLine();
            sb.AppendLine("        $copied = $false");
            sb.AppendLine("        for ($attempt = 1; $attempt -le 5; $attempt++) {");
            sb.AppendLine("            try {");
            sb.AppendLine("                Copy-Item -Path $_.FullName -Destination $targetPath -Force -ErrorAction Stop");
            sb.AppendLine("                $copied = $true");
            sb.AppendLine("                break");
            sb.AppendLine("            }");
            sb.AppendLine("            catch {");
            sb.AppendLine("                if ($attempt -lt 5) {");
            sb.AppendLine("                    Start-Sleep -Milliseconds (300 * $attempt)");
            sb.AppendLine("                }");
            sb.AppendLine("                else {");
            sb.AppendLine("                    throw");
            sb.AppendLine("                }");
            sb.AppendLine("            }");
            sb.AppendLine("        }");
            sb.AppendLine();
            sb.AppendLine("        if ($copied) {");
            sb.AppendLine("            Write-Host ('Updated: ' + $relativePath)");
            sb.AppendLine("        }");
            sb.AppendLine("    }");
            sb.AppendLine();
            sb.AppendLine("    Write-Host 'Update complete!'");
            sb.AppendLine("    Write-Host 'Starting Redball...'");
            sb.AppendLine();
            sb.AppendLine("    $appExe = Join-Path $targetDir 'Redball.UI.WPF.exe'");
            sb.AppendLine("    if (-not (Test-Path $appExe)) {");
            sb.AppendLine("        try {");
            sb.AppendLine("            $regInstallPath = (Get-ItemProperty -Path 'HKCU:\\Software\\Redball' -Name 'InstallPath' -ErrorAction Stop).InstallPath");
            sb.AppendLine("            if (-not [string]::IsNullOrWhiteSpace($regInstallPath)) {");
            sb.AppendLine("                $appExe = $regInstallPath");
            sb.AppendLine("            }");
            sb.AppendLine("        }");
            sb.AppendLine("        catch {");
            sb.AppendLine("        }");
            sb.AppendLine("    }");
            sb.AppendLine();
            sb.AppendLine("    if (-not (Test-Path $appExe)) {");
            sb.AppendLine("        $fallbackRedball = Join-Path $env:LOCALAPPDATA 'Redball\\Redball.UI.WPF.exe'");
            sb.AppendLine("        $fallbackRedballApp = Join-Path $env:LOCALAPPDATA 'RedballApp\\Redball.UI.WPF.exe'");
            sb.AppendLine("        if (Test-Path $fallbackRedball) {");
            sb.AppendLine("            $appExe = $fallbackRedball");
            sb.AppendLine("        }");
            sb.AppendLine("        elseif (Test-Path $fallbackRedballApp) {");
            sb.AppendLine("            $appExe = $fallbackRedballApp");
            sb.AppendLine("        }");
            sb.AppendLine("    }");
            sb.AppendLine();
            sb.AppendLine("    if (-not (Test-Path $appExe)) {");
            sb.AppendLine("        throw \"Updated application executable not found. Expected at: $appExe\"");
            sb.AppendLine("    }");
            sb.AppendLine();
            sb.AppendLine("    Start-Process -FilePath $appExe");
            sb.AppendLine();
            sb.AppendLine("    Write-Host 'Cleaning up backup...'");
            sb.AppendLine("    Remove-Backup");
            sb.AppendLine();
            sb.AppendLine("    Remove-Item -Path (Split-Path -Parent $sourceDir) -Recurse -Force -ErrorAction SilentlyContinue");
            sb.AppendLine("    Remove-Item -Path $PSCommandPath -Force -ErrorAction SilentlyContinue");
            sb.AppendLine("    exit 0");
            sb.AppendLine("}");
            sb.AppendLine("catch {");
            sb.AppendLine("    $errorText = ($_ | Out-String)");
            sb.AppendLine("    $timestamp = Get-Date -Format 'yyyy-MM-dd HH:mm:ss'");
            sb.AppendLine("    \"[$timestamp] Update failed\" | Out-File -FilePath $logFile -Encoding UTF8 -Append");
            sb.AppendLine("    $errorText | Out-File -FilePath $logFile -Encoding UTF8 -Append");
            sb.AppendLine();
            sb.AppendLine("    if ($hasBackup) {");
            sb.AppendLine("        Write-Host \"`nAttempting to restore UserData from backup...\" -ForegroundColor Yellow");
            sb.AppendLine("        try {");
            sb.AppendLine("            Restore-UserData");
            sb.AppendLine("            Write-Host 'UserData restored successfully.' -ForegroundColor Green");
            sb.AppendLine("        }");
            sb.AppendLine("        catch {");
            sb.AppendLine("            $restoreError = $_ | Out-String");
            sb.AppendLine("            \"[$timestamp] UserData restore failed: $restoreError\" | Out-File -FilePath $logFile -Encoding UTF8 -Append");
            sb.AppendLine("            Write-Warning ('Failed to restore UserData: ' + $restoreError)");
            sb.AppendLine("        }");
            sb.AppendLine("    }");
            sb.AppendLine();
            sb.AppendLine("    Write-Host ('Update failed. Details saved to: ' + $logFile) -ForegroundColor Red");
            sb.AppendLine("    Write-Host $errorText -ForegroundColor Red");
            sb.AppendLine("    Read-Host 'Press Enter to close updater'");
            sb.AppendLine("    exit 1");
            sb.AppendLine("}");

            File.WriteAllText(scriptPath, sb.ToString());
            return scriptPath;
        }
        catch (Exception ex)
        {
            Logger.Error("UpdateService", "Failed to create update script", ex);
            return null;
        }
    }

    private string? CreateInstallerLaunchScript(string installerPath, string fileName)
    {
        try
        {
            var scriptDir = Path.Combine(Path.GetTempPath(), "RedballUpdate");
            var scriptPath = Path.Combine(scriptDir, "install-update.ps1");
            
            // Ensure directory exists before writing script
            Directory.CreateDirectory(scriptDir);
            
            var packageDir = Path.GetDirectoryName(installerPath) ?? scriptDir;
            var installerCommand = fileName.EndsWith(".msi", StringComparison.OrdinalIgnoreCase)
                ? "$process = Start-Process -FilePath 'msiexec.exe' -ArgumentList '/i \"" + installerPath.Replace("'", "''") + "\" /passive /norestart' -Wait -PassThru"
                : "$process = Start-Process -FilePath '" + installerPath.Replace("'", "''") + "' -ArgumentList '/quiet /norestart' -Wait -PassThru";

            var script = $@"
$ErrorActionPreference = 'Stop'
$processName = 'Redball.UI.WPF'

while (Get-Process -Name `$processName -ErrorAction SilentlyContinue) {{
    Start-Sleep -Seconds 1
}}

{installerCommand}

if ($process.ExitCode -ne 0) {{
    throw 'Installer exited with code ' + $process.ExitCode
}}

$appPath = $null
try {{
    $appPath = (Get-ItemProperty -Path 'HKCU:\Software\Redball' -Name 'InstallPath' -ErrorAction Stop).InstallPath
}}
catch {{
}}

if ([string]::IsNullOrWhiteSpace($appPath)) {{
    $appPath = Join-Path $env:LOCALAPPDATA 'Redball\Redball.UI.WPF.exe'
}}

if (Test-Path $appPath) {{
    Start-Process -FilePath $appPath
}}

Remove-Item -Path '{packageDir.Replace("'", "''")}' -Recurse -Force -ErrorAction SilentlyContinue
Remove-Item -Path $PSCommandPath -Force -ErrorAction SilentlyContinue
exit 0
";

            File.WriteAllText(scriptPath, script);
            return scriptPath;
        }
        catch (Exception ex)
        {
            Logger.Error("UpdateService", "Failed to create installer launch script", ex);
            return null;
        }
    }
}

/// <summary>
/// Information about an available update.
/// </summary>
public class UpdateInfo
{
    public Version CurrentVersion { get; set; } = new(0, 0, 0);
    public Version LatestVersion { get; set; } = new(0, 0, 0);
    public string DownloadUrl { get; set; } = "";
    public string FileName { get; set; } = "";
    public string ReleaseNotes { get; set; } = "";
    public DateTime ReleaseDate { get; set; }

    public List<FileUpdateInfo>? FilesToUpdate { get; set; }
    public string VersionDisplay => $"v{LatestVersion.Major}.{LatestVersion.Minor}.{LatestVersion.Build}";
    
    /// <summary>
    /// Expected SHA256 hash from signed manifest for integrity verification (sec-3).
    /// </summary>
    public string? ManifestHash { get; set; }
    
    /// <summary>
    /// Trust validation result after download (sec-3).
    /// </summary>
    public TrustValidationResult? TrustValidation { get; set; }
}

public class UpdateManifest
{
    [System.Text.Json.Serialization.JsonPropertyName("version")]
    public string Version { get; set; } = "";
    [System.Text.Json.Serialization.JsonPropertyName("files")]
    public List<FileUpdateInfo> Files { get; set; } = new();
}

public class FileUpdateInfo
{
    [System.Text.Json.Serialization.JsonPropertyName("name")]
    public string Name { get; set; } = "";
    [System.Text.Json.Serialization.JsonPropertyName("hash")]
    public string Hash { get; set; } = "";
    [System.Text.Json.Serialization.JsonPropertyName("size")]
    public long Size { get; set; }
    [System.Text.Json.Serialization.JsonPropertyName("signature")]
    public string Signature { get; set; } = "";
    public string DownloadUrl { get; set; } = "";
    public string? PatchUrl { get; set; }  // Binary delta patch URL (if available)
    public long PatchSize { get; set; }     // Size of patch file
}

/// <summary>
/// Represents a single version's changelog entry for display in the update dialog.
/// </summary>
public class VersionChangelog
{
    public Version Version { get; set; } = new(0, 0, 0);
    public string TagName { get; set; } = "";
    public string ReleaseNotes { get; set; } = "";
    public DateTime ReleaseDate { get; set; }
    public bool IsPreRelease { get; set; }

    public string VersionDisplay => $"v{Version.Major}.{Version.Minor}.{Version.Build}";
    public string DateDisplay => ReleaseDate.ToString("d MMM yyyy");
}

/// <summary>
/// GitHub API release response model.
/// </summary>
public class GitHubRelease
{
    [System.Text.Json.Serialization.JsonPropertyName("tag_name")]
    public string TagName { get; set; } = "";
    public string Body { get; set; } = "";
    [System.Text.Json.Serialization.JsonPropertyName("published_at")]
    public DateTime PublishedAt { get; set; }
    public List<GitHubAsset> Assets { get; set; } = new();
    [System.Text.Json.Serialization.JsonPropertyName("draft")]
    public bool IsDraft { get; set; }
    [System.Text.Json.Serialization.JsonPropertyName("prerelease")]
    public bool IsPreRelease { get; set; }
}

/// <summary>
/// GitHub API asset response model.
/// </summary>
public class GitHubAsset
{
    public string Name { get; set; } = "";
    [System.Text.Json.Serialization.JsonPropertyName("browser_download_url")]
    public string DownloadUrl { get; set; } = "";
    public long Size { get; set; }
}
