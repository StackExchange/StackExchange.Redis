using System;
using System.Diagnostics.CodeAnalysis;
using System.Threading.Tasks;

// ReSharper disable once CheckNamespace
namespace StackExchange.Redis.KeyspaceIsolation;

internal partial class KeyPrefixed<TInner>
{
    // Vector Set operations - async methods
    [Experimental(Experiments.VectorSets, UrlFormat = Experiments.UrlFormat)]
    public Task<bool> VectorSetAddAsync(
        RedisKey key,
        VectorSetAddRequest request,
        CommandFlags flags = CommandFlags.None) =>
        Inner.VectorSetAddAsync(ToInner(key), request, flags);

    public Task<long> VectorSetLengthAsync(RedisKey key, CommandFlags flags = CommandFlags.None) =>
        Inner.VectorSetLengthAsync(ToInner(key), flags);

    public Task<int> VectorSetDimensionAsync(RedisKey key, CommandFlags flags = CommandFlags.None) =>
        Inner.VectorSetDimensionAsync(ToInner(key), flags);

    public Task<Lease<float>?> VectorSetGetApproximateVectorAsync(RedisKey key, RedisValue member, CommandFlags flags = CommandFlags.None) =>
        Inner.VectorSetGetApproximateVectorAsync(ToInner(key), member, flags);

    public Task<string?> VectorSetGetAttributesJsonAsync(RedisKey key, RedisValue member, CommandFlags flags = CommandFlags.None) =>
        Inner.VectorSetGetAttributesJsonAsync(ToInner(key), member, flags);

    public Task<VectorSetInfo?> VectorSetInfoAsync(RedisKey key, CommandFlags flags = CommandFlags.None) =>
        Inner.VectorSetInfoAsync(ToInner(key), flags);

    public Task<bool> VectorSetContainsAsync(RedisKey key, RedisValue member, CommandFlags flags = CommandFlags.None) =>
        Inner.VectorSetContainsAsync(ToInner(key), member, flags);

    public Task<Lease<RedisValue>?> VectorSetGetLinksAsync(RedisKey key, RedisValue member, CommandFlags flags = CommandFlags.None) =>
        Inner.VectorSetGetLinksAsync(ToInner(key), member, flags);

    public Task<Lease<VectorSetLink>?> VectorSetGetLinksWithScoresAsync(RedisKey key, RedisValue member, CommandFlags flags = CommandFlags.None) =>
        Inner.VectorSetGetLinksWithScoresAsync(ToInner(key), member, flags);

    public Task<RedisValue> VectorSetRandomMemberAsync(RedisKey key, CommandFlags flags = CommandFlags.None) =>
        Inner.VectorSetRandomMemberAsync(ToInner(key), flags);

    public Task<RedisValue[]> VectorSetRandomMembersAsync(RedisKey key, long count, CommandFlags flags = CommandFlags.None) =>
        Inner.VectorSetRandomMembersAsync(ToInner(key), count, flags);

    public Task<bool> VectorSetRemoveAsync(RedisKey key, RedisValue member, CommandFlags flags = CommandFlags.None) =>
        Inner.VectorSetRemoveAsync(ToInner(key), member, flags);

    public Task<bool> VectorSetSetAttributesJsonAsync(RedisKey key, RedisValue member, string attributesJson, CommandFlags flags = CommandFlags.None) =>
        Inner.VectorSetSetAttributesJsonAsync(ToInner(key), member, attributesJson, flags);

    public Task<Lease<VectorSetSimilaritySearchResult>?> VectorSetSimilaritySearchAsync(
        RedisKey key,
        VectorSetSimilaritySearchRequest query,
        CommandFlags flags = CommandFlags.None) =>
        Inner.VectorSetSimilaritySearchAsync(ToInner(key), query, flags);

    public Task<RedisValue[]> VectorSetRangeAsync(
        RedisKey key,
        RedisValue start = default,
        RedisValue end = default,
        long count = -1,
        Exclude exclude = Exclude.None,
        CommandFlags flags = CommandFlags.None) =>
        Inner.VectorSetRangeAsync(ToInner(key), start, end, count, exclude, flags);

    public System.Collections.Generic.IAsyncEnumerable<RedisValue> VectorSetRangeEnumerateAsync(
        RedisKey key,
        RedisValue start = default,
        RedisValue end = default,
        long count = 100,
        Exclude exclude = Exclude.None,
        CommandFlags flags = CommandFlags.None) =>
        Inner.VectorSetRangeEnumerateAsync(ToInner(key), start, end, count, exclude, flags);
}
