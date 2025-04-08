using System;
using System.Buffers;
using System.Diagnostics;
using System.IO.Pipelines;

namespace StackExchange.Redis;

internal sealed partial class SyncBufferWriter : PipeWriter
{
    private sealed class Segment : ReadOnlySequenceSegment<byte>
    {
        private byte[] buffer;
        private int committed;
        public int Committed => committed;

        public int Available => buffer.Length - committed;

        public Segment(int minSize)
        {
            buffer = ArrayPool<byte>.Shared.Rent(minSize);
            Memory = buffer;
        }

        public Segment TruncateAndAppend(int minSize)
        {
            Debug.Assert(committed > 0);
            if (committed != buffer.Length)
            {
                // trim
                Memory = new(buffer, 0, committed);
            }
            var next = new Segment(minSize)
            {
                RunningIndex = RunningIndex + committed,
            };
            Next = next;
            return next;
        }

        public void Commit(int bytes) => committed += bytes;

        public void Release()
        {
            var tmp = buffer;
            Next = null;
            committed = 0;
            RunningIndex = 0;
            Memory = buffer = [];
            if (tmp.Length != 0)
            {
                ArrayPool<byte>.Shared.Return(tmp);
            }
        }

        internal ArraySegment<byte> WritableChunk() => new(buffer, committed, buffer.Length - committed);
    }
}
