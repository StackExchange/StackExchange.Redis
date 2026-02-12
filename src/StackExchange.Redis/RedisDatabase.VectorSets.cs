using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

// ReSharper disable once CheckNamespace
namespace StackExchange.Redis;

internal partial class RedisDatabase
{
    public bool VectorSetAdd(
        RedisKey key,
        VectorSetAddRequest request,
        CommandFlags flags = CommandFlags.None)
    {
        var msg = request.ToMessage(key, Database, flags);
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

    public bool VectorSetSetAttributesJson(RedisKey key, RedisValue member, string attributesJson, CommandFlags flags = CommandFlags.None)
    {
        var msg = Message.Create(Database, flags, RedisCommand.VSETATTR, key, member, attributesJson);
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
        VectorSetAddRequest request,
        CommandFlags flags = CommandFlags.None)
    {
        var msg = request.ToMessage(key, Database, flags);
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

    public Task<bool> VectorSetSetAttributesJsonAsync(RedisKey key, RedisValue member, string attributesJson, CommandFlags flags = CommandFlags.None)
    {
        var msg = Message.Create(Database, flags, RedisCommand.VSETATTR, key, member, attributesJson);
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

    private Message GetVectorSetRangeMessage(
        in RedisKey key,
        in RedisValue start,
        in RedisValue end,
        long count,
        Exclude exclude,
        CommandFlags flags)
    {
        static RedisValue GetTerminator(RedisValue value, Exclude exclude, bool isStart)
        {
            if (value.IsNull) return isStart ? RedisLiterals.MinusSymbol : RedisLiterals.PlusSymbol;
            var mask = isStart ? Exclude.Start : Exclude.Stop;
            var isExclusive = (exclude & mask) != 0;
            return (isExclusive ? "(" : "[") + value;
        }

        var from = GetTerminator(start, exclude, true);
        var to = GetTerminator(end, exclude, false);
        return count < 0
            ? Message.Create(Database, flags, RedisCommand.VRANGE, key, from, to)
            : Message.Create(Database, flags, RedisCommand.VRANGE, key, from, to, count);
    }

    public RedisValue[] VectorSetRange(
        RedisKey key,
        RedisValue start = default,
        RedisValue end = default,
        long count = -1,
        Exclude exclude = Exclude.None,
        CommandFlags flags = CommandFlags.None)
    {
        var msg = GetVectorSetRangeMessage(key, start, end, count, exclude, flags);
        return ExecuteSync(msg, ResultProcessor.RedisValueArray)!; // returns empty array if no key
    }

    public Task<RedisValue[]> VectorSetRangeAsync(
        RedisKey key,
        RedisValue start = default,
        RedisValue end = default,
        long count = -1,
        Exclude exclude = Exclude.None,
        CommandFlags flags = CommandFlags.None)
    {
        var msg = GetVectorSetRangeMessage(key, start, end, count, exclude, flags);
        return ExecuteAsync(msg, ResultProcessor.RedisValueArray)!; // returns empty array if no key
    }

    public IEnumerable<RedisValue> VectorSetRangeEnumerate(
        RedisKey key,
        RedisValue start = default,
        RedisValue end = default,
        long count = 100,
        Exclude exclude = Exclude.None,
        CommandFlags flags = CommandFlags.None)
    {
        // intentionally not using "scan" naming in case a VSCAN command is added later
        while (true)
        {
            var batch = VectorSetRange(key, start, end, count, exclude, flags);
            exclude |= Exclude.Start; // on subsequent iterations, exclude the start (we've already yielded it)

            if (batch.Length == 0) yield break;
            for (int i = 0; i < batch.Length; i++)
            {
                yield return batch[i];
            }
            start = batch[batch.Length - 1]; // use the last value as the exclusive start of the next batch
            if (batch.Length < count || (!end.IsNull && end == start)) yield break; // no need to issue a final query
        }
    }

    public IAsyncEnumerable<RedisValue> VectorSetRangeEnumerateAsync(
        RedisKey key,
        RedisValue start = default,
        RedisValue end = default,
        long count = 100,
        Exclude exclude = Exclude.None,
        CommandFlags flags = CommandFlags.None)
    {
        // intentionally not using "scan" naming in case a VSCAN command is added later
        return WithCancellationSupport(CancellationToken.None);

        async IAsyncEnumerable<RedisValue> WithCancellationSupport([EnumeratorCancellation] CancellationToken cancellationToken)
        {
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var batch = await VectorSetRangeAsync(key, start, end, count, exclude, flags);
                exclude |= Exclude.Start; // on subsequent iterations, exclude the start (we've already yielded it)

                if (batch.Length == 0) yield break;
                for (int i = 0; i < batch.Length; i++)
                {
                    yield return batch[i];
                }
                start = batch[batch.Length - 1]; // use the last value as the exclusive start of the next batch
                if (batch.Length < count || (!end.IsNull && end == start)) yield break; // no need to issue a final query
            }
        }
    }
}
