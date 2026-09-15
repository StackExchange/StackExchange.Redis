using System;
using System.Diagnostics.CodeAnalysis;
using System.Threading.Tasks;
using RESPite;

namespace StackExchange.Redis.Interpolated
{
    /// <summary>
    /// EXPERIMENTAL SPIKE. The list-command group: <c>target.Lists.LeftPush(...)</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The old surface spells these with a <c>List</c> prefix, so within the group it goes:
    /// <c>ListLeftPop</c> becomes <c>Lists.LeftPop</c>. The left/right halves stay in the method name
    /// rather than becoming a <see cref="ListSide"/> parameter, because that is how the server names the
    /// commands and how every caller already thinks about them - <c>LPUSH</c> and <c>RPUSH</c> are two
    /// commands, not one with an operand. <c>Move</c> is the exception, and there the side genuinely is an
    /// operand.
    /// </para>
    /// <para>
    /// <c>RPOPLPUSH</c> is <b>not</b> here: it is exactly <c>LMOVE src dst RIGHT LEFT</c>, deprecated in
    /// its favour since 6.2, and the same kind of spelling relic as <c>GETSET</c>. The adapter keeps the
    /// old name working by naming the sides.
    /// </para>
    /// </remarks>
    [Experimental(Experiments.InterpolatedWriter, UrlFormat = Experiments.UrlFormat)]
    public readonly struct RespLists
    {
        private readonly RespContext _context;

        /// <summary>Group the list commands of a context.</summary>
        /// <param name="context">The context to send through.</param>
        public RespLists(in RespContext context) => _context = context;

        /// <summary>The underlying context.</summary>
        public RespContext Context => _context;
    }

    public static partial class RespSurface
    {
        extension(IRespKeyspaceTarget target)
        {
            /// <summary>The list commands.</summary>
            public RespLists Lists => new(target.Context);
        }

        extension(in RespContext context)
        {
            /// <summary>The list commands.</summary>
            public RespLists Lists => new(context);
        }

        // ---- reads -------------------------------------------------------------------------------------

        /// <summary>LINDEX.</summary>
        /// <param name="lists">The list command group.</param>
        /// <param name="key">The key to read.</param>
        /// <param name="index">The index to read; negative counts back from the end.</param>
        /// <param name="flags">Command flags.</param>
        public static ValueTask<RedisValue> GetByIndex(this in RespLists lists, RedisKey key, long index, CommandFlags flags = CommandFlags.None)
            => lists.Context.SendAsync<RedisValue>(
                $"{RedisCommand.LINDEX}{key}{index}", flags.WithDefaultCategory(RedisCommand.LINDEX));

        /// <summary>LLEN.</summary>
        /// <param name="lists">The list command group.</param>
        /// <param name="key">The key to measure.</param>
        /// <param name="flags">Command flags.</param>
        public static ValueTask<long> Length(this in RespLists lists, RedisKey key, CommandFlags flags = CommandFlags.None)
            => lists.Context.SendAsync<long>(
                $"{RedisCommand.LLEN}{key}", flags.WithDefaultCategory(RedisCommand.LLEN));

        /// <summary>LRANGE.</summary>
        /// <param name="lists">The list command group.</param>
        /// <param name="key">The key to read.</param>
        /// <param name="start">The first index to take; negative counts back from the end.</param>
        /// <param name="stop">The last index to take; negative counts back from the end.</param>
        /// <param name="flags">Command flags.</param>
        /// <remarks>A pooled lease, not an array, and it must be disposed; see <c>Strings.Get</c>.</remarks>
        public static ValueTask<ReadOnlyLease<RedisValue>> Range(this in RespLists lists, RedisKey key, long start = 0, long stop = -1, CommandFlags flags = CommandFlags.None)
            => lists.Context.SendAsync(
                $"{RedisCommand.LRANGE}{key}{start}{stop}",
                flags.WithDefaultCategory(RedisCommand.LRANGE),
                RespHandlers.ValueLease);

        /// <summary>LPOS: where an element sits, or -1.</summary>
        /// <param name="lists">The list command group.</param>
        /// <param name="key">The key to search.</param>
        /// <param name="element">The element to look for.</param>
        /// <param name="rank">Which match to report; negative searches from the tail.</param>
        /// <param name="maxLength">How many entries to compare before giving up; zero for the whole list.</param>
        /// <param name="flags">Command flags.</param>
        /// <remarks>
        /// <c>RANK</c> and <c>MAXLEN</c> are always written, as the old surface writes them - the server's
        /// defaults are the same values, so this costs four arguments and keeps one shape.
        /// </remarks>
        public static ValueTask<long> Position(
            this in RespLists lists,
            RedisKey key,
            RedisValue element,
            long rank = 1,
            long maxLength = 0,
            CommandFlags flags = CommandFlags.None)
            => WhenDiscarded(
                lists.Context.SendAsync(
                    $"{RedisCommand.LPOS}{key}{element}{RespLiterals.Rank}{rank}{RespLiterals.MaxLen}{maxLength}",
                    flags.WithDefaultCategory(RedisCommand.LPOS),
                    RespHandlers.Int64OrMinusOne),
                flags,
                -1L);

        /// <summary>LPOS ... COUNT: where several matching elements sit.</summary>
        /// <param name="lists">The list command group.</param>
        /// <param name="key">The key to search.</param>
        /// <param name="element">The element to look for.</param>
        /// <param name="count">How many matches to report; zero for all of them.</param>
        /// <param name="rank">Which match to start from; negative searches from the tail.</param>
        /// <param name="maxLength">How many entries to compare before giving up; zero for the whole list.</param>
        /// <param name="flags">Command flags.</param>
        /// <remarks>The lease must be disposed.</remarks>
        public static ValueTask<ReadOnlyLease<long>> Positions(
            this in RespLists lists,
            RedisKey key,
            RedisValue element,
            long count,
            long rank = 1,
            long maxLength = 0,
            CommandFlags flags = CommandFlags.None)
            => WhenDiscarded(
                lists.Context.SendAsync<ReadOnlyLease<long>>(
                    $"{RedisCommand.LPOS}{key}{element}{RespLiterals.Rank}{rank}{RespLiterals.MaxLen}{maxLength}{RespLiterals.Count}{count}",
                    flags.WithDefaultCategory(RedisCommand.LPOS)),
                flags,
                ReadOnlyLease<long>.Empty);

        // ---- pushes ------------------------------------------------------------------------------------

        /// <summary>LPUSH, or LPUSHX when the list must already exist.</summary>
        /// <param name="lists">The list command group.</param>
        /// <param name="key">The key to write.</param>
        /// <param name="value">The value to push.</param>
        /// <param name="when">Whether the list must already exist.</param>
        /// <param name="flags">Command flags.</param>
        /// <remarks>
        /// <c>LPUSHX</c> survives for the reason <c>HSETNX</c> did and <c>SETNX</c> did not: <c>LPUSH</c>
        /// has no conditional operand at all, so the two really are two commands.
        /// </remarks>
        public static ValueTask<long> LeftPush(this in RespLists lists, RedisKey key, RedisValue value, When when = When.Always, CommandFlags flags = CommandFlags.None)
            => Push(in lists, key, value, when, left: true, flags);

        /// <inheritdoc cref="LeftPush(in RespLists, RedisKey, RedisValue, When, CommandFlags)"/>
        /// <param name="lists">The list command group.</param>
        /// <param name="key">The key to write.</param>
        /// <param name="values">The values to push.</param>
        /// <param name="when">Whether the list must already exist.</param>
        /// <param name="flags">Command flags.</param>
        /// <remarks>
        /// <b>Pushing nothing asks the length instead.</b> An arity-zero <c>LPUSH</c> is a server error,
        /// but the question "how long is it now" still has an answer, and that is what the old surface
        /// returns - so an empty run sends <c>LLEN</c>. Kept because the reply is observable and callers
        /// building a batch from a filtered collection do hit it.
        /// </remarks>
        public static ValueTask<long> LeftPush(this in RespLists lists, RedisKey key, ReadOnlySpan<RedisValue> values, When when = When.Always, CommandFlags flags = CommandFlags.None)
            => Push(in lists, key, values, when, left: true, flags);

        /// <inheritdoc cref="LeftPush(in RespLists, RedisKey, RedisValue, When, CommandFlags)"/>
        /// <param name="lists">The list command group.</param>
        /// <param name="key">The key to write.</param>
        /// <param name="value">The value to push.</param>
        /// <param name="when">Whether the list must already exist.</param>
        /// <param name="flags">Command flags.</param>
        public static ValueTask<long> RightPush(this in RespLists lists, RedisKey key, RedisValue value, When when = When.Always, CommandFlags flags = CommandFlags.None)
            => Push(in lists, key, value, when, left: false, flags);

        /// <inheritdoc cref="LeftPush(in RespLists, RedisKey, ReadOnlySpan{RedisValue}, When, CommandFlags)"/>
        /// <param name="lists">The list command group.</param>
        /// <param name="key">The key to write.</param>
        /// <param name="values">The values to push.</param>
        /// <param name="when">Whether the list must already exist.</param>
        /// <param name="flags">Command flags.</param>
        public static ValueTask<long> RightPush(this in RespLists lists, RedisKey key, ReadOnlySpan<RedisValue> values, When when = When.Always, CommandFlags flags = CommandFlags.None)
            => Push(in lists, key, values, when, left: false, flags);

        // ---- pops --------------------------------------------------------------------------------------

        /// <summary>LPOP.</summary>
        /// <param name="lists">The list command group.</param>
        /// <param name="key">The key to write.</param>
        /// <param name="flags">Command flags.</param>
        public static ValueTask<RedisValue> LeftPop(this in RespLists lists, RedisKey key, CommandFlags flags = CommandFlags.None)
            => lists.Context.SendAsync<RedisValue>(
                $"{RedisCommand.LPOP}{key}", flags.WithDefaultCategory(RedisCommand.LPOP));

        /// <summary>LPOP with a count.</summary>
        /// <param name="lists">The list command group.</param>
        /// <param name="key">The key to write.</param>
        /// <param name="count">How many to take.</param>
        /// <param name="flags">Command flags.</param>
        /// <remarks>The lease must be disposed.</remarks>
        public static ValueTask<ReadOnlyLease<RedisValue>> LeftPop(this in RespLists lists, RedisKey key, long count, CommandFlags flags = CommandFlags.None)
            => lists.Context.SendAsync(
                $"{RedisCommand.LPOP}{key}{count}",
                flags.WithDefaultCategory(RedisCommand.LPOP),
                RespHandlers.ValueLease);

        /// <summary>RPOP.</summary>
        /// <param name="lists">The list command group.</param>
        /// <param name="key">The key to write.</param>
        /// <param name="flags">Command flags.</param>
        public static ValueTask<RedisValue> RightPop(this in RespLists lists, RedisKey key, CommandFlags flags = CommandFlags.None)
            => lists.Context.SendAsync<RedisValue>(
                $"{RedisCommand.RPOP}{key}", flags.WithDefaultCategory(RedisCommand.RPOP));

        /// <summary>RPOP with a count.</summary>
        /// <param name="lists">The list command group.</param>
        /// <param name="key">The key to write.</param>
        /// <param name="count">How many to take.</param>
        /// <param name="flags">Command flags.</param>
        /// <remarks>The lease must be disposed.</remarks>
        public static ValueTask<ReadOnlyLease<RedisValue>> RightPop(this in RespLists lists, RedisKey key, long count, CommandFlags flags = CommandFlags.None)
            => lists.Context.SendAsync(
                $"{RedisCommand.RPOP}{key}{count}",
                flags.WithDefaultCategory(RedisCommand.RPOP),
                RespHandlers.ValueLease);

        /// <summary>LMPOP: take from the first of several keys that has anything.</summary>
        /// <param name="lists">The list command group.</param>
        /// <param name="keys">The keys to try, in order.</param>
        /// <param name="count">How many to take from whichever key answers.</param>
        /// <param name="flags">Command flags.</param>
        /// <remarks>
        /// The result carries which key answered, since the caller asked about several.
        /// </remarks>
        public static ValueTask<ListPopResult> LeftPop(this in RespLists lists, ReadOnlySpan<RedisKey> keys, long count, CommandFlags flags = CommandFlags.None)
            => MultiPop(in lists, keys, count, left: true, flags);

        /// <inheritdoc cref="LeftPop(in RespLists, ReadOnlySpan{RedisKey}, long, CommandFlags)"/>
        /// <param name="lists">The list command group.</param>
        /// <param name="keys">The keys to try, in order.</param>
        /// <param name="count">How many to take from whichever key answers.</param>
        /// <param name="flags">Command flags.</param>
        public static ValueTask<ListPopResult> RightPop(this in RespLists lists, ReadOnlySpan<RedisKey> keys, long count, CommandFlags flags = CommandFlags.None)
            => MultiPop(in lists, keys, count, left: false, flags);

        // ---- moves and edits ---------------------------------------------------------------------------

        /// <summary>LMOVE: take one element from one end of a list and put it on an end of another.</summary>
        /// <param name="lists">The list command group.</param>
        /// <param name="sourceKey">The key to take from.</param>
        /// <param name="destinationKey">The key to add to.</param>
        /// <param name="sourceSide">Which end to take from.</param>
        /// <param name="destinationSide">Which end to add to.</param>
        /// <param name="flags">Command flags.</param>
        /// <remarks>
        /// The one place a <see cref="ListSide"/> is a parameter rather than part of the name, because
        /// here the server takes it as an operand too. <c>RPOPLPUSH</c> is this command with both sides
        /// named, which is why it has no method of its own.
        /// </remarks>
        public static ValueTask<RedisValue> Move(
            this in RespLists lists,
            RedisKey sourceKey,
            RedisKey destinationKey,
            ListSide sourceSide,
            ListSide destinationSide,
            CommandFlags flags = CommandFlags.None)
            => lists.Context.SendAsync<RedisValue>(
                $"{RedisCommand.LMOVE}{sourceKey}{destinationKey}{AsFragment(sourceSide)}{AsFragment(destinationSide)}",
                flags.WithDefaultCategory(RedisCommand.LMOVE));

        /// <summary>
        /// <c>LMOVE src dst RIGHT LEFT</c> where the server has it, <c>RPOPLPUSH</c> where it does not:
        /// the old <c>ListRightPopLeftPush</c> shape, and nothing more.
        /// </summary>
        /// <param name="lists">The list command group.</param>
        /// <param name="sourceKey">The key to take from.</param>
        /// <param name="destinationKey">The key to add to.</param>
        /// <param name="flags">Command flags.</param>
        /// <remarks>
        /// Internal, and deliberately not part of the group's own surface: naming both sides is all
        /// <c>RPOPLPUSH</c> is, so <see cref="Move(in RespLists, RedisKey, RedisKey, ListSide, ListSide, CommandFlags)"/>
        /// is the method callers want. This exists so that a caller of the <b>old</b> method keeps working
        /// against a server older than 6.2, where <c>LMOVE</c> does not exist.
        /// </remarks>
        internal static ValueTask<RedisValue> RightPopLeftPush(
            this in RespLists lists,
            RedisKey sourceKey,
            RedisKey destinationKey,
            CommandFlags flags = CommandFlags.None)
        {
            var context = lists.Context;
            if (context.CommandMap.IsAvailable(RedisCommand.LMOVE)
                && context.TryGetFeatures(RedisCommand.LMOVE, in sourceKey, flags, out var features)
                && features.ListMove)
            {
                return Move(in lists, sourceKey, destinationKey, ListSide.Right, ListSide.Left, flags);
            }

            // the version is asked about the SOURCE key, which is the one the command routes on; in a
            // cluster both keys are in the same slot anyway, or the server rejects the call
            return context.SendAsync<RedisValue>(
                $"{RedisCommand.RPOPLPUSH}{sourceKey}{destinationKey}",
                flags.WithDefaultCategory(RedisCommand.RPOPLPUSH));
        }

        /// <summary>LMOVEM: the bulk form, moving several elements at once.</summary>
        /// <param name="lists">The list command group.</param>
        /// <param name="sourceKey">The key to take from.</param>
        /// <param name="destinationKey">The key to add to.</param>
        /// <param name="sourceSide">Which end to take from.</param>
        /// <param name="destinationSide">Which end to add to.</param>
        /// <param name="count">How many to move.</param>
        /// <param name="mode">Whether <paramref name="count"/> is a maximum or an exact requirement.</param>
        /// <param name="order">Whether the elements move as a block or one at a time.</param>
        /// <param name="flags">Command flags.</param>
        /// <remarks>
        /// <see langword="null"/> - not empty - when nothing moved, which is the one array reply in this
        /// library where those two are different answers; see <see cref="RespHandlers.NullableValueLease"/>.
        /// The lease must be disposed.
        /// </remarks>
        public static ValueTask<ReadOnlyLease<RedisValue>?> Move(
            this in RespLists lists,
            RedisKey sourceKey,
            RedisKey destinationKey,
            ListSide sourceSide,
            ListSide destinationSide,
            long count,
            ListMoveCount mode = ListMoveCount.UpTo,
            ListMoveOrder order = ListMoveOrder.Bulk,
            CommandFlags flags = CommandFlags.None)
            => lists.Context.SendAsync(
                $"{RedisCommand.LMOVEM}{sourceKey}{destinationKey}{AsFragment(sourceSide)}{AsFragment(destinationSide)}{AsFragment(mode)}{count}{AsFragment(order)}",
                flags.WithDefaultCategory(RedisCommand.LMOVEM),
                RespHandlers.NullableValueLease);

        /// <summary>LINSERT ... BEFORE.</summary>
        /// <param name="lists">The list command group.</param>
        /// <param name="key">The key to write.</param>
        /// <param name="pivot">The existing element to insert next to.</param>
        /// <param name="value">The value to insert.</param>
        /// <param name="flags">Command flags.</param>
        public static ValueTask<long> InsertBefore(this in RespLists lists, RedisKey key, RedisValue pivot, RedisValue value, CommandFlags flags = CommandFlags.None)
            => lists.Context.SendAsync<long>(
                $"{RedisCommand.LINSERT}{key}{RespLiterals.Before}{pivot}{value}", flags.WithDefaultCategory(RedisCommand.LINSERT));

        /// <summary>LINSERT ... AFTER.</summary>
        /// <param name="lists">The list command group.</param>
        /// <param name="key">The key to write.</param>
        /// <param name="pivot">The existing element to insert next to.</param>
        /// <param name="value">The value to insert.</param>
        /// <param name="flags">Command flags.</param>
        public static ValueTask<long> InsertAfter(this in RespLists lists, RedisKey key, RedisValue pivot, RedisValue value, CommandFlags flags = CommandFlags.None)
            => lists.Context.SendAsync<long>(
                $"{RedisCommand.LINSERT}{key}{RespLiterals.After}{pivot}{value}", flags.WithDefaultCategory(RedisCommand.LINSERT));

        /// <summary>LREM; the reply is how many were removed.</summary>
        /// <param name="lists">The list command group.</param>
        /// <param name="key">The key to write.</param>
        /// <param name="value">The value to remove.</param>
        /// <param name="count">How many to remove; zero for all, negative to work from the tail.</param>
        /// <param name="flags">Command flags.</param>
        public static ValueTask<long> Remove(this in RespLists lists, RedisKey key, RedisValue value, long count = 0, CommandFlags flags = CommandFlags.None)
            => lists.Context.SendAsync<long>(
                $"{RedisCommand.LREM}{key}{count}{value}", flags.WithDefaultCategory(RedisCommand.LREM));

        /// <summary>LSET.</summary>
        /// <param name="lists">The list command group.</param>
        /// <param name="key">The key to write.</param>
        /// <param name="index">The index to write; negative counts back from the end.</param>
        /// <param name="value">The value to write.</param>
        /// <param name="flags">Command flags.</param>
        /// <remarks>Result-less: the reply is <c>+OK</c> and carries nothing, but is still read, because
        /// an error is the only thing such a call can report.</remarks>
        public static ValueTask SetByIndex(this in RespLists lists, RedisKey key, long index, RedisValue value, CommandFlags flags = CommandFlags.None)
            => lists.Context.SendAsync(
                $"{RedisCommand.LSET}{key}{index}{value}", flags.WithDefaultCategory(RedisCommand.LSET));

        /// <summary>LTRIM.</summary>
        /// <param name="lists">The list command group.</param>
        /// <param name="key">The key to write.</param>
        /// <param name="start">The first index to keep.</param>
        /// <param name="stop">The last index to keep.</param>
        /// <param name="flags">Command flags.</param>
        /// <remarks><inheritdoc cref="SetByIndex" path="/remarks"/></remarks>
        public static ValueTask Trim(this in RespLists lists, RedisKey key, long start, long stop, CommandFlags flags = CommandFlags.None)
            => lists.Context.SendAsync(
                $"{RedisCommand.LTRIM}{key}{start}{stop}", flags.WithDefaultCategory(RedisCommand.LTRIM));

        // ---- the array shapes IDatabase still needs ----------------------------------------------------
        // Internal, as everywhere else: the array is the OLD spelling, it allocates where the lease need
        // not, and nothing outside this assembly should be able to choose it. See Strings.GetArray.

        /// <inheritdoc cref="Range"/>
        /// <param name="lists">The list command group.</param>
        /// <param name="key">The key to read.</param>
        /// <param name="start">The first index to take.</param>
        /// <param name="stop">The last index to take.</param>
        /// <param name="flags">Command flags.</param>
        internal static ValueTask<RedisValue[]> RangeArray(this in RespLists lists, RedisKey key, long start = 0, long stop = -1, CommandFlags flags = CommandFlags.None)
            => lists.Context.SendAsync(
                $"{RedisCommand.LRANGE}{key}{start}{stop}",
                flags.WithDefaultCategory(RedisCommand.LRANGE),
                RespHandlers.Values);

        /// <inheritdoc cref="Positions"/>
        /// <param name="lists">The list command group.</param>
        /// <param name="key">The key to search.</param>
        /// <param name="element">The element to look for.</param>
        /// <param name="count">How many matches to report.</param>
        /// <param name="rank">Which match to start from.</param>
        /// <param name="maxLength">How many entries to compare before giving up.</param>
        /// <param name="flags">Command flags.</param>
        internal static ValueTask<long[]> PositionsArray(
            this in RespLists lists,
            RedisKey key,
            RedisValue element,
            long count,
            long rank = 1,
            long maxLength = 0,
            CommandFlags flags = CommandFlags.None)
            => WhenDiscarded(
                lists.Context.SendAsync<long[]>(
                    $"{RedisCommand.LPOS}{key}{element}{RespLiterals.Rank}{rank}{RespLiterals.MaxLen}{maxLength}{RespLiterals.Count}{count}",
                    flags.WithDefaultCategory(RedisCommand.LPOS)),
                flags,
                Array.Empty<long>());

        /// <inheritdoc cref="LeftPop(in RespLists, RedisKey, long, CommandFlags)"/>
        /// <param name="lists">The list command group.</param>
        /// <param name="key">The key to write.</param>
        /// <param name="count">How many to take.</param>
        /// <param name="flags">Command flags.</param>
        internal static ValueTask<RedisValue[]> LeftPopArray(this in RespLists lists, RedisKey key, long count, CommandFlags flags = CommandFlags.None)
            => lists.Context.SendAsync(
                $"{RedisCommand.LPOP}{key}{count}",
                flags.WithDefaultCategory(RedisCommand.LPOP),
                RespHandlers.Values);

        /// <inheritdoc cref="RightPop(in RespLists, RedisKey, long, CommandFlags)"/>
        /// <param name="lists">The list command group.</param>
        /// <param name="key">The key to write.</param>
        /// <param name="count">How many to take.</param>
        /// <param name="flags">Command flags.</param>
        internal static ValueTask<RedisValue[]> RightPopArray(this in RespLists lists, RedisKey key, long count, CommandFlags flags = CommandFlags.None)
            => lists.Context.SendAsync(
                $"{RedisCommand.RPOP}{key}{count}",
                flags.WithDefaultCategory(RedisCommand.RPOP),
                RespHandlers.Values);

        /// <inheritdoc cref="Move(in RespLists, RedisKey, RedisKey, ListSide, ListSide, long, ListMoveCount, ListMoveOrder, CommandFlags)"/>
        /// <param name="lists">The list command group.</param>
        /// <param name="sourceKey">The key to take from.</param>
        /// <param name="destinationKey">The key to add to.</param>
        /// <param name="sourceSide">Which end to take from.</param>
        /// <param name="destinationSide">Which end to add to.</param>
        /// <param name="count">How many to move.</param>
        /// <param name="mode">Whether the count is a maximum or exact.</param>
        /// <param name="order">Whether the elements move as a block or one at a time.</param>
        /// <param name="flags">Command flags.</param>
        /// <remarks>Null stays null here, as the old signature promises.</remarks>
        internal static ValueTask<RedisValue[]?> MoveArray(
            this in RespLists lists,
            RedisKey sourceKey,
            RedisKey destinationKey,
            ListSide sourceSide,
            ListSide destinationSide,
            long count,
            ListMoveCount mode = ListMoveCount.UpTo,
            ListMoveOrder order = ListMoveOrder.Bulk,
            CommandFlags flags = CommandFlags.None)
            => lists.Context.SendAsync(
                $"{RedisCommand.LMOVEM}{sourceKey}{destinationKey}{AsFragment(sourceSide)}{AsFragment(destinationSide)}{AsFragment(mode)}{count}{AsFragment(order)}",
                flags.WithDefaultCategory(RedisCommand.LMOVEM),
                RespHandlers.NullableValues);

        // ---- shared -------------------------------------------------------------------------------------

        /// <summary>
        /// The value a call reports when it has declined its reply.
        /// </summary>
        /// <typeparam name="T">The result type.</typeparam>
        /// <param name="pending">The send, which still happens; only the answer is a stand-in.</param>
        /// <param name="flags">The command's flags.</param>
        /// <param name="value">What to report instead.</param>
        /// <remarks>
        /// <para>
        /// Fire-and-forget produces no reply, so there is nothing to parse and the executor reports
        /// <c>default</c>. For most commands that is the right stand-in; for a few it is a <i>wrong
        /// answer</i> rather than an absent one - <c>LPOS</c> reports -1 for "not found", and zero is a
        /// perfectly good position. The old surface says so explicitly (<c>defaultValue: -1</c>), so this
        /// does too.
        /// </para>
        /// <para>
        /// The reply is still consumed - a ValueTask must be awaited exactly once whatever it carries -
        /// and the send is unaffected; only the value the caller sees is substituted.
        /// </para>
        /// <para>
        /// Local to the surface on purpose, but general: the same question exists for every array-shaped
        /// command, where <c>default</c> is a null array and the old surface promises an empty one. That
        /// wants a real "no-reply value" on the executor rather than a wrapper per command.
        /// </para>
        /// </remarks>
        private static ValueTask<T> WhenDiscarded<T>(ValueTask<T> pending, CommandFlags flags, T value)
        {
            if ((flags & CommandFlags.FireAndForget) == 0) return pending;

            if (pending.IsCompletedSuccessfully)
            {
                pending.GetAwaiter().GetResult(); // consume it; see TransitionalDatabase.Wait
                return new ValueTask<T>(value);
            }

            return Awaited(pending, value);

            static async ValueTask<T> Awaited(ValueTask<T> pending, T value)
            {
                await pending.ConfigureAwait(false);
                return value;
            }
        }

        /// <summary>LPUSH/RPUSH/LPUSHX/RPUSHX, which differ only in end and condition.</summary>
        private static ValueTask<long> Push(in RespLists lists, RedisKey key, RedisValue value, When when, bool left, CommandFlags flags)
        {
            var command = SelectPush(when, left);
            return lists.Context.SendAsync<long>($"{command}{key}{value}", flags.WithDefaultCategory(command));
        }

        /// <inheritdoc cref="Push(in RespLists, RedisKey, RedisValue, When, bool, CommandFlags)"/>
        private static ValueTask<long> Push(in RespLists lists, RedisKey key, ReadOnlySpan<RedisValue> values, When when, bool left, CommandFlags flags)
        {
            if (values.IsEmpty)
            {
                // see the remarks on LeftPush: pushing nothing still has a length to report
                return lists.Context.SendAsync<long>(
                    $"{RedisCommand.LLEN}{key}", flags.WithDefaultCategory(RedisCommand.LLEN));
            }

            var command = SelectPush(when, left);
            return lists.Context.SendAsync<long>($"{command}{key}{values}", flags.WithDefaultCategory(command));
        }

        private static RedisCommand SelectPush(When when, bool left) => when switch
        {
            When.Always => left ? RedisCommand.LPUSH : RedisCommand.RPUSH,
            When.Exists => left ? RedisCommand.LPUSHX : RedisCommand.RPUSHX,
            _ => throw new ArgumentOutOfRangeException(nameof(when), when, "This command supports Always and Exists only."),
        };

        /// <summary>LMPOP, whose only difference between ends is one token.</summary>
        private static ValueTask<ListPopResult> MultiPop(in RespLists lists, ReadOnlySpan<RedisKey> keys, long count, bool left, CommandFlags flags)
        {
            if (keys.IsEmpty) throw new ArgumentOutOfRangeException(nameof(keys), "keys must have a size of at least 1");

            var end = left ? RespLiterals.Left : RespLiterals.Right;

            // COUNT is always written, where the old builder omits it at 1. Both are valid and mean the
            // same thing; one shape is worth more than four bytes, and it matches ZMPOP next door.
            // Note what does NOT work: a RedisValue.Null in the hole to "skip" the number. A null value
            // still writes an ARGUMENT - an empty bulk string - so that would send `LMPOP 1 k LEFT ""`,
            // which the server rejects. Omitting an operand means writing no token at all, which is what
            // a zero-argument fragment is for.
            return lists.Context.SendAsync<ListPopResult>(
                $"{RedisCommand.LMPOP}{keys.Length}{keys}{end}{RespLiterals.Count}{count}",
                flags.WithDefaultCategory(RedisCommand.LMPOP));
        }

        private static RespFragment AsFragment(ListSide side) => side switch
        {
            ListSide.Left => RespLiterals.Left,
            ListSide.Right => RespLiterals.Right,
            _ => throw new ArgumentOutOfRangeException(nameof(side)),
        };

        private static RespFragment AsFragment(ListMoveCount mode) => mode switch
        {
            ListMoveCount.UpTo => RespLiterals.Count,
            ListMoveCount.Exactly => RespLiterals.Exactly,
            _ => throw new ArgumentOutOfRangeException(nameof(mode)),
        };

        private static RespFragment AsFragment(ListMoveOrder order) => order switch
        {
            ListMoveOrder.Bulk => RespLiterals.Bulk,
            ListMoveOrder.OneByOne => RespLiterals.Obo,
            _ => throw new ArgumentOutOfRangeException(nameof(order)),
        };
    }
}
