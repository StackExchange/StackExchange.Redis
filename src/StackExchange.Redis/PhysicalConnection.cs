using System;
using System.Buffers;
using System.Buffers.Text;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Pipelines;
using System.Linq;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Pipelines.Sockets.Unofficial;
using Pipelines.Sockets.Unofficial.Arenas;
using StackExchange.Redis.Transports;

namespace StackExchange.Redis
{
    internal sealed partial class PhysicalConnection : IDisposable
    {
        internal readonly byte[] ChannelPrefix;

        private static readonly CommandBytes message = "message", pmessage = "pmessage";

        private static int totalCount;

        private readonly ConnectionType connectionType;

        // things sent to this physical, but not yet received
        private readonly Queue<Message> _writtenAwaitingResponse = new Queue<Message>();

        private readonly string _physicalName;

        private volatile int currentDatabase = 0;

        private ReadMode currentReadMode = ReadMode.NotSpecified;

        private int failureReported;

        private int lastWriteTickCount, lastReadTickCount, lastBeatTickCount;

        internal void GetBytes(out long sent, out long received)
        {
            if (_ioPipe is IMeasuredDuplexPipe sc)
            {
                sent = sc.TotalBytesSent;
                received = sc.TotalBytesReceived;
            }
            else
            {
                sent = received = -1;
            }
        }

        private IDuplexPipe _ioPipe;
        internal bool HasOutputPipe => _ioPipe?.Output != null;

        private Socket _socket;
        internal Socket VolatileSocket => Volatile.Read(ref _socket);

        public PhysicalConnection(PhysicalBridge bridge)
        {
            lastWriteTickCount = lastReadTickCount = Environment.TickCount;
            lastBeatTickCount = 0;
            connectionType = bridge.ConnectionType;
            _bridge = new WeakReference(bridge);
            ChannelPrefix = bridge.Multiplexer.RawConfig.ChannelPrefix;
            if (ChannelPrefix?.Length == 0) ChannelPrefix = null; // null tests are easier than null+empty
            var endpoint = bridge.ServerEndPoint.EndPoint;
            _physicalName = connectionType + "#" + Interlocked.Increment(ref totalCount) + "@" + Format.ToString(endpoint);

            OnCreateEcho();
        }

        internal async Task BeginConnectAsync(LogProxy log)
        {
            var bridge = BridgeCouldBeNull;
            var endpoint = bridge?.ServerEndPoint?.EndPoint;
            if (endpoint == null)
            {
                log?.WriteLine("No endpoint");
                return;
            }

            Trace("Connecting...");
            _socket = SocketManager.CreateSocket(endpoint);
            bridge.Multiplexer.RawConfig.BeforeSocketConnect?.Invoke(endpoint, bridge.ConnectionType, _socket);
            bridge.Multiplexer.OnConnecting(endpoint, bridge.ConnectionType);
            log?.WriteLine($"{Format.ToString(endpoint)}: BeginConnectAsync");

            CancellationTokenSource timeoutSource = null;
            try
            {
                using (var args = new SocketAwaitableEventArgs
                {
                    RemoteEndPoint = endpoint,
                })
                {
                    var x = VolatileSocket;
                    if (x == null)
                    {
                        args.Abort();
                    }
                    else if (x.ConnectAsync(args))
                    {   // asynchronous operation is pending
                        timeoutSource = ConfigureTimeout(args, bridge.Multiplexer.RawConfig.ConnectTimeout);
                    }
                    else
                    {   // completed synchronously
                        args.Complete();
                    }

                    // Complete connection
                    try
                    {
                        // If we're told to ignore connect, abort here
                        if (BridgeCouldBeNull?.Multiplexer?.IgnoreConnect ?? false) return;

                        await args; // wait for the connect to complete or fail (will throw)
                        if (timeoutSource != null)
                        {
                            timeoutSource.Cancel();
                            timeoutSource.Dispose();
                        }

                        x = VolatileSocket;
                        if (x == null)
                        {
                            ConnectionMultiplexer.TraceWithoutContext("Socket was already aborted");
                        }
                        else if (await ConnectedAsync(x, log, bridge.Multiplexer.SocketManager).ForAwait())
                        {
                            log?.WriteLine($"{Format.ToString(endpoint)}: Starting read");
                            try
                            {
                                StartReading();
                                // Normal return
                            }
                            catch (Exception ex)
                            {
                                ConnectionMultiplexer.TraceWithoutContext(ex.Message);
                                Shutdown();
                            }
                        }
                        else
                        {
                            ConnectionMultiplexer.TraceWithoutContext("Aborting socket");
                            Shutdown();
                        }
                    }
                    catch (ObjectDisposedException)
                    {
                        log?.WriteLine($"{Format.ToString(endpoint)}: (socket shutdown)");
                        try { RecordConnectionFailed(ConnectionFailureType.UnableToConnect, isInitialConnect: true); }
                        catch (Exception inner)
                        {
                            ConnectionMultiplexer.TraceWithoutContext(inner.Message);
                        }
                    }
                    catch (Exception outer)
                    {
                        ConnectionMultiplexer.TraceWithoutContext(outer.Message);
                        try { RecordConnectionFailed(ConnectionFailureType.UnableToConnect, isInitialConnect: true); }
                        catch (Exception inner)
                        {
                            ConnectionMultiplexer.TraceWithoutContext(inner.Message);
                        }
                    }
                }
            }
            catch (NotImplementedException ex) when (endpoint is not IPEndPoint)
            {
                throw new InvalidOperationException("BeginConnect failed with NotImplementedException; consider using IP endpoints, or enable ResolveDns in the configuration", ex);
            }
            finally
            {
                if (timeoutSource != null) try { timeoutSource.Dispose(); } catch { }
            }
        }

        private static CancellationTokenSource ConfigureTimeout(SocketAwaitableEventArgs args, int timeoutMilliseconds)
        {
            var cts = new CancellationTokenSource();
            var timeout = Task.Delay(timeoutMilliseconds, cts.Token);
            timeout.ContinueWith((_, state) =>
            {
                try
                {
                    var a = (SocketAwaitableEventArgs)state;
                    a.Abort(SocketError.TimedOut);
                    Socket.CancelConnectAsync(a);
                }
                catch { }
            }, args);
            return cts;
        }

        private enum ReadMode : byte
        {
            NotSpecified,
            ReadOnly,
            ReadWrite,
        }

        private readonly WeakReference _bridge;
        public PhysicalBridge BridgeCouldBeNull => (PhysicalBridge)_bridge.Target;

        public long LastWriteSecondsAgo => unchecked(Environment.TickCount - Thread.VolatileRead(ref lastWriteTickCount)) / 1000;

        private bool IncludeDetailInExceptions => BridgeCouldBeNull?.Multiplexer.RawConfig.IncludeDetailInExceptions ?? false;

        [Conditional("VERBOSE")]
        internal void Trace(string message) => BridgeCouldBeNull?.Multiplexer?.Trace(message, ToString());

        public long SubscriptionCount { get; set; }

        public bool TransactionActive { get; internal set; }

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Usage", "CA2202:Do not dispose objects multiple times")]
        internal void Shutdown()
        {
            var ioPipe = Interlocked.Exchange(ref _ioPipe, null); // compare to the critical read
            var socket = Interlocked.Exchange(ref _socket, null);

            if (ioPipe != null)
            {
                Trace("Disconnecting...");
                try { BridgeCouldBeNull?.OnDisconnected(ConnectionFailureType.ConnectionDisposed, this, out _, out _); } catch { }
                try { ioPipe.Input?.CancelPendingRead(); } catch { }
                try { ioPipe.Input?.Complete(); } catch { }
                try { ioPipe.Output?.CancelPendingFlush(); } catch { }
                try { ioPipe.Output?.Complete(); } catch { }
                try { using (ioPipe as IDisposable) { } } catch { }
            }

            if (socket != null)
            {
                try { socket.Shutdown(SocketShutdown.Both); } catch { }
                try { socket.Close(); } catch { }
                try { socket.Dispose(); } catch { }
            }
        }

        public void Dispose()
        {
            bool markDisposed = VolatileSocket != null;
            Shutdown();
            if (markDisposed)
            {
                Trace("Disconnected");
                RecordConnectionFailed(ConnectionFailureType.ConnectionDisposed);
            }
            OnCloseEcho();
            _reusableFlushSyncTokenSource?.Dispose();
            GC.SuppressFinalize(this);
        }

        private async Task AwaitedFlush(ValueTask<FlushResult> flush)
        {
            await flush.ForAwait();
            _writeStatus = WriteStatus.Flushed;
            UpdateLastWriteTime();
        }
        internal void UpdateLastWriteTime() => Interlocked.Exchange(ref lastWriteTickCount, Environment.TickCount);
        public Task FlushAsync()
        {
            var tmp = _ioPipe?.Output;
            if (tmp != null)
            {
                _writeStatus = WriteStatus.Flushing;
                var flush = tmp.FlushAsync();
                if (!flush.IsCompletedSuccessfully)
                {
                    return AwaitedFlush(flush);
                }
                _writeStatus = WriteStatus.Flushed;
                UpdateLastWriteTime();
            }
            return Task.CompletedTask;
        }

        internal void SimulateConnectionFailure(SimulatedFailureType failureType)
        {
            var raiseFailed = false;
            if (connectionType == ConnectionType.Interactive)
            {
                if (failureType.HasFlag(SimulatedFailureType.InteractiveInbound))
                {
                    _ioPipe?.Input.Complete(new Exception("Simulating interactive input failure"));
                    raiseFailed = true;
                }
                if (failureType.HasFlag(SimulatedFailureType.InteractiveOutbound))
                {
                    _ioPipe?.Output.Complete(new Exception("Simulating interactive output failure"));
                    raiseFailed = true;
                }
            }
            else if (connectionType == ConnectionType.Subscription)
            {
                if (failureType.HasFlag(SimulatedFailureType.SubscriptionInbound))
                {
                    _ioPipe?.Input.Complete(new Exception("Simulating subscription input failure"));
                    raiseFailed = true;
                }
                if (failureType.HasFlag(SimulatedFailureType.SubscriptionOutbound))
                {
                    _ioPipe?.Output.Complete(new Exception("Simulating subscription output failure"));
                    raiseFailed = true;
                }
            }
            if (raiseFailed)
            {
                RecordConnectionFailed(ConnectionFailureType.SocketFailure);
            }
        }

        public void RecordConnectionFailed(
            ConnectionFailureType failureType,
            Exception innerException = null,
            [CallerMemberName] string origin = null,
            bool isInitialConnect = false,
            IDuplexPipe connectingPipe = null
            )
        {
            Exception outerException = innerException;
            IdentifyFailureType(innerException, ref failureType);
            var bridge = BridgeCouldBeNull;
            if (_ioPipe != null || isInitialConnect) // if *we* didn't burn the pipe: flag it
            {
                if (failureType == ConnectionFailureType.InternalFailure) OnInternalError(innerException, origin);

                // stop anything new coming in...
                bridge?.Trace("Failed: " + failureType);
                ConnectionStatus connStatus = ConnectionStatus.Default;
                PhysicalBridge.State oldState = PhysicalBridge.State.Disconnected;
                bool isCurrent = false;
                bridge?.OnDisconnected(failureType, this, out isCurrent, out oldState);
                if (oldState == PhysicalBridge.State.ConnectedEstablished)
                {
                    try
                    {
                        connStatus = GetStatus();
                    }
                    catch { /* best effort only */ }
                }

                if (isCurrent && Interlocked.CompareExchange(ref failureReported, 1, 0) == 0)
                {
                    int now = Environment.TickCount, lastRead = Thread.VolatileRead(ref lastReadTickCount), lastWrite = Thread.VolatileRead(ref lastWriteTickCount),
                        lastBeat = Thread.VolatileRead(ref lastBeatTickCount);

                    int unansweredWriteTime = 0;
                    lock (_writtenAwaitingResponse)
                    {
                        // find oldest message awaiting a response
                        if (_writtenAwaitingResponse.Count != 0)
                        {
                            var next = _writtenAwaitingResponse.Peek();
                            unansweredWriteTime = next.GetWriteTime();
                        }
                    }

                    var exMessage = new StringBuilder(failureType.ToString());

                    var pipe = connectingPipe ?? _ioPipe;
                    if (pipe is SocketConnection sc)
                    {
                        exMessage.Append(" (").Append(sc.ShutdownKind);
                        if (sc.SocketError != SocketError.Success)
                        {
                            exMessage.Append('/').Append(sc.SocketError);
                        }
                        if (sc.BytesRead == 0) exMessage.Append(", 0-read");
                        if (sc.BytesSent == 0) exMessage.Append(", 0-sent");
                        exMessage.Append(", last-recv: ").Append(sc.LastReceived).Append(')');
                    }
                    else if (pipe is IMeasuredDuplexPipe mdp)
                    {
                        long sent = mdp.TotalBytesSent, recd = mdp.TotalBytesReceived;

                        if (sent == 0) { exMessage.Append(recd == 0 ? " (0-read, 0-sent)" : " (0-sent)"); }
                        else if (recd == 0) { exMessage.Append(" (0-read)"); }
                    }

                    var data = new List<Tuple<string, string>>();
                    void add(string lk, string sk, string v)
                    {
                        if (lk != null) data.Add(Tuple.Create(lk, v));
                        if (sk != null) exMessage.Append(", ").Append(sk).Append(": ").Append(v);
                    }

                    if (IncludeDetailInExceptions)
                    {
                        if (bridge != null)
                        {
                            exMessage.Append(" on ").Append(Format.ToString(bridge.ServerEndPoint?.EndPoint)).Append('/').Append(connectionType)
                                .Append(", ").Append(_writeStatus).Append('/').Append(_readStatus)
                                .Append(", last: ").Append(bridge.LastCommand);

                            data.Add(Tuple.Create("FailureType", failureType.ToString()));
                            data.Add(Tuple.Create("EndPoint", Format.ToString(bridge.ServerEndPoint?.EndPoint)));

                            add("Origin", "origin", origin);
                            // add("Input-Buffer", "input-buffer", _ioPipe.Input);
                            add("Outstanding-Responses", "outstanding", GetSentAwaitingResponseCount().ToString());
                            add("Last-Read", "last-read", (unchecked(now - lastRead) / 1000) + "s ago");
                            add("Last-Write", "last-write", (unchecked(now - lastWrite) / 1000) + "s ago");
                            if (unansweredWriteTime != 0) add("Unanswered-Write", "unanswered-write", (unchecked(now - unansweredWriteTime) / 1000) + "s ago");
                            add("Keep-Alive", "keep-alive", bridge.ServerEndPoint?.WriteEverySeconds + "s");
                            add("Previous-Physical-State", "state", oldState.ToString());
                            add("Manager", "mgr", bridge.Multiplexer.SocketManager?.GetState());
                            if (connStatus.BytesAvailableOnSocket >= 0) add("Inbound-Bytes", "in", connStatus.BytesAvailableOnSocket.ToString());
                            if (connStatus.BytesInReadPipe >= 0) add("Inbound-Pipe-Bytes", "in-pipe", connStatus.BytesInReadPipe.ToString());
                            if (connStatus.BytesInWritePipe >= 0) add("Outbound-Pipe-Bytes", "out-pipe", connStatus.BytesInWritePipe.ToString());

                            add("Last-Heartbeat", "last-heartbeat", (lastBeat == 0 ? "never" : ((unchecked(now - lastBeat) / 1000) + "s ago")) + (BridgeCouldBeNull.IsBeating ? " (mid-beat)" : ""));
                            var mbeat = bridge.Multiplexer.LastHeartbeatSecondsAgo;
                            if (mbeat >= 0)
                            {
                                add("Last-Multiplexer-Heartbeat", "last-mbeat", mbeat + "s ago");
                            }
                            add("Last-Global-Heartbeat", "global", ConnectionMultiplexer.LastGlobalHeartbeatSecondsAgo + "s ago");
                        }
                    }

                    add("Version", "v", Utils.GetLibVersion());

                    outerException = new RedisConnectionException(failureType, exMessage.ToString(), innerException);

                    foreach (var kv in data)
                    {
                        outerException.Data["Redis-" + kv.Item1] = kv.Item2;
                    }

                    bridge?.OnConnectionFailed(this, failureType, outerException);
                }
            }
            // cleanup
            lock (_writtenAwaitingResponse)
            {
                bridge?.Trace(_writtenAwaitingResponse.Count != 0, "Failing outstanding messages: " + _writtenAwaitingResponse.Count);
                while (_writtenAwaitingResponse.Count != 0)
                {
                    var next = _writtenAwaitingResponse.Dequeue();

                    if (next.Command == RedisCommand.QUIT && next.TrySetResult(true))
                    {
                        // fine, death of a socket is close enough
                        next.Complete();
                    }
                    else
                    {
                        var ex = innerException is RedisException ? innerException : outerException;
                        if (bridge != null)
                        {
                            bridge.Trace("Failing: " + next);
                            bridge.Multiplexer?.OnMessageFaulted(next, ex, origin);
                        }
                        next.SetExceptionAndComplete(ex, bridge);
                    }
                }
            }

            // burn the socket
            Shutdown();
        }

        internal bool IsIdle() => _writeStatus == WriteStatus.Idle;
        internal void SetIdle() => _writeStatus = WriteStatus.Idle;
        internal void SetWriting() => _writeStatus = WriteStatus.Writing;

        private volatile WriteStatus _writeStatus;

        internal WriteStatus GetWriteStatus() => _writeStatus;

        internal enum WriteStatus
        {
            Initializing,
            Idle,
            Writing,
            Flushing,
            Flushed,

            NA = -1,
        }

        /// <summary>Returns a string that represents the current object.</summary>
        /// <returns>A string that represents the current object.</returns>
        public override string ToString() => $"{_physicalName} ({_writeStatus})";

        internal static void IdentifyFailureType(Exception exception, ref ConnectionFailureType failureType)
        {
            if (exception != null && failureType == ConnectionFailureType.InternalFailure)
            {
                if (exception is AggregateException)
                {
                    exception = exception.InnerException ?? exception;
                }

                failureType = exception switch
                {
                    AuthenticationException => ConnectionFailureType.AuthenticationFailure,
                    EndOfStreamException or ObjectDisposedException => ConnectionFailureType.SocketClosed,
                    SocketException or IOException => ConnectionFailureType.SocketFailure,
                    _ => failureType
                };
            }
        }

        internal void EnqueueInsideWriteLock(Message next)
        {
            lock (_writtenAwaitingResponse)
            {
                _writtenAwaitingResponse.Enqueue(next);
            }
        }

        internal void GetCounters(ConnectionCounters counters)
        {
            lock (_writtenAwaitingResponse)
            {
                counters.SentItemsAwaitingResponse = _writtenAwaitingResponse.Count;
            }
            counters.Subscriptions = SubscriptionCount;
        }

        internal Message GetReadModeCommand(bool isPrimaryOnly)
        {
            if (BridgeCouldBeNull?.ServerEndPoint?.RequiresReadMode == true)
            {
                ReadMode requiredReadMode = isPrimaryOnly ? ReadMode.ReadWrite : ReadMode.ReadOnly;
                if (requiredReadMode != currentReadMode)
                {
                    currentReadMode = requiredReadMode;
                    switch (requiredReadMode)
                    {
                        case ReadMode.ReadOnly: return Message.Create(-1, CommandFlags.FireAndForget, RedisCommand.READONLY);
                        case ReadMode.ReadWrite: return Message.Create(-1, CommandFlags.FireAndForget, RedisCommand.READWRITE);
                    }
                }
            }
            else if (currentReadMode == ReadMode.ReadOnly)
            {
                // we don't need it (because we're not a cluster, or not a replica),
                // but we are in read-only mode; switch to read-write
                currentReadMode = ReadMode.ReadWrite;
                return Message.Create(-1, CommandFlags.FireAndForget, RedisCommand.READWRITE);
            }
            return null;
        }

        internal Message GetSelectDatabaseCommand(int targetDatabase, Message message)
        {
            if (targetDatabase < 0 || targetDatabase == currentDatabase)
            {
                return null;
            }

            if (BridgeCouldBeNull?.ServerEndPoint is not ServerEndPoint serverEndpoint)
            {
                return null;
            }
            int available = serverEndpoint.Databases;

            // Only db0 is available on cluster/twemproxy/envoyproxy
            if (!serverEndpoint.SupportsDatabases)
            {
                if (targetDatabase != 0)
                {
                    // We should never see this, since the API doesn't allow it; thus not too worried about ExceptionFactory
                    throw new RedisCommandException("Multiple databases are not supported on this server; cannot switch to database: " + targetDatabase);
                }
                return null;
            }

            if (message.Command == RedisCommand.SELECT)
            {
                // This could come from an EVAL/EVALSHA inside a transaction, for example; we'll accept it
                BridgeCouldBeNull?.Trace("Switching database: " + targetDatabase);
                currentDatabase = targetDatabase;
                return null;
            }

            if (TransactionActive)
            {
                // Should never see this, since the API doesn't allow it, thus not too worried about ExceptionFactory
                throw new RedisCommandException("Multiple databases inside a transaction are not currently supported: " + targetDatabase);
            }

            // We positively know it is out of range
            if (available != 0 && targetDatabase >= available)
            {
                throw ExceptionFactory.DatabaseOutfRange(IncludeDetailInExceptions, targetDatabase, message, serverEndpoint);
            }
            BridgeCouldBeNull?.Trace("Switching database: " + targetDatabase);
            currentDatabase = targetDatabase;
            return GetSelectDatabaseCommand(targetDatabase);
        }

        internal static Message GetSelectDatabaseCommand(int targetDatabase)
            => Message.Create(targetDatabase, CommandFlags.FireAndForget, RedisCommand.SELECT);

        internal int GetSentAwaitingResponseCount()
        {
            lock (_writtenAwaitingResponse)
            {
                return _writtenAwaitingResponse.Count;
            }
        }

        internal void GetStormLog(StringBuilder sb)
        {
            lock (_writtenAwaitingResponse)
            {
                if (_writtenAwaitingResponse.Count == 0) return;
                sb.Append("Sent, awaiting response from server: ").Append(_writtenAwaitingResponse.Count).AppendLine();
                int total = 0;
                foreach (var item in _writtenAwaitingResponse)
                {
                    if (++total >= 500) break;
                    item.AppendStormLog(sb);
                    sb.AppendLine();
                }
            }
        }

        internal void OnBridgeHeartbeat()
        {
            var now = Environment.TickCount;
            Interlocked.Exchange(ref lastBeatTickCount, now);

            lock (_writtenAwaitingResponse)
            {
                if (_writtenAwaitingResponse.Count != 0 && BridgeCouldBeNull is PhysicalBridge bridge)
                {
                    var server = bridge?.ServerEndPoint;
                    var timeout = bridge.Multiplexer.AsyncTimeoutMilliseconds;
                    foreach (var msg in _writtenAwaitingResponse)
                    {
                        // We only handle async timeouts here, synchronous timeouts are handled upstream.
                        // Those sync timeouts happen in ConnectionMultiplexer.ExecuteSyncImpl() via Monitor.Wait.
                        if (msg.ResultBoxIsAsync && msg.HasTimedOut(now, timeout, out var elapsed))
                        {
                            bool haveDeltas = msg.TryGetPhysicalState(out _, out _, out long sentDelta, out var receivedDelta) && sentDelta >= 0 && receivedDelta >= 0;
                            var timeoutEx = ExceptionFactory.Timeout(bridge.Multiplexer, haveDeltas
                                ? $"Timeout awaiting response (outbound={sentDelta >> 10}KiB, inbound={receivedDelta >> 10}KiB, {elapsed}ms elapsed, timeout is {timeout}ms)"
                                : $"Timeout awaiting response ({elapsed}ms elapsed, timeout is {timeout}ms)", msg, server);
                            bridge.Multiplexer?.OnMessageFaulted(msg, timeoutEx);
                            msg.SetExceptionAndComplete(timeoutEx, bridge); // tell the message that it is doomed
                            bridge.Multiplexer.OnAsyncTimeout();
                        }
                        // Note: it is important that we **do not** remove the message unless we're tearing down the socket; that
                        // would disrupt the chain for MatchResult; we just preemptively abort the message from the caller's
                        // perspective, and set a flag on the message so we don't keep doing it
                    }
                }
            }
        }

        internal void OnInternalError(Exception exception, [CallerMemberName] string origin = null)
        {
            if (BridgeCouldBeNull is PhysicalBridge bridge)
            {
                bridge.Multiplexer.OnInternalError(exception, bridge.ServerEndPoint.EndPoint, connectionType, origin);
            }
        }

        internal void SetUnknownDatabase()
        {
            // forces next db-specific command to issue a select
            currentDatabase = -1;
        }

        internal IBufferWriter<byte> DefaultOutput => _ioPipe.Output;

        internal void RecordQuit() // don't blame redis if we fired the first shot
            => (_ioPipe as SocketConnection)?.TrySetProtocolShutdown(PipeShutdownKind.ProtocolExitClient);

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1822:Mark members as static", Justification = "DEBUG uses instance data")]
        private async ValueTask<WriteResult> FlushAsync_Awaited(PhysicalConnection connection, ValueTask<FlushResult> flush, bool throwOnFailure)
        {
            try
            {
                await flush.ForAwait();
                connection._writeStatus = WriteStatus.Flushed;
                connection.UpdateLastWriteTime();
                return WriteResult.Success;
            }
            catch (ConnectionResetException ex) when (!throwOnFailure)
            {
                connection.RecordConnectionFailed(ConnectionFailureType.SocketClosed, ex);
                return WriteResult.WriteFailure;
            }
        }

        CancellationTokenSource _reusableFlushSyncTokenSource;
        [Obsolete("this is an anti-pattern; work to reduce reliance on this is in progress")]
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Style", "IDE0062:Make local function 'static'", Justification = "DEBUG uses instance data")]
        internal WriteResult FlushSync(bool throwOnFailure, int millisecondsTimeout)
        {
            var cts = _reusableFlushSyncTokenSource ??= new CancellationTokenSource();
            var flush = FlushAsync(throwOnFailure, cts.Token);
            if (!flush.IsCompletedSuccessfully)
            {
                // only schedule cancellation if it doesn't complete synchronously; at this point, it is doomed
                _reusableFlushSyncTokenSource = null;
                cts.CancelAfter(TimeSpan.FromMilliseconds(millisecondsTimeout));
                try
                {
                    // here lies the evil
                    flush.AsTask().Wait();
                }
                catch (AggregateException ex) when (ex.InnerExceptions.Any(e => e is TaskCanceledException))
                {
                    ThrowTimeout();
                }
                finally
                {
                    cts.Dispose();
                }
            }
            return flush.Result;

            void ThrowTimeout()
            {
                throw new TimeoutException("timeout while synchronously flushing");
            }
        }
        internal ValueTask<WriteResult> FlushAsync(bool throwOnFailure, CancellationToken cancellationToken = default)
        {
            var tmp = _ioPipe?.Output;
            if (tmp == null) return new ValueTask<WriteResult>(WriteResult.NoConnectionAvailable);
            try
            {
                _writeStatus = WriteStatus.Flushing;
                var flush = tmp.FlushAsync(cancellationToken);
                if (!flush.IsCompletedSuccessfully) return FlushAsync_Awaited(this, flush, throwOnFailure);
                _writeStatus = WriteStatus.Flushed;
                UpdateLastWriteTime();
                return new ValueTask<WriteResult>(WriteResult.Success);
            }
            catch (ConnectionResetException ex) when (!throwOnFailure)
            {
                RecordConnectionFailed(ConnectionFailureType.SocketClosed, ex);
                return new ValueTask<WriteResult>(WriteResult.WriteFailure);
            }
        }

        internal readonly struct ConnectionStatus
        {
            /// <summary>
            /// Number of messages sent outbound, but we don't yet have a response for.
            /// </summary>
            public int MessagesSentAwaitingResponse { get; init; }

            /// <summary>
            /// Bytes available on the socket, not yet read into the pipe.
            /// </summary>
            public long BytesAvailableOnSocket { get; init; }
            /// <summary>
            /// Bytes read from the socket, pending in the reader pipe.
            /// </summary>
            public long BytesInReadPipe { get; init; }
            /// <summary>
            /// Bytes in the writer pipe, waiting to be written to the socket.
            /// </summary>
            public long BytesInWritePipe { get; init; }

            /// <summary>
            /// The inbound pipe reader status.
            /// </summary>
            public ReadStatus ReadStatus { get; init; }
            /// <summary>
            /// The outbound pipe writer status.
            /// </summary>
            public WriteStatus WriteStatus { get; init; }

            public override string ToString() =>
                $"SentAwaitingResponse: {MessagesSentAwaitingResponse}, AvailableOnSocket: {BytesAvailableOnSocket} byte(s), InReadPipe: {BytesInReadPipe} byte(s), InWritePipe: {BytesInWritePipe} byte(s), ReadStatus: {ReadStatus}, WriteStatus: {WriteStatus}";

            /// <summary>
            /// The default connection stats, notable *not* the same as <c>default</c> since initializers don't run.
            /// </summary>
            public static ConnectionStatus Default { get; } = new()
            {
                BytesAvailableOnSocket = -1,
                BytesInReadPipe = -1,
                BytesInWritePipe = -1,
                ReadStatus = ReadStatus.NA,
                WriteStatus = WriteStatus.NA,
            };

            /// <summary>
            /// The zeroed connection stats, which we want to display as zero for default exception cases.
            /// </summary>
            public static ConnectionStatus Zero { get; } = new()
            {
                BytesAvailableOnSocket = 0,
                BytesInReadPipe = 0,
                BytesInWritePipe = 0,
                ReadStatus = ReadStatus.NA,
                WriteStatus = WriteStatus.NA,
            };
        }

        public ConnectionStatus GetStatus()
        {
            if (_ioPipe is SocketConnection conn)
            {
                var counters = conn.GetCounters();
                return new ConnectionStatus()
                {
                    MessagesSentAwaitingResponse = GetSentAwaitingResponseCount(),
                    BytesAvailableOnSocket = counters.BytesAvailableOnSocket,
                    BytesInReadPipe = counters.BytesWaitingToBeRead,
                    BytesInWritePipe = counters.BytesWaitingToBeSent,
                    ReadStatus = _readStatus,
                    WriteStatus = _writeStatus,
                };
            }

            // Fall back to bytes waiting on the socket if we can
            int fallbackBytesAvailable;
            try
            {
                fallbackBytesAvailable = VolatileSocket?.Available ?? -1;
            }
            catch
            {
                // If this fails, we're likely in a race disposal situation and do not want to blow sky high here.
                fallbackBytesAvailable = -1;
            }

            return new ConnectionStatus()
            {
                BytesAvailableOnSocket = fallbackBytesAvailable,
                BytesInReadPipe = -1,
                BytesInWritePipe = -1,
                ReadStatus = _readStatus,
                WriteStatus = _writeStatus,
            };
        }

        private static RemoteCertificateValidationCallback GetAmbientIssuerCertificateCallback()
        {
            try
            {
                var issuerPath = Environment.GetEnvironmentVariable("SERedis_IssuerCertPath");
                if (!string.IsNullOrEmpty(issuerPath)) return ConfigurationOptions.TrustIssuerCallback(issuerPath);
            }
            catch (Exception ex)
            {
                Debug.WriteLine(ex.Message);
            }
            return null;
        }
        private static LocalCertificateSelectionCallback GetAmbientClientCertificateCallback()
        {
            try
            {
                var pfxPath = Environment.GetEnvironmentVariable("SERedis_ClientCertPfxPath");
                var pfxPassword = Environment.GetEnvironmentVariable("SERedis_ClientCertPassword");
                var pfxStorageFlags = Environment.GetEnvironmentVariable("SERedis_ClientCertStorageFlags");

                X509KeyStorageFlags? flags = null;
                if (!string.IsNullOrEmpty(pfxStorageFlags))
                {
                    flags = Enum.Parse(typeof(X509KeyStorageFlags), pfxStorageFlags) as X509KeyStorageFlags?;
                }

                if (!string.IsNullOrEmpty(pfxPath) && File.Exists(pfxPath))
                {
                    return delegate { return new X509Certificate2(pfxPath, pfxPassword ?? "", flags ?? X509KeyStorageFlags.DefaultKeySet); };
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine(ex.Message);
            }
            return null;
        }

        internal async ValueTask<bool> ConnectedAsync(Socket socket, LogProxy log, SocketManager manager)
        {
            var bridge = BridgeCouldBeNull;
            if (bridge == null) return false;

            IDuplexPipe pipe = null;
            try
            {
                // disallow connection in some cases
                OnDebugAbort();

                // the order is important here:
                // non-TLS: [Socket]<==[SocketConnection:IDuplexPipe]
                // TLS:     [Socket]<==[NetworkStream]<==[SslStream]<==[StreamConnection:IDuplexPipe]

                var config = bridge.Multiplexer.RawConfig;

                if (config.Ssl)
                {
                    log?.WriteLine("Configuring TLS");
                    var host = config.SslHost;
                    if (string.IsNullOrWhiteSpace(host)) host = Format.ToStringHostOnly(bridge.ServerEndPoint.EndPoint);

                    var ssl = new SslStream(new NetworkStream(socket), false,
                        config.CertificateValidationCallback ?? GetAmbientIssuerCertificateCallback(),
                        config.CertificateSelectionCallback ?? GetAmbientClientCertificateCallback(),
                        EncryptionPolicy.RequireEncryption);
                    try
                    {
                        try
                        {
                            ssl.AuthenticateAsClient(host, config.SslProtocols, config.CheckCertificateRevocation);
                        }
                        catch (Exception ex)
                        {
                            Debug.WriteLine(ex.Message);
                            bridge.Multiplexer?.SetAuthSuspect();
                            throw;
                        }
                        log?.WriteLine($"TLS connection established successfully using protocol: {ssl.SslProtocol}");
                    }
                    catch (AuthenticationException authexception)
                    {
                        RecordConnectionFailed(ConnectionFailureType.AuthenticationFailure, authexception, isInitialConnect: true);
                        bridge.Multiplexer.Trace("Encryption failure");
                        return false;
                    }
                    pipe = StreamConnection.GetDuplex(ssl, manager.SendPipeOptions, manager.ReceivePipeOptions, name: bridge.Name);
                }
                else
                {
                    pipe = SocketConnection.Create(socket, manager.SendPipeOptions, manager.ReceivePipeOptions, name: bridge.Name);
                }
                OnWrapForLogging(ref pipe, _physicalName, manager);

                _ioPipe = pipe;

                log?.WriteLine($"{bridge?.Name}: Connected ");

                await bridge.OnConnectedAsync(this, log).ForAwait();
                return true;
            }
            catch (Exception ex)
            {
                RecordConnectionFailed(ConnectionFailureType.InternalFailure, ex, isInitialConnect: true, connectingPipe: pipe); // includes a bridge.OnDisconnected
                bridge.Multiplexer.Trace("Could not connect: " + ex.Message, ToString());
                return false;
            }
        }

        private void MatchResult(in RawResult result)
        {
            throw new NotImplementedException();
        }

        private volatile Message _activeMessage;

        internal void GetHeadMessages(out Message now, out Message next)
        {
            now = _activeMessage;
            lock (_writtenAwaitingResponse)
            {
                next = _writtenAwaitingResponse.Count == 0 ? null : _writtenAwaitingResponse.Peek();
            }
        }

        partial void OnCloseEcho();

        partial void OnCreateEcho();

        private void OnDebugAbort()
        {
            var bridge = BridgeCouldBeNull;
            if (bridge == null || !bridge.Multiplexer.AllowConnect)
            {
                throw new RedisConnectionException(ConnectionFailureType.InternalFailure, "Aborting (AllowConnect: False)");
            }
        }

        partial void OnWrapForLogging(ref IDuplexPipe pipe, string name, SocketManager mgr);

        internal void UpdateLastReadTime() => Interlocked.Exchange(ref lastReadTickCount, Environment.TickCount);

        private async Task ReadFromPipe()
        {
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

        private int ProcessBuffer(ref ReadOnlySequence<byte> buffer)
        {
            int messageCount = 0;

            while (!buffer.IsEmpty)
            {
                _readStatus = ReadStatus.TryParseResult;
                var reader = new BufferReader(buffer);
                var result = TryParseResult(RefCountedMemoryPool<RawResult>.Shared, ref reader, IncludeDetailInExceptions, BridgeCouldBeNull?.ServerEndPoint);
                try
                {
                    if (result.HasValue)
                    {
                        buffer = reader.SliceFromCurrent();

                        messageCount++;
                        Trace(result.ToString());
                        _readStatus = ReadStatus.MatchResult;
                        MatchResult(result);
                    }
                    else
                    {
                        break; // remaining buffer isn't enough; give up
                    }
                }
                finally
                {
                    _readStatus = ReadStatus.ResetArena;
                }
            }
            _readStatus = ReadStatus.ProcessBufferComplete;
            return messageCount;
        }
        //void ISocketCallback.Read()
        //{
        //    Interlocked.Increment(ref haveReader);
        //    try
        //    {
        //        do
        //        {
        //            int space = EnsureSpaceAndComputeBytesToRead();
        //            int bytesRead = netStream?.Read(ioBuffer, ioBufferBytes, space) ?? 0;

        //            if (!ProcessReadBytes(bytesRead)) return; // EOF
        //        } while (socketToken.Available != 0);
        //        Multiplexer.Trace("Buffer exhausted", physicalName);
        //        // ^^^ note that the socket manager will call us again when there is something to do
        //    }
        //    catch (Exception ex)
        //    {
        //        RecordConnectionFailed(ConnectionFailureType.InternalFailure, ex);
        //    }
        //    finally
        //    {
        //        Interlocked.Decrement(ref haveReader);
        //    }
        //}

        private static RawResult ReadArray(IAllocator<RawResult> allocator, ref BufferReader reader, bool includeDetailInExceptions, ServerEndPoint server)
        {
            var itemCount = ReadLineTerminatedString(ResultType.Integer, ref reader);
            if (itemCount.HasValue)
            {
                if (!itemCount.TryGetInt64(out long i64)) throw ExceptionFactory.ConnectionFailure(includeDetailInExceptions, ConnectionFailureType.ProtocolFailure, "Invalid array length", server);
                int itemCountActual = checked((int)i64);

                if (itemCountActual < 0)
                {
                    //for null response by command like EXEC, RESP array: *-1\r\n
                    return RawResult.NullMultiBulk;
                }
                else if (itemCountActual == 0)
                {
                    //for zero array response by command like SCAN, Resp array: *0\r\n 
                    return RawResult.EmptyMultiBulk;
                }

                var memory = allocator.Allocate(itemCountActual);
                //var seq = memory.AsReadOnlySequence();
                var result = new RawResult(memory.AsReadOnlySequence());
                var span = memory.Span;
                for (int i = 0; i < span.Length; i++)
                {
                    if (!(span[i] = TryParseResult(allocator, ref reader, includeDetailInExceptions, server)).HasValue)
                    {
                        memory.Release();
                        return RawResult.Nil;
                    }
                }
                return result;
                //if (seq.IsSingleSegment)
                //{
                //    var span = seq.First.Span;
                //    for(int i = 0; i < span.Length; i++)
                //    {
                //        if (!(span[i] = TryParseResult(allocator, ref reader, includeDetailInExceptions, server)).HasValue)
                //        {
                //            return RawResult.Nil;
                //        }
                //    }
                //}
                //else
                //{
                //    foreach(var segment in seq)
                //    {
                //        var span = segment.Span;
                //        for (int i = 0; i < span.Length; i++)
                //        {
                //            if (!(span[i] = TryParseResult(allocator, ref reader, includeDetailInExceptions, server)).HasValue)
                //            {
                //                return RawResult.Nil;
                //            }
                //        }
                //    }
                //}
            }
            return RawResult.Nil;
        }

        private static RawResult ReadBulkString(ref BufferReader reader, bool includeDetailInExceptions, ServerEndPoint server)
        {
            var prefix = ReadLineTerminatedString(ResultType.Integer, ref reader);
            if (prefix.HasValue)
            {
                if (!prefix.TryGetInt64(out long i64)) throw ExceptionFactory.ConnectionFailure(includeDetailInExceptions, ConnectionFailureType.ProtocolFailure, "Invalid bulk string length", server);
                int bodySize = checked((int)i64);
                if (bodySize < 0)
                {
                    return new RawResult(ResultType.BulkString, ReadOnlySequence<byte>.Empty, true);
                }

                if (reader.TryConsumeAsBuffer(bodySize, out var payload))
                {
                    switch (reader.TryConsumeCRLF())
                    {
                        case ConsumeResult.NeedMoreData:
                            break; // see NilResult below
                        case ConsumeResult.Success:
                            return new RawResult(ResultType.BulkString, payload, false);
                        default:
                            throw ExceptionFactory.ConnectionFailure(includeDetailInExceptions, ConnectionFailureType.ProtocolFailure, "Invalid bulk string terminator", server);
                    }
                }
            }
            return RawResult.Nil;
        }

        private static RawResult ReadLineTerminatedString(ResultType type, ref BufferReader reader)
        {
            int crlfOffsetFromCurrent = BufferReader.FindNextCrLf(reader);
            if (crlfOffsetFromCurrent < 0) return RawResult.Nil;

            var payload = reader.ConsumeAsBuffer(crlfOffsetFromCurrent);
            reader.Consume(2);

            return new RawResult(type, payload, false);
        }

        internal enum ReadStatus
        {
            NotStarted,
            Init,
            RanToCompletion,
            Faulted,
            ReadSync,
            ReadAsync,
            UpdateWriteTime,
            ProcessBuffer,
            MarkProcessed,
            TryParseResult,
            MatchResult,
            PubSubMessage,
            PubSubPMessage,
            Reconfigure,
            InvokePubSub,
            DequeueResult,
            ComputeResult,
            CompletePendingMessageSync,
            CompletePendingMessageAsync,
            MatchResultComplete,
            ResetArena,
            ProcessBufferComplete,
            NA = -1,
        }
        private volatile ReadStatus _readStatus;
        internal ReadStatus GetReadStatus() => _readStatus;

        internal void StartReading() => ReadFromPipe().RedisFireAndForget();

        internal static RawResult TryParseResult(IAllocator<RawResult> allocator, ref BufferReader reader,
            bool includeDetilInExceptions, ServerEndPoint server, bool allowInlineProtocol = false)
        {
            var prefix = reader.PeekByte();
            if (prefix < 0) return RawResult.Nil; // EOF
            switch (prefix)
            {
                case '+': // simple string
                    reader.Consume(1);
                    return ReadLineTerminatedString(ResultType.SimpleString, ref reader);
                case '-': // error
                    reader.Consume(1);
                    return ReadLineTerminatedString(ResultType.Error, ref reader);
                case ':': // integer
                    reader.Consume(1);
                    return ReadLineTerminatedString(ResultType.Integer, ref reader);
                case '$': // bulk string
                    reader.Consume(1);
                    return ReadBulkString(ref reader, includeDetilInExceptions, server);
                case '*': // array
                    reader.Consume(1);
                    return ReadArray(allocator, ref reader, includeDetilInExceptions, server);
                default:
                    // string s = Format.GetString(buffer);
                    if (allowInlineProtocol) return ParseInlineProtocol(allocator, ReadLineTerminatedString(ResultType.SimpleString, ref reader));
                    throw new InvalidOperationException("Unexpected response prefix: " + (char)prefix);
            }
        }

        private static RawResult ParseInlineProtocol(IAllocator<RawResult> arena, in RawResult line)
        {
            if (!line.HasValue) return RawResult.Nil; // incomplete line

            int count = 0;
            foreach (var _ in line.GetInlineTokenizer()) count++;
            var block = arena.Allocate(count);

            var span = block.Span;
            int index = 0;
            foreach (var token in line.GetInlineTokenizer())
            {
                span[index++] = new RawResult(line.Type, token, false);
            }
            return new RawResult(block.AsReadOnlySequence());
        }
    }
}
