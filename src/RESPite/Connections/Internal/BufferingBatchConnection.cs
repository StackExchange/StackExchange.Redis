using System.Buffers;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using RESPite.Internal;

namespace RESPite.Connections.Internal;

/// <summary>
/// Collects messages into a buffer, and then flushes them all at once. Subclass defines how to flush.
/// </summary>
internal abstract class BufferingBatchConnection(in RespContext context, int sizeHint) : RespBatch(context)
{
    internal static void Return(ref RespOperation[] buffer)
    {
        if (buffer.Length != 0)
        {
            DebugCounters.OnBatchBufferReturn(buffer.Length);
            ArrayPool<RespOperation>.Shared.Return(buffer);
            buffer = [];
        }
    }

    private static RespOperation[] Rent(int sizeHint)
    {
        if (sizeHint <= 0) return [];
        var arr = ArrayPool<RespOperation>.Shared.Rent(sizeHint);
        DebugCounters.OnBatchBufferLease(arr.Length);
        return arr;
    }

    private RespOperation[] _buffer = Rent(sizeHint);

    private int _count = 0;

    protected object SyncLock => this;

    protected override void OnDispose(bool disposing)
    {
        if (disposing)
        {
            lock (SyncLock)
            {
                /* everyone else checks disposal inside the lock;
                 the base type already marked as disposed, so:
                 once we're past this point, we can be sure that no more
                 items will be added */
                Debug.Assert(IsDisposed);
            }

            var buffer = _buffer;
            _buffer = [];
            var span = buffer.AsSpan(0, _count);
            foreach (var message in span)
            {
                message.Message.TrySetException(message.Token, CreateObjectDisposedException());
            }

            Return(ref buffer);
            ConnectionError = null;
        }

        base.OnDispose(disposing);
    }

    internal override int OutstandingOperations => _count; // always a thread-race, no point locking

    public override void Write(in RespOperation message)
    {
        lock (SyncLock)
        {
            ThrowIfDisposed();
            EnsureSpaceForLocked(1);
            _buffer[_count++] = message;
        }
    }

    public override void EnsureCapacity(int additionalCount)
    {
        if (additionalCount > _buffer.Length - _count)
        {
            lock (SyncLock)
            {
                EnsureSpaceForLocked(additionalCount);
            }
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void EnsureSpaceForLocked(int add)
    {
        var required = _count + add;
        if (_buffer.Length < required) GrowLocked(required);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void GrowLocked(int required)
    {
        const int maxLength = 0X7FFFFFC7; // not directly available on down-level runtimes :(
        var newCapacity = _buffer.Length * 2; // try doubling
        if ((uint)newCapacity > maxLength) newCapacity = maxLength; // account for max
        if (newCapacity < required) newCapacity = required; // in case doubling wasn't enough

        var newBuffer = Rent(newCapacity);
        DebugCounters.OnBatchGrow(_count);
        _buffer.AsSpan(0, _count).CopyTo(newBuffer);
        Return(ref _buffer);
        _buffer = newBuffer;
    }

    internal override void Write(ReadOnlySpan<RespOperation> messages)
    {
        if (messages.Length != 0)
        {
            lock (SyncLock)
            {
                ThrowIfDisposed();
                EnsureSpaceForLocked(messages.Length);
                messages.CopyTo(_buffer.AsSpan(_count));
                _count += messages.Length;
            }
        }
    }

    protected int Flush(out RespOperation[] oversized, out RespOperation single)
    {
        lock (SyncLock)
        {
            var count = _count;
            switch (_count)
            {
                case 0:
                    // nothing to do, keep our local buffer
                    oversized = [];
                    single = default;
                    return 0;
                case 1:
                    // but keep our local buffer, just reset the count
                    oversized = [];
                    single = _buffer[0];
                    _count = 0;
                    return 1;
                default:
                    // hand the caller our buffer, and reset
                    oversized = _buffer;
                    single = default;
                    _buffer = []; // we *expect* people to only flush once, so: don't rent a new one
                    _count = 0;
                    return count;
            }
        }
    }

    protected void OnConnectionError(Exception exception, [CallerMemberName] string operation = "")
        => OnConnectionError(ConnectionError, exception, operation);

    public override event EventHandler<RespConnectionErrorEventArgs>? ConnectionError;

    protected static void TrySetException(ReadOnlySpan<RespOperation> messages, Exception ex)
    {
        foreach (var message in messages)
        {
            message.Message.TrySetException(message.Token, ex);
        }
    }
}
