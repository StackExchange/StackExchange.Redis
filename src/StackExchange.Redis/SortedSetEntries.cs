using System;

namespace StackExchange.Redis;

/// <summary>
/// A contiguous portion of a sorted set
/// </summary>
public readonly struct SortedSetEntries
{

    /// <summary>
    /// A null SortedSetSpan
    /// </summary>
    public static SortedSetEntries Null { get; } = new SortedSetEntries(RedisKey.Null, Array.Empty<SortedSetEntry>());

    /// <summary>
    /// Checks if the object is null.
    /// </summary>
    public bool IsNull => Key.IsNull && Entries == Array.Empty<SortedSetEntry>();

    /// <summary>
    /// The key of the sorted set.
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
