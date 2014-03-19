using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net;
using System.Text;
using System.Threading;

namespace StackExchange.Redis
{
    sealed partial class PhysicalBridge : IDisposable
    {
        private static readonly Message
           ReusableAskingCommand = Message.Create(-1, CommandFlags.FireAndForget, RedisCommand.ASKING);

        private readonly CompletionManager completionManager;
        private readonly ConnectionType connectionType;
        private readonly ConnectionMultiplexer multiplexer;
        internal readonly string Name;
        private readonly MessageQueue queue = new MessageQueue();
        private readonly ServerEndPoint serverEndPoint;
        int activeWriters = 0;
        internal int inWriteQueue = 0;
        private int beating;
        int failConnectCount = 0;
        volatile bool isDisposed;
        //private volatile int missedHeartbeats;
        private long operationCount, socketCount;
        private volatile PhysicalConnection physical;


        volatile bool reportNextFailure = true, reconfigureNextFailure = false;
        private volatile int state = (int)State.Disconnected;
        public PhysicalBridge(ServerEndPoint serverEndPoint, ConnectionType type)
        {
            this.serverEndPoint = serverEndPoint;
            this.connectionType = type;
            this.multiplexer = serverEndPoint.Multiplexer;
            this.Name = serverEndPoint.EndPoint.ToString();
            this.completionManager = new CompletionManager(multiplexer, Name);
        }

        public enum State : byte
        {
            Connecting,
            ConnectedEstablishing,
            ConnectedEstablished,
            Disconnected
        }

        public ConnectionType ConnectionType { get { return connectionType; } }

        public bool IsConnected
        {
            get
            {
                return state == (int)State.ConnectedEstablished;
            }
        }

        public ConnectionMultiplexer Multiplexer { get { return multiplexer; } }

        public ServerEndPoint ServerEndPoint { get { return serverEndPoint; } }

        internal State ConnectionState { get { return (State)state; } }
        internal long OperationCount
        {
            get { return Interlocked.Read(ref operationCount); }
        }

        public long SubscriptionCount
        {
            get
            {
                var tmp = physical;
                return tmp == null ? 0 : physical.SubscriptionCount;
            }
        }

        public void CompleteSyncOrAsync(ICompletable operation)
        {
            completionManager.CompleteSyncOrAsync(operation);
        }

        public void Dispose()
        {
            isDisposed = true;
            using (var tmp = physical)
            {
                physical = null;
            }
        }

        public void ReportNextFailure()
        {
            reportNextFailure = true;
        }

        public override string ToString()
        {
            return connectionType + "/" + serverEndPoint.EndPoint.ToString();
        }

        public void TryConnect()
        {
            GetConnection();
        }

        public bool TryEnqueue(Message message, bool isSlave)
        {
            if (isDisposed) throw new ObjectDisposedException(Name);
            if (!IsConnected)
            {
                if (message.IsInternalCall)
                {
                    // you can go in the queue, but we won't be starting
                    // a worker, because the handshake has not completed
                    queue.Push(message);
                    return true;
                }
                else
                {
                    // sorry, we're just not ready for you yet;
                    return false;
                }
            }

            bool reqWrite = queue.Push(message);
            LogNonPreferred(message.Flags, isSlave);
            Trace("Now pending: " + GetPendingCount());

            if (reqWrite)
            {
                multiplexer.RequestWrite(this, false);
            }
            return true;
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
        long nonPreferredEndpointCount;

        internal void GetCounters(ConnectionCounters counters)
        {
            counters.PendingUnsentItems = queue.Count();
            counters.OperationCount = OperationCount;
            counters.SocketCount = Interlocked.Read(ref socketCount);
            counters.WriterCount = Interlocked.CompareExchange(ref activeWriters, 0, 0);
            counters.NonPreferredEndpointCount = Interlocked.Read(ref nonPreferredEndpointCount);
            completionManager.GetCounters(counters);
            var tmp = physical;
            if (tmp != null)
            {
                tmp.GetCounters(counters);
            }
        }

        internal int GetOutstandingCount(out int inst, out int qu, out int qs, out int qc, out int wr, out int wq)
        {// defined as: PendingUnsentItems + SentItemsAwaitingResponse + ResponsesAwaitingAsyncCompletion
            inst = (int)(Interlocked.Read(ref operationCount) - Interlocked.Read(ref profileLastLog));
            qu = queue.Count();
            var tmp = physical;
            qs = tmp == null ? 0 :  tmp.GetOutstandingCount();
            qc = completionManager.GetOutstandingCount();
            wr = Interlocked.CompareExchange(ref activeWriters, 0, 0);
            wq = Interlocked.CompareExchange(ref inWriteQueue, 0, 0);
            return qu + qs + qc;
        }

        internal string GetStormLog()
        {
            var sb = new StringBuilder("Storm log for ").Append(Format.ToString(serverEndPoint.EndPoint)).Append(" / ").Append(connectionType)
                .Append(" at ").Append(DateTime.UtcNow)
                .AppendLine().AppendLine();
            queue.GetStormLog(sb);
            var tmp = physical;
            if (tmp != null) tmp.GetStormLog(sb);
            completionManager.GetStormLog(sb);
            sb.Append("Circular op-count snapshot:");
            AppendProfile(sb);
            sb.AppendLine();
            return sb.ToString();
        }
        internal void AppendProfile(StringBuilder sb)
        {
            long[] clone = new long[ProfileLogSamples + 1];
            for(int i = 0; i < ProfileLogSamples; i++)
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
        const double ProfileLogSeconds = (ConnectionMultiplexer.MillisecondsPerHeartbeat * ProfileLogSamples) / 1000.0;
        const int ProfileLogSamples = 10;
        readonly long[] profileLog = new long[ProfileLogSamples];
        long profileLastLog;
        int profileLogIndex;
        internal void IncrementOpCount()
        {
            Interlocked.Increment(ref operationCount);
        }

        internal void KeepAlive()
        {
            var commandMap = multiplexer.CommandMap;
            Message msg = null;
            switch (connectionType)
            {
                case ConnectionType.Interactive:
                    if (commandMap.IsAvailable(RedisCommand.PING))
                    {
                        msg = Message.Create(-1, CommandFlags.FireAndForget, RedisCommand.PING);
                        msg.SetSource(ResultProcessor.DemandPONG, null);
                    }
                    break;
                case ConnectionType.Subscription:
                    if (commandMap.IsAvailable(RedisCommand.UNSUBSCRIBE))
                    {
                        msg = Message.Create(-1, CommandFlags.FireAndForget, RedisCommand.UNSUBSCRIBE,
                            (RedisChannel)Guid.NewGuid().ToByteArray());
                        msg.SetSource(ResultProcessor.TrackSubscriptions, null);
                    }
                    break;
            }
            if(msg != null)
            {
                msg.SetInternalCall();
                multiplexer.Trace("Enqueue: " + msg);
                TryEnqueue(msg, serverEndPoint.IsSlave);
            }
        }

        internal void OnConnected(PhysicalConnection connection)
        {
            Trace("OnConnected");
            if (physical == connection && !isDisposed && ChangeState(State.Connecting, State.ConnectedEstablishing))
            {
                serverEndPoint.OnEstablishing(connection);
            }
            else
            {
                try { connection.Dispose(); } catch { }
            }
        }

        internal void OnConnectionFailed(PhysicalConnection connection, ConnectionFailureType failureType, Exception innerException)
        {
            if (reportNextFailure)
            {
                reportNextFailure = false; // until it is restored
                var endpoint = serverEndPoint.EndPoint;
                multiplexer.OnConnectionFailed(endpoint, connectionType, failureType, innerException, reconfigureNextFailure);
            }
        }

        internal void OnDisconnected(ConnectionFailureType failureType, PhysicalConnection connection, out bool isCurrent)
        {
            Trace("OnDisconnected");

            // if the next thing in the pipe is a PING, we can tell it that we failed (this really helps spot doomed connects)
            // note that for simplicity we haven't removed it from the queue; that's OK
            int count;
            var ping = queue.PeekPing(out count);
            if (ping != null)
            {
                Trace("Marking PING as failed (queue length: " + count + ")");
                ping.Fail(failureType, null);
                CompleteSyncOrAsync(ping);
            }

            if (isCurrent = physical == connection)
            {
                Trace("Bridge noting disconnect from active connection" + (isDisposed ? " (disposed)" : ""));
                ChangeState(State.Disconnected);
                physical = null;

                if (!isDisposed && Interlocked.Increment(ref failConnectCount) == 1)
                {
                    GetConnection(); // try to connect immediately
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

        internal void OnFullyEstablished(PhysicalConnection connection)
        {
            Trace("OnFullyEstablished");
            if (physical == connection && !isDisposed && ChangeState(State.ConnectedEstablishing, State.ConnectedEstablished))
            {
                reportNextFailure = reconfigureNextFailure = true;
                Interlocked.Exchange(ref failConnectCount, 0);
                serverEndPoint.OnFullyEstablished(connection);
                multiplexer.RequestWrite(this, true);
            }
            else
            {
                try { connection.Dispose(); } catch { }
            }
        }
        internal int GetPendingCount()
        {
            return queue.Count();
        }
        internal void OnHeartbeat()
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

                switch (state)
                {
                    case (int)State.ConnectedEstablishing:
                    case (int)State.ConnectedEstablished:
                        var tmp = physical;
                        if (tmp != null)
                        {
                            int writeEverySeconds = serverEndPoint.WriteEverySeconds;
                            if (writeEverySeconds > 0 && tmp.LastWriteSecondsAgo >= writeEverySeconds)
                            {
                                Trace("OnHeartbeat - overdue");
                                if (state == (int)State.ConnectedEstablished)
                                {
                                    KeepAlive();
                                }
                                else
                                {
                                    bool ignore;
                                    OnDisconnected(ConnectionFailureType.SocketFailure, tmp, out ignore);
                                }
                            }
                        }
                        break;
                    case (int)State.Disconnected:
                        multiplexer.Trace("Resurrecting " + this.ToString());
                        GetConnection();
                        break;
                }
            }
            catch (Exception ex)
            {
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
            multiplexer.Trace(message, ToString());
        }

        [Conditional("VERBOSE")]
        internal void Trace(bool condition, string message)
        {
            if (condition) multiplexer.Trace(message, ToString());
        }

        internal bool TryEnqueue(List<Message> messages, bool isSlave)
        {
            if (messages == null || messages.Count == 0) return true;

            if (isDisposed) throw new ObjectDisposedException(Name);

            if(!IsConnected)
            {
                return false;
            }
            bool reqWrite = false;
            foreach(var message in messages)
            {   // deliberately not taking a single lock here; we don't care if
                // other threads manage to interleave - in fact, it would be desirable
                // (to avoid a batch monopolising the connection)
                if (queue.Push(message)) reqWrite = true;
                LogNonPreferred(message.Flags, isSlave);
            }
            Trace("Now pending: " + GetPendingCount());
            if(reqWrite) // was empty before
            {
                multiplexer.RequestWrite(this, false);
            }
            return true;
        }

        internal bool ConfirmRemoveFromWriteQueue()
        {
            lock(queue.SyncLock)
            {
                if(queue.Count() == 0)
                {
                    Interlocked.Exchange(ref inWriteQueue, 0);
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// This writes a message **directly** to the output stream; note
        /// that this ignores the queue, so should only be used *either*
        /// from the regular dequeue loop, *or* from the "I've just
        /// connected" handshake (when there is no dequeue loop) - otherwise,
        /// you can pretty much assume you're going to destroy the stream
        /// </summary>
        internal void WriteMessageDirect(PhysicalConnection tmp, Message next)
        {
            Trace("Writing: " + next);
            if (next is IMultiMessage)
            {
                SelectDatabase(tmp, next); // need to switch database *before* the transaction
                foreach (var subCommand in ((IMultiMessage)next).GetMessages(tmp))
                {
                    if (!WriteMessageToServer(tmp, subCommand))
                    {
                        // we screwed up; abort; note that WriteMessageToServer already
                        // killed the underlying connection
                        next.Fail(ConnectionFailureType.ProtocolFailure, null);
                        CompleteSyncOrAsync(next);
                        break;
                    }
                }
            }
            else
            {
                WriteMessageToServer(tmp, next);
            }
        }

        private void ChangeState(State newState)
        {
            var oldState = (State)Interlocked.Exchange(ref state, (int)newState);
            if (oldState != newState)
            {
                multiplexer.Trace(connectionType + " state changed from " + oldState + " to " + newState);
            }
        }

        private bool ChangeState(State oldState, State newState)
        {
            bool result = Interlocked.CompareExchange(ref state, (int)newState, (int)oldState) == (int)oldState;
            if (result)
            {
                multiplexer.Trace(connectionType + " state changed from " + oldState + " to " + newState);
            }
            return result;
        }

        private void Flush()
        {
            var tmp = physical;
            if (tmp != null)
            {
                try
                {
                    Trace(connectionType + " flushed");
                    tmp.Flush();
                }
                catch
                { }
            }
        }

        private PhysicalConnection GetConnection()
        {
            if (state == (int)State.Disconnected)
            {
                try
                {
                    if (!multiplexer.IsDisposed)
                    {
                        Multiplexer.Trace("Connecting...", Name);
                        if (ChangeState(State.Disconnected, State.Connecting))
                        {
                            Interlocked.Increment(ref socketCount);
                            physical = new PhysicalConnection(this);
                        }
                    }
                    return null;
                }
                catch (Exception ex)
                {
                    Multiplexer.Trace("Connect failed: " + ex.Message, Name);
                    ChangeState(State.Disconnected);
                    throw;
                }
            }
            return physical;
        }

        

        private void SelectDatabase(PhysicalConnection connection, Message message)
        {
            int db = message.Db;
            if (db >= 0)
            {
                var sel = connection.GetSelectDatabaseCommand(db);
                if (sel != null)
                {
                    connection.Enqueue(sel);
                    sel.WriteImpl(connection);
                    IncrementOpCount();
                }
            }
        }
        private bool WriteMessageToServer(PhysicalConnection connection, Message message)
        {
            if (message == null) return true;

            try
            {
                var cmd = message.Command;
                bool isMasterOnly = message.IsMasterOnly();
                if (isMasterOnly && serverEndPoint.IsSlave)
                {
                    throw ExceptionFactory.MasterOnly(message.Command);
                }

                SelectDatabase(connection, message);

                if (!connection.TransactionActive)
                {
                    var readmode = connection.GetReadModeCommand(isMasterOnly);
                    if (readmode != null)
                    {
                        connection.Enqueue(readmode);
                        readmode.WriteTo(connection);
                        IncrementOpCount();
                    }

                    if (message.IsAsking)
                    {
                        var asking = ReusableAskingCommand;
                        connection.Enqueue(asking);
                        asking.WriteImpl(connection);
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

                connection.Enqueue(message);
                message.WriteImpl(connection);
                IncrementOpCount();

                // some commands smash our ability to trust the database; some commands
                // demand an immediate flush
                switch (cmd)
                {
                    case RedisCommand.EVAL:
                    case RedisCommand.EVALSHA:
                    case RedisCommand.DISCARD:
                    case RedisCommand.EXEC:
                        connection.SetUnknownDatabase();
                        break;
                }
                return true;
            }
            catch (RedisCommandException ex)
            {
                Trace("Write failed: " + ex.Message);
                message.Fail(ConnectionFailureType.ProtocolFailure, ex);
                CompleteSyncOrAsync(message);
                // this failed without actually writing; we're OK with that... unless there's a transaction

                if (connection.TransactionActive)
                {
                    // we left it in a broken state; need to kill the connection
                    connection.RecordConnectionFailed(ConnectionFailureType.ProtocolFailure, ex);
                    return false;
                }
                return true;
            }
            catch (Exception ex)
            {
                Trace("Write failed: " + ex.Message);
                message.Fail(ConnectionFailureType.InternalFailure, ex);
                CompleteSyncOrAsync(message);

                // we're not sure *what* happened here; kill the connection
                connection.RecordConnectionFailed(ConnectionFailureType.InternalFailure, ex);
                return false;
            }
        }


        internal WriteResult WriteQueue(int maxWork)
        {
            bool weAreWriter = false;
            try
            {
                Trace("Writing queue from bridge");

                weAreWriter = Interlocked.CompareExchange(ref activeWriters, 1, 0) == 0;
                if (!weAreWriter)
                {
                    Trace("(aborting: existing writer)");
                    return WriteResult.CompetingWriter;
                }

                var conn = GetConnection();
                if(conn == null)
                {
                    Trace("Connection not available; exiting");
                    return WriteResult.NoConnection;
                }

                int count = 0;
                Message last = null;
                while (true)
                {
                    var next = queue.Dequeue();
                    if (next == null)
                    {
                        Trace("Nothing to write; exiting");
                        Trace(last != null, "Flushed up to: " + last);
                        conn.Flush();
                        return WriteResult.QueueEmpty;
                    }
                    last = next;
                    
                    Trace("Now pending: " + GetPendingCount());
                    WriteMessageDirect(conn, next);
                    count++;
                    if (maxWork > 0 && count >= maxWork)
                    {
                        Trace("Work limit; exiting");
                        Trace(last != null, "Flushed up to: " + last);
                        conn.Flush();
                        break;
                    }
                }
            }
            catch
            { }
            finally
            {
                if (weAreWriter)
                {
                    Interlocked.Exchange(ref activeWriters, 0);
                    Trace("Exiting writer");
                }
            }
            return queue.Any() ? WriteResult.MoreWork : WriteResult.QueueEmpty;
        }
    }

    enum WriteResult
    {
        QueueEmpty,
        MoreWork,
        CompetingWriter,
        NoConnection,        
    }
}
