namespace Redball.Core.Sync;

/// <summary>
/// Service locator for sync/outbox services.
/// Used by UI layer to access the outbox store and dispatcher.
/// </summary>
public static class ServiceLocator
{
    private static IOutboxStore? _outboxStore;
    private static OutboxDispatcherService? _outboxDispatcher;

    /// <summary>
    /// The outbox store instance (SQLite-backed).
    /// </summary>
    public static IOutboxStore OutboxStore
    {
        get => _outboxStore ??= new SqliteOutboxStore();
        set => _outboxStore = value;
    }

    /// <summary>
    /// The outbox dispatcher service (background processor).
    /// </summary>
    public static OutboxDispatcherService? OutboxDispatcher
    {
        get => _outboxDispatcher;
        set => _outboxDispatcher = value;
    }

    /// <summary>
    /// Resets all services (used for testing).
    /// </summary>
    public static void Reset()
    {
        _outboxStore?.Dispose();
        _outboxStore = null;
        _outboxDispatcher = null;
    }
}
