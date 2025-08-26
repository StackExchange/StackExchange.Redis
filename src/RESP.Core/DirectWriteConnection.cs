using System;
using System.Buffers;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace Resp;

internal sealed class DirectWriteConnection : IRespConnection
{
    private bool _isDoomed;
    private RespScanState _readScanState = default;
    private CycleBuffer _readBuffer = CycleBuffer.Create();

    public bool CanWrite => Volatile.Read(ref _readStatus) == WRITER_AVAILABLE;

    public int Outstanding => _outstanding.Count;

    public Task Reader { get; private set; } = Task.CompletedTask;

    private readonly Stream tail;
    private ConcurrentQueue<IRespMessage> _outstanding = new();
    public RespConfiguration Configuration { get; }

    public DirectWriteConnection(RespConfiguration configuration, Stream tail, bool asyncRead = true)
    {
        Configuration = configuration;
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

    public RespMode Mode { get; set; } = RespMode.Resp2;

    public enum RespMode
    {
        Resp2,
        Resp2PubSub,
        Resp3,
    }

    private static byte[]? SharedNoLease;

    private bool CommitAndParseFrames(int bytesRead)
    {
        if (bytesRead <= 0)
        {
            return false;
        }

#if DEBUG
        string src = $"parse {bytesRead}";
        try
#endif
        {
            Debug.Assert(
                bytesRead <= _readBuffer.UncommittedAvailable,
                $"Insufficient bytes in {nameof(CommitAndParseFrames)}; got {bytesRead}, Available={_readBuffer.UncommittedAvailable}");
            _readBuffer.Commit(bytesRead);

            var scanner = RespFrameScanner.Default;

            OperationStatus status = OperationStatus.NeedMoreData;
            ref RespScanState state = ref _readScanState;
            if (_readBuffer.TryGetCommitted(out var fullSpan))
            {
#if DEBUG
                src += $",span {fullSpan.Length}";
#endif
                Debug.Assert(!fullSpan.IsEmpty);

                int fullyConsumed = 0;
                var toParse = fullSpan.Slice((int)state.TotalBytes); // skip what we've already parsed
                while (toParse.Length >= RespScanState.MinBytes
                       && (status = scanner.TryRead(ref state, toParse)) == OperationStatus.Done)
                {
                    Debug.Assert(
                        state is
                        {
                            IsComplete: true, TotalBytes: >= RespScanState.MinBytes, Prefix: not RespPrefix.None
                        },
                        "Invalid RESP read state");

                    // extract the frame
                    var bytes = (int)state.TotalBytes;

                    // send the frame somewhere
                    OnResponseFrame(state.Prefix, toParse.Slice(0, bytes), ref SharedNoLease);

                    // update our buffers to the unread potions and reset for a new RESP frame
                    fullyConsumed += bytes;
                    toParse = toParse.Slice(bytes);
                    state = default;
                    status = OperationStatus.NeedMoreData;
                }

                _readBuffer.DiscardCommitted(fullyConsumed);
            }
            else // the same thing again, but this time with multi-segment sequence
            {
                var fullSequence = _readBuffer.GetAllCommitted();
#if DEBUG
                src += $",ros {fullSequence.Length}";
#endif
                Debug.Assert(
                    fullSequence is { IsEmpty: false, IsSingleSegment: false },
                    "non-trivial sequence expected");

                long fullyConsumed = 0;
                var toParse = fullSequence.Slice((int)state.TotalBytes); // skip what we've already parsed
                while (toParse.Length >= RespScanState.MinBytes
                       && (status = scanner.TryRead(ref state, toParse)) == OperationStatus.Done)
                {
                    Debug.Assert(
                        state is
                        {
                            IsComplete: true, TotalBytes: >= RespScanState.MinBytes, Prefix: not RespPrefix.None
                        },
                        "Invalid RESP read state");

                    // extract the frame
                    var bytes = (int)state.TotalBytes;

                    // send the frame somewhere
                    OnResponseFrame(state.Prefix, toParse.Slice(0, bytes));

                    // update our buffers to the unread potions and reset for a new RESP frame
                    fullyConsumed += bytes;
                    toParse = toParse.Slice(bytes);
                    state = default;
                    status = OperationStatus.NeedMoreData;
                }

                _readBuffer.DiscardCommitted(fullyConsumed);
            }

            if (status != OperationStatus.NeedMoreData)
            {
                ThrowStatus(status);

                static void ThrowStatus(OperationStatus status) =>
                    throw new InvalidOperationException($"Unexpected operation status: {status}");
            }

            return true;
        }
#if DEBUG
        catch (Exception ex)
        {
            Console.WriteLine($"{nameof(CommitAndParseFrames)}: {ex.Message}");
            Console.WriteLine(src);
            throw;
        }
#endif
    }

    private async Task ReadAllAsync()
    {
        try
        {
            CancellationToken cancellationToken = CancellationToken.None;
            int read;
            do
            {
                var buffer = _readBuffer.GetUncommittedMemory();
                var pending = tail.ReadAsync(buffer, cancellationToken);
#if DEBUG
                bool inline = pending.IsCompleted;
#endif
                read = await pending.ConfigureAwait(false);
#if DEBUG
                DebugCounters.OnAsyncRead(read, inline);
#endif
            }
            // another formatter glitch
            while (CommitAndParseFrames(read));

            Volatile.Write(ref _readStatus, READER_COMPLETED);
            _readBuffer.Release(); // clean exit, we can recycle
            Console.WriteLine("Reader clean exit");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Reader failed: {ex.Message}");
            OnReadException(ex);
            throw;
        }
        finally
        {
            Console.WriteLine("Reader finally");
            OnReadAllFinally();
        }
    }

    private void ReadAll()
    {
        var tcs = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
        Reader = tcs.Task;
        try
        {
            CancellationToken cancellationToken = CancellationToken.None;
            int read;
            do
            {
#if NETCOREAPP || NETSTANDARD2_1_OR_GREATER
                var buffer = _readBuffer.GetUncommittedSpan();
                read = tail.Read(buffer);
#else
                var buffer = _readBuffer.GetUncommittedMemory();
                read = tail.Read(buffer);
#endif
                DebugCounters.OnRead(read);
            }
            // another formatter glitch
            while (CommitAndParseFrames(read));

            Volatile.Write(ref _readStatus, READER_COMPLETED);
            _readBuffer.Release(); // clean exit, we can recycle
            tcs.TrySetResult(null);
        }
        catch (Exception ex)
        {
            tcs.TrySetException(ex);
            OnReadException(ex);
        }
        finally
        {
            OnReadAllFinally();
        }
    }

    private void OnReadException(Exception ex)
    {
        Volatile.Write(ref _readStatus, READER_FAILED);
        Debug.WriteLine($"Reader failed: {ex.Message}");
        while (_outstanding.TryDequeue(out var pending))
        {
            pending.TrySetException(ex);
        }
    }

    private void OnReadAllFinally()
    {
        Doom();
        _readBuffer.Release();

        // abandon anything in the queue
        while (_outstanding.TryDequeue(out var pending))
        {
            pending.TrySetCanceled(CancellationToken.None);
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

    private void OnOutOfBand(ReadOnlySpan<byte> payload, ref byte[]? lease)
    {
        throw new NotImplementedException(nameof(OnOutOfBand));
    }

    private void OnResponseFrame(RespPrefix prefix, ReadOnlySequence<byte> payload)
    {
        if (payload.IsSingleSegment)
        {
#if NETCOREAPP || NETSTANDARD2_1_OR_GREATER
            OnResponseFrame(prefix, payload.FirstSpan, ref SharedNoLease);
#else
            OnResponseFrame(prefix, payload.First.Span, ref SharedNoLease);
#endif
        }
        else
        {
            var len = checked((int)payload.Length);
            byte[]? oversized = ArrayPool<byte>.Shared.Rent(len);
            payload.CopyTo(oversized);
            OnResponseFrame(prefix, new(oversized, 0, len), ref oversized);

            // the lease could have been claimed by the activation code (to prevent another memcpy); otherwise, free
            if (oversized is not null)
            {
                ArrayPool<byte>.Shared.Return(oversized);
            }
        }
    }

    [Conditional("DEBUG")]
    private static void DebugOnValidateSingleFrame(ReadOnlySpan<byte> payload)
    {
        var reader = new RespReader(payload);
        reader.MoveNext();
        reader.SkipChildren();
        if (reader.TryMoveNext())
        {
            throw new InvalidOperationException($"Unexpected trailing {reader.Prefix}");
        }

        if (reader.ProtocolBytesRemaining != 0)
        {
            var copy = reader; // leave reader alone for inspection
            var prefix = copy.TryMoveNext() ? copy.Prefix : RespPrefix.None;
            throw new InvalidOperationException(
                $"Unexpected additional {reader.ProtocolBytesRemaining} bytes remaining, {prefix}");
        }
    }

    private void OnResponseFrame(RespPrefix prefix, ReadOnlySpan<byte> payload, ref byte[]? lease)
    {
        DebugOnValidateSingleFrame(payload);
        if (prefix == RespPrefix.Push ||
            (prefix == RespPrefix.Array && Mode is RespMode.Resp2PubSub && !IsArrayPong(payload)))
        {
            // out-of-band; pub/sub etc
            OnOutOfBand(payload, ref lease);
            return;
        }

        // request/response; match to inbound
        if (_outstanding.TryDequeue(out var pending))
        {
            ActivationHelper.ProcessResponse(pending, payload, ref lease);
        }
        else
        {
            Debug.Fail("Unexpected response without pending message!");
        }

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

    private void ReleaseWriter(int status = WRITER_AVAILABLE)
    {
        if (status == WRITER_AVAILABLE && _isDoomed)
        {
            status = WRITER_DOOMED;
        }

        Interlocked.CompareExchange(ref _writeStatus, status, WRITER_TAKEN);
    }

    public void Send(IRespMessage message)
    {
        bool releaseRequest = message.TryReserveRequest(out var bytes);
        if (!releaseRequest) return;
        TakeWriter();
        try
        {
            _outstanding.Enqueue(message);
            releaseRequest = false; // once we write, only release on success
#if NETCOREAPP || NETSTANDARD2_1_OR_GREATER
            tail.Write(bytes.Span);
#else
            tail.Write(bytes);
#endif
            DebugCounters.OnWrite(bytes.Length);
            ReleaseWriter();
            message.ReleaseRequest();
        }
        catch
        {
            ReleaseWriter(WRITER_DOOMED);
            if (releaseRequest) message.ReleaseRequest();
            throw;
        }
    }

    public Task SendAsync(IRespMessage message, CancellationToken cancellationToken = default)
    {
        bool releaseRequest = message.TryReserveRequest(out var bytes);
        if (!releaseRequest) return Task.CompletedTask;
        TakeWriter();
        try
        {
            _outstanding.Enqueue(message);
            releaseRequest = false; // once we write, only release on success
            var pendingWrite = tail.WriteAsync(bytes, cancellationToken);
            if (!pendingWrite.IsCompleted)
            {
                return AwaitedSingleWithToken(
                    this,
                    pendingWrite,
#if DEBUG
                    bytes.Length,
#endif
                    message);
            }

            pendingWrite.GetAwaiter().GetResult();
            DebugCounters.OnAsyncWrite(bytes.Length, true);
            ReleaseWriter();
            message.ReleaseRequest();
            return Task.CompletedTask;
        }
        catch
        {
            ReleaseWriter(WRITER_DOOMED);
            if (releaseRequest) message.ReleaseRequest();
            throw;
        }

        static async Task AwaitedSingleWithToken(
            DirectWriteConnection @this,
            ValueTask pendingWrite,
#if DEBUG
            int length,
#endif
            IRespMessage message)
        {
            try
            {
                await pendingWrite.ConfigureAwait(false);
#if DEBUG
                DebugCounters.OnAsyncWrite(length, false);
#endif
                @this.ReleaseWriter();
                message.ReleaseRequest();
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
