using System;
using System.Collections.Generic;

namespace StackExchange.Redis;

/// <summary>
/// Describes an array entry at a specific index.
/// </summary>
public readonly struct RedisArrayEntry : IEquatable<RedisArrayEntry>
{
    internal readonly RedisArrayIndex index;
    internal readonly RedisValue value;

    internal RedisArrayEntry(RedisArrayIndex index)
    {
        this.index = index;
        value = default;
    }

    /// <summary>
    /// Initializes a <see cref="RedisArrayEntry"/> value.
    /// </summary>
    /// <param name="index">The array index.</param>
    /// <param name="value">The value at this index.</param>
    public RedisArrayEntry(RedisArrayIndex index, RedisValue value)
    {
        this.index = index;
        this.value = value;
    }

    /// <summary>
    /// The array index.
    /// </summary>
    public RedisArrayIndex Index => index;

    /// <summary>
    /// The value at this index.
    /// </summary>
    public RedisValue Value => value;

    /// <summary>
    /// Converts to a key/value pair.
    /// </summary>
    /// <param name="value">The <see cref="RedisArrayEntry"/> to create a <see cref="KeyValuePair{TKey, TValue}"/> from.</param>
    public static implicit operator KeyValuePair<RedisArrayIndex, RedisValue>(RedisArrayEntry value) =>
        new KeyValuePair<RedisArrayIndex, RedisValue>(value.index, value.value);

    /// <summary>
    /// Converts from a key/value pair.
    /// </summary>
    /// <param name="value">The <see cref="KeyValuePair{TKey, TValue}"/> to get a <see cref="RedisArrayEntry"/> from.</param>
    public static implicit operator RedisArrayEntry(KeyValuePair<RedisArrayIndex, RedisValue> value) =>
        new RedisArrayEntry(value.Key, value.Value);

    /// <summary>
    /// The "{index}: {value}" string representation.
    /// </summary>
    public override string ToString() => index + ": " + value;

    /// <inheritdoc />
    public override int GetHashCode() => index.GetHashCode() ^ value.GetHashCode();

    /// <summary>
    /// Compares two values for equality.
    /// </summary>
    /// <param name="obj">The <see cref="RedisArrayEntry"/> to compare to.</param>
    public override bool Equals(object? obj) => obj is RedisArrayEntry entry && Equals(entry);

    /// <summary>
    /// Compares two values for equality.
    /// </summary>
    /// <param name="other">The <see cref="RedisArrayEntry"/> to compare to.</param>
    public bool Equals(RedisArrayEntry other) => index == other.index && value == other.value;

    /// <summary>
    /// Compares two values for equality.
    /// </summary>
    /// <param name="x">The first <see cref="RedisArrayEntry"/> to compare.</param>
    /// <param name="y">The second <see cref="RedisArrayEntry"/> to compare.</param>
    public static bool operator ==(RedisArrayEntry x, RedisArrayEntry y) => x.index == y.index && x.value == y.value;

    /// <summary>
    /// Compares two values for non-equality.
    /// </summary>
    /// <param name="x">The first <see cref="RedisArrayEntry"/> to compare.</param>
    /// <param name="y">The second <see cref="RedisArrayEntry"/> to compare.</param>
    public static bool operator !=(RedisArrayEntry x, RedisArrayEntry y) => x.index != y.index || x.value != y.value;
}
