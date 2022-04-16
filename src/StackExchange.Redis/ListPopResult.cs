using System;

namespace StackExchange.Redis;

/// <summary>
/// A contiguous portion of a redis list.
/// </summary>
public readonly struct ListPopResult
{
    /// <summary>
    /// A null ListPopResult, indicating no results.
    /// </summary>
    public static ListPopResult Null { get; } = new ListPopResult(RedisKey.Null, Array.Empty<RedisValue>());

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

    internal ListPopResult(RedisKey key, RedisValue[] values)
    {
        Key = key;
        Values = values;
    }
}
