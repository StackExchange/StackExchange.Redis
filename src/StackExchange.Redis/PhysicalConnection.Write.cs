using System;
using System.Buffers;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using RESPite.Streams;

namespace StackExchange.Redis;

internal partial class PhysicalConnection
{
    private BufferedStreamWriter? _output;

    /// <summary>
    /// Set by <see cref="Shutdown"/> before it discards <see cref="_output"/>, so a writer that finds the
    /// output gone can tell the two cases apart; see <see cref="ThrowOutputUnavailable"/>.
    /// </summary>
    private volatile bool _isShutdown;

    private long TotalBytesSent => _output?.TotalBytesWritten ?? 0;
    public IBufferWriter<byte> Output => _output ?? ThrowOutputUnavailable();

    /// <summary>
    /// <see cref="Shutdown"/> discards the output writer, and it can run concurrently with a writer that is
    /// already inside the write lock - teardown is usually detected on the read loop, which must not block on
    /// that lock. A write that loses the pipe that way is an ordinary closure, not a bug, so report it as one:
    /// <see cref="ObjectDisposedException"/> is what <see cref="IdentifyFailureType"/> maps to
    /// <see cref="ConnectionFailureType.SocketClosed"/>. A missing output when we were *not* shut down really
    /// is a bug, and stays an <see cref="InvalidOperationException"/>. See #3167.
    /// </summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    [DoesNotReturn]
    private BufferedStreamWriter ThrowOutputUnavailable() => throw (_isShutdown
        ? new ObjectDisposedException(nameof(PhysicalConnection), "The connection was closed while writing")
        : (Exception)new InvalidOperationException("Output pipe not initialized"));

    private void InitOutput(Stream? stream)
    {
        if (stream is null) return;
        _ioStream = stream;
        var config = BridgeCouldBeNull?.Multiplexer?.RawConfig;

        // Redis policy over the generic writer: sync-mode targets latency, which pub/sub never needs.
        var mode = connectionType is ConnectionType.Subscription ? BufferedStreamWriter.WriteMode.Async : WriteMode;
        _output = BufferedStreamWriter.Create(mode, stream, config?.RequestBufferPool, OutputCancel);

        // Nothing awaits WriteComplete in production (it is mostly a test affordance); observe it so a
        // teardown-time (or any other) write fault never becomes an UnobservedTaskException. Applies to
        // every BufferedStreamWriter implementation.
        _output.WriteComplete.RedisFireAndForget();
#if DEBUG
        if (config?.OutputLog is { } log)
        {
            _output.DebugSetLog(log);
        }
#endif
    }

    internal bool HasOutputPipe => _output is not null;

    internal Task CompleteOutputAsync(Exception? exception = null)
    {
        if (_output is not { } output) return Task.CompletedTask;
        output.Complete(exception);
        return output.WriteComplete;
    }
}
