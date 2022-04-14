namespace StackExchange.Redis;

/// <summary>
/// A contiguous portion of a list.
/// </summary>
public readonly struct ListSpan
{
    /// <summary>
    /// The key of the list that this span came form.
    /// </summary>
    public RedisKey Key { get; }


    /// <summary>
    /// The values from the list
    /// </summary>
    public RedisValue[] Values { get; }

    internal ListSpan(RedisKey key, RedisValue[] values)
    {
        Key = key;
        Values = values;
    }
}
