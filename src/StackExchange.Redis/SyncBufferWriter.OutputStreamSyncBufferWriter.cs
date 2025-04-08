using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;

namespace StackExchange.Redis;

// write-through pipe buffer with deferred write (to avoid packet fragmentation)
internal sealed class OutputStreamSyncBufferWriter(Stream destination) : SyncBufferWriter
{
    private readonly object progressLock = new();
    private bool progressReleased;

    public void StartWriteLoop()
    {
        Thread thread = new Thread(static s => ((OutputStreamSyncBufferWriter)s!).CopyFromPipeToStream())
        {
            Priority = ThreadPriority.AboveNormal,
            Name = "StreamWriterLoop",
        };
        thread.Start(this);
    }

    public override void Flush()
    {
        lock (progressLock)
        {
            progressReleased = true;
            Monitor.Pulse(progressLock);
        }
        base.Flush();
    }

    private void CopyFromPipeToStream()
    {
        try
        {
            while (true)
            {
                var read = Read();
                var buffer = read.Buffer;
                if (buffer.IsEmpty)
                {
                    if (buffer.IsSingleSegment)
                    {
                        var segment = GetSegment(buffer.First);
                        destination.Write(segment.Array!, segment.Offset, segment.Count);
                    }
                    else
                    {
                        foreach (var chunk in buffer)
                        {
                            var segment = GetSegment(chunk);
                            destination.Write(segment.Array!, segment.Offset, segment.Count);
                        }
                    }
                }
                else
                {
                    // exhausted current data; flush
                    destination.Flush();
                }
                var end = buffer.End;
                AdvanceReaderTo(end, end);

                if (read.IsCompleted)
                {
                    break; // EOF
                }

                if (buffer.IsEmpty)
                {
                    // wait for more data
                    lock (progressLock)
                    {
                        if (!progressReleased)
                        {
                            Monitor.Wait(progressLock);
                            progressReleased = false;
                        }
                    }
                }
            }
            CompleteReader(null);
        }
        catch (Exception ex)
        {
            CompleteReader(ex);
        }
    }
}
