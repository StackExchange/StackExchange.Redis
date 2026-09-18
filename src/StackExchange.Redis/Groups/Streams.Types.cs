using System;
using System.Diagnostics.CodeAnalysis;
using RESPite;
using RESPite.Messages;
using StackExchange.Redis.Protocol;

namespace StackExchange.Redis;

/// <summary>
/// The stream reply shapes, as <b>windows over the reply buffer</b>.
/// </summary>
/// <remarks>
/// <para>
/// <b>Nested in the group, because these types are the group's.</b> A stream entry means nothing outside
/// streams, so it lives where it is used rather than in a shared namespace. The counterpart rule is that
/// anything the <i>wire</i> shares gets hoisted out - <see cref="RespNameValueEntry"/> is a name/value
/// pair, which is equally <c>HGETALL</c> and <c>CONFIG GET</c>, so it sits at the top level instead.
/// </para>
/// <para>
/// <b>The <c>Resp</c> prefix is not decoration.</b> Reusing the shipped simple names and relying on
/// nesting alone makes <c>using static</c> ambiguous (<c>CS0104</c>) between <see cref="StreamEntry"/>
/// and the window - loud rather than silent, but it takes the escape hatch away. The prefix also follows
/// a transformation this codebase already made once: <see cref="RespValue"/> is the window counterpart to
/// <see cref="RedisValue"/>.
/// </para>
/// </remarks>
public static partial class Streams
{
    /// <summary>
    /// The reply to a stream range read (<c>XRANGE</c>/<c>XREVRANGE</c>): a run of entries, walked on
    /// demand rather than materialised.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>This is the shape the whole deferred-view exercise exists for.</b> Materialising a nested stream
    /// reply costs N+1 arrays - one per entry's fields, plus one for the entries - which measured at
    /// ~55KB for a thousand-entry read. Walking it costs <b>nothing</b> beyond the reply that already
    /// arrived, because every entry and every field is a window over the same bytes.
    /// </para>
    /// <para>
    /// The price is the <c>using</c>: the buffer goes back when this is disposed, and every window
    /// reachable from it dies at the same moment. See <see cref="RespReply"/> for the rule in full.
    /// </para>
    /// </remarks>
    public sealed class RespRangeReply : RespReply
    {
        private readonly RespAggregate<RespStreamEntry> _entries;

        /// <summary>Read a stream range reply from a payload.</summary>
        /// <param name="payload">The reply's bytes, with one reference already taken on this reply's behalf.</param>
        /// <remarks>
        /// <b>The walk happens once, here.</b> Capturing the entry run in the constructor means
        /// <see cref="Entries"/> is a field read rather than a re-parse, so touching it twice costs one
        /// walk rather than two. The windows it yields are still produced on demand.
        /// </remarks>
        public RespRangeReply(RespPayload payload) : base(payload)
        {
            var reader = GetReader();
            RespAggregate<RespStreamEntry>.TryCaptureNext(Payload, ref reader, RespStreamEntry.Projection, out _entries);
        }

        /// <summary>The entries, in the order the server returned them.</summary>
        /// <exception cref="ObjectDisposedException">If this reply has already been disposed.</exception>
        public RespAggregate<RespStreamEntry> Entries
        {
            get
            {
                _ = Payload; // the windows are only meaningful while the buffer is held
                return _entries;
            }
        }

        /// <summary>How many entries the reply carries.</summary>
        public int Count => _entries.Count;

        /// <summary>Materialise the whole reply into the array shape the older surface promises.</summary>
        /// <remarks>
        /// <para>
        /// <b><c>To</c>, not <c>As</c>: this allocates, and the name is the price.</b> It exists for the
        /// caller who says "I accept the overhead, but I have existing code paths that want the old
        /// shape" - and it is how <c>IDatabase.StreamRange</c> is satisfied, so it is exercised by the
        /// whole existing test suite rather than by whoever remembers to call it.
        /// </para>
        /// <para>
        /// <b>Not implemented in terms of the walk.</b> Projecting the old shape by enumerating the typed
        /// view would capture a window per entry, build a sub-reader per level and re-read frame headers
        /// at each step - paying for laziness that is discarded immediately. One forward pass is what the
        /// eager shape wants, and it is exactly what the existing parser already does. So this and the
        /// deferred view are two <i>call sites</i> of one parse, not two implementations to keep in sync.
        /// </para>
        /// </remarks>
        /// <exception cref="ObjectDisposedException">If this reply has already been disposed.</exception>
        public StreamEntry[] ToArray()
        {
            var reader = GetReader();
            reader.MoveNext();

            // allowJaggedFields, and not a protocol version: this reply is a buffer, not a connection, so
            // there is no protocol here to claim. Permitting jagged is what the deferred walk does, which
            // is what makes this and Entries agree - and it cannot change what a real reply reads as,
            // because a stream entry's fields are scalars and so can never look jagged. See
            // RespRangeReplyTests.ScalarFieldsAreNotJagged.
            return ResultProcessor.ParseRedisStreamEntries(ref reader, allowJaggedFields: true);
        }

        /// <inheritdoc/>
        public override string ToString()
            => IsDisposed ? "(disposed)" : $"({Count} entr{(Count == 1 ? "y" : "ies")})";
    }

    /// <summary>
    /// The reply to an <c>XAUTOCLAIM</c>: a resume cursor, the claimed entries, and the ids that turned
    /// out to be gone.
    /// </summary>
    /// <remarks>
    /// <b>Three elements, or two.</b> The trailing deleted-id list arrived in 7.0, so a 6.2 server sends a
    /// two-element reply and <see cref="DeletedIds"/> is simply empty - the same defensiveness the shipped
    /// processor has always had, kept because it is a server fact rather than a taste.
    /// </remarks>
    public sealed class RespAutoClaimReply : RespReply
    {
        private readonly RespValue _nextStartId;
        private readonly RespAggregate<RespStreamEntry> _entries;
        private readonly RespAggregate<RespValue> _deletedIds;

        /// <summary>Read an <c>XAUTOCLAIM</c> reply from a payload.</summary>
        /// <param name="payload">The reply's bytes, with one reference already taken on this reply's behalf.</param>
        public RespAutoClaimReply(RespPayload payload) : base(payload)
        {
            var reader = GetReader();
            if (!reader.TryMoveNext() || !reader.IsAggregate || reader.IsNull) return;

            var length = reader.AggregateLength();
            RespValue.TryCaptureNext(Payload, ref reader, out _nextStartId);
            RespAggregate<RespStreamEntry>.TryCaptureNext(Payload, ref reader, RespStreamEntry.Projection, out _entries);
            if (length >= 3)
            {
                RespAggregate<RespValue>.TryCaptureNext(Payload, ref reader, RespValueProjection, out _deletedIds);
            }
        }

        /// <summary>Where to resume from on the next call.</summary>
        /// <exception cref="ObjectDisposedException">If this reply has already been disposed.</exception>
        public RespValue NextStartId
        {
            get
            {
                _ = Payload;
                return _nextStartId;
            }
        }

        /// <summary>The entries this call took ownership of.</summary>
        /// <exception cref="ObjectDisposedException">If this reply has already been disposed.</exception>
        public RespAggregate<RespStreamEntry> ClaimedEntries
        {
            get
            {
                _ = Payload;
                return _entries;
            }
        }

        /// <summary>Ids that were pending but no longer exist; empty before 7.0.</summary>
        /// <exception cref="ObjectDisposedException">If this reply has already been disposed.</exception>
        public RespAggregate<RespValue> DeletedIds
        {
            get
            {
                _ = Payload;
                return _deletedIds;
            }
        }

        /// <summary>Materialise the whole reply into the struct the older surface promises.</summary>
        /// <remarks><inheritdoc cref="RespRangeReply.ToArray" path="/remarks/para[2]"/></remarks>
        /// <exception cref="ObjectDisposedException">If this reply has already been disposed.</exception>
        public StreamAutoClaimResult ToStreamAutoClaimResult()
        {
            var reader = GetReader();
            reader.MoveNext();
            return ResultProcessor.TryParseStreamAutoClaim(ref reader, allowJaggedFields: true, out var value)
                ? value : StreamAutoClaimResult.Null;
        }

        /// <inheritdoc/>
        public override string ToString()
            => IsDisposed ? "(disposed)" : $"({_entries.Count} claimed, next {_nextStartId})";
    }

    /// <summary>The reply to an <c>XAUTOCLAIM JUSTID</c>: the same shape, with ids where the entries were.</summary>
    /// <remarks><inheritdoc cref="RespAutoClaimReply" path="/remarks"/></remarks>
    public sealed class RespAutoClaimIdsOnlyReply : RespReply
    {
        private readonly RespValue _nextStartId;
        private readonly RespAggregate<RespValue> _claimedIds, _deletedIds;

        /// <summary>Read an <c>XAUTOCLAIM JUSTID</c> reply from a payload.</summary>
        /// <param name="payload">The reply's bytes, with one reference already taken on this reply's behalf.</param>
        public RespAutoClaimIdsOnlyReply(RespPayload payload) : base(payload)
        {
            var reader = GetReader();
            if (!reader.TryMoveNext() || !reader.IsAggregate || reader.IsNull) return;

            var length = reader.AggregateLength();
            RespValue.TryCaptureNext(Payload, ref reader, out _nextStartId);
            RespAggregate<RespValue>.TryCaptureNext(Payload, ref reader, RespValueProjection, out _claimedIds);
            if (length >= 3)
            {
                RespAggregate<RespValue>.TryCaptureNext(Payload, ref reader, RespValueProjection, out _deletedIds);
            }
        }

        /// <summary>Where to resume from on the next call.</summary>
        /// <exception cref="ObjectDisposedException">If this reply has already been disposed.</exception>
        public RespValue NextStartId
        {
            get
            {
                _ = Payload;
                return _nextStartId;
            }
        }

        /// <summary>The ids this call took ownership of.</summary>
        /// <exception cref="ObjectDisposedException">If this reply has already been disposed.</exception>
        public RespAggregate<RespValue> ClaimedIds
        {
            get
            {
                _ = Payload;
                return _claimedIds;
            }
        }

        /// <summary>Ids that were pending but no longer exist; empty before 7.0.</summary>
        /// <exception cref="ObjectDisposedException">If this reply has already been disposed.</exception>
        public RespAggregate<RespValue> DeletedIds
        {
            get
            {
                _ = Payload;
                return _deletedIds;
            }
        }

        /// <summary>Materialise the whole reply into the struct the older surface promises.</summary>
        /// <remarks><inheritdoc cref="RespRangeReply.ToArray" path="/remarks/para[2]"/></remarks>
        /// <exception cref="ObjectDisposedException">If this reply has already been disposed.</exception>
        public StreamAutoClaimIdsOnlyResult ToStreamAutoClaimIdsOnlyResult()
        {
            var reader = GetReader();
            reader.MoveNext();
            return ResultProcessor.TryParseStreamAutoClaimIdsOnly(ref reader, out var value)
                ? value : StreamAutoClaimIdsOnlyResult.Null;
        }

        /// <inheritdoc/>
        public override string ToString()
            => IsDisposed ? "(disposed)" : $"({_claimedIds.Count} claimed, next {_nextStartId})";
    }

    /// <summary>Captures one scalar as a window, for the flat id runs <c>XAUTOCLAIM</c> returns.</summary>
    private static readonly RespReader.Projection<object?, RespValue> RespValueProjection =
        static (ref object? owner, ref RespReader reader) =>
        {
            RespValue.TryCaptureNext(owner, ref reader, out var value);
            return value;
        };

    /// <summary>
    /// The reply to an <c>XPENDING</c> summary: the group's pending count, its id bounds, and a
    /// per-consumer breakdown walked on demand.
    /// </summary>
    /// <remarks>
    /// <b>A reply object where the shipped surface has a plain struct</b>, because
    /// <see cref="StreamPendingInfo.Consumers"/> is a <see cref="StreamConsumer"/><c>[]</c> - and a naked
    /// array is exactly what this surface does not hand out. The three scalars are read eagerly here,
    /// since a window over an integer costs more than the integer.
    /// </remarks>
    public sealed class RespPendingReply : RespReply
    {
        private readonly RespAggregate<RespStreamConsumer> _consumers;
        private readonly long _count;
        private readonly RespValue _lowestId, _highestId;

        /// <summary>Read an <c>XPENDING</c> summary reply from a payload.</summary>
        /// <param name="payload">The reply's bytes, with one reference already taken on this reply's behalf.</param>
        public RespPendingReply(RespPayload payload) : base(payload)
        {
            // one reader, walked forward through the four children - the same shape as
            // RespStreamEntry.Projection, where TryCaptureNext does the MoveNext into each child itself
            var reader = GetReader();
            if (!reader.TryMoveNext() || !reader.IsAggregate || reader.IsNull) return;

            if (reader.TryMoveNext()) reader.TryReadInt64(out _count);
            RespValue.TryCaptureNext(Payload, ref reader, out _lowestId);
            RespValue.TryCaptureNext(Payload, ref reader, out _highestId);
            RespAggregate<RespStreamConsumer>.TryCaptureNext(Payload, ref reader, RespStreamConsumer.Projection, out _consumers);
        }

        /// <summary>How many entries the group has pending in total.</summary>
        public long PendingMessageCount => _count;

        /// <summary>The lowest pending id, or null when nothing is pending.</summary>
        /// <exception cref="ObjectDisposedException">If this reply has already been disposed.</exception>
        public RespValue LowestPendingMessageId
        {
            get
            {
                _ = Payload;
                return _lowestId;
            }
        }

        /// <summary>The highest pending id, or null when nothing is pending.</summary>
        /// <exception cref="ObjectDisposedException">If this reply has already been disposed.</exception>
        public RespValue HighestPendingMessageId
        {
            get
            {
                _ = Payload;
                return _highestId;
            }
        }

        /// <summary>The per-consumer breakdown; empty when the group has no consumers yet.</summary>
        /// <exception cref="ObjectDisposedException">If this reply has already been disposed.</exception>
        public RespAggregate<RespStreamConsumer> Consumers
        {
            get
            {
                _ = Payload;
                return _consumers;
            }
        }

        /// <summary>Materialise the whole reply into the struct the older surface promises.</summary>
        /// <remarks><inheritdoc cref="RespRangeReply.ToArray" path="/remarks/para[2]"/></remarks>
        /// <exception cref="ObjectDisposedException">If this reply has already been disposed.</exception>
        public StreamPendingInfo ToStreamPendingInfo()
        {
            var reader = GetReader();
            reader.MoveNext();
            return ResultProcessor.TryParseStreamPendingInfo(ref reader, out var value) ? value : default;
        }

        /// <inheritdoc/>
        public override string ToString()
            => IsDisposed ? "(disposed)" : $"({_count} pending, {_consumers.Count} consumer{(_consumers.Count == 1 ? "" : "s")})";
    }

    /// <summary>One consumer's share of a group's pending entries, as a window.</summary>
    /// <remarks><inheritdoc cref="RespStreamEntry" path="/remarks"/></remarks>
    public readonly struct RespStreamConsumer
    {
        /// <summary>Reads one <c>[name, count]</c> pair; handed a reader positioned before it.</summary>
        internal static readonly RespReader.Projection<object?, RespStreamConsumer> Projection =
            static (ref object? owner, ref RespReader reader) =>
            {
                if (!reader.TryMoveNext() || !reader.IsAggregate || reader.IsNull) return default;

                RespValue.TryCaptureNext(owner, ref reader, out var name);

                // read rather than captured: a window over a small integer costs more than the integer,
                // and the server sends this one as a bulk string, which is why it is not TryReadInt64
                long count = 0;
                if (reader.TryMoveNext()) reader.TryReadInt64(out count);

                return new RespStreamConsumer(name, count);
            };

        private readonly RespValue _name;
        private readonly long _pendingMessageCount;

        private RespStreamConsumer(RespValue name, long pendingMessageCount)
        {
            _name = name;
            _pendingMessageCount = pendingMessageCount;
        }

        /// <summary>The consumer's name.</summary>
        public RespValue Name => _name;

        /// <summary>How many entries this consumer has pending.</summary>
        public long PendingMessageCount => _pendingMessageCount;

        /// <inheritdoc/>
        public override string ToString() => $"{_name}: {_pendingMessageCount}";
    }

    /// <summary>
    /// The reply to an extended <c>XPENDING</c>: one record per pending entry, walked on demand.
    /// </summary>
    /// <remarks><inheritdoc cref="RespRangeReply" path="/remarks"/></remarks>
    public sealed class RespPendingMessagesReply : RespReply
    {
        private readonly RespAggregate<RespStreamPendingMessage> _messages;

        /// <summary>Read an extended <c>XPENDING</c> reply from a payload.</summary>
        /// <param name="payload">The reply's bytes, with one reference already taken on this reply's behalf.</param>
        public RespPendingMessagesReply(RespPayload payload) : base(payload)
        {
            var reader = GetReader();
            RespAggregate<RespStreamPendingMessage>.TryCaptureNext(Payload, ref reader, RespStreamPendingMessage.Projection, out _messages);
        }

        /// <summary>The pending entries, in the order the server returned them.</summary>
        /// <exception cref="ObjectDisposedException">If this reply has already been disposed.</exception>
        public RespAggregate<RespStreamPendingMessage> Messages
        {
            get
            {
                _ = Payload;
                return _messages;
            }
        }

        /// <summary>How many records the reply carries.</summary>
        public int Count => _messages.Count;

        /// <summary>Materialise the whole reply into the array shape the older surface promises.</summary>
        /// <remarks><inheritdoc cref="RespRangeReply.ToArray" path="/remarks/para[2]"/></remarks>
        /// <exception cref="ObjectDisposedException">If this reply has already been disposed.</exception>
        public StreamPendingMessageInfo[] ToArray()
        {
            var reader = GetReader();
            reader.MoveNext();
            return ResultProcessor.ParseStreamPendingMessages(ref reader);
        }

        /// <inheritdoc/>
        public override string ToString()
            => IsDisposed ? "(disposed)" : $"({Count} pending message{(Count == 1 ? "" : "s")})";
    }

    /// <summary>One pending entry's record, as a window.</summary>
    /// <remarks><inheritdoc cref="RespStreamEntry" path="/remarks"/></remarks>
    public readonly struct RespStreamPendingMessage
    {
        /// <summary>
        /// Reads one <c>[id, consumer, idle-ms, delivery-count]</c> record; handed a reader positioned
        /// before it.
        /// </summary>
        internal static readonly RespReader.Projection<object?, RespStreamPendingMessage> Projection =
            static (ref object? owner, ref RespReader reader) =>
            {
                if (!reader.TryMoveNext() || !reader.IsAggregate || reader.IsNull) return default;

                RespValue.TryCaptureNext(owner, ref reader, out var id);
                RespValue.TryCaptureNext(owner, ref reader, out var consumer);

                long idleMs = 0, deliveries = 0;
                if (reader.TryMoveNext()) reader.TryReadInt64(out idleMs);
                if (reader.TryMoveNext()) reader.TryReadInt64(out deliveries);

                return new RespStreamPendingMessage(id, consumer, TimeSpan.FromMilliseconds(idleMs), ResultProcessor.ParseStreamDeliveryCount(deliveries));
            };

        private readonly RespValue _id, _consumer;
        private readonly TimeSpan _idleTime;
        private readonly int _deliveryCount;

        private RespStreamPendingMessage(RespValue id, RespValue consumer, TimeSpan idleTime, int deliveryCount)
        {
            _id = id;
            _consumer = consumer;
            _idleTime = idleTime;
            _deliveryCount = deliveryCount;
        }

        /// <summary>The pending entry's id.</summary>
        public RespValue MessageId => _id;

        /// <summary>The consumer currently holding it.</summary>
        public RespValue ConsumerName => _consumer;

        /// <summary>How long since it was last delivered.</summary>
        /// <remarks>
        /// A <see cref="TimeSpan"/> where <see cref="StreamPendingMessageInfo.IdleTimeInMilliseconds"/> is
        /// a number, for the same reason <c>ClaimAsync</c> takes one: a duration is a duration.
        /// </remarks>
        public TimeSpan IdleTime => _idleTime;

        /// <summary>How many times it has been delivered.</summary>
        public int DeliveryCount => _deliveryCount;

        /// <summary>Materialise this record, so it outlives the reply it came from.</summary>
        /// <remarks><b><c>To</c>, not <c>As</c></b>: the id and the consumer name are copied out.</remarks>
        public StreamPendingMessageInfo ToStreamPendingMessageInfo()
            => new(_id.AsRedisValue(), _consumer.AsRedisValue(), (long)_idleTime.TotalMilliseconds, _deliveryCount);

        /// <inheritdoc/>
        public override string ToString() => $"{_id} ({_consumer}, {_deliveryCount} deliver{(_deliveryCount == 1 ? "y" : "ies")})";
    }

    /// <summary>
    /// One stream entry, as a window: an id and a field list, both pointing into the reply that produced
    /// them.
    /// </summary>
    /// <remarks>
    /// Uncounted, and freely copyable: the reply holds the single reference, and these are views valid for
    /// exactly as long as it is. Copying one does not extend its life, which is why disposal lives on the
    /// reply and nowhere else.
    /// </remarks>
    public readonly struct RespStreamEntry
    {
        /// <summary>Reads one entry from the run; handed a reader positioned before that entry.</summary>
        /// <remarks>
        /// The entry is <c>[id, [name, value, ...]]</c>, and optionally - for <c>XREADGROUP</c> with
        /// <c>CLAIM</c> - <c>[id, [fields], idle-time, delivery-count]</c>. Both halves are
        /// <i>captured</i> rather than read, which is what the positioned-before contract is for.
        /// </remarks>
        internal static readonly RespReader.Projection<object?, RespStreamEntry> Projection =
            static (ref object? owner, ref RespReader reader) =>
            {
                if (!reader.TryMoveNext() || !reader.IsAggregate || reader.IsNull) return default;

                var length = reader.AggregateLength();
                RespValue.TryCaptureNext(owner, ref reader, out var id);
                RespPairAggregate<RespNameValueEntry>.TryCaptureNext(
                    owner, ref reader, RespNameValueEntry.Projection, allowJagged: true, out var fields);

                // the optional CLAIM extras; read rather than captured, because they are small fixed
                // numbers whose window would cost more than the value
                if (length >= 4
                    && reader.TryMoveNext() && reader.TryReadInt64(out var idleMs)
                    && reader.TryMoveNext() && reader.TryReadInt64(out var deliveries))
                {
                    return new RespStreamEntry(
                        id,
                        fields,
                        TimeSpan.FromMilliseconds(idleMs),
                        ResultProcessor.ParseStreamDeliveryCount(deliveries));
                }

                return new RespStreamEntry(id, fields, null, 0);
            };

        private readonly RespValue _id;
        private readonly RespPairAggregate<RespNameValueEntry> _fields;
        private readonly TimeSpan? _idleTime;
        private readonly int _deliveryCount;

        private RespStreamEntry(RespValue id, RespPairAggregate<RespNameValueEntry> fields, TimeSpan? idleTime, int deliveryCount)
        {
            _id = id;
            _fields = fields;
            _idleTime = idleTime;
            _deliveryCount = deliveryCount;
        }

        /// <summary>The entry's id, as the server spelled it.</summary>
        public RespValue Id => _id;

        /// <summary>The entry's name/value fields.</summary>
        public RespPairAggregate<RespNameValueEntry> Fields => _fields;

        /// <summary>How long this entry has been pending; only <c>XREADGROUP</c> with <c>CLAIM</c> reports it.</summary>
        public TimeSpan? IdleTime => _idleTime;

        /// <summary>How many times this entry has been delivered; zero unless the server reported it.</summary>
        public int DeliveryCount => _deliveryCount;

        /// <summary>Materialise this entry, so it outlives the reply it came from.</summary>
        /// <remarks><b><c>To</c>, not <c>As</c></b>: the id and every field are copied out.</remarks>
        public StreamEntry ToStreamEntry()
            => _id.IsNull && _fields.Count == 0
                ? StreamEntry.Null
                : new StreamEntry(_id.AsRedisValue(), RespNameValueEntry.ToNameValueEntries(_fields.ToArray()), _idleTime, _deliveryCount);

        /// <inheritdoc/>
        public override string ToString() => $"{_id} ({_fields.Count} field{(_fields.Count == 1 ? "" : "s")})";
    }
}
