using System;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Threading.Tasks;
using RESPite;
using RESPite.Messages;

namespace StackExchange.Redis.Interpolated
{
    /// <summary>
    /// EXPERIMENTAL SPIKE. The sorted-set command group: <c>target.SortedSets.AddAsync(...)</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The largest group in the library, and the one with the most formatting the wire cares about and the
    /// caller does not: exclusive bounds carry a <c>(</c>, lexical bounds a <c>[</c>, a descending range
    /// swaps its own limits, and <c>ZADD</c> has six independent option tokens. All of that is shared with
    /// the <c>MessageWriter</c> path rather than restated - a second copy of a bound convention is a silent
    /// off-by-one-bound waiting to happen.
    /// </para>
    /// <para>
    /// <c>ZSCAN</c> stays with the other cursors.
    /// </para>
    /// </remarks>
    [Experimental(Experiments.InterpolatedWriter, UrlFormat = Experiments.UrlFormat)]
    public readonly struct RespSortedSets
    {
        private readonly RespContext _context;

        /// <summary>Group the sorted-set commands of a context.</summary>
        /// <param name="context">The context to send through.</param>
        public RespSortedSets(in RespContext context) => _context = context;

        /// <summary>The underlying context.</summary>
        public RespContext Context => _context;
    }

    public static partial class RespSurface
    {
        extension(IRespKeyspaceTarget target)
        {
            /// <summary>The sorted-set commands.</summary>
            public RespSortedSets SortedSets => new(target.Context);
        }

        extension(in RespContext context)
        {
            /// <summary>The sorted-set commands.</summary>
            public RespSortedSets SortedSets => new(context);
        }

        // ---- membership --------------------------------------------------------------------------------

        /// <summary>ZADD.</summary>
        /// <param name="sortedSets">The sorted-set command group.</param>
        /// <param name="key">The key to write.</param>
        /// <param name="member">The member to add.</param>
        /// <param name="score">The score to give it.</param>
        /// <param name="when">The condition the write is subject to.</param>
        /// <param name="change">Count members whose score changed, not only members that were new.</param>
        /// <param name="flags">Command flags.</param>
        /// <param name="cancellationToken">Cancels the request; only cancellation <i>before</i> the send is honoured today.</param>
        /// <remarks>
        /// <c>change</c> is what the old surface spells as a separate <c>SortedSetUpdate</c> method; it is
        /// one token on the wire (<c>CH</c>) and changes what the reply counts, which is a parameter rather
        /// than a command.
        /// </remarks>
        public static ValueTask<bool> AddAsync(
            this in RespSortedSets sortedSets,
            RedisKey key,
            RedisValue member,
            double score,
            SortedSetWhen when = SortedSetWhen.Always,
            bool change = false,
            CommandFlags flags = CommandFlags.None,
            CancellationToken cancellationToken = default)
        {
            var options = new RespSortedSetOptions(when, change, increment: false);
            return sortedSets.Context.SendAsync<bool>(
                $"{RedisCommand.ZADD}{key}{options}{score}{member}",
                flags.WithRetryCategory(options.RetryCategory),
                cancellationToken: cancellationToken);
        }

        /// <summary>ZADD with several members; the reply is how many were added (or changed, under CH).</summary>
        /// <param name="sortedSets">The sorted-set command group.</param>
        /// <param name="key">The key to write.</param>
        /// <param name="entries">The members and their scores.</param>
        /// <param name="when">The condition the write is subject to.</param>
        /// <param name="change">Count members whose score changed, not only members that were new.</param>
        /// <param name="flags">Command flags.</param>
        /// <param name="cancellationToken">Cancels the request; only cancellation <i>before</i> the send is honoured today.</param>
        /// <remarks>
        /// Each entry writes <b>score then element</b>, which is the reverse of how a
        /// <see cref="SortedSetEntry"/> reads; the type owns that ordering, so the whole run is one hole.
        /// </remarks>
        public static ValueTask<long> AddAsync(
            this in RespSortedSets sortedSets,
            RedisKey key,
            ReadOnlySpan<SortedSetEntry> entries,
            SortedSetWhen when = SortedSetWhen.Always,
            bool change = false,
            CommandFlags flags = CommandFlags.None,
            CancellationToken cancellationToken = default)
        {
            if (entries.IsEmpty) return new ValueTask<long>(0L);

            var options = new RespSortedSetOptions(when, change, increment: false);
            return sortedSets.Context.SendAsync<long>(
                $"{RedisCommand.ZADD}{key}{options}{entries}",
                flags.WithRetryCategory(options.RetryCategory),
                cancellationToken: cancellationToken);
        }

        /// <summary>ZADD ... INCR, or ZINCRBY when there is no condition to carry.</summary>
        /// <param name="sortedSets">The sorted-set command group.</param>
        /// <param name="key">The key to write.</param>
        /// <param name="member">The member to increment.</param>
        /// <param name="value">The amount to add.</param>
        /// <param name="when">The condition the increment is subject to.</param>
        /// <param name="flags">Command flags.</param>
        /// <param name="cancellationToken">Cancels the request; only cancellation <i>before</i> the send is honoured today.</param>
        /// <remarks>
        /// <para>
        /// <see langword="null"/> when the condition refused the increment - which is why this reports
        /// <c>double?</c> where the old surface's unconditional overload reports <c>double</c>. An
        /// unconditional increment cannot fail that way, so its caller can safely take the value.
        /// </para>
        /// <para>
        /// <c>ZINCRBY</c> is emitted only for the unconditional case, where it is the shorter spelling of
        /// exactly the same request; anything with a condition needs <c>ZADD ... INCR</c>, which is the
        /// only form that has one.
        /// </para>
        /// </remarks>
        public static ValueTask<double?> IncrementAsync(
            this in RespSortedSets sortedSets,
            RedisKey key,
            RedisValue member,
            double value,
            SortedSetWhen when = SortedSetWhen.Always,
            CommandFlags flags = CommandFlags.None,
            CancellationToken cancellationToken = default)
        {
            if (when == SortedSetWhen.Always)
            {
                return sortedSets.Context.SendAsync<double?>(
                    $"{RedisCommand.ZINCRBY}{key}{value}{member}", flags, cancellationToken: cancellationToken);
            }

            var options = new RespSortedSetOptions(when, change: false, increment: true);
            return sortedSets.Context.SendAsync<double?>(
                $"{RedisCommand.ZADD}{key}{options}{value}{member}",
                flags.WithRetryCategory(options.RetryCategory),
                cancellationToken: cancellationToken);
        }

        /// <summary>ZREM.</summary>
        /// <param name="sortedSets">The sorted-set command group.</param>
        /// <param name="key">The key to write.</param>
        /// <param name="member">The member to remove.</param>
        /// <param name="flags">Command flags.</param>
        /// <param name="cancellationToken">Cancels the request; only cancellation <i>before</i> the send is honoured today.</param>
        public static ValueTask<bool> RemoveAsync(this in RespSortedSets sortedSets, RedisKey key, RedisValue member, CommandFlags flags = CommandFlags.None, CancellationToken cancellationToken = default)
            => sortedSets.Context.SendAsync<bool>(
                $"{RedisCommand.ZREM}{key}{member}", flags, cancellationToken: cancellationToken);

        /// <summary>ZREM with several members; the reply is how many were removed.</summary>
        /// <param name="sortedSets">The sorted-set command group.</param>
        /// <param name="key">The key to write.</param>
        /// <param name="members">The members to remove.</param>
        /// <param name="flags">Command flags.</param>
        /// <param name="cancellationToken">Cancels the request; only cancellation <i>before</i> the send is honoured today.</param>
        public static ValueTask<long> RemoveAsync(this in RespSortedSets sortedSets, RedisKey key, ReadOnlySpan<RedisValue> members, CommandFlags flags = CommandFlags.None, CancellationToken cancellationToken = default)
            => members.IsEmpty
                ? new ValueTask<long>(0L)
                : sortedSets.Context.SendAsync<long>(
                    $"{RedisCommand.ZREM}{key}{members}", flags, cancellationToken: cancellationToken);

        // ---- simple reads ------------------------------------------------------------------------------

        /// <summary>ZSCORE.</summary>
        /// <param name="sortedSets">The sorted-set command group.</param>
        /// <param name="key">The key to read.</param>
        /// <param name="member">The member to look up.</param>
        /// <param name="flags">Command flags.</param>
        /// <param name="cancellationToken">Cancels the request; only cancellation <i>before</i> the send is honoured today.</param>
        public static ValueTask<double?> ScoreAsync(this in RespSortedSets sortedSets, RedisKey key, RedisValue member, CommandFlags flags = CommandFlags.None, CancellationToken cancellationToken = default)
            => sortedSets.Context.SendAsync<double?>(
                $"{RedisCommand.ZSCORE}{key}{member}", flags, cancellationToken: cancellationToken);

        /// <summary>ZMSCORE: one score per member, in order; nil for a member that is not there.</summary>
        /// <param name="sortedSets">The sorted-set command group.</param>
        /// <param name="key">The key to read.</param>
        /// <param name="members">The members to look up.</param>
        /// <param name="flags">Command flags.</param>
        /// <param name="cancellationToken">Cancels the request; only cancellation <i>before</i> the send is honoured today.</param>
        public static ValueTask<ReadOnlyLease<double?>> ScoresAsync(this in RespSortedSets sortedSets, RedisKey key, ReadOnlySpan<RedisValue> members, CommandFlags flags = CommandFlags.None, CancellationToken cancellationToken = default)
            => members.IsEmpty
                ? new ValueTask<ReadOnlyLease<double?>>(ReadOnlyLease<double?>.Empty)
                : sortedSets.Context.SendAsync<ReadOnlyLease<double?>>(
                    $"{RedisCommand.ZMSCORE}{key}{members}", flags, cancellationToken: cancellationToken);

        /// <summary>Scores, as an array, for the old <c>IDatabase</c> surface.</summary>
        /// <remarks>
        /// Internal sibling of <c>Scores</c>. A sibling rather than a conversion: <c>IDatabase</c> promises
        /// an array the caller owns, so going via the lease would rent a pooled buffer only to copy out of
        /// it.
        /// <para>
        /// <b>Permanent, not scaffolding.</b> <c>IDatabase</c> is not going anywhere - compatibility
        /// outranks tidiness here - so this is how that signature is served from the new core, for as long
        /// as the signature exists. Internal because the array is the <i>old</i> spelling: new code should
        /// reach for the lease, and nothing outside this assembly should be able to choose otherwise.
        /// </para>
        /// </remarks>
        internal static ValueTask<double?[]> ScoresArray(this in RespSortedSets sortedSets, RedisKey key, ReadOnlySpan<RedisValue> members, CommandFlags flags = CommandFlags.None, CancellationToken cancellationToken = default)
            => members.IsEmpty
                ? new ValueTask<double?[]>(Array.Empty<double?>())
                : sortedSets.Context.SendAsync<double?[]>(
                    $"{RedisCommand.ZMSCORE}{key}{members}", flags, cancellationToken: cancellationToken);

        /// <summary>ZCARD, or ZCOUNT when a score range is given.</summary>
        /// <param name="sortedSets">The sorted-set command group.</param>
        /// <param name="key">The key to measure.</param>
        /// <param name="min">The lowest score to count.</param>
        /// <param name="max">The highest score to count.</param>
        /// <param name="exclude">Which bounds are exclusive.</param>
        /// <param name="flags">Command flags.</param>
        /// <param name="cancellationToken">Cancels the request; only cancellation <i>before</i> the send is honoured today.</param>
        /// <remarks>
        /// An unbounded range is the whole set, which <c>ZCARD</c> answers without the server having to
        /// walk anything - so the default arguments pick a different command, exactly as the old surface
        /// does.
        /// </remarks>
        public static ValueTask<long> LengthAsync(
            this in RespSortedSets sortedSets,
            RedisKey key,
            double min = double.NegativeInfinity,
            double max = double.PositiveInfinity,
            Exclude exclude = Exclude.None,
            CommandFlags flags = CommandFlags.None,
            CancellationToken cancellationToken = default)
        {
            if (double.IsNegativeInfinity(min) && double.IsPositiveInfinity(max))
            {
                return sortedSets.Context.SendAsync<long>(
                    $"{RedisCommand.ZCARD}{key}", flags, cancellationToken: cancellationToken);
            }

            return sortedSets.Context.SendAsync<long>(
                $"{RedisCommand.ZCOUNT}{key}{RedisDatabase.GetRange(min, exclude, isStart: true)}{RedisDatabase.GetRange(max, exclude, isStart: false)}",
                flags,
                cancellationToken: cancellationToken);
        }

        /// <summary>ZLEXCOUNT: how many members fall in a lexical range.</summary>
        /// <param name="sortedSets">The sorted-set command group.</param>
        /// <param name="key">The key to measure.</param>
        /// <param name="min">The lowest member to count.</param>
        /// <param name="max">The highest member to count.</param>
        /// <param name="exclude">Which bounds are exclusive.</param>
        /// <param name="flags">Command flags.</param>
        /// <param name="cancellationToken">Cancels the request; only cancellation <i>before</i> the send is honoured today.</param>
        public static ValueTask<long> LengthByValueAsync(
            this in RespSortedSets sortedSets,
            RedisKey key,
            RedisValue min,
            RedisValue max,
            Exclude exclude = Exclude.None,
            CommandFlags flags = CommandFlags.None,
            CancellationToken cancellationToken = default)
        {
            RedisDatabase.ReverseLimits(Order.Ascending, ref exclude, ref min, ref max);
            return sortedSets.Context.SendAsync<long>(
                $"{RedisCommand.ZLEXCOUNT}{key}{Lex(min, exclude, isStart: true)}{Lex(max, exclude, isStart: false)}",
                flags,
                cancellationToken: cancellationToken);
        }

        /// <summary>ZRANK/ZREVRANK; <see langword="null"/> when the member is not there.</summary>
        /// <param name="sortedSets">The sorted-set command group.</param>
        /// <param name="key">The key to read.</param>
        /// <param name="member">The member to locate.</param>
        /// <param name="order">Which end to count from.</param>
        /// <param name="flags">Command flags.</param>
        /// <param name="cancellationToken">Cancels the request; only cancellation <i>before</i> the send is honoured today.</param>
        public static ValueTask<long?> RankAsync(this in RespSortedSets sortedSets, RedisKey key, RedisValue member, Order order = Order.Ascending, CommandFlags flags = CommandFlags.None, CancellationToken cancellationToken = default)
        {
            var command = order == Order.Descending ? RedisCommand.ZREVRANK : RedisCommand.ZRANK;
            return sortedSets.Context.SendAsync<long?>(
                $"{command}{key}{member}", flags, cancellationToken: cancellationToken);
        }

        /// <summary>ZRANDMEMBER.</summary>
        /// <param name="sortedSets">The sorted-set command group.</param>
        /// <param name="key">The key to read.</param>
        /// <param name="flags">Command flags.</param>
        /// <param name="cancellationToken">Cancels the request; only cancellation <i>before</i> the send is honoured today.</param>
        public static ValueTask<RedisValue> RandomMemberAsync(this in RespSortedSets sortedSets, RedisKey key, CommandFlags flags = CommandFlags.None, CancellationToken cancellationToken = default)
            => sortedSets.Context.SendAsync<RedisValue>(
                $"{RedisCommand.ZRANDMEMBER}{key}", flags.NeverCached(), cancellationToken: cancellationToken);

        /// <summary>ZRANDMEMBER with a count.</summary>
        /// <param name="sortedSets">The sorted-set command group.</param>
        /// <param name="key">The key to read.</param>
        /// <param name="count">How many to take; a negative count allows repeats.</param>
        /// <param name="flags">Command flags.</param>
        /// <param name="cancellationToken">Cancels the request; only cancellation <i>before</i> the send is honoured today.</param>
        public static ValueTask<ReadOnlyLease<RespValue>> RandomMembersAsync(this in RespSortedSets sortedSets, RedisKey key, long count, CommandFlags flags = CommandFlags.None, CancellationToken cancellationToken = default)
            => sortedSets.Context.SendAsync<ReadOnlyLease<RespValue>>(
                $"{RedisCommand.ZRANDMEMBER}{key}{count}", flags.NeverCached(), cancellationToken: cancellationToken);

        /// <summary>RandomMembers, as an array, for the old <c>IDatabase</c> surface.</summary>
        /// <remarks>
        /// Internal sibling of <c>RandomMembers</c>. A sibling rather than a conversion: <c>IDatabase</c> promises
        /// an array the caller owns, so going via the lease would rent a pooled buffer only to copy out of
        /// it.
        /// <para>
        /// <b>Permanent, not scaffolding.</b> <c>IDatabase</c> is not going anywhere - compatibility
        /// outranks tidiness here - so this is how that signature is served from the new core, for as long
        /// as the signature exists. Internal because the array is the <i>old</i> spelling: new code should
        /// reach for the lease, and nothing outside this assembly should be able to choose otherwise.
        /// </para>
        /// </remarks>
        internal static ValueTask<RedisValue[]> RandomMembersArray(this in RespSortedSets sortedSets, RedisKey key, long count, CommandFlags flags = CommandFlags.None, CancellationToken cancellationToken = default)
            => sortedSets.Context.SendAsync<RedisValue[]>(
                $"{RedisCommand.ZRANDMEMBER}{key}{count}", flags.NeverCached(), cancellationToken: cancellationToken);

        /// <summary>ZRANDMEMBER ... WITHSCORES.</summary>
        /// <param name="sortedSets">The sorted-set command group.</param>
        /// <param name="key">The key to read.</param>
        /// <param name="count">How many to take; a negative count allows repeats.</param>
        /// <param name="flags">Command flags.</param>
        /// <param name="cancellationToken">Cancels the request; only cancellation <i>before</i> the send is honoured today.</param>
        public static ValueTask<ReadOnlyLease<SortedSetEntry>> RandomMembersWithScoresAsync(this in RespSortedSets sortedSets, RedisKey key, long count, CommandFlags flags = CommandFlags.None, CancellationToken cancellationToken = default)
            => sortedSets.Context.SendAsync<ReadOnlyLease<SortedSetEntry>>(
                $"{RedisCommand.ZRANDMEMBER}{key}{count}{RespLiterals.WithScores}",
                flags.NeverCached(),
                cancellationToken: cancellationToken);

        /// <summary>RandomMembersWithScores, as an array, for the old <c>IDatabase</c> surface.</summary>
        /// <remarks>
        /// Internal sibling of <c>RandomMembersWithScores</c>. A sibling rather than a conversion: <c>IDatabase</c> promises
        /// an array the caller owns, so going via the lease would rent a pooled buffer only to copy out of
        /// it.
        /// <para>
        /// <b>Permanent, not scaffolding.</b> <c>IDatabase</c> is not going anywhere - compatibility
        /// outranks tidiness here - so this is how that signature is served from the new core, for as long
        /// as the signature exists. Internal because the array is the <i>old</i> spelling: new code should
        /// reach for the lease, and nothing outside this assembly should be able to choose otherwise.
        /// </para>
        /// </remarks>
        internal static ValueTask<SortedSetEntry[]> RandomMembersWithScoresArray(this in RespSortedSets sortedSets, RedisKey key, long count, CommandFlags flags = CommandFlags.None, CancellationToken cancellationToken = default)
            => sortedSets.Context.SendAsync<SortedSetEntry[]>(
                $"{RedisCommand.ZRANDMEMBER}{key}{count}{RespLiterals.WithScores}",
                flags.NeverCached(),
                cancellationToken: cancellationToken);

        // ---- ranges ------------------------------------------------------------------------------------

        /// <summary>ZRANGE/ZREVRANGE by rank.</summary>
        /// <param name="sortedSets">The sorted-set command group.</param>
        /// <param name="key">The key to read.</param>
        /// <param name="start">The first rank to take.</param>
        /// <param name="stop">The last rank to take.</param>
        /// <param name="order">Which end to count from.</param>
        /// <param name="flags">Command flags.</param>
        /// <param name="cancellationToken">Cancels the request; only cancellation <i>before</i> the send is honoured today.</param>
        public static ValueTask<ReadOnlyLease<RespValue>> RangeByRankAsync(
            this in RespSortedSets sortedSets,
            RedisKey key,
            long start = 0,
            long stop = -1,
            Order order = Order.Ascending,
            CommandFlags flags = CommandFlags.None,
            CancellationToken cancellationToken = default)
        {
            var command = order == Order.Descending ? RedisCommand.ZREVRANGE : RedisCommand.ZRANGE;
            return sortedSets.Context.SendAsync<ReadOnlyLease<RespValue>>(
                $"{command}{key}{start}{stop}", flags, cancellationToken: cancellationToken);
        }

        /// <summary>RangeByRank, as an array, for the old <c>IDatabase</c> surface.</summary>
        /// <remarks>
        /// Internal sibling of <c>RangeByRank</c>. A sibling rather than a conversion: <c>IDatabase</c> promises
        /// an array the caller owns, so going via the lease would rent a pooled buffer only to copy out of
        /// it.
        /// <para>
        /// <b>Permanent, not scaffolding.</b> <c>IDatabase</c> is not going anywhere - compatibility
        /// outranks tidiness here - so this is how that signature is served from the new core, for as long
        /// as the signature exists. Internal because the array is the <i>old</i> spelling: new code should
        /// reach for the lease, and nothing outside this assembly should be able to choose otherwise.
        /// </para>
        /// </remarks>
        internal static ValueTask<RedisValue[]> RangeByRankArray(
            this in RespSortedSets sortedSets,
            RedisKey key,
            long start = 0,
            long stop = -1,
            Order order = Order.Ascending,
            CommandFlags flags = CommandFlags.None,
            CancellationToken cancellationToken = default)
        {
            var command = order == Order.Descending ? RedisCommand.ZREVRANGE : RedisCommand.ZRANGE;
            return sortedSets.Context.SendAsync<RedisValue[]>(
                $"{command}{key}{start}{stop}", flags, cancellationToken: cancellationToken);
        }

        /// <inheritdoc cref="RangeByRankAsync"/>
        /// <param name="sortedSets">The sorted-set command group.</param>
        /// <param name="key">The key to read.</param>
        /// <param name="start">The first rank to take.</param>
        /// <param name="stop">The last rank to take.</param>
        /// <param name="order">Which end to count from.</param>
        /// <param name="flags">Command flags.</param>
        /// <param name="cancellationToken">Cancels the request; only cancellation <i>before</i> the send is honoured today.</param>
        public static ValueTask<ReadOnlyLease<SortedSetEntry>> RangeByRankWithScoresAsync(
            this in RespSortedSets sortedSets,
            RedisKey key,
            long start = 0,
            long stop = -1,
            Order order = Order.Ascending,
            CommandFlags flags = CommandFlags.None,
            CancellationToken cancellationToken = default)
        {
            var command = order == Order.Descending ? RedisCommand.ZREVRANGE : RedisCommand.ZRANGE;
            return sortedSets.Context.SendAsync<ReadOnlyLease<SortedSetEntry>>(
                $"{command}{key}{start}{stop}{RespLiterals.WithScores}", flags, cancellationToken: cancellationToken);
        }

        /// <summary>RangeByRankWithScores, as an array, for the old <c>IDatabase</c> surface.</summary>
        /// <remarks>
        /// Internal sibling of <c>RangeByRankWithScores</c>. A sibling rather than a conversion: <c>IDatabase</c> promises
        /// an array the caller owns, so going via the lease would rent a pooled buffer only to copy out of
        /// it.
        /// <para>
        /// <b>Permanent, not scaffolding.</b> <c>IDatabase</c> is not going anywhere - compatibility
        /// outranks tidiness here - so this is how that signature is served from the new core, for as long
        /// as the signature exists. Internal because the array is the <i>old</i> spelling: new code should
        /// reach for the lease, and nothing outside this assembly should be able to choose otherwise.
        /// </para>
        /// </remarks>
        internal static ValueTask<SortedSetEntry[]> RangeByRankWithScoresArray(
            this in RespSortedSets sortedSets,
            RedisKey key,
            long start = 0,
            long stop = -1,
            Order order = Order.Ascending,
            CommandFlags flags = CommandFlags.None,
            CancellationToken cancellationToken = default)
        {
            var command = order == Order.Descending ? RedisCommand.ZREVRANGE : RedisCommand.ZRANGE;
            return sortedSets.Context.SendAsync<SortedSetEntry[]>(
                $"{command}{key}{start}{stop}{RespLiterals.WithScores}", flags, cancellationToken: cancellationToken);
        }

        /// <summary>ZRANGEBYSCORE/ZREVRANGEBYSCORE.</summary>
        /// <param name="sortedSets">The sorted-set command group.</param>
        /// <param name="key">The key to read.</param>
        /// <param name="start">The lowest score to take.</param>
        /// <param name="stop">The highest score to take.</param>
        /// <param name="exclude">Which bounds are exclusive.</param>
        /// <param name="order">Which end to read from.</param>
        /// <param name="skip">How many to discard from the front.</param>
        /// <param name="take">How many to return; -1 for all.</param>
        /// <param name="flags">Command flags.</param>
        /// <param name="cancellationToken">Cancels the request; only cancellation <i>before</i> the send is honoured today.</param>
        /// <remarks>
        /// The bounds are <b>swapped</b> when the caller's order and their numeric order disagree, and the
        /// exclusivity swaps with them - the server always wants low-then-high, whichever direction it is
        /// asked to walk. That is the old builder's rule, kept exactly, because a caller who passed
        /// <c>(10, 1)</c> descending has always meant the same thing.
        /// </remarks>
        public static ValueTask<ReadOnlyLease<RespValue>> RangeByScoreAsync(
            this in RespSortedSets sortedSets,
            RedisKey key,
            double start = double.NegativeInfinity,
            double stop = double.PositiveInfinity,
            Exclude exclude = Exclude.None,
            Order order = Order.Ascending,
            long skip = 0,
            long take = -1,
            CommandFlags flags = CommandFlags.None,
            CancellationToken cancellationToken = default)
            => RangeByScoreCore<ReadOnlyLease<RespValue>>(in sortedSets, key, start, stop, exclude, order, skip, take, withScores: false, flags);

        /// <summary>RangeByScore, as an array, for the old <c>IDatabase</c> surface.</summary>
        /// <remarks>
        /// Internal sibling of <c>RangeByScore</c>. A sibling rather than a conversion: <c>IDatabase</c> promises
        /// an array the caller owns, so going via the lease would rent a pooled buffer only to copy out of
        /// it.
        /// <para>
        /// <b>Permanent, not scaffolding.</b> <c>IDatabase</c> is not going anywhere - compatibility
        /// outranks tidiness here - so this is how that signature is served from the new core, for as long
        /// as the signature exists. Internal because the array is the <i>old</i> spelling: new code should
        /// reach for the lease, and nothing outside this assembly should be able to choose otherwise.
        /// </para>
        /// </remarks>
        internal static ValueTask<RedisValue[]> RangeByScoreArray(
            this in RespSortedSets sortedSets,
            RedisKey key,
            double start = double.NegativeInfinity,
            double stop = double.PositiveInfinity,
            Exclude exclude = Exclude.None,
            Order order = Order.Ascending,
            long skip = 0,
            long take = -1,
            CommandFlags flags = CommandFlags.None,
            CancellationToken cancellationToken = default)
            => RangeByScoreCore<RedisValue[]>(in sortedSets, key, start, stop, exclude, order, skip, take, withScores: false, flags);

        /// <inheritdoc cref="RangeByScoreAsync"/>
        /// <param name="sortedSets">The sorted-set command group.</param>
        /// <param name="key">The key to read.</param>
        /// <param name="start">The lowest score to take.</param>
        /// <param name="stop">The highest score to take.</param>
        /// <param name="exclude">Which bounds are exclusive.</param>
        /// <param name="order">Which end to read from.</param>
        /// <param name="skip">How many to discard from the front.</param>
        /// <param name="take">How many to return; -1 for all.</param>
        /// <param name="flags">Command flags.</param>
        /// <param name="cancellationToken">Cancels the request; only cancellation <i>before</i> the send is honoured today.</param>
        public static ValueTask<ReadOnlyLease<SortedSetEntry>> RangeByScoreWithScoresAsync(
            this in RespSortedSets sortedSets,
            RedisKey key,
            double start = double.NegativeInfinity,
            double stop = double.PositiveInfinity,
            Exclude exclude = Exclude.None,
            Order order = Order.Ascending,
            long skip = 0,
            long take = -1,
            CommandFlags flags = CommandFlags.None,
            CancellationToken cancellationToken = default)
            => RangeByScoreCore<ReadOnlyLease<SortedSetEntry>>(in sortedSets, key, start, stop, exclude, order, skip, take, withScores: true, flags);

        /// <summary>RangeByScoreWithScores, as an array, for the old <c>IDatabase</c> surface.</summary>
        /// <remarks>
        /// Internal sibling of <c>RangeByScoreWithScores</c>. A sibling rather than a conversion: <c>IDatabase</c> promises
        /// an array the caller owns, so going via the lease would rent a pooled buffer only to copy out of
        /// it.
        /// <para>
        /// <b>Permanent, not scaffolding.</b> <c>IDatabase</c> is not going anywhere - compatibility
        /// outranks tidiness here - so this is how that signature is served from the new core, for as long
        /// as the signature exists. Internal because the array is the <i>old</i> spelling: new code should
        /// reach for the lease, and nothing outside this assembly should be able to choose otherwise.
        /// </para>
        /// </remarks>
        internal static ValueTask<SortedSetEntry[]> RangeByScoreWithScoresArray(
            this in RespSortedSets sortedSets,
            RedisKey key,
            double start = double.NegativeInfinity,
            double stop = double.PositiveInfinity,
            Exclude exclude = Exclude.None,
            Order order = Order.Ascending,
            long skip = 0,
            long take = -1,
            CommandFlags flags = CommandFlags.None,
            CancellationToken cancellationToken = default)
            => RangeByScoreCore<SortedSetEntry[]>(in sortedSets, key, start, stop, exclude, order, skip, take, withScores: true, flags);

        /// <summary>ZRANGEBYLEX/ZREVRANGEBYLEX.</summary>
        /// <param name="sortedSets">The sorted-set command group.</param>
        /// <param name="key">The key to read.</param>
        /// <param name="min">The lowest member to take.</param>
        /// <param name="max">The highest member to take.</param>
        /// <param name="exclude">Which bounds are exclusive.</param>
        /// <param name="order">Which end to read from.</param>
        /// <param name="skip">How many to discard from the front.</param>
        /// <param name="take">How many to return; -1 for all.</param>
        /// <param name="flags">Command flags.</param>
        /// <param name="cancellationToken">Cancels the request; only cancellation <i>before</i> the send is honoured today.</param>
        /// <remarks>
        /// As with the score form, the range is put into low-then-high order first; for a lexical range
        /// the open bounds then flip too, which is why <c>-</c> and <c>+</c> are chosen by the order
        /// rather than by the position.
        /// </remarks>
        public static ValueTask<ReadOnlyLease<RespValue>> RangeByValueAsync(
            this in RespSortedSets sortedSets,
            RedisKey key,
            RedisValue min = default,
            RedisValue max = default,
            Exclude exclude = Exclude.None,
            Order order = Order.Ascending,
            long skip = 0,
            long take = -1,
            CommandFlags flags = CommandFlags.None,
            CancellationToken cancellationToken = default)
        {
            var command = order == Order.Descending ? RedisCommand.ZREVRANGEBYLEX : RedisCommand.ZRANGEBYLEX;

            // the bounds stay in start-then-stop order even for the reversed command; what reverses is
            // which of them is "low", and GetLexRange's order-aware -/+ mapping is where that lives
            RedisDatabase.ReverseLimits(order, ref exclude, ref min, ref max);

            return sortedSets.Context.SendAsync<ReadOnlyLease<RespValue>>(
                $"{command}{key}{Lex(min, exclude, isStart: true, order)}{Lex(max, exclude, isStart: false, order)}{new RespLimitRange(skip, take)}",
                flags,
                cancellationToken: cancellationToken);
        }

        /// <summary>RangeByValue, as an array, for the old <c>IDatabase</c> surface.</summary>
        /// <remarks>
        /// Internal sibling of <c>RangeByValue</c>. A sibling rather than a conversion: <c>IDatabase</c> promises
        /// an array the caller owns, so going via the lease would rent a pooled buffer only to copy out of
        /// it.
        /// <para>
        /// <b>Permanent, not scaffolding.</b> <c>IDatabase</c> is not going anywhere - compatibility
        /// outranks tidiness here - so this is how that signature is served from the new core, for as long
        /// as the signature exists. Internal because the array is the <i>old</i> spelling: new code should
        /// reach for the lease, and nothing outside this assembly should be able to choose otherwise.
        /// </para>
        /// </remarks>
        internal static ValueTask<RedisValue[]> RangeByValueArray(
            this in RespSortedSets sortedSets,
            RedisKey key,
            RedisValue min = default,
            RedisValue max = default,
            Exclude exclude = Exclude.None,
            Order order = Order.Ascending,
            long skip = 0,
            long take = -1,
            CommandFlags flags = CommandFlags.None,
            CancellationToken cancellationToken = default)
        {
            var command = order == Order.Descending ? RedisCommand.ZREVRANGEBYLEX : RedisCommand.ZRANGEBYLEX;

            // the bounds stay in start-then-stop order even for the reversed command; what reverses is
            // which of them is "low", and GetLexRange's order-aware -/+ mapping is where that lives
            RedisDatabase.ReverseLimits(order, ref exclude, ref min, ref max);

            return sortedSets.Context.SendAsync<RedisValue[]>(
                $"{command}{key}{Lex(min, exclude, isStart: true, order)}{Lex(max, exclude, isStart: false, order)}{new RespLimitRange(skip, take)}",
                flags,
                cancellationToken: cancellationToken);
        }

        /// <summary>ZRANGESTORE; the reply is the destination's size.</summary>
        /// <param name="sortedSets">The sorted-set command group.</param>
        /// <param name="sourceKey">The key to read.</param>
        /// <param name="destinationKey">The key to write the result to.</param>
        /// <param name="start">The first bound.</param>
        /// <param name="stop">The second bound.</param>
        /// <param name="sortedSetOrder">Whether the bounds are ranks, scores or members.</param>
        /// <param name="exclude">Which bounds are exclusive.</param>
        /// <param name="order">Which end to read from.</param>
        /// <param name="skip">How many to discard from the front.</param>
        /// <param name="take">How many to store; <see langword="null"/> for all.</param>
        /// <param name="flags">Command flags.</param>
        /// <param name="cancellationToken">Cancels the request; only cancellation <i>before</i> the send is honoured today.</param>
        /// <remarks>
        /// By rank, neither <paramref name="exclude"/> nor <paramref name="take"/> means anything and both
        /// are rejected - the server has no operand for either in that mode, so silently dropping them
        /// would store a different range than was asked for.
        /// </remarks>
        public static ValueTask<long> RangeAndStoreAsync(
            this in RespSortedSets sortedSets,
            RedisKey sourceKey,
            RedisKey destinationKey,
            RedisValue start,
            RedisValue stop,
            SortedSetOrder sortedSetOrder = SortedSetOrder.ByRank,
            Exclude exclude = Exclude.None,
            Order order = Order.Ascending,
            long skip = 0,
            long? take = null,
            CommandFlags flags = CommandFlags.None,
            CancellationToken cancellationToken = default)
        {
            var category = flags.WithDefaultCategory(RedisCommand.ZRANGESTORE);
            var rev = RespLiterals.Rev.When(order == Order.Descending); // a zero-argument fragment

            if (sortedSetOrder == SortedSetOrder.ByRank)
            {
                if (take > 0)
                {
                    throw new ArgumentException(
                        "take argument is not valid when sortedSetOrder is ByRank you may want to try setting the SortedSetOrder to ByLex or ByScore",
                        nameof(take));
                }

                if (exclude != Exclude.None)
                {
                    throw new ArgumentException(
                        "exclude argument is not valid when sortedSetOrder is ByRank, you may want to try setting the sortedSetOrder to ByLex or ByScore",
                        nameof(exclude));
                }

                return sortedSets.Context.SendAsync<long>(
                    $"{RedisCommand.ZRANGESTORE}{destinationKey}{sourceKey}{start}{stop}{rev}", category, cancellationToken: cancellationToken);
            }

            // ZRANGESTORE brackets a LEXICAL bound that is merely inclusive, where the read commands leave
            // it bare; that asymmetry is the server's, and RespRangeStoreBound is where it is written down
            var from = RespRangeStoreBound.Start(start, exclude, sortedSetOrder);
            var to = RespRangeStoreBound.Stop(stop, exclude, sortedSetOrder);
            var by = sortedSetOrder == SortedSetOrder.ByLex ? RespLiterals.ByLex : RespLiterals.ByScore;
            var limit = take is > 0 ? new RespLimitRange(skip, take.GetValueOrDefault()) : RespLimitRange.None;

            return sortedSets.Context.SendAsync<long>(
                $"{RedisCommand.ZRANGESTORE}{destinationKey}{sourceKey}{from}{to}{by}{rev}{limit}", category, cancellationToken: cancellationToken);
        }

        // ---- removal by range --------------------------------------------------------------------------

        /// <summary>ZREMRANGEBYRANK.</summary>
        /// <param name="sortedSets">The sorted-set command group.</param>
        /// <param name="key">The key to write.</param>
        /// <param name="start">The first rank to remove.</param>
        /// <param name="stop">The last rank to remove.</param>
        /// <param name="flags">Command flags.</param>
        /// <param name="cancellationToken">Cancels the request; only cancellation <i>before</i> the send is honoured today.</param>
        public static ValueTask<long> RemoveRangeByRankAsync(this in RespSortedSets sortedSets, RedisKey key, long start, long stop, CommandFlags flags = CommandFlags.None, CancellationToken cancellationToken = default)
            => sortedSets.Context.SendAsync<long>(
                $"{RedisCommand.ZREMRANGEBYRANK}{key}{start}{stop}", flags, cancellationToken: cancellationToken);

        /// <summary>ZREMRANGEBYSCORE.</summary>
        /// <param name="sortedSets">The sorted-set command group.</param>
        /// <param name="key">The key to write.</param>
        /// <param name="start">The lowest score to remove.</param>
        /// <param name="stop">The highest score to remove.</param>
        /// <param name="exclude">Which bounds are exclusive.</param>
        /// <param name="flags">Command flags.</param>
        /// <param name="cancellationToken">Cancels the request; only cancellation <i>before</i> the send is honoured today.</param>
        public static ValueTask<long> RemoveRangeByScoreAsync(this in RespSortedSets sortedSets, RedisKey key, double start, double stop, Exclude exclude = Exclude.None, CommandFlags flags = CommandFlags.None, CancellationToken cancellationToken = default)
            => sortedSets.Context.SendAsync<long>(
                $"{RedisCommand.ZREMRANGEBYSCORE}{key}{RedisDatabase.GetRange(start, exclude, isStart: true)}{RedisDatabase.GetRange(stop, exclude, isStart: false)}",
                flags,
                cancellationToken: cancellationToken);

        /// <summary>ZREMRANGEBYLEX.</summary>
        /// <param name="sortedSets">The sorted-set command group.</param>
        /// <param name="key">The key to write.</param>
        /// <param name="min">The lowest member to remove.</param>
        /// <param name="max">The highest member to remove.</param>
        /// <param name="exclude">Which bounds are exclusive.</param>
        /// <param name="flags">Command flags.</param>
        /// <param name="cancellationToken">Cancels the request; only cancellation <i>before</i> the send is honoured today.</param>
        public static ValueTask<long> RemoveRangeByValueAsync(this in RespSortedSets sortedSets, RedisKey key, RedisValue min, RedisValue max, Exclude exclude = Exclude.None, CommandFlags flags = CommandFlags.None, CancellationToken cancellationToken = default)
        {
            RedisDatabase.ReverseLimits(Order.Ascending, ref exclude, ref min, ref max);
            return sortedSets.Context.SendAsync<long>(
                $"{RedisCommand.ZREMRANGEBYLEX}{key}{Lex(min, exclude, isStart: true)}{Lex(max, exclude, isStart: false)}",
                flags,
                cancellationToken: cancellationToken);
        }

        // ---- combinations ------------------------------------------------------------------------------

        /// <summary>ZUNION/ZINTER/ZDIFF.</summary>
        /// <param name="sortedSets">The sorted-set command group.</param>
        /// <param name="operation">The operation to apply.</param>
        /// <param name="keys">The keys to combine.</param>
        /// <param name="weights">A multiplier per key, or <see langword="null"/> for all ones.</param>
        /// <param name="aggregate">How to fold the scores of a member present in several keys.</param>
        /// <param name="flags">Command flags.</param>
        /// <param name="cancellationToken">Cancels the request; only cancellation <i>before</i> the send is honoured today.</param>
        public static ValueTask<ReadOnlyLease<RespValue>> CombineAsync(
            this in RespSortedSets sortedSets,
            SetOperation operation,
            ReadOnlySpan<RedisKey> keys,
            ReadOnlySpan<double> weights = default,
            Aggregate aggregate = Aggregate.Sum,
            CommandFlags flags = CommandFlags.None,
            CancellationToken cancellationToken = default)
        {
            var command = ValidateCombine(operation.ToSortedSetCommand(), keys, weights, aggregate);
            return CombineCore<ReadOnlyLease<RespValue>>(in sortedSets, command, destination: default, keys, weights, aggregate, withScores: false, flags);
        }

        /// <summary>Combine, as an array, for the old <c>IDatabase</c> surface.</summary>
        /// <remarks>
        /// Internal sibling of <c>Combine</c>. A sibling rather than a conversion: <c>IDatabase</c> promises
        /// an array the caller owns, so going via the lease would rent a pooled buffer only to copy out of
        /// it.
        /// <para>
        /// <b>Permanent, not scaffolding.</b> <c>IDatabase</c> is not going anywhere - compatibility
        /// outranks tidiness here - so this is how that signature is served from the new core, for as long
        /// as the signature exists. Internal because the array is the <i>old</i> spelling: new code should
        /// reach for the lease, and nothing outside this assembly should be able to choose otherwise.
        /// </para>
        /// </remarks>
        internal static ValueTask<RedisValue[]> CombineArray(
            this in RespSortedSets sortedSets,
            SetOperation operation,
            ReadOnlySpan<RedisKey> keys,
            ReadOnlySpan<double> weights = default,
            Aggregate aggregate = Aggregate.Sum,
            CommandFlags flags = CommandFlags.None,
            CancellationToken cancellationToken = default)
        {
            var command = ValidateCombine(operation.ToSortedSetCommand(), keys, weights, aggregate);
            return CombineCore<RedisValue[]>(in sortedSets, command, destination: default, keys, weights, aggregate, withScores: false, flags);
        }

        /// <inheritdoc cref="CombineAsync(in RespSortedSets, SetOperation, ReadOnlySpan{RedisKey}, ReadOnlySpan{double}, Aggregate, CommandFlags, CancellationToken)"/>
        /// <param name="sortedSets">The sorted-set command group.</param>
        /// <param name="operation">The operation to apply.</param>
        /// <param name="keys">The keys to combine.</param>
        /// <param name="weights">A multiplier per key, or <see langword="null"/> for all ones.</param>
        /// <param name="aggregate">How to fold the scores of a member present in several keys.</param>
        /// <param name="flags">Command flags.</param>
        /// <param name="cancellationToken">Cancels the request; only cancellation <i>before</i> the send is honoured today.</param>
        public static ValueTask<ReadOnlyLease<SortedSetEntry>> CombineWithScoresAsync(
            this in RespSortedSets sortedSets,
            SetOperation operation,
            ReadOnlySpan<RedisKey> keys,
            ReadOnlySpan<double> weights = default,
            Aggregate aggregate = Aggregate.Sum,
            CommandFlags flags = CommandFlags.None,
            CancellationToken cancellationToken = default)
        {
            var command = ValidateCombine(operation.ToSortedSetCommand(), keys, weights, aggregate);
            return CombineCore<ReadOnlyLease<SortedSetEntry>>(in sortedSets, command, destination: default, keys, weights, aggregate, withScores: true, flags);
        }

        /// <summary>CombineWithScores, as an array, for the old <c>IDatabase</c> surface.</summary>
        /// <remarks>
        /// Internal sibling of <c>CombineWithScores</c>. A sibling rather than a conversion: <c>IDatabase</c> promises
        /// an array the caller owns, so going via the lease would rent a pooled buffer only to copy out of
        /// it.
        /// <para>
        /// <b>Permanent, not scaffolding.</b> <c>IDatabase</c> is not going anywhere - compatibility
        /// outranks tidiness here - so this is how that signature is served from the new core, for as long
        /// as the signature exists. Internal because the array is the <i>old</i> spelling: new code should
        /// reach for the lease, and nothing outside this assembly should be able to choose otherwise.
        /// </para>
        /// </remarks>
        internal static ValueTask<SortedSetEntry[]> CombineWithScoresArray(
            this in RespSortedSets sortedSets,
            SetOperation operation,
            ReadOnlySpan<RedisKey> keys,
            ReadOnlySpan<double> weights = default,
            Aggregate aggregate = Aggregate.Sum,
            CommandFlags flags = CommandFlags.None,
            CancellationToken cancellationToken = default)
        {
            var command = ValidateCombine(operation.ToSortedSetCommand(), keys, weights, aggregate);
            return CombineCore<SortedSetEntry[]>(in sortedSets, command, destination: default, keys, weights, aggregate, withScores: true, flags);
        }

        /// <summary>ZUNIONSTORE/ZINTERSTORE/ZDIFFSTORE; the reply is the destination's size.</summary>
        /// <param name="sortedSets">The sorted-set command group.</param>
        /// <param name="operation">The operation to apply.</param>
        /// <param name="destination">The key to write the result to.</param>
        /// <param name="keys">The keys to combine.</param>
        /// <param name="weights">A multiplier per key, or <see langword="null"/> for all ones.</param>
        /// <param name="aggregate">How to fold the scores of a member present in several keys.</param>
        /// <param name="flags">Command flags.</param>
        /// <param name="cancellationToken">Cancels the request; only cancellation <i>before</i> the send is honoured today.</param>
        public static ValueTask<long> CombineAndStoreAsync(
            this in RespSortedSets sortedSets,
            SetOperation operation,
            RedisKey destination,
            ReadOnlySpan<RedisKey> keys,
            ReadOnlySpan<double> weights = default,
            Aggregate aggregate = Aggregate.Sum,
            CommandFlags flags = CommandFlags.None,
            CancellationToken cancellationToken = default)
        {
            var command = ValidateCombine(operation.ToSortedSetStoreCommand(), keys, weights, aggregate);
            return CombineCore<long>(in sortedSets, command, destination, keys, weights, aggregate, withScores: false, flags);
        }

        /// <summary>ZINTERCARD: the size of an intersection, without building it.</summary>
        /// <param name="sortedSets">The sorted-set command group.</param>
        /// <param name="keys">The keys to intersect.</param>
        /// <param name="limit">Stop counting at this many; <c>null</c> for no limit.</param>
        /// <param name="flags">Command flags.</param>
        /// <param name="cancellationToken">Cancels the request; only cancellation <i>before</i> the send is honoured today.</param>
        public static ValueTask<long> CombineLengthAsync(this in RespSortedSets sortedSets, ReadOnlySpan<RedisKey> keys, long? limit = null, CommandFlags flags = CommandFlags.None, CancellationToken cancellationToken = default)
        {
            if (keys.IsEmpty) throw new ArgumentException("At least one key is required.", nameof(keys));

            return sortedSets.Context.SendAsync<long>(
                $"{RedisCommand.ZINTERCARD}{keys.Length}{keys}{RespLiterals.Limit.When(limit)}{limit}",
                flags,
                cancellationToken: cancellationToken);
        }

        // ---- pops --------------------------------------------------------------------------------------

        /// <summary>ZPOPMIN/ZPOPMAX.</summary>
        /// <param name="sortedSets">The sorted-set command group.</param>
        /// <param name="key">The key to write.</param>
        /// <param name="order">Which end to take from.</param>
        /// <param name="flags">Command flags.</param>
        /// <param name="cancellationToken">Cancels the request; only cancellation <i>before</i> the send is honoured today.</param>
        public static ValueTask<SortedSetEntry?> PopAsync(this in RespSortedSets sortedSets, RedisKey key, Order order = Order.Ascending, CommandFlags flags = CommandFlags.None, CancellationToken cancellationToken = default)
        {
            var command = order == Order.Descending ? RedisCommand.ZPOPMAX : RedisCommand.ZPOPMIN;
            return sortedSets.Context.SendAsync<SortedSetEntry?>($"{command}{key}", flags, cancellationToken: cancellationToken);
        }

        /// <summary>ZPOPMIN/ZPOPMAX with a count.</summary>
        /// <param name="sortedSets">The sorted-set command group.</param>
        /// <param name="key">The key to write.</param>
        /// <param name="count">How many to take.</param>
        /// <param name="order">Which end to take from.</param>
        /// <param name="flags">Command flags.</param>
        /// <param name="cancellationToken">Cancels the request; only cancellation <i>before</i> the send is honoured today.</param>
        public static ValueTask<ReadOnlyLease<SortedSetEntry>> PopAsync(this in RespSortedSets sortedSets, RedisKey key, long count, Order order = Order.Ascending, CommandFlags flags = CommandFlags.None, CancellationToken cancellationToken = default)
        {
            // unlike SPOP, a count of zero here is well defined on the wire - but sending it is a round
            // trip to be told nothing, which the old surface also declines to make
            if (count == 0) return new ValueTask<ReadOnlyLease<SortedSetEntry>>(ReadOnlyLease<SortedSetEntry>.Empty);

            var command = order == Order.Descending ? RedisCommand.ZPOPMAX : RedisCommand.ZPOPMIN;
            return sortedSets.Context.SendAsync<ReadOnlyLease<SortedSetEntry>>($"{command}{key}{count}", flags, cancellationToken: cancellationToken);
        }

        /// <summary>Pop, as an array, for the old <c>IDatabase</c> surface.</summary>
        /// <remarks>
        /// Internal sibling of <c>Pop</c>. A sibling rather than a conversion: <c>IDatabase</c> promises
        /// an array the caller owns, so going via the lease would rent a pooled buffer only to copy out of
        /// it.
        /// <para>
        /// <b>Permanent, not scaffolding.</b> <c>IDatabase</c> is not going anywhere - compatibility
        /// outranks tidiness here - so this is how that signature is served from the new core, for as long
        /// as the signature exists. Internal because the array is the <i>old</i> spelling: new code should
        /// reach for the lease, and nothing outside this assembly should be able to choose otherwise.
        /// </para>
        /// </remarks>
        internal static ValueTask<SortedSetEntry[]> PopArray(this in RespSortedSets sortedSets, RedisKey key, long count, Order order = Order.Ascending, CommandFlags flags = CommandFlags.None, CancellationToken cancellationToken = default)
        {
            // unlike SPOP, a count of zero here is well defined on the wire - but sending it is a round
            // trip to be told nothing, which the old surface also declines to make
            if (count == 0) return new ValueTask<SortedSetEntry[]>(Array.Empty<SortedSetEntry>());

            var command = order == Order.Descending ? RedisCommand.ZPOPMAX : RedisCommand.ZPOPMIN;
            return sortedSets.Context.SendAsync<SortedSetEntry[]>($"{command}{key}{count}", flags, cancellationToken: cancellationToken);
        }

        /// <summary>ZMPOP: take from the first of several keys that has anything.</summary>
        /// <param name="sortedSets">The sorted-set command group.</param>
        /// <param name="keys">The keys to try, in order.</param>
        /// <param name="count">How many to take from whichever key answers.</param>
        /// <param name="order">Which end to take from.</param>
        /// <param name="flags">Command flags.</param>
        /// <param name="cancellationToken">Cancels the request; only cancellation <i>before</i> the send is honoured today.</param>
        public static ValueTask<SortedSetPopResult> PopAsync(this in RespSortedSets sortedSets, ReadOnlySpan<RedisKey> keys, long count, Order order = Order.Ascending, CommandFlags flags = CommandFlags.None, CancellationToken cancellationToken = default)
        {
            if (keys.IsEmpty) throw new ArgumentOutOfRangeException(nameof(keys), "keys must have a size of at least 1");

            var end = order == Order.Descending ? RespLiterals.Max : RespLiterals.Min;
            return sortedSets.Context.SendAsync<SortedSetPopResult>(
                $"{RedisCommand.ZMPOP}{keys.Length}{keys}{end}{RespLiterals.Count}{count}",
                flags,
                cancellationToken: cancellationToken);
        }

        // ---- shared -------------------------------------------------------------------------------------

        /// <summary>ZRANGEBYSCORE and its with-scores twin, which differ only in one token and the result.</summary>
        private static ValueTask<TResult> RangeByScoreCore<TResult>(
            in RespSortedSets sortedSets,
            RedisKey key,
            double start,
            double stop,
            Exclude exclude,
            Order order,
            long skip,
            long take,
            bool withScores,
            CommandFlags flags)
        {
            var command = order == Order.Descending ? RedisCommand.ZREVRANGEBYSCORE : RedisCommand.ZRANGEBYSCORE;

            // the server always wants low-then-high, whichever direction it walks; a caller who wrote the
            // bounds in their reading order gets them swapped here, exclusivity included
            if ((order == Order.Ascending) == (start > stop))
            {
                (start, stop) = (stop, start);
                exclude = exclude switch
                {
                    Exclude.Start => Exclude.Stop,
                    Exclude.Stop => Exclude.Start,
                    _ => exclude,
                };
            }

            var from = RedisDatabase.GetRange(start, exclude, isStart: true);
            var to = RedisDatabase.GetRange(stop, exclude, isStart: false);
            var scores = RespLiterals.WithScores.When(withScores);

            return sortedSets.Context.SendAsync<TResult>(
                $"{command}{key}{from}{to}{scores}{new RespLimitRange(skip, take)}",
                flags);
        }

        /// <summary>The checks the combination commands share, and the command they resolve to.</summary>
        private static RedisCommand ValidateCombine(RedisCommand command, ReadOnlySpan<RedisKey> keys, ReadOnlySpan<double> weights, Aggregate aggregate)
        {
            if (keys.IsEmpty) throw new ArgumentException("At least one key is required.", nameof(keys));

            if ((command is RedisCommand.ZDIFF or RedisCommand.ZDIFFSTORE) && (!weights.IsEmpty || aggregate != Aggregate.Sum))
            {
                throw new ArgumentException($"{command} cannot be used with weights or aggregation.");
            }

            if (!weights.IsEmpty && keys.Length != weights.Length)
            {
                throw new ArgumentException("Keys and weights should have the same number of elements.", nameof(weights));
            }

            return command;
        }

        /// <summary>
        /// The body every combination shares: numkeys, the keys, the optional weights and aggregation, and
        /// for the STORE forms a destination in front.
        /// </summary>
        /// <remarks>
        /// <b>Composed rather than interpolated</b>, and for a reason the other variadic commands do not
        /// have: the weights are a run of <c>double</c>, and a run is only a hole when it is a span of
        /// something the handler knows. A <c>ref struct</c> cannot implement <see cref="IRespArgument"/> on
        /// every target this library builds for, and copying the doubles into a <c>RedisValue[]</c> just to
        /// make them a hole would allocate on a path that has no other reason to. So this is what Compose
        /// is for - the same answer BITFIELD reached for a different reason.
        /// </remarks>
        private static ValueTask<TResult> CombineCore<TResult>(
            in RespSortedSets sortedSets,
            RedisCommand command,
            RedisKey destination,
            ReadOnlySpan<RedisKey> keys,
            ReadOnlySpan<double> weights,
            Aggregate aggregate,
            bool withScores,
            CommandFlags flags)
        {
            var argHint = 2 + keys.Length + (weights.IsEmpty ? 0 : 1 + weights.Length) + 3;
            var cmd = sortedSets.Context.Compose(command, argHint);
            try
            {
                if (!destination.IsNull) cmd.AppendFormatted(destination);
                cmd.AppendFormatted((RedisValue)keys.Length);
                cmd.AppendFormatted(keys);

                if (!weights.IsEmpty)
                {
                    cmd.AppendFormatted(RespLiterals.Weights);
                    foreach (var weight in weights)
                    {
                        cmd.AppendFormatted((RedisValue)weight);
                    }
                }

                cmd.AppendFormatted(AsFragment(aggregate));
                if (withScores) cmd.AppendFormatted(RespLiterals.WithScores);
            }
            catch
            {
                cmd.Dispose();
                throw;
            }

            var frame = cmd.Complete();
            return sortedSets.Context.SendAsync(ref frame, flags, RespHandlers.Inbuilt<TResult>.Require(), default);
        }

        /// <summary>A lexical bound, shared with the MessageWriter path; see RedisDatabase.GetLexRange.</summary>
        private static RedisValue Lex(in RedisValue value, Exclude exclude, bool isStart, Order order = Order.Ascending)
            => RedisDatabase.GetLexRange(value, exclude, isStart, order);

        /// <summary>The <c>AGGREGATE mode</c> pair; SUM is the server's default and writes nothing.</summary>
        private static RespAggregate AsFragment(Aggregate aggregate) => new(aggregate);
    }

    /// <summary>
    /// EXPERIMENTAL SPIKE. The option tokens of <c>ZADD</c>: up to six of them, in the order the server
    /// documents.
    /// </summary>
    /// <remarks>
    /// The retry category belongs here too, because it depends on the same flags: NX/XX are conditional
    /// and GT/LT are monotone, so a replay converges - but <c>INCR</c> compounds on every call unless NX
    /// makes a replay a no-op. That is <c>SortedSetAddMessage.GetRetryCategory</c>'s rule, kept with the
    /// tokens it belongs to rather than at each of the three call sites.
    /// </remarks>
    internal readonly struct RespSortedSetOptions : IRespArgument
    {
        private const SortedSetWhen Known =
            SortedSetWhen.Exists | SortedSetWhen.GreaterThan | SortedSetWhen.LessThan | SortedSetWhen.NotExists;

        private readonly SortedSetWhen _when;
        private readonly bool _change;
        private readonly bool _increment;

        internal RespSortedSetOptions(SortedSetWhen when, bool change, bool increment)
        {
            if ((when & ~Known) != 0) throw new ArgumentOutOfRangeException(nameof(when));

            _when = when;
            _change = change;
            _increment = increment;
        }

        /// <summary>
        /// ZADD covers three very different side-effect profiles depending on its options, so the
        /// per-command default (last-wins) is only right for the plain form.
        /// </summary>
        internal CommandFlags RetryCategory
        {
            get
            {
                if (!_increment)
                {
                    // NX/XX are conditional; GT/LT are monotone, so re-applying the same score converges.
                    // A bare ZADD is an unconditional overwrite: leave the per-command default alone.
                    return _when == SortedSetWhen.Always ? CommandFlags.None : CommandFlags.CommandRetryWriteChecked;
                }

                // ZADD ... INCR compounds on every call - *unless* NX, where a replay can only find the
                // member present and no-op.
                return (_when & SortedSetWhen.NotExists) != 0
                    ? CommandFlags.CommandRetryWriteChecked
                    : CommandFlags.CommandRetryWriteAccumulating;
            }
        }

        /// <inheritdoc/>
        public void WriteTo(scoped ref RespRequestBuilder handler)
        {
            if ((_when & SortedSetWhen.NotExists) != 0) handler.AppendFormatted(RespLiterals.Nx);
            if ((_when & SortedSetWhen.Exists) != 0) handler.AppendFormatted(RespLiterals.Xx);
            if ((_when & SortedSetWhen.GreaterThan) != 0) handler.AppendFormatted(RespLiterals.Gt);
            if ((_when & SortedSetWhen.LessThan) != 0) handler.AppendFormatted(RespLiterals.Lt);
            if (_change) handler.AppendFormatted(RespLiterals.Ch);
            if (_increment) handler.AppendFormatted(RespLiterals.Incr);
        }
    }

    /// <summary>
    /// EXPERIMENTAL SPIKE. A <c>ZRANGESTORE</c> bound, which brackets an inclusive LEXICAL bound where the
    /// read commands leave it bare.
    /// </summary>
    /// <remarks>
    /// The asymmetry is the server's rather than ours, and it is exactly the kind of thing that gets
    /// "tidied up" into a bug - so it lives in one place and is named for what it is.
    /// </remarks>
    internal readonly struct RespRangeStoreBound : IRespArgument
    {
        private readonly RedisValue _value;
        private readonly bool _exclusive;
        private readonly bool _lex;

        private RespRangeStoreBound(in RedisValue value, bool exclusive, bool lex)
        {
            _value = value;
            _exclusive = exclusive;
            _lex = lex;
        }

        /// <summary>The lower bound of a ZRANGESTORE range.</summary>
        internal static RespRangeStoreBound Start(in RedisValue value, Exclude exclude, SortedSetOrder order)
            => new(value, (exclude & Exclude.Start) != 0, order == SortedSetOrder.ByLex);

        /// <summary>The upper bound of a ZRANGESTORE range.</summary>
        internal static RespRangeStoreBound Stop(in RedisValue value, Exclude exclude, SortedSetOrder order)
            => new(value, (exclude & Exclude.Stop) != 0, order == SortedSetOrder.ByLex);

        /// <inheritdoc/>
        public void WriteTo(scoped ref RespRequestBuilder handler)
        {
            if (_exclusive) handler.AppendFormatted(("(" + _value).AsRedisValue());
            else if (_lex) handler.AppendFormatted(("[" + _value).AsRedisValue());
            else handler.AppendFormatted(_value);
        }
    }

    /// <summary>
    /// EXPERIMENTAL SPIKE. The <c>AGGREGATE mode</c> pair, which writes two arguments or none.
    /// </summary>
    /// <remarks>
    /// <c>SUM</c> is the server's own default, so it renders as nothing at all - the same arrangement as
    /// BYTE on <c>BITCOUNT</c> and KEEPTTL's absence on SET.
    /// </remarks>
    internal readonly struct RespAggregate(Aggregate aggregate) : IRespArgument
    {
        /// <inheritdoc/>
        public void WriteTo(scoped ref RespRequestBuilder handler)
        {
            switch (aggregate)
            {
                case Aggregate.Sum:
                    return;
                case Aggregate.Min:
                    handler.AppendFormatted(RespLiterals.Aggregate);
                    handler.AppendFormatted(RespLiterals.Min);
                    return;
                case Aggregate.Max:
                    handler.AppendFormatted(RespLiterals.Aggregate);
                    handler.AppendFormatted(RespLiterals.Max);
                    return;
                case Aggregate.Count:
                    handler.AppendFormatted(RespLiterals.Aggregate);
                    handler.AppendFormatted(RespLiterals.Count);
                    return;
                default:
                    throw new ArgumentOutOfRangeException(nameof(aggregate));
            }
        }
    }

    /// <summary>
    /// EXPERIMENTAL SPIKE. The <c>LIMIT offset count</c> triple, which writes three arguments or none.
    /// </summary>
    /// <remarks>
    /// <c>(0, -1)</c> is "everything", which is the server's own behaviour without the operand - so the
    /// defaults write nothing at all, and only a caller who asked for a window pays for one.
    /// </remarks>
    internal readonly struct RespLimitRange(long skip, long take) : IRespArgument
    {
        /// <summary>
        /// No window at all - which is <c>(0, -1)</c>, not <c>default</c>.
        /// </summary>
        /// <remarks>
        /// <b>The sentinel is not the zero value</b>, and that is worth a name rather than a literal: a
        /// <c>default</c> instance is <c>(0, 0)</c>, which is a perfectly meaningful window of nothing, and
        /// writing <c>default</c> where "no window" was meant renders <c>LIMIT 0 0</c> and quietly returns
        /// an empty range. Found exactly that way.
        /// </remarks>
        internal static RespLimitRange None => new(0, -1);

        /// <inheritdoc/>
        public void WriteTo(scoped ref RespRequestBuilder handler)
        {
            if (skip == 0 && take == -1) return; // the server's own behaviour without the operand

            handler.AppendFormatted(RespLiterals.Limit);
            handler.AppendFormatted((RedisValue)skip);
            handler.AppendFormatted((RedisValue)take);
        }
    }
}
