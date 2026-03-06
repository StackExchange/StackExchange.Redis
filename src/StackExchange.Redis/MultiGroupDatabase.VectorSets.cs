namespace StackExchange.Redis;

internal sealed partial class MultiGroupDatabase
{
    // VectorSet operations
    public bool VectorSetAdd(RedisKey key, VectorSetAddRequest request, CommandFlags flags = CommandFlags.None)
        => GetActiveDatabase().VectorSetAdd(key, request, flags);

    public long VectorSetLength(RedisKey key, CommandFlags flags = CommandFlags.None)
        => GetActiveDatabase().VectorSetLength(key, flags);

    public int VectorSetDimension(RedisKey key, CommandFlags flags = CommandFlags.None)
        => GetActiveDatabase().VectorSetDimension(key, flags);

    public Lease<float>? VectorSetGetApproximateVector(RedisKey key, RedisValue member, CommandFlags flags = CommandFlags.None)
        => GetActiveDatabase().VectorSetGetApproximateVector(key, member, flags);

    public string? VectorSetGetAttributesJson(RedisKey key, RedisValue member, CommandFlags flags = CommandFlags.None)
        => GetActiveDatabase().VectorSetGetAttributesJson(key, member, flags);

    public VectorSetInfo? VectorSetInfo(RedisKey key, CommandFlags flags = CommandFlags.None)
        => GetActiveDatabase().VectorSetInfo(key, flags);

    public bool VectorSetContains(RedisKey key, RedisValue member, CommandFlags flags = CommandFlags.None)
        => GetActiveDatabase().VectorSetContains(key, member, flags);

    public Lease<RedisValue>? VectorSetGetLinks(RedisKey key, RedisValue member, CommandFlags flags = CommandFlags.None)
        => GetActiveDatabase().VectorSetGetLinks(key, member, flags);

    public Lease<VectorSetLink>? VectorSetGetLinksWithScores(RedisKey key, RedisValue member, CommandFlags flags = CommandFlags.None)
        => GetActiveDatabase().VectorSetGetLinksWithScores(key, member, flags);

    public RedisValue VectorSetRandomMember(RedisKey key, CommandFlags flags = CommandFlags.None)
        => GetActiveDatabase().VectorSetRandomMember(key, flags);

    public RedisValue[] VectorSetRandomMembers(RedisKey key, long count, CommandFlags flags = CommandFlags.None)
        => GetActiveDatabase().VectorSetRandomMembers(key, count, flags);

    public bool VectorSetRemove(RedisKey key, RedisValue member, CommandFlags flags = CommandFlags.None)
        => GetActiveDatabase().VectorSetRemove(key, member, flags);

    public bool VectorSetSetAttributesJson(RedisKey key, RedisValue member, string attributesJson, CommandFlags flags = CommandFlags.None)
        => GetActiveDatabase().VectorSetSetAttributesJson(key, member, attributesJson, flags);

    public Lease<VectorSetSimilaritySearchResult>? VectorSetSimilaritySearch(RedisKey key, VectorSetSimilaritySearchRequest query, CommandFlags flags = CommandFlags.None)
        => GetActiveDatabase().VectorSetSimilaritySearch(key, query, flags);

    public Lease<RedisValue> VectorSetRange(RedisKey key, RedisValue start = default, RedisValue end = default, long count = -1, Exclude exclude = Exclude.None, CommandFlags flags = CommandFlags.None)
        => GetActiveDatabase().VectorSetRange(key, start, end, count, exclude, flags);

    public System.Collections.Generic.IEnumerable<RedisValue> VectorSetRangeEnumerate(RedisKey key, RedisValue start = default, RedisValue end = default, long count = 100, Exclude exclude = Exclude.None, CommandFlags flags = CommandFlags.None)
        => GetActiveDatabase().VectorSetRangeEnumerate(key, start, end, count, exclude, flags);
}
