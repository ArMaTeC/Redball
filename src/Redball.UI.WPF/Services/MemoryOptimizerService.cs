using System;
using System.Runtime;
using System.Threading.Tasks;

namespace Redball.UI.Services;

/// <summary>
/// Proactively compacts the Large Object Heap (LOH) when the system idle tracker registers AFK states.
/// Masks garbage collection performance stutters from the active user interaction context.
/// </summary>
public sealed class MemoryOptimizerService
{
    private static readonly MemoryOptimizerService _instance = new();
    public static MemoryOptimizerService Instance => _instance;

    private bool _initialized;

    private MemoryOptimizerService() { }

    public void Initialize(IdleDetectionService idleService)
    {
        if (_initialized) return;

        // Note: Assumes IdleDetectionService exists and exposes OnUserIdleDetected.
        // For the purpose of the 10/10 implementation plan execution.
        try
        {
            idleService.OnUserIdleDetected += HandleUserIdle;
            _initialized = true;
            Logger.Info("MemoryOptimizer", "Idle-Triggered Garbage Collection hooked into IdleDetectionService.");
        }
        catch (Exception ex)
        {
            Logger.Error("MemoryOptimizer", "Failed to hook idle detection.", ex);
        }
    }

    private void HandleUserIdle(object? sender, EventArgs e)
    {
        Task.Run(() =>
        {
            try
            {
                Logger.Info("MemoryOptimizer", "Aggressive memory compaction triggered (user idle)");
                GCSettings.LargeObjectHeapCompactionMode = GCLargeObjectHeapCompactionMode.CompactOnce;
                GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, true, true);
                Logger.Info("MemoryOptimizer", "Aggressive memory compaction complete");
            }
            catch (Exception ex)
            {
                Logger.Error("MemoryOptimizer", "Memory compaction failure", ex);
            }
        });
    }
}
