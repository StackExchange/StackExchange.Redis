using System;

namespace StackExchange.Redis;

/// <summary>
/// A contiguous portion of a redis list.
/// </summary>
public readonly struct ListEntries
{
    /// <summary>
    /// A null ListEntries, indicating no results.
    /// </summary>
    public static ListEntries Null { get; } = new ListEntries(RedisKey.Null, Array.Empty<RedisValue>());

    /// <summary>
    /// Whether this object is null/empty.
    /// </summary>
    public bool IsNull => Key.IsNull && Values == Array.Empty<RedisValue>();

    /// <summary>
    /// The key of the list that this set of entries came form.
    /// </summary>
    public RedisKey Key { get; }

    /// <summary>
    /// The values from the list.
    /// </summary>
    public RedisValue[] Values { get; }

    internal ListEntries(RedisKey key, RedisValue[] values)
    {
        Key = key;
        Values = values;
    }
}
