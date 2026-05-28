using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Threading.Tasks.Sources;

namespace StackExchange.Redis;

internal sealed class BufferedAsyncStreamWriter : CycleBufferStreamWriter, IValueTaskSource
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
        bool lockTaken = false;
        try
        {
            while (true)
            {
                ValueTask pending = AwaitWake();
                if (!pending.IsCompleted)
                {
                    TakeLock(ref lockTaken);
                    // double-checked marking inactive
                    if (!pending.IsCompleted) RemoveStateFlagInsideLock(StateFlags.ActiveWriter);
                    ReleaseLock(ref lockTaken);
                }
                // await activation and check status;
                await pending.ConfigureAwait(false);

                StateFlags stateFlags;
                while (true)
                {
                    ReadOnlyMemory<byte> memory;
                    TakeLock(ref lockTaken);
                    stateFlags = State;
                    var minBytes = (stateFlags & StateFlags.Flush) == 0 ? -1 : 1;
                    if (!GetFirstChunkInsideLock(minBytes, out memory))
                    {
                        // out of data; remove flush flag and wait for more work
                        RemoveStateFlagInsideLock(StateFlags.Flush | StateFlags.ActiveWriter);
                        stateFlags = State;
                        ReleaseLock(ref lockTaken);
                        break;
                    }
                    ReleaseLock(ref lockTaken);

                    if (IsFaulted) ThrowCompleteOrFaulted(); // this is cheap to check ongoing
                    if (!memory.IsEmpty)
                    {
                        OnWritten(memory.Length);
                        OnDebugBufferLog(memory);

                        await Target.WriteAsync(memory, CancellationToken).ConfigureAwait(false);
                    }

                    TakeLock(ref lockTaken);
                    DiscardCommitted(memory.Length);
                    ReleaseLock(ref lockTaken);
                }
                await Target.FlushAsync(CancellationToken).ConfigureAwait(false);

                if ((stateFlags & StateFlags.Closed) != 0) break;
            }

            // recycle on clean exit (only), since we know the buffers aren't being used
            TakeLock(ref lockTaken);
            ReleaseBuffer();
            ReleaseLock(ref lockTaken);
        }
        catch (Exception ex)
        {
            Complete(ex);
            throw; // ensure visible via WriteComplete
        }
        finally
        {
            ReleaseLock(ref lockTaken);
        }
        // note we do *not* close the stream here - we have to settle for flushing; Close is explicit
    }

    private ValueTask AwaitWake()
    {
        bool lockTaken = false;
        try
        {
            TakeLock(ref lockTaken); // guard all transitions
            return new(this, _readerTask.Version);
        }
        finally
        {
            ReleaseLock(ref lockTaken);
        }
    }

    void IValueTaskSource.GetResult(short token)
    {
        bool lockTaken = false;
        try
        {
            TakeLock(ref lockTaken); // guard all transitions
            _readerTask.GetResult(token); // may throw, note
            _readerTask.Reset();
        }
        finally
        {
            ReleaseLock(ref lockTaken);
        }
    }

    ValueTaskSourceStatus IValueTaskSource.GetStatus(short token)
    {
        bool lockTaken = false;
        try
        {
            TakeLock(ref lockTaken); // guard all transitions
            return _readerTask.GetStatus(token);
        }
        finally
        {
            ReleaseLock(ref lockTaken);
        }
    }

    void IValueTaskSource.OnCompleted(
        Action<object?> continuation,
        object? state,
        short token,
        ValueTaskSourceOnCompletedFlags flags)
    {
        bool lockTaken = false;
        try
        {
            TakeLock(ref lockTaken); // guard all transitions
            _readerTask.OnCompleted(continuation, state, token, flags);
        }
        finally
        {
            ReleaseLock(ref lockTaken);
        }
    }

    protected override void OnWakeReaderInsideLock()
    {
        _readerTask.SetResult(true);
    }
}
