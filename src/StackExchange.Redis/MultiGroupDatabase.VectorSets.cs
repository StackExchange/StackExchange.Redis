namespace StackExchange.Redis;

internal sealed partial class MultiGroupDatabase
{
    // VectorSet operations
    public bool VectorSetAdd(RedisKey key, VectorSetAddRequest request, CommandFlags flags = CommandFlags.None)
        => GetDatabase().VectorSetAdd(key, request, flags);

    public long VectorSetLength(RedisKey key, CommandFlags flags = CommandFlags.None)
        => GetDatabase().VectorSetLength(key, flags);

    public int VectorSetDimension(RedisKey key, CommandFlags flags = CommandFlags.None)
        => GetDatabase().VectorSetDimension(key, flags);

    public Lease<float>? VectorSetGetApproximateVector(RedisKey key, RedisValue member, CommandFlags flags = CommandFlags.None)
        => GetDatabase().VectorSetGetApproximateVector(key, member, flags);

    public string? VectorSetGetAttributesJson(RedisKey key, RedisValue member, CommandFlags flags = CommandFlags.None)
        => GetDatabase().VectorSetGetAttributesJson(key, member, flags);

    public VectorSetInfo? VectorSetInfo(RedisKey key, CommandFlags flags = CommandFlags.None)
        => GetDatabase().VectorSetInfo(key, flags);

    public bool VectorSetContains(RedisKey key, RedisValue member, CommandFlags flags = CommandFlags.None)
        => GetDatabase().VectorSetContains(key, member, flags);

    public Lease<RedisValue>? VectorSetGetLinks(RedisKey key, RedisValue member, CommandFlags flags = CommandFlags.None)
        => GetDatabase().VectorSetGetLinks(key, member, flags);

    public Lease<VectorSetLink>? VectorSetGetLinksWithScores(RedisKey key, RedisValue member, CommandFlags flags = CommandFlags.None)
        => GetDatabase().VectorSetGetLinksWithScores(key, member, flags);

    public RedisValue VectorSetRandomMember(RedisKey key, CommandFlags flags = CommandFlags.None)
        => GetDatabase().VectorSetRandomMember(key, flags);

    public RedisValue[] VectorSetRandomMembers(RedisKey key, long count, CommandFlags flags = CommandFlags.None)
        => GetDatabase().VectorSetRandomMembers(key, count, flags);

    public bool VectorSetRemove(RedisKey key, RedisValue member, CommandFlags flags = CommandFlags.None)
        => GetDatabase().VectorSetRemove(key, member, flags);

    public bool VectorSetSetAttributesJson(RedisKey key, RedisValue member, string attributesJson, CommandFlags flags = CommandFlags.None)
        => GetDatabase().VectorSetSetAttributesJson(key, member, attributesJson, flags);

    public Lease<VectorSetSimilaritySearchResult>? VectorSetSimilaritySearch(RedisKey key, VectorSetSimilaritySearchRequest query, CommandFlags flags = CommandFlags.None)
        => GetDatabase().VectorSetSimilaritySearch(key, query, flags);
}
