using System;
using System.Buffers;
using System.Diagnostics;
using System.IO.Pipelines;
using System.Runtime.CompilerServices;

namespace StackExchange.Redis;

internal partial class SyncBufferWriter : PipeWriter
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

        public void Commit(int bytes)
        {
            if (bytes < 0 | bytes > Available) ThrowOutOfRange();
            committed += bytes;

            [MethodImpl(MethodImplOptions.NoInlining)]
            static void ThrowOutOfRange() => throw new ArgumentOutOfRangeException(nameof(bytes));
        }

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

        internal Memory<byte> GetWritableMemory() => new(buffer, committed, buffer.Length - committed);

        internal Span<byte> GetWritableSpan() => new(buffer, committed, buffer.Length - committed);
    }
}
