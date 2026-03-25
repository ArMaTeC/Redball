namespace Redball.Service;

using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.Runtime.InteropServices;

public class InputInjectionService : BackgroundService
{
    private readonly ILogger<InputInjectionService> _logger;
    private readonly InputInjectionEngine _engine;
    private readonly IpcServer _ipcServer;

    public InputInjectionService(
        ILogger<InputInjectionService> logger,
        InputInjectionEngine engine,
        IpcServer ipcServer)
    {
        _logger = logger;
        _engine = engine;
        _ipcServer = ipcServer;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Redball Input Service starting...");

        try
        {
            _engine.Initialize();
            _logger.LogInformation("Input injection engine initialized");

            _ipcServer.Start();
            _logger.LogInformation("IPC server started on pipe: {PipeName}", IpcServer.PipeName);

            await Task.Delay(Timeout.Infinite, stoppingToken);
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("Service shutdown requested");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Service encountered fatal error");
            throw;
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Redball Input Service stopping...");
        _ipcServer.Stop();
        _engine.Shutdown();
        await base.StopAsync(cancellationToken);
    }
}
