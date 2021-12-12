using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Pipelines.Sockets.Unofficial.Threading;
using static Pipelines.Sockets.Unofficial.Threading.MutexSlim;
using static StackExchange.Redis.ConnectionMultiplexer;
using PendingSubscriptionState = global::StackExchange.Redis.ConnectionMultiplexer.Subscription.PendingSubscriptionState;

namespace StackExchange.Redis
{
    internal sealed class PhysicalBridge : IDisposable
    {
        internal readonly string Name;

        private const int ProfileLogSamples = 10;

        private const double ProfileLogSeconds = (ConnectionMultiplexer.MillisecondsPerHeartbeat * ProfileLogSamples) / 1000.0;

        private static readonly Message ReusableAskingCommand = Message.Create(-1, CommandFlags.FireAndForget, RedisCommand.ASKING);

        private readonly long[] profileLog = new long[ProfileLogSamples];

        /// <summary>
        /// We have 1 queue in play on this bridge.
        /// We're bypassing the queue for handshake events that go straight to the socket.
        /// Everything else that's not an internal call goes into the queue if there is a queue.
        ///
        /// In a later release we want to remove per-server events from this queue compeltely and shunt queued messages
        /// to another capable primary connection if oone if avaialble to process them faster (order is already hosed).
        /// For now, simplicity in: queue it all, replay or timeout it all.
        /// </summary>
        private readonly ConcurrentQueue<Message> _backlog = new();
        private bool BacklogHasItems => !_backlog.IsEmpty;
        private int _backlogProcessorIsRunning = 0;

        private int activeWriters = 0;
        private int beating;
        private int failConnectCount = 0;
        private volatile bool isDisposed;
        private long nonPreferredEndpointCount;

        //private volatile int missedHeartbeats;
        private long operationCount, socketCount;
        private volatile PhysicalConnection physical;

        private long profileLastLog;
        private int profileLogIndex;

        private volatile bool reportNextFailure = true, reconfigureNextFailure = false;

        private volatile int state = (int)State.Disconnected;

        internal string PhysicalName => physical?.ToString();
        public PhysicalBridge(ServerEndPoint serverEndPoint, ConnectionType type, int timeoutMilliseconds)
        {
            ServerEndPoint = serverEndPoint;
            ConnectionType = type;
            Multiplexer = serverEndPoint.Multiplexer;
            Name = Format.ToString(serverEndPoint.EndPoint) + "/" + ConnectionType.ToString();
            TimeoutMilliseconds = timeoutMilliseconds;
            _singleWriterMutex = new MutexSlim(timeoutMilliseconds: timeoutMilliseconds);
        }

        private readonly int TimeoutMilliseconds;

        public enum State : byte
        {
            Connecting,
            ConnectedEstablishing,
            ConnectedEstablished,
            Disconnected
        }

        public Exception LastException { get; private set; }

        public ConnectionType ConnectionType { get; }

        public bool IsConnected => state == (int)State.ConnectedEstablished;

        public bool IsConnecting => state == (int)State.ConnectedEstablishing || state == (int)State.Connecting;

        public ConnectionMultiplexer Multiplexer { get; }

        public ServerEndPoint ServerEndPoint { get; }

        public long SubscriptionCount
        {
            get
            {
                var tmp = physical;
                return tmp == null ? 0 : physical.SubscriptionCount;
            }
        }

        internal State ConnectionState => (State)state;
        internal bool IsBeating => Interlocked.CompareExchange(ref beating, 0, 0) == 1;

        internal long OperationCount => Interlocked.Read(ref operationCount);

        public RedisCommand LastCommand { get; private set; }

        public void Dispose()
        {
            isDisposed = true;
            ShutdownSubscriptionQueue();
            using (var tmp = physical)
            {
                physical = null;
            }
            GC.SuppressFinalize(this);
        }
        ~PhysicalBridge()
        {
            isDisposed = true; // make damn sure we don't true to resurrect

            // shouldn't *really* touch managed objects
            // in a finalizer, but we need to kill that socket,
            // and this is the first place that isn't going to
            // be rooted by the socket async bits
            try
            {
                var tmp = physical;
                physical = null;
                tmp?.Shutdown();
            }
            catch { }
        }
        public void ReportNextFailure()
        {
            reportNextFailure = true;
        }

        public override string ToString() => ConnectionType + "/" + Format.ToString(ServerEndPoint.EndPoint);

        private WriteResult QueueOrFailMessage(Message message)
        {
            if (message.IsInternalCall && message.Command != RedisCommand.QUIT)
            {
                // you can go in the queue, but we won't be starting
                // a worker, because the handshake has not completed
                message.SetEnqueued(null);
                message.SetBacklogState(_backlog.Count, null);
                _backlog.Enqueue(message);
                return WriteResult.Success; // we'll take it...
            }
            else if (Multiplexer.RawConfig.BacklogPolicy.QueueWhileDisconnected)
            {
                message.SetEnqueued(null);
                message.SetBacklogState(_backlog.Count, null);
                _backlog.Enqueue(message);
                return WriteResult.Success; // we'll queue for retry here...
            }
            else
            {
                // sorry, we're just not ready for you yet;
                message.Cancel();
                Multiplexer?.OnMessageFaulted(message, null);
                message.Complete();
                return WriteResult.NoConnectionAvailable;
            }
        }

        private WriteResult FailDueToNoConnection(Message message)
        {
            message.Cancel();
            Multiplexer?.OnMessageFaulted(message, null);
            message.Complete();
            return WriteResult.NoConnectionAvailable;
        }

        [Obsolete("prefer async")]
        public WriteResult TryWriteSync(Message message, bool isReplica)
        {
            if (isDisposed) throw new ObjectDisposedException(Name);
            if (!IsConnected) return QueueOrFailMessage(message);

            var physical = this.physical;
            if (physical == null) return FailDueToNoConnection(message);
            if (physical == null)
            {
                // If we're not connected yet and supposed to, queue it up
                if (Multiplexer.RawConfig.BacklogPolicy.QueueWhileDisconnected)
                {
                    if (TryPushToBacklog(message, onlyIfExists: false))
                    {
                        message.SetEnqueued(null);
                        return WriteResult.Success;
                    }
                }
                return FailDueToNoConnection(message);
            }
            var result = WriteMessageTakingWriteLockSync(physical, message);
            LogNonPreferred(message.Flags, isReplica);
            return result;
        }

        public ValueTask<WriteResult> TryWriteAsync(Message message, bool isReplica, bool isHandshake = false)
        {
            if (isDisposed) throw new ObjectDisposedException(Name);
            if (!IsConnected) return new ValueTask<WriteResult>(QueueOrFailMessage(message));

            var physical = this.physical;
            if (physical == null)
            {
                // If we're not connected yet and supposed to, queue it up
                if (!isHandshake && Multiplexer.RawConfig.BacklogPolicy.QueueWhileDisconnected)
                {
                    if (TryPushToBacklog(message, onlyIfExists: false))
                    {
                        message.SetEnqueued(null);
                        return new ValueTask<WriteResult>(WriteResult.Success);
                    }
                }
                return new ValueTask<WriteResult>(FailDueToNoConnection(message));
            }

            var result = WriteMessageTakingWriteLockAsync(physical, message, isHandshake);
            LogNonPreferred(message.Flags, isReplica);
            return result;
        }

        internal void AppendProfile(StringBuilder sb)
        {
            var clone = new long[ProfileLogSamples + 1];
            for (int i = 0; i < ProfileLogSamples; i++)
            {
                clone[i] = Interlocked.Read(ref profileLog[i]);
            }
            clone[ProfileLogSamples] = Interlocked.Read(ref operationCount);
            Array.Sort(clone);
            sb.Append(' ').Append(clone[0]);
            for (int i = 1; i < clone.Length; i++)
            {
                if (clone[i] != clone[i - 1])
                {
                    sb.Append('+').Append(clone[i] - clone[i - 1]);
                }
            }
            if (clone[0] != clone[ProfileLogSamples])
            {
                sb.Append('=').Append(clone[ProfileLogSamples]);
            }
            double rate = (clone[ProfileLogSamples] - clone[0]) / ProfileLogSeconds;
            sb.Append(" (").Append(rate.ToString("N2")).Append(" ops/s; spans ").Append(ProfileLogSeconds).Append("s)");
        }

        internal void GetCounters(ConnectionCounters counters)
        {
            counters.OperationCount = OperationCount;
            counters.SocketCount = Interlocked.Read(ref socketCount);
            counters.WriterCount = Interlocked.CompareExchange(ref activeWriters, 0, 0);
            counters.NonPreferredEndpointCount = Interlocked.Read(ref nonPreferredEndpointCount);
            physical?.GetCounters(counters);
        }

        private Channel<PendingSubscriptionState> _subscriptionBackgroundQueue;
        private static readonly UnboundedChannelOptions s_subscriptionQueueOptions = new UnboundedChannelOptions
        {
             AllowSynchronousContinuations = false, // we do *not* want the async work to end up on the caller's thread
             SingleReader = true, // only one reader will be started per channel
             SingleWriter = true, // writes will be synchronized, because order matters
        };

        private Channel<PendingSubscriptionState> GetSubscriptionQueue()
        {
            var queue = _subscriptionBackgroundQueue;
            if (queue == null)
            {
                queue = Channel.CreateUnbounded<PendingSubscriptionState>(s_subscriptionQueueOptions);
                var existing = Interlocked.CompareExchange(ref _subscriptionBackgroundQueue, queue, null);

                if (existing != null) return existing; // we didn't win, but that's fine 

                // we won (_subqueue is now queue)
                // this means we have a new channel without a reader; let's fix that!
                Task.Run(() => ExecuteSubscriptionLoop());
            }
            return queue;
        }

        private void ShutdownSubscriptionQueue()
        {
            try
            {
                Interlocked.CompareExchange(ref _subscriptionBackgroundQueue, null, null)?.Writer.TryComplete();
            }
            catch { }
        }

        private async Task ExecuteSubscriptionLoop() // pushes items that have been enqueued over the bridge
        {
            // note: this will execute on the default pool rather than our dedicated pool; I'm... OK with this
            var queue = _subscriptionBackgroundQueue ?? Interlocked.CompareExchange(ref _subscriptionBackgroundQueue, null, null); // just to be sure we can read it!
            try
            {
                while (await queue.Reader.WaitToReadAsync().ForAwait() && queue.Reader.TryRead(out var next))
                {
                    try
                    {
                        if ((await TryWriteAsync(next.Message, next.IsReplica).ForAwait()) != WriteResult.Success)
                        {
                            next.Abort();
                        }
                    }
                    catch (Exception ex)
                    {
                        next.Fail(ex);
                    }
                }
            }
            catch (Exception ex)
            {
                Multiplexer.OnInternalError(ex, ServerEndPoint?.EndPoint, ConnectionType);
            }
        }

        internal bool TryEnqueueBackgroundSubscriptionWrite(in PendingSubscriptionState state)
            => !isDisposed && (_subscriptionBackgroundQueue ?? GetSubscriptionQueue()).Writer.TryWrite(state);

        internal readonly struct BridgeStatus
        {
            /// <summary>
            /// Number of messages sent since the last heartbeat was processed.
            /// </summary>
            public int MessagesSinceLastHeartbeat { get; init; }
            /// <summary>
            /// Whether the pipe writer is currently active.
            /// </summary>
            public bool IsWriterActive { get; init; }

            /// <summary>
            /// The number of messages that are in the backlog queue (waiting to be sent when the connection is healthy again).
            /// </summary>
            public int BacklogMessagesPending { get; init; }
            /// <summary>
            /// Status of the currently processing backlog, if any.
            /// </summary>
            public BacklogStatus BacklogStatus { get; init; }

            /// <summary>
            /// Status for the underlying <see cref="PhysicalConnection"/>.
            /// </summary>
            public PhysicalConnection.ConnectionStatus Connection { get; init; }

            /// <summary>
            /// The default bridge stats, notable *not* the same as <code>default</code> since initializers don't run.
            /// </summary>
            public static BridgeStatus Zero { get; } = new() { Connection = PhysicalConnection.ConnectionStatus.Zero };
        }

        internal BridgeStatus GetStatus() => new()
        {
            MessagesSinceLastHeartbeat = (int)(Interlocked.Read(ref operationCount) - Interlocked.Read(ref profileLastLog)),
            IsWriterActive = !_singleWriterMutex.IsAvailable,
            BacklogMessagesPending = _backlog.Count,
            BacklogStatus = _backlogStatus,
            Connection = physical?.GetStatus() ?? PhysicalConnection.ConnectionStatus.Default,
        };

        internal string GetStormLog()
        {
            var sb = new StringBuilder("Storm log for ").Append(Format.ToString(ServerEndPoint.EndPoint)).Append(" / ").Append(ConnectionType)
                .Append(" at ").Append(DateTime.UtcNow)
                .AppendLine().AppendLine();
            physical?.GetStormLog(sb);
            sb.Append("Circular op-count snapshot:");
            AppendProfile(sb);
            sb.AppendLine();
            return sb.ToString();
        }

        internal void IncrementOpCount()
        {
            Interlocked.Increment(ref operationCount);
        }

        internal void KeepAlive()
        {
            if (!(physical?.IsIdle() ?? false)) return; // don't pile on if already doing something

            var commandMap = Multiplexer.CommandMap;
            Message msg = null;
            var features = ServerEndPoint.GetFeatures();
            switch (ConnectionType)
            {
                case ConnectionType.Interactive:
                    msg = ServerEndPoint.GetTracerMessage(false);
                    msg.SetSource(ResultProcessor.Tracer, null);
                    break;
                case ConnectionType.Subscription:
                    if (commandMap.IsAvailable(RedisCommand.PING) && features.PingOnSubscriber)
                    {
                        msg = Message.Create(-1, CommandFlags.FireAndForget, RedisCommand.PING);
                        msg.SetSource(ResultProcessor.Tracer, null);
                    }
                    else if (commandMap.IsAvailable(RedisCommand.UNSUBSCRIBE))
                    {
                        msg = Message.Create(-1, CommandFlags.FireAndForget, RedisCommand.UNSUBSCRIBE,
                            (RedisChannel)Multiplexer.UniqueId);
                        msg.SetSource(ResultProcessor.TrackSubscriptions, null);
                    }
                    break;
            }

            if (msg != null)
            {
                msg.SetInternalCall();
                Multiplexer.Trace("Enqueue: " + msg);
                Multiplexer.OnInfoMessage($"heartbeat ({physical?.LastWriteSecondsAgo}s >= {ServerEndPoint?.WriteEverySeconds}s, {physical?.GetSentAwaitingResponseCount()} waiting) '{msg.CommandAndKey}' on '{PhysicalName}' (v{features.Version})");
                physical?.UpdateLastWriteTime(); // pre-emptively
#pragma warning disable CS0618
                var result = TryWriteSync(msg, ServerEndPoint.IsReplica);
#pragma warning restore CS0618

                if (result != WriteResult.Success)
                {
                    var ex = Multiplexer.GetException(result, msg, ServerEndPoint);
                    OnInternalError(ex);
                }
            }
        }

        internal async Task OnConnectedAsync(PhysicalConnection connection, LogProxy log)
        {
            Trace("OnConnected");
            if (physical == connection && !isDisposed && ChangeState(State.Connecting, State.ConnectedEstablishing))
            {
                await ServerEndPoint.OnEstablishingAsync(connection, log).ForAwait();
                log?.WriteLine($"{Format.ToString(ServerEndPoint)}: OnEstablishingAsync complete");
            }
            else
            {
                try
                {
                    connection.Dispose();
                }
                catch { }
            }
        }

        internal void ResetNonConnected()
        {
            var tmp = physical;
            if (tmp != null && state != (int)State.ConnectedEstablished)
            {
                tmp.RecordConnectionFailed(ConnectionFailureType.UnableToConnect);
            }
            TryConnect(null);
        }

        internal void OnConnectionFailed(PhysicalConnection connection, ConnectionFailureType failureType, Exception innerException)
        {
            Trace($"OnConnectionFailed: {connection}");
            // If we're configured to, fail all pending backlogged messages
            if (Multiplexer.RawConfig.BacklogPolicy?.AbortPendingOnConnectionFailure == true)
            {
                AbandonPendingBacklog(innerException);
            }

            if (reportNextFailure)
            {
                LastException = innerException;
                reportNextFailure = false; // until it is restored
                var endpoint = ServerEndPoint.EndPoint;
                Multiplexer.OnConnectionFailed(endpoint, ConnectionType, failureType, innerException, reconfigureNextFailure, connection?.ToString());
            }
        }

        internal void OnDisconnected(ConnectionFailureType failureType, PhysicalConnection connection, out bool isCurrent, out State oldState)
        {
            Trace($"OnDisconnected: {failureType}");

            oldState = default(State); // only defined when isCurrent = true
            if (isCurrent = (physical == connection))
            {
                Trace("Bridge noting disconnect from active connection" + (isDisposed ? " (disposed)" : ""));
                oldState = ChangeState(State.Disconnected);
                physical = null;
                if (oldState == State.ConnectedEstablished && !ServerEndPoint.IsReplica)
                {
                    // if the disconnected endpoint was a master endpoint run info replication
                    // more frequently on it's replica with exponential increments
                    foreach (var r in ServerEndPoint.Replicas)
                    {
                        r.ForceExponentialBackoffReplicationCheck();
                    }
                }
                ServerEndPoint.OnDisconnected(this);

                if (!isDisposed && Interlocked.Increment(ref failConnectCount) == 1)
                {
                    TryConnect(null); // try to connect immediately
                }
            }
            else if (physical == null)
            {
                Trace("Bridge noting disconnect (already terminated)");
            }
            else
            {
                Trace("Bridge noting disconnect, but from different connection");
            }
        }

        private void AbandonPendingBacklog(Exception ex)
        {
            while (_backlog.TryDequeue(out Message next))
            {
                Multiplexer?.OnMessageFaulted(next, ex);
                next.SetExceptionAndComplete(ex, this);
            }
        }

        internal void OnFullyEstablished(PhysicalConnection connection, string source)
        {
            Trace("OnFullyEstablished");
            connection?.SetIdle();
            if (physical == connection && !isDisposed && ChangeState(State.ConnectedEstablishing, State.ConnectedEstablished))
            {
                reportNextFailure = reconfigureNextFailure = true;
                LastException = null;
                Interlocked.Exchange(ref failConnectCount, 0);
                ServerEndPoint.OnFullyEstablished(connection, source);

                // do we have pending system things to do?
                if (BacklogHasItems) StartBacklogProcessor();

                if (ConnectionType == ConnectionType.Interactive) ServerEndPoint.CheckInfoReplication();
            }
            else
            {
                try { connection.Dispose(); } catch { }
            }
        }

        private int connectStartTicks;
        private long connectTimeoutRetryCount = 0;

        internal void OnHeartbeat(bool ifConnectedOnly)
        {
            bool runThisTime = false;
            try
            {
                if (BacklogHasItems)
                {
                    CheckBacklogsForTimeouts();
                    // Ensure we're processing the backlog
                    if (BacklogHasItems)
                    {
                        StartBacklogProcessor();
                    }
                }

                runThisTime = !isDisposed && Interlocked.CompareExchange(ref beating, 1, 0) == 0;
                if (!runThisTime) return;

                uint index = (uint)Interlocked.Increment(ref profileLogIndex);
                long newSampleCount = Interlocked.Read(ref operationCount);
                Interlocked.Exchange(ref profileLog[index % ProfileLogSamples], newSampleCount);
                Interlocked.Exchange(ref profileLastLog, newSampleCount);
                Trace("OnHeartbeat: " + (State)state);
                switch (state)
                {
                    case (int)State.Connecting:
                        int connectTimeMilliseconds = unchecked(Environment.TickCount - Thread.VolatileRead(ref connectStartTicks));
                        bool shouldRetry = Multiplexer.RawConfig.ReconnectRetryPolicy.ShouldRetry(Interlocked.Read(ref connectTimeoutRetryCount), connectTimeMilliseconds);
                        if (shouldRetry)
                        {
                            Interlocked.Increment(ref connectTimeoutRetryCount);
                            LastException = ExceptionFactory.UnableToConnect(Multiplexer, "ConnectTimeout");
                            Trace("Aborting connect");
                            // abort and reconnect
                            var snapshot = physical;
                            OnDisconnected(ConnectionFailureType.UnableToConnect, snapshot, out bool isCurrent, out State oldState);
                            using (snapshot) { } // dispose etc
                            TryConnect(null);
                        }
                        break;
                    case (int)State.ConnectedEstablishing:
                    case (int)State.ConnectedEstablished:
                        var tmp = physical;
                        if (tmp != null)
                        {
                            if (state == (int)State.ConnectedEstablished)
                            {
                                Interlocked.Exchange(ref connectTimeoutRetryCount, 0);
                                tmp.BridgeCouldBeNull?.ServerEndPoint?.ClearUnselectable(UnselectableFlags.DidNotRespond);
                            }
                            tmp.OnBridgeHeartbeat();
                            int writeEverySeconds = ServerEndPoint.WriteEverySeconds,
                                checkConfigSeconds = ServerEndPoint.ConfigCheckSeconds;

                            if (state == (int)State.ConnectedEstablished && ConnectionType == ConnectionType.Interactive
                                && checkConfigSeconds > 0 && ServerEndPoint.LastInfoReplicationCheckSecondsAgo >= checkConfigSeconds
                                && ServerEndPoint.CheckInfoReplication())
                            {
                                // that serves as a keep-alive, if it is accepted
                            }
                            else if (writeEverySeconds > 0 && tmp.LastWriteSecondsAgo >= writeEverySeconds)
                            {
                                Trace("OnHeartbeat - overdue");
                                if (state == (int)State.ConnectedEstablished)
                                {
                                    KeepAlive();
                                }
                                else
                                {
                                    OnDisconnected(ConnectionFailureType.SocketFailure, tmp, out bool ignore, out State oldState);
                                }
                            }
                            else if (writeEverySeconds <= 0 && tmp.IsIdle()
                                && tmp.LastWriteSecondsAgo > 2
                                && tmp.GetSentAwaitingResponseCount() != 0)
                            {
                                // there's a chance this is a dead socket; sending data will shake that
                                // up a bit, so if we have an empty unsent queue and a non-empty sent
                                // queue, test the socket
                                KeepAlive();
                            }
                        }
                        break;
                    case (int)State.Disconnected:
                        Interlocked.Exchange(ref connectTimeoutRetryCount, 0);
                        if (!ifConnectedOnly)
                        {
                            Multiplexer.Trace("Resurrecting " + ToString());
                            Multiplexer.OnResurrecting(ServerEndPoint?.EndPoint, ConnectionType);
                            TryConnect(null);
                        }
                        break;
                    default:
                        Interlocked.Exchange(ref connectTimeoutRetryCount, 0);
                        break;
                }
            }
            catch (Exception ex)
            {
                OnInternalError(ex);
                Trace("OnHeartbeat error: " + ex.Message);
            }
            finally
            {
                if (runThisTime) Interlocked.Exchange(ref beating, 0);
            }
        }

        internal void RemovePhysical(PhysicalConnection connection)
        {
            Interlocked.CompareExchange(ref physical, null, connection);
        }

        [Conditional("VERBOSE")]
        internal void Trace(string message)
        {
            Multiplexer.Trace(message, ToString());
        }

        [Conditional("VERBOSE")]
        internal void Trace(bool condition, string message)
        {
            if (condition) Multiplexer.Trace(message, ToString());
        }

        internal bool TryEnqueue(List<Message> messages, bool isReplica)
        {
            if (messages == null || messages.Count == 0) return true;

            if (isDisposed) throw new ObjectDisposedException(Name);

            if (!IsConnected)
            {
                return false;
            }

            var physical = this.physical;
            if (physical == null) return false;
            foreach (var message in messages)
            {   // deliberately not taking a single lock here; we don't care if
                // other threads manage to interleave - in fact, it would be desirable
                // (to avoid a batch monopolising the connection)
#pragma warning disable CS0618
                WriteMessageTakingWriteLockSync(physical, message);
#pragma warning restore CS0618
                LogNonPreferred(message.Flags, isReplica);
            }
            return true;
        }

        private readonly MutexSlim _singleWriterMutex;

        private Message _activeMessage;

        private WriteResult WriteMessageInsideLock(PhysicalConnection physical, Message message)
        {
            WriteResult result;
            var existingMessage = Interlocked.CompareExchange(ref _activeMessage, message, null);
            if (existingMessage != null)
            {
                Multiplexer?.OnInfoMessage($"reentrant call to WriteMessageTakingWriteLock for {message.CommandAndKey}, {existingMessage.CommandAndKey} is still active");
                return WriteResult.NoConnectionAvailable;
            }
#if DEBUG
            int startWriteTime = Environment.TickCount;
            try
#endif
            {
                physical.SetWriting();
                var messageIsSent = false;
                if (message is IMultiMessage multiMessage)
                {
                    SelectDatabaseInsideWriteLock(physical, message); // need to switch database *before* the transaction
                    foreach (var subCommand in multiMessage.GetMessages(physical))
                    {
                        result = WriteMessageToServerInsideWriteLock(physical, subCommand);
                        if (result != WriteResult.Success)
                        {
                            // we screwed up; abort; note that WriteMessageToServer already
                            // killed the underlying connection
                            Trace("Unable to write to server");
                            message.Fail(ConnectionFailureType.ProtocolFailure, null, "failure before write: " + result.ToString());
                            message.Complete();
                            return result;
                        }
                        //The parent message (next) may be returned from GetMessages
                        //and should not be marked as sent again below
                        messageIsSent = messageIsSent || subCommand == message;
                    }
                    if (!messageIsSent)
                    {
                        message.SetRequestSent(); // well, it was attempted, at least...
                    }

                    return WriteResult.Success;
                }
                else
                {
                    return WriteMessageToServerInsideWriteLock(physical, message);
                }
            }
#if DEBUG
            finally
            {
                int endWriteTime = Environment.TickCount;
                int writeDuration = unchecked(endWriteTime - startWriteTime);
                if (writeDuration > _maxWriteTime)
                {
                    _maxWriteTime = writeDuration;
                    _maxWriteCommand = message?.Command ?? default;
                }
            }
#endif
        }
#if DEBUG
        private volatile int _maxWriteTime = -1;
        private RedisCommand _maxWriteCommand;
#endif

        [Obsolete("prefer async")]
        internal WriteResult WriteMessageTakingWriteLockSync(PhysicalConnection physical, Message message)
        {
            Trace("Writing: " + message);
            message.SetEnqueued(physical); // this also records the read/write stats at this point

            // AVOID REORDERING MESSAGES
            // Prefer to add it to the backlog if this thread can see that there might already be a message backlog.
            // We do this before attempting to take the writelock, because we won't actually write, we'll just let the backlog get processed in due course
            if (TryPushToBacklog(message, onlyIfExists: true))
            {
                return WriteResult.Success; // queued counts as success
            }

            LockToken token = default;
            try
            {
                token = _singleWriterMutex.TryWait(WaitOptions.NoDelay);
                if (!token.Success)
                {
                    // we can't get it *instantaneously*; is there
                    // perhaps a backlog and active backlog processor?
                    if (TryPushToBacklog(message, onlyIfExists: true)) return WriteResult.Success; // queued counts as success

                    // no backlog... try to wait with the timeout;
                    // if we *still* can't get it: that counts as
                    // an actual timeout
                    token = _singleWriterMutex.TryWait();
                    if (!token.Success) return TimedOutBeforeWrite(message);
                }

                var result = WriteMessageInsideLock(physical, message);

                if (result == WriteResult.Success)
                {
                    result = physical.FlushSync(false, TimeoutMilliseconds);
                }

                physical.SetIdle();
                return result;
            }
            catch (Exception ex) { return HandleWriteException(message, ex); }
            finally
            {
                UnmarkActiveMessage(message);
                token.Dispose();
            }

        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private bool TryPushToBacklog(Message message, bool onlyIfExists, bool isHandshake = false)
        {
            // In the handshake case: send the command directly through.
            // If we're disconnected *in the middle of a handshake*, we've bombed a brand new socket and failing,
            // backing off, and retrying next heartbeat is best anyway.
            // 
            // Internal calls also shouldn't queue - try immediately. If these aren't errors (most aren't), we
            // won't alert the user.
            if (isHandshake || message.IsInternalCall)
            {
                return false;
            }

            // Note, for deciding emptyness for whether to push onlyIfExists, and start worker, 
            // we only need care if WE are able to 
            // see the queue when its empty. Not whether anyone else sees it as empty.
            // So strong synchronization is not required.
            if (_backlog.IsEmpty & onlyIfExists) return false;

            int count = _backlog.Count;
            message.SetBacklogState(count, physical);
            _backlog.Enqueue(message);

            // The correct way to decide to start backlog process is not based on previously empty
            // but based on a) not empty now (we enqueued!) and b) no backlog processor already running.
            // Which StartBacklogProcessor will check.
            StartBacklogProcessor();
            return true;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void StartBacklogProcessor()
        {
            if (Interlocked.CompareExchange(ref _backlogProcessorIsRunning, 1, 0) == 0)
            {
#if DEBUG
                _backlogProcessorRequestedTime = Environment.TickCount;
#endif
                _backlogStatus = BacklogStatus.Activating;

                // Start the backlog processor; this is a bit unorthadox, as you would *expect* this to just
                // be Task.Run; that would work fine when healthy, but when we're falling on our face, it is
                // easy to get into a thread-pool-starvation "spiral of death" if we rely on the thread-pool
                // to unblock the thread-pool when there could be sync-over-async callers. Note that in reality,
                // the initial "enough" of the back-log processor is typically sync, which means that the thread
                // we start is actually useful, despite thinking "but that will just go async and back to the pool"
                var thread = new Thread(s => ((PhysicalBridge)s).ProcessBacklogsAsync().RedisFireAndForget())
                {
                    IsBackground = true,                  // don't keep process alive (also: act like the thread-pool used to)
                    Name = "StackExchange.Redis Backlog", // help anyone looking at thread-dumps
                };
                thread.Start(this);
            }
        }
#if DEBUG
        private volatile int _backlogProcessorRequestedTime;
#endif

        /// <summary>
        /// Crawls from the head of the backlog queue, consuming anything that should have timed out
        /// and pruning it accoordingly (these messages will get timeout exceptions).
        /// </summary>
        private void CheckBacklogsForTimeouts()
        {
            var now = Environment.TickCount;
            var timeout = TimeoutMilliseconds;

            // Because peeking at the backlog, checking message and then dequeueing, is not thread-safe, we do have to use
            // a lock here, for mutual exclusion of backlog DEQUEUERS. Unfortunately.
            // But we reduce contention by only locking if we see something that looks timed out.
            while (_backlog.TryPeek(out Message message))
            {
                // See if the message has pass our async timeout threshold
                // or has otherwise been completed (e.g. a sync wait timed out) which would have cleared the ResultBox
                if (message.HasAsyncTimedOut(now, timeout, out var _) || message.ResultBox == null) break; // not a timeout - we can stop looking
                lock (_backlog)
                {
                    // Peek again since we didn't have lock before...
                    // and rerun the exact same checks as above, note that it may be a different message now
                    if (!_backlog.TryPeek(out message)) break;
                    if (!message.HasAsyncTimedOut(now, timeout, out var _) && message.ResultBox != null) break;

                    if (!_backlog.TryDequeue(out var message2) || (message != message2)) // consume it for real
                    {
                        throw new RedisException("Thread safety bug detected! A queue message disappeared while we had the backlog lock");
                    }
                }

                // Tell the message it has failed
                // Note: Attempting to *avoid* reentrancy/deadlock issues by not holding the lock while completing messages.
                var ex = Multiplexer.GetException(WriteResult.TimeoutBeforeWrite, message, ServerEndPoint);
                message.SetExceptionAndComplete(ex, this);
            }
        }

        internal enum BacklogStatus : byte
        {
            Inactive,
            Activating,
            Starting,
            Started,
            CheckingForWork,
            CheckingForTimeout,
            RecordingTimeout,
            WritingMessage,
            Flushing,
            MarkingInactive,
            RecordingWriteFailure,
            RecordingFault,
            SettingIdle,
            Faulted,
        }

        private volatile BacklogStatus _backlogStatus;
        /// <summary>
        /// Process the backlog(s) in play if any.
        /// This means flushing commands to an available/active connection (if any) or spinning until timeout if not.
        /// </summary>
        private async Task ProcessBacklogsAsync()
        {
            _backlogStatus = BacklogStatus.Starting;
            try
            {
                if (!_backlog.IsEmpty)
                {
                    // TODO: vNext handoff this backlog to another primary ("can handle everything") connection
                    // and remove any per-server commands. This means we need to track a bit of whether something
                    // was server-endpoint-specific in PrepareToPushMessageToBridge (was the server ref null or not)
                    await ProcessBridgeBacklogAsync(_backlog); // Needs handoff
                }
            }
            catch
            {
                _backlogStatus = BacklogStatus.Faulted;
            }
            finally
            {
                // Do this in finally block, so that thread aborts can't convince us the backlog processor is running forever
                if (Interlocked.CompareExchange(ref _backlogProcessorIsRunning, 0, 1) != 1)
                {
                    throw new RedisException("Bug detection, couldn't indicate shutdown of backlog processor");
                }

                // Now that nobody is processing the backlog, we should consider starting a new backlog processor
                // in case a new message came in after we ended this loop.
                if (BacklogHasItems)
                {
                    // Check for faults mainly to prevent unlimited tasks spawning in a fault scenario
                    // This won't cause a StackOverflowException due to the Task.Run() handoff
                    if (_backlogStatus != BacklogStatus.Faulted)
                    {
                        StartBacklogProcessor();
                    }
                }
            }
        }

        private async Task ProcessBridgeBacklogAsync(ConcurrentQueue<Message> backlog)
        {
            // Importantly: don't assume we have a physical connection here
            // We are very likely to hit a state where it's not re-established or even referenced here
            LockToken token = default;
            try
            {
#if DEBUG
                int tryToAcquireTime = Environment.TickCount;
                var msToStartWorker = unchecked(tryToAcquireTime - _backlogProcessorRequestedTime);
                int failureCount = 0;
#endif
                _backlogStatus = BacklogStatus.Starting;

                while (true)
                {
                    // check whether the backlog is empty *before* even trying to get the lock
                    if (backlog.IsEmpty) return; // nothing to do

                    // try and get the lock; if unsuccessful, retry
                    token = await _singleWriterMutex.TryWaitAsync().ConfigureAwait(false);
                    if (token.Success) break; // got the lock; now go do something with it
#if DEBUG
                    failureCount++;
#endif
                }
                _backlogStatus = BacklogStatus.Started;

#if DEBUG
                int acquiredTime = Environment.TickCount;
                var msToGetLock = unchecked(acquiredTime - tryToAcquireTime);
#endif

                // so now we are the writer; write some things!
                Message message;
                var timeout = TimeoutMilliseconds;
                while (true)
                {
                    _backlogStatus = BacklogStatus.CheckingForWork;
                    // We need to lock _backlog when dequeueing because of 
                    // races with timeout processing logic
                    lock (backlog)
                    {
                        if (!backlog.TryDequeue(out message)) break; // all done
                    }

                    try
                    {
                        _backlogStatus = BacklogStatus.CheckingForTimeout;
                        if (message.HasAsyncTimedOut(Environment.TickCount, timeout, out var _))
                        {
                            _backlogStatus = BacklogStatus.RecordingTimeout;
                            var ex = Multiplexer.GetException(WriteResult.TimeoutBeforeWrite, message, ServerEndPoint);
#if DEBUG // additional tracking
                            ex.Data["Redis-BacklogStartDelay"] = msToStartWorker;
                            ex.Data["Redis-BacklogGetLockDelay"] = msToGetLock;
                            if (failureCount != 0) ex.Data["Redis-BacklogFailCount"] = failureCount;
                            if (_maxWriteTime >= 0) ex.Data["Redis-MaxWrite"] = _maxWriteTime.ToString() + "ms, " + _maxWriteCommand.ToString();
                            var maxFlush = physical?.MaxFlushTime ?? -1;
                            if (maxFlush >= 0) ex.Data["Redis-MaxFlush"] = maxFlush.ToString() + "ms, " + (physical?.MaxFlushBytes ?? -1).ToString();
                            if (_maxLockDuration >= 0) ex.Data["Redis-MaxLockDuration"] = _maxLockDuration;
#endif
                            message.SetExceptionAndComplete(ex, this);
                        }
                        else if (physical?.HasOutputPipe == true)
                        {
                            _backlogStatus = BacklogStatus.WritingMessage;
                            var result = WriteMessageInsideLock(physical, message);

                            if (result == WriteResult.Success)
                            {
                                _backlogStatus = BacklogStatus.Flushing;
                                result = await physical.FlushAsync(false).ConfigureAwait(false);
                            }

                            _backlogStatus = BacklogStatus.MarkingInactive;
                            if (result != WriteResult.Success)
                            {
                                _backlogStatus = BacklogStatus.RecordingWriteFailure;
                                var ex = Multiplexer.GetException(result, message, ServerEndPoint);
                                HandleWriteException(message, ex);
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        _backlogStatus = BacklogStatus.RecordingFault;
                        HandleWriteException(message, ex);
                    }
                    finally
                    {
                        UnmarkActiveMessage(message);
                    }
                }
                _backlogStatus = BacklogStatus.SettingIdle;
                physical?.SetIdle();
                _backlogStatus = BacklogStatus.Inactive;
            }
            finally
            {
                token.Dispose();
            }
        }

        private WriteResult TimedOutBeforeWrite(Message message)
        {
            message.Cancel();
            Multiplexer?.OnMessageFaulted(message, null);
            message.Complete();
            return WriteResult.TimeoutBeforeWrite;
        }

        /// <summary>
        /// This writes a message to the output stream
        /// </summary>
        /// <param name="physical">The phsyical connection to write to.</param>
        /// <param name="message">The message to be written.</param>
        /// <param name="isHandshake">Whether this message is part of the handshake process.</param>
        internal ValueTask<WriteResult> WriteMessageTakingWriteLockAsync(PhysicalConnection physical, Message message, bool isHandshake = false)
        {
            /* design decision/choice; the code works fine either way, but if this is
             * set to *true*, then when we can't take the writer-lock *right away*,
             * we push the message to the backlog (starting a worker if needed)
             *
             * otherwise, we go for a TryWaitAsync and rely on the await machinery
             *
             * "true" seems to give faster times *when under heavy contention*, based on profiling
             * but it involves the backlog concept; "false" works well under low contention, and
             * makes more use of async
             */
            const bool ALWAYS_USE_BACKLOG_IF_CANNOT_GET_SYNC_LOCK = true;

            Trace("Writing: " + message);
            message.SetEnqueued(physical); // this also records the read/write stats at this point

            // AVOID REORDERING MESSAGES
            // Prefer to add it to the backlog if this thread can see that there might already be a message backlog.
            // We do this before attempting to take the writelock, because we won't actually write, we'll just let the backlog get processed in due course
            if (TryPushToBacklog(message, onlyIfExists: physical.HasOutputPipe, isHandshake: isHandshake))
            {
                return new ValueTask<WriteResult>(WriteResult.Success); // queued counts as success
            }

            bool releaseLock = true; // fine to default to true, as it doesn't matter until token is a "success"
            int lockTaken = 0;
            LockToken token = default;
            try
            {

                // try to acquire it synchronously
                // note: timeout is specified in mutex-constructor
                token = _singleWriterMutex.TryWait(options: WaitOptions.NoDelay);
                if (!token.Success)
                {
                    // we can't get it *instantaneously*; is there
                    // perhaps a backlog and active backlog processor?
                    if (TryPushToBacklog(message, onlyIfExists: !ALWAYS_USE_BACKLOG_IF_CANNOT_GET_SYNC_LOCK, isHandshake: isHandshake))
                        return new ValueTask<WriteResult>(WriteResult.Success); // queued counts as success

                    // no backlog... try to wait with the timeout;
                    // if we *still* can't get it: that counts as
                    // an actual timeout
                    var pending = _singleWriterMutex.TryWaitAsync(options: WaitOptions.DisableAsyncContext);
                    if (!pending.IsCompletedSuccessfully) return WriteMessageTakingWriteLockAsync_Awaited(pending, physical, message);

                    token = pending.Result; // fine since we know we got a result
                    if (!token.Success) return new ValueTask<WriteResult>(TimedOutBeforeWrite(message));
                }
                lockTaken = Environment.TickCount;

                var result = WriteMessageInsideLock(physical, message);

                if (result == WriteResult.Success)
                {
                    var flush = physical.FlushAsync(false);
                    if (!flush.IsCompletedSuccessfully)
                    {
                        releaseLock = false; // so we don't release prematurely
                        return CompleteWriteAndReleaseLockAsync(token, flush, message, lockTaken);
                    }

                    result = flush.Result; // we know it was completed, this is fine
                }
                
                physical.SetIdle();

                return new ValueTask<WriteResult>(result);
            }
            catch (Exception ex) { return new ValueTask<WriteResult>(HandleWriteException(message, ex)); }
            finally
            {
                if (token.Success)
                {
                    UnmarkActiveMessage(message);

                    if (releaseLock)
                    {
#if DEBUG
                        RecordLockDuration(lockTaken);
#endif
                        token.Dispose();
                    }
                }
            }
        }

#if DEBUG
        private void RecordLockDuration(int lockTaken)
        {

            var lockDuration = unchecked(Environment.TickCount - lockTaken);
            if (lockDuration > _maxLockDuration) _maxLockDuration = lockDuration;
        }
        volatile int _maxLockDuration = -1;
#endif

        private async ValueTask<WriteResult> WriteMessageTakingWriteLockAsync_Awaited(ValueTask<LockToken> pending, PhysicalConnection physical, Message message)
        {
            try
            {
                using (var token = await pending.ForAwait())
                {
                    if (!token.Success) return TimedOutBeforeWrite(message);
#if DEBUG
                    int lockTaken = Environment.TickCount;
#endif
                    var result = WriteMessageInsideLock(physical, message);

                    if (result == WriteResult.Success)
                    {
                        result = await physical.FlushAsync(false).ForAwait();
                    }
                    
                    physical.SetIdle();

#if DEBUG
                    RecordLockDuration(lockTaken);
#endif
                    return result;
                }
            }
            catch (Exception ex)
            {
                return HandleWriteException(message, ex);
            }
            finally
            {
                UnmarkActiveMessage(message);
            }
        }

        private async ValueTask<WriteResult> CompleteWriteAndReleaseLockAsync(LockToken lockToken, ValueTask<WriteResult> flush, Message message, int lockTaken)
        {
            using (lockToken)
            {
                try
                {
                    var result = await flush.ForAwait();
                    physical.SetIdle();
                    return result;
                }
                catch (Exception ex) { return HandleWriteException(message, ex); }
#if DEBUG
                finally { RecordLockDuration(lockTaken); }
#endif
            }
        }

        private WriteResult HandleWriteException(Message message, Exception ex)
        {
            var inner = new RedisConnectionException(ConnectionFailureType.InternalFailure, "Failed to write", ex);
            message.SetExceptionAndComplete(inner, this);
            return WriteResult.WriteFailure;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void UnmarkActiveMessage(Message message)
            => Interlocked.CompareExchange(ref _activeMessage, null, message); // remove if it is us

        private State ChangeState(State newState)
        {
            var oldState = (State)Interlocked.Exchange(ref state, (int)newState);
            if (oldState != newState)
            {
                Multiplexer.Trace(ConnectionType + " state changed from " + oldState + " to " + newState);
            }
            return oldState;
        }

        private bool ChangeState(State oldState, State newState)
        {
            bool result = Interlocked.CompareExchange(ref state, (int)newState, (int)oldState) == (int)oldState;
            if (result)
            {
                Multiplexer.Trace(ConnectionType + " state changed from " + oldState + " to " + newState);
            }
            return result;
        }

        public PhysicalConnection TryConnect(LogProxy log)
        {
            if (state == (int)State.Disconnected)
            {
                try
                {
                    if (!Multiplexer.IsDisposed)
                    {
                        log?.WriteLine($"{Name}: Connecting...");
                        Multiplexer.Trace("Connecting...", Name);
                        if (ChangeState(State.Disconnected, State.Connecting))
                        {
                            Interlocked.Increment(ref socketCount);
                            Interlocked.Exchange(ref connectStartTicks, Environment.TickCount);
                            // separate creation and connection for case when connection completes synchronously
                            // in that case PhysicalConnection will call back to PhysicalBridge, and most PhysicalBridge methods assume that physical is not null;
                            physical = new PhysicalConnection(this);

                            physical.BeginConnectAsync(log).RedisFireAndForget();
                        }
                    }
                    return null;
                }
                catch (Exception ex)
                {
                    log?.WriteLine($"{Name}: Connect failed: {ex.Message}");
                    Multiplexer.Trace("Connect failed: " + ex.Message, Name);
                    ChangeState(State.Disconnected);
                    OnInternalError(ex);
                    throw;
                }
            }
            return physical;
        }

        private void LogNonPreferred(CommandFlags flags, bool isReplica)
        {
            if ((flags & Message.InternalCallFlag) == 0) // don't log internal-call
            {
                if (isReplica)
                {
                    if (Message.GetMasterReplicaFlags(flags) == CommandFlags.PreferMaster)
                        Interlocked.Increment(ref nonPreferredEndpointCount);
                }
                else
                {
                    if (Message.GetMasterReplicaFlags(flags) == CommandFlags.PreferReplica)
                        Interlocked.Increment(ref nonPreferredEndpointCount);
                }
            }
        }

        private void OnInternalError(Exception exception, [CallerMemberName] string origin = null)
        {
            Multiplexer.OnInternalError(exception, ServerEndPoint.EndPoint, ConnectionType, origin);
        }

        private void SelectDatabaseInsideWriteLock(PhysicalConnection connection, Message message)
        {
            int db = message.Db;
            if (db >= 0)
            {
                var sel = connection.GetSelectDatabaseCommand(db, message);
                if (sel != null)
                {
                    connection.EnqueueInsideWriteLock(sel);
                    sel.WriteTo(connection);
                    sel.SetRequestSent();
                    IncrementOpCount();
                }
            }
        }

        private WriteResult WriteMessageToServerInsideWriteLock(PhysicalConnection connection, Message message)
        {
            if (message == null) return WriteResult.Success; // for some definition of success

            bool isQueued = false;
            try
            {
                var cmd = message.Command;
                LastCommand = cmd;
                bool isMasterOnly = message.IsMasterOnly();

                if (isMasterOnly && ServerEndPoint.IsReplica && (ServerEndPoint.ReplicaReadOnly || !ServerEndPoint.AllowReplicaWrites))
                {
                    throw ExceptionFactory.MasterOnly(Multiplexer.IncludeDetailInExceptions, message.Command, message, ServerEndPoint);
                }
                switch(cmd)
                {
                    case RedisCommand.QUIT:
                        connection.RecordQuit();
                        break;
                    case RedisCommand.EXEC:
                        Multiplexer.OnPreTransactionExec(message); // testing purposes, to force certain errors
                        break;
                }

                SelectDatabaseInsideWriteLock(connection, message);

                if (!connection.TransactionActive)
                {
                    // If we are executing AUTH, it means we are still unauthenticated
                    // Setting READONLY before AUTH always fails but we think it succeeded since
                    // we run it as Fire and Forget. 
                    if (cmd != RedisCommand.AUTH)
                    {
                        var readmode = connection.GetReadModeCommand(isMasterOnly);
                        if (readmode != null)
                        {
                            connection.EnqueueInsideWriteLock(readmode);
                            readmode.WriteTo(connection);
                            readmode.SetRequestSent();
                            IncrementOpCount();
                        }
                    }
                    if (message.IsAsking)
                    {
                        var asking = ReusableAskingCommand;
                        connection.EnqueueInsideWriteLock(asking);
                        asking.WriteTo(connection);
                        asking.SetRequestSent();
                        IncrementOpCount();
                    }
                }
                switch (cmd)
                {
                    case RedisCommand.WATCH:
                    case RedisCommand.MULTI:
                        connection.TransactionActive = true;
                        break;
                    case RedisCommand.UNWATCH:
                    case RedisCommand.EXEC:
                    case RedisCommand.DISCARD:
                        connection.TransactionActive = false;
                        break;
                }

                connection.EnqueueInsideWriteLock(message);
                isQueued = true;
                message.WriteTo(connection);

                message.SetRequestSent();
                IncrementOpCount();

                // some commands smash our ability to trust the database; some commands
                // demand an immediate flush
                switch (cmd)
                {
                    case RedisCommand.EVAL:
                    case RedisCommand.EVALSHA:
                        if (!ServerEndPoint.GetFeatures().ScriptingDatabaseSafe)
                        {
                            connection.SetUnknownDatabase();
                        }
                        break;
                    case RedisCommand.UNKNOWN:
                    case RedisCommand.DISCARD:
                    case RedisCommand.EXEC:
                        connection.SetUnknownDatabase();
                        break;
                }
                return WriteResult.Success;
            }
            catch (RedisCommandException ex) when (!isQueued)
            {
                Trace("Write failed: " + ex.Message);
                message.Fail(ConnectionFailureType.InternalFailure, ex, null);
                message.Complete();
                // this failed without actually writing; we're OK with that... unless there's a transaction

                if (connection?.TransactionActive == true)
                {
                    // we left it in a broken state; need to kill the connection
                    connection.RecordConnectionFailed(ConnectionFailureType.ProtocolFailure, ex);
                    return WriteResult.WriteFailure;
                }
                return WriteResult.Success;
            }
            catch (Exception ex)
            {
                Trace("Write failed: " + ex.Message);
                message.Fail(ConnectionFailureType.InternalFailure, ex, null);
                message.Complete();

                // we're not sure *what* happened here; probably an IOException; kill the connection
                connection?.RecordConnectionFailed(ConnectionFailureType.InternalFailure, ex);
                return WriteResult.WriteFailure;
            }
        }

        /// <summary>
        /// For testing only
        /// </summary>
        internal void SimulateConnectionFailure(SimulatedFailureType failureType)
        {
            if (!Multiplexer.RawConfig.AllowAdmin)
            {
                throw ExceptionFactory.AdminModeNotEnabled(Multiplexer.IncludeDetailInExceptions, RedisCommand.DEBUG, null, ServerEndPoint); // close enough
            }
            physical?.SimulateConnectionFailure(failureType);
        }

        internal RedisCommand? GetActiveMessage() => Volatile.Read(ref _activeMessage)?.Command;
    }
}
