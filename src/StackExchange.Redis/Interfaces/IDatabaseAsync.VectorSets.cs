using System;
using System.Diagnostics.CodeAnalysis;
using System.Threading.Tasks;

// ReSharper disable once CheckNamespace
namespace StackExchange.Redis;

/// <summary>
/// Describes functionality that is common to both standalone redis servers and redis clusters.
/// </summary>
public partial interface IDatabaseAsync
{
    // Vector Set operations

    /// <inheritdoc cref="IDatabase.VectorSetAdd(RedisKey, VectorSetAddRequest, CommandFlags)"/>
    [Experimental(Experiments.VectorSets, UrlFormat = Experiments.UrlFormat)]
    Task<bool> VectorSetAddAsync(
        RedisKey key,
        VectorSetAddRequest request,
        CommandFlags flags = CommandFlags.None);

    /// <inheritdoc cref="IDatabase.VectorSetLength(RedisKey, CommandFlags)"/>
    [Experimental(Experiments.VectorSets, UrlFormat = Experiments.UrlFormat)]
    Task<long> VectorSetLengthAsync(RedisKey key, CommandFlags flags = CommandFlags.None);

    /// <inheritdoc cref="IDatabase.VectorSetDimension(RedisKey, CommandFlags)"/>
    [Experimental(Experiments.VectorSets, UrlFormat = Experiments.UrlFormat)]
    Task<int> VectorSetDimensionAsync(RedisKey key, CommandFlags flags = CommandFlags.None);

    /// <inheritdoc cref="IDatabase.VectorSetGetApproximateVector(RedisKey, RedisValue, CommandFlags)"/>
    [Experimental(Experiments.VectorSets, UrlFormat = Experiments.UrlFormat)]
    Task<Lease<float>?> VectorSetGetApproximateVectorAsync(
        RedisKey key,
        RedisValue member,
        CommandFlags flags = CommandFlags.None);

    /// <inheritdoc cref="IDatabase.VectorSetGetAttributesJson(RedisKey, RedisValue, CommandFlags)"/>
    [Experimental(Experiments.VectorSets, UrlFormat = Experiments.UrlFormat)]
    Task<string?> VectorSetGetAttributesJsonAsync(
        RedisKey key,
        RedisValue member,
        CommandFlags flags = CommandFlags.None);

    /// <inheritdoc cref="IDatabase.VectorSetInfo(RedisKey, CommandFlags)"/>
    [Experimental(Experiments.VectorSets, UrlFormat = Experiments.UrlFormat)]
    Task<VectorSetInfo?> VectorSetInfoAsync(RedisKey key, CommandFlags flags = CommandFlags.None);

    /// <inheritdoc cref="IDatabase.VectorSetContains(RedisKey, RedisValue, CommandFlags)"/>
    [Experimental(Experiments.VectorSets, UrlFormat = Experiments.UrlFormat)]
    Task<bool> VectorSetContainsAsync(RedisKey key, RedisValue member, CommandFlags flags = CommandFlags.None);

    /// <inheritdoc cref="IDatabase.VectorSetGetLinks(RedisKey, RedisValue, CommandFlags)"/>
    [Experimental(Experiments.VectorSets, UrlFormat = Experiments.UrlFormat)]
    Task<Lease<RedisValue>?> VectorSetGetLinksAsync(
        RedisKey key,
        RedisValue member,
        CommandFlags flags = CommandFlags.None);

    /// <inheritdoc cref="IDatabase.VectorSetGetLinksWithScores(RedisKey, RedisValue, CommandFlags)"/>
    [Experimental(Experiments.VectorSets, UrlFormat = Experiments.UrlFormat)]
    Task<Lease<VectorSetLink>?> VectorSetGetLinksWithScoresAsync(
        RedisKey key,
        RedisValue member,
        CommandFlags flags = CommandFlags.None);

    /// <inheritdoc cref="IDatabase.VectorSetRandomMember(RedisKey, CommandFlags)"/>
    [Experimental(Experiments.VectorSets, UrlFormat = Experiments.UrlFormat)]
    Task<RedisValue> VectorSetRandomMemberAsync(RedisKey key, CommandFlags flags = CommandFlags.None);

    /// <inheritdoc cref="IDatabase.VectorSetRandomMembers(RedisKey, long, CommandFlags)"/>
    [Experimental(Experiments.VectorSets, UrlFormat = Experiments.UrlFormat)]
    Task<RedisValue[]> VectorSetRandomMembersAsync(RedisKey key, long count, CommandFlags flags = CommandFlags.None);

    /// <inheritdoc cref="IDatabase.VectorSetRemove(RedisKey, RedisValue, CommandFlags)"/>
    [Experimental(Experiments.VectorSets, UrlFormat = Experiments.UrlFormat)]
    Task<bool> VectorSetRemoveAsync(RedisKey key, RedisValue member, CommandFlags flags = CommandFlags.None);

    /// <inheritdoc cref="IDatabase.VectorSetSetAttributesJson(RedisKey, RedisValue, string, CommandFlags)"/>
    [Experimental(Experiments.VectorSets, UrlFormat = Experiments.UrlFormat)]
    Task<bool> VectorSetSetAttributesJsonAsync(
        RedisKey key,
        RedisValue member,
        string jsonAttributes,
        CommandFlags flags = CommandFlags.None);

    /// <inheritdoc cref="IDatabase.VectorSetSimilaritySearch(RedisKey, VectorSetSimilaritySearchRequest, CommandFlags)"/>
    [Experimental(Experiments.VectorSets, UrlFormat = Experiments.UrlFormat)]
    Task<Lease<VectorSetSimilaritySearchResult>?> VectorSetSimilaritySearchAsync(
        RedisKey key,
        VectorSetSimilaritySearchRequest query,
        CommandFlags flags = CommandFlags.None);
}
