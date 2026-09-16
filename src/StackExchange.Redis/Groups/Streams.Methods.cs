using System;
using System.Diagnostics.CodeAnalysis;
using System.Threading.Tasks;
using RESPite;
using StackExchange.Redis.Interpolated;

namespace StackExchange.Redis;

/// <summary>
/// The stream commands: <c>target.Streams.LengthAsync(...)</c>.
/// </summary>
/// <remarks>
/// <para>
/// <b>Here rather than in a single surface-wide class</b>, so that a group is one place: its type, its
/// commands and its reply shapes sit in three files that sort together. The extension methods bind by
/// namespace, and the namespace is <c>StackExchange.Redis</c>, so this costs a caller nothing.
/// </para>
/// <para>
/// Every id list takes a <see cref="ReadOnlySpan{T}"/>, not an array, so calling does not force an
/// allocation on the caller.
/// </para>
/// </remarks>
public static partial class Streams
{
    /// <summary>
    /// XRANGE/XREVRANGE; the entries in a range, as a reply whose contents are windows over its buffer.
    /// </summary>
    /// <param name="streams">The stream command group.</param>
    /// <param name="key">The stream to read.</param>
    /// <param name="minId">The lowest id to include; the start of the stream when omitted.</param>
    /// <param name="maxId">The highest id to include; the end of the stream when omitted.</param>
    /// <param name="count">How many entries to return at most; all of them when omitted.</param>
    /// <param name="messageOrder">
    /// Ascending reads with <c>XRANGE</c>, descending with <c>XREVRANGE</c> - which also swaps the two
    /// bounds, since <c>XREVRANGE</c> takes them the other way round.
    /// </param>
    /// <param name="flags">Command flags.</param>
    /// <returns>
    /// A reply that must be disposed. Everything reachable from it - entries, ids, fields - points
    /// into its buffer and dies with it; <c>ToArray()</c> is the way to keep the contents.
    /// </returns>
    /// <remarks>
    /// <b>The first command on the deferred shape.</b> The old surface answers
    /// <c>StreamEntry[]</c>, which for a nested reply means one array per entry's fields plus one for
    /// the entries - measured at ~55KB for a thousand-entry read. This walks the reply where it landed
    /// instead, and allocates the reply object and nothing else.
    /// </remarks>
    public static ValueTask<RespRangeReply> RangeAsync(
        this in RespStreams streams,
        RedisKey key,
        RedisValue? minId = null,
        RedisValue? maxId = null,
        int? count = null,
        Order messageOrder = Order.Ascending,
        CommandFlags flags = CommandFlags.None)
    {
        if (count is <= 0) throw new ArgumentOutOfRangeException(nameof(count), "count must be greater than 0.");

        var min = minId ?? StreamConstants.ReadMinValue;
        var max = maxId ?? StreamConstants.ReadMaxValue;
        var command = messageOrder == Order.Ascending ? RedisCommand.XRANGE : RedisCommand.XREVRANGE;

        // XREVRANGE takes (high, low); the caller always says (min, max), so the swap happens here.
        // Two locals rather than a tuple deconstruction: Roslyn does optimise the tuple away here (all six
        // target frameworks were checked for a System.ValueTuple type reference and all are clean), but
        // this library deliberately does not reference System.ValueTuple - SanityChecks.ValueTupleNotReferenced
        // guards it, because it breaks binding on .NET Framework - and that guard only ever inspects the
        // one build the test process happened to load. Not relying on an optimisation to stay inside a
        // rule costs nothing here.
        RedisValue first = min, second = max;
        if (messageOrder != Order.Ascending)
        {
            first = max;
            second = min;
        }

        return streams.Context.SendAsync(
            $"{command}{key}{first}{second}{RespLiterals.Count.When(count)}{count}",
            flags,
            RangeReplyHandler);
    }

    private static readonly RespReplyHandler<RespRangeReply> RangeReplyHandler
        = new(static payload => new RespRangeReply(payload));

    /// <summary>XLEN; the number of entries in the stream.</summary>
    /// <param name="streams">The stream command group.</param>
    /// <param name="key">The stream to measure.</param>
    /// <param name="flags">Command flags.</param>
    public static ValueTask<long> LengthAsync(this in RespStreams streams, RedisKey key, CommandFlags flags = CommandFlags.None)
        => streams.Context.SendAsync<long>(
            $"{RedisCommand.XLEN}{key}", flags);

    /// <summary>XACK; how many of the listed entries were pending and are now acknowledged.</summary>
    /// <param name="streams">The stream command group.</param>
    /// <param name="key">The stream.</param>
    /// <param name="group">The consumer group.</param>
    /// <param name="messageId">The entry to acknowledge.</param>
    /// <param name="flags">Command flags.</param>
    public static ValueTask<long> AcknowledgeAsync(this in RespStreams streams, RedisKey key, RedisValue group, RedisValue messageId, CommandFlags flags = CommandFlags.None)
        => streams.Context.SendAsync<long>(
            $"{RedisCommand.XACK}{key}{group}{messageId}", flags);

    /// <inheritdoc cref="AcknowledgeAsync(in RespStreams, RedisKey, RedisValue, RedisValue, CommandFlags)"/>
    /// <param name="streams">The stream command group.</param>
    /// <param name="key">The stream.</param>
    /// <param name="group">The consumer group.</param>
    /// <param name="messageIds">The entries to acknowledge; at least one.</param>
    /// <param name="flags">Command flags.</param>
    /// <remarks>
    /// An empty run is <b>not</b> short-circuited to zero: <c>XACK</c> with no ids is a caller error
    /// rather than a request that trivially acknowledges nothing, and the old surface throws for it.
    /// </remarks>
    public static ValueTask<long> AcknowledgeAsync(this in RespStreams streams, RedisKey key, RedisValue group, ReadOnlySpan<RedisValue> messageIds, CommandFlags flags = CommandFlags.None)
    {
        DemandAtLeastOneId(messageIds);
        return streams.Context.SendAsync<long>(
            $"{RedisCommand.XACK}{key}{group}{messageIds}", flags);
    }

    /// <summary>XDEL; how many of the listed entries existed and were removed.</summary>
    /// <param name="streams">The stream command group.</param>
    /// <param name="key">The stream.</param>
    /// <param name="messageIds">The entries to delete; at least one.</param>
    /// <param name="flags">Command flags.</param>
    public static ValueTask<long> DeleteAsync(this in RespStreams streams, RedisKey key, ReadOnlySpan<RedisValue> messageIds, CommandFlags flags = CommandFlags.None)
    {
        DemandAtLeastOneId(messageIds);
        return streams.Context.SendAsync<long>(
            $"{RedisCommand.XDEL}{key}{messageIds}", flags);
    }

    /// <summary>
    /// XDELEX; per-id outcomes rather than a count, so a caller can tell "not there" from "not deleted".
    /// </summary>
    /// <param name="streams">The stream command group.</param>
    /// <param name="key">The stream.</param>
    /// <param name="messageIds">The entries to delete; at least one.</param>
    /// <param name="mode">What to do with entries that consumer groups still reference.</param>
    /// <param name="flags">Command flags.</param>
    /// <remarks>
    /// A <see cref="ReadOnlyLease{T}"/> of an <b>enum</b>, which is the <c>ExpireResult</c> shape and
    /// not one of the composite results still to be designed: the elements own nothing, so the lease is
    /// the whole of the storage question.
    /// </remarks>
    public static ValueTask<ReadOnlyLease<StreamTrimResult>> DeleteAsync(
        this in RespStreams streams,
        RedisKey key,
        ReadOnlySpan<RedisValue> messageIds,
        StreamTrimMode mode,
        CommandFlags flags = CommandFlags.None)
    {
        DemandAtLeastOneId(messageIds);
        return streams.Context.SendAsync<ReadOnlyLease<StreamTrimResult>>(
            $"{RedisCommand.XDELEX}{key}{TrimModeToken(mode)}{RespLiterals.Ids}{messageIds.Length}{messageIds}",
            flags);
    }

    /// <inheritdoc cref="DeleteAsync(in RespStreams, RedisKey, ReadOnlySpan{RedisValue}, StreamTrimMode, CommandFlags)"/>
    /// <remarks>
    /// <b>Permanent, not scaffolding.</b> <c>IDatabase.StreamDelete</c> promises an array and is not
    /// going anywhere, so this is how that signature is served from the new core. Internal because the
    /// array is the <i>old</i> spelling: new code reaches for the lease, and nothing outside this
    /// assembly should be able to choose otherwise.
    /// </remarks>
    internal static ValueTask<StreamTrimResult[]> DeleteArray(
        this in RespStreams streams,
        RedisKey key,
        ReadOnlySpan<RedisValue> messageIds,
        StreamTrimMode mode,
        CommandFlags flags = CommandFlags.None)
    {
        DemandAtLeastOneId(messageIds);
        return streams.Context.SendAsync<StreamTrimResult[]>(
            $"{RedisCommand.XDELEX}{key}{TrimModeToken(mode)}{RespLiterals.Ids}{messageIds.Length}{messageIds}",
            flags);
    }

    /// <summary>XGROUP CREATE.</summary>
    /// <param name="streams">The stream command group.</param>
    /// <param name="key">The stream.</param>
    /// <param name="group">The consumer group to create.</param>
    /// <param name="position">Where the group starts reading; defaults to new messages only.</param>
    /// <param name="createStream">Whether to create the stream if it does not exist (<c>MKSTREAM</c>).</param>
    /// <param name="flags">Command flags.</param>
    public static ValueTask<bool> CreateConsumerGroupAsync(
        this in RespStreams streams,
        RedisKey key,
        RedisValue group,
        RedisValue? position = null,
        bool createStream = true,
        CommandFlags flags = CommandFlags.None)
        => streams.Context.SendAsync<bool>(
            $"{RedisCommand.XGROUP}{RespLiterals.Create}{key}{group}{ResolveGroupPosition(position)}{RespLiterals.MkStream.When(createStream)}",
            flags);

    /// <summary>XGROUP DESTROY.</summary>
    /// <param name="streams">The stream command group.</param>
    /// <param name="key">The stream.</param>
    /// <param name="group">The consumer group to remove.</param>
    /// <param name="flags">Command flags.</param>
    public static ValueTask<bool> DeleteConsumerGroupAsync(this in RespStreams streams, RedisKey key, RedisValue group, CommandFlags flags = CommandFlags.None)
        => streams.Context.SendAsync<bool>(
            $"{RedisCommand.XGROUP}{RespLiterals.Destroy}{key}{group}",
            flags);

    /// <summary>XGROUP DELCONSUMER; the number of pending entries the consumer still owned.</summary>
    /// <param name="streams">The stream command group.</param>
    /// <param name="key">The stream.</param>
    /// <param name="group">The consumer group.</param>
    /// <param name="consumer">The consumer to remove.</param>
    /// <param name="flags">Command flags.</param>
    public static ValueTask<long> DeleteConsumerAsync(this in RespStreams streams, RedisKey key, RedisValue group, RedisValue consumer, CommandFlags flags = CommandFlags.None)
        => streams.Context.SendAsync<long>(
            $"{RedisCommand.XGROUP}{RespLiterals.DeleteConsumer}{key}{group}{consumer}",
            flags);

    /// <summary>XGROUP SETID; move a group's read position.</summary>
    /// <param name="streams">The stream command group.</param>
    /// <param name="key">The stream.</param>
    /// <param name="group">The consumer group.</param>
    /// <param name="position">The new position.</param>
    /// <param name="flags">Command flags.</param>
    public static ValueTask<bool> SetConsumerGroupPositionAsync(this in RespStreams streams, RedisKey key, RedisValue group, RedisValue position, CommandFlags flags = CommandFlags.None)
        => streams.Context.SendAsync<bool>(
            $"{RedisCommand.XGROUP}{RespLiterals.SetId}{key}{group}{ResolveGroupPosition(position)}",
            flags);

    /// <summary>XTRIM MAXLEN; the number of entries removed.</summary>
    /// <param name="streams">The stream command group.</param>
    /// <param name="key">The stream.</param>
    /// <param name="maxLength">The length to trim to.</param>
    /// <param name="approximate">Whether the server may stop at a node boundary (<c>~</c>).</param>
    /// <param name="limit">The most entries to remove in one call.</param>
    /// <param name="mode">What to do with references to trimmed entries.</param>
    /// <param name="flags">Command flags.</param>
    public static ValueTask<long> TrimAsync(
        this in RespStreams streams,
        RedisKey key,
        long maxLength,
        bool approximate = false,
        long? limit = null,
        StreamTrimMode mode = StreamTrimMode.KeepReferences,
        CommandFlags flags = CommandFlags.None)
        => streams.Context.SendAsync<long>(
            $"{RedisCommand.XTRIM}{key}{RespLiterals.MaxLen}{new TrimOperand(approximate, maxLength, limit, mode)}",
            flags);

    /// <summary>XTRIM MINID; the number of entries removed.</summary>
    /// <param name="streams">The stream command group.</param>
    /// <param name="key">The stream.</param>
    /// <param name="minId">The lowest id to keep.</param>
    /// <param name="approximate">Whether the server may stop at a node boundary (<c>~</c>).</param>
    /// <param name="limit">The most entries to remove in one call.</param>
    /// <param name="mode">What to do with references to trimmed entries.</param>
    /// <param name="flags">Command flags.</param>
    public static ValueTask<long> TrimByMinIdAsync(
        this in RespStreams streams,
        RedisKey key,
        RedisValue minId,
        bool approximate = false,
        long? limit = null,
        StreamTrimMode mode = StreamTrimMode.KeepReferences,
        CommandFlags flags = CommandFlags.None)
        => streams.Context.SendAsync<long>(
            $"{RedisCommand.XTRIM}{key}{RespLiterals.MinId}{new TrimOperand(approximate, minId, limit, mode)}",
            flags);

    /// <summary>
    /// The tail of an <c>XTRIM</c>: <c>[~] threshold [LIMIT n] [mode]</c>.
    /// </summary>
    /// <remarks>
    /// One operand rather than four holes, because the parts are not independent - the <c>~</c> comes
    /// before the threshold and the mode after the limit - so writing them as separate holes would put
    /// the ordering rule at every call site instead of in one place. The strategy token (<c>MAXLEN</c>
    /// or <c>MINID</c>) stays outside, since that is the one part the caller really is choosing.
    /// </remarks>
    private readonly struct TrimOperand(bool approximate, RedisValue threshold, long? limit, StreamTrimMode mode) : IRespArgument
    {
        public void WriteTo(scoped ref RespCommandHandler handler)
        {
            if (approximate) handler.AppendFormatted(RespLiterals.Approximate);
            handler.AppendFormatted(threshold);

            if (limit.HasValue)
            {
                handler.AppendFormatted(RespLiterals.Limit);
                handler.AppendFormatted((RedisValue)limit.GetValueOrDefault());
            }

            // omitted when KeepReferences, which is the server's default: an older server rejects a
            // token it has never heard of, so sending it unasked would break deployments that work today
            if (mode != StreamTrimMode.KeepReferences) handler.AppendFormatted(TrimModeToken(mode));
        }
    }

    /// <summary>
    /// The position operand of <c>XGROUP CREATE</c>/<c>SETID</c>, defaulting to new messages only.
    /// </summary>
    private static RedisValue ResolveGroupPosition(RedisValue? position)
        => StreamPosition.Resolve(position ?? StreamConstants.NewMessages, RedisCommand.XGROUP);

    /// <summary>
    /// The trim-mode token. Always written by <c>XDELEX</c>, and omitted by <c>XTRIM</c> when it is the
    /// default - which is the server's rule, not ours: <c>XTRIM</c> predates the modes, so an older
    /// server rejects a token it has never heard of.
    /// </summary>
    private static RespFragment TrimModeToken(StreamTrimMode mode) => mode switch
    {
        StreamTrimMode.KeepReferences => RespLiterals.KeepRef,
        StreamTrimMode.DeleteReferences => RespLiterals.DelRef,
        StreamTrimMode.Acknowledged => RespLiterals.Acked,
        _ => throw new ArgumentOutOfRangeException(nameof(mode)),
    };

    private static void DemandAtLeastOneId(ReadOnlySpan<RedisValue> messageIds)
    {
        if (messageIds.IsEmpty)
        {
            throw new ArgumentOutOfRangeException(nameof(messageIds), "messageIds must contain at least one item.");
        }
    }
}
