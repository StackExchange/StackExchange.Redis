using System;
using System.Buffers;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Threading.Tasks.Sources;
using RESPite.Buffers;

namespace RESPite.Streams;

internal sealed class SwitchableBufferedStreamWriter : CycleBufferStreamWriter, IValueTaskSource
{
    private readonly TaskCompletionSource<bool> _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private ManualResetValueTaskSourceCore<bool> _readerTask;
    private bool _syncSignalled;

    public SwitchableBufferedStreamWriter(MemoryPool<byte>? pool, Stream target, CancellationToken cancellationToken, bool initiallySync)
        : base(pool, target, cancellationToken, initiallySync ? StateFlags.None : StateFlags.AsyncMode)
    {
        _readerTask.RunContinuationsAsynchronously = true; // we never want the flusher to take over the copying
        _inlineCapable = target is System.Net.Sockets.NetworkStream or System.Net.Security.SslStream;
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

    // On a deliberate teardown the transport can be disposed (or the write cancelled) underneath an
    // in-flight write; the resulting ObjectDisposedException/OperationCanceledException is expected noise,
    // not a novel fault - the real failure is reported via the connection's disconnect path. Gate strictly
    // on the Closed flag (set before the socket is disposed) so a genuine mid-operation fault still surfaces.
    private bool IsExpectedDuringClose(Exception ex)
        => (State & StateFlags.Closed) != 0 && ex is ObjectDisposedException or OperationCanceledException;

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
        catch (Exception ex) when (IsExpectedDuringClose(ex))
        {
            Complete(); // ensure Closed; do not record teardown noise as the fault
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
                        // double-checked marking inactive - unless the writer is a caller sending inline, which may
                        // have claimed it since this loop last went idle: it is that caller's to release
                        if (!pending.IsCompleted && (State & StateFlags.InlineSending) == 0) RemoveStateFlagInsideLock(StateFlags.ActiveWriter);
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
        catch (Exception ex) when (IsExpectedDuringClose(ex))
        {
            Complete(); // ensure Closed; do not record teardown noise as the fault
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

    /// <inheritdoc/>
    /// <remarks>
    /// <para>
    /// <b>The caller sends its own bytes when nobody else is writing</b>, rather than waking the writer loop: on a
    /// socket with buffer room the write completes synchronously, so a sequential request costs no hand-off - and a
    /// hand-off is a thread-pool worker woken, which then spins idle (see <c>FeatureFlags.InlineSends</c>).
    /// </para>
    /// <para>
    /// <b>Bounded, so a caller does not become everyone's sender.</b> It claims the writer (<see cref="StateFlags.ActiveWriter"/>)
    /// only when it is idle, and sends only what was committed when it claimed it. Anything committed since is the
    /// loop's, and so is a write that does not complete synchronously - the kernel's buffer is full - which is
    /// finished asynchronously and then handed over. Hand-over and going idle happen in one hold of the lock, with
    /// the check for more data, as the loop's own going-idle does; so no wake-up is lost.
    /// </para>
    /// <para>
    /// Only over a <see cref="System.Net.Sockets.NetworkStream"/> or an <see cref="System.Net.Security.SslStream"/>:
    /// the loop flushes its stream after releasing the writer, which with these is a no-op but with a buffering
    /// stream would overlap an inline write.
    /// </para>
    /// </remarks>
    public override void FlushInline()
    {
        if (!_inlineCapable || (State & StateFlags.AsyncMode) == 0)
        {
            Flush(); // a dedicated writer thread, or a stream we cannot write beside the loop
            return;
        }

        long budget;
        bool lockTaken = false;
        try
        {
            TakeLock(ref lockTaken);
            var state = State;
            if ((state & StateFlags.Closed) != 0) return;
            if ((state & (StateFlags.ActiveWriter | StateFlags.TransitionToAsync)) != 0)
            {
                AddStateFlagInsideLock(StateFlags.Flush); // a writer is active, and will see these bytes
                return;
            }

            budget = GetCommittedLengthInsideLock();
            if (budget == 0) return;
            AddStateFlagInsideLock(StateFlags.ActiveWriter | StateFlags.InlineSending | StateFlags.Flush); // ours, until handed over
        }
        finally
        {
            ReleaseLock(ref lockTaken);
        }

        SendInline(budget);
    }

    private readonly bool _inlineCapable;

    private void SendInline(long budget)
    {
        bool lockTaken = false;
        try
        {
            while (budget > 0)
            {
                ReadOnlyMemory<byte> memory;
                TakeLock(ref lockTaken);
                var any = GetFirstChunkInsideLock(1, out memory);
                ReleaseLock(ref lockTaken);
                if (!any) break;

                if (memory.Length > budget) memory = memory.Slice(0, (int)budget);
                if (IsFaulted) ThrowCompleteOrFaulted();
                OnWritten(memory.Length);
                OnDebugBufferLog(memory);

                var pending = Target.WriteAsync(memory, CancellationToken);
                if (!pending.IsCompletedSuccessfully)
                {
                    // would block (or failed): finished asynchronously, and then the loop's
                    _ = CompleteInlineAsync(pending, memory.Length);
                    return;
                }

                pending.GetAwaiter().GetResult();
                TakeLock(ref lockTaken);
                DiscardCommitted(memory.Length);
                ReleaseLock(ref lockTaken);
                budget -= memory.Length;
            }

            TakeLock(ref lockTaken);
            HandOverOrIdleInsideLock();
        }
        catch (Exception ex)
        {
            OnInlineFault(ref lockTaken, ex);
        }
        finally
        {
            ReleaseLock(ref lockTaken);
        }
    }

    private async Task CompleteInlineAsync(ValueTask pending, int length)
    {
        bool lockTaken = false;
        try
        {
            await pending.ConfigureAwait(false);
            TakeLock(ref lockTaken);
            DiscardCommitted(length);
            HandOverOrIdleInsideLock();
        }
        catch (Exception ex)
        {
            OnInlineFault(ref lockTaken, ex);
        }
        finally
        {
            ReleaseLock(ref lockTaken);
        }
    }

    /// <summary>Done sending inline: hand anything left to the loop, or go idle - in one hold of the lock.</summary>
    private void HandOverOrIdleInsideLock()
    {
        Debug.Assert(Monitor.IsEntered(this), $"{nameof(HandOverOrIdleInsideLock)} must be called while holding the writer lock.");
        if (GetCommittedLengthInsideLock() != 0)
        {
            // the loop is parked (the writer was ours); it takes over, ActiveWriter and all
            RemoveStateFlagInsideLock(StateFlags.InlineSending);
            OnWakeReaderInsideLock();
        }
        else
        {
            RemoveStateFlagInsideLock(StateFlags.Flush | StateFlags.ActiveWriter | StateFlags.InlineSending);
        }
    }

    /// <summary>A failed inline send: release the writer, then fault it - which wakes the loop to close, as its own failures do.</summary>
    private void OnInlineFault(ref bool lockTaken, Exception ex)
    {
        TakeLock(ref lockTaken);
        RemoveStateFlagInsideLock(StateFlags.ActiveWriter | StateFlags.InlineSending);
        ReleaseLock(ref lockTaken);
        if (IsExpectedDuringClose(ex)) Complete();
        else Complete(ex);
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
