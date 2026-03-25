using System;
using System.Threading.Tasks;

namespace Redball.UI.Services;

/// <summary>
/// Interface for application health monitoring.
/// </summary>
public interface IHealthCheckService : IDisposable
{
    Task<HealthStatus> CheckHealthAsync();
    string GetHealthReport(HealthStatus status);
}
