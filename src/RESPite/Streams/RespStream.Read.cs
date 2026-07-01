using System.Buffers;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using RESPite.Buffers;
using RESPite.Internal;
using RESPite.Messages;
using RESPite.Shared;

namespace RESPite.Streams;

public partial class RespStream
{
    private ReadStatus _readStatus = ReadStatus.Init;
    private CycleBuffer _readBuffer;
    private RespScanState _readState = default;
    private long _totalBytesRead;
    protected virtual MemoryPool<byte> ReaderBufferPool => MemoryPool<byte>.Shared;

    protected virtual void UpdateLastReadTime() { }

    private protected virtual void RecordConnectionFailed(StreamFailureKind kind, Exception? fault = null) { }

    protected virtual bool ShouldTransitionToAsync() => false;
    protected virtual bool ForceReconnect => false;

    public long TotalBytesRead => _totalBytesRead;

    [Conditional("PARSE_DETAIL")]
    private protected void OnDetailLog(string message) { }

    private async Task ReadAllAsync(CancellationToken cancellationToken)
    {
        if (_readStatus is not ReadStatus.TransitioningToAsync)
        {
            // preserve existing state if transitioning
            _readStatus = ReadStatus.Init;
            _readState = default;
            _readBuffer = CycleBuffer.Create(pool: ReaderBufferPool);
        }

        try
        {
            int read;
            do
            {
                _readStatus = ReadStatus.ReadAsync;
                var buffer = _readBuffer.GetUncommittedMemory();
                var pending = tail.ReadAsync(buffer, cancellationToken);
#if DEBUG
                bool inline = pending.IsCompleted;
#endif
                read = await pending.ConfigureAwait(false);
                _readStatus = ReadStatus.UpdateWriteTime;
                UpdateLastReadTime();
#if DEBUG
                DebugCounters.OnAsyncRead(read, inline);
#endif
                _readStatus = ReadStatus.TryParseResult;
            }
            // another formatter glitch
            while (CommitAndParseFrames(read) && !ForceReconnect);

            _readStatus = ReadStatus.ProcessBufferComplete;

            // Volatile.Write(ref _readStatus, ReaderCompleted);
            _readBuffer.Release(); // clean exit, we can recycle
            _readStatus = ReadStatus.RanToCompletion;
            RecordConnectionFailed(StreamFailureKind.SocketClosed);
        }
        catch (EndOfStreamException) when (_readStatus is ReadStatus.ReadAsync)
        {
            _readStatus = ReadStatus.RanToCompletion;
            RecordConnectionFailed(StreamFailureKind.SocketClosed);
        }
        catch (OperationCanceledException) when (_readStatus is ReadStatus.ReadAsync)
        {
            _readStatus = ReadStatus.RanToCompletion;
            RecordConnectionFailed(StreamFailureKind.SocketClosed);
        }
        catch (Exception ex)
        {
            _readStatus = ReadStatus.Faulted;
            RecordConnectionFailed(StreamFailureKind.InternalFailure, ex);
        }
        finally
        {
            _readBuffer = default; // wipe, however we exited
        }
    }

    private void ReadAllSync(CancellationToken cancellationToken)
    {
        _readStatus = ReadStatus.Init;
        _readState = default;
        _readBuffer = CycleBuffer.Create(pool: ReaderBufferPool);
        try
        {
            int read;
            do
            {
                _readStatus = ReadStatus.ReadSync;
                var buffer = _readBuffer.GetUncommittedMemory();
                cancellationToken.ThrowIfCancellationRequested();
#if NET
                read = tail.Read(buffer.Span);
#else
                read = tail.Read(buffer);
#endif

                _readStatus = ReadStatus.UpdateWriteTime;
                UpdateLastReadTime();

                DebugCounters.OnSyncRead(read);
                _readStatus = ReadStatus.TryParseResult;
            }
            // another formatter glitch
            while (CommitAndParseFrames(read) && !ForceReconnect && !ShouldTransitionToAsync());

            if (_readStatus is ReadStatus.TransitioningToAsync) return;

            _readStatus = ReadStatus.ProcessBufferComplete;

            // Volatile.Write(ref _readStatus, ReaderCompleted);
            _readBuffer.Release(); // clean exit, we can recycle
            _readStatus = ReadStatus.RanToCompletion;
            RecordConnectionFailed(StreamFailureKind.SocketClosed);
        }
        catch (EndOfStreamException) when (_readStatus is ReadStatus.ReadSync)
        {
            _readStatus = ReadStatus.RanToCompletion;
            RecordConnectionFailed(StreamFailureKind.SocketClosed);
        }
        catch (OperationCanceledException) when (_readStatus is ReadStatus.ReadSync)
        {
            _readStatus = ReadStatus.RanToCompletion;
            RecordConnectionFailed(StreamFailureKind.SocketClosed);
        }
        catch (Exception ex)
        {
            _readStatus = ReadStatus.Faulted;
            RecordConnectionFailed(StreamFailureKind.InternalFailure, ex);
        }
        finally
        {
            if (_readStatus is ReadStatus.TransitioningToAsync)
            {
                StartReading(false, cancellationToken);
            }
            else
            {
                _readBuffer = default; // wipe, however we exited
            }
        }
    }

    protected virtual string Name => GetType().Name;
    public override string ToString() => Name;

    public void StartReading(bool sync, CancellationToken cancellationToken)
    {
        if (sync)
        {
            StartReadingSync(this, cancellationToken);
            static void StartReadingSync(RespStream conn, CancellationToken cancellation)
            {
                // this method exists purely to limit capture context scope
                Thread thread = new Thread(() => conn.ReadAllSync(cancellation))
                {
                    IsBackground = true,
                    Priority = ThreadPriority.AboveNormal,
                    Name = conn.Name,
                };
                thread.Start();
            }
        }
        else
        {
            StartReadingAsync(this, cancellationToken);
            static void StartReadingAsync(RespStream conn, CancellationToken cancellationToken)
            {
                // this method exists purely to limit capture context scope
                Task.Run(() => conn.ReadAllAsync(cancellationToken), cancellationToken).FireAndForget();
            }
        }
    }

    private bool CommitAndParseFrames(int bytesRead)
    {
        if (bytesRead <= 0)
        {
            return false;
        }

        ref RespScanState state = ref _readState; // avoid a ton of ldarg0

        _totalBytesRead += bytesRead;
#if PARSE_DETAIL
        string src = $"parse {bytesRead}";
        try
#endif
        {
            Debug.Assert(_readBuffer.GetCommittedLength() >= 0, "multi-segment running-indices are corrupt");
#if PARSE_DETAIL
            src += $" ({_readBuffer.GetCommittedLength()}+{bytesRead}-{state.TotalBytes})";
#endif
            Debug.Assert(
                bytesRead <= _readBuffer.UncommittedAvailable,
                $"Insufficient bytes in {nameof(CommitAndParseFrames)}; got {bytesRead}, Available={_readBuffer.UncommittedAvailable}");
            _readBuffer.Commit(bytesRead);
#if PARSE_DETAIL
            src += $",total {_readBuffer.GetCommittedLength()}";
#endif
            var scanner = RespFrameScanner.Default;

            OperationStatus status = OperationStatus.NeedMoreData;
            if (_readBuffer.TryGetCommitted(out var fullSpan))
            {
                int fullyConsumed = 0;
                var toParse = fullSpan.Slice((int)state.TotalBytes); // skip what we've already parsed
                OnDetailLog($"parsing {toParse.Length} bytes, single buffer");

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
                    OnDetailLog($"found {state.Prefix} frame, {bytes} bytes");
                    OnResponseFrame(state.Prefix, fullSpan.Slice(fullyConsumed, bytes), ref SharedNoLease);
                    UpdateBufferStats(bytes, toParse.Length);

                    // update our buffers to the unread potions and reset for a new RESP frame
                    fullyConsumed += bytes;
                    toParse = toParse.Slice(bytes - totalBytesBefore); // move past the extra bytes we just read
                    state = default;
                    status = OperationStatus.NeedMoreData;
                }

                OnDetailLog($"discarding {fullyConsumed} bytes");
                _readBuffer.DiscardCommitted(fullyConsumed);
            }
            else // the same thing again, but this time with multi-segment sequence
            {
                var fullSequence = _readBuffer.GetAllCommitted();
                Debug.Assert(
                    fullSequence is { IsEmpty: false, IsSingleSegment: false },
                    "non-trivial sequence expected");

                long fullyConsumed = 0;
                var toParse = fullSequence.Slice((int)state.TotalBytes); // skip what we've already parsed
                OnDetailLog($"parsing {toParse.Length} bytes, multi-buffer");

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
                    OnDetailLog($"found {state.Prefix} frame, {bytes} bytes");
                    OnResponseFrame(state.Prefix, fullSequence.Slice(fullyConsumed, bytes));
                    UpdateBufferStats(bytes, toParse.Length);

                    // update our buffers to the unread potions and reset for a new RESP frame
                    fullyConsumed += bytes;
                    toParse = toParse.Slice(bytes - totalBytesBefore); // move past the extra bytes we just read
                    state = default;
                    status = OperationStatus.NeedMoreData;
                }

                OnDetailLog($"discarding {fullyConsumed} bytes");
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
#if PARSE_DETAIL
        catch (Exception ex)
        {
            OnDetailLog($"{nameof(CommitAndParseFrames)}: {ex.Message}");
            OnDetailLog(src);
            if (Debugger.IsAttached) Debugger.Break();
            throw new InvalidOperationException($"{src} lead to {ex.Message}", ex);
        }
#endif
    }

    protected virtual void UpdateBufferStats(int lastFrameBytes, long bufferBytes) { }

    private void OnResponseFrame(RespPrefix prefix, ReadOnlySequence<byte> payload)
    {
        if (payload.IsSingleSegment)
        {
            OnResponseFrame(prefix, payload.FirstSpan, ref SharedNoLease);
        }
        else
        {
            var len = checked((int)payload.Length);
            var memoryOwner = ReaderBufferPool.Rent(len);
            Span<byte> oversized = memoryOwner.Memory.Span.Slice(0, len);

            payload.CopyTo(oversized);

            OnResponseFrame(prefix, oversized, ref memoryOwner);

            memoryOwner?.Dispose();
        }
    }

    // private static IMemoryOwner<byte>? SharedNoLease;
    private static ref IMemoryOwner<byte>? SharedNoLease => ref Unsafe.NullRef<IMemoryOwner<byte>?>();

    protected virtual bool IsPubSub => false; // specifically, RESP2 in pub/sub mode

    private void OnResponseFrame(RespPrefix prefix, ReadOnlySpan<byte> frame, ref IMemoryOwner<byte>? memoryOwner)
    {
        DebugValidateSingleFrame(frame);
        _readStatus = ReadStatus.MatchResult;

        switch (prefix)
        {
            case RespPrefix.Push: // explicit RESP3 push message
            case RespPrefix.Array when IsPubSub && !IsArrayPong(frame): // could be a RESP2 pub/sub payload
                // out-of-band; pub/sub etc
                if (OnReadOutOfBand(prefix, frame, ref memoryOwner))
                {
                    OnDetailLog($"out-of-band message, not dequeuing: {prefix}");
                    return;
                }
                break;
        }

        // request/response; match to inbound
        OnReadFrame(prefix, frame, ref memoryOwner);

        static bool IsArrayPong(ReadOnlySpan<byte> payload)
        {
            if (payload.Length >= sizeof(ulong))
            {
                var hash = AsciiHash.HashCS(payload);
                switch (hash)
                {
                    case ArrayPong_LC_Bulk.HashCS when payload.StartsWith(ArrayPong_LC_Bulk.U8):
                    case ArrayPong_UC_Bulk.HashCS when payload.StartsWith(ArrayPong_UC_Bulk.U8):
                    case ArrayPong_LC_Simple.HashCS when payload.StartsWith(ArrayPong_LC_Simple.U8):
                    case ArrayPong_UC_Simple.HashCS when payload.StartsWith(ArrayPong_UC_Simple.U8):
                        var reader = new RespReader(payload);
                        return reader.SafeTryMoveNext() // have root
                               && reader.Prefix == RespPrefix.Array // root is array
                               && reader.SafeTryMoveNext() // have first child
                               && (reader.IsInlneCpuUInt32(pong) || reader.IsInlneCpuUInt32(PONG)); // pong
                }
            }

            return false;
        }
        // ReSharper restore InconsistentNaming
        [Conditional("DEBUG")]
        static void DebugValidateSingleFrame(ReadOnlySpan<byte> payload)
        {
#if DEBUG
            var reader = new RespReader(payload);
            if (!reader.TryMoveNext(checkError: false))
            {
                throw new InvalidOperationException("No root RESP element");
            }
            reader.SkipChildren();

#pragma warning disable CS0618 // we don't expect *any* additional data, even attributes
            if (reader.TryReadNext())
#pragma warning restore CS0618
            {
                throw new InvalidOperationException($"Unexpected trailing {reader.Prefix}");
            }

            if (reader.ProtocolBytesRemaining != 0)
            {
                var copy = reader; // leave reader alone for inspection
                var prefix = copy.SafeTryMoveNext() ? copy.Prefix : RespPrefix.None;
                throw new InvalidOperationException(
                    $"Unexpected additional {reader.ProtocolBytesRemaining} bytes remaining, {prefix}");
            }
#endif
        }
    }

    protected virtual void OnReadFrame(RespPrefix prefix, ReadOnlySpan<byte> frame, ref IMemoryOwner<byte>? memoryOwner) { }

    protected virtual bool OnReadOutOfBand(RespPrefix prefix, ReadOnlySpan<byte> frame, ref IMemoryOwner<byte>? memoryOwner) => false;

    [AsciiHash("*2\r\n$4\r\npong\r\n$")]
    private static partial class ArrayPong_LC_Bulk { }
    [AsciiHash("*2\r\n$4\r\nPONG\r\n$")]
    private static partial class ArrayPong_UC_Bulk { }
    [AsciiHash("*2\r\n+pong\r\n$")]
    private static partial class ArrayPong_LC_Simple { }
    [AsciiHash("*2\r\n+PONG\r\n$")]
    private static partial class ArrayPong_UC_Simple { }

    // ReSharper disable InconsistentNaming
    private static readonly uint
        pong = RespConstants.UnsafeCpuUInt32("pong"u8),
        PONG = RespConstants.UnsafeCpuUInt32("PONG"u8);
}
