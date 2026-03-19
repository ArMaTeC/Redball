using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;

namespace Redball.UI.Services;

/// <summary>
/// Service for checking, downloading, and installing updates from GitHub releases.
/// </summary>
public class UpdateService : IUpdateService
{
    private static readonly HttpClient _httpClient;
    private readonly string _repoOwner;
    private readonly string _repoName;
    private readonly string _updateChannel;
    private readonly bool _verifySignature;

    static UpdateService()
    {
        _httpClient = new HttpClient();
        _httpClient.DefaultRequestHeaders.UserAgent.Add(
            new ProductInfoHeaderValue("Redball-Updater", 
                Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "0.0.0"));
    }

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
    public async Task<UpdateInfo?> CheckForUpdateAsync(CancellationToken cancellationToken = default)
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
            var allReleases = await GetAllReleasesAsync(cancellationToken);
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

            Logger.Info("UpdateService", $"Selected release {latestRelease.TagName} with asset {asset.Name}");
            Logger.Debug("UpdateService", $"Selected asset download URL: {asset.DownloadUrl}");

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
        catch (OperationCanceledException)
        {
            Logger.Info("UpdateService", "Update check was cancelled");
            return null;
        }
        catch (Exception ex)
        {
            Logger.Error("UpdateService", "Update check failed", ex);
            return null;
        }
    }

    /// <summary>
    /// Downloads and installs the update.
    /// </summary>
    /// <param name="updateInfo">Update information from CheckForUpdateAsync.</param>
    /// <param name="progress">Callback for download progress (0-100).</param>
    /// <returns>True if update was downloaded and prepared successfully.</returns>
    public async Task<bool> DownloadAndInstallAsync(UpdateInfo updateInfo, IProgress<int>? progress = null, CancellationToken cancellationToken = default)
    {
        try
        {
            var tempDir = Path.Combine(Path.GetTempPath(), "RedballUpdate");
            Directory.CreateDirectory(tempDir);

            Logger.Info("UpdateService", $"Preparing to download update {updateInfo.LatestVersion} ({updateInfo.FileName})");
            Logger.Debug("UpdateService", $"Download URL requested: {updateInfo.DownloadUrl}");

            // Download the update package
            var downloadPath = Path.Combine(tempDir, updateInfo.FileName);
            if (!await DownloadFileAsync(updateInfo.DownloadUrl, downloadPath, progress, cancellationToken))
                return false;

            Logger.Debug("UpdateService", $"Downloaded file path: {downloadPath}");

            // Verify signature if enabled
            if (_verifySignature)
            {
                var hashFileName = updateInfo.FileName + ".sha256";
                var expectedHash = await DownloadHashFileAsync(updateInfo.DownloadUrl + ".sha256", hashFileName, cancellationToken);
                
                if (!string.IsNullOrEmpty(expectedHash))
                {
                    if (!VerifyFileHash(downloadPath, expectedHash))
                    {
                        Logger.Warning("UpdateService", "Signature verification failed - file hash mismatch");
                        File.Delete(downloadPath);
                        return false;
                    }
                    Logger.Info("UpdateService", "Signature verification passed");
                }
                else
                {
                    Logger.Warning("UpdateService", "No hash file available for verification, proceeding without verification");
                }
            }

            if (updateInfo.FileName.EndsWith(".msi", StringComparison.OrdinalIgnoreCase) ||
                updateInfo.FileName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            {
                var installerScriptPath = CreateInstallerLaunchScript(downloadPath, updateInfo.FileName);
                if (installerScriptPath == null)
                    return false;

                Process.Start(new ProcessStartInfo
                {
                    FileName = "powershell.exe",
                    Arguments = $"-ExecutionPolicy Bypass -File \"{installerScriptPath}\"",
                    UseShellExecute = true,
                    CreateNoWindow = false
                });

                return true;
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
        catch (OperationCanceledException)
        {
            Logger.Info("UpdateService", "Update download was cancelled");
            return false;
        }
        catch (Exception ex)
        {
            Logger.Error("UpdateService", "Update installation failed", ex);
            return false;
        }
    }

    private async Task<List<GitHubRelease>> GetAllReleasesAsync(CancellationToken cancellationToken = default)
    {
        var url = $"https://api.github.com/repos/{_repoOwner}/{_repoName}/releases";
        Logger.Debug("UpdateService", $"Fetching all releases from: {url}");
        
        var response = await _httpClient.GetStringAsync(url, cancellationToken);
        Logger.Debug("UpdateService", $"API response length: {response.Length} chars");
        
        var releases = JsonSerializer.Deserialize<List<GitHubRelease>>(response, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        });
        
        return releases ?? new List<GitHubRelease>();
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
            ".exe",
            ".msi"
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

    private async Task<bool> DownloadFileAsync(string url, string destinationPath, IProgress<int>? progress, CancellationToken cancellationToken = default)
    {
        Logger.Info("UpdateService", $"Downloading update from: {url}");
        using var response = await _httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();

        var totalBytes = response.Content.Headers.ContentLength ?? -1L;
        var downloadedBytes = 0L;

        await using var contentStream = await response.Content.ReadAsStreamAsync(cancellationToken);
        await using var fileStream = new FileStream(destinationPath, FileMode.Create, FileAccess.Write, FileShare.None);

        var buffer = new byte[8192];
        int read;
        while ((read = await contentStream.ReadAsync(buffer, cancellationToken)) > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await fileStream.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
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
            Logger.Error("UpdateService", "Hash verification error", ex);
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
    $parentDir = Split-Path -Parent $targetPath
    
    if (-not (Test-Path $parentDir)) {{
        New-Item -ItemType Directory -Path $parentDir -Force | Out-Null
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
            Logger.Error("UpdateService", "Failed to create update script", ex);
            return null;
        }
    }

    private string? CreateInstallerLaunchScript(string installerPath, string fileName)
    {
        try
        {
            var scriptPath = Path.Combine(Path.GetTempPath(), "RedballUpdate", "install-update.ps1");
            var packageDir = Path.GetDirectoryName(installerPath) ?? Path.Combine(Path.GetTempPath(), "RedballUpdate");
            var installerCommand = fileName.EndsWith(".msi", StringComparison.OrdinalIgnoreCase)
                ? "$process = Start-Process -FilePath 'msiexec.exe' -ArgumentList '/i \"" + installerPath.Replace("'", "''") + "\" /passive /norestart' -Wait -PassThru"
                : "$process = Start-Process -FilePath '" + installerPath.Replace("'", "''") + "' -ArgumentList '/quiet /norestart' -Wait -PassThru";

            var script = $@"
$ErrorActionPreference = 'Stop'
$processName = 'Redball.UI.WPF'

while (Get-Process -Name $processName -ErrorAction SilentlyContinue) {{
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

    public string VersionDisplay => $"v{LatestVersion.Major}.{LatestVersion.Minor}.{LatestVersion.Build}";
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
