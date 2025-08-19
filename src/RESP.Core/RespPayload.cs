using System;
using System.Buffers;
using System.Threading;
using System.Threading.Tasks;
using Resp.RedisCommands;

namespace Resp;

public abstract class RespPayload : IDisposable
{
    public virtual Task WaitAsync() => Task.CompletedTask;
    public virtual void Wait(TimeSpan timeout) { }

    private bool _isDisposed;

    public void Dispose()
    {
        if (!_isDisposed)
        {
            _isDisposed = true;
            Dispose(true);
        }
    }

    protected abstract void Dispose(bool disposing);
    protected abstract ReadOnlySequence<byte> GetPayload();

    /// <inheritdoc/>
    public override string ToString() => _isDisposed ? "(disposed)" : $"{GetPayload().Length} bytes";

    public ReadOnlySequence<byte> Payload
    {
        get
        {
            if (_isDisposed) ThrowDisposed(this);
            return GetPayload();

            static void ThrowDisposed(RespPayload obj) => throw new ObjectDisposedException(obj.GetType().Name);
        }
    }

    /// <summary>
    /// Ensure that this is a valid RESP payload and contains the expected number of top-level elements.
    /// </summary>
    /// <param name="checkError">Whether to check for error replies.</param>
    public void Validate(bool checkError = true)
    {
        RespReader reader = new(Payload);
        int count = 0;
        while (reader.TryMoveNext(checkError))
        {
            reader.SkipChildren();
            count++;
        }

        if (count != 1)
        {
            throw new InvalidOperationException($"Expected single message, found {count}");
        }
    }

    internal static RespPayload Create<TRequest>(
        scoped ReadOnlySpan<byte> command,
        in TRequest request,
        IRespFormatter<TRequest> formatter,
        bool disposeOnWrite = false)
    {
        int size = 0;
        if (formatter is IRespSizeEstimator<TRequest> estimator)
        {
            size = estimator.EstimateSize(command, request);
        }
        var buffer = AmbientBufferWriter.Get(size);
        try
        {
            var writer = new RespWriter(buffer);
            formatter.Format(command, ref writer, request);
            writer.Flush();
            var payload = buffer.Detach(out int length);
            return disposeOnWrite
                ? new DisposeOnWriteRespPayload(payload, length)
                : new ArrayPoolRespPayload(payload, length);
        }
        catch
        {
            buffer.Reset();
            throw;
        }
    }

    internal TResponse ParseAndDispose<TResponse>(IRespParser<TResponse>? parser = null, TimeSpan timeout = default)
    {
        try
        {
            Wait(timeout);
            parser ??= DefaultParsers.Get<TResponse>();
            var reader = new RespReader(Payload);
            // ReSharper disable once SuspiciousTypeConversion.Global
            if (parser is not IRespMetadataParser)
            {
                reader.MoveNext(); // move to content by default
            }
            return parser.Parse(ref reader);
        }
        finally
        {
            Dispose();
        }
    }

    internal TResponse ParseAndDispose<TRequest, TResponse>(in TRequest request, IRespParser<TRequest, TResponse> parser)
    {
        try
        {
            var reader = new RespReader(Payload);
            // ReSharper disable once SuspiciousTypeConversion.Global
            if (parser is not IRespMetadataParser)
            {
                reader.MoveNext(); // move to content by default
            }
            return parser.Parse(in request, ref reader);
        }
        finally
        {
            Dispose();
        }
    }

    public static RespPayload Create(ReadOnlyMemory<byte> payload) => new ReadOnlyMemoryRespPayload(payload);
    public static RespPayload Create(ReadOnlySequence<byte> payload) =>
        payload.IsSingleSegment ? new ReadOnlyMemoryRespPayload(payload.First) : new ReadOnlySequenceRespPayload(payload);
}

internal sealed class ReadOnlyMemoryRespPayload(ReadOnlyMemory<byte> payload) : RespPayload
{
    protected override ReadOnlySequence<byte> GetPayload() => new(payload);
    protected override void Dispose(bool disposing) { }
}

internal sealed class ReadOnlySequenceRespPayload(ReadOnlySequence<byte> payload) : RespPayload
{
    protected override ReadOnlySequence<byte> GetPayload() => payload;
    protected override void Dispose(bool disposing) { }
}

internal class DisposeOnWriteRespPayload(byte[] payload, int length) : ArrayPoolRespPayload(payload, length)
{
}

internal class ArrayPoolRespPayload : RespPayload
{
    private byte[] _payload;
    private int _length;

    /// <summary>
    /// Create a new instance using the supplied payload.
    /// </summary>
    internal ArrayPoolRespPayload(byte[] payload, int length)
    {
        _payload = payload;
        _length = length;
    }

    protected override ReadOnlySequence<byte> GetPayload() => new(_payload, 0, _length);

    protected override void Dispose(bool disposing)
    {
        var payload = _payload;
        _length = 0;
        _payload = [];
        if (disposing)
        {
            ArrayPool<byte>.Shared.Return(payload);
        }
    }

    private protected void SetPayload(byte[] payload, int length)
    {
        _payload = payload;
        _length = length;
    }
}

internal interface IPendingRespPayload
{
    bool TryFail();
    bool TryComplete(byte[] payload, int length);
}

internal sealed class SyncArrayPoolRespPayload() : ArrayPoolRespPayload([], 0), IPendingRespPayload
{
    private const int STATUS_PENDING = 0, STATUS_COMPLETED = 1, STATUS_FAILED = 2, STATUS_DISPOSED = 3;
    private int _status;
    public override Task WaitAsync() => throw new NotSupportedException("This payload must be awaited asynchronously");

    public bool TryComplete(byte[] payload, int length)
    {
        if (Volatile.Read(ref _status) == STATUS_PENDING)
        {
            lock (this)
            {
                if (_status == STATUS_PENDING)
                {
                    _status = STATUS_COMPLETED;
                    SetPayload(payload, length);
                    Monitor.PulseAll(this);
                    return true;
                }
            }
        }

        // we couldn't take ownship; recycle
        return false;
    }

    protected override void Dispose(bool disposing)
    {
        Volatile.Write(ref _status, STATUS_DISPOSED);
        if (Monitor.TryEnter(this, 0))
        {
            Monitor.PulseAll(this);
            Monitor.Exit(this);
        }
        base.Dispose(disposing);
    }

    public bool TryFail()
    {
        if (Volatile.Read(ref _status) == STATUS_PENDING)
        {
            lock (this)
            {
                if (_status == STATUS_PENDING)
                {
                    _status = STATUS_FAILED;
                    Monitor.PulseAll(this);
                    return true;
                }
            }
        }

        return false;
    }

    public override void Wait(TimeSpan timeout)
    {
        int status = Volatile.Read(ref _status);
        if (status == STATUS_PENDING)
        {
            lock (this)
            {
                status = _status;
                if (status == STATUS_PENDING)
                {
                    if (timeout == TimeSpan.Zero)
                    {
                        Monitor.Wait(this);
                    }
                    else if (!Monitor.Wait(this, timeout))
                    {
                        ThrowTimeout();
                    }
                    status = _status;
                }
            }
        }

        if (status != STATUS_COMPLETED) ThrowStatus(_status);
        static void ThrowTimeout() => throw new TimeoutException();
    }

    protected override ReadOnlySequence<byte> GetPayload()
    {
        var status = Volatile.Read(ref _status);
        if (status != STATUS_COMPLETED) ThrowStatus(status);
        return base.GetPayload();
    }

    private static void ThrowStatus(int status) => throw new InvalidOperationException(status switch
    {
        STATUS_PENDING => "Operation is still pending",
        STATUS_FAILED => "Operation failed",
        STATUS_DISPOSED => "Operation was disposed",
        _ => $"Unexpected status: {status}",
    });
}
