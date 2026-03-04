using System;
using System.Buffers;
using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using System.Threading.Tasks.Sources;
using RESPite.Buffers;

namespace StackExchange.Redis;

internal sealed class BufferedStreamWriter : IBufferWriter<byte>, IDisposable, ICycleBufferCallback, IValueTaskSource<bool>
{
    public BufferedStreamWriter(Stream target, CancellationToken cancellationToken = default)
    {
        _target = target;
        _buffer = CycleBuffer.Create(callback: this);
        _cancellationToken = cancellationToken;
        WriteComplete = Task.Run(CopyOutAsync, cancellationToken);
    }

    public Task WriteComplete { get; }

    private CycleBuffer _buffer;
    private readonly Stream _target;
    private readonly CancellationToken _cancellationToken;
    private StateFlags _stateFlags;
    private Exception? _exception;

    [Flags]
    private enum StateFlags
    {
        None = 0,
        Flush = 1 << 0, // allow reading incomplete pages
        ActiveWriter = 1 << 1,
        Closed = 1 << 2,
    }

    private ManualResetValueTaskSourceCore<bool> _readerTask;

    void ICycleBufferCallback.PageComplete() => OnActivate(StateFlags.None);

    public void Flush() => OnActivate(StateFlags.Flush);

    public void Complete(Exception? exception = null)
    {
        _exception ??= exception;
        OnActivate(StateFlags.Flush | StateFlags.Closed);
    }

    private void OnActivate(StateFlags newFlags)
    {
        bool activate = false;
        lock (this)
        {
            var state = _stateFlags;
            if ((state & StateFlags.Closed) != 0) return;
            state |= newFlags & ~StateFlags.ActiveWriter;
            if ((state & StateFlags.ActiveWriter) == 0)
            {
                state |= StateFlags.ActiveWriter;
                activate = true;
            }
            _stateFlags = state;
        }
        if (activate) _readerTask.SetResult(true);
    }

    public void Advance(int count)
    {
        ThrowIfComplete();
        lock (this)
        {
            _buffer.Commit(count);
        }
    }

    public Memory<byte> GetMemory(int sizeHint = 0)
    {
        ThrowIfComplete();
        lock (this)
        {
            return _buffer.GetUncommittedMemory(sizeHint);
        }
    }

    public Span<byte> GetSpan(int sizeHint = 0)
    {
        ThrowIfComplete();
        lock (this)
        {
            return _buffer.GetUncommittedSpan(sizeHint);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void ThrowIfComplete()
    {
        // prevents a writer continuing to write to a dead pipe
        if ((_stateFlags & StateFlags.Closed) != 0) ThrowComplete();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void ThrowComplete()
    {
        var ex = _exception;
        if (ex is null) throw new InvalidOperationException("Output has been completed successfully.");
        throw new InvalidOperationException($"Output has been completed with fault: " + ex.Message, ex);
    }

    private ValueTask<bool> WorkAvailable => new(this, _readerTask.Version);
    private async Task CopyOutAsync()
    {
        try
        {
            while (await WorkAvailable.ConfigureAwait(false))
            {
                StateFlags stateFlags;
                while (true)
                {
                    ReadOnlyMemory<byte> memory;
                    lock (this)
                    {
                        stateFlags = _stateFlags;
                        if ((stateFlags & StateFlags.Closed) != 0) return;
                        var minBytes = (stateFlags & StateFlags.Flush) == 0 ? -1 : 1;
                        if (!_buffer.TryGetFirstCommittedMemory(minBytes, out memory))
                        {
                            // out of data; remove flush flag and wait for more work
                            stateFlags &= ~StateFlags.Flush;
                            break;
                        }
                    }

                    if (memory.IsEmpty) continue; // empty segment :shrug:
                    _totalBytesWritten += memory.Length;
                    await _target.WriteAsync(memory, _cancellationToken).ConfigureAwait(false);
                }
                await _target.FlushAsync(_cancellationToken).ConfigureAwait(false);

                if ((stateFlags & StateFlags.Closed) != 0) break;
            }
            lock (this)
            {
                _buffer.Release();
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine(ex.Message);
        }
        finally
        {
            _target.Close();
        }
    }

    private long _totalBytesWritten;
    public long TotalBytesWritten => _totalBytesWritten;
    public void Dispose()
    {
        _target.Dispose();
    }

    bool IValueTaskSource<bool>.GetResult(short token)
    {
        var result = _readerTask.GetResult(token);
        _readerTask.Reset();
        return result;
    }

    ValueTaskSourceStatus IValueTaskSource<bool>.GetStatus(short token) => _readerTask.GetStatus(token);

    void IValueTaskSource<bool>.OnCompleted(Action<object?> continuation, object? state, short token, ValueTaskSourceOnCompletedFlags flags)
        => _readerTask.OnCompleted(continuation, state, token, flags);
}
