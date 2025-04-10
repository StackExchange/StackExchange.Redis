using System;
using System.IO;
using System.IO.Pipelines;

namespace StackExchange.Redis;

// synchronous read-thru pipe buffer
internal sealed class InputStreamSyncBufferReader : SyncBufferWriter
{
    private readonly Stream source;

    public InputStreamSyncBufferReader(Stream source)
    {
        this.source = source;
        Reader = new SyncBufferReader(this);
    }

    public PipeReader Reader { get; }

    protected override void RequestMoreData()
    {
        try
        {
            var segment = GetSegment(GetMemory());
            DebugLog("Read", $"Reading from source (buffer; {segment.Count})");
            int bytes = source.Read(segment.Array!, segment.Offset, segment.Count);
            if (bytes > 0)
            {
                Advance(bytes);
                DebugLog("Read", $"Received {bytes} bytes");
            }
            else
            {
                DebugLog("Read", "EOF");
                Complete(null);
            }
        }
        catch (Exception ex)
        {
            DebugLog("Read", $"Fault: {ex.Message}");
            Complete(ex);
        }
    }
}
