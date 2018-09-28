using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace StackExchange.Redis
{
    internal sealed class PhysicalBridge : IDisposable
    {
        internal readonly string Name;

        private const int ProfileLogSamples = 10;

        private const double ProfileLogSeconds = (ConnectionMultiplexer.MillisecondsPerHeartbeat * ProfileLogSamples) / 1000.0;

        private static readonly Message ReusableAskingCommand = Message.Create(-1, CommandFlags.FireAndForget, RedisCommand.ASKING);

        internal readonly CompletionManager completionManager;
        private readonly long[] profileLog = new long[ProfileLogSamples];

        private readonly Queue<Message> _preconnectBacklog = new Queue<Message>();

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
            completionManager = new CompletionManager(Multiplexer, Name);
            TimeoutMilliseconds = timeoutMilliseconds;
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

        public void TryConnect(TextWriter log) => GetConnection(log);

        public WriteResult TryWrite(Message message, bool isSlave)
        {
            if (isDisposed) throw new ObjectDisposedException(Name);
            if (!IsConnected)
            {
                if (message.IsInternalCall && message.Command != RedisCommand.QUIT)
                {
                    // you can go in the queue, but we won't be starting
                    // a worker, because the handshake has not completed
                    var queue = _preconnectBacklog;
                    lock (queue)
                    {
                        queue.Enqueue(message);
                    }
                    message.SetEnqueued();
                    return WriteResult.Success; // we'll take it...
                }
                else
                {
                    // sorry, we're just not ready for you yet;
                    message.Cancel();
                    Multiplexer?.OnMessageFaulted(message, null);
                    this.CompleteSyncOrAsync(message);
                    return WriteResult.NoConnectionAvailable;
                }
            }

            var physical = this.physical;
            if (physical == null)
            {
                message.Cancel();
                Multiplexer?.OnMessageFaulted(message, null);
                this.CompleteSyncOrAsync(message);
                return WriteResult.NoConnectionAvailable;
            }

            var result = WriteMessageTakingWriteLock(physical, message);
            LogNonPreferred(message.Flags, isSlave);

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
            sb.Append(" ").Append(clone[0]);
            for (int i = 1; i < clone.Length; i++)
            {
                if (clone[i] != clone[i - 1])
                {
                    sb.Append("+").Append(clone[i] - clone[i - 1]);
                }
            }
            if (clone[0] != clone[ProfileLogSamples])
            {
                sb.Append("=").Append(clone[ProfileLogSamples]);
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
            completionManager.GetCounters(counters);
            physical?.GetCounters(counters);
        }

        internal void GetOutstandingCount(out int inst, out int qs, out int @in)
        {// defined as: PendingUnsentItems + SentItemsAwaitingResponse + ResponsesAwaitingAsyncCompletion
            inst = (int)(Interlocked.Read(ref operationCount) - Interlocked.Read(ref profileLastLog));
            var tmp = physical;
            if (tmp == null)
            {
                qs = 0;
                @in = -1;
            }
            else
            {
                qs = tmp.GetSentAwaitingResponseCount();
                @in = tmp.GetAvailableInboundBytes();
            }
        }

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
                var result = TryWrite(msg, ServerEndPoint.IsSlave);

                if (result != WriteResult.Success)
                {
                    var ex = Multiplexer.GetException(result, msg, ServerEndPoint);
                    OnInternalError(ex);
                }
            }
        }

        internal async Task OnConnectedAsync(PhysicalConnection connection, TextWriter log)
        {
            Trace("OnConnected");
            if (physical == connection && !isDisposed && ChangeState(State.Connecting, State.ConnectedEstablishing))
            {
                await ServerEndPoint.OnEstablishingAsync(connection, log).ForAwait();
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
            GetConnection(null);
        }

        internal void OnConnectionFailed(PhysicalConnection connection, ConnectionFailureType failureType, Exception innerException)
        {
            Trace($"OnConnectionFailed: {connection}");
            AbandonPendingBacklog(innerException);
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

                if (!isDisposed && Interlocked.Increment(ref failConnectCount) == 1)
                {
                    GetConnection(null); // try to connect immediately
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

        private Message DequeueNextPendingBacklog()
        {
            lock (_preconnectBacklog)
            {
                return _preconnectBacklog.Count == 0 ? null : _preconnectBacklog.Dequeue();
            }
        }
        private void WritePendingBacklog(PhysicalConnection connection)
        {
            if (connection != null)
            {
                Message next;
                do
                {
                    next = DequeueNextPendingBacklog();
                    if (next != null) WriteMessageTakingWriteLock(connection, next);
                } while (next != null);
            }
        }
        private void AbandonPendingBacklog(Exception ex)
        {
            Message next;
            do
            {
                next = DequeueNextPendingBacklog();
                if (next != null)
                {
                    Multiplexer?.OnMessageFaulted(next, ex);
                    next.SetExceptionAndComplete(ex, this);
                }
            } while (next != null);
        }
        internal void OnFullyEstablished(PhysicalConnection connection)
        {
            Trace("OnFullyEstablished");
            connection?.SetIdle();
            if (physical == connection && !isDisposed && ChangeState(State.ConnectedEstablishing, State.ConnectedEstablished))
            {
                reportNextFailure = reconfigureNextFailure = true;
                LastException = null;
                Interlocked.Exchange(ref failConnectCount, 0);
                ServerEndPoint.OnFullyEstablished(connection);
                WritePendingBacklog(connection);

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
                                checkConfigSeconds = Multiplexer.RawConfig.ConfigCheckSeconds;

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
                            GetConnection(null);
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
#pragma warning disable 0420
            Interlocked.CompareExchange(ref physical, null, connection);
#pragma warning restore 0420
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

        internal bool TryEnqueue(List<Message> messages, bool isSlave)
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
                WriteMessageTakingWriteLock(physical, message);
                LogNonPreferred(message.Flags, isSlave);
            }
            return true;
        }

        private readonly object SingleWriterLock = new object();

        private Message _activeMesssage;
        /// <summary>
        /// This writes a message to the output stream
        /// </summary>
        /// <param name="physical">The phsyical connection to write to.</param>
        /// <param name="message">The message to be written.</param>
        internal WriteResult WriteMessageTakingWriteLock(PhysicalConnection physical, Message message)
        {
            Trace("Writing: " + message);
            message.SetEnqueued();

            WriteResult result;
            bool haveLock = false;
            try
            {
                Monitor.TryEnter(SingleWriterLock, TimeoutMilliseconds, ref haveLock);
                if (!haveLock)
                {
                    message.Cancel();
                    Multiplexer?.OnMessageFaulted(message, null);
                    this.CompleteSyncOrAsync(message);
                    return WriteResult.TimeoutBeforeWrite;
                }

                var existingMessage = Interlocked.CompareExchange(ref _activeMesssage, message, null);
                if (existingMessage != null)
                {
                    Multiplexer?.OnInfoMessage($"reentrant call to WriteMessageTakingWriteLock for {message.CommandAndKey}, {existingMessage.CommandAndKey} is still active");
                    return WriteResult.NoConnectionAvailable;
                }
                physical.SetWriting();
                var messageIsSent = false;
                if (message is IMultiMessage)
                {
                    SelectDatabaseInsideWriteLock(physical, message); // need to switch database *before* the transaction
                    foreach (var subCommand in ((IMultiMessage)message).GetMessages(physical))
                    {
                        result = WriteMessageToServerInsideWriteLock(physical, subCommand);
                        if (result != WriteResult.Success)
                        {
                            // we screwed up; abort; note that WriteMessageToServer already
                            // killed the underlying connection
                            Trace("Unable to write to server");
                            message.Fail(ConnectionFailureType.ProtocolFailure, null, "failure before write: " + result.ToString());
                            this.CompleteSyncOrAsync(message);
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

                    result = WriteResult.Success;
                }
                else
                {
                    result = WriteMessageToServerInsideWriteLock(physical, message);
                }

                if (result == WriteResult.Success)
                {
                    result = physical.FlushSync();
                }
                physical.SetIdle();
            }
            catch (Exception ex)
            {
                var inner = new RedisConnectionException(ConnectionFailureType.InternalFailure, "Failed to write", ex);
                message.SetExceptionAndComplete(inner, this);
                result = WriteResult.WriteFailure;
            }
            finally
            {
                if (haveLock)
                {
                    Interlocked.CompareExchange(ref _activeMesssage, null, message); // remove if it is us
                    Monitor.Exit(SingleWriterLock);
                }
            }

            return result;
        }

        private State ChangeState(State newState)
        {
#pragma warning disable 0420
            var oldState = (State)Interlocked.Exchange(ref state, (int)newState);
#pragma warning restore 0420
            if (oldState != newState)
            {
                Multiplexer.Trace(ConnectionType + " state changed from " + oldState + " to " + newState);
            }
            return oldState;
        }

        private bool ChangeState(State oldState, State newState)
        {
#pragma warning disable 0420
            bool result = Interlocked.CompareExchange(ref state, (int)newState, (int)oldState) == (int)oldState;
#pragma warning restore 0420
            if (result)
            {
                Multiplexer.Trace(ConnectionType + " state changed from " + oldState + " to " + newState);
            }
            return result;
        }

        private PhysicalConnection GetConnection(TextWriter log)
        {
            if (state == (int)State.Disconnected)
            {
                try
                {
                    if (!Multiplexer.IsDisposed)
                    {
                        Multiplexer.LogLocked(log, "Connecting {0}...", Name);
                        Multiplexer.Trace("Connecting...", Name);
                        if (ChangeState(State.Disconnected, State.Connecting))
                        {
                            Interlocked.Increment(ref socketCount);
                            Interlocked.Exchange(ref connectStartTicks, Environment.TickCount);
                            // separate creation and connection for case when connection completes synchronously
                            // in that case PhysicalConnection will call back to PhysicalBridge, and most of  PhysicalBridge methods assumes that physical is not null;
                            physical = new PhysicalConnection(this);

                            physical.BeginConnectAsync(log).RedisFireAndForget();
                        }
                    }
                    return null;
                }
                catch (Exception ex)
                {
                    Multiplexer.LogLocked(log, "Connect {0} failed: {1}", Name, ex.Message);
                    Multiplexer.Trace("Connect failed: " + ex.Message, Name);
                    ChangeState(State.Disconnected);
                    OnInternalError(ex);
                    throw;
                }
            }
            return physical;
        }

        private void LogNonPreferred(CommandFlags flags, bool isSlave)
        {
            if ((flags & Message.InternalCallFlag) == 0) // don't log internal-call
            {
                if (isSlave)
                {
                    if (Message.GetMasterSlaveFlags(flags) == CommandFlags.PreferMaster)
                        Interlocked.Increment(ref nonPreferredEndpointCount);
                }
                else
                {
                    if (Message.GetMasterSlaveFlags(flags) == CommandFlags.PreferSlave)
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

                if (isMasterOnly && ServerEndPoint.IsSlave && (ServerEndPoint.SlaveReadOnly || !ServerEndPoint.AllowSlaveWrites))
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
                    var readmode = connection.GetReadModeCommand(isMasterOnly);
                    if (readmode != null)
                    {
                        connection.EnqueueInsideWriteLock(readmode);
                        readmode.WriteTo(connection);
                        readmode.SetRequestSent();
                        IncrementOpCount();
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
                this.CompleteSyncOrAsync(message);
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
                this.CompleteSyncOrAsync(message);

                // we're not sure *what* happened here; probably an IOException; kill the connection
                connection?.RecordConnectionFailed(ConnectionFailureType.InternalFailure, ex);
                return WriteResult.WriteFailure;
            }
        }

        /// <summary>
        /// For testing only
        /// </summary>
        internal void SimulateConnectionFailure()
        {
            if (!Multiplexer.RawConfig.AllowAdmin)
            {
                throw ExceptionFactory.AdminModeNotEnabled(Multiplexer.IncludeDetailInExceptions, RedisCommand.DEBUG, null, ServerEndPoint); // close enough
            }
            physical?.RecordConnectionFailed(ConnectionFailureType.SocketFailure);
        }

        internal RedisCommand? GetActiveMessage() => Volatile.Read(ref _activeMesssage)?.Command;
    }
}
