namespace StackExchange.Redis;

/// <summary>
/// Enum to manage ordering in sorted sets.
/// </summary>
public enum SortedSetOrder
{
    /// <summary>
    /// Bases ordering off of the rank in the sorted set. This means that your start and stop inside the sorted set will be some offset into the set.
    /// </summary>
    ByRank,

    /// <summary>
    /// Bases ordering off of the score in the sorted set. This means your start/stop will be some number which is the score for each member in the sorted set.
    /// </summary>
    ByScore,

    /// <summary>
    /// Bases ordering off of lexicographical order, this is only appropriate in an instance where all the members of your sorted set are given the same score
    /// </summary>
    ByLex,
}

internal static class SortedSetOrderByExtensions
{
    internal static RedisValue GetLiteral(this SortedSetOrder sortedSetOrder) => sortedSetOrder switch
    {
        SortedSetOrder.ByLex => RedisLiterals.BYLEX,
        SortedSetOrder.ByScore => RedisLiterals.BYSCORE,
        _ => RedisValue.Null
    };
}
