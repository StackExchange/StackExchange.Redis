using System;
using System.Buffers;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Serialization;
using System.Text;
#pragma warning disable CS1591 // new API

namespace StackExchange.Redis.Protocol;

[Experimental(DiagnosticID)]
public abstract class RespRequest
{
    internal const string DiagnosticID = "SERED001";
    protected RespRequest() { }
    public abstract void Write(ref Resp2Writer writer);
}

[Experimental(RespRequest.DiagnosticID)]
[SuppressMessage("Usage", "CA2231:Overload operator equals on overriding value type Equals", Justification = "API not necessary here")]
public readonly struct RespChunk : IEquatable<RespChunk>
{
    private readonly byte[] _buffer;
    private readonly int _preambleIndex, _payloadIndex, _totalBytes;

    /// <summary>
    /// Compares 2 chunks for equality; note that this uses buffer reference equality - byte contents are not compared.
    /// </summary>
    public bool Equals(RespChunk other)
        => ReferenceEquals(_buffer, other._buffer) && _payloadIndex == other._payloadIndex
        && _preambleIndex == other._preambleIndex && _totalBytes == other._totalBytes;

    /// <inheritdoc cref="Equals(RespChunk)"/>
    public override bool Equals([NotNullWhen(true)] object? obj) => obj is RespChunk other && Equals(other);

    /// <inheritdoc/>
    public override int GetHashCode() => RuntimeHelpers.GetHashCode(_buffer) ^ _preambleIndex ^ _payloadIndex ^ _totalBytes;

    private RespChunk(byte[] buffer, int preambleIndex, int payloadIndex, int totalBytes)
    {
        _buffer = buffer;
        _preambleIndex = preambleIndex;
        _payloadIndex = payloadIndex;
        _totalBytes = totalBytes;
    }

    internal RespChunk(byte[] buffer, int payloadIndex, int totalBytes)
    {
        _buffer = buffer;
        _preambleIndex = _payloadIndex = payloadIndex;
        _totalBytes = totalBytes;
    }

    public bool TryGetSpan(out ReadOnlySpan<byte> span)
    {
        span = _totalBytes == 0 ? default : new(_buffer, _preambleIndex, _totalBytes);
        return true;
    }

    /// <summary>
    /// Gets a text (UTF8) representation of the RESP payload; this API is intended for debugging purposes only, and may
    /// be misleading for non-UTF8 payloads.
    /// </summary>
    public override string ToString()
    {
        if (!TryGetSpan(out var span))
        {
            return nameof(RespChunk);
        }
        if (span.Length == 0) return "";

#if NETCOREAPP3_1_OR_GREATER
        return Resp2Writer.UTF8.GetString(span);
#else
        unsafe
        {
            fixed (byte* ptr = span)
            {
                return Resp2Writer.UTF8.GetString(ptr, span.Length);
            }
        }
#endif
    }

    /// <summary>
    /// Releases all buffers associated with this instance.
    /// </summary>
    public void Recycle()
    {
        var buffer = _buffer;
        // nuke self (best effort to prevent multi-release)
        Unsafe.AsRef(in this) = default;
        if (buffer is not null)
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    /// <summary>
    /// Prepends the given preamble contents 
    /// </summary>
    public RespChunk WithPreamble(ReadOnlySpan<byte> value)
    {
        int length = value.Length, newStart = _preambleIndex - length;
        if (newStart < 0) Throw();
        value.CopyTo(new(_buffer, newStart, length));
        return new(_buffer, newStart, _payloadIndex, _totalBytes + length); 

        static void Throw() => throw new InvalidOperationException("There is insufficient capacity to add the requested preamble");
    }

    /// <summary>
    /// Removes all preamble, reverting to just the original payload
    /// </summary>
    public RespChunk WithoutPreamble() => new RespChunk(_buffer, _payloadIndex, _totalBytes - (_payloadIndex - _preambleIndex));
}

[Experimental(RespRequest.DiagnosticID)]
public ref struct Resp2Writer
{
    private byte[] _targetArr;
    private readonly int _preambleReservation;
    private int _targetIndex, _targetLength, _argCountIncludingCommand, _argIndexIncludingCommand;

    public Resp2Writer(int preambleReservation)
    {
        _targetIndex = _targetLength = _preambleReservation = preambleReservation;
        _argCountIncludingCommand = _argIndexIncludingCommand = 0;
        _targetArr = [];
    }

#if NET7_0_OR_GREATER
    private ref byte _targetArrRoot;
    private readonly Span<byte> RemainingSpan => MemoryMarshal.CreateSpan(ref Unsafe.Add(ref _targetArrRoot, _targetIndex), _targetLength - _targetIndex);
    private readonly ref byte CurrentPosition => ref Unsafe.Add(ref _targetArrRoot, _targetIndex);
    private void AppendUnsafe(char value)
    {
        Debug.Assert(_targetIndex < _targetLength);
        Unsafe.Add(ref _targetArrRoot, _targetIndex++) = (byte)value; // caller must ensure capacity
    }
#else
    private readonly Span<byte> RemainingSpan => new(_targetArr, _targetIndex, _targetLength - _targetIndex);
    private readonly ref byte CurrentPosition => ref _targetArr[_targetIndex];
    private void AppendUnsafe(char value)
    {
        Debug.Assert(_targetIndex < _targetLength);
        _targetArr[_targetIndex++] = (byte)value; // caller must ensure capacity
    }
#endif





    private void EnsureAtLeast(int count)
    {
        if (_targetIndex == _preambleReservation) count += _preambleReservation;
        if (_targetIndex + count > _targetLength)
        {
            AddChunkImpl(Math.Max(count, DEFAULT_CHUNK_SIZE));
        }
    }

    private void EnsureSome(int hint)
    {
        if (_targetIndex == _targetLength)
        {
            if (_targetIndex == _preambleReservation) hint += _preambleReservation;

#if NETCOREAPP3_1_OR_GREATER
            AddChunkImpl(Math.Clamp(DEFAULT_CHUNK_SIZE, hint, MAX_CHUNK_SIZE));
#else
            AddChunkImpl(Math.Min(Math.Max(DEFAULT_CHUNK_SIZE, hint), MAX_CHUNK_SIZE));
#endif
        }
    }

    private const int DEFAULT_CHUNK_SIZE = 1024, MAX_CHUNK_SIZE = 1024 * 1024, DEFAULT_PREAMBLE_BYTES = 64;

    public static int EstimateSize(int value)
        => EstimateSize(value < 0 ? (value == int.MinValue ? (uint)int.MaxValue : (uint)(-value)) : (uint)value);

#if NETCOREAPP3_1_OR_GREATER
    [CLSCompliant(false)]
    public static int EstimateSize(uint value)
    // we can estimate an upper bound just using the LZCNT
        => (32 - BitOperations.LeadingZeroCount(value)) switch
        {
            // 1-digit; 0-7
            0 or 1 or 2 or 3 => 7, // $1\r\nX\r\n
            // 2-digit; 8-63
            4 or 5 or 6 => 8, // $2\r\nXX\r\n
            // 3-digit; 64-511
            7 or 8 or 9 => 9, // $3\r\nXXX\r\n
            // 4-digit; 512-8,191
            10 or 11 or 12 or 13 => 10, // $4\r\nXXXX\r\n
            // 5-digit; 8,192-65,535
            14 or 15 or 16 => 11, // $5\r\nXXXXX\r\n
            // 6-digit; 65,536-524,287
            17 or 18 or 19 => 12, // $6\r\nXXXXXX\r\n
            // 7-digit; 524,288-8,388,607
            20 or 21 or 22 or 23 => 13, // $7\r\nXXXXXXX\r\n
            // 8-digit; 8,388,608-67,108,863
            24 or 25 or 26 => 14, // $8\r\nXXXXXXXX\r\n
            // 9-digit; 67,108,864-536,870,911
            27 or 28 or 29 => 15, // $9\r\nXXXXXXXXX\r\n
            // 10-digit; 536,870,912-4,294,967,295
            _ => 17, // $10\r\nXXXXXXXXXX\r\n
        };

    private static int EstimatePrefixSize(uint value)
    // we can estimate an upper bound just using the LZCNT
    => (32 - BitOperations.LeadingZeroCount(value)) switch
    {
        // 1-digit; 0-7
        0 or 1 or 2 or 3 => 4, // *X\r\n
        // 2-digit; 8-63
        4 or 5 or 6 => 5, // *XX\r\n
        // 3-digit; 64-511
        7 or 8 or 9 => 6, // *XXX\r\n
        // 4-digit; 512-8,191
        10 or 11 or 12 or 13 => 7, // *XXXX\r\n
        // 5-digit; 8,192-65,535
        14 or 15 or 16 => 8, // *XXXXX\r\n
        // 6-digit; 65,536-524,287
        17 or 18 or 19 => 9, // *XXXXXX\r\n
        // 7-digit; 524,288-8,388,607
        20 or 21 or 22 or 23 => 10, // *XXXXXXX\r\n
        // 8-digit; 8,388,608-67,108,863
        24 or 25 or 26 => 11, // *XXXXXXXX\r\n
        // 9-digit; 67,108,864-536,870,911
        27 or 28 or 29 => 12, // *XXXXXXXXX\r\n
        // 10-digit; 536,870,912-4,294,967,295
        _ => 13, // *XXXXXXXXXX\r\n
    };
#else
    [CLSCompliant(false)]
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Style", "IDE0060:Remove unused parameter", Justification = "Parity")]
    public static int EstimateSize(uint value) => 17;
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Style", "IDE0060:Remove unused parameter", Justification = "Parity")]
    private static int EstimatePrefixSize(uint value) => 13;
#endif

    [CLSCompliant(false)]
    public static int EstimateSize(ulong value)
    // we can estimate an upper bound just using the LZCNT
    => value <= uint.MaxValue ? EstimateSize((uint)value) : MaxBytesInt64;

    public static int EstimateSize(string value) => value is null ? NullLength : EstimateSizeBytes((uint)value.Length * MAX_UTF8_BYTES_PER_CHAR);
    public static int EstimateSize(scoped ReadOnlySpan<char> value) => EstimateSizeBytes((uint)value.Length * MAX_UTF8_BYTES_PER_CHAR);
    public static int EstimateSize(byte[] value) => value is null ? NullLength : EstimateSizeBytes((uint)value.Length);
    public static int EstimateSize(scoped ReadOnlySpan<byte> value) => EstimateSizeBytes((uint)value.Length);
    private static int EstimateSizeBytes(uint count) => EstimatePrefixSize(count) + (int)count + 2;

    public const int MaxBytesInt32 = 17, // $10\r\nX10X\r\n
                    MaxBytesInt64 = 26, // $19\r\nX19X\r\n
                    MaxBytesSingle = 27; // $NN\r\nX...X\r\n - note G17 format, allow 20 for payload

    private const int NullLength = 5; // $-1\r\n 

    internal void Recycle()
    {
        var arr = _targetArr;
        this = default; // nuke self to prevent multi-release
        if (arr is not null)
        {
            ArrayPool<byte>.Shared.Return(arr);
        }
    }



    private void AddChunkImpl(int hint)
    {
        uint totalUsed = (uint)(_targetIndex - _preambleReservation);
        // for the first alloc, preamble is already built into the hint
        var newArr = ArrayPool<byte>.Shared.Rent(totalUsed == 0 ? hint : (_targetLength + hint));
        if (totalUsed != 0)
        {
            Unsafe.CopyBlock(ref newArr[_preambleReservation], ref _targetArr[_preambleReservation], totalUsed);
        }
        if (_targetArr is not null)
        {
            ArrayPool<byte>.Shared.Return(_targetArr);
        }
        _targetArr = newArr;
        _targetLength = _targetArr.Length;
#if NET7_0_OR_GREATER
        _targetArrRoot = ref _targetArr[0]; // always to array root; will apply index separately
        Debug.Assert(!Unsafe.IsNullRef(ref _targetArrRoot));
#endif
    }

    internal static readonly UTF8Encoding UTF8 = new(false);

    [System.Diagnostics.CodeAnalysis.SuppressMessage("ApiDesign", "RS0026:Do not add multiple public overloads with optional parameters", Justification = "Overloaded by type, acceptable")]
    public void WriteCommand(string command, int argCount, int argBytesEstimate = 0)
        => WriteCommand(command.AsSpan(), argCount, argBytesEstimate);

    private const int MAX_UTF8_BYTES_PER_CHAR = 4, MAX_CHARS_FOR_STACKALLOC_ENCODE = 64,
        ENCODE_STACKALLOC_BYTES = MAX_CHARS_FOR_STACKALLOC_ENCODE * MAX_UTF8_BYTES_PER_CHAR;

    [System.Diagnostics.CodeAnalysis.SuppressMessage("ApiDesign", "RS0026:Do not add multiple public overloads with optional parameters", Justification = "Overloaded by type, acceptable")]
    public void WriteCommand(scoped ReadOnlySpan<char> command, int argCount, int argBytesEstimate = 0)
    {
        if (command.Length <= MAX_CHARS_FOR_STACKALLOC_ENCODE)
        {
            WriteCommand(Utf8Encode(command, stackalloc byte[ENCODE_STACKALLOC_BYTES]), argCount, argBytesEstimate);
        }
        else
        {
            WriteCommandSlow(ref this, command, argCount, argBytesEstimate);
        }

        static void WriteCommandSlow(ref Resp2Writer @this, scoped ReadOnlySpan<char> command, int argCount, int argBytesEstimate)
        {
            @this.WriteCommand(Utf8EncodeLease(command, out var lease), argCount, argBytesEstimate);
            ArrayPool<byte>.Shared.Return(lease);
        }
    }

    private static unsafe ReadOnlySpan<byte> Utf8Encode(scoped ReadOnlySpan<char> source, Span<byte> target)
    {
        int len;
#if NETCOREAPP3_1_OR_GREATER
        len = UTF8.GetBytes(source, target);
#else
        fixed (byte* bPtr = target)
        fixed (char* cPtr = source)
        {
            len = UTF8.GetBytes(cPtr, source.Length, bPtr, target.Length);
        }
#endif
        return target.Slice(0, len);
    }
    private static ReadOnlySpan<byte> Utf8EncodeLease(scoped ReadOnlySpan<char> value, out byte[] arr)
    {
        arr = ArrayPool<byte>.Shared.Rent(MAX_UTF8_BYTES_PER_CHAR * value.Length);
        int len;
#if NETCOREAPP3_1_OR_GREATER
        len = UTF8.GetBytes(value, arr);
#else
        unsafe
        {
            fixed (char* cPtr = value)
            fixed (byte* bPtr = arr)
            {
                len = UTF8.GetBytes(cPtr, value.Length, bPtr, arr.Length);
            }
        }
#endif
        return new ReadOnlySpan<byte>(arr, 0, len);
    }
    internal readonly void AssertFullyWritten()
    {
        if (_argCountIncludingCommand != _argIndexIncludingCommand) Throw(_argIndexIncludingCommand, _argCountIncludingCommand);

        static void Throw(int count, int total) => throw new InvalidOperationException($"Not all command arguments ({count - 1} of {total - 1}) have been written");
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("ApiDesign", "RS0026:Do not add multiple public overloads with optional parameters", Justification = "Overloaded by type, acceptable")]
    public void WriteCommand(scoped ReadOnlySpan<byte> command, int argCount, int argBytesEstimate = 0)
    {
        if (_argCountIncludingCommand > 0) ThrowCommandAlreadyWritten();
        if (command.IsEmpty) ThrowEmptyCommand();
        if (argCount < 0) ThrowNegativeArgs();
        if (argBytesEstimate <= 0) argBytesEstimate = 32 * argCount;
        _argCountIncludingCommand = argCount + 1;
        _argIndexIncludingCommand = 0;

        // get a rough estimate and allocate an initial buffer
        int argCountLenEstimate = EstimatePrefixSize((uint)(argCount + 1));
        var estimatedSize = argCountLenEstimate + EstimateSize(command) + argBytesEstimate;
        EnsureSome(estimatedSize);
        EnsureAtLeast(argCountLenEstimate); // this will *almost always* be a no-op; would need to have a preamble in
        // excess of MAX_CHUNK_SIZE for EnsureSome to have not allocated capacity; we will never do this, obviously

        // format:
        // *{totalargs}\r\n
        // ${cmdbytes}\r\n{cmd}\r\n
        // {other args}

        AppendUnsafe('*');
        _targetIndex += WriteCountPrefix(argCount + 1, RemainingSpan);
        WriteValue(command);
        Debug.Assert(_argIndexIncludingCommand == 1);


        static void ThrowCommandAlreadyWritten() => throw new InvalidOperationException(nameof(WriteCommand) + " can only be called once");
        static void ThrowEmptyCommand() => throw new ArgumentOutOfRangeException(nameof(command), "command cannot be empty");
        static void ThrowNegativeArgs() => throw new ArgumentOutOfRangeException(nameof(argCount), "argCount cannot be negative");
    }

    private static int WriteCountPrefix(int count, Span<byte> target)
    {
        var len = Format.FormatInt32(count, target);
        Debug.Assert(target.Length >= len + 2);
        UnsafeWriteCrlf(ref target[len]);
        return len + 2;
    }

    private void WriteNullString() // private because I don't think this is allowed in client streams? check
        => WriteRaw("$-1\r\n"u8);

    private void WriteEmptyString() // private because I don't think this is allowed in client streams? check
        => WriteRaw("$0\r\n\r\n"u8);

    private void WriteRaw(scoped ReadOnlySpan<byte> value)
    {
        EnsureAtLeast(value.Length);
        value.CopyTo(RemainingSpan);
        _targetIndex += value.Length;
    }

    private void AddArg()
    {
        if (_argIndexIncludingCommand >= _argCountIncludingCommand) ThrowAllWritten(_argCountIncludingCommand);
        _argIndexIncludingCommand++;

        static void ThrowAllWritten(int advertised) => throw new InvalidOperationException($"All command arguments ({advertised - 1}) have already been written");
    }
    public void WriteValue(scoped ReadOnlySpan<byte> value)
    {
        AddArg();
        if (value.IsEmpty)
        {
            WriteEmptyString();
            return;
        }

        EnsureAtLeast(EstimatePrefixSize((uint)value.Length));
        AppendUnsafe('$');
        _targetIndex += WriteCountPrefix(value.Length, RemainingSpan);

        while (!value.IsEmpty)
        {
            EnsureSome(value.Length);
            var buffer = RemainingSpan;
            if (value.Length <= buffer.Length)
            {
                // we can write everything
                value.CopyTo(buffer);
                _targetIndex += value.Length;
                break; // done
            }

            // write what we can
            value.Slice(0, buffer.Length).CopyTo(buffer);
            _targetIndex += value.Length;
            value = value.Slice(buffer.Length);
        }

        EnsureAtLeast(2);
        UnsafeWriteCrlf(ref CurrentPosition);
        _targetIndex += 2;
    }

    private static readonly ushort Crlf = BitConverter.IsLittleEndian ? (ushort)0x0A0D : (ushort)0x0D0A;

    // unsafe===caller **MUST** ensure there is capacity
    private static void UnsafeWriteCrlf(ref byte destination) => Unsafe.WriteUnaligned(ref destination, Crlf);

    public void WriteValue(scoped ReadOnlySpan<char> value)
    {
        if (value.Length == 0)
        {
            AddArg();
            WriteEmptyString();
        }
        else if (value.Length <= MAX_CHARS_FOR_STACKALLOC_ENCODE)
        {
            WriteValue(Utf8Encode(value, stackalloc byte[ENCODE_STACKALLOC_BYTES]));
        }
        else
        {
            WriteValue(Utf8EncodeLease(value, out var lease));
            ArrayPool<byte>.Shared.Return(lease);
        }
    }

    public void WriteValue(string value)
    {
        if (value is null)
        {
            AddArg();
            WriteNullString();
        }
        else WriteValue(value.AsSpan());
    }

    internal RespChunk Commit()
    {
        var chunk = new RespChunk(_targetArr, _preambleReservation, _targetIndex - _preambleReservation);
        this = default; // nuke self; transferring ownership to the chunk
        return chunk;
    }
}
