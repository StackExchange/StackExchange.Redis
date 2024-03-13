using Pipelines.Sockets.Unofficial.Arenas;
using System;
using System.Buffers;
using System.Buffers.Text;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Serialization;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
#pragma warning disable CS1591 // new API

namespace StackExchange.Redis.Protocol;

[Experimental(ExperimentalDiagnosticID)]
public abstract class RespRequest
{
    internal const string ExperimentalDiagnosticID = "SERED002";
    protected RespRequest() { }
    public abstract void Write(ref Resp2Writer writer);
}

[Experimental(RespRequest.ExperimentalDiagnosticID)]
public enum RespPrefix : byte
{
    None = 0,
    SimpleString = (byte)'+',
    SimpleError = (byte)'-',
    Integer = (byte)':',
    BulkString = (byte)'$',
    Array = (byte)'*',
    Null = (byte)'_',
    Boolean = (byte)'#',
    Double = (byte)',',
    BigNumber = (byte)'(',
    BulkError = (byte)'!',
    VerbatimString = (byte)'=',
    Map = (byte)'%',
    Set = (byte)'~',
    Push = (byte)'>',

    // these are not actually implemented
    // Stream = (byte)';',
    // UnboundEnd = (byte)'.',
    // Attribute = (byte)'|',
}

//[Experimental(RespRequest.ExperimentalDiagnosticID)]
//public abstract class RespProcessor<T>
//{
//    public abstract T Parse(in RespChunk value);
//}

internal sealed class RefCountedSequenceSegment<T> : ReadOnlySequenceSegment<T>, IMemoryOwner<T>
{
    public override string ToString() => $"(ref-count: {RefCount}) {base.ToString()}";
    private int _refCount;
    internal int RefCount => Volatile.Read(ref _refCount);
    private static void ThrowDisposed() => throw new ObjectDisposedException(nameof(RefCountedSequenceSegment<T>));
    private sealed class DisposedMemoryManager : MemoryManager<T>
    {
        public static readonly ReadOnlyMemory<T> Instance = new DisposedMemoryManager().Memory;
        private DisposedMemoryManager() { }

        protected override void Dispose(bool disposing) { }
        public override Span<T> GetSpan() { ThrowDisposed(); return default; }
        public override Memory<T> Memory { get { ThrowDisposed(); return default; } }
        public override MemoryHandle Pin(int elementIndex = 0) { ThrowDisposed(); return default; }
        public override void Unpin() => ThrowDisposed();
        protected override bool TryGetArray(out ArraySegment<T> segment)
        {
            ThrowDisposed();
            segment = default;
            return default;
        }
    }

    public RefCountedSequenceSegment(int minSize, RefCountedSequenceSegment<T>? previous = null)
    {
        _refCount = 1;
        Memory = ArrayPool<T>.Shared.Rent(minSize);
        if (previous is not null)
        {
            RunningIndex = previous.RunningIndex + previous.Memory.Length;
            previous.Next = this;
        }
    }

    Memory<T> IMemoryOwner<T>.Memory => MemoryMarshal.AsMemory(Memory);

    public void Dispose()
    {
        int oldCount;
        do
        {
            oldCount = Volatile.Read(ref _refCount);
            if (oldCount == 0) return; // already released
        } while (Interlocked.CompareExchange(ref _refCount, oldCount - 1, oldCount) != oldCount);
        if (oldCount == 0) // we killed it
        {
            Release();
        }
    }

    public void AddRef()
    {
        int oldCount;
        do
        {
            oldCount = Volatile.Read(ref _refCount);
            if (oldCount == 0) ThrowDisposed();
        } while (Interlocked.CompareExchange(ref _refCount, checked(oldCount + 1), oldCount) != oldCount);
    }

    private void Release()
    {
        var memory = Memory;
        Memory = DisposedMemoryManager.Instance;
        if (MemoryMarshal.TryGetArray<T>(memory, out var segment) && segment.Array is not null)
        {
            ArrayPool<T>.Shared.Return(segment.Array);
        }
    }

    internal new RefCountedSequenceSegment<T>? Next
    {
        get => (RefCountedSequenceSegment<T>?)base.Next;
        set => base.Next = value;
    }
}

public readonly struct LeasedSequence<T> : IDisposable
{
    public LeasedSequence(ReadOnlySequence<T> value) => _value = value;
    private readonly ReadOnlySequence<T> _value;

    public override string ToString() => _value.ToString();
    public long Length => _value.Length;
    public bool IsEmpty => _value.IsEmpty;
    public bool IsSingleSegment => _value.IsSingleSegment;
    public SequencePosition Start => _value.Start;
    public SequencePosition End => _value.End;
    public SequencePosition GetPosition(long offset) => _value.GetPosition(offset);
    public SequencePosition GetPosition(long offset, SequencePosition origin) => _value.GetPosition(offset, origin);

    public ReadOnlyMemory<T> First => _value.First;
#if NETCOREAPP3_0_OR_GREATER
    public ReadOnlySpan<T> FirstSpan => _value.FirstSpan;
#else
    public ReadOnlySpan<T> FirstSpan => _value.First.Span;
#endif

    public bool TryGet(ref SequencePosition position, out ReadOnlyMemory<T> memory, bool advance = true)
        => _value.TryGet(ref position, out memory, advance);
    public ReadOnlySequence<T>.Enumerator GetEnumerator() => _value.GetEnumerator();

    public static implicit operator ReadOnlySequence<T>(LeasedSequence<T> value) => value._value;

    // we do *not* assume that slices take additional leases; usually slicing is a transient operation
    public ReadOnlySequence<T> Slice(long start) => _value.Slice(start);
    public ReadOnlySequence<T> Slice(SequencePosition start) => _value.Slice(start);
    public ReadOnlySequence<T> Slice(int start, int length) => _value.Slice(start, length);
    public ReadOnlySequence<T> Slice(int start, SequencePosition end) => _value.Slice(start, end);
    public ReadOnlySequence<T> Slice(long start, long length) => _value.Slice(start, length);
    public ReadOnlySequence<T> Slice(long start, SequencePosition end) => _value.Slice(start, end);
    public ReadOnlySequence<T> Slice(SequencePosition start, int length) => _value.Slice(start, length);
    public ReadOnlySequence<T> Slice(SequencePosition start, long length) => _value.Slice(start, length);
    public ReadOnlySequence<T> Slice(SequencePosition start, SequencePosition end) => _value.Slice(start, end);

    public void Dispose()
    {
        if (_value.Start.GetObject() is SequenceSegment<T> segment)
        {
            var end = _value.End.GetObject();
            do
            {
                if (segment is IDisposable d)
                {
                    d.Dispose();
                }
            }
            while (!ReferenceEquals(segment, end) && (segment = segment!.Next) is not null);
        }
    }

    public void AddRef()
    {
        if (_value.Start.GetObject() is SequenceSegment<T> segment)
        {
            var end = _value.End.GetObject();
            do
            {
                if (segment is RefCountedSequenceSegment<T> counted)
                {
                    counted.AddRef();
                }
            }
            while (!ReferenceEquals(segment, end) && (segment = segment!.Next) is not null);
        }
    }
}

/// <summary>
/// Abstract source of streaming RESP data; the implementation is responsible
/// for retaining a back buffer of pending bytes, and exposing those bytes via <see cref="GetBuffer"/>;
/// additional data is requested via <see cref="TryReadAsync(CancellationToken)"/>, and
/// is consumed via <see cref="Take(long)"/>. The data returned from <see cref="Take(long)"/>
/// can optionally be a chain of <see cref="SequenceSegment{T}"/> that additionally
/// implement <see cref="IDisposable"/>, in which case the <see cref="LeasedSequence{T}"/>
/// will dispose them appropriately (allowing for buffer pool scenarios). Note also that
/// the buffer returned from <see cref="Take"/> does not need to be the same chain as
/// used in <see cref="GetBuffer"/> - it is permitted to copy (etc) the data when consuming.
/// </summary>
[Experimental(RespRequest.ExperimentalDiagnosticID)]
public abstract class RespSource : IAsyncDisposable
{
    public static RespSource Create(Stream source)
    {
        if (source is null) throw new ArgumentNullException(nameof(source));
        if (!source.CanRead) throw new ArgumentException("Source stream cannot be read", nameof(source));
        return new StreamRespSource(source);
    }

    protected abstract ReadOnlySequence<byte> GetBuffer();

    public static RespSource Create(ReadOnlySequence<byte> payload) => new InMemoryRespSource(payload);
    public static RespSource Create(ReadOnlyMemory<byte> payload) => new InMemoryRespSource(new(payload));

    private protected RespSource() { }

    protected abstract ValueTask<bool> TryReadAsync(CancellationToken cancellationToken);

    // internal abstract long Scan(long skip, ref int count);
    public async ValueTask<LeasedSequence<byte>> ReadNextAsync(CancellationToken cancellationToken)
    {
        int pending = 1;
        long totalConsumed = 0;
        while (pending != 0)
        {
            var consumed = Scan(GetBuffer().Slice(totalConsumed), ref pending);
            totalConsumed += consumed;

            if (pending != 0 && !(await TryReadAsync(cancellationToken)))
            {
                throw new EndOfStreamException();
            }
        }

        var chunk = Take(totalConsumed);
        if (chunk.Length != totalConsumed) Throw();
        return new(chunk);

        static void Throw() => throw new InvalidOperationException("Buffer length mismatch in " + nameof(ReadNextAsync));

        // can't use ref-struct in async method
        static long Scan(ReadOnlySequence<byte> payload, ref int count)
        {
            var reader = new RespReader(payload);
            while (count > 0 && reader.ReadNext())
            {
                count = count - 1 + reader.ChildCount;
            }
            return reader.BytesConsumed;
        }
    }

    protected abstract ReadOnlySequence<byte> Take(long bytes);

    public virtual ValueTask DisposeAsync() => default;

    private sealed class InMemoryRespSource : RespSource
    {
        private ReadOnlySequence<byte> _remaining;
        public InMemoryRespSource(ReadOnlySequence<byte> value)
            => _remaining = value;

        protected override ReadOnlySequence<byte> GetBuffer() => _remaining;
        protected override ReadOnlySequence<byte> Take(long bytes)
        {
            var take = _remaining.Slice(0, bytes);
            _remaining = _remaining.Slice(take.End);
            return take;
        }
        protected override ValueTask<bool> TryReadAsync(CancellationToken cancellationToken) => default; // nothing more to get
    }

    private sealed class StreamRespSource : RespSource
    {
        private readonly Stream _source;
        private RefCountedSequenceSegment<byte> _head, _tail;
        private readonly int _bufferSize;
        private int _headOffset, _tailOffset;
        internal StreamRespSource(Stream source, int bufferSize = 64 * 1024)
        {
            _bufferSize = Math.Max(1024, bufferSize);
            _source = source;
            Expand();
            _head = _tail;
        }

        protected override ReadOnlySequence<byte> GetBuffer() => new(_head, _headOffset, _tail, _tailOffset);


#if NETCOREAPP3_1_OR_GREATER
        public override ValueTask DisposeAsync() {
            var node = _head;
            _head = _tail = null!;
            _headOffset = _tailOffset = 0;
            while (node is not null)
            {
                node.Dispose(); // release the memory
                var tmp = node.Next;
                node.Next = null; // break the chain
                node = tmp;
            }
            return _source.DisposeAsync();
        }
#else
        public override ValueTask DisposeAsync()
        {
            _source.Dispose();
            return default;
        }
#endif

        [MemberNotNull(nameof(_tail))]
        private void Expand()
        {
            var next = new RefCountedSequenceSegment<byte>(_bufferSize, _tail);
            _tail = next;
            _tailOffset = 0;
        }
        protected override ValueTask<bool> TryReadAsync(CancellationToken cancellationToken)
        {
            var readBuffer = _tail.Memory;
            var capacity = readBuffer.Length - _tailOffset;
            if (capacity == 0)
            {
                Expand();
                readBuffer = _tail.Memory;
                capacity = readBuffer.Length;
            }
#if NETCOREAPP3_1_OR_GREATER
            if (_tailOffset != 0) readBuffer = readBuffer.Slice(_tailOffset);
            Debug.Assert(readBuffer.Length == capacity);
            var pending = _source.ReadAsync(MemoryMarshal.AsMemory(readBuffer), cancellationToken);
            if (!pending.IsCompletedSuccessfully) return Awaited(this, pending);
#else
            // we know it is an array; happy to explode weirdly otherwise!
            MemoryMarshal.TryGetArray(readBuffer, out var segment);
            var pending = _source.ReadAsync(segment.Array, segment.Offset + _tailOffset, capacity, cancellationToken);
            if (pending.Status != TaskStatus.RanToCompletion) return Awaited(this, pending);
#endif

            // synchronous happy case
            var bytes = pending.GetAwaiter().GetResult();
            if (bytes > 0)
            {
                _tailOffset += bytes;
                return new(true);
            }
            return default;

            static async ValueTask<bool> Awaited(StreamRespSource @this,
#if NETCOREAPP3_1_OR_GREATER
                ValueTask<int> pending
#else
                Task<int> pending
#endif
                )
            {
                var bytes = await pending;
                if (bytes > 0)
                {
                    @this._tailOffset += bytes;
                    return true;
                }
                return false;
            }
        }

        protected override ReadOnlySequence<byte> Take(long bytes)
        {
            // semantically, we're going to AddRef on all the nodes in take, and then
            // drop (and Dispose()) all nodes that we no longer need; but this means
            // that the only shared segment is the first one (and only if there is data left),
            // so we can manually check that one segment, rather than walk two chains
            var all = GetBuffer();
            var take = all.Slice(0, bytes);

            var end = take.End;
            var endSegment = (RefCountedSequenceSegment<byte>)end.GetObject()!;

            var bytesLeftLastPage = endSegment.Memory.Length - end.GetInteger();
            if (bytesLeftLastPage != 0 && (
                bytesLeftLastPage >= 64 // worth using for the next read, regardless
                || endSegment.Next is not null // we've already allocated another page, which means this page is full
                || _tailOffset != end.GetInteger() // (^^ final page) & we have additional read bytes
                ))
            {
                // keep sharing the last page of the outbound / first page of retained
                endSegment.AddRef();
                _head = endSegment;
                _headOffset = end.GetInteger();
            }
            else
            {
                // move to the next page
                _headOffset = 0;
                if (endSegment.Next is null)
                {
                    // no next page buffered; reset completely
                    Debug.Assert(ReferenceEquals(endSegment, _tail));
                    _head = _tail = null!;
                    Expand();
                    _head = _tail;
                }
                else
                {
                    // start fresh from the next page
                    var next = endSegment.Next;
                    endSegment.Next = null; // walk never needed
                    _head = next;
                }
            }
            return take;
        }
    }
}

[Experimental(RespRequest.ExperimentalDiagnosticID)]
public ref struct RespReader
{
    private readonly ReadOnlySequence<byte> _full;
    private long _positionBase;
    private ReadOnlySequence<byte>.Enumerator _chunks;
    private ReadOnlyMemory<byte> _bufferMemory;
    private int _bufferIndex, _bufferLength;
    private RespPrefix _prefix;
    public readonly long BytesConsumed => _positionBase + _bufferIndex;
    public readonly RespPrefix Prefix => _prefix;

    private int _currentOffset, _currentLength;

    /// <summary>
    /// Returns as much data as possible into the buffer, ignoring
    /// any data that cannot fit into <paramref name="target"/>, and
    /// returning the segment representing copied data.
    /// </summary>
    public readonly Span<byte> CopyTo(Span<byte> target)
    {
        if (IsAggregate) return default; // only possible for scalars
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

    internal readonly bool TryGetValueSpan(out ReadOnlySpan<byte> span)
    {
        if (IsAggregate)
        {
            span = default;
            return false; // only possible for scalars
        }
        if (_currentOffset < 0) Throw();
        if (_currentLength == 0)
        {
            span = default;
        }
        else
        {
#if NET7_0_OR_GREATER
            span = MemoryMarshal.CreateReadOnlySpan(ref Unsafe.Add(ref _bufferRoot, _currentOffset), _currentLength);
#else
            span = _bufferSpan.Slice(_currentOffset, _currentLength);
#endif
        }
        return true;

        static void Throw() => throw new InvalidOperationException();
    }
    internal readonly string? ReadString()
    {
        if (IsNull()) return null;
        if (TryGetValueSpan(out var span))
        {
            if (span.IsEmpty) return "";
#if NETCOREAPP3_0_OR_GREATER
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
        throw new NotImplementedException();
    }

#if NET7_0_OR_GREATER
    private ref byte _bufferRoot;
    private RespPrefix PeekPrefix() => (RespPrefix)Unsafe.Add(ref _bufferRoot, _bufferIndex);
    private ReadOnlySpan<byte> PeekPastPrefix() => MemoryMarshal.CreateReadOnlySpan(
        ref Unsafe.Add(ref _bufferRoot, _bufferIndex + 1), _bufferLength - (_bufferIndex + 1));
    private void AssertCrlfPastPrefixUnsafe(int offset)
    {
        if (Unsafe.ReadUnaligned<ushort>(ref Unsafe.Add(ref _bufferRoot, _bufferIndex + offset + 1)) != Resp2Writer.CrLf)
            ThrowProtocolFailure();
    }
    private void SetCurrent(ReadOnlyMemory<byte> current)
    {
        _positionBase += _bufferLength; // accumulate previous length
        _bufferMemory = current;
        _bufferRoot = ref MemoryMarshal.GetReference(current.Span);
        _bufferIndex = 0;
        _bufferLength = current.Length;
    }
#else
    private ReadOnlySpan<byte> _bufferSpan;
    private readonly RespPrefix PeekPrefix() => (RespPrefix)_bufferSpan[_bufferIndex];
    private readonly ReadOnlySpan<byte> PeekPastPrefix() => _bufferSpan.Slice(_bufferIndex + 1);
    private readonly void AssertCrlfPastPrefixUnsafe(int offset)
    {
        if (Unsafe.ReadUnaligned<ushort>(ref Unsafe.AsRef(in _bufferSpan[_bufferIndex + offset + 1])) != Resp2Writer.CrLf)
            ThrowProtocolFailure();
    }
    private void SetCurrent(ReadOnlyMemory<byte> current)
    {
        _positionBase += _bufferLength; // accumulate previous length
        _bufferMemory = current;
        _bufferSpan = current.Span;
        _bufferIndex = 0;
        _bufferLength = _bufferSpan.Length;
    }
#endif

    public RespReader(ReadOnlyMemory<byte> value) : this(new ReadOnlySequence<byte>(value)) { }

    public RespReader(ReadOnlySequence<byte> value)
    {
        _full = value;
        _positionBase = _bufferIndex = _bufferLength = 0;
        _bufferMemory = default;
#if NET7_0_OR_GREATER
        _bufferRoot = ref Unsafe.NullRef<byte>();
#else
        _bufferSpan = default;
#endif
        if (value.IsSingleSegment)
        {
            _chunks = default;
            SetCurrent(value.First);
        }
        else
        {
            _chunks = value.GetEnumerator();
            if (_chunks.MoveNext())
            {
                SetCurrent(_chunks.Current);
            }
        }
    }

    private static ReadOnlySpan<byte> CrLf => "\r\n"u8;

    public readonly int Length => _currentLength;
    public readonly long LongLength => Length;

    public readonly int ChildCount => Prefix switch
    {
        _ when Length <= 0 => 0, // null arrays don't have -1 child-elements; might as well handle zero here too
        RespPrefix.Array or RespPrefix.Set or RespPrefix.Push => Length,
        RespPrefix.Map /* or RespPrefix.Attribute */ => 2 * Length,
        _ => 0,
    };

    public readonly bool IsAggregate => Prefix switch
    {
        RespPrefix.Array or RespPrefix.Set or RespPrefix.Map or RespPrefix.Push => true,
        _ => false,
    };

    private static bool TryReadIntegerCrLf(ReadOnlySpan<byte> bytes, out int value, out int byteCount)
    {
        var end = bytes.IndexOf(CrLf);
        if (end < 0)
        {
            byteCount = value = 0;
            return false;
        }
        if (!(Utf8Parser.TryParse(bytes, out value, out byteCount) && byteCount == end))
            ThrowProtocolFailure();
        byteCount += 2; // include the CrLf
        return true;
    }

    private static void ThrowProtocolFailure() => throw new InvalidOperationException(); // protocol exception?

    public readonly bool IsNull()
    {
        if (_currentLength < -1) ThrowProtocolFailure();
        return _currentLength == -1;
    }

    private void ResetCurrent()
    {
        _prefix = default;
        _currentOffset = -1;
        _currentLength = 0;
    }
    public bool ReadNext()
    {
        ResetCurrent();
        if (_bufferIndex + 2 < _bufferLength) // shortest possible RESP fragment is length 3
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
                    int end = PeekPastPrefix().IndexOf(CrLf);
                    if (end < 0) break;
                    _currentOffset = _bufferIndex + 1;
                    _currentLength = end - _bufferIndex;
                    _bufferIndex += _currentLength + 3;
                    return true;
                case RespPrefix.BulkError:
                case RespPrefix.BulkString:
                case RespPrefix.VerbatimString:
                    // length prefix with value payload
                    if (!TryReadIntegerCrLf(PeekPastPrefix(), out _currentLength, out int consumed)) break;
                    _currentOffset = _bufferIndex + 1 + consumed;
                    if (IsNull())
                    {
                        _bufferIndex += consumed + 1;
                        return true;
                    }
                    if (_currentLength + 2 > (((_bufferLength - _bufferIndex) - 1) - consumed)) break;
                    AssertCrlfPastPrefixUnsafe(consumed + _currentLength);
                    _bufferIndex += consumed + _currentLength + 3;
                    return true;
                case RespPrefix.Array:
                case RespPrefix.Set:
                case RespPrefix.Map:
                case RespPrefix.Push:
                    // length prefix without value payload (child values follow)
                    if (!TryReadIntegerCrLf(PeekPastPrefix(), out _currentLength, out consumed)) break;
                    _ = IsNull(); // for validation/consistency
                    _bufferIndex += consumed + 1;
                    return true;
                case RespPrefix.Null: // null
                    // note we already checked we had 3 bytes
                    AssertCrlfPastPrefixUnsafe(0);
                    _currentOffset = _bufferIndex + 1;
                    _bufferIndex += 3;
                    return true;
                default:
                    ThrowProtocolFailure();
                    return false;
            }
        }
        return ReadSlow();
    }

    private bool ReadSlow()
    {
        ResetCurrent();
        if (_bufferLength == _bufferIndex && !_chunks.MoveNext())
        {
            // natural EOF, single chunk
            return false;
        }
        throw new NotImplementedException(); // multi-segment parsing
    }

    /// <summary>Performs a byte-wise equality check on the payload</summary>
    public bool Is(ReadOnlySpan<byte> readOnlySpan) => throw new NotImplementedException();
}

[Experimental(RespRequest.ExperimentalDiagnosticID)]
[SuppressMessage("Usage", "CA2231:Overload operator equals on overriding value type Equals", Justification = "API not necessary here")]
public readonly struct OpaqueChunk : IEquatable<OpaqueChunk>
{
    private readonly byte[] _buffer;
    private readonly int _preambleIndex, _payloadIndex, _totalBytes;

    public long Length => _totalBytes;

    /// <summary>
    /// Compares 2 chunks for equality; note that this uses buffer reference equality - byte contents are not compared.
    /// </summary>
    public bool Equals(OpaqueChunk other)
        => ReferenceEquals(_buffer, other._buffer) && _payloadIndex == other._payloadIndex
        && _preambleIndex == other._preambleIndex && _totalBytes == other._totalBytes;

    /// <inheritdoc cref="Equals(OpaqueChunk)"/>
    public override bool Equals([NotNullWhen(true)] object? obj) => obj is OpaqueChunk other && Equals(other);

    /// <inheritdoc/>
    public override int GetHashCode() => RuntimeHelpers.GetHashCode(_buffer) ^ _preambleIndex ^ _payloadIndex ^ _totalBytes;

    private OpaqueChunk(byte[] buffer, int preambleIndex, int payloadIndex, int totalBytes)
    {
        _buffer = buffer;
        _preambleIndex = preambleIndex;
        _payloadIndex = payloadIndex;
        _totalBytes = totalBytes;
    }

    internal OpaqueChunk(byte[] buffer, int payloadIndex, int totalBytes)
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

    public ReadOnlySequence<byte> GetBuffer()
    {
        return _totalBytes == 0 ? default : new(_buffer, _preambleIndex, _totalBytes);
    }

    /// <summary>
    /// Gets a text (UTF8) representation of the RESP payload; this API is intended for debugging purposes only, and may
    /// be misleading for non-UTF8 payloads.
    /// </summary>
    public override string ToString()
    {
        if (!TryGetSpan(out var span))
        {
            return nameof(OpaqueChunk);
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
    public OpaqueChunk WithPreamble(ReadOnlySpan<byte> value)
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
    public OpaqueChunk WithoutPreamble() => new OpaqueChunk(_buffer, _payloadIndex, _totalBytes - (_payloadIndex - _preambleIndex));
}

[Experimental(RespRequest.ExperimentalDiagnosticID)]
[SuppressMessage("CodeQuality", "IDE0064:Make readonly fields writable", Justification = "Defensive")]
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
    private void AppendUnsafe(RespPrefix value)
    {
        Debug.Assert(_targetIndex < _targetLength);
        Unsafe.Add(ref _targetArrRoot, _targetIndex++) = (byte)value; // caller must ensure capacity
    }
#else
    private readonly Span<byte> RemainingSpan => new(_targetArr, _targetIndex, _targetLength - _targetIndex);
    private readonly ref byte CurrentPosition => ref _targetArr[_targetIndex];
    private void AppendUnsafe(RespPrefix value)
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

        AppendUnsafe(RespPrefix.Array);
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
        AppendUnsafe(RespPrefix.BulkString);
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

    internal static readonly ushort CrLf = BitConverter.IsLittleEndian ? (ushort)0x0A0D : (ushort)0x0D0A;

    // unsafe===caller **MUST** ensure there is capacity
    private static void UnsafeWriteCrlf(ref byte destination) => Unsafe.WriteUnaligned(ref destination, CrLf);

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

    internal OpaqueChunk Commit()
    {
        var chunk = new OpaqueChunk(_targetArr, _preambleReservation, _targetIndex - _preambleReservation);
        this = default; // nuke self; transferring ownership to the chunk
        return chunk;
    }
}
