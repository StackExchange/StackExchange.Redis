using System;
using System.Buffers;
using System.Buffers.Text;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using RESPite.Internal;
using RESPite.Messages;
using static RESPite.Internal.Constants;
namespace RESPite.Resp;

/// <summary>
/// Base implementation for RESP writers.
/// </summary>
public abstract class RespWriterBase<TRequest> : IWriter<TRequest>
{
    /// <summary>
    /// Write a raw RESP payload.
    /// </summary>
    public virtual void Write(in TRequest request, IBufferWriter<byte> target)
    {
        var writer = new RespWriter(target);
        Write(ref writer);
        writer.Flush();
    }

    /// <summary>
    /// Write a RESP payload via the <see cref="RespWriter"/> API.
    /// </summary>
    public virtual void Write(ref RespWriter writer)
        => throw new NotSupportedException("A " + nameof(Write) + " overload must be overridden");
}

/// <summary>
/// Provides low-level RESP formatting operations.
/// </summary>
public ref struct RespWriter
{
    private readonly IBufferWriter<byte> _target;
    private int _index;

#if NET7_0_OR_GREATER
    private ref byte StartOfBuffer;
    private int BufferLength;
    private ref byte WriteHead
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => ref Unsafe.Add(ref StartOfBuffer, _index);
    }
    private Span<byte> Tail
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => MemoryMarshal.CreateSpan(ref Unsafe.Add(ref StartOfBuffer, _index), BufferLength - _index);
    }
    private void WriteRawUnsafe(byte value) => Unsafe.Add(ref StartOfBuffer, _index++) = (byte)value;

    private ReadOnlySpan<byte> WrittenLocalBuffer => MemoryMarshal.CreateReadOnlySpan(ref StartOfBuffer, _index);
#else
    private Span<byte> _buffer;
    private int BufferLength => _buffer.Length;
    private ref byte StartOfBuffer
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => ref MemoryMarshal.GetReference(_buffer);
    }
    private ref byte WriteHead
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => ref Unsafe.Add(ref MemoryMarshal.GetReference(_buffer), _index);
    }
    private Span<byte> Tail => _buffer.Slice(_index);
    private void WriteRawUnsafe(byte value) => _buffer[_index++] = value;

    private ReadOnlySpan<byte> WrittenLocalBuffer => _buffer.Slice(0, _index);
#endif

    internal string DebugBuffer() => UTF8.GetString(WrittenLocalBuffer);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void WriteCrLfUnsafe()
    {
        Unsafe.WriteUnaligned(ref WriteHead, CrLfUInt16);
        _index += 2;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void WriteCrLf()
    {
        if (Available >= 2)
        {
            Unsafe.WriteUnaligned(ref WriteHead, CrLfUInt16);
            _index += 2;
        }
        else
        {
            WriteRaw(CrlfBytes);
        }
    }

    private int Available
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => BufferLength - _index;
    }

    /// <summary>
    /// Create a new RESP writer over the provided target.
    /// </summary>
    public RespWriter(in IBufferWriter<byte> target)
    {
        _target = target;
        _index = 0;
#if NET7_0_OR_GREATER
        StartOfBuffer = ref Unsafe.NullRef<byte>();
        BufferLength = 0;
#else
        _buffer = default;
#endif
        GetBuffer();
    }

    /// <summary>
    /// Commits any unwritten bytes to the output.
    /// </summary>
    public void Flush()
    {
        if (_index != 0)
        {
            _target.Advance(_index);
#if NET7_0_OR_GREATER
            _index = BufferLength = 0;
            StartOfBuffer = ref Unsafe.NullRef<byte>();
#else
            _index = 0;
            _buffer = default;
#endif
        }
    }

    private void FlushAndGetBuffer(int sizeHint)
    {
        Flush();
        GetBuffer(sizeHint);
    }

    private void GetBuffer(int sizeHint = 128)
    {
        if (Available == 0)
        {
            const int MIN_BUFFER = 1024;
            _index = 0;
#if NET7_0_OR_GREATER
            var span = _target.GetSpan(Math.Max(sizeHint, MIN_BUFFER));
            BufferLength = span.Length;
            StartOfBuffer = ref MemoryMarshal.GetReference(span);
#else
            _buffer = _target.GetSpan(Math.Max(sizeHint, MIN_BUFFER));
#endif
            Debug.Assert(Available > 0);
        }
    }

    /// <summary>
    /// Write raw RESP data to the output; no validation will occur.
    /// </summary>
    public void WriteRaw(scoped ReadOnlySpan<byte> buffer)
    {
        const int MAX_TO_DOUBLE_BUFFER = 128;
        if (buffer.Length <= MAX_TO_DOUBLE_BUFFER && buffer.Length <= Available)
        {
            buffer.CopyTo(Tail);
            _index += buffer.Length;
        }
        else
        {
            // write directly to the output
            Flush();
            _target.Write(buffer);
        }
    }

    /// <summary>
    /// Write an array header.
    /// </summary>
    /// <param name="count">The number of elements in the array.</param>
    public void WriteArray(int count) => WritePrefixedInteger(RespPrefix.Array, count);

    /// <summary>
    /// Write a payload as a bulk string.
    /// </summary>
    /// <param name="value">The payload to write.</param>
    public void WriteBulkString(scoped ReadOnlySpan<byte> value)
    {
        if (value.IsEmpty)
        {
            WriteRaw("$0\r\n\r\n"u8);
        }
        else if (value.Length < 10 && Available >= value.Length + 6) // in-place write "$N\r\nX...X\r\n" for 1-9 X
        {
            StringPrefixNullToNine(value.Length).CopyTo(Tail);
            _index += 4;
            value.CopyTo(Tail);
            _index += value.Length;
            WriteCrLfUnsafe();
        }
        else
        {
            // slow path
            WritePrefixedInteger(RespPrefix.BulkString, value.Length);
            WriteRaw(value);
            WriteCrLf();
        }
    }

    /// <summary>
    /// Write an integer as a bulk string.
    /// </summary>
    public void WriteBulkString(bool value)
        => WriteRaw(value ? "$1\r\n1\r\n"u8 : "$1\r\n0\r\n"u8);

    /// <summary>
    /// Write an integer as a bulk string.
    /// </summary>
    public void WriteBulkString(int value)
    {
        if (value >= -1 & value <= 20)
        {
            WriteRaw(value switch
            {
                -1 => "$2\r\n-1\r\n"u8,
                0 => "$1\r\n0\r\n"u8,
                1 => "$1\r\n1\r\n"u8,
                2 => "$1\r\n2\r\n"u8,
                3 => "$1\r\n3\r\n"u8,
                4 => "$1\r\n4\r\n"u8,
                5 => "$1\r\n5\r\n"u8,
                6 => "$1\r\n6\r\n"u8,
                7 => "$1\r\n7\r\n"u8,
                8 => "$1\r\n8\r\n"u8,
                9 => "$1\r\n9\r\n"u8,
                10 => "$2\r\n10\r\n"u8,
                11 => "$2\r\n11\r\n"u8,
                12 => "$2\r\n12\r\n"u8,
                13 => "$2\r\n13\r\n"u8,
                14 => "$2\r\n14\r\n"u8,
                15 => "$2\r\n15\r\n"u8,
                16 => "$2\r\n16\r\n"u8,
                17 => "$2\r\n17\r\n"u8,
                18 => "$2\r\n18\r\n"u8,
                19 => "$2\r\n19\r\n"u8,
                20 => "$2\r\n20\r\n"u8,
                _ => Throw(),
            });

            static ReadOnlySpan<byte> Throw() => throw new ArgumentOutOfRangeException(nameof(value));
        }
        else if (Available >= MaxProtocolBytesBulkStringIntegerInt32)
        {
            var singleDigit = value >= -99_999_999 && value <= 999_999_999;
            WriteRawUnsafe((byte)RespPrefix.BulkString);

            var target = Tail.Slice(singleDigit ? 3 : 4); // N\r\n or NN\r\n
            if (!Utf8Formatter.TryFormat(value, target, out var valueBytes))
                ThrowFormatException();

            Debug.Assert(valueBytes > 0 && singleDigit ? (valueBytes < 10) : (valueBytes is 10 or 11));
            if (!Utf8Formatter.TryFormat(valueBytes, Tail, out var prefixBytes))
                ThrowFormatException();
            Debug.Assert(prefixBytes == (singleDigit ? 1 : 2));
            _index += prefixBytes;
            WriteCrLfUnsafe();
            _index += valueBytes;
            WriteCrLfUnsafe();
        }
        else
        {
            Debug.Assert(MaxRawBytesInt32 <= 16);
            Span<byte> scratch = stackalloc byte[16];
            if (!Utf8Formatter.TryFormat(value, scratch, out int bytes))
                ThrowFormatException();
            WritePrefixedInteger(RespPrefix.BulkString, bytes);
            WriteRaw(scratch.Slice(0, bytes));
            WriteCrLf();
        }
    }

    private static void ThrowFormatException() => throw new FormatException();
    private void WritePrefixedInteger(RespPrefix prefix, int length)
    {
        if (Available >= MaxProtocolBytesIntegerInt32)
        {
            WriteRawUnsafe((byte)prefix);
            if (length >= 0 & length <= 9)
            {
                WriteRawUnsafe((byte)(length + '0'));
            }
            else
            {
                if (!Utf8Formatter.TryFormat(length, Tail, out var bytesWritten))
                {
                    ThrowFormatException();
                }
                _index += bytesWritten;
            }
            WriteCrLfUnsafe();
        }
        else
        {
            WriteViaStack(ref this, prefix, length);
        }
        static void WriteViaStack(ref RespWriter respWriter, RespPrefix prefix, int length)
        {
            Debug.Assert(MaxProtocolBytesIntegerInt32 <= 16);
            Span<byte> buffer = stackalloc byte[16];
            buffer[0] = (byte)prefix;
            int payloadLength;
            if (length >= 0 & length <= 9)
            {
                buffer[1] = (byte)(length + '0');
                payloadLength = 1;
            }
            else if (!Utf8Formatter.TryFormat(length, buffer.Slice(3), out payloadLength))
            {
                ThrowFormatException();
            }
            Unsafe.WriteUnaligned(ref buffer[payloadLength + 1], CrLfUInt16);
            respWriter.WriteRaw(buffer.Slice(0, payloadLength + 3));
        }
        bool writeToStack = Available < MaxProtocolBytesIntegerInt32;

        Span<byte> target = writeToStack ? stackalloc byte[16] : Tail;
        target[0] = (byte)prefix;
    }

    private static ReadOnlySpan<byte> StringPrefixNullToNine(int count)
    {
        return count switch
        {
            -1 => "$-1\r\n"u8,
            0 => "$0\r\n"u8,
            1 => "$1\r\n"u8,
            2 => "$2\r\n"u8,
            3 => "$3\r\n"u8,
            4 => "$4\r\n"u8,
            5 => "$5\r\n"u8,
            6 => "$6\r\n"u8,
            7 => "$7\r\n"u8,
            8 => "$8\r\n"u8,
            9 => "$9\r\n"u8,
            _ => Throw(),
        };
        static ReadOnlySpan<byte> Throw() => throw new ArgumentOutOfRangeException(nameof(count));
    }

    /// <summary>
    /// Write a payload as a bulk string.
    /// </summary>
    /// <param name="value">The payload to write.</param>
    public void WriteBulkString(string value)
    {
        if (value is null)
        {
            WriteRaw("$-1\r\n"u8);
        }
        else
        {
            WriteBulkString(value.AsSpan());
        }
    }

    /// <summary>
    /// Write a payload as a bulk string.
    /// </summary>
    /// <param name="value">The payload to write.</param>
    public void WriteBulkString(scoped ReadOnlySpan<char> value)
    {
        const int MAX_HINT = 64 * 1024;
        if (value.Length == 0)
        {
            WriteRaw("$0\r\n\r\n"u8);
        }
        else
        {
            var byteCount = UTF8.GetByteCount(value);
            WritePrefixedInteger(RespPrefix.BulkString, byteCount);
            if (Available >= 2 + byteCount)
            {
                var actual = UTF8.GetBytes(value, Tail);
                Debug.Assert(actual == byteCount);
                _index += actual;
                WriteCrLfUnsafe();
            }
            else
            {
                FlushAndGetBuffer(Math.Min(byteCount, MAX_HINT));
                if (Available >= byteCount)
                {
                    // that'll work
                    var actual = UTF8.GetBytes(value, Tail);
                    Debug.Assert(actual == byteCount);
                    _index += actual;
                }
                else
                {
                    WriteUtf8Slow(ref this, value, byteCount);
                }
                WriteCrLf();
            }
        }

        static void WriteUtf8Slow(ref RespWriter writer, scoped ReadOnlySpan<char> value, int remaining)
        {
            var enc = s_PerThreadEncoder;
            if (enc is null)
            {
                enc = s_PerThreadEncoder = UTF8.GetEncoder();
            }
            else
            {
                enc.Reset();
            }

            bool completed;
            int charsUsed, bytesUsed;
            do
            {
                enc.Convert(value, writer.Tail, false, out charsUsed, out bytesUsed, out completed);
                value = value.Slice(charsUsed);
                writer._index += bytesUsed;
                remaining -= bytesUsed;
                writer.FlushAndGetBuffer(Math.Min(remaining, MAX_HINT));
            }
            while (!completed);

            if (remaining != 0)
            {
                // any trailing data?
                writer.FlushAndGetBuffer(Math.Min(remaining, MAX_HINT));
                enc.Convert(value, writer.Tail, true, out charsUsed, out bytesUsed, out completed);
                Debug.Assert(charsUsed == 0);
                writer._index += bytesUsed;
                remaining -= bytesUsed;
            }
            enc.Reset();
            Debug.Assert(remaining == 0);
        }
    }

    [ThreadStatic]
    private static Encoder? s_PerThreadEncoder;
}
