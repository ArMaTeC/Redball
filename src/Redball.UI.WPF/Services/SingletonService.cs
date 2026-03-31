using System;
using System.Threading;

namespace Redball.UI.Services;

/// <summary>
/// Ensures only one instance of Redball runs at a time using a named Mutex.
/// Port of Test-RedballInstanceRunning, Initialize-RedballSingleton.
/// </summary>
public class SingletonService : IDisposable
{
    private const string MutexName = "Global\\Redball_Singleton_Mutex";
    private Mutex? _mutex;
    private bool _hasOwnership;
    private bool _disposed;

    /// <summary>
    /// Attempts to acquire the singleton mutex.
    /// Returns true if this is the only instance, false if another instance is running.
    /// </summary>
    public bool TryAcquire()
    {
        try
        {
            _mutex = new Mutex(true, MutexName, out _hasOwnership);

            if (_hasOwnership)
            {
                Logger.Info("SingletonService", "Singleton mutex acquired - this is the only instance");
                return true;
            }
            else
            {
                Logger.Warning("SingletonService", "Another Redball instance is already running");
                return false;
            }
        }
        catch (AbandonedMutexException)
        {
            // Previous instance crashed without releasing mutex
            Logger.Warning("SingletonService", "Acquired abandoned mutex - previous instance may have crashed");
            _hasOwnership = true;
            return true;
        }
        catch (Exception ex)
        {
            Logger.Error("SingletonService", "Failed to create singleton mutex", ex);
            // Allow running if mutex creation fails
            return true;
        }
    }

    /// <summary>
    /// Returns true if another instance of Redball is already running.
    /// </summary>
    public static bool IsAnotherInstanceRunning()
    {
        try
        {
            using var mutex = Mutex.OpenExisting(MutexName);
            return true; // Mutex exists, another instance is running
        }
        catch (WaitHandleCannotBeOpenedException)
        {
            return false; // No mutex, no other instance
        }
        catch (Exception ex)
        {
            Logger.Debug("SingletonService", $"Failed to check mutex existence: {ex.Message}");
            return false;
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        try
        {
            if (_hasOwnership && _mutex != null)
            {
                _mutex.ReleaseMutex();
                Logger.Debug("SingletonService", "Singleton mutex released");
            }
            _mutex?.Dispose();
        }
        catch (Exception ex)
        {
            Logger.Debug("SingletonService", $"Mutex cleanup: {ex.Message}");
        }
    }
}
