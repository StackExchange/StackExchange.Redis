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
