using System;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Threading.Tasks;
using RESPite;
using RESPite.Messages;
using StackExchange.Redis.Protocol;

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
    /// <param name="cancellationToken">Cancels the request; only cancellation <i>before</i> the send is honoured today.</param>
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
        CommandFlags flags = CommandFlags.None,
        CancellationToken cancellationToken = default)
    {
        var cmd = RangeCommand(streams.Context, key, minId, maxId, count, messageOrder);
        return streams.Context.SendAsync(ref cmd, flags, RangeReplyHandler, cancellationToken);
    }

    /// <inheritdoc cref="Streams.RangeAsync(in RespStreams, RedisKey, RedisValue?, RedisValue?, int?, Order, CommandFlags, CancellationToken)"/>
    /// <remarks>
    /// <para>
    /// <b>Permanent, not scaffolding.</b> <c>IDatabase</c> promises <see cref="StreamEntry"/><c>[]</c> and
    /// is not going anywhere, so this is how that signature is served from the new core - the same
    /// arrangement as the other <c>*Array</c> shims on this surface. Internal because the array is the
    /// <i>old</i> spelling.
    /// </para>
    /// <para>
    /// <b>It does not go through the reply object</b>, and that is the point: projecting with
    /// <c>reply.ToArray()</c> means an extra <c>async</c> layer wrapping the send, and a suspending async
    /// layer costs ~120 bytes - measured - on top of the reply object it allocates and immediately throws
    /// away. Supplying a handler that parses straight to the array removes both. The parse is the same
    /// function either way, so the two shapes still cannot drift.
    /// </para>
    /// </remarks>
    internal static ValueTask<StreamEntry[]> RangeArray(
        this in RespStreams streams,
        RedisKey key,
        RedisValue? minId = null,
        RedisValue? maxId = null,
        int? count = null,
        Order messageOrder = Order.Ascending,
        CommandFlags flags = CommandFlags.None,
        CancellationToken cancellationToken = default)
    {
        var cmd = RangeCommand(streams.Context, key, minId, maxId, count, messageOrder);
        return streams.Context.SendAsync(ref cmd, flags, StreamTypesHandler.Entries, cancellationToken);
    }

    /// <summary>
    /// Render <c>XRANGE</c>/<c>XREVRANGE</c> - the one place the command is composed.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>A factory, because the command text is the part that must not be written twice.</b> The two
    /// overloads above differ only in what parses the reply; everything decided here - which command, the
    /// bound swap, the optional count, the validation - is identical, and a surface where that is copied
    /// per overload is a surface where the copies drift. The older <c>RedisDatabase</c> has message
    /// factories for exactly this reason.
    /// </para>
    /// <para>
    /// The returned frame owns a pooled buffer and is consumed by the send on every path, so a caller must
    /// send it. Validation happens <i>before</i> the render, so a rejected call never rents at all.
    /// </para>
    /// </remarks>
    private static RespRequestFrame RangeCommand(
        in RespContext context,
        RedisKey key,
        RedisValue? minId,
        RedisValue? maxId,
        int? count,
        Order messageOrder)
    {
        if (count is <= 0) throw new ArgumentOutOfRangeException(nameof(count), "count must be greater than 0.");

        var min = minId ?? StreamConstants.ReadMinValue;
        var max = maxId ?? StreamConstants.ReadMaxValue;
        var command = messageOrder == Order.Ascending ? RedisCommand.XRANGE : RedisCommand.XREVRANGE;

        // XREVRANGE takes (high, low); the caller always says (min, max), so the swap happens here.
        // Two locals rather than a tuple deconstruction: this library deliberately does not reference
        // System.ValueTuple, and not relying on an optimisation to stay inside a rule costs nothing here.
        RedisValue first = min, second = max;
        if (messageOrder != Order.Ascending)
        {
            first = max;
            second = min;
        }

        return context.Render($"{command}{key}{first}{second}{RespLiterals.Count.When(count)}{count}");
    }

    /// <summary>
    /// The stream group's own reply shapes - one handler for the types only this group produces.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Group-specific parses stay out of <c>RespHandlers</c></b>, which is for types any command might
    /// answer with - <see cref="long"/>, <see cref="bool"/>, <see cref="RedisValue"/>, the leases. One
    /// handler per <i>group</i> rather than per type keeps the stream parses together and stops the shared
    /// registry filling up with shapes only one group can produce. The other groups' exotics belong in
    /// their own equivalents.
    /// </para>
    /// <para>
    /// <b>The implementations are explicit, and must be:</b> every <c>IRespHandler&lt;T&gt;.Parse</c> has
    /// the same parameter list and differs only in return type, which C# cannot overload on. Written that
    /// way from the first one so that adding the second - <c>XCLAIM</c>, <c>XREAD</c> and
    /// <c>XREADGROUP</c> all answer <see cref="StreamEntry"/><c>[]</c>, and the <c>XINFO</c> shapes are
    /// still to come - does not churn it. The named accessors are so call sites need no cast, which is
    /// how the existing multi-interface handlers read.
    /// </para>
    /// </remarks>
    private sealed class StreamTypesHandler :
        IRespHandler<StreamEntry[]>,
        IRespHandler<StreamPendingInfo>,
        IRespHandler<StreamPendingMessageInfo[]>,
        IRespHandler<StreamAutoClaimResult>,
        IRespHandler<StreamAutoClaimIdsOnlyResult>
    {
        private static readonly StreamTypesHandler Instance = new();

        /// <summary>An <c>XPENDING</c> summary as the struct the older surface promises.</summary>
        internal static IRespHandler<StreamPendingInfo> PendingInfo => Instance;

        /// <summary>An extended <c>XPENDING</c> as the array the older surface promises.</summary>
        internal static IRespHandler<StreamPendingMessageInfo[]> PendingMessages => Instance;

        /// <summary>An <c>XAUTOCLAIM</c> as the struct the older surface promises.</summary>
        internal static IRespHandler<StreamAutoClaimResult> AutoClaim => Instance;

        /// <summary>An <c>XAUTOCLAIM JUSTID</c> as the struct the older surface promises.</summary>
        internal static IRespHandler<StreamAutoClaimIdsOnlyResult> AutoClaimIdsOnly => Instance;

        /// <summary>
        /// A single-stream <c>XREAD</c>/<c>XREADGROUP</c> as the array the older surface promises.
        /// </summary>
        internal static IRespHandler<StreamEntry[]> NamedEntries => NamedEntriesHandler.Instance;

        /// <summary>An <c>XRANGE</c>-shaped reply as the array shape the older surface promises.</summary>
        /// <remarks>
        /// The same <c>ParseRedisStreamEntries</c> the reply object's <c>ToArray</c> calls, so the
        /// deferred and materialising shapes remain two call sites of one function.
        /// </remarks>
        internal static IRespHandler<StreamEntry[]> Entries => Instance;

        StreamEntry[] IRespHandler<StreamEntry[]>.Parse(ref RespReader reader)
            => ResultProcessor.ParseRedisStreamEntries(ref reader, allowJaggedFields: true);

        StreamPendingInfo IRespHandler<StreamPendingInfo>.Parse(ref RespReader reader)
            => ResultProcessor.TryParseStreamPendingInfo(ref reader, out var value) ? value : default;

        StreamPendingMessageInfo[] IRespHandler<StreamPendingMessageInfo[]>.Parse(ref RespReader reader)
            => ResultProcessor.ParseStreamPendingMessages(ref reader);

        StreamAutoClaimResult IRespHandler<StreamAutoClaimResult>.Parse(ref RespReader reader)
            => ResultProcessor.TryParseStreamAutoClaim(ref reader, allowJaggedFields: true, out var value)
                ? value : StreamAutoClaimResult.Null;

        StreamAutoClaimIdsOnlyResult IRespHandler<StreamAutoClaimIdsOnlyResult>.Parse(ref RespReader reader)
            => ResultProcessor.TryParseStreamAutoClaimIdsOnly(ref reader, out var value)
                ? value : StreamAutoClaimIdsOnlyResult.Null;
    }

    /// <summary>
    /// The name-skipping twin of <see cref="StreamTypesHandler"/>, for the read commands.
    /// </summary>
    /// <remarks>
    /// A separate class only because <c>IRespHandler&lt;StreamEntry[]&gt;</c> can be implemented once per
    /// type, and the two parse differently: <c>XRANGE</c> answers a bare run, the reads answer one inside
    /// a per-stream envelope.
    /// </remarks>
    private sealed class NamedEntriesHandler : IRespHandler<StreamEntry[]>
    {
        internal static readonly NamedEntriesHandler Instance = new();

        StreamEntry[] IRespHandler<StreamEntry[]>.Parse(ref RespReader reader)
            => reader.IsAggregate && !reader.IsNull
                ? ResultProcessor.ParseStreamWithNameSkip(ref reader, reader.Prefix == RespPrefix.Map, allowJaggedFields: true)
                : [];
    }

    private static readonly RespReplyHandler<RespRangeReply> RangeReplyHandler
        = new(static payload => new RespRangeReply(payload));

    /// <summary>XLEN; the number of entries in the stream.</summary>
    /// <param name="streams">The stream command group.</param>
    /// <param name="key">The stream to measure.</param>
    /// <param name="flags">Command flags.</param>
    /// <param name="cancellationToken">Cancels the request; only cancellation <i>before</i> the send is honoured today.</param>
    public static ValueTask<long> LengthAsync(this in RespStreams streams, RedisKey key, CommandFlags flags = CommandFlags.None, CancellationToken cancellationToken = default)
        => streams.Context.SendAsync<long>(
            $"{RedisCommand.XLEN}{key}", flags, cancellationToken: cancellationToken);

    /// <summary>XACK; how many of the listed entries were pending and are now acknowledged.</summary>
    /// <param name="streams">The stream command group.</param>
    /// <param name="key">The stream.</param>
    /// <param name="group">The consumer group.</param>
    /// <param name="messageId">The entry to acknowledge.</param>
    /// <param name="flags">Command flags.</param>
    /// <param name="cancellationToken">Cancels the request; only cancellation <i>before</i> the send is honoured today.</param>
    public static ValueTask<long> AcknowledgeAsync(this in RespStreams streams, RedisKey key, RedisValue group, RedisValue messageId, CommandFlags flags = CommandFlags.None, CancellationToken cancellationToken = default)
        => streams.Context.SendAsync<long>(
            $"{RedisCommand.XACK}{key}{group}{messageId}", flags, cancellationToken: cancellationToken);

    /// <inheritdoc cref="Streams.AcknowledgeAsync(in RespStreams, RedisKey, RedisValue, RedisValue, CommandFlags, CancellationToken)"/>
    /// <param name="streams">The stream command group.</param>
    /// <param name="key">The stream.</param>
    /// <param name="group">The consumer group.</param>
    /// <param name="messageIds">The entries to acknowledge; at least one.</param>
    /// <param name="flags">Command flags.</param>
    /// <param name="cancellationToken">Cancels the request; only cancellation <i>before</i> the send is honoured today.</param>
    /// <remarks>
    /// An empty run is <b>not</b> short-circuited to zero: <c>XACK</c> with no ids is a caller error
    /// rather than a request that trivially acknowledges nothing, and the old surface throws for it.
    /// </remarks>
    public static ValueTask<long> AcknowledgeAsync(this in RespStreams streams, RedisKey key, RedisValue group, ReadOnlySpan<RedisValue> messageIds, CommandFlags flags = CommandFlags.None, CancellationToken cancellationToken = default)
    {
        DemandAtLeastOneId(messageIds);
        return streams.Context.SendAsync<long>(
            $"{RedisCommand.XACK}{key}{group}{messageIds}", flags, cancellationToken: cancellationToken);
    }

    /// <summary>XDEL; how many of the listed entries existed and were removed.</summary>
    /// <param name="streams">The stream command group.</param>
    /// <param name="key">The stream.</param>
    /// <param name="messageIds">The entries to delete; at least one.</param>
    /// <param name="flags">Command flags.</param>
    /// <param name="cancellationToken">Cancels the request; only cancellation <i>before</i> the send is honoured today.</param>
    public static ValueTask<long> DeleteAsync(this in RespStreams streams, RedisKey key, ReadOnlySpan<RedisValue> messageIds, CommandFlags flags = CommandFlags.None, CancellationToken cancellationToken = default)
    {
        DemandAtLeastOneId(messageIds);
        return streams.Context.SendAsync<long>(
            $"{RedisCommand.XDEL}{key}{messageIds}", flags, cancellationToken: cancellationToken);
    }

    /// <summary>
    /// XDELEX; per-id outcomes rather than a count, so a caller can tell "not there" from "not deleted".
    /// </summary>
    /// <param name="streams">The stream command group.</param>
    /// <param name="key">The stream.</param>
    /// <param name="messageIds">The entries to delete; at least one.</param>
    /// <param name="mode">What to do with entries that consumer groups still reference.</param>
    /// <param name="flags">Command flags.</param>
    /// <param name="cancellationToken">Cancels the request; only cancellation <i>before</i> the send is honoured today.</param>
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
        CommandFlags flags = CommandFlags.None,
        CancellationToken cancellationToken = default)
    {
        var cmd = DeleteExCommand(streams.Context, key, messageIds, mode);
        return streams.Context.SendAsync(ref cmd, flags, RespHandlers.Inbuilt<ReadOnlyLease<StreamTrimResult>>.Require(), cancellationToken);
    }

    /// <inheritdoc cref="Streams.DeleteAsync(in RespStreams, RedisKey, ReadOnlySpan{RedisValue}, StreamTrimMode, CommandFlags, CancellationToken)"/>
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
        CommandFlags flags = CommandFlags.None,
        CancellationToken cancellationToken = default)
    {
        var cmd = DeleteExCommand(streams.Context, key, messageIds, mode);
        return streams.Context.SendAsync(ref cmd, flags, RespHandlers.Inbuilt<StreamTrimResult[]>.Require(), cancellationToken);
    }

    /// <summary>Render <c>XDELEX</c> - the one place the command is composed.</summary>
    /// <remarks><inheritdoc cref="RangeCommand" path="/remarks"/></remarks>
    private static RespRequestFrame DeleteExCommand(
        in RespContext context,
        RedisKey key,
        ReadOnlySpan<RedisValue> messageIds,
        StreamTrimMode mode)
    {
        DemandAtLeastOneId(messageIds);
        return context.Render($"{RedisCommand.XDELEX}{key}{TrimModeToken(mode)}{RespLiterals.Ids}{messageIds.Length}{messageIds}");
    }

    /// <summary>XGROUP CREATE.</summary>
    /// <param name="streams">The stream command group.</param>
    /// <param name="key">The stream.</param>
    /// <param name="group">The consumer group to create.</param>
    /// <param name="position">Where the group starts reading; defaults to new messages only.</param>
    /// <param name="createStream">Whether to create the stream if it does not exist (<c>MKSTREAM</c>).</param>
    /// <param name="flags">Command flags.</param>
    /// <param name="cancellationToken">Cancels the request; only cancellation <i>before</i> the send is honoured today.</param>
    public static ValueTask<bool> CreateConsumerGroupAsync(
        this in RespStreams streams,
        RedisKey key,
        RedisValue group,
        RedisValue? position = null,
        bool createStream = true,
        CommandFlags flags = CommandFlags.None,
        CancellationToken cancellationToken = default)
        => streams.Context.SendAsync<bool>(
            $"{RedisCommand.XGROUP}{RespLiterals.Create}{key}{group}{ResolveGroupPosition(position)}{RespLiterals.MkStream.When(createStream)}",
            flags,
            cancellationToken: cancellationToken);

    /// <summary>XGROUP DESTROY.</summary>
    /// <param name="streams">The stream command group.</param>
    /// <param name="key">The stream.</param>
    /// <param name="group">The consumer group to remove.</param>
    /// <param name="flags">Command flags.</param>
    /// <param name="cancellationToken">Cancels the request; only cancellation <i>before</i> the send is honoured today.</param>
    public static ValueTask<bool> DeleteConsumerGroupAsync(this in RespStreams streams, RedisKey key, RedisValue group, CommandFlags flags = CommandFlags.None, CancellationToken cancellationToken = default)
        => streams.Context.SendAsync<bool>(
            $"{RedisCommand.XGROUP}{RespLiterals.Destroy}{key}{group}",
            flags,
            cancellationToken: cancellationToken);

    /// <summary>XGROUP DELCONSUMER; the number of pending entries the consumer still owned.</summary>
    /// <param name="streams">The stream command group.</param>
    /// <param name="key">The stream.</param>
    /// <param name="group">The consumer group.</param>
    /// <param name="consumer">The consumer to remove.</param>
    /// <param name="flags">Command flags.</param>
    /// <param name="cancellationToken">Cancels the request; only cancellation <i>before</i> the send is honoured today.</param>
    public static ValueTask<long> DeleteConsumerAsync(this in RespStreams streams, RedisKey key, RedisValue group, RedisValue consumer, CommandFlags flags = CommandFlags.None, CancellationToken cancellationToken = default)
        => streams.Context.SendAsync<long>(
            $"{RedisCommand.XGROUP}{RespLiterals.DeleteConsumer}{key}{group}{consumer}",
            flags,
            cancellationToken: cancellationToken);

    /// <summary>XGROUP SETID; move a group's read position.</summary>
    /// <param name="streams">The stream command group.</param>
    /// <param name="key">The stream.</param>
    /// <param name="group">The consumer group.</param>
    /// <param name="position">The new position.</param>
    /// <param name="flags">Command flags.</param>
    /// <param name="cancellationToken">Cancels the request; only cancellation <i>before</i> the send is honoured today.</param>
    public static ValueTask<bool> SetConsumerGroupPositionAsync(this in RespStreams streams, RedisKey key, RedisValue group, RedisValue position, CommandFlags flags = CommandFlags.None, CancellationToken cancellationToken = default)
        => streams.Context.SendAsync<bool>(
            $"{RedisCommand.XGROUP}{RespLiterals.SetId}{key}{group}{ResolveGroupPosition(position)}",
            flags,
            cancellationToken: cancellationToken);

    /// <summary>XTRIM MAXLEN; the number of entries removed.</summary>
    /// <param name="streams">The stream command group.</param>
    /// <param name="key">The stream.</param>
    /// <param name="maxLength">The length to trim to.</param>
    /// <param name="approximate">Whether the server may stop at a node boundary (<c>~</c>).</param>
    /// <param name="limit">The most entries to remove in one call.</param>
    /// <param name="mode">What to do with references to trimmed entries.</param>
    /// <param name="flags">Command flags.</param>
    /// <param name="cancellationToken">Cancels the request; only cancellation <i>before</i> the send is honoured today.</param>
    public static ValueTask<long> TrimAsync(
        this in RespStreams streams,
        RedisKey key,
        long maxLength,
        bool approximate = false,
        long? limit = null,
        StreamTrimMode mode = StreamTrimMode.KeepReferences,
        CommandFlags flags = CommandFlags.None,
        CancellationToken cancellationToken = default)
        => streams.Context.SendAsync<long>(
            $"{RedisCommand.XTRIM}{key}{RespLiterals.MaxLen}{new TrimOperand(approximate, maxLength, limit, mode)}",
            flags,
            cancellationToken: cancellationToken);

    /// <summary>XTRIM MINID; the number of entries removed.</summary>
    /// <param name="streams">The stream command group.</param>
    /// <param name="key">The stream.</param>
    /// <param name="minId">The lowest id to keep.</param>
    /// <param name="approximate">Whether the server may stop at a node boundary (<c>~</c>).</param>
    /// <param name="limit">The most entries to remove in one call.</param>
    /// <param name="mode">What to do with references to trimmed entries.</param>
    /// <param name="flags">Command flags.</param>
    /// <param name="cancellationToken">Cancels the request; only cancellation <i>before</i> the send is honoured today.</param>
    public static ValueTask<long> TrimByMinIdAsync(
        this in RespStreams streams,
        RedisKey key,
        RedisValue minId,
        bool approximate = false,
        long? limit = null,
        StreamTrimMode mode = StreamTrimMode.KeepReferences,
        CommandFlags flags = CommandFlags.None,
        CancellationToken cancellationToken = default)
        => streams.Context.SendAsync<long>(
            $"{RedisCommand.XTRIM}{key}{RespLiterals.MinId}{new TrimOperand(approximate, minId, limit, mode)}",
            flags,
            cancellationToken: cancellationToken);

    /// <summary>XADD; the id of the entry that was appended.</summary>
    /// <param name="streams">The stream command group.</param>
    /// <param name="key">The stream.</param>
    /// <param name="name">The single field's name.</param>
    /// <param name="value">The single field's value.</param>
    /// <param name="options">Entry id, trimming and idempotency; the default appends with a server-assigned id.</param>
    /// <param name="flags">Command flags.</param>
    /// <param name="cancellationToken">Cancels the request; only cancellation <i>before</i> the send is honoured today.</param>
    /// <remarks>
    /// <b>Two overloads here against eight on <see cref="IDatabase"/>.</b> The shipped ones spell the
    /// options out positionally - message id, max length, approximate, limit, trim mode - and then repeat
    /// the whole run for the idempotent and options-carrying forms. <see cref="StreamAddOptions"/> already
    /// holds every one of those, so the new surface takes it and the count collapses; the old spellings
    /// are the transitional adapter's problem, which is where they belong.
    /// </remarks>
    public static ValueTask<RedisValue> AddAsync(
        this in RespStreams streams,
        RedisKey key,
        RedisValue name,
        RedisValue value,
        in StreamAddOptions options = default,
        CommandFlags flags = CommandFlags.None,
        CancellationToken cancellationToken = default)
        => streams.Context.SendAsync<RedisValue>(
            $"{RedisCommand.XADD}{key}{new AddOperand(options)}{name}{value}",
            AddFlags(flags, in options),
            cancellationToken: cancellationToken);

    /// <summary>XADD; the id of the entry that was appended.</summary>
    /// <param name="streams">The stream command group.</param>
    /// <param name="key">The stream.</param>
    /// <param name="fields">The entry's fields; at least one.</param>
    /// <param name="options">Entry id, trimming and idempotency; the default appends with a server-assigned id.</param>
    /// <param name="flags">Command flags.</param>
    /// <param name="cancellationToken">Cancels the request; only cancellation <i>before</i> the send is honoured today.</param>
    /// <remarks>
    /// <inheritdoc cref="AddAsync(in RespStreams, RedisKey, RedisValue, RedisValue, in StreamAddOptions, CommandFlags, CancellationToken)" path="/remarks"/>
    /// <para>
    /// <paramref name="fields"/> needs no loop at the call site: <see cref="NameValueEntry"/> is an
    /// <c>IRespArgument</c>, so the span overload asks each pair to write its own name and value.
    /// </para>
    /// </remarks>
    public static ValueTask<RedisValue> AddAsync(
        this in RespStreams streams,
        RedisKey key,
        scoped ReadOnlySpan<NameValueEntry> fields,
        in StreamAddOptions options = default,
        CommandFlags flags = CommandFlags.None,
        CancellationToken cancellationToken = default)
    {
        if (fields.IsEmpty) throw new ArgumentOutOfRangeException(nameof(fields), "fields must contain at least one item.");
        return streams.Context.SendAsync<RedisValue>(
            $"{RedisCommand.XADD}{key}{new AddOperand(options)}{fields}",
            AddFlags(flags, in options),
            cancellationToken: cancellationToken);
    }

    /// <summary>
    /// XCLAIM; takes ownership of the listed pending entries and returns them.
    /// </summary>
    /// <param name="streams">The stream command group.</param>
    /// <param name="key">The stream.</param>
    /// <param name="group">The consumer group.</param>
    /// <param name="consumer">The consumer taking ownership.</param>
    /// <param name="minIdleTime">Only claim entries idle for at least this long.</param>
    /// <param name="messageIds">The entries to claim; at least one.</param>
    /// <param name="flags">Command flags.</param>
    /// <param name="cancellationToken">Cancels the request; only cancellation <i>before</i> the send is honoured today.</param>
    /// <returns><inheritdoc cref="RangeAsync(in RespStreams, RedisKey, RedisValue?, RedisValue?, int?, Order, CommandFlags, CancellationToken)" path="/returns"/></returns>
    /// <remarks>
    /// <b>A <see cref="TimeSpan"/> where <c>IDatabase.StreamClaim</c> takes <c>long minIdleTimeInMs</c>.</b>
    /// A duration is a duration; the millisecond spelling is the shipped signature's, and the adapter is
    /// where it belongs. <c>IDatabase.StreamReadGroup</c> already takes a <see cref="TimeSpan"/> for the
    /// same quantity, so the old surface is not even consistent with itself here.
    /// </remarks>
    public static ValueTask<RespRangeReply> ClaimAsync(
        this in RespStreams streams,
        RedisKey key,
        RedisValue group,
        RedisValue consumer,
        TimeSpan minIdleTime,
        scoped ReadOnlySpan<RedisValue> messageIds,
        CommandFlags flags = CommandFlags.None,
        CancellationToken cancellationToken = default)
    {
        var cmd = ClaimCommand(streams.Context, key, group, consumer, minIdleTime, messageIds, justId: false);
        return streams.Context.SendAsync(ref cmd, flags, RangeReplyHandler, cancellationToken);
    }

    /// <inheritdoc cref="ClaimAsync(in RespStreams, RedisKey, RedisValue, RedisValue, TimeSpan, ReadOnlySpan{RedisValue}, CommandFlags, CancellationToken)"/>
    /// <remarks><inheritdoc cref="RangeArray" path="/remarks"/></remarks>
    internal static ValueTask<StreamEntry[]> ClaimArray(
        this in RespStreams streams,
        RedisKey key,
        RedisValue group,
        RedisValue consumer,
        TimeSpan minIdleTime,
        scoped ReadOnlySpan<RedisValue> messageIds,
        CommandFlags flags = CommandFlags.None,
        CancellationToken cancellationToken = default)
    {
        var cmd = ClaimCommand(streams.Context, key, group, consumer, minIdleTime, messageIds, justId: false);
        return streams.Context.SendAsync(ref cmd, flags, StreamTypesHandler.Entries, cancellationToken);
    }

    /// <summary>
    /// XCLAIM JUSTID; claims the listed entries and returns only their ids.
    /// </summary>
    /// <param name="streams">The stream command group.</param>
    /// <param name="key">The stream.</param>
    /// <param name="group">The consumer group.</param>
    /// <param name="consumer">The consumer taking ownership.</param>
    /// <param name="minIdleTime">Only claim entries idle for at least this long.</param>
    /// <param name="messageIds">The entries to claim; at least one.</param>
    /// <param name="flags">Command flags.</param>
    /// <param name="cancellationToken">Cancels the request; only cancellation <i>before</i> the send is honoured today.</param>
    /// <remarks>
    /// <b>Not merely a cheaper <see cref="ClaimAsync(in RespStreams, RedisKey, RedisValue, RedisValue, TimeSpan, ReadOnlySpan{RedisValue}, CommandFlags, CancellationToken)"/>.</b>
    /// <c>JUSTID</c> also tells the server not to bump each entry's delivery counter, which is what makes
    /// this form safely retryable where the full one is not - see <c>WithJustIdCategory</c>.
    /// </remarks>
    public static ValueTask<ReadOnlyLease<RedisValue>> ClaimIdsOnlyAsync(
        this in RespStreams streams,
        RedisKey key,
        RedisValue group,
        RedisValue consumer,
        TimeSpan minIdleTime,
        scoped ReadOnlySpan<RedisValue> messageIds,
        CommandFlags flags = CommandFlags.None,
        CancellationToken cancellationToken = default)
    {
        var cmd = ClaimCommand(streams.Context, key, group, consumer, minIdleTime, messageIds, justId: true);
        return streams.Context.SendAsync(ref cmd, JustIdFlags(flags), RespHandlers.Inbuilt<ReadOnlyLease<RedisValue>>.Require(), cancellationToken);
    }

    /// <inheritdoc cref="ClaimIdsOnlyAsync(in RespStreams, RedisKey, RedisValue, RedisValue, TimeSpan, ReadOnlySpan{RedisValue}, CommandFlags, CancellationToken)"/>
    /// <remarks><inheritdoc cref="RangeArray" path="/remarks"/></remarks>
    internal static ValueTask<RedisValue[]> ClaimIdsOnlyArray(
        this in RespStreams streams,
        RedisKey key,
        RedisValue group,
        RedisValue consumer,
        TimeSpan minIdleTime,
        scoped ReadOnlySpan<RedisValue> messageIds,
        CommandFlags flags = CommandFlags.None,
        CancellationToken cancellationToken = default)
    {
        var cmd = ClaimCommand(streams.Context, key, group, consumer, minIdleTime, messageIds, justId: true);
        return streams.Context.SendAsync(ref cmd, JustIdFlags(flags), RespHandlers.Inbuilt<RedisValue[]>.Require(), cancellationToken);
    }

    /// <summary>Render <c>XCLAIM</c> - the one place the command is composed.</summary>
    /// <remarks><inheritdoc cref="RangeCommand" path="/remarks"/></remarks>
    private static RespRequestFrame ClaimCommand(
        in RespContext context,
        RedisKey key,
        RedisValue group,
        RedisValue consumer,
        TimeSpan minIdleTime,
        scoped ReadOnlySpan<RedisValue> messageIds,
        bool justId)
    {
        DemandAtLeastOneId(messageIds);
        var idleMs = (long)minIdleTime.TotalMilliseconds;
        return justId
            ? context.Render($"{RedisCommand.XCLAIM}{key}{group}{consumer}{idleMs}{messageIds}{RespLiterals.JustId}")
            : context.Render($"{RedisCommand.XCLAIM}{key}{group}{consumer}{idleMs}{messageIds}");
    }

    /// <summary>
    /// <c>JUSTID</c> makes a claim a clean conditional write; without it, it is not.
    /// </summary>
    /// <remarks>
    /// Reassigning ownership is itself idempotent, but <c>XCLAIM</c>/<c>XAUTOCLAIM</c> bump the entry's
    /// delivery counter every call - except under <c>JUSTID</c>, which explicitly does not. Without it the
    /// per-command default stands, since the counter is group bookkeeping rather than caller data. The
    /// same rule as <c>RedisDatabase.WithJustIdCategory</c>, which is where it is explained in full.
    /// </remarks>
    private static CommandFlags JustIdFlags(CommandFlags flags)
        => flags.WithRetryCategory(CommandFlags.CommandRetryWriteChecked);

    /// <summary>XREAD against one stream; the entries after <paramref name="position"/>.</summary>
    /// <param name="streams">The stream command group.</param>
    /// <param name="key">The stream to read.</param>
    /// <param name="position">Read entries after this id.</param>
    /// <param name="count">How many entries to return at most; the server's default when omitted.</param>
    /// <param name="flags">Command flags.</param>
    /// <param name="cancellationToken">Cancels the request; only cancellation <i>before</i> the send is honoured today.</param>
    /// <returns><inheritdoc cref="RangeAsync(in RespStreams, RedisKey, RedisValue?, RedisValue?, int?, Order, CommandFlags, CancellationToken)" path="/returns"/></returns>
    /// <remarks>
    /// <b><see cref="StreamPosition.NewMessages"/> is rejected here</b>, because <c>$</c> means "entries
    /// added after this call blocks" and this call does not block. That is the shipped rule, enforced by
    /// <c>StreamPosition.Resolve</c>, and it is worth knowing that the multi-stream overload does
    /// <i>not</i> enforce it - see the remarks there.
    /// </remarks>
    public static ValueTask<RespReadReply> ReadAsync(
        this in RespStreams streams,
        RedisKey key,
        RedisValue position,
        int? count = null,
        CommandFlags flags = CommandFlags.None,
        CancellationToken cancellationToken = default)
    {
        var cmd = ReadCommand(streams.Context, key, position, count);
        return streams.Context.SendAsync(ref cmd, flags, ReadReplyHandler, cancellationToken);
    }

    /// <inheritdoc cref="ReadAsync(in RespStreams, RedisKey, RedisValue, int?, CommandFlags, CancellationToken)"/>
    /// <remarks><inheritdoc cref="RangeArray" path="/remarks"/></remarks>
    internal static ValueTask<StreamEntry[]> ReadArray(
        this in RespStreams streams,
        RedisKey key,
        RedisValue position,
        int? count = null,
        CommandFlags flags = CommandFlags.None,
        CancellationToken cancellationToken = default)
    {
        var cmd = ReadCommand(streams.Context, key, position, count);
        return streams.Context.SendAsync(ref cmd, flags, StreamTypesHandler.NamedEntries, cancellationToken);
    }

    /// <summary>Render the single-stream <c>XREAD</c> - the one place the command is composed.</summary>
    /// <remarks><inheritdoc cref="RangeCommand" path="/remarks"/></remarks>
    private static RespRequestFrame ReadCommand(in RespContext context, RedisKey key, RedisValue position, int? count)
    {
        DemandPositiveCount(count);
        var after = StreamPosition.Resolve(position, RedisCommand.XREAD);
        return context.Render($"{RedisCommand.XREAD}{RespLiterals.Count.When(count)}{count}{RespLiterals.StreamsKeyword}{key}{after}");
    }

    /// <summary>XREADGROUP against one stream; the entries the group has for this consumer.</summary>
    /// <param name="streams">The stream command group.</param>
    /// <param name="key">The stream to read.</param>
    /// <param name="group">The consumer group.</param>
    /// <param name="consumer">The consumer reading.</param>
    /// <param name="position">Read after this id; undelivered messages (<c>&gt;</c>) when omitted.</param>
    /// <param name="count">How many entries to return at most; the server's default when omitted.</param>
    /// <param name="noAck">Whether the server should skip adding these to the pending list.</param>
    /// <param name="claimMinIdleTime">Also claim entries idle for at least this long.</param>
    /// <param name="flags">Command flags.</param>
    /// <param name="cancellationToken">Cancels the request; only cancellation <i>before</i> the send is honoured today.</param>
    /// <returns><inheritdoc cref="RangeAsync(in RespStreams, RedisKey, RedisValue?, RedisValue?, int?, Order, CommandFlags, CancellationToken)" path="/returns"/></returns>
    /// <remarks>
    /// <b>Three <c>IDatabase</c> overloads become one</b>: the older two simply lack
    /// <paramref name="noAck"/> and <paramref name="claimMinIdleTime"/>, which are optional here.
    /// </remarks>
    public static ValueTask<RespReadReply> ReadGroupAsync(
        this in RespStreams streams,
        RedisKey key,
        RedisValue group,
        RedisValue consumer,
        RedisValue? position = null,
        int? count = null,
        bool noAck = false,
        TimeSpan? claimMinIdleTime = null,
        CommandFlags flags = CommandFlags.None,
        CancellationToken cancellationToken = default)
    {
        var cmd = ReadGroupCommand(streams.Context, key, group, consumer, position, count, noAck, claimMinIdleTime);
        return streams.Context.SendAsync(ref cmd, flags, ReadReplyHandler, cancellationToken);
    }

    /// <inheritdoc cref="ReadGroupAsync(in RespStreams, RedisKey, RedisValue, RedisValue, RedisValue?, int?, bool, TimeSpan?, CommandFlags, CancellationToken)"/>
    /// <remarks><inheritdoc cref="RangeArray" path="/remarks"/></remarks>
    internal static ValueTask<StreamEntry[]> ReadGroupArray(
        this in RespStreams streams,
        RedisKey key,
        RedisValue group,
        RedisValue consumer,
        RedisValue? position = null,
        int? count = null,
        bool noAck = false,
        TimeSpan? claimMinIdleTime = null,
        CommandFlags flags = CommandFlags.None,
        CancellationToken cancellationToken = default)
    {
        var cmd = ReadGroupCommand(streams.Context, key, group, consumer, position, count, noAck, claimMinIdleTime);
        return streams.Context.SendAsync(ref cmd, flags, StreamTypesHandler.NamedEntries, cancellationToken);
    }

    /// <summary>Render the single-stream <c>XREADGROUP</c> - the one place the command is composed.</summary>
    /// <remarks><inheritdoc cref="RangeCommand" path="/remarks"/></remarks>
    private static RespRequestFrame ReadGroupCommand(
        in RespContext context,
        RedisKey key,
        RedisValue group,
        RedisValue consumer,
        RedisValue? position,
        int? count,
        bool noAck,
        TimeSpan? claimMinIdleTime)
    {
        DemandPositiveCount(count);
        var after = StreamPosition.Resolve(position ?? StreamPosition.NewMessages, RedisCommand.XREADGROUP);

        // the CLAIM operand is written as the DOUBLE the shipped writer writes, not as whole milliseconds:
        // WriteBulkString(TimeSpan.TotalMilliseconds) is a double, and matching it is the point
        var claimMs = claimMinIdleTime.HasValue ? (double?)claimMinIdleTime.GetValueOrDefault().TotalMilliseconds : null;
        return context.Render(
            $"{RedisCommand.XREADGROUP}{RespLiterals.Group}{group}{consumer}{RespLiterals.Count.When(count)}{count}{RespLiterals.NoAck.When(noAck)}{RespLiterals.Claim.When(claimMs)}{claimMs}{RespLiterals.StreamsKeyword}{key}{after}");
    }

    private static readonly RespReplyHandler<RespReadReply> ReadReplyHandler
        = new(static payload => new RespReadReply(payload));

    private static void DemandPositiveCount(int? count)
    {
        if (count.HasValue && count <= 0) throw new ArgumentOutOfRangeException(nameof(count), "count must be greater than 0.");
    }

    /// <summary>
    /// XAUTOCLAIM; claims whatever has been idle too long, starting from a cursor.
    /// </summary>
    /// <param name="streams">The stream command group.</param>
    /// <param name="key">The stream.</param>
    /// <param name="group">The consumer group.</param>
    /// <param name="consumer">The consumer taking ownership.</param>
    /// <param name="minIdleTime">Only claim entries idle for at least this long.</param>
    /// <param name="startAtId">Where to start scanning; the cursor from the previous call.</param>
    /// <param name="count">How many entries to attempt at most; the server's default when omitted.</param>
    /// <param name="flags">Command flags.</param>
    /// <param name="cancellationToken">Cancels the request; only cancellation <i>before</i> the send is honoured today.</param>
    /// <returns><inheritdoc cref="RangeAsync(in RespStreams, RedisKey, RedisValue?, RedisValue?, int?, Order, CommandFlags, CancellationToken)" path="/returns"/></returns>
    public static ValueTask<RespAutoClaimReply> AutoClaimAsync(
        this in RespStreams streams,
        RedisKey key,
        RedisValue group,
        RedisValue consumer,
        TimeSpan minIdleTime,
        RedisValue startAtId,
        int? count = null,
        CommandFlags flags = CommandFlags.None,
        CancellationToken cancellationToken = default)
    {
        var cmd = AutoClaimCommand(streams.Context, key, group, consumer, minIdleTime, startAtId, count, justId: false);
        return streams.Context.SendAsync(ref cmd, flags, AutoClaimReplyHandler, cancellationToken);
    }

    /// <inheritdoc cref="AutoClaimAsync(in RespStreams, RedisKey, RedisValue, RedisValue, TimeSpan, RedisValue, int?, CommandFlags, CancellationToken)"/>
    /// <remarks><inheritdoc cref="RangeArray" path="/remarks"/></remarks>
    internal static ValueTask<StreamAutoClaimResult> AutoClaimResult(
        this in RespStreams streams,
        RedisKey key,
        RedisValue group,
        RedisValue consumer,
        TimeSpan minIdleTime,
        RedisValue startAtId,
        int? count = null,
        CommandFlags flags = CommandFlags.None,
        CancellationToken cancellationToken = default)
    {
        var cmd = AutoClaimCommand(streams.Context, key, group, consumer, minIdleTime, startAtId, count, justId: false);
        return streams.Context.SendAsync(ref cmd, flags, StreamTypesHandler.AutoClaim, cancellationToken);
    }

    /// <summary>XAUTOCLAIM JUSTID; the same, returning ids rather than entries.</summary>
    /// <param name="streams">The stream command group.</param>
    /// <param name="key">The stream.</param>
    /// <param name="group">The consumer group.</param>
    /// <param name="consumer">The consumer taking ownership.</param>
    /// <param name="minIdleTime">Only claim entries idle for at least this long.</param>
    /// <param name="startAtId">Where to start scanning; the cursor from the previous call.</param>
    /// <param name="count">How many entries to attempt at most; the server's default when omitted.</param>
    /// <param name="flags">Command flags.</param>
    /// <param name="cancellationToken">Cancels the request; only cancellation <i>before</i> the send is honoured today.</param>
    /// <remarks><inheritdoc cref="ClaimIdsOnlyAsync(in RespStreams, RedisKey, RedisValue, RedisValue, TimeSpan, ReadOnlySpan{RedisValue}, CommandFlags, CancellationToken)" path="/remarks"/></remarks>
    public static ValueTask<RespAutoClaimIdsOnlyReply> AutoClaimIdsOnlyAsync(
        this in RespStreams streams,
        RedisKey key,
        RedisValue group,
        RedisValue consumer,
        TimeSpan minIdleTime,
        RedisValue startAtId,
        int? count = null,
        CommandFlags flags = CommandFlags.None,
        CancellationToken cancellationToken = default)
    {
        var cmd = AutoClaimCommand(streams.Context, key, group, consumer, minIdleTime, startAtId, count, justId: true);
        return streams.Context.SendAsync(ref cmd, JustIdFlags(flags), AutoClaimIdsOnlyReplyHandler, cancellationToken);
    }

    /// <inheritdoc cref="AutoClaimIdsOnlyAsync(in RespStreams, RedisKey, RedisValue, RedisValue, TimeSpan, RedisValue, int?, CommandFlags, CancellationToken)"/>
    /// <remarks><inheritdoc cref="RangeArray" path="/remarks"/></remarks>
    internal static ValueTask<StreamAutoClaimIdsOnlyResult> AutoClaimIdsOnlyResult(
        this in RespStreams streams,
        RedisKey key,
        RedisValue group,
        RedisValue consumer,
        TimeSpan minIdleTime,
        RedisValue startAtId,
        int? count = null,
        CommandFlags flags = CommandFlags.None,
        CancellationToken cancellationToken = default)
    {
        var cmd = AutoClaimCommand(streams.Context, key, group, consumer, minIdleTime, startAtId, count, justId: true);
        return streams.Context.SendAsync(ref cmd, JustIdFlags(flags), StreamTypesHandler.AutoClaimIdsOnly, cancellationToken);
    }

    /// <summary>Render <c>XAUTOCLAIM</c> - the one place the command is composed.</summary>
    /// <remarks><inheritdoc cref="RangeCommand" path="/remarks"/></remarks>
    private static RespRequestFrame AutoClaimCommand(
        in RespContext context,
        RedisKey key,
        RedisValue group,
        RedisValue consumer,
        TimeSpan minIdleTime,
        RedisValue startAtId,
        int? count,
        bool justId)
    {
        var idleMs = (long)minIdleTime.TotalMilliseconds;
        return justId
            ? context.Render($"{RedisCommand.XAUTOCLAIM}{key}{group}{consumer}{idleMs}{startAtId}{RespLiterals.Count.When(count)}{count}{RespLiterals.JustId}")
            : context.Render($"{RedisCommand.XAUTOCLAIM}{key}{group}{consumer}{idleMs}{startAtId}{RespLiterals.Count.When(count)}{count}");
    }

    private static readonly RespReplyHandler<RespAutoClaimReply> AutoClaimReplyHandler
        = new(static payload => new RespAutoClaimReply(payload));

    private static readonly RespReplyHandler<RespAutoClaimIdsOnlyReply> AutoClaimIdsOnlyReplyHandler
        = new(static payload => new RespAutoClaimIdsOnlyReply(payload));

    /// <summary>XPENDING; the group's pending summary, with a per-consumer breakdown.</summary>
    /// <param name="streams">The stream command group.</param>
    /// <param name="key">The stream.</param>
    /// <param name="group">The consumer group.</param>
    /// <param name="flags">Command flags.</param>
    /// <param name="cancellationToken">Cancels the request; only cancellation <i>before</i> the send is honoured today.</param>
    /// <returns><inheritdoc cref="RangeAsync(in RespStreams, RedisKey, RedisValue?, RedisValue?, int?, Order, CommandFlags, CancellationToken)" path="/returns"/></returns>
    public static ValueTask<RespPendingReply> PendingAsync(
        this in RespStreams streams,
        RedisKey key,
        RedisValue group,
        CommandFlags flags = CommandFlags.None,
        CancellationToken cancellationToken = default)
    {
        var cmd = streams.Context.Render($"{RedisCommand.XPENDING}{key}{group}");
        return streams.Context.SendAsync(ref cmd, flags, PendingReplyHandler, cancellationToken);
    }

    /// <inheritdoc cref="PendingAsync(in RespStreams, RedisKey, RedisValue, CommandFlags, CancellationToken)"/>
    /// <remarks><inheritdoc cref="RangeArray" path="/remarks"/></remarks>
    internal static ValueTask<StreamPendingInfo> PendingInfo(
        this in RespStreams streams,
        RedisKey key,
        RedisValue group,
        CommandFlags flags = CommandFlags.None,
        CancellationToken cancellationToken = default)
    {
        var cmd = streams.Context.Render($"{RedisCommand.XPENDING}{key}{group}");
        return streams.Context.SendAsync(ref cmd, flags, StreamTypesHandler.PendingInfo, cancellationToken);
    }

    /// <summary>XPENDING with a range; one record per pending entry.</summary>
    /// <param name="streams">The stream command group.</param>
    /// <param name="key">The stream.</param>
    /// <param name="group">The consumer group.</param>
    /// <param name="count">How many records to return at most; must be positive.</param>
    /// <param name="consumer">Only this consumer's entries; all consumers when null.</param>
    /// <param name="minId">The lowest id to include; the start of the stream when omitted.</param>
    /// <param name="maxId">The highest id to include; the end of the stream when omitted.</param>
    /// <param name="minIdleTime">Only entries idle for at least this long; all of them when omitted.</param>
    /// <param name="flags">Command flags.</param>
    /// <param name="cancellationToken">Cancels the request; only cancellation <i>before</i> the send is honoured today.</param>
    /// <returns><inheritdoc cref="RangeAsync(in RespStreams, RedisKey, RedisValue?, RedisValue?, int?, Order, CommandFlags, CancellationToken)" path="/returns"/></returns>
    /// <remarks>
    /// <b>Two <c>IDatabase</c> overloads become one</b>: the older of them simply lacks
    /// <paramref name="minIdleTime"/>, which is an optional argument here.
    /// </remarks>
    public static ValueTask<RespPendingMessagesReply> PendingMessagesAsync(
        this in RespStreams streams,
        RedisKey key,
        RedisValue group,
        int count,
        RedisValue consumer = default,
        RedisValue? minId = null,
        RedisValue? maxId = null,
        TimeSpan? minIdleTime = null,
        CommandFlags flags = CommandFlags.None,
        CancellationToken cancellationToken = default)
    {
        var cmd = PendingMessagesCommand(streams.Context, key, group, count, consumer, minId, maxId, minIdleTime);
        return streams.Context.SendAsync(ref cmd, flags, PendingMessagesReplyHandler, cancellationToken);
    }

    /// <inheritdoc cref="PendingMessagesAsync(in RespStreams, RedisKey, RedisValue, int, RedisValue, RedisValue?, RedisValue?, TimeSpan?, CommandFlags, CancellationToken)"/>
    /// <remarks><inheritdoc cref="RangeArray" path="/remarks"/></remarks>
    internal static ValueTask<StreamPendingMessageInfo[]> PendingMessagesArray(
        this in RespStreams streams,
        RedisKey key,
        RedisValue group,
        int count,
        RedisValue consumer = default,
        RedisValue? minId = null,
        RedisValue? maxId = null,
        TimeSpan? minIdleTime = null,
        CommandFlags flags = CommandFlags.None,
        CancellationToken cancellationToken = default)
    {
        var cmd = PendingMessagesCommand(streams.Context, key, group, count, consumer, minId, maxId, minIdleTime);
        return streams.Context.SendAsync(ref cmd, flags, StreamTypesHandler.PendingMessages, cancellationToken);
    }

    /// <summary>Render the extended <c>XPENDING</c> - the one place the command is composed.</summary>
    /// <remarks><inheritdoc cref="RangeCommand" path="/remarks"/></remarks>
    private static RespRequestFrame PendingMessagesCommand(
        in RespContext context,
        RedisKey key,
        RedisValue group,
        int count,
        RedisValue consumer,
        RedisValue? minId,
        RedisValue? maxId,
        TimeSpan? minIdleTime)
    {
        if (count <= 0) throw new ArgumentOutOfRangeException(nameof(count), "count must be greater than 0.");

        var idle = minIdleTime.HasValue ? (long?)minIdleTime.GetValueOrDefault().TotalMilliseconds : null;
        return context.Render(
            $"{RedisCommand.XPENDING}{key}{group}{RespLiterals.Idle.When(idle)}{idle}{minId ?? StreamConstants.ReadMinValue}{maxId ?? StreamConstants.ReadMaxValue}{count}{new OptionalValue(consumer)}");
    }

    private static readonly RespReplyHandler<RespPendingReply> PendingReplyHandler
        = new(static payload => new RespPendingReply(payload));

    private static readonly RespReplyHandler<RespPendingMessagesReply> PendingMessagesReplyHandler
        = new(static payload => new RespPendingMessagesReply(payload));

    /// <summary>XNACK; how many of the listed entries were released back to the group.</summary>
    /// <param name="streams">The stream command group.</param>
    /// <param name="key">The stream.</param>
    /// <param name="group">The consumer group.</param>
    /// <param name="mode">Whether the release counts as a failed delivery.</param>
    /// <param name="messageId">The entry to release.</param>
    /// <param name="flags">Command flags.</param>
    /// <param name="cancellationToken">Cancels the request; only cancellation <i>before</i> the send is honoured today.</param>
    public static ValueTask<long> NegativeAcknowledgeAsync(
        this in RespStreams streams,
        RedisKey key,
        RedisValue group,
        StreamNackMode mode,
        RedisValue messageId,
        CommandFlags flags = CommandFlags.None,
        CancellationToken cancellationToken = default)
        => streams.Context.SendAsync<long>(
            $"{RedisCommand.XNACK}{key}{group}{NackModeToken(mode)}{RespLiterals.Ids}{1}{messageId}",
            flags,
            cancellationToken: cancellationToken);

    /// <inheritdoc cref="NegativeAcknowledgeAsync(in RespStreams, RedisKey, RedisValue, StreamNackMode, RedisValue, CommandFlags, CancellationToken)"/>
    /// <param name="streams">The stream command group.</param>
    /// <param name="key">The stream.</param>
    /// <param name="group">The consumer group.</param>
    /// <param name="mode">Whether the release counts as a failed delivery.</param>
    /// <param name="messageIds">The entries to release; at least one.</param>
    /// <param name="flags">Command flags.</param>
    /// <param name="cancellationToken">Cancels the request; only cancellation <i>before</i> the send is honoured today.</param>
    public static ValueTask<long> NegativeAcknowledgeAsync(
        this in RespStreams streams,
        RedisKey key,
        RedisValue group,
        StreamNackMode mode,
        scoped ReadOnlySpan<RedisValue> messageIds,
        CommandFlags flags = CommandFlags.None,
        CancellationToken cancellationToken = default)
    {
        DemandAtLeastOneId(messageIds);
        return streams.Context.SendAsync<long>(
            $"{RedisCommand.XNACK}{key}{group}{NackModeToken(mode)}{RespLiterals.Ids}{messageIds.Length}{messageIds}",
            flags,
            cancellationToken: cancellationToken);
    }

    /// <summary>
    /// XACKDEL; per-id outcomes, so a caller can tell "acknowledged and deleted" from "was not there".
    /// </summary>
    /// <param name="streams">The stream command group.</param>
    /// <param name="key">The stream.</param>
    /// <param name="group">The consumer group.</param>
    /// <param name="mode">What to do with entries that other consumer groups still reference.</param>
    /// <param name="messageIds">The entries to acknowledge and delete; at least one.</param>
    /// <param name="flags">Command flags.</param>
    /// <param name="cancellationToken">Cancels the request; only cancellation <i>before</i> the send is honoured today.</param>
    /// <remarks>
    /// <inheritdoc cref="DeleteAsync(in RespStreams, RedisKey, ReadOnlySpan{RedisValue}, StreamTrimMode, CommandFlags, CancellationToken)" path="/remarks"/>
    /// <para>
    /// <b>Only the span form, where <see cref="IDatabase"/> has a single-id one too.</b> The reply is an
    /// array either way - the server is told <c>IDS 1</c> - so the single-id spelling buys a caller
    /// nothing the transitional adapter cannot do with a one-element span.
    /// </para>
    /// </remarks>
    public static ValueTask<ReadOnlyLease<StreamTrimResult>> AcknowledgeAndDeleteAsync(
        this in RespStreams streams,
        RedisKey key,
        RedisValue group,
        StreamTrimMode mode,
        scoped ReadOnlySpan<RedisValue> messageIds,
        CommandFlags flags = CommandFlags.None,
        CancellationToken cancellationToken = default)
    {
        var cmd = AcknowledgeAndDeleteCommand(streams.Context, key, group, mode, messageIds);
        return streams.Context.SendAsync(ref cmd, flags, RespHandlers.Inbuilt<ReadOnlyLease<StreamTrimResult>>.Require(), cancellationToken);
    }

    /// <inheritdoc cref="AcknowledgeAndDeleteAsync(in RespStreams, RedisKey, RedisValue, StreamTrimMode, ReadOnlySpan{RedisValue}, CommandFlags, CancellationToken)"/>
    /// <remarks><inheritdoc cref="DeleteArray(in RespStreams, RedisKey, ReadOnlySpan{RedisValue}, StreamTrimMode, CommandFlags, CancellationToken)" path="/remarks"/></remarks>
    internal static ValueTask<StreamTrimResult[]> AcknowledgeAndDeleteArray(
        this in RespStreams streams,
        RedisKey key,
        RedisValue group,
        StreamTrimMode mode,
        scoped ReadOnlySpan<RedisValue> messageIds,
        CommandFlags flags = CommandFlags.None,
        CancellationToken cancellationToken = default)
    {
        var cmd = AcknowledgeAndDeleteCommand(streams.Context, key, group, mode, messageIds);
        return streams.Context.SendAsync(ref cmd, flags, RespHandlers.Inbuilt<StreamTrimResult[]>.Require(), cancellationToken);
    }

    /// <summary>Render <c>XACKDEL</c> - the one place the command is composed.</summary>
    /// <remarks><inheritdoc cref="RangeCommand" path="/remarks"/></remarks>
    private static RespRequestFrame AcknowledgeAndDeleteCommand(
        in RespContext context,
        RedisKey key,
        RedisValue group,
        StreamTrimMode mode,
        scoped ReadOnlySpan<RedisValue> messageIds)
    {
        DemandAtLeastOneId(messageIds);
        return context.Render(
            $"{RedisCommand.XACKDEL}{key}{group}{TrimModeToken(mode)}{RespLiterals.Ids}{messageIds.Length}{messageIds}");
    }

    /// <summary>XCFGSET; per-stream idempotency settings.</summary>
    /// <param name="streams">The stream command group.</param>
    /// <param name="key">The stream.</param>
    /// <param name="configuration">The settings to apply.</param>
    /// <param name="flags">Command flags.</param>
    /// <param name="cancellationToken">Cancels the request; only cancellation <i>before</i> the send is honoured today.</param>
    /// <remarks>
    /// An empty <paramref name="configuration"/> still sends the bare command, which the server rejects.
    /// That is deliberate and matches the shipped behaviour: the server's message says what is wrong with
    /// more authority than a guess made here could.
    /// </remarks>
    public static ValueTask ConfigureAsync(
        this in RespStreams streams,
        RedisKey key,
        StreamConfiguration configuration,
        CommandFlags flags = CommandFlags.None,
        CancellationToken cancellationToken = default)
    {
        if (configuration is null) throw new ArgumentNullException(nameof(configuration));
        return streams.Context.SendAsync(
            $"{RedisCommand.XCFGSET}{key}{new ConfigureOperand(configuration)}", flags, cancellationToken);
    }

    /// <summary>
    /// Everything <c>XADD</c> writes between the key and the fields:
    /// <c>[NOMKSTREAM] [MAXLEN|MINID [~] threshold] [LIMIT n] [mode] [IDMP...] &lt;*|id&gt;</c>.
    /// </summary>
    /// <remarks>
    /// One operand for the same reason as <see cref="TrimOperand"/>: the parts are ordered with respect to
    /// one another, and the entry id has to come last however few of the others are present. Spelling that
    /// as separate holes would put the ordering rule at every call site.
    /// </remarks>
    private readonly struct AddOperand(StreamAddOptions options) : IRespArgument
    {
        public void WriteTo(scoped ref RespRequestBuilder handler)
        {
            if (!options.CreateStream) handler.AppendFormatted(RespLiterals.NoMkStream);

            if (options.HasThreshold)
            {
                var byMaxLength = options.MaxLength.HasValue;
                handler.AppendFormatted(byMaxLength ? RespLiterals.MaxLen : RespLiterals.MinId);
                if (options.Approximate) handler.AppendFormatted(RespLiterals.Approximate);
                handler.AppendFormatted(byMaxLength ? (RedisValue)options.MaxLength.GetValueOrDefault() : options.MinId);
            }

            if (options.Limit.HasValue)
            {
                handler.AppendFormatted(RespLiterals.Limit);
                handler.AppendFormatted((RedisValue)options.Limit.GetValueOrDefault());
            }

            // omitted when KeepReferences; see TrimOperand for why that is the server's rule and not ours
            if (options.TrimMode != StreamTrimMode.KeepReferences) handler.AppendFormatted(TrimModeToken(options.TrimMode));

            var idempotent = options.IdempotentId;
            if (idempotent.IdempotentId.HasValue)
            {
                handler.AppendFormatted(RespLiterals.Idmp);
                handler.AppendFormatted(idempotent.ProducerId);
                handler.AppendFormatted(idempotent.IdempotentId);
            }
            else if (idempotent.ProducerId.HasValue)
            {
                handler.AppendFormatted(RespLiterals.IdmpAuto);
                handler.AppendFormatted(idempotent.ProducerId);
            }

            handler.AppendFormatted(options.EntryId);
        }
    }

    /// <summary>The operands of <c>XCFGSET</c>, each written only when it was set.</summary>
    private readonly struct ConfigureOperand(StreamConfiguration configuration) : IRespArgument
    {
        public void WriteTo(scoped ref RespRequestBuilder handler)
        {
            if (configuration.IdmpDuration is { } duration)
            {
                handler.AppendFormatted(RespLiterals.IdmpDuration);
                handler.AppendFormatted((RedisValue)duration);
            }

            if (configuration.IdmpMaxSize is { } maxSize)
            {
                handler.AppendFormatted(RespLiterals.IdmpMaxSize);
                handler.AppendFormatted((RedisValue)maxSize);
            }
        }
    }

    /// <summary>
    /// <c>XADD</c> is only safely retryable when a replay cannot append a second entry.
    /// </summary>
    /// <remarks>
    /// Which is the case exactly when the caller pinned the id - a fully explicit id is rejected the
    /// second time as "equal or smaller" - or asked for idempotency. A server-assigned id (<c>*</c>, or
    /// the <c>&lt;ms&gt;-*</c> auto-sequence form) would append twice, so it stays uncategorised. Shared
    /// with the classic path's rule rather than restated: see <c>RedisDatabase.IsServerAssignedId</c>.
    /// </remarks>
    private static CommandFlags AddFlags(CommandFlags flags, in StreamAddOptions options)
        => options.IdempotentId.ArgCount != 0 || !RedisDatabase.IsServerAssignedId(options.EntryId)
            ? flags.WithRetryCategory(CommandFlags.CommandRetryWriteChecked)
            : flags;

    /// <summary>The <c>SILENT</c>/<c>FAIL</c>/<c>FATAL</c> mode token of <c>XNACK</c>.</summary>
    private static RespFragment NackModeToken(StreamNackMode mode) => mode switch
    {
        StreamNackMode.Silent => RespLiterals.Silent,
        StreamNackMode.Fail => RespLiterals.Fail,
        StreamNackMode.Fatal => RespLiterals.Fatal,
        _ => throw new ArgumentOutOfRangeException(nameof(mode)),
    };

    /// <summary>A value written only when it is not null; omitted entirely otherwise.</summary>
    /// <remarks>
    /// <b>Omitted, not empty - and the difference is a real one.</b> <c>AppendFormatted(RedisValue)</c>
    /// writes <c>$0</c> for a null, which for <c>XPENDING</c>'s trailing consumer means "the consumer
    /// whose name is the empty string" rather than "all consumers". A parity test caught exactly that;
    /// the classic path had always tested <c>consumerName != RedisValue.Null</c> and skipped the argument.
    /// </remarks>
    private readonly struct OptionalValue(RedisValue value) : IRespArgument
    {
        public void WriteTo(scoped ref RespRequestBuilder handler)
        {
            if (!value.IsNull) handler.AppendFormatted(value);
        }
    }

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
        public void WriteTo(scoped ref RespRequestBuilder handler)
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
