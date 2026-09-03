using System;

namespace StackExchange.Redis;

/// <summary>
/// Represents a key or value that can be stored in redis.
/// </summary>
public readonly struct RedisKeyOrValue : IEquatable<RedisKeyOrValue>, IEquatable<RedisKey>, IEquatable<RedisValue>
{
    // _keyPrefix is non-null (possibly empty) when this represents a key - it is the key's own prefix
    // bytes, if any; the key's remaining payload (its "KeyValue") sits directly in _value, using
    // RedisValue's own byte[]/string storage rather than a separate copy. _keyPrefix is null when this
    // represents a value (or a genuine null - see IsNull/IsKey/IsValue below, which are deliberately
    // independent checks rather than a single tri-state tag, but remain mutually exclusive in practice).
    private readonly RedisValue _value;
    private readonly byte[]? _keyPrefix;

    /// <summary>
    /// IsNull.
    /// </summary>
    public bool IsNull => _value.IsNull;

    /// <summary>
    /// IsKey.
    /// </summary>
    public bool IsKey => _keyPrefix is not null;

    /// <summary>
    /// Key.
    /// </summary>
    public RedisKey Key => IsKey ? new RedisKey(_keyPrefix, _value.DirectObject) : default;

    /// <summary>
    /// IsValue.
    /// </summary>
    public bool IsValue => _keyPrefix is null && !_value.IsNull;

    /// <summary>
    /// Value.
    /// </summary>
    public RedisValue Value => IsKey ? default : _value;

    // Construction is deliberately funneled through FromKey/FromValue and the implicit operators only,
    // not public constructors: with both RedisKey and RedisValue implicitly constructible from a bare
    // literal (e.g. a string), a public RedisKeyOrValue(RedisKey)/(RedisValue) ctor pair would make
    // `new RedisKeyOrValue("abc")` ambiguous between the two.
    private RedisKeyOrValue(in RedisKey key)
    {
        // an empty (rather than null) prefix still marks this as a key - see IsKey - and RedisKey's own
        // constructor normalizes a zero-length prefix back to null, so nothing is lost by using it here.
        _keyPrefix = key.KeyPrefix ?? Array.Empty<byte>();

        // KeyValue is only ever null/byte[]/string; assign it directly (never via a repacking
        // conversion such as RedisValue.FromRaw) so DirectObject can hand the exact object back later.
        _value = key.KeyValue switch
        {
            null => default,
            byte[] bytes => bytes,
            // .AsRedisValue(), not the bare implicit conversion: this is a deliberate, intentional
            // wrap of the key's own payload, not a string read off the wire (see StringToRedisValue.md).
            string str => str.AsRedisValue(),
            _ => throw new ArgumentException("Unrecognized key type", nameof(key)),
        };
    }

    private RedisKeyOrValue(in RedisValue value)
    {
        _keyPrefix = null;
        _value = value;
    }

    /// <inheritdoc/>
    public override int GetHashCode()
    {
        if (IsKey) return Key.GetHashCode();
        if (IsValue) return _value.GetHashCode();
        return 0;
    }

    /// <inheritdoc/>
    public override bool Equals(object? obj) => obj switch
    {
        RedisKeyOrValue other => Equals(other),
        RedisKey key => IsKey && Key.Equals(key),
        RedisValue value => IsValue && _value.Equals(value),
        _ => false,
    };

    /// <inheritdoc/>
    public override string ToString()
    {
        if (IsKey) return Key.ToString();
        if (IsValue) return _value.ToString();
        return "(null)";
    }

    /// <inheritdoc/>
    public bool Equals(RedisKeyOrValue other)
    {
        if (IsKey) return other.IsKey && Key.Equals(other.Key);
        if (IsValue) return other.IsValue && _value.Equals(other._value);
        return other is { IsKey: false, IsNull: true }; // both a genuine null, not merely a null-valued key
    }

    /// <inheritdoc/>
    public bool Equals(RedisKey other) => IsKey && Key.Equals(other);

    /// <inheritdoc/>
    public bool Equals(RedisValue other) => IsValue && _value.Equals(other);

    /// <summary>Create a new instance representing a key.</summary>
    /// <param name="key">key.</param>
    public static RedisKeyOrValue FromKey(in RedisKey key) => new(in key);

    /// <summary>Create a new instance representing a value.</summary>
    /// <param name="value">value.</param>
    public static RedisKeyOrValue FromValue(in RedisValue value) => new(in value);

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
    public static bool operator !=(RedisKeyOrValue x, RedisKeyOrValue y) => !x.Equals(y);

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
        if (!value.IsKey) ThrowInvalidCast(value);
        return value.Key;
    }

    /// <summary>Obtains the underlying payload as a value.</summary>
    /// <param name="value">value.</param>
    public static explicit operator RedisValue(RedisKeyOrValue value)
    {
        if (!value.IsValue) ThrowInvalidCast(value);
        return value._value;
    }

    private static void ThrowInvalidCast(in RedisKeyOrValue value) =>
        throw new InvalidCastException($"Operation not valid on {(value.IsKey ? "Key" : value.IsValue ? "Value" : "Null")} value.");
}
