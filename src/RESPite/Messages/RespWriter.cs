using System;
using System.Buffers;
using System.Buffers.Text;
using System.ComponentModel;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using RESPite.Internal;

namespace RESPite.Messages;

/// <summary>
/// Provides low-level RESP formatting operations.
/// </summary>
public ref struct RespWriter
{
    private readonly IBufferWriter<byte>? _target;

    [SuppressMessage("Style", "IDE0032:Use auto property", Justification = "Clarity")]
    private int _index;

    internal readonly int IndexInCurrentBuffer => _index;

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

    private void WriteRawUnsafe(byte value) => Unsafe.Add(ref StartOfBuffer, _index++) = value;

    private readonly ReadOnlySpan<byte> WrittenLocalBuffer =>
        MemoryMarshal.CreateReadOnlySpan(ref StartOfBuffer, _index);
#else
    private Span<byte> _buffer;
    private readonly int BufferLength => _buffer.Length;

    private readonly ref byte StartOfBuffer
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => ref MemoryMarshal.GetReference(_buffer);
    }

    private readonly ref byte WriteHead
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => ref Unsafe.Add(ref MemoryMarshal.GetReference(_buffer), _index);
    }

    private readonly Span<byte> Tail => _buffer.Slice(_index);
    private void WriteRawUnsafe(byte value) => _buffer[_index++] = value;

    private readonly ReadOnlySpan<byte> WrittenLocalBuffer => _buffer.Slice(0, _index);
#endif

    internal readonly string DebugBuffer() => RespConstants.UTF8.GetString(WrittenLocalBuffer);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void WriteCrLfUnsafe()
    {
        Unsafe.WriteUnaligned(ref WriteHead, RespConstants.CrLfUInt16);
        _index += 2;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void WriteCrLf()
    {
        if (Available >= 2)
        {
            Unsafe.WriteUnaligned(ref WriteHead, RespConstants.CrLfUInt16);
            _index += 2;
        }
        else
        {
            WriteRaw(RespConstants.CrlfBytes);
        }
    }

    private readonly int Available
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => BufferLength - _index;
    }

    /// <summary>
    /// Create a new RESP writer over the provided target.
    /// </summary>
    public RespWriter(IBufferWriter<byte> target)
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
    /// Create a new RESP writer over the provided target.
    /// </summary>
    public RespWriter(Span<byte> target)
    {
        _index = 0;
#if NET7_0_OR_GREATER
        BufferLength = target.Length;
        StartOfBuffer = ref MemoryMarshal.GetReference(target);
#else
        _buffer = target;
#endif
    }

    /// <summary>
    /// Commits any unwritten bytes to the output.
    /// </summary>
    public void Flush()
    {
        if (_index != 0 && _target is not null)
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
            if (_target is null)
            {
                ThrowFixedBufferExceeded();
            }
            else
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
                ActivationHelper.DebugBreakIf(Available == 0);
            }
        }
    }

    [DoesNotReturn, MethodImpl(MethodImplOptions.NoInlining)]
    private static void ThrowFixedBufferExceeded() =>
        throw new InvalidOperationException("Fixed buffer cannot be expanded");

    /// <summary>
    /// Write raw RESP data to the output; no validation will occur.
    /// </summary>
    [Browsable(false), EditorBrowsable(EditorBrowsableState.Never)]
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
            if (_target is null)
            {
                ThrowFixedBufferExceeded();
            }
            else
            {
                _target.Write(buffer);
            }
        }
    }

    public RespCommandMap? CommandMap { get; set; }

    /// <summary>
    /// Write a command header.
    /// </summary>
    /// <param name="command">The command name to write.</param>
    /// <param name="args">The number of arguments for the command (excluding the command itself).</param>
    public void WriteCommand(scoped ReadOnlySpan<byte> command, int args)
    {
        if (args < 0) Throw();
        WritePrefixInteger(RespPrefix.Array, args + 1);
        if (command.IsEmpty) ThrowEmptyCommand();
        if (CommandMap is { } map)
        {
            var mapped = map.Map(command);
            if (mapped.IsEmpty) ThrowCommandUnavailable(command);
            command = mapped;
        }

        WriteBulkString(command);

        static void Throw() => throw new ArgumentOutOfRangeException(nameof(args));

        static void ThrowEmptyCommand() =>
            throw new ArgumentException(paramName: nameof(command), message: "Empty command specified.");

        static void ThrowCommandUnavailable(ReadOnlySpan<byte> command)
            => throw new ArgumentException(
                paramName: nameof(command),
                message: $"The command {Encoding.UTF8.GetString(command)} is not available.");
    }

    /// <summary>
    /// Write a key as a bulk string.
    /// </summary>
    /// <param name="value">The key to write.</param>
    public void WriteKey(scoped ReadOnlySpan<byte> value) => WriteBulkString(value);

    /// <summary>
    /// Write a key as a bulk string.
    /// </summary>
    /// <param name="value">The key to write.</param>
    public void WriteKey(ReadOnlyMemory<byte> value) => WriteBulkString(value.Span);

    /// <summary>
    /// Write a key as a bulk string.
    /// </summary>
    /// <param name="value">The key to write.</param>
    public void WriteKey(scoped ReadOnlySpan<char> value) => WriteBulkString(value);

    /// <summary>
    /// Write a key as a bulk string.
    /// </summary>
    /// <param name="value">The key to write.</param>
    public void WriteKey(ReadOnlyMemory<char> value) => WriteBulkString(value.Span);

    /// <summary>
    /// Write a key as a bulk string.
    /// </summary>
    /// <param name="value">The key to write.</param>
    public void WriteKey(string value) => WriteBulkString(value);

    /// <summary>
    /// Write a key as a bulk string.
    /// </summary>
    /// <param name="value">The key to write.</param>
    public void WriteKey(byte[] value) => WriteBulkString(value.AsSpan());

    /// <summary>
    /// Write a payload as a bulk string.
    /// </summary>
    /// <param name="value">The payload to write.</param>
    public void WriteBulkString(byte[] value) => WriteBulkString(value.AsSpan());

    /// <summary>
    /// Write a payload as a bulk string.
    /// </summary>
    /// <param name="value">The payload to write.</param>
    public void WriteBulkString(ReadOnlyMemory<byte> value)
        => WriteBulkString(value.Span);

    /// <summary>
    /// Write a payload as a bulk string.
    /// </summary>
    /// <param name="value">The payload to write.</param>
    public void WriteBulkString(scoped ReadOnlySpan<byte> value)
    {
        if (value.IsEmpty)
        {
            if (Available >= 6)
            {
                WriteRawPrechecked(Raw.BulkStringEmpty_6, 6);
            }
            else
            {
                WriteRaw("$0\r\n\r\n"u8);
            }
        }
        else
        {
            WriteBulkStringHeader(value.Length);
            if (Available >= value.Length + 2)
            {
                value.CopyTo(Tail);
                _index += value.Length;
                WriteCrLfUnsafe();
            }
            else
            {
                // slow path
                WriteRaw(value);
                WriteCrLf();
            }
        }
    }

    /*
    /// <summary>
    /// Write a payload as a bulk string.
    /// </summary>
    /// <param name="value">The payload to write.</param>
    public void WriteBulkString(in SimpleString value)
    {
        if (value.IsEmpty)
        {
            WriteRaw("$0\r\n\r\n"u8);
        }
        else if (value.TryGetBytes(span: out var bytes))
        {
            WriteBulkString(bytes);
        }
        else if (value.TryGetChars(span: out var chars))
        {
            WriteBulkString(chars);
        }
        else if (value.TryGetBytes(sequence: out var bytesSeq))
        {
            WriteBulkString(bytesSeq);
        }
        else if (value.TryGetChars(sequence: out var charsSeq))
        {
            WriteBulkString(charsSeq);
        }
        else
        {
            Throw();
        }

        static void Throw() => throw new InvalidOperationException($"It was not possible to read the {nameof(SimpleString)} contents");
    }
    */

    /// <summary>
    /// Write an integer as a bulk string.
    /// </summary>
    public void WriteBulkString(bool value) => WriteBulkString(value ? 1 : 0);

    /// <summary>
    /// Write a bounded floating point as a bulk string.
    /// </summary>
    public void WriteBulkString(in BoundedDouble value)
    {
        if (value.Inclusive)
        {
            WriteBulkString(value.Value);
        }
        else
        {
            WriteBulkStringExclusive(value.Value);
        }
    }

    /// <summary>
    /// Write a floating point as a bulk string.
    /// </summary>
    public void WriteBulkString(double value) // implicitly: inclusive
    {
        if (value == 0.0 | double.IsNaN(value) | double.IsInfinity(value))
        {
            WriteKnownDoubleInclusive(ref this, value);

            static void WriteKnownDoubleInclusive(ref RespWriter writer, double value)
            {
                if (value == 0.0)
                {
                    writer.WriteRaw("$1\r\n0\r\n"u8);
                }
                else if (double.IsNaN(value))
                {
                    writer.WriteRaw("$3\r\nnan\r\n"u8);
                }
                else if (double.IsPositiveInfinity(value))
                {
                    writer.WriteRaw("$3\r\ninf\r\n"u8);
                }
                else if (double.IsNegativeInfinity(value))
                {
                    writer.WriteRaw("$4\r\n-inf\r\n"u8);
                }
                else
                {
                    Throw();
                    static void Throw() => throw new ArgumentOutOfRangeException(nameof(value));
                }
            }
        }
        else
        {
            Debug.Assert((RespConstants.MaxProtocolBytesBytesNumber + 1) <= 32);
            Span<byte> scratch = stackalloc byte[32];
            if (!Utf8Formatter.TryFormat(value, scratch, out int bytes, G17))
                ThrowFormatException();

            WritePrefixInteger(RespPrefix.BulkString, bytes);
            WriteRaw(scratch.Slice(0, bytes));
            WriteCrLf();
        }
    }

    private void WriteBulkStringExclusive(double value)
    {
        if (value == 0.0 | double.IsNaN(value) | double.IsInfinity(value))
        {
            WriteKnownDoubleExclusive(ref this, value);

            static void WriteKnownDoubleExclusive(ref RespWriter writer, double value)
            {
                if (value == 0.0)
                {
                    writer.WriteRaw("$2\r\n(0\r\n"u8);
                }
                else if (double.IsNaN(value))
                {
                    writer.WriteRaw("$4\r\n(nan\r\n"u8);
                }
                else if (double.IsPositiveInfinity(value))
                {
                    writer.WriteRaw("$4\r\n(inf\r\n"u8);
                }
                else if (double.IsNegativeInfinity(value))
                {
                    writer.WriteRaw("$5\r\n(-inf\r\n"u8);
                }
                else
                {
                    Throw();
                    static void Throw() => throw new ArgumentOutOfRangeException(nameof(value));
                }
            }
        }
        else
        {
            Debug.Assert((RespConstants.MaxProtocolBytesBytesNumber + 1) <= 32);
            Span<byte> scratch = stackalloc byte[32];
            scratch[0] = (byte)'(';
            if (!Utf8Formatter.TryFormat(value, scratch.Slice(1), out int bytes, G17))
                ThrowFormatException();
            bytes++;

            WritePrefixInteger(RespPrefix.BulkString, bytes);
            WriteRaw(scratch.Slice(0, bytes));
            WriteCrLf();
        }
    }

    private static readonly StandardFormat G17 = new('G', 17);

    /// <summary>
    /// Write an integer as a bulk string.
    /// </summary>
    public void WriteBulkString(long value)
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
        else if (Available >= RespConstants.MaxProtocolBytesBulkStringIntegerInt64)
        {
            var singleDigit = value >= -99_999_999 && value <= 999_999_999;
            WriteRawUnsafe((byte)RespPrefix.BulkString);

            var target = Tail.Slice(singleDigit ? 3 : 4); // N\r\n or NN\r\n
            if (!Utf8Formatter.TryFormat(value, target, out var valueBytes))
                ThrowFormatException();

            Debug.Assert(valueBytes > 0 && singleDigit ? valueBytes < 10 : valueBytes is 10 or 11);
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
            Debug.Assert(RespConstants.MaxRawBytesInt64 <= 24);
            Span<byte> scratch = stackalloc byte[24];
            if (!Utf8Formatter.TryFormat(value, scratch, out int bytes))
                ThrowFormatException();
            WritePrefixInteger(RespPrefix.BulkString, bytes);
            WriteRaw(scratch.Slice(0, bytes));
            WriteCrLf();
        }
    }

    /// <summary>
    /// Write an unsigned integer as a bulk string.
    /// </summary>
    public void WriteBulkString(ulong value)
    {
        if (value <= (ulong)long.MaxValue)
        {
            // re-use existing code for most values
            WriteBulkString((long)value);
        }
        else if (Available >= RespConstants.MaxProtocolBytesBulkStringIntegerInt64)
        {
            WriteRaw("$20\r\n"u8);
            if (!Utf8Formatter.TryFormat(value, Tail, out var bytes) || bytes != 20)
                ThrowFormatException();
            _index += 20;
            WriteCrLfUnsafe();
        }
        else
        {
            WriteRaw("$20\r\n"u8);
            Span<byte> scratch = stackalloc byte[20];
            if (!Utf8Formatter.TryFormat(value, scratch, out int bytes) || bytes != 20)
                ThrowFormatException();
            WriteRaw(scratch);
            WriteCrLf();
        }
    }

    private static void ThrowFormatException() => throw new FormatException();

    private void WritePrefixInteger(RespPrefix prefix, int length)
    {
        if (Available >= RespConstants.MaxProtocolBytesIntegerInt32)
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
            Debug.Assert(RespConstants.MaxProtocolBytesIntegerInt32 <= 16);
            Span<byte> buffer = stackalloc byte[16];
            buffer[0] = (byte)prefix;
            int payloadLength;
            if (length >= 0 & length <= 9)
            {
                buffer[1] = (byte)(length + '0');
                payloadLength = 1;
            }
            else if (!Utf8Formatter.TryFormat(length, buffer.Slice(1), out payloadLength))
            {
                ThrowFormatException();
            }

            Unsafe.WriteUnaligned(ref buffer[payloadLength + 1], RespConstants.CrLfUInt16);
            respWriter.WriteRaw(buffer.Slice(0, payloadLength + 3));
        }

        bool writeToStack = Available < RespConstants.MaxProtocolBytesIntegerInt32;

        Span<byte> target = writeToStack ? stackalloc byte[16] : Tail;
        target[0] = (byte)prefix;
    }

    /// <summary>
    /// Write a payload as a bulk string.
    /// </summary>
    /// <param name="value">The payload to write.</param>
    public void WriteBulkString(string value)
    {
        // ReSharper disable once ConditionIsAlwaysTrueOrFalseAccordingToNullableAPIContract
        if (value is null) ThrowNull();
        WriteBulkString(value.AsSpan());
    }

    [MethodImpl(MethodImplOptions.NoInlining), DoesNotReturn]
    // ReSharper disable once NotResolvedInText
    private static void ThrowNull() =>
        // ReSharper disable once NotResolvedInText
        throw new ArgumentNullException("value", "Null values cannot be sent from client to server");

    internal void WriteBulkStringUnoptimized(string? value)
    {
        if (value is null) ThrowNull();
        if (value.Length == 0)
        {
            WriteRaw("$0\r\n\r\n"u8);
        }
        else
        {
            var byteCount = RespConstants.UTF8.GetByteCount(value);
            WritePrefixInteger(RespPrefix.BulkString, byteCount);
            if (Available >= byteCount)
            {
                var actual = RespConstants.UTF8.GetBytes(value.AsSpan(), Tail);
                Debug.Assert(actual == byteCount);
                _index += actual;
            }
            else
            {
                WriteUtf8Slow(value.AsSpan(), byteCount);
            }

            WriteCrLf();
        }
    }

    /// <summary>
    /// Write a payload as a bulk string.
    /// </summary>
    /// <param name="value">The payload to write.</param>
    public void WriteBulkString(ReadOnlyMemory<char> value) => WriteBulkString(value.Span);

    /// <summary>
    /// Write a payload as a bulk string.
    /// </summary>
    /// <param name="value">The payload to write.</param>
    public void WriteBulkString(scoped ReadOnlySpan<char> value)
    {
        if (value.Length == 0)
        {
            if (Available >= 6)
            {
                WriteRawPrechecked(Raw.BulkStringEmpty_6, 6);
            }
            else
            {
                WriteRaw("$0\r\n\r\n"u8);
            }
        }
        else
        {
            var byteCount = RespConstants.UTF8.GetByteCount(value);
            WriteBulkStringHeader(byteCount);
            if (Available >= 2 + byteCount)
            {
                var actual = RespConstants.UTF8.GetBytes(value, Tail);
                Debug.Assert(actual == byteCount);
                _index += actual;
                WriteCrLfUnsafe();
            }
            else
            {
                FlushAndGetBuffer(Math.Min(byteCount, MAX_BUFFER_HINT));
                if (Available >= byteCount + 2)
                {
                    // that'll work
                    var actual = RespConstants.UTF8.GetBytes(value, Tail);
                    Debug.Assert(actual == byteCount);
                    _index += actual;
                    WriteCrLfUnsafe();
                }
                else
                {
                    WriteUtf8Slow(value, byteCount);
                    WriteCrLf();
                }
            }
        }
    }

    private const int MAX_BUFFER_HINT = 64 * 1024;

    private void WriteUtf8Slow(scoped ReadOnlySpan<char> value, int remaining)
    {
        var enc = _perThreadEncoder;
        if (enc is null)
        {
            enc = _perThreadEncoder = RespConstants.UTF8.GetEncoder();
        }
        else
        {
            enc.Reset();
        }

        bool completed;
        int charsUsed, bytesUsed;
        do
        {
            enc.Convert(value, Tail, false, out charsUsed, out bytesUsed, out completed);
            value = value.Slice(charsUsed);
            _index += bytesUsed;
            remaining -= bytesUsed;
            FlushAndGetBuffer(Math.Min(remaining, MAX_BUFFER_HINT));
        }
        // until done...
        while (!completed);

        if (remaining != 0)
        {
            // any trailing data?
            FlushAndGetBuffer(Math.Min(remaining, MAX_BUFFER_HINT));
            enc.Convert(value, Tail, true, out charsUsed, out bytesUsed, out completed);
            Debug.Assert(charsUsed == 0 && completed);
            _index += bytesUsed;
            // ReSharper disable once RedundantAssignment - it is in debug!
            remaining -= bytesUsed;
        }

        enc.Reset();
        Debug.Assert(remaining == 0);
    }

    internal void WriteBulkString(in ReadOnlySequence<byte> value)
    {
        if (value.IsSingleSegment)
        {
#if NETCOREAPP3_0_OR_GREATER
            WriteBulkString(value.FirstSpan);
#else
            WriteBulkString(value.First.Span);
#endif
        }
        else
        {
            // lazy for now
            int len = checked((int)value.Length);
            byte[] buffer = ArrayPool<byte>.Shared.Rent(len);
            value.CopyTo(buffer);
            WriteBulkString(new ReadOnlySpan<byte>(buffer, 0, len));
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    internal void WriteBulkString(in ReadOnlySequence<char> value)
    {
        if (value.IsSingleSegment)
        {
#if NETCOREAPP3_0_OR_GREATER
            WriteBulkString(value.FirstSpan);
#else
            WriteBulkString(value.First.Span);
#endif
        }
        else
        {
            // lazy for now
            int len = checked((int)value.Length);
            char[] buffer = ArrayPool<char>.Shared.Rent(len);
            value.CopyTo(buffer);
            WriteBulkString(new ReadOnlySpan<char>(buffer, 0, len));
            ArrayPool<char>.Shared.Return(buffer);
        }
    }

    /// <summary>
    /// Experimental.
    /// </summary>
    public void WriteBulkString(int value)
    {
        if (Available >= sizeof(ulong))
        {
            switch (value)
            {
                case -1:
                    WriteRawPrechecked(Raw.BulkStringInt32_M1_8, 8);
                    return;
                case 0:
                    WriteRawPrechecked(Raw.BulkStringInt32_0_7, 7);
                    return;
                case 1:
                    WriteRawPrechecked(Raw.BulkStringInt32_1_7, 7);
                    return;
                case 2:
                    WriteRawPrechecked(Raw.BulkStringInt32_2_7, 7);
                    return;
                case 3:
                    WriteRawPrechecked(Raw.BulkStringInt32_3_7, 7);
                    return;
                case 4:
                    WriteRawPrechecked(Raw.BulkStringInt32_4_7, 7);
                    return;
                case 5:
                    WriteRawPrechecked(Raw.BulkStringInt32_5_7, 7);
                    return;
                case 6:
                    WriteRawPrechecked(Raw.BulkStringInt32_6_7, 7);
                    return;
                case 7:
                    WriteRawPrechecked(Raw.BulkStringInt32_7_7, 7);
                    return;
                case 8:
                    WriteRawPrechecked(Raw.BulkStringInt32_8_7, 7);
                    return;
                case 9:
                    WriteRawPrechecked(Raw.BulkStringInt32_9_7, 7);
                    return;
                case 10:
                    WriteRawPrechecked(Raw.BulkStringInt32_10_8, 8);
                    return;
            }
        }

        WriteBulkStringUnoptimized(value);
    }

    internal void WriteBulkStringUnoptimized(int value)
    {
        if (Available >= RespConstants.MaxProtocolBytesBulkStringIntegerInt32)
        {
            var singleDigit = value >= -99_999_999 && value <= 999_999_999;
            WriteRawUnsafe((byte)RespPrefix.BulkString);

            var target = Tail.Slice(singleDigit ? 3 : 4); // N\r\n or NN\r\n
            if (!Utf8Formatter.TryFormat(value, target, out var valueBytes))
                ThrowFormatException();

            Debug.Assert(valueBytes > 0 && singleDigit ? valueBytes < 10 : valueBytes is 10 or 11);
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
            Debug.Assert(RespConstants.MaxRawBytesInt32 <= 16);
            Span<byte> scratch = stackalloc byte[16];
            if (!Utf8Formatter.TryFormat(value, scratch, out int bytes))
                ThrowFormatException();
            WritePrefixInteger(RespPrefix.BulkString, bytes);
            WriteRaw(scratch.Slice(0, bytes));
            WriteCrLf();
        }
    }

    /// <summary>
    /// Write an array header.
    /// </summary>
    /// <param name="count">The number of elements in the array.</param>
    public void WriteArray(int count)
    {
        if (Available >= sizeof(uint))
        {
            switch (count)
            {
                case 0:
                    WriteRawPrechecked(Raw.ArrayPrefix_0_4, 4);
                    return;
                case 1:
                    WriteRawPrechecked(Raw.ArrayPrefix_1_4, 4);
                    return;
                case 2:
                    WriteRawPrechecked(Raw.ArrayPrefix_2_4, 4);
                    return;
                case 3:
                    WriteRawPrechecked(Raw.ArrayPrefix_3_4, 4);
                    return;
                case 4:
                    WriteRawPrechecked(Raw.ArrayPrefix_4_4, 4);
                    return;
                case 5:
                    WriteRawPrechecked(Raw.ArrayPrefix_5_4, 4);
                    return;
                case 6:
                    WriteRawPrechecked(Raw.ArrayPrefix_6_4, 4);
                    return;
                case 7:
                    WriteRawPrechecked(Raw.ArrayPrefix_7_4, 4);
                    return;
                case 8:
                    WriteRawPrechecked(Raw.ArrayPrefix_8_4, 4);
                    return;
                case 9:
                    WriteRawPrechecked(Raw.ArrayPrefix_9_4, 4);
                    return;
                case 10 when Available >= sizeof(ulong):
                    WriteRawPrechecked(Raw.ArrayPrefix_10_5, 5);
                    return;
                case -1:
                    WriteRawPrechecked(Raw.ArrayPrefix_M1_5, 5);
                    return;
            }
        }

        WritePrefixInteger(RespPrefix.Array, count);
    }

    private void WriteBulkStringHeader(int count)
    {
        if (Available >= sizeof(uint))
        {
            switch (count)
            {
                case 0:
                    WriteRawPrechecked(Raw.BulkStringPrefix_0_4, 4);
                    return;
                case 1:
                    WriteRawPrechecked(Raw.BulkStringPrefix_1_4, 4);
                    return;
                case 2:
                    WriteRawPrechecked(Raw.BulkStringPrefix_2_4, 4);
                    return;
                case 3:
                    WriteRawPrechecked(Raw.BulkStringPrefix_3_4, 4);
                    return;
                case 4:
                    WriteRawPrechecked(Raw.BulkStringPrefix_4_4, 4);
                    return;
                case 5:
                    WriteRawPrechecked(Raw.BulkStringPrefix_5_4, 4);
                    return;
                case 6:
                    WriteRawPrechecked(Raw.BulkStringPrefix_6_4, 4);
                    return;
                case 7:
                    WriteRawPrechecked(Raw.BulkStringPrefix_7_4, 4);
                    return;
                case 8:
                    WriteRawPrechecked(Raw.BulkStringPrefix_8_4, 4);
                    return;
                case 9:
                    WriteRawPrechecked(Raw.BulkStringPrefix_9_4, 4);
                    return;
                case 10 when Available >= sizeof(ulong):
                    WriteRawPrechecked(Raw.BulkStringPrefix_10_5, 5);
                    return;
                case -1 when Available >= sizeof(ulong):
                    WriteRawPrechecked(Raw.BulkStringPrefix_M1_5, 5);
                    return;
            }
        }

        WritePrefixInteger(RespPrefix.BulkString, count);
    }

    internal void WriteArrayUnpotimized(int count) => WritePrefixInteger(RespPrefix.Array, count);

    private void WriteRawPrechecked(ulong value, int count)
    {
        Debug.Assert(Available >= sizeof(ulong));
        Debug.Assert(count >= 0 && count <= sizeof(long));
        Unsafe.WriteUnaligned<ulong>(ref WriteHead, value);
        _index += count;
    }

    private void WriteRawPrechecked(uint value, int count)
    {
        Debug.Assert(Available >= sizeof(uint));
        Debug.Assert(count >= 0 && count <= sizeof(uint));
        Unsafe.WriteUnaligned<uint>(ref WriteHead, value);
        _index += count;
    }

    internal void DebugResetIndex() => _index = 0;

    [ThreadStatic]
    // used for multi-chunk encoding
    private static Encoder? _perThreadEncoder;
}
