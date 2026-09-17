using System;
using System.Threading.Tasks;

namespace StackExchange.Redis
{
    /// <summary>
    /// The sorted-set commands, where they have moved to the RESP context surface.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The biggest adapter file, and mostly for one reason: <c>SortedSetAdd</c> alone has six overloads
    /// (three arities of condition across single and multiple members), <c>SortedSetUpdate</c> is the same
    /// command with <c>CH</c>, and <c>SortedSetIncrement</c>/<c>SortedSetDecrement</c> are the same command
    /// again with <c>INCR</c> and a sign. All of them land on three group methods.
    /// </para>
    /// <para>
    /// <c>ZSCAN</c> stays in <c>TransitionalDatabase.Scans.cs</c>.
    /// </para>
    /// </remarks>
    internal sealed partial class TransitionalDatabase
    {
        // ---- membership --------------------------------------------------------------------------------
        // `When` converts to SortedSetWhen, and the CommandFlags-only overloads are the pre-`When` shapes;
        // both are pure adapters with no second opinion about semantics

        /// <inheritdoc/>
        public bool SortedSetAdd(RedisKey key, RedisValue member, double score, CommandFlags flags)
            => SortedSetAdd(key, member, score, SortedSetWhen.Always, flags);

        /// <inheritdoc/>
        public Task<bool> SortedSetAddAsync(RedisKey key, RedisValue member, double score, CommandFlags flags)
            => SortedSetAddAsync(key, member, score, SortedSetWhen.Always, flags);

        /// <inheritdoc/>
        public bool SortedSetAdd(RedisKey key, RedisValue member, double score, When when, CommandFlags flags = CommandFlags.None)
            => SortedSetAdd(key, member, score, SortedSetWhenExtensions.Parse(when), flags);

        /// <inheritdoc/>
        public Task<bool> SortedSetAddAsync(RedisKey key, RedisValue member, double score, When when, CommandFlags flags = CommandFlags.None)
            => SortedSetAddAsync(key, member, score, SortedSetWhenExtensions.Parse(when), flags);

        /// <inheritdoc/>
        public bool SortedSetAdd(RedisKey key, RedisValue member, double score, SortedSetWhen when = SortedSetWhen.Always, CommandFlags flags = CommandFlags.None)
            => Wait(Context.SortedSets.AddAsync(key, member, score, when, change: false, flags));

        /// <inheritdoc/>
        public Task<bool> SortedSetAddAsync(RedisKey key, RedisValue member, double score, SortedSetWhen when = SortedSetWhen.Always, CommandFlags flags = CommandFlags.None)
            => Context.SortedSets.AddAsync(key, member, score, when, change: false, flags).AsTask();

        /// <inheritdoc/>
        public long SortedSetAdd(RedisKey key, SortedSetEntry[] values, CommandFlags flags)
            => SortedSetAdd(key, values, SortedSetWhen.Always, flags);

        /// <inheritdoc/>
        public Task<long> SortedSetAddAsync(RedisKey key, SortedSetEntry[] values, CommandFlags flags)
            => SortedSetAddAsync(key, values, SortedSetWhen.Always, flags);

        /// <inheritdoc/>
        public long SortedSetAdd(RedisKey key, SortedSetEntry[] values, When when, CommandFlags flags = CommandFlags.None)
            => SortedSetAdd(key, values, SortedSetWhenExtensions.Parse(when), flags);

        /// <inheritdoc/>
        public Task<long> SortedSetAddAsync(RedisKey key, SortedSetEntry[] values, When when, CommandFlags flags = CommandFlags.None)
            => SortedSetAddAsync(key, values, SortedSetWhenExtensions.Parse(when), flags);

        /// <inheritdoc/>
        public long SortedSetAdd(RedisKey key, SortedSetEntry[] values, SortedSetWhen when = SortedSetWhen.Always, CommandFlags flags = CommandFlags.None)
            => Wait(Context.SortedSets.AddAsync(key, Required(values, nameof(values)), when, change: false, flags));

        /// <inheritdoc/>
        public Task<long> SortedSetAddAsync(RedisKey key, SortedSetEntry[] values, SortedSetWhen when = SortedSetWhen.Always, CommandFlags flags = CommandFlags.None)
            => Context.SortedSets.AddAsync(key, Required(values, nameof(values)), when, change: false, flags).AsTask();

        // SortedSetUpdate is SortedSetAdd with CH - the same command, counting changed members rather than
        // new ones, which is a parameter on the group method

        /// <inheritdoc/>
        public bool SortedSetUpdate(RedisKey key, RedisValue member, double score, SortedSetWhen when = SortedSetWhen.Always, CommandFlags flags = CommandFlags.None)
            => Wait(Context.SortedSets.AddAsync(key, member, score, when, change: true, flags));

        /// <inheritdoc/>
        public Task<bool> SortedSetUpdateAsync(RedisKey key, RedisValue member, double score, SortedSetWhen when = SortedSetWhen.Always, CommandFlags flags = CommandFlags.None)
            => Context.SortedSets.AddAsync(key, member, score, when, change: true, flags).AsTask();

        /// <inheritdoc/>
        public long SortedSetUpdate(RedisKey key, SortedSetEntry[] values, SortedSetWhen when = SortedSetWhen.Always, CommandFlags flags = CommandFlags.None)
            => Wait(Context.SortedSets.AddAsync(key, Required(values, nameof(values)), when, change: true, flags));

        /// <inheritdoc/>
        public Task<long> SortedSetUpdateAsync(RedisKey key, SortedSetEntry[] values, SortedSetWhen when = SortedSetWhen.Always, CommandFlags flags = CommandFlags.None)
            => Context.SortedSets.AddAsync(key, Required(values, nameof(values)), when, change: true, flags).AsTask();

        // an unconditional increment cannot be refused, so the old signature's non-nullable double is safe;
        // the conditional overload is the one that reports null, and it already says so

        /// <inheritdoc/>
        public double SortedSetIncrement(RedisKey key, RedisValue member, double value, CommandFlags flags = CommandFlags.None)
            => Wait(Context.SortedSets.IncrementAsync(key, member, value, SortedSetWhen.Always, flags)).GetValueOrDefault();

        /// <inheritdoc/>
        public async Task<double> SortedSetIncrementAsync(RedisKey key, RedisValue member, double value, CommandFlags flags = CommandFlags.None)
            => (await Context.SortedSets.IncrementAsync(key, member, value, SortedSetWhen.Always, flags).ConfigureAwait(false)).GetValueOrDefault();

        /// <inheritdoc/>
        public double? SortedSetIncrement(RedisKey key, RedisValue member, double value, ValueCondition when, CommandFlags flags)
            => Wait(Context.SortedSets.IncrementAsync(key, member, value, AsSortedSetWhen(when), flags));

        /// <inheritdoc/>
        public Task<double?> SortedSetIncrementAsync(RedisKey key, RedisValue member, double value, ValueCondition when, CommandFlags flags)
            => Context.SortedSets.IncrementAsync(key, member, value, AsSortedSetWhen(when), flags).AsTask();

        /// <inheritdoc/>
        public double SortedSetDecrement(RedisKey key, RedisValue member, double value, CommandFlags flags = CommandFlags.None)
            => SortedSetIncrement(key, member, -value, flags);

        /// <inheritdoc/>
        public Task<double> SortedSetDecrementAsync(RedisKey key, RedisValue member, double value, CommandFlags flags = CommandFlags.None)
            => SortedSetIncrementAsync(key, member, -value, flags);

        /// <inheritdoc/>
        public bool SortedSetRemove(RedisKey key, RedisValue member, CommandFlags flags = CommandFlags.None)
            => Wait(Context.SortedSets.RemoveAsync(key, member, flags));

        /// <inheritdoc/>
        public Task<bool> SortedSetRemoveAsync(RedisKey key, RedisValue member, CommandFlags flags = CommandFlags.None)
            => Context.SortedSets.RemoveAsync(key, member, flags).AsTask();

        /// <inheritdoc/>
        public long SortedSetRemove(RedisKey key, RedisValue[] members, CommandFlags flags = CommandFlags.None)
            => Wait(Context.SortedSets.RemoveAsync(key, Required(members, nameof(members)), flags));

        /// <inheritdoc/>
        public Task<long> SortedSetRemoveAsync(RedisKey key, RedisValue[] members, CommandFlags flags = CommandFlags.None)
            => Context.SortedSets.RemoveAsync(key, Required(members, nameof(members)), flags).AsTask();

        // ---- simple reads ------------------------------------------------------------------------------

        /// <inheritdoc/>
        public double? SortedSetScore(RedisKey key, RedisValue member, CommandFlags flags = CommandFlags.None)
            => Wait(Context.SortedSets.ScoreAsync(key, member, flags));

        /// <inheritdoc/>
        public Task<double?> SortedSetScoreAsync(RedisKey key, RedisValue member, CommandFlags flags = CommandFlags.None)
            => Context.SortedSets.ScoreAsync(key, member, flags).AsTask();

        /// <inheritdoc/>
        public double?[] SortedSetScores(RedisKey key, RedisValue[] members, CommandFlags flags = CommandFlags.None)
            => Wait(Context.SortedSets.ScoresArray(key, Required(members, nameof(members)), flags));

        /// <inheritdoc/>
        public Task<double?[]> SortedSetScoresAsync(RedisKey key, RedisValue[] members, CommandFlags flags = CommandFlags.None)
            => Context.SortedSets.ScoresArray(key, Required(members, nameof(members)), flags).AsTask();

        /// <inheritdoc/>
        public long SortedSetLength(RedisKey key, double min = double.NegativeInfinity, double max = double.PositiveInfinity, Exclude exclude = Exclude.None, CommandFlags flags = CommandFlags.None)
            => Wait(Context.SortedSets.LengthAsync(key, min, max, exclude, flags));

        /// <inheritdoc/>
        public Task<long> SortedSetLengthAsync(RedisKey key, double min = double.NegativeInfinity, double max = double.PositiveInfinity, Exclude exclude = Exclude.None, CommandFlags flags = CommandFlags.None)
            => Context.SortedSets.LengthAsync(key, min, max, exclude, flags).AsTask();

        /// <inheritdoc/>
        public long SortedSetLengthByValue(RedisKey key, RedisValue min, RedisValue max, Exclude exclude = Exclude.None, CommandFlags flags = CommandFlags.None)
            => Wait(Context.SortedSets.LengthByValueAsync(key, min, max, exclude, flags));

        /// <inheritdoc/>
        public Task<long> SortedSetLengthByValueAsync(RedisKey key, RedisValue min, RedisValue max, Exclude exclude = Exclude.None, CommandFlags flags = CommandFlags.None)
            => Context.SortedSets.LengthByValueAsync(key, min, max, exclude, flags).AsTask();

        /// <inheritdoc/>
        public long? SortedSetRank(RedisKey key, RedisValue member, Order order = Order.Ascending, CommandFlags flags = CommandFlags.None)
            => Wait(Context.SortedSets.RankAsync(key, member, order, flags));

        /// <inheritdoc/>
        public Task<long?> SortedSetRankAsync(RedisKey key, RedisValue member, Order order = Order.Ascending, CommandFlags flags = CommandFlags.None)
            => Context.SortedSets.RankAsync(key, member, order, flags).AsTask();

        /// <inheritdoc/>
        public RedisValue SortedSetRandomMember(RedisKey key, CommandFlags flags = CommandFlags.None)
            => Wait(Context.SortedSets.RandomMemberAsync(key, flags));

        /// <inheritdoc/>
        public Task<RedisValue> SortedSetRandomMemberAsync(RedisKey key, CommandFlags flags = CommandFlags.None)
            => Context.SortedSets.RandomMemberAsync(key, flags).AsTask();

        /// <inheritdoc/>
        public RedisValue[] SortedSetRandomMembers(RedisKey key, long count, CommandFlags flags = CommandFlags.None)
            => Wait(Context.SortedSets.RandomMembersArray(key, count, flags));

        /// <inheritdoc/>
        public Task<RedisValue[]> SortedSetRandomMembersAsync(RedisKey key, long count, CommandFlags flags = CommandFlags.None)
            => Context.SortedSets.RandomMembersArray(key, count, flags).AsTask();

        /// <inheritdoc/>
        public SortedSetEntry[] SortedSetRandomMembersWithScores(RedisKey key, long count, CommandFlags flags = CommandFlags.None)
            => Wait(Context.SortedSets.RandomMembersWithScoresArray(key, count, flags));

        /// <inheritdoc/>
        public Task<SortedSetEntry[]> SortedSetRandomMembersWithScoresAsync(RedisKey key, long count, CommandFlags flags = CommandFlags.None)
            => Context.SortedSets.RandomMembersWithScoresArray(key, count, flags).AsTask();

        // ---- ranges ------------------------------------------------------------------------------------

        /// <inheritdoc/>
        public RedisValue[] SortedSetRangeByRank(RedisKey key, long start = 0, long stop = -1, Order order = Order.Ascending, CommandFlags flags = CommandFlags.None)
            => Wait(Context.SortedSets.RangeByRankArray(key, start, stop, order, flags));

        /// <inheritdoc/>
        public Task<RedisValue[]> SortedSetRangeByRankAsync(RedisKey key, long start = 0, long stop = -1, Order order = Order.Ascending, CommandFlags flags = CommandFlags.None)
            => Context.SortedSets.RangeByRankArray(key, start, stop, order, flags).AsTask();

        /// <inheritdoc/>
        public SortedSetEntry[] SortedSetRangeByRankWithScores(RedisKey key, long start = 0, long stop = -1, Order order = Order.Ascending, CommandFlags flags = CommandFlags.None)
            => Wait(Context.SortedSets.RangeByRankWithScoresArray(key, start, stop, order, flags));

        /// <inheritdoc/>
        public Task<SortedSetEntry[]> SortedSetRangeByRankWithScoresAsync(RedisKey key, long start = 0, long stop = -1, Order order = Order.Ascending, CommandFlags flags = CommandFlags.None)
            => Context.SortedSets.RangeByRankWithScoresArray(key, start, stop, order, flags).AsTask();

        /// <inheritdoc/>
        public RedisValue[] SortedSetRangeByScore(RedisKey key, double start = double.NegativeInfinity, double stop = double.PositiveInfinity, Exclude exclude = Exclude.None, Order order = Order.Ascending, long skip = 0, long take = -1, CommandFlags flags = CommandFlags.None)
            => Wait(Context.SortedSets.RangeByScoreArray(key, start, stop, exclude, order, skip, take, flags));

        /// <inheritdoc/>
        public Task<RedisValue[]> SortedSetRangeByScoreAsync(RedisKey key, double start = double.NegativeInfinity, double stop = double.PositiveInfinity, Exclude exclude = Exclude.None, Order order = Order.Ascending, long skip = 0, long take = -1, CommandFlags flags = CommandFlags.None)
            => Context.SortedSets.RangeByScoreArray(key, start, stop, exclude, order, skip, take, flags).AsTask();

        /// <inheritdoc/>
        public SortedSetEntry[] SortedSetRangeByScoreWithScores(RedisKey key, double start = double.NegativeInfinity, double stop = double.PositiveInfinity, Exclude exclude = Exclude.None, Order order = Order.Ascending, long skip = 0, long take = -1, CommandFlags flags = CommandFlags.None)
            => Wait(Context.SortedSets.RangeByScoreWithScoresArray(key, start, stop, exclude, order, skip, take, flags));

        /// <inheritdoc/>
        public Task<SortedSetEntry[]> SortedSetRangeByScoreWithScoresAsync(RedisKey key, double start = double.NegativeInfinity, double stop = double.PositiveInfinity, Exclude exclude = Exclude.None, Order order = Order.Ascending, long skip = 0, long take = -1, CommandFlags flags = CommandFlags.None)
            => Context.SortedSets.RangeByScoreWithScoresArray(key, start, stop, exclude, order, skip, take, flags).AsTask();

        /// <inheritdoc/>
        public RedisValue[] SortedSetRangeByValue(RedisKey key, RedisValue min, RedisValue max, Exclude exclude, long skip, long take = -1, CommandFlags flags = CommandFlags.None)
            => SortedSetRangeByValue(key, min, max, exclude, Order.Ascending, skip, take, flags);

        /// <inheritdoc/>
        public Task<RedisValue[]> SortedSetRangeByValueAsync(RedisKey key, RedisValue min, RedisValue max, Exclude exclude, long skip, long take = -1, CommandFlags flags = CommandFlags.None)
            => SortedSetRangeByValueAsync(key, min, max, exclude, Order.Ascending, skip, take, flags);

        /// <inheritdoc/>
        public RedisValue[] SortedSetRangeByValue(RedisKey key, RedisValue min = default, RedisValue max = default, Exclude exclude = Exclude.None, Order order = Order.Ascending, long skip = 0, long take = -1, CommandFlags flags = CommandFlags.None)
            => Wait(Context.SortedSets.RangeByValueArray(key, min, max, exclude, order, skip, take, flags));

        /// <inheritdoc/>
        public Task<RedisValue[]> SortedSetRangeByValueAsync(RedisKey key, RedisValue min = default, RedisValue max = default, Exclude exclude = Exclude.None, Order order = Order.Ascending, long skip = 0, long take = -1, CommandFlags flags = CommandFlags.None)
            => Context.SortedSets.RangeByValueArray(key, min, max, exclude, order, skip, take, flags).AsTask();

        /// <inheritdoc/>
        public long SortedSetRangeAndStore(RedisKey sourceKey, RedisKey destinationKey, RedisValue start, RedisValue stop, SortedSetOrder sortedSetOrder = SortedSetOrder.ByRank, Exclude exclude = Exclude.None, Order order = Order.Ascending, long skip = 0, long? take = null, CommandFlags flags = CommandFlags.None)
            => Wait(Context.SortedSets.RangeAndStoreAsync(sourceKey, destinationKey, start, stop, sortedSetOrder, exclude, order, skip, take, flags));

        /// <inheritdoc/>
        public Task<long> SortedSetRangeAndStoreAsync(RedisKey sourceKey, RedisKey destinationKey, RedisValue start, RedisValue stop, SortedSetOrder sortedSetOrder = SortedSetOrder.ByRank, Exclude exclude = Exclude.None, Order order = Order.Ascending, long skip = 0, long? take = null, CommandFlags flags = CommandFlags.None)
            => Context.SortedSets.RangeAndStoreAsync(sourceKey, destinationKey, start, stop, sortedSetOrder, exclude, order, skip, take, flags).AsTask();

        // ---- removal by range --------------------------------------------------------------------------

        /// <inheritdoc/>
        public long SortedSetRemoveRangeByRank(RedisKey key, long start, long stop, CommandFlags flags = CommandFlags.None)
            => Wait(Context.SortedSets.RemoveRangeByRankAsync(key, start, stop, flags));

        /// <inheritdoc/>
        public Task<long> SortedSetRemoveRangeByRankAsync(RedisKey key, long start, long stop, CommandFlags flags = CommandFlags.None)
            => Context.SortedSets.RemoveRangeByRankAsync(key, start, stop, flags).AsTask();

        /// <inheritdoc/>
        public long SortedSetRemoveRangeByScore(RedisKey key, double start, double stop, Exclude exclude = Exclude.None, CommandFlags flags = CommandFlags.None)
            => Wait(Context.SortedSets.RemoveRangeByScoreAsync(key, start, stop, exclude, flags));

        /// <inheritdoc/>
        public Task<long> SortedSetRemoveRangeByScoreAsync(RedisKey key, double start, double stop, Exclude exclude = Exclude.None, CommandFlags flags = CommandFlags.None)
            => Context.SortedSets.RemoveRangeByScoreAsync(key, start, stop, exclude, flags).AsTask();

        /// <inheritdoc/>
        public long SortedSetRemoveRangeByValue(RedisKey key, RedisValue min, RedisValue max, Exclude exclude = Exclude.None, CommandFlags flags = CommandFlags.None)
            => Wait(Context.SortedSets.RemoveRangeByValueAsync(key, min, max, exclude, flags));

        /// <inheritdoc/>
        public Task<long> SortedSetRemoveRangeByValueAsync(RedisKey key, RedisValue min, RedisValue max, Exclude exclude = Exclude.None, CommandFlags flags = CommandFlags.None)
            => Context.SortedSets.RemoveRangeByValueAsync(key, min, max, exclude, flags).AsTask();

        // ---- combinations ------------------------------------------------------------------------------

        /// <inheritdoc/>
        public RedisValue[] SortedSetCombine(SetOperation operation, RedisKey[] keys, double[]? weights = null, Aggregate aggregate = Aggregate.Sum, CommandFlags flags = CommandFlags.None)
            => Wait(Context.SortedSets.CombineArray(operation, Required(keys, nameof(keys)), weights, aggregate, flags));

        /// <inheritdoc/>
        public Task<RedisValue[]> SortedSetCombineAsync(SetOperation operation, RedisKey[] keys, double[]? weights = null, Aggregate aggregate = Aggregate.Sum, CommandFlags flags = CommandFlags.None)
            => Context.SortedSets.CombineArray(operation, Required(keys, nameof(keys)), weights, aggregate, flags).AsTask();

        /// <inheritdoc/>
        public SortedSetEntry[] SortedSetCombineWithScores(SetOperation operation, RedisKey[] keys, double[]? weights = null, Aggregate aggregate = Aggregate.Sum, CommandFlags flags = CommandFlags.None)
            => Wait(Context.SortedSets.CombineWithScoresArray(operation, Required(keys, nameof(keys)), weights, aggregate, flags));

        /// <inheritdoc/>
        public Task<SortedSetEntry[]> SortedSetCombineWithScoresAsync(SetOperation operation, RedisKey[] keys, double[]? weights = null, Aggregate aggregate = Aggregate.Sum, CommandFlags flags = CommandFlags.None)
            => Context.SortedSets.CombineWithScoresArray(operation, Required(keys, nameof(keys)), weights, aggregate, flags).AsTask();

        /// <inheritdoc/>
        public long SortedSetCombineAndStore(SetOperation operation, RedisKey destination, RedisKey first, RedisKey second, Aggregate aggregate = Aggregate.Sum, CommandFlags flags = CommandFlags.None)
            => Wait(Context.SortedSets.CombineAndStoreAsync(operation, destination, [first, second], default, aggregate, flags));

        /// <inheritdoc/>
        public Task<long> SortedSetCombineAndStoreAsync(SetOperation operation, RedisKey destination, RedisKey first, RedisKey second, Aggregate aggregate = Aggregate.Sum, CommandFlags flags = CommandFlags.None)
            => Context.SortedSets.CombineAndStoreAsync(operation, destination, [first, second], default, aggregate, flags).AsTask();

        /// <inheritdoc/>
        public long SortedSetCombineAndStore(SetOperation operation, RedisKey destination, RedisKey[] keys, double[]? weights = null, Aggregate aggregate = Aggregate.Sum, CommandFlags flags = CommandFlags.None)
            => Wait(Context.SortedSets.CombineAndStoreAsync(operation, destination, Required(keys, nameof(keys)), weights, aggregate, flags));

        /// <inheritdoc/>
        public Task<long> SortedSetCombineAndStoreAsync(SetOperation operation, RedisKey destination, RedisKey[] keys, double[]? weights = null, Aggregate aggregate = Aggregate.Sum, CommandFlags flags = CommandFlags.None)
            => Context.SortedSets.CombineAndStoreAsync(operation, destination, Required(keys, nameof(keys)), weights, aggregate, flags).AsTask();

        /// <inheritdoc/>
        public long SortedSetIntersectionLength(RedisKey[] keys, long limit = 0, CommandFlags flags = CommandFlags.None)
            => Wait(Context.SortedSets.CombineLengthAsync(Required(keys, nameof(keys)), limit > 0 ? limit : null, flags));

        /// <inheritdoc/>
        public Task<long> SortedSetIntersectionLengthAsync(RedisKey[] keys, long limit = 0, CommandFlags flags = CommandFlags.None)
            => Context.SortedSets.CombineLengthAsync(Required(keys, nameof(keys)), limit > 0 ? limit : null, flags).AsTask();

        // ---- pops --------------------------------------------------------------------------------------

        /// <inheritdoc/>
        public SortedSetEntry? SortedSetPop(RedisKey key, Order order = Order.Ascending, CommandFlags flags = CommandFlags.None)
            => Wait(Context.SortedSets.PopAsync(key, order, flags));

        /// <inheritdoc/>
        public Task<SortedSetEntry?> SortedSetPopAsync(RedisKey key, Order order = Order.Ascending, CommandFlags flags = CommandFlags.None)
            => Context.SortedSets.PopAsync(key, order, flags).AsTask();

        /// <inheritdoc/>
        public SortedSetEntry[] SortedSetPop(RedisKey key, long count, Order order = Order.Ascending, CommandFlags flags = CommandFlags.None)
            => Wait(Context.SortedSets.PopArray(key, count, order, flags));

        /// <inheritdoc/>
        public Task<SortedSetEntry[]> SortedSetPopAsync(RedisKey key, long count, Order order = Order.Ascending, CommandFlags flags = CommandFlags.None)
            => Context.SortedSets.PopArray(key, count, order, flags).AsTask();

        /// <inheritdoc/>
        public SortedSetPopResult SortedSetPop(RedisKey[] keys, long count, Order order = Order.Ascending, CommandFlags flags = CommandFlags.None)
            => Wait(Context.SortedSets.PopAsync(Required(keys, nameof(keys)), count, order, flags));

        /// <inheritdoc/>
        public Task<SortedSetPopResult> SortedSetPopAsync(RedisKey[] keys, long count, Order order = Order.Ascending, CommandFlags flags = CommandFlags.None)
            => Context.SortedSets.PopAsync(Required(keys, nameof(keys)), count, order, flags).AsTask();

        /// <summary>
        /// The existence half of a <see cref="ValueCondition"/>, which is all <c>ZADD</c> can express.
        /// </summary>
        /// <remarks>
        /// A value or digest test has no sorted-set spelling; rejecting it here reuses the condition's own
        /// message rather than inventing a second one.
        /// </remarks>
        private static SortedSetWhen AsSortedSetWhen(in ValueCondition when) => when.Kind switch
        {
            ValueCondition.ConditionKind.Always => SortedSetWhen.Always,
            ValueCondition.ConditionKind.Exists => SortedSetWhen.Exists,
            ValueCondition.ConditionKind.NotExists => SortedSetWhen.NotExists,
            _ => ThrowUnsupported(when),
        };

        private static SortedSetWhen ThrowUnsupported(in ValueCondition when)
        {
            when.ThrowInvalidOperation(nameof(SortedSetIncrement));
            return default; // not reached
        }
    }
}
