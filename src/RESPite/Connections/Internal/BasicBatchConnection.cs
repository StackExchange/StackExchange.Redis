using System.Buffers;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using RESPite.Internal;

namespace RESPite.Connections.Internal;

/// <summary>
/// Holds basic RespOperation, queue and release - turns
/// multiple send/send-many calls into a single send-many call.
/// </summary>
internal sealed class BasicBatchConnection : RespBatch
{
    private RespOperation[] _buffer;
    private int _count = 0;

    private object SyncLock => this;

    public BasicBatchConnection(in RespContext context, int sizeHint) : base(context)
    {
        // ack: yes, I know we won't spot every recursive+decorated scenario
        if (Tail is BasicBatchConnection) ThrowNestedBatch();

        _buffer = sizeHint <= 0 ? [] : ArrayPool<RespOperation>.Shared.Rent(sizeHint);

        static void ThrowNestedBatch() =>
            throw new ArgumentException("Nested batches are not supported", nameof(context));
    }

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

            ArrayPool<RespOperation>.Shared.Return(buffer);
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

        var newBuffer = ArrayPool<RespOperation>.Shared.Rent(newCapacity);
        DebugCounters.OnBatchGrow(_count);
        _buffer.AsSpan(0, _count).CopyTo(newBuffer);
        ArrayPool<RespOperation>.Shared.Return(_buffer);
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

    private int Flush(out RespOperation[] oversized, out RespOperation single)
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

    public override event EventHandler<RespConnectionErrorEventArgs>? ConnectionError;

    public override Task FlushAsync()
    {
        try
        {
            var count = Flush(out var oversized, out var single);
            return count switch
            {
                0 => Task.CompletedTask,
                1 => Tail.WriteAsync(single!),
                _ => SendAndRecycleAsync(Tail, oversized, count),
            };
        }
        catch (Exception ex)
        {
            OnConnectionError(ConnectionError, ex);
            throw;
        }

        static async Task SendAndRecycleAsync(RespConnection tail, RespOperation[] oversized, int count)
        {
            try
            {
                await tail.WriteAsync(oversized.AsMemory(0, count)).ConfigureAwait(false);
                ArrayPool<RespOperation>.Shared.Return(oversized); // only on success, in case captured
            }
            catch (Exception ex)
            {
                TrySetException(oversized.AsSpan(0, count), ex);
                throw;
            }
        }
    }

    private static void TrySetException(ReadOnlySpan<RespOperation> messages, Exception ex)
    {
        foreach (var message in messages)
        {
            message.Message.TrySetException(message.Token, ex);
        }
    }

    public override void Flush()
    {
        string operation = nameof(Flush);
        int count;
        RespOperation[] oversized;
        RespOperation single;
        try
        {
            count = Flush(out oversized, out single);
            switch (count)
            {
                case 0:
                    return;
                case 1:
                    operation = nameof(Tail.Write);
                    Tail.Write(single!);
                    return;
            }
        }
        catch (Exception ex)
        {
            OnConnectionError(ConnectionError, ex, operation);
            throw;
        }

        try
        {
            Tail.Write(oversized.AsSpan(0, count));
        }
        catch (Exception ex)
        {
            TrySetException(oversized.AsSpan(0, count), ex);
            throw;
        }
        finally
        {
            // in the sync case, Send takes a span - hence can't have been captured anywhere; always recycle
            ArrayPool<RespOperation>.Shared.Return(oversized);
        }
    }
}
