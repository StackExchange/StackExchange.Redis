using System;
using System.Diagnostics.CodeAnalysis;
using System.Threading.Tasks;
using RESPite;

namespace StackExchange.Redis.Interpolated
{
    /// <summary>
    /// EXPERIMENTAL SPIKE. The stream group: <c>target.Streams.LengthAsync(...)</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Partial on purpose.</b> This is the half of the stream family that is scalar in and scalar out.
    /// The reads - <c>XRANGE</c>, <c>XREAD</c>, <c>XREADGROUP</c>, <c>XCLAIM</c>, <c>XAUTOCLAIM</c>,
    /// <c>XPENDING</c>, <c>XINFO</c> - return composites that hold arrays (<c>StreamEntry</c> holds
    /// <c>NameValueEntry[]</c>, <c>RedisStream</c> holds <c>StreamEntry[]</c>), and picking a lease-shaped
    /// representation for those is a design decision rather than a transcription. They are deliberately
    /// absent rather than done badly; see the queue.
    /// </para>
    /// <para>
    /// Every id list here takes a <see cref="ReadOnlySpan{T}"/>, not an array, so calling does not force an
    /// allocation on the caller.
    /// </para>
    /// </remarks>
    [Experimental(Experiments.InterpolatedWriter, UrlFormat = Experiments.UrlFormat)]
    public readonly struct RespStreams
    {
        private readonly RespContext _context;

        /// <summary>Group the stream commands of a context.</summary>
        /// <param name="context">The context to send through.</param>
        public RespStreams(in RespContext context) => _context = context;

        /// <summary>The underlying context.</summary>
        public RespContext Context => _context;
    }

    public static partial class RespSurface
    {
        extension(IRespKeyspaceTarget target)
        {
            /// <summary>The stream commands.</summary>
            public RespStreams Streams => new(target.Context);
        }

        extension(in RespContext context)
        {
            /// <summary>The stream commands.</summary>
            public RespStreams Streams => new(context);
        }

        /// <summary>XLEN; the number of entries in the stream.</summary>
        /// <param name="streams">The stream command group.</param>
        /// <param name="key">The stream to measure.</param>
        /// <param name="flags">Command flags.</param>
        public static ValueTask<long> LengthAsync(this in RespStreams streams, RedisKey key, CommandFlags flags = CommandFlags.None)
            => streams.Context.SendAsync<long>(
                $"{RedisCommand.XLEN}{key}", flags.WithDefaultCategory(RedisCommand.XLEN));

        /// <summary>XACK; how many of the listed entries were pending and are now acknowledged.</summary>
        /// <param name="streams">The stream command group.</param>
        /// <param name="key">The stream.</param>
        /// <param name="group">The consumer group.</param>
        /// <param name="messageId">The entry to acknowledge.</param>
        /// <param name="flags">Command flags.</param>
        public static ValueTask<long> AcknowledgeAsync(this in RespStreams streams, RedisKey key, RedisValue group, RedisValue messageId, CommandFlags flags = CommandFlags.None)
            => streams.Context.SendAsync<long>(
                $"{RedisCommand.XACK}{key}{group}{messageId}", flags.WithDefaultCategory(RedisCommand.XACK));

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
                $"{RedisCommand.XACK}{key}{group}{messageIds}", flags.WithDefaultCategory(RedisCommand.XACK));
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
                $"{RedisCommand.XDEL}{key}{messageIds}", flags.WithDefaultCategory(RedisCommand.XDEL));
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
                flags.WithDefaultCategory(RedisCommand.XDELEX));
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
                flags.WithDefaultCategory(RedisCommand.XDELEX));
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
                $"{RedisCommand.XGROUP}{RespLiterals.Create}{key}{group}{ResolveGroupPosition(position)}{(createStream ? RespLiterals.MkStream : default)}",
                flags.WithDefaultCategory(RedisCommand.XGROUP));

        /// <summary>XGROUP DESTROY.</summary>
        /// <param name="streams">The stream command group.</param>
        /// <param name="key">The stream.</param>
        /// <param name="group">The consumer group to remove.</param>
        /// <param name="flags">Command flags.</param>
        public static ValueTask<bool> DeleteConsumerGroupAsync(this in RespStreams streams, RedisKey key, RedisValue group, CommandFlags flags = CommandFlags.None)
            => streams.Context.SendAsync<bool>(
                $"{RedisCommand.XGROUP}{RespLiterals.Destroy}{key}{group}",
                flags.WithDefaultCategory(RedisCommand.XGROUP));

        /// <summary>XGROUP DELCONSUMER; the number of pending entries the consumer still owned.</summary>
        /// <param name="streams">The stream command group.</param>
        /// <param name="key">The stream.</param>
        /// <param name="group">The consumer group.</param>
        /// <param name="consumer">The consumer to remove.</param>
        /// <param name="flags">Command flags.</param>
        public static ValueTask<long> DeleteConsumerAsync(this in RespStreams streams, RedisKey key, RedisValue group, RedisValue consumer, CommandFlags flags = CommandFlags.None)
            => streams.Context.SendAsync<long>(
                $"{RedisCommand.XGROUP}{RespLiterals.DeleteConsumer}{key}{group}{consumer}",
                flags.WithDefaultCategory(RedisCommand.XGROUP));

        /// <summary>XGROUP SETID; move a group's read position.</summary>
        /// <param name="streams">The stream command group.</param>
        /// <param name="key">The stream.</param>
        /// <param name="group">The consumer group.</param>
        /// <param name="position">The new position.</param>
        /// <param name="flags">Command flags.</param>
        public static ValueTask<bool> SetConsumerGroupPositionAsync(this in RespStreams streams, RedisKey key, RedisValue group, RedisValue position, CommandFlags flags = CommandFlags.None)
            => streams.Context.SendAsync<bool>(
                $"{RedisCommand.XGROUP}{RespLiterals.SetId}{key}{group}{ResolveGroupPosition(position)}",
                flags.WithDefaultCategory(RedisCommand.XGROUP));

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
                flags.WithDefaultCategory(RedisCommand.XTRIM));

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
                flags.WithDefaultCategory(RedisCommand.XTRIM));

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
}
