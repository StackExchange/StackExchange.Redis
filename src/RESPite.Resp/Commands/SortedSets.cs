namespace RESPite.Resp.Commands;

/// <summary>
/// Operations relating to sorted sets.
/// </summary>
public static class SortedSets
{
    /// <summary>
    /// Returns the sorted set cardinality (number of elements) of the sorted set stored at key.
    /// </summary>
    public static RespCommand<SimpleString, long> ZCARD { get; } = new(default);
}
