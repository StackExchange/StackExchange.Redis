using System;
using System.Diagnostics.CodeAnalysis;
using RESPite;
using StackExchange.Redis.Protocol;

namespace StackExchange.Redis;

/// <summary>
/// Describes a range of array indices.
/// </summary>
/// <param name="start">The start index.</param>
/// <param name="end">The end index.</param>
public readonly struct RedisArrayRange(RedisArrayIndex start, RedisArrayIndex end) : IEquatable<RedisArrayRange>, IRespArgument
{
    private readonly RedisArrayIndex _start = start;
    private readonly RedisArrayIndex _end = end;

    /// <summary>
    /// The start index.
    /// </summary>
    public RedisArrayIndex Start => _start;

    /// <summary>
    /// The end index.
    /// </summary>
    public RedisArrayIndex End => _end;

    /// <summary>Writes this range as two arguments, start then end.</summary>
    /// <remarks>
    /// Explicit, as <see cref="HashEntry"/> does it: reached only through a command hole, which is the one
    /// place it means anything. Two arguments, so a span of these renders as a run through the handler's
    /// open span hole rather than needing an overload of its own.
    /// </remarks>
    void IRespArgument.WriteTo(scoped ref RespRequestBuilder handler)
    {
        handler.AppendFormatted(_start);
        handler.AppendFormatted(_end);
    }

    /// <summary>
    /// The "{start}..{end}" string representation.
    /// </summary>
    public override string ToString() => _start + ".." + _end;

    /// <inheritdoc />
    public override int GetHashCode() => _start.GetHashCode() ^ _end.GetHashCode();

    /// <summary>
    /// Compares two values for equality.
    /// </summary>
    /// <param name="obj">The <see cref="RedisArrayRange"/> to compare to.</param>
    public override bool Equals(object? obj) => obj is RedisArrayRange range && Equals(range);

    /// <summary>
    /// Compares two values for equality.
    /// </summary>
    /// <param name="other">The <see cref="RedisArrayRange"/> to compare to.</param>
    public bool Equals(RedisArrayRange other) => _start == other._start && _end == other._end;

    /// <summary>
    /// Compares two values for equality.
    /// </summary>
    /// <param name="x">The first <see cref="RedisArrayRange"/> to compare.</param>
    /// <param name="y">The second <see cref="RedisArrayRange"/> to compare.</param>
    public static bool operator ==(RedisArrayRange x, RedisArrayRange y) => x._start == y._start && x._end == y._end;

    /// <summary>
    /// Compares two values for non-equality.
    /// </summary>
    /// <param name="x">The first <see cref="RedisArrayRange"/> to compare.</param>
    /// <param name="y">The second <see cref="RedisArrayRange"/> to compare.</param>
    public static bool operator !=(RedisArrayRange x, RedisArrayRange y) => x._start != y._start || x._end != y._end;
}
