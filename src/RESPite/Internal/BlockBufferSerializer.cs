using System.Buffers;
using System.Diagnostics;
using RESPite.Messages;

namespace RESPite.Internal;

/// <summary>
/// Provides abstracted access to a buffer-writing API. Conveniently, we only give the caller
/// RespWriter - which they cannot export (ref-type), thus we never actually give the
/// public caller our IBufferWriter{byte}. Likewise, note that serialization is synchronous,
/// i.e. never switches thread during an operation. This gives us quite a bit of flexibility.
/// There are two main uses of BlockBufferSerializer:
/// 1. thread-local: ambient, used for random messages so that each thread is quietly packing
///    a thread-specific buffer; zero concurrency because of [ThreadStatic] hackery.
/// 2. batching: RespBatch hosts a serializer that reflects the batch we're building; successive
///    commands in the same batch are written adjacently in a shared buffer - we explicitly
///    detect and reject concurrency attempts in a batch (which is fair: a batch has order).
/// </summary>
internal abstract partial class BlockBufferSerializer(ArrayPool<byte>? arrayPool = null) : IBufferWriter<byte>
{
    private readonly ArrayPool<byte> _arrayPool = arrayPool ?? ArrayPool<byte>.Shared;
    private protected abstract BlockBuffer? Buffer { get; set; }

    Memory<byte> IBufferWriter<byte>.GetMemory(int sizeHint) => BlockBuffer.GetBuffer(this, sizeHint).UncommittedMemory;

    Span<byte> IBufferWriter<byte>.GetSpan(int sizeHint) => BlockBuffer.GetBuffer(this, sizeHint).UncommittedSpan;

    void IBufferWriter<byte>.Advance(int count) => BlockBuffer.Advance(this, count);

    public virtual void Clear() => BlockBuffer.Clear(this);

    internal virtual ReadOnlySequence<byte> Flush() => throw new NotSupportedException();

    public virtual ReadOnlyMemory<byte> Serialize<TRequest>(
        RespCommandMap? commandMap,
        ReadOnlySpan<byte> command,
        in TRequest request,
        IRespFormatter<TRequest> formatter)
#if NET9_0_OR_GREATER
    where TRequest : allows ref struct
#endif
    {
        try
        {
            var writer = new RespWriter(this);
            writer.CommandMap = commandMap;
            formatter.Format(command, ref writer, request);
            writer.Flush();
            return BlockBuffer.FinalizeMessage(this);
        }
        catch
        {
            Buffer?.RevertUnfinalized(this);
            throw;
        }
    }

    protected virtual bool ClaimSegment(ReadOnlyMemory<byte> segment) => false;

#if DEBUG
    private int _countAdded, _countRecycled, _countLeaked, _countMessages;
    private long _countMessageBytes;
    public int CountLeaked => Volatile.Read(ref _countLeaked);
    public int CountRecycled => Volatile.Read(ref _countRecycled);
    public int CountAdded => Volatile.Read(ref _countAdded);
    public int CountMessages => Volatile.Read(ref _countMessages);
    public long CountMessageBytes => Volatile.Read(ref _countMessageBytes);

    [Conditional("DEBUG")]
    private void DebugBufferLeaked() => Interlocked.Increment(ref _countLeaked);

    [Conditional("DEBUG")]
    private void DebugBufferRecycled(int length)
    {
        Interlocked.Increment(ref _countRecycled);
        DebugCounters.OnBufferRecycled(length);
    }

    [Conditional("DEBUG")]
    private void DebugBufferCreated()
    {
        Interlocked.Increment(ref _countAdded);
        DebugCounters.OnBufferCreated();
    }

    [Conditional("DEBUG")]
    private void DebugMessageFinalized(int bytes)
    {
        Interlocked.Increment(ref _countMessages);
        Interlocked.Add(ref _countMessageBytes, bytes);
    }
#endif
}
