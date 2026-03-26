using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.IO;
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
        // infrastructure to simulate connection death, debug only
        private partial bool CanCancel();
        [Conditional("DEBUG")]
        partial void OnCancel(bool input, bool output);
#if DEBUG
        private readonly CancellationTokenSource _inputCancel = new(), _outputCancel = new();
        internal CancellationToken InputCancel => _inputCancel.Token;
        internal CancellationToken OutputCancel => _outputCancel.Token;

        partial void OnCancel(bool input, bool output)
        {
            if (input) _inputCancel.Cancel();
            if (output) _outputCancel.Cancel();
        }
        private partial bool CanCancel() => true;
#else
        private partial bool CanCancel() => false;
        internal CancellationToken InputCancel => CancellationToken.None;
        internal CancellationToken OutputCancel => CancellationToken.None;
#endif

        internal readonly byte[]? ChannelPrefix;

        private const int DefaultRedisDatabaseCount = 16;

        private static readonly Message[] ReusableChangeDatabaseCommands = Enumerable.Range(0, DefaultRedisDatabaseCount).Select(
            i => Message.Create(i, CommandFlags.FireAndForget, RedisCommand.SELECT)).ToArray();

        private static readonly Message
            ReusableReadOnlyCommand = Message.Create(-1, CommandFlags.FireAndForget, RedisCommand.READONLY),
            ReusableReadWriteCommand = Message.Create(-1, CommandFlags.FireAndForget, RedisCommand.READWRITE);

        private static int totalCount;

        private readonly ConnectionType connectionType;

        // things sent to this physical, but not yet received
        private readonly Queue<Message> _writtenAwaitingResponse = new Queue<Message>();

        private Message? _awaitingToken;

        private readonly string _physicalName;

        private volatile int currentDatabase = 0;

        private ReadMode currentReadMode = ReadMode.NotSpecified;

        private int failureReported;

        private int clientSentQuit;

        private int lastWriteTickCount, lastReadTickCount, lastBeatTickCount;

        private long bytesLastResult;
        private long bytesInBuffer;
        internal long? ConnectionId { get; set; }

        internal void GetBytes(out long sent, out long received)
        {
            sent = TotalBytesSent;
            received = totalBytesReceived;
        }

        /// <summary>
        /// Nullable because during simulation of failure, we'll null out.
        /// ...but in those cases, we'll accept any null ref in a race - it's fine.
        /// </summary>
        private Stream? _ioStream;

        private Socket? _socket;
        internal Socket? VolatileSocket => Volatile.Read(ref _socket);

        // used for dummy test connections
        public PhysicalConnection(
            ConnectionType connectionType = ConnectionType.Interactive,
            RedisProtocol protocol = RedisProtocol.Resp2,
            Stream? ioStream = null,
            BufferedStreamWriter.WriteMode writeMode = BufferedStreamWriter.WriteMode.Default,
            [CallerMemberName] string name = "")
        {
            lastWriteTickCount = lastReadTickCount = Environment.TickCount;
            lastBeatTickCount = 0;
            this.connectionType = connectionType;
            WriteMode = writeMode;
            _protocol = protocol;
            _bridge = new WeakReference(null);
            _physicalName = name;
            InitOutput(ioStream);
            OnCreateEcho();
        }

        public PhysicalConnection(PhysicalBridge bridge, BufferedStreamWriter.WriteMode writeMode)
        {
            lastWriteTickCount = lastReadTickCount = Environment.TickCount;
            lastBeatTickCount = 0;
            connectionType = bridge.ConnectionType;
            WriteMode = writeMode;
            _bridge = new WeakReference(bridge);
            ChannelPrefix = bridge.Multiplexer.ChannelPrefix;
            if (ChannelPrefix?.Length == 0) ChannelPrefix = null; // null tests are easier than null+empty
            var endpoint = bridge.ServerEndPoint.EndPoint;
            _physicalName = connectionType + "#" + Interlocked.Increment(ref totalCount) + "@" + Format.ToString(endpoint);

            OnCreateEcho();
        }

        // *definitely* multi-database; this can help identify some unusual config scenarios
        internal bool MultiDatabasesOverride { get; set; } // switch to flags-enum if more needed later

#if NET
        private static CancellationTokenSource? _spareTimeoutSource;
#endif

        private static CancellationTokenSource GetTimeout(int milliseconds)
        {
#if NET
            var source = Interlocked.Exchange(ref _spareTimeoutSource, null) ?? new();
#else
            var source = new CancellationTokenSource();
#endif
            source.CancelAfter(milliseconds);
            return source;
        }

        private static void DiscardTimeout(ref CancellationTokenSource? source)
        {
            #if NET // can try to recycle
            if (source is not null
                && source.TryReset()
                && Interlocked.CompareExchange(ref _spareTimeoutSource, source, null) is null)
            {
                // reusable and stashed, nice
                source = null;
            }
            #endif

            if (source is not null)
            {
                try { source.Dispose(); }
                catch { }

                source = null;
            }
        }

        internal async Task BeginConnectAsync(ILogger? log)
        {
            var bridge = BridgeCouldBeNull;
            var endpoint = bridge?.ServerEndPoint?.EndPoint;
            if (bridge == null || endpoint == null)
            {
                log?.LogErrorNoEndpoint(new ArgumentNullException(nameof(endpoint)));
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
                _socket = CreateSocket(connectTo);

                static Socket CreateSocket(EndPoint endpoint)
                {
                    var addressFamily = endpoint.AddressFamily;
                    var protocolType = addressFamily == AddressFamily.Unix ? ProtocolType.Unspecified : ProtocolType.Tcp;

                    var socket = addressFamily == AddressFamily.Unspecified
                        ? new Socket(SocketType.Stream, protocolType)
                        : new Socket(addressFamily, SocketType.Stream, protocolType);
                    socket.SetRecommendedSocketOptions();
                    // socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.Linger, false);
                    return socket;
                }
            }

            if (_socket is not null)
            {
                bridge.Multiplexer.RawConfig.BeforeSocketConnect?.Invoke(endpoint, bridge.ConnectionType, _socket);
                if (tunnel is not null)
                {
                    // same functionality as part of a tunnel
                    await tunnel.BeforeSocketConnectAsync(endpoint, bridge.ConnectionType, _socket, CancellationToken.None).ForAwait();
                }
            }
            bridge.Multiplexer.OnConnecting(endpoint, bridge.ConnectionType);
            log?.LogInformationBeginConnectAsync(new(endpoint));

            CancellationTokenSource? timeoutSource = null;
            try
            {
                ValueTask pendingConnect;
                if (connectTo is not null && VolatileSocket is { } socket)
                {
                    timeoutSource = GetTimeout(bridge.Multiplexer.RawConfig.ConnectTimeout);
                    pendingConnect = socket.ConnectAsync(connectTo, timeoutSource.Token);
                }
                else
                {
                    pendingConnect = default;
                }

                // Complete connection
                try
                {
                    // If we're told to ignore connect, abort here
                    if (BridgeCouldBeNull?.Multiplexer?.IgnoreConnect ?? false) return;

                    await pendingConnect.ForAwait(); // wait for the connect to complete or fail (will throw)
                    DiscardTimeout(ref timeoutSource);

                    socket = VolatileSocket;
                    if (socket is null && connectTo is not null)
                    {
                        ConnectionMultiplexer.TraceWithoutContext("Socket was already aborted");
                    }
                    else if (await ConnectedAsync(socket, log).ForAwait())
                    {
                        log?.LogInformationStartingRead(new(endpoint));
                        try
                        {
                            StartReading(CancellationToken.None); // this already includes InputCancel
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
                    log?.LogErrorSocketShutdown(ex, new(endpoint));
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
            catch (NotImplementedException ex) when (endpoint is not IPEndPoint)
            {
                throw new InvalidOperationException("BeginConnect failed with NotImplementedException; consider using IP endpoints, or enable ResolveDns in the configuration", ex);
            }
            finally
            {
                if (timeoutSource != null) try { timeoutSource.Dispose(); } catch { }
            }
        }

        private enum ReadMode : byte
        {
            NotSpecified,
            ReadOnly,
            ReadWrite,
        }

        private readonly WeakReference _bridge;
        public PhysicalBridge? BridgeCouldBeNull => (PhysicalBridge?)_bridge.Target;

        public long LastReadSecondsAgo => unchecked(Environment.TickCount - Volatile.Read(ref lastReadTickCount)) / 1000;
        public long LastWriteSecondsAgo => unchecked(Environment.TickCount - Volatile.Read(ref lastWriteTickCount)) / 1000;

        private bool IncludeDetailInExceptions => BridgeCouldBeNull?.Multiplexer.RawConfig.IncludeDetailInExceptions ?? false;

        [Conditional("VERBOSE")]
        internal void Trace(string message) => BridgeCouldBeNull?.Multiplexer?.Trace(message, ToString());

        public long SubscriptionCount { get; set; }

        public bool TransactionActive { get; internal set; }

        private RedisProtocol _protocol; // note starts at **zero**, not RESP2
        public RedisProtocol? Protocol => _protocol == 0 ? null : _protocol;

        public void SetProtocol(RedisProtocol value)
        {
            _protocol = value;
            BridgeCouldBeNull?.SetProtocol(value);
        }

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Usage", "CA2202:Do not dispose objects multiple times", Justification = "Trust me yo")]
        internal void Shutdown(ConnectionFailureType failureType = ConnectionFailureType.ConnectionDisposed)
        {
            var output = Interlocked.Exchange(ref _output, null); // compare to the critical read
            var socket = Interlocked.Exchange(ref _socket, null);

            if (output != null)
            {
                Trace("Disconnecting...");
                try { BridgeCouldBeNull?.OnDisconnected(failureType, this, out _, out _); } catch { }
                try { output.Complete(); } catch { }
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
            // ReSharper disable once GCSuppressFinalizeForTypeWithoutDestructor
            GC.SuppressFinalize(this);
        }

        internal void UpdateLastWriteTime() => Interlocked.Exchange(ref lastWriteTickCount, Environment.TickCount);

        internal bool CanSimulateConnectionFailure => false;

        internal void SimulateConnectionFailure(SimulatedFailureType failureType)
        {
            bool killInput = false, killOutput = false;
            switch (connectionType)
            {
                case ConnectionType.Interactive:
                    killInput = failureType.HasFlag(SimulatedFailureType.InteractiveInbound);
                    killOutput = failureType.HasFlag(SimulatedFailureType.InteractiveOutbound);
                    break;
                case ConnectionType.Subscription:
                    killInput = failureType.HasFlag(SimulatedFailureType.SubscriptionInbound);
                    killOutput = failureType.HasFlag(SimulatedFailureType.SubscriptionOutbound);
                    break;
            }
            if (killInput | killOutput)
            {
                OnCancel(killInput, killOutput);
                RecordConnectionFailed(ConnectionFailureType.SocketFailure);
            }
        }

        public void RecordConnectionFailed(
            ConnectionFailureType failureType,
            Exception? innerException = null,
            [CallerMemberName] string? origin = null,
            bool isInitialConnect = false,
            Stream? connectingStream = null)
        {
            Exception? outerException = innerException;
            IdentifyFailureType(innerException, ref failureType);
            var bridge = BridgeCouldBeNull;
            Message? nextMessage;

            if (_ioStream is not null || isInitialConnect) // if *we* didn't burn the pipe: flag it
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
                    int now = Environment.TickCount, lastRead = Volatile.Read(ref lastReadTickCount), lastWrite = Volatile.Read(ref lastWriteTickCount),
                        lastBeat = Volatile.Read(ref lastBeatTickCount);

                    int unansweredWriteTime = 0;
                    lock (_writtenAwaitingResponse)
                    {
                        // find oldest message awaiting a response
                        if (_writtenAwaitingResponse.TryPeek(out nextMessage))
                        {
                            unansweredWriteTime = nextMessage.GetWriteTime();
                        }
                    }

                    var exMessage = new StringBuilder(failureType.ToString());

                    // If the reason for the shutdown was we asked for the socket to die, don't log it as an error (only informational)
                    var weAskedForThis = Volatile.Read(ref clientSentQuit) != 0;

                    /*
                    var pipe = connectingStream ?? _ioStream;
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
                    */

                    long sent = TotalBytesSent, recd = totalBytesReceived;
                    if (sent == 0) { exMessage.Append(recd == 0 ? " (0-read, 0-sent)" : " (0-sent)"); }
                    else if (recd == 0) { exMessage.Append(" (0-read)"); }

                    var data = new List<Tuple<string, string?>>();
                    void AddData(string? lk, string? sk, string? v)
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

                            AddData("Origin", "origin", origin);
                            // add("Input-Buffer", "input-buffer", _ioPipe.Input);
                            AddData("Outstanding-Responses", "outstanding", GetSentAwaitingResponseCount().ToString());
                            AddData("Last-Read", "last-read", (unchecked(now - lastRead) / 1000) + "s ago");
                            AddData("Last-Write", "last-write", (unchecked(now - lastWrite) / 1000) + "s ago");
                            if (unansweredWriteTime != 0) AddData("Unanswered-Write", "unanswered-write", (unchecked(now - unansweredWriteTime) / 1000) + "s ago");
                            AddData("Keep-Alive", "keep-alive", bridge.ServerEndPoint?.WriteEverySeconds + "s");
                            AddData("Previous-Physical-State", "state", oldState.ToString());
                            if (connStatus.BytesAvailableOnSocket >= 0) AddData("Inbound-Bytes", "in", connStatus.BytesAvailableOnSocket.ToString());
                            if (connStatus.BytesInReadPipe >= 0) AddData("Inbound-Pipe-Bytes", "in-pipe", connStatus.BytesInReadPipe.ToString());
                            if (connStatus.BytesInWritePipe >= 0) AddData("Outbound-Pipe-Bytes", "out-pipe", connStatus.BytesInWritePipe.ToString());

                            AddData("Last-Heartbeat", "last-heartbeat", (lastBeat == 0 ? "never" : ((unchecked(now - lastBeat) / 1000) + "s ago")) + (bridge.IsBeating ? " (mid-beat)" : ""));
                            var mbeat = bridge.Multiplexer.LastHeartbeatSecondsAgo;
                            if (mbeat >= 0)
                            {
                                AddData("Last-Multiplexer-Heartbeat", "last-mbeat", mbeat + "s ago");
                            }
                            AddData("Last-Global-Heartbeat", "global", ConnectionMultiplexer.LastGlobalHeartbeatSecondsAgo + "s ago");
                        }
                    }

                    AddData("Version", "v", Utils.GetLibVersion());

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

            var ex = innerException is RedisException ? innerException : outerException;

            nextMessage = Interlocked.Exchange(ref _awaitingToken, null);
            if (nextMessage is not null)
            {
                RecordMessageFailed(nextMessage, ex, origin, bridge);
            }

            while (TryDequeueLocked(_writtenAwaitingResponse, out nextMessage))
            {
                RecordMessageFailed(nextMessage, ex, origin, bridge);
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

        private void RecordMessageFailed(Message next, Exception? ex, string? origin, PhysicalBridge? bridge)
        {
            if (next.Command == RedisCommand.QUIT && next.TrySetResult(true))
            {
                // fine, death of a socket is close enough
                next.Complete();
            }
            else
            {
                if (bridge != null)
                {
                    bridge.Trace("Failing: " + next);
                    bridge.Multiplexer?.OnMessageFaulted(next, ex, origin);
                }
                next.SetExceptionAndComplete(ex!, bridge);
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
                    _ => failureType,
                };
            }
        }

        internal void EnqueueInsideWriteLock(Message next, bool enforceMuxer = true)
        {
            var multiplexer = BridgeCouldBeNull?.Multiplexer;
            if (multiplexer is null & enforceMuxer) // note: this should only be false for testing
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
        /// <param name="asyncTimeoutDetected">How many async commands were overdue and threw timeout exceptions.</param>
        /// <param name="syncTimeoutDetected">How many sync commands were overdue. No exception are thrown for these commands here.</param>
        internal void OnBridgeHeartbeat(out int asyncTimeoutDetected, out int syncTimeoutDetected)
        {
            asyncTimeoutDetected = 0;
            syncTimeoutDetected = 0;
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
                                var baseErrorMessage = haveDeltas
                                    ? $"Timeout awaiting response (outbound={sentDelta >> 10}KiB, inbound={receivedDelta >> 10}KiB, {elapsed}ms elapsed, timeout is {timeout}ms)"
                                    : $"Timeout awaiting response ({elapsed}ms elapsed, timeout is {timeout}ms)";
                                var timeoutEx = ExceptionFactory.Timeout(multiplexer, baseErrorMessage, msg, server);
                                multiplexer.OnMessageFaulted(msg, timeoutEx);
                                msg.SetExceptionAndComplete(timeoutEx, bridge); // tell the message that it is doomed
                                multiplexer.OnAsyncTimeout();
                                asyncTimeoutDetected++;
                            }
                            else
                            {
                                // Only count how many sync timeouts we detect here (do not poke them;
                                // the actual timeout is handled in ConnectionMultiplexer.ExecuteSyncImpl)
                                syncTimeoutDetected++;

                                if (msg.IsHandshakeCompletion)
                                {
                                    // Critical handshake validation timed out; note that this doesn't have a result-box,
                                    // so doesn't get timed out via the async path above.
                                    Shutdown(ConnectionFailureType.UnableToConnect);
                                }
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

        internal void RecordQuit()
        {
            // don't blame redis if we fired the first shot
            Volatile.Write(ref clientSentQuit, 1);
            // (_ioPipe as SocketConnection)?.TrySetProtocolShutdown(PipeShutdownKind.ProtocolExitClient);
        }

        internal void Flush()
        {
            var tmp = _output;
            if (tmp is null) Throw();
            _writeStatus = WriteStatus.Flushing;
            tmp.Flush();
            _writeStatus = WriteStatus.Flushed;
            UpdateLastWriteTime();
            [DoesNotReturn]
            static void Throw() => throw new InvalidOperationException("Output pipe not initialized");
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
            // Fall back to bytes waiting on the socket if we can
            int socketBytes;
            try
            {
                socketBytes = VolatileSocket?.Available ?? -1;
            }
            catch
            {
                // If this fails, we're likely in a race disposal situation and do not want to blow sky high here.
                socketBytes = -1;
            }

            return new ConnectionStatus()
            {
                BytesAvailableOnSocket = socketBytes,
                BytesInReadPipe = GetReadCommittedLength(),
                BytesInWritePipe = -1,
                ReadStatus = _readStatus,
                WriteStatus = _writeStatus,
                BytesLastResult = bytesLastResult,
                BytesInBuffer = bytesInBuffer,
            };
        }

        internal static RemoteCertificateValidationCallback? GetAmbientIssuerCertificateCallback()
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
        internal static LocalCertificateSelectionCallback? GetAmbientClientCertificateCallback()
        {
            try
            {
                var certificatePath = Environment.GetEnvironmentVariable("SERedis_ClientCertPfxPath");
                if (!string.IsNullOrEmpty(certificatePath) && File.Exists(certificatePath))
                {
                    var password = Environment.GetEnvironmentVariable("SERedis_ClientCertPassword");
                    var pfxStorageFlags = Environment.GetEnvironmentVariable("SERedis_ClientCertStorageFlags");
                    X509KeyStorageFlags storageFlags = X509KeyStorageFlags.DefaultKeySet;
                    if (!string.IsNullOrEmpty(pfxStorageFlags) && Enum.TryParse<X509KeyStorageFlags>(pfxStorageFlags, true, out var typedFlags))
                    {
                        storageFlags = typedFlags;
                    }

                    return ConfigurationOptions.CreatePfxUserCertificateCallback(certificatePath, password, storageFlags);
                }

#if NET
                certificatePath = Environment.GetEnvironmentVariable("SERedis_ClientCertPemPath");
                if (!string.IsNullOrEmpty(certificatePath) && File.Exists(certificatePath))
                {
                    var passwordPath = Environment.GetEnvironmentVariable("SERedis_ClientCertPasswordPath");
                    return ConfigurationOptions.CreatePemUserCertificateCallback(certificatePath, passwordPath);
                }
#endif
            }
            catch (Exception ex)
            {
                Debug.WriteLine(ex.Message);
            }
            return null;
        }

        internal async ValueTask<bool> ConnectedAsync(Socket? socket, ILogger? log)
        {
            var bridge = BridgeCouldBeNull;
            if (bridge == null) return false;

            Stream? stream = null;
            try
            {
                // disallow connection in some cases
                OnDebugAbort();

                // the order is important here:
                // non-TLS: [Socket]<==[SocketConnection:IDuplexPipe]
                // TLS:     [Socket]<==[NetworkStream]<==[SslStream]<==[StreamConnection:IDuplexPipe]
                var config = bridge.Multiplexer.RawConfig;

                var tunnel = config.Tunnel;
                if (tunnel is not null)
                {
                    stream = await tunnel.BeforeAuthenticateAsync(bridge.ServerEndPoint.EndPoint, bridge.ConnectionType, socket, CancellationToken.None).ForAwait();
                }

                static Stream DemandSocketStream(Socket? socket)
                    => new NetworkStream(socket ?? throw new InvalidOperationException("No socket or stream available - possibly a tunnel error"));

                if (config.Ssl)
                {
                    log?.LogInformationConfiguringTLS();
                    var host = config.SslHost;
                    if (host.IsNullOrWhiteSpace())
                    {
                        host = Format.ToStringHostOnly(bridge.ServerEndPoint.EndPoint);
                    }

                    stream ??= DemandSocketStream(socket);
                    var ssl = new SslStream(
                        innerStream: stream,
                        leaveInnerStreamOpen: false,
                        userCertificateValidationCallback: config.CertificateValidationCallback ?? GetAmbientIssuerCertificateCallback(),
                        userCertificateSelectionCallback: config.CertificateSelectionCallback ?? GetAmbientClientCertificateCallback(),
                        encryptionPolicy: EncryptionPolicy.RequireEncryption);
                    try
                    {
                        try
                        {
#if NET
                            var configOptions = config.SslClientAuthenticationOptions?.Invoke(host);
                            if (configOptions is not null)
                            {
                                await ssl.AuthenticateAsClientAsync(configOptions).ForAwait();
                            }
                            else
                            {
                                await ssl.AuthenticateAsClientAsync(host, config.SslProtocols, config.CheckCertificateRevocation).ForAwait();
                            }
#else
                            await ssl.AuthenticateAsClientAsync(host, config.SslProtocols, config.CheckCertificateRevocation).ForAwait();
#endif
                        }
                        catch (Exception ex)
                        {
                            Debug.WriteLine(ex.Message);
                            bridge.Multiplexer.SetAuthSuspect(ex);
                            bridge.Multiplexer.Logger?.LogErrorConnectionIssue(ex, ex.Message);
                            throw;
                        }
                        log?.LogInformationTLSConnectionEstablished(ssl.SslProtocol);
                    }
                    catch (AuthenticationException authexception)
                    {
                        RecordConnectionFailed(ConnectionFailureType.AuthenticationFailure, authexception, isInitialConnect: true);
                        bridge.Multiplexer.Trace("Encryption failure");
                        return false;
                    }
                    stream = ssl;
                }

                stream ??= DemandSocketStream(socket);
                OnWrapForLogging(ref stream, _physicalName);

                InitOutput(stream);

                log?.LogInformationConnected(bridge.Name);

                await bridge.OnConnectedAsync(this, log).ForAwait();
                return true;
            }
            catch (Exception ex)
            {
                RecordConnectionFailed(ConnectionFailureType.InternalFailure, ex, isInitialConnect: true, connectingStream: stream); // includes a bridge.OnDisconnected
                bridge.Multiplexer.Trace("Could not connect: " + ex.Message, ToString());
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

        partial void OnWrapForLogging(ref Stream stream, string name);

        internal void UpdateLastReadTime() => Interlocked.Exchange(ref lastReadTickCount, Environment.TickCount);

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
            PubSubSMessage,
            Reconfigure,
            InvokePubSub,
            ResponseSequenceCheck, // high-integrity mode only
            DequeueResult,
            ComputeResult,
            CompletePendingMessageSync,
            CompletePendingMessageAsync,
            MatchResultComplete,
            ResetArena,
            ProcessBufferComplete,
            PubSubUnsubscribe,
            NA = -1,
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
