using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
#if !NETCOREAPP
using Pipelines.Sockets.Unofficial.Threading;
using static Pipelines.Sockets.Unofficial.Threading.MutexSlim;
#endif

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
        /// </summary>
        /// <remarks>
        /// In a later release we want to remove per-server events from this queue completely and shunt queued messages
        /// to another capable primary connection if one is available to process them faster (order is already hosed).
        /// For now, simplicity in: queue it all, replay or timeout it all.
        /// </remarks>
        private readonly ConcurrentQueue<Message> _backlog = new();
        private bool BacklogHasItems => !_backlog.IsEmpty;
        private int _backlogProcessorIsRunning = 0;
        private int _backlogCurrentEnqueued = 0;
        private long _backlogTotalEnqueued = 0;

        private int activeWriters = 0;
        private int beating;
        private int failConnectCount = 0;
        private volatile bool isDisposed;
        private long nonPreferredEndpointCount;

        //private volatile int missedHeartbeats;
        private long operationCount, socketCount;
        private volatile PhysicalConnection? physical;

        private long profileLastLog;
        private int profileLogIndex;

        private volatile bool reportNextFailure = true, reconfigureNextFailure = false;

        private volatile int state = (int)State.Disconnected;

#if NETCOREAPP
        private readonly SemaphoreSlim _singleWriterMutex = new(1,1);
#else
        private readonly MutexSlim _singleWriterMutex;
#endif

        internal string? PhysicalName => physical?.ToString();

        public PhysicalBridge(ServerEndPoint serverEndPoint, ConnectionType type, int timeoutMilliseconds)
        {
            ServerEndPoint = serverEndPoint;
            ConnectionType = type;
            Multiplexer = serverEndPoint.Multiplexer;
            Name = Format.ToString(serverEndPoint.EndPoint) + "/" + ConnectionType.ToString();
            TimeoutMilliseconds = timeoutMilliseconds;
#if !NETCOREAPP
            _singleWriterMutex = new MutexSlim(timeoutMilliseconds: timeoutMilliseconds);
#endif
        }

        private readonly int TimeoutMilliseconds;

        public enum State : byte
        {
            Connecting,
            ConnectedEstablishing,
            ConnectedEstablished,
            Disconnected
        }

        public Exception? LastException { get; private set; }

        public ConnectionType ConnectionType { get; }

        public bool IsConnected => state == (int)State.ConnectedEstablished;

        public bool IsConnecting => state == (int)State.ConnectedEstablishing || state == (int)State.Connecting;

        public ConnectionMultiplexer Multiplexer { get; }

        public ServerEndPoint ServerEndPoint { get; }

        public long SubscriptionCount => physical?.SubscriptionCount ?? 0;

        internal State ConnectionState => (State)state;
        internal bool IsBeating => Interlocked.CompareExchange(ref beating, 0, 0) == 1;

        internal long OperationCount => Interlocked.Read(ref operationCount);

        public RedisCommand LastCommand { get; private set; }

        public void Dispose()
        {
            isDisposed = true;
            _backlogAutoReset?.Dispose();
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
            // If it's an internal call that's not a QUIT
            // or we're allowed to queue in general, then queue
            if (message.IsInternalCall || Multiplexer.RawConfig.BacklogPolicy.QueueWhileDisconnected)
            {
                // Let's just never ever queue a QUIT message
                if (message.Command != RedisCommand.QUIT)
                {
                    message.SetEnqueued(null);
                    BacklogEnqueue(message);
                    // Note: we don't start a worker on each message here
                    return WriteResult.Success; // Successfully queued, so indicate success
                }
            }

            // Anything else goes in the bin - we're just not ready for you yet
            message.Cancel();
            Multiplexer?.OnMessageFaulted(message, null);
            message.Complete();
            return WriteResult.NoConnectionAvailable;
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

        public ValueTask<WriteResult> TryWriteAsync(Message message, bool isReplica, bool bypassBacklog = false)
        {
            if (isDisposed) throw new ObjectDisposedException(Name);
            if (!IsConnected && !bypassBacklog) return new ValueTask<WriteResult>(QueueOrFailMessage(message));

            var physical = this.physical;
            if (physical == null)
            {
                // If we're not connected yet and supposed to, queue it up
                if (!bypassBacklog && Multiplexer.RawConfig.BacklogPolicy.QueueWhileDisconnected)
                {
                    if (TryPushToBacklog(message, onlyIfExists: false))
                    {
                        message.SetEnqueued(null);
                        return new ValueTask<WriteResult>(WriteResult.Success);
                    }
                }
                return new ValueTask<WriteResult>(FailDueToNoConnection(message));
            }

            var result = WriteMessageTakingWriteLockAsync(physical, message, bypassBacklog: bypassBacklog);
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
            /// Status of the currently processing backlog, if any.
            /// </summary>
            public BacklogStatus BacklogStatus { get; init; }

            /// <summary>
            /// The number of messages that are in the backlog queue (waiting to be sent when the connection is healthy again).
            /// </summary>
            public int BacklogMessagesPending { get; init; }
            /// <summary>
            /// The number of messages that are in the backlog queue (waiting to be sent when the connection is healthy again).
            /// </summary>
            public int BacklogMessagesPendingCounter { get; init; }
            /// <summary>
            /// The number of messages ever added to the backlog queue in the life of this connection.
            /// </summary>
            public long TotalBacklogMessagesQueued { get; init; }

            /// <summary>
            /// Status for the underlying <see cref="PhysicalConnection"/>.
            /// </summary>
            public PhysicalConnection.ConnectionStatus Connection { get; init; }

            /// <summary>
            /// The default bridge stats, notable *not* the same as <c>default</c> since initializers don't run.
            /// </summary>
            public static BridgeStatus Zero { get; } = new() { Connection = PhysicalConnection.ConnectionStatus.Zero };

            public override string ToString() =>
                $"MessagesSinceLastHeartbeat: {MessagesSinceLastHeartbeat}, Writer: {(IsWriterActive ? "Active" : "Inactive")}, BacklogStatus: {BacklogStatus}, BacklogMessagesPending: (Queue: {BacklogMessagesPending}, Counter: {BacklogMessagesPendingCounter}), TotalBacklogMessagesQueued: {TotalBacklogMessagesQueued}, Connection: ({Connection})";
        }

        internal BridgeStatus GetStatus() => new()
        {
            MessagesSinceLastHeartbeat = (int)(Interlocked.Read(ref operationCount) - Interlocked.Read(ref profileLastLog)),
#if NETCOREAPP
            IsWriterActive = _singleWriterMutex.CurrentCount == 0,
#else
            IsWriterActive = !_singleWriterMutex.IsAvailable,
#endif
            BacklogMessagesPending = _backlog.Count,
            BacklogMessagesPendingCounter = Volatile.Read(ref _backlogCurrentEnqueued),
            BacklogStatus = _backlogStatus,
            TotalBacklogMessagesQueued = _backlogTotalEnqueued,
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
            Message? msg = null;
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
                        msg.SetForSubscriptionBridge();
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
                Multiplexer.OnInfoMessage($"heartbeat ({physical?.LastWriteSecondsAgo}s >= {ServerEndPoint.WriteEverySeconds}s, {physical?.GetSentAwaitingResponseCount()} waiting) '{msg.CommandAndKey}' on '{PhysicalName}' (v{features.Version})");
                physical?.UpdateLastWriteTime(); // preemptively
#pragma warning disable CS0618 // Type or member is obsolete
                var result = TryWriteSync(msg, ServerEndPoint.IsReplica);
#pragma warning restore CS0618

                if (result != WriteResult.Success)
                {
                    var ex = Multiplexer.GetException(result, msg, ServerEndPoint);
                    OnInternalError(ex);
                }
            }
        }

        internal async Task OnConnectedAsync(PhysicalConnection connection, LogProxy? log)
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

        internal void OnDisconnected(ConnectionFailureType failureType, PhysicalConnection? connection, out bool isCurrent, out State oldState)
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
                    // if the disconnected endpoint was a primary endpoint run info replication
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
            while (BacklogTryDequeue(out Message? next))
            {
                Multiplexer?.OnMessageFaulted(next, ex);
                next.SetExceptionAndComplete(ex, this);
            }
        }

        internal void OnFullyEstablished(PhysicalConnection connection, string source)
        {
            Trace("OnFullyEstablished");
            connection.SetIdle();
            if (physical == connection && !isDisposed && ChangeState(State.ConnectedEstablishing, State.ConnectedEstablished))
            {
                reportNextFailure = reconfigureNextFailure = true;
                LastException = null;
                Interlocked.Exchange(ref failConnectCount, 0);
                ServerEndPoint.OnFullyEstablished(connection, source);

                // do we have pending system things to do?
                if (BacklogHasItems)
                {
                    StartBacklogProcessor();
                }

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
                    // If we have a backlog, kickoff the processing
                    // This will first timeout any messages that have sat too long and either:
                    // A: Abort if we're still not connected yet (we should be in this path)
                    // or B: Process the backlog and send those messages through the pipe
                    StartBacklogProcessor();
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
                            Multiplexer.OnResurrecting(ServerEndPoint.EndPoint, ConnectionType);
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

        internal void RemovePhysical(PhysicalConnection connection) =>
            Interlocked.CompareExchange(ref physical, null, connection);

        [Conditional("VERBOSE")]
        internal void Trace(string message) => Multiplexer.Trace(message, ToString());

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
            {
                // deliberately not taking a single lock here; we don't care if
                // other threads manage to interleave - in fact, it would be desirable
                // (to avoid a batch monopolising the connection)
#pragma warning disable CS0618 // Type or member is obsolete
                WriteMessageTakingWriteLockSync(physical, message);
#pragma warning restore CS0618
                LogNonPreferred(message.Flags, isReplica);
            }
            return true;
        }

        private Message? _activeMessage;

        private WriteResult WriteMessageInsideLock(PhysicalConnection physical, Message message)
        {
            WriteResult result;
            var existingMessage = Interlocked.CompareExchange(ref _activeMessage, message, null);
            if (existingMessage != null)
            {
                Multiplexer?.OnInfoMessage($"Reentrant call to WriteMessageTakingWriteLock for {message.CommandAndKey}, {existingMessage.CommandAndKey} is still active");
                return WriteResult.NoConnectionAvailable;
            }

            physical.SetWriting();
            if (message is IMultiMessage multiMessage)
            {
                var messageIsSent = false;
                SelectDatabaseInsideWriteLock(physical, message); // need to switch database *before* the transaction
                foreach (var subCommand in multiMessage.GetMessages(physical))
                {
                    result = WriteMessageToServerInsideWriteLock(physical, subCommand);
                    if (result != WriteResult.Success)
                    {
                        // we screwed up; abort; note that WriteMessageToServer already
                        // killed the underlying connection
                        Trace("Unable to write to server");
                        message.Fail(ConnectionFailureType.ProtocolFailure, null, "failure before write: " + result.ToString(), Multiplexer);
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

        [Obsolete("prefer async")]
        internal WriteResult WriteMessageTakingWriteLockSync(PhysicalConnection physical, Message message)
        {
            Trace("Writing: " + message);
            message.SetEnqueued(physical); // this also records the read/write stats at this point

            // AVOID REORDERING MESSAGES
            // Prefer to add it to the backlog if this thread can see that there might already be a message backlog.
            // We do this before attempting to take the write lock, because we won't actually write, we'll just let the backlog get processed in due course
            if (TryPushToBacklog(message, onlyIfExists: true))
            {
                return WriteResult.Success; // queued counts as success
            }

#if NETCOREAPP
            bool gotLock = false;
#else
            LockToken token = default;
#endif
            try
            {
#if NETCOREAPP
                gotLock = _singleWriterMutex.Wait(0);
                if (!gotLock)
#else
                token = _singleWriterMutex.TryWait(WaitOptions.NoDelay);
                if (!token.Success)
#endif
                {
                    // If we can't get it *instantaneously*, pass it to the backlog for throughput
                    if (TryPushToBacklog(message, onlyIfExists: false))
                    {
                        return WriteResult.Success; // queued counts as success
                    }

                    // no backlog... try to wait with the timeout;
                    // if we *still* can't get it: that counts as
                    // an actual timeout
#if NETCOREAPP
                    gotLock = _singleWriterMutex.Wait(TimeoutMilliseconds);
                    if (!gotLock) return TimedOutBeforeWrite(message);
#else
                    token = _singleWriterMutex.TryWait();
                    if (!token.Success) return TimedOutBeforeWrite(message);
#endif
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
#if NETCOREAPP
                if (gotLock)
                {
                    _singleWriterMutex.Release();
                }
#else
                token.Dispose();
#endif
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private bool TryPushToBacklog(Message message, bool onlyIfExists, bool bypassBacklog = false)
        {
            // In the handshake case: send the command directly through.
            // If we're disconnected *in the middle of a handshake*, we've bombed a brand new socket and failing,
            // backing off, and retrying next heartbeat is best anyway.
            // 
            // Internal calls also shouldn't queue - try immediately. If these aren't errors (most aren't), we
            // won't alert the user.
            if (bypassBacklog || message.IsInternalCall)
            {
                return false;
            }

            // Note, for deciding emptiness for whether to push onlyIfExists, and start worker, 
            // we only need care if WE are able to 
            // see the queue when its empty. Not whether anyone else sees it as empty.
            // So strong synchronization is not required.
            if (onlyIfExists && Volatile.Read(ref _backlogCurrentEnqueued) == 0)
            {
                return false;
            }

            BacklogEnqueue(message);

            // The correct way to decide to start backlog process is not based on previously empty
            // but based on a) not empty now (we enqueued!) and b) no backlog processor already running.
            // Which StartBacklogProcessor will check.
            StartBacklogProcessor();
            return true;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void BacklogEnqueue(Message message)
        {
            _backlog.Enqueue(message);
            Interlocked.Increment(ref _backlogTotalEnqueued);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private bool BacklogTryDequeue([NotNullWhen(true)] out Message? message)
        {
            if (_backlog.TryDequeue(out message))
            {
                Interlocked.Decrement(ref _backlogCurrentEnqueued);
                return true;
            }
            return false;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void StartBacklogProcessor()
        {
            if (Interlocked.CompareExchange(ref _backlogProcessorIsRunning, 1, 0) == 0)
            {
                _backlogStatus = BacklogStatus.Activating;

#if NET6_0_OR_GREATER
                // In .NET 6, use the thread pool stall semantics to our advantage and use a lighter-weight Task
                Task.Run(ProcessBacklogAsync);
#else
                // Start the backlog processor; this is a bit unorthodox, as you would *expect* this to just
                // be Task.Run; that would work fine when healthy, but when we're falling on our face, it is
                // easy to get into a thread-pool-starvation "spiral of death" if we rely on the thread-pool
                // to unblock the thread-pool when there could be sync-over-async callers. Note that in reality,
                // the initial "enough" of the back-log processor is typically sync, which means that the thread
                // we start is actually useful, despite thinking "but that will just go async and back to the pool"
                var thread = new Thread(s => ((PhysicalBridge)s!).ProcessBacklogAsync().RedisFireAndForget())
                {
                    IsBackground = true,                  // don't keep process alive (also: act like the thread-pool used to)
                    Name = "StackExchange.Redis Backlog", // help anyone looking at thread-dumps
                };
                thread.Start(this);
#endif
            }
            else
            {
                _backlogAutoReset.Set();
            }
        }

        /// <summary>
        /// Crawls from the head of the backlog queue, consuming anything that should have timed out
        /// and pruning it accordingly (these messages will get timeout exceptions).
        /// </summary>
        private void CheckBacklogForTimeouts()
        {
            var now = Environment.TickCount;
            var timeout = TimeoutMilliseconds;

            // Because peeking at the backlog, checking message and then dequeuing, is not thread-safe, we do have to use
            // a lock here, for mutual exclusion of backlog DEQUEUERS. Unfortunately.
            // But we reduce contention by only locking if we see something that looks timed out.
            while (_backlog.TryPeek(out Message? message))
            {
                // See if the message has pass our async timeout threshold
                // or has otherwise been completed (e.g. a sync wait timed out) which would have cleared the ResultBox
                if (!message.HasTimedOut(now, timeout, out var _) || message.ResultBox == null) break; // not a timeout - we can stop looking
                lock (_backlog)
                {
                    // Peek again since we didn't have lock before...
                    // and rerun the exact same checks as above, note that it may be a different message now
                    if (!_backlog.TryPeek(out message)) break;
                    if (!message.HasTimedOut(now, timeout, out var _) && message.ResultBox != null) break;

                    if (!BacklogTryDequeue(out var message2) || (message != message2)) // consume it for real
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
            SpinningDown,
            CheckingForTimeout,
            CheckingForTimeoutComplete,
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
        private async Task ProcessBacklogAsync()
        {
            _backlogStatus = BacklogStatus.Starting;
            try
            {
                while (true)
                {
                    if (!_backlog.IsEmpty)
                    {
                        // TODO: vNext handoff this backlog to another primary ("can handle everything") connection
                        // and remove any per-server commands. This means we need to track a bit of whether something
                        // was server-endpoint-specific in PrepareToPushMessageToBridge (was the server ref null or not)
                        await ProcessBridgeBacklogAsync().ForAwait();
                    }

                    // The cost of starting a new thread is high, and we can bounce in and out of the backlog a lot.
                    // So instead of just exiting, keep this thread waiting for 5 seconds to see if we got another backlog item.
                    _backlogStatus = BacklogStatus.SpinningDown;
                    // Note this is happening *outside* the lock
                    var gotMore = _backlogAutoReset.WaitOne(5000);
                    if (!gotMore)
                    {
                        break;
                    }
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

        /// <summary>
        /// Reset event for monitoring backlog additions mid-run.
        /// This allows us to keep the thread around for a full flush and prevent "feathering the throttle" trying
        /// to flush it. In short, we don't start and stop so many threads with a bit of linger.
        /// </summary>
        private readonly AutoResetEvent _backlogAutoReset = new AutoResetEvent(false);

        private async Task ProcessBridgeBacklogAsync()
        {
            // Importantly: don't assume we have a physical connection here
            // We are very likely to hit a state where it's not re-established or even referenced here
#if NETCOREAPP
            bool gotLock = false;
#else
            LockToken token = default;
#endif
            _backlogAutoReset.Reset();
            try
            {
                _backlogStatus = BacklogStatus.Starting;

                // First eliminate any messages that have timed out already.
                _backlogStatus = BacklogStatus.CheckingForTimeout;
                CheckBacklogForTimeouts();
                _backlogStatus = BacklogStatus.CheckingForTimeoutComplete;

                // For the rest of the backlog, if we're not connected there's no point - abort out
                while (IsConnected)
                {
                    // check whether the backlog is empty *before* even trying to get the lock
                    if (_backlog.IsEmpty) return; // nothing to do

                    // try and get the lock; if unsuccessful, retry
#if NETCOREAPP
                    gotLock = await _singleWriterMutex.WaitAsync(TimeoutMilliseconds).ForAwait();
                    if (gotLock) break; // got the lock; now go do something with it
#else
                    token = await _singleWriterMutex.TryWaitAsync().ForAwait();
                    if (token.Success) break; // got the lock; now go do something with it
#endif
                }
                _backlogStatus = BacklogStatus.Started;

                // Only execute if we're connected.
                // Timeouts are handled above, so we're exclusively into backlog items eligible to write at this point.
                // If we can't write them, abort and wait for the next heartbeat or activation to try this again.
                while (IsConnected && physical?.HasOutputPipe == true)
                {
                    Message? message;
                    _backlogStatus = BacklogStatus.CheckingForWork;

                    lock (_backlog)
                    {
                        // Note that we're actively taking it off the queue here, not peeking
                        // If there's nothing left in queue, we're done.
                        if (!BacklogTryDequeue(out message))
                        {
                            break;
                        }
                    }

                    try
                    {
                        _backlogStatus = BacklogStatus.WritingMessage;
                        var result = WriteMessageInsideLock(physical, message);

                        if (result == WriteResult.Success)
                        {
                            _backlogStatus = BacklogStatus.Flushing;
                            result = await physical.FlushAsync(false).ForAwait();
                        }

                        _backlogStatus = BacklogStatus.MarkingInactive;
                        if (result != WriteResult.Success)
                        {
                            _backlogStatus = BacklogStatus.RecordingWriteFailure;
                            var ex = Multiplexer.GetException(result, message, ServerEndPoint);
                            HandleWriteException(message, ex);
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
#if NETCOREAPP
                if (gotLock)
                {
                    _singleWriterMutex.Release();
                }
#else
                token.Dispose();
#endif
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
        /// This writes a message to the output stream.
        /// </summary>
        /// <param name="physical">The physical connection to write to.</param>
        /// <param name="message">The message to be written.</param>
        /// <param name="bypassBacklog">Whether this message should bypass the backlog, going straight to the pipe or failing.</param>
        internal ValueTask<WriteResult> WriteMessageTakingWriteLockAsync(PhysicalConnection physical, Message message, bool bypassBacklog = false)
        {
            Trace("Writing: " + message);
            message.SetEnqueued(physical); // this also records the read/write stats at this point

            // AVOID REORDERING MESSAGES
            // Prefer to add it to the backlog if this thread can see that there might already be a message backlog.
            // We do this before attempting to take the write lock, because we won't actually write, we'll just let the backlog get processed in due course
            if (TryPushToBacklog(message, onlyIfExists: true, bypassBacklog: bypassBacklog))
            {
                return new ValueTask<WriteResult>(WriteResult.Success); // queued counts as success
            }

            bool releaseLock = true; // fine to default to true, as it doesn't matter until token is a "success"
#if NETCOREAPP
            bool gotLock = false;
#else
            LockToken token = default;
#endif
            try
            {
                // try to acquire it synchronously
#if NETCOREAPP
                gotLock = _singleWriterMutex.Wait(0);
                if (!gotLock)
#else
                // note: timeout is specified in mutex-constructor
                token = _singleWriterMutex.TryWait(options: WaitOptions.NoDelay);
                if (!token.Success)
#endif
                {
                    // If we can't get it *instantaneously*, pass it to the backlog for throughput
                    if (TryPushToBacklog(message, onlyIfExists: false, bypassBacklog: bypassBacklog))
                    {
                        return new ValueTask<WriteResult>(WriteResult.Success); // queued counts as success
                    }

                    // no backlog... try to wait with the timeout;
                    // if we *still* can't get it: that counts as
                    // an actual timeout
#if NETCOREAPP
                    var pending = _singleWriterMutex.WaitAsync(TimeoutMilliseconds);
                    if (pending.Status != TaskStatus.RanToCompletion) return WriteMessageTakingWriteLockAsync_Awaited(pending, physical, message);

                    gotLock = pending.Result; // fine since we know we got a result
                    if (!gotLock) return new ValueTask<WriteResult>(TimedOutBeforeWrite(message));
#else
                    var pending = _singleWriterMutex.TryWaitAsync(options: WaitOptions.DisableAsyncContext);
                    if (!pending.IsCompletedSuccessfully) return WriteMessageTakingWriteLockAsync_Awaited(pending, physical, message);

                    token = pending.Result; // fine since we know we got a result
                    if (!token.Success) return new ValueTask<WriteResult>(TimedOutBeforeWrite(message));
#endif
                }
                var result = WriteMessageInsideLock(physical, message);
                if (result == WriteResult.Success)
                {
                    var flush = physical.FlushAsync(false);
                    if (!flush.IsCompletedSuccessfully)
                    {
                        releaseLock = false; // so we don't release prematurely
#if NETCOREAPP
                        return CompleteWriteAndReleaseLockAsync(flush, message);
#else
                        return CompleteWriteAndReleaseLockAsync(token, flush, message);
#endif
                    }

                    result = flush.Result; // .Result: we know it was completed, so this is fine
                }

                physical.SetIdle();

                return new ValueTask<WriteResult>(result);
            }
            catch (Exception ex)
            {
                return new ValueTask<WriteResult>(HandleWriteException(message, ex));
            }
            finally
            {
#if NETCOREAPP
                if (gotLock)
#else
                if (token.Success)
#endif
                {
                    UnmarkActiveMessage(message);

                    if (releaseLock)
                    {
#if NETCOREAPP
                        _singleWriterMutex.Release();
#else
                        token.Dispose();
#endif
                    }
                }
            }
        }

        private async ValueTask<WriteResult> WriteMessageTakingWriteLockAsync_Awaited(
#if NETCOREAPP
            Task<bool> pending,
#else
            ValueTask<LockToken> pending,
#endif
            PhysicalConnection physical, Message message)
        {
#if NETCOREAPP
            bool gotLock = false;
#endif

            try
            {
#if NETCOREAPP
                gotLock = await pending.ForAwait();
                if (!gotLock) return TimedOutBeforeWrite(message);
#else
                using var token = await pending.ForAwait();
#endif
                var result = WriteMessageInsideLock(physical, message);

                if (result == WriteResult.Success)
                {
                    result = await physical.FlushAsync(false).ForAwait();
                }

                physical.SetIdle();

                return result;
            }
            catch (Exception ex)
            {
                return HandleWriteException(message, ex);
            }
            finally
            {
                UnmarkActiveMessage(message);
#if NETCOREAPP
                if (gotLock)
                {
                    _singleWriterMutex.Release();
                }
#endif
            }
        }

        private async ValueTask<WriteResult> CompleteWriteAndReleaseLockAsync(
#if !NETCOREAPP
            LockToken lockToken,
#endif
            ValueTask<WriteResult> flush,
            Message message)
        {
#if !NETCOREAPP
            using (lockToken)
#endif
            try
            {
                var result = await flush.ForAwait();
                physical?.SetIdle();
                return result;
            }
            catch (Exception ex)
            {
                return HandleWriteException(message, ex);
            }
            finally
            {
#if NETCOREAPP
                _singleWriterMutex.Release();
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

        public PhysicalConnection? TryConnect(LogProxy? log)
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
                    if (Message.GetPrimaryReplicaFlags(flags) == CommandFlags.PreferMaster)
                        Interlocked.Increment(ref nonPreferredEndpointCount);
                }
                else
                {
                    if (Message.GetPrimaryReplicaFlags(flags) == CommandFlags.PreferReplica)
                        Interlocked.Increment(ref nonPreferredEndpointCount);
                }
            }
        }

        private void OnInternalError(Exception exception, [CallerMemberName] string? origin = null)
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
            if (message == null)
            {
                return WriteResult.Success; // for some definition of success
            }

            bool isQueued = false;
            try
            {
                var cmd = message.Command;
                LastCommand = cmd;
                bool isPrimaryOnly = message.IsPrimaryOnly();

                if (isPrimaryOnly && !ServerEndPoint.SupportsPrimaryWrites)
                {
                    throw ExceptionFactory.PrimaryOnly(Multiplexer.RawConfig.IncludeDetailInExceptions, message.Command, message, ServerEndPoint);
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
                        var readmode = connection.GetReadModeCommand(isPrimaryOnly);
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

                // Some commands smash our ability to trust the database
                // and some commands demand an immediate flush
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
                        if (ServerEndPoint.SupportsDatabases)
                        {
                            connection.SetUnknownDatabase();
                        }
                        break;
                }
                return WriteResult.Success;
            }
            catch (RedisCommandException ex) when (!isQueued)
            {
                Trace("Write failed: " + ex.Message);
                message.Fail(ConnectionFailureType.InternalFailure, ex, null, Multiplexer);
                message.Complete();
                // This failed without actually writing; we're OK with that... unless there's a transaction

                if (connection?.TransactionActive == true)
                {
                    // We left it in a broken state - need to kill the connection
                    connection.RecordConnectionFailed(ConnectionFailureType.ProtocolFailure, ex);
                    return WriteResult.WriteFailure;
                }
                return WriteResult.Success;
            }
            catch (Exception ex)
            {
                Trace("Write failed: " + ex.Message);
                message.Fail(ConnectionFailureType.InternalFailure, ex, null, Multiplexer);
                message.Complete();

                // We're not sure *what* happened here - probably an IOException; kill the connection
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
                throw ExceptionFactory.AdminModeNotEnabled(Multiplexer.RawConfig.IncludeDetailInExceptions, RedisCommand.DEBUG, null, ServerEndPoint); // close enough
            }
            physical?.SimulateConnectionFailure(failureType);
        }

        internal RedisCommand? GetActiveMessage() => Volatile.Read(ref _activeMessage)?.Command;
    }
}
