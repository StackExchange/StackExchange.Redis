using System;
using System.Diagnostics;
using System.IO.Pipelines;
using System.Runtime.CompilerServices;
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

    private int completed = 0;
    private const int COMPLETED_READER = 1, COMPLETED_WRITER = 2;
    private Segment writeTail;

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

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void CheckWritable()
    {
        if (Volatile.Read(ref completed) != 0) SlowCheckWritable();
    }

    private bool WriterCompleted
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => (Volatile.Read(ref completed) & COMPLETED_WRITER) != 0;
    }
    private bool ReaderCompleted
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => (Volatile.Read(ref completed) & COMPLETED_READER) != 0;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void SlowCheckWritable()
    {
        var completed = Volatile.Read(ref this.completed);
        if ((completed & COMPLETED_WRITER) != 0) throw new InvalidOperationException("The writer has already been completed; additional writes are not allowed.");
        if ((completed & COMPLETED_READER) != 0) throw new InvalidOperationException("The reader has already been completed; additional writes are not allowed.");
    }

    private void SetCompleted(int flags)
    {
        int test;
        do
        {
            test = Volatile.Read(ref completed);
        }
        while (Interlocked.CompareExchange(ref completed, test | flags, test) != test);
    }

    public override void Advance(int bytes)
    {
        // no need to check writable; that is checked in GetMemory etc
        writeTail.Commit(bytes);
        // if (bytes != 0) ResetReadProgress(); // defer this to Flush()
    }

    public override void CancelPendingFlush() { }
    public override void Complete(Exception? exception = null)
    {
        SetCompleted(COMPLETED_WRITER);
        ResetReadProgress();
        Flush();
    }

    public virtual void Flush()
    {
        ResetReadProgress();
    }

    [Conditional("DEBUG")]
    private protected void DebugLog(string label, string message)
    {
#if DEBUG
        Console.WriteLine($"[{index} {label}] {message}");
#endif
    }

#if DEBUG
    private static int nextIndex;
    private readonly int index = Interlocked.Increment(ref nextIndex);
#endif

    internal static ArraySegment<byte> GetSegment(ReadOnlyMemory<byte> memory)
    {
        return MemoryMarshal.TryGetArray(memory, out var segment) ? segment : Throw();
        static ArraySegment<byte> Throw() => throw new InvalidOperationException();
    }

    public override ValueTask<FlushResult> FlushAsync(CancellationToken cancellationToken = default)
    {
        Flush();
        return new(new FlushResult(false, ReaderCompleted));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private Segment GetWritableSegment(int sizeHint)
    {
        var tail = writeTail;
        return tail.Available < Math.Min(Math.Max(16, sizeHint), MIN_CHUNK_BYTES) ? AppendNewSegment() : tail;
    }

    private Segment AppendNewSegment()
    {
        CheckWritable();
        lock (WriteSyncLock)
        {
            return writeTail = writeTail.TruncateAndAppend(SEGMENT_BYTES);
        }
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
        => GetWritableSegment(sizeHint).GetWritableMemory();

    public override Span<byte> GetSpan(int sizeHint = 0)
        => GetWritableSegment(sizeHint).GetWritableSpan();
}
