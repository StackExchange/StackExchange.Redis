using System;
using System.Buffers;
using System.IO;
using System.Threading.Tasks;
using RESPite.Streams;

namespace StackExchange.Redis;

internal partial class PhysicalConnection
{
    private BufferedStreamWriter? _output;
    private long TotalBytesSent => _output?.TotalBytesWritten ?? 0;
    public IBufferWriter<byte> Output
    {
        get
        {
            return _output ?? Throw();
            static IBufferWriter<byte> Throw() => throw new InvalidOperationException("Output pipe not initialized");
        }
    }

    /// <summary>
    /// Which writer this connection should use, given how it is configured and what it is for.
    /// </summary>
    /// <remarks>
    /// Policy rather than plumbing, so it is a function that can be tested rather than a condition buried in
    /// <see cref="InitOutput"/>: three rules interact here, and getting the precedence subtly wrong would show
    /// up only as a performance characteristic, which is the kind of bug nobody notices for a year.
    /// </remarks>
    internal static BufferedStreamWriter.WriteMode ResolveWriteMode(
        ConnectionType connectionType,
        BufferedStreamWriter.WriteMode configured,
        bool dedicatedThreads)
    {
        // Redis policy over the generic writer: sync-mode targets latency, which pub/sub never needs.
        if (connectionType is ConnectionType.Subscription) return BufferedStreamWriter.WriteMode.Async;

        // Sync-mode also owns its reader and writer threads rather than borrowing the thread-pool, which is
        // what the DedicatedThreads flag is asking for: on a saturated pool the reply cannot be processed,
        // because processing it needs a thread and every thread is waiting on one. Note this promotes an
        // *unstated* preference only - anything explicitly configured still wins.
        if (configured == BufferedStreamWriter.WriteMode.Default && dedicatedThreads)
        {
            return BufferedStreamWriter.WriteMode.Sync;
        }

        return configured;
    }

    private void InitOutput(Stream? stream)
    {
        if (stream is null) return;
        _ioStream = stream;
        var config = BridgeCouldBeNull?.Multiplexer?.RawConfig;

        var mode = ResolveWriteMode(connectionType, WriteMode, ConnectionMultiplexer.DedicatedThreads);
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
