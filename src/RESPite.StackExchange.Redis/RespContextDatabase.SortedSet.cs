using RESPite.Messages;
using StackExchange.Redis;

namespace RESPite.StackExchange.Redis;

internal partial class RespContextDatabase
{
    public bool SortedSetAdd(
        RedisKey key,
        RedisValue member,
        double score,
        CommandFlags flags)
        => Context(flags).SortedSets().ZAdd(key, member, score).Wait(SyncTimeout);

    public bool SortedSetAdd(
        RedisKey key,
        RedisValue member,
        double score,
        When when,
        CommandFlags flags)
        => Context(flags).SortedSets().ZAdd(key, when.ToSortedSetWhen(), member, score).Wait(SyncTimeout);

    public bool SortedSetAdd(
        RedisKey key,
        RedisValue member,
        double score,
        SortedSetWhen when,
        CommandFlags flags)
        => Context(flags).SortedSets().ZAdd(key, when, member, score).Wait(SyncTimeout);

    public long SortedSetAdd(RedisKey key, SortedSetEntry[] values, CommandFlags flags = CommandFlags.None)
        => Context(flags).SortedSets().ZAdd(key, values).Wait(SyncTimeout);

    public long SortedSetAdd(
        RedisKey key,
        SortedSetEntry[] values,
        When when,
        CommandFlags flags = CommandFlags.None)
        => Context(flags).SortedSets().ZAdd(key, when.ToSortedSetWhen(), values).Wait(SyncTimeout);

    public long SortedSetAdd(
        RedisKey key,
        SortedSetEntry[] values,
        SortedSetWhen when = SortedSetWhen.Always,
        CommandFlags flags = CommandFlags.None)
        => Context(flags).SortedSets().ZAdd(key, when, values).Wait(SyncTimeout);

    public Task<bool> SortedSetAddAsync(
        RedisKey key,
        RedisValue member,
        double score,
        CommandFlags flags)
        => Context(flags).SortedSets().ZAdd(key, member, score).AsTask();

    public Task<bool> SortedSetAddAsync(
        RedisKey key,
        RedisValue member,
        double score,
        When when,
        CommandFlags flags)
        => Context(flags).SortedSets().ZAdd(key, when.ToSortedSetWhen(), member, score).AsTask();

    public Task<bool> SortedSetAddAsync(
        RedisKey key,
        RedisValue member,
        double score,
        SortedSetWhen when,
        CommandFlags flags)
        => Context(flags).SortedSets().ZAdd(key, when, member, score).AsTask();

    public Task<long> SortedSetAddAsync(
        RedisKey key,
        SortedSetEntry[] values,
        CommandFlags flags = CommandFlags.None)
        => Context(flags).SortedSets().ZAdd(key, values).AsTask();

    public Task<long> SortedSetAddAsync(
        RedisKey key,
        SortedSetEntry[] values,
        When when,
        CommandFlags flags = CommandFlags.None)
        => Context(flags).SortedSets().ZAdd(key, when.ToSortedSetWhen(), values).AsTask();

    public Task<long> SortedSetAddAsync(
        RedisKey key,
        SortedSetEntry[] values,
        SortedSetWhen when = SortedSetWhen.Always,
        CommandFlags flags = CommandFlags.None)
        => Context(flags).SortedSets().ZAdd(key, when, values).AsTask();

    public RedisValue[] SortedSetCombine(
        SetOperation operation,
        RedisKey[] keys,
        double[]? weights = null,
        Aggregate aggregate = Aggregate.Sum,
        CommandFlags flags = CommandFlags.None)
        => Context(flags).SortedSets().Combine(operation, keys, weights, aggregate).Wait(SyncTimeout);

    public long SortedSetCombineAndStore(
        SetOperation operation,
        RedisKey destination,
        RedisKey first,
        RedisKey second,
        Aggregate aggregate = Aggregate.Sum,
        CommandFlags flags = CommandFlags.None)
        => Context(flags).SortedSets().CombineAndStore(operation, destination, first, second, aggregate).Wait(SyncTimeout);

    public long SortedSetCombineAndStore(
        SetOperation operation,
        RedisKey destination,
        RedisKey[] keys,
        double[]? weights = null,
        Aggregate aggregate = Aggregate.Sum,
        CommandFlags flags = CommandFlags.None)
        => Context(flags).SortedSets().CombineAndStore(operation, destination, keys, weights, aggregate).Wait(SyncTimeout);

    public Task<RedisValue[]> SortedSetCombineAsync(
        SetOperation operation,
        RedisKey[] keys,
        double[]? weights = null,
        Aggregate aggregate = Aggregate.Sum,
        CommandFlags flags = CommandFlags.None)
        => Context(flags).SortedSets().Combine(operation, keys, weights, aggregate).AsTask();

    public Task<long> SortedSetCombineAndStoreAsync(
        SetOperation operation,
        RedisKey destination,
        RedisKey first,
        RedisKey second,
        Aggregate aggregate = Aggregate.Sum,
        CommandFlags flags = CommandFlags.None)
        => Context(flags).SortedSets().CombineAndStore(operation, destination, first, second, aggregate).AsTask();

    public Task<long> SortedSetCombineAndStoreAsync(
        SetOperation operation,
        RedisKey destination,
        RedisKey[] keys,
        double[]? weights = null,
        Aggregate aggregate = Aggregate.Sum,
        CommandFlags flags = CommandFlags.None)
        => Context(flags).SortedSets().CombineAndStore(operation, destination, keys, weights, aggregate).AsTask();

    public SortedSetEntry[] SortedSetCombineWithScores(
        SetOperation operation,
        RedisKey[] keys,
        double[]? weights = null,
        Aggregate aggregate = Aggregate.Sum,
        CommandFlags flags = CommandFlags.None)
        => Context(flags).SortedSets().CombineWithScores(operation, keys, weights, aggregate).Wait(SyncTimeout);

    public Task<SortedSetEntry[]> SortedSetCombineWithScoresAsync(
        SetOperation operation,
        RedisKey[] keys,
        double[]? weights = null,
        Aggregate aggregate = Aggregate.Sum,
        CommandFlags flags = CommandFlags.None)
        => Context(flags).SortedSets().CombineWithScores(operation, keys, weights, aggregate).AsTask();

    public double SortedSetDecrement(
        RedisKey key,
        RedisValue member,
        double value,
        CommandFlags flags = CommandFlags.None)
        => Context(flags).SortedSets().ZIncrBy(key, member, -value).Wait(SyncTimeout);

    public Task<double> SortedSetDecrementAsync(
        RedisKey key,
        RedisValue member,
        double value,
        CommandFlags flags = CommandFlags.None)
        => Context(flags).SortedSets().ZIncrBy(key, member, -value).AsTask();

    public double SortedSetIncrement(
        RedisKey key,
        RedisValue member,
        double value,
        CommandFlags flags = CommandFlags.None)
        => Context(flags).SortedSets().ZIncrBy(key, member, value).Wait(SyncTimeout);

    public Task<double> SortedSetIncrementAsync(
        RedisKey key,
        RedisValue member,
        double value,
        CommandFlags flags = CommandFlags.None)
        => Context(flags).SortedSets().ZIncrBy(key, member, value).AsTask();

    public long SortedSetIntersectionLength(RedisKey[] keys, long limit = 0, CommandFlags flags = CommandFlags.None)
        => Context(flags).SortedSets().ZInterCard(keys, limit).Wait(SyncTimeout);

    public Task<long> SortedSetIntersectionLengthAsync(
        RedisKey[] keys,
        long limit = 0,
        CommandFlags flags = CommandFlags.None)
        => Context(flags).SortedSets().ZInterCard(keys, limit).AsTask();

    public long SortedSetLength(
        RedisKey key,
        double min = double.NegativeInfinity,
        double max = double.PositiveInfinity,
        Exclude exclude = Exclude.None,
        CommandFlags flags = CommandFlags.None)
        => Context(flags).SortedSets().ZCardOrCount(key, min, max, exclude).Wait(SyncTimeout);

    public Task<long> SortedSetLengthAsync(
        RedisKey key,
        double min = double.NegativeInfinity,
        double max = double.PositiveInfinity,
        Exclude exclude = Exclude.None,
        CommandFlags flags = CommandFlags.None)
        => Context(flags).SortedSets().ZCardOrCount(key, min, max, exclude).AsTask();

    public long SortedSetLengthByValue(
        RedisKey key,
        RedisValue min,
        RedisValue max,
        Exclude exclude = Exclude.None,
        CommandFlags flags = CommandFlags.None)
        => Context(flags).SortedSets().ZLexCount(key, min, max, exclude).Wait(SyncTimeout);

    public Task<long> SortedSetLengthByValueAsync(
        RedisKey key,
        RedisValue min,
        RedisValue max,
        Exclude exclude = Exclude.None,
        CommandFlags flags = CommandFlags.None)
        => Context(flags).SortedSets().ZLexCount(key, min, max, exclude).AsTask();

    public SortedSetEntry? SortedSetPop(
        RedisKey key,
        Order order = Order.Ascending,
        CommandFlags flags = CommandFlags.None)
        => Context(flags).SortedSets().ZPop(key, order).Wait(SyncTimeout);

    public SortedSetEntry[] SortedSetPop(
        RedisKey key,
        long count,
        Order order = Order.Ascending,
        CommandFlags flags = CommandFlags.None)
        => Context(flags).SortedSets().ZPop(key, count, order).Wait(SyncTimeout);

    public SortedSetPopResult SortedSetPop(
        RedisKey[] keys,
        long count,
        Order order = Order.Ascending,
        CommandFlags flags = CommandFlags.None)
        => Context(flags).SortedSets().ZMPop(keys, order, count).Wait(SyncTimeout);

    public Task<SortedSetEntry?> SortedSetPopAsync(
        RedisKey key,
        Order order = Order.Ascending,
        CommandFlags flags = CommandFlags.None)
        => Context(flags).SortedSets().ZPop(key, order).AsTask();

    public Task<SortedSetEntry[]> SortedSetPopAsync(
        RedisKey key,
        long count,
        Order order = Order.Ascending,
        CommandFlags flags = CommandFlags.None)
        => Context(flags).SortedSets().ZPop(key, count, order).AsTask();

    public Task<SortedSetPopResult> SortedSetPopAsync(
        RedisKey[] keys,
        long count,
        Order order = Order.Ascending,
        CommandFlags flags = CommandFlags.None)
        => Context(flags).SortedSets().ZMPop(keys, order, count).AsTask();

    public RedisValue SortedSetRandomMember(RedisKey key, CommandFlags flags = CommandFlags.None)
        => Context(flags).SortedSets().ZRandMember(key).Wait(SyncTimeout);

    public Task<RedisValue> SortedSetRandomMemberAsync(RedisKey key, CommandFlags flags = CommandFlags.None)
        => Context(flags).SortedSets().ZRandMember(key).AsTask();

    public RedisValue[] SortedSetRandomMembers(RedisKey key, long count, CommandFlags flags = CommandFlags.None)
        => Context(flags).SortedSets().ZRandMember(key, count).Wait(SyncTimeout);

    public Task<RedisValue[]> SortedSetRandomMembersAsync(
        RedisKey key,
        long count,
        CommandFlags flags = CommandFlags.None)
        => Context(flags).SortedSets().ZRandMember(key, count).AsTask();

    public SortedSetEntry[] SortedSetRandomMembersWithScores(
        RedisKey key,
        long count,
        CommandFlags flags = CommandFlags.None)
        => Context(flags).SortedSets().ZRandMemberWithScores(key, count).Wait(SyncTimeout);

    public Task<SortedSetEntry[]> SortedSetRandomMembersWithScoresAsync(
        RedisKey key,
        long count,
        CommandFlags flags = CommandFlags.None)
        => Context(flags).SortedSets().ZRandMemberWithScores(key, count).AsTask();

    public long? SortedSetRank(
        RedisKey key,
        RedisValue member,
        Order order = Order.Ascending,
        CommandFlags flags = CommandFlags.None)
        => Context(flags).SortedSets().ZRank(key, member, order).Wait(SyncTimeout);

    public Task<long?> SortedSetRankAsync(
        RedisKey key,
        RedisValue member,
        Order order = Order.Ascending,
        CommandFlags flags = CommandFlags.None)
        => Context(flags).SortedSets().ZRank(key, member, order).AsTask();

    public RedisValue[] SortedSetRangeByRank(
        RedisKey key,
        long start = 0,
        long stop = -1,
        Order order = Order.Ascending,
        CommandFlags flags = CommandFlags.None)
        => Context(flags).SortedSets().ZRange(key, start, stop, order).Wait(SyncTimeout);

    public long SortedSetRangeAndStore(
        RedisKey sourceKey,
        RedisKey destinationKey,
        RedisValue start,
        RedisValue stop,
        SortedSetOrder sortedSetOrder = SortedSetOrder.ByRank,
        Exclude exclude = Exclude.None,
        Order order = Order.Ascending,
        long skip = 0,
        long? take = null,
        CommandFlags flags = CommandFlags.None)
        => Context(flags).SortedSets().ZRangeStore(sourceKey, destinationKey, start, stop, sortedSetOrder, exclude, order, skip, take).Wait(SyncTimeout);

    public Task<RedisValue[]> SortedSetRangeByRankAsync(
        RedisKey key,
        long start = 0,
        long stop = -1,
        Order order = Order.Ascending,
        CommandFlags flags = CommandFlags.None)
        => Context(flags).SortedSets().ZRange(key, start, stop, order).AsTask();

    public Task<long> SortedSetRangeAndStoreAsync(
        RedisKey sourceKey,
        RedisKey destinationKey,
        RedisValue start,
        RedisValue stop,
        SortedSetOrder sortedSetOrder = SortedSetOrder.ByRank,
        Exclude exclude = Exclude.None,
        Order order = Order.Ascending,
        long skip = 0,
        long? take = null,
        CommandFlags flags = CommandFlags.None)
        => Context(flags).SortedSets().ZRangeStore(sourceKey, destinationKey, start, stop, sortedSetOrder, exclude, order, skip, take).AsTask();

    public SortedSetEntry[] SortedSetRangeByRankWithScores(
        RedisKey key,
        long start = 0,
        long stop = -1,
        Order order = Order.Ascending,
        CommandFlags flags = CommandFlags.None)
        => Context(flags).SortedSets().ZRangeWithScores(key, start, stop, order).Wait(SyncTimeout);

    public Task<SortedSetEntry[]> SortedSetRangeByRankWithScoresAsync(
        RedisKey key,
        long start = 0,
        long stop = -1,
        Order order = Order.Ascending,
        CommandFlags flags = CommandFlags.None)
        => Context(flags).SortedSets().ZRangeWithScores(key, start, stop, order).AsTask();

    public RedisValue[] SortedSetRangeByScore(
        RedisKey key,
        double start = double.NegativeInfinity,
        double stop = double.PositiveInfinity,
        Exclude exclude = Exclude.None,
        Order order = Order.Ascending,
        long skip = 0,
        long take = -1,
        CommandFlags flags = CommandFlags.None)
        => Context(flags).SortedSets().ZRangeByScore(key, ByScore(start, stop, exclude, skip, take), order).Wait(SyncTimeout);

    private static SortedSetCommands.ZRangeRequest ByScore(double start, double stop, Exclude exclude, long skip, long take)
    {
        var req = SortedSetCommands.ZRangeRequest.ByScore(start, stop, exclude);
        req.Offset = skip;
        req.Count = take;
        return req;
    }

    public Task<RedisValue[]> SortedSetRangeByScoreAsync(
        RedisKey key,
        double start = double.NegativeInfinity,
        double stop = double.PositiveInfinity,
        Exclude exclude = Exclude.None,
        Order order = Order.Ascending,
        long skip = 0,
        long take = -1,
        CommandFlags flags = CommandFlags.None)
        => Context(flags).SortedSets().ZRangeByScore(key, ByScore(start, stop, exclude, skip, take), order).AsTask();

    public SortedSetEntry[] SortedSetRangeByScoreWithScores(
        RedisKey key,
        double start = double.NegativeInfinity,
        double stop = double.PositiveInfinity,
        Exclude exclude = Exclude.None,
        Order order = Order.Ascending,
        long skip = 0,
        long take = -1,
        CommandFlags flags = CommandFlags.None)
        => Context(flags).SortedSets().ZRangeByScoreWithScores(key, ByScore(start, stop, exclude, skip, take), order).Wait(SyncTimeout);

    public Task<SortedSetEntry[]> SortedSetRangeByScoreWithScoresAsync(
        RedisKey key,
        double start = double.NegativeInfinity,
        double stop = double.PositiveInfinity,
        Exclude exclude = Exclude.None,
        Order order = Order.Ascending,
        long skip = 0,
        long take = -1,
        CommandFlags flags = CommandFlags.None)
        => Context(flags).SortedSets().ZRangeByScoreWithScores(key, ByScore(start, stop, exclude, skip, take), order).AsTask();

    private static SortedSetCommands.ZRangeRequest ByLex(RedisValue start, RedisValue stop, Exclude exclude, long skip, long take)
    {
        var req = SortedSetCommands.ZRangeRequest.ByLex(start, stop, exclude);
        req.Offset = skip;
        req.Count = take;
        return req;
    }

    public RedisValue[] SortedSetRangeByValue(
        RedisKey key,
        RedisValue min = default,
        RedisValue max = default,
        Exclude exclude = Exclude.None,
        long skip = 0,
        long take = -1,
        CommandFlags flags = CommandFlags.None)
        => Context(flags).SortedSets().ZRangeByLex(key, ByLex(min, max, exclude, skip, take)).Wait(SyncTimeout);

    public RedisValue[] SortedSetRangeByValue(
        RedisKey key,
        RedisValue min,
        RedisValue max,
        Exclude exclude,
        Order order,
        long skip = 0,
        long take = -1,
        CommandFlags flags = CommandFlags.None)
        => Context(flags).SortedSets().ZRangeByLex(key, ByLex(min, max, exclude, skip, take), order).Wait(SyncTimeout);

    public Task<RedisValue[]> SortedSetRangeByValueAsync(
        RedisKey key,
        RedisValue min = default,
        RedisValue max = default,
        Exclude exclude = Exclude.None,
        long skip = 0,
        long take = -1,
        CommandFlags flags = CommandFlags.None)
        => Context(flags).SortedSets().ZRangeByLex(key, ByLex(min, max, exclude, skip, take)).AsTask();

    public Task<RedisValue[]> SortedSetRangeByValueAsync(
        RedisKey key,
        RedisValue min,
        RedisValue max,
        Exclude exclude,
        Order order,
        long skip = 0,
        long take = -1,
        CommandFlags flags = CommandFlags.None)
        => Context(flags).SortedSets().ZRangeByLex(key, ByLex(min, max, exclude, skip, take), order).AsTask();

    public bool SortedSetRemove(RedisKey key, RedisValue member, CommandFlags flags = CommandFlags.None)
        => Context(flags).SortedSets().ZRem(key, member).Wait(SyncTimeout);

    public long SortedSetRemove(RedisKey key, RedisValue[] members, CommandFlags flags = CommandFlags.None)
        => Context(flags).SortedSets().ZRem(key, members).Wait(SyncTimeout);

    public Task<bool> SortedSetRemoveAsync(RedisKey key, RedisValue member, CommandFlags flags = CommandFlags.None)
        => Context(flags).SortedSets().ZRem(key, member).AsTask();

    public Task<long> SortedSetRemoveAsync(
        RedisKey key,
        RedisValue[] members,
        CommandFlags flags = CommandFlags.None)
        => Context(flags).SortedSets().ZRem(key, members).AsTask();

    public long SortedSetRemoveRangeByRank(
        RedisKey key,
        long start,
        long stop,
        CommandFlags flags = CommandFlags.None)
        => Context(flags).SortedSets().ZRemRangeByRank(key, start, stop).Wait(SyncTimeout);

    public Task<long> SortedSetRemoveRangeByRankAsync(
        RedisKey key,
        long start,
        long stop,
        CommandFlags flags = CommandFlags.None)
        => Context(flags).SortedSets().ZRemRangeByRank(key, start, stop).AsTask();

    public long SortedSetRemoveRangeByScore(
        RedisKey key,
        double start,
        double stop,
        Exclude exclude = Exclude.None,
        CommandFlags flags = CommandFlags.None)
        => Context(flags).SortedSets().ZRemRangeByScore(key, SortedSetCommands.ZRangeRequest.ByScore(start, stop, exclude)).Wait(SyncTimeout);

    public Task<long> SortedSetRemoveRangeByScoreAsync(
        RedisKey key,
        double start,
        double stop,
        Exclude exclude = Exclude.None,
        CommandFlags flags = CommandFlags.None)
        => Context(flags).SortedSets().ZRemRangeByScore(key, SortedSetCommands.ZRangeRequest.ByScore(start, stop, exclude)).AsTask();

    public long SortedSetRemoveRangeByValue(
        RedisKey key,
        RedisValue min,
        RedisValue max,
        Exclude exclude = Exclude.None,
        CommandFlags flags = CommandFlags.None)
        => Context(flags).SortedSets().ZRemRangeByScore(key, SortedSetCommands.ZRangeRequest.ByLex(min, max, exclude)).Wait(SyncTimeout);

    public Task<long> SortedSetRemoveRangeByValueAsync(
        RedisKey key,
        RedisValue min,
        RedisValue max,
        Exclude exclude = Exclude.None,
        CommandFlags flags = CommandFlags.None)
        => Context(flags).SortedSets().ZRemRangeByScore(key, SortedSetCommands.ZRangeRequest.ByLex(min, max, exclude)).AsTask();

    public IEnumerable<SortedSetEntry>
        SortedSetScan(RedisKey key, RedisValue pattern, int pageSize, CommandFlags flags)
        => throw new NotImplementedException();

    public IEnumerable<SortedSetEntry> SortedSetScan(
        RedisKey key,
        RedisValue pattern = default,
        int pageSize = RedisBase.CursorUtils.DefaultLibraryPageSize,
        long cursor = RedisBase.CursorUtils.Origin,
        int pageOffset = 0,
        CommandFlags flags = CommandFlags.None)
        => throw new NotImplementedException();

    public IAsyncEnumerable<SortedSetEntry> SortedSetScanAsync(
        RedisKey key,
        RedisValue pattern = default,
        int pageSize = RedisBase.CursorUtils.DefaultLibraryPageSize,
        long cursor = RedisBase.CursorUtils.Origin,
        int pageOffset = 0,
        CommandFlags flags = CommandFlags.None)
        => throw new NotImplementedException();

    public double? SortedSetScore(RedisKey key, RedisValue member, CommandFlags flags = CommandFlags.None)
        => Context(flags).SortedSets().ZScore(key, member).Wait(SyncTimeout);

    public Task<double?> SortedSetScoreAsync(RedisKey key, RedisValue member, CommandFlags flags = CommandFlags.None)
        => Context(flags).SortedSets().ZScore(key, member).AsTask();

    public double?[] SortedSetScores(RedisKey key, RedisValue[] members, CommandFlags flags = CommandFlags.None)
        => Context(flags).SortedSets().ZScore(key, members).Wait(SyncTimeout);

    public Task<double?[]> SortedSetScoresAsync(
        RedisKey key,
        RedisValue[] members,
        CommandFlags flags = CommandFlags.None)
        => Context(flags).SortedSets().ZScore(key, members).AsTask();

    public bool SortedSetUpdate(
        RedisKey key,
        RedisValue member,
        double score,
        SortedSetWhen when = SortedSetWhen.Always,
        CommandFlags flags = CommandFlags.None)
        => Context(flags).SortedSets().ZAdd(key, when, member, score).Wait(SyncTimeout);

    public long SortedSetUpdate(
        RedisKey key,
        SortedSetEntry[] values,
        SortedSetWhen when = SortedSetWhen.Always,
        CommandFlags flags = CommandFlags.None)
        => Context(flags).SortedSets().ZAdd(key, when, values).Wait(SyncTimeout);

    public Task<bool> SortedSetUpdateAsync(
        RedisKey key,
        RedisValue member,
        double score,
        SortedSetWhen when = SortedSetWhen.Always,
        CommandFlags flags = CommandFlags.None)
        => Context(flags).SortedSets().ZAdd(key, when, member, score).AsTask();

    public Task<long> SortedSetUpdateAsync(
        RedisKey key,
        SortedSetEntry[] values,
        SortedSetWhen when = SortedSetWhen.Always,
        CommandFlags flags = CommandFlags.None)
        => Context(flags).SortedSets().ZAdd(key, when, values).AsTask();
}
