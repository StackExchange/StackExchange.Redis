using System;
using System.Buffers;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using System.Threading.Tasks.Sources;
using Microsoft.Extensions.Logging;
using static StackExchange.Redis.PhysicalConnection;

#nullable enable
namespace StackExchange.Redis.Transports
{
    internal abstract class Transport : ITransportState, IDisposable, IValueTaskSource, IBridge
    {
        sealed class DefaultState : ITransportState
        {
            private DefaultState() { }
            private static DefaultState? s_instance;
            public static DefaultState Instance => s_instance ?? (s_instance = new DefaultState());

            CommandMap ITransportState.CommandMap => CommandMap.Default;

            byte[]? ITransportState.ChannelPrefix => null;

            ServerEndPoint? ITransportState.ServerEndPoint => null;

            void ITransportState.OnTransactionLogImpl(string message) { }
            void ITransportState.TraceImpl(string message) { }
            void ITransportState.RecordConnectionFailed(ConnectionFailureType failureType, Exception? exception) { }
            ConnectionType ITransportState.ConnectionType => ConnectionType.None;
        }

        private readonly ITransportState _parentState;
        private readonly int _inputBufferSize, _outputBufferSize;
        private volatile ReadStatus _readStatus;
        private readonly ILogger? _logger;
        private readonly RefCountedMemoryPool<byte> _pool;
        private readonly ServerEndPoint _server;

        public void Dispose() => Abort();

        public ReadStatus ReadStatus => _readStatus;

        bool IncludeDetailInExceptions => _server?.Multiplexer?.IncludeDetailInExceptions ?? false;


        public Transport(int inputBufferSize, int outputBufferSize, ILogger? logger, RefCountedMemoryPool<byte>? pool, ITransportState? parentState, ServerEndPoint server, ConnectionType connectionType)
        {
            _server = server!;
            _inputBufferSize = inputBufferSize;
            _outputBufferSize = outputBufferSize;
            _logger = logger;
            _pool = pool ?? RefCountedMemoryPool<byte>.Shared;
            _readStatus = ReadStatus.NotStarted;
            ConnectionType = connectionType;
            _parentState = parentState ?? DefaultState.Instance;
            _cancellationToken = _cancellation.Token;

            if (server is null) ThrowNull(nameof(server));
            if (inputBufferSize <= 0) ThrowBufferSize(nameof(inputBufferSize));
            if (outputBufferSize <= 0) ThrowBufferSize(nameof(outputBufferSize));

            // start the actual IO loops
            _ = Task.Run(WriteAllAsync);
            _ = Task.Run(ReadAllAsync);

            static void ThrowBufferSize(string parameterName) => throw new ArgumentOutOfRangeException(parameterName);
            static void ThrowNull(string parameterName) => throw new ArgumentNullException(parameterName);
        }

        ManualResetValueTaskSourceCore<bool> _pendingWork;

        ValueTaskSourceStatus IValueTaskSource.GetStatus(short token) => _pendingWork.GetStatus(token);
        void IValueTaskSource.GetResult(short token)
        {
            lock (_queue)
            {
                _pendingWork.GetResult(token);
                _pendingWork.Reset();
            }
        }

        void IValueTaskSource.OnCompleted(Action<object?> continuation, object? state, short token, ValueTaskSourceOnCompletedFlags flags)
            => _pendingWork.OnCompleted(continuation, state, token, flags);

        private static RefCountedMemoryPool<RawResult> ResultPool => RefCountedMemoryPool<RawResult>.Shared;

        CommandMap ITransportState.CommandMap => _parentState.CommandMap;
        ServerEndPoint ITransportState.ServerEndPoint => _server;
        public ConnectionType ConnectionType { get; }

        void ITransportState.RecordConnectionFailed(ConnectionFailureType failureType, Exception? exception)
        {
            // more here?
            _parentState.RecordConnectionFailed(failureType, exception);
        }

        public byte[]? ChannelPrefix => _parentState.ChannelPrefix;

        public long SubscriptionCount { get; set; }

        protected abstract ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken);
        protected abstract ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken);

        protected virtual ValueTask WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
            => WriteAsync(new ReadOnlyMemory<byte>(buffer, offset, count), cancellationToken);

        protected virtual ValueTask FlushAsync(CancellationToken cancellationToken) => default;

        private readonly Queue<WrittenMessage> _queue = new Queue<WrittenMessage>();

        private readonly CancellationTokenSource _cancellation = new CancellationTokenSource();
        private readonly CancellationToken _cancellationToken;
        private bool _writerNeedsActivation;
        WriteResult IBridge.Write(Message message)
        {
            if (_cancellation.IsCancellationRequested)
            {
                _logger.Debug(message, static (state, _) => "Writing to aborted transport: " + state.CommandAndKey);
                return WriteResult.NoConnectionAvailable;
            }

            WrittenMessage payload;
            try
            {
                payload = MessageWriter.Write(message, this);
            }
            catch (Exception ex)
            {
                _logger.Error(ex);
                return WriteResult.WriteFailure;
            }
            lock (_queue)
            {
                _queue.Enqueue(payload);
                if (_writerNeedsActivation)
                {
                    _writerNeedsActivation = false;
                    ThreadPool.QueueUserWorkItem(s_ActivateWriter, this);
                }
                return WriteResult.Success;
            }
        }

        static readonly WaitCallback s_ActivateWriter = static state => Unsafe.As<Transport>(state)!._pendingWork.SetResult(true);

        void ThrowIfCancellationRequested() => _cancellationToken.ThrowIfCancellationRequested();

        ValueTask NextMessageAvailableAsync()
        {
            lock (_queue)
            {
                if (_queue.Count != 0) return default;
                _writerNeedsActivation = true;
                return new ValueTask(this, _pendingWork.Version);
            }
        }
        private async Task WriteAllAsync()
        {
            byte[]? bufferArray = null;
            try
            {
                int buffered = 0, capacity = _outputBufferSize;

                while (!_cancellation.IsCancellationRequested) // any time we get here: we have the conch
                {
                    bool needFlush = false;
                    while (true)
                    {
                        WrittenMessage pair;
                        lock (_queue) // try to read synchronously
                        {
#if NETCOREAPP3_1_OR_GREATER
                            if (!_queue.TryDequeue(out pair)) break;
#else
                            if (_queue.Count == 0) break;
                            pair = _queue.Dequeue();
#endif
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
                                await WriteAsync(inbound, _cancellationToken);
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
                                        await WriteAsync(bufferArray, 0, buffered, _cancellationToken);
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
                                    await WriteAsync(bufferArray, 0, buffered, _cancellationToken);
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
                                        await WriteAsync(remaining, _cancellationToken);
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
                                await WriteAsync(bufferArray, 0, buffered, _cancellationToken);
                                buffered = 0;
                            }

                            Return(ref bufferArray);
                            capacity = _outputBufferSize;
                        }

                        _logger.Debug("Flushing...");
                        await FlushAsync(_cancellationToken);
                    }

                    _logger.Debug("Awaiting more work...");
                    await NextMessageAvailableAsync(); // self-activation via IVTS
                }
                _logger.Debug("Exiting write-loop cleanly due to cancellation");
            }
            catch (OperationCanceledException oce) when (oce.CancellationToken == _cancellationToken)
            {
                _logger.Debug("Exiting write-loop via fault due to cancellation");
            }
            catch (Exception ex)
            {
                // unexected error
                _logger.Error(ex);
            }
            finally
            {
                Return(ref bufferArray);
                Abort();
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

        void Abort()
        {
            // prevent anything new coming in
            try
            {
                if (!_cancellation.IsCancellationRequested)
                {
                    _cancellation.Cancel(false);
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex);
            }

            // clean up anything that already came in
            try
            {
                while (true)
                {
                    WrittenMessage pair;
                    lock (_queue)
                    {
                        if (_queue.Count == 0) break;
                        pair = _queue.Dequeue();
                    }
                    try { pair.Payload.Release(); }
                    catch (Exception ex)
                    {
                        _logger.Error(ex);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex);
            }

            lock (_queue)
            {
                if (_writerNeedsActivation)
                {   // awaken the writer, to allow clean exit
                    _writerNeedsActivation = false;
                    ThreadPool.QueueUserWorkItem(s_ActivateWriter, this);
                }
            }
        }

        private async Task ReadAllAsync()
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
                    int bytesRead = await ReadAsync(readBuffer, _cancellationToken);

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
                Abort();
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
            if (ConnectionType == ConnectionType.Subscription && result.Type == ResultType.MultiBulk)
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

        [Obsolete] // if OnTransactionLogImpl is being called, we can forward it directly
        void ITransportState.OnTransactionLogImpl(string message) => _parentState.OnTransactionLogImpl(message);

        [Obsolete] // if TraceImpl is being called, we can forward it directly
        void ITransportState.TraceImpl(string message) => _parentState.TraceImpl(message);

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
