using System;

namespace StackExchange.Redis;

/// <summary>
/// Describes a range of array indices.
/// </summary>
public readonly struct RedisArrayRange : IEquatable<RedisArrayRange>
{
    internal readonly RedisArrayIndex start;
    internal readonly RedisArrayIndex end;

    /// <summary>
    /// Initializes a <see cref="RedisArrayRange"/> value.
    /// </summary>
    /// <param name="start">The start index.</param>
    /// <param name="end">The end index.</param>
    public RedisArrayRange(RedisArrayIndex start, RedisArrayIndex end)
    {
        this.start = start;
        this.end = end;
    }

    /// <summary>
    /// The start index.
    /// </summary>
    public RedisArrayIndex Start => start;

    /// <summary>
    /// The end index.
    /// </summary>
    public RedisArrayIndex End => end;

    /// <summary>
    /// The "{start}..{end}" string representation.
    /// </summary>
    public override string ToString() => start + ".." + end;

    /// <inheritdoc />
    public override int GetHashCode() => start.GetHashCode() ^ end.GetHashCode();

    /// <summary>
    /// Compares two values for equality.
    /// </summary>
    /// <param name="obj">The <see cref="RedisArrayRange"/> to compare to.</param>
    public override bool Equals(object? obj) => obj is RedisArrayRange range && Equals(range);

    /// <summary>
    /// Compares two values for equality.
    /// </summary>
    /// <param name="other">The <see cref="RedisArrayRange"/> to compare to.</param>
    public bool Equals(RedisArrayRange other) => start == other.start && end == other.end;

    /// <summary>
    /// Compares two values for equality.
    /// </summary>
    /// <param name="x">The first <see cref="RedisArrayRange"/> to compare.</param>
    /// <param name="y">The second <see cref="RedisArrayRange"/> to compare.</param>
    public static bool operator ==(RedisArrayRange x, RedisArrayRange y) => x.start == y.start && x.end == y.end;

    /// <summary>
    /// Compares two values for non-equality.
    /// </summary>
    /// <param name="x">The first <see cref="RedisArrayRange"/> to compare.</param>
    /// <param name="y">The second <see cref="RedisArrayRange"/> to compare.</param>
    public static bool operator !=(RedisArrayRange x, RedisArrayRange y) => x.start != y.start || x.end != y.end;
}
