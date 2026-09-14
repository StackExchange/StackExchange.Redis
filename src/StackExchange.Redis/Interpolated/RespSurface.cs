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

    /// <summary>EXPERIMENTAL SPIKE. Reply handlers for the prototype command surface.</summary>
    [Experimental(Experiments.InterpolatedWriter, UrlFormat = Experiments.UrlFormat)]
    public static class RespHandlers
    {
        /// <summary>Reads a bulk string reply as a <see cref="RedisValue"/>; null stays null.</summary>
        public static IRespHandler<RedisValue> Value { get; } = new ValueHandler();

        /// <summary>Reads a reply as a boolean, in any of the spellings the server uses for one.</summary>
        /// <remarks>
        /// <b>One handler, not two.</b> <c>SET</c> replies <c>+OK</c>, <c>SETBIT</c> replies <c>:0</c>/<c>:1</c>,
        /// RESP3 has <c>#t</c>/<c>#f</c>, and a conditional write that did not happen replies nil -
        /// four wire shapes for the same question. <see cref="RespReader.ReadBoolean"/> already accepts
        /// the first three (its two-byte simple-string case IS the <c>IsOK</c> compare), so the only thing
        /// left to decide here is that nil means "no", which is what it means everywhere it appears.
        /// A separate OK-only handler would buy one branch and cost every caller a decision.
        /// </remarks>
        public static IRespHandler<bool> Boolean { get; } = new BooleanHandler();

        /// <summary>Reads an integer reply.</summary>
        public static IRespHandler<long> Int64 { get; } = new Int64Handler();

        /// <summary>Reads an integer reply that may be nil, as <c>BITFIELD</c>'s overflow case is.</summary>
        public static IRespHandler<long?> NullableInt64 { get; } = new NullableInt64Handler();

        /// <summary>Reads a floating-point reply; RESP2 sends these as bulk strings.</summary>
        public static IRespHandler<double> Double { get; } = new DoubleHandler();

        /// <summary>Reads an array reply as <see cref="RedisValue"/>s; a nil array reads as empty.</summary>
        public static IRespHandler<RedisValue[]> Values { get; } = new ValuesHandler();

        /// <summary>Reads a bulk string reply as a <see cref="string"/>; null stays null.</summary>
        public static IRespHandler<string?> String { get; } = new StringHandler();

        /// <summary>Reads a bulk string reply as a <see cref="Lease{T}"/>; null stays null.</summary>
        /// <remarks>
        /// The lease always <b>copies</b> here, where the same read against a live reply may instead point
        /// into the reply's buffer: a handler is handed a span whose lifetime ends when it returns, so
        /// there is nothing to share. See <see cref="RespReaderExtensions.ReadLease"/>.
        /// </remarks>
        public static IRespHandler<Lease<byte>?> Lease { get; } = new LeaseHandler();

        /// <summary>The reply as a read-only buffer; shares the underlying memory where it can.</summary>
        public static IRespHandler<ReadOnlyLease<byte>?> ReadOnlyLease { get; } = new ReadOnlyLeaseHandler();

        /// <summary>The whole reply, undecoded - the general-purpose answer for commands we do not model.</summary>
        public static IRespHandler<RespResult> Result { get; } = new RespResultHandler();

        /// <summary>Reads a one-element array reply as the single value it wraps.</summary>
        /// <remarks>
        /// The <c>FIELDS n</c> commands always reply with an array, one element per field - so asking for
        /// exactly one field still gets <c>*1</c>. This is the shape that turns that back into the single
        /// value the caller asked for. It cannot be the built-in handler for
        /// <see cref="RedisValue"/> (that one reads a scalar), which is why it is named.
        /// </remarks>
        public static IRespHandler<RedisValue> SingletonValue { get; } = new SingletonValueHandler();

        /// <inheritdoc cref="SingletonValue"/>
        public static IRespHandler<Lease<byte>?> SingletonLease { get; } = new SingletonLeaseHandler();

        /// <summary>Checks the reply for a server error, and reads nothing else.</summary>
        /// <remarks>
        /// What a command with no result still has to do. Without it a failed command would complete
        /// quietly, because there would be no value whose absence gave the game away - the error is the
        /// <i>only</i> thing such a call can report.
        /// </remarks>
        public static IRespHandler<bool> Success { get; } = new SuccessHandler();

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

            private static IRespHandler<T>? Resolve()
            {
                object? handler = null;
                if (typeof(T) == typeof(RedisValue)) handler = Value;
                else if (typeof(T) == typeof(bool)) handler = Boolean;
                else if (typeof(T) == typeof(long)) handler = Int64;
                else if (typeof(T) == typeof(long?)) handler = NullableInt64;
                else if (typeof(T) == typeof(double)) handler = Double;
                else if (typeof(T) == typeof(RedisValue[])) handler = Values;
                else if (typeof(T) == typeof(string)) handler = String;
                else if (typeof(T) == typeof(Lease<byte>)) handler = Lease;
                else if (typeof(T) == typeof(ReadOnlyLease<byte>)) handler = ReadOnlyLease;
                else if (typeof(T) == typeof(RespResult)) handler = Result;

                // Below this line: shapes that belong to ONE command. They are registered so a command
                // body stays one expression, but they are not exposed as named properties - a handler
                // nobody can reuse is not part of a vocabulary, and RespHandlers is the vocabulary. If a
                // shape ever earns a second caller, promoting it is a one-line change.
                else if (typeof(T) == typeof(ValueCondition?)) handler = s_digest;
                else if (typeof(T) == typeof(LCSMatchResult)) handler = s_lcsMatch;
                else if (typeof(T) == typeof(StringIncrementResult<long>)) handler = s_incrementInt64;
                else if (typeof(T) == typeof(StringIncrementResult<double>)) handler = s_incrementDouble;
                else if (typeof(T) == typeof(Lease<long?>)) handler = s_nullableInt64Lease;
                else if (typeof(T) == typeof(HashEntry[])) handler = s_hashEntries;
                else if (typeof(T) == typeof(long[])) handler = s_int64Array;
                else if (typeof(T) == typeof(ExpireResult[])) handler = s_expireResults;
                else if (typeof(T) == typeof(PersistResult[])) handler = s_persistResults;
                else if (typeof(T) == typeof(bool[])) handler = s_booleans;
                else if (typeof(T) == typeof(double?)) handler = s_nullableDouble;
                else if (typeof(T) == typeof(double?[])) handler = s_nullableDoubles;
                else if (typeof(T) == typeof(SortedSetEntry[])) handler = s_sortedSetEntries;
                else if (typeof(T) == typeof(SortedSetEntry?)) handler = s_sortedSetEntry;
                else if (typeof(T) == typeof(SortedSetPopResult)) handler = s_sortedSetPop;
                return (IRespHandler<T>?)handler;
            }
        }

        private sealed class ValueHandler : IRespHandler<RedisValue>
        {
            public RedisValue Parse(ReadOnlySpan<byte> response)
            {
                var reader = new RespReader(response);
                reader.MoveNext();
                return reader.IsNull ? RedisValue.Null : reader.ReadRedisValue();
            }
        }

        private sealed class SuccessHandler : IRespHandler<bool>
        {
            public bool Parse(ReadOnlySpan<byte> response)
            {
                var reader = new RespReader(response);
                reader.MoveNext(); // skips attributes, and throws RespException on an error element
                return true;
            }
        }

        private sealed class BooleanHandler : IRespHandler<bool>
        {
            public bool Parse(ReadOnlySpan<byte> response)
            {
                var reader = new RespReader(response);
                reader.MoveNext();

                // nil is not a failure here, and this is the one place that has to say so: a SET under
                // NX/XX that did not write, a GETEX on a missing key - the command worked, the answer is no
                return !reader.IsNull && reader.ReadBoolean();
            }
        }

        private sealed class Int64Handler : IRespHandler<long>
        {
            public long Parse(ReadOnlySpan<byte> response)
            {
                var reader = new RespReader(response);
                reader.MoveNext();
                return reader.ReadInt64();
            }
        }

        private sealed class NullableInt64Handler : IRespHandler<long?>
        {
            public long? Parse(ReadOnlySpan<byte> response)
            {
                var reader = new RespReader(response);
                reader.MoveNext();

                // a single-operation BITFIELD still replies with an array; unwrap a unit one, as the
                // MessageWriter path's NullableInt64Processor does, so the caller sees one value
                if (reader.IsAggregate) reader.MoveNext();

                return reader.IsNull ? null : reader.ReadInt64();
            }
        }

        private sealed class DoubleHandler : IRespHandler<double>
        {
            public double Parse(ReadOnlySpan<byte> response)
            {
                var reader = new RespReader(response);
                reader.MoveNext();
                return reader.ReadDouble();
            }
        }

        private sealed class ValuesHandler : IRespHandler<RedisValue[]>
        {
            public RedisValue[] Parse(ReadOnlySpan<byte> response)
            {
                var reader = new RespReader(response);
                reader.MoveNext();

                // a nil array - which MGET does not send, but a RESP3 server may for an empty aggregate -
                // reads as empty rather than null, because every caller of an array reply wants to iterate it
                return reader.ReadPastRedisValues() ?? Array.Empty<RedisValue>();
            }
        }

        private sealed class StringHandler : IRespHandler<string?>
        {
            public string? Parse(ReadOnlySpan<byte> response)
            {
                var reader = new RespReader(response);
                reader.MoveNext();
                return reader.IsNull ? null : reader.ReadString();
            }
        }

        /// <summary>
        /// Captures the whole reply, undecoded, as a <see cref="RespResult"/>.
        /// </summary>
        /// <remarks>
        /// <para>
        /// The general-purpose answer, and the one that matters most for <i>other people's</i> commands: a
        /// library like NRedisStack reaches the server through the escape hatch and wants the reply, not a
        /// decoded shape this library happens to know. Registering it here means every such command gets
        /// the new surface without anyone enumerating commands - the plumbing lights up once.
        /// </para>
        /// <para>
        /// Note it is not the same as "cacheable": whether a given command's reply <i>may</i> be cached is
        /// still per-command, and the server does not track the <c>FT.*</c> family for invalidation at all.
        /// What generalises is the mechanism.
        /// </para>
        /// <para>
        /// <b>This copies today, and that is not yet avoidable here.</b> Sharing the buffer needs the reader
        /// to know which buffer the bytes live in - <c>RespResult.Read</c> passes it as a reader service for
        /// exactly that reason - and a <see cref="ReadOnlySpan{T}"/> parameter cannot carry it. See design
        /// notes 6.16.
        /// </para>
        /// </remarks>
        private sealed class RespResultHandler : IRespPayloadHandler<RespResult>
        {
            /// <summary>
            /// Share the reply's buffer rather than copying it - one more reference, not a second copy.
            /// </summary>
            /// <remarks>
            /// The whole reason <c>RespResult</c> is the general-purpose result type: it exposes only
            /// readers, so nothing can write through it, which is what makes sharing memory that is still
            /// owned elsewhere safe. Falls back to a copy if the buffer has already gone - losing that race
            /// means it is on its way back to the pool, and resurrecting it is exactly what must not happen.
            /// </remarks>
            public RespResult Parse(RespPayload payload)
                => payload.ShareAsResult() ?? RespResult.Capture(payload.Span);

            /// <summary>The copying path, for a caller who only has the bytes.</summary>
            public RespResult Parse(ReadOnlySpan<byte> response) => RespResult.Capture(response);
        }

        /// <summary>The reply as a buffer the caller owns outright, and may write to.</summary>
        /// <remarks>
        /// Copies, necessarily: a mutable lease must not point at memory anything else can read. The
        /// <see cref="ReadOnlyLease{T}"/> sibling is the one that can share. See design notes 6.16.
        /// </remarks>
        private sealed class LeaseHandler : IRespHandler<Lease<byte>?>
        {
            public Lease<byte>? Parse(ReadOnlySpan<byte> response)
            {
                var reader = new RespReader(response);
                reader.MoveNext();
#pragma warning disable CS0618 // Type or member is obsolete - the copying form is what this contract needs
                return RespReaderExtensions.ReadLease(in reader);
#pragma warning restore CS0618
            }
        }

        /// <summary>The reply as a read-only buffer, which may share rather than copy.</summary>
        private sealed class ReadOnlyLeaseHandler : IRespHandler<ReadOnlyLease<byte>?>
        {
            public ReadOnlyLease<byte>? Parse(ReadOnlySpan<byte> response)
            {
                var reader = new RespReader(response);
                reader.MoveNext();
                return reader.ReadLease();
            }
        }

        // ---- one-command shapes; reachable through Inbuilt<T>, deliberately not named above ----
        private static readonly IRespHandler<ValueCondition?> s_digest = new DigestHandler();
        private static readonly IRespHandler<LCSMatchResult> s_lcsMatch = new LCSMatchHandler();
        private static readonly IRespHandler<StringIncrementResult<long>> s_incrementInt64 = new IncrementInt64Handler();
        private static readonly IRespHandler<StringIncrementResult<double>> s_incrementDouble = new IncrementDoubleHandler();
        private static readonly IRespHandler<Lease<long?>> s_nullableInt64Lease = new NullableInt64LeaseHandler();
        private static readonly IRespHandler<HashEntry[]> s_hashEntries = new HashEntryHandler();
        private static readonly IRespHandler<long[]> s_int64Array = new Int64ArrayHandler();
        private static readonly IRespHandler<ExpireResult[]> s_expireResults = new ExpireResultHandler();
        private static readonly IRespHandler<PersistResult[]> s_persistResults = new PersistResultHandler();
        private static readonly IRespHandler<bool[]> s_booleans = new BooleanArrayHandler();
        private static readonly IRespHandler<double?> s_nullableDouble = new NullableDoubleHandler();
        private static readonly IRespHandler<double?[]> s_nullableDoubles = new NullableDoubleArrayHandler();
        private static readonly IRespHandler<SortedSetEntry[]> s_sortedSetEntries = new SortedSetEntryArrayHandler();
        private static readonly IRespHandler<SortedSetEntry?> s_sortedSetEntry = new SortedSetEntryHandler();
        private static readonly IRespHandler<SortedSetPopResult> s_sortedSetPop = new SortedSetPopHandler();

        private sealed class DigestHandler : IRespHandler<ValueCondition?>
        {
            public ValueCondition? Parse(ReadOnlySpan<byte> response)
            {
                var reader = new RespReader(response);
                reader.MoveNext();
                return ValueCondition.TryReadDigest(in reader, out var digest)
                    ? digest
                    : throw new RespException("Unexpected DIGEST reply.");
            }
        }

        private sealed class LCSMatchHandler : IRespHandler<LCSMatchResult>
        {
            public LCSMatchResult Parse(ReadOnlySpan<byte> response)
            {
                var reader = new RespReader(response);
                reader.MoveNext();
                return LCSMatchResult.TryRead(ref reader, out var result)
                    ? result
                    : throw new RespException("Unexpected LCS IDX reply.");
            }
        }

        private sealed class IncrementInt64Handler : IRespHandler<StringIncrementResult<long>>
        {
            public StringIncrementResult<long> Parse(ReadOnlySpan<byte> response)
            {
                // [value, applied-increment]; under a bound the second is not the one that was asked for
                var reader = new RespReader(response);
                reader.MoveNext();
                if (reader.IsAggregate
                    && reader.TryMoveNext() && reader.IsScalar && reader.TryReadInt64(out var value)
                    && reader.TryMoveNext() && reader.IsScalar && reader.TryReadInt64(out var applied))
                {
                    return new StringIncrementResult<long>(value, applied);
                }

                throw new RespException("Unexpected INCREX reply.");
            }
        }

        private sealed class NullableInt64LeaseHandler : IRespHandler<Lease<long?>>
        {
            public Lease<long?> Parse(ReadOnlySpan<byte> response)
            {
                // BITFIELD's reply: a flat array with one element per sub-operation, nil where
                // OVERFLOW FAIL skipped one
                var reader = new RespReader(response);
                reader.MoveNext();
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
                        target[i] = reader.IsNull ? null : reader.ReadInt64();
                    }
                }
                catch
                {
                    lease.Dispose();
                    throw;
                }

                return lease;
            }
        }

        private sealed class SingletonValueHandler : IRespHandler<RedisValue>
        {
            public RedisValue Parse(ReadOnlySpan<byte> response)
            {
                var reader = new RespReader(response);
                reader.MoveNext();
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
            public Lease<byte>? Parse(ReadOnlySpan<byte> response)
            {
                var reader = new RespReader(response);
                reader.MoveNext();
                if (reader.IsNull) return null;
                reader.MoveNext();
#pragma warning disable CS0618 // the copying form is what this contract needs; see the remarks
                return RespReaderExtensions.ReadLease(in reader);
#pragma warning restore CS0618
            }
        }

        private sealed class HashEntryHandler : IRespHandler<HashEntry[]>
        {
            // RESP2 sends name/value interleaved and RESP3 may send them jagged; the existing processor
            // already decides between them from the CONTENT rather than from the negotiated protocol, so
            // reusing it is both less code and the only way the two readers cannot disagree. Resp3 is
            // passed to enable that detection, not to assert anything about the connection.
            private static readonly ResultProcessor.HashEntryArrayProcessor Shape = new();

            public HashEntry[] Parse(ReadOnlySpan<byte> response)
            {
                var reader = new RespReader(response);
                reader.MoveNext();
                return Shape.ParseArray(ref reader, RedisProtocol.Resp3, allowOversized: false, out _, state: null)
                       ?? Array.Empty<HashEntry>();
            }
        }

        private sealed class Int64ArrayHandler : IRespHandler<long[]>
        {
            public long[] Parse(ReadOnlySpan<byte> response)
            {
                var reader = new RespReader(response);
                reader.MoveNext();
                return reader.ReadPastArray(static (ref r) => r.ReadInt64(), scalar: true) ?? Array.Empty<long>();
            }
        }

        private sealed class ExpireResultHandler : IRespHandler<ExpireResult[]>
        {
            public ExpireResult[] Parse(ReadOnlySpan<byte> response)
            {
                var reader = new RespReader(response);
                reader.MoveNext();
                return reader.ReadPastArray(static (ref r) => (ExpireResult)r.ReadInt64(), scalar: true)
                       ?? Array.Empty<ExpireResult>();
            }
        }

        private sealed class NullableDoubleHandler : IRespHandler<double?>
        {
            public double? Parse(ReadOnlySpan<byte> response)
            {
                var reader = new RespReader(response);
                reader.MoveNext();
                return reader.IsNull ? null : reader.ReadDouble();
            }
        }

        private sealed class NullableDoubleArrayHandler : IRespHandler<double?[]>
        {
            public double?[] Parse(ReadOnlySpan<byte> response)
            {
                // ZMSCORE replies nil for a member that is not there, so the element type has to be nullable
                var reader = new RespReader(response);
                reader.MoveNext();
                return reader.ReadPastArray(static (ref r) => r.IsNull ? (double?)null : r.ReadDouble(), scalar: true)
                       ?? Array.Empty<double?>();
            }
        }

        private sealed class SortedSetEntryArrayHandler : IRespHandler<SortedSetEntry[]>
        {
            // as HashEntryHandler: interleaved in RESP2, possibly jagged in RESP3, decided from the content
            private static readonly ResultProcessor.SortedSetEntryArrayProcessor Shape = new();

            public SortedSetEntry[] Parse(ReadOnlySpan<byte> response)
            {
                var reader = new RespReader(response);
                reader.MoveNext();
                return Shape.ParseArray(ref reader, RedisProtocol.Resp3, allowOversized: false, out _, state: null)
                       ?? Array.Empty<SortedSetEntry>();
            }
        }

        private sealed class SortedSetEntryHandler : IRespHandler<SortedSetEntry?>
        {
            public SortedSetEntry? Parse(ReadOnlySpan<byte> response)
            {
                var reader = new RespReader(response);
                reader.MoveNext();
                return SortedSetEntry.TryRead(ref reader, out var result)
                    ? result
                    : throw new RespException("Unexpected sorted-set pop reply.");
            }
        }

        private sealed class SortedSetPopHandler : IRespHandler<SortedSetPopResult>
        {
            public SortedSetPopResult Parse(ReadOnlySpan<byte> response)
            {
                var reader = new RespReader(response);
                reader.MoveNext();
                return SortedSetPopResult.TryRead(ref reader, out var result)
                    ? result
                    : throw new RespException("Unexpected ZMPOP reply.");
            }
        }

        private sealed class BooleanArrayHandler : IRespHandler<bool[]>
        {
            public bool[] Parse(ReadOnlySpan<byte> response)
            {
                var reader = new RespReader(response);
                reader.MoveNext();
                return reader.ReadPastArray(static (ref r) => r.ReadBoolean(), scalar: true) ?? Array.Empty<bool>();
            }
        }

        private sealed class PersistResultHandler : IRespHandler<PersistResult[]>
        {
            public PersistResult[] Parse(ReadOnlySpan<byte> response)
            {
                var reader = new RespReader(response);
                reader.MoveNext();
                return reader.ReadPastArray(static (ref r) => (PersistResult)r.ReadInt64(), scalar: true)
                       ?? Array.Empty<PersistResult>();
            }
        }

        private sealed class IncrementDoubleHandler : IRespHandler<StringIncrementResult<double>>
        {
            public StringIncrementResult<double> Parse(ReadOnlySpan<byte> response)
            {
                var reader = new RespReader(response);
                reader.MoveNext();
                if (reader.IsAggregate
                    && reader.TryMoveNext() && reader.IsScalar && reader.TryReadDouble(out var value)
                    && reader.TryMoveNext() && reader.IsScalar && reader.TryReadDouble(out var applied))
                {
                    return new StringIncrementResult<double>(value, applied);
                }

                throw new RespException("Unexpected INCREX reply.");
            }
        }
    }

    /// <summary>
    /// EXPERIMENTAL SPIKE. The command surface, as extension members.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is the shape the whole design exists to enable: <c>ctx.Strings.Set(key, value)</c> reads like a
    /// built-in method, groups the surface the way Redis documents itself, and is reachable by any library -
    /// including one that is not this one - without a wrapper interface or a forked surface.
    /// </para>
    /// <para>
    /// One partial file per command group - <c>RespSurface.Strings.cs</c>, and so on - matching how Redis
    /// documents itself, and how <c>RedisDatabase</c>'s ~6k lines would have liked to be split.
    /// </para>
    /// </remarks>
    [Experimental(Experiments.InterpolatedWriter, UrlFormat = Experiments.UrlFormat)]

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
