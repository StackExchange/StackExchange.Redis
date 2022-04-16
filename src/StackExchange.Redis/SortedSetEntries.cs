using System;

namespace StackExchange.Redis;

/// <summary>
/// A contiguous portion of a redis sorted set.
/// </summary>
public readonly struct SortedSetEntries
{
    /// <summary>
    /// A null SortedSetEntries, indicating no results.
    /// </summary>
    public static SortedSetEntries Null { get; } = new SortedSetEntries(RedisKey.Null, Array.Empty<SortedSetEntry>());

    /// <summary>
    /// Whether this object is null/empty.
    /// </summary>
    public bool IsNull => Key.IsNull && Entries == Array.Empty<SortedSetEntry>();

    /// <summary>
    /// The key of the sorted set these entries came form.
    /// </summary>
    public RedisKey Key { get; }

    /// <summary>
    /// The provided entries of the sorted set.
    /// </summary>
    public SortedSetEntry[] Entries { get; }

    internal SortedSetEntries(RedisKey key, SortedSetEntry[] entries)
    {
        Key = key;
        Entries = entries;
    }
}
