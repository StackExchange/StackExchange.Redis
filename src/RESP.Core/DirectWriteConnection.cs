using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Resp;

internal sealed class DirectWriteConnection : IRespConnection
{
    private bool _isDoomed;
    private ReadBuffer _readBuffer;

    public bool CanWrite => Volatile.Read(ref _readStatus) == WRITER_AVAILABLE;

    public int Outstanding => _outstanding.Count;

    public Task Reader { get; private set; } = Task.CompletedTask;

    private readonly Stream tail;
    private ConcurrentQueue<IPendingRespPayload> _outstanding = new();

    public DirectWriteConnection(Stream tail, bool asyncRead = true)
    {
        if (!(tail.CanRead && tail.CanWrite)) Throw();
        this.tail = tail;
        if (asyncRead)
        {
            Reader = Task.Run(ReadAllAsync);
        }
        else
        {
            new Thread(ReadAll).Start();
        }
        static void Throw() => throw new ArgumentException("Stream must be readable and writable", nameof(tail));
    }

    private void ReadAll()
    {
        var tcs = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
        Reader = tcs.Task;
        try
        {
            while (true)
            {
                var buffer = _readBuffer.GetWriteBuffer();
                var read = tail.Read(buffer.Array!, buffer.Offset, buffer.Count);
                if (!_readBuffer.OnRead(read)) break;
            }

            Volatile.Write(ref _readStatus, READER_COMPLETED);
            _readBuffer.Release(); // clean exit, we can recycle
            tcs.SetResult(null);
        }
        catch (Exception ex)
        {
            Volatile.Write(ref _readStatus, READER_FAILED);
            Debug.WriteLine($"Reader failed: {ex.Message}");
            tcs.SetResult(ex);
        }
        finally
        {
            Doom();
            _readBuffer = default; // for GC purposes

            // abandon anything in the queue
            while (_outstanding.TryDequeue(out var pending))
            {
                pending.TryFail();
            }
        }
    }

    private async Task ReadAllAsync()
    {
        try
        {
            CancellationToken cancellationToken = CancellationToken.None;
            while (true)
            {
                var buffer = _readBuffer.GetWriteBuffer();
                var read = await tail.ReadAsync(buffer.Array!, buffer.Offset, buffer.Count, cancellationToken)
                    .ConfigureAwait(false);
                if (!_readBuffer.OnRead(read)) break;
            }

            Volatile.Write(ref _readStatus, READER_COMPLETED);
            _readBuffer.Release(); // clean exit, we can recycle
        }
        catch (Exception ex)
        {
            Volatile.Write(ref _readStatus, READER_FAILED);
            Debug.WriteLine($"Reader failed: {ex.Message}");
            throw;
        }
        finally
        {
            Doom();
            _readBuffer = default; // for GC purposes

            // abandon anything in the queue
            while (_outstanding.TryDequeue(out var pending))
            {
                pending.TryFail();
            }
        }
    }

    private int _writeStatus, _readStatus;
    private const int WRITER_AVAILABLE = 0, WRITER_TAKEN = 1, WRITER_DOOMED = 2;
    private const int READER_ACTIVE = 0, READER_FAILED = 1, READER_COMPLETED = 2;

    private void TakeWriter()
    {
        var status = Interlocked.CompareExchange(ref _writeStatus, WRITER_TAKEN, WRITER_AVAILABLE);
        if (status != WRITER_AVAILABLE) Throw(status);

        static void Throw(int status) => throw new InvalidOperationException(status switch
        {
            WRITER_TAKEN => "A write operation is already in progress; concurrent writes are not supported.",
            WRITER_DOOMED => "This connection is terminated; no further writes are possible.",
            _ => $"Unknown writer status: {status}",
        });
    }
    private void ReleaseWriter(int status = WRITER_AVAILABLE)
    {
        if (status == WRITER_AVAILABLE && _isDoomed)
        {
            status = WRITER_DOOMED;
        }
        Interlocked.CompareExchange(ref _writeStatus, status, WRITER_TAKEN);
    }

    public RespPayload Send(RespPayload payload)
    {
        TakeWriter();
        try
        {
            var bytes = payload.Payload;
            if (bytes.IsSingleSegment)
            {
#if NETCOREAPP || NETSTANDARD2_1_OR_GREATER
                tail.Write(bytes.FirstSpan);
#else
                tail.Write(bytes.First);
#endif
            }
            else
            {
                foreach (var segment in bytes)
                {
#if NETCOREAPP || NETSTANDARD2_1_OR_GREATER
                tail.Write(segment.Span);
#else
                    tail.Write(segment);
#endif
                }
            }

            if (payload is DisposeOnWriteRespPayload)
            {
                payload.Dispose();
            }

            var pending = new SyncArrayPoolRespPayload();
            _outstanding.Enqueue(pending);
            ReleaseWriter();
            return pending;
        }
        catch
        {
            ReleaseWriter(WRITER_DOOMED);
            throw;
        }
    }

    public ValueTask<RespPayload> SendAsync(RespPayload payload, CancellationToken cancellationToken = default)
    {
        _ = tail;
        throw new NotImplementedException();
    }

    private void Doom()
    {
        _isDoomed = true; // without a reader, there's no point writing
        Interlocked.CompareExchange(ref _writeStatus, WRITER_DOOMED, WRITER_AVAILABLE);
    }

    public void Dispose()
    {
        Doom();
        tail.Dispose();
    }

    public ValueTask DisposeAsync()
    {
#if COREAPP3_0_OR_GREATER || NETSTANDARD2_1_OR_GREATER
        return tail.DisposeAsync().AsTask();
#else
        Dispose();
        return default;
#endif
    }
}
