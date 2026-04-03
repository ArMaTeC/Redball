using System;
using Microsoft.Extensions.DependencyInjection;

namespace Redball.UI.Services;

/// <summary>
/// Central service locator providing DI container access for the application.
/// Configured once at startup in App.xaml.cs and used by services that cannot
/// receive constructor injection (e.g. XAML-instantiated views).
/// 
/// Prefer constructor injection via DI wherever possible. Use ServiceLocator
/// only as a bridge for WPF components that must be created by the framework.
/// </summary>
public static class ServiceLocator
{
    private static IServiceProvider? _provider;

    /// <summary>
    /// The application-wide service provider. Set once during App.OnStartup.
    /// </summary>
    public static IServiceProvider Provider
    {
        get => _provider ?? throw new InvalidOperationException(
            "ServiceLocator has not been configured. Call ServiceLocator.Configure() in App.OnStartup.");
        private set => _provider = value;
    }

    /// <summary>
    /// Configures the service locator with the given provider. Call once at startup.
    /// </summary>
    public static void Configure(IServiceProvider provider)
    {
        _provider = provider ?? throw new ArgumentNullException(nameof(provider));
        Logger.Info("ServiceLocator", "DI container configured");
    }

    /// <summary>
    /// Resolves a service from the DI container.
    /// </summary>
    public static T GetRequired<T>() where T : notnull
        => Provider.GetRequiredService<T>();

    /// <summary>
    /// Resolves a service from the DI container, returning null if not registered.
    /// </summary>
    public static T? Get<T>() where T : class
        => Provider.GetService<T>();

    /// <summary>
    /// Gets the outbox store service, or null if not registered.
    /// </summary>
    public static Redball.Core.Sync.IOutboxStore? OutboxStore
        => Get<Redball.Core.Sync.IOutboxStore>();

    /// <summary>
    /// Configures all application services in the DI container.
    /// </summary>
    public static IServiceProvider BuildServiceProvider(RedballConfig config)
    {
        var services = new ServiceCollection();

        // Core services — singletons
        services.AddSingleton<IConfigService>(ConfigService.Instance);
        services.AddSingleton<IKeepAwakeService>(KeepAwakeService.Instance);
        services.AddSingleton<ILocalizationService>(LocalizationService.Instance);
        services.AddSingleton<INotificationService>(NotificationService.Instance);

        // Analytics — singleton instance shared across app
        services.AddSingleton<IAnalyticsService>(AnalyticsService.Instance);

        // Transient services — new instance per resolution
        services.AddTransient<IHealthCheckService, HealthCheckService>();
        services.AddTransient<ISessionStateService, SessionStateService>();
        services.AddTransient<IUpdateService>(sp =>
            new UpdateService(
                config.UpdateRepoOwner,
                config.UpdateRepoName,
                config.UpdateChannel,
                config.VerifyUpdateSignature));

        var provider = services.BuildServiceProvider();
        Configure(provider);
        return provider;
    }
}
