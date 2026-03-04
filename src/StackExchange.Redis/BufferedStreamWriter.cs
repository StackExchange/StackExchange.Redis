using System;
using System.Buffers;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Threading.Tasks.Sources;
using RESPite.Buffers;

namespace StackExchange.Redis;

internal sealed class BufferedStreamWriter
    : IBufferWriter<byte>,
    IDisposable,
    ICycleBufferCallback,
    IValueTaskSource,
    IAsyncDisposable
{
    public BufferedStreamWriter(Stream target, CancellationToken cancellationToken = default)
    {
        _target = target;
        _buffer = CycleBuffer.Create(callback: this);
        _cancellationToken = cancellationToken;
        WriteComplete = Task.Run(CopyOutAsync, cancellationToken);
        _readerTask.RunContinuationsAsynchronously = true; // we never want the flusher to take over the copying
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

    /// <summary>
    /// Activate the writer if necessary, but only consume complete pages.
    /// </summary>
    void ICycleBufferCallback.PageComplete() => OnActivate(StateFlags.None);

    /// <summary>
    /// Activate the writer if necessary, and indicate that all committed data can be consumed, even incomplete pages.
    /// </summary>
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
        if ((_stateFlags & StateFlags.Closed) != 0) ThrowCompleteOrFaulted();
    }

    [MethodImpl(MethodImplOptions.NoInlining), DoesNotReturn]
    private void ThrowCompleteOrFaulted()
    {
        var ex = _exception;
        if (ex is null) throw new InvalidOperationException("Output has been completed successfully.");
        throw new InvalidOperationException($"Output has been completed with fault: " + ex.Message, ex);
    }

#if DEBUG
    private readonly int id = Interlocked.Increment(ref s_id);
    private static int s_id;
#endif

    private async Task CopyOutAsync()
    {
        try
        {
#if DEBUG
            int fragment = 0;
#endif
            while (true)
            {
                ValueTask pending = new(this, _readerTask.Version);
                if (!pending.IsCompleted)
                {
                    lock (this)
                    {
                        // double-checked marking inactive
                        if (!pending.IsCompleted)
                        {
                            _stateFlags &= ~StateFlags.ActiveWriter;
                        }
                    }
                }
                // await activation and check status;
                await pending.ConfigureAwait(false);

                StateFlags stateFlags;
                while (true)
                {
                    ReadOnlyMemory<byte> memory;
                    lock (this)
                    {
                        stateFlags = _stateFlags;
                        var minBytes = (stateFlags & StateFlags.Flush) == 0 ? -1 : 1;
                        if (!_buffer.TryGetFirstCommittedMemory(minBytes, out memory))
                        {
                            // out of data; remove flush flag and wait for more work
                            stateFlags &= ~StateFlags.Flush;
                            break;
                        }
                    }

                    if (_exception is not null) ThrowCompleteOrFaulted(); // this is cheap to check ongoing
                    if (!memory.IsEmpty)
                    {
                        _totalBytesWritten += memory.Length;
#if DEBUG
                        if (_log is not null)
                        {
                            const string CR = "\u240D", LF = "\u240A", CRLF = CR + LF;
                            string raw = Encoding.UTF8.GetString(memory.Span)
                                .Replace("\r\n", CRLF).Replace("\r", CR).Replace("\n", LF);
                            string s = $"{id}.{fragment++}: {raw}";
                            OnDebugLog(s);
                        }
#endif
                        await _target.WriteAsync(memory, _cancellationToken).ConfigureAwait(false);
                    }

                    lock (this)
                    {
                        _buffer.DiscardCommitted(memory.Length);
                    }
                }
                await _target.FlushAsync(_cancellationToken).ConfigureAwait(false);

                if ((stateFlags & StateFlags.Closed) != 0) break;
            }

            // recycle on clean exit (only), since we know the buffers aren't being used
            lock (this)
            {
                _buffer.Release();
            }
        }
        catch (Exception ex)
        {
            Complete(ex);
        }
        // note we do *not* close the stream here - we have to settle for flushing; Close is explicit
    }

    [Conditional("DEBUG")]
    private void OnDebugLog(string message)
    {
#if DEBUG
        // deliberately get away from the working thread
        ThreadPool.QueueUserWorkItem(_ => _log?.Invoke(message));
#endif
    }
    [Conditional("DEBUG")]
    public void DebugSetLog(Action<string> log)
    {
#if DEBUG
        _log = log;
#endif
    }
#if DEBUG
    private Action<string>? _log;
#endif

    private long _totalBytesWritten;
    public long TotalBytesWritten => _totalBytesWritten;

    public void Dispose() => _target.Dispose();

    public ValueTask DisposeAsync()
    {
        if (_target is IAsyncDisposable asyncDisposable)
        {
            return asyncDisposable.DisposeAsync();
        }
        _target.Dispose();
        return default;
    }

    void IValueTaskSource.GetResult(short token)
    {
        _readerTask.GetResult(token); // may throw, note
        _readerTask.Reset();
    }

    ValueTaskSourceStatus IValueTaskSource.GetStatus(short token) => _readerTask.GetStatus(token);

    void IValueTaskSource.OnCompleted(Action<object?> continuation, object? state, short token, ValueTaskSourceOnCompletedFlags flags)
        => _readerTask.OnCompleted(continuation, state, token, flags);
}
