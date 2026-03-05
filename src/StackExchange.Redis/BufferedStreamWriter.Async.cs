using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Threading.Tasks.Sources;

namespace StackExchange.Redis;

internal sealed class BufferedAsyncStreamWriter : BufferedStreamWriter, IValueTaskSource
{
    private ManualResetValueTaskSourceCore<bool> _readerTask;

    public BufferedAsyncStreamWriter(Stream target, CancellationToken cancellationToken = default)
        : base(target, cancellationToken)
    {
        WriteComplete = Task.Run(CopyOutAsync, cancellationToken);
        _readerTask.RunContinuationsAsynchronously = true; // we never want the flusher to take over the copying
    }

    public override Task WriteComplete { get; }

    private async Task CopyOutAsync()
    {
        try
        {
            while (true)
            {
                ValueTask pending = new(this, _readerTask.Version);
                if (!pending.IsCompleted)
                {
                    lock (this)
                    {
                        // double-checked marking inactive
                        if (!pending.IsCompleted) OnWriterInactive(); // update state flags
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
                        stateFlags = State;
                        var minBytes = (stateFlags & StateFlags.Flush) == 0 ? -1 : 1;
                        if (!TryGetFirstCommittedMemory(minBytes, out memory))
                        {
                            // out of data; remove flush flag and wait for more work
                            stateFlags &= ~StateFlags.Flush;
                            break;
                        }
                    }

                    if (IsFaulted) ThrowCompleteOrFaulted(); // this is cheap to check ongoing
                    if (!memory.IsEmpty)
                    {
                        OnWritten(memory.Length);
                        OnDebugBufferLog(memory);

                        await Target.WriteAsync(memory, CancellationToken).ConfigureAwait(false);
                    }

                    lock (this)
                    {
                        DiscardCommitted(memory.Length);
                    }
                }
                await Target.FlushAsync(CancellationToken).ConfigureAwait(false);

                if ((stateFlags & StateFlags.Closed) != 0) break;
            }

            // recycle on clean exit (only), since we know the buffers aren't being used
            lock (this)
            {
                ReleaseBuffer();
            }
        }
        catch (Exception ex)
        {
            Complete(ex);
        }
        // note we do *not* close the stream here - we have to settle for flushing; Close is explicit
    }

    void IValueTaskSource.GetResult(short token)
    {
        _readerTask.GetResult(token); // may throw, note
        _readerTask.Reset();
    }

    ValueTaskSourceStatus IValueTaskSource.GetStatus(short token) => _readerTask.GetStatus(token);

    void IValueTaskSource.OnCompleted(Action<object?> continuation, object? state, short token, ValueTaskSourceOnCompletedFlags flags)
        => _readerTask.OnCompleted(continuation, state, token, flags);

    protected override void OnWakeReader() => _readerTask.SetResult(true);
}
