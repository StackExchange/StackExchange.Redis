using System;
using System.Buffers;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Threading.Tasks.Sources;

namespace StackExchange.Redis;

internal sealed class SwitchableBufferedStreamWriter : CycleBufferStreamWriter, IValueTaskSource
{
    private readonly TaskCompletionSource<bool> _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private ManualResetValueTaskSourceCore<bool> _readerTask;
    private bool _syncSignalled;

    public SwitchableBufferedStreamWriter(Stream target, CancellationToken cancellationToken, BufferOptions? bufferOptions, bool initiallySync)
        : base(target, cancellationToken, bufferOptions, initiallySync ? StateFlags.None : StateFlags.AsyncMode)
    {
        _readerTask.RunContinuationsAsynchronously = true; // we never want the flusher to take over the copying
        if (initiallySync)
        {
            Thread thread = new(static s => ((SwitchableBufferedStreamWriter)s!).CopyOutSync())
            {
                IsBackground = true,
                Priority = ThreadPriority.AboveNormal,
                Name = "SE.Redis Sync Writer",
            };
            thread.Start(this);
        }
        else
        {
            StartAsyncWorker(alreadyActive: false);
        }
    }

    public override bool IsSync
        => (State & StateFlags.AsyncMode) == 0;

    public override Task WriteComplete => _completion.Task;

    public override bool TransitionToAsync()
    {
        bool lockTaken = false;
        try
        {
            TakeLock(ref lockTaken);
            if ((State & (StateFlags.AsyncMode | StateFlags.Closed | StateFlags.TransitionToAsync)) != 0)
            {
                return false;
            }

            ActivateInsideLock(StateFlags.TransitionToAsync);
            return true;
        }
        finally
        {
            ReleaseLock(ref lockTaken);
        }
    }

    private void CopyOutSync()
    {
        bool lockTaken = false;
        try
        {
            while (true)
            {
                CancellationToken.ThrowIfCancellationRequested();
                TakeLock(ref lockTaken);
                if (TryTransitionToAsyncInsideLock())
                {
                    ReleaseLock(ref lockTaken);
                    return;
                }
                if (!_syncSignalled)
                {
                    RemoveStateFlagInsideLock(StateFlags.ActiveWriter);
                    // even if not pulsed, wake periodically to check for hard exit
                    Monitor.Wait(this, 10_000);
                    CancellationToken.ThrowIfCancellationRequested();
                }
                _syncSignalled = false;
                if (TryTransitionToAsyncInsideLock())
                {
                    ReleaseLock(ref lockTaken);
                    return;
                }
                ReleaseLock(ref lockTaken);

                StateFlags stateFlags;
                while (true)
                {
                    ReadOnlyMemory<byte> memory;
                    TakeLock(ref lockTaken);
                    if (TryTransitionToAsyncInsideLock())
                    {
                        ReleaseLock(ref lockTaken);
                        return;
                    }

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

#if NET
                        Target.Write(memory.Span);
#else
                        Target.Write(memory);
#endif
                    }

                    TakeLock(ref lockTaken);
                    DiscardCommitted(memory.Length);
                    ReleaseLock(ref lockTaken);
                }

                Target.Flush();

                if ((stateFlags & StateFlags.Closed) != 0) break;
            }

            // recycle on clean exit (only), since we know the buffers aren't being used
            TakeLock(ref lockTaken);
            ReleaseBuffer();
            ReleaseLock(ref lockTaken);

            _completion.TrySetResult(true);
        }
        catch (Exception ex)
        {
            Complete(ex);
            _completion.TrySetException(ex);
        }
        finally
        {
            ReleaseLock(ref lockTaken);
        }
        // note we do *not* close the stream here - we have to settle for flushing; Close is explicit
    }

    private async Task CopyOutAsync(bool alreadyActive)
    {
        bool lockTaken = false;
        try
        {
            while (true)
            {
                CancellationToken.ThrowIfCancellationRequested();
                if (alreadyActive)
                {
                    alreadyActive = false;
                }
                else
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
                }

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

            _completion.TrySetResult(true);
        }
        catch (Exception ex)
        {
            Complete(ex);
            _completion.TrySetException(ex);
        }
        finally
        {
            ReleaseLock(ref lockTaken);
        }
        // note we do *not* close the stream here - we have to settle for flushing; Close is explicit
    }

    private bool TryTransitionToAsyncInsideLock()
    {
        Debug.Assert(Monitor.IsEntered(this), $"{nameof(TryTransitionToAsyncInsideLock)} must be called while holding the writer lock.");

        if ((State & StateFlags.TransitionToAsync) == 0) return false;

        RemoveStateFlagInsideLock(StateFlags.TransitionToAsync);
        AddStateFlagInsideLock(StateFlags.AsyncMode);
        StartAsyncWorker(alreadyActive: true);
        return true;
    }

    private void StartAsyncWorker(bool alreadyActive)
    {
        _ = Task.Run(() => CopyOutAsync(alreadyActive));
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
        Debug.Assert(Monitor.IsEntered(this), $"{nameof(OnWakeReaderInsideLock)} must be called while holding the writer lock.");
        if ((State & StateFlags.AsyncMode) == 0)
        {
            _syncSignalled = true;
            Monitor.Pulse(this);
        }
        else
        {
            _readerTask.SetResult(true);
        }
    }
}
