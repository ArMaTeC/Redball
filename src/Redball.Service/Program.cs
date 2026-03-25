namespace Redball.Service;

using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;
using System.ServiceProcess;

public class Program
{
    public static void Main(string[] args)
    {
        var builder = Host.CreateApplicationBuilder(args);

        builder.Services.AddWindowsService(options =>
        {
            options.ServiceName = "Redball Input Service";
        });

        builder.Services.AddHostedService<InputInjectionService>();
        builder.Services.AddSingleton<InputInjectionEngine>();
        builder.Services.AddSingleton<IpcServer>();

        builder.Logging.AddEventLog(settings =>
        {
            settings.SourceName = "RedballService";
            settings.LogName = "Application";
        });

        var host = builder.Build();
        host.Run();
    }
}
