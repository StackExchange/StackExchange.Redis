using System;
using System.Buffers;
using System.Diagnostics;
using System.IO;
using System.IO.Pipelines;
using System.Threading;
using System.Threading.Tasks;
using Pipelines.Sockets.Unofficial;

namespace StackExchange.Redis;

/// <summary>
/// A duplex pipe implemented using synchronous read/write. This is intended to be used with a dedicated
/// reader and writer, to prevent async-over-sync blockages.
/// </summary>
internal sealed class SyncPipe : IDuplexPipe, IMeasuredDuplexPipe
{
    public SyncPipe(Stream stream)
    {
        reader = new(stream);
        writer = new(stream);
    }

    private readonly SyncPipeReader reader;
    private readonly SyncPipeWriter writer;

    private sealed class SyncPipeReader : PipeReader
    {
        private const int PAGE_SIZE = 64 * 1024, MIN_READ = 1024;

        public SyncPipeReader(Stream stream)
        {
            this.stream = stream;
            head = tail = new(0);
        }

        private readonly Stream stream;
        private long totalBytesReceived;
        private long examinedTo;
        private bool isCompleted;
        public long TotalBytesReceived => Volatile.Read(ref totalBytesReceived);

        private Node head, tail;

        public override bool TryRead(out ReadResult result)
        {
            var end = tail.EndIndex;
            if (examinedTo >= end) ReadMoreData();

            result = new(CurrentBuffer(), false, isCompleted);
            return true;
        }

        private ReadOnlySequence<byte> CurrentBuffer() => new(head, head.Discarded, tail, tail.Committed);

        private void ReadMoreData()
        {
            if (isCompleted) return;

            var available = tail.AvailableBytes;
            if (available < MIN_READ)
            {
                // use a new segment
                tail.Trim();
                tail = new(tail);
            }
#if NETCOREAPP2_1_OR_GREATER || NETSTANDARD2_1_OR_GREATER
            int read = stream.Read(tail.AvailableSpan);
#else
            var segment = tail.AvailableSegment;
            int read = stream.Read(segment.Array, segment.Offset, segment.Count);
#endif
            if (read <= 0)
            {
                isCompleted = true;
            }
            else
            {
                totalBytesReceived += read;
                tail.Commit(read);
            }
        }

        public override ValueTask<ReadResult> ReadAsync(CancellationToken cancellationToken = default)
        {
            TryRead(out var result);
            return new(result);
        }

        public override void AdvanceTo(SequencePosition consumed) => AdvanceTo(consumed, consumed);

        private static long GetOffset(SequencePosition value)
        {
            if (value.GetObject() is not Node node) Throw();
            return node.RunningIndex + value.GetInteger();

            static void Throw() => throw new ArgumentException(nameof(value));
        }
        public override void AdvanceTo(SequencePosition consumed, SequencePosition examined)
        {
            var discardTo = GetOffset(consumed);
            var newExaminedTo = Math.Max(GetOffset(examined), discardTo);
            if (newExaminedTo > examinedTo)
            {
                examinedTo = newExaminedTo;
            }

            // discard entire pages
            while (head is not null && discardTo >= head.EndIndex)
            {
                var tmp = head;
                head = (Node)head.Next!;
                tmp.Recycle();
            }

            // special-case for reading everything
            if (head is null)
            {
                head = tail = new(discardTo);
            }
            else
            {
                // discard partial last page
                head.DiscardTo(discardTo);
            }
        }

        public override void CancelPendingRead() { }
        public override void Complete(Exception? exception = null)
        {
            isCompleted = true;
        }

        private sealed class Node : ReadOnlySequenceSegment<byte>
        {
            private byte[] buffer;
            public long EndIndex => RunningIndex + Committed;
            public int Discarded { get; private set; }
            public int Committed { get; private set; }
            public int AvailableBytes => Memory.Length - Committed;
            public Span<byte> AvailableSpan => new(buffer, Committed, buffer.Length - Committed);
            public ArraySegment<byte> AvailableSegment => new(buffer, Committed, buffer.Length - Committed);

            public void Trim()
            {
                var mem = Memory;
                if (mem.Length > Committed)
                {
                    Memory = mem.Slice(0, Committed);
                }
            }

            public void Commit(int bytes)
            {
                Debug.Assert(bytes > 0 && bytes < AvailableBytes);
                Committed += bytes;
            }

            public Node(Node previous)
            {
                Memory = buffer = ArrayPool<byte>.Shared.Rent(PAGE_SIZE);
                RunningIndex = previous.RunningIndex + previous.Committed;
                previous.Next = this;
            }

            public Node(long runningIndex)
            {
                Memory = buffer = ArrayPool<byte>.Shared.Rent(PAGE_SIZE);
                RunningIndex = runningIndex;
            }

            public void Recycle()
            {
                Next = null;
                Committed = Discarded = 0;
                var tmp = buffer;
                Memory = buffer = Array.Empty<byte>();
                if (buffer is { Length: > 0 } arr)
                {
                    ArrayPool<byte>.Shared.Return(arr);
                }
            }

            internal void DiscardTo(long value)
            {
                long newDiscard = value - RunningIndex;
                Debug.Assert(newDiscard >= Discarded && newDiscard < Committed); // not <= because we only expect this to be used for partial discards
                Discarded = (int)newDiscard;
            }
        }
    }

    private sealed class SyncPipeWriter : PipeWriter
    {
        public SyncPipeWriter(Stream stream)
            => this.stream = stream;

        private readonly Stream stream;
        private bool isCompleted;
        private int offered;
        private long totalBytesSent;
        public long TotalBytesSent => Volatile.Read(ref totalBytesSent);

        private byte[] buffer = Array.Empty<byte>();
        private int committed;

        public override void Advance(int bytes)
        {
            if (bytes < 0 | bytes > offered) Throw();
            offered = 0;
            committed += bytes;

            static void Throw() => throw new ArgumentOutOfRangeException(nameof(bytes));
        }
        public override void CancelPendingFlush() => throw new NotImplementedException();
        public override void Complete(Exception? exception = null)
        {
            Flush();
            ReleaseBuffer();
            isCompleted = true;
        }

        public override ValueTask<FlushResult> FlushAsync(CancellationToken cancellationToken = default)
        {
            Flush();
            return default;
        }
        public override Memory<byte> GetMemory(int sizeHint = 0)
        {
            offered = Offer(sizeHint);
            return new(buffer, committed, offered);
        }
        public override Span<byte> GetSpan(int sizeHint = 0)
        {
            offered = Offer(sizeHint);
            return new(buffer, committed, offered);
        }

        private const int MIN_HINT = 256, MAX_BUFFER = 8 * 1024;
        private int Offer(int sizeHint)
        {
            if (isCompleted) ThrowCompleted();
            sizeHint = Math.Min(Math.Max(sizeHint, MIN_HINT), MAX_BUFFER);

            // check if it already fits in what we have
            int available = buffer.Length - committed;
            return available >= sizeHint ? available : EnsureSlow(sizeHint);

            static void ThrowCompleted() => throw new InvalidOperationException("Pipe has been completed");
        }
        private int EnsureSlow(int sizeHint)
        {
            // check if it will fit in a larger buffer; upsize
            if (committed + sizeHint <= MAX_BUFFER)
            {
                var newBuffer = ArrayPool<byte>.Shared.Rent(committed + sizeHint);
                if (committed != 0)
                {
                    new ReadOnlySpan<byte>(buffer, 0, committed).CopyTo(newBuffer);
                }
                ReleaseBuffer();
                buffer = newBuffer;
                return newBuffer.Length - committed;
            }

            // cannot fit; flush what we have, and offer whatever we can
            Flush();
            if (sizeHint > buffer.Length)
            {
                // increase the buffer size
                ReleaseBuffer();
                buffer = ArrayPool<byte>.Shared.Rent(sizeHint);
            }
            return buffer.Length - committed;
        }

        private void Flush()
        {
            if (committed != 0)
            {
                stream.Write(buffer, 0, committed);
                totalBytesSent += committed;
            }
            committed = 0;
        }

        private void ReleaseBuffer()
        {
            if (buffer is { Length: > 0 })
            {
                ArrayPool<byte>.Shared.Return(buffer);
            }
            buffer = Array.Empty<byte>();
        }
    }

    public PipeReader Input => reader;

    public PipeWriter Output => writer;

    public long TotalBytesSent => writer.TotalBytesSent;

    public long TotalBytesReceived => reader.TotalBytesReceived;
}
