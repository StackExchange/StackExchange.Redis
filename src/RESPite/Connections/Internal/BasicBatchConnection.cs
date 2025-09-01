using System.Buffers;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace RESPite.Connections.Internal;

/// <summary>
/// Holds basic RespOperation, queue and release - turns
/// multiple send/send-many calls into a single send-many call.
/// </summary>
internal sealed class BasicBatchConnection : RespBatch
{
    private readonly List<RespOperation> _unsent;

    public BasicBatchConnection(in RespContext context, int sizeHint) : base(context)
    {
        // ack: yes, I know we won't spot every recursive+decorated scenario
        if (Tail is BasicBatchConnection) ThrowNestedBatch();

        _unsent = sizeHint <= 0 ? [] : new List<RespOperation>(sizeHint);

        static void ThrowNestedBatch() => throw new ArgumentException("Nested batches are not supported", nameof(context));
    }

    protected override void OnDispose(bool disposing)
    {
        if (disposing)
        {
            lock (_unsent)
            {
                /* everyone else checks disposal inside the lock;
                 the base type already marked as disposed, so:
                 once we're past this point, we can be sure that no more
                 items will be added */
                Debug.Assert(IsDisposed);
            }
#if NET5_0_OR_GREATER
            var span = CollectionsMarshal.AsSpan(_unsent);
            foreach (var message in span)
            {
                message.Message.TrySetException(message.Token, CreateObjectDisposedException());
            }
#else
            foreach (var message in _unsent)
            {
                message.Message.TrySetException(message.Token, CreateObjectDisposedException());
            }
#endif
            _unsent.Clear();
        }

        base.OnDispose(disposing);
    }

    internal override int OutstandingOperations
    {
        get
        {
            lock (_unsent)
            {
                return _unsent.Count;
            }
        }
    }

    public override void Write(in RespOperation message)
    {
        lock (_unsent)
        {
            ThrowIfDisposed();
            _unsent.Add(message);
        }
    }

    internal override void Write(ReadOnlySpan<RespOperation> messages)
    {
        if (messages.Length != 0)
        {
            lock (_unsent)
            {
                ThrowIfDisposed();
#if NET8_0_OR_GREATER
                _unsent.AddRange(messages); // internally optimized
#else
                // two-step; first ensure capacity, then add in loop
#if NET6_0_OR_GREATER
                _unsent.EnsureCapacity(_unsent.Count + messages.Length);
#else
                var required = _unsent.Count + messages.Length;
                if (_unsent.Capacity < required)
                {
                    const int maxLength = 0X7FFFFFC7; // not directly available on down-level runtimes :(
                    var newCapacity = _unsent.Capacity * 2; // try doubling
                    if ((uint)newCapacity > maxLength) newCapacity = maxLength; // account for max
                    if (newCapacity < required) newCapacity = required; // in case doubling wasn't enough
                    _unsent.Capacity = newCapacity;
                }
#endif
                foreach (var message in messages)
                {
                    _unsent.Add(message);
                }
#endif
            }
        }
    }

    private int Flush(out RespOperation[] oversized, out RespOperation single)
    {
        lock (_unsent)
        {
            var count = _unsent.Count;
            switch (count)
            {
                case 0:
                    oversized = [];
                    single = default;
                    break;
                case 1:
                    oversized = [];
                    single = _unsent[0];
                    break;
                default:
                    oversized = ArrayPool<RespOperation>.Shared.Rent(count);
                    single = default;
                    _unsent.CopyTo(oversized);
                    break;
            }

            _unsent.Clear();
            return count;
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
