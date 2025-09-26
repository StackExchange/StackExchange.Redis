using System;

// ReSharper disable once CheckNamespace
namespace StackExchange.Redis.KeyspaceIsolation;

internal sealed partial class KeyPrefixedDatabase
{
    // Vector Set operations
    public bool VectorSetAdd(
        RedisKey key,
        VectorSetAddRequest request,
        CommandFlags flags = CommandFlags.None) =>
        Inner.VectorSetAdd(ToInner(key), request, flags);

    public long VectorSetLength(RedisKey key, CommandFlags flags = CommandFlags.None) =>
        Inner.VectorSetLength(ToInner(key), flags);

    public int VectorSetDimension(RedisKey key, CommandFlags flags = CommandFlags.None) =>
        Inner.VectorSetDimension(ToInner(key), flags);

    public Lease<float>? VectorSetGetApproximateVector(RedisKey key, RedisValue member, CommandFlags flags = CommandFlags.None) =>
        Inner.VectorSetGetApproximateVector(ToInner(key), member, flags);

    public string? VectorSetGetAttributesJson(RedisKey key, RedisValue member, CommandFlags flags = CommandFlags.None) =>
        Inner.VectorSetGetAttributesJson(ToInner(key), member, flags);

    public VectorSetInfo? VectorSetInfo(RedisKey key, CommandFlags flags = CommandFlags.None) =>
        Inner.VectorSetInfo(ToInner(key), flags);

    public bool VectorSetContains(RedisKey key, RedisValue member, CommandFlags flags = CommandFlags.None) =>
        Inner.VectorSetContains(ToInner(key), member, flags);

    public Lease<RedisValue>? VectorSetGetLinks(RedisKey key, RedisValue member, CommandFlags flags = CommandFlags.None) =>
        Inner.VectorSetGetLinks(ToInner(key), member, flags);

    public Lease<VectorSetLink>? VectorSetGetLinksWithScores(RedisKey key, RedisValue member, CommandFlags flags = CommandFlags.None) =>
        Inner.VectorSetGetLinksWithScores(ToInner(key), member, flags);

    public RedisValue VectorSetRandomMember(RedisKey key, CommandFlags flags = CommandFlags.None) =>
        Inner.VectorSetRandomMember(ToInner(key), flags);

    public RedisValue[] VectorSetRandomMembers(RedisKey key, long count, CommandFlags flags = CommandFlags.None) =>
        Inner.VectorSetRandomMembers(ToInner(key), count, flags);

    public bool VectorSetRemove(RedisKey key, RedisValue member, CommandFlags flags = CommandFlags.None) =>
        Inner.VectorSetRemove(ToInner(key), member, flags);

    public bool VectorSetSetAttributesJson(RedisKey key, RedisValue member, string attributesJson, CommandFlags flags = CommandFlags.None) =>
        Inner.VectorSetSetAttributesJson(ToInner(key), member, attributesJson, flags);

    public Lease<VectorSetSimilaritySearchResult>? VectorSetSimilaritySearch(
        RedisKey key,
        VectorSetSimilaritySearchRequest query,
        CommandFlags flags = CommandFlags.None) =>
        Inner.VectorSetSimilaritySearch(ToInner(key), query, flags);
}
