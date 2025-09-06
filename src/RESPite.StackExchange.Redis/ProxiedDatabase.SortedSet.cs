using StackExchange.Redis;

namespace RESPite.StackExchange.Redis;

internal partial class ProxiedDatabase
{
    // Async SortedSet methods
    public Task<bool> SortedSetAddAsync(
        RedisKey key,
        RedisValue member,
        double score,
        CommandFlags flags = CommandFlags.None) =>
        throw new NotImplementedException();

    public Task<bool> SortedSetAddAsync(
        RedisKey key,
        RedisValue member,
        double score,
        When when,
        CommandFlags flags = CommandFlags.None) =>
        throw new NotImplementedException();

    public Task<bool> SortedSetAddAsync(
        RedisKey key,
        RedisValue member,
        double score,
        SortedSetWhen when = SortedSetWhen.Always,
        CommandFlags flags = CommandFlags.None) =>
        throw new NotImplementedException();

    public Task<long> SortedSetAddAsync(
        RedisKey key,
        SortedSetEntry[] values,
        CommandFlags flags = CommandFlags.None) =>
        throw new NotImplementedException();

    public Task<long> SortedSetAddAsync(
        RedisKey key,
        SortedSetEntry[] values,
        When when,
        CommandFlags flags = CommandFlags.None) =>
        throw new NotImplementedException();

    public Task<long> SortedSetAddAsync(
        RedisKey key,
        SortedSetEntry[] values,
        SortedSetWhen when = SortedSetWhen.Always,
        CommandFlags flags = CommandFlags.None) =>
        throw new NotImplementedException();

    public Task<RedisValue[]> SortedSetCombineAsync(
        SetOperation operation,
        RedisKey[] keys,
        double[]? weights = null,
        Aggregate aggregate = Aggregate.Sum,
        CommandFlags flags = CommandFlags.None) =>
        throw new NotImplementedException();

    public Task<SortedSetEntry[]> SortedSetCombineWithScoresAsync(
        SetOperation operation,
        RedisKey[] keys,
        double[]? weights = null,
        Aggregate aggregate = Aggregate.Sum,
        CommandFlags flags = CommandFlags.None) =>
        throw new NotImplementedException();

    public Task<long> SortedSetCombineAndStoreAsync(
        SetOperation operation,
        RedisKey destination,
        RedisKey first,
        RedisKey second,
        Aggregate aggregate = Aggregate.Sum,
        CommandFlags flags = CommandFlags.None) =>
        throw new NotImplementedException();

    public Task<long> SortedSetCombineAndStoreAsync(
        SetOperation operation,
        RedisKey destination,
        RedisKey[] keys,
        double[]? weights = null,
        Aggregate aggregate = Aggregate.Sum,
        CommandFlags flags = CommandFlags.None) =>
        throw new NotImplementedException();

    public Task<double> SortedSetDecrementAsync(
        RedisKey key,
        RedisValue member,
        double value,
        CommandFlags flags = CommandFlags.None) =>
        throw new NotImplementedException();

    public Task<double> SortedSetIncrementAsync(
        RedisKey key,
        RedisValue member,
        double value,
        CommandFlags flags = CommandFlags.None) =>
        throw new NotImplementedException();

    public Task<long> SortedSetIntersectionLengthAsync(
        RedisKey[] keys,
        long limit = 0,
        CommandFlags flags = CommandFlags.None) =>
        throw new NotImplementedException();

    public Task<long> SortedSetLengthAsync(
        RedisKey key,
        double min = double.NegativeInfinity,
        double max = double.PositiveInfinity,
        Exclude exclude = Exclude.None,
        CommandFlags flags = CommandFlags.None) =>
        throw new NotImplementedException();

    public Task<long> SortedSetLengthByValueAsync(
        RedisKey key,
        RedisValue min,
        RedisValue max,
        Exclude exclude = Exclude.None,
        CommandFlags flags = CommandFlags.None) =>
        throw new NotImplementedException();

    public Task<RedisValue> SortedSetRandomMemberAsync(RedisKey key, CommandFlags flags = CommandFlags.None) =>
        throw new NotImplementedException();

    public Task<RedisValue[]> SortedSetRandomMembersAsync(
        RedisKey key,
        long count,
        CommandFlags flags = CommandFlags.None) =>
        throw new NotImplementedException();

    public Task<SortedSetEntry[]> SortedSetRandomMembersWithScoresAsync(
        RedisKey key,
        long count,
        CommandFlags flags = CommandFlags.None) =>
        throw new NotImplementedException();

    public Task<RedisValue[]> SortedSetRangeByRankAsync(
        RedisKey key,
        long start = 0,
        long stop = -1,
        Order order = Order.Ascending,
        CommandFlags flags = CommandFlags.None) =>
        throw new NotImplementedException();

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
        CommandFlags flags = CommandFlags.None) =>
        throw new NotImplementedException();

    public Task<SortedSetEntry[]> SortedSetRangeByRankWithScoresAsync(
        RedisKey key,
        long start = 0,
        long stop = -1,
        Order order = Order.Ascending,
        CommandFlags flags = CommandFlags.None) =>
        throw new NotImplementedException();

    public Task<RedisValue[]> SortedSetRangeByScoreAsync(
        RedisKey key,
        double start = double.NegativeInfinity,
        double stop = double.PositiveInfinity,
        Exclude exclude = Exclude.None,
        Order order = Order.Ascending,
        long skip = 0,
        long take = -1,
        CommandFlags flags = CommandFlags.None) =>
        throw new NotImplementedException();

    public Task<SortedSetEntry[]> SortedSetRangeByScoreWithScoresAsync(
        RedisKey key,
        double start = double.NegativeInfinity,
        double stop = double.PositiveInfinity,
        Exclude exclude = Exclude.None,
        Order order = Order.Ascending,
        long skip = 0,
        long take = -1,
        CommandFlags flags = CommandFlags.None) =>
        throw new NotImplementedException();

    public Task<RedisValue[]> SortedSetRangeByValueAsync(
        RedisKey key,
        RedisValue min = default,
        RedisValue max = default,
        Exclude exclude = Exclude.None,
        long skip = 0,
        long take = -1,
        CommandFlags flags = CommandFlags.None) =>
        throw new NotImplementedException();

    public Task<RedisValue[]> SortedSetRangeByValueAsync(
        RedisKey key,
        RedisValue min,
        RedisValue max,
        Exclude exclude,
        Order order,
        long skip = 0,
        long take = -1,
        CommandFlags flags = CommandFlags.None) =>
        throw new NotImplementedException();

    public Task<long?> SortedSetRankAsync(
        RedisKey key,
        RedisValue member,
        Order order = Order.Ascending,
        CommandFlags flags = CommandFlags.None) =>
        throw new NotImplementedException();

    public Task<bool> SortedSetRemoveAsync(RedisKey key, RedisValue member, CommandFlags flags = CommandFlags.None) =>
        throw new NotImplementedException();

    public Task<long> SortedSetRemoveAsync(
        RedisKey key,
        RedisValue[] members,
        CommandFlags flags = CommandFlags.None) =>
        throw new NotImplementedException();

    public Task<long> SortedSetRemoveRangeByRankAsync(
        RedisKey key,
        long start,
        long stop,
        CommandFlags flags = CommandFlags.None) =>
        throw new NotImplementedException();

    public Task<long> SortedSetRemoveRangeByScoreAsync(
        RedisKey key,
        double start,
        double stop,
        Exclude exclude = Exclude.None,
        CommandFlags flags = CommandFlags.None) =>
        throw new NotImplementedException();

    public Task<long> SortedSetRemoveRangeByValueAsync(
        RedisKey key,
        RedisValue min,
        RedisValue max,
        Exclude exclude = Exclude.None,
        CommandFlags flags = CommandFlags.None) =>
        throw new NotImplementedException();

    public IAsyncEnumerable<SortedSetEntry> SortedSetScanAsync(
        RedisKey key,
        RedisValue pattern = default,
        int pageSize = RedisBase.CursorUtils.DefaultLibraryPageSize,
        long cursor = RedisBase.CursorUtils.Origin,
        int pageOffset = 0,
        CommandFlags flags = CommandFlags.None) =>
        throw new NotImplementedException();

    public Task<double?> SortedSetScoreAsync(RedisKey key, RedisValue member, CommandFlags flags = CommandFlags.None) =>
        throw new NotImplementedException();

    public Task<double?[]> SortedSetScoresAsync(
        RedisKey key,
        RedisValue[] members,
        CommandFlags flags = CommandFlags.None) =>
        throw new NotImplementedException();

    public Task<SortedSetEntry?> SortedSetPopAsync(
        RedisKey key,
        Order order = Order.Ascending,
        CommandFlags flags = CommandFlags.None) =>
        throw new NotImplementedException();

    public Task<SortedSetEntry[]> SortedSetPopAsync(
        RedisKey key,
        long count,
        Order order = Order.Ascending,
        CommandFlags flags = CommandFlags.None) =>
        throw new NotImplementedException();

    public Task<SortedSetPopResult> SortedSetPopAsync(
        RedisKey[] keys,
        long count,
        Order order = Order.Ascending,
        CommandFlags flags = CommandFlags.None) =>
        throw new NotImplementedException();

    public Task<bool> SortedSetUpdateAsync(
        RedisKey key,
        RedisValue member,
        double score,
        SortedSetWhen when = SortedSetWhen.Always,
        CommandFlags flags = CommandFlags.None) =>
        throw new NotImplementedException();

    public Task<long> SortedSetUpdateAsync(
        RedisKey key,
        SortedSetEntry[] values,
        SortedSetWhen when = SortedSetWhen.Always,
        CommandFlags flags = CommandFlags.None) =>
        throw new NotImplementedException();

    // Synchronous SortedSet methods
    public bool SortedSetAdd(RedisKey key, RedisValue member, double score, CommandFlags flags = CommandFlags.None) =>
        throw new NotImplementedException();

    public bool SortedSetAdd(
        RedisKey key,
        RedisValue member,
        double score,
        When when,
        CommandFlags flags = CommandFlags.None) =>
        throw new NotImplementedException();

    public bool SortedSetAdd(
        RedisKey key,
        RedisValue member,
        double score,
        SortedSetWhen when = SortedSetWhen.Always,
        CommandFlags flags = CommandFlags.None) =>
        throw new NotImplementedException();

    public long SortedSetAdd(RedisKey key, SortedSetEntry[] values, CommandFlags flags = CommandFlags.None) =>
        throw new NotImplementedException();

    public long SortedSetAdd(
        RedisKey key,
        SortedSetEntry[] values,
        When when,
        CommandFlags flags = CommandFlags.None) =>
        throw new NotImplementedException();

    public long SortedSetAdd(
        RedisKey key,
        SortedSetEntry[] values,
        SortedSetWhen when = SortedSetWhen.Always,
        CommandFlags flags = CommandFlags.None) =>
        throw new NotImplementedException();

    public RedisValue[] SortedSetCombine(
        SetOperation operation,
        RedisKey[] keys,
        double[]? weights = null,
        Aggregate aggregate = Aggregate.Sum,
        CommandFlags flags = CommandFlags.None) =>
        throw new NotImplementedException();

    public SortedSetEntry[] SortedSetCombineWithScores(
        SetOperation operation,
        RedisKey[] keys,
        double[]? weights = null,
        Aggregate aggregate = Aggregate.Sum,
        CommandFlags flags = CommandFlags.None) =>
        throw new NotImplementedException();

    public long SortedSetCombineAndStore(
        SetOperation operation,
        RedisKey destination,
        RedisKey first,
        RedisKey second,
        Aggregate aggregate = Aggregate.Sum,
        CommandFlags flags = CommandFlags.None) =>
        throw new NotImplementedException();

    public long SortedSetCombineAndStore(
        SetOperation operation,
        RedisKey destination,
        RedisKey[] keys,
        double[]? weights = null,
        Aggregate aggregate = Aggregate.Sum,
        CommandFlags flags = CommandFlags.None) =>
        throw new NotImplementedException();

    public double SortedSetDecrement(
        RedisKey key,
        RedisValue member,
        double value,
        CommandFlags flags = CommandFlags.None) =>
        throw new NotImplementedException();

    public double SortedSetIncrement(
        RedisKey key,
        RedisValue member,
        double value,
        CommandFlags flags = CommandFlags.None) =>
        throw new NotImplementedException();

    public long SortedSetIntersectionLength(RedisKey[] keys, long limit = 0, CommandFlags flags = CommandFlags.None) =>
        throw new NotImplementedException();

    public long SortedSetLength(
        RedisKey key,
        double min = double.NegativeInfinity,
        double max = double.PositiveInfinity,
        Exclude exclude = Exclude.None,
        CommandFlags flags = CommandFlags.None) =>
        throw new NotImplementedException();

    public long SortedSetLengthByValue(
        RedisKey key,
        RedisValue min,
        RedisValue max,
        Exclude exclude = Exclude.None,
        CommandFlags flags = CommandFlags.None) =>
        throw new NotImplementedException();

    public RedisValue SortedSetRandomMember(RedisKey key, CommandFlags flags = CommandFlags.None) =>
        throw new NotImplementedException();

    public RedisValue[] SortedSetRandomMembers(RedisKey key, long count, CommandFlags flags = CommandFlags.None) =>
        throw new NotImplementedException();

    public SortedSetEntry[] SortedSetRandomMembersWithScores(
        RedisKey key,
        long count,
        CommandFlags flags = CommandFlags.None) =>
        throw new NotImplementedException();

    public RedisValue[] SortedSetRangeByRank(
        RedisKey key,
        long start = 0,
        long stop = -1,
        Order order = Order.Ascending,
        CommandFlags flags = CommandFlags.None) =>
        throw new NotImplementedException();

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
        CommandFlags flags = CommandFlags.None) =>
        throw new NotImplementedException();

    public SortedSetEntry[] SortedSetRangeByRankWithScores(
        RedisKey key,
        long start = 0,
        long stop = -1,
        Order order = Order.Ascending,
        CommandFlags flags = CommandFlags.None) =>
        throw new NotImplementedException();

    public RedisValue[] SortedSetRangeByScore(
        RedisKey key,
        double start = double.NegativeInfinity,
        double stop = double.PositiveInfinity,
        Exclude exclude = Exclude.None,
        Order order = Order.Ascending,
        long skip = 0,
        long take = -1,
        CommandFlags flags = CommandFlags.None) =>
        throw new NotImplementedException();

    public SortedSetEntry[] SortedSetRangeByScoreWithScores(
        RedisKey key,
        double start = double.NegativeInfinity,
        double stop = double.PositiveInfinity,
        Exclude exclude = Exclude.None,
        Order order = Order.Ascending,
        long skip = 0,
        long take = -1,
        CommandFlags flags = CommandFlags.None) =>
        throw new NotImplementedException();

    public RedisValue[] SortedSetRangeByValue(
        RedisKey key,
        RedisValue min = default,
        RedisValue max = default,
        Exclude exclude = Exclude.None,
        long skip = 0,
        long take = -1,
        CommandFlags flags = CommandFlags.None) =>
        throw new NotImplementedException();

    public RedisValue[] SortedSetRangeByValue(
        RedisKey key,
        RedisValue min,
        RedisValue max,
        Exclude exclude,
        Order order,
        long skip = 0,
        long take = -1,
        CommandFlags flags = CommandFlags.None) =>
        throw new NotImplementedException();

    public long? SortedSetRank(
        RedisKey key,
        RedisValue member,
        Order order = Order.Ascending,
        CommandFlags flags = CommandFlags.None) =>
        throw new NotImplementedException();

    public bool SortedSetRemove(RedisKey key, RedisValue member, CommandFlags flags = CommandFlags.None) =>
        throw new NotImplementedException();

    public long SortedSetRemove(RedisKey key, RedisValue[] members, CommandFlags flags = CommandFlags.None) =>
        throw new NotImplementedException();

    public long SortedSetRemoveRangeByRank(
        RedisKey key,
        long start,
        long stop,
        CommandFlags flags = CommandFlags.None) =>
        throw new NotImplementedException();

    public long SortedSetRemoveRangeByScore(
        RedisKey key,
        double start,
        double stop,
        Exclude exclude = Exclude.None,
        CommandFlags flags = CommandFlags.None) =>
        throw new NotImplementedException();

    public long SortedSetRemoveRangeByValue(
        RedisKey key,
        RedisValue min,
        RedisValue max,
        Exclude exclude = Exclude.None,
        CommandFlags flags = CommandFlags.None) =>
        throw new NotImplementedException();

    public IEnumerable<SortedSetEntry>
        SortedSetScan(RedisKey key, RedisValue pattern, int pageSize, CommandFlags flags) =>
        throw new NotImplementedException();

    public IEnumerable<SortedSetEntry> SortedSetScan(
        RedisKey key,
        RedisValue pattern = default,
        int pageSize = RedisBase.CursorUtils.DefaultLibraryPageSize,
        long cursor = RedisBase.CursorUtils.Origin,
        int pageOffset = 0,
        CommandFlags flags = CommandFlags.None) =>
        throw new NotImplementedException();

    public double? SortedSetScore(RedisKey key, RedisValue member, CommandFlags flags = CommandFlags.None) =>
        throw new NotImplementedException();

    public double?[] SortedSetScores(RedisKey key, RedisValue[] members, CommandFlags flags = CommandFlags.None) =>
        throw new NotImplementedException();

    public SortedSetEntry? SortedSetPop(
        RedisKey key,
        Order order = Order.Ascending,
        CommandFlags flags = CommandFlags.None) =>
        throw new NotImplementedException();

    public SortedSetEntry[] SortedSetPop(
        RedisKey key,
        long count,
        Order order = Order.Ascending,
        CommandFlags flags = CommandFlags.None) =>
        throw new NotImplementedException();

    public SortedSetPopResult SortedSetPop(
        RedisKey[] keys,
        long count,
        Order order = Order.Ascending,
        CommandFlags flags = CommandFlags.None) =>
        throw new NotImplementedException();

    public bool SortedSetUpdate(
        RedisKey key,
        RedisValue member,
        double score,
        SortedSetWhen when = SortedSetWhen.Always,
        CommandFlags flags = CommandFlags.None) =>
        throw new NotImplementedException();

    public long SortedSetUpdate(
        RedisKey key,
        SortedSetEntry[] values,
        SortedSetWhen when = SortedSetWhen.Always,
        CommandFlags flags = CommandFlags.None) =>
        throw new NotImplementedException();
}
