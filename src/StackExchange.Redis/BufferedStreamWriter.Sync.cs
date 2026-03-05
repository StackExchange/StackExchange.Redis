using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace StackExchange.Redis;

internal sealed class BuffereSyncStreamWriter : BufferedStreamWriter
{
    private readonly TaskCompletionSource<bool> _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public BuffereSyncStreamWriter(Stream target, CancellationToken cancellationToken = default)
        : base(target, cancellationToken)
    {
        Thread thread = new Thread(static s => ((BuffereSyncStreamWriter)s!).CopyOutSync())
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
        try
        {
            while (true)
            {
                CancellationToken.ThrowIfCancellationRequested();
                lock (this)
                {
                    if (!_signalled)
                    {
                        OnWriterInactive();
                        // even if not pulsed, wake periodically to check for hard exit
                        Monitor.Wait(this, 10_000);
                        CancellationToken.ThrowIfCancellationRequested();
                    }
                    _signalled = false;
                }

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

#if NET || NETSTANDARD2_1_OR_GREATER
                        Target.Write(memory.Span);
#else
                        Target.Write(memory);
#endif
                    }

                    lock (this)
                    {
                        DiscardCommitted(memory.Length);
                    }
                }

                Target.Flush();

                if ((stateFlags & StateFlags.Closed) != 0) break;
            }

            // recycle on clean exit (only), since we know the buffers aren't being used
            lock (this)
            {
                ReleaseBuffer();
            }
            _completion.TrySetResult(true);
        }
        catch (Exception ex)
        {
            Complete(ex);
            _completion.TrySetException(ex);
        }
        // note we do *not* close the stream here - we have to settle for flushing; Close is explicit
    }

    protected override void OnWakeReader()
    {
        lock (this)
        {
            _signalled = true;
            Monitor.Pulse(this);
        }
    }
}
