using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;

namespace Redball.UI.Services;

/// <summary>
/// Startup performance optimizer with deferred service initialization.
/// Moves heavy services off the critical startup path for sub-second launch.
/// </summary>
public sealed class StartupOptimizer
{
    private readonly Dictionary<string, LazyService> _lazyServices = new();
    private readonly Stopwatch _startupTimer = new();
    private bool _startupComplete;

    public static StartupOptimizer Instance { get; } = new();

    public TimeSpan StartupDuration { get; private set; }
    public bool IsStartupComplete => _startupComplete;

    private StartupOptimizer() { }

    /// <summary>
    /// Begins measuring startup time and registers lazy services.
    /// </summary>
    public void BeginStartup()
    {
        _startupTimer.Restart();
        Logger.Info("StartupOptimizer", "Startup optimization begun");

        // Register heavy services for lazy initialization
        RegisterLazyService("AdvancedAnalytics", () => AdvancedAnalyticsService.Instance, 
            priority: LazyLoadPriority.Background, 
            delay: TimeSpan.FromSeconds(5));

        RegisterLazyService("ScheduleLearning", () => ScheduleLearningService.Instance,
            priority: LazyLoadPriority.Background,
            delay: TimeSpan.FromSeconds(10));

        RegisterLazyService("MobileCompanion", () => MobileCompanionApiService.Instance,
            priority: LazyLoadPriority.OnDemand,
            condition: () => true); // Always available

        RegisterLazyService("VoiceCommand", () => VoiceCommandService.Instance,
            priority: LazyLoadPriority.OnDemand,
            condition: () => true); // Always available

        RegisterLazyService("Windows11Widget", () => Windows11WidgetService.Instance,
            priority: LazyLoadPriority.Background,
            delay: TimeSpan.FromSeconds(3),
            condition: () => Environment.OSVersion.Version.Build >= 22000);

        RegisterLazyService("DesignTokenPipeline", () => DesignTokenPipelineService.Instance,
            priority: LazyLoadPriority.OnDemand);

        RegisterLazyService("DiagnosticsExport", () => DiagnosticsExportService.Instance,
            priority: LazyLoadPriority.OnDemand);

        RegisterLazyService("DeltaUpdate", () => DeltaUpdateService.Instance,
            priority: LazyLoadPriority.AfterFirstIdle);
    }

    /// <summary>
    /// Marks startup as complete and begins lazy loading background services.
    /// </summary>
    public void MarkStartupComplete()
    {
        _startupTimer.Stop();
        StartupDuration = _startupTimer.Elapsed;
        _startupComplete = true;

        Logger.Info("StartupOptimizer", $"Core startup completed in {StartupDuration.TotalMilliseconds:F0}ms");

        // Begin deferred service initialization
        Task.Run(async () => await InitializeDeferredServicesAsync());
    }

    /// <summary>
    /// Gets a lazy-loaded service, triggering initialization if needed.
    /// </summary>
    public T GetService<T>(string name) where T : class
    {
        if (_lazyServices.TryGetValue(name, out var lazy))
        {
            // Ensure initialization if not already done
            if (!lazy.IsInitialized && lazy.Priority == LazyLoadPriority.OnDemand)
            {
                Task.Run(() => lazy.InitializeAsync()).Wait(TimeSpan.FromSeconds(5));
            }
            return lazy.Instance as T ?? throw new InvalidOperationException($"Service {name} type mismatch");
        }

        throw new KeyNotFoundException($"Service {name} not registered");
    }

    /// <summary>
    /// Attempts to get a service without triggering initialization.
    /// </summary>
    public bool TryGetService<T>(string name, out T? service) where T : class
    {
        if (_lazyServices.TryGetValue(name, out var lazy) && lazy.IsInitialized)
        {
            service = lazy.Instance as T;
            return service != null;
        }

        service = null;
        return false;
    }

    private void RegisterLazyService(string name, Func<object> factory, 
        LazyLoadPriority priority = LazyLoadPriority.Background,
        TimeSpan? delay = null,
        Func<bool>? condition = null)
    {
        _lazyServices[name] = new LazyService(name, factory, priority, delay, condition);
    }

    private async Task InitializeDeferredServicesAsync()
    {
        var sw = Stopwatch.StartNew();

        // Group by priority and delay
        var immediate = _lazyServices.Values.Where(s => s.Priority == LazyLoadPriority.AfterFirstIdle);
        var delayed = _lazyServices.Values.Where(s => s.Priority == LazyLoadPriority.Background && s.Delay.HasValue);
        var onDemand = _lazyServices.Values.Where(s => s.Priority == LazyLoadPriority.OnDemand);

        // Initialize AfterFirstIdle services immediately
        foreach (var service in immediate.Where(s => s.ShouldInitialize()))
        {
            await service.InitializeAsync();
        }

        // Wait for system to become idle
        await WaitForIdleAsync();

        // Initialize background services with delays
        foreach (var service in delayed.Where(s => s.ShouldInitialize()))
        {
            if (service.Delay.HasValue)
            {
                await Task.Delay(service.Delay.Value);
            }
            
            await service.InitializeAsync();
        }

        sw.Stop();
        Logger.Info("StartupOptimizer", $"Deferred services initialized in {sw.Elapsed.TotalMilliseconds:F0}ms");
    }

    private async Task WaitForIdleAsync()
    {
        // Simple idle detection - wait for low CPU or just delay
        await Task.Delay(TimeSpan.FromSeconds(2));
    }

    /// <summary>
    /// Gets a startup performance report.
    /// </summary>
    public StartupReport GetReport()
    {
        var report = new StartupReport
        {
            CoreStartupMs = StartupDuration.TotalMilliseconds,
            Services = new List<ServiceStartupInfo>()
        };

        foreach (var service in _lazyServices.Values)
        {
            report.Services.Add(new ServiceStartupInfo
            {
                Name = service.Name,
                Priority = service.Priority.ToString(),
                Initialized = service.IsInitialized,
                InitializationMs = service.InitializationDuration?.TotalMilliseconds
            });
        }

        return report;
    }
}

/// <summary>
/// Represents a lazily-initialized service.
/// </summary>
public class LazyService
{
    private readonly System.Threading.SemaphoreSlim _lock = new(1, 1);
    private object? _instance;

    public string Name { get; }
    public Func<object> Factory { get; }
    public LazyLoadPriority Priority { get; }
    public TimeSpan? Delay { get; }
    public Func<bool>? Condition { get; }
    
    public bool IsInitialized { get; private set; }
    public TimeSpan? InitializationDuration { get; private set; }

    public LazyService(string name, Func<object> factory, 
        LazyLoadPriority priority, TimeSpan? delay, Func<bool>? condition)
    {
        Name = name;
        Factory = factory;
        Priority = priority;
        Delay = delay;
        Condition = condition;
    }

    public bool ShouldInitialize()
    {
        return Condition?.Invoke() ?? true;
    }

    public async Task<object> InitializeAsync()
    {
        if (IsInitialized) return _instance!;

        await _lock.WaitAsync();
        try
        {
            if (IsInitialized) return _instance!;

            var sw = Stopwatch.StartNew();
            
            try
            {
                _instance = Factory();
                
                // If the instance has an async init method, call it
                if (_instance is IInitializable initializable)
                {
                    await initializable.InitializeAsync().WaitAsync(TimeSpan.FromSeconds(10));
                }
                
                IsInitialized = true;
                InitializationDuration = sw.Elapsed;
                
                Logger.Debug("StartupOptimizer", $"Service {Name} initialized in {sw.Elapsed.TotalMilliseconds:F0}ms");
            }
            catch (Exception ex)
            {
                Logger.Error("StartupOptimizer", $"Failed to initialize service {Name}", ex);
                throw;
            }
            finally
            {
                sw.Stop();
            }

            return _instance;
        }
        finally
        {
            _lock.Release();
        }
    }

    public object Instance => _instance ?? throw new InvalidOperationException($"Service {Name} not initialized");
}

public enum LazyLoadPriority
{
    /// <summary>
    /// Initialize immediately after core startup.
    /// </summary>
    AfterFirstIdle,

    /// <summary>
    /// Initialize with specified delay after startup.
    /// </summary>
    Background,

    /// <summary>
    /// Initialize only when first accessed.
    /// </summary>
    OnDemand
}

public interface IInitializable
{
    Task InitializeAsync();
}

public class StartupReport
{
    public double CoreStartupMs { get; set; }
    public List<ServiceStartupInfo> Services { get; set; } = new();
}

public class ServiceStartupInfo
{
    public string Name { get; set; } = "";
    public string Priority { get; set; } = "";
    public bool Initialized { get; set; }
    public double? InitializationMs { get; set; }
}
