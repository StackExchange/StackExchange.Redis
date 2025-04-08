using System;
using System.Buffers;
using System.IO.Pipelines;
using System.Threading;
using System.Threading.Tasks;

namespace StackExchange.Redis;

internal partial class SyncBufferWriter : PipeWriter
{
    private Segment readHead;
    private int readOffset;
    private SyncBufferReader? reader;
    private volatile bool readerCompleted;
    private volatile int readerFailedProgressCount;

    public PipeReader Reader => reader ??= new(this);

    private void CheckReadable()
    {
        if (readerCompleted) ThrowReaderCompleted();
        static void ThrowReaderCompleted() => throw new InvalidOperationException("The reader has already been completed; additional reads are not allowed.");
    }

    internal void AdvanceReaderTo(in SequencePosition consumed, in SequencePosition examined)
    {
        CheckReadable();
        long readIndex = readHead.RunningIndex + readOffset, consumedIndex = GetIndex(in consumed), examinedIndex = GetIndex(in examined);
        if (consumedIndex < readIndex) Throw(nameof(consumed));
        if (consumedIndex > examinedIndex) Throw(nameof(examined));

        if (consumedIndex == readIndex)
        {
            readerFailedProgressCount++;
        }
        else
        {
            readerFailedProgressCount = 0;
            Segment node = readHead, end = readHead = (Segment)consumed.GetObject()!;
            readOffset = consumed.GetInteger();

            while (!ReferenceEquals(node, end))
            {
                node.Release();
                node = (Segment)node.Next!;
            }
        }

        // normally we retain the final segment; however, we can release it if
        // the writer is completed and we've read everything
        if (writerCompleted && ReleaseIfWriteEnd(consumed))
        {
            readOffset = 0;
        }

        static void Throw(string paramName) => throw new ArgumentOutOfRangeException(paramName);
    }

    private static long GetIndex(in SequencePosition position) => ((Segment)position.GetObject()!).RunningIndex + position.GetInteger();

    private void ResetReadProgress()
    {
        readerFailedProgressCount = 0;
    }

    internal void CompleteReader(Exception? exception)
    {
        readerCompleted = true;
    }

    public virtual ReadResult Read()
    {
        const int MAX_FAILED_PROGRESS = 1;
        CheckReadable();
        if (++readerFailedProgressCount > MAX_FAILED_PROGRESS) ThrowNotMakingProgress();

        Segment endSegment;
        int endIndex;
        lock (WriteSyncLock)
        {
            endSegment = writeTail;
            endIndex = endSegment.Committed;
        }

        var payload = new ReadOnlySequence<byte>(readHead, readOffset, endSegment, endIndex);
        return new ReadResult(payload, isCanceled: false, isCompleted: writerCompleted);

        static void ThrowNotMakingProgress()
            => throw new InvalidOperationException("The reader has failed to make progress; a tight read-loop is not permitted.");
    }

    private sealed class SyncBufferReader(SyncBufferWriter writer) : PipeReader
    {
        public override void AdvanceTo(SequencePosition consumed) => writer.AdvanceReaderTo(consumed, consumed);
        public override void AdvanceTo(SequencePosition consumed, SequencePosition examined) => writer.AdvanceReaderTo(consumed, examined);
        public override void CancelPendingRead() { }
        public override void Complete(Exception? exception = null) => writer.CompleteReader(exception);
        public override ValueTask<ReadResult> ReadAsync(CancellationToken cancellationToken = default)
            => new(writer.Read());

        public override bool TryRead(out ReadResult result)
        {
            result = writer.Read();
            return true;
        }
    }
}
