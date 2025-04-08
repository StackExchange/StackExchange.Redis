using System;
using System.Diagnostics;
using System.IO;
using System.IO.Pipelines;

namespace StackExchange.Redis;

// synchronous read-thru pipe buffer
internal sealed class InputStreamSyncBufferReader(Stream source) : SyncBufferWriter
{
    public override ReadResult Read()
    {
        var read = base.Read();
        if (read.Buffer.IsEmpty && !read.IsCompleted
            && ReadMore())
        {
            read = base.Read();
            Debug.Assert(!read.Buffer.IsEmpty);
        }
        return read;
    }

    private bool ReadMore()
    {
        try
        {
            var segment = GetSegment(GetMemory());
            int bytes = source.Read(segment.Array!, segment.Offset, segment.Count);
            if (bytes > 0)
            {
                Advance(bytes);
                return true;
            }
            else
            {
                Complete(null);
            }
        }
        catch (Exception ex)
        {
            Complete(ex);
        }
        return false;
    }
}
