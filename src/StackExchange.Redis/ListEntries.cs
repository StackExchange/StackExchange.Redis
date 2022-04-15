using System;

namespace StackExchange.Redis;

/// <summary>
/// A contiguous portion of a list.
/// </summary>
public readonly struct ListEntries
{
    /// <summary>
    /// A null ListSpan
    /// </summary>
    public static ListEntries Null { get; } = new ListEntries(RedisKey.Null, Array.Empty<RedisValue>());

    /// <summary>
    /// checks if the the object is null
    /// </summary>
    public bool IsNull => Key.IsNull && Values == Array.Empty<RedisValue>();

    /// <summary>
    /// The key of the list that this span came form.
    /// </summary>
    public RedisKey Key { get; }


    /// <summary>
    /// The values from the list
    /// </summary>
    public RedisValue[] Values { get; }

    internal ListEntries(RedisKey key, RedisValue[] values)
    {
        Key = key;
        Values = values;
    }
}
