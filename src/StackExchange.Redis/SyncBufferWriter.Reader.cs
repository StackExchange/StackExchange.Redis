using System;
using System.Buffers;
using System.IO.Pipelines;
using System.Threading;
using System.Threading.Tasks;
using Pipelines.Sockets.Unofficial.Arenas;

namespace StackExchange.Redis;

internal sealed partial class SyncBufferWriter : PipeWriter
{
    private Segment readHead;
    private int readOffset;
    private SyncBufferReader? reader;
    private volatile bool readerCompleted;
    private volatile int readerFailedProgressCount;

    public PipeReader Reader => reader ??= new(this);

    private void AdvanceReaderTo(SequencePosition consumed, SequencePosition examined)
        => throw new NotImplementedException();

    private void ResetReadProgress()
    {
        readerFailedProgressCount = 0;
    }

    private void CompleteReader(Exception? exception)
    {
        readerCompleted = true;
        ResetReadProgress();
    }

    public ReadResult Read()
    {
        const int MAX_FAILED_PROGRESS = 1;
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
