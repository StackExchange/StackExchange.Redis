using Pipelines.Sockets.Unofficial;
using Pipelines.Sockets.Unofficial.Arenas;
using System;
using System.Buffers;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
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
using Microsoft.Extensions.Logging;
using static StackExchange.Redis.Message;

namespace StackExchange.Redis
{
    internal sealed partial class PhysicalConnection : IDisposable
    {
        internal readonly byte[]? ChannelPrefix;

        private const int DefaultRedisDatabaseCount = 16;

        private static readonly CommandBytes message = "message", pmessage = "pmessage";

        private static readonly Message[] ReusableChangeDatabaseCommands = Enumerable.Range(0, DefaultRedisDatabaseCount).Select(
            i => Message.Create(i, CommandFlags.FireAndForget, RedisCommand.SELECT)).ToArray();

        private static readonly Message
            ReusableReadOnlyCommand = Message.Create(-1, CommandFlags.FireAndForget, RedisCommand.READONLY),
            ReusableReadWriteCommand = Message.Create(-1, CommandFlags.FireAndForget, RedisCommand.READWRITE);

        private static int totalCount;

        private readonly ConnectionType connectionType;

        // things sent to this physical, but not yet received
        private readonly Queue<Message> _writtenAwaitingResponse = new Queue<Message>();

        private readonly string _physicalName;

        private volatile int currentDatabase = 0;

        private ReadMode currentReadMode = ReadMode.NotSpecified;

        private int failureReported;

        private int lastWriteTickCount, lastReadTickCount, lastBeatTickCount;

        private long bytesLastResult;
        private long bytesInBuffer;
        internal long? ConnectionId { get; set; }

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

        /// <summary>
        /// Nullable because during simulation of failure, we'll null out.
        /// ...but in those cases, we'll accept any null ref in a race - it's fine.
        /// </summary>
        private IDuplexPipe? _ioPipe;
        internal bool HasOutputPipe => _ioPipe?.Output != null;

        private Socket? _socket;
        internal Socket? VolatileSocket => Volatile.Read(ref _socket);

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

        internal async Task BeginConnectAsync(ILogger? log)
        {
            var bridge = BridgeCouldBeNull;
            var endpoint = bridge?.ServerEndPoint?.EndPoint;
            if (bridge == null || endpoint == null)
            {
                log?.LogError(new ArgumentNullException(nameof(endpoint)), "No endpoint");
                return;
            }

            Trace("Connecting...");
            var tunnel = bridge.Multiplexer.RawConfig.Tunnel;
            var connectTo = endpoint;
            if (tunnel is not null)
            {
                connectTo = await tunnel.GetSocketConnectEndpointAsync(endpoint, CancellationToken.None).ForAwait();
            }
            if (connectTo is not null)
            {
                _socket = SocketManager.CreateSocket(connectTo);
            }

            if (_socket is not null)
            {
                bridge.Multiplexer.RawConfig.BeforeSocketConnect?.Invoke(endpoint, bridge.ConnectionType, _socket);
                if (tunnel is not null)
                {   // same functionality as part of a tunnel
                    await tunnel.BeforeSocketConnectAsync(endpoint, bridge.ConnectionType, _socket, CancellationToken.None).ForAwait();
                }
            }
            bridge.Multiplexer.OnConnecting(endpoint, bridge.ConnectionType);
            log?.LogInformation($"{Format.ToString(endpoint)}: BeginConnectAsync");

            CancellationTokenSource? timeoutSource = null;
            try
            {
                using (var args = connectTo is null ? null : new SocketAwaitableEventArgs
                {
                    RemoteEndPoint = connectTo,
                })
                {
                    var x = VolatileSocket;
                    if (x == null)
                    {
                        args?.Abort();
                    }
                    else if (args is not null && x.ConnectAsync(args))
                    {   // asynchronous operation is pending
                        timeoutSource = ConfigureTimeout(args, bridge.Multiplexer.RawConfig.ConnectTimeout);
                    }
                    else
                    {   // completed synchronously
                        args?.Complete();
                    }

                    // Complete connection
                    try
                    {
                        // If we're told to ignore connect, abort here
                        if (BridgeCouldBeNull?.Multiplexer?.IgnoreConnect ?? false) return;

                        if (args is not null)
                        {
                            await args; // wait for the connect to complete or fail (will throw)
                        }
                        if (timeoutSource != null)
                        {
                            timeoutSource.Cancel();
                            timeoutSource.Dispose();
                        }

                        x = VolatileSocket;
                        if (x == null && args is not null)
                        {
                            ConnectionMultiplexer.TraceWithoutContext("Socket was already aborted");
                        }
                        else if (await ConnectedAsync(x, log, bridge.Multiplexer.SocketManager!).ForAwait())
                        {
                            log?.LogInformation($"{Format.ToString(endpoint)}: Starting read");
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
                    catch (ObjectDisposedException ex)
                    {
                        log?.LogError(ex, $"{Format.ToString(endpoint)}: (socket shutdown)");
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
                    var a = (SocketAwaitableEventArgs)state!;
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
        public PhysicalBridge? BridgeCouldBeNull => (PhysicalBridge?)_bridge.Target;

        public long LastReadSecondsAgo => unchecked(Environment.TickCount - Thread.VolatileRead(ref lastReadTickCount)) / 1000;
        public long LastWriteSecondsAgo => unchecked(Environment.TickCount - Thread.VolatileRead(ref lastWriteTickCount)) / 1000;

        private bool IncludeDetailInExceptions => BridgeCouldBeNull?.Multiplexer.RawConfig.IncludeDetailInExceptions ?? false;

        [Conditional("VERBOSE")]
        internal void Trace(string message) => BridgeCouldBeNull?.Multiplexer?.Trace(message, ToString());

        public long SubscriptionCount { get; set; }

        public bool TransactionActive { get; internal set; }

        private RedisProtocol _protocol; // note starts at **zero**, not RESP2
        public RedisProtocol? Protocol => _protocol == 0 ? null : _protocol;
        internal void SetProtocol(RedisProtocol value) => _protocol = value;

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
            _arena.Dispose();
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

        /// <summary>
        /// Did we ask for the shutdown? If so this leads to informational messages for tracking but not errors.
        /// </summary>
        private bool IsRequestedShutdown(PipeShutdownKind kind) => kind switch
        {
            PipeShutdownKind.ProtocolExitClient => true,
            _ => false,
        };

        public void RecordConnectionFailed(
            ConnectionFailureType failureType,
            Exception? innerException = null,
            [CallerMemberName] string? origin = null,
            bool isInitialConnect = false,
            IDuplexPipe? connectingPipe = null
            )
        {
            bool weAskedForThis = false;
            Exception? outerException = innerException;
            IdentifyFailureType(innerException, ref failureType);
            var bridge = BridgeCouldBeNull;
            if (_ioPipe != null || isInitialConnect) // if *we* didn't burn the pipe: flag it
            {
                if (failureType == ConnectionFailureType.InternalFailure && innerException is not null)
                {
                    OnInternalError(innerException, origin);
                }

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
                        if (_writtenAwaitingResponse.TryPeek(out var next))
                        {
                            unansweredWriteTime = next.GetWriteTime();
                        }
                    }

                    var exMessage = new StringBuilder(failureType.ToString());

                    var pipe = connectingPipe ?? _ioPipe;
                    if (pipe is SocketConnection sc)
                    {
                        // If the reason for the shutdown was we asked for the socket to die, don't log it as an error (only informational)
                        weAskedForThis = IsRequestedShutdown(sc.ShutdownKind);

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

                    var data = new List<Tuple<string, string?>>();
                    void add(string lk, string sk, string? v)
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

                            data.Add(Tuple.Create<string, string?>("FailureType", failureType.ToString()));
                            data.Add(Tuple.Create<string, string?>("EndPoint", Format.ToString(bridge.ServerEndPoint?.EndPoint)));

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

                            add("Last-Heartbeat", "last-heartbeat", (lastBeat == 0 ? "never" : ((unchecked(now - lastBeat) / 1000) + "s ago")) + (bridge.IsBeating ? " (mid-beat)" : ""));
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

                    bridge?.OnConnectionFailed(this, failureType, outerException, wasRequested: weAskedForThis);
                }
            }
            // clean up (note: avoid holding the lock when we complete things, even if this means taking
            // the lock multiple times; this is fine here - we shouldn't be fighting anyone, and we're already toast)
            lock (_writtenAwaitingResponse)
            {
                bridge?.Trace(_writtenAwaitingResponse.Count != 0, "Failing outstanding messages: " + _writtenAwaitingResponse.Count);
            }

            while (TryDequeueLocked(_writtenAwaitingResponse, out var next))
            {
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
                    next.SetExceptionAndComplete(ex!, bridge);
                }
            }

            // burn the socket
            Shutdown();

            static bool TryDequeueLocked(Queue<Message> queue, [NotNullWhen(true)] out Message? message)
            {
                lock (queue)
                {
                    return queue.TryDequeue(out message);
                }
            }
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

        internal static void IdentifyFailureType(Exception? exception, ref ConnectionFailureType failureType)
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
            var multiplexer = BridgeCouldBeNull?.Multiplexer;
            if (multiplexer is null)
            {
                // multiplexer already collected? then we're almost certainly doomed;
                // we can still process it to avoid making things worse/more complex,
                // but: we can't reliably assume this works, so: shout now!
                next.Cancel();
                next.Complete();
            }

            bool wasEmpty;
            lock (_writtenAwaitingResponse)
            {
                wasEmpty = _writtenAwaitingResponse.Count == 0;
                _writtenAwaitingResponse.Enqueue(next);
            }
            if (wasEmpty)
            {
                // it is important to do this *after* adding, so that we can't
                // get into a thread-race where the heartbeat checks too fast;
                // the fact that we're accessing Multiplexer down here means that
                // we're rooting it ourselves via the stack, so we don't need
                // to worry about it being collected until at least after this
                multiplexer?.Root();
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

        internal Message? GetReadModeCommand(bool isPrimaryOnly)
        {
            if (BridgeCouldBeNull?.ServerEndPoint?.RequiresReadMode == true)
            {
                ReadMode requiredReadMode = isPrimaryOnly ? ReadMode.ReadWrite : ReadMode.ReadOnly;
                if (requiredReadMode != currentReadMode)
                {
                    currentReadMode = requiredReadMode;
                    switch (requiredReadMode)
                    {
                        case ReadMode.ReadOnly: return ReusableReadOnlyCommand;
                        case ReadMode.ReadWrite: return ReusableReadWriteCommand;
                    }
                }
            }
            else if (currentReadMode == ReadMode.ReadOnly)
            {
                // we don't need it (because we're not a cluster, or not a replica),
                // but we are in read-only mode; switch to read-write
                currentReadMode = ReadMode.ReadWrite;
                return ReusableReadWriteCommand;
            }
            return null;
        }

        internal Message? GetSelectDatabaseCommand(int targetDatabase, Message message)
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
        {
            return targetDatabase < DefaultRedisDatabaseCount
                   ? ReusableChangeDatabaseCommands[targetDatabase] // 0-15 by default
                   : Message.Create(targetDatabase, CommandFlags.FireAndForget, RedisCommand.SELECT);
        }

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

        /// <summary>
        /// Runs on every heartbeat for a bridge, timing out any commands that are overdue and returning an integer of how many we timed out.
        /// </summary>
        /// <returns>How many commands were overdue and threw timeout exceptions.</returns>
        internal int OnBridgeHeartbeat()
        {
            var result = 0;
            var now = Environment.TickCount;
            Interlocked.Exchange(ref lastBeatTickCount, now);

            lock (_writtenAwaitingResponse)
            {
                if (_writtenAwaitingResponse.Count != 0 && BridgeCouldBeNull is PhysicalBridge bridge)
                {
                    var server = bridge.ServerEndPoint;
                    var multiplexer = bridge.Multiplexer;
                    var timeout = multiplexer.AsyncTimeoutMilliseconds;
                    foreach (var msg in _writtenAwaitingResponse)
                    {
                        // We only handle async timeouts here, synchronous timeouts are handled upstream.
                        // Those sync timeouts happen in ConnectionMultiplexer.ExecuteSyncImpl() via Monitor.Wait.
                        if (msg.HasTimedOut(now, timeout, out var elapsed))
                        {
                            if (msg.ResultBoxIsAsync)
                            {
                                bool haveDeltas = msg.TryGetPhysicalState(out _, out _, out long sentDelta, out var receivedDelta) && sentDelta >= 0 && receivedDelta >= 0;
                                var timeoutEx = ExceptionFactory.Timeout(multiplexer, haveDeltas
                                    ? $"Timeout awaiting response (outbound={sentDelta >> 10}KiB, inbound={receivedDelta >> 10}KiB, {elapsed}ms elapsed, timeout is {timeout}ms)"
                                    : $"Timeout awaiting response ({elapsed}ms elapsed, timeout is {timeout}ms)", msg, server);
                                multiplexer.OnMessageFaulted(msg, timeoutEx);
                                msg.SetExceptionAndComplete(timeoutEx, bridge); // tell the message that it is doomed
                                multiplexer.OnAsyncTimeout();
                                result++;
                            }
                        }
                        else
                        {
                            // This is a head-of-line queue, which means the first thing we hit that *hasn't* timed out means no more will timeout
                            // and we can stop looping and release the lock early.
                            break;
                        }
                        // Note: it is important that we **do not** remove the message unless we're tearing down the socket; that
                        // would disrupt the chain for MatchResult; we just preemptively abort the message from the caller's
                        // perspective, and set a flag on the message so we don't keep doing it
                    }
                }
            }
            return result;
        }

        internal void OnInternalError(Exception exception, [CallerMemberName] string? origin = null)
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

        internal void Write(in RedisKey key)
        {
            var val = key.KeyValue;
            if (val is string s)
            {
                WriteUnifiedPrefixedString(_ioPipe?.Output, key.KeyPrefix, s);
            }
            else
            {
                WriteUnifiedPrefixedBlob(_ioPipe?.Output, key.KeyPrefix, (byte[]?)val);
            }
        }

        internal void Write(in RedisChannel channel)
            => WriteUnifiedPrefixedBlob(_ioPipe?.Output, ChannelPrefix, channel.Value);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal void WriteBulkString(in RedisValue value)
            => WriteBulkString(value, _ioPipe?.Output);
        internal static void WriteBulkString(in RedisValue value, PipeWriter? maybeNullWriter)
        {
            if (maybeNullWriter is not PipeWriter writer)
            {
                return; // Prevent null refs during disposal
            }

            switch (value.Type)
            {
                case RedisValue.StorageType.Null:
                    WriteUnifiedBlob(writer, (byte[]?)null);
                    break;
                case RedisValue.StorageType.Int64:
                    WriteUnifiedInt64(writer, value.OverlappedValueInt64);
                    break;
                case RedisValue.StorageType.UInt64:
                    WriteUnifiedUInt64(writer, value.OverlappedValueUInt64);
                    break;
                case RedisValue.StorageType.Double: // use string
                case RedisValue.StorageType.String:
                    WriteUnifiedPrefixedString(writer, null, (string?)value);
                    break;
                case RedisValue.StorageType.Raw:
                    WriteUnifiedSpan(writer, ((ReadOnlyMemory<byte>)value).Span);
                    break;
                default:
                    throw new InvalidOperationException($"Unexpected {value.Type} value: '{value}'");
            }
        }

        internal const int REDIS_MAX_ARGS = 1024 * 1024; // there is a <= 1024*1024 max constraint inside redis itself: https://github.com/antirez/redis/blob/6c60526db91e23fb2d666fc52facc9a11780a2a3/src/networking.c#L1024

        internal void WriteHeader(RedisCommand command, int arguments, CommandBytes commandBytes = default)
        {
            if (_ioPipe?.Output is not PipeWriter writer)
            {
                return; // Prevent null refs during disposal
            }

            var bridge = BridgeCouldBeNull ?? throw new ObjectDisposedException(ToString());

            if (command == RedisCommand.UNKNOWN)
            {
                // using >= here because we will be adding 1 for the command itself (which is an arg for the purposes of the multi-bulk protocol)
                if (arguments >= REDIS_MAX_ARGS) throw ExceptionFactory.TooManyArgs(commandBytes.ToString(), arguments);
            }
            else
            {
                // using >= here because we will be adding 1 for the command itself (which is an arg for the purposes of the multi-bulk protocol)
                if (arguments >= REDIS_MAX_ARGS) throw ExceptionFactory.TooManyArgs(command.ToString(), arguments);

                // for everything that isn't custom commands: ask the muxer for the actual bytes
                commandBytes = bridge.Multiplexer.CommandMap.GetBytes(command);
            }

            // in theory we should never see this; CheckMessage dealt with "regular" messages, and
            // ExecuteMessage should have dealt with everything else
            if (commandBytes.IsEmpty) throw ExceptionFactory.CommandDisabled(command);

            // *{argCount}\r\n      = 3 + MaxInt32TextLen
            // ${cmd-len}\r\n       = 3 + MaxInt32TextLen
            // {cmd}\r\n            = 2 + commandBytes.Length
            var span = writer.GetSpan(commandBytes.Length + 8 + Format.MaxInt32TextLen + Format.MaxInt32TextLen);
            span[0] = (byte)'*';

            int offset = WriteRaw(span, arguments + 1, offset: 1);

            offset = AppendToSpanCommand(span, commandBytes, offset: offset);

            writer.Advance(offset);
        }

        internal void RecordQuit() // don't blame redis if we fired the first shot
            => (_ioPipe as SocketConnection)?.TrySetProtocolShutdown(PipeShutdownKind.ProtocolExitClient);

        internal static void WriteMultiBulkHeader(PipeWriter output, long count)
        {
            // *{count}\r\n         = 3 + MaxInt32TextLen
            var span = output.GetSpan(3 + Format.MaxInt32TextLen);
            span[0] = (byte)'*';
            int offset = WriteRaw(span, count, offset: 1);
            output.Advance(offset);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static int WriteCrlf(Span<byte> span, int offset)
        {
            span[offset++] = (byte)'\r';
            span[offset++] = (byte)'\n';
            return offset;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static void WriteCrlf(PipeWriter writer)
        {
            var span = writer.GetSpan(2);
            span[0] = (byte)'\r';
            span[1] = (byte)'\n';
            writer.Advance(2);
        }

        internal static int WriteRaw(Span<byte> span, long value, bool withLengthPrefix = false, int offset = 0)
        {
            if (value >= 0 && value <= 9)
            {
                if (withLengthPrefix)
                {
                    span[offset++] = (byte)'1';
                    offset = WriteCrlf(span, offset);
                }
                span[offset++] = (byte)((int)'0' + (int)value);
            }
            else if (value >= 10 && value < 100)
            {
                if (withLengthPrefix)
                {
                    span[offset++] = (byte)'2';
                    offset = WriteCrlf(span, offset);
                }
                span[offset++] = (byte)((int)'0' + ((int)value / 10));
                span[offset++] = (byte)((int)'0' + ((int)value % 10));
            }
            else if (value >= 100 && value < 1000)
            {
                int v = (int)value;
                int units = v % 10;
                v /= 10;
                int tens = v % 10, hundreds = v / 10;
                if (withLengthPrefix)
                {
                    span[offset++] = (byte)'3';
                    offset = WriteCrlf(span, offset);
                }
                span[offset++] = (byte)((int)'0' + hundreds);
                span[offset++] = (byte)((int)'0' + tens);
                span[offset++] = (byte)((int)'0' + units);
            }
            else if (value < 0 && value >= -9)
            {
                if (withLengthPrefix)
                {
                    span[offset++] = (byte)'2';
                    offset = WriteCrlf(span, offset);
                }
                span[offset++] = (byte)'-';
                span[offset++] = (byte)((int)'0' - (int)value);
            }
            else if (value <= -10 && value > -100)
            {
                if (withLengthPrefix)
                {
                    span[offset++] = (byte)'3';
                    offset = WriteCrlf(span, offset);
                }
                value = -value;
                span[offset++] = (byte)'-';
                span[offset++] = (byte)((int)'0' + ((int)value / 10));
                span[offset++] = (byte)((int)'0' + ((int)value % 10));
            }
            else
            {
                // we're going to write it, but *to the wrong place*
                var availableChunk = span.Slice(offset);
                var formattedLength = Format.FormatInt64(value, availableChunk);
                if (withLengthPrefix)
                {
                    // now we know how large the prefix is: write the prefix, then write the value
                    var prefixLength = Format.FormatInt32(formattedLength, availableChunk);
                    offset += prefixLength;
                    offset = WriteCrlf(span, offset);

                    availableChunk = span.Slice(offset);
                    var finalLength = Format.FormatInt64(value, availableChunk);
                    offset += finalLength;
                    Debug.Assert(finalLength == formattedLength);
                }
                else
                {
                    offset += formattedLength;
                }
            }

            return WriteCrlf(span, offset);
        }

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

        private CancellationTokenSource? _reusableFlushSyncTokenSource;
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

        private static readonly ReadOnlyMemory<byte> NullBulkString = Encoding.ASCII.GetBytes("$-1\r\n"), EmptyBulkString = Encoding.ASCII.GetBytes("$0\r\n\r\n");

        private static void WriteUnifiedBlob(PipeWriter writer, byte[]? value)
        {
            if (value == null)
            {
                // special case:
                writer.Write(NullBulkString.Span);
            }
            else
            {
                WriteUnifiedSpan(writer, new ReadOnlySpan<byte>(value));
            }
        }

        private static void WriteUnifiedSpan(PipeWriter writer, ReadOnlySpan<byte> value)
        {
            // ${len}\r\n           = 3 + MaxInt32TextLen
            // {value}\r\n          = 2 + value.Length

            const int MaxQuickSpanSize = 512;
            if (value.Length == 0)
            {
                // special case:
                writer.Write(EmptyBulkString.Span);
            }
            else if (value.Length <= MaxQuickSpanSize)
            {
                var span = writer.GetSpan(5 + Format.MaxInt32TextLen + value.Length);
                span[0] = (byte)'$';
                int bytes = AppendToSpan(span, value, 1);
                writer.Advance(bytes);
            }
            else
            {
                // too big to guarantee can do in a single span
                var span = writer.GetSpan(3 + Format.MaxInt32TextLen);
                span[0] = (byte)'$';
                int bytes = WriteRaw(span, value.Length, offset: 1);
                writer.Advance(bytes);

                writer.Write(value);

                WriteCrlf(writer);
            }
        }

        private static int AppendToSpanCommand(Span<byte> span, in CommandBytes value, int offset = 0)
        {
            span[offset++] = (byte)'$';
            int len = value.Length;
            offset = WriteRaw(span, len, offset: offset);
            value.CopyTo(span.Slice(offset, len));
            offset += value.Length;
            return WriteCrlf(span, offset);
        }

        private static int AppendToSpan(Span<byte> span, ReadOnlySpan<byte> value, int offset = 0)
        {
            offset = WriteRaw(span, value.Length, offset: offset);
            value.CopyTo(span.Slice(offset, value.Length));
            offset += value.Length;
            return WriteCrlf(span, offset);
        }

        internal void WriteSha1AsHex(byte[] value)
        {
            if (_ioPipe?.Output is not PipeWriter writer)
            {
                return; // Prevent null refs during disposal
            }

            if (value == null)
            {
                writer.Write(NullBulkString.Span);
            }
            else if (value.Length == ResultProcessor.ScriptLoadProcessor.Sha1HashLength)
            {
                // $40\r\n              = 5
                // {40 bytes}\r\n       = 42

                var span = writer.GetSpan(47);
                span[0] = (byte)'$';
                span[1] = (byte)'4';
                span[2] = (byte)'0';
                span[3] = (byte)'\r';
                span[4] = (byte)'\n';

                int offset = 5;
                for (int i = 0; i < value.Length; i++)
                {
                    var b = value[i];
                    span[offset++] = ToHexNibble(b >> 4);
                    span[offset++] = ToHexNibble(b & 15);
                }
                span[offset++] = (byte)'\r';
                span[offset++] = (byte)'\n';

                writer.Advance(offset);
            }
            else
            {
                throw new InvalidOperationException("Invalid SHA1 length: " + value.Length);
            }
        }

        internal static byte ToHexNibble(int value)
        {
            return value < 10 ? (byte)('0' + value) : (byte)('a' - 10 + value);
        }

        internal static void WriteUnifiedPrefixedString(PipeWriter? maybeNullWriter, byte[]? prefix, string? value)
        {
            if (maybeNullWriter is not PipeWriter writer)
            {
                return; // Prevent null refs during disposal
            }

            if (value == null)
            {
                // special case
                writer.Write(NullBulkString.Span);
            }
            else
            {
                // ${total-len}\r\n         3 + MaxInt32TextLen
                // {prefix}{value}\r\n
                int encodedLength = Encoding.UTF8.GetByteCount(value),
                    prefixLength = prefix?.Length ?? 0,
                    totalLength = prefixLength + encodedLength;

                if (totalLength == 0)
                {
                    // special-case
                    writer.Write(EmptyBulkString.Span);
                }
                else
                {
                    var span = writer.GetSpan(3 + Format.MaxInt32TextLen);
                    span[0] = (byte)'$';
                    int bytes = WriteRaw(span, totalLength, offset: 1);
                    writer.Advance(bytes);

                    if (prefixLength != 0) writer.Write(prefix);
                    if (encodedLength != 0) WriteRaw(writer, value, encodedLength);
                    WriteCrlf(writer);
                }
            }
        }

        [ThreadStatic]
        private static Encoder? s_PerThreadEncoder;
        internal static Encoder GetPerThreadEncoder()
        {
            var encoder = s_PerThreadEncoder;
            if (encoder == null)
            {
                s_PerThreadEncoder = encoder = Encoding.UTF8.GetEncoder();
            }
            else
            {
                encoder.Reset();
            }
            return encoder;
        }

        internal static unsafe void WriteRaw(PipeWriter writer, string value, int expectedLength)
        {
            const int MaxQuickEncodeSize = 512;

            fixed (char* cPtr = value)
            {
                int totalBytes;
                if (expectedLength <= MaxQuickEncodeSize)
                {
                    // encode directly in one hit
                    var span = writer.GetSpan(expectedLength);
                    fixed (byte* bPtr = span)
                    {
                        totalBytes = Encoding.UTF8.GetBytes(cPtr, value.Length, bPtr, expectedLength);
                    }
                    writer.Advance(expectedLength);
                }
                else
                {
                    // use an encoder in a loop
                    var encoder = GetPerThreadEncoder();
                    int charsRemaining = value.Length, charOffset = 0;
                    totalBytes = 0;

                    bool final = false;
                    while (true)
                    {
                        var span = writer.GetSpan(5); // get *some* memory - at least enough for 1 character (but hopefully lots more)

                        int charsUsed, bytesUsed;
                        bool completed;
                        fixed (byte* bPtr = span)
                        {
                            encoder.Convert(cPtr + charOffset, charsRemaining, bPtr, span.Length, final, out charsUsed, out bytesUsed, out completed);
                        }
                        writer.Advance(bytesUsed);
                        totalBytes += bytesUsed;
                        charOffset += charsUsed;
                        charsRemaining -= charsUsed;

                        if (charsRemaining <= 0)
                        {
                            if (charsRemaining < 0) throw new InvalidOperationException("String encode went negative");
                            if (completed) break; // fine
                            if (final) throw new InvalidOperationException("String encode failed to complete");
                            final = true; // flush the encoder to one more span, then exit
                        }
                    }
                }
                if (totalBytes != expectedLength) throw new InvalidOperationException("String encode length check failure");
            }
        }

        private static void WriteUnifiedPrefixedBlob(PipeWriter? maybeNullWriter, byte[]? prefix, byte[]? value)
        {
            if (maybeNullWriter is not PipeWriter writer)
            {
                return; // Prevent null refs during disposal
            }

            // ${total-len}\r\n 
            // {prefix}{value}\r\n
            if (prefix == null || prefix.Length == 0 || value == null)
            {   // if no prefix, just use the non-prefixed version;
                // even if prefixed, a null value writes as null, so can use the non-prefixed version
                WriteUnifiedBlob(writer, value);
            }
            else
            {
                var span = writer.GetSpan(3 + Format.MaxInt32TextLen); // note even with 2 max-len, we're still in same text range
                span[0] = (byte)'$';
                int bytes = WriteRaw(span, prefix.LongLength + value.LongLength, offset: 1);
                writer.Advance(bytes);

                writer.Write(prefix);
                writer.Write(value);

                span = writer.GetSpan(2);
                WriteCrlf(span, 0);
                writer.Advance(2);
            }
        }

        private static void WriteUnifiedInt64(PipeWriter writer, long value)
        {
            // note from specification: A client sends to the Redis server a RESP Array consisting of just Bulk Strings.
            // (i.e. we can't just send ":123\r\n", we need to send "$3\r\n123\r\n"

            // ${asc-len}\r\n           = 3 + MaxInt32TextLen
            // {asc}\r\n                = MaxInt64TextLen + 2
            var span = writer.GetSpan(5 + Format.MaxInt32TextLen + Format.MaxInt64TextLen);

            span[0] = (byte)'$';
            var bytes = WriteRaw(span, value, withLengthPrefix: true, offset: 1);
            writer.Advance(bytes);
        }

        private static void WriteUnifiedUInt64(PipeWriter writer, ulong value)
        {
            // note from specification: A client sends to the Redis server a RESP Array consisting of just Bulk Strings.
            // (i.e. we can't just send ":123\r\n", we need to send "$3\r\n123\r\n"

            // ${asc-len}\r\n           = 3 + MaxInt32TextLen
            // {asc}\r\n                = MaxInt64TextLen + 2
            var span = writer.GetSpan(5 + Format.MaxInt32TextLen + Format.MaxInt64TextLen);

            Span<byte> valueSpan = stackalloc byte[Format.MaxInt64TextLen];
            var len = Format.FormatUInt64(value, valueSpan);
            span[0] = (byte)'$';
            int offset = WriteRaw(span, len, withLengthPrefix: false, offset: 1);
            valueSpan.Slice(0, len).CopyTo(span.Slice(offset));
            offset += len;
            offset = WriteCrlf(span, offset);
            writer.Advance(offset);
        }
        internal static void WriteInteger(PipeWriter writer, long value)
        {
            //note: client should never write integer; only server does this

            // :{asc}\r\n                = MaxInt64TextLen + 3
            var span = writer.GetSpan(3 + Format.MaxInt64TextLen);

            span[0] = (byte)':';
            var bytes = WriteRaw(span, value, withLengthPrefix: false, offset: 1);
            writer.Advance(bytes);
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
            /// Byte size of the last result we processed.
            /// </summary>
            public long BytesLastResult { get; init; }
            /// <summary>
            /// Byte size on the buffer that isn't processed yet.
            /// </summary>
            public long BytesInBuffer { get; init; }

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
                    BytesLastResult = bytesLastResult,
                    BytesInBuffer = bytesInBuffer,
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
                BytesLastResult = bytesLastResult,
                BytesInBuffer = bytesInBuffer,
            };
        }

        private static RemoteCertificateValidationCallback? GetAmbientIssuerCertificateCallback()
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
        private static LocalCertificateSelectionCallback? GetAmbientClientCertificateCallback()
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

        internal async ValueTask<bool> ConnectedAsync(Socket? socket, ILogger? log, SocketManager manager)
        {
            var bridge = BridgeCouldBeNull;
            if (bridge == null) return false;

            IDuplexPipe? pipe = null;
            try
            {
                // disallow connection in some cases
                OnDebugAbort();

                // the order is important here:
                // non-TLS: [Socket]<==[SocketConnection:IDuplexPipe]
                // TLS:     [Socket]<==[NetworkStream]<==[SslStream]<==[StreamConnection:IDuplexPipe]

                var config = bridge.Multiplexer.RawConfig;

                var tunnel = config.Tunnel;
                Stream? stream = null;
                if (tunnel is not null)
                {
                    stream = await tunnel.BeforeAuthenticateAsync(bridge.ServerEndPoint.EndPoint, bridge.ConnectionType, socket, CancellationToken.None).ForAwait();
                }

                if (config.Ssl)
                {
                    log?.LogInformation("Configuring TLS");
                    var host = config.SslHost;
                    if (host.IsNullOrWhiteSpace())
                    {
                        host = Format.ToStringHostOnly(bridge.ServerEndPoint.EndPoint);
                    }

                    stream ??= new NetworkStream(socket ?? throw new InvalidOperationException("No socket or stream available - possibly a tunnel error"));
                    var ssl = new SslStream(stream, false,
                        config.CertificateValidationCallback ?? GetAmbientIssuerCertificateCallback(),
                        config.CertificateSelectionCallback ?? GetAmbientClientCertificateCallback(),
                        EncryptionPolicy.RequireEncryption);
                    try
                    {
                        try
                        {
#if NETCOREAPP3_1_OR_GREATER
                            var configOptions = config.SslClientAuthenticationOptions?.Invoke(host);
                            if (configOptions is not null)
                            {
                                await ssl.AuthenticateAsClientAsync(configOptions).ForAwait();
                            }
                            else
                            {
                                ssl.AuthenticateAsClient(host, config.SslProtocols, config.CheckCertificateRevocation);
                            }
#else
                            ssl.AuthenticateAsClient(host, config.SslProtocols, config.CheckCertificateRevocation);
#endif
                        }
                        catch (Exception ex)
                        {
                            Debug.WriteLine(ex.Message);
                            bridge.Multiplexer?.SetAuthSuspect(ex);
                            bridge.Multiplexer?.Logger?.LogError(ex, ex.Message);
                            throw;
                        }
                        log?.LogInformation($"TLS connection established successfully using protocol: {ssl.SslProtocol}");
                    }
                    catch (AuthenticationException authexception)
                    {
                        RecordConnectionFailed(ConnectionFailureType.AuthenticationFailure, authexception, isInitialConnect: true);
                        bridge.Multiplexer.Trace("Encryption failure");
                        return false;
                    }
                    stream = ssl;
                }

                if (stream is not null)
                {
                    pipe = StreamConnection.GetDuplex(stream, manager.SendPipeOptions, manager.ReceivePipeOptions, name: bridge.Name);
                }
                else
                {
                    pipe = SocketConnection.Create(socket, manager.SendPipeOptions, manager.ReceivePipeOptions, name: bridge.Name);
                }
                OnWrapForLogging(ref pipe, _physicalName, manager);

                _ioPipe = pipe;

                log?.LogInformation($"{bridge.Name}: Connected ");

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
            // check to see if it could be an out-of-band pubsub message
            if ((connectionType == ConnectionType.Subscription && result.Resp2TypeArray == ResultType.Array) || result.Resp3Type == ResultType.Push)
            {
                var muxer = BridgeCouldBeNull?.Multiplexer;
                if (muxer == null) return;

                // out of band message does not match to a queued message
                var items = result.GetItems();
                if (items.Length >= 3 && items[0].IsEqual(message))
                {
                    _readStatus = ReadStatus.PubSubMessage;

                    // special-case the configuration change broadcasts (we don't keep that in the usual pub/sub registry)
                    var configChanged = muxer.ConfigurationChangedChannel;
                    if (configChanged != null && items[1].IsEqual(configChanged))
                    {
                        EndPoint? blame = null;
                        try
                        {
                            if (!items[2].IsEqual(CommonReplies.wildcard))
                            {
                                // We don't want to fail here, just trying to identify
                                _ = Format.TryParseEndPoint(items[2].GetString(), out blame);
                            }
                        }
                        catch { /* no biggie */ }
                        Trace("Configuration changed: " + Format.ToString(blame));
                        _readStatus = ReadStatus.Reconfigure;
                        muxer.ReconfigureIfNeeded(blame, true, "broadcast");
                    }

                    // invoke the handlers
                    var channel = items[1].AsRedisChannel(ChannelPrefix, RedisChannel.PatternMode.Literal);
                    Trace("MESSAGE: " + channel);
                    if (!channel.IsNull)
                    {
                        if (TryGetPubSubPayload(items[2], out var payload))
                        {
                            _readStatus = ReadStatus.InvokePubSub;
                            muxer.OnMessage(channel, channel, payload);
                        }
                        // could be multi-message: https://github.com/StackExchange/StackExchange.Redis/issues/2507
                        else if (TryGetMultiPubSubPayload(items[2], out var payloads))
                        {
                            _readStatus = ReadStatus.InvokePubSub;
                            muxer.OnMessage(channel, channel, payloads);
                        }
                    }
                    return; // AND STOP PROCESSING!
                }
                else if (items.Length >= 4 && items[0].IsEqual(pmessage))
                {
                    _readStatus = ReadStatus.PubSubPMessage;

                    var channel = items[2].AsRedisChannel(ChannelPrefix, RedisChannel.PatternMode.Literal);
                    Trace("PMESSAGE: " + channel);
                    if (!channel.IsNull)
                    {
                        if (TryGetPubSubPayload(items[3], out var payload))
                        {
                            var sub = items[1].AsRedisChannel(ChannelPrefix, RedisChannel.PatternMode.Pattern);
                            _readStatus = ReadStatus.InvokePubSub;
                            muxer.OnMessage(sub, channel, payload);
                        }
                        else if (TryGetMultiPubSubPayload(items[3], out var payloads))
                        {
                            var sub = items[1].AsRedisChannel(ChannelPrefix, RedisChannel.PatternMode.Pattern);
                            _readStatus = ReadStatus.InvokePubSub;
                            muxer.OnMessage(sub, channel, payloads);
                        }
                    }
                    return; // AND STOP PROCESSING!
                }

                // if it didn't look like "[p]message", then we still need to process the pending queue
            }
            Trace("Matching result...");
            Message? msg;
            _readStatus = ReadStatus.DequeueResult;
            lock (_writtenAwaitingResponse)
            {
                if (!_writtenAwaitingResponse.TryDequeue(out msg))
                {
                    throw new InvalidOperationException("Received response with no message waiting: " + result.ToString());
                }
            }
            _activeMessage = msg;

            Trace("Response to: " + msg);
            _readStatus = ReadStatus.ComputeResult;
            if (msg.ComputeResult(this, result))
            {
                _readStatus = msg.ResultBoxIsAsync ? ReadStatus.CompletePendingMessageAsync : ReadStatus.CompletePendingMessageSync;
                msg.Complete();
            }
            _readStatus = ReadStatus.MatchResultComplete;
            _activeMessage = null;

            static bool TryGetPubSubPayload(in RawResult value, out RedisValue parsed, bool allowArraySingleton = true)
            {
                if (value.IsNull)
                {
                    parsed = RedisValue.Null;
                    return true;
                }
                switch (value.Resp2TypeBulkString)
                {
                    case ResultType.Integer:
                    case ResultType.SimpleString:
                    case ResultType.BulkString:
                        parsed = value.AsRedisValue();
                        return true;
                    case ResultType.Array when allowArraySingleton && value.ItemsCount == 1:
                        return TryGetPubSubPayload(in value[0], out parsed, allowArraySingleton: false);
                }
                parsed = default;
                return false;
            }

            static bool TryGetMultiPubSubPayload(in RawResult value, out Sequence<RawResult> parsed)
            {
                if (value.Resp2TypeArray == ResultType.Array && value.ItemsCount != 0)
                {
                    parsed = value.GetItems();
                    return true;
                }
                parsed = default;
                return false;
            }
        }

        private volatile Message? _activeMessage;

        internal void GetHeadMessages(out Message? now, out Message? next)
        {
            now = _activeMessage;
            bool haveLock = false;
            try
            {
                // careful locking here; a: don't try too hard (this is error info only), b: avoid deadlock (see #2376)
                Monitor.TryEnter(_writtenAwaitingResponse, 10, ref haveLock);
                if (haveLock)
                {
                    _writtenAwaitingResponse.TryPeek(out next);
                }
                else
                {
                    next = UnknownMessage.Instance;
                }
            }
            finally
            {
                if (haveLock) Monitor.Exit(_writtenAwaitingResponse);
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

        private static readonly ArenaOptions s_arenaOptions = new ArenaOptions();
        private readonly Arena<RawResult> _arena = new Arena<RawResult>(s_arenaOptions);

        private int ProcessBuffer(ref ReadOnlySequence<byte> buffer)
        {
            int messageCount = 0;
            bytesInBuffer = buffer.Length;

            while (!buffer.IsEmpty)
            {
                _readStatus = ReadStatus.TryParseResult;
                var reader = new BufferReader(buffer);
                var result = TryParseResult(_protocol >= RedisProtocol.Resp3, _arena, in buffer, ref reader, IncludeDetailInExceptions, this);
                try
                {
                    if (result.HasValue)
                    {
                        buffer = reader.SliceFromCurrent();

                        messageCount++;
                        Trace(result.ToString());
                        _readStatus = ReadStatus.MatchResult;
                        MatchResult(result);

                        // Track the last result size *after* processing for the *next* error message
                        bytesInBuffer = buffer.Length;
                        bytesLastResult = result.Payload.Length;
                    }
                    else
                    {
                        break; // remaining buffer isn't enough; give up
                    }
                }
                finally
                {
                    _readStatus = ReadStatus.ResetArena;
                    _arena.Reset();
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

        private static RawResult.ResultFlags AsNull(RawResult.ResultFlags flags) => flags & ~RawResult.ResultFlags.NonNull;

        private static RawResult ReadArray(ResultType resultType, RawResult.ResultFlags flags, Arena<RawResult> arena, in ReadOnlySequence<byte> buffer, ref BufferReader reader, bool includeDetailInExceptions, ServerEndPoint? server)
        {
            var itemCount = ReadLineTerminatedString(ResultType.Integer, flags, ref reader);
            if (itemCount.HasValue)
            {
                if (!itemCount.TryGetInt64(out long i64)) throw ExceptionFactory.ConnectionFailure(includeDetailInExceptions, ConnectionFailureType.ProtocolFailure,
                     itemCount.Is('?') ? "Streamed aggregate types not yet implemented" : "Invalid array length", server);
                int itemCountActual = checked((int)i64);

                if (itemCountActual < 0)
                {
                    //for null response by command like EXEC, RESP array: *-1\r\n
                    return new RawResult(resultType, items: default, AsNull(flags));
                }
                else if (itemCountActual == 0)
                {
                    //for zero array response by command like SCAN, Resp array: *0\r\n 
                    return new RawResult(resultType, items: default, flags);
                }

                if (resultType == ResultType.Map) itemCountActual <<= 1; // if it says "3", it means 3 pairs, i.e. 6 values

                var oversized = arena.Allocate(itemCountActual);
                var result = new RawResult(resultType, oversized, flags);

                if (oversized.IsSingleSegment)
                {
                    var span = oversized.FirstSpan;
                    for (int i = 0; i < span.Length; i++)
                    {
                        if (!(span[i] = TryParseResult(flags, arena, in buffer, ref reader, includeDetailInExceptions, server)).HasValue)
                        {
                            return RawResult.Nil;
                        }
                    }
                }
                else
                {
                    foreach (var span in oversized.Spans)
                    {
                        for (int i = 0; i < span.Length; i++)
                        {
                            if (!(span[i] = TryParseResult(flags, arena, in buffer, ref reader, includeDetailInExceptions, server)).HasValue)
                            {
                                return RawResult.Nil;
                            }
                        }
                    }
                }
                return result;
            }
            return RawResult.Nil;
        }

        private static RawResult ReadBulkString(ResultType type, RawResult.ResultFlags flags, ref BufferReader reader, bool includeDetailInExceptions, ServerEndPoint? server)
        {
            var prefix = ReadLineTerminatedString(ResultType.Integer, flags, ref reader);
            if (prefix.HasValue)
            {
                if (!prefix.TryGetInt64(out long i64))
                {
                    throw ExceptionFactory.ConnectionFailure(includeDetailInExceptions, ConnectionFailureType.ProtocolFailure,
                        prefix.Is('?') ? "Streamed strings not yet implemented" : "Invalid bulk string length", server);
                }
                int bodySize = checked((int)i64);
                if (bodySize < 0)
                {
                    return new RawResult(type, ReadOnlySequence<byte>.Empty, AsNull(flags));
                }

                if (reader.TryConsumeAsBuffer(bodySize, out var payload))
                {
                    switch (reader.TryConsumeCRLF())
                    {
                        case ConsumeResult.NeedMoreData:
                            break; // see NilResult below
                        case ConsumeResult.Success:
                            return new RawResult(type, payload, flags);
                        default:
                            throw ExceptionFactory.ConnectionFailure(includeDetailInExceptions, ConnectionFailureType.ProtocolFailure, "Invalid bulk string terminator", server);
                    }
                }
            }
            return RawResult.Nil;
        }

        private static RawResult ReadLineTerminatedString(ResultType type, RawResult.ResultFlags flags, ref BufferReader reader)
        {
            int crlfOffsetFromCurrent = BufferReader.FindNextCrLf(reader);
            if (crlfOffsetFromCurrent < 0) return RawResult.Nil;

            var payload = reader.ConsumeAsBuffer(crlfOffsetFromCurrent);
            reader.Consume(2);

            return new RawResult(type, payload, flags);
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

        internal static RawResult TryParseResult(bool isResp3, Arena<RawResult> arena, in ReadOnlySequence<byte> buffer, ref BufferReader reader,
            bool includeDetilInExceptions, PhysicalConnection? connection, bool allowInlineProtocol = false)
        {
            return TryParseResult(isResp3 ? (RawResult.ResultFlags.Resp3 | RawResult.ResultFlags.NonNull) : RawResult.ResultFlags.NonNull,
                arena, buffer, ref reader, includeDetilInExceptions, connection?.BridgeCouldBeNull?.ServerEndPoint, allowInlineProtocol);
        }

        private static RawResult TryParseResult(RawResult.ResultFlags flags, Arena<RawResult> arena, in ReadOnlySequence<byte> buffer, ref BufferReader reader,
            bool includeDetilInExceptions, ServerEndPoint? server, bool allowInlineProtocol = false)
        {
            int prefix;
            do // this loop is just to allow us to parse (skip) attributes without doing a stack-dive
            {
                prefix = reader.PeekByte();
                if (prefix < 0) return RawResult.Nil; // EOF
                switch (prefix)
                {
                    // RESP2
                    case '+': // simple string
                        reader.Consume(1);
                        return ReadLineTerminatedString(ResultType.SimpleString, flags, ref reader);
                    case '-': // error
                        reader.Consume(1);
                        return ReadLineTerminatedString(ResultType.Error, flags, ref reader);
                    case ':': // integer
                        reader.Consume(1);
                        return ReadLineTerminatedString(ResultType.Integer, flags, ref reader);
                    case '$': // bulk string
                        reader.Consume(1);
                        return ReadBulkString(ResultType.BulkString, flags, ref reader, includeDetilInExceptions, server);
                    case '*': // array
                        reader.Consume(1);
                        return ReadArray(ResultType.Array, flags, arena, in buffer, ref reader, includeDetilInExceptions, server);
                    // RESP3
                    case '_': // null
                        reader.Consume(1);
                        return ReadLineTerminatedString(ResultType.Null, flags, ref reader);
                    case ',': // double
                        reader.Consume(1);
                        return ReadLineTerminatedString(ResultType.Double, flags, ref reader);
                    case '#': // boolean
                        reader.Consume(1);
                        return ReadLineTerminatedString(ResultType.Boolean, flags, ref reader);
                    case '!': // blob error
                        reader.Consume(1);
                        return ReadBulkString(ResultType.BlobError, flags, ref reader, includeDetilInExceptions, server);
                    case '=': // verbatim string
                        reader.Consume(1);
                        return ReadBulkString(ResultType.VerbatimString, flags, ref reader, includeDetilInExceptions, server);
                    case '(': // big number
                        reader.Consume(1);
                        return ReadLineTerminatedString(ResultType.BigInteger, flags, ref reader);
                    case '%': // map
                        reader.Consume(1);
                        return ReadArray(ResultType.Map, flags, arena, in buffer, ref reader, includeDetilInExceptions, server);
                    case '~': // set
                        reader.Consume(1);
                        return ReadArray(ResultType.Set, flags, arena, in buffer, ref reader, includeDetilInExceptions, server);
                    case '|': // attribute
                        reader.Consume(1);
                        var arr = ReadArray(ResultType.Attribute, flags, arena, in buffer, ref reader, includeDetilInExceptions, server);
                        if (!arr.HasValue) return RawResult.Nil; // failed to parse attribute data

                        // for now, we want to just skip attribute data; so
                        // drop whatever we parsed on the floor and keep looking
                        break; // exits the SWITCH, not the DO/WHILE
                    case '>': // push
                        reader.Consume(1);
                        return ReadArray(ResultType.Push, flags, arena, in buffer, ref reader, includeDetilInExceptions, server);
                }
            } while (prefix == '|');

            if (allowInlineProtocol) return ParseInlineProtocol(flags, arena, ReadLineTerminatedString(ResultType.SimpleString, flags, ref reader));
            throw new InvalidOperationException("Unexpected response prefix: " + (char)prefix);
        }

        private static RawResult ParseInlineProtocol(RawResult.ResultFlags flags, Arena<RawResult> arena, in RawResult line)
        {
            if (!line.HasValue) return RawResult.Nil; // incomplete line

            int count = 0;
            foreach (var _ in line.GetInlineTokenizer()) count++;
            var block = arena.Allocate(count);

            var iter = block.GetEnumerator();
            foreach (var token in line.GetInlineTokenizer())
            {   // this assigns *via a reference*, returned via the iterator; just... sweet
                iter.GetNext() = new RawResult(line.Resp3Type, token, flags); // spoof RESP2 from RESP1
            }
            return new RawResult(ResultType.Array, block, flags); // spoof RESP2 from RESP1
        }

        internal bool HasPendingCallerFacingItems()
        {
            bool lockTaken = false;
            try
            {
                Monitor.TryEnter(_writtenAwaitingResponse, 0, ref lockTaken);
                if (lockTaken)
                {
                    if (_writtenAwaitingResponse.Count != 0)
                    {
                        foreach (var item in _writtenAwaitingResponse)
                        {
                            if (!item.IsInternalCall) return true;
                        }
                    }
                    return false;
                }
                else
                {
                    // don't contend the lock; *presume* that something
                    // qualifies; we can check again next heartbeat
                    return true;
                }
            }
            finally
            {
                if (lockTaken) Monitor.Exit(_writtenAwaitingResponse);
            }
        }
    }
}
