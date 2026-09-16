using System;
using System.Diagnostics.CodeAnalysis;
using RESPite;
using RESPite.Messages;

namespace StackExchange.Redis.Interpolated
{
    /// <summary>
    /// EXPERIMENTAL SPIKE. The root of the context-based surface: one member, from which everything else
    /// hangs as extension members.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The point of having exactly one member is that it is the <b>last</b> addition to an interface. Once a
    /// context is reachable, new commands - ours and other libraries' - are extension members over it, and
    /// break nobody. See design notes section 9.4.
    /// </para>
    /// <para>
    /// <see cref="Context"/> returns <b>by value</b>. A <c>ref readonly</c> would save a copy of roughly
    /// four registers, and cost the ability to use the result in an <c>async</c> method - which is the only
    /// kind of method this surface has.
    /// </para>
    /// </remarks>
    [Experimental(Experiments.InterpolatedWriter, UrlFormat = Experiments.UrlFormat)]
    public interface IRespTarget
    {
        /// <summary>The context commands are composed and sent through.</summary>
        RespContext Context { get; }
    }

    /// <summary>
    /// EXPERIMENTAL SPIKE. A target whose commands are routed by <b>key</b>: a database, a batch, a
    /// transaction.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The keyspace groups - <c>Strings</c>, <c>Hashes</c>, <c>Keys</c> and the rest - bind here rather
    /// than to <see cref="IRespTarget"/>, so that resolving them is what decides whether they make sense.
    /// They used to bind to <see cref="IRespTarget"/>, which <c>IServer</c> and <c>ISubscriber</c> also
    /// carry, so <c>server.Strings.GetAsync(key)</c> compiled - an offer of something an <c>IServer</c> has no
    /// business doing, discovered by exactly the tab-completion that is meant to be the point.
    /// </para>
    /// <para>
    /// Note what this does <i>not</i> do: it does not carry a different executor. Routing already differs
    /// correctly by inheritance - a batch's context queues because its executor's target is the batch, and
    /// a server's pins to one endpoint because <c>RedisServer.ExecuteAsync</c> injects it. The split is
    /// about which commands are <b>offered</b>, which is a separate question from where they go.
    /// </para>
    /// </remarks>
    [Experimental(Experiments.InterpolatedWriter, UrlFormat = Experiments.UrlFormat)]
    public interface IRespKeyspaceTarget : IRespTarget
    {
    }

    /// <summary>
    /// EXPERIMENTAL SPIKE. A target whose commands are pinned to <b>one endpoint</b>: a server.
    /// </summary>
    /// <remarks>
    /// The counterpart to <see cref="IRespKeyspaceTarget"/>, and the reason <c>IServer</c> keeps a context
    /// at all now that the keyspace groups have moved off it: server-scoped groups will bind here, and a
    /// third party's can too.
    /// <para>
    /// A few group <i>names</i> will end up on both, with different members - <c>Keys</c> is key-routed for
    /// <c>DEL</c> and server-scoped for <c>KEYS</c>/<c>SCAN</c>, and <c>Scripts</c> divides the same way
    /// between <c>EVALSHA</c> and <c>SCRIPT LOAD</c>. That is the line <c>IDatabase</c> and <c>IServer</c>
    /// already draw, and it wants drawing deliberately rather than by whichever group is written first.
    /// </para>
    /// </remarks>
    [Experimental(Experiments.InterpolatedWriter, UrlFormat = Experiments.UrlFormat)]
    public interface IRespServerTarget : IRespTarget
    {
    }

    /// <summary>EXPERIMENTAL SPIKE. Reply handlers for the prototype command surface.</summary>
    [Experimental(Experiments.InterpolatedWriter, UrlFormat = Experiments.UrlFormat)]
    public static class RespHandlers
    {
        /// <summary>Reads a bulk string reply as a <see cref="RedisValue"/>; null stays null.</summary>
        public static IRespHandler<RedisValue> Value { get; } = DefaultHandlers.Instance;

        /// <summary>Reads a reply as a boolean, in any of the spellings the server uses for one.</summary>
        /// <remarks>
        /// <b>One handler, not two.</b> <c>SET</c> replies <c>+OK</c>, <c>SETBIT</c> replies <c>:0</c>/<c>:1</c>,
        /// RESP3 has <c>#t</c>/<c>#f</c>, and a conditional write that did not happen replies nil -
        /// four wire shapes for the same question. <see cref="RespReader.ReadBoolean"/> already accepts
        /// the first three (its two-byte simple-string case IS the <c>IsOK</c> compare), so the only thing
        /// left to decide here is that nil means "no", which is what it means everywhere it appears.
        /// A separate OK-only handler would buy one branch and cost every caller a decision.
        /// </remarks>
        public static IRespHandler<bool> Boolean { get; } = DefaultHandlers.Instance;

        /// <summary>Reads an integer reply.</summary>
        public static IRespHandler<long> Int64 { get; } = DefaultHandlers.Instance;

        /// <summary>Reads an integer reply that may be nil, as <c>BITFIELD</c>'s overflow case is.</summary>
        public static IRespHandler<long?> NullableInt64 { get; } = DefaultHandlers.Instance;

        /// <summary>Reads a floating-point reply; RESP2 sends these as bulk strings.</summary>
        public static IRespHandler<double> Double { get; } = DefaultHandlers.Instance;

        /// <summary>Reads an array reply as <see cref="RedisValue"/>s; a nil array reads as empty.</summary>
        /// <remarks>
        /// <b>Internal: the array is the old spelling, and it stays.</b> The public surface returns
        /// <see cref="ValueLease"/>, so a caller can give the storage back; this serves the internal
        /// <c>...Array</c> siblings behind the <c>IDatabase</c> signatures, which are not going anywhere -
        /// compatibility outranks tidiness. Internal rather than retired, so that new code cannot pick the
        /// shape it has no way to reclaim.
        /// </remarks>
        internal static IRespHandler<RedisValue[]> Values { get; } = DefaultHandlers.Instance;

        /// <summary>Reads an array reply into a pooled <see cref="ReadOnlyLease{T}"/> the caller gives back.</summary>
        /// <remarks>
        /// What the new surface returns, where <see cref="Values"/> is what the old one needs. See the
        /// queue notes on getting arrays off this API: the difference is who owns the storage, not what is
        /// in it.
        /// </remarks>
        public static IRespHandler<ReadOnlyLease<RedisValue>> ValueLease { get; } = DefaultHandlers.Instance;

        /// <summary>Reads a bulk string reply as a <see cref="string"/>; null stays null.</summary>
        public static IRespHandler<string?> String { get; } = DefaultHandlers.Instance;

        /// <summary>Reads a bulk string reply as a <see cref="Lease{T}"/>; null stays null.</summary>
        /// <remarks>
        /// The lease always <b>copies</b> here, where the same read against a live reply may instead point
        /// into the reply's buffer: a handler is handed a span whose lifetime ends when it returns, so
        /// there is nothing to share. See <see cref="RespReaderExtensions.ReadLease"/>.
        /// </remarks>
        /// <remarks>
        /// <b>Internal: the writable lease is the old spelling, and it stays.</b> The public surface hands
        /// out <see cref="ReadOnlyLease"/>, which can share the reply's memory because nothing can write
        /// through it; this serves the internal <c>...WritableLease</c> siblings behind the
        /// <c>IDatabase</c> signatures that say <see cref="Lease{T}"/>. Internal rather than retired, so
        /// new code cannot pick the shape that forces a copy.
        /// </remarks>
        internal static IRespHandler<Lease<byte>?> Lease { get; } = DefaultHandlers.Instance;

        /// <summary>The reply as a read-only buffer; shares the underlying memory where it can.</summary>
        public static IRespHandler<ReadOnlyLease<byte>?> ReadOnlyLease { get; } = DefaultHandlers.Instance;

        /// <summary>The whole reply, undecoded - the general-purpose answer for commands we do not model.</summary>
        public static IRespHandler<RespResult> Result { get; } = DefaultHandlers.Instance;

        /// <summary>Reads a one-element array reply as the single value it wraps.</summary>
        /// <remarks>
        /// The <c>FIELDS n</c> commands always reply with an array, one element per field - so asking for
        /// exactly one field still gets <c>*1</c>. This is the shape that turns that back into the single
        /// value the caller asked for. It cannot be the built-in handler for
        /// <see cref="RedisValue"/> (that one reads a scalar), which is why it is named.
        /// </remarks>
        public static IRespHandler<RedisValue> SingletonValue { get; } = new SingletonValueHandler();

        /// <inheritdoc cref="SingletonValue"/>
        /// <remarks><inheritdoc cref="Lease" path="/remarks"/></remarks>
        internal static IRespHandler<Lease<byte>?> SingletonLease { get; } = new SingletonLeaseHandler();

        /// <summary>
        /// Reads an array reply as <see cref="RedisValue"/>s, keeping nil distinct from empty.
        /// </summary>
        /// <remarks>
        /// <b>The one array shape where nil and empty are different answers.</b> <c>LMOVEM</c> replies nil
        /// when nothing moved, and that is worth telling apart from "moved, but nothing was there".
        /// Everywhere else a nil aggregate collapses to empty, because every caller of an array reply
        /// wants to iterate it. A separate handler rather than another interface on the shared one:
        /// nullability of a reference type does not make a distinct interface, so the two cannot coexist
        /// on one class (CS8645).
        /// </remarks>
        public static IRespHandler<ReadOnlyLease<RedisValue>?> NullableValueLease { get; } = new NullableValueLeaseHandler();

        /// <summary>Reads an integer reply, reporting -1 where the server replies nil.</summary>
        /// <remarks>
        /// <c>LPOS</c>'s "not found", which the old surface has always reported as -1 rather than as a
        /// nullable. Named rather than built in, because -1 is a perfectly ordinary integer everywhere
        /// else and nothing should get this by accident.
        /// </remarks>
        public static IRespHandler<long> Int64OrMinusOne { get; } = new Int64OrMinusOneHandler();

        /// <inheritdoc cref="NullableValueLease"/>
        /// <remarks>
        /// The array flavour, for the <c>IDatabase</c> signature that promises a nullable array; internal
        /// for the reason every other array shape is.
        /// </remarks>
        internal static IRespHandler<RedisValue[]?> NullableValues { get; } = new NullableValuesHandler();

        /// <inheritdoc cref="SingletonLease"/>
        /// <remarks>
        /// The sharing flavour, which is what the command surface hands out; <see cref="SingletonLease"/>
        /// is kept for the <c>IDatabase</c> signatures that say <see cref="Lease{T}"/>.
        /// </remarks>
        public static IRespHandler<ReadOnlyLease<byte>?> SingletonReadOnlyLease { get; } = new SingletonReadOnlyLeaseHandler();

        /// <summary>An integer reply counted in <b>seconds</b>, as <c>OBJECT IDLETIME</c> reports.</summary>
        /// <remarks>
        /// Named rather than built in, because the default <see cref="TimeSpan"/> handler reads
        /// <b>milliseconds</b> - it serves <c>PTTL</c>, which is the common case. Two units for one type is
        /// exactly the shape that produces an answer wrong by a factor of a thousand and right-looking, so
        /// the odd one out is spelt out at the call site rather than inferred.
        /// </remarks>
        public static IRespHandler<TimeSpan?> TimeSpanFromSeconds { get; } = new TimeSpanFromSecondsHandler();

        private sealed class TimeSpanFromSecondsHandler : IRespHandler<TimeSpan?>
        {
            public TimeSpan? Parse(ref RespReader reader)
            {
                // nil, not just negative: OBJECT IDLETIME answers a missing key with a nil bulk string,
                // where PTTL - which the millisecond handler serves - answers -2 and never nils. So the
                // two handlers differ in more than the unit, and reading this one as an integer throws
                // "Invalid format parsing BulkString as Int64" the first time it is asked about a key that
                // is not there. Found by comparing against IDatabase rather than by reasoning.
                if (reader.IsNull) return null;

                var seconds = reader.ReadInt64();
                return seconds < 0 ? null : TimeSpan.FromSeconds(seconds);
            }
        }

        /// <summary>Checks the reply for a server error, and reads nothing else.</summary>
        /// <remarks>
        /// What a command with no result still has to do. Without it a failed command would complete
        /// quietly, because there would be no value whose absence gave the game away - the error is the
        /// <i>only</i> thing such a call can report.
        /// </remarks>
        public static IRespHandler<bool> Success { get; } = new SuccessHandler();

        /// <summary>
        /// One projection per element type, shared by <b>every</b> aggregate form of that type.
        /// </summary>
        /// <remarks>
        /// <para>
        /// The same element was being read by two or three hand-written lambdas - the array handler, the
        /// lease handler, and for <c>long?</c> a third inside <c>Lease&lt;long?&gt;</c>'s own loop - sitting
        /// up to 250 lines apart. They agreed, checked one by one, but nothing made them: the array and
        /// lease forms of one command silently disagreeing is a bug with no symptom at the call site.
        /// Naming the projection once makes that unexpressible, and gives the next element type an obvious
        /// home rather than a lambda to copy.
        /// </para>
        /// <para>
        /// <b>These are not the scalar handlers, and must not become them.</b> A top-level handler carries
        /// reply-level semantics that are wrong per element: <c>IRespHandler&lt;bool&gt;</c> reads nil as
        /// <i>false</i> (a conditional <c>SET</c> that did not write), and <c>IRespHandler&lt;long?&gt;</c>
        /// unwraps a unit aggregate (a one-operation <c>BITFIELD</c> still replies <c>*1</c>). Inside an
        /// aggregate both of those would be nonsense. So "implement the element handler and get the
        /// aggregate free" is the wrong shape by exactly the amount those two differ.
        /// </para>
        /// </remarks>
        internal static class Elements
        {
            /// <summary>An integer element.</summary>
            internal static readonly RespReader.Projection<long> Int64 = static (ref r) => r.ReadInt64();

            /// <summary>A boolean element; unlike the top-level handler, nil is not "no" here.</summary>
            internal static readonly RespReader.Projection<bool> Boolean = static (ref r) => r.ReadBoolean();

            /// <summary>A per-key outcome from the <c>EXPIRE</c> family.</summary>
            internal static readonly RespReader.Projection<ExpireResult> Expire = static (ref r) => (ExpireResult)r.ReadInt64();

            /// <summary>A per-id outcome from <c>XDELEX</c>/<c>XACKDEL</c>.</summary>
            internal static readonly RespReader.Projection<StreamTrimResult> TrimResult = static (ref r) => (StreamTrimResult)r.ReadInt64();

            /// <summary>A per-key outcome from <c>PERSIST</c>.</summary>
            internal static readonly RespReader.Projection<PersistResult> Persist = static (ref r) => (PersistResult)r.ReadInt64();

            /// <summary>A score element; <c>ZMSCORE</c> replies nil for a member that is not there.</summary>
            internal static readonly RespReader.Projection<double?> NullableDouble = static (ref r) => r.IsNull ? (double?)null : r.ReadDouble();

            /// <summary>A <c>BITFIELD</c> element; nil where <c>OVERFLOW FAIL</c> skipped an operation.</summary>
            internal static readonly RespReader.Projection<long?> NullableInt64 = static (ref r) => r.IsNull ? (long?)null : r.ReadInt64();

            /// <summary>A coordinate element; nil for a member the key does not hold.</summary>
            internal static readonly RespReader.Projection<GeoPosition?> Position = static (ref r) => GeoPosition.TryRead(ref r);

            /// <summary>A string element that may be nil.</summary>
            internal static readonly RespReader.Projection<string?> NullableString = static (ref r) => r.IsNull ? null : r.ReadString();

            /// <summary>A vector element; the server sends these as doubles.</summary>
            internal static readonly RespReader.Projection<float> Single = static (ref r) => (float)r.ReadDouble();
        }

        /// <summary>
        /// Read an aggregate of scalars straight into a pooled lease.
        /// </summary>
        /// <remarks>
        /// The lease counterpart of <c>ReadPastArray(projection, scalar: true)</c>: same walk, same
        /// projection, but filling storage the caller gives back instead of a fresh array. Six of the
        /// element types here differ only in that projection, so they share this rather than repeating
        /// the rent-and-guard dance eight times. It lives here rather than on the defaults object so
        /// that handlers outside it - the geo group's, whose shape depends on the request - can share
        /// it too.
        /// </remarks>
        /// <typeparam name="T">The element type.</typeparam>
        /// <param name="reader">The reply, already positioned on its first element.</param>
        /// <param name="projection">How to read one element.</param>
        internal static ReadOnlyLease<T> ReadScalarLease<T>(ref RespReader reader, RespReader.Projection<T> projection)
        {
            // a nil aggregate reads as empty, as it does for the array handlers: every caller of an
            // array reply wants to iterate it
            if (reader.IsNull) return ReadOnlyLease<T>.Empty;

            var count = reader.AggregateLength();
            if (count <= 0) return ReadOnlyLease<T>.Empty;

            var lease = ReadOnlyLease<T>.Rent(count, null, out var target);
            try
            {
                var iter = reader.AggregateChildren();
                for (var i = 0; i < count; i++)
                {
                    iter.DemandNext();
                    var element = iter.Value;
                    target[i] = projection(ref element);
                }

                return lease;
            }
            catch
            {
                // rented by now, and nobody else has a reference to hand back
                lease.Dispose();
                throw;
            }
        }

        /// <summary>
        /// Both aggregate forms of a <b>pair</b> element type, derived from the one shape that reads it.
        /// </summary>
        /// <remarks>
        /// <para>
        /// The pair counterpart of <see cref="Elements"/> and <see cref="ReadScalarLease"/>: a pair type
        /// declares one <c>ValuePairInterleavedProcessorBase&lt;T&gt;</c> and gets the array and the lease
        /// from it, rather than writing the rent-and-adopt dance once per type per form.
        /// </para>
        /// <para>
        /// <b>Jagged versus interleaved is already handled, and deliberately not re-derived here.</b> RESP2
        /// sends a row as two interleaved elements and RESP3 may send it as a nested two-element array;
        /// <c>ParseArray</c> decides which from the <i>content</i>, once per reply, and runs one of two
        /// loops that both call the same per-row parse. So the walker the queue imagined building already
        /// exists on the processor - what was missing was only these two lines of derivation.
        /// </para>
        /// </remarks>
        /// <typeparam name="T">The row type.</typeparam>
        /// <param name="reader">The reply, positioned on the aggregate.</param>
        /// <param name="shape">The processor that knows how to read one row.</param>
        internal static T[] ReadPairArray<T>(ref RespReader reader, ResultProcessor.ValuePairInterleavedProcessorBase<T> shape)
            => shape.ParseArray(ref reader, RedisProtocol.Resp3, allowOversized: false, out _, state: null)
               ?? Array.Empty<T>();

        /// <inheritdoc cref="ReadPairArray"/>
        /// <remarks>
        /// <b>No copy.</b> <c>ParseArray(allowOversized: true)</c> already rents from <c>ArrayPool</c> and
        /// reports the live length separately, which is a lease wearing different clothes - so this adopts
        /// the rental rather than copying out of it.
        /// </remarks>
        internal static ReadOnlyLease<T> ReadPairLease<T>(ref RespReader reader, ResultProcessor.ValuePairInterleavedProcessorBase<T> shape)
        {
            var pooled = shape.ParseArray(ref reader, RedisProtocol.Resp3, allowOversized: true, out var count, state: null);
            return pooled is null ? ReadOnlyLease<T>.Empty : ReadOnlyLease<T>.Adopt(pooled, count);
        }

        /// <summary>
        /// An array reply as a lease of scalar windows, all pointing into the one reply buffer.
        /// </summary>
        /// <remarks>
        /// <para>
        /// <b>The point of <see cref="RespValue"/>.</b> The <see cref="RedisValue"/> equivalent is one
        /// pooled array plus an allocation per element that is neither small nor a canonical number; this
        /// is one pooled array of windows, and the reply buffer they share.
        /// </para>
        /// <para>
        /// The lease owns both: its own array, and - as its secondary - the payload whose reference this
        /// handler took. Disposing the lease gives back both, in either order, because neither depends on
        /// the other.
        /// </para>
        /// <para>
        /// Losing the retain race means the buffer is already on its way back to the pool, so the copying
        /// path takes over - exactly as <c>ShareAsResult</c> does, and for the same reason: resurrecting
        /// it is the one thing that must not happen.
        /// </para>
        /// </remarks>
        internal sealed class ValueWindowHandler :
            IRespHandler<ReadOnlyLease<RespValue>>,
            IRespPayloadHandler<ReadOnlyLease<RespValue>>
        {
            private static readonly ValueWindowHandler Instance = new();

            /// <summary>A nil aggregate reads as empty: every caller of an array reply wants to iterate it.</summary>
            internal static IRespHandler<ReadOnlyLease<RespValue>> Lease => Instance;

            /// <summary>
            /// A nil aggregate reads as <see langword="null"/>, for the handful of commands where "nothing
            /// happened" and "an empty result" are different answers - <c>LMPOP</c>, and the bulk move.
            /// </summary>
            /// <remarks>
            /// A separate object rather than another interface on this one: the lease is a class, so the
            /// two differ only in nullability annotation and name the same runtime type. That is why the
            /// RedisValue pair are two instances too.
            /// </remarks>
            internal static IRespHandler<ReadOnlyLease<RespValue>?> NullableLease { get; }
                = new NullableValueWindowHandler();

            private sealed class NullableValueWindowHandler :
                IRespHandler<ReadOnlyLease<RespValue>?>,
                IRespPayloadHandler<ReadOnlyLease<RespValue>?>
            {
                ReadOnlyLease<RespValue>? IRespPayloadHandler<ReadOnlyLease<RespValue>?>.Parse(RespPayload payload)
                {
                    var reader = payload.GetReader();
                    reader.MoveNext();
                    return reader.IsNull
                        ? null
                        : ((IRespPayloadHandler<ReadOnlyLease<RespValue>>)Instance).Parse(payload);
                }

                /// <summary>Never reached, as on the non-null form: a window needs an owner.</summary>
                ReadOnlyLease<RespValue>? IRespHandler<ReadOnlyLease<RespValue>?>.Parse(ref RespReader reader)
                    => throw new NotSupportedException(
                        $"{nameof(RespValue)} points into the reply buffer, so it is parsed from the payload rather than a positioned reader.");
            }

            ReadOnlyLease<RespValue> IRespPayloadHandler<ReadOnlyLease<RespValue>>.Parse(RespPayload payload)
            {
                if (!payload.TryRetain()) return Copy(payload.Span);

                try
                {
                    return Capture(payload.Span, payload, payload);
                }
                catch
                {
                    payload.Release();
                    throw;
                }
            }

            /// <summary>Never reached: a window needs an owner, so callers take the payload form.</summary>
            /// <remarks>
            /// The same shape as <see cref="RespResult"/>'s: a reader carries a position, not the identity
            /// of the buffer it is reading, and a <see cref="RespValue"/> is defined by pointing into one.
            /// Callers route through <see cref="IRespPayloadHandler{TResult}"/> first, which is why this is
            /// unreachable rather than merely unimplemented.
            /// </remarks>
            ReadOnlyLease<RespValue> IRespHandler<ReadOnlyLease<RespValue>>.Parse(ref RespReader reader)
                => throw new NotSupportedException(
                    $"{nameof(RespValue)} points into the reply buffer, so it is parsed from the payload rather than a positioned reader.");

            /// <summary>No payload to hold, so take a copy of the bytes and own that instead.</summary>
            private static ReadOnlyLease<RespValue> Copy(ReadOnlySpan<byte> response)
            {
                var bytes = ReadOnlyLease<byte>.Rent(response.Length, null, out var target);
                if (bytes.IsEmpty) return ReadOnlyLease<RespValue>.Empty;

                try
                {
                    response.CopyTo(target);
                    return Capture(bytes.Span, bytes, bytes);
                }
                catch
                {
                    bytes.Dispose();
                    throw;
                }
            }

            private static ReadOnlyLease<RespValue> Capture(ReadOnlySpan<byte> frame, object owner, IDisposable secondary)
            {
                var reader = new RespReader(frame);
                reader.MoveNext();

                // a nil aggregate reads as empty, as it does for the array handlers
                if (reader.IsNull) return Release(secondary);

                var count = reader.AggregateLength();
                if (count <= 0) return Release(secondary);

                var lease = ReadOnlyLease<RespValue>.Rent(count, null, out var target, secondary);
                try
                {
                    for (var i = 0; i < count; i++)
                    {
                        if (!RespValue.TryCaptureNext(owner, ref reader, out target[i]))
                        {
                            throw new RespException("Fewer elements than the reply promised.");
                        }
                    }

                    return lease;
                }
                catch
                {
                    lease.Dispose(); // takes the secondary with it
                    throw;
                }

                static ReadOnlyLease<RespValue> Release(IDisposable held)
                {
                    held.Dispose();
                    return ReadOnlyLease<RespValue>.Empty;
                }
            }
        }

        /// <summary>The handler used when a call does not name one; resolved by result type.</summary>
        /// <typeparam name="T">The result type.</typeparam>
        /// <remarks>
        /// This is what lets a command surface be one expression: most commands want the obvious handler
        /// for their result type, and naming it every time is noise. A type with no registered handler
        /// throws where the call is written, saying which type and what to do - not at the point the reply
        /// arrives.
        /// </remarks>
        internal static class Inbuilt<T>
        {
            internal static readonly IRespHandler<T>? Handler = Resolve();

            internal static IRespHandler<T> Require()
                => Handler ?? throw new InvalidOperationException(
                    $"No built-in RESP handler for '{typeof(T).Name}'; pass one explicitly.");

            /// <summary>
            /// Ask the defaults object whether it handles <typeparamref name="T"/>.
            /// </summary>
            /// <remarks>
            /// One cast, memoized by the runtime in the static field above - so this runs once per closed
            /// <typeparamref name="T"/>, exactly as the hand-written ladder it replaces did, without anybody
            /// having to maintain the ladder.
            /// <para>
            /// This relies on <c>IRespHandler&lt;T&gt;</c> being <b>invariant</b>. Were it covariant, the cast
            /// would honour variance and a handler for a derived type could answer a request for a base one -
            /// an <c>IRespHandler&lt;string&gt;</c> quietly serving <c>SendAsync&lt;object&gt;</c>, say.
            /// </para>
            /// </remarks>
            private static IRespHandler<T>? Resolve() => DefaultHandlers.Instance as IRespHandler<T>;
        }

        /// <summary>
        /// Every default reply handler, on one object.
        /// </summary>
        /// <remarks>
        /// <para>
        /// <b>Implementing the interface IS the registration.</b> There is no list to remember to extend
        /// and no <c>typeof</c> ladder to keep in step: <see cref="Inbuilt{T}"/> simply asks whether this
        /// object is an <c>IRespHandler&lt;T&gt;</c>. A handler that compiles is a handler that is reachable.
        /// </para>
        /// <para>
        /// It also makes "two defaults for one result type" a <b>compile error</b> rather than a silent
        /// coin-toss, which neither a <c>typeof</c> chain nor a registration array can manage. That matters
        /// here, because several result types genuinely have more than one handler - <c>bool</c> has
        /// <see cref="Boolean"/> and <see cref="Success"/>, <see cref="RedisValue"/> has <see cref="Value"/>
        /// and <see cref="SingletonValue"/>. Exactly one of each can live here; the alternates stay as their
        /// own classes and are named explicitly by the commands that want them, which is the honest way to
        /// say "this is not the default".
        /// </para>
        /// <para>
        /// The implementations are explicit, which is forced rather than chosen: one <c>Parse</c> per result
        /// type, differing only by return type, is not a legal implicit overload set. The happy side effect
        /// is that none of them clutters this type's own surface.
        /// </para>
        /// </remarks>
        private sealed class DefaultHandlers :
            IRespHandler<ReadOnlyLease<RespValue>>,
            IRespPayloadHandler<ReadOnlyLease<RespValue>>,
            IRespHandler<ExpireResult[]>,
            IRespHandler<ReadOnlyLease<ExpireResult>>,
            IRespHandler<HashEntry[]>,
            IRespHandler<ReadOnlyLease<HashEntry>>,
            IRespHandler<LCSMatchResult>,
            IRespHandler<Lease<byte>?>,
            IRespHandler<Lease<long?>>,
            IRespHandler<PersistResult[]>,
            IRespHandler<StreamTrimResult[]>,
            IRespHandler<RedisArrayIndex>,
            IRespHandler<RedisArrayIndex?>,
            IRespHandler<ArrayInfo>,
            IRespHandler<RedisArrayEntry[]>,
            IRespHandler<ReadOnlyLease<RedisArrayEntry>>,
            IRespHandler<ReadOnlyLease<StreamTrimResult>>,
            IRespHandler<ReadOnlyLease<PersistResult>>,
            IRespHandler<ReadOnlyLease<byte>?>,
            IRespHandler<ReadOnlyLease<long?>>,
            IRespHandler<ListPopResult>,
            IRespHandler<RedisValue>,
            IRespHandler<RedisKey>,
            IRespHandler<RedisType>,
            IRespHandler<TimeSpan?>,
            IRespHandler<DateTime?>,
            IRespHandler<RedisValue[]>,
            IRespHandler<ReadOnlyLease<RedisValue>>,
            IRespHandler<SortedSetEntry?>,
            IRespHandler<SortedSetEntry[]>,
            IRespHandler<ReadOnlyLease<SortedSetEntry>>,
            IRespHandler<SortedSetPopResult>,
            IRespHandler<StringIncrementResult<double>>,
            IRespHandler<StringIncrementResult<long>>,
            IRespHandler<ValueCondition?>,
            IRespHandler<bool>,
            IRespHandler<bool[]>,
            IRespHandler<ReadOnlyLease<bool>>,
            IRespHandler<double>,
            IRespHandler<double?>,
            IRespHandler<double?[]>,
            IRespHandler<ReadOnlyLease<double?>>,
            IRespHandler<long>,
            IRespHandler<long?>,
            IRespHandler<long[]>,
            IRespHandler<ReadOnlyLease<long>>,
            IRespHandler<string?>,
            IRespPayloadHandler<RespResult>
        {
            internal static readonly DefaultHandlers Instance = new();

            RedisValue IRespHandler<RedisValue>.Parse(ref RespReader reader)
            {
                return reader.IsNull ? RedisValue.Null : reader.ReadRedisValue();
            }

            bool IRespHandler<bool>.Parse(ref RespReader reader)
            {
                // nil is not a failure here, and this is the one place that has to say so: a SET under
                // NX/XX that did not write, a GETEX on a missing key - the command worked, the answer is no
                return !reader.IsNull && reader.ReadBoolean();
            }

            long IRespHandler<long>.Parse(ref RespReader reader)
            {
                return reader.ReadInt64();
            }

            long? IRespHandler<long?>.Parse(ref RespReader reader)
            {
                // a single-operation BITFIELD still replies with an array; unwrap a unit one, as the
                // MessageWriter path's NullableInt64Processor does, so the caller sees one value
                if (reader.IsAggregate) reader.MoveNext();

                return reader.IsNull ? null : reader.ReadInt64();
            }

            double IRespHandler<double>.Parse(ref RespReader reader)
            {
                return reader.ReadDouble();
            }

            /// <remarks>
            /// The pooled counterpart of the array handler below. Same reply, same elements; what differs is
            /// that the caller can give the storage back, which on a large <c>MGET</c> is the part that
            /// would otherwise reach gen 2. The elements themselves still allocate - <c>RedisValue</c> has
            /// no lifetime and cannot be handed one - so this saves the array, not its contents.
            /// </remarks>
            ReadOnlyLease<RedisValue> IRespHandler<ReadOnlyLease<RedisValue>>.Parse(ref RespReader reader)
            {
                // as the array handler: a nil aggregate reads as empty, because every caller of an array
                // reply wants to iterate it
                if (reader.IsNull) return ReadOnlyLease<RedisValue>.Empty;

                var count = reader.AggregateLength();
                if (count <= 0) return ReadOnlyLease<RedisValue>.Empty;

                var lease = ReadOnlyLease<RedisValue>.Rent(count, null, out var target);
                try
                {
                    var children = reader.AggregateChildren();
                    var index = 0;
                    while (index < count && children.MoveNext())
                    {
                        target[index++] = children.Value.ReadRedisValue();
                    }

                    return lease;
                }
                catch
                {
                    // the lease is rented by now, and nobody else has a reference to give back
                    lease.Dispose();
                    throw;
                }
            }

            ReadOnlyLease<long> IRespHandler<ReadOnlyLease<long>>.Parse(ref RespReader reader)
                => ReadScalarLease(ref reader, Elements.Int64);

            ReadOnlyLease<bool> IRespHandler<ReadOnlyLease<bool>>.Parse(ref RespReader reader)
                => ReadScalarLease(ref reader, Elements.Boolean);

            ReadOnlyLease<ExpireResult> IRespHandler<ReadOnlyLease<ExpireResult>>.Parse(ref RespReader reader)
                => ReadScalarLease(ref reader, Elements.Expire);

            // the array family: every one of these delegates to the parse the classic path already uses,
            // rather than carrying a second copy of it
            private static readonly ResultProcessor.RedisArrayEntryArrayProcessor ArrayEntryShape = new();

            RedisArrayIndex IRespHandler<RedisArrayIndex>.Parse(ref RespReader reader)
                => ResultProcessor.TryParseArrayIndex(ref reader, out var index)
                    ? index
                    : throw new RespException("Unexpected array-index reply.");

            /// <remarks>ARNEXT replies nil when the array is full, which is an answer rather than a failure.</remarks>
            RedisArrayIndex? IRespHandler<RedisArrayIndex?>.Parse(ref RespReader reader)
            {
                if (reader.IsScalar && reader.IsNull) return null;
                return ResultProcessor.TryParseArrayIndex(ref reader, out var index)
                    ? index
                    : throw new RespException("Unexpected array-index reply.");
            }

            ArrayInfo IRespHandler<ArrayInfo>.Parse(ref RespReader reader)
                => ResultProcessor.TryParseArrayInfo(ref reader, out var info)
                    ? info
                    : throw new RespException("Unexpected ARINFO reply.");

            RedisArrayEntry[] IRespHandler<RedisArrayEntry[]>.Parse(ref RespReader reader)
                => ReadPairArray(ref reader, ArrayEntryShape);

            ReadOnlyLease<RedisArrayEntry> IRespHandler<ReadOnlyLease<RedisArrayEntry>>.Parse(ref RespReader reader)
                => ReadPairLease(ref reader, ArrayEntryShape);

            ReadOnlyLease<StreamTrimResult> IRespHandler<ReadOnlyLease<StreamTrimResult>>.Parse(ref RespReader reader)
                => ReadScalarLease(ref reader, Elements.TrimResult);

            ReadOnlyLease<PersistResult> IRespHandler<ReadOnlyLease<PersistResult>>.Parse(ref RespReader reader)
                => ReadScalarLease(ref reader, Elements.Persist);

            /// <remarks>ZMSCORE replies nil for a member that is not there, so the element type is nullable.</remarks>
            ReadOnlyLease<double?> IRespHandler<ReadOnlyLease<double?>>.Parse(ref RespReader reader)
                => ReadScalarLease(ref reader, Elements.NullableDouble);

            /// <remarks>BITFIELD replies nil for an operation skipped by OVERFLOW FAIL, hence nullable.</remarks>
            ReadOnlyLease<long?> IRespHandler<ReadOnlyLease<long?>>.Parse(ref RespReader reader)
                => ReadScalarLease(ref reader, Elements.NullableInt64);

            ListPopResult IRespHandler<ListPopResult>.Parse(ref RespReader reader)
            {
                return ListPopResult.TryRead(ref reader, out var result)
                    ? result
                    : throw new RespException("Unexpected LMPOP reply.");
            }

            ReadOnlyLease<HashEntry> IRespHandler<ReadOnlyLease<HashEntry>>.Parse(ref RespReader reader)
                => ReadPairLease(ref reader, HashEntryShape);

            // the window shape is registered HERE, not just on ValueWindowHandler, because Inbuilt<T> asks
            // this object and nothing else: a handler that is not on the defaults is reachable only by
            // being named explicitly, which the *Core forwarders cannot do

            /// <summary>Unreachable in the same way, and for the same reason: see ValueWindowHandler.</summary>
            ReadOnlyLease<RespValue> IRespHandler<ReadOnlyLease<RespValue>>.Parse(ref RespReader reader)
                => ValueWindowHandler.Lease.Parse(ref reader);

            ReadOnlyLease<RespValue> IRespPayloadHandler<ReadOnlyLease<RespValue>>.Parse(RespPayload payload)
                => ((IRespPayloadHandler<ReadOnlyLease<RespValue>>)ValueWindowHandler.Lease).Parse(payload);

            /// <inheritdoc cref="IRespHandler{T}.Parse" path="/remarks"/>
            ReadOnlyLease<SortedSetEntry> IRespHandler<ReadOnlyLease<SortedSetEntry>>.Parse(ref RespReader reader)
                => ReadPairLease(ref reader, SortedSetEntryShape);

            RedisKey IRespHandler<RedisKey>.Parse(ref RespReader reader)
            {
                return reader.IsNull ? default : (RedisKey)reader.ReadString()!;
            }

            /// <remarks>
            /// Parsed through the generated token table rather than <c>Enum.TryParse</c>, because the wire
            /// spellings are not the member names: a sorted set is <c>zset</c>.
            /// </remarks>
            RedisType IRespHandler<RedisType>.Parse(ref RespReader reader)
            {
                RedisType result;
                unsafe
                {
                    if (!reader.TryParseScalar(&RedisTypeMetadata.TryParse, out result)) result = RedisType.Unknown;
                }

                return result;
            }

            /// <remarks>
            /// <c>PTTL</c> answers <c>-2</c> for "no such key" and <c>-1</c> for "no expiry", and the old
            /// surface collapses both to null - the caller asked how long is left, and in both cases the
            /// answer is "no deadline". Distinguishing them is what <c>EXISTS</c> is for.
            /// </remarks>
            TimeSpan? IRespHandler<TimeSpan?>.Parse(ref RespReader reader)
            {
                var ms = reader.ReadInt64();
                return ms < 0 ? null : TimeSpan.FromMilliseconds(ms);
            }

            /// <remarks>As the <see cref="TimeSpan"/> handler: negative means there is no deadline to report.</remarks>
            DateTime? IRespHandler<DateTime?>.Parse(ref RespReader reader)
            {
                var ms = reader.ReadInt64();
                return ms < 0 ? null : DateTimeOffset.FromUnixTimeMilliseconds(ms).UtcDateTime;
            }

            RedisValue[] IRespHandler<RedisValue[]>.Parse(ref RespReader reader)
            {
                // a nil array - which MGET does not send, but a RESP3 server may for an empty aggregate -
                // reads as empty rather than null, because every caller of an array reply wants to iterate it
                return reader.ReadPastRedisValues() ?? Array.Empty<RedisValue>();
            }

            string? IRespHandler<string?>.Parse(ref RespReader reader)
            {
                return reader.IsNull ? null : reader.ReadString();
            }

            /// <summary>
            /// Share the reply's buffer rather than copying it - one more reference, not a second copy.
            /// </summary>
            /// <remarks>
            /// The whole reason <c>RespResult</c> is the general-purpose result type: it exposes only
            /// readers, so nothing can write through it, which is what makes sharing memory that is still
            /// owned elsewhere safe. Falls back to a copy if the buffer has already gone - losing that race
            /// means it is on its way back to the pool, and resurrecting it is exactly what must not happen.
            /// </remarks>
            RespResult IRespPayloadHandler<RespResult>.Parse(RespPayload payload)
                => payload.ShareAsResult() ?? RespResult.Capture(payload.Span);

            /// <summary>Never reached: this handler needs the whole frame, so callers take the payload form.</summary>
            /// <remarks>
            /// A positioned reader is past the prefix and length bytes, and a <see cref="RespResult"/> is
            /// defined by carrying them - so this is the one shape the reader form cannot express. Callers
            /// route through <see cref="IRespPayloadHandler{TResult}"/> first, which is why this is
            /// unreachable rather than merely unimplemented; same pattern as the result processors that
            /// fully override <c>SetResult</c> and leave <c>SetResultCore</c> throwing.
            /// </remarks>
            RespResult IRespHandler<RespResult>.Parse(ref RespReader reader)
                => throw new NotSupportedException(
                    $"{nameof(RespResult)} retains the reply frame, so it is parsed from the payload rather than a positioned reader.");

            /// <summary>The reply as a buffer the caller owns outright, and may write to.</summary>
            /// <remarks>
            /// Copies, necessarily: a mutable lease must not point at memory anything else can read. The
            /// <see cref="ReadOnlyLease{T}"/> sibling is the one that can share. See design notes 6.16.
            /// </remarks>
            Lease<byte>? IRespHandler<Lease<byte>?>.Parse(ref RespReader reader)
            {
#pragma warning disable CS0618 // Type or member is obsolete - the copying form is what this contract needs
                return RespReaderExtensions.ReadLease(in reader);
#pragma warning restore CS0618
            }

            ReadOnlyLease<byte>? IRespHandler<ReadOnlyLease<byte>?>.Parse(ref RespReader reader)
            {
                return reader.ReadLease();
            }

            ValueCondition? IRespHandler<ValueCondition?>.Parse(ref RespReader reader)
            {
                return ValueCondition.TryReadDigest(in reader, out var digest)
                    ? digest
                    : throw new RespException("Unexpected DIGEST reply.");
            }

            LCSMatchResult IRespHandler<LCSMatchResult>.Parse(ref RespReader reader)
            {
                return LCSMatchResult.TryRead(ref reader, out var result)
                    ? result
                    : throw new RespException("Unexpected LCS IDX reply.");
            }

            StringIncrementResult<long> IRespHandler<StringIncrementResult<long>>.Parse(ref RespReader reader)
            {
                // [value, applied-increment]; under a bound the second is not the one that was asked for
                if (reader.IsAggregate
                    && reader.TryMoveNext() && reader.IsScalar && reader.TryReadInt64(out var value)
                    && reader.TryMoveNext() && reader.IsScalar && reader.TryReadInt64(out var applied))
                {
                    return new StringIncrementResult<long>(value, applied);
                }

                throw new RespException("Unexpected INCREX reply.");
            }

            Lease<long?> IRespHandler<Lease<long?>>.Parse(ref RespReader reader)
            {
                // BITFIELD's reply: a flat array with one element per sub-operation, nil where
                // OVERFLOW FAIL skipped one
                reader.DemandAggregate();
                if (reader.IsNull) return Lease<long?>.Empty;

                var length = reader.AggregateLength();
                if (length == 0) return Lease<long?>.Empty;

                var lease = Lease<long?>.Create(length, clear: false);
                try
                {
                    var target = lease.Span;
                    for (var i = 0; i < length; i++)
                    {
                        reader.MoveNextScalar();
                        target[i] = Elements.NullableInt64(ref reader);
                    }
                }
                catch
                {
                    lease.Dispose();
                    throw;
                }

                return lease;
            }

            // RESP2 sends name/value interleaved and RESP3 may send them jagged; the existing processor
            // already decides between them from the CONTENT rather than from the negotiated protocol, so
            // reusing it is both less code and the only way the two readers cannot disagree. Resp3 is
            // passed to enable that detection, not to assert anything about the connection.
            private static readonly ResultProcessor.HashEntryArrayProcessor HashEntryShape = new();

            HashEntry[] IRespHandler<HashEntry[]>.Parse(ref RespReader reader)
                => ReadPairArray(ref reader, HashEntryShape);

            long[] IRespHandler<long[]>.Parse(ref RespReader reader)
            {
                return reader.ReadPastArray(Elements.Int64, scalar: true) ?? Array.Empty<long>();
            }

            ExpireResult[] IRespHandler<ExpireResult[]>.Parse(ref RespReader reader)
            {
                return reader.ReadPastArray(Elements.Expire, scalar: true) ?? Array.Empty<ExpireResult>();
            }

            double? IRespHandler<double?>.Parse(ref RespReader reader)
            {
                return reader.IsNull ? null : reader.ReadDouble();
            }

            double?[] IRespHandler<double?[]>.Parse(ref RespReader reader)
            {
                // ZMSCORE replies nil for a member that is not there, so the element type has to be nullable
                return reader.ReadPastArray(Elements.NullableDouble, scalar: true) ?? Array.Empty<double?>();
            }

            // as HashEntryHandler: interleaved in RESP2, possibly jagged in RESP3, decided from the content
            private static readonly ResultProcessor.SortedSetEntryArrayProcessor SortedSetEntryShape = new();

            SortedSetEntry[] IRespHandler<SortedSetEntry[]>.Parse(ref RespReader reader)
                => ReadPairArray(ref reader, SortedSetEntryShape);

            SortedSetEntry? IRespHandler<SortedSetEntry?>.Parse(ref RespReader reader)
            {
                return SortedSetEntry.TryRead(ref reader, out var result)
                    ? result
                    : throw new RespException("Unexpected sorted-set pop reply.");
            }

            SortedSetPopResult IRespHandler<SortedSetPopResult>.Parse(ref RespReader reader)
            {
                return SortedSetPopResult.TryRead(ref reader, out var result)
                    ? result
                    : throw new RespException("Unexpected ZMPOP reply.");
            }

            bool[] IRespHandler<bool[]>.Parse(ref RespReader reader)
            {
                return reader.ReadPastArray(Elements.Boolean, scalar: true) ?? Array.Empty<bool>();
            }

            StreamTrimResult[] IRespHandler<StreamTrimResult[]>.Parse(ref RespReader reader)
                => reader.ReadPastArray(Elements.TrimResult, scalar: true) ?? Array.Empty<StreamTrimResult>();

            PersistResult[] IRespHandler<PersistResult[]>.Parse(ref RespReader reader)
            {
                return reader.ReadPastArray(Elements.Persist, scalar: true) ?? Array.Empty<PersistResult>();
            }

            StringIncrementResult<double> IRespHandler<StringIncrementResult<double>>.Parse(ref RespReader reader)
            {
                if (reader.IsAggregate
                    && reader.TryMoveNext() && reader.IsScalar && reader.TryReadDouble(out var value)
                    && reader.TryMoveNext() && reader.IsScalar && reader.TryReadDouble(out var applied))
                {
                    return new StringIncrementResult<double>(value, applied);
                }

                throw new RespException("Unexpected INCREX reply.");
            }
        }

        private sealed class SuccessHandler : IRespHandler<bool>
        {
            /// <remarks>
            /// Nothing to read: the caller's <c>MoveNext</c> already skipped attributes and threw on an
            /// error element, which is the whole of "did it work?".
            /// </remarks>
            public bool Parse(ref RespReader reader) => true;
        }

        // ---- one-command shapes; reachable through Inbuilt<T>, deliberately not named above ----
        private static readonly IRespHandler<ValueCondition?> s_digest = DefaultHandlers.Instance;
        private static readonly IRespHandler<LCSMatchResult> s_lcsMatch = DefaultHandlers.Instance;
        private static readonly IRespHandler<StringIncrementResult<long>> s_incrementInt64 = DefaultHandlers.Instance;
        private static readonly IRespHandler<StringIncrementResult<double>> s_incrementDouble = DefaultHandlers.Instance;
        private static readonly IRespHandler<Lease<long?>> s_nullableInt64Lease = DefaultHandlers.Instance;
        private static readonly IRespHandler<HashEntry[]> s_hashEntries = DefaultHandlers.Instance;
        private static readonly IRespHandler<long[]> s_int64Array = DefaultHandlers.Instance;
        private static readonly IRespHandler<ExpireResult[]> s_expireResults = DefaultHandlers.Instance;
        private static readonly IRespHandler<PersistResult[]> s_persistResults = DefaultHandlers.Instance;
        private static readonly IRespHandler<bool[]> s_booleans = DefaultHandlers.Instance;
        private static readonly IRespHandler<double?> s_nullableDouble = DefaultHandlers.Instance;
        private static readonly IRespHandler<double?[]> s_nullableDoubles = DefaultHandlers.Instance;
        private static readonly IRespHandler<SortedSetEntry[]> s_sortedSetEntries = DefaultHandlers.Instance;
        private static readonly IRespHandler<SortedSetEntry?> s_sortedSetEntry = DefaultHandlers.Instance;
        private static readonly IRespHandler<SortedSetPopResult> s_sortedSetPop = DefaultHandlers.Instance;

        private sealed class SingletonValueHandler : IRespHandler<RedisValue>
        {
            public RedisValue Parse(ref RespReader reader)
            {
                if (reader.IsNull) return RedisValue.Null; // the whole reply, not an element of it
                reader.MoveNext();
                return reader.IsNull ? RedisValue.Null : reader.ReadRedisValue();
            }
        }

        /// <remarks>
        /// The copying form, matching <see cref="Lease"/> rather than <see cref="ReadOnlyLease"/>: this
        /// exists to serve <c>IDatabase.HashFieldGetLease*</c>, whose signatures say <see cref="Lease{T}"/>.
        /// A sharing singleton would be a <see cref="ReadOnlyLease{T}"/> sibling, which is a decision for
        /// whoever finishes design notes 6.16 rather than one to guess at here.
        /// </remarks>
        private sealed class SingletonLeaseHandler : IRespHandler<Lease<byte>?>
        {
            public Lease<byte>? Parse(ref RespReader reader)
            {
                if (reader.IsNull) return null;
                reader.MoveNext();
#pragma warning disable CS0618 // the copying form is what this contract needs; see the remarks
                return RespReaderExtensions.ReadLease(in reader);
#pragma warning restore CS0618
            }
        }

        /// <inheritdoc cref="Int64OrMinusOne"/>
        private sealed class Int64OrMinusOneHandler : IRespHandler<long>
        {
            public long Parse(ref RespReader reader)
            {
                return reader.IsNull ? -1 : reader.ReadInt64();
            }
        }

        /// <inheritdoc cref="NullableValues"/>
        private sealed class NullableValuesHandler : IRespHandler<RedisValue[]?>
        {
            public RedisValue[]? Parse(ref RespReader reader)
                => reader.IsNull ? null : RespHandlers.Values.Parse(ref reader);
        }

        /// <inheritdoc cref="NullableValueLease"/>
        private sealed class NullableValueLeaseHandler : IRespHandler<ReadOnlyLease<RedisValue>?>
        {
            public ReadOnlyLease<RedisValue>? Parse(ref RespReader reader)
                => reader.IsNull ? null : RespHandlers.ValueLease.Parse(ref reader);
        }

        /// <inheritdoc cref="SingletonLeaseHandler"/>
        private sealed class SingletonReadOnlyLeaseHandler : IRespHandler<ReadOnlyLease<byte>?>
        {
            public ReadOnlyLease<byte>? Parse(ref RespReader reader)
            {
                if (reader.IsNull) return null; // the whole reply, not an element of it
                reader.MoveNext();
                return reader.ReadLease();
            }
        }
    }

    /// <summary>
    /// EXPERIMENTAL SPIKE. The command surface, as extension members.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is the shape the whole design exists to enable: <c>ctx.Strings.SetAsync(key, value)</c> reads like a
    /// built-in method, groups the surface the way Redis documents itself, and is reachable by any library -
    /// including one that is not this one - without a wrapper interface or a forked surface.
    /// </para>
    /// <para>
    /// One partial file per command group - <c>RespSurface.Strings.cs</c>, and so on - matching how Redis
    /// documents itself, and how <c>RedisDatabase</c>'s ~6k lines would have liked to be split.
    /// </para>
    /// </remarks>
    [Experimental(Experiments.InterpolatedWriter, UrlFormat = Experiments.UrlFormat)]

    // HOW A COMMAND SAYS HOW SAFE IT IS TO REPLAY - the house rule for every group on this surface.
    //
    // The retry category comes from CommandFlagsExtensions.WithDefaultCategory: the same per-command table
    // the MessageWriter path uses, rather than a constant named at each call site. Two writers agreeing on
    // the bytes and disagreeing on whether a command is safe to replay is the kind of divergence nothing
    // would catch, so the table is the single source of truth.
    //
    // A command whose ARGUMENTS change the answer raises it EXPLICITLY, with WithRetryCategory, before
    // falling through to the table - SORT with STORE, GETEX with a TTL, SET under NX, SCAN past the first
    // cursor. There are 18 such sites, and they are the whole reason the table can afford to be keyed on
    // the command alone: it says "raised to a write where we can see the args", and these are where they
    // are seen. WithRetryCategory is caller-wins, so this composes in either order and never overrides a
    // category the caller chose.
    //
    // Which is also why spelling the category at all ~250 sites was considered and REJECTED: it would be
    // ~240 restatements of the table plus the 18 refinements that already exist, and the restatements are
    // pure drift risk against the path that still reads the table. See the queue.
    //
    // WithRetryCategory stays public for surfaces outside this assembly, which cannot see the table.

    // RS0026 warns about overloads that carry optional parameters, because adding one later can make an
    // existing call ambiguous. That hazard cannot arise here, and saying so once beats a pragma per
    // command: every member of this class is an extension method whose FIRST parameter is a group type -
    // RespStrings, RespHashes, RespSets, ... - so two members sharing a name are only ever candidates for
    // the same call when their receivers are the same group, and within a group the overloads differ in a
    // parameter that has no default (a span versus a single value, a long versus a double). The names
    // repeat across groups on purpose: ctx.Strings.Length and ctx.Sets.Length are the same word because
    // they are the same idea, which is the entire argument for grouping.
    [SuppressMessage("ApiDesign", "RS0026:Do not add multiple overloads with optional parameters", Justification = "Extension members on distinct group types; see the comment above")]
    public static partial class RespSurface
    {
    }
}
