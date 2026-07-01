using System;
using System.Buffers;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using RESPite.Messages;

namespace StackExchange.Redis;

internal static class RespReaderExtensions
{
    extension(in RespReader reader)
    {
        public RedisValue ReadRedisValue()
        {
            reader.DemandScalar();
            if (reader.IsNull) return RedisValue.Null;

            switch (reader.Prefix)
            {
                case RespPrefix.Boolean:
                    return reader.ReadBoolean();
                case RespPrefix.Integer:
                    return reader.ReadInt64();
            }

            // bulk/simple/verbatim string. Only inline (non-streaming) scalars get the compact storage
            // kinds; streaming scalars fall through to ReadByteArray.
            if (reader.IsInlineScalar)
            {
                var length = reader.ScalarLength();

                // Short payloads (<= 8 bytes) pack inline as a short-blob: allocation-free, and with *no*
                // eager numeric parse - any later (long)/(double)/etc. is deferred to the caller (Simplify
                // on demand), which is cheaper for the common case of values never interpreted as numbers.
                // Contiguous data (the common case) is taken straight from TryGetSpan - no stackalloc. Only a
                // scalar that straddles segments needs linearizing into the 8-byte stack buffer; the length
                // guard is what makes that fixed buffer safe, since Buffer() silently truncates an over-long
                // discontiguous payload.
                if (length <= RedisValue.MaxInlineBytes)
                {
                    return RedisValue.FromRaw(reader.TryGetSpan(out var buffer) ?
                        buffer : reader.Buffer(stackalloc byte[RedisValue.MaxInlineBytes]));
                }

                // Longer payloads: prefer a compact numeric storage kind when the text is the *canonical*
                // representation of that number, so every projection (ToString, (byte[]), equality, hash)
                // still round-trips byte-for-byte; this also avoids the byte[] alloc. Canonical parsing needs
                // a contiguous span, so a discontiguous payload falls through to ReadByteArray.
                if (reader.TryGetSpan(out var span) && TryReadCanonicalNumber(span, out var number))
                {
                    return number;
                }
            }
            return reader.ReadByteArray();
        }

        public string DebugReadTruncatedString(int maxChars)
        {
            if (!reader.IsScalar) return "";
            try
            {
                var s = reader.ReadString() ?? "";
                return s.Length <= maxChars ? s : s.Substring(0, maxChars) + "...";
            }
            catch
            {
                return "";
            }
        }

        public RedisKey ReadRedisKey() => (RedisKey)reader.ReadByteArray();

        public RedisChannel ReadRedisChannel(RedisChannel.RedisChannelOptions options)
            => new(reader.ReadByteArray(), options);

        private bool TryGetFirst(out string first)
        {
            if (reader.IsNonNullAggregate && !reader.AggregateIsEmpty())
            {
                var clone = reader.Clone();
                if (clone.TryMoveNext())
                {
                    unsafe
                    {
                        if (clone.IsScalar &&
                            clone.TryParseScalar(&PhysicalConnection.PushKindMetadata.TryParse, out PhysicalConnection.PushKind kind))
                        {
                            first = kind.ToString();
                            return true;
                        }
                    }

                    first = clone.GetOverview();
                    return true;
                }
            }
            first = "";
            return false;
        }

        public string GetOverview()
        {
            // return reader.BufferUtf8(); // <== for when you really can't grok what is happening
            if (reader.Prefix is RespPrefix.None)
            {
                var copy = reader;
                copy.MovePastBof();
                return copy.Prefix is RespPrefix.None ? "(empty)" : copy.GetOverview();
            }
            if (reader.IsNull) return "(null)";

            return reader.Prefix switch
            {
                RespPrefix.SimpleString or RespPrefix.Integer or RespPrefix.SimpleError or RespPrefix.Double => $"{reader.Prefix}: {reader.ReadString()}",
                RespPrefix.Push when reader.TryGetFirst(out var first) => $"{reader.Prefix} ({first}): {reader.AggregateLength()} items",
                _ when reader.IsScalar => $"{reader.Prefix}: {reader.ScalarLength()} bytes, '{reader.DebugReadTruncatedString(16)}'",
                _ when reader.IsAggregate => $"{reader.Prefix}: {reader.AggregateLength()} items",
                _ => $"(unknown: {reader.Prefix})",
            };
        }

        public RespPrefix GetFirstPrefix()
        {
            var prefix = reader.Prefix;
            if (prefix is RespPrefix.None)
            {
                var mutable = reader;
                mutable.MovePastBof();
                prefix = mutable.Prefix;
            }
            return prefix;
        }

        /*
        public bool AggregateHasAtLeast(int count)
        {
            reader.DemandAggregate();
            if (reader.IsNull) return false;
            if (reader.IsStreaming) return CheckStreamingAggregateAtLeast(in reader, count);
            return reader.AggregateLength() >= count;

            static bool CheckStreamingAggregateAtLeast(in RespReader reader, int count)
            {
                var iter = reader.AggregateChildren();
                object? attributes = null;
                while (count > 0 && iter.MoveNextRaw(null!, ref attributes))
                {
                    count--;
                }

                return count == 0;
            }
        }
        */
    }

    extension(ref RespReader reader)
    {
        public void MovePastBof()
        {
            // if we're at BOF, read the first element, ignoring errors
            if (reader.Prefix is RespPrefix.None) reader.SafeTryMoveNext();
        }

        public RedisValue[]? ReadPastRedisValues()
            => reader.ReadPastArray(static (ref r) => r.ReadRedisValue(), scalar: true);

        public Lease<byte>? AsLease(PhysicalConnection? connection)
        {
            if (!reader.IsScalar) throw new InvalidCastException("Cannot convert to Lease: " + reader.Prefix);
            if (reader.IsNull) return null;

            var length = reader.ScalarLength();
            if (length == 0) return Lease<byte>.Empty;

            var pool = connection?.BridgeCouldBeNull?.Multiplexer?.RawConfig?.ResponseBufferPool;
            var lease = Lease<byte>.Create(length, pool, clear: false);
            if (reader.TryGetSpan(out var span))
            {
                span.CopyTo(lease.Span);
            }
            else
            {
                var buffer = reader.Buffer(lease.Span);
                Debug.Assert(buffer.Length == length, "buffer length mismatch");
            }
            return lease;
        }
    }

    public static RespPrefix GetRespPrefix(ReadOnlySpan<byte> frame)
    {
        var reader = new RespReader(frame);
        reader.SafeTryMoveNext();
        return reader.Prefix;
    }

    extension(RespPrefix prefix)
    {
        public ResultType ToResultType() => prefix switch
        {
            RespPrefix.Array => ResultType.Array,
            RespPrefix.Attribute => ResultType.Attribute,
            RespPrefix.BigInteger => ResultType.BigInteger,
            RespPrefix.Boolean => ResultType.Boolean,
            RespPrefix.BulkError => ResultType.BlobError,
            RespPrefix.BulkString => ResultType.BulkString,
            RespPrefix.SimpleString => ResultType.SimpleString,
            RespPrefix.Map => ResultType.Map,
            RespPrefix.Set => ResultType.Set,
            RespPrefix.Double => ResultType.Double,
            RespPrefix.Integer => ResultType.Integer,
            RespPrefix.SimpleError => ResultType.Error,
            RespPrefix.Null => ResultType.Null,
            RespPrefix.VerbatimString => ResultType.VerbatimString,
            RespPrefix.Push => ResultType.Push,
            _ => throw new ArgumentOutOfRangeException(nameof(prefix), prefix, null),
        };
    }

    extension<T>(T?[] array) where T : class
    {
        internal bool AnyNull()
        {
            foreach (var el in array)
            {
                if (el is null) return true;
            }

            return false;
        }
    }

#if !NET
    extension(Task task)
    {
        public bool IsCompletedSuccessfully => task.Status is TaskStatus.RanToCompletion;
    }
#endif

    private static readonly int MaxCanonicalLength = Math.Max(Format.MaxInt64TextLen, Format.MaxDoubleTextLen);

    // Recognizes the canonical decimal text of an integer (signed Int64 or, for non-negative values up to
    // ulong.MaxValue, UInt64) or a finite double, returning a numeric-backed RedisValue only when re-formatting
    // the parsed value reproduces the exact input bytes. This keeps the optimization invisible to callers:
    // non-canonical spellings ("01234", "+5", "1.50", "1e3"), values beyond the ulong/double range, and the
    // special inf/nan tokens all return false and are kept as a byte[] payload by the caller.
    private static bool TryReadCanonicalNumber(ReadOnlySpan<byte> span, out RedisValue value)
    {
        static bool Failure(out RedisValue value)
        {
            value = default;
            return false;
        }
        // integer: canonical exactly when the round-trip text length matches (rules out leading zeros, a
        // leading '+', "-0", trailing junk, etc.) - so no need to re-emit and compare bytes
        if (span.IsEmpty | span.Length > MaxCanonicalLength) return Failure(out value);

        // restrict to *just* basic number tokens, tracking the two facts that let us pick a single parse:
        // whether a '-' appeared (so we know whether the integer is signed), and whether a '.'/'e'/'E'
        // appeared (which rules out an integer entirely - only a double can be canonical)
        bool seenNegative = false, seenDotOrExp = false;
        foreach (var b in span)
        {
            switch (b)
            {
                case (byte)'0':
                case (byte)'1':
                case (byte)'2':
                case (byte)'3':
                case (byte)'4':
                case (byte)'5':
                case (byte)'6':
                case (byte)'7':
                case (byte)'8':
                case (byte)'9':
                    break;
                case (byte)'-':
                    seenNegative = true;
                    break;
                case (byte)'.':
                case (byte)'E':
                case (byte)'e':
                    seenDotOrExp = true;
                    break;
                default:
                    return Failure(out value);
            }
        }

        if (!seenDotOrExp)
        {
            // pure integer text. For a non-negative value parse as *unsigned* so the full ulong range is
            // covered; the RedisValue(ulong) ctor demotes to Int64 storage when the value fits, so smaller
            // values still land as Int64 exactly as before. A negative value can only be Int64.
            if (seenNegative)
            {
                if (Format.TryParseInt64(span, out var i64) && Format.MeasureInt64(i64) == span.Length)
                {
                    value = i64;
                    return true;
                }
            }
            else if (Format.TryParseUInt64(span, out var u64) && Format.MeasureUInt64(u64) == span.Length)
            {
                value = u64;
                return true;
            }

            // note that all-digit text which isn't a canonical integer (oversize, leading zero, etc.) can't
            // be a canonical double either - any such value re-formats with an exponent - so we simply fall
            // through to the failure at the bottom rather than attempting a (futile) double parse
        }
        else if (Format.TryParseDouble(span, out var dbl))
        {
            if (dbl == 0.0) dbl = Math.Abs(dbl); // prevent problems with -0 formatting
            Span<byte> formatted = stackalloc byte[Format.MaxDoubleTextLen];
            var len = Format.FormatDouble(dbl, formatted);
            if (formatted.Slice(0, len).SequenceEqual(span))
            {
                value = dbl;
                return true;
            }
        }

        return Failure(out value);
    }
}
