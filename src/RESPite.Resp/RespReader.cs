using RESPite.Internal;
using System;
using System.Buffers;
using System.Buffers.Text;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using static RESPite.Internal.Constants;
using System.Text;
using RESPite.Messages;
namespace RESPite.Resp;


/// <summary>
/// Base implementation for RESP writers that do not depend on the request parameter
/// </summary>
public abstract class RespReaderBase<TResponse> : IReader<Empty, TResponse>
{
    TResponse IReader<Empty, TResponse>.Read(in Empty request, in ReadOnlySequence<byte> content)
        => Read(content);

    /// <summary>
    /// Read a raw RESP payload
    /// </summary>
    public virtual TResponse Read(scoped in ReadOnlySequence<byte> content)
    {
        var reader = new RespReader(in content);
        if (!reader.TryReadNext()) RespReader.ThrowEOF();
        if (reader.IsError) throw reader.ReadError();
        return Read(ref reader);
    }

    /// <summary>
    /// Read a RESP payload via the <see cref="RespReader"/> API
    /// </summary>
    public virtual TResponse Read(ref RespReader reader)
        => throw new NotSupportedException("A " + nameof(Read) + " overload must be overridden");
}

/// <summary>
/// Base implementation for RESP writers that do depend on the request parameter
/// </summary>
public abstract class RespReaderBase<TRequest, TResponse> : IReader<TRequest, TResponse>
{
    /// <summary>
    /// Read a raw RESP payload
    /// </summary>
    public virtual TResponse Read(in TRequest request, in ReadOnlySequence<byte> content)
    {
        var reader = new RespReader(in content);
        if (!reader.TryReadNext()) RespReader.ThrowEOF();
        if (reader.IsError) throw reader.ReadError();
        return Read(in request, ref reader);
    }

    /// <summary>
    /// Read a RESP payload via the <see cref="RespReader"/> API
    /// </summary>
    public virtual TResponse Read(in TRequest request, ref RespReader reader)
        => throw new NotSupportedException("A " + nameof(Read) + " overload must be overridden");
}

/// <summary>
/// Low-level RESP reading API
/// </summary>
public ref struct RespReader
{
    private readonly ReadOnlySequence<byte> _fullPayload;
    private SequencePosition _segPos;
    private long _positionBase;
    private int _bufferIndex; // after TryRead, this should be positioned immediately before the actual data
    private int _bufferLength;
    private int _length; // for null: -1; for scalars: the length of the payload; for aggregates: the child count
    private RespPrefix _prefix;
    /// <summary>
    /// Returns the position after the end of the current element
    /// </summary>
    public readonly long BytesConsumed => _positionBase + _bufferIndex + TrailingLength;

    //internal int DebugBufferIndex => _bufferIndex;

    /// <summary>
    /// Indicates the payload kind of the current element
    /// </summary>
    public readonly RespPrefix Prefix
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _prefix;
    }

    /// <summary>
    /// Returns as much data as possible into the buffer, ignoring
    /// any data that cannot fit into <paramref name="target"/>, and
    /// returning the segment representing copied data.
    /// </summary>
    public readonly Span<byte> CopyTo(Span<byte> target)
    {
        if (!IsScalar) return default; // only possible for scalars
        if (TryGetValueSpan(out var source))
        {
            if (source.Length > target.Length)
            {
                source = source.Slice(0, target.Length);
            }
            else if (source.Length < target.Length)
            {
                target = target.Slice(0, source.Length);
            }
            source.CopyTo(target);
            return target;
        }
        throw new NotImplementedException();
    }

    /// <summary>
    /// Returns true if the value is a valid scalar value <em>that is available as a single contiguous chunk</em>;
    /// a value could be a valid scalar but if it spans segments, this will report <c>false</c>; alternative APIs
    /// are available to inspect the value.
    /// </summary>
    internal readonly bool TryGetValueSpan(out ReadOnlySpan<byte> span)
    {
        if (!IsScalar | _length < 0)
        {
            span = default;
            return false; // only possible for scalars
        }
        if (_length == 0)
        {
            span = default;
            return true;
        }

        if (_bufferIndex + _length <= _bufferLength)
        {
#if NET7_0_OR_GREATER
            span = MemoryMarshal.CreateReadOnlySpan(ref Unsafe.Add(ref _bufferRoot, _bufferIndex), _length);
#else
            span = _bufferSpan.Slice(_bufferIndex, _length);
#endif
            return true;
        }

        // not available as a convenient contiguous chunk
        span = default;
        return false;
    }

    /// <summary>
    /// The a scalar integer value
    /// </summary>
    public readonly int ReadInt32()
    {
        DemandScalar();
        if (_length > MaxRawBytesInt32) ThrowFormatException();
        if (TryGetValueSpan(out var span))
        {
            if (!(Utf8Parser.TryParse(span, out int value, out int bytes) & bytes == span.Length))
            {
                ThrowFormatException();
            }
            return value;
        }
        return ReadInt32Slow();
    }

    private readonly int ReadInt32Slow()
    {
        Span<byte> buffer = stackalloc byte[_length]; // we already checked vs MaxRawBytesInt32
        if (new SlowReader(in this).Fill(buffer) != _length) ThrowEOF();
        if (!(Utf8Parser.TryParse(buffer.Slice(0, _length), out int value, out int bytes) & bytes == _length))
        {
            ThrowFormatException();
        }
        return value;
    }

    private static void ThrowFormatException() => throw new FormatException();

    private readonly void DemandScalar()
    {
        if (!IsScalar) Throw(Prefix);
        static void Throw(RespPrefix prefix) => throw new InvalidOperationException($"Scalar value expected; got {prefix}");
    }

    /// <summary>
    /// The a scalar string value
    /// </summary>
    public readonly string? ReadString()
    {
        DemandScalar();
        if (_length < 0) return null;
        if (_length == 0) return "";
        if (TryGetValueSpan(out var span))
        {
            return UTF8.GetString(span);
        }
        return SlowReadString();
    }
    private readonly string SlowReadString()
    {
        // simple cases and pre-conditions already checked
        byte[]? lease = null;
        Span<byte> buffer = _length <= 128 ? stackalloc byte[128] : new(lease = ArrayPool<byte>.Shared.Rent(_length), 0, _length);
        var reader = new SlowReader(in this);
        var len = reader.Fill(buffer);
        Debug.Assert(len == _length);
        var s = UTF8.GetString(buffer);
        if (lease is not null) ArrayPool<byte>.Shared.Return(lease);
        return s;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void AssertClLfUnsafe(scoped ref byte source, int offset)
    {
        if (Unsafe.ReadUnaligned<ushort>(ref Unsafe.Add(ref source, offset)) != CrLfUInt16)
        {
            ThrowProtocolFailure("Expected CR/LF");
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void AssertClLfUnsafe(scoped ref readonly byte source)
    {
        if (Unsafe.ReadUnaligned<ushort>(ref Unsafe.AsRef(in source)) != CrLfUInt16)
        {
            ThrowProtocolFailure("Expected CR/LF");
        }
    }

#if NET7_0_OR_GREATER
    private ref byte _bufferRoot;
    private readonly ref byte CurrentUnsafe => ref Unsafe.Add(ref _bufferRoot, _bufferIndex);
    private readonly RespPrefix PeekPrefix() => (RespPrefix)Unsafe.Add(ref _bufferRoot, _bufferIndex);
    private readonly ReadOnlySpan<byte> PeekPastPrefix() => MemoryMarshal.CreateReadOnlySpan(
        ref Unsafe.Add(ref _bufferRoot, _bufferIndex + 1), _bufferLength - (_bufferIndex + 1));
    private readonly ReadOnlySpan<byte> PeekCurrent() => MemoryMarshal.CreateReadOnlySpan(
        ref Unsafe.Add(ref _bufferRoot, _bufferIndex), _bufferLength - _bufferIndex);
    private readonly void AssertCrlfPastPrefixUnsafe(int offset) => AssertClLfUnsafe(ref _bufferRoot, _bufferIndex + offset + 1);
    private void SetCurrent(ReadOnlySpan<byte> current)
    {
        _positionBase += _bufferLength; // accumulate previous length
        _bufferIndex = 0;
        _bufferLength = current.Length;
        _bufferRoot = ref MemoryMarshal.GetReference(current);
    }
#else
    private ReadOnlySpan<byte> _bufferSpan;
    private readonly ref byte CurrentUnsafe => ref Unsafe.AsRef(in _bufferSpan[_bufferIndex]);
    private readonly RespPrefix PeekPrefix() => (RespPrefix)_bufferSpan[_bufferIndex];
    private readonly ReadOnlySpan<byte> PeekCurrent() => _bufferSpan.Slice(_bufferIndex);
    private readonly ReadOnlySpan<byte> PeekPastPrefix() => _bufferSpan.Slice(_bufferIndex + 1);
    private readonly void AssertCrlfPastPrefixUnsafe(int offset)
        => AssertClLfUnsafe(in _bufferSpan[_bufferIndex + offset + 1]);
    private void SetCurrent(ReadOnlySpan<byte> current)
    {
        _positionBase += _bufferLength; // accumulate previous length
        _bufferIndex = 0;
        _bufferLength = current.Length;
        _bufferSpan = current;
    }
#endif

    /// <summary>
    /// Read a RESP fragment
    /// </summary>
    public RespReader(byte[] value, int start = 0, int length = -1) : this(new ReadOnlySpan<byte>(value, start, length < 0 ? value.Length - start : length)) { }
    /// <summary>
    /// Read a RESP fragment
    /// </summary>
    public RespReader(ReadOnlyMemory<byte> value) : this(value.Span) { }
    /// <summary>
    /// Read a RESP fragment
    /// </summary>
    public RespReader(ReadOnlySpan<byte> value)
    {
        _fullPayload = default;
        _positionBase = _bufferIndex = _bufferLength = 0;
        _length = -1;
        _prefix = RespPrefix.None;
#if NET7_0_OR_GREATER
        _bufferRoot = ref Unsafe.NullRef<byte>();
#else
        _bufferSpan = default;
#endif
        _segPos = default;
        SetCurrent(value);
    }
    /// <summary>
    /// Read a RESP fragment
    /// </summary>
    public RespReader(scoped in ReadOnlySequence<byte> value)
    {
        _fullPayload = value;
        _positionBase = _bufferIndex = _bufferLength = 0;
        _length = -1;
        _prefix = RespPrefix.None;
#if NET7_0_OR_GREATER
        _bufferRoot = ref Unsafe.NullRef<byte>();
#else
        _bufferSpan = default;
#endif
        if (value.IsSingleSegment)
        {
            _segPos = default;
#if NETCOREAPP3_1_OR_GREATER
            SetCurrent(value.FirstSpan);
#else
            SetCurrent(value.First.Span);
#endif
        }
        else
        {
            _segPos = value.Start;
            if (value.TryGet(ref _segPos, out var current))
            {
                SetCurrent(current.Span);
            }
        }
    }

    /// <summary>
    /// Indicates the length of the current scalar element
    /// </summary>
    public readonly int ScalarLength
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => Prefix switch
        {
            RespPrefix.SimpleString or RespPrefix.SimpleError or RespPrefix.Integer
            or RespPrefix.Boolean or RespPrefix.Double or RespPrefix.BigNumber
            or RespPrefix.BulkError or RespPrefix.BulkString or RespPrefix.VerbatimString when _length > 0 => _length,
            _ => 0,
        };
    }

    /// <summary>
    /// Indicates the number of child elements of the current aggregate element
    /// </summary>
    public readonly int ChildCount
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => Prefix switch
        {
            RespPrefix.Array or RespPrefix.Set or RespPrefix.Push when _length > 0 => _length,
            RespPrefix.Map when _length > 0 => 2 * _length,
            _ => 0,
        };
    }

    /// <summary>
    /// Indicates a type with a discreet value - string, integer, etc - <see cref="TryGetValueSpan(out ReadOnlySpan{byte})"/>,
    /// <see cref="Is(ReadOnlySpan{byte})"/>, <see cref="CopyTo(Span{byte})"/> etc are meaningful
    /// </summary>
    public readonly bool IsScalar
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => Prefix switch
        {
            RespPrefix.SimpleString or RespPrefix.SimpleError or RespPrefix.Integer
            or RespPrefix.Boolean or RespPrefix.Double or RespPrefix.BigNumber
            or RespPrefix.BulkError or RespPrefix.BulkString or RespPrefix.VerbatimString => true,
            _ => false,
        };
    }

    /// <summary>
    /// Indicates if the payload represents a RESP error
    /// </summary>
    public readonly bool IsError
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => Prefix is RespPrefix.BulkError or RespPrefix.SimpleError;
    }

    internal readonly Exception ReadError()
    {
        var message = ReadString();
        if (string.IsNullOrWhiteSpace(message)) message = "unknown RESP error";
        return new RespException(message!);
    }

    /// <summary>
    /// Indicates a collection type - array, set, etc - <see cref="ChildCount"/>, <see cref="SkipChildren()"/> are are meaningful
    /// </summary>
    public readonly bool IsAggregate
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => Prefix switch
        {
            RespPrefix.Array or RespPrefix.Set or RespPrefix.Map or RespPrefix.Push => true,
            _ => false,
        };
    }

    private static bool TryReadIntegerCrLf(ReadOnlySpan<byte> bytes, out int value, out int byteCount)
    {
        var end = bytes.IndexOf(CrlfBytes);
        if (end < 0)
        {
            byteCount = value = 0;
            if (bytes.Length >= MaxRawBytesInt32 + 2)
            {
                ThrowProtocolFailure("Unterminated or over-length integer"); // should have failed; report failure to prevent infinite loop
            }
            return false;
        }
        if (!(Utf8Parser.TryParse(bytes, out value, out byteCount) && byteCount == end))
            ThrowProtocolFailure("Unable to parse integer");
        byteCount += 2; // include the CrLf
        return true;
    }

    private static void ThrowProtocolFailure(string message)
        => throw new InvalidOperationException("RESP protocol failure: " + message); // protocol exception?

    /// <summary>
    /// Indicates whether the current element is a null value
    /// </summary>
    public readonly bool IsNull
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _length < 0;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void ResetCurrent()
    {
        _prefix = RespPrefix.None;
        _length = -1;
    }

    private void AdvanceSlow(long bytes)
    {
        while (bytes > 0)
        {
            var available = _bufferLength - _bufferIndex;
            if (bytes <= available)
            {
                _bufferIndex += (int)bytes;
                return;
            }
            bytes -= available;
            if (_fullPayload.IsSingleSegment || !_fullPayload.TryGet(ref _segPos, out var next))
            {
                throw new EndOfStreamException();
            }
            SetCurrent(next.Span);
        }
    }

    /// <summary>
    /// Body length of scalar values, plus any terminating sentinels
    /// </summary>
    private readonly int TrailingLength
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => IsScalar && _length >= 0 ? _length + 2 : 0;
    }

    /// <summary>
    /// Attempt to move to the next RESP element, and assert that the prefix matches
    /// </summary>
    public bool TryReadNext(RespPrefix demand)
    {
        if (TryReadNext())
        {
            if (Prefix != demand) Throw(demand, Prefix);
            return true;
        }
        return false;
        static void Throw(RespPrefix expected, RespPrefix actual)
            => throw new InvalidOperationException($"Expected {expected}, got {actual}");
    }

    /// <summary>
    /// Attempt to move to the next RESP element
    /// </summary>
    public bool TryReadNext()
    {
        var skip = TrailingLength;
        if (_bufferIndex + skip <= _bufferLength)
        {
            _bufferIndex += skip; // available in the current buffer
        }
        else
        {
            AdvanceSlow(skip);
        }
        ResetCurrent();

        if (_bufferIndex + 3 <= _bufferLength) // shortest possible RESP fragment is length 3
        {
            switch (_prefix = PeekPrefix())
            {
                case RespPrefix.SimpleString:
                case RespPrefix.SimpleError:
                case RespPrefix.Integer:
                case RespPrefix.Boolean:
                case RespPrefix.Double:
                case RespPrefix.BigNumber:
                    // CRLF-terminated
                    _length = PeekPastPrefix().IndexOf(CrlfBytes);
                    if (_length < 0) break; // can't find, need more data
                    _bufferIndex++; // skip past prefix (payload follows directly)
                    return true;
                case RespPrefix.BulkError:
                case RespPrefix.BulkString:
                case RespPrefix.VerbatimString:
                    // length prefix with value payload
                    var remaining = PeekPastPrefix();
                    if (!TryReadIntegerCrLf(remaining, out _length, out int consumed)) break;
                    if (_length >= 0) // not null (nulls don't have second CRLF)
                    {
                        // still need to valid terminating CRLF
                        if (remaining.Length < consumed + _length + 2) break; // need more data
                        AssertCrlfPastPrefixUnsafe(consumed + _length);
                    }
                    _bufferIndex += 1 + consumed;
                    return true;
                case RespPrefix.Array:
                case RespPrefix.Set:
                case RespPrefix.Map:
                case RespPrefix.Push:
                    // length prefix without value payload (child values follow)
                    if (!TryReadIntegerCrLf(PeekPastPrefix(), out _length, out consumed)) break;
                    _bufferIndex += consumed + 1;
                    return true;
                case RespPrefix.Null: // null
                    // note we already checked we had 3 bytes
                    AssertCrlfPastPrefixUnsafe(0);
                    _length = -1;
                    _bufferIndex += 3; // skip prefix+terminator
                    return true;
                default:
                    ThrowProtocolFailure("Unexpected protocol prefix: " + _prefix);
                    return false;
            }
        }

        return TryReadNextSlow();
    }

    /// <inheritdoc/>
    public override readonly string ToString()
    {
        if (IsScalar) return IsNull ? $"@{BytesConsumed} {Prefix}: {nameof(RespPrefix.Null)}" : $"@{BytesConsumed} {Prefix} with {ScalarLength} bytes '{ReadString()}'";
        if (IsAggregate) return IsNull ? $"@{BytesConsumed} {Prefix}: {nameof(RespPrefix.Null)}" : $"@{BytesConsumed} {Prefix} with {ChildCount} sub-items";
        return $"@{BytesConsumed} {Prefix}";
    }

    private bool NeedMoreData()
    {
        ResetCurrent();
        return false;
    }
    private bool TryReadNextSlow()
    {
        ResetCurrent();
        var reader = new SlowReader(in this);
        int next = reader.TryRead();
        if (next < 0) return NeedMoreData();
        switch (_prefix = (RespPrefix)next)
        {
            case RespPrefix.SimpleString:
            case RespPrefix.SimpleError:
            case RespPrefix.Integer:
            case RespPrefix.Boolean:
            case RespPrefix.Double:
            case RespPrefix.BigNumber:
                // CRLF-terminated
                if (!reader.TryFindCrLfWithoutMoving(out _length)) return NeedMoreData();
                break;
            case RespPrefix.BulkError:
            case RespPrefix.BulkString:
            case RespPrefix.VerbatimString:
                // length prefix with value payload
                if (!reader.TryReadLengthCrLf(out _length)) return NeedMoreData();
                if (!reader.TryAssertBytesCrLfWithoutMoving(_length)) return NeedMoreData();
                break;
            case RespPrefix.Array:
            case RespPrefix.Set:
            case RespPrefix.Map:
            case RespPrefix.Push:
                // length prefix without value payload (child values follow)
                if (!reader.TryReadLengthCrLf(out _length)) return NeedMoreData();
                break;
            case RespPrefix.Null: // null
                if (!reader.TryReadCrLf()) return NeedMoreData();
                break;
            default:
                ThrowProtocolFailure("Unexpected protocol prefix: " + _prefix);
                return NeedMoreData();
        }
        AdvanceSlow(reader.TotalConsumed);
        return true;
    }

    private ref partial struct SlowReader
    {
        public SlowReader(in RespReader reader)
        {
#if NET7_0_OR_GREATER
            _full = ref reader._fullPayload;
#else
            _full = reader._fullPayload;
#endif
            _segPos = reader._segPos;
            _current = reader.PeekCurrent();
            _totalBase = _index = 0;
            DebugAssertValid();
        }

        [Conditional("DEBUG")]
        readonly partial void DebugAssertValid();
#if DEBUG
        readonly partial void DebugAssertValid()
        {
            Debug.Assert(_index >= 0 && _index <= _current.Length);
        }
#endif

        private bool TryAdvanceToData()
        {
            DebugAssertValid();
            while (CurrentRemainingBytes == 0)
            {
                if (_full.IsSingleSegment || !_full.TryGet(ref _segPos, out var next))
                {
                    return false;
                }
                _totalBase += _current.Length; // accumulate prior
                _current = next.Span;
                _index = 0;
            }
            DebugAssertValid();
            return true;
        }

        public int TryRead()
        {
            if (CurrentRemainingBytes == 0 && !TryAdvanceToData()) return -1;
            return _current[_index++];
        }

        private int CurrentRemainingBytes
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => _current.Length - _index;
        }
        internal bool TryReadCrLf() // assert and advance
        {
            DebugAssertValid();
            if (CurrentRemainingBytes >= 2)
            {
                AssertClLfUnsafe(in _current[_index]);
                _index += 2;
                return true;
            }

            var x = TryRead();
            if (x < 0) return false;
            if (x != '\r') ThrowProtocolFailure("Expected CR/LF");
            x = TryRead();
            if (x < 0) return false;
            if (x != '\n') ThrowProtocolFailure("Expected CR/LF");
            return true;
        }

        internal bool TryReadLengthCrLf(out int length)
        {
            if (CurrentRemainingBytes >= MaxRawBytesInt32 + 2)
            {
                if (TryReadIntegerCrLf(_current.Slice(_index), out length, out int consumed))
                {
                    _index += consumed;
                    return true;
                }
            }
            else
            {
                Span<byte> buffer = stackalloc byte[MaxRawBytesInt32 + 2];
                SlowReader snapshot = this; // we might over-advance when filling the buffer
                length = snapshot.Fill(buffer);
                if (TryReadIntegerCrLf(buffer.Slice(0, length), out length, out int consumed))
                {
                    // we expect this to work - we just aw the bytes!
                    if (!TryAdvance(consumed)) Throw();
                    return true;

                    static void Throw() => throw new InvalidOperationException("Unexpected failure to advance in " + nameof(TryReadLengthCrLf));
                }
            }
            return false;
        }

        internal int Fill(scoped Span<byte> buffer)
        {
            DebugAssertValid();
            int total = 0;
            while (buffer.Length > 0 && TryAdvanceToData())
            {
                int available = CurrentRemainingBytes;
                if (available >= buffer.Length)
                {
                    // we have enough to finish
                    _current.Slice(_index, buffer.Length).CopyTo(buffer);
                    _index += buffer.Length;
                    total += buffer.Length;
                    break;
                }

                // not enough; copy what we have
                var source = _current.Slice(_index);
                source.CopyTo(buffer);
                _index += available;
                total += available;
                buffer = buffer.Slice(available);
            }
            DebugAssertValid();
            return total;
        }

        private bool TryAdvance(int bytes)
        {
            DebugAssertValid();
            while (bytes > 0 && TryAdvanceToData())
            {
                var available = CurrentRemainingBytes;
                if (bytes <= available)
                {
                    _index += bytes;
                    return true;
                }
                _index += available;
                bytes -= available;
            }
            DebugAssertValid();
            return bytes == 0;
        }

        internal readonly bool TryAssertBytesCrLfWithoutMoving(int length)
        {
            SlowReader copy = this;
            return copy.TryAdvance(length) && copy.TryReadCrLf();
        }

        internal readonly bool TryFindCrLfWithoutMoving(out int length)
        {
            DebugAssertValid();
            SlowReader copy = this; // don't want to advance
            length = 0;
            while (copy.TryAdvanceToData())
            {
                var index = copy._current.Slice(copy._index).IndexOf((byte)'\r');
                if (index >= 0)
                {
                    length += index;
                    if (!(copy.TryAdvance(index) && copy.TryReadCrLf())) return false;
                    return true;
                }
                var scanned = copy.CurrentRemainingBytes;
                length += scanned;
                copy._index += scanned;
            }
            return false;
        }

#if NET7_0_OR_GREATER
        private readonly ref readonly ReadOnlySequence<byte> _full;
#else
        private readonly ReadOnlySequence<byte> _full;
#endif
        private SequencePosition _segPos;
        private ReadOnlySpan<byte> _current;
        private int _index;
        private long _totalBase;
        public long TotalConsumed => _totalBase + _index;

        internal bool StartsWith(scoped ReadOnlySpan<byte> value)
        {
            while (value.Length > 0 && TryAdvanceToData())
            {
                int available = CurrentRemainingBytes;
                if (available >= value.Length)
                {
                    // we have enough to finish
                    return _current.StartsWith(value);
                }

                // not enough; test what we have
                var source = _current.Slice(_index);
                if (!source.SequenceEqual(value.Slice(0, available)))
                    return false;
                _index += available;
                value = value.Slice(available);
            }
            return value.IsEmpty;
        }
    }

    /// <summary>Performs a byte-wise equality check on the payload</summary>
    public readonly bool Is(ReadOnlySpan<byte> value)
    {
        if (!(IsScalar && value.Length == _length)) return false;
        if (TryGetValueSpan(out var span))
        {
            return span.SequenceEqual(value);
        }
        return IsSlow(value);
    }
    private readonly bool IsSlow(ReadOnlySpan<byte> value) => value.Length == ScalarLength && new SlowReader(in this).StartsWith(value);

    internal readonly bool IsOK() // go mad with this, because it is used so often
        => _length == 2 & _bufferIndex + 2 <= _bufferLength // single-buffer fast path - can we safely read 2 bytes?
        ? Prefix == RespPrefix.SimpleString & Unsafe.ReadUnaligned<ushort>(ref CurrentUnsafe) == OK
        : IsOKSlow();

    [MethodImpl(MethodImplOptions.NoInlining)]
    private readonly bool IsOKSlow() => _length == 2 && Prefix == RespPrefix.SimpleString && IsSlow("OK"u8);

    // note this should be treated as "const" by modern JIT
    private static readonly ushort OK = UnsafeCpuUInt16("OK"u8);

    /// <summary>
    /// Skips all child/descendent nodes of this element, returning the number
    /// of elements skipped
    /// </summary>
    public int SkipChildren()
    {
        int remaining = ChildCount, total = 0;
        while (remaining > 0 && TryReadNext())
        {
            total++;
            remaining = remaining - 1 + ChildCount;
        }
        if (remaining != 0) ThrowEOF();
        if (total != 0)
        {
            ResetCurrent(); // would be confusing to see the last descendent state
        }
        return total;
    }
    [DoesNotReturn]
    internal static void ThrowEOF() => throw new EndOfStreamException();
}
