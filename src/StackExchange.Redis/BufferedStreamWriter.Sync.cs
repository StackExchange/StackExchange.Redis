using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace StackExchange.Redis;

internal sealed class BufferedSyncStreamWriter : CycleBufferStreamWriter
{
    public override bool IsSync => true;

    private readonly TaskCompletionSource<bool> _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public BufferedSyncStreamWriter(Stream target, CancellationToken cancellationToken = default)
        : base(target, cancellationToken)
    {
        Thread thread = new Thread(static s => ((BufferedSyncStreamWriter)s!).CopyOutSync())
        {
            IsBackground = true,
            Priority = ThreadPriority.AboveNormal,
            Name = "SE.Redis Sync Writer",
        };
        thread.Start(this);
    }

    public override Task WriteComplete => _completion.Task;

    private bool _signalled;

    private void CopyOutSync()
    {
        bool lockTaken = false;
        try
        {
            while (true)
            {
                CancellationToken.ThrowIfCancellationRequested();
                TakeLock(ref lockTaken);
                if (!_signalled)
                {
                    RemoveStateFlagInsideLock(StateFlags.ActiveWriter);
                    // even if not pulsed, wake periodically to check for hard exit
                    Monitor.Wait(this, 10_000);
                    CancellationToken.ThrowIfCancellationRequested();
                }
                _signalled = false;
                ReleaseLock(ref lockTaken);

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

    protected override void OnWakeReaderInsideLock()
    {
        System.Diagnostics.Debug.Assert(Monitor.IsEntered(this), $"{nameof(OnWakeReaderInsideLock)} must be called while holding the writer lock.");
        _signalled = true;
        Monitor.Pulse(this);
    }
}
