using System;
using System.Runtime.InteropServices;

namespace StackExchange.Redis;

/// <summary>
/// Represents a placeholder that could represent a redis key or a redis value
/// </summary>
public readonly struct RedisKeyOrValue : IEquatable<RedisKeyOrValue>, IEquatable<RedisKey>, IEquatable<RedisValue>
{
    private enum Mode
    {
        Empty,
        Key,
        Value,
    }

    private readonly Mode _mode;
    private readonly object? _objectOrSentinel;
    private readonly ReadOnlyMemory<byte> _memory;
    private readonly long _overlappedBits64;

    private RedisKeyOrValue(in RedisKey key)
    {
        _mode = Mode.Key;
        _overlappedBits64 = 0;
        _memory = key.KeyPrefix;
        _objectOrSentinel = key.KeyValue;
    }

    private RedisKeyOrValue(in RedisValue value)
    {
        _mode = Mode.Value;
        _overlappedBits64 = value.DirectOverlappedBits64;
        _memory = value.DirectMemory;
        _objectOrSentinel = value.DirectObject;
    }

    /// <inheritdoc/>
    public override int GetHashCode()
        => _mode switch
        {
            Mode.Key => AsKey().GetHashCode(),
            Mode.Value => AsValue().GetHashCode(),
            _ => 0,
        };

    /// <inheritdoc/>
    public override bool Equals(object? obj) => obj switch
    {
        RedisKeyOrValue other => Equals(other),
        RedisKey key => _mode == Mode.Key && AsKey().Equals(key),
        RedisValue value => _mode == Mode.Value && AsValue().Equals(value),
        _ => false,
    };

    /// <inheritdoc/>
    public override string ToString() => _mode switch
    {
        Mode.Key => AsKey().ToString(),
        Mode.Value => AsValue().ToString(),
        _ => _mode.ToString(),
    };

    /// <summary>Create a new instance representing a key</summary>
    public static RedisKeyOrValue Key(RedisKey key) => new RedisKeyOrValue(in key);

    /// <summary>Create a new instance representing a value</summary>
    public static RedisKeyOrValue Value(RedisValue value) => new RedisKeyOrValue(in value);

    /// <summary>Create a new instance representing a key</summary>
    public static implicit operator RedisKeyOrValue(RedisKey key) => new RedisKeyOrValue(in key);

    /// <summary>Create a new instance representing a value</summary>
    public static implicit operator RedisKeyOrValue(RedisValue value) => new RedisKeyOrValue(in value);

    /// <summary>Obtains the underlying payload as a key</summary>
    public static explicit operator RedisKey(RedisKeyOrValue value) => value.AsKey();

    /// <summary>Obtains the underlying payload as a value</summary>
    public static explicit operator RedisValue(RedisKeyOrValue value) => value.AsValue();

    /// <summary>Indicates whether this instance represents a key</summary>
    public bool IsKey => _mode == Mode.Key;

    /// <summary>Indicates whether this instance represents a value</summary>
    public bool IsValue => _mode == Mode.Value;

    /// <summary>Obtains the underlying payload as a key</summary>
    public RedisKey AsKey()
    {
        AssertMode(Mode.Key);
        byte[]? keyPrefix = null;
        if (MemoryMarshal.TryGetArray(_memory, out var segment) && segment.Array is not null && segment.Offset == 0 && segment.Count == segment.Array.Length)
        {
            keyPrefix = segment.Array;
        }
        return new RedisKey(keyPrefix, _objectOrSentinel);
    }

    /// <summary>Obtains the underlying payload as a value</summary>
    public RedisValue AsValue()
    {
        AssertMode(Mode.Value);
        return new RedisValue(_overlappedBits64, _memory, _objectOrSentinel);
    }

    private void AssertMode(Mode mode)
    {
        if (mode != _mode) Throw(_mode);
        static void Throw(Mode mode) => throw new InvalidOperationException($"Operation not valid on {mode} value");
    }

    /// <inheritdoc/>
    public bool Equals(RedisKeyOrValue other) => _mode switch
    {
        Mode.Key => other._mode == Mode.Key && AsKey().Equals(other.AsKey()),
        Mode.Value => other._mode == Mode.Value && AsValue().Equals(other.AsValue()),
        _ => other._mode == _mode,
    };

    /// <inheritdoc/>
    public bool Equals(RedisKey other) => _mode == Mode.Key && AsKey().Equals(other);

    /// <inheritdoc/>
    public bool Equals(RedisValue other) => _mode == Mode.Value && AsValue().Equals(other);
}
