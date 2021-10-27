using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using static StackExchange.Redis.ConnectionMultiplexer;
using static StackExchange.Redis.PhysicalBridge;

namespace StackExchange.Redis
{
    [Flags]
    internal enum UnselectableFlags
    {
        None = 0,
        RedundantMaster = 1,
        DidNotRespond = 2,
        ServerType = 4
    }

    internal sealed partial class ServerEndPoint : IDisposable
    {
        internal volatile ServerEndPoint Master;
        internal volatile ServerEndPoint[] Replicas = Array.Empty<ServerEndPoint>();
        private static readonly Regex nameSanitizer = new Regex("[^!-~]", RegexOptions.Compiled);

        private readonly Hashtable knownScripts = new Hashtable(StringComparer.Ordinal);

        private int databases, writeEverySeconds;
        private PhysicalBridge interactive, subscription;
        private bool isDisposed;
        private ServerType serverType;
        private bool replicaReadOnly, isReplica;
        private volatile UnselectableFlags unselectableReasons;
        private Version version;

        internal void ResetNonConnected()
        {
            interactive?.ResetNonConnected();
            subscription?.ResetNonConnected();
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
            // overrides for twemproxy
            if (multiplexer.RawConfig.Proxy == Proxy.Twemproxy)
            {
                databases = 1;
                serverType = ServerType.Twemproxy;
            }
        }

        public ClusterConfiguration ClusterConfiguration { get; private set; }

        public int Databases { get { return databases; } set { SetConfig(ref databases, value); } }

        public EndPoint EndPoint { get; }

        public bool HasDatabases => serverType == ServerType.Standalone;

        public bool IsConnected => interactive?.IsConnected == true;

        public bool IsConnecting => interactive?.IsConnecting == true;

        private readonly List<TaskCompletionSource<string>> _pendingConnectionMonitors = new List<TaskCompletionSource<string>>();

        /// <summary>
        /// Awaitable state seeing if this endpoint is connected.
        /// </summary>
        public Task<string> OnConnectedAsync(LogProxy log = null, bool sendTracerIfConnected = false, bool autoConfigureIfConnected = false)
        {
            async Task<string> IfConnectedAsync(LogProxy log, bool sendTracerIfConnected, bool autoConfigureIfConnected)
            {
                log?.WriteLine($"{Format.ToString(this)}: OnConnectedAsync already connected start");
                if (autoConfigureIfConnected)
                {
                    await AutoConfigureAsync(null, log).ForAwait();
                }
                if (sendTracerIfConnected)
                {
                    await SendTracer(log).ForAwait();
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

        internal Exception LastException
        {
            get
            {
                var snapshot = interactive;
                var subEx = subscription?.LastException;
                var subExData = subEx?.Data;

                //check if subscription endpoint has a better lastexception
                if (subExData != null && subExData.Contains("Redis-FailureType") && subExData["Redis-FailureType"]?.ToString() != nameof(ConnectionFailureType.UnableToConnect))
                {
                    return subEx;
                }
                return snapshot?.LastException;
            }
        }

        internal PhysicalBridge.State ConnectionState
        {
            get
            {
                var tmp = interactive;
                return tmp.ConnectionState;
            }
        }

        public bool IsReplica { get { return isReplica; } set { SetConfig(ref isReplica, value); } }

        public long OperationCount
        {
            get
            {
                long total = 0;
                var tmp = interactive;
                if (tmp != null) total += tmp.OperationCount;
                tmp = subscription;
                if (tmp != null) total += tmp.OperationCount;
                return total;
            }
        }

        public bool RequiresReadMode => serverType == ServerType.Cluster && IsReplica;

        public ServerType ServerType { get { return serverType; } set { SetConfig(ref serverType, value); } }

        public bool ReplicaReadOnly { get { return replicaReadOnly; } set { SetConfig(ref replicaReadOnly, value); } }

        public bool AllowReplicaWrites { get; set; }

        public Version Version { get { return version; } set { SetConfig(ref version, value); } }

        public int WriteEverySeconds { get { return writeEverySeconds; } set { SetConfig(ref writeEverySeconds, value); } }

        internal ConnectionMultiplexer Multiplexer { get; }

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

        public void Dispose()
        {
            isDisposed = true;
            var tmp = interactive;
            interactive = null;
            tmp?.Dispose();

            tmp = subscription;
            subscription = null;
            tmp?.Dispose();
        }

        public PhysicalBridge GetBridge(ConnectionType type, bool create = true, LogProxy log = null)
        {
            if (isDisposed) return null;
            return type switch
            {
                ConnectionType.Interactive => interactive ?? (create ? interactive = CreateBridge(ConnectionType.Interactive, log) : null),
                ConnectionType.Subscription => subscription ?? (create ? subscription = CreateBridge(ConnectionType.Subscription, log) : null),
                _ => null,
            };
        }

        public PhysicalBridge GetBridge(RedisCommand command, bool create = true)
        {
            if (isDisposed) return null;
            switch (command)
            {
                case RedisCommand.SUBSCRIBE:
                case RedisCommand.UNSUBSCRIBE:
                case RedisCommand.PSUBSCRIBE:
                case RedisCommand.PUNSUBSCRIBE:
                    return subscription ?? (create ? subscription = CreateBridge(ConnectionType.Subscription, null) : null);
                default:
                    return interactive ?? (create ? interactive = CreateBridge(ConnectionType.Interactive, null) : null);
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

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Style", "IDE0071:Simplify interpolation", Justification = "Allocations (string.Concat vs. string.Format)")]
        public void UpdateNodeRelations(ClusterConfiguration configuration)
        {
            var thisNode = configuration.Nodes.FirstOrDefault(x => x.EndPoint.Equals(EndPoint));
            if (thisNode != null)
            {
                Multiplexer.Trace($"Updating node relations for {thisNode.EndPoint.ToString()}...");
                List<ServerEndPoint> replicas = null;
                ServerEndPoint master = null;
                foreach (var node in configuration.Nodes)
                {
                    if (node.NodeId == thisNode.ParentNodeId)
                    {
                        master = Multiplexer.GetServerEndPoint(node.EndPoint);
                    }
                    else if (node.ParentNodeId == thisNode.NodeId)
                    {
                        (replicas ??= new List<ServerEndPoint>()).Add(Multiplexer.GetServerEndPoint(node.EndPoint));
                    }
                }
                Master = master;
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

        public override string ToString() => Format.ToString(EndPoint);

        [Obsolete("prefer async")]
        public WriteResult TryWriteSync(Message message) => GetBridge(message.Command)?.TryWriteSync(message, isReplica) ?? WriteResult.NoConnectionAvailable;

        public ValueTask<WriteResult> TryWriteAsync(Message message) => GetBridge(message.Command)?.TryWriteAsync(message, isReplica) ?? new ValueTask<WriteResult>(WriteResult.NoConnectionAvailable);

        internal void Activate(ConnectionType type, LogProxy log)
        {
            GetBridge(type, true, log);
        }

        internal void AddScript(string script, byte[] hash)
        {
            lock (knownScripts)
            {
                knownScripts[script] = hash;
            }
        }

        internal async Task AutoConfigureAsync(PhysicalConnection connection, LogProxy log = null)
        {
            if (serverType == ServerType.Twemproxy)
            {
                // don't try to detect configuration; all the config commands are disabled, and
                // the fallback master/replica detection won't help
                return;
            }

            log?.WriteLine($"{Format.ToString(this)}: Auto-configuring...");

            var commandMap = Multiplexer.CommandMap;
#pragma warning disable CS0618
            const CommandFlags flags = CommandFlags.FireAndForget | CommandFlags.HighPriority | CommandFlags.NoRedirect;
#pragma warning restore CS0618
            var features = GetFeatures();
            Message msg;

            var autoConfigProcessor = new ResultProcessor.AutoConfigureProcessor(log);

            if (commandMap.IsAvailable(RedisCommand.CONFIG))
            {
                if (Multiplexer.RawConfig.KeepAlive <= 0)
                {
                    msg = Message.Create(-1, flags, RedisCommand.CONFIG, RedisLiterals.GET, RedisLiterals.timeout);
                    msg.SetInternalCall();
                    await WriteDirectOrQueueFireAndForgetAsync(connection, msg, autoConfigProcessor).ForAwait();
                }
                msg = Message.Create(-1, flags, RedisCommand.CONFIG, RedisLiterals.GET, features.ReplicaCommands ? RedisLiterals.replica_read_only : RedisLiterals.slave_read_only);
                msg.SetInternalCall();
                await WriteDirectOrQueueFireAndForgetAsync(connection, msg, autoConfigProcessor).ForAwait();
                msg = Message.Create(-1, flags, RedisCommand.CONFIG, RedisLiterals.GET, RedisLiterals.databases);
                msg.SetInternalCall();
                await WriteDirectOrQueueFireAndForgetAsync(connection, msg, autoConfigProcessor).ForAwait();
            }
            if (commandMap.IsAvailable(RedisCommand.SENTINEL))
            {
                msg = Message.Create(-1, flags, RedisCommand.SENTINEL, RedisLiterals.MASTERS);
                msg.SetInternalCall();
                await WriteDirectOrQueueFireAndForgetAsync(connection, msg, autoConfigProcessor).ForAwait();
            }
            if (commandMap.IsAvailable(RedisCommand.INFO))
            {
                lastInfoReplicationCheckTicks = Environment.TickCount;
                if (features.InfoSections)
                {
                    msg = Message.Create(-1, flags, RedisCommand.INFO, RedisLiterals.replication);
                    msg.SetInternalCall();
                    await WriteDirectOrQueueFireAndForgetAsync(connection, msg, autoConfigProcessor).ForAwait();

                    msg = Message.Create(-1, flags, RedisCommand.INFO, RedisLiterals.server);
                    msg.SetInternalCall();
                    await WriteDirectOrQueueFireAndForgetAsync(connection, msg, autoConfigProcessor).ForAwait();
                }
                else
                {
                    msg = Message.Create(-1, flags, RedisCommand.INFO);
                    msg.SetInternalCall();
                    await WriteDirectOrQueueFireAndForgetAsync(connection, msg, autoConfigProcessor).ForAwait();
                }
            }
            else if (commandMap.IsAvailable(RedisCommand.SET))
            {
                // this is a nasty way to find if we are a replica, and it will only work on up-level servers, but...
                RedisKey key = Multiplexer.UniqueId;
                // the actual value here doesn't matter (we detect the error code if it fails); the value here is to at least give some
                // indication to anyone watching via "monitor", but we could send two guids (key/value) and it would work the same
                msg = Message.Create(0, flags, RedisCommand.SET, key, RedisLiterals.replica_read_only, RedisLiterals.PX, 1, RedisLiterals.NX);
                msg.SetInternalCall();
                await WriteDirectOrQueueFireAndForgetAsync(connection, msg, autoConfigProcessor).ForAwait();
            }
            if (commandMap.IsAvailable(RedisCommand.CLUSTER))
            {
                msg = Message.Create(-1, flags, RedisCommand.CLUSTER, RedisLiterals.NODES);
                msg.SetInternalCall();
                await WriteDirectOrQueueFireAndForgetAsync(connection, msg, ResultProcessor.ClusterNodes).ForAwait();
            }
        }

        private int _nextReplicaOffset;
        internal uint NextReplicaOffset() // used to round-robin between multiple replicas
            => (uint)System.Threading.Interlocked.Increment(ref _nextReplicaOffset);

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

        private string runId;
        internal string RunId
        {
            get { return runId; }
            set
            {
                if (value != runId) // we only care about changes
                {
                    // if we had an old run-id, and it has changed, then the
                    // server has been restarted; which means the script cache
                    // is toast
                    if (runId != null) FlushScriptCache();
                    runId = value;
                }
            }
        }

        internal ServerCounters GetCounters()
        {
            var counters = new ServerCounters(EndPoint);
            interactive?.GetCounters(counters.Interactive);
            subscription?.GetCounters(counters.Subscription);
            return counters;
        }

        internal BridgeStatus GetBridgeStatus(RedisCommand command)
        {
            try
            {
                return GetBridge(command, false)?.GetStatus() ?? BridgeStatus.Zero;
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

        internal byte[] GetScriptHash(string script, RedisCommand command)
        {
            var found = (byte[])knownScripts[script];
            if (found == null && command == RedisCommand.EVALSHA)
            {
                // the script provided is a hex sha; store and re-use the ascii for that
                found = Encoding.ASCII.GetBytes(script);
                lock (knownScripts)
                {
                    knownScripts[script] = found;
                }
            }
            return found;
        }

        internal string GetStormLog(RedisCommand command)
        {
            var bridge = GetBridge(command);
            return bridge?.GetStormLog();
        }

        internal Message GetTracerMessage(bool assertIdentity)
        {
            // different configurations block certain commands, as can ad-hoc local configurations, so
            // we'll do the best with what we have available.
            // note that the muxer-ctor asserts that one of ECHO, PING, TIME of GET is available
            // see also: TracerProcessor
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
                // we'll use echo as a PING substitute if it is all we have (in preference to EXISTS)
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
            var bridge = unselectableReasons == 0 ? GetBridge(command, false) : null;
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

        internal void OnDisconnected(PhysicalBridge bridge)
        {
            if (bridge == interactive)
            {
                CompletePendingConnectionMonitors("Disconnected");
            }
        }

        internal Task OnEstablishingAsync(PhysicalConnection connection, LogProxy log)
        {
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

        private static async Task OnEstablishingAsyncAwaited(PhysicalConnection connection, Task handshake)
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
        
        internal void OnFullyEstablished(PhysicalConnection connection, string source)
        {
            try
            {
                var bridge = connection?.BridgeCouldBeNull;
                if (bridge != null)
                {
                    if (bridge == subscription)
                    {
                        Multiplexer.ResendSubscriptions(this);
                    }
                    else if (bridge == interactive)
                    {
                        CompletePendingConnectionMonitors(source);
                    }

                    Multiplexer.OnConnectionRestored(EndPoint, bridge.ConnectionType, connection?.ToString());                    
                }
            }
            catch (Exception ex)
            {
                connection.RecordConnectionFailed(ConnectionFailureType.InternalFailure, ex);
            }
        }

        internal int LastInfoReplicationCheckSecondsAgo
        {
            get { return unchecked(Environment.TickCount - Thread.VolatileRead(ref lastInfoReplicationCheckTicks)) / 1000; }
        }

        private EndPoint masterEndPoint;
        public EndPoint MasterEndPoint
        {
            get { return masterEndPoint; }
            set { SetConfig(ref masterEndPoint, value); }
        }

        internal bool CheckInfoReplication()
        {
            lastInfoReplicationCheckTicks = Environment.TickCount;
            ResetExponentiallyReplicationCheck();
            PhysicalBridge bridge;
            if (version >= RedisFeatures.v2_8_0 && Multiplexer.CommandMap.IsAvailable(RedisCommand.INFO)
                && (bridge = GetBridge(ConnectionType.Interactive, false)) != null)
            {
#pragma warning disable CS0618
                var msg = Message.Create(-1, CommandFlags.FireAndForget | CommandFlags.HighPriority | CommandFlags.NoRedirect, RedisCommand.INFO, RedisLiterals.replication);
                msg.SetInternalCall();
                WriteDirectFireAndForgetSync(msg, ResultProcessor.AutoConfigure, bridge);
#pragma warning restore CS0618
                return true;
            }
            return false;
        }

        private int lastInfoReplicationCheckTicks;
        internal volatile int ConfigCheckSeconds;
        [ThreadStatic]
        private static Random r;


        // Forces frequent replication check starting from 1 second up to max ConfigCheckSeconds with an exponential increment
        internal void ForceExponentialBackoffReplicationCheck()
        {
            ConfigCheckSeconds = 1;  // start checking info replication more frequently
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
            // don't overlap operations on an endpoint
            if (Interlocked.CompareExchange(ref _heartBeatActive, 1, 0) == 0)
            {
                try
                {
                    interactive?.OnHeartbeat(false);
                    subscription?.OnHeartbeat(false);
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

        internal Task<T> WriteDirectAsync<T>(Message message, ResultProcessor<T> processor, object asyncState = null, PhysicalBridge bridge = null)
        {
            static async Task<T> Awaited(ServerEndPoint @this, Message message, ValueTask<WriteResult> write, TaskCompletionSource<T> tcs)
            {
                var result = await write.ForAwait();
                if (result != WriteResult.Success)
                {
                    var ex = @this.Multiplexer.GetException(result, message, @this);
                    ConnectionMultiplexer.ThrowFailed(tcs, ex);
                }
                return await tcs.Task.ForAwait();
            }

            var source = TaskResultBox<T>.Create(out var tcs, asyncState);
            message.SetSource(processor, source);
            if (bridge == null) bridge = GetBridge(message.Command);

            WriteResult result;
            if (bridge == null)
            {
                result = WriteResult.NoConnectionAvailable;
            }
            else
            {
                var write = bridge.TryWriteAsync(message, isReplica);
                if (!write.IsCompletedSuccessfully)
                {
                    return Awaited(this, message, write, tcs);
                }
                result = write.Result;
            }

            if (result != WriteResult.Success)
            {
                var ex = Multiplexer.GetException(result, message, this);
                ConnectionMultiplexer.ThrowFailed(tcs, ex);
            }
            return tcs.Task;
        }

        [Obsolete("prefer async")]
        internal void WriteDirectFireAndForgetSync<T>(Message message, ResultProcessor<T> processor, PhysicalBridge bridge = null)
        {
            if (message != null)
            {
                message.SetSource(processor, null);
                Multiplexer.Trace("Enqueue: " + message);
                (bridge ?? GetBridge(message.Command)).TryWriteSync(message, isReplica);
            }
        }

        internal void ReportNextFailure()
        {
            interactive?.ReportNextFailure();
            subscription?.ReportNextFailure();
        }

        internal Task<bool> SendTracer(LogProxy log = null)
        {
            var msg = GetTracerMessage(false);
            msg = LoggingMessage.Create(log, msg);
            return WriteDirectAsync(msg, ResultProcessor.Tracer);
        }

        internal string Summary()
        {
            var sb = new StringBuilder(Format.ToString(EndPoint))
                .Append(": ").Append(serverType).Append(" v").Append(version).Append(", ").Append(isReplica ? "replica" : "master");

            if (databases > 0) sb.Append("; ").Append(databases).Append(" databases");
            if (writeEverySeconds > 0)
                sb.Append("; keep-alive: ").Append(TimeSpan.FromSeconds(writeEverySeconds));
            var tmp = interactive;
            sb.Append("; int: ").Append(tmp?.ConnectionState.ToString() ?? "n/a");
            tmp = subscription;
            if (tmp == null)
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
        /// Write the message directly or queues in the handshake (priority) queue.
        /// </summary>
        internal ValueTask WriteDirectOrQueueFireAndForgetAsync<T>(PhysicalConnection connection, Message message, ResultProcessor<T> processor)
        {
            static async ValueTask Awaited(ValueTask<WriteResult> l_result) => await l_result.ForAwait();

            if (message != null)
            {
                message.SetSource(processor, null);
                ValueTask<WriteResult> result;
                if (connection == null)
                {
                    Multiplexer.Trace($"{Format.ToString(this)}: Enqueue (async): " + message);
                    result = GetBridge(message.Command).TryWriteAsync(message, isReplica, isHandshake: true);
                }
                else
                {
                    Multiplexer.Trace($"{Format.ToString(this)}: Writing direct (async): " + message);
                    var bridge = connection.BridgeCouldBeNull;
                    if (bridge == null)
                    {
                        throw new ObjectDisposedException(connection.ToString());
                    }
                    else
                    {
                        result = bridge.WriteMessageTakingWriteLockAsync(connection, message, isHandshake: true);
                    }
                }

                if (!result.IsCompletedSuccessfully)
                {
                    return Awaited(result);
                }
            }
            return default;
        }

        private PhysicalBridge CreateBridge(ConnectionType type, LogProxy log)
        {
            if (Multiplexer.IsDisposed) return null;
            Multiplexer.Trace(type.ToString());
            var bridge = new PhysicalBridge(this, type, Multiplexer.TimeoutMilliseconds);
            bridge.TryConnect(log);
            return bridge;
        }

        private async Task HandshakeAsync(PhysicalConnection connection, LogProxy log)
        {
            log?.WriteLine($"{Format.ToString(this)}: Server handshake");
            if (connection == null)
            {
                Multiplexer.Trace("No connection!?");
                return;
            }
            Message msg;
            // note that we need "" (not null) for password in the case of 'nopass' logins
            string user = Multiplexer.RawConfig.User, password = Multiplexer.RawConfig.Password ?? "";
            if (!string.IsNullOrWhiteSpace(user))
            {
                log?.WriteLine($"{Format.ToString(this)}: Authenticating (user/password)");
                msg = Message.Create(-1, CommandFlags.FireAndForget, RedisCommand.AUTH, (RedisValue)user, (RedisValue)password);
                msg.SetInternalCall();
                await WriteDirectOrQueueFireAndForgetAsync(connection, msg, ResultProcessor.DemandOK).ForAwait();
            }
            else if (!string.IsNullOrWhiteSpace(password))
            {
                log?.WriteLine($"{Format.ToString(this)}: Authenticating (password)");
                msg = Message.Create(-1, CommandFlags.FireAndForget, RedisCommand.AUTH, (RedisValue)password);
                msg.SetInternalCall();
                await WriteDirectOrQueueFireAndForgetAsync(connection, msg, ResultProcessor.DemandOK).ForAwait();
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
                        await WriteDirectOrQueueFireAndForgetAsync(connection, msg, ResultProcessor.DemandOK).ForAwait();
                    }
                }
            }

            var bridge = connection.BridgeCouldBeNull;
            if (bridge == null)
            {
                return;
            }
            var connType = bridge.ConnectionType;

            if (connType == ConnectionType.Interactive)
            {
                await AutoConfigureAsync(connection, log);
            }

            var tracer = GetTracerMessage(true);
            tracer = LoggingMessage.Create(log, tracer);
            log?.WriteLine($"{Format.ToString(this)}: Sending critical tracer (handshake): {tracer.CommandAndKey}");
            await WriteDirectOrQueueFireAndForgetAsync(connection, tracer, ResultProcessor.EstablishConnection).ForAwait();

            // note: this **must** be the last thing on the subscription handshake, because after this
            // we will be in subscriber mode: regular commands cannot be sent
            if (connType == ConnectionType.Subscription)
            {
                var configChannel = Multiplexer.ConfigurationChangedChannel;
                if (configChannel != null)
                {
                    msg = Message.Create(-1, CommandFlags.FireAndForget, RedisCommand.SUBSCRIBE, (RedisChannel)configChannel);
                    await WriteDirectOrQueueFireAndForgetAsync(connection, msg, ResultProcessor.TrackSubscriptions).ForAwait();
                }
            }
            log?.WriteLine($"{Format.ToString(this)}: Flushing outbound buffer");
            await connection.FlushAsync().ForAwait();
        }

        private void SetConfig<T>(ref T field, T value, [CallerMemberName] string caller = null)
        {
            if (!EqualityComparer<T>.Default.Equals(field, value))
            {
                Multiplexer.Trace(caller + " changed from " + field + " to " + value, "Configuration");
                field = value;
                Multiplexer.ReconfigureIfNeeded(EndPoint, false, caller);
            }
        }

        /// <summary>
        /// For testing only
        /// </summary>
        internal void SimulateConnectionFailure(SimulatedFailureType failureType)
        {
            interactive?.SimulateConnectionFailure(failureType);
            subscription?.SimulateConnectionFailure(failureType);
        }
    }
}
