namespace Redball.Service;

using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.Runtime.InteropServices;

public partial class InputInjectionService : BackgroundService
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
        Log.ServiceStarting(_logger);

        try
        {
            _engine.Initialize();
            Log.EngineInitialized(_logger);

            _ipcServer.Start();
            Log.IpcServerStarted(_logger, IpcServer.PipeName);

            await Task.Delay(Timeout.Infinite, stoppingToken);
        }
        catch (OperationCanceledException)
        {
            Log.ServiceShutdown(_logger);
        }
        catch (Exception ex)
        {
            Log.ServiceFatalError(_logger, ex);
            throw;
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        Log.ServiceStopping(_logger);
        _ipcServer.Stop();
        _engine.Shutdown();
        await base.StopAsync(cancellationToken);
    }

    private static partial class Log
    {
        [LoggerMessage(Level = LogLevel.Information, Message = "Redball Input Service starting...")]
        public static partial void ServiceStarting(ILogger logger);

        [LoggerMessage(Level = LogLevel.Information, Message = "Input injection engine initialized")]
        public static partial void EngineInitialized(ILogger logger);

        [LoggerMessage(Level = LogLevel.Information, Message = "IPC server started on pipe: {PipeName}")]
        public static partial void IpcServerStarted(ILogger logger, string pipeName);

        [LoggerMessage(Level = LogLevel.Information, Message = "Service shutdown requested")]
        public static partial void ServiceShutdown(ILogger logger);

        [LoggerMessage(Level = LogLevel.Error, Message = "Service encountered fatal error")]
        public static partial void ServiceFatalError(ILogger logger, Exception ex);

        [LoggerMessage(Level = LogLevel.Information, Message = "Redball Input Service stopping...")]
        public static partial void ServiceStopping(ILogger logger);
    }
}
