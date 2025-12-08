using System.Threading.Tasks;

namespace StackExchange.Redis;

internal sealed partial class MultiGroupDatabase
{
    // VectorSet Async operations
    public Task<bool> VectorSetAddAsync(RedisKey key, VectorSetAddRequest request, CommandFlags flags = CommandFlags.None)
        => GetDatabase().VectorSetAddAsync(key, request, flags);

    public Task<long> VectorSetLengthAsync(RedisKey key, CommandFlags flags = CommandFlags.None)
        => GetDatabase().VectorSetLengthAsync(key, flags);

    public Task<int> VectorSetDimensionAsync(RedisKey key, CommandFlags flags = CommandFlags.None)
        => GetDatabase().VectorSetDimensionAsync(key, flags);

    public Task<Lease<float>?> VectorSetGetApproximateVectorAsync(RedisKey key, RedisValue member, CommandFlags flags = CommandFlags.None)
        => GetDatabase().VectorSetGetApproximateVectorAsync(key, member, flags);

    public Task<string?> VectorSetGetAttributesJsonAsync(RedisKey key, RedisValue member, CommandFlags flags = CommandFlags.None)
        => GetDatabase().VectorSetGetAttributesJsonAsync(key, member, flags);

    public Task<VectorSetInfo?> VectorSetInfoAsync(RedisKey key, CommandFlags flags = CommandFlags.None)
        => GetDatabase().VectorSetInfoAsync(key, flags);

    public Task<bool> VectorSetContainsAsync(RedisKey key, RedisValue member, CommandFlags flags = CommandFlags.None)
        => GetDatabase().VectorSetContainsAsync(key, member, flags);

    public Task<Lease<RedisValue>?> VectorSetGetLinksAsync(RedisKey key, RedisValue member, CommandFlags flags = CommandFlags.None)
        => GetDatabase().VectorSetGetLinksAsync(key, member, flags);

    public Task<Lease<VectorSetLink>?> VectorSetGetLinksWithScoresAsync(RedisKey key, RedisValue member, CommandFlags flags = CommandFlags.None)
        => GetDatabase().VectorSetGetLinksWithScoresAsync(key, member, flags);

    public Task<RedisValue> VectorSetRandomMemberAsync(RedisKey key, CommandFlags flags = CommandFlags.None)
        => GetDatabase().VectorSetRandomMemberAsync(key, flags);

    public Task<RedisValue[]> VectorSetRandomMembersAsync(RedisKey key, long count, CommandFlags flags = CommandFlags.None)
        => GetDatabase().VectorSetRandomMembersAsync(key, count, flags);

    public Task<bool> VectorSetRemoveAsync(RedisKey key, RedisValue member, CommandFlags flags = CommandFlags.None)
        => GetDatabase().VectorSetRemoveAsync(key, member, flags);

    public Task<bool> VectorSetSetAttributesJsonAsync(RedisKey key, RedisValue member, string attributesJson, CommandFlags flags = CommandFlags.None)
        => GetDatabase().VectorSetSetAttributesJsonAsync(key, member, attributesJson, flags);

    public Task<Lease<VectorSetSimilaritySearchResult>?> VectorSetSimilaritySearchAsync(RedisKey key, VectorSetSimilaritySearchRequest query, CommandFlags flags = CommandFlags.None)
        => GetDatabase().VectorSetSimilaritySearchAsync(key, query, flags);
}
