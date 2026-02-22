using System;
using System.Buffers;
using System.Buffers.Binary;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.IO.Pipelines;
using System.Net;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using RESPite;
using RESPite.Buffers;
using RESPite.Internal;
using RESPite.Messages;

namespace StackExchange.Redis;

internal sealed partial class PhysicalConnection
{
    internal static PhysicalConnection Dummy() => new(null!);

    private volatile ReadStatus _readStatus = ReadStatus.NotStarted;
    internal ReadStatus GetReadStatus() => _readStatus;

    internal void StartReading(CancellationToken cancellationToken = default) => ReadAllAsync(cancellationToken).RedisFireAndForget();

    private async Task ReadAllAsync(CancellationToken cancellationToken)
    {
        var tail = _ioStream ?? Stream.Null;
        _readStatus = ReadStatus.Init;
        RespScanState state = default;
        _readBuffer = CycleBuffer.Create();
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
            while (CommitAndParseFrames(ref state, read));

            _readStatus = ReadStatus.ProcessBufferComplete;

            // Volatile.Write(ref _readStatus, ReaderCompleted);
            _readBuffer.Release(); // clean exit, we can recycle
            _readStatus = ReadStatus.RanToCompletion;
            RecordConnectionFailed(ConnectionFailureType.SocketClosed);
        }
        catch (EndOfStreamException) when (_readStatus is ReadStatus.ReadAsync)
        {
            _readStatus = ReadStatus.RanToCompletion;
            RecordConnectionFailed(ConnectionFailureType.SocketClosed);
        }
        catch (Exception ex)
        {
            _readStatus = ReadStatus.Faulted;
            RecordConnectionFailed(ConnectionFailureType.InternalFailure, ex);
        }
        finally
        {
            _readBuffer = default; // wipe, however we exited
        }
    }

    private static byte[]? SharedNoLease;

    private CycleBuffer _readBuffer;
    private long GetReadCommittedLength()
    {
        try
        {
            var len = _readBuffer.GetCommittedLength();
            return len < 0 ? -1 : len;
        }
        catch
        {
            return -1;
        }
    }

    private bool CommitAndParseFrames(ref RespScanState state, int bytesRead)
    {
        if (bytesRead <= 0)
        {
            return false;
        }

        totalBytesReceived += bytesRead;
#if PARSE_DETAIL
        string src = $"parse {bytesRead}";
        try
#endif
        {
            Debug.Assert(_readBuffer.GetCommittedLength() >= 0, "multi-segment running-indices are corrupt");
#if PARSE_DETAIL
            src += $" ({readBuffer.GetCommittedLength()}+{bytesRead}-{state.TotalBytes})";
#endif
            Debug.Assert(
                bytesRead <= _readBuffer.UncommittedAvailable,
                $"Insufficient bytes in {nameof(CommitAndParseFrames)}; got {bytesRead}, Available={_readBuffer.UncommittedAvailable}");
            _readBuffer.Commit(bytesRead);
#if PARSE_DETAIL
            src += $",total {readBuffer.GetCommittedLength()}";
#endif
            var scanner = RespFrameScanner.Default;

            OperationStatus status = OperationStatus.NeedMoreData;
            if (_readBuffer.TryGetCommitted(out var fullSpan))
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
                    UpdateBufferStats(bytes, toParse.Length);

                    // update our buffers to the unread potions and reset for a new RESP frame
                    fullyConsumed += bytes;
                    toParse = toParse.Slice(bytes - totalBytesBefore); // move past the extra bytes we just read
                    state = default;
                    status = OperationStatus.NeedMoreData;
                }

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
                    UpdateBufferStats(bytes, toParse.Length);

                    // update our buffers to the unread potions and reset for a new RESP frame
                    fullyConsumed += bytes;
                    toParse = toParse.Slice(bytes - totalBytesBefore); // move past the extra bytes we just read
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

    public RespMode Mode { get; set; } = RespMode.Resp2;

    public enum RespMode
    {
        Resp2,
        Resp2PubSub,
        Resp3,
    }

    private void UpdateBufferStats(int lastResult, long inBuffer)
    {
        // Track the last result size *after* processing for the *next* error message
        bytesInBuffer = inBuffer;
        bytesLastResult = lastResult;
    }

    private void OnResponseFrame(RespPrefix prefix, ReadOnlySpan<byte> frame, ref byte[]? lease)
    {
        DebugValidateSingleFrame(frame);
        _readStatus = ReadStatus.MatchResult;
        switch (prefix)
        {
            case RespPrefix.Push: // explicit push message
            case RespPrefix.Array when Mode is RespMode.Resp2PubSub && !IsArrayPong(frame): // likely pub/sub payload
                // out-of-band; pub/sub etc
                if (OnOutOfBand(frame, ref lease))
                {
                    return;
                }
                break;
        }

        // request/response; match to inbound
        MatchNextResult(frame);

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
    }

    private enum PushKind
    {
        None,
        Message,
        PMessage,
        SMessage,
        Subscribe,
        PSubscribe,
        SSubscribe,
        Unsubscribe,
        PUnsubscribe,
        SUnsubscribe,
    }

    internal static ReadOnlySpan<byte> StackCopyLengthChecked(scoped in RespReader reader, Span<byte> buffer)
    {
        var len = reader.CopyTo(buffer);
        if (len == buffer.Length && reader.ScalarLength() > len) return default; // too small
        return buffer.Slice(0, len);
    }

    private bool OnOutOfBand(ReadOnlySpan<byte> payload, ref byte[]? lease)
    {
        var muxer = BridgeCouldBeNull?.Multiplexer;
        if (muxer is null) return true; // consume it blindly

        var reader = new RespReader(payload);

        // read the message kind from the first element
        int len;
        if (reader.SafeTryMoveNext() & reader.IsAggregate & !reader.IsStreaming
            && (len = reader.AggregateLength()) >= 2
            && (reader.SafeTryMoveNext() & reader.IsInlineScalar & !reader.IsError))
        {
            const int MAX_TYPE_LEN = 16;
            var span = reader.TryGetSpan(out var tmp)
                ? tmp : StackCopyLengthChecked(in reader, stackalloc byte[MAX_TYPE_LEN]);

            var hash = AsciiHash.HashCS(span);
            RedisChannel.RedisChannelOptions channelOptions = RedisChannel.RedisChannelOptions.None;
            PushKind kind;
            switch (hash)
            {
                case PushMessage.HashCS when PushMessage.IsCS(hash, span) & len >= 3:
                    kind = PushKind.Message;
                    break;
                case PushPMessage.HashCS when PushPMessage.IsCS(hash, span) & len >= 4:
                    channelOptions = RedisChannel.RedisChannelOptions.Pattern;
                    kind = PushKind.PMessage;
                    break;
                case PushSMessage.HashCS when PushSMessage.IsCS(hash, span) & len >= 3:
                    channelOptions = RedisChannel.RedisChannelOptions.Sharded;
                    kind = PushKind.SMessage;
                    break;
                case PushSubscribe.HashCS when PushSubscribe.IsCS(hash, span):
                    kind = PushKind.Subscribe;
                    break;
                case PushPSubscribe.HashCS when PushPSubscribe.IsCS(hash, span):
                    channelOptions = RedisChannel.RedisChannelOptions.Pattern;
                    kind = PushKind.PSubscribe;
                    break;
                case PushSSubscribe.HashCS when PushSSubscribe.IsCS(hash, span):
                    channelOptions = RedisChannel.RedisChannelOptions.Sharded;
                    kind = PushKind.SSubscribe;
                    break;
                case PushUnsubscribe.HashCS when PushUnsubscribe.IsCS(hash, span):
                    kind = PushKind.Unsubscribe;
                    break;
                case PushPUnsubscribe.HashCS when PushPUnsubscribe.IsCS(hash, span):
                    channelOptions = RedisChannel.RedisChannelOptions.Pattern;
                    kind = PushKind.PUnsubscribe;
                    break;
                case PushSUnsubscribe.HashCS when PushSUnsubscribe.IsCS(hash, span):
                    channelOptions = RedisChannel.RedisChannelOptions.Sharded;
                    kind = PushKind.SUnsubscribe;
                    break;
                default:
                    kind = PushKind.None;
                    break;
            }

            static bool TryMoveNextString(ref RespReader reader)
                => reader.SafeTryMoveNext() & reader.IsInlineScalar &
                   reader.Prefix is RespPrefix.BulkString or RespPrefix.SimpleString;

            if (kind is PushKind.None || !TryMoveNextString(ref reader)) return false;

            // the channel is always the second element
            var subscriptionChannel = AsRedisChannel(reader, channelOptions);

            switch (kind)
            {
                case (PushKind.Message or PushKind.SMessage) when reader.SafeTryMoveNext():
                    _readStatus = kind is PushKind.Message ? ReadStatus.PubSubMessage : ReadStatus.PubSubSMessage;

                    // special-case the configuration change broadcasts (we don't keep that in the usual pub/sub registry)
                    var configChanged = muxer.ConfigurationChangedChannel;
                    if (configChanged != null && reader.Prefix is RespPrefix.BulkString or RespPrefix.SimpleString && reader.Is(configChanged))
                    {
                        EndPoint? blame = null;
                        try
                        {
                            if (!reader.Is("*"u8))
                            {
                                // We don't want to fail here, just trying to identify
                                _ = Format.TryParseEndPoint(reader.ReadString(), out blame);
                            }
                        }
                        catch
                        {
                            /* no biggie */
                        }

                        Trace("Configuration changed: " + Format.ToString(blame));
                        _readStatus = ReadStatus.Reconfigure;
                        muxer.ReconfigureIfNeeded(blame, true, "broadcast");
                    }

                    // invoke the handlers
                    if (!subscriptionChannel.IsNull)
                    {
                        Trace($"{kind}: {subscriptionChannel}");
                        OnMessage(muxer, subscriptionChannel, subscriptionChannel, in reader);
                    }

                    return true;
                case PushKind.PMessage when TryMoveNextString(ref reader):
                    _readStatus = ReadStatus.PubSubPMessage;

                    var messageChannel = AsRedisChannel(reader, RedisChannel.RedisChannelOptions.None);
                    if (!messageChannel.IsNull && reader.SafeTryMoveNext())
                    {
                        Trace($"{kind}: {messageChannel} via {subscriptionChannel}");
                        OnMessage(muxer, subscriptionChannel, messageChannel, in reader);
                    }

                    return true;
                case PushKind.SUnsubscribe when !PeekChannelMessage(RedisCommand.SUNSUBSCRIBE, subscriptionChannel):
                    // then it was *unsolicited* - this probably means the slot was migrated
                    // (otherwise, we'll let the command-processor deal with it)
                    _readStatus = ReadStatus.PubSubUnsubscribe;
                    var server = BridgeCouldBeNull?.ServerEndPoint;
                    if (server is not null && muxer.TryGetSubscription(subscriptionChannel, out var subscription))
                    {
                        // wipe and reconnect; but: to where?
                        // counter-intuitively, the only server we *know* already knows the new route is:
                        // the outgoing server, since it had to change to MIGRATING etc; the new INCOMING server
                        // knows, but *we don't know who that is*, and other nodes: aren't guaranteed to know (yet)
                        muxer.DefaultSubscriber.ResubscribeToServer(subscription, subscriptionChannel, server, cause: PushSUnsubscribe.Text);
                    }
                    return true;
            }
        }
        return false;
    }

    private void OnMessage(
        ConnectionMultiplexer muxer,
        in RedisChannel subscriptionChannel,
        in RedisChannel messageChannel,
        in RespReader reader)
    {
        // note: this could be multi-message: https://github.com/StackExchange/StackExchange.Redis/issues/2507
        _readStatus = ReadStatus.InvokePubSub;
        switch (reader.Prefix)
        {
            case RespPrefix.BulkString:
            case RespPrefix.SimpleString:
                muxer.OnMessage(subscriptionChannel, messageChannel, reader.ReadRedisValue());
                break;
            case RespPrefix.Array:
                var iter = reader.AggregateChildren();
                while (iter.MoveNext())
                {
                    muxer.OnMessage(subscriptionChannel, messageChannel, iter.Value.ReadRedisValue());
                }

                break;
        }
    }

    private void MatchNextResult(ReadOnlySpan<byte> frame)
    {
        Trace("Matching result...");

        Message? msg = null;
        // check whether we're waiting for a high-integrity mode post-response checksum (using cheap null-check first)
        if (_awaitingToken is not null && (msg = Interlocked.Exchange(ref _awaitingToken, null)) is not null)
        {
            _readStatus = ReadStatus.ResponseSequenceCheck;
            if (!ProcessHighIntegrityResponseToken(msg, frame, BridgeCouldBeNull))
            {
                RecordConnectionFailed(ConnectionFailureType.ResponseIntegrityFailure, origin: nameof(ReadStatus.ResponseSequenceCheck));
            }
            return;
        }

        _readStatus = ReadStatus.DequeueResult;
        lock (_writtenAwaitingResponse)
        {
            if (msg is not null)
            {
                _awaitingToken = null;
            }

            if (!_writtenAwaitingResponse.TryDequeue(out msg))
            {
                Throw(frame);

                [DoesNotReturn]
                static void Throw(ReadOnlySpan<byte> frame)
                {
                    var prefix = RespReaderExtensions.GetRespPrefix(frame);
                    throw new InvalidOperationException("Received response with no message waiting: " + prefix.ToString());
                }
            }
        }
        _activeMessage = msg;

        Trace("Response to: " + msg);
        _readStatus = ReadStatus.ComputeResult;
        var reader = new RespReader(frame);
        if (msg.ComputeResult(this, ref reader))
        {
            _readStatus = msg.ResultBoxIsAsync ? ReadStatus.CompletePendingMessageAsync : ReadStatus.CompletePendingMessageSync;
            if (!msg.IsHighIntegrity)
            {
                // can't complete yet if needs checksum
                msg.Complete();
            }
        }
        if (msg.IsHighIntegrity)
        {
            // stash this for the next non-OOB response
            Volatile.Write(ref _awaitingToken, msg);
        }

        _readStatus = ReadStatus.MatchResultComplete;
        _activeMessage = null;

        static bool ProcessHighIntegrityResponseToken(Message message, ReadOnlySpan<byte> frame, PhysicalBridge? bridge)
        {
            bool isValid = false;
            var reader = new RespReader(frame);
            if ((reader.SafeTryMoveNext() & reader.IsScalar)
                && reader.ScalarLength() is 4)
            {
                uint interpreted;
                if (reader.TryGetSpan(out var span))
                {
                    interpreted = BinaryPrimitives.ReadUInt32LittleEndian(span);
                }
                else
                {
                    Span<byte> tmp = stackalloc byte[4];
                    reader.CopyTo(tmp);
                    interpreted = BinaryPrimitives.ReadUInt32LittleEndian(tmp);
                }
                isValid = interpreted == message.HighIntegrityToken;
            }
            if (isValid)
            {
                message.Complete();
                return true;
            }
            else
            {
                message.SetExceptionAndComplete(new InvalidOperationException("High-integrity mode detected possible protocol de-sync"), bridge);
                return false;
            }
        }
    }

    private bool PeekChannelMessage(RedisCommand command, in RedisChannel channel)
    {
        Message? msg;
        bool haveMsg;
        lock (_writtenAwaitingResponse)
        {
            haveMsg = _writtenAwaitingResponse.TryPeek(out msg);
        }

        return haveMsg && msg is Message.CommandChannelBase typed
                       && typed.Command == command && typed.Channel == channel;
    }

    internal RedisChannel AsRedisChannel(in RespReader reader, RedisChannel.RedisChannelOptions options)
    {
        var channelPrefix = ChannelPrefix;
        if (channelPrefix is null)
        {
            // no channel-prefix enabled, just use as-is
            return new RedisChannel(reader.ReadByteArray(), options);
        }

        byte[] lease = [];
        var span = reader.TryGetSpan(out var tmp) ? tmp : reader.Buffer(ref lease, stackalloc byte[256]);

        if (span.StartsWith(channelPrefix))
        {
            // we have a channel-prefix, and it matches; strip it
            span = span.Slice(channelPrefix.Length);
        }
        else if (span.StartsWith("__keyspace@"u8) || span.StartsWith("__keyevent@"u8))
        {
            // we shouldn't get unexpected events, so to get here: we've received a notification
            // on a channel that doesn't match our prefix; this *should* be limited to
            // key notifications (see: IgnoreChannelPrefix), but: we need to be sure

            // leave alone
        }
        else
        {
            // no idea what this is
            span = default;
        }

        RedisChannel channel = span.IsEmpty ? default : new(span.ToArray(), options);
        if (lease.Length != 0) ArrayPool<byte>.Shared.Return(lease);
        return channel;
    }

    [AsciiHash("message")]
    internal static partial class PushMessage { }

    [AsciiHash("pmessage")]
    internal static partial class PushPMessage { }

    [AsciiHash("smessage")]
    internal static partial class PushSMessage { }

    [AsciiHash("subscribe")]
    private static partial class PushSubscribe { }

    [AsciiHash("psubscribe")]
    private static partial class PushPSubscribe { }

    [AsciiHash("ssubscribe")]
    private static partial class PushSSubscribe { }

    [AsciiHash("unsubscribe")]
    private static partial class PushUnsubscribe { }

    [AsciiHash("punsubscribe")]
    private static partial class PushPUnsubscribe { }

    [AsciiHash("sunsubscribe")]
    private static partial class PushSUnsubscribe { }

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

    // ReSharper restore InconsistentNaming
    [Conditional("DEBUG")]
    private static void DebugValidateSingleFrame(ReadOnlySpan<byte> payload)
    {
        var reader = new RespReader(payload);
        if (!reader.SafeTryMoveNext())
        {
            throw new InvalidOperationException("No root RESP element");
        }
        reader.SkipChildren();

        if (reader.SafeTryMoveNext())
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
    }

    /*
    private async Task ReadFromPipe()
    {
        _readBuffer = CycleBuffer.Create();
        bool allowSyncRead = true, isReading = false;
        try
        {
            _readStatus = ReadStatus.Init;
            while (true)
            {
                var input = _ioPipe?.Input;
                if (input == null) break;

                // note: TryRead will give us back the same buffer in a tight loop
                // - so: only use that if we're making progress
                isReading = true;
                _readStatus = ReadStatus.ReadSync;
                if (!(allowSyncRead && input.TryRead(out var readResult)))
                {
                    _readStatus = ReadStatus.ReadAsync;
                    readResult = await input.ReadAsync().ForAwait();
                }
                isReading = false;
                _readStatus = ReadStatus.UpdateWriteTime;
                UpdateLastReadTime();

                _readStatus = ReadStatus.ProcessBuffer;
                var buffer = readResult.Buffer;
                int handled = 0;
                if (!buffer.IsEmpty)
                {
                    handled = ProcessBuffer(ref buffer); // updates buffer.Start
                }

                allowSyncRead = handled != 0;

                _readStatus = ReadStatus.MarkProcessed;
                Trace($"Processed {handled} messages");
                input.AdvanceTo(buffer.Start, buffer.End);

                if (handled == 0 && readResult.IsCompleted)
                {
                    break; // no more data, or trailing incomplete messages
                }
            }
            Trace("EOF");
            RecordConnectionFailed(ConnectionFailureType.SocketClosed);
            _readStatus = ReadStatus.RanToCompletion;
        }
        catch (Exception ex)
        {
            _readStatus = ReadStatus.Faulted;
            // this CEX is just a hardcore "seriously, read the actual value" - there's no
            // convenient "Thread.VolatileRead<T>(ref T field) where T : class", and I don't
            // want to make the field volatile just for this one place that needs it
            if (isReading)
            {
                var pipe = Volatile.Read(ref _ioPipe);
                if (pipe == null)
                {
                    return;
                    // yeah, that's fine... don't worry about it; we nuked it
                }

                // check for confusing read errors - no need to present "Reading is not allowed after reader was completed."
                if (pipe is SocketConnection sc && sc.ShutdownKind == PipeShutdownKind.ReadEndOfStream)
                {
                    RecordConnectionFailed(ConnectionFailureType.SocketClosed, new EndOfStreamException());
                    return;
                }
            }
            Trace("Faulted");
            RecordConnectionFailed(ConnectionFailureType.InternalFailure, ex);
        }
    }
    */
}
