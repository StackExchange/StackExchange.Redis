using System;
using System.Buffers;
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

    public RespMode Mode { get; set; } = RespMode.Resp2;

    public enum RespMode
    {
        Resp2,
        Resp2PubSub,
        Resp3,
    }

    private async Task ReadAllAsync()
    {
        try
        {
            CancellationToken cancellationToken = CancellationToken.None;
            var scanner = RespFrameScanner.Default;

            RespScanState state = default;
            OperationStatus status = OperationStatus.NeedMoreData;

            while (true) // main IO loop
            {
                var buffer = _readBuffer.GetWriteBuffer();
                var read = await tail.ReadAsync(buffer.Array!, buffer.Offset, buffer.Count, cancellationToken)
                    .ConfigureAwait(false);
                if (!_readBuffer.OnRead(read)) break;

                var fullBuffer = _readBuffer.GetSpan();
                var toParse = fullBuffer.Slice((int)state.TotalBytes); // skip what we've already parsed

                int fullyConsumed = 0;
                while (toParse.Length >= RespScanState.MinBytes &&
                       (status = scanner.TryRead(ref state, toParse)) == OperationStatus.Done)
                {
                    Debug.Assert(state.IsComplete && state.TotalBytes >= RespScanState.MinBytes &&
                                 state.Prefix is not RespPrefix.None);

                    // extract the frame
                    var bytes = (int)state.TotalBytes;

                    // send the frame somewhere
                    OnResponseFrame(state.Prefix, fullBuffer.Slice(fullyConsumed, bytes));

                    // update our buffers to the unread potions and reset for a new RESP frame
                    fullyConsumed += bytes;
                    toParse = fullBuffer.Slice(fullyConsumed);
                    state = default;
                    status = OperationStatus.NeedMoreData;
                }

                _readBuffer.Consume(fullyConsumed);

                if (status != OperationStatus.NeedMoreData)
                {
                    ThrowStatus(status);

                    static void ThrowStatus(OperationStatus status) =>
                        throw new InvalidOperationException($"Unexpected operation status: {status}");
                }
            } // main IO loop - read the next chunk

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

    private static readonly ulong
        ArrayPong_LC_Bulk = RespConstants.UnsafeCpuUInt64("*2\r\n$4\r\npong\r\n$"u8),
        ArrayPong_UC_Bulk = RespConstants.UnsafeCpuUInt64("*2\r\n$4\r\nPONG\r\n$"u8),
        ArrayPong_LC_Simple = RespConstants.UnsafeCpuUInt64("*2\r\n+pong\r\n$"u8),
        ArrayPong_UC_Simple = RespConstants.UnsafeCpuUInt64("*2\r\n+PONG\r\n$"u8);

    private static readonly uint
        pong = RespConstants.UnsafeCpuUInt32("pong"u8),
        PONG = RespConstants.UnsafeCpuUInt32("PONG"u8);

    private void OnOutOfBand(ReadOnlySpan<byte> payload)
    {
        throw new NotImplementedException(nameof(OnOutOfBand));
    }

    private void OnResponseFrame(RespPrefix prefix, ReadOnlySpan<byte> payload)
    {
        if (prefix == RespPrefix.Push ||
            (prefix == RespPrefix.Array && Mode is RespMode.Resp2PubSub && !IsArrayPong(payload)))
        {
            // out-of-band; pub/sub etc
            OnOutOfBand(payload);
            return;
        }

        // request/response; match to inbound
        if (!_outstanding.TryDequeue(out var pending))
        {
            Debug.Fail("Unexpected response!");
            return;
        }

        // create isolated copy of the payload
        var oversized = ArrayPool<byte>.Shared.Rent(payload.Length);
        payload.CopyTo(oversized);
        pending.TryComplete(oversized, payload.Length);

        static bool IsArrayPong(ReadOnlySpan<byte> payload)
        {
            if (payload.Length >= sizeof(ulong))
            {
                var raw = RespConstants.UnsafeCpuUInt64(payload);
                if (raw == ArrayPong_LC_Bulk
                    || raw == ArrayPong_UC_Bulk
                    || raw == ArrayPong_LC_Simple
                    || raw == ArrayPong_UC_Simple)
                {
                    var reader = new RespReader(payload);
                    return reader.TryMoveNext() // have root
                           && reader.Prefix == RespPrefix.Array // root is array
                           && reader.TryMoveNext() // have first child
                           && (reader.IsInlneCpuUInt32(pong) || reader.IsInlneCpuUInt32(PONG)); // pong
                }
            }

            return false;
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

    private void ReleaseWriter(int status = WRITER_AVAILABLE, RespPayload? payload = null)
    {
        if (payload is DisposeOnWriteRespPayload)
        {
            payload.Dispose();
        }

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
            var placeholder = new SyncArrayPoolRespPayload();
            _outstanding.Enqueue(placeholder);
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

            ReleaseWriter(payload: payload);
            return placeholder;
        }
        catch
        {
            ReleaseWriter(WRITER_DOOMED);
            throw;
        }
    }

    public ValueTask<RespPayload> SendAsync(RespPayload payload, CancellationToken cancellationToken = default)
    {
        TakeWriter();
        try
        {
            var bytes = payload.Payload;
            var placeholder = new AsyncArrayPoolRespPayload();
            _outstanding.Enqueue(placeholder);
            if (!bytes.IsSingleSegment)
            {
                return WriteMultiWithToken(this, payload, placeholder, cancellationToken);
            }

            var pendingWrite = tail.WriteAsync(bytes.First, cancellationToken);
            if (!pendingWrite.IsCompleted)
            {
                return AwaitedSingleWithToken(this, pendingWrite, payload, placeholder);
            }

            pendingWrite.GetAwaiter().GetResult();
            ReleaseWriter(payload: payload);
            return new(placeholder);
        }
        catch
        {
            ReleaseWriter(WRITER_DOOMED);
            throw;
        }

        static async ValueTask<RespPayload> AwaitedSingleWithToken(
            DirectWriteConnection @this,
            ValueTask pendingWrite,
            RespPayload payload,
            RespPayload placeholder)
        {
            Debug.Fail("Async write could be a problem re overlapping IO and not knowing when we can write again");
            try
            {
                await pendingWrite.ConfigureAwait(false);

                @this.ReleaseWriter(payload: payload);
                return placeholder;
            }
            catch
            {
                @this.ReleaseWriter(WRITER_DOOMED);
                throw;
            }
        }

        static async ValueTask<RespPayload> WriteMultiWithToken(
            DirectWriteConnection @this,
            RespPayload payload,
            RespPayload placeholder,
            CancellationToken cancellationToken)
        {
            try
            {
                foreach (var segment in payload.Payload)
                {
                    await @this.tail.WriteAsync(segment, cancellationToken).ConfigureAwait(false);
                }

                if (payload is DisposeOnWriteRespPayload)
                {
                    payload.Dispose();
                }

                @this.ReleaseWriter();
                return placeholder;
            }
            catch
            {
                @this.ReleaseWriter(WRITER_DOOMED);
                throw;
            }
        }
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
