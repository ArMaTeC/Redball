using System;
using System.Threading;
using System.Threading.Tasks;

namespace Redball.UI.Services;

/// <summary>
/// Interface for update checking and installation.
/// </summary>
    public interface IUpdateService
    {
        Task<UpdateInfo?> CheckForUpdateAsync(CancellationToken cancellationToken = default);
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
    }
