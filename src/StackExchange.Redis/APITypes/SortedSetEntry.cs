using System;
using System.Collections.Generic;
using System.ComponentModel;

namespace StackExchange.Redis;

/// <summary>
/// Describes a sorted-set element with the corresponding value.
/// </summary>
public readonly struct SortedSetEntry : IEquatable<SortedSetEntry>, IComparable, IComparable<SortedSetEntry>
{
    internal readonly RedisValue element;
    internal readonly double score;

    /// <summary>
    /// Initializes a <see cref="SortedSetEntry"/> value.
    /// </summary>
    /// <param name="element">The <see cref="RedisValue"/> to get an entry for.</param>
    /// <param name="score">The redis score for <paramref name="element"/>.</param>
    public SortedSetEntry(RedisValue element, double score)
    {
        this.element = element;
        this.score = score;
    }

    /// <summary>
    /// The unique element stored in the sorted set.
    /// </summary>
    public RedisValue Element => element;

    /// <summary>
    /// The score against the element.
    /// </summary>
    public double Score => score;

    /// <summary>
    /// The score against the element.
    /// </summary>
    [Browsable(false)]
    [EditorBrowsable(EditorBrowsableState.Never), Obsolete("Please use Score", false)]
    public double Value => score;

    /// <summary>
    /// The unique element stored in the sorted set.
    /// </summary>
    [Browsable(false)]
    [EditorBrowsable(EditorBrowsableState.Never), Obsolete("Please use Element", false)]
    public RedisValue Key => element;

    /// <summary>
    /// Converts to a key/value pair.
    /// </summary>
    /// <param name="value">The <see cref="SortedSetEntry"/> to get a <see cref="KeyValuePair{TKey, TValue}"/> for.</param>
    public static implicit operator KeyValuePair<RedisValue, double>(SortedSetEntry value) => new KeyValuePair<RedisValue, double>(value.element, value.score);

    /// <summary>
    /// Converts from a key/value pair.
    /// </summary>
    /// <param name="value">The  <see cref="KeyValuePair{TKey, TValue}"/> to get a <see cref="SortedSetEntry"/> for.</param>
    public static implicit operator SortedSetEntry(KeyValuePair<RedisValue, double> value) => new SortedSetEntry(value.Key, value.Value);

    /// <summary>
    /// A "{element}: {score}" string representation of the entry.
    /// </summary>
    public override string ToString() => element + ": " + score;

    /// <inheritdoc/>
    public override int GetHashCode() => element.GetHashCode() ^ score.GetHashCode();

    /// <summary>
    /// Compares two values for equality.
    /// </summary>
    /// <param name="obj">The <see cref="SortedSetEntry"/> to compare to.</param>
    public override bool Equals(object? obj) => obj is SortedSetEntry ssObj && Equals(ssObj);

    /// <summary>
    /// Compares two values for equality.
    /// </summary>
    /// <param name="other">The <see cref="SortedSetEntry"/> to compare to.</param>
    public bool Equals(SortedSetEntry other) => score == other.score && element == other.element;

    /// <summary>
    /// Compares two values by score.
    /// </summary>
    /// <param name="other">The <see cref="SortedSetEntry"/> to compare to.</param>
    public int CompareTo(SortedSetEntry other) => score.CompareTo(other.score);

    /// <summary>
    /// Compares two values by score.
    /// </summary>
    /// <param name="obj">The <see cref="SortedSetEntry"/> to compare to.</param>
    public int CompareTo(object? obj) => obj is SortedSetEntry ssObj ? CompareTo(ssObj) : -1;

    /// <summary>
    /// Compares two values for equality.
    /// </summary>
    /// <param name="x">The first <see cref="SortedSetEntry"/> to compare.</param>
    /// <param name="y">The second <see cref="SortedSetEntry"/> to compare.</param>
    public static bool operator ==(SortedSetEntry x, SortedSetEntry y) => x.score == y.score && x.element == y.element;

    /// <summary>
    /// Compares two values for non-equality.
    /// </summary>
    /// <param name="x">The first <see cref="SortedSetEntry"/> to compare.</param>
    /// <param name="y">The second <see cref="SortedSetEntry"/> to compare.</param>
    public static bool operator !=(SortedSetEntry x, SortedSetEntry y) => x.score != y.score || x.element != y.element;
}
