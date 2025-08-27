#define PARSE_DETAIL // additional trace info in CommitAndParseFrames
#if DEBUG
#define PARSE_DETAIL // always enable this in debug builds
#endif

using System;
using System.Buffers;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace Resp;

internal sealed class DirectWriteConnection : IRespConnection
{
    private bool _isDoomed;
    private RespScanState _readScanState = default;

    private CycleBuffer _readBuffer;

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
        _readBuffer = CycleBuffer.Create(configuration.GetService<MemoryPool<byte>>());
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

        // let's bypass a bunch of ldarg0 by hoisting the field-refs (this is **NOT** a struct copy; emphasis "ref")
        ref RespScanState state = ref _readScanState;
        ref CycleBuffer readBuffer = ref _readBuffer;

#if PARSE_DETAIL
        string src = $"parse {bytesRead}";
        try
#endif
        {
            Debug.Assert(readBuffer.GetCommittedLength() >= 0, "multi-segment running-indices are corrupt");
#if PARSE_DETAIL
            src += $" ({readBuffer.GetCommittedLength()}+{bytesRead}-{state.TotalBytes})";
#endif
            Debug.Assert(
                bytesRead <= readBuffer.UncommittedAvailable,
                $"Insufficient bytes in {nameof(CommitAndParseFrames)}; got {bytesRead}, Available={readBuffer.UncommittedAvailable}");
            readBuffer.Commit(bytesRead);
#if PARSE_DETAIL
            src += $",total {readBuffer.GetCommittedLength()}";
#endif
            var scanner = RespFrameScanner.Default;

            OperationStatus status = OperationStatus.NeedMoreData;
            if (readBuffer.TryGetCommitted(out var fullSpan))
            {
                int fullyConsumed = 0;
                var toParse = fullSpan.Slice((int)state.TotalBytes); // skip what we've already parsed

                Debug.Assert(!toParse.IsEmpty);
                while (true)
                {
#if PARSE_DETAIL
                    src += $",span {toParse.Length}";
#endif
                    int totalBytesBefore = (int)state.TotalBytes;
                    if (toParse.Length < RespScanState.MinBytes
                        || (status = scanner.TryRead(ref state, toParse)) != OperationStatus.Done)
                    {
                        break;
                    }

                    Debug.Assert(
                        state is
                        {
                            IsComplete: true, TotalBytes: >= RespScanState.MinBytes, Prefix: not RespPrefix.None
                        },
                        "Invalid RESP read state");

                    // extract the frame
                    var bytes = (int)state.TotalBytes;
#if PARSE_DETAIL
                    src += $",frame {bytes}";
#endif
                    // send the frame somewhere (note this is the *full* frame, not just the bit we just parsed)
                    OnResponseFrame(state.Prefix, fullSpan.Slice(fullyConsumed, bytes), ref SharedNoLease);

                    // update our buffers to the unread potions and reset for a new RESP frame
                    fullyConsumed += bytes;
                    toParse = toParse.Slice(bytes - totalBytesBefore); // move past the extra bytes we just read
                    state = default;
                    status = OperationStatus.NeedMoreData;
                }

                readBuffer.DiscardCommitted(fullyConsumed);
            }
            else // the same thing again, but this time with multi-segment sequence
            {
                var fullSequence = readBuffer.GetAllCommitted();
                Debug.Assert(
                    fullSequence is { IsEmpty: false, IsSingleSegment: false },
                    "non-trivial sequence expected");

                long fullyConsumed = 0;
                var toParse = fullSequence.Slice((int)state.TotalBytes); // skip what we've already parsed
                while (true)
                {
#if PARSE_DETAIL
                    src += $",ros {toParse.Length}";
#endif
                    int totalBytesBefore = (int)state.TotalBytes;
                    if (toParse.Length < RespScanState.MinBytes
                        || (status = scanner.TryRead(ref state, toParse)) != OperationStatus.Done)
                    {
                        break;
                    }

                    Debug.Assert(
                        state is
                        {
                            IsComplete: true, TotalBytes: >= RespScanState.MinBytes, Prefix: not RespPrefix.None
                        },
                        "Invalid RESP read state");

                    // extract the frame
                    var bytes = (int)state.TotalBytes;
#if PARSE_DETAIL
                    src += $",frame {bytes}";
#endif
                    // send the frame somewhere (note this is the *full* frame, not just the bit we just parsed)
                    OnResponseFrame(state.Prefix, fullSequence.Slice(fullyConsumed, bytes));

                    // update our buffers to the unread potions and reset for a new RESP frame
                    fullyConsumed += bytes;
                    toParse = toParse.Slice(bytes - totalBytesBefore); // move past the extra bytes we just read
                    state = default;
                    status = OperationStatus.NeedMoreData;
                }

                readBuffer.DiscardCommitted(fullyConsumed);
            }

            if (status != OperationStatus.NeedMoreData)
            {
                ThrowStatus(status);

                static void ThrowStatus(OperationStatus status) =>
                    throw new InvalidOperationException($"Unexpected operation status: {status}");
            }

            return true;
        }
#if PARSE_DETAIL
        catch (Exception ex)
        {
            Debug.WriteLine($"{nameof(CommitAndParseFrames)}: {ex.Message}");
            Debug.WriteLine(src);
            ActivationHelper.DebugBreak();
            throw new InvalidOperationException($"{src} lead to {ex.Message}", ex);
        }
#endif
    }

    private async Task ReadAllAsync()
    {
        try
        {
            int read;
            do
            {
                var buffer = _readBuffer.GetUncommittedMemory();
                var pending = tail.ReadAsync(buffer, CancellationToken.None);
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
        }
        catch (Exception ex)
        {
            OnReadException(ex);
            throw;
        }
        finally
        {
            OnReadAllFinally();
        }
    }

    private void ReadAll()
    {
        var tcs = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
        Reader = tcs.Task;
        try
        {
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
        _fault ??= ex;
        Volatile.Write(ref _readStatus, READER_FAILED);
        Debug.WriteLine($"Reader failed: {ex.Message}");
        ActivationHelper.DebugBreak();
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
    private static void DebugValidateSingleFrame(ReadOnlySpan<byte> payload)
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
        DebugValidateSingleFrame(payload);
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
        if (status != WRITER_AVAILABLE) ThrowWriterNotAvailable();
        Debug.Assert(Volatile.Read(ref _writeStatus) == WRITER_TAKEN, "writer should be taken");
    }

    private void ThrowWriterNotAvailable()
    {
        var fault = Volatile.Read(ref _fault);
        var status = Volatile.Read(ref _writeStatus);
        var msg = status switch
        {
            WRITER_TAKEN => "A write operation is already in progress; concurrent writes are not supported.",
            WRITER_DOOMED when fault is not null => "This connection is terminated; no further writes are possible: " +
                                                    fault.Message,
            WRITER_DOOMED => "This connection is terminated; no further writes are possible.",
            _ => $"Unexpected writer status: {status}",
        };
        throw fault is null ? new InvalidOperationException(msg) : new InvalidOperationException(msg, fault);
    }

    private Exception? _fault;

    private void ReleaseWriter(int status = WRITER_AVAILABLE)
    {
        if (status == WRITER_AVAILABLE && _isDoomed)
        {
            status = WRITER_DOOMED;
        }

        Interlocked.CompareExchange(ref _writeStatus, status, WRITER_TAKEN);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void OnResponseUnavailable(IRespMessage message)
    {
        if (!message.IsCompleted)
        {
            // make sure they know something is wrong
            message.TrySetException(new InvalidOperationException("Connection is not available"));
        }
    }

    public void Send(IRespMessage message)
    {
        bool releaseRequest = message.TryReserveRequest(out var bytes);
        if (!releaseRequest)
        {
            OnResponseUnavailable(message);
            return;
        }

        DebugValidateSingleFrame(bytes.Span);
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
        catch (Exception ex)
        {
            Debug.WriteLine($"Writer failed: {ex.Message}");
            ActivationHelper.DebugBreak();
            ReleaseWriter(WRITER_DOOMED);
            if (releaseRequest) message.ReleaseRequest();
            throw;
        }
    }

    public void Send(ReadOnlySpan<IRespMessage> messages)
    {
        // lazy and temporary - we should pack these on the stream
        foreach (var message in messages)
        {
            Send(message);
        }
    }

    public Task SendAsync(IRespMessage message)
    {
        bool releaseRequest = message.TryReserveRequest(out var bytes);
        if (!releaseRequest)
        {
            OnResponseUnavailable(message);
            return Task.CompletedTask;
        }

        DebugValidateSingleFrame(bytes.Span);
        TakeWriter();
        try
        {
            _outstanding.Enqueue(message);
            releaseRequest = false; // once we write, only release on success
            var pendingWrite = tail.WriteAsync(bytes, CancellationToken.None);
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
        catch (Exception ex)
        {
            Debug.WriteLine($"Writer failed: {ex.Message}");
            ActivationHelper.DebugBreak();
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

    public Task SendAsync(ReadOnlyMemory<IRespMessage> messages)
    {
        return messages.Length switch
        {
            0 => Task.CompletedTask,
            1 => SendAsync(messages.Span[0]),
            _ => SendMultiple(this, messages),
        };

        static async Task SendMultiple(DirectWriteConnection @this, ReadOnlyMemory<IRespMessage> messages)
        {
            // lazy and temporary - we should pack these on the stream
            var length = messages.Length;
            for (int i = 0; i < length; i++)
            {
                await @this.SendAsync(messages.Span[i]).ConfigureAwait(false);
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
        _fault ??= new ObjectDisposedException(ToString());
        Doom();
        tail.Dispose();
    }

    public override string ToString() => nameof(DirectWriteConnection);

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
