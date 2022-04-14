namespace StackExchange.Redis;

/// <summary>
/// A contiguous portion of a sorted set
/// </summary>
public readonly struct SortedSetSpan
{
    /// <summary>
    /// The key of the sorted set.
    /// </summary>
    public RedisKey Key { get; }

    /// <summary>
    /// The provided entries of the sorted set.
    /// </summary>
    public SortedSetEntry[] Entries { get; }

    internal SortedSetSpan(RedisKey key, SortedSetEntry[] entries)
    {
        Key = key;
        Entries = entries;
    }
}
