using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Redball.Core.Sync;

/// <summary>
/// Conflict-free Replicated Data Type (CRDT) for settings synchronization.
/// Guarantees eventual consistency across devices without conflicts.
/// 
/// Uses a Grow-only Set (G-Set) of operations with Last-Writer-Wins (LWW)
/// semantics per property. Each operation is immutable and carries a
/// vector clock for causal ordering.
/// </summary>
public class CrdtSettingsSync : IDisposable
{
    private readonly string _nodeId;
    private readonly GSet<SettingsOperation> _operations;
    private readonly VectorClock _clock;
    private readonly SemaphoreSlim _lock = new(1, 1);

    public CrdtSettingsSync(string nodeId)
    {
        _nodeId = nodeId ?? throw new ArgumentNullException(nameof(nodeId));
        _operations = new GSet<SettingsOperation>();
        _clock = new VectorClock(nodeId);
    }

    /// <summary>
    /// Records a settings change as an immutable operation.
    /// This is the local update path.
    /// </summary>
    public async Task RecordOperationAsync(string propertyPath, object value, DateTime timestamp)
    {
        await _lock.WaitAsync();
        try
        {
            _clock.Increment();
            var operation = new SettingsOperation(
                Guid.NewGuid(),
                _clock.GetCurrent(),
                _nodeId,
                propertyPath,
                value,
                timestamp,
                OperationType.Update
            );
            _operations.Add(operation);
            
            Logger.Debug("CrdtSettingsSync", $"Recorded operation {operation.Id} for {propertyPath}");
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>
    /// Merges operations from another device. Idempotent and commutative.
    /// This is the sync receive path.
    /// </summary>
    public async Task MergeAsync(CrdtSettingsSync other)
    {
        await _lock.WaitAsync();
        try
        {
            int added = 0;
            foreach (var op in other._operations.GetAll())
            {
                if (_operations.Add(op))
                {
                    added++;
                    _clock.Merge(op.VectorClock);
                }
            }
            
            if (added > 0)
            {
                Logger.Info("CrdtSettingsSync", $"Merged {added} operations from {other._nodeId}");
            }
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>
    /// Resolves the current state by applying operations in causal order.
    /// Last-Writer-Wins per property with vector clock tie-breaking.
    /// </summary>
    public async Task<RedballConfig> ResolveStateAsync()
    {
        await _lock.WaitAsync();
        try
        {
            var config = new RedballConfig();
            var propertyStates = new Dictionary<string, SettingsOperation>();

            // Group by property and find most recent based on vector clock
            foreach (var op in _operations.GetAll().OrderBy(o => o.Timestamp))
            {
                if (!propertyStates.TryGetValue(op.PropertyPath, out var existing))
                {
                    propertyStates[op.PropertyPath] = op;
                }
                else if (op.VectorClock.IsAfter(existing.VectorClock))
                {
                    propertyStates[op.PropertyPath] = op;
                }
                // If concurrent (neither is after the other), use timestamp
                else if (!existing.VectorClock.IsAfter(op.VectorClock))
                {
                    if (op.Timestamp > existing.Timestamp)
                    {
                        propertyStates[op.PropertyPath] = op;
                    }
                }
            }

            // Apply winning operations to config
            foreach (var (path, op) in propertyStates)
            {
                ApplyToConfig(config, path, op.Value);
            }

            return config;
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>
    /// Gets all operations since a given vector clock (for incremental sync).
    /// </summary>
    public IReadOnlyList<SettingsOperation> GetOperationsSince(VectorClock since)
    {
        return _operations.GetAll()
            .Where(op => !since.IsBefore(op.VectorClock))
            .ToList();
    }

    /// <summary>
    /// Serializes the CRDT state for storage or transmission.
    /// </summary>
    public string Serialize()
    {
        var state = new CrdtState
        {
            NodeId = _nodeId,
            Clock = _clock.GetCurrent(),
            Operations = _operations.GetAll().ToList()
        };

        return JsonSerializer.Serialize(state, new JsonSerializerOptions
        {
            WriteIndented = false,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        });
    }

    /// <summary>
    /// Deserializes CRDT state from storage or transmission.
    /// </summary>
    public static CrdtSettingsSync Deserialize(string json)
    {
        var state = JsonSerializer.Deserialize<CrdtState>(json, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        });

        if (state == null) throw new ArgumentException("Invalid CRDT state");

        var sync = new CrdtSettingsSync(state.NodeId);
        
        foreach (var op in state.Operations)
        {
            sync._operations.Add(op);
        }
        
        sync._clock.Merge(state.Clock);
        
        return sync;
    }

    /// <summary>
    /// Gets the current vector clock for conflict detection.
    /// </summary>
    public VectorClock GetVectorClock()
    {
        return _clock.GetCurrent();
    }

    private static void ApplyToConfig(RedballConfig config, string path, object value)
    {
        try
        {
            var property = typeof(RedballConfig).GetProperty(path);
            if (property != null && property.CanWrite)
            {
                var converted = Convert.ChangeType(value, property.PropertyType);
                property.SetValue(config, converted);
            }
        }
        catch (Exception ex)
        {
            Logger.Warning("CrdtSettingsSync", $"Failed to apply {path}: {ex.Message}");
        }
    }

    public void Dispose()
    {
        _lock?.Dispose();
    }
}

/// <summary>
/// Grow-only Set (G-Set) CRDT - elements can only be added, never removed.
/// </summary>
public class GSet<T> where T : notnull
{
    private readonly HashSet<T> _elements = new();
    private readonly object _lock = new();

    public bool Add(T element)
    {
        lock (_lock)
        {
            return _elements.Add(element);
        }
    }

    public bool Contains(T element)
    {
        lock (_lock)
        {
            return _elements.Contains(element);
        }
    }

    public IEnumerable<T> GetAll()
    {
        lock (_lock)
        {
            return _elements.ToList(); // Return copy for safety
        }
    }

    public int Count 
    { 
        get 
        { 
            lock (_lock) return _elements.Count; 
        } 
    }
}

/// <summary>
/// Vector clock for causal ordering of events.
/// </summary>
public class VectorClock
{
    private readonly string _nodeId;
    private readonly Dictionary<string, long> _clocks;
    private readonly object _lock = new();

    public VectorClock(string nodeId)
    {
        _nodeId = nodeId;
        _clocks = new Dictionary<string, long> { [nodeId] = 0 };
    }

    public void Increment()
    {
        lock (_lock)
        {
            _clocks[_nodeId] = _clocks.GetValueOrDefault(_nodeId) + 1;
        }
    }

    public void Merge(VectorClock other)
    {
        lock (_lock)
        {
            foreach (var (node, time) in other._clocks)
            {
                _clocks[node] = Math.Max(_clocks.GetValueOrDefault(node), time);
            }
        }
    }

    /// <summary>
    /// Checks if this clock is causally after another clock.
    /// </summary>
    public bool IsAfter(VectorClock other)
    {
        lock (_lock)
        {
            bool anyGreater = false;
            
            foreach (var (node, time) in other._clocks)
            {
                var ourTime = _clocks.GetValueOrDefault(node);
                if (ourTime < time) return false;
                if (ourTime > time) anyGreater = true;
            }

            // Check we have nodes the other doesn't
            foreach (var (node, time) in _clocks)
            {
                if (!other._clocks.ContainsKey(node) && time > 0)
                {
                    anyGreater = true;
                }
            }

            return anyGreater;
        }
    }

    /// <summary>
    /// Checks if this clock is causally before another clock.
    /// </summary>
    public bool IsBefore(VectorClock other)
    {
        return other.IsAfter(this);
    }

    /// <summary>
    /// Returns a copy of the current clock state.
    /// </summary>
    public VectorClock GetCurrent()
    {
        lock (_lock)
        {
            var copy = new VectorClock(_nodeId);
            foreach (var (node, time) in _clocks)
            {
                copy._clocks[node] = time;
            }
            return copy;
        }
    }

    public Dictionary<string, long> ToDictionary()
    {
        lock (_lock)
        {
            return new Dictionary<string, long>(_clocks);
        }
    }

    public override string ToString()
    {
        lock (_lock)
        {
            var parts = _clocks.OrderBy(x => x.Key).Select(x => $"{x.Key}:{x.Value}");
            return $"[{string.Join(", ", parts)}]";
        }
    }
}

/// <summary>
/// Immutable settings operation.
/// </summary>
public record SettingsOperation(
    Guid Id,
    VectorClock VectorClock,
    string NodeId,
    string PropertyPath,
    object Value,
    DateTime Timestamp,
    OperationType Type);

public enum OperationType
{
    Update,
    Delete
}

/// <summary>
/// Serializable CRDT state for persistence.
/// </summary>
public class CrdtState
{
    public string NodeId { get; set; } = "";
    public VectorClock Clock { get; set; } = new("");
    public List<SettingsOperation> Operations { get; set; } = new();
}
