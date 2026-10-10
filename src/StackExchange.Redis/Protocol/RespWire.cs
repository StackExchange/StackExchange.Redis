using System;
using System.Buffers;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using RESPite.Messages;

namespace StackExchange.Redis;

/// <summary>The byte-level RESP encoding primitives the request builder shares: integers, CRLF, and the argument limit.</summary>
/// <remarks>Moved out of <c>MessageWriter</c>, unchanged, when the old write path was deleted.</remarks>
internal static class RespWire
{
    // there is a <= 1024*1024 max constraint inside redis itself: https://github.com/antirez/redis/blob/6c60526db91e23fb2d666fc52facc9a11780a2a3/src/networking.c#L1024
    internal const int REDIS_MAX_ARGS = 1024 * 1024;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static int WriteCrlf(Span<byte> span, int offset)
    {
        span[offset++] = (byte)'\r';
        span[offset++] = (byte)'\n';
        return offset;
    }

    internal static int WriteRaw(Span<byte> span, long value, bool withLengthPrefix = false, int offset = 0)
    {
        if (value >= 0 && value <= 9)
        {
            if (withLengthPrefix)
            {
                span[offset++] = (byte)'1';
                offset = WriteCrlf(span, offset);
            }

            span[offset++] = (byte)((int)'0' + (int)value);
        }
        else if (value >= 10 && value < 100)
        {
            if (withLengthPrefix)
            {
                span[offset++] = (byte)'2';
                offset = WriteCrlf(span, offset);
            }

            span[offset++] = (byte)((int)'0' + ((int)value / 10));
            span[offset++] = (byte)((int)'0' + ((int)value % 10));
        }
        else if (value >= 100 && value < 1000)
        {
            int v = (int)value;
            int units = v % 10;
            v /= 10;
            int tens = v % 10, hundreds = v / 10;
            if (withLengthPrefix)
            {
                span[offset++] = (byte)'3';
                offset = WriteCrlf(span, offset);
            }

            span[offset++] = (byte)((int)'0' + hundreds);
            span[offset++] = (byte)((int)'0' + tens);
            span[offset++] = (byte)((int)'0' + units);
        }
        else if (value < 0 && value >= -9)
        {
            if (withLengthPrefix)
            {
                span[offset++] = (byte)'2';
                offset = WriteCrlf(span, offset);
            }

            span[offset++] = (byte)'-';
            span[offset++] = (byte)((int)'0' - (int)value);
        }
        else if (value <= -10 && value > -100)
        {
            if (withLengthPrefix)
            {
                span[offset++] = (byte)'3';
                offset = WriteCrlf(span, offset);
            }

            value = -value;
            span[offset++] = (byte)'-';
            span[offset++] = (byte)((int)'0' + ((int)value / 10));
            span[offset++] = (byte)((int)'0' + ((int)value % 10));
        }
        else
        {
            // we're going to write it, but *to the wrong place*
            var availableChunk = span.Slice(offset);
            var formattedLength = Format.FormatInt64(value, availableChunk);
            if (withLengthPrefix)
            {
                // now we know how large the prefix is: write the prefix, then write the value
                var prefixLength = Format.FormatInt32(formattedLength, availableChunk);
                offset += prefixLength;
                offset = WriteCrlf(span, offset);

                availableChunk = span.Slice(offset);
                var finalLength = Format.FormatInt64(value, availableChunk);
                offset += finalLength;
                Debug.Assert(finalLength == formattedLength);
            }
            else
            {
                offset += formattedLength;
            }
        }

        return WriteCrlf(span, offset);
    }

    // the IBufferWriter forms, used to write whole RESP values - by the in-process test server, among others
    [ThreadStatic]
    private static Encoder? s_PerThreadEncoder;

    internal static void WriteBulkString(in RedisValue value, IBufferWriter<byte> writer)
    {
        switch (value.Type)
        {
            case RedisValue.StorageType.Null:
                writer.Write(NullBulkString);
                break;
            case RedisValue.StorageType.Int64:
                WriteUnifiedInt64(writer, value.OverlappedValueInt64);
                break;
            case RedisValue.StorageType.UInt64:
                WriteUnifiedUInt64(writer, value.OverlappedValueUInt64);
                break;
            case RedisValue.StorageType.Double:
                WriteUnifiedDouble(writer, value.OverlappedValueDouble);
                break;
            case RedisValue.StorageType.String:
                WriteUnifiedPrefixedString(writer, null, value.RawString());
                break;
            case RedisValue.StorageType.MemoryManager or RedisValue.StorageType.ByteArray or RedisValue.StorageType.ShortBlob:
                WriteUnifiedSpan(writer, value.UnsafeRawSpan(out _));
                break;
            case RedisValue.StorageType.Sequence:
                WriteUnifiedSequenceIterator(writer, value.RawSequenceIterator());
                break;
            default:
                throw new InvalidOperationException($"Unexpected {value.Type} value: '{value}'");
        }
    }

    internal static void WriteMultiBulkHeader(IBufferWriter<byte> writer, long count)
    {
        WriteCountPrefix(writer, (byte)'*', count);
    }

    internal static void WriteMultiBulkHeader(IBufferWriter<byte> writer, long count, RespPrefix prefix)
    {
        if ((prefix is RespPrefix.Map or RespPrefix.Attribute) & count > 0)
        {
            if ((count & 1) != 0) Throw(prefix, count);
            count >>= 1;
            static void Throw(RespPrefix type, long count) => throw new ArgumentOutOfRangeException(
                paramName: nameof(count),
                message: $"{type} data must be in pairs; got {count}");
        }
        WriteCountPrefix(writer, (byte)prefix, count);
    }

    /// <summary>
    /// Obtain a span of at least <paramref name="length"/> bytes, reporting whether the writer obliged.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>sizeHint</c> is <b>advisory</b>. The "should be at least this size" wording in the docs is left over
    /// from when the parameter was called <c>minSize</c>; the guarantee is one element, and consuming code is
    /// expected to test what it got and fall back. The BCL does exactly that - <see cref="BuffersExtensions"/>
    /// loops and copies in slices rather than demanding one contiguous block - which is why the fallbacks here
    /// hand their bytes to <see cref="BuffersExtensions.Write{T}"/> instead of asking again.
    /// See <see href="https://github.com/CommunityToolkit/dotnet/issues/1208"/>.
    /// </para>
    /// <para>
    /// The point of the looser contract is transports with page limits: they can honour reasonable requests
    /// and refuse excessive ones. <c>CycleBuffer</c> is one - it caps the hint at 1k, sizes a fresh segment
    /// from the committed total rather than from the hint, and, the case with no floor at all, hands back a
    /// dangling recycled segment exactly as it is, whatever length that happens to be. All legitimate; the
    /// callers were wrong.
    /// </para>
    /// <para>
    /// Note that a <c>false</c> result is <b>not</b> side-effect free: <c>GetSpan</c> has already run, and on
    /// <c>CycleBuffer</c> that may have trimmed the active segment and raised <c>PageComplete</c>. That is
    /// fine - the span we then decline is simply left uncommitted, and the subsequent
    /// <see cref="BuffersExtensions.Write{T}"/> asks again and fills from wherever the writer is now - but it
    /// does mean this must not be used to speculatively "probe" a writer for capacity.
    /// </para>
    /// </remarks>
    private static bool TryGetSpan(IBufferWriter<byte> writer, int length, out Span<byte> span)
    {
        span = writer.GetSpan(length);
        return span.Length >= length;
    }

    internal static void WriteUnifiedPrefixedString(IBufferWriter<byte> writer, byte[]? prefix, string? value)
    {
        if (value == null)
        {
            // special case
            writer.Write(NullBulkString);
        }
        else
        {
            // ${total-len}\r\n         3 + MaxInt32TextLen
            // {prefix}{value}\r\n
            int encodedLength = Encoding.UTF8.GetByteCount(value),
                prefixLength = prefix?.Length ?? 0,
                totalLength = prefixLength + encodedLength;

            if (totalLength == 0)
            {
                // special-case
                writer.Write(EmptyBulkString);
            }
            else
            {
                WriteCountPrefix(writer, (byte)'$', totalLength);

                if (prefixLength != 0) writer.Write(prefix);
                if (encodedLength != 0) WriteRaw(writer, value, encodedLength);
                WriteCrlf(writer);
            }
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static void WriteCrlf(IBufferWriter<byte> writer)
    {
        // NOTE the hint stays at 2. BuffersExtensions.Write asks with no hint at all, and CycleBuffer's
        // "hint <= 0" branch returns anything non-empty - so a 1-byte segment tail would split the CRLF
        // across segments rather than rolling to a fresh one. Protocol-correct either way, but it is a
        // fragmentation change on a per-bulk-string path, and not one to make by accident.
        if (TryGetSpan(writer, 2, out var span))
        {
            span[0] = (byte)'\r';
            span[1] = (byte)'\n';
            writer.Advance(2);
        }
        else
        {
            writer.Write("\r\n"u8); // loops; a writer this stingy is pathological, but legal
        }
    }

    internal static Encoder GetPerThreadEncoder()
    {
        var encoder = s_PerThreadEncoder;
        if (encoder == null)
        {
            s_PerThreadEncoder = encoder = Encoding.UTF8.GetEncoder();
        }
        else
        {
            encoder.Reset();
        }

        return encoder;
    }

    internal static unsafe void WriteRaw(IBufferWriter<byte> writer, string value, int expectedLength)
    {
        const int MaxQuickEncodeSize = 512;

        fixed (char* cPtr = value)
        {
            int totalBytes;

            // NOTE the second condition is load-bearing, not belt-and-braces: GetSpan's size is a hint, and
            // expectedLength is handed to the encoder as the destination capacity. Against a shorter span that
            // is a buffer OVERRUN rather than an exception - it writes past the end of someone else's memory.
            // When the writer declines, fall through to the encoder loop below, which sizes every write from
            // span.Length and so copes with whatever it is given.
            if (expectedLength <= MaxQuickEncodeSize && TryGetSpan(writer, expectedLength, out var quick))
            {
                // encode directly in one hit
                fixed (byte* bPtr = &MemoryMarshal.GetReference(quick))
                {
                    totalBytes = Encoding.UTF8.GetBytes(
                        cPtr,
                        value.Length,
                        bPtr,
                        expectedLength);
                }

                writer.Advance(expectedLength);
            }
            else
            {
                // use an encoder in a loop
                var encoder = GetPerThreadEncoder();
                int charsRemaining = value.Length, charOffset = 0;
                totalBytes = 0;

                bool final = false;
                while (true)
                {
                    var span = writer
                        .GetSpan(5); // get *some* memory - at least enough for 1 character (but hopefully lots more)

                    int charsUsed, bytesUsed;
                    bool completed;
                    fixed (byte* bPtr = &MemoryMarshal.GetReference(span))
                    {
                        encoder.Convert(
                            cPtr + charOffset,
                            charsRemaining,
                            bPtr,
                            span.Length,
                            final,
                            out charsUsed,
                            out bytesUsed,
                            out completed);
                    }

                    writer.Advance(bytesUsed);
                    totalBytes += bytesUsed;
                    charOffset += charsUsed;
                    charsRemaining -= charsUsed;

                    if (charsRemaining <= 0)
                    {
                        if (charsRemaining < 0) throw new InvalidOperationException("String encode went negative");
                        if (completed) break; // fine
                        if (final) throw new InvalidOperationException("String encode failed to complete");
                        final = true; // flush the encoder to one more span, then exit
                    }
                }
            }

            if (totalBytes != expectedLength) throw new InvalidOperationException("String encode length check failure");
        }
    }

    private static void WriteUnifiedInt64(IBufferWriter<byte> writer, long value)
    {
        // note from specification: A client sends to the Redis server a RESP Array consisting of just Bulk Strings.
        // (i.e. we can't just send ":123\r\n", we need to send "$3\r\n123\r\n"

        // ${asc-len}\r\n           = 4/5 (asc-len at most 2 digits)
        // {asc}\r\n                = MaxInt64TextLen + 2
        var scratch = new PrefixScratch(writer, 7 + Format.MaxInt64TextLen, stackalloc byte[MaxValueScratch]);
        var span = scratch.Span;
        span[0] = (byte)'$';
        scratch.Commit(WriteRaw(span, value, withLengthPrefix: true, offset: 1));
    }

    private static void WriteUnifiedUInt64(IBufferWriter<byte> writer, ulong value)
    {
        // note from specification: A client sends to the Redis server a RESP Array consisting of just Bulk Strings.
        // (i.e. we can't just send ":123\r\n", we need to send "$3\r\n123\r\n"
        Span<byte> valueSpan = stackalloc byte[Format.MaxInt64TextLen];

        var len = Format.FormatUInt64(value, valueSpan);
        // ${asc-len}\r\n           = 4/5 (asc-len at most 2 digits)
        // {asc}\r\n                = {len} + 2
        var scratch = new PrefixScratch(writer, 7 + len, stackalloc byte[MaxValueScratch]);
        var span = scratch.Span;
        span[0] = (byte)'$';
        int offset = WriteRaw(span, len, withLengthPrefix: false, offset: 1);
        valueSpan.Slice(0, len).CopyTo(span.Slice(offset));
        offset += len;
        scratch.Commit(WriteCrlf(span, offset));
    }

    private static void WriteUnifiedDouble(IBufferWriter<byte> writer, double value)
    {
#if NET8_0_OR_GREATER
        Span<byte> valueSpan = stackalloc byte[Format.MaxDoubleTextLen];
        var len = Format.FormatDouble(value, valueSpan);

        // ${asc-len}\r\n           = 4/5 (asc-len at most 2 digits)
        // {asc}\r\n                = {len} + 2
        var scratch = new PrefixScratch(writer, 7 + len, stackalloc byte[MaxValueScratch]);
        var span = scratch.Span;
        span[0] = (byte)'$';
        int offset = WriteRaw(span, len, withLengthPrefix: false, offset: 1);
        valueSpan.Slice(0, len).CopyTo(span.Slice(offset));
        offset += len;
        scratch.Commit(WriteCrlf(span, offset));
#else
        // fallback: drop to string
        WriteUnifiedPrefixedString(writer, null, Format.ToString(value));
#endif
    }

    internal static void WriteInteger(IBufferWriter<byte> writer, long value)
    {
        // note: client should never write integer; only server does this
        // :{asc}\r\n                = MaxInt64TextLen + 3
        var scratch = new PrefixScratch(writer, 3 + Format.MaxInt64TextLen, stackalloc byte[MaxValueScratch]);
        var span = scratch.Span;
        span[0] = (byte)':';
        scratch.Commit(WriteRaw(span, value, withLengthPrefix: false, offset: 1));
    }

    private static void WriteUnifiedBlob(IBufferWriter<byte> writer, byte[]? value)
    {
        if (value is null)
        {
            // special case:
            writer.Write(NullBulkString);
        }
        else
        {
            WriteUnifiedSpan(writer, new ReadOnlySpan<byte>(value));
        }
    }

    private static void WriteUnifiedSpan(IBufferWriter<byte> writer, ReadOnlySpan<byte> value)
    {
        // ${len}\r\n           = 3 + MaxInt32TextLen
        // {value}\r\n          = 2 + value.Length
        const int MaxQuickSpanSize = 512;
        if (value.Length == 0)
        {
            // special case:
            writer.Write(EmptyBulkString);
        }
        else
        {
            var needed = 5 + Format.MaxInt32TextLen + value.Length;
            Span<byte> quick;
            if (value.Length <= MaxQuickSpanSize && (quick = writer.GetSpan(needed)).Length >= needed)
            {
                quick[0] = (byte)'$';
                writer.Advance(AppendToSpan(quick, value, 1));
            }
            else
            {
                // too big for one span, or the writer declined the hint
                WriteUnifiedSpanPiecewise(writer, value);
            }
        }
    }

    /// <summary>
    /// Write a <c>{prefix}{count}\r\n</c> header - the <c>*</c> of a multi-bulk and the <c>$</c> of a bulk
    /// string are the same shape.
    /// </summary>
    /// <remarks>
    /// Sized for an int64: <see cref="WriteMultiBulkHeader(IBufferWriter{byte}, long)"/>,
    /// <c>WriteUnifiedPrefixedBlob</c> and <c>WriteUnifiedSequenceIterator</c> all format a <c>long</c>, and
    /// previously asked for int32 width. Unreachable in practice - it needs a value above <c>int.MaxValue</c>
    /// - but the direct path could be handed exactly the 14 bytes it asked for and then write up to 23.
    /// </remarks>
    private static void WriteCountPrefix(IBufferWriter<byte> writer, byte prefix, long count)
    {
        const int Needed = 3 + Format.MaxInt64TextLen;
        var span = writer.GetSpan(Needed);
        if (span.Length >= Needed)
        {
            span[0] = prefix;
            writer.Advance(WriteRaw(span, count, offset: 1));
        }
        else
        {
            WriteCountPrefixSlow(writer, prefix, count);
        }
    }

    /// <summary>
    /// A destination for a short, fixed-size burst: the writer's own span when it is long enough, and a stack
    /// buffer when it is not.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Keeps the zero-copy path for the overwhelmingly common case while staying correct when the hint is not
    /// honoured, which <see cref="TryGetSpan"/> explains. On the fallback path the composed bytes go through
    /// <see cref="BuffersExtensions.Write{T}"/>, which loops until everything is written.
    /// </para>
    /// <para>
    /// Writing past a short span was an <see cref="ArgumentOutOfRangeException"/> at best, and - where the
    /// destination length was handed to an encoder rather than derived from the span - a buffer overrun.
    /// </para>
    /// </remarks>
    private readonly ref struct PrefixScratch
    {
        private readonly IBufferWriter<byte> _writer;
        private readonly Span<byte> _span;
        private readonly bool _direct;

        public PrefixScratch(IBufferWriter<byte> writer, int length, Span<byte> fallback)
        {
            _writer = writer;

            // both are sliced to exactly length: the slice costs nothing (every write is bounds-checked
            // anyway) but it holds in Release, where a Debug.Assert does not. This is the PR's own thesis
            // applied to its own buffer - do not trust that it is as long as you think.
            if (TryGetSpan(writer, length, out var span))
            {
                _span = span.Slice(0, length);
                _direct = true;
            }
            else
            {
                _span = fallback.Slice(0, length);
                _direct = false;
            }
        }

        /// <summary>Where to compose the bytes.</summary>
        public Span<byte> Span => _span;

        /// <summary>Hand <paramref name="bytes"/> of <see cref="Span"/> to the writer.</summary>
        public void Commit(int bytes)
        {
            if (_direct)
            {
                _writer.Advance(bytes);
            }
            else
            {
                _writer.Write(_span.Slice(0, bytes));
            }
        }
    }

    private static ReadOnlySpan<byte> NullBulkString => "$-1\r\n"u8;

    private static ReadOnlySpan<byte> EmptyBulkString => "$0\r\n\r\n"u8;

    private static void WriteUnifiedSequenceIterator(IBufferWriter<byte> writer, ReadOnlySequenceSegmentIterator<byte> seq)
    {
        WriteCountPrefix(writer, (byte)'$', seq.Length);

        while (seq.TryNext(out var memory))
        {
            writer.Write(memory.Span);
        }

        WriteCrlf(writer);
    }

    /// <summary>
    /// The largest fixed-size burst any single compose-then-write below needs: a prefix byte, a formatted
    /// int64 or double payload, and the framing CRLFs - whichever is larger.
    /// </summary>
    private const int MaxValueScratch = 7 + Format.MaxDoubleTextLen > 7 + Format.MaxInt64TextLen
        ? 7 + Format.MaxDoubleTextLen
        : 7 + Format.MaxInt64TextLen;

    private static int AppendToSpan(Span<byte> span, ReadOnlySpan<byte> value, int offset = 0)
    {
        offset = WriteRaw(span, value.Length, offset: offset);
        value.CopyTo(span.Slice(offset, value.Length));
        offset += value.Length;
        return WriteCrlf(span, offset);
    }

    /// <summary>
    /// Write a bulk string in pieces rather than as one burst.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Unlike the <c>…Slow</c> methods this is <b>not</b> only a fallback: <see cref="WriteUnifiedSpan"/>
    /// sends every value over <c>MaxQuickSpanSize</c> here because no single span could hold it, so for large
    /// binary values this is the normal path. It therefore uses the checked <see cref="WriteCountPrefix"/>,
    /// which writes the length prefix in place when the writer can take 23 bytes; going straight to
    /// <see cref="WriteCountPrefixSlow"/> would compose into a stack buffer and hand it to a hintless
    /// <see cref="BuffersExtensions.Write{T}"/>, costing a copy and risking a prefix split across segments.
    /// </para>
    /// <para>
    /// It is still <see cref="MethodImplOptions.NoInlining"/>, but <b>not</b> for the reason given on
    /// <see cref="WriteCountPrefixSlow"/> - there is no <c>stackalloc</c> left in here to perturb the
    /// caller's codegen. The reason now is simply that <see cref="WriteUnifiedSpan"/>'s hot path is the
    /// small-value one, and this body has no business being in that frame.
    /// </para>
    /// </remarks>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void WriteUnifiedSpanPiecewise(IBufferWriter<byte> writer, ReadOnlySpan<byte> value)
    {
        WriteCountPrefix(writer, (byte)'$', value.Length);
        writer.Write(value);
        WriteCrlf(writer);
    }

    /// <summary>
    /// The fallback when the writer declined the hint: compose in a stack buffer and let the looping write
    /// place it.
    /// </summary>
    /// <remarks>
    /// Deliberately separate and not inlined. A <c>stackalloc</c> in the caller's <em>cold</em> branch changes
    /// codegen for the whole method: written inline, these fallbacks cost ~20% on the simplest command shape.
    /// It is the same "inline-optimized for it-fits, pathological case doesn't inline" split the BCL uses, and
    /// that <see href="https://github.com/CommunityToolkit/dotnet/issues/1208"/> describes.
    /// </remarks>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void WriteCountPrefixSlow(IBufferWriter<byte> writer, byte prefix, long count)
    {
        Span<byte> scratch = stackalloc byte[MaxValueScratch];
        scratch[0] = prefix;
        writer.Write(scratch.Slice(0, WriteRaw(scratch, count, offset: 1)));
    }
}
