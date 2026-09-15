using System;
using System.Buffers;
using System.Diagnostics.CodeAnalysis;
using RESPite;
using RESPite.Messages;

namespace StackExchange.Redis.Interpolated
{
    /// <summary>
    /// EXPERIMENTAL SPIKE. One scalar reply value, as a window over somebody else's buffer.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>What <see cref="RedisValue"/> could not be.</b> The two have the same shape - a window
    /// <c>(owner, offset, length)</c>, sixteen bytes - and differ in two things. This one holds the RESP
    /// <i>frame</i> rather than the decoded payload, which is what lets a streamed value be represented at
    /// all; and it <i>borrows</i> rather than owns, which is what lets a whole reply's worth of values
    /// share one pooled buffer instead of allocating one array each.
    /// </para>
    /// <para>
    /// <b>Uncounted, deliberately.</b> A struct copy cannot increment a reference count, so counting per
    /// value would be a leak or a double-free waiting to happen. The owner - a lease, a payload, a result -
    /// holds the single reference, and these are views valid for as long as it is. That is the contract
    /// <see cref="ReadOnlyLease{T}.Span"/> already has, so there is no new rule: one <c>using</c> on the
    /// thing that owns the buffer, none on the values inside it.
    /// </para>
    /// <para>
    /// <b>Scalar by construction.</b> Not a narrower <see cref="RespResult"/>: that is "whatever came
    /// back", errors and pushes included, and this is "a value you asked for". The invariant is what makes
    /// <see cref="AsInt64"/> and friends total rather than partial, and it is enforced where the value is
    /// captured rather than tested in every accessor. Scalar means the family the reader means by
    /// <c>IsScalar</c> - bulk, simple, integer, double, boolean, big number, verbatim - not one prefix.
    /// </para>
    /// <para>
    /// <b>Everything is delegated.</b> Every accessor is a reader pointed at this window, so there is no
    /// second copy of the parse rules, the coercions, or - the part most likely to be got wrong - the
    /// streamed-scalar chunk walk. Equality goes the same way, so <c>:1</c> and <c>$1\r\n1</c> compare
    /// equal without a normalisation rule of its own.
    /// </para>
    /// </remarks>
    [Experimental(Experiments.InterpolatedWriter, UrlFormat = Experiments.UrlFormat)]
    public readonly struct RespValue : IEquatable<RespValue>
    {
        /// <summary>
        /// Whoever the bytes belong to: a <see cref="ReadOnlyLease{T}"/>, an <see cref="IMemoryOwner{T}"/>,
        /// a bare array, or <see langword="null"/> for a value with no bytes at all.
        /// </summary>
        /// <remarks>
        /// Holding the <i>owner</i> rather than a <see cref="ReadOnlyMemory{T}"/> is what keeps the
        /// disposal hardening: a lease nulls its buffer when it goes back to the pool, so reading a stale
        /// value throws instead of quietly returning recycled bytes.
        /// </remarks>
        private readonly object? _owner;

        private readonly int _start;
        private readonly int _length;

        private RespValue(object? owner, int start, int length, bool isNull)
        {
            _owner = owner;
            _start = start;
            _length = length;
            IsNull = isNull;
        }

        /// <summary>A value that is not there: the server's nil, however it spelled it.</summary>
        /// <remarks>
        /// RESP spells absence three ways - <c>_</c>, <c>$-1</c> and <c>*-1</c> - and they mean the same
        /// thing to a caller, so this reports all three alike, as <see cref="RedisValue.Null"/> does.
        /// </remarks>
        public static RespValue Null => default;

        /// <summary>Whether this is the server's nil rather than a value.</summary>
        public bool IsNull { get; }

        /// <summary>Whether this is a value at all, as opposed to nil.</summary>
        public bool HasValue => !IsNull;

        /// <summary>
        /// Move to the next element and capture it, which must be a scalar.
        /// </summary>
        /// <param name="owner">Who the bytes belong to; the captured value is valid for as long as it is.</param>
        /// <param name="reader">The reader, positioned before the element to capture.</param>
        /// <param name="value">The captured value, when this returns <see langword="true"/>.</param>
        /// <exception cref="InvalidOperationException">If the element is not a scalar.</exception>
        /// <remarks>
        /// <para>
        /// The reader knows <i>where</i> but not <i>whose</i>: it works from spans, and a span carries no
        /// origin. So capture takes the owner from the caller - the lease or payload the reader was built
        /// over - and the extent from the reader.
        /// </para>
        /// <para>
        /// <b>The extent needs nothing new.</b> <c>BytesConsumed</c> sampled either side of the move is
        /// exactly the element's window, because the reader counts to the <i>end</i> of what it just read.
        /// <c>RespValueTests.TheReaderBracketsEachElement</c> pins that, because everything here
        /// rests on it and nothing else would notice if it changed.
        /// </para>
        /// </remarks>
        internal static bool TryCaptureNext(object? owner, scoped ref RespReader reader, out RespValue value)
        {
            var start = checked((int)reader.BytesConsumed);
            if (!reader.TryMoveNext())
            {
                value = default;
                return false;
            }

            reader.DemandScalar();
            var end = checked((int)reader.BytesConsumed);
            value = new RespValue(owner, start, end - start, reader.IsNull);
            return true;
        }

        /// <summary>The bytes of this value's frame, as the owner still holds them.</summary>
        /// <exception cref="ObjectDisposedException">If the owner has already given its buffer back.</exception>
        public ReadOnlySpan<byte> Frame => _owner switch
        {
            null => default,
            ReadOnlyLease<byte> lease => lease.Span.Slice(_start, _length),
            IMemoryOwner<byte> owner => owner.Memory.Span.Slice(_start, _length),
            _ => new ReadOnlySpan<byte>((byte[])_owner, _start, _length),
        };

        /// <summary>Read this value as a 64-bit integer.</summary>
        public long AsInt64() => Reader().ReadInt64();

        /// <summary>Read this value as a 32-bit integer.</summary>
        public int AsInt32() => Reader().ReadInt32();

        /// <summary>Read this value as a double.</summary>
        public double AsDouble() => Reader().ReadDouble();

        /// <summary>Read this value as a boolean.</summary>
        /// <remarks>
        /// The reader's rule, which is stricter than <see cref="RedisValue"/>'s: <c>:0</c>/<c>:1</c>,
        /// <c>#f</c>/<c>#t</c> and <c>+OK</c> are booleans; a <i>bulk</i> <c>"1"</c> is not, because no
        /// server answers a boolean that way. Casting a <see cref="RedisValue"/> would have coerced it.
        /// </remarks>
        public bool AsBoolean() => Reader().ReadBoolean();

        /// <summary>Read this value as text, or <see langword="null"/> if it is nil.</summary>
        public string? AsString() => Reader().ReadString();

        /// <summary>
        /// Copy this value out into something that owns its own bytes and can outlive the buffer.
        /// </summary>
        /// <remarks>
        /// The deliberate exit from borrowing, and the bridge to the old surface: <see cref="IDatabase"/>
        /// hands out <see cref="RedisValue"/> and always will, so the adapters pay one copy here.
        /// </remarks>
        public RedisValue ToRedisValue() => Reader().ReadRedisValue();

        /// <summary>
        /// The value's payload as one contiguous run, when it is one.
        /// </summary>
        /// <param name="payload">The bytes, when this returns <see langword="true"/>.</param>
        /// <remarks>
        /// <para>
        /// <see langword="false"/> for a streamed value, whose payload arrives in chunks and so exists
        /// nowhere as a single run - use <see cref="CopyTo(Span{byte})"/> for those. That is what the
        /// <c>Try</c> is for, and the only thing it is for.
        /// </para>
        /// <para>
        /// Also <see langword="false"/> for nil. The reader itself says <see langword="true"/> with an
        /// empty span there, which would read a missing value as a present empty one - the same trap
        /// <see cref="RedisValue.IsNull"/> exists to avoid, one layer down.
        /// </para>
        /// </remarks>
        public bool TryGetSpan(out ReadOnlySpan<byte> payload)
        {
            var reader = Reader();
            if (reader.IsNull || reader.IsStreaming)
            {
                payload = default;
                return false;
            }

            return reader.TryGetSpan(out payload);
        }

        /// <summary>Copy this value's payload out, joining the chunks of a streamed value.</summary>
        /// <param name="target">Where to write the payload; <see cref="Length"/> says how much room to leave.</param>
        /// <returns>How many bytes were written.</returns>
        public int CopyTo(scoped Span<byte> target) => Reader().CopyTo(target);

        /// <summary>How many bytes this value's payload holds, however it arrived.</summary>
        public int Length => IsNull ? 0 : Reader().ScalarLength();

        /// <inheritdoc/>
        /// <remarks>
        /// Through the reader, as everything else is - so <c>:1</c> and <c>$1\r\n1</c> are equal, which is
        /// what <see cref="RedisValue"/> promises and what anyone using these as dictionary keys assumes.
        /// Comparing frame bytes would say otherwise, quietly.
        /// </remarks>
        public bool Equals(RespValue other) => ToRedisValue().Equals(other.ToRedisValue());

        /// <inheritdoc/>
        public override bool Equals(object? obj) => obj is RespValue other && Equals(other);

        /// <inheritdoc/>
        public override int GetHashCode() => ToRedisValue().GetHashCode();

        /// <inheritdoc/>
        public override string ToString() => AsString() ?? "(nil)";

        private RespReader Reader()
        {
            var reader = new RespReader(Frame);
            reader.MoveNext();
            return reader;
        }
    }
}
