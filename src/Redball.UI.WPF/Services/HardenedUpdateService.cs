using System;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Diagnostics;
using Redball.UI.Services;

namespace Redball.UI.Services;

/// <summary>
/// Hardened update service wrapper that adds reliability features:
/// - Automatic retry with exponential backoff
/// - Pre-flight connectivity checks
/// - Graceful handling of first-run scenarios
/// - File locking resilience
/// - Comprehensive diagnostic logging
/// </summary>
public class HardenedUpdateService
{
    private readonly UpdateService _innerService;
    private readonly UpdateDiagnostics _diagnostics;
    private static readonly TimeSpan[] RetryDelays = new[]
    {
        TimeSpan.FromSeconds(1),
        TimeSpan.FromSeconds(2),
        TimeSpan.FromSeconds(5),
        TimeSpan.FromSeconds(10)
    };

    public HardenedUpdateService(string repoOwner, string repoName, string updateChannel = "stable", 
        bool verifySignature = false, string? updateServerUrl = null)
    {
        _innerService = new UpdateService(repoOwner, repoName, updateChannel, verifySignature, updateServerUrl);
        _diagnostics = new UpdateDiagnostics();
    }

    /// <summary>
    /// Performs pre-flight checks before attempting update.
    /// Returns true if update should proceed, false otherwise.
    /// </summary>
    public async Task<PreFlightResult> PerformPreFlightChecksAsync(CancellationToken cancellationToken = default)
    {
        var result = new PreFlightResult();
        var sw = Stopwatch.StartNew();

        try
        {
            Logger.Info("HardenedUpdate", "Starting pre-flight checks...");

            // Check 1: Network connectivity
            result.NetworkAvailable = await CheckNetworkConnectivityAsync(cancellationToken);
            if (!result.NetworkAvailable)
            {
                result.FailureReason = "Network connectivity check failed";
                Logger.Warning("HardenedUpdate", "Pre-flight failed: No network connectivity");
                return result;
            }

            // Check 2: DNS resolution for GitHub API
            result.DnsResolutionWorking = await CheckDnsResolutionAsync("api.github.com", cancellationToken);
            if (!result.DnsResolutionWorking)
            {
                result.FailureReason = "DNS resolution failed for api.github.com";
                Logger.Warning("HardenedUpdate", "Pre-flight failed: DNS resolution not working");
                return result;
            }

            // Check 3: Disk space (need at least 200MB free)
            result.SufficientDiskSpace = CheckDiskSpace(200 * 1024 * 1024);
            if (!result.SufficientDiskSpace)
            {
                result.FailureReason = "Insufficient disk space (need 200MB)";
                Logger.Warning("HardenedUpdate", "Pre-flight failed: Insufficient disk space");
                return result;
            }

            // Check 4: Temp directory writable
            result.TempDirectoryWritable = CheckTempDirectoryWritable();
            if (!result.TempDirectoryWritable)
            {
                result.FailureReason = "Temp directory not writable";
                Logger.Warning("HardenedUpdate", "Pre-flight failed: Cannot write to temp directory");
                return result;
            }

            // Check 5: Update cache directory initialization
            result.CacheDirectoryReady = await InitializeCacheDirectoryAsync(cancellationToken);
            if (!result.CacheDirectoryReady)
            {
                result.FailureReason = "Cache directory initialization failed";
                Logger.Warning("HardenedUpdate", "Pre-flight failed: Cache directory issue");
                return result;
            }

            // Check 6: HTTP client connectivity (lightweight test)
            result.HttpClientWorking = await CheckHttpClientAsync(cancellationToken);
            if (!result.HttpClientWorking)
            {
                result.FailureReason = "HTTP client initialization failed";
                Logger.Warning("HardenedUpdate", "Pre-flight failed: HTTP client not working");
                return result;
            }

            result.AllChecksPassed = true;
            Logger.Info("HardenedUpdate", $"All pre-flight checks passed in {sw.ElapsedMilliseconds}ms");
            return result;
        }
        catch (Exception ex)
        {
            result.FailureReason = $"Pre-flight exception: {ex.Message}";
            Logger.Error("HardenedUpdate", "Pre-flight checks failed with exception", ex);
            return result;
        }
    }

    /// <summary>
    /// Checks for updates with automatic retry and comprehensive logging.
    /// </summary>
    public async Task<UpdateInfo?> CheckForUpdateWithRetryAsync(CancellationToken cancellationToken = default)
    {
        _diagnostics.AttemptCount = 0;
        _diagnostics.StartTime = DateTime.UtcNow;

        // Perform pre-flight checks first
        var preFlight = await PerformPreFlightChecksAsync(cancellationToken);
        if (!preFlight.AllChecksPassed)
        {
            _diagnostics.LastError = $"Pre-flight failed: {preFlight.FailureReason}";
            Logger.Warning("HardenedUpdate", $"Skipping update check - pre-flight failed: {preFlight.FailureReason}");
            return null;
        }

        // Attempt update check with retries
        for (int attempt = 0; attempt <= RetryDelays.Length; attempt++)
        {
            _diagnostics.AttemptCount++;
            var attemptSw = Stopwatch.StartNew();

            try
            {
                Logger.Info("HardenedUpdate", $"Update check attempt {attempt + 1}/{RetryDelays.Length + 1}...");
                
                var result = await _innerService.CheckForUpdateAsync(cancellationToken);
                
                attemptSw.Stop();
                _diagnostics.LastAttemptDuration = attemptSw.Elapsed;
                _diagnostics.Success = true;

                if (result != null)
                {
                    Logger.Info("HardenedUpdate", $"Update available: {result.CurrentVersion} -> {result.LatestVersion} " +
                        $"(attempt {attempt + 1}, took {attemptSw.ElapsedMilliseconds}ms)");
                }
                else
                {
                    Logger.Info("HardenedUpdate", $"No update available (attempt {attempt + 1}, took {attemptSw.ElapsedMilliseconds}ms)");
                }

                return result;
            }
            catch (HttpRequestException ex) when (ex.Message.Contains("rate limit", StringComparison.OrdinalIgnoreCase))
            {
                // Don't retry on rate limit - respect the server's decision
                Logger.Warning("HardenedUpdate", "GitHub API rate limit hit - not retrying");
                _diagnostics.LastError = ex.Message;
                _diagnostics.RateLimitHit = true;
                return null;
            }
            catch (HttpRequestException ex) when (attempt < RetryDelays.Length)
            {
                // Transient network error - retry
                var delay = RetryDelays[attempt];
                _diagnostics.LastError = ex.Message;
                Logger.Warning("HardenedUpdate", $"Attempt {attempt + 1} failed: {ex.Message}. Retrying in {delay.TotalSeconds}s...");
                await Task.Delay(delay, cancellationToken);
            }
            catch (TaskCanceledException ex) when (cancellationToken.IsCancellationRequested)
            {
                Logger.Info("HardenedUpdate", "Update check cancelled by user");
                _diagnostics.LastError = "Cancelled";
                throw;
            }
            catch (Exception ex)
            {
                _diagnostics.LastError = ex.Message;
                if (attempt < RetryDelays.Length && IsRetryableException(ex))
                {
                    var delay = RetryDelays[attempt];
                    Logger.Warning("HardenedUpdate", $"Attempt {attempt + 1} failed: {ex.Message}. Retrying in {delay.TotalSeconds}s...");
                    await Task.Delay(delay, cancellationToken);
                }
                else
                {
                    Logger.Error("HardenedUpdate", $"Update check failed after {attempt + 1} attempts", ex);
                    return null;
                }
            }
        }

        Logger.Error("HardenedUpdate", "Update check exhausted all retry attempts");
        return null;
    }

    /// <summary>
    /// Downloads and installs update with retry logic and file locking resilience.
    /// </summary>
    public async Task<bool> DownloadAndInstallWithRetryAsync(UpdateInfo updateInfo, 
        IProgress<UpdateDownloadProgress>? progress = null, 
        CancellationToken cancellationToken = default)
    {
        _diagnostics.DownloadAttemptCount = 0;

        for (int attempt = 0; attempt <= RetryDelays.Length; attempt++)
        {
            _diagnostics.DownloadAttemptCount++;
            var attemptSw = Stopwatch.StartNew();

            try
            {
                Logger.Info("HardenedUpdate", $"Download attempt {attempt + 1}/{RetryDelays.Length + 1}...");

                // Pre-validate temp directory exists and is clean
                await PrepareTempDirectoryAsync(cancellationToken);

                var result = await _innerService.DownloadAndInstallAsync(updateInfo, progress, cancellationToken);
                
                attemptSw.Stop();
                
                if (result)
                {
                    Logger.Info("HardenedUpdate", $"Download and install succeeded on attempt {attempt + 1} " +
                        $"(took {attemptSw.ElapsedMilliseconds}ms)");
                    _diagnostics.DownloadSuccess = true;
                    return true;
                }
                else
                {
                    Logger.Warning("HardenedUpdate", $"Download returned false on attempt {attempt + 1}");
                }
            }
            catch (IOException ex) when (IsFileLockException(ex) && attempt < RetryDelays.Length)
            {
                // File locking issue (likely AV scanning) - retry with longer delay
                var delay = RetryDelays[attempt] * 2; // Double delay for file locking issues
                _diagnostics.LastDownloadError = ex.Message;
                Logger.Warning("HardenedUpdate", $"File locked on attempt {attempt + 1} (AV scan?). " +
                    $"Waiting {delay.TotalSeconds}s before retry...");
                await Task.Delay(delay, cancellationToken);
            }
            catch (Exception ex) when (attempt < RetryDelays.Length && IsRetryableException(ex))
            {
                var delay = RetryDelays[attempt];
                _diagnostics.LastDownloadError = ex.Message;
                Logger.Warning("HardenedUpdate", $"Download attempt {attempt + 1} failed: {ex.Message}. " +
                    $"Retrying in {delay.TotalSeconds}s...");
                await Task.Delay(delay, cancellationToken);
            }
            catch (Exception ex)
            {
                _diagnostics.LastDownloadError = ex.Message;
                Logger.Error("HardenedUpdate", $"Download failed after {attempt + 1} attempts", ex);
                return false;
            }
        }

        Logger.Error("HardenedUpdate", "Download exhausted all retry attempts");
        return false;
    }

    /// <summary>
    /// Gets diagnostic information about the last update attempt.
    /// </summary>
    public UpdateDiagnostics GetDiagnostics() => _diagnostics;

    // Private helper methods

    private async Task<bool> CheckNetworkConnectivityAsync(CancellationToken cancellationToken)
    {
        try
        {
            // Lightweight check - just verify we can reach the internet
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
            var response = await client.GetAsync("https://1.1.1.1", cancellationToken);
            return true; // If we get any response (even 404/403), network is up
        }
        catch
        {
            return false;
        }
    }

    private async Task<bool> CheckDnsResolutionAsync(string hostname, CancellationToken cancellationToken)
    {
        try
        {
            var addresses = await System.Net.Dns.GetHostAddressesAsync(hostname, cancellationToken);
            return addresses.Length > 0;
        }
        catch
        {
            return false;
        }
    }

    private bool CheckDiskSpace(long requiredBytes)
    {
        try
        {
            var tempPath = Path.GetTempPath();
            var driveInfo = new DriveInfo(Path.GetPathRoot(tempPath) ?? "C:\\");
            return driveInfo.AvailableFreeSpace > requiredBytes;
        }
        catch
        {
            return false;
        }
    }

    private bool CheckTempDirectoryWritable()
    {
        try
        {
            var tempFile = Path.Combine(Path.GetTempPath(), $"redball_write_test_{Guid.NewGuid()}.tmp");
            File.WriteAllText(tempFile, "test");
            File.Delete(tempFile);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private async Task<bool> InitializeCacheDirectoryAsync(CancellationToken cancellationToken)
    {
        try
        {
            var cacheDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Redball", "UpdateCache");

            if (!Directory.Exists(cacheDir))
            {
                Directory.CreateDirectory(cacheDir);
                // Small delay to ensure directory is fully created
                await Task.Delay(100, cancellationToken);
            }

            // Test write access
            var testFile = Path.Combine(cacheDir, $".init_test_{Guid.NewGuid()}");
            await File.WriteAllTextAsync(testFile, "test", cancellationToken);
            File.Delete(testFile);

            return true;
        }
        catch (Exception ex)
        {
            Logger.Error("HardenedUpdate", "Cache directory initialization failed", ex);
            return false;
        }
    }

    private async Task<bool> CheckHttpClientAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
            // Just check if we can create and dispose the client
            client.DefaultRequestHeaders.Add("User-Agent", "Redball-Update-Test/1.0");
            
            // Try a lightweight HEAD request
            var request = new HttpRequestMessage(HttpMethod.Head, "https://api.github.com");
            request.Headers.Add("Accept", "application/vnd.github.v3+json");
            
            var response = await client.SendAsync(request, cancellationToken);
            // 403 is expected without auth, but it means the connection works
            return response.StatusCode == System.Net.HttpStatusCode.Forbidden || 
                   response.StatusCode == System.Net.HttpStatusCode.OK;
        }
        catch
        {
            return false;
        }
    }

    private async Task PrepareTempDirectoryAsync(CancellationToken cancellationToken)
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "RedballUpdate");
        
        try
        {
            if (Directory.Exists(tempDir))
            {
                // Clean up old temp directory with retry
                for (int i = 0; i < 3; i++)
                {
                    try
                    {
                        Directory.Delete(tempDir, true);
                        break;
                    }
                    catch (IOException) when (i < 2)
                    {
                        await Task.Delay(500, cancellationToken);
                    }
                }
            }

            Directory.CreateDirectory(tempDir);
        }
        catch (Exception ex)
        {
            Logger.Warning("HardenedUpdate", $"Temp directory preparation issue: {ex.Message}");
            // Continue anyway - the inner service will handle it
        }
    }

    private bool IsRetryableException(Exception ex)
    {
        return ex is HttpRequestException ||
               ex is TimeoutException ||
               ex is TaskCanceledException ||
               (ex is IOException && !IsFatalIoException(ex));
    }

    private bool IsFileLockException(IOException ex)
    {
        var message = ex.Message.ToLowerInvariant();
        return message.Contains("being used by another process") ||
               message.Contains("access is denied") ||
               message.Contains("cannot access");
    }

    private bool IsFatalIoException(Exception ex)
    {
        var message = ex.Message.ToLowerInvariant();
        return message.Contains("not enough space") ||
               message.Contains("disk full") ||
               message.Contains("path too long");
    }

    // Delegate methods to inner service
    public async Task<List<VersionChangelog>> GetChangelogBetweenVersionsAsync(Version currentVersion, 
        Version latestVersion, CancellationToken cancellationToken = default)
    {
        return await _innerService.GetChangelogBetweenVersionsAsync(currentVersion, latestVersion, cancellationToken);
    }
}

/// <summary>
/// Result of pre-flight checks before update.
/// </summary>
public class PreFlightResult
{
    public bool AllChecksPassed { get; set; }
    public bool NetworkAvailable { get; set; }
    public bool DnsResolutionWorking { get; set; }
    public bool SufficientDiskSpace { get; set; }
    public bool TempDirectoryWritable { get; set; }
    public bool CacheDirectoryReady { get; set; }
    public bool HttpClientWorking { get; set; }
    public string? FailureReason { get; set; }
}

/// <summary>
/// Diagnostic information about update attempts.
/// </summary>
public class UpdateDiagnostics
{
    public DateTime StartTime { get; set; }
    public int AttemptCount { get; set; }
    public int DownloadAttemptCount { get; set; }
    public bool Success { get; set; }
    public bool DownloadSuccess { get; set; }
    public bool RateLimitHit { get; set; }
    public string? LastError { get; set; }
    public string? LastDownloadError { get; set; }
    public TimeSpan LastAttemptDuration { get; set; }

    public override string ToString()
    {
        var duration = DateTime.UtcNow - StartTime;
        return $"Update diagnostics: {AttemptCount} check attempts, {DownloadAttemptCount} download attempts, " +
               $"Success={Success}, Duration={duration.TotalSeconds:F1}s, " +
               $"LastError={LastError ?? "none"}";
    }
}
