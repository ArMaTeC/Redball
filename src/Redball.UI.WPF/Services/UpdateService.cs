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
/// Service for checking, downloading, and installing updates from GitHub releases.
/// </summary>
public class UpdateService : IUpdateService
{
    private static readonly HttpClient _httpClient;
    private readonly string _repoOwner;
    private readonly string _repoName;
    private readonly string _updateChannel;
    private readonly bool _verifySignature;

    // Circuit breaker: stop calling GitHub API after consecutive failures
    private static int _consecutiveFailures;
    private static DateTime _circuitOpenUntil = DateTime.MinValue;
    private const int CircuitBreakerThreshold = 3;
    private static readonly TimeSpan CircuitBreakerCooldown = TimeSpan.FromMinutes(30);

    static UpdateService()
    {
        _httpClient = new HttpClient();
        _httpClient.DefaultRequestHeaders.UserAgent.Add(
            new ProductInfoHeaderValue("Redball-Updater", 
                Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "0.0.0"));
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
        // Circuit breaker: skip if too many recent failures
        if (_consecutiveFailures >= CircuitBreakerThreshold && DateTime.UtcNow < _circuitOpenUntil)
        {
            Logger.Warning("UpdateService", $"Circuit breaker open — skipping update check until {_circuitOpenUntil:HH:mm:ss UTC}");
            return null;
        }

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
                    
                    foreach (var file in manifest.Files)
                    {
                        var normalizedName = NormalizeRelativeUpdatePath(file.Name);
                        var localPath = Path.Combine(appDir, normalizedName);
                        bool needsUpdate = true;
                        
                        if (File.Exists(localPath))
                        {
                            var localHash = (await CalculateHashAsync(localPath)).ToUpper();
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
                                filesToUpdate.Add(new FileUpdateInfo
                                {
                                    Name = normalizedName,
                                    DownloadUrl = asset.DownloadUrl,
                                    Hash = file.Hash,
                                    Size = file.Size
                                });
                            }
                        }
                    }
                    
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
        catch (Exception ex)
        {
            _consecutiveFailures++;
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
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
            Directory.CreateDirectory(tempDir);
            var stagingDir = Path.Combine(tempDir, "staging");
            Directory.CreateDirectory(stagingDir);

            if (updateInfo.FilesToUpdate != null && updateInfo.FilesToUpdate.Count > 0)
            {
                Logger.Info("UpdateService", $"Starting differential download of {updateInfo.FilesToUpdate.Count} files...");
                int completed = 0;
                foreach (var file in updateInfo.FilesToUpdate)
                {
                    var normalizedName = NormalizeRelativeUpdatePath(file.Name);
                    var destPath = Path.Combine(stagingDir, normalizedName);
                    var destDir = Path.GetDirectoryName(destPath);
                    if (destDir != null && !Directory.Exists(destDir)) Directory.CreateDirectory(destDir);
                    
                    if (!await DownloadFileAsync(file.DownloadUrl, destPath, progress, cancellationToken))
                        return false;
                    
                    completed++;
                    progress?.Report(new UpdateDownloadProgress 
                    { 
                        Percentage = completed * 100 / updateInfo.FilesToUpdate.Count,
                        StatusText = $"File {completed} of {updateInfo.FilesToUpdate.Count}: {normalizedName}"
                    });
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

            // Fallback: Original full asset download logic
            var downloadPath = Path.Combine(tempDir, updateInfo.FileName);
            if (!await DownloadFileAsync(updateInfo.DownloadUrl, downloadPath, progress, cancellationToken))
                return false;

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

    private async Task<string> CalculateHashAsync(string filePath)
    {
        using var sha256 = SHA256.Create();
        using var stream = File.OpenRead(filePath);
        var hashBytes = await sha256.ComputeHashAsync(stream);
        return BitConverter.ToString(hashBytes).Replace("-", "");
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
        await using var fileStream = new FileStream(destinationPath, FileMode.Create, FileAccess.Write, FileShare.None);

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
            sb.AppendLine("    Write-Host 'Waiting for Redball to close...'");
            sb.AppendLine("    while (Get-Process -Name $processName -ErrorAction SilentlyContinue) {");
            sb.AppendLine("        Start-Sleep -Seconds 1");
            sb.AppendLine("    }");
            sb.AppendLine();
            sb.AppendLine("    Write-Host 'Creating backup of UserData...'");
            sb.AppendLine("    $hasBackup = Backup-UserData");
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

    public List<FileUpdateInfo>? FilesToUpdate { get; set; }
    public string VersionDisplay => $"v{LatestVersion.Major}.{LatestVersion.Minor}.{LatestVersion.Build}";
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
    public string DownloadUrl { get; set; } = "";
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
