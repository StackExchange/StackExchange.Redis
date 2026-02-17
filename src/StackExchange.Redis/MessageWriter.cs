using System;
using System.Buffers;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using RESPite.Internal;

namespace StackExchange.Redis;

internal readonly ref struct MessageWriter(PhysicalConnection connection)
{
    public PhysicalBridge? BridgeCouldBeNull => connection.BridgeCouldBeNull;
    private readonly IBufferWriter<byte> _writer = BlockBufferSerializer.Shared;

    public ReadOnlyMemory<byte> Flush() =>
        BlockBufferSerializer.BlockBuffer.FinalizeMessage(BlockBufferSerializer.Shared);

    public void Revert() => BlockBufferSerializer.Shared.Revert();

    public static void Release(ReadOnlyMemory<byte> memory)
    {
        if (MemoryMarshal.TryGetMemoryManager<byte, BlockBufferSerializer.BlockBuffer>(
                memory, out var block))
        {
            block.Release();
        }
    }

    public static void Release(in ReadOnlySequence<byte> request) =>
        BlockBufferSerializer.BlockBuffer.Release(in request);

    public void Write(in RedisKey key)
    {
        var val = key.KeyValue;
        if (val is string s)
        {
            WriteUnifiedPrefixedString(_writer, key.KeyPrefix, s);
        }
        else
        {
            WriteUnifiedPrefixedBlob(_writer, key.KeyPrefix, (byte[]?)val);
        }
    }

    internal void Write(in RedisChannel channel)
        => WriteUnifiedPrefixedBlob(_writer, channel.IgnoreChannelPrefix ? null : connection.ChannelPrefix, channel.Value);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal void WriteBulkString(in RedisValue value)
        => WriteBulkString(value, _writer);

    internal static void WriteBulkString(in RedisValue value, IBufferWriter<byte> writer)
    {
        switch (value.Type)
        {
            case RedisValue.StorageType.Null:
                WriteUnifiedBlob(writer, (byte[]?)null);
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
                WriteUnifiedPrefixedString(writer, null, (string?)value);
                break;
            case RedisValue.StorageType.Raw:
                WriteUnifiedSpan(writer, ((ReadOnlyMemory<byte>)value).Span);
                break;
            default:
                throw new InvalidOperationException($"Unexpected {value.Type} value: '{value}'");
        }
    }

    internal void WriteBulkString(ReadOnlySpan<byte> value) => WriteUnifiedSpan(_writer, value);

    internal const int
        REDIS_MAX_ARGS =
            1024 * 1024; // there is a <= 1024*1024 max constraint inside redis itself: https://github.com/antirez/redis/blob/6c60526db91e23fb2d666fc52facc9a11780a2a3/src/networking.c#L1024

    internal void WriteHeader(RedisCommand command, int arguments, CommandBytes commandBytes = default)
    {
        var bridge = connection.BridgeCouldBeNull ?? throw new ObjectDisposedException(connection.ToString());

        if (command == RedisCommand.UNKNOWN)
        {
            // using >= here because we will be adding 1 for the command itself (which is an arg for the purposes of the multi-bulk protocol)
            if (arguments >= REDIS_MAX_ARGS) throw ExceptionFactory.TooManyArgs(commandBytes.ToString(), arguments);
        }
        else
        {
            // using >= here because we will be adding 1 for the command itself (which is an arg for the purposes of the multi-bulk protocol)
            if (arguments >= REDIS_MAX_ARGS) throw ExceptionFactory.TooManyArgs(command.ToString(), arguments);

            // for everything that isn't custom commands: ask the muxer for the actual bytes
            commandBytes = bridge.Multiplexer.CommandMap.GetBytes(command);
        }

        // in theory we should never see this; CheckMessage dealt with "regular" messages, and
        // ExecuteMessage should have dealt with everything else
        if (commandBytes.IsEmpty) throw ExceptionFactory.CommandDisabled(command);

        // *{argCount}\r\n      = 3 + MaxInt32TextLen
        // ${cmd-len}\r\n       = 3 + MaxInt32TextLen
        // {cmd}\r\n            = 2 + commandBytes.Length
        var span = _writer.GetSpan(commandBytes.Length + 8 + Format.MaxInt32TextLen + Format.MaxInt32TextLen);
        span[0] = (byte)'*';

        int offset = WriteRaw(span, arguments + 1, offset: 1);

        offset = AppendToSpanCommand(span, commandBytes, offset: offset);

        _writer.Advance(offset);
    }

    internal static void WriteMultiBulkHeader(IBufferWriter<byte> writer, long count)
    {
        // *{count}\r\n         = 3 + MaxInt32TextLen
        var span = writer.GetSpan(3 + Format.MaxInt32TextLen);
        span[0] = (byte)'*';
        int offset = WriteRaw(span, count, offset: 1);
        writer.Advance(offset);
    }

    private static ReadOnlySpan<byte> NullBulkString => "$-1\r\n"u8;
    private static ReadOnlySpan<byte> EmptyBulkString => "$0\r\n\r\n"u8;

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
                var span = writer.GetSpan(3 + Format.MaxInt32TextLen);
                span[0] = (byte)'$';
                int bytes = WriteRaw(span, totalLength, offset: 1);
                writer.Advance(bytes);

                if (prefixLength != 0) writer.Write(prefix);
                if (encodedLength != 0) WriteRaw(writer, value, encodedLength);
                WriteCrlf(writer);
            }
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static int WriteCrlf(Span<byte> span, int offset)
    {
        span[offset++] = (byte)'\r';
        span[offset++] = (byte)'\n';
        return offset;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static void WriteCrlf(IBufferWriter<byte> writer)
    {
        var span = writer.GetSpan(2);
        span[0] = (byte)'\r';
        span[1] = (byte)'\n';
        writer.Advance(2);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal void WriteRaw(ReadOnlySpan<byte> value) => _writer.Write(value);

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

    [ThreadStatic]
    private static Encoder? s_PerThreadEncoder;

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
            if (expectedLength <= MaxQuickEncodeSize)
            {
                // encode directly in one hit
                var span = writer.GetSpan(expectedLength);
                fixed (byte* bPtr = span)
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
                    fixed (byte* bPtr = span)
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

    private static void WriteUnifiedPrefixedBlob(IBufferWriter<byte> writer, byte[]? prefix, byte[]? value)
    {
        // ${total-len}\r\n
        // {prefix}{value}\r\n
        if (prefix == null || prefix.Length == 0 || value == null)
        {
            // if no prefix, just use the non-prefixed version;
            // even if prefixed, a null value writes as null, so can use the non-prefixed version
            WriteUnifiedBlob(writer, value);
        }
        else
        {
            var span = writer.GetSpan(3 +
                                      Format
                                          .MaxInt32TextLen); // note even with 2 max-len, we're still in same text range
            span[0] = (byte)'$';
            int bytes = WriteRaw(span, prefix.LongLength + value.LongLength, offset: 1);
            writer.Advance(bytes);

            writer.Write(prefix);
            writer.Write(value);

            span = writer.GetSpan(2);
            WriteCrlf(span, 0);
            writer.Advance(2);
        }
    }

    private static void WriteUnifiedInt64(IBufferWriter<byte> writer, long value)
    {
        // note from specification: A client sends to the Redis server a RESP Array consisting of just Bulk Strings.
        // (i.e. we can't just send ":123\r\n", we need to send "$3\r\n123\r\n"

        // ${asc-len}\r\n           = 4/5 (asc-len at most 2 digits)
        // {asc}\r\n                = MaxInt64TextLen + 2
        var span = writer.GetSpan(7 + Format.MaxInt64TextLen);

        span[0] = (byte)'$';
        var bytes = WriteRaw(span, value, withLengthPrefix: true, offset: 1);
        writer.Advance(bytes);
    }

    private static void WriteUnifiedUInt64(IBufferWriter<byte> writer, ulong value)
    {
        // note from specification: A client sends to the Redis server a RESP Array consisting of just Bulk Strings.
        // (i.e. we can't just send ":123\r\n", we need to send "$3\r\n123\r\n"
        Span<byte> valueSpan = stackalloc byte[Format.MaxInt64TextLen];

        var len = Format.FormatUInt64(value, valueSpan);
        // ${asc-len}\r\n           = 4/5 (asc-len at most 2 digits)
        // {asc}\r\n                = {len} + 2
        var span = writer.GetSpan(7 + len);
        span[0] = (byte)'$';
        int offset = WriteRaw(span, len, withLengthPrefix: false, offset: 1);
        valueSpan.Slice(0, len).CopyTo(span.Slice(offset));
        offset += len;
        offset = WriteCrlf(span, offset);
        writer.Advance(offset);
    }

    private static void WriteUnifiedDouble(IBufferWriter<byte> writer, double value)
    {
#if NET8_0_OR_GREATER
        Span<byte> valueSpan = stackalloc byte[Format.MaxDoubleTextLen];
        var len = Format.FormatDouble(value, valueSpan);

        // ${asc-len}\r\n           = 4/5 (asc-len at most 2 digits)
        // {asc}\r\n                = {len} + 2
        var span = writer.GetSpan(7 + len);
        span[0] = (byte)'$';
        int offset = WriteRaw(span, len, withLengthPrefix: false, offset: 1);
        valueSpan.Slice(0, len).CopyTo(span.Slice(offset));
        offset += len;
        offset = WriteCrlf(span, offset);
        writer.Advance(offset);
#else
        // fallback: drop to string
        WriteUnifiedPrefixedString(writer, null, Format.ToString(value));
#endif
    }

    internal static void WriteInteger(IBufferWriter<byte> writer, long value)
    {
        // note: client should never write integer; only server does this
        // :{asc}\r\n                = MaxInt64TextLen + 3
        var span = writer.GetSpan(3 + Format.MaxInt64TextLen);

        span[0] = (byte)':';
        var bytes = WriteRaw(span, value, withLengthPrefix: false, offset: 1);
        writer.Advance(bytes);
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
        else if (value.Length <= MaxQuickSpanSize)
        {
            var span = writer.GetSpan(5 + Format.MaxInt32TextLen + value.Length);
            span[0] = (byte)'$';
            int bytes = AppendToSpan(span, value, 1);
            writer.Advance(bytes);
        }
        else
        {
            // too big to guarantee can do in a single span
            var span = writer.GetSpan(3 + Format.MaxInt32TextLen);
            span[0] = (byte)'$';
            int bytes = WriteRaw(span, value.Length, offset: 1);
            writer.Advance(bytes);

            writer.Write(value);

            WriteCrlf(writer);
        }
    }

    private static int AppendToSpanCommand(Span<byte> span, in CommandBytes value, int offset = 0)
    {
        span[offset++] = (byte)'$';
        int len = value.Length;
        offset = WriteRaw(span, len, offset: offset);
        value.CopyTo(span.Slice(offset, len));
        offset += value.Length;
        return WriteCrlf(span, offset);
    }

    private static int AppendToSpan(Span<byte> span, ReadOnlySpan<byte> value, int offset = 0)
    {
        offset = WriteRaw(span, value.Length, offset: offset);
        value.CopyTo(span.Slice(offset, value.Length));
        offset += value.Length;
        return WriteCrlf(span, offset);
    }

    internal void WriteSha1AsHex(byte[]? value)
    {
        var writer = _writer;
        if (value is null)
        {
            writer.Write(NullBulkString);
        }
        else if (value.Length == ResultProcessor.ScriptLoadProcessor.Sha1HashLength)
        {
            // $40\r\n              = 5
            // {40 bytes}\r\n       = 42
            var span = writer.GetSpan(47);
            span[0] = (byte)'$';
            span[1] = (byte)'4';
            span[2] = (byte)'0';
            span[3] = (byte)'\r';
            span[4] = (byte)'\n';

            int offset = 5;
            for (int i = 0; i < value.Length; i++)
            {
                var b = value[i];
                span[offset++] = ToHexNibble(b >> 4);
                span[offset++] = ToHexNibble(b & 15);
            }

            span[offset++] = (byte)'\r';
            span[offset++] = (byte)'\n';

            writer.Advance(offset);
        }
        else
        {
            throw new InvalidOperationException("Invalid SHA1 length: " + value.Length);
        }
    }

    internal static byte ToHexNibble(int value)
    {
        return value < 10 ? (byte)('0' + value) : (byte)('a' - 10 + value);
    }
}
