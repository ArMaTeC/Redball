using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media.Animation;
using Redball.UI.Services;

namespace Redball.UI.WPF.Services;

/// <summary>
/// Latency masking options for async operations.
/// Implements ux-4 from improve_me.txt: Standard latency masking pattern.
/// </summary>
public class LatencyMaskingOptions
{
    /// <summary>
    /// Minimum time to show loading state (prevents flicker for fast operations).
    /// </summary>
    public TimeSpan MinDisplayDuration { get; set; } = TimeSpan.FromMilliseconds(300);

    /// <summary>
    /// Maximum time before showing extended waiting UI.
    /// </summary>
    public TimeSpan ExtendedWaitThreshold { get; set; } = TimeSpan.FromSeconds(3);

    /// <summary>
    /// Whether to show progress percentage.
    /// </summary>
    public bool ShowProgress { get; set; } = true;

    /// <summary>
    /// Whether to allow cancellation.
    /// </summary>
    public bool AllowCancellation { get; set; } = true;

    /// <summary>
    /// Loading message to display.
    /// </summary>
    public string? LoadingMessage { get; set; }

    /// <summary>
    /// Extended wait message (shown after threshold).
    /// </summary>
    public string? ExtendedWaitMessage { get; set; }
}

/// <summary>
/// Service for managing latency masking across async operations.
/// Provides consistent loading states, progress indication, and skeleton screens.
/// </summary>
public class LatencyMaskingService
{
    private static readonly Lazy<LatencyMaskingService> _instance = new(() => new LatencyMaskingService());
    public static LatencyMaskingService Instance => _instance.Value;

    private LatencyMaskingService()
    {
        Logger.Info("LatencyMaskingService", "Latency masking service initialized");
    }

    /// <summary>
    /// Executes an async operation with latency masking.
    /// </summary>
    public async Task<T> ExecuteWithMaskingAsync<T>(
        Func<CancellationToken, Task<T>> operation,
        LatencyMaskingOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        options ??= new LatencyMaskingOptions();
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        try
        {
            // Show loading state immediately
            ShowLoadingState(options);

            // Start the operation
            var operationTask = operation(cts.Token);

            // Wait for min display duration OR operation completion, whichever is longer
            var minDisplayTask = Task.Delay(options.MinDisplayDuration, cts.Token);
            await Task.WhenAll(operationTask, minDisplayTask);

            var result = await operationTask;

            stopwatch.Stop();
            Logger.Debug("LatencyMaskingService", $"Operation completed in {stopwatch.ElapsedMilliseconds}ms");

            return result;
        }
        catch (OperationCanceledException)
        {
            Logger.Warning("LatencyMaskingService", "Operation was cancelled");
            throw;
        }
        catch (Exception ex)
        {
            Logger.Error("LatencyMaskingService", "Operation failed", ex);
            throw;
        }
        finally
        {
            HideLoadingState();
            cts.Dispose();
        }
    }

    /// <summary>
    /// Executes an async operation with latency masking (void return).
    /// </summary>
    public async Task ExecuteWithMaskingAsync(
        Func<CancellationToken, Task> operation,
        LatencyMaskingOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        await ExecuteWithMaskingAsync(async ct =>
        {
            await operation(ct);
            return true;
        }, options, cancellationToken);
    }

    /// <summary>
    /// Shows a skeleton screen for data loading scenarios.
    /// </summary>
    public void ShowSkeletonScreen(string containerElementId, int skeletonItemCount = 5)
    {
        // This would integrate with UI to show placeholder skeleton items
        Logger.Debug("LatencyMaskingService", $"Showing skeleton screen in {containerElementId} with {skeletonItemCount} items");

        Application.Current.Dispatcher.Invoke(() =>
        {
            // Trigger skeleton animation via event that UI can subscribe to
            SkeletonScreenRequested?.Invoke(this, new SkeletonScreenEventArgs
            {
                ContainerId = containerElementId,
                ItemCount = skeletonItemCount
            });
        });
    }

    /// <summary>
    /// Hides skeleton screen.
    /// </summary>
    public void HideSkeletonScreen(string containerElementId)
    {
        Application.Current.Dispatcher.Invoke(() =>
        {
            SkeletonScreenHidden?.Invoke(this, new SkeletonScreenEventArgs
            {
                ContainerId = containerElementId,
                ItemCount = 0
            });
        });
    }

    /// <summary>
    /// Shows an optimistic UI update that will be confirmed or rolled back.
    /// </summary>
    public void ShowOptimisticUpdate(string elementId, object temporaryValue)
    {
        Logger.Debug("LatencyMaskingService", $"Optimistic update for {elementId}");

        Application.Current.Dispatcher.Invoke(() =>
        {
            OptimisticUpdateRequested?.Invoke(this, new OptimisticUpdateEventArgs
            {
                ElementId = elementId,
                TemporaryValue = temporaryValue
            });
        });
    }

    /// <summary>
    /// Confirms an optimistic update (operation succeeded).
    /// </summary>
    public void ConfirmOptimisticUpdate(string elementId, object? finalValue = null)
    {
        Application.Current.Dispatcher.Invoke(() =>
        {
            OptimisticUpdateConfirmed?.Invoke(this, new OptimisticUpdateEventArgs
            {
                ElementId = elementId,
                FinalValue = finalValue
            });
        });
    }

    /// <summary>
    /// Rolls back an optimistic update (operation failed).
    /// </summary>
    public void RollbackOptimisticUpdate(string elementId, object originalValue)
    {
        Logger.Warning("LatencyMaskingService", $"Rolling back optimistic update for {elementId}");

        Application.Current.Dispatcher.Invoke(() =>
        {
            OptimisticUpdateRolledBack?.Invoke(this, new OptimisticUpdateEventArgs
            {
                ElementId = elementId,
                OriginalValue = originalValue
            });
        });
    }

    /// <summary>
    /// Debounces an action to prevent rapid successive calls.
    /// </summary>
    public Action<T> Debounce<T>(Action<T> action, TimeSpan interval)
    {
        CancellationTokenSource? cts = null;

        return (arg) =>
        {
            cts?.Cancel();
            cts?.Dispose();
            cts = new CancellationTokenSource();

            Task.Run(async () =>
            {
                try
                {
                    await Task.Delay(interval, cts.Token);
                    Application.Current.Dispatcher.Invoke(() => action(arg));
                }
                catch (OperationCanceledException)
                {
                    // Expected, ignore
                }
            }, cts.Token);
        };
    }

    /// <summary>
    /// Throttles an action to limit execution rate.
    /// </summary>
    public Action<T> Throttle<T>(Action<T> action, TimeSpan interval)
    {
        DateTime lastExecution = DateTime.MinValue;
        object _lock = new();

        return (arg) =>
        {
            lock (_lock)
            {
                if (DateTime.Now - lastExecution < interval)
                    return;

                lastExecution = DateTime.Now;
            }

            action(arg);
        };
    }

    // Events for UI integration
    public event EventHandler<LoadingStateEventArgs>? LoadingStateChanged;
    public event EventHandler<SkeletonScreenEventArgs>? SkeletonScreenRequested;
    public event EventHandler<SkeletonScreenEventArgs>? SkeletonScreenHidden;
    public event EventHandler<OptimisticUpdateEventArgs>? OptimisticUpdateRequested;
    public event EventHandler<OptimisticUpdateEventArgs>? OptimisticUpdateConfirmed;
    public event EventHandler<OptimisticUpdateEventArgs>? OptimisticUpdateRolledBack;

    private void ShowLoadingState(LatencyMaskingOptions options)
    {
        Application.Current.Dispatcher.Invoke(() =>
        {
            LoadingStateChanged?.Invoke(this, new LoadingStateEventArgs
            {
                IsLoading = true,
                Message = options.LoadingMessage,
                ShowProgress = options.ShowProgress,
                AllowCancellation = options.AllowCancellation
            });
        });
    }

    private void HideLoadingState()
    {
        Application.Current.Dispatcher.Invoke(() =>
        {
            LoadingStateChanged?.Invoke(this, new LoadingStateEventArgs
            {
                IsLoading = false
            });
        });
    }
}

/// <summary>
/// Event args for loading state changes.
/// </summary>
public class LoadingStateEventArgs : EventArgs
{
    public bool IsLoading { get; set; }
    public string? Message { get; set; }
    public bool ShowProgress { get; set; }
    public bool AllowCancellation { get; set; }
    public double? ProgressPercent { get; set; }
}

/// <summary>
/// Event args for skeleton screen requests.
/// </summary>
public class SkeletonScreenEventArgs : EventArgs
{
    public string ContainerId { get; set; } = "";
    public int ItemCount { get; set; }
}

/// <summary>
/// Event args for optimistic updates.
/// </summary>
public class OptimisticUpdateEventArgs : EventArgs
{
    public string ElementId { get; set; } = "";
    public object? TemporaryValue { get; set; }
    public object? FinalValue { get; set; }
    public object? OriginalValue { get; set; }
}

/// <summary>
/// Extension methods for easy latency masking integration.
/// </summary>
public static class LatencyMaskingExtensions
{
    /// <summary>
    /// Wraps a Task with latency masking.
    /// </summary>
    public static Task<T> WithLatencyMasking<T>(
        this Task<T> task,
        string? loadingMessage = null,
        CancellationToken cancellationToken = default)
    {
        return LatencyMaskingService.Instance.ExecuteWithMaskingAsync(
            _ => task,
            new LatencyMaskingOptions { LoadingMessage = loadingMessage },
            cancellationToken);
    }

    /// <summary>
    /// Wraps a Task with latency masking (void).
    /// </summary>
    public static Task WithLatencyMasking(
        this Task task,
        string? loadingMessage = null,
        CancellationToken cancellationToken = default)
    {
        return LatencyMaskingService.Instance.ExecuteWithMaskingAsync(
            async _ => { await task; },
            new LatencyMaskingOptions { LoadingMessage = loadingMessage },
            cancellationToken);
    }
}
