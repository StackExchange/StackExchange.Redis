using System;
using System.Buffers;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text.RegularExpressions;
using RESPite.Messages;

namespace StackExchange.Redis;

/// <summary>
/// The reply parsers the RESP handlers share - what survived of <c>ResultProcessor</c> once the old core went.
/// </summary>
/// <remarks>
/// <b>Moved, not rewritten</b>: every parse here is the one both cores used, lifted off the processor classes it lived
/// on (whose other half - completing a <c>Message</c> - is gone), so the wire rules that took years to settle are
/// the same rules, in one place.
/// </remarks>
internal static partial class RespParsers
{
    /// <summary>Reads an index reply; shared with the context surface's handler.</summary>
    internal static bool TryParseArrayIndex(ref RespReader reader, out RedisArrayIndex index)
    {
        if (reader.IsScalar && !reader.IsNull)
        {
            unsafe
            {
                if (reader.TryParseScalar(&Format.TryParseUInt64, out ulong value))
                {
                    index = new RedisArrayIndex(value);
                    return true;
                }
            }
        }

        index = default;
        return false;
    }

    /// <summary>
    /// Reads an <c>ARINFO</c> reply: a flat map, of which the values we understand are scalars.
    /// </summary>
    /// <remarks>
    /// Shared with the context surface's handler rather than written twice. Two copies of one parse is the
    /// shape that lets the two paths disagree while every test that exercises only one of them passes; the
    /// script pair carried that bug for exactly that reason.
    /// </remarks>
    internal static bool TryParseArrayInfo(ref RespReader reader, out ArrayInfo info)
    {
        if (!reader.IsAggregate || reader.IsNull)
        {
            info = default;
            return false;
        }

        {
            var lease = ArrayPool<KeyValuePair<string, RedisValue>>.Shared.Rent(reader.AggregateLength() / 2);
            int count = 0;
            var iter = reader.AggregateChildren();
            while (iter.MoveNext())
            {
                unsafe
                {
                    // try to parse the field as a known enum, and get the known string for it, otherwise: alloc
                    if (!(iter.Value.TryParseScalar(&ArrayInfoFieldMetadata.TryParse, out ArrayInfoField field)
                          && ArrayInfoFieldMetadata.TryFormat(field, out var key)))
                    {
                        key = iter.Value.ReadString() ?? "";
                    }

                    if (!iter.MoveNext())
                    {
                        break;
                    }

                    try
                    {
                        if (iter.Value.IsScalar)
                        {
                            lease[count++] = new(key, iter.Value.ReadRedisValue());
                        }
                    }
                    catch (Exception ex)
                    {
                        // quietly ignore non-scalar results and other oddities
                        Debug.WriteLine(ex.Message);
                    }
                }
            }

            info = new ArrayInfo(new(lease, 0, count));
            ArrayPool<KeyValuePair<string, RedisValue>>.Shared.Return(lease);
            return true;
        }
    }

    /// <summary>
    /// Reads a reply of field/value pairs - interleaved <c>[k1, v1, k2, v2]</c>, or jagged <c>[[k1, v1], [k2, v2]]</c>,
    /// which RESP3 turned some of these into - into one row per pair.
    /// </summary>
    /// <typeparam name="T">The row type.</typeparam>
    internal abstract class PairParser<T>
    {
        protected virtual bool AllowJaggedPairs(RedisProtocol protocol) => protocol >= RedisProtocol.Resp3;

        /// <summary>Read the pairs, deciding the wire shape from the protocol's policy.</summary>
        public T[]? ParseArray(ref RespReader reader, RedisProtocol protocol, bool allowOversized, out int count, object? state)
            => ParseArray(ref reader, AllowJaggedPairs(protocol), allowOversized, out count, state);

        /// <summary>
        /// Read the pairs, being told outright whether jagged is permitted.
        /// </summary>
        /// <remarks>
        /// The protocol is never anything but a way of asking <see cref="AllowJaggedPairs"/> this
        /// question, so a caller that already knows the answer - or that has no connection to ask
        /// about, as the deferred reply shapes do not - says so directly rather than naming a
        /// protocol version it is not really claiming.
        /// </remarks>
        public T[]? ParseArray(ref RespReader reader, bool allowJagged, bool allowOversized, out int count, object? state)
        {
            if (reader.IsNull)
            {
                count = 0;
                return null;
            }

            // Get the aggregate length first
            count = reader.AggregateLength();
            if (count == 0)
            {
                return [];
            }

            // Whether the bytes ARE jagged is RespReader.IsAllJaggedPairs - shared with the deferred
            // pair window (RespPairAggregate<T>), so the two paths cannot drift about what arrived.
            // Whether jagged is PERMITTED is the caller's policy, and arrives as allowJagged.
            bool isJagged = allowJagged && reader.IsAllJaggedPairs();

            if (isJagged)
            {
                // Jagged format: [[k1, v1], [k2, v2], ...]
                // Count is the number of pairs (outer array length)
                var pairs = allowOversized ? ArrayPool<T>.Shared.Rent(count) : new T[count];
                var iter = reader.AggregateChildren();
                for (int i = 0; i < count; i++)
                {
                    iter.DemandNext();

                    var pairIter = iter.Value.AggregateChildren();
                    pairIter.DemandNext();
                    var first = pairIter.Value;

                    pairIter.DemandNext();
                    var second = pairIter.Value;

                    pairs[i] = Parse(ref first, ref second, state);
                }
                return pairs;
            }
            else
            {
                // Interleaved format: [k1, v1, k2, v2, ...]
                // Count is half the array length (>> 1 discards odd element if present)
                count >>= 1; // divide by 2
                var pairs = allowOversized ? ArrayPool<T>.Shared.Rent(count) : new T[count];
                var iter = reader.AggregateChildren();

                for (int i = 0; i < count; i++)
                {
                    iter.DemandNext();
                    var first = iter.Value;

                    iter.DemandNext();
                    var second = iter.Value;

                    pairs[i] = Parse(ref first, ref second, state);
                }
                return pairs;
            }
        }

        protected abstract T Parse(ref RespReader first, ref RespReader second, object? state);
    }

    /// <summary>Field/value rows of a hash.</summary>
    internal sealed class HashEntryPairs : PairParser<HashEntry>
    {
        protected override HashEntry Parse(ref RespReader first, ref RespReader second, object? state) =>
            new HashEntry(first.ReadRedisValue(), second.ReadRedisValue());
    }

    /// <summary>Member/score rows of a sorted set; an unreadable score is <see cref="double.NaN"/>.</summary>
    internal sealed class SortedSetEntryPairs : PairParser<SortedSetEntry>
    {
        protected override SortedSetEntry Parse(ref RespReader first, ref RespReader second, object? state) =>
            new SortedSetEntry(first.ReadRedisValue(), second.TryReadDouble(out double val) ? val : double.NaN);
    }

    /// <summary>Name/value rows of a stream entry.</summary>
    internal sealed class NameValueEntryPairs : PairParser<NameValueEntry>
    {
        public static readonly NameValueEntryPairs Instance = new();

        private NameValueEntryPairs()
        {
        }

        protected override NameValueEntry Parse(ref RespReader first, ref RespReader second, object? state)
            => new NameValueEntry(first.ReadRedisValue(), second.ReadRedisValue());

        /// <summary>Whether a stream's field pairs may arrive jagged on this protocol.</summary>
        internal bool AllowsJaggedPairs(RedisProtocol protocol) => AllowJaggedPairs(protocol);
    }

    /// <summary>Index/value rows of an array, which may arrive jagged even on RESP2.</summary>
    internal sealed class RedisArrayEntryPairs : PairParser<RedisArrayEntry>
    {
        protected override bool AllowJaggedPairs(RedisProtocol protocol) => true; // i.e. even in RESP2

        protected override RedisArrayEntry Parse(ref RespReader first, ref RespReader second, object? state)
        {
            TryParseArrayIndex(ref first, out RedisArrayIndex index);
            return new RedisArrayEntry(index, second.ReadRedisValue());
        }
    }

    private sealed class StringPairParser : PairParser<KeyValuePair<string, string>>
    {
        protected override KeyValuePair<string, string> Parse(ref RespReader first, ref RespReader second, object? state) =>
            new KeyValuePair<string, string>(first.ReadString()!, second.ReadString()!);
    }

    /// <summary>String/string pairs, as <c>CONFIG GET</c>, <c>SENTINEL MASTER</c> and friends reply.</summary>
    internal static PairParser<KeyValuePair<string, string>> StringPairs { get; } = new StringPairParser();

    /// <summary>A reply of exactly <c>0</c> or <c>1</c>, as <c>EXISTS</c>, <c>HEXISTS</c> and <c>SISMEMBER</c> answer a single key.</summary>
    internal static bool TryParseZeroOrOne(ref RespReader reader, out bool value)
    {
        if (reader.IsScalar && reader.ScalarLengthIs(1))
        {
            var span = reader.TryGetSpan(out var tmp) ? tmp : reader.Buffer(stackalloc byte[8]);
            var byteValue = span[0];
            if (byteValue == (byte)'1')
            {
                value = true;
                return true;
            }
            if (byteValue == (byte)'0')
            {
                value = false;
                return true;
            }
        }
        value = false;
        return false;
    }

    /// <summary>A scalar copied into a small stack buffer, or empty when it does not fit.</summary>
    internal static ReadOnlySpan<byte> StackCopyLengthChecked(scoped in RespReader reader, Span<byte> buffer)
    {
        var len = reader.CopyTo(buffer);
        if (len == buffer.Length && reader.ScalarLength() > len) return default; // too small
        return buffer.Slice(0, len);
    }

    private const int SHA1Length = 40;

    private static readonly Regex Sha1Hex = new Regex("^[0-9a-f]{40}$", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    /// <summary>Whether a script argument is really a SHA1 in hex - the v3 rule for "send it by hash".</summary>
    internal static bool IsSHA1(string? script) => script is not null && script.Length == SHA1Length && Sha1Hex.IsMatch(script);

    /// <summary>The length of a raw SHA1 digest.</summary>
    internal const int Sha1HashLength = 20;
}
