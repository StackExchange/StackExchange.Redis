using System;
using System.Buffers;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Pipelines.Sockets.Unofficial;
using RESPite.Buffers;

namespace StackExchange.Redis;

internal partial class PhysicalConnection : IBufferWriter<byte>
{
    private CycleBuffer _writeBuffer = CycleBuffer.Create();

    public IBufferWriter<byte> Output => this;

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1822:Mark members as static", Justification = "DEBUG uses instance data")]
    private async ValueTask<WriteResult> FlushAsync_Awaited(PhysicalConnection connection, Task flush, FlushFlags flags)
    {
        try
        {
            await flush.ForAwait();
            connection._writeStatus = WriteStatus.Flushed;
            connection.UpdateLastWriteTime();
            return WriteResult.Success;
        }
        catch (ConnectionResetException ex) when ((flags & FlushFlags.ThrowOnFailure) == 0)
        {
            connection.RecordConnectionFailed(ConnectionFailureType.SocketClosed, ex);
            return WriteResult.WriteFailure;
        }
    }

    private CancellationTokenSource? _reusableFlushSyncTokenSource;
    [Obsolete("this is an anti-pattern; work to reduce reliance on this is in progress")]
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Style", "IDE0062:Make local function 'static'", Justification = "DEBUG uses instance data")]
    internal WriteResult FlushSync(FlushFlags flags, int millisecondsTimeout)
    {
        var cts = _reusableFlushSyncTokenSource ??= new CancellationTokenSource();
        var flush = FlushAsync(flags, cts.Token);
        if (!flush.IsCompletedSuccessfully)
        {
            // only schedule cancellation if it doesn't complete synchronously; at this point, it is doomed
            _reusableFlushSyncTokenSource = null;
            cts.CancelAfter(TimeSpan.FromMilliseconds(millisecondsTimeout));
            try
            {
                // here lies the evil
                flush.AsTask().Wait();
            }
            catch (AggregateException ex) when (ex.InnerExceptions.Any(e => e is TaskCanceledException))
            {
                ThrowTimeout();
            }
            finally
            {
                cts.Dispose();
            }
        }
        return flush.Result;

        void ThrowTimeout()
        {
            throw new TimeoutException("timeout while synchronously flushing");
        }
    }

    [Flags]
    internal enum FlushFlags
    {
        None = 0,
        ThrowOnFailure = 1 << 0,
        CompletePagesOnly = 1 << 1,
    }

    internal ValueTask<WriteResult> FlushAsync(FlushFlags flags, CancellationToken cancellationToken = default)
    {
        var stream = _ioStream;
        if (stream == null) return new ValueTask<WriteResult>(WriteResult.NoConnectionAvailable);
        try
        {
            _writeStatus = WriteStatus.Flushing;

            while (_writeBuffer.TryGetFirstCommittedMemory((flags & FlushFlags.CompletePagesOnly) == 0 ? 1 : -1, out var memory))
            {
                var write = stream.WriteAsync(memory, cancellationToken);
                if (!write.IsCompletedSuccessfully)
                {
                    return FlushWriteAsync_Awaited(this, stream, write, flags, memory.Length, cancellationToken);
                }

                _writeBuffer.DiscardCommitted(memory.Length);
            }

            if ((flags & FlushFlags.CompletePagesOnly) == 0)
            {
                var flush = stream.FlushAsync(cancellationToken);
                if (!flush.IsCompletedSuccessfully) return FlushAsync_Awaited(this, flush, flags);
            }

            _writeStatus = WriteStatus.Flushed;
            UpdateLastWriteTime();
            return new ValueTask<WriteResult>(WriteResult.Success);
        }
        catch (ConnectionResetException ex) when ((flags & FlushFlags.ThrowOnFailure) == 0)
        {
            RecordConnectionFailed(ConnectionFailureType.SocketClosed, ex);
            return new ValueTask<WriteResult>(WriteResult.WriteFailure);
        }

        static async ValueTask<WriteResult> FlushWriteAsync_Awaited(PhysicalConnection connection, Stream stream, ValueTask pending, FlushFlags flags, int bytes, CancellationToken cancellationToken)
        {
            try
            {
                while (true)
                {
                    await pending.ConfigureAwait(false);
                    connection._writeBuffer.DiscardCommitted(bytes);

                    if (!connection._writeBuffer.TryGetFirstCommittedMemory((flags & FlushFlags.CompletePagesOnly) == 0 ? 1 : -1, out var memory))
                    {
                        break;
                    }
                    bytes = memory.Length;
                    pending = stream.WriteAsync(memory, cancellationToken);
                }

                if ((flags & FlushFlags.CompletePagesOnly) == 0)
                {
                    await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
                }

                connection._writeStatus = WriteStatus.Flushed;
                connection.UpdateLastWriteTime();
                return WriteResult.Success;
            }
            catch (ConnectionResetException ex) when ((flags & FlushFlags.ThrowOnFailure) == 0)
            {
                connection.RecordConnectionFailed(ConnectionFailureType.SocketClosed, ex);
                return WriteResult.WriteFailure;
            }
        }
    }

    void IBufferWriter<byte>.Advance(int count) => _writeBuffer.Commit(count);

    Memory<byte> IBufferWriter<byte>.GetMemory(int sizeHint) => _writeBuffer.GetUncommittedMemory(sizeHint);

    Span<byte> IBufferWriter<byte>.GetSpan(int sizeHint) => _writeBuffer.GetUncommittedSpan(sizeHint);

    public void Release() => _writeBuffer.Release();
}
