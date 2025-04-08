using System;
using System.Buffers;
using System.IO.Pipelines;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace StackExchange.Redis;

// SyncBufferReader and SyncBufferWriter form two halves of a single entity.
// The write API is more granular (get, advance, etc), so that is being used
// as the primary API (to minimize method forwarding).
internal partial class SyncBufferWriter : PipeWriter
{
    // This type has independent single reader and single writer; the writer only appends from the
    // write-head, so: it doesn't need to synchronize with the reader; conversely, the reader can
    // only offer data up-to the write head, so the read *does* need to synchronize with the writer.
    // To achieve this, we offer a lock around updates to the write-head, which both reader and writer
    // must respect. There is no synchronization around the read-head.
    private object WriteSyncLock => this;

    private volatile bool writerCompleted;
    private Segment writeTail;
    private int writeOffer;
    private const int SEGMENT_BYTES = 64 * 1024, MIN_CHUNK_BYTES = 128;

    public SyncBufferWriter()
    {
        readHead = writeTail = new(SEGMENT_BYTES);
        readOffset = 0;
    }

    internal bool IsDrained // for debugging
    {
        get
        {
            var tail = writeTail;
            return tail.Memory.IsEmpty && ReferenceEquals(tail, readHead);
        }
    }

    private void CheckWritable()
    {
        if (writerCompleted) ThrowWriterCompleted();
        if (readerCompleted) ThrowReaderCompleted();
        static void ThrowWriterCompleted() => throw new InvalidOperationException("The writer has already been completed; additional writes are not allowed.");
        static void ThrowReaderCompleted() => throw new InvalidOperationException("The reader has already been completed; additional writes are not allowed.");
    }

    public override void Advance(int bytes)
    {
        CheckWritable();
        if (bytes != 0)
        {
            if (bytes < 0 | bytes > writeOffer) ThrowOutOfRange();
            lock (WriteSyncLock)
            {
                writeTail.Commit(bytes);
            }
            ResetReadProgress();
        }
        writeOffer = 0;

        static void ThrowOutOfRange() => throw new ArgumentOutOfRangeException(nameof(bytes));
    }

    public override void CancelPendingFlush() { }
    public override void Complete(Exception? exception = null)
    {
        writerCompleted = true;
        ResetReadProgress();
        Flush();
    }

    public virtual void Flush()
    {
        ResetReadProgress();
    }

    internal static ArraySegment<byte> GetSegment(ReadOnlyMemory<byte> memory)
    {
        return MemoryMarshal.TryGetArray(memory, out var segment) ? segment : Throw();
        static ArraySegment<byte> Throw() => throw new InvalidOperationException();
    }

    public override ValueTask<FlushResult> FlushAsync(CancellationToken cancellationToken = default)
    {
        Flush();
        return new(new FlushResult(false, readerCompleted));
    }

    private ArraySegment<byte> GetWritableChunk(int sizeHint)
    {
        CheckWritable();
        sizeHint = Math.Max(16, sizeHint);
        var tail = writeTail;
        var available = tail.Available;
        if (available < Math.Min(sizeHint, MIN_CHUNK_BYTES))
        {
            lock (WriteSyncLock)
            {
                writeTail = tail = tail.TruncateAndAppend(SEGMENT_BYTES);
            }
        }
        var chunk = tail.WritableChunk();
        writeOffer = chunk.Count;
        return chunk;
    }

    private bool ReleaseIfWriteEnd(in SequencePosition position)
    {
        Segment target;
        bool release;
        lock (WriteSyncLock)
        {
            target = writeTail;
            release = ReferenceEquals(target, position.GetObject())
                & target.Committed == position.GetInteger();
        }
        if (release)
        {
            target.Release();
        }
        return release;
    }

    public override Memory<byte> GetMemory(int sizeHint = 0)
    {
        var chunk = GetWritableChunk(sizeHint);
        return new(chunk.Array, chunk.Offset, chunk.Count);
    }
    public override Span<byte> GetSpan(int sizeHint = 0)
    {
        var chunk = GetWritableChunk(sizeHint);
        return new(chunk.Array, chunk.Offset, chunk.Count);
    }
}
