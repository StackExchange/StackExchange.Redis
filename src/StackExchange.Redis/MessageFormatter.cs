using System;
using System.Buffers;
using System.Buffers.Text;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text;
using StackExchange.Redis.Transports;

namespace StackExchange.Redis
{
    internal static class MessageFormatter
    {
        internal const int REDIS_MAX_ARGS = 1024 * 1024; // there is a <= 1024*1024 max constraint inside redis itself: https://github.com/antirez/redis/blob/6c60526db91e23fb2d666fc52facc9a11780a2a3/src/networking.c#L1024
        internal static void WriteHeader(ITransportState writeState, IBufferWriter<byte> output, RedisCommand command, int arguments, CommandBytes commandBytes = default)
        {
            if (writeState is null) ThrowDisposed();
            static void ThrowDisposed() => throw new ObjectDisposedException("No write-state; connection has been detached");

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
                commandBytes = writeState.CommandMap.GetBytes(command);
            }

            // in theory we should never see this; CheckMessage dealt with "regular" messages, and
            // ExecuteMessage should have dealt with everything else
            if (commandBytes.IsEmpty) throw ExceptionFactory.CommandDisabled(command);

            // *{argCount}\r\n      = 3 + MaxInt32TextLen
            // ${cmd-len}\r\n       = 3 + MaxInt32TextLen
            // {cmd}\r\n            = 2 + commandBytes.Length
            var span = output.GetSpan(commandBytes.Length + 8 + MaxInt32TextLen + MaxInt32TextLen);
            span[0] = (byte)'*';

            int offset = WriteRaw(span, arguments + 1, offset: 1);

            offset = AppendToSpanCommand(span, commandBytes, offset: offset);

            output.Advance(offset);
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

        internal static void WriteMultiBulkHeader(IBufferWriter<byte> output, long count)
        {
            // *{count}\r\n         = 3 + MaxInt32TextLen
            var span = output.GetSpan(3 + MaxInt32TextLen);
            span[0] = (byte)'*';
            int offset = WriteRaw(span, count, offset: 1);
            output.Advance(offset);
        }

        internal const int
            MaxInt32TextLen = 11, // -2,147,483,648 (not including the commas)
            MaxInt64TextLen = 20; // -9,223,372,036,854,775,808 (not including the commas)

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static int WriteCrlf(Span<byte> span, int offset)
        {
            span[offset++] = (byte)'\r';
            span[offset++] = (byte)'\n';
            return offset;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static void WriteCrlf(IBufferWriter<byte> output)
        {
            var span = output.GetSpan(2);
            span[0] = (byte)'\r';
            span[1] = (byte)'\n';
            output.Advance(2);
        }

        [ThreadStatic]
        private static Encoder s_PerThreadEncoder;
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

        unsafe static internal void WriteRaw(IBufferWriter<byte> output, string value, int expectedLength)
        {
            const int MaxQuickEncodeSize = 512;

            fixed (char* cPtr = value)
            {
                int totalBytes;
                if (expectedLength <= MaxQuickEncodeSize)
                {
                    // encode directly in one hit
                    var span = output.GetSpan(expectedLength);
                    fixed (byte* bPtr = span)
                    {
                        totalBytes = Encoding.UTF8.GetBytes(cPtr, value.Length, bPtr, expectedLength);
                    }
                    output.Advance(expectedLength);
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
                        var span = output.GetSpan(5); // get *some* memory - at least enough for 1 character (but hopefully lots more)

                        int charsUsed, bytesUsed;
                        bool completed;
                        fixed (byte* bPtr = span)
                        {
                            encoder.Convert(cPtr + charOffset, charsRemaining, bPtr, span.Length, final, out charsUsed, out bytesUsed, out completed);
                        }
                        output.Advance(bytesUsed);
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
                if (!Utf8Formatter.TryFormat(value, availableChunk, out int formattedLength))
                {
                    throw new InvalidOperationException("TryFormat failed");
                }
                if (withLengthPrefix)
                {
                    // now we know how large the prefix is: write the prefix, then write the value
                    if (!Utf8Formatter.TryFormat(formattedLength, availableChunk, out int prefixLength))
                    {
                        throw new InvalidOperationException("TryFormat failed");
                    }
                    offset += prefixLength;
                    offset = WriteCrlf(span, offset);

                    availableChunk = span.Slice(offset);
                    if (!Utf8Formatter.TryFormat(value, availableChunk, out int finalLength))
                    {
                        throw new InvalidOperationException("TryFormat failed");
                    }
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

        internal static void Write(ITransportState writeState, IBufferWriter<byte> output, in RedisChannel channel)
            => WriteUnifiedPrefixedBlob(output, writeState.ChannelPrefix, channel.Value);

        internal static void Write(IBufferWriter<byte> output, in RedisKey key)
        {
            var val = key.KeyValue;
            if (val is string s)
            {
                WriteUnifiedPrefixedString(output, key.KeyPrefix, s);
            }
            else
            {
                WriteUnifiedPrefixedBlob(output, key.KeyPrefix, (byte[])val);
            }
        }

        private static void WriteUnifiedPrefixedBlob(IBufferWriter<byte> output, byte[] prefix, byte[] value)
        {
            // ${total-len}\r\n 
            // {prefix}{value}\r\n
            if (prefix == null || prefix.Length == 0 || value == null)
            {   // if no prefix, just use the non-prefixed version;
                // even if prefixed, a null value writes as null, so can use the non-prefixed version
                WriteUnifiedBlob(output, value);
            }
            else
            {
                var span = output.GetSpan(3 + MaxInt32TextLen); // note even with 2 max-len, we're still in same text range
                span[0] = (byte)'$';
                int bytes = WriteRaw(span, prefix.LongLength + value.LongLength, offset: 1);
                output.Advance(bytes);

                output.Write(prefix);
                output.Write(value);

                span = output.GetSpan(2);
                WriteCrlf(span, 0);
                output.Advance(2);
            }
        }

        internal static void WriteBulkString(IBufferWriter<byte> output, in RedisValue value)
        {
            switch (value.Type)
            {
                case RedisValue.StorageType.Null:
                    WriteUnifiedBlob(output, (byte[])null);
                    break;
                case RedisValue.StorageType.Int64:
                    WriteUnifiedInt64(output, value.OverlappedValueInt64);
                    break;
                case RedisValue.StorageType.UInt64:
                    WriteUnifiedUInt64(output, value.OverlappedValueUInt64);
                    break;
                case RedisValue.StorageType.Double: // use string
                case RedisValue.StorageType.String:
                    WriteUnifiedPrefixedString(output, null, (string)value);
                    break;
                case RedisValue.StorageType.Raw:
                    WriteUnifiedSpan(output, ((ReadOnlyMemory<byte>)value).Span);
                    break;
                default:
                    throw new InvalidOperationException($"Unexpected {value.Type} value: '{value}'");
            }
        }

        private static void WriteUnifiedInt64(IBufferWriter<byte> output, long value)
        {
            // note from specification: A client sends to the Redis server a RESP Array consisting of just Bulk Strings.
            // (i.e. we can't just send ":123\r\n", we need to send "$3\r\n123\r\n"

            // ${asc-len}\r\n           = 3 + MaxInt32TextLen
            // {asc}\r\n                = MaxInt64TextLen + 2
            var span = output.GetSpan(5 + MaxInt32TextLen + MaxInt64TextLen);

            span[0] = (byte)'$';
            var bytes = WriteRaw(span, value, withLengthPrefix: true, offset: 1);
            output.Advance(bytes);
        }

        private static void WriteUnifiedUInt64(IBufferWriter<byte> output, ulong value)
        {
            // note from specification: A client sends to the Redis server a RESP Array consisting of just Bulk Strings.
            // (i.e. we can't just send ":123\r\n", we need to send "$3\r\n123\r\n"

            // ${asc-len}\r\n           = 3 + MaxInt32TextLen
            // {asc}\r\n                = MaxInt64TextLen + 2
            var span = output.GetSpan(5 + MaxInt32TextLen + MaxInt64TextLen);

            Span<byte> valueSpan = stackalloc byte[MaxInt64TextLen];
            if (!Utf8Formatter.TryFormat(value, valueSpan, out var len))
                throw new InvalidOperationException("TryFormat failed");
            span[0] = (byte)'$';
            int offset = WriteRaw(span, len, withLengthPrefix: false, offset: 1);
            valueSpan.Slice(0, len).CopyTo(span.Slice(offset));
            offset += len;
            offset = WriteCrlf(span, offset);
            output.Advance(offset);
        }

        internal static void WriteUnifiedPrefixedString(IBufferWriter<byte> output, byte[] prefix, string value)
        {
            if (value == null)
            {
                // special case
                output.Write(NullBulkString.Span);
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
                    output.Write(EmptyBulkString.Span);
                }
                else
                {
                    var span = output.GetSpan(3 + MaxInt32TextLen);
                    span[0] = (byte)'$';
                    int bytes = WriteRaw(span, totalLength, offset: 1);
                    output.Advance(bytes);

                    if (prefixLength != 0) output.Write(prefix);
                    if (encodedLength != 0) WriteRaw(output, value, encodedLength);
                    WriteCrlf(output);
                }
            }
        }

        private static readonly ReadOnlyMemory<byte> NullBulkString = Encoding.ASCII.GetBytes("$-1\r\n"), EmptyBulkString = Encoding.ASCII.GetBytes("$0\r\n\r\n");

        private static void WriteUnifiedBlob(IBufferWriter<byte> output, byte[] value)
        {
            if (value == null)
            {
                // special case:
                output.Write(NullBulkString.Span);
            }
            else
            {
                WriteUnifiedSpan(output, new ReadOnlySpan<byte>(value));
            }
        }

        internal static void WriteInteger(IBufferWriter<byte> output, long value)
        {
            //note: client should never write integer; only server does this

            // :{asc}\r\n                = MaxInt64TextLen + 3
            var span = output.GetSpan(3 + MaxInt64TextLen);

            span[0] = (byte)':';
            var bytes = WriteRaw(span, value, withLengthPrefix: false, offset: 1);
            output.Advance(bytes);
        }

        private static void WriteUnifiedSpan(IBufferWriter<byte> output, ReadOnlySpan<byte> value)
        {
            // ${len}\r\n           = 3 + MaxInt32TextLen
            // {value}\r\n          = 2 + value.Length

            const int MaxQuickSpanSize = 512;
            if (value.Length == 0)
            {
                // special case:
                output.Write(EmptyBulkString.Span);
            }
            else if (value.Length <= MaxQuickSpanSize)
            {
                var span = output.GetSpan(5 + MaxInt32TextLen + value.Length);
                span[0] = (byte)'$';
                int bytes = AppendToSpan(span, value, 1);
                output.Advance(bytes);
            }
            else
            {
                // too big to guarantee can do in a single span
                var span = output.GetSpan(3 + MaxInt32TextLen);
                span[0] = (byte)'$';
                int bytes = WriteRaw(span, value.Length, offset: 1);
                output.Advance(bytes);

                output.Write(value);

                WriteCrlf(output);
            }
        }

        internal static void WriteSha1AsHex(IBufferWriter<byte> output, byte[] value)
        {
            if (value == null)
            {
                output.Write(NullBulkString.Span);
            }
            else if (value.Length == ResultProcessor.ScriptLoadProcessor.Sha1HashLength)
            {
                // $40\r\n              = 5
                // {40 bytes}\r\n       = 42

                var span = output.GetSpan(47);
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

                output.Advance(offset);
            }
            else
            {
                throw new InvalidOperationException("Invalid SHA1 length: " + value.Length);
            }

            static byte ToHexNibble(int value)
            {
                return value < 10 ? (byte)('0' + value) : (byte)('a' - 10 + value);
            }
        }
    }
}
