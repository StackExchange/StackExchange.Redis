using System;
using System.IO;
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
        if (!progressReleased)
        {
            lock (progressLock)
            {
                progressReleased = true;
                Monitor.Pulse(progressLock);
            }
        }

        base.Flush();
    }

    private void CopyFromPipeToStream()
    {
        try
        {
            while (true)
            {
                DebugLog("Write", "Checking for data to write");
                var read = Read();
                DebugLog("Write", $"Got {read.Buffer.Length} bytes; completed: {read.IsCompleted}");
                var buffer = read.Buffer;

                if (!buffer.IsEmpty)
                {
                    if (buffer.IsSingleSegment)
                    {
                        var segment = GetSegment(buffer.First);
                        if (segment.Count != 0)
                        {
                            destination.Write(segment.Array!, segment.Offset, segment.Count);
                        }
                    }
                    else
                    {
                        foreach (var chunk in buffer)
                        {
                            var segment = GetSegment(chunk);
                            if (segment.Count != 0)
                            {
                                destination.Write(segment.Array!, segment.Offset, segment.Count);
                            }
                        }
                    }
                }
                var end = buffer.End;
                AdvanceReaderTo(end, end);

                if (!buffer.IsEmpty)
                {
                    // exhausted current data; flush
                    destination.Flush();
                    DebugLog("Write", $"Flushed {buffer.Length} bytes");
                }

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
                            DebugLog("Write", "Waiting for data...");
                            Monitor.Wait(progressLock);
                            DebugLog("Write", "Unblocked");
                        }
                        progressReleased = false;
                    }
                }
            }

            DebugLog("Write", "Writer complete");
            CompleteReader(null);
        }
        catch (Exception ex)
        {
            DebugLog("Write", $"Fault: {ex.Message}, {ex.StackTrace}");
            CompleteReader(ex);
        }
    }
}
