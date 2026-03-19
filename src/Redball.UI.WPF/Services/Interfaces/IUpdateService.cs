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
    Task<bool> DownloadAndInstallAsync(UpdateInfo updateInfo, IProgress<int>? progress = null, CancellationToken cancellationToken = default);
}
