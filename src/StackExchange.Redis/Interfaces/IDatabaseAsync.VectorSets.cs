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
    Task<bool> VectorSetAddAsync(
        RedisKey key,
        VectorSetAddRequest request,
        CommandFlags flags = CommandFlags.None);

    /// <inheritdoc cref="IDatabase.VectorSetLength(RedisKey, CommandFlags)"/>
    Task<long> VectorSetLengthAsync(RedisKey key, CommandFlags flags = CommandFlags.None);

    /// <inheritdoc cref="IDatabase.VectorSetDimension(RedisKey, CommandFlags)"/>
    Task<int> VectorSetDimensionAsync(RedisKey key, CommandFlags flags = CommandFlags.None);

    /// <inheritdoc cref="IDatabase.VectorSetGetApproximateVector(RedisKey, RedisValue, CommandFlags)"/>
    Task<Lease<float>?> VectorSetGetApproximateVectorAsync(
        RedisKey key,
        RedisValue member,
        CommandFlags flags = CommandFlags.None);

    /// <inheritdoc cref="IDatabase.VectorSetGetAttributesJson(RedisKey, RedisValue, CommandFlags)"/>
    Task<string?> VectorSetGetAttributesJsonAsync(
        RedisKey key,
        RedisValue member,
        CommandFlags flags = CommandFlags.None);

    /// <inheritdoc cref="IDatabase.VectorSetInfo(RedisKey, CommandFlags)"/>
    Task<VectorSetInfo?> VectorSetInfoAsync(RedisKey key, CommandFlags flags = CommandFlags.None);

    /// <inheritdoc cref="IDatabase.VectorSetContains(RedisKey, RedisValue, CommandFlags)"/>
    Task<bool> VectorSetContainsAsync(RedisKey key, RedisValue member, CommandFlags flags = CommandFlags.None);

    /// <inheritdoc cref="IDatabase.VectorSetGetLinks(RedisKey, RedisValue, CommandFlags)"/>
    Task<Lease<RedisValue>?> VectorSetGetLinksAsync(
        RedisKey key,
        RedisValue member,
        CommandFlags flags = CommandFlags.None);

    /// <inheritdoc cref="IDatabase.VectorSetGetLinksWithScores(RedisKey, RedisValue, CommandFlags)"/>
    Task<Lease<VectorSetLink>?> VectorSetGetLinksWithScoresAsync(
        RedisKey key,
        RedisValue member,
        CommandFlags flags = CommandFlags.None);

    /// <inheritdoc cref="IDatabase.VectorSetRandomMember(RedisKey, CommandFlags)"/>
    Task<RedisValue> VectorSetRandomMemberAsync(RedisKey key, CommandFlags flags = CommandFlags.None);

    /// <inheritdoc cref="IDatabase.VectorSetRandomMembers(RedisKey, long, CommandFlags)"/>
    Task<RedisValue[]> VectorSetRandomMembersAsync(RedisKey key, long count, CommandFlags flags = CommandFlags.None);

    /// <inheritdoc cref="IDatabase.VectorSetRemove(RedisKey, RedisValue, CommandFlags)"/>
    Task<bool> VectorSetRemoveAsync(RedisKey key, RedisValue member, CommandFlags flags = CommandFlags.None);

    /// <inheritdoc cref="IDatabase.VectorSetSetAttributesJson(RedisKey, RedisValue, string, CommandFlags)"/>
    Task<bool> VectorSetSetAttributesJsonAsync(
        RedisKey key,
        RedisValue member,
#if NET8_0_OR_GREATER
        [StringSyntax(StringSyntaxAttribute.Json)]
#endif
        string attributesJson,
        CommandFlags flags = CommandFlags.None);

    /// <inheritdoc cref="IDatabase.VectorSetSimilaritySearch(RedisKey, VectorSetSimilaritySearchRequest, CommandFlags)"/>
    Task<Lease<VectorSetSimilaritySearchResult>?> VectorSetSimilaritySearchAsync(
        RedisKey key,
        VectorSetSimilaritySearchRequest query,
        CommandFlags flags = CommandFlags.None);

    /// <inheritdoc cref="IDatabase.VectorSetRange(RedisKey, RedisValue, RedisValue, long, Exclude, CommandFlags)"/>
    Task<Lease<RedisValue>?> VectorSetRangeAsync(
        RedisKey key,
        RedisValue start = default,
        RedisValue end = default,
        long count = -1,
        Exclude exclude = Exclude.None,
        CommandFlags flags = CommandFlags.None);

    /// <inheritdoc cref="IDatabase.VectorSetRangeEnumerate(RedisKey, RedisValue, RedisValue, long, Exclude, CommandFlags)"/>
    System.Collections.Generic.IAsyncEnumerable<RedisValue> VectorSetRangeEnumerateAsync(
        RedisKey key,
        RedisValue start = default,
        RedisValue end = default,
        long count = 100,
        Exclude exclude = Exclude.None,
        CommandFlags flags = CommandFlags.None);

    /// <inheritdoc cref="IDatabase.StreamReadGroup(StreamPosition[], RedisValue, RedisValue, int?, bool, TimeSpan?, CommandFlags)"/>
    Task<RedisStream[]> StreamReadGroupAsync(StreamPosition[] streamPositions, RedisValue groupName, RedisValue consumerName, int? countPerStream = null, bool noAck = false, TimeSpan? claimMinIdleTime = null, CommandFlags flags = CommandFlags.None);
}
