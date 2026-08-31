using System;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace StackExchange.Redis;

/// <summary>
/// Represents a key or value that can be stored in redis.
/// </summary>
[StructLayout(LayoutKind.Explicit)]
public readonly struct RedisKeyOrValue : IEquatable<RedisKeyOrValue>, IEquatable<RedisKey>, IEquatable<RedisValue>
{
    private enum StorageType
    {
        Null,
        Key,
        Value,
    }

#pragma warning disable SA1134
    [FieldOffset(0)] private readonly int _index;
    [FieldOffset(4)] private readonly int _length;
    [FieldOffset(8)] private readonly object? _obj;
#pragma warning restore SA1134

    private StorageType Type
    {
        get
        {
            var obj = _obj;
            if (obj == null) return StorageType.Null;
            if ((obj is byte[] || obj is string) && _index < 0) return StorageType.Key;
            return StorageType.Value;
        }
    }

    private RedisKey UnsafeKey
    {
        get
        {
            Debug.Assert(IsKey);

            return new RedisKey(null, _obj);
        }
    }

    private RedisValue UnsafeValue
    {
        get
        {
            Debug.Assert(IsValue);

            var copy = this;
            return Unsafe.As<RedisKeyOrValue, RedisValue>(ref copy);
        }
    }

    /// <summary>
    /// IsNull.
    /// </summary>
    public bool IsNull => _obj is null;

    /// <summary>
    /// IsKey.
    /// </summary>
    public bool IsKey => Type == StorageType.Key;

    /// <summary>
    /// Key.
    /// </summary>
    public RedisKey Key => Type == StorageType.Key ? new RedisKey(null, _obj) : default;

    /// <summary>
    /// IsValue.
    /// </summary>
    public bool IsValue => Type == StorageType.Value;

    /// <summary>
    /// Value.
    /// </summary>
    public RedisValue Value
    {
        get
        {
            if (Type != StorageType.Value) return default;

            var copy = this;
            return Unsafe.As<RedisKeyOrValue, RedisValue>(ref copy);
        }
    }

    /// <summary>
    /// Key.
    /// </summary>
    /// <param name="key">key.</param>
    public RedisKeyOrValue(in RedisKey key)
    {
        var keyValue = key.KeyValue;
        var keyPrefix = key.KeyPrefix;
        if (keyPrefix != null)
        {
            if (keyValue != null)
                keyPrefix = (byte[]?)key ?? throw new InvalidOperationException("keyPrefix is null");

            _obj = keyPrefix;
            _index = -1;
            _length = keyPrefix.Length;
        }
        else if (keyValue == null)
        {
            this = default;
        }
        else if (keyValue is byte[] bytes)
        {
            _obj = bytes;
            _index = -1;
            _length = bytes.Length;
        }
        else if (keyValue is string str)
        {
            _obj = str;
            _index = -1;
            _length = str.Length;
        }
        else
        {
            throw new ArgumentException("Unrecognized key type", nameof(key));
        }
    }

    /// <summary>
    /// Value.
    /// </summary>
    /// <param name="value">value.</param>
    public RedisKeyOrValue(in RedisValue value)
    {
        var copy = value;
        this = Unsafe.As<RedisValue, RedisKeyOrValue>(ref copy);
    }

    /// <inheritdoc/>
    public override int GetHashCode() => Type switch
    {
        StorageType.Key => UnsafeKey.GetHashCode(),
        StorageType.Value => UnsafeValue.GetHashCode(),
        _ => 0,
    };

    /// <inheritdoc/>
    public override bool Equals(object? obj) => obj switch
    {
        RedisKeyOrValue other => Equals(other),
        RedisKey key => Type == StorageType.Key && UnsafeKey.Equals(key),
        RedisValue value => Type == StorageType.Value && UnsafeValue.Equals(value),
        _ => false,
    };

    /// <inheritdoc/>
    public override string ToString() => Type switch
    {
        StorageType.Key => UnsafeKey.ToString(),
        StorageType.Value => UnsafeValue.ToString(),
        _ => "(null)",
    };

    /// <inheritdoc/>
    public bool Equals(RedisKeyOrValue other) => Type switch
    {
        StorageType.Key => other.Type == StorageType.Key && UnsafeKey.Equals(other.UnsafeKey),
        StorageType.Value => other.Type == StorageType.Value && UnsafeValue.Equals(other.UnsafeValue),
        _ => other.Type == Type,
    };

    /// <inheritdoc/>
    public bool Equals(RedisKey other) => Type == StorageType.Key && UnsafeKey.Equals(other);

    /// <inheritdoc/>
    public bool Equals(RedisValue other) => Type == StorageType.Value && UnsafeValue.Equals(other);

    /// <summary>Create a new instance representing a key.</summary>
    /// <param name="key">key.</param>
    public static RedisKeyOrValue FromKey(RedisKey key) => new RedisKeyOrValue(in key);

    /// <summary>Create a new instance representing a value.</summary>
    /// <param name="value">value.</param>
    public static RedisKeyOrValue FromValue(RedisValue value) => new RedisKeyOrValue(in value);

    /// <summary>
    /// Compares two values for equality.
    /// </summary>
    /// <param name="x">The first keyOrValue to compare.</param>
    /// <param name="y">The second keyOrValue to compare.</param>
    public static bool operator ==(RedisKeyOrValue x, RedisKeyOrValue y) => x.Equals(y);

    /// <summary>
    /// Compares two values for non-equality.
    /// </summary>
    /// <param name="x">The first keyOrValue to compare.</param>
    /// <param name="y">The second keyOrValue to compare.</param>
    public static bool operator !=(RedisKeyOrValue x, RedisKeyOrValue y) => x.Equals(y);

    /// <summary>Create a new instance representing a key.</summary>
    /// <param name="key">key.</param>
    public static implicit operator RedisKeyOrValue(RedisKey key) => new RedisKeyOrValue(in key);

    /// <summary>Create a new instance representing a value.</summary>
    /// <param name="value">value.</param>
    public static implicit operator RedisKeyOrValue(RedisValue value) => new RedisKeyOrValue(in value);

    /// <summary>Obtains the underlying payload as a key.</summary>
    /// <param name="value">value.</param>
    public static explicit operator RedisKey(RedisKeyOrValue value)
    {
        if (value.Type != StorageType.Key)
            ThrowInvalidCast(value.Type);

        return value.UnsafeKey;
    }

    /// <summary>Obtains the underlying payload as a value.</summary>
    /// <param name="value">value.</param>
    public static explicit operator RedisValue(RedisKeyOrValue value)
    {
        if (value.Type != StorageType.Value)
            ThrowInvalidCast(value.Type);

        return value.UnsafeValue;
    }

    private static void ThrowInvalidCast(StorageType type) => throw new InvalidCastException($"Operation not valid on {type} value.");
}
