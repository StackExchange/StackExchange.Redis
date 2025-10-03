using StackExchange.Redis;

namespace RESPite.StackExchange.Redis;

internal partial class RespContextDatabase
{
    // Vector Set operations
    public Task<bool> VectorSetAddAsync(
        RedisKey key,
        VectorSetAddRequest request,
        CommandFlags flags = CommandFlags.None) => throw new NotImplementedException();

    public Task<long> VectorSetLengthAsync(RedisKey key, CommandFlags flags = CommandFlags.None) =>
        throw new NotImplementedException();

    public Task<int> VectorSetDimensionAsync(RedisKey key, CommandFlags flags = CommandFlags.None) =>
        throw new NotImplementedException();

    public Task<Lease<float>?> VectorSetGetApproximateVectorAsync(
        RedisKey key,
        RedisValue member,
        CommandFlags flags = CommandFlags.None) => throw new NotImplementedException();

    public Task<string?> VectorSetGetAttributesJsonAsync(
        RedisKey key,
        RedisValue member,
        CommandFlags flags = CommandFlags.None) => throw new NotImplementedException();

    public Task<VectorSetInfo?> VectorSetInfoAsync(RedisKey key, CommandFlags flags = CommandFlags.None) =>
        throw new NotImplementedException();

    public Task<bool> VectorSetContainsAsync(RedisKey key, RedisValue member, CommandFlags flags = CommandFlags.None) =>
        throw new NotImplementedException();

    public Task<Lease<RedisValue>?> VectorSetGetLinksAsync(
        RedisKey key,
        RedisValue member,
        CommandFlags flags = CommandFlags.None) => throw new NotImplementedException();

    public Task<Lease<VectorSetLink>?> VectorSetGetLinksWithScoresAsync(
        RedisKey key,
        RedisValue member,
        CommandFlags flags = CommandFlags.None) => throw new NotImplementedException();

    public Task<RedisValue> VectorSetRandomMemberAsync(RedisKey key, CommandFlags flags = CommandFlags.None) =>
        throw new NotImplementedException();

    public Task<RedisValue[]> VectorSetRandomMembersAsync(
        RedisKey key,
        long count,
        CommandFlags flags = CommandFlags.None) => throw new NotImplementedException();

    public Task<bool> VectorSetRemoveAsync(RedisKey key, RedisValue member, CommandFlags flags = CommandFlags.None) =>
        throw new NotImplementedException();

    public Task<bool> VectorSetSetAttributesJsonAsync(
        RedisKey key,
        RedisValue member,
        string attributesJson,
        CommandFlags flags = CommandFlags.None) =>
        throw new NotImplementedException();

    public Task<Lease<VectorSetSimilaritySearchResult>?> VectorSetSimilaritySearchAsync(
        RedisKey key,
        VectorSetSimilaritySearchRequest query,
        CommandFlags flags = CommandFlags.None) =>
        throw new NotImplementedException();

    public bool VectorSetAdd(RedisKey key, VectorSetAddRequest request, CommandFlags flags = CommandFlags.None) =>
        throw new NotImplementedException();

    public long VectorSetLength(RedisKey key, CommandFlags flags = CommandFlags.None) =>
        throw new NotImplementedException();

    public int VectorSetDimension(RedisKey key, CommandFlags flags = CommandFlags.None) =>
        throw new NotImplementedException();

    public Lease<float>? VectorSetGetApproximateVector(
        RedisKey key,
        RedisValue member,
        CommandFlags flags = CommandFlags.None) => throw new NotImplementedException();

    public string?
        VectorSetGetAttributesJson(RedisKey key, RedisValue member, CommandFlags flags = CommandFlags.None) =>
        throw new NotImplementedException();

    public VectorSetInfo? VectorSetInfo(RedisKey key, CommandFlags flags = CommandFlags.None) =>
        throw new NotImplementedException();

    public bool VectorSetContains(RedisKey key, RedisValue member, CommandFlags flags = CommandFlags.None) =>
        throw new NotImplementedException();

    public Lease<RedisValue>?
        VectorSetGetLinks(RedisKey key, RedisValue member, CommandFlags flags = CommandFlags.None) =>
        throw new NotImplementedException();

    public Lease<VectorSetLink>? VectorSetGetLinksWithScores(
        RedisKey key,
        RedisValue member,
        CommandFlags flags = CommandFlags.None) => throw new NotImplementedException();

    public RedisValue VectorSetRandomMember(RedisKey key, CommandFlags flags = CommandFlags.None) =>
        throw new NotImplementedException();

    public RedisValue[] VectorSetRandomMembers(RedisKey key, long count, CommandFlags flags = CommandFlags.None) =>
        throw new NotImplementedException();

    public bool VectorSetRemove(RedisKey key, RedisValue member, CommandFlags flags = CommandFlags.None) =>
        throw new NotImplementedException();

    public bool VectorSetSetAttributesJson(
        RedisKey key,
        RedisValue member,
        string attributesJson,
        CommandFlags flags = CommandFlags.None) =>
        throw new NotImplementedException();

    public Lease<VectorSetSimilaritySearchResult>? VectorSetSimilaritySearch(
        RedisKey key,
        VectorSetSimilaritySearchRequest query,
        CommandFlags flags = CommandFlags.None) =>
        throw new NotImplementedException();
}
