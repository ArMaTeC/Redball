using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;

namespace Redball.UI.Services;

/// <summary>
/// Service for checking, downloading, and installing updates from GitHub releases.
/// </summary>
public class UpdateService
{
    private static readonly HttpClient _httpClient = new();
    private readonly string _repoOwner;
    private readonly string _repoName;
    private readonly string _updateChannel;
    private readonly bool _verifySignature;

    public UpdateService(string repoOwner, string repoName, string updateChannel = "stable", bool verifySignature = false)
    {
        _repoOwner = repoOwner;
        _repoName = repoName;
        _updateChannel = updateChannel;
        _verifySignature = verifySignature;
    }

    /// <summary>
    /// Checks for updates by comparing current version with latest GitHub release.
    /// </summary>
    /// <returns>Update info if an update is available, null if up to date or error.</returns>
    public async Task<UpdateInfo?> CheckForUpdateAsync()
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
            
            // Get all releases and find the highest version (not just "latest" by date)
            var allReleases = await GetAllReleasesAsync();
            var latestRelease = FindHighestVersionRelease(allReleases);
            
            if (latestRelease == null)
            {
                Logger.Warning("UpdateService", "Could not find any valid release");
                return null;
            }

            // Parse version from tag (remove 'v' prefix if present)
            var tagName = latestRelease.TagName.TrimStart('v', 'V');
            Logger.Debug("UpdateService", $"Latest release tag: {latestRelease.TagName}");
            
            if (!Version.TryParse(tagName, out var latestVersion))
            {
                Logger.Error("UpdateService", $"Failed to parse version from tag: {tagName}");
                return null;
            }
            
            // Normalize versions for comparison (ignore revision number)
            var currentNormalized = new Version(currentVersion.Major, currentVersion.Minor, currentVersion.Build);
            var latestNormalized = new Version(latestVersion.Major, latestVersion.Minor, latestVersion.Build);
            
            Logger.Info("UpdateService", $"Comparing versions: current={currentNormalized}, latest={latestNormalized}");

            // Compare versions
            if (latestNormalized <= currentNormalized)
            {
                Logger.Info("UpdateService", $"Up to date (current: {currentNormalized}, latest: {latestNormalized})");
                return null;
            }

            // Find appropriate asset (prefer standalone/portable versions)
            var asset = FindBestAsset(latestRelease);
            if (asset == null)
                return null;

            return new UpdateInfo
            {
                CurrentVersion = currentNormalized,
                LatestVersion = latestNormalized,
                DownloadUrl = asset.DownloadUrl,
                FileName = asset.Name,
                ReleaseNotes = latestRelease.Body,
                ReleaseDate = latestRelease.PublishedAt
            };
        }
        catch (Exception ex)
        {
            Log($"Update check failed: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Downloads and installs the update.
    /// </summary>
    /// <param name="updateInfo">Update information from CheckForUpdateAsync.</param>
    /// <param name="progress">Callback for download progress (0-100).</param>
    /// <returns>True if update was downloaded and prepared successfully.</returns>
    public async Task<bool> DownloadAndInstallAsync(UpdateInfo updateInfo, IProgress<int>? progress = null)
    {
        try
        {
            var tempDir = Path.Combine(Path.GetTempPath(), "RedballUpdate");
            Directory.CreateDirectory(tempDir);

            // Download the update package
            var downloadPath = Path.Combine(tempDir, updateInfo.FileName);
            if (!await DownloadFileAsync(updateInfo.DownloadUrl, downloadPath, progress))
                return false;

            // Verify signature if enabled
            if (_verifySignature)
            {
                var hashFileName = updateInfo.FileName + ".sha256";
                var expectedHash = await DownloadHashFileAsync(updateInfo.DownloadUrl + ".sha256", hashFileName);
                
                if (!string.IsNullOrEmpty(expectedHash))
                {
                    if (!VerifyFileHash(downloadPath, expectedHash))
                    {
                        Log("Signature verification failed - file hash mismatch");
                        File.Delete(downloadPath);
                        return false;
                    }
                    Log("Signature verification passed");
                }
                else
                {
                    Log("No hash file available for verification, proceeding without verification");
                }
            }

            // Extract if it's a zip file
            var extractDir = Path.Combine(tempDir, "extracted");
            if (updateInfo.FileName.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            {
                if (!ExtractZip(downloadPath, extractDir))
                    return false;
            }
            else
            {
                // Single file update (e.g., Redball.ps1)
                Directory.CreateDirectory(extractDir);
                File.Copy(downloadPath, Path.Combine(extractDir, updateInfo.FileName), true);
            }

            // Create update script that will run after this process exits
            var updateScriptPath = CreateUpdateScript(extractDir);
            if (updateScriptPath == null)
                return false;

            // Launch the update script
            Process.Start(new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = $"-ExecutionPolicy Bypass -File \"{updateScriptPath}\"",
                UseShellExecute = true,
                CreateNoWindow = false
            });

            return true;
        }
        catch (Exception ex)
        {
            Log($"Update installation failed: {ex.Message}");
            return false;
        }
    }

    private async Task<List<GitHubRelease>> GetAllReleasesAsync()
    {
        var url = $"https://api.github.com/repos/{_repoOwner}/{_repoName}/releases";
        Logger.Debug("UpdateService", $"Fetching all releases from: {url}");
        
        _httpClient.DefaultRequestHeaders.Add("User-Agent", "Redball-Updater");
        
        var response = await _httpClient.GetStringAsync(url);
        Logger.Debug("UpdateService", $"API response length: {response.Length} chars");
        
        var releases = JsonSerializer.Deserialize<List<GitHubRelease>>(response, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        });
        
        return releases ?? new List<GitHubRelease>();
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
        // Priority: standalone/portable > full installer > any zip
        var priorities = new[]
        {
            "standalone",
            "portable",
            "win-x64",
            "windows",
            ".zip"
        };

        foreach (var priority in priorities)
        {
            var asset = release.Assets.Find(a => 
                a.Name.Contains(priority, StringComparison.OrdinalIgnoreCase) &&
                !a.Name.Contains("debug", StringComparison.OrdinalIgnoreCase));
            
            if (asset != null)
                return asset;
        }

        // Fallback to first zip file
        return release.Assets.Find(a => a.Name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase));
    }

    private async Task<bool> DownloadFileAsync(string url, string destinationPath, IProgress<int>? progress)
    {
        using var response = await _httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
        response.EnsureSuccessStatusCode();

        var totalBytes = response.Content.Headers.ContentLength ?? -1L;
        var downloadedBytes = 0L;

        await using var contentStream = await response.Content.ReadAsStreamAsync();
        await using var fileStream = new FileStream(destinationPath, FileMode.Create, FileAccess.Write, FileShare.None);

        var buffer = new byte[8192];
        int read;
        while ((read = await contentStream.ReadAsync(buffer)) > 0)
        {
            await fileStream.WriteAsync(buffer.AsMemory(0, read));
            downloadedBytes += read;

            if (totalBytes > 0 && progress != null)
            {
                var percent = (int)((downloadedBytes * 100) / totalBytes);
                progress.Report(percent);
            }
        }

        progress?.Report(100);
        return true;
    }

    private bool ExtractZip(string zipPath, string extractPath)
    {
        try
        {
            if (Directory.Exists(extractPath))
                Directory.Delete(extractPath, true);
            
            Directory.CreateDirectory(extractPath);
            ZipFile.ExtractToDirectory(zipPath, extractPath);
            return true;
        }
        catch (Exception ex)
        {
            Log($"Failed to extract zip: {ex.Message}");
            return false;
        }
    }

    private async Task<string?> DownloadHashFileAsync(string hashUrl, string hashFileName)
    {
        try
        {
            var response = await _httpClient.GetAsync(hashUrl);
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
        catch
        {
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
            Log($"Hash verification error: {ex.Message}");
            return false;
        }
    }

    private string? CreateUpdateScript(string sourceDir)
    {
        try
        {
            var appDir = AppContext.BaseDirectory;
            var scriptPath = Path.Combine(Path.GetTempPath(), "RedballUpdate", "update.ps1");
            
            // Create PowerShell script to replace files and restart
            var script = $@"
# Redball Auto-Update Script
$ErrorActionPreference = 'Stop'
$sourceDir = '{sourceDir.Replace("'", "''")}'
$targetDir = '{appDir.Replace("'", "''")}'
$processName = 'Redball.UI.WPF'

Write-Host 'Waiting for Redball to close...'
# Wait for process to exit
while (Get-Process -Name $processName -ErrorAction SilentlyContinue) {{
    Start-Sleep -Seconds 1
}}

Write-Host 'Installing update...'
Start-Sleep -Seconds 2

# Copy files
Get-ChildItem -Path $sourceDir -Recurse | ForEach-Object {{
    $targetPath = $_.FullName.Replace($sourceDir, $targetDir)
    $targetDir = Split-Path -Parent $targetPath
    
    if (-not (Test-Path $targetDir)) {{
        New-Item -ItemType Directory -Path $targetDir -Force | Out-Null
    }}
    
    if (-not $_.PSIsContainer) {{
        Copy-Item -Path $_.FullName -Destination $targetPath -Force
        Write-Host ""Updated: $($_.Name)""
    }}
}}

Write-Host 'Update complete!'
Write-Host 'Starting Redball...'

# Restart application
Start-Process -FilePath (Join-Path $targetDir 'Redball.UI.WPF.exe')

# Cleanup
Remove-Item -Path (Split-Path -Parent $sourceDir) -Recurse -Force -ErrorAction SilentlyContinue

# Self-delete
Remove-Item -Path $PSCommandPath -Force -ErrorAction SilentlyContinue
";

            File.WriteAllText(scriptPath, script);
            return scriptPath;
        }
        catch (Exception ex)
        {
            Log($"Failed to create update script: {ex.Message}");
            return null;
        }
    }

    private void Log(string message)
    {
        var logPath = Path.Combine(AppContext.BaseDirectory, "Redball.UI.log");
        var line = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [Update] {message}{Environment.NewLine}";
        File.AppendAllText(logPath, line);
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

    public string VersionDisplay => $"v{LatestVersion.Major}.{LatestVersion.Minor}.{LatestVersion.Build}";
}

/// <summary>
/// GitHub API release response model.
/// </summary>
public class GitHubRelease
{
    public string TagName { get; set; } = "";
    public string Body { get; set; } = "";
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
    public string DownloadUrl { get; set; } = "";
    public long Size { get; set; }
}
