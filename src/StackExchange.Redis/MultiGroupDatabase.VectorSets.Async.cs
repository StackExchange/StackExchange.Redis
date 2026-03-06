using System.Threading.Tasks;

namespace StackExchange.Redis;

internal sealed partial class MultiGroupDatabase
{
    // VectorSet Async operations
    public Task<bool> VectorSetAddAsync(RedisKey key, VectorSetAddRequest request, CommandFlags flags = CommandFlags.None)
        => GetActiveDatabase().VectorSetAddAsync(key, request, flags);

    public Task<long> VectorSetLengthAsync(RedisKey key, CommandFlags flags = CommandFlags.None)
        => GetActiveDatabase().VectorSetLengthAsync(key, flags);

    public Task<int> VectorSetDimensionAsync(RedisKey key, CommandFlags flags = CommandFlags.None)
        => GetActiveDatabase().VectorSetDimensionAsync(key, flags);

    public Task<Lease<float>?> VectorSetGetApproximateVectorAsync(RedisKey key, RedisValue member, CommandFlags flags = CommandFlags.None)
        => GetActiveDatabase().VectorSetGetApproximateVectorAsync(key, member, flags);

    public Task<string?> VectorSetGetAttributesJsonAsync(RedisKey key, RedisValue member, CommandFlags flags = CommandFlags.None)
        => GetActiveDatabase().VectorSetGetAttributesJsonAsync(key, member, flags);

    public Task<VectorSetInfo?> VectorSetInfoAsync(RedisKey key, CommandFlags flags = CommandFlags.None)
        => GetActiveDatabase().VectorSetInfoAsync(key, flags);

    public Task<bool> VectorSetContainsAsync(RedisKey key, RedisValue member, CommandFlags flags = CommandFlags.None)
        => GetActiveDatabase().VectorSetContainsAsync(key, member, flags);

    public Task<Lease<RedisValue>?> VectorSetGetLinksAsync(RedisKey key, RedisValue member, CommandFlags flags = CommandFlags.None)
        => GetActiveDatabase().VectorSetGetLinksAsync(key, member, flags);

    public Task<Lease<VectorSetLink>?> VectorSetGetLinksWithScoresAsync(RedisKey key, RedisValue member, CommandFlags flags = CommandFlags.None)
        => GetActiveDatabase().VectorSetGetLinksWithScoresAsync(key, member, flags);

    public Task<RedisValue> VectorSetRandomMemberAsync(RedisKey key, CommandFlags flags = CommandFlags.None)
        => GetActiveDatabase().VectorSetRandomMemberAsync(key, flags);

    public Task<RedisValue[]> VectorSetRandomMembersAsync(RedisKey key, long count, CommandFlags flags = CommandFlags.None)
        => GetActiveDatabase().VectorSetRandomMembersAsync(key, count, flags);

    public Task<bool> VectorSetRemoveAsync(RedisKey key, RedisValue member, CommandFlags flags = CommandFlags.None)
        => GetActiveDatabase().VectorSetRemoveAsync(key, member, flags);

    public Task<bool> VectorSetSetAttributesJsonAsync(RedisKey key, RedisValue member, string attributesJson, CommandFlags flags = CommandFlags.None)
        => GetActiveDatabase().VectorSetSetAttributesJsonAsync(key, member, attributesJson, flags);

    public Task<Lease<VectorSetSimilaritySearchResult>?> VectorSetSimilaritySearchAsync(RedisKey key, VectorSetSimilaritySearchRequest query, CommandFlags flags = CommandFlags.None)
        => GetActiveDatabase().VectorSetSimilaritySearchAsync(key, query, flags);

    public Task<Lease<RedisValue>?> VectorSetRangeAsync(RedisKey key, RedisValue start = default, RedisValue end = default, long count = -1, Exclude exclude = Exclude.None, CommandFlags flags = CommandFlags.None)
        => GetActiveDatabase().VectorSetRangeAsync(key, start, end, count, exclude, flags);

    public System.Collections.Generic.IAsyncEnumerable<RedisValue> VectorSetRangeEnumerateAsync(RedisKey key, RedisValue start = default, RedisValue end = default, long count = 100, Exclude exclude = Exclude.None, CommandFlags flags = CommandFlags.None)
        => GetActiveDatabase().VectorSetRangeEnumerateAsync(key, start, end, count, exclude, flags);
}
