using System;
using System.Diagnostics.CodeAnalysis;
using RESPite;

namespace StackExchange.Redis;

/// <summary>
/// Represents an array index or length; conceptually this can be considered a <see cref="ulong"/>,
/// but wrapped for convenience from languages that do not work well with unsigned values.
/// </summary>
/// <param name="value">The array index.</param>
[Experimental(Experiments.Server_8_8, UrlFormat = Experiments.UrlFormat)]
[method: CLSCompliant(false)]
public readonly struct RedisArrayIndex(ulong value) : IEquatable<RedisArrayIndex>
{
    private readonly ulong value = value;

    /// <summary>
    /// The minimum array index value.
    /// </summary>
    public static RedisArrayIndex MinValue => new RedisArrayIndex(0);

    /// <summary>
    /// The maximum array index value.
    /// </summary>
    public static RedisArrayIndex MaxValue => new RedisArrayIndex(ulong.MaxValue);

    /// <summary>
    /// Initializes a <see cref="RedisArrayIndex"/> value.
    /// </summary>
    /// <param name="value">The array index.</param>
    public RedisArrayIndex(int value)
        : this(CheckedNonNegative(value))
    {
    }

    /// <summary>
    /// Initializes a <see cref="RedisArrayIndex"/> value.
    /// </summary>
    /// <param name="value">The array index.</param>
    public RedisArrayIndex(long value)
        : this(CheckedNonNegative(value))
    {
    }

    /// <summary>
    /// The numeric value of this index.
    /// </summary>
    [CLSCompliant(false)]
    public ulong Value => value;

    internal RedisValue ToRedisValue() => value;

    /// <summary>
    /// Converts from an <see cref="int"/>.
    /// </summary>
    /// <param name="value">The array index.</param>
    public static implicit operator RedisArrayIndex(int value) => new RedisArrayIndex(value);

    /// <summary>
    /// Converts from a <see cref="long"/>.
    /// </summary>
    /// <param name="value">The array index.</param>
    public static implicit operator RedisArrayIndex(long value) => new RedisArrayIndex(value);

    /// <summary>
    /// Converts from a <see cref="ulong"/>.
    /// </summary>
    /// <param name="value">The array index.</param>
    [CLSCompliant(false)]
    public static implicit operator RedisArrayIndex(ulong value) => new RedisArrayIndex(value);

    /// <summary>
    /// Converts to an <see cref="int"/>.
    /// </summary>
    /// <param name="value">The array index.</param>
    public static explicit operator int(RedisArrayIndex value) => checked((int)value.value);

    /// <summary>
    /// Converts to a <see cref="long"/>.
    /// </summary>
    /// <param name="value">The array index.</param>
    public static explicit operator long(RedisArrayIndex value) => checked((long)value.value);

    /// <summary>
    /// Converts to a <see cref="ulong"/>.
    /// </summary>
    /// <param name="value">The array index.</param>
    [CLSCompliant(false)]
    public static implicit operator ulong(RedisArrayIndex value) => value.value;

    /// <summary>
    /// The string representation of this array index.
    /// </summary>
    public override string ToString() => value.ToString();

    /// <inheritdoc />
    public override int GetHashCode() => value.GetHashCode();

    /// <summary>
    /// Compares two values for equality.
    /// </summary>
    /// <param name="obj">The <see cref="RedisArrayIndex"/> to compare to.</param>
    public override bool Equals(object? obj) => obj is RedisArrayIndex index && Equals(index);

    /// <summary>
    /// Compares two values for equality.
    /// </summary>
    /// <param name="other">The <see cref="RedisArrayIndex"/> to compare to.</param>
    public bool Equals(RedisArrayIndex other) => value == other.value;

    /// <summary>
    /// Compares two values for equality.
    /// </summary>
    /// <param name="x">The first <see cref="RedisArrayIndex"/> to compare.</param>
    /// <param name="y">The second <see cref="RedisArrayIndex"/> to compare.</param>
    public static bool operator ==(RedisArrayIndex x, RedisArrayIndex y) => x.value == y.value;

    /// <summary>
    /// Compares two values for non-equality.
    /// </summary>
    /// <param name="x">The first <see cref="RedisArrayIndex"/> to compare.</param>
    /// <param name="y">The second <see cref="RedisArrayIndex"/> to compare.</param>
    public static bool operator !=(RedisArrayIndex x, RedisArrayIndex y) => x.value != y.value;

    private static ulong CheckedNonNegative(long value)
    {
        if (value < 0) throw new ArgumentOutOfRangeException(nameof(value), "Array indices must be non-negative.");
        return (ulong)value;
    }
}
