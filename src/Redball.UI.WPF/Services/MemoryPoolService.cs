using System;
using System.Buffers;
using System.Security.Cryptography;
using System.Text;

namespace Redball.UI.Services;

/// <summary>
/// Manages pooled buffers for high-frequency allocations to reduce GC pressure.
/// Uses ArrayPool<T> for efficient memory reuse across hot paths like SendInput
/// operations and sync event serialization.
/// </summary>
public sealed class MemoryPoolService : IDisposable
{
    private readonly ArrayPool<byte> _bytePool;
    private readonly ArrayPool<char> _charPool;
    private readonly StringBuilderPool _stringBuilderPool;

    public static MemoryPoolService Instance { get; } = new();

    private MemoryPoolService()
    {
        // Configure pools for typical workload patterns
        _bytePool = ArrayPool<byte>.Create(
            maxArrayLength: 4096,  // Max buffer size
            maxArraysPerBucket: 64  // Pool depth per bucket
        );
        _charPool = ArrayPool<char>.Create(
            maxArrayLength: 1024,
            maxArraysPerBucket: 32
        );
        _stringBuilderPool = new StringBuilderPool(maxPoolSize: 16);
    }

    /// <summary>
    /// Rents a buffer for SendInput keypress operations.
    /// Size: 40 bytes (2 x INPUT structures for key down/up).
    /// Always return via using statement or try-finally.
    /// </summary>
    public PooledBuffer<byte> RentSendInputBuffer()
    {
        // INPUT structure is 28 bytes on x64 + padding
        // We need 2 INPUTs (key down + key up) = ~56 bytes minimum
        var buffer = _bytePool.Rent(64);
        return new PooledBuffer<byte>(buffer, _bytePool, clearOnReturn: true);
    }

    /// <summary>
    /// Rents a buffer for general serialization/deserialization.
    /// </summary>
    public PooledBuffer<byte> RentBuffer(int minimumLength)
    {
        var buffer = _bytePool.Rent(minimumLength);
        return new PooledBuffer<byte>(buffer, _bytePool, minimumLength, clearOnReturn: false);
    }

    /// <summary>
    /// Rents a char buffer for string operations.
    /// </summary>
    public PooledBuffer<char> RentCharBuffer(int minimumLength)
    {
        var buffer = _charPool.Rent(minimumLength);
        return new PooledBuffer<char>(buffer, _charPool, minimumLength, clearOnReturn: false);
    }

    /// <summary>
    /// Rents a StringBuilder for log message or JSON formatting.
    /// </summary>
    public PooledStringBuilder RentStringBuilder()
    {
        return new PooledStringBuilder(_stringBuilderPool.Rent(), _stringBuilderPool);
    }

    /// <summary>
    /// Gets pool statistics for diagnostics.
    /// </summary>
    public MemoryPoolStats GetStats()
    {
        // Note: ArrayPool doesn't expose internal stats directly
        // This is a best-effort tracking based on our usage
        return new MemoryPoolStats(
            BytePoolId: _bytePool.GetHashCode(),
            CharPoolId: _charPool.GetHashCode(),
            EstimatedCapacity: 64 * 4096 + 32 * 1024  // buckets x max size
        );
    }

    public void Dispose()
    {
        // ArrayPool doesn't require explicit disposal
        _stringBuilderPool?.Clear();
    }
}

/// <summary>
/// Disposable wrapper for pooled buffers with automatic return.
/// </summary>
public readonly struct PooledBuffer<T> : IDisposable where T : unmanaged
{
    private readonly T[] _buffer;
    private readonly ArrayPool<T> _pool;
    private readonly int _usedLength;
    private readonly bool _clearOnReturn;

    public PooledBuffer(T[] buffer, ArrayPool<T> pool, int usedLength = 0, bool clearOnReturn = false)
    {
        _buffer = buffer;
        _pool = pool;
        _usedLength = usedLength > 0 ? usedLength : buffer.Length;
        _clearOnReturn = clearOnReturn;
    }

    /// <summary>
    /// Gets the rented array segment.
    /// </summary>
    public ArraySegment<T> Segment => new(_buffer, 0, _usedLength);

    /// <summary>
    /// Gets the full rented array (use with caution).
    /// </summary>
    public T[] Array => _buffer;

    /// <summary>
    /// Gets span over the requested length.
    /// </summary>
    public Span<T> Span => _buffer.AsSpan(0, _usedLength);

    /// <summary>
    /// Gets memory over the requested length.
    /// </summary>
    public Memory<T> Memory => _buffer.AsMemory(0, _usedLength);

    /// <summary>
    /// Gets the actual capacity of the rented buffer (may be larger than requested).
    /// </summary>
    public int Capacity => _buffer.Length;

    public void Dispose()
    {
        if (_buffer != null)
        {
            _pool.Return(_buffer, clearArray: _clearOnReturn);
        }
    }
}

/// <summary>
/// Simple object pool for StringBuilder instances.
/// </summary>
public sealed class StringBuilderPool
{
    private readonly StringBuilder?[] _pool;
    private readonly object _lock = new();
    private int _count;

    public StringBuilderPool(int maxPoolSize)
    {
        _pool = new StringBuilder[maxPoolSize];
    }

    public StringBuilder Rent()
    {
        lock (_lock)
        {
            if (_count > 0)
            {
                _count--;
                var builder = _pool[_count];
                _pool[_count] = null;
                if (builder != null)
                {
                    builder.Clear();
                    return builder;
                }
            }
        }
        return new StringBuilder(256);
    }

    public void Return(StringBuilder builder)
    {
        if (builder == null) return;

        lock (_lock)
        {
            if (_count < _pool.Length)
            {
                builder.Clear();
                _pool[_count] = builder;
                _count++;
            }
        }
    }

    public void Clear()
    {
        lock (_lock)
        {
            for (int i = 0; i < _pool.Length; i++)
            {
                _pool[i] = null;
            }
            _count = 0;
        }
    }
}

/// <summary>
/// Disposable wrapper for pooled StringBuilder.
/// </summary>
public readonly struct PooledStringBuilder : IDisposable
{
    private readonly StringBuilder _builder;
    private readonly StringBuilderPool _pool;

    public PooledStringBuilder(StringBuilder builder, StringBuilderPool pool)
    {
        _builder = builder;
        _pool = pool;
    }

    public StringBuilder Builder => _builder;

    public override string ToString() => _builder.ToString();

    public void Dispose()
    {
        _pool?.Return(_builder);
    }
}

/// <summary>
/// Statistics for memory pool diagnostics.
/// </summary>
public sealed record MemoryPoolStats(
    int BytePoolId,
    int CharPoolId,
    long EstimatedCapacity);
