using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace StackExchange.Redis.Tests;

public class BufferedStreamWriterTests
{
    private const int PageSize = 8 * 1024;

    [Theory]
    [InlineData(WriteMode.Sync)]
    [InlineData(WriteMode.Async)]
    public async Task FlushStateDoesNotLeakIntoNextPageActivation(WriteMode mode)
    {
        var stream = new ObservedStream();
        var writer = BufferedStreamWriter.Create((BufferedStreamWriter.WriteMode)mode, ConnectionType.Interactive, stream, CancellationToken.None);
        try
        {
            Write(writer, 1, 1);
            writer.Flush();
            await stream.WaitForFlushCountAsync(1).ForAwait();
            await WaitForInactiveAsync(writer).ForAwait();

            stream.BlockNextWrite();
            Write(writer, PageSize, 2);

            var partial = writer.GetMemory(1);
            await stream.WaitForBlockedWriteAsync().ForAwait();
            partial.Span[0] = 3;
            writer.Advance(1);

            stream.ReleaseBlockedWrite();
            await stream.WaitForFlushCountAsync(2).ForAwait();

            Assert.Equal(1 + PageSize, stream.BytesWritten);
        }
        finally
        {
            await CompleteAsync(writer, stream).ForAwait();
        }
    }

    [Theory]
    [InlineData(WriteMode.Sync)]
    [InlineData(WriteMode.Async)]
    public async Task WriterDoesNotLoseFlushRequestedDuringDrainFlush(WriteMode mode)
    {
        var stream = new ObservedStream();
        var writer = BufferedStreamWriter.Create((BufferedStreamWriter.WriteMode)mode, ConnectionType.Interactive, stream, CancellationToken.None);
        try
        {
            stream.BlockNextFlush();
            Write(writer, 1, 1);
            writer.Flush();

            await stream.WaitForBlockedFlushAsync().ForAwait();

            Write(writer, 1, 2);
            writer.Flush();

            stream.ReleaseBlockedFlush();
            await stream.WaitForFlushCountAsync(2).ForAwait();

            Assert.Equal(2, stream.BytesWritten);
        }
        finally
        {
            await CompleteAsync(writer, stream).ForAwait();
        }
    }

    [Theory]
    [InlineData(WriteMode.Sync)]
    [InlineData(WriteMode.Async)]
    [InlineData(WriteMode.Pipe)]
    public async Task WriterFaultsWriteCompleteAfterTargetWriteFailure(WriteMode mode)
    {
        var failure = new IOException("simulated target write failure");
        var stream = new ObservedStream { WriteException = failure };
        var writer = BufferedStreamWriter.Create((BufferedStreamWriter.WriteMode)mode, ConnectionType.Interactive, stream, CancellationToken.None);

        Write(writer, 1, 1);
        writer.Flush();

        var ex = await Assert.ThrowsAsync<IOException>(
            async () => await WaitWithTimeoutAsync(writer.WriteComplete, TimeSpan.FromSeconds(5)).ForAwait()).ForAwait();
        Assert.Same(failure, ex);

        var closed = Assert.Throws<InvalidOperationException>(() => writer.GetSpan(1));
        if (mode is not WriteMode.Pipe)
        {
            Assert.Same(failure, closed.InnerException);
        }
    }

    private static void Write(BufferedStreamWriter writer, int count, byte value)
    {
        var span = writer.GetSpan(count);
        span.Slice(0, count).Fill(value);
        writer.Advance(count);
    }

    private static bool IsActive(BufferedStreamWriter writer)
        => writer is CycleBufferStreamWriter cycle
           && (cycle.State & CycleBufferStreamWriter.StateFlags.ActiveWriter) != 0;

    private static Task WaitForInactiveAsync(BufferedStreamWriter writer)
        => WaitUntilAsync(() => !IsActive(writer), "writer did not become inactive");

    private static async Task CompleteAsync(BufferedStreamWriter writer, ObservedStream stream)
    {
        stream.ReleaseBlockedWrite();
        stream.ReleaseBlockedFlush();
        writer.Complete();
        await WaitWithTimeoutAsync(writer.WriteComplete, TimeSpan.FromSeconds(5)).ForAwait();
    }

    private static async Task WaitUntilAsync(Func<bool> condition, string message, TimeSpan? timeout = null)
    {
        var watch = Stopwatch.StartNew();
        var limit = timeout ?? TimeSpan.FromSeconds(5);
        while (!condition())
        {
            if (watch.Elapsed >= limit) Assert.Fail(message);
            await Task.Delay(10).ForAwait();
        }
    }

    private static async Task WaitWithTimeoutAsync(Task task, TimeSpan timeout)
    {
        var delay = Task.Delay(timeout);
        var first = await Task.WhenAny(task, delay).ForAwait();
        Assert.Same(task, first);
        await task.ForAwait();
    }

    private sealed class ObservedStream : Stream
    {
        private readonly object _lock = new();
        private long _bytesWritten;
        private int _flushCount;
        private bool _blockNextWrite, _blockNextFlush;
        private TaskCompletionSource<bool>? _blockedWrite, _allowWrite;
        private TaskCompletionSource<bool>? _blockedFlush, _allowFlush;

        public Exception? WriteException { get; set; }
        public long BytesWritten => Interlocked.Read(ref _bytesWritten);

        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => throw new NotSupportedException();
        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public void BlockNextWrite()
        {
            lock (_lock)
            {
                _blockNextWrite = true;
                _blockedWrite = NewCompletionSource();
                _allowWrite = NewCompletionSource();
            }
        }

        public Task WaitForBlockedWriteAsync()
        {
            lock (_lock)
            {
                return _blockedWrite?.Task ?? Task.CompletedTask;
            }
        }

        public void ReleaseBlockedWrite()
        {
            TaskCompletionSource<bool>? allow;
            lock (_lock)
            {
                _blockNextWrite = false;
                allow = _allowWrite;
                _allowWrite = null;
            }
            allow?.TrySetResult(true);
        }

        public void BlockNextFlush()
        {
            lock (_lock)
            {
                _blockNextFlush = true;
                _blockedFlush = NewCompletionSource();
                _allowFlush = NewCompletionSource();
            }
        }

        public Task WaitForBlockedFlushAsync()
        {
            lock (_lock)
            {
                return _blockedFlush?.Task ?? Task.CompletedTask;
            }
        }

        public void ReleaseBlockedFlush()
        {
            TaskCompletionSource<bool>? allow;
            lock (_lock)
            {
                _blockNextFlush = false;
                allow = _allowFlush;
                _allowFlush = null;
            }
            allow?.TrySetResult(true);
        }

        public Task WaitForFlushCountAsync(int count)
            => WaitUntilAsync(() => Volatile.Read(ref _flushCount) >= count, $"flush count did not reach {count}");

        public override void Flush()
            => BeforeFlush();

        public override Task FlushAsync(CancellationToken cancellationToken)
            => BeforeFlushAsync().AsTask();

        public override void Write(byte[] buffer, int offset, int count)
        {
            BeforeWrite();
            AfterWrite(count);
        }

        public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
            => WriteAsync(count).AsTask();

#if NET
        public override void Write(ReadOnlySpan<byte> buffer)
        {
            BeforeWrite();
            AfterWrite(buffer.Length);
        }

        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
            => WriteAsync(buffer.Length);
#endif

        private void BeforeWrite()
            => GetBlockedOperation(ref _blockNextWrite, ref _blockedWrite, ref _allowWrite)?.GetAwaiter().GetResult();

        private async ValueTask WriteAsync(int count)
        {
            var blocked = GetBlockedOperation(ref _blockNextWrite, ref _blockedWrite, ref _allowWrite);
            if (blocked is not null) await blocked.ForAwait();
            AfterWrite(count);
        }

        private void AfterWrite(int count)
        {
            if (WriteException is not null) throw WriteException;
            Interlocked.Add(ref _bytesWritten, count);
        }

        private void BeforeFlush()
            => BeforeFlushAsync().AsTask().GetAwaiter().GetResult();

        private async ValueTask BeforeFlushAsync()
        {
            Interlocked.Increment(ref _flushCount);
            var blocked = GetBlockedOperation(ref _blockNextFlush, ref _blockedFlush, ref _allowFlush);
            if (blocked is not null) await blocked.ForAwait();
        }

        private Task? GetBlockedOperation(
            ref bool blockNext,
            ref TaskCompletionSource<bool>? blocked,
            ref TaskCompletionSource<bool>? allow)
        {
            lock (_lock)
            {
                if (!blockNext) return null;
                blockNext = false;
                blocked?.TrySetResult(true);
                return allow?.Task;
            }
        }

        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();

        private static TaskCompletionSource<bool> NewCompletionSource()
            => new(TaskCreationOptions.RunContinuationsAsynchronously);
    }
}
