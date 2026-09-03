using System;
using System.Buffers;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using RESPite;
using RESPite.Messages;

namespace StackExchange.Redis;

/// <summary>
/// Provides utility methods for consuming <see cref="RespReader"/> values as additional SE.Redis common types.
/// </summary>
public static class RespReaderExtensions
{
    /// <summary>
    /// Read a scalar value as a <see cref="RedisValue"/>.
    /// </summary>
    public static RedisValue ReadRedisValue(this in RespReader reader)
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

    /// <summary>
    /// Read a scalar value as a <see cref="Lease{T}"/>.
    /// </summary>
    public static Lease<byte>? ReadLease(this in RespReader reader, MemoryPool<byte>? pool = null)
    {
        reader.DemandScalar();
        if (reader.IsNull) return null;

        var length = reader.ScalarLength();
        if (length == 0) return Lease<byte>.Empty;

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

    /// <summary>
    /// Read a scalar value as a <see cref="RedisKey"/>; note that no key-prefix compensation is made.
    /// </summary>
    /// <returns>The key value.</returns>
    public static RedisKey ReadRedisKey(this in RespReader reader) => (RedisKey)reader.ReadByteArray();

    /// <summary>
    /// Read a value - scalar, aggregate, error, or null - as a <see cref="RedisResult"/>.
    /// </summary>
    public static RedisResult ReadRedisResult(this in RespReader reader)
    {
        var mutable = reader.Clone();
        if (!RedisResult.TryCreate(null, ref mutable, out var result))
        {
            throw new InvalidOperationException("Unable to interpret RESP reply as a RedisResult");
        }
        return result;
    }

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
