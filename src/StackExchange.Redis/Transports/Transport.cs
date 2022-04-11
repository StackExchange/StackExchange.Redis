using System;
using System.Buffers;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using static StackExchange.Redis.PhysicalConnection;

#nullable enable
namespace StackExchange.Redis.Transports
{
    internal abstract class Transport : ITransportState
    {
        private readonly ITransportState? _parentState;
        private readonly int _inputBufferSize, _outputBufferSize;
        private readonly bool _pubsub;
        private readonly ILogger? _logger;
        private readonly RefCountedMemoryPool<byte> _pool;
        private readonly ServerEndPoint _server; 
        private volatile ReadStatus _readStatus;

        public ReadStatus ReadStatus => _readStatus;

        bool IncludeDetailInExceptions => _server?.Multiplexer?.IncludeDetailInExceptions ?? false;

        
        public Transport(int inputBufferSize, int outputBufferSize, ILogger? logger, RefCountedMemoryPool<byte>? pool, ITransportState? parentState, ServerEndPoint server, bool pubsub)
        {
            _server = server!;
            _inputBufferSize = inputBufferSize;
            _outputBufferSize = outputBufferSize;
            _logger = logger;
            _pool = pool ?? RefCountedMemoryPool<byte>.Shared;
            _readStatus = ReadStatus.NotStarted;
            _pubsub = pubsub;
            _parentState = parentState;

            if (server is null) ThrowNull(nameof(server));
            if (inputBufferSize <= 0) ThrowBufferSize(nameof(inputBufferSize));
            if (outputBufferSize <= 0) ThrowBufferSize(nameof(outputBufferSize));

            static void ThrowBufferSize(string parameterName) => throw new ArgumentOutOfRangeException(parameterName);
            static void ThrowNull(string parameterName) => throw new ArgumentNullException(parameterName);
        }

        private static RefCountedMemoryPool<RawResult> ResultPool => RefCountedMemoryPool<RawResult>.Shared;

        CommandMap ITransportState.CommandMap => _parentState?.CommandMap ?? CommandMap.Default;
        ServerEndPoint ITransportState.ServerEndPoint => _server;

        public byte[]? ChannelPrefix => _parentState?.ChannelPrefix;

        protected abstract ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken);
        protected abstract ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken);

        protected virtual ValueTask WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
            => WriteAsync(new ReadOnlyMemory<byte>(buffer, offset, count), cancellationToken);

        protected virtual ValueTask FlushAsync(CancellationToken cancellationToken) => default;

        public async Task WriteAllAsync(ChannelReader<WrittenMessage> messages, CancellationToken cancellationToken)
        {
            byte[]? bufferArray = null;
            try
            {
                int buffered = 0, capacity = _outputBufferSize;
                do
                {
                    bool needFlush = false;
                    while (true) // try to read synchronously
                    {
                        if (!messages.TryRead(out var pair))
                        {
                            break;
                            //await Task.Yield(); // blink; see if things improved
                            //if (!source.TryRead(out frame)) break; // nope, definitely nothing there
                        }

                        var sequence = pair.Payload;
                        _logger.Debug(pair.Message, static (state, _) => $"Dequeued {state.CommandAndKey} for writing");

                        if (pair.Message is not null)
                        {
                            lock (_writtenAwaitingResponse)
                            {
                                _writtenAwaitingResponse.Enqueue(pair.Message);
                            }
                        }
                        foreach (var inbound in sequence)
                        {
                            int inboundLength = inbound.Length;

                            // scenarios:
                            // A: nothing buffered, inbound doesn't fit: just write inbound directly
                            // B: (something or nothing buffered); inbound fits into buffer: buffer it (write if no remaining capacity after buffering) 
                            // C: something buffered, inbound doesn't fit:
                            //    fill the existing buffer and write
                            //    D: remainder fits into next buffer: buffer the remainder
                            //    E: otherwise, send the remainder
                            //_logger.Debug((inbound, capacity), static (state, _) => $"Considering {state.inbound.Length} bytes (buffer capacity: {state.capacity}): {state.inbound.ToHex()}");
                            if (inboundLength == 0)
                            {
                                Debug.Assert(false, "empty frame!");
                                inbound.Release();
                                continue;
                            }

                            needFlush = true; // we have at least something; we'll need to flush

                            if (buffered == 0 && inboundLength >= capacity)
                            {
                                // scenario A
                                await WriteAsync(inbound, cancellationToken);
                            }
                            else
                            {
                                if (bufferArray is null) // all of B/C/D require a buffer
                                {
                                    bufferArray = ArrayPool<byte>.Shared.Rent(_outputBufferSize);
                                    capacity = bufferArray.Length;
                                }
                                if (inboundLength <= capacity)
                                {
                                    // scenario B
                                    Debug.Assert(buffered + capacity == bufferArray.Length, "tracking mismatch!");
                                    inbound.Span.CopyTo(new Span<byte>(bufferArray, buffered, inboundLength));
                                    capacity -= inboundLength;
                                    buffered += inboundLength;
                                    //_logger.Debug((capacity, buffered), static (state, _) => $"scenario B; buffered: {state.buffered}, capacity: {state.capacity}");
                                    if (capacity == 0) // all full up
                                    {
                                        _logger.Debug<ReadOnlyMemory<byte>>(bufferArray, static (state, _) => $"(B2) Writing {state.Length} bytes from buffer: {state.ToHex()}");
                                        await WriteAsync(bufferArray, 0, buffered, cancellationToken);
                                        capacity = buffered;
                                        buffered = 0;
                                    }
                                }
                                else
                                {
                                    // scenario C
                                    Debug.Assert(buffered > 0, "we expect a partial buffer");
                                    _logger.Debug("scenario C");
                                    inbound.Slice(start: 0, length: capacity).Span.CopyTo(new Span<byte>(bufferArray, buffered, capacity));
                                    var remaining = inbound.Slice(start: capacity);
                                    buffered += capacity;
                                    Debug.Assert(buffered == bufferArray.Length, "we expect to have filled the buffer");
                                    await WriteAsync(bufferArray, 0, buffered, cancellationToken);
                                    capacity = buffered;
                                    buffered = 0;

                                    if (remaining.Length < capacity)
                                    {
                                        // scenario D
                                        _logger.Debug("scenario D");
                                        remaining.CopyTo(bufferArray);
                                        buffered = remaining.Length;
                                        capacity -= buffered;
                                    }
                                    else
                                    {
                                        // scenario E
                                        await WriteAsync(remaining, cancellationToken);
                                    }
                                }
                            }
                            inbound.Release();
                        }
                    }

                    // no remaining synchronous work

                    // write any remaining buffered data
                    if (needFlush) // note that bufferMem is preserved between writes if not flushing
                    {
                        if (bufferArray is not null) // note that we need to do this even if buffered == 0, if last op was scenario E
                        {
                            if (buffered != 0)
                            {
                                await WriteAsync(bufferArray, 0, buffered, cancellationToken);
                                buffered = 0;
                            }

                            Return(ref bufferArray);
                            capacity = _outputBufferSize;
                        }

                        _logger.Debug("Flushing...");
                        await FlushAsync(cancellationToken);
                    }
                    _logger.Debug("Awaiting more work...");
                }
                while (await messages.WaitToReadAsync(cancellationToken));
                _logger.Debug("Exiting write-loop due to end of data");
            }
            catch (OperationCanceledException oce) when (oce.CancellationToken == cancellationToken)
            {
                _logger.Debug("Exiting write-loop due to cancellation");
            }
            catch (Exception ex)
            {
                _logger.Error(ex);
                throw;
            }
            finally
            {
                Return(ref bufferArray);
            }

            static void Return(ref byte[]? buffer)
            {
                if (buffer is not null)
                {
                    ArrayPool<byte>.Shared.Return(buffer);
                    buffer = null;
                }
            }
        }

        public async Task ReadAllAsync(CancellationToken cancellationToken)
        {
            _readStatus = ReadStatus.Init;
            try
            {
                Memory<byte> readBuffer = default;
                Segment startSegment = null!, endSegment = null!; // this feels odd, but fixing these nulls is the first thing we do, so... meh
                int startIndex = 0, endIndex = 0;
                while (true)
                {
                    if (readBuffer.IsEmpty)
                    {
                        // add a new link to the chain with a new buffer
                        readBuffer = _pool.RentMemory(_inputBufferSize);
                        endSegment = new Segment(endSegment, readBuffer);
                        startSegment ??= endSegment;
                        endIndex = 0;
                    }

                    _readStatus = ReadStatus.ReadAsync;
                    int bytesRead = await ReadAsync(readBuffer, cancellationToken);

                    //_readStatus = ReadStatus.UpdateWriteTime;
                    //UpdateLastReadTime();

                    if (bytesRead <= 0) break;

                    // account for the data we just read
                    _readStatus = ReadStatus.ProcessBuffer;
                    endIndex += bytesRead;
                    readBuffer = readBuffer.Slice(bytesRead);

                    // we may now have a viable payload to parse
                    var ros = new ReadOnlySequence<byte>(startSegment, startIndex, endSegment, endIndex);
                    var handled = ProcessBuffer(ref ros);
                    _logger.Debug(handled, static (state, _) => $"Pased {state} responses from stream");

                    // release any buffers that are no longer in scope, and update startIndex
                    var start = ros.Start;
                    startIndex = start.GetInteger();
                    while (!ReferenceEquals(startSegment, start.GetObject()))
                    {
                        startSegment.Memory.Release();
                        startSegment = (Segment)startSegment.Next!;
                    }
                    _readStatus = ReadStatus.ProcessBufferComplete;
                } // we ran out of inbound data

                // release any unconsumed buffer(s)
                new ReadOnlySequence<byte>(startSegment, startIndex, endSegment, endIndex).Release();

                // if the start and end are at different absolutes, then we have uncomsumed data, i.e. partial frames;
                // that's an unexpected EOF
                if (startSegment.RunningIndex + startIndex != endSegment.RunningIndex + endIndex) ThrowEOF();
                static void ThrowEOF() => throw new EndOfStreamException();

                _readStatus = ReadStatus.RanToCompletion;
            }
            catch (Exception ex)
            {
                _logger.Error(ex);
                _readStatus = ReadStatus.Faulted;
            }
        }

        private int ProcessBuffer(ref ReadOnlySequence<byte> buffer)
        {
            int messageCount = 0;

            while (!buffer.IsEmpty)
            {
                _readStatus = ReadStatus.TryParseResult;
                var reader = new BufferReader(buffer);
                var result = TryParseResult(ResultPool, ref reader, IncludeDetailInExceptions, _server);
                if (result.HasValue)
                {
                    buffer = reader.SliceFromCurrent();

                    messageCount++;
                    _readStatus = ReadStatus.MatchResult;
                    if (MatchResult(in buffer, in result))
                    {
                        // completed synchronously on the reader thread; release any RawResult array buffers
                        result.ReleaseItemsRecursive();
                    }
                }
                else
                {
                    break; // remaining buffer isn't enough; give up
                }
            }
            _readStatus = ReadStatus.ProcessBufferComplete;
            return messageCount;
        }

        private static readonly CommandBytes s_message = "message", s_pmessage = "pmessage";

        [Conditional("VERBOSE")]
        internal void Trace(string message) => _server?.Multiplexer?.Trace(message, ToString());

        private bool MatchResult(in ReadOnlySequence<byte> buffer, in RawResult result)
        {
            // check to see if it could be an out-of-band pubsub message
            var obj = ExecutableResult.Create(in result, _server);
            if (_pubsub && result.Type == ResultType.MultiBulk)
            {
                var muxer = _server.Multiplexer;
                if (muxer is null)
                {
                    obj.Dispose();
                    return true;
                }

                // out of band message does not match to a queued message
                var items = result.GetItems();
                if (items.Length >= 3 && items.GetRef(0).IsEqual(s_message))
                {
                    _readStatus = ReadStatus.PubSubMessage;

                    // special-case the configuration change broadcasts (we don't keep that in the usual pub/sub registry)
                    var configChanged = muxer.ConfigurationChangedChannel;
                    if (configChanged != null && items.GetRef(1).IsEqual(configChanged))
                    {
                        EndPoint? blame = null;
                        try
                        {
                            if (!items.GetRef(2).IsEqual(CommonReplies.wildcard))
                            {
                                blame = Format.TryParseEndPoint(items.GetRef(2).GetString());
                            }
                        }
                        catch { /* no biggie */ }
                        Trace("Configuration changed: " + Format.ToString(blame));
                        _readStatus = ReadStatus.Reconfigure;
                        muxer.ReconfigureIfNeeded(blame, true, "broadcast");
                    }

                    // invoke the handlers
                    var channel = items.GetRef(1).AsRedisChannel(ChannelPrefix, RedisChannel.PatternMode.Literal);
                    Trace("MESSAGE: " + channel);
                    if (!channel.IsNull)
                    {
                        _readStatus = ReadStatus.InvokePubSub;
                        muxer.OnMessage(channel, channel, items.GetRef(2).AsRedisValue());
                    }
                    return true; // AND STOP PROCESSING!
                }
                else if (items.Length >= 4 && items.GetRef(0).IsEqual(s_pmessage))
                {
                    _readStatus = ReadStatus.PubSubPMessage;

                    var channel = items.GetRef(2).AsRedisChannel(ChannelPrefix, RedisChannel.PatternMode.Literal);
                    Trace("PMESSAGE: " + channel);
                    if (!channel.IsNull)
                    {
                        var sub = items.GetRef(1).AsRedisChannel(ChannelPrefix, RedisChannel.PatternMode.Pattern);
                        _readStatus = ReadStatus.InvokePubSub;
                        muxer.OnMessage(sub, channel, items.GetRef(3).AsRedisValue());
                    }
                    return true; // AND STOP PROCESSING!
                }

                // if it didn't look like "[p]message", then we still need to process the pending queue
            }
            Trace("Matching result...");
            Message? msg;
            _readStatus = ReadStatus.DequeueResult;
            lock (_writtenAwaitingResponse)
            {
#if NET5_0_OR_GREATER
                if (!_writtenAwaitingResponse.TryDequeue(out msg))
                {
                    throw new InvalidOperationException("Received response with no message waiting: " + result.ToString());
                }
#else
                if (_writtenAwaitingResponse.Count == 0)
                {
                    throw new InvalidOperationException("Received response with no message waiting: " + result.ToString());
                }
                msg = _writtenAwaitingResponse.Dequeue();
#endif
            }

            Trace("Response to: " + msg);
            _readStatus = ReadStatus.ComputeResult;
            msg.SetBufferAndQueueExecute(this, in buffer, in result);
            _readStatus = ReadStatus.MatchResultComplete;
            return false;
        }

        public void OnTransactionLogImpl(string message) => _parentState.OnTransactionLog(message);

        private readonly Queue<Message> _writtenAwaitingResponse = new Queue<Message>();

        private enum ResponseKind
        {
            None,
            Response,
            PubSubMessage,
            PubSubPMessage,
            PubSubConfigurationChanged,
        }
        private sealed class ExecutableResult : IDisposable
        {
            int _state; // 0 = empty, 1 = value, 2 = disposing
            private RawResult _result;
            private Message? _message;
            private ServerEndPoint? _server;
            private ResponseKind _kind;
            public static ExecutableResult Create(in RawResult result, ServerEndPoint server)
            {
                var obj = new ExecutableResult(); // TODO: reuse
                obj.Transition(0, 1);
                obj._server = server;
                obj._result = result;
                return obj;
            }

            public void Set(ResponseKind kind, Message? message)
            {
                _kind = kind;
                _message = message;
            }

            void Transition(int from, int to)
            {
                if (Interlocked.CompareExchange(ref _state, to, from) != from) Throw();
                static void Throw() => throw new InvalidOperationException();
            }
            public void Dispose()
            {
                Transition(1, 0);
                Release(in _result);
                _result = default;
                _kind = ResponseKind.None;
                _message = null;
                _server = null;
                // TODO: recycle
            }

            static void Release(in RawResult result)
            {
                throw new NotImplementedException("release arrays and segments here");
            }
        }

        private sealed class Segment : ReadOnlySequenceSegment<byte>
        {
            public Segment(Segment? previous, ReadOnlyMemory<byte> value)
            {
                base.Memory = value;
                if (previous is not null)
                {
                    RunningIndex = previous.RunningIndex + previous.Memory.Length;
                    previous.Next = this;
                }
            }
        }
    }

    internal readonly struct WrittenMessage
    {
        public WrittenMessage(ReadOnlySequence<byte> payload, Message message)
        {
            Message = message;
            Payload = payload;
        }

        public ReadOnlySequence<byte> Payload { get; }
        public readonly Message Message { get; }
    }
}
