using System;

namespace StackExchange.Redis;

internal sealed partial class MultiGroupDatabase
{
    // SortedSet operations
    public bool SortedSetAdd(RedisKey key, RedisValue member, double score, CommandFlags flags)
        => GetActiveDatabase().SortedSetAdd(key, member, score, flags);

    public bool SortedSetAdd(RedisKey key, RedisValue member, double score, When when, CommandFlags flags = CommandFlags.None)
        => GetActiveDatabase().SortedSetAdd(key, member, score, when, flags);

    public bool SortedSetAdd(RedisKey key, RedisValue member, double score, SortedSetWhen when = SortedSetWhen.Always, CommandFlags flags = CommandFlags.None)
        => GetActiveDatabase().SortedSetAdd(key, member, score, when, flags);

    public long SortedSetAdd(RedisKey key, SortedSetEntry[] values, CommandFlags flags)
        => GetActiveDatabase().SortedSetAdd(key, values, flags);

    public long SortedSetAdd(RedisKey key, SortedSetEntry[] values, When when, CommandFlags flags = CommandFlags.None)
        => GetActiveDatabase().SortedSetAdd(key, values, when, flags);

    public long SortedSetAdd(RedisKey key, SortedSetEntry[] values, SortedSetWhen when = SortedSetWhen.Always, CommandFlags flags = CommandFlags.None)
        => GetActiveDatabase().SortedSetAdd(key, values, when, flags);

    public RedisValue[] SortedSetCombine(SetOperation operation, RedisKey[] keys, double[]? weights = null, Aggregate aggregate = Aggregate.Sum, CommandFlags flags = CommandFlags.None)
        => GetActiveDatabase().SortedSetCombine(operation, keys, weights, aggregate, flags);

    public SortedSetEntry[] SortedSetCombineWithScores(SetOperation operation, RedisKey[] keys, double[]? weights = null, Aggregate aggregate = Aggregate.Sum, CommandFlags flags = CommandFlags.None)
        => GetActiveDatabase().SortedSetCombineWithScores(operation, keys, weights, aggregate, flags);

    public long SortedSetCombineAndStore(SetOperation operation, RedisKey destination, RedisKey first, RedisKey second, Aggregate aggregate = Aggregate.Sum, CommandFlags flags = CommandFlags.None)
        => GetActiveDatabase().SortedSetCombineAndStore(operation, destination, first, second, aggregate, flags);

    public long SortedSetCombineAndStore(SetOperation operation, RedisKey destination, RedisKey[] keys, double[]? weights = null, Aggregate aggregate = Aggregate.Sum, CommandFlags flags = CommandFlags.None)
        => GetActiveDatabase().SortedSetCombineAndStore(operation, destination, keys, weights, aggregate, flags);

    public double SortedSetDecrement(RedisKey key, RedisValue member, double value, CommandFlags flags = CommandFlags.None)
        => GetActiveDatabase().SortedSetDecrement(key, member, value, flags);

    public double SortedSetIncrement(RedisKey key, RedisValue member, double value, CommandFlags flags = CommandFlags.None)
        => GetActiveDatabase().SortedSetIncrement(key, member, value, flags);

    public long SortedSetIntersectionLength(RedisKey[] keys, long limit = 0, CommandFlags flags = CommandFlags.None)
        => GetActiveDatabase().SortedSetIntersectionLength(keys, limit, flags);

    public long SortedSetLength(RedisKey key, double min = double.NegativeInfinity, double max = double.PositiveInfinity, Exclude exclude = Exclude.None, CommandFlags flags = CommandFlags.None)
        => GetActiveDatabase().SortedSetLength(key, min, max, exclude, flags);

    public long SortedSetLengthByValue(RedisKey key, RedisValue min, RedisValue max, Exclude exclude = Exclude.None, CommandFlags flags = CommandFlags.None)
        => GetActiveDatabase().SortedSetLengthByValue(key, min, max, exclude, flags);

    public RedisValue SortedSetRandomMember(RedisKey key, CommandFlags flags = CommandFlags.None)
        => GetActiveDatabase().SortedSetRandomMember(key, flags);

    public RedisValue[] SortedSetRandomMembers(RedisKey key, long count, CommandFlags flags = CommandFlags.None)
        => GetActiveDatabase().SortedSetRandomMembers(key, count, flags);

    public SortedSetEntry[] SortedSetRandomMembersWithScores(RedisKey key, long count, CommandFlags flags = CommandFlags.None)
        => GetActiveDatabase().SortedSetRandomMembersWithScores(key, count, flags);

    public RedisValue[] SortedSetRangeByRank(RedisKey key, long start = 0, long stop = -1, Order order = Order.Ascending, CommandFlags flags = CommandFlags.None)
        => GetActiveDatabase().SortedSetRangeByRank(key, start, stop, order, flags);

    public long SortedSetRangeAndStore(RedisKey sourceKey, RedisKey destinationKey, RedisValue start, RedisValue stop, SortedSetOrder sortedSetOrder = SortedSetOrder.ByRank, Exclude exclude = Exclude.None, Order order = Order.Ascending, long skip = 0, long? take = null, CommandFlags flags = CommandFlags.None)
        => GetActiveDatabase().SortedSetRangeAndStore(sourceKey, destinationKey, start, stop, sortedSetOrder, exclude, order, skip, take, flags);

    public SortedSetEntry[] SortedSetRangeByRankWithScores(RedisKey key, long start = 0, long stop = -1, Order order = Order.Ascending, CommandFlags flags = CommandFlags.None)
        => GetActiveDatabase().SortedSetRangeByRankWithScores(key, start, stop, order, flags);

    public RedisValue[] SortedSetRangeByScore(RedisKey key, double start = double.NegativeInfinity, double stop = double.PositiveInfinity, Exclude exclude = Exclude.None, Order order = Order.Ascending, long skip = 0, long take = -1, CommandFlags flags = CommandFlags.None)
        => GetActiveDatabase().SortedSetRangeByScore(key, start, stop, exclude, order, skip, take, flags);

    public SortedSetEntry[] SortedSetRangeByScoreWithScores(RedisKey key, double start = double.NegativeInfinity, double stop = double.PositiveInfinity, Exclude exclude = Exclude.None, Order order = Order.Ascending, long skip = 0, long take = -1, CommandFlags flags = CommandFlags.None)
        => GetActiveDatabase().SortedSetRangeByScoreWithScores(key, start, stop, exclude, order, skip, take, flags);

    public RedisValue[] SortedSetRangeByValue(RedisKey key, RedisValue min, RedisValue max, Exclude exclude, long skip, long take = -1, CommandFlags flags = CommandFlags.None)
        => GetActiveDatabase().SortedSetRangeByValue(key, min, max, exclude, skip, take, flags);

    public RedisValue[] SortedSetRangeByValue(RedisKey key, RedisValue min = default, RedisValue max = default, Exclude exclude = Exclude.None, Order order = Order.Ascending, long skip = 0, long take = -1, CommandFlags flags = CommandFlags.None)
        => GetActiveDatabase().SortedSetRangeByValue(key, min, max, exclude, order, skip, take, flags);

    public long? SortedSetRank(RedisKey key, RedisValue member, Order order = Order.Ascending, CommandFlags flags = CommandFlags.None)
        => GetActiveDatabase().SortedSetRank(key, member, order, flags);

    public bool SortedSetRemove(RedisKey key, RedisValue member, CommandFlags flags = CommandFlags.None)
        => GetActiveDatabase().SortedSetRemove(key, member, flags);

    public long SortedSetRemove(RedisKey key, RedisValue[] members, CommandFlags flags = CommandFlags.None)
        => GetActiveDatabase().SortedSetRemove(key, members, flags);

    public long SortedSetRemoveRangeByRank(RedisKey key, long start, long stop, CommandFlags flags = CommandFlags.None)
        => GetActiveDatabase().SortedSetRemoveRangeByRank(key, start, stop, flags);

    public long SortedSetRemoveRangeByScore(RedisKey key, double start, double stop, Exclude exclude = Exclude.None, CommandFlags flags = CommandFlags.None)
        => GetActiveDatabase().SortedSetRemoveRangeByScore(key, start, stop, exclude, flags);

    public long SortedSetRemoveRangeByValue(RedisKey key, RedisValue min, RedisValue max, Exclude exclude = Exclude.None, CommandFlags flags = CommandFlags.None)
        => GetActiveDatabase().SortedSetRemoveRangeByValue(key, min, max, exclude, flags);

    public System.Collections.Generic.IEnumerable<SortedSetEntry> SortedSetScan(RedisKey key, RedisValue pattern, int pageSize, CommandFlags flags)
        => GetActiveDatabase().SortedSetScan(key, pattern, pageSize, flags);

    public System.Collections.Generic.IEnumerable<SortedSetEntry> SortedSetScan(RedisKey key, RedisValue pattern = default, int pageSize = RedisBase.CursorUtils.DefaultLibraryPageSize, long cursor = RedisBase.CursorUtils.Origin, int pageOffset = 0, CommandFlags flags = CommandFlags.None)
        => GetActiveDatabase().SortedSetScan(key, pattern, pageSize, cursor, pageOffset, flags);

    public double? SortedSetScore(RedisKey key, RedisValue member, CommandFlags flags = CommandFlags.None)
        => GetActiveDatabase().SortedSetScore(key, member, flags);

    public double?[] SortedSetScores(RedisKey key, RedisValue[] members, CommandFlags flags = CommandFlags.None)
        => GetActiveDatabase().SortedSetScores(key, members, flags);

    public SortedSetEntry? SortedSetPop(RedisKey key, Order order = Order.Ascending, CommandFlags flags = CommandFlags.None)
        => GetActiveDatabase().SortedSetPop(key, order, flags);

    public SortedSetEntry[] SortedSetPop(RedisKey key, long count, Order order = Order.Ascending, CommandFlags flags = CommandFlags.None)
        => GetActiveDatabase().SortedSetPop(key, count, order, flags);

    public SortedSetPopResult SortedSetPop(RedisKey[] keys, long count, Order order = Order.Ascending, CommandFlags flags = CommandFlags.None)
        => GetActiveDatabase().SortedSetPop(keys, count, order, flags);

    public bool SortedSetUpdate(RedisKey key, RedisValue member, double score, SortedSetWhen when = SortedSetWhen.Always, CommandFlags flags = CommandFlags.None)
        => GetActiveDatabase().SortedSetUpdate(key, member, score, when, flags);

    public long SortedSetUpdate(RedisKey key, SortedSetEntry[] values, SortedSetWhen when = SortedSetWhen.Always, CommandFlags flags = CommandFlags.None)
        => GetActiveDatabase().SortedSetUpdate(key, values, when, flags);
}
