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
        RedisValue element,
        ReadOnlyMemory<float> values,
        int? reducedDimensions = null,
        VectorSetQuantization quantization = VectorSetQuantization.Int8,
        int? buildExplorationFactor = null,
        int? maxConnections = null,
        bool useCheckAndSet = false,
        string? attributesJson = null,
        CommandFlags flags = CommandFlags.None) =>
        Inner.VectorSetAddAsync(ToInner(key), element, values, reducedDimensions, quantization, buildExplorationFactor, maxConnections, useCheckAndSet, attributesJson, flags);

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

    public Task<bool> VectorSetSetAttributesJsonAsync(RedisKey key, RedisValue member, string jsonAttributes, CommandFlags flags = CommandFlags.None) =>
        Inner.VectorSetSetAttributesJsonAsync(ToInner(key), member, jsonAttributes, flags);

    public Task<Lease<VectorSetSimilaritySearchResult>?> VectorSetSimilaritySearchByVectorAsync(
        RedisKey key,
        ReadOnlyMemory<float> vector,
        int? count = null,
        bool withScores = false,
        bool withAttributes = false,
        double? epsilon = null,
        int? searchExplorationFactor = null,
        string? filterExpression = null,
        int? maxFilteringEffort = null,
        bool useExactSearch = false,
        bool disableThreading = false,
        CommandFlags flags = CommandFlags.None) =>
        Inner.VectorSetSimilaritySearchByVectorAsync(ToInner(key), vector, count, withScores, withAttributes, epsilon, searchExplorationFactor, filterExpression, maxFilteringEffort, useExactSearch, disableThreading, flags);

    public Task<Lease<VectorSetSimilaritySearchResult>?> VectorSetSimilaritySearchByMemberAsync(
        RedisKey key,
        RedisValue member,
        int? count = null,
        bool withScores = false,
        bool withAttributes = false,
        double? epsilon = null,
        int? searchExplorationFactor = null,
        string? filterExpression = null,
        int? maxFilteringEffort = null,
        bool useExactSearch = false,
        bool disableThreading = false,
        CommandFlags flags = CommandFlags.None) =>
        Inner.VectorSetSimilaritySearchByMemberAsync(ToInner(key), member, count, withScores, withAttributes, epsilon, searchExplorationFactor, filterExpression, maxFilteringEffort, useExactSearch, disableThreading, flags);
}
