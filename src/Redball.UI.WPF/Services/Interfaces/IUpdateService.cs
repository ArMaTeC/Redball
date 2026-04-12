using System;
using System.Threading;
using System.Threading.Tasks;

namespace Redball.UI.Services;

/// <summary>
/// Update pipeline stages shown in the progress window.
/// </summary>
public enum UpdateStage
{
    Checking,
    Downloading,
    Patching,
    Verifying,
    Staging,
    Applying,
    Complete,
    Failed
}

/// <summary>
/// Stages for update checking progress.
/// </summary>
public enum UpdateCheckStage
{
    Connecting,
    FetchingReleases,
    ParsingManifest,
    ComparingVersions,
    HashingFiles,
    CalculatingDiff,
    Complete,
    Failed
}

/// <summary>
/// Progress information for update check operation.
/// </summary>
public class UpdateCheckProgress
{
    public int Percentage { get; set; }
    public UpdateCheckStage Stage { get; set; } = UpdateCheckStage.Connecting;
    public string? StatusText { get; set; }
    public string? LogEntry { get; set; }
    public int FilesHashed { get; set; }
    public int TotalFilesToHash { get; set; }
}

/// <summary>
/// Interface for update checking and installation.
/// </summary>
    public interface IUpdateService
    {
        Task<UpdateInfo?> CheckForUpdateAsync(bool bypassCache = false, IProgress<UpdateCheckProgress>? progress = null, CancellationToken cancellationToken = default);
        Task<bool> DownloadAndInstallAsync(UpdateInfo updateInfo, IProgress<UpdateDownloadProgress>? progress = null, CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// Detailed progress information for update downloads.
    /// </summary>
    public class UpdateDownloadProgress
    {
        public int Percentage { get; set; }
        public double BytesPerSecond { get; set; }
        public long BytesReceived { get; set; }
        public long TotalBytes { get; set; }
        public string? StatusText { get; set; }
        public UpdateStage Stage { get; set; } = UpdateStage.Downloading;
        /// <summary>
        /// Optional log line to append to the update log window.
        /// </summary>
        public string? LogEntry { get; set; }
        /// <summary>
        /// Current file index when processing multiple files (1-based).
        /// </summary>
        public int CurrentFile { get; set; }
        /// <summary>
        /// Total number of files being processed.
        /// </summary>
        public int TotalFiles { get; set; }
        /// <summary>
        /// Name of the file currently being processed.
        /// </summary>
        public string? CurrentFileName { get; set; }
        /// <summary>
        /// Whether this update is a delta/differential update.
        /// </summary>
        public bool IsDelta { get; set; }
    }
