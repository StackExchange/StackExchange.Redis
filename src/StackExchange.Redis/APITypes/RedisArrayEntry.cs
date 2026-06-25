using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using RESPite;

namespace StackExchange.Redis;

/// <summary>
/// Describes an array entry at a specific index.
/// </summary>
/// <param name="index">The array index.</param>
/// <param name="value">The value at this index.</param>
[Experimental(Experiments.Server_8_8, UrlFormat = Experiments.UrlFormat)]
public readonly struct RedisArrayEntry(RedisArrayIndex index, RedisValue value) : IEquatable<RedisArrayEntry>
{
    private readonly RedisArrayIndex _index = index;
    private readonly RedisValue _value = value;

    internal RedisArrayEntry(RedisArrayIndex index)
        : this(index, default)
    {
    }

    /// <summary>
    /// The array index.
    /// </summary>
    public RedisArrayIndex Index => _index;

    /// <summary>
    /// The value at this index.
    /// </summary>
    public RedisValue Value => _value;

    /// <summary>
    /// Converts to a key/value pair.
    /// </summary>
    /// <param name="value">The <see cref="RedisArrayEntry"/> to create a <see cref="KeyValuePair{TKey, TValue}"/> from.</param>
    public static implicit operator KeyValuePair<RedisArrayIndex, RedisValue>(RedisArrayEntry value) =>
        new KeyValuePair<RedisArrayIndex, RedisValue>(value._index, value._value);

    /// <summary>
    /// Converts from a key/value pair.
    /// </summary>
    /// <param name="value">The <see cref="KeyValuePair{TKey, TValue}"/> to get a <see cref="RedisArrayEntry"/> from.</param>
    public static implicit operator RedisArrayEntry(KeyValuePair<RedisArrayIndex, RedisValue> value) =>
        new RedisArrayEntry(value.Key, value.Value);

    /// <summary>
    /// The "{index}: {value}" string representation.
    /// </summary>
    public override string ToString() => _index + ": " + _value;

    /// <inheritdoc />
    public override int GetHashCode() => _index.GetHashCode() ^ _value.GetHashCode();

    /// <summary>
    /// Compares two values for equality.
    /// </summary>
    /// <param name="obj">The <see cref="RedisArrayEntry"/> to compare to.</param>
    public override bool Equals(object? obj) => obj is RedisArrayEntry entry && Equals(entry);

    /// <summary>
    /// Compares two values for equality.
    /// </summary>
    /// <param name="other">The <see cref="RedisArrayEntry"/> to compare to.</param>
    public bool Equals(RedisArrayEntry other) => _index == other._index && _value == other._value;

    /// <summary>
    /// Compares two values for equality.
    /// </summary>
    /// <param name="x">The first <see cref="RedisArrayEntry"/> to compare.</param>
    /// <param name="y">The second <see cref="RedisArrayEntry"/> to compare.</param>
    public static bool operator ==(RedisArrayEntry x, RedisArrayEntry y) => x._index == y._index && x._value == y._value;

    /// <summary>
    /// Compares two values for non-equality.
    /// </summary>
    /// <param name="x">The first <see cref="RedisArrayEntry"/> to compare.</param>
    /// <param name="y">The second <see cref="RedisArrayEntry"/> to compare.</param>
    public static bool operator !=(RedisArrayEntry x, RedisArrayEntry y) => x._index != y._index || x._value != y._value;
}
