using System;
using System.Collections.Generic;
using StackExchange.Redis.Protocol;

namespace StackExchange.Redis;

/// <summary>
/// Describes a value contained in a stream (a name/value pair).
/// </summary>
public readonly struct NameValueEntry : IEquatable<NameValueEntry>, IRespArgument
{
    internal readonly RedisValue name, value;

    /// <summary>
    /// Initializes a <see cref="NameValueEntry"/> value.
    /// </summary>
    /// <param name="name">The name for this entry.</param>
    /// <param name="value">The value for this entry.</param>
    public NameValueEntry(RedisValue name, RedisValue value)
    {
        this.name = name;
        this.value = value;
    }

    /// <summary>
    /// The name of the field.
    /// </summary>
    public RedisValue Name => name;

    /// <summary>
    /// The value of the field.
    /// </summary>
    public RedisValue Value => value;

    /// <summary>
    /// Converts to a key/value pair.
    /// </summary>
    /// <param name="value">The <see cref="NameValueEntry"/> to create a <see cref="KeyValuePair{TKey, TValue}"/> from.</param>
    public static implicit operator KeyValuePair<RedisValue, RedisValue>(NameValueEntry value) =>
        new KeyValuePair<RedisValue, RedisValue>(value.name, value.value);

    /// <summary>
    /// Converts from a key/value pair.
    /// </summary>
    /// <param name="value">The <see cref="KeyValuePair{TKey, TValue}"/> to get a <see cref="NameValueEntry"/> from.</param>
    public static implicit operator NameValueEntry(KeyValuePair<RedisValue, RedisValue> value) =>
        new NameValueEntry(value.Key, value.Value);

    /// <summary>
    /// The "{name}: {value}" string representation.
    /// </summary>
    public override string ToString() => name + ": " + value;

    /// <inheritdoc />
    public override int GetHashCode() => name.GetHashCode() ^ value.GetHashCode();

    /// <summary>
    /// Compares two values for equality.
    /// </summary>
    /// <param name="obj">The <see cref="NameValueEntry"/> to compare to.</param>
    public override bool Equals(object? obj) => obj is NameValueEntry heObj && Equals(heObj);

    /// <summary>
    /// Compares two values for equality.
    /// </summary>
    /// <param name="other">The <see cref="NameValueEntry"/> to compare to.</param>
    public bool Equals(NameValueEntry other) => name == other.name && value == other.value;

    /// <summary>
    /// Compares two values for equality.
    /// </summary>
    /// <param name="x">The first <see cref="NameValueEntry"/> to compare.</param>
    /// <param name="y">The second <see cref="NameValueEntry"/> to compare.</param>
    public static bool operator ==(NameValueEntry x, NameValueEntry y) => x.name == y.name && x.value == y.value;

    /// <summary>
    /// Compares two values for non-equality.
    /// </summary>
    /// <param name="x">The first <see cref="NameValueEntry"/> to compare.</param>
    /// <param name="y">The second <see cref="NameValueEntry"/> to compare.</param>
    public static bool operator !=(NameValueEntry x, NameValueEntry y) => x.name != y.name || x.value != y.value;

    /// <summary>Write this pair as two consecutive arguments: the name, then the value.</summary>
    /// <remarks>
    /// <b>Explicit, so the public surface does not grow.</b> What it buys is the unroll -
    /// <c>$"{RedisCommand.XADD}{key}{fields}"</c> writes a whole run through the existing
    /// <c>AppendFormatted&lt;T&gt;(ReadOnlySpan&lt;T&gt;) where T : IRespArgument</c>, instead of every
    /// caller looping and spelling out "name, then value" again. Same move as
    /// <see cref="RedisKeyOrValue"/>, for the same reason: the type knows its own shape, so the decision
    /// is made once here rather than at each call site.
    /// </remarks>
    void IRespArgument.WriteTo(scoped ref RespRequestBuilder handler)
    {
        handler.AppendFormatted(name);
        handler.AppendFormatted(value);
    }
}
