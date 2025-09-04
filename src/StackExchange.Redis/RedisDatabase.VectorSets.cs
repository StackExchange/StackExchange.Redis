using System;
using System.Threading.Tasks;

// ReSharper disable once CheckNamespace
namespace StackExchange.Redis;

internal partial class RedisDatabase
{
    public bool VectorSetAdd(
        RedisKey key,
        RedisValue element,
        ReadOnlyMemory<float> values,
        int? reducedDimensions = null,
        VectorSetQuantization quantization = VectorSetQuantization.Int8,
        int? buildExplorationFactor = null,
        int? maxConnections = null,
        bool useCheckAndSet = false,
        string? attributesJson = null,
        CommandFlags flags = CommandFlags.None)
    {
        var msg = new VectorSetAddMessage(Database, flags, key, element, values, reducedDimensions, quantization, buildExplorationFactor, maxConnections, useCheckAndSet, attributesJson);
        return ExecuteSync(msg, ResultProcessor.Boolean);
    }

    public long VectorSetLength(RedisKey key, CommandFlags flags = CommandFlags.None)
    {
        var msg = Message.Create(Database, flags, RedisCommand.VCARD, key);
        return ExecuteSync(msg, ResultProcessor.Int64);
    }

    public int VectorSetDimension(RedisKey key, CommandFlags flags = CommandFlags.None)
    {
        var msg = Message.Create(Database, flags, RedisCommand.VDIM, key);
        return ExecuteSync(msg, ResultProcessor.Int32);
    }

    public Lease<float>? VectorSetGetApproximateVector(RedisKey key, RedisValue member, CommandFlags flags = CommandFlags.None)
    {
        var msg = Message.Create(Database, flags, RedisCommand.VEMB, key, member);
        return ExecuteSync(msg, ResultProcessor.LeaseFloat32);
    }

    public string? VectorSetGetAttributesJson(RedisKey key, RedisValue member, CommandFlags flags = CommandFlags.None)
    {
        var msg = Message.Create(Database, flags, RedisCommand.VGETATTR, key, member);
        return ExecuteSync(msg, ResultProcessor.String);
    }

    public VectorSetInfo? VectorSetInfo(RedisKey key, CommandFlags flags = CommandFlags.None)
    {
        var msg = Message.Create(Database, flags, RedisCommand.VINFO, key);
        return ExecuteSync(msg, ResultProcessor.VectorSetInfo);
    }

    public bool VectorSetContains(RedisKey key, RedisValue member, CommandFlags flags = CommandFlags.None)
    {
        var msg = Message.Create(Database, flags, RedisCommand.VISMEMBER, key, member);
        return ExecuteSync(msg, ResultProcessor.Boolean);
    }

    public Lease<RedisValue>? VectorSetGetLinks(RedisKey key, RedisValue member, CommandFlags flags = CommandFlags.None)
    {
        var msg = Message.Create(Database, flags, RedisCommand.VLINKS, key, member);
        return ExecuteSync(msg, ResultProcessor.VectorSetLinks);
    }

    public Lease<VectorSetLink>? VectorSetGetLinksWithScores(RedisKey key, RedisValue member, CommandFlags flags = CommandFlags.None)
    {
        var msg = Message.Create(Database, flags, RedisCommand.VLINKS, key, member, RedisLiterals.WITHSCORES);
        return ExecuteSync(msg, ResultProcessor.VectorSetLinksWithScores);
    }

    public RedisValue VectorSetRandomMember(RedisKey key, CommandFlags flags = CommandFlags.None)
    {
        var msg = Message.Create(Database, flags, RedisCommand.VRANDMEMBER, key);
        return ExecuteSync(msg, ResultProcessor.RedisValue);
    }

    public RedisValue[] VectorSetRandomMembers(RedisKey key, long count, CommandFlags flags = CommandFlags.None)
    {
        var msg = Message.Create(Database, flags, RedisCommand.VRANDMEMBER, key, count);
        return ExecuteSync(msg, ResultProcessor.RedisValueArray, defaultValue: Array.Empty<RedisValue>());
    }

    public bool VectorSetRemove(RedisKey key, RedisValue member, CommandFlags flags = CommandFlags.None)
    {
        var msg = Message.Create(Database, flags, RedisCommand.VREM, key, member);
        return ExecuteSync(msg, ResultProcessor.Boolean);
    }

    public bool VectorSetSetAttributesJson(RedisKey key, RedisValue member, string jsonAttributes, CommandFlags flags = CommandFlags.None)
    {
        var msg = Message.Create(Database, flags, RedisCommand.VSETATTR, key, member, jsonAttributes);
        return ExecuteSync(msg, ResultProcessor.Boolean);
    }

    public Lease<VectorSetSimilaritySearchResult>? VectorSetSimilaritySearch(
        RedisKey key,
        VectorSetSimilaritySearchRequest query,
        CommandFlags flags = CommandFlags.None)
    {
        if (query == null) throw new ArgumentNullException(nameof(query));
        var msg = query.ToMessage(key, Database, flags);
        return ExecuteSync(msg, msg.GetResultProcessor());
    }

    // Vector Set async operations
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
        CommandFlags flags = CommandFlags.None)
    {
        var msg = new VectorSetAddMessage(Database, flags, key, element, values, reducedDimensions, quantization, buildExplorationFactor, maxConnections, useCheckAndSet, attributesJson);
        return ExecuteAsync(msg, ResultProcessor.Boolean);
    }

    public Task<long> VectorSetLengthAsync(RedisKey key, CommandFlags flags = CommandFlags.None)
    {
        var msg = Message.Create(Database, flags, RedisCommand.VCARD, key);
        return ExecuteAsync(msg, ResultProcessor.Int64);
    }

    public Task<int> VectorSetDimensionAsync(RedisKey key, CommandFlags flags = CommandFlags.None)
    {
        var msg = Message.Create(Database, flags, RedisCommand.VDIM, key);
        return ExecuteAsync(msg, ResultProcessor.Int32);
    }

    public Task<Lease<float>?> VectorSetGetApproximateVectorAsync(RedisKey key, RedisValue member, CommandFlags flags = CommandFlags.None)
    {
        var msg = Message.Create(Database, flags, RedisCommand.VEMB, key, member);
        return ExecuteAsync(msg, ResultProcessor.LeaseFloat32);
    }

    public Task<string?> VectorSetGetAttributesJsonAsync(RedisKey key, RedisValue member, CommandFlags flags = CommandFlags.None)
    {
        var msg = Message.Create(Database, flags, RedisCommand.VGETATTR, key, member);
        return ExecuteAsync(msg, ResultProcessor.String);
    }

    public Task<VectorSetInfo?> VectorSetInfoAsync(RedisKey key, CommandFlags flags = CommandFlags.None)
    {
        var msg = Message.Create(Database, flags, RedisCommand.VINFO, key);
        return ExecuteAsync(msg, ResultProcessor.VectorSetInfo);
    }

    public Task<bool> VectorSetContainsAsync(RedisKey key, RedisValue member, CommandFlags flags = CommandFlags.None)
    {
        var msg = Message.Create(Database, flags, RedisCommand.VISMEMBER, key, member);
        return ExecuteAsync(msg, ResultProcessor.Boolean);
    }

    public Task<Lease<RedisValue>?> VectorSetGetLinksAsync(RedisKey key, RedisValue member, CommandFlags flags = CommandFlags.None)
    {
        var msg = Message.Create(Database, flags, RedisCommand.VLINKS, key, member);
        return ExecuteAsync(msg, ResultProcessor.VectorSetLinks);
    }

    public Task<Lease<VectorSetLink>?> VectorSetGetLinksWithScoresAsync(RedisKey key, RedisValue member, CommandFlags flags = CommandFlags.None)
    {
        var msg = Message.Create(Database, flags, RedisCommand.VLINKS, key, member, RedisLiterals.WITHSCORES);
        return ExecuteAsync(msg, ResultProcessor.VectorSetLinksWithScores);
    }

    public Task<RedisValue> VectorSetRandomMemberAsync(RedisKey key, CommandFlags flags = CommandFlags.None)
    {
        var msg = Message.Create(Database, flags, RedisCommand.VRANDMEMBER, key);
        return ExecuteAsync(msg, ResultProcessor.RedisValue);
    }

    public Task<RedisValue[]> VectorSetRandomMembersAsync(RedisKey key, long count, CommandFlags flags = CommandFlags.None)
    {
        var msg = Message.Create(Database, flags, RedisCommand.VRANDMEMBER, key, count);
        return ExecuteAsync(msg, ResultProcessor.RedisValueArray, defaultValue: Array.Empty<RedisValue>());
    }

    public Task<bool> VectorSetRemoveAsync(RedisKey key, RedisValue member, CommandFlags flags = CommandFlags.None)
    {
        var msg = Message.Create(Database, flags, RedisCommand.VREM, key, member);
        return ExecuteAsync(msg, ResultProcessor.Boolean);
    }

    public Task<bool> VectorSetSetAttributesJsonAsync(RedisKey key, RedisValue member, string jsonAttributes, CommandFlags flags = CommandFlags.None)
    {
        var msg = Message.Create(Database, flags, RedisCommand.VSETATTR, key, member, jsonAttributes);
        return ExecuteAsync(msg, ResultProcessor.Boolean);
    }

    public Task<Lease<VectorSetSimilaritySearchResult>?> VectorSetSimilaritySearchAsync(
        RedisKey key,
        VectorSetSimilaritySearchRequest query,
        CommandFlags flags = CommandFlags.None)
    {
        if (query == null) throw new ArgumentNullException(nameof(query));
        var msg = query.ToMessage(key, Database, flags);
        return ExecuteAsync(msg, msg.GetResultProcessor());
    }
}
