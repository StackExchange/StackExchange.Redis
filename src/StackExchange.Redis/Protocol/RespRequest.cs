using Pipelines.Sockets.Unofficial.Arenas;
using System;
using System.Buffers;
using System.Buffers.Text;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
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

/*
[Experimental(RespRequest.ExperimentalDiagnosticID)]
public interface IWhatever
{
    public TResponse Execute<TRequest, TResponse>(TRequest request, RespProcessor<TRequest, TResponse> processor);
}
[Experimental(RespRequest.ExperimentalDiagnosticID)]
public abstract class RespProcessor<TRequest, TResponse>
{
    public abstract void Write(TRequest request, ref Resp2Writer writer);
    public abstract TResponse Read(TRequest request, ref RespReader reader);
}
[Experimental(RespRequest.ExperimentalDiagnosticID)]
internal static class IWhateverExtensions
{
    public static TResponse Execute<TRequest, TResponse>(this IWhatever obj, TRequest request, RespWriter<TRequest> writer, RespReader<TResponse> reader)
        => obj.Execute(new RespSplitProcessor<TRequest, TResponse>.State(request, writer, reader), RespSplitProcessor<TRequest, TResponse>.Instance);
}
[Experimental(RespRequest.ExperimentalDiagnosticID)]
internal sealed class RespSplitProcessor<TRequest, TResponse> : RespProcessor<RespSplitProcessor<TRequest, TResponse>.State, TResponse>
{
    internal static readonly RespSplitProcessor<TRequest, TResponse> Instance = new();
    private RespSplitProcessor() { }
    public override TResponse Read(State request, ref RespReader reader) => request.Reader.Read(ref reader);
    public override void Write(State request, ref Resp2Writer writer) => request.Writer.Write(request.Request, ref writer);
    internal readonly struct State
    {
        public State(TRequest request, RespWriter<TRequest> writer, RespReader<TResponse> reader)
        {
            Request = request;
            Writer = writer;
            Reader = reader;
        }
        public readonly TRequest Request;
        public readonly RespWriter<TRequest> Writer;
        public readonly RespReader<TResponse> Reader;
    }
}

[Experimental(RespRequest.ExperimentalDiagnosticID)]
public abstract class RespWriter<TRequest>
{
    public abstract void Write(TRequest request, ref Resp2Writer writer);
}
[Experimental(RespRequest.ExperimentalDiagnosticID)]
public abstract class RespReader<TResponse>
{
    public abstract TResponse Read(ref RespReader writer);
}
*/
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

internal sealed partial class RefCountedSequenceSegment<T> : ReadOnlySequenceSegment<T>, IMemoryOwner<T>
{
#if DEBUG
    private static long _debugTotalLeased, _debugTotalReturned;
    internal static long DebugOutstanding => Volatile.Read(ref _debugTotalLeased) - Volatile.Read(ref _debugTotalReturned);
    internal static long DebugTotalLeased => Volatile.Read(ref _debugTotalLeased);
    partial void DebugIncrOutstanding() => Interlocked.Increment(ref _debugTotalLeased);
    partial void DebugDecrOutstanding() => Interlocked.Increment(ref _debugTotalReturned);
#endif

    partial void DebugIncrOutstanding();
    partial void DebugDecrOutstanding();

    public override string ToString() => $"(ref-count: {RefCount}) {base.ToString()}";
    private int _refCount;
    internal int RefCount => Volatile.Read(ref _refCount);
    private static void ThrowDisposed() => throw new ObjectDisposedException(nameof(RefCountedSequenceSegment<T>));
    private sealed class DisposedMemoryManager : MemoryManager<T>
    {
        public static readonly ReadOnlyMemory<T> Instance;
        private static readonly bool _triggered;
        static DisposedMemoryManager()
        {
            // accessing .Memory touches .Span for .Length, so
            // we need to delay making it throw
            Instance = new DisposedMemoryManager().Memory;
            _triggered = true;
        }

        protected override void Dispose(bool disposing) { }

        // note that we deliberately spoof a non-empty length, to avoid IsEmpty short-circuits,
        // because we *want* people to know that they're doing something wrong
        public override Span<T> GetSpan() { if (_triggered) ThrowDisposed(); return new T[8]; }

        public override MemoryHandle Pin(int elementIndex = 0) { if (_triggered) ThrowDisposed(); return default; }
        public override void Unpin() { if (_triggered) ThrowDisposed(); }
        protected override bool TryGetArray(out ArraySegment<T> segment)
        {
            if (_triggered) ThrowDisposed();
            segment = default;
            return default;
        }
    }

    public RefCountedSequenceSegment(int minSize, RefCountedSequenceSegment<T>? previous = null)
    {
        _refCount = 1;
        Memory = ArrayPool<T>.Shared.Rent(minSize);
        DebugIncrOutstanding();
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
        if (oldCount == 1) // then we killed it
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
        DebugDecrOutstanding();
    }

    internal new RefCountedSequenceSegment<T>? Next
    {
        get => (RefCountedSequenceSegment<T>?)base.Next;
        set => base.Next = value;
    }
}

public readonly struct LeasedSequence<T> : IDisposable
{
#if DEBUG
    [SuppressMessage("ApiDesign", "RS0016:Add public types and members to the declared API", Justification = "Debug API")]
    public static long DebugOutstanding => RefCountedSequenceSegment<byte>.DebugOutstanding;
    [SuppressMessage("ApiDesign", "RS0016:Add public types and members to the declared API", Justification = "Debug API")]
    public static long DebugTotalLeased => RefCountedSequenceSegment<byte>.DebugTotalLeased;
#endif

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
        if (_value.Start.GetObject() is ReadOnlySequenceSegment<T> segment)
        {
            var end = _value.End.GetObject();
            do
            {
                if (segment is IDisposable d)
                {
                    d.Dispose();
                }
            }
            while (!ReferenceEquals(segment, end) && (segment = segment!.Next!) is not null);
        }
    }

    public void AddRef()
    {
        if (_value.Start.GetObject() is ReadOnlySequenceSegment<T> segment)
        {
            var end = _value.End.GetObject();
            do
            {
                if (segment is RefCountedSequenceSegment<T> counted)
                {
                    counted.AddRef();
                }
            }
            while (!ReferenceEquals(segment, end) && (segment = segment!.Next!) is not null);
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
public abstract partial class RespSource : IAsyncDisposable
{
    public static RespSource Create(Stream source, bool closeStream = false)
    {
        if (source is null) throw new ArgumentNullException(nameof(source));
        if (!source.CanRead) throw new ArgumentException("Source stream cannot be read", nameof(source));
        return new StreamRespSource(source, closeStream);
    }

    protected abstract ReadOnlySequence<byte> GetBuffer();

    public static RespSource Create(ReadOnlySequence<byte> payload) => new InMemoryRespSource(payload);
    public static RespSource Create(ReadOnlyMemory<byte> payload) => new InMemoryRespSource(new(payload));

    private protected RespSource() { }

    protected abstract ValueTask<bool> TryReadAsync(CancellationToken cancellationToken);

    [Conditional("DEBUG")]
    static partial void DebugWrite(ReadOnlySequence<byte> data);

#if DEBUG
    static partial void DebugWrite(ReadOnlySequence<byte> data)
    {
        try
        {
            var reader = new RespReader(data);
            reader.ReadNext();
            Debug.WriteLine(reader.ToString());
        }
        catch(Exception ex)
        {
            Debug.WriteLine(ex.Message);
        }
    }
#endif

    // internal abstract long Scan(long skip, ref int count);
    public async ValueTask<LeasedSequence<byte>> ReadNextAsync(CancellationToken cancellationToken = default)
    {
        int pending = 1;
        long totalConsumed = 0;
        while (pending != 0)
        {
            var consumed = Scan(GetBuffer().Slice(totalConsumed), ref pending);
            totalConsumed += consumed;

            if (pending != 0)
            {
                if (!await TryReadAsync(cancellationToken))
                {
                    if (totalConsumed != 0)
                    {
                        throw new EndOfStreamException();
                    }
                    return default;
                }
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
            Debug.Assert(reader.BytesConsumed <= payload.Length);
            return reader.BytesConsumed;
        }
    }

    protected abstract ReadOnlySequence<byte> Take(long bytes);

    public virtual ValueTask DisposeAsync()
    {
        GC.SuppressFinalize(this);
        return default;
    }

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
        private readonly bool _closeStream;

        private RotatingBufferCore _buffer;
        internal StreamRespSource(Stream source, bool closeStream, int blockSize = 64 * 1024)
        {
            _buffer = new(Math.Max(1024, blockSize));
            _source = source;
            _closeStream = closeStream;
        }

        protected override ReadOnlySequence<byte> GetBuffer() => _buffer.GetBuffer();


#if NETCOREAPP3_1_OR_GREATER
        public override ValueTask DisposeAsync()
        {
            _buffer.Dispose();
            return _closeStream ? _source.DisposeAsync() : default;
        }
#else
        public override ValueTask DisposeAsync()
        {
            _buffer.Dispose();
            if (_closeStream) _source.Dispose();
            return default;
        }
#endif
        protected override ValueTask<bool> TryReadAsync(CancellationToken cancellationToken)
        {
            var readBuffer = _buffer.GetWritableTail();
            Debug.Assert(!readBuffer.IsEmpty, "should have space");
#if NETCOREAPP3_1_OR_GREATER
            var pending = _source.ReadAsync(readBuffer, cancellationToken);
            if (!pending.IsCompletedSuccessfully) return Awaited(this, pending);
#else
            // we know it is an array; happy to explode weirdly otherwise!
            if (!MemoryMarshal.TryGetArray<byte>(readBuffer, out var segment)) ThrowNotArray();
            var pending = _source.ReadAsync(segment.Array, segment.Offset, segment.Count, cancellationToken);
            if (pending.Status != TaskStatus.RanToCompletion) return Awaited(this, pending);

            static void ThrowNotArray() => throw new InvalidOperationException("Unable to obtain array from tail buffer");
#endif

            // synchronous happy case
            var bytes = pending.GetAwaiter().GetResult();
            if (bytes > 0)
            {
                _buffer.Commit(bytes);
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
                    @this._buffer.Commit(bytes);
                    return true;
                }
                return false;
            }
        }

        protected override ReadOnlySequence<byte> Take(long bytes) => _buffer.DetachRotating(bytes);
    }
}

[Experimental(RespRequest.ExperimentalDiagnosticID)]
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

    internal int DebugBufferIndex => _bufferIndex;

    public readonly RespPrefix Prefix => _prefix;

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
        if (!IsScalar || _length < 0)
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
    internal readonly string? ReadString()
    {
        if (!IsScalar || _length < 0) return null;
        if (_length == 0) return "";
        if (TryGetValueSpan(out var span))
        {
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
#if NETCOREAPP3_1_OR_GREATER
        var s = Resp2Writer.UTF8.GetString(buffer);
#else
        string s;
        unsafe
        {
            fixed (byte* ptr = buffer)
            {
                s = Resp2Writer.UTF8.GetString(ptr, buffer.Length);
            }
        }
#endif
        if (lease is not null) ArrayPool<byte>.Shared.Return(lease);
        return s;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void AssertClLfUnsafe(scoped ref byte source, int offset)
    {
        if (Unsafe.ReadUnaligned<ushort>(ref Unsafe.Add(ref source, offset)) != Resp2Writer.CrLf)
        {
            ThrowProtocolFailure("Expected CR/LF");
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void AssertClLfUnsafe(scoped ref readonly byte source)
    {
        if (Unsafe.ReadUnaligned<ushort>(ref Unsafe.AsRef(in source)) != Resp2Writer.CrLf)
        {
            ThrowProtocolFailure("Expected CR/LF");
        }
    }

#if NET7_0_OR_GREATER
    private ref byte _bufferRoot;
    private RespPrefix PeekPrefix() => (RespPrefix)Unsafe.Add(ref _bufferRoot, _bufferIndex);
    private ReadOnlySpan<byte> PeekPastPrefix() => MemoryMarshal.CreateReadOnlySpan(
        ref Unsafe.Add(ref _bufferRoot, _bufferIndex + 1), _bufferLength - (_bufferIndex + 1));
    private ReadOnlySpan<byte> PeekCurrent() => MemoryMarshal.CreateReadOnlySpan(
        ref Unsafe.Add(ref _bufferRoot, _bufferIndex), _bufferLength - _bufferIndex);
    private void AssertCrlfPastPrefixUnsafe(int offset) => AssertClLfUnsafe(ref _bufferRoot, _bufferIndex + offset + 1);
    private void SetCurrent(ReadOnlySpan<byte> current)
    {
        _positionBase += _bufferLength; // accumulate previous length
        _bufferIndex = 0;
        _bufferLength = current.Length;
        _bufferRoot = ref MemoryMarshal.GetReference(current);
    }
#else
    private ReadOnlySpan<byte> _bufferSpan;
    private readonly RespPrefix PeekPrefix() => (RespPrefix)_bufferSpan[_bufferIndex];
    private ReadOnlySpan<byte> PeekCurrent() => _bufferSpan.Slice(_bufferIndex);
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

    public RespReader(ReadOnlyMemory<byte> value) : this(new ReadOnlySequence<byte>(value)) { }
    public RespReader(ReadOnlySequence<byte> value)
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

    private static ReadOnlySpan<byte> CrLf => "\r\n"u8;

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
        var end = bytes.IndexOf(CrLf);
        if (end < 0)
        {
            byteCount = value = 0;
            if (bytes.Length >= Resp2Writer.MaxRawBytesInt32 + 2)
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


    public bool ReadNext()
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
                    _length = PeekPastPrefix().IndexOf(CrLf);
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
        if (IsScalar) return IsNull? $"@{BytesConsumed} {Prefix}: {nameof(RespPrefix.Null)}" : $"@{BytesConsumed} {Prefix} with {ScalarLength} bytes '{ReadString()}'";
        if (IsAggregate) return IsNull? $"@{BytesConsumed} {Prefix}: {nameof(RespPrefix.Null)}" : $"@{BytesConsumed} {Prefix} with {ChildCount} sub-items";
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
            if (CurrentRemainingBytes >= Resp2Writer.MaxRawBytesInt32 + 2)
            {
                if (TryReadIntegerCrLf(_current.Slice(_index), out length, out int consumed))
                {
                    _index += consumed;
                    return true;
                }
            }
            else
            {
                Span<byte> buffer = stackalloc byte[Resp2Writer.MaxRawBytesInt32 + 2];
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
                    if (!(copy.TryAdvance(index) && copy.TryReadCrLf())) ThrowProtocolFailure("Expected CR/LF");
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

    }

    /// <summary>Performs a byte-wise equality check on the payload</summary>
    public readonly bool Is(ReadOnlySpan<byte> value)
    {
        if (!IsScalar) return false;
        if (TryGetValueSpan(out var span))
        {
            return span.SequenceEqual(value);
        }
        throw new NotImplementedException();
    }

    /// <summary>
    /// Skips all child/descendent nodes of this element, returning the number
    /// of elements skipped
    /// </summary>
    public int SkipChildren()
    {
        int remaining = ChildCount, total = 0;
        while (remaining > 0 && ReadNext())
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
        static void ThrowEOF() => throw new EndOfStreamException();
    }
}

[Experimental(RespRequest.ExperimentalDiagnosticID)]
[SuppressMessage("Usage", "CA2231:Overload operator equals on overriding value type Equals", Justification = "API not necessary here")]
public readonly struct RequestBuffer
{
    private readonly ReadOnlySequence<byte> _buffer;
    private readonly int _preambleIndex, _payloadIndex;

    public long Length => _buffer.Length - _preambleIndex;

    private RequestBuffer(ReadOnlySequence<byte> buffer, int preambleIndex, int payloadIndex)
    {
        _buffer = buffer;
        _preambleIndex = preambleIndex;
        _payloadIndex = payloadIndex;
    }

    internal RequestBuffer(ReadOnlySequence<byte> buffer, int payloadIndex)
    {
        _buffer = buffer;
        _preambleIndex = _payloadIndex = payloadIndex;
    }

    public bool TryGetSpan(out ReadOnlySpan<byte> span)
    {
        var buffer = GetBuffer(); // handle preamble
        if (buffer.IsSingleSegment)
        {
#if NETCOREAPP3_1_OR_GREATER
            span = buffer.FirstSpan;
#else
            span = buffer.First.Span;
#endif
            return true;
        }
        span = default;
        return false;
    }

    public ReadOnlySequence<byte> GetBuffer() => _preambleIndex == 0 ? _buffer : _buffer.Slice(_preambleIndex);

    /// <summary>
    /// Gets a text (UTF8) representation of the RESP payload; this API is intended for debugging purposes only, and may
    /// be misleading for non-UTF8 payloads.
    /// </summary>
    public override string ToString()
    {
        var length = Length;
        if (length == 0) return "";
        if (length > 1024) return $"({length} bytes)";
        var buffer = GetBuffer();
#if NET6_0_OR_GREATER
        return Resp2Writer.UTF8.GetString(buffer);
#else
#if NETCOREAPP3_0_OR_GREATER
        if (buffer.IsSingleSegment)
        {
            return Resp2Writer.UTF8.GetString(buffer.FirstSpan);
        }
#endif
        var arr = ArrayPool<byte>.Shared.Rent((int)length);
        buffer.CopyTo(arr);
        var s = Resp2Writer.UTF8.GetString(arr, 0, (int)length);
        ArrayPool<byte>.Shared.Return(arr);
        return s;
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
        new LeasedSequence<byte>(buffer).Dispose();
    }

    /// <summary>
    /// Prepends the given preamble contents 
    /// </summary>
    public RequestBuffer WithPreamble(ReadOnlySpan<byte> value)
    {
        if (value.IsEmpty) return this; // trivial

        int length = value.Length, preambleIndex = _preambleIndex - length;
        if (preambleIndex < 0) Throw();
        var target = _buffer.Slice(preambleIndex, length);
        if (target.IsSingleSegment)
        {
            value.CopyTo(MemoryMarshal.AsMemory(target.First).Span);
        }
        else
        {
            MultiCopy(in target, value);
        }
        return new(_buffer, preambleIndex, _payloadIndex);

        static void Throw() => throw new InvalidOperationException("There is insufficient capacity to add the requested preamble");

        static void MultiCopy(in ReadOnlySequence<byte> buffer, ReadOnlySpan<byte> source)
        {
            // note that we've already asserted that the source is non-trivial
            var iter = buffer.GetEnumerator();
            while (iter.MoveNext())
            {
                var target = MemoryMarshal.AsMemory(iter.Current).Span;
                if (source.Length <= target.Length)
                {
                    source.CopyTo(target);
                    return;
                }
                source.Slice(0, target.Length).CopyTo(target);
                source = source.Slice(target.Length);
                Debug.Assert(!source.IsEmpty);
            }
            Debug.Assert(!source.IsEmpty);
            Throw();
            static void Throw() => throw new InvalidOperationException("Insufficient target space");
        }
    }

    /// <summary>
    /// Removes all preamble, reverting to just the original payload
    /// </summary>
    public RequestBuffer WithoutPreamble() => new RequestBuffer(_buffer, _payloadIndex, _payloadIndex);
}

[Experimental(RespRequest.ExperimentalDiagnosticID)]
public ref struct Resp2Writer
{
    private RotatingBufferCore _buffer;
    private readonly int _preambleReservation;
    private int _argCountIncludingCommand, _argIndexIncludingCommand;

    public Resp2Writer(int preambleReservation = 64, int blockSize = 1024)
    {
        _preambleReservation = preambleReservation;
        _argCountIncludingCommand = _argIndexIncludingCommand = 0;
        _buffer = new(blockSize);
        _buffer.Commit(preambleReservation);
    }

    internal const int MaxRawBytesInt32 = 10,
        MaxProtocolBytesIntegerInt32 = MaxRawBytesInt32 + 3, // ?X10X\r\n where ? could be $, *, etc - usually a length prefix
        MaxProtocolBytesBulkStringInt32 = MaxRawBytesInt32 + 7; // $10\r\nX10X\r\n
    /*
                    MaxBytesInt64 = 26, // $19\r\nX19X\r\n
                    MaxBytesSingle = 27; // $NN\r\nX...X\r\n - note G17 format, allow 20 for payload
    */

    private const int NullLength = 5; // $-1\r\n 

    internal void Recycle() => _buffer.Dispose();

    internal static readonly UTF8Encoding UTF8 = new(false);

    public void WriteCommand(string command, int argCount) => WriteCommand(command.AsSpan(), argCount);

    private const int MAX_UTF8_BYTES_PER_CHAR = 4, MAX_CHARS_FOR_STACKALLOC_ENCODE = 64,
        ENCODE_STACKALLOC_BYTES = MAX_CHARS_FOR_STACKALLOC_ENCODE * MAX_UTF8_BYTES_PER_CHAR;

    public void WriteCommand(scoped ReadOnlySpan<char> command, int argCount)
    {
        if (command.Length <= MAX_CHARS_FOR_STACKALLOC_ENCODE)
        {
            WriteCommand(Utf8Encode(command, stackalloc byte[ENCODE_STACKALLOC_BYTES]), argCount);
        }
        else
        {
            WriteCommandSlow(ref this, command, argCount);
        }

        static void WriteCommandSlow(ref Resp2Writer @this, scoped ReadOnlySpan<char> command, int argCount)
        {
            @this.WriteCommand(Utf8EncodeLease(command, out var lease), argCount);
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

    public void WriteCommand(scoped ReadOnlySpan<byte> command, int argCount)
    {
        if (_argCountIncludingCommand > 0) ThrowCommandAlreadyWritten();
        if (command.IsEmpty) ThrowEmptyCommand();
        if (argCount < 0) ThrowNegativeArgs();
        _argCountIncludingCommand = argCount + 1;
        _argIndexIncludingCommand = 1;

        var payloadAndFooter = command.Length + 2;

        // optimize for single buffer-fetch path
        var worstCase = MaxProtocolBytesIntegerInt32 + MaxProtocolBytesIntegerInt32 + command.Length + 2;
        if (_buffer.TryGetWritableSpan(worstCase, out var span))
        {
            ref byte head = ref MemoryMarshal.GetReference(span);
            var header = WriteCountPrefix(RespPrefix.Array, _argCountIncludingCommand, span);
#if NETCOREAPP3_1_OR_GREATER
            header += WriteCountPrefix(RespPrefix.BulkString, command.Length,
                MemoryMarshal.CreateSpan(ref Unsafe.Add(ref head, header), MaxProtocolBytesIntegerInt32));
            command.CopyTo(MemoryMarshal.CreateSpan(ref Unsafe.Add(ref head, header), command.Length));
#else
            header += WriteCountPrefix(RespPrefix.BulkString, command.Length, span.Slice(header));
            command.CopyTo(span.Slice(header));
#endif

            Unsafe.WriteUnaligned(ref Unsafe.Add(ref head, header + command.Length), CrLf);
            _buffer.Commit(header + command.Length + 2);
            return; // yay!
        }

        // slow path, multiple buffer fetches
        WriteCountPrefix(RespPrefix.Array, _argCountIncludingCommand);
        WriteCountPrefix(RespPrefix.BulkString, command.Length);
        WriteRaw(command);
        WriteRaw(CrlfBytes);


        static void ThrowCommandAlreadyWritten() => throw new InvalidOperationException(nameof(WriteCommand) + " can only be called once");
        static void ThrowEmptyCommand() => throw new ArgumentOutOfRangeException(nameof(command), "command cannot be empty");
        static void ThrowNegativeArgs() => throw new ArgumentOutOfRangeException(nameof(argCount), "argCount cannot be negative");
    }

    private static int WriteCountPrefix(RespPrefix prefix, int count, Span<byte> target)
    {
        var len = Format.FormatInt32(count, target.Slice(1)); // we only want to pay for this one slice
        if (target.Length < len + 3) Throw();
        ref byte head = ref MemoryMarshal.GetReference(target);
        head = (byte)prefix;
        Unsafe.WriteUnaligned(ref Unsafe.Add(ref head, len + 1), CrLf);
        return len + 3;

        static void Throw() => throw new InvalidOperationException("Insufficient buffer space to write count prefix");
    }

    private void WriteNullString() // private because I don't think this is allowed in client streams? check
        => WriteRaw("$-1\r\n"u8);

    private void WriteEmptyString() // private because I don't think this is allowed in client streams? check
        => WriteRaw("$0\r\n\r\n"u8);

    private void WriteRaw(scoped ReadOnlySpan<byte> value)
    {
        while (!value.IsEmpty)
        {
            var target = _buffer.GetWritableTail().Span;
            Debug.Assert(!target.IsEmpty, "need something!");

            if (target.Length >= value.Length)
            {
                // it all fits
                value.CopyTo(target);
                _buffer.Commit(value.Length);
                return;
            }

            // write what we can
            value.Slice(target.Length).CopyTo(target);
            _buffer.Commit(target.Length);
            value = value.Slice(target.Length);
        }
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
        // optimize for fitting everything into a single buffer-fetch
        var payloadAndFooter = value.Length + 2;
        var worstCase = MaxProtocolBytesIntegerInt32 + payloadAndFooter;
        if (_buffer.TryGetWritableSpan(worstCase, out var span))
        {
            ref byte head = ref MemoryMarshal.GetReference(span);
            var header = WriteCountPrefix(RespPrefix.BulkString, value.Length, span);
#if NETCOREAPP3_1_OR_GREATER
            value.CopyTo(MemoryMarshal.CreateSpan(ref Unsafe.Add(ref head, header), payloadAndFooter));
#else
            value.CopyTo(span.Slice(header));
#endif
            Unsafe.WriteUnaligned(ref Unsafe.Add(ref head, header + value.Length), CrLf);
            _buffer.Commit(header + payloadAndFooter);
            return; // yay!
        }

        // slow path - involves multiple buffer fetches
        WriteCountPrefix(RespPrefix.BulkString, value.Length);
        WriteRaw(value);
        WriteRaw(CrlfBytes);
    }

    private void WriteCountPrefix(RespPrefix prefix, int count)
    {
        Span<byte> buffer = stackalloc byte[MaxProtocolBytesIntegerInt32];
        WriteRaw(buffer.Slice(0, WriteCountPrefix(prefix, count, buffer)));
    }

    internal static readonly ushort CrLf = BitConverter.IsLittleEndian ? (ushort)0x0A0D : (ushort)0x0D0A;

    internal static ReadOnlySpan<byte> CrlfBytes => "\r\n"u8;

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

    internal RequestBuffer Detach() => new RequestBuffer(_buffer.Detach(), _preambleReservation);
}

internal struct RotatingBufferCore : IDisposable, IBufferWriter<byte> // note mutable struct intended to encapsulate logic as a field inside a class instance
{
    private RefCountedSequenceSegment<byte> _head, _tail;
    private readonly long _maxLength;
    private readonly int _xorBlockSize;
    private int _headOffset, _tailOffset, _tailSize;
    internal readonly int BlockSize => _xorBlockSize ^ DEFAULT_BLOCK_SIZE; // allows default to apply on new()
    internal readonly long MaxLength => _maxLength;

    private const int DEFAULT_BLOCK_SIZE = 1024;

    public RotatingBufferCore(int blockSize, int maxLength = 0)
    {
        if (blockSize <= 0) Throw();
        if (maxLength <= 0) maxLength = int.MaxValue;
        _xorBlockSize = blockSize ^ DEFAULT_BLOCK_SIZE;
        _maxLength = maxLength;
        _headOffset = _tailOffset = _tailSize = 0;
        Expand();

        static void Throw() => throw new ArgumentOutOfRangeException(nameof(blockSize));
    }

    /// <summary>
    /// The immediately available contiguous bytes in the current buffer (or next buffer, if none)
    /// </summary>
    public readonly int AvailableBytes
    {
        get
        {
            var remaining = _tailSize - _tailOffset;
            return remaining == 0 ? BlockSize : remaining;
        }
    }

    [MemberNotNull(nameof(_head))]
    [MemberNotNull(nameof(_tail))]
    private void Expand()
    {
        Debug.Assert(_tail is null || _tailOffset == _tail.Memory.Length, "tail page should be full");
        if (MaxLength > 0 && (GetBuffer().Length + BlockSize) > MaxLength) ThrowQuota();
        var next = new RefCountedSequenceSegment<byte>(BlockSize, _tail);
        _tail = next;
        _tailOffset = 0;
        _tailSize = next.Memory.Length;
        if (_head is null)
        {
            _head = next;
            _headOffset = 0;
        }

        static void ThrowQuota() => throw new InvalidOperationException("Buffer quota exceeded");
    }

    public bool TryGetWritableSpan(int minSize, out Span<byte> span)
    {
        if (minSize <= AvailableBytes) // don't pay lookup cost if impossible
        {
            span = GetWritableTail().Span;
            return span.Length >= minSize;
        }
        span = default;
        return false;
    }

    public Memory<byte> GetWritableTail()
    {
        if (_tailOffset == _tailSize)
        {
            Expand();
        }
        // definitely something available; return the gap
        return MemoryMarshal.AsMemory(_tail.Memory).Slice(_tailOffset);
    }
    public readonly ReadOnlySequence<byte> GetBuffer() => _head is null ? default : new(_head, _headOffset, _tail, _tailOffset);
    internal void Commit(int bytes) // unlike Advance, this remains valid for data outside what has been written
    {
        if (bytes >= 0 && bytes <= _tailSize - _tailOffset)
        {
            _tailOffset += bytes;
        }
        else
        {
            CommitSlow(bytes);
        }
    }
    private void CommitSlow(int bytes) // multi-segment commits (valid even though it remains unwritten) and error-cases
    {
        if (bytes < 0) throw new ArgumentOutOfRangeException(nameof(bytes));
        while (bytes > 0)
        {
            var space = _tailSize - _tailOffset;
            if (bytes <= space)
            {
                _tailOffset += bytes;
            }
            else
            {
                _tailOffset += space;
                Expand(); // need more
            }
            bytes -= space;
        }
    }

    /// <summary>
    /// Detaches the entire committed chain to the caller without leaving things in a resumable state
    /// </summary>
    public ReadOnlySequence<byte> Detach()
    {
        var all = GetBuffer();
        _head = _tail = null!;
        _headOffset = _tailOffset = _tailSize = 0;
        return all;
    }

    /// <summary>
    /// Detaches the head portion of the committed chain, retaining the rest of the buffered data
    /// for additional use
    /// </summary>
    public ReadOnlySequence<byte> DetachRotating(long bytes)
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

    public void Dispose()
    {
        LeasedSequence<byte> leased = new(GetBuffer());
        _head = _tail = null!;
        _headOffset = _tailOffset = _tailSize = 0;
        leased.Dispose();
    }

    void IBufferWriter<byte>.Advance(int count) => Commit(count);
    Memory<byte> IBufferWriter<byte>.GetMemory(int sizeHint) => GetWritableTail();
    Span<byte> IBufferWriter<byte>.GetSpan(int sizeHint) => GetWritableTail().Span;
}
