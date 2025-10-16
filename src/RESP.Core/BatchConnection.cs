using System;
using System.Buffers;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

namespace Resp;

public interface IBatchConnection : IRespConnection
{
    Task FlushAsync();
    void Flush();
}

internal sealed class BatchConnection : IBatchConnection
{
    private bool _isDisposed;
    private readonly List<IRespMessage> _unsent;
    private readonly IRespConnection _tail;
    private readonly RespContext _context;

    public BatchConnection(in RespContext context, int sizeHint)
    {
        // ReSharper disable once ConditionIsAlwaysTrueOrFalseAccordingToNullableAPIContract - an abundance of caution
        var tail = context.Connection;
        if (tail is not { CanWrite: true }) ThrowNonWritable();
        if (tail is BatchConnection) ThrowBatch();

        _unsent = sizeHint <= 0 ? [] : new List<IRespMessage>(sizeHint);
        _tail = tail!;
        _context = context.WithConnection(this);
        static void ThrowBatch() => throw new ArgumentException("Nested batches are not supported", nameof(tail));

        static void ThrowNonWritable() =>
            throw new ArgumentException("A writable connection is required", nameof(tail));
    }

    public void Dispose()
    {
        lock (_unsent)
        {
            /* everyone else checks disposal inside the lock, so:
             once we've set this, we can be sure that no more
             items will be added */
            _isDisposed = true;
        }
#if NET5_0_OR_GREATER
        var span = CollectionsMarshal.AsSpan(_unsent);
        foreach (var message in span)
        {
            message.TrySetException(new ObjectDisposedException(ToString()));
        }
#else
        foreach (var message in _unsent)
        {
            message.TrySetException(new ObjectDisposedException(ToString()));
        }
#endif
        _unsent.Clear();
    }

    public ValueTask DisposeAsync()
    {
        Dispose();
        return default;
    }

    public RespConfiguration Configuration => _tail.Configuration;
    public bool CanWrite => _tail.CanWrite;

    public int Outstanding
    {
        get
        {
            lock (_unsent)
            {
                return _unsent.Count;
            }
        }
    }

    public ref readonly RespContext Context => ref _context;

    private const string SyncMessage = "Batch connections do not support synchronous sends";
    public void Send(IRespMessage message) => throw new NotSupportedException(SyncMessage);

    public void Send(ReadOnlySpan<IRespMessage> messages) => throw new NotSupportedException(SyncMessage);

    private void ThrowIfDisposed()
    {
        if (_isDisposed) Throw();
        static void Throw() => throw new ObjectDisposedException(nameof(BatchConnection));
    }

    public Task SendAsync(IRespMessage message)
    {
        lock (_unsent)
        {
            ThrowIfDisposed();
            _unsent.Add(message);
        }

        return Task.CompletedTask;
    }

    public Task SendAsync(ReadOnlyMemory<IRespMessage> messages)
    {
        if (messages.Length != 0)
        {
            lock (_unsent)
            {
                ThrowIfDisposed();
#if NET8_0_OR_GREATER
                _unsent.AddRange(messages.Span); // internally optimized
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
                foreach (var message in messages.Span)
                {
                    _unsent.Add(message);
                }
#endif
            }
        }

        return Task.CompletedTask;
    }

    private int Flush(out IRespMessage[] oversized, out IRespMessage? single)
    {
        lock (_unsent)
        {
            var count = _unsent.Count;
            switch (count)
            {
                case 0:
                    oversized = [];
                    single = null;
                    break;
                case 1:
                    oversized = [];
                    single = _unsent[0];
                    break;
                default:
                    oversized = ArrayPool<IRespMessage>.Shared.Rent(count);
                    single = null;
                    _unsent.CopyTo(oversized);
                    break;
            }

            _unsent.Clear();
            return count;
        }
    }

    public Task FlushAsync()
    {
        var count = Flush(out var oversized, out var single);
        return count switch
        {
            0 => Task.CompletedTask,
            1 => _tail.SendAsync(single!),
            _ => SendAndRecycleAsync(_tail, oversized, count),
        };

        static async Task SendAndRecycleAsync(IRespConnection tail, IRespMessage[] oversized, int count)
        {
            try
            {
                await tail.SendAsync(oversized.AsMemory(0, count)).ConfigureAwait(false);
                ArrayPool<IRespMessage>.Shared.Return(oversized); // only on success, in case captured
            }
            catch (Exception ex)
            {
                foreach (var message in oversized.AsSpan(0, count))
                {
                    message.TrySetException(ex);
                }

                throw;
            }
        }
    }

    public void Flush()
    {
        var count = Flush(out var oversized, out var single);
        switch (count)
        {
            case 0:
                return;
            case 1:
                _tail.Send(single!);
                return;
        }

        try
        {
            _tail.Send(oversized.AsSpan(0, count));
        }
        catch (Exception ex)
        {
            foreach (var message in oversized.AsSpan(0, count))
            {
                message.TrySetException(ex);
            }

            throw;
        }
        finally
        {
            // in the sync case, Send takes a span - hence can't have been captured anywhere; always recycle
            ArrayPool<IRespMessage>.Shared.Return(oversized);
        }
    }
}
