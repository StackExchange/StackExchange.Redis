using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using StackExchange.Redis.Transports;

namespace StackExchange.Redis
{
    [Flags]
    internal enum UnselectableFlags
    {
        None = 0,
        RedundantPrimary = 1,
        DidNotRespond = 2,
        ServerType = 4,
    }

    internal sealed partial class ServerEndPoint : IDisposable, IBridge
    {
        internal volatile ServerEndPoint? Primary;
        internal volatile ServerEndPoint[] Replicas = Array.Empty<ServerEndPoint>();
        private static readonly Regex nameSanitizer = new Regex("[^!-~]", RegexOptions.Compiled);

        private readonly Hashtable knownScripts = new Hashtable(StringComparer.Ordinal);

        private int databases, writeEverySeconds;

        private object? _activePool;
        private Transport? _subscription;

        private bool isDisposed, replicaReadOnly, isReplica, allowReplicaWrites;
        private bool? supportsDatabases, supportsPrimaryWrites;
        private ServerType serverType;
        private volatile UnselectableFlags unselectableReasons;
        private Version version;
        private readonly ConcurrentQueue<Message> _sharedBacklog = new ConcurrentQueue<Message>();

        internal void ResetNonConnected()
        {
            interactive?.ResetNonConnected();
            _subscription?.ResetNonConnected();
        }

        public ServerEndPoint(ConnectionMultiplexer multiplexer, EndPoint endpoint)
        {
            Multiplexer = multiplexer;
            EndPoint = endpoint;
            var config = multiplexer.RawConfig;
            version = config.DefaultVersion;
            replicaReadOnly = true;
            isReplica = false;
            databases = 0;
            writeEverySeconds = config.KeepAlive > 0 ? config.KeepAlive : 60;
            serverType = ServerType.Standalone;
            ConfigCheckSeconds = Multiplexer.RawConfig.ConfigCheckSeconds;

            // overrides for twemproxy/envoyproxy
            switch (multiplexer.RawConfig.Proxy)
            {
                case Proxy.Twemproxy:
                    databases = 1;
                    serverType = ServerType.Twemproxy;
                    break;
                case Proxy.Envoyproxy:
                    databases = 1;
                    serverType = ServerType.Envoyproxy;
                    break;
            }
        }

        public EndPoint EndPoint { get; }

        public ClusterConfiguration? ClusterConfiguration { get; private set; }

        /// <summary>
        /// Whether this endpoint supports databases at all.
        /// Note that some servers are cluster but present as standalone (e.g. Redis Enterprise), so we respect
        /// <see cref="RedisCommand.SELECT"/> being disabled here as a performance workaround.
        /// </summary>
        /// <remarks>
        /// This is memoized because it's accessed on hot paths inside the write lock.
        /// </remarks>
        public bool SupportsDatabases =>
            supportsDatabases ??= (serverType == ServerType.Standalone && Multiplexer.CommandMap.IsAvailable(RedisCommand.SELECT));

        public int Databases
        {
            get => databases;
            set => SetConfig(ref databases, value);
        }

        public bool IsConnecting => interactive?.IsConnecting == true;
        public bool IsConnected => _activePool is not null;
        public bool IsSubscriberConnected => _subscription is object;

        public bool SupportsSubscriptions => Multiplexer.CommandMap.IsAvailable(RedisCommand.SUBSCRIBE);
        public bool SupportsPrimaryWrites => supportsPrimaryWrites ??= (!IsReplica || !ReplicaReadOnly || AllowReplicaWrites);

        private readonly List<TaskCompletionSource<string>> _pendingConnectionMonitors = new List<TaskCompletionSource<string>>();

        /// <summary>
        /// Awaitable state seeing if this endpoint is connected.
        /// </summary>
        public Task<string> OnConnectedAsync(LogProxy? log = null, bool sendTracerIfConnected = false, bool autoConfigureIfConnected = false)
        {
            async Task<string> IfConnectedAsync(LogProxy? log, bool sendTracerIfConnected, bool autoConfigureIfConnected)
            {
                log?.WriteLine($"{Format.ToString(this)}: OnConnectedAsync already connected start");
                if (autoConfigureIfConnected)
                {
                    AutoConfigure(null, log);
                }
                if (sendTracerIfConnected)
                {
                    await SendTracerAsync(log).ForAwait();
                }
                log?.WriteLine($"{Format.ToString(this)}: OnConnectedAsync already connected end");
                return "Already connected";
            }

            if (!IsConnected)
            {
                log?.WriteLine($"{Format.ToString(this)}: OnConnectedAsync init (State={interactive?.ConnectionState})");
                var tcs = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
                _ = tcs.Task.ContinueWith(t => log?.WriteLine($"{Format.ToString(this)}: OnConnectedAsync completed ({t.Result})"));
                lock (_pendingConnectionMonitors)
                {
                    _pendingConnectionMonitors.Add(tcs);
                    // In case we complete in a race above, before attaching
                    if (IsConnected)
                    {
                        tcs.TrySetResult("Connection race");
                        _pendingConnectionMonitors.Remove(tcs);
                    }
                }
                return tcs.Task;
            }
            return IfConnectedAsync(log, sendTracerIfConnected, autoConfigureIfConnected);
        }

        internal Exception? LastException
        {
            get
            {
                var snapshot = interactive;
                var subEx = subscription?.LastException;
                var subExData = subEx?.Data;

                //check if subscription endpoint has a better last exception
                if (subExData != null && subExData.Contains("Redis-FailureType") && subExData["Redis-FailureType"]?.ToString() != nameof(ConnectionFailureType.UnableToConnect))
                {
                    return subEx;
                }
                return snapshot?.LastException;
            }
        }

        internal State ConnectionState => interactive?.ConnectionState ?? State.Disconnected;

        public long OperationCount => interactive?.OperationCount ?? 0 + _subscription?.OperationCount ?? 0;

        public bool RequiresReadMode => serverType == ServerType.Cluster && IsReplica;

        public ServerType ServerType
        {
            get => serverType;
            set => SetConfig(ref serverType, value);
        }

        public bool IsReplica
        {
            get => isReplica;
            set => SetConfig(ref isReplica, value);
        }

        public bool ReplicaReadOnly
        {
            get => replicaReadOnly;
            set => SetConfig(ref replicaReadOnly, value);
        }

        public bool AllowReplicaWrites
        {
            get => allowReplicaWrites;
            set
            {
                allowReplicaWrites = value;
                ClearMemoized();
            }
        }

        public Version Version
        {
            get => version;
            set => SetConfig(ref version, value);
        }

        public int WriteEverySeconds
        {
            get => writeEverySeconds;
            set => SetConfig(ref writeEverySeconds, value);
        }

        internal ConnectionMultiplexer Multiplexer { get; }

        public void Dispose()
        {
            isDisposed = true;
            var snapshot = _activePool;
            if (snapshot is List<Transport> list)
            {
                lock (list)
                {
                    foreach (var transport in list)
                    {
                        transport.Dispose();
                    }
                    list.Clear();
                }
            }
            else if (snapshot is Transport transport)
            {
                transport.Dispose();
                snapshot = null;
            }
            var tmp = _subscription;
            _subscription = null;
            tmp?.Dispose();
        }

        public IBridge? GetBridge(ConnectionType type, bool create = true, LogProxy? log = null)
        {
            if (isDisposed) return null;
            return type switch
            {
                ConnectionType.Interactive => NextInteractive() ?? (create ? CreateTransport(ConnectionType.Interactive, log) : null),
                ConnectionType.Subscription => _subscription ?? (create ? CreateTransport(ConnectionType.Subscription, log) : null),
                _ => null,
            };
        }

        uint _nextIndex; // round-robin between transports
        IBridge? NextInteractive()
        {
            var snapshot = _activePool;
            if (snapshot is null) return null;
            if (snapshot is Transport transport) return transport;
            var list = (List<Transport>)snapshot;
            lock (list)
            {
                var count = (uint)list.Count;
                return count switch
                {
                    0 => null,
                    1 => list[0],
                    _ => list[(int)(_nextIndex++ % count)],
                };
            }
        }
        private IBridge AddLocked(Transport transport)
        {
            if (transport is null) return transport!;

            object? snapshot, newValue;
            do
            {
                snapshot = Volatile.Read(ref _activePool);
                if (snapshot is null)
                {
                    newValue = transport;
                }
                else if (snapshot is Transport existing)
                {
                    if (ReferenceEquals(existing, transport)) break; // nothing to do
                    newValue = new List<Transport> { existing, transport };
                }
                else
                {
                    var list = (List<Transport>)snapshot;
                    lock (list)
                    {
                        if (!list.Contains(transport))
                        {
                            list.Add(transport);
                        }
                    }
                    break; // no need to swap the ref - already a list
                }

            } while (Interlocked.CompareExchange(ref _activePool, newValue, snapshot) != snapshot);
            return transport;
        }


        public IBridge? GetBridge(Message message, bool create = true)
        {
            if (isDisposed) return null;

            // Subscription commands go to a specific bridge - so we need to set that up.
            // There are other commands we need to send to the right connection (e.g. subscriber PING with an explicit SetForSubscriptionBridge call),
            // but these always go subscriber.
            switch (message.Command)
            {
                case RedisCommand.SUBSCRIBE:
                case RedisCommand.UNSUBSCRIBE:
                case RedisCommand.PSUBSCRIBE:
                case RedisCommand.PUNSUBSCRIBE:
                    message.SetForSubscriptionBridge();
                    break;
            }

            return message.IsForSubscriptionBridge
                ? _subscription ?? (create ? CreateTransport(ConnectionType.Subscription, null) : null)
                : NextInteractive() ?? (create ? CreateTransport(ConnectionType.Interactive, null) : null);
        }

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Style", "IDE0066:Convert switch statement to expression", Justification = "readability")]
        public IBridge? GetBridge(RedisCommand command, bool create = true)
        {
            if (isDisposed) return null;
            switch (command)
            {
                case RedisCommand.SUBSCRIBE:
                case RedisCommand.UNSUBSCRIBE:
                case RedisCommand.PSUBSCRIBE:
                case RedisCommand.PUNSUBSCRIBE:
                    return _subscription?? (create ? CreateTransport(ConnectionType.Subscription, null) : null);
                default:
                    return NextInteractive() ?? (create ? CreateTransport(ConnectionType.Interactive, null) : null);
            }
        }

        public RedisFeatures GetFeatures() => new RedisFeatures(version);

        public void SetClusterConfiguration(ClusterConfiguration configuration)
        {
            ClusterConfiguration = configuration;

            if (configuration != null)
            {
                Multiplexer.Trace("Updating cluster ranges...");
                Multiplexer.UpdateClusterRange(configuration);
                Multiplexer.Trace("Resolving genealogy...");
                UpdateNodeRelations(configuration);
                Multiplexer.Trace("Cluster configured");
            }
        }

        public void UpdateNodeRelations(ClusterConfiguration configuration)
        {
            var thisNode = configuration.Nodes.FirstOrDefault(x => x.EndPoint?.Equals(EndPoint) == true);
            if (thisNode != null)
            {
                Multiplexer.Trace($"Updating node relations for {Format.ToString(thisNode.EndPoint)}...");
                List<ServerEndPoint>? replicas = null;
                ServerEndPoint? primary = null;
                foreach (var node in configuration.Nodes)
                {
                    if (node.NodeId == thisNode.ParentNodeId)
                    {
                        primary = Multiplexer.GetServerEndPoint(node.EndPoint);
                    }
                    else if (node.ParentNodeId == thisNode.NodeId && node.EndPoint is not null)
                    {
                        (replicas ??= new List<ServerEndPoint>()).Add(Multiplexer.GetServerEndPoint(node.EndPoint));
                    }
                }
                Primary = primary;
                Replicas = replicas?.ToArray() ?? Array.Empty<ServerEndPoint>();
            }
        }

        public void SetUnselectable(UnselectableFlags flags)
        {
            if (flags != 0)
            {
                var oldFlags = unselectableReasons;
                unselectableReasons |= flags;
                if (unselectableReasons != oldFlags)
                {
                    Multiplexer.Trace(unselectableReasons == 0 ? "Now usable" : ("Now unusable: " + flags), ToString());
                }
            }
        }

        public void ClearUnselectable(UnselectableFlags flags)
        {
            var oldFlags = unselectableReasons;
            if (oldFlags != 0)
            {
                unselectableReasons &= ~flags;
                if (unselectableReasons != oldFlags)
                {
                    Multiplexer.Trace(unselectableReasons == 0 ? "Now usable" : ("Now unusable: " + flags), ToString());
                }
            }
        }

        public override string ToString() => Format.ToString(EndPoint);

        [Obsolete("prefer async")]
        public WriteResult TryWrite(Message message)
        {
            var bridge = GetBridge(message);
            if (bridge is null) return WriteResult.NoConnectionAvailable;

            return bridge.Write(message);
        }

        internal void Activate(ConnectionType type, LogProxy? log) => GetBridge(type, true, log);

        internal void AddScript(string script, byte[] hash)
        {
            lock (knownScripts)
            {
                knownScripts[script] = hash;
            }
        }

        internal void AutoConfigure(Transport connection, LogProxy? log = null)
        {
            if (!serverType.SupportsAutoConfigure())
            {
                // Don't try to detect configuration.
                // All the config commands are disabled and the fallback primary/replica detection won't help
                return;
            }

            log?.WriteLine($"{Format.ToString(this)}: Auto-configuring...");

            var commandMap = Multiplexer.CommandMap;
            const CommandFlags flags = CommandFlags.FireAndForget | CommandFlags.NoRedirect;
            var features = GetFeatures();
            Message msg;

            var autoConfigProcessor = new ResultProcessor.AutoConfigureProcessor(log);

            if (commandMap.IsAvailable(RedisCommand.CONFIG))
            {
                if (Multiplexer.RawConfig.KeepAlive <= 0)
                {
                    msg = Message.Create(-1, flags, RedisCommand.CONFIG, RedisLiterals.GET, RedisLiterals.timeout);
                    msg.SetInternalCall();
                    WriteDirectOrQueueFireAndForget(connection, msg, autoConfigProcessor);
                }
                msg = Message.Create(-1, flags, RedisCommand.CONFIG, RedisLiterals.GET, features.ReplicaCommands ? RedisLiterals.replica_read_only : RedisLiterals.slave_read_only);
                msg.SetInternalCall();
                WriteDirectOrQueueFireAndForget(connection, msg, autoConfigProcessor);
                msg = Message.Create(-1, flags, RedisCommand.CONFIG, RedisLiterals.GET, RedisLiterals.databases);
                msg.SetInternalCall();
                WriteDirectOrQueueFireAndForget(connection, msg, autoConfigProcessor);
            }
            if (commandMap.IsAvailable(RedisCommand.SENTINEL))
            {
                msg = Message.Create(-1, flags, RedisCommand.SENTINEL, RedisLiterals.MASTERS);
                msg.SetInternalCall();
                WriteDirectOrQueueFireAndForget(connection, msg, autoConfigProcessor);
            }
            if (commandMap.IsAvailable(RedisCommand.INFO))
            {
                lastInfoReplicationCheckTicks = Environment.TickCount;
                if (features.InfoSections)
                {
                    msg = Message.Create(-1, flags, RedisCommand.INFO, RedisLiterals.replication);
                    msg.SetInternalCall();
                    WriteDirectOrQueueFireAndForget(connection, msg, autoConfigProcessor);

                    msg = Message.Create(-1, flags, RedisCommand.INFO, RedisLiterals.server);
                    msg.SetInternalCall();
                    WriteDirectOrQueueFireAndForget(connection, msg, autoConfigProcessor);
                }
                else
                {
                    msg = Message.Create(-1, flags, RedisCommand.INFO);
                    msg.SetInternalCall();
                    WriteDirectOrQueueFireAndForget(connection, msg, autoConfigProcessor);
                }
            }
            else if (commandMap.IsAvailable(RedisCommand.SET))
            {
                // This is a nasty way to find if we are a replica, and it will only work on up-level servers, but...
                RedisKey key = Multiplexer.UniqueId;
                // The actual value here doesn't matter (we detect the error code if it fails).
                // The value here is to at least give some indication to anyone watching via "monitor",
                // but we could send two GUIDs (key/value) and it would work the same.
                msg = Message.Create(0, flags, RedisCommand.SET, key, RedisLiterals.replica_read_only, RedisLiterals.PX, 1, RedisLiterals.NX);
                msg.SetInternalCall();
                WriteDirectOrQueueFireAndForget(connection, msg, autoConfigProcessor);
            }
            if (commandMap.IsAvailable(RedisCommand.CLUSTER))
            {
                msg = Message.Create(-1, flags, RedisCommand.CLUSTER, RedisLiterals.NODES);
                msg.SetInternalCall();
                WriteDirectOrQueueFireAndForget(connection, msg, ResultProcessor.ClusterNodes).ForAwait();
            }
            // If we are going to fetch a tie breaker, do so last and we'll get it in before the tracer fires completing the connection
            // But if GETs are disabled on this, do not fail the connection - we just don't get tiebreaker benefits
            if (Multiplexer.RawConfig.TryGetTieBreaker(out var tieBreakerKey) && Multiplexer.CommandMap.IsAvailable(RedisCommand.GET))
            {
                log?.WriteLine($"{Format.ToString(EndPoint)}: Requesting tie-break (Key=\"{tieBreakerKey}\")...");
                msg = Message.Create(0, flags, RedisCommand.GET, tieBreakerKey);
                msg.SetInternalCall();
                msg = LoggingMessage.Create(log, msg);
                WriteDirectOrQueueFireAndForget(connection, msg, ResultProcessor.TieBreaker);
            }
        }

        private int _nextReplicaOffset;
        /// <summary>
        /// Used to round-robin between multiple replicas
        /// </summary>
        internal uint NextReplicaOffset()
            => (uint)Interlocked.Increment(ref _nextReplicaOffset);

        internal Task Close(ConnectionType connectionType)
        {
            try
            {
                var tmp = GetBridge(connectionType, create: false);
                if (tmp == null || !tmp.IsConnected || !Multiplexer.CommandMap.IsAvailable(RedisCommand.QUIT))
                {
                    return Task.CompletedTask;
                }
                else
                {
                    return WriteDirectAsync(Message.Create(-1, CommandFlags.None, RedisCommand.QUIT), ResultProcessor.DemandOK, bridge: tmp);
                }
            }
            catch (Exception ex)
            {
                return Task.FromException(ex);
            }
        }

        internal void FlushScriptCache()
        {
            lock (knownScripts)
            {
                knownScripts.Clear();
            }
        }

        private string? runId;
        internal string? RunId
        {
            get => runId;
            set
            {
                // We only care about changes
                if (value != runId)
                {
                    // If we had an old run-id, and it has changed, then the server has been restarted
                    // ...which means the script cache is toast
                    if (runId != null)
                    {
                        FlushScriptCache();
                    }
                    runId = value;
                }
            }
        }

        internal ServerCounters GetCounters()
        {
            var counters = new ServerCounters(EndPoint);
            // TODO: bridge fixup
            //interactive?.GetCounters(counters.Interactive);
            //subscription?.GetCounters(counters.Subscription);
            return counters;
        }

        internal BridgeStatus GetBridgeStatus(ConnectionType connectionType)
        {
            try
            {
                return GetBridge(connectionType, false)?.GetStatus() ?? BridgeStatus.Zero;
            }
            catch (Exception ex)
            {   // only needs to be best efforts
                System.Diagnostics.Debug.WriteLine(ex.Message);
            }

            return BridgeStatus.Zero;
        }

        internal string GetProfile()
        {
            var sb = new StringBuilder(Format.ToString(EndPoint)).Append(": ");
            sb.Append("Circular op-count snapshot; int:");
            interactive?.AppendProfile(sb);
            sb.Append("; sub:");
            subscription?.AppendProfile(sb);
            return sb.ToString();
        }

        internal byte[]? GetScriptHash(string script, RedisCommand command)
        {
            var found = (byte[]?)knownScripts[script];
            if (found == null && command == RedisCommand.EVALSHA)
            {
                // The script provided is a hex SHA - store and re-use the ASCii for that
                found = Encoding.ASCII.GetBytes(script);
                lock (knownScripts)
                {
                    knownScripts[script] = found;
                }
            }
            return found;
        }

        internal string? GetStormLog(Message message) => GetBridge(message)?.GetStormLog();

        internal Message GetTracerMessage(bool assertIdentity)
        {
            // Different configurations block certain commands, as can ad-hoc local configurations, so
            //   we'll do the best with what we have available.
            // Note: muxer-ctor asserts that one of ECHO, PING, TIME of GET is available
            // See also: TracerProcessor
            var map = Multiplexer.CommandMap;
            Message msg;
            const CommandFlags flags = CommandFlags.NoRedirect | CommandFlags.FireAndForget;
            if (assertIdentity && map.IsAvailable(RedisCommand.ECHO))
            {
                msg = Message.Create(-1, flags, RedisCommand.ECHO, (RedisValue)Multiplexer.UniqueId);
            }
            else if (map.IsAvailable(RedisCommand.PING))
            {
                msg = Message.Create(-1, flags, RedisCommand.PING);
            }
            else if (map.IsAvailable(RedisCommand.TIME))
            {
                msg = Message.Create(-1, flags, RedisCommand.TIME);
            }
            else if (!assertIdentity && map.IsAvailable(RedisCommand.ECHO))
            {
                // We'll use echo as a PING substitute if it is all we have (in preference to EXISTS)
                msg = Message.Create(-1, flags, RedisCommand.ECHO, (RedisValue)Multiplexer.UniqueId);
            }
            else
            {
                map.AssertAvailable(RedisCommand.EXISTS);
                msg = Message.Create(0, flags, RedisCommand.EXISTS, (RedisValue)Multiplexer.UniqueId);
            }
            msg.SetInternalCall();
            return msg;
        }

        internal UnselectableFlags GetUnselectableFlags() => unselectableReasons;

        internal bool IsSelectable(RedisCommand command, bool allowDisconnected = false)
        {
            // Until we've connected at least once, we're going to have a DidNotRespond unselectable reason present
            var bridge = unselectableReasons == 0 || (allowDisconnected && unselectableReasons == UnselectableFlags.DidNotRespond)
                ? GetBridge(command, false)
                : null;

            return bridge != null && (allowDisconnected || bridge.IsConnected);
        }

        private void CompletePendingConnectionMonitors(string source)
        {
            lock (_pendingConnectionMonitors)
            {
                foreach (var tcs in _pendingConnectionMonitors)
                {
                    tcs.TrySetResult(source);
                }
                _pendingConnectionMonitors.Clear();
            }
        }

        internal void OnDisconnected(Transport transport)
        {
            switch (transport.ConnectionType)
            {
                case ConnectionType.Interactive:
                    CompletePendingConnectionMonitors("Disconnected");
                    break;
                case ConnectionType.Subscription:
                    Multiplexer.UpdateSubscriptions();
                    break;
            }
        }

        internal Task OnEstablishingAsync(PhysicalConnection connection, LogProxy? log)
        {
            static async Task OnEstablishingAsyncAwaited(PhysicalConnection connection, Task handshake)
            {
                try
                {
                    await handshake.ForAwait();
                }
                catch (Exception ex)
                {
                    connection.RecordConnectionFailed(ConnectionFailureType.InternalFailure, ex);
                }
            }

            try
            {
                if (connection == null) return Task.CompletedTask;
                var handshake = HandshakeAsync(connection, log);

                if (handshake.Status != TaskStatus.RanToCompletion)
                    return OnEstablishingAsyncAwaited(connection, handshake);
            }
            catch (Exception ex)
            {
                connection.RecordConnectionFailed(ConnectionFailureType.InternalFailure, ex);
            }
            return Task.CompletedTask;
        }

        internal void OnFullyEstablished(ITransportState transport, string source)
        {
            try
            {
                var type = transport.ConnectionType;
                if (transport.ConnectionType != ConnectionType.None)
                {
                    // Clear the unselectable flag ASAP since we are open for business
                    ClearUnselectable(UnselectableFlags.DidNotRespond);

                    if (type ==  ConnectionType.Subscription)
                    {
                        // Note: this MUST be fire and forget, because we might be in the middle of a Sync processing
                        // TracerProcessor which is executing this line inside a SetResultCore().
                        // Since we're issuing commands inside a SetResult path in a message, we'd create a deadlock by waiting.
                        Multiplexer.EnsureSubscriptions(CommandFlags.FireAndForget);
                    }
                    if (IsConnected && (IsSubscriberConnected || !SupportsSubscriptions))
                    {
                        // Only connect on the second leg - we can accomplish this by checking both
                        // Or the first leg, if we're only making 1 connection because subscriptions aren't supported
                        CompletePendingConnectionMonitors(source);
                    }

                    Multiplexer.OnConnectionRestored(EndPoint, type, transport?.ToString());
                }
            }
            catch (Exception ex)
            {
                transport.RecordConnectionFailed(ConnectionFailureType.InternalFailure, ex);
            }
        }

        internal int LastInfoReplicationCheckSecondsAgo =>
            unchecked(Environment.TickCount - Thread.VolatileRead(ref lastInfoReplicationCheckTicks)) / 1000;

        private EndPoint? primaryEndPoint;
        public EndPoint? PrimaryEndPoint
        {
            get => primaryEndPoint;
            set => SetConfig(ref primaryEndPoint, value);
        }

        /// <summary>
        /// Result of the latest tie breaker (from the last reconfigure).
        /// </summary>
        internal string? TieBreakerResult { get; set; }

        internal bool CheckInfoReplication()
        {
            lastInfoReplicationCheckTicks = Environment.TickCount;
            ResetExponentiallyReplicationCheck();

            if (version >= RedisFeatures.v2_8_0 && Multiplexer.CommandMap.IsAvailable(RedisCommand.INFO)
                && GetBridge(ConnectionType.Interactive, false) is { } bridge)
            {
                var msg = Message.Create(-1, CommandFlags.FireAndForget | CommandFlags.NoRedirect, RedisCommand.INFO, RedisLiterals.replication);
                msg.SetInternalCall();
                msg.SetSource(ResultProcessor.AutoConfigure, null);
                return bridge.Write(msg) == WriteResult.Success;
            }
            return false;
        }

        private int lastInfoReplicationCheckTicks;
        internal volatile int ConfigCheckSeconds;
        [ThreadStatic]
        private static Random? r;

        /// <summary>
        /// Forces frequent replication check starting from 1 second up to max ConfigCheckSeconds with an exponential increment.
        /// </summary>
        internal void ForceExponentialBackoffReplicationCheck()
        {
            ConfigCheckSeconds = 1;
        }

        private void ResetExponentiallyReplicationCheck()
        {
            if (ConfigCheckSeconds < Multiplexer.RawConfig.ConfigCheckSeconds)
            {
                r ??= new Random();
                var newExponentialConfigCheck = ConfigCheckSeconds * 2;
                var jitter = r.Next(ConfigCheckSeconds + 1, newExponentialConfigCheck);
                ConfigCheckSeconds = Math.Min(jitter, Multiplexer.RawConfig.ConfigCheckSeconds);
            }
        }

        private int _heartBeatActive;
        internal void OnHeartbeat()
        {
            // Don't overlap heartbeat operations on an endpoint
            if (Interlocked.CompareExchange(ref _heartBeatActive, 1, 0) == 0)
            {
                try
                {
                    interactive?.OnHeartbeat(false);
                    _subscription?.OnHeartbeat(false);
                }
                catch (Exception ex)
                {
                    Multiplexer.OnInternalError(ex, EndPoint);
                }
                finally
                {
                    Interlocked.Exchange(ref _heartBeatActive, 0);
                }
            }
        }

        internal Task<T> WriteDirectAsync<T>(Message message, ResultProcessor<T> processor, IBridge? bridge = null)
        {
            var source = TaskResultBox<T>.Create(out var tcs, null);
            message.SetSource(processor, source);
            if (bridge == null) bridge = GetBridge(message);

            WriteResult result = bridge is null ? WriteResult.NoConnectionAvailable : bridge.Write(message);

            if (result != WriteResult.Success)
            {
                var ex = Multiplexer.GetException(result, message, this);
                ConnectionMultiplexer.ThrowFailed(tcs, ex);
            }
            return tcs.Task;
        }

        internal Task<bool> SendTracerAsync(LogProxy? log = null)
        {
            var msg = GetTracerMessage(false);
            msg = LoggingMessage.Create(log, msg);
            return WriteDirectAsync(msg, ResultProcessor.Tracer);
        }

        internal string Summary()
        {
            var sb = new StringBuilder(Format.ToString(EndPoint))
                .Append(": ").Append(serverType).Append(" v").Append(version).Append(", ").Append(isReplica ? "replica" : "primary");

            if (databases > 0) sb.Append("; ").Append(databases).Append(" databases");
            if (writeEverySeconds > 0)
                sb.Append("; keep-alive: ").Append(TimeSpan.FromSeconds(writeEverySeconds));
            var snapshot = _activePool;
            static void Append(StringBuilder sb, Transport? transport)
            {
                sb.Append("; int: ").Append(transport?.ConnectionState.ToString() ?? "n/a");
            }
            if (snapshot is List<Transport> list)
            {
                lock (list)
                {
                    foreach (var transport in list)
                        Append(sb, transport);
                }
            }
            else
            {
                Append(sb, snapshot as Transport);
            }

            var tmp = _subscription;
            if (tmp is null)
            {
                sb.Append("; sub: n/a");
            }
            else
            {
                var state = tmp.ConnectionState;
                sb.Append("; sub: ").Append(state);
                if (state == PhysicalBridge.State.ConnectedEstablished)
                {
                    sb.Append(", ").Append(tmp.SubscriptionCount).Append(" active");
                }
            }

            var flags = unselectableReasons;
            if (flags != 0)
            {
                sb.Append("; not in use: ").Append(flags);
            }
            return sb.ToString();
        }

        /// <summary>
        /// Write the message directly to the pipe or fail...will not queue.
        /// </summary>
        internal void WriteDirectOrQueueFireAndForget<T>(IBridge? bridge, Message message, ResultProcessor<T> processor)
        {
            if (message is null) return;
            message.SetSource(processor, null);
            bridge ??= GetBridge(message);
            if (bridge is not null) 
            {
                Multiplexer.Trace($"{Format.ToString(this)}: Writing " + message);
                bridge.Write(message).AssertSuccess();
            }
            else
            {
                _sharedBacklog.Enqueue(message);
            }
        }

        private IBridge? CreateTransport(ConnectionType type, LogProxy? log)
        {
            if (Multiplexer.IsDisposed) return null;
            Multiplexer.Trace(type.ToString());
            var bridge = new PhysicalBridge(this, type, Multiplexer.TimeoutMilliseconds);
            bridge.TryConnect(log);
            switch (type)
            {
                case ConnectionType.Subscription:
                    _subscription = bridge;
                    break;
                case ConnectionType.Interactive:
                    lock (_interactivePool)
                    {
                        _interactivePool.Add(bridge);
                    }
                    break;
            }
            return bridge;
        }

        private async Task HandshakeAsync(Transport transport, LogProxy? log)
        {
            log?.WriteLine($"{Format.ToString(this)}: Server handshake");
            if (transport is null)
            {
                Multiplexer.Trace("No connection!?");
                return;
            }
            Message msg;
            // Note that we need "" (not null) for password in the case of 'nopass' logins
            string? user = Multiplexer.RawConfig.User;
            string password = Multiplexer.RawConfig.Password ?? "";
            if (!string.IsNullOrWhiteSpace(user))
            {
                log?.WriteLine($"{Format.ToString(this)}: Authenticating (user/password)");
                msg = Message.Create(-1, CommandFlags.FireAndForget, RedisCommand.AUTH, (RedisValue)user, (RedisValue)password);
                msg.SetInternalCall();
                WriteDirectOrQueueFireAndForget(transport, msg, ResultProcessor.DemandOK);
            }
            else if (!string.IsNullOrWhiteSpace(password))
            {
                log?.WriteLine($"{Format.ToString(this)}: Authenticating (password)");
                msg = Message.Create(-1, CommandFlags.FireAndForget, RedisCommand.AUTH, (RedisValue)password);
                msg.SetInternalCall();
                WriteDirectOrQueueFireAndForget(transport, msg, ResultProcessor.DemandOK);
            }

            if (Multiplexer.CommandMap.IsAvailable(RedisCommand.CLIENT))
            {
                string name = Multiplexer.ClientName;
                if (!string.IsNullOrWhiteSpace(name))
                {
                    name = nameSanitizer.Replace(name, "");
                    if (!string.IsNullOrWhiteSpace(name))
                    {
                        log?.WriteLine($"{Format.ToString(this)}: Setting client name: {name}");
                        msg = Message.Create(-1, CommandFlags.FireAndForget, RedisCommand.CLIENT, RedisLiterals.SETNAME, (RedisValue)name);
                        msg.SetInternalCall();
                        WriteDirectOrQueueFireAndForget(transport, msg, ResultProcessor.DemandOK);
                    }
                }
            }

            var connType = transport.ConnectionType;
            if (connType == ConnectionType.Interactive)
            {
                AutoConfigure(transport, log);
            }

            var tracer = GetTracerMessage(true);
            tracer = LoggingMessage.Create(log, tracer);
            log?.WriteLine($"{Format.ToString(this)}: Sending critical tracer (handshake): {tracer.CommandAndKey}");
            WriteDirectOrQueueFireAndForget(transport, tracer, ResultProcessor.EstablishConnection).ForAwait();

            // Note: this **must** be the last thing on the subscription handshake, because after this
            // we will be in subscriber mode: regular commands cannot be sent
            if (connType == ConnectionType.Subscription)
            {
                var configChannel = Multiplexer.ConfigurationChangedChannel;
                if (configChannel != null)
                {
                    msg = Message.Create(-1, CommandFlags.FireAndForget, RedisCommand.SUBSCRIBE, (RedisChannel)configChannel);
                    // Note: this is NOT internal, we want it to queue in a backlog for sending when ready if necessary
                    WriteDirectOrQueueFireAndForget(transport, msg, ResultProcessor.TrackSubscriptions);
                }
            }
        }

        private void SetConfig<T>(ref T field, T value, [CallerMemberName] string? caller = null)
        {
            if (!EqualityComparer<T>.Default.Equals(field, value))
            {
                Multiplexer.Trace(caller + " changed from " + field + " to " + value, "Configuration");
                field = value;
                ClearMemoized();
                Multiplexer.ReconfigureIfNeeded(EndPoint, false, caller!);
            }
        }

        private void ClearMemoized()
        {
            supportsDatabases = null;
            supportsPrimaryWrites = null;
        }

        /// <summary>
        /// For testing only
        /// </summary>
        internal void SimulateConnectionFailure(SimulatedFailureType failureType)
        {
            var snapshot = _activePool;
            if (snapshot is List<Transport> list)
            {
                foreach (var transport in list)
                {
                    transport.SimulateConnectionFailure(failureType);
                }
            }
            else if (snapshot is Transport transport)
            {
                transport.SimulateConnectionFailure(failureType);
            }
            _subscription?.SimulateConnectionFailure(failureType);
        }
    }
}
