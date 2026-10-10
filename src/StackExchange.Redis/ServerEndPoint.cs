using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using StackExchange.Redis.Availability;
using StackExchange.Redis.Caching;

namespace StackExchange.Redis
{
    [Flags]
    internal enum UnselectableFlags
    {
        None = 0,
        RedundantPrimary = 1,
        DidNotRespond = 2,
        ServerType = 4,

        /// <summary>This server is being retired; it must not be selected for new work.</summary>
        Retiring = 8,
    }

    /// <summary>
    /// How we came to know about a server, which decides what may retire it: "absent from the topology" only
    /// means anything if the source that would have listed it actually ran.
    /// </summary>
    internal enum ServerProvenance
    {
        /// <summary>Named in <see cref="ConfigurationOptions.EndPoints"/>; never pruned.</summary>
        Configured = 0,

        /// <summary>Discovered from cluster topology; prunable when the topology stops listing it.</summary>
        ClusterTopology,

        /// <summary>
        /// Discovered from sentinel. Cluster absence must never count against it - in a sentinel deployment
        /// there is no cluster topology at all, so a single rule would prune the entire thing.
        /// </summary>
        Sentinel,

        /// <summary>
        /// Learned from a redirect, so it is legitimately ahead of the topology; initial absence is expected
        /// rather than evidence.
        /// </summary>
        Redirect,
    }

    internal sealed partial class ServerEndPoint : IDisposable
    {
        internal volatile ServerEndPoint? Primary;
        internal volatile ServerEndPoint[] Replicas = Array.Empty<ServerEndPoint>();
        private static readonly Regex nameSanitizer = new Regex("[^!-~]+", RegexOptions.Compiled);

        private readonly Hashtable knownScripts = new Hashtable(StringComparer.Ordinal);

        private int databases, writeEverySeconds;
        private bool isDisposed, replicaReadOnly, isReplica, allowReplicaWrites;
        private bool? supportsDatabases, supportsPrimaryWrites;
        private ServerType serverType;
        private TracerKeyCache? tracerKeyCache;
        private volatile UnselectableFlags unselectableReasons;
        private Version version;

        public ServerEndPoint(ConnectionMultiplexer multiplexer, EndPoint endpoint, ServerProvenance provenance)
        {
            Multiplexer = multiplexer;
            EndPoint = endpoint;
            // both collections, deliberately: ResolveDns rewrites the multiplexer's working set at startup,
            // replacing configured DnsEndPoints with the addresses they resolved to, while RawConfig keeps the
            // original names. Testing only RawConfig would classify a *configured* endpoint as discovered - and
            // therefore prunable - whenever ResolveDns is enabled
            Provenance = multiplexer.RawConfig.EndPoints.Contains(endpoint) || multiplexer.EndPoints.Contains(endpoint)
                ? ServerProvenance.Configured
                : provenance;
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

        private RedisServer? _defaultServer;
        public RedisServer GetRedisServer(object? asyncState)
            => asyncState is null
            ? (_defaultServer ??= new RedisServer(this, null)) // reuse and memoize
            : new RedisServer(this, asyncState);

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
            supportsDatabases ??= serverType switch
            {
                ServerType.Standalone => true,
                ServerType.Cluster => _productVariant is ProductVariant.Valkey,
                _ => false,
            } && Multiplexer.CommandMap.IsAvailable(RedisCommand.SELECT);

        public int Databases
        {
            get => databases;
            set => SetConfig(ref databases, value);
        }

        public bool IsConnecting => false; // the new core dials on demand and reports only whether it is connected
        public bool IsConnected => Multiplexer.ConnectionsIfCreated?.IsConnected(EndPoint) == true;
        // ...and where there is no second socket, SupportsSubscriptions is the term that would otherwise
        // go missing: in v3 a bridge for a disabled SUBSCRIBE never connected, so the answer was no by
        // construction, where sharing one connection has to say no on purpose.
        public bool IsSubscriberConnected => IsConnected && (SharesSubscriptionConnection() || SupportsSubscriptions);

        /// <summary>
        /// Whether pub/sub shares the interactive connection: only under RESP3, and only when opted into via
        /// <see cref="ConfigurationOptions.SharedSubscriptionConnection"/>. Otherwise subscriptions get a dedicated
        /// connection, as they always do under RESP2 - because the server classifies any connection with a live
        /// subscription as a pub/sub client, applying the (much tighter) pub/sub output-buffer limits to it; see #3263.
        /// </summary>
        public bool SharesSubscriptionConnection() => Multiplexer.RawConfig.SharedSubscriptionConnection && KnowOrAssumeResp3();

        public bool KnowOrAssumeResp3()
        {
            var protocol = Protocol;
            return protocol is not null
                ? protocol.GetValueOrDefault() >= RedisProtocol.Resp3 // <= if we've completed handshake, use what we *know for sure*
                : Multiplexer.RawConfig.TryResp3(); // otherwise, use what we *expect*
        }

        public bool SupportsSubscriptions => Multiplexer.CommandMap.IsAvailable(RedisCommand.SUBSCRIBE);
        public bool SupportsPrimaryWrites => supportsPrimaryWrites ??= !IsReplica || !ReplicaReadOnly || AllowReplicaWrites;

        private readonly List<TaskCompletionSource<string>> _pendingConnectionMonitors = new List<TaskCompletionSource<string>>();

        /// <summary>
        /// Awaitable state seeing if this endpoint is connected.
        /// </summary>
        public Task<string> OnConnectedAsync(ILogger? log = null, bool sendTracerIfConnected = false, bool autoConfigureIfConnected = false)
        {
            async Task<string> IfConnectedAsync(ILogger? log, bool sendTracerIfConnected, bool autoConfigureIfConnected)
            {
                log?.LogInformationOnConnectedAsyncAlreadyConnectedStart(new(this));
                if (sendTracerIfConnected)
                {
                    await SendTracerAsync(log).ForAwait();
                }
                log?.LogInformationOnConnectedAsyncAlreadyConnectedEnd(new(this));
                return "Already connected";
            }

            if (!IsConnected)
            {
                log?.LogInformationOnConnectedAsyncInit(new(this), InteractiveConnectionState);
                var tcs = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
                _ = tcs.Task.ContinueWith(t => log?.LogInformationOnConnectedAsyncCompleted(new(this), t.Result));
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
            => Multiplexer.ConnectionsIfCreated?.LastConnectFault(EndPoint, ConnectionType.Interactive)
            ?? Multiplexer.ConnectionsIfCreated?.LastConnectFault(EndPoint, ConnectionType.Subscription);

        internal BridgeState InteractiveConnectionState
            => Multiplexer.ConnectionsIfCreated?.IsInteractiveConnected(EndPoint) == true ? BridgeState.ConnectedEstablished : BridgeState.Disconnected;

        internal BridgeState SubscriptionConnectionState
            => Multiplexer.ConnectionsIfCreated?.IsSubscriptionConnected(EndPoint) == true ? BridgeState.ConnectedEstablished : InteractiveConnectionState;

        public long OperationCount => Multiplexer.ConnectionsIfCreated?.OperationCount(EndPoint) ?? 0;

        public bool RequiresReadMode => serverType == ServerType.Cluster && IsReplica;

        public ServerType ServerType
        {
            get => serverType;
            set => SetConfig(ref serverType, value);
        }

        public bool IsReplica
        {
            get => isReplica;
            set
            {
                var changed = isReplica != value;
                SetConfig(ref isReplica, value);

                // ...and tell the core's own topology, which learns roles only from its handshakes and
                // dials lazily: without this the replica of an ordinary standalone pair is never dialled, so
                // its role is never learned, so a DemandReplica read has no replica to choose and goes to the
                // primary. See RespConnectionManager.OnRole.
                if (changed) PublishRole();
            }
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

        /// <summary>What this server is being spoken to in, as far as any connection to it has settled.</summary>
        /// <remarks>
        /// <b>Asked of the core, which records what each endpoint's handshake actually agreed.</b> In v3 this
        /// read the interactive bridge; here <c>null</c> means nothing has handshaken this endpoint yet, not
        /// that the answer is unknowable. Reporting <c>null</c> for a server that has been answering RESP3
        /// since the first command would be a diagnostic that is wrong exactly when somebody is trying to
        /// find out what the protocol is.
        /// </remarks>
        public RedisProtocol? Protocol
            => Multiplexer?.ConnectionsIfCreated?.ObservedProtocol(EndPoint);

        public int WriteEverySeconds
        {
            get => writeEverySeconds;
            set => SetConfig(ref writeEverySeconds, value);
        }

        internal ConnectionMultiplexer Multiplexer { get; }

        /// <summary>
        /// Whether this server has been disposed; a retired server must not be handed out again.
        /// </summary>
        internal bool IsDisposed => isDisposed;

        /// <summary>How we learned of this server; see <see cref="ServerProvenance"/>.</summary>
        internal ServerProvenance Provenance { get; private set; }

        /// <summary>
        /// The topology generation in which this server was last listed, or -1 if it never has been.
        /// </summary>
        internal int LastSeenGeneration { get; private set; } = -1;

        /// <summary>
        /// The generation in which this server first went missing from the topology, or -1 if present.
        /// </summary>
        internal int AbsentSinceGeneration { get; private set; } = -1;

        /// <summary>
        /// Note that the topology still lists this server, clearing any accrued absence.
        /// </summary>
        internal void OnSeenInTopology(int generation)
        {
            LastSeenGeneration = generation;
            AbsentSinceGeneration = -1;

            // a node first learned from a redirect is confirmed by the topology, so it stops being a special
            // case; configured endpoints keep their provenance, since nothing may prune them
            if (Provenance == ServerProvenance.Redirect) Provenance = ServerProvenance.ClusterTopology;
        }

        /// <summary>
        /// Note that the topology did not list this server. Returns the number of consecutive generations it
        /// has now been missing for, counting this one.
        /// </summary>
        /// <remarks>
        /// The design notes proposed also resetting this whenever the server had been *used* since the last
        /// absence, on the grounds that "recently useful" is stronger evidence than "not listed". That is not
        /// implementable as stated and turned out to be unnecessary: the only usage counter v3 had
        /// (<c>PhysicalBridge.IncrementOpCount</c>) was incremented by our own heartbeat pings as well as by
        /// callers, so an idle-but-connected server never looks unused - and every case it was meant to
        /// protect is already covered by <see cref="IsIdle"/>, since a server actually carrying traffic owns
        /// slots in the map. What remains uncovered is a server used only via <c>GetServer</c> by hand while
        /// absent from the topology, and pruning that is consistent with the endpoint collection being a
        /// snapshot.
        /// </remarks>
        internal int OnMissingFromTopology(int generation)
        {
            if (AbsentSinceGeneration < 0)
            {
                AbsentSinceGeneration = generation;
                return 1;
            }
            return generation - AbsentSinceGeneration + 1;
        }

        /// <summary>
        /// Whether retiring this server would abandon anything: slots it owns, subscriptions it carries, or
        /// work it still owes.
        /// </summary>
        /// <remarks>
        /// Subscriptions are asked of the registry. In v3 they were asked of the bridges too, whose counters
        /// were the record of what those bridges subscribed and so were silent about a subscription carried
        /// any other way - a server whose only work was one of those looked idle and was pruned while still
        /// delivering. The registry entry is the fact that does not depend on who carried it.
        /// </remarks>
        internal bool IsIdle()
            => !Multiplexer.ServerSelectionStrategy.OwnsAnySlot(this)
            && !Multiplexer.AnySubscriptionNames(EndPoint)
            && !HasCallerWork();

        /// <summary>
        /// Whether a *caller* is waiting on anything here, which is the only kind of work that should stop us
        /// retiring a server.
        /// </summary>
        /// <remarks>
        /// Deliberately not <see cref="GetOutstandingCount"/>, which counts everything. A node the topology has
        /// stopped listing still receives our autoconfigure probes on every pass, and nothing answers them, so
        /// they accumulate in its backlog: measured at ~170 per pass, growing without bound. Counting those
        /// made the node look busy *because* we were looking for it, so it could never be retired - the
        /// precondition defeated itself in exactly the case pruning exists for. Keep-alive traffic has the same
        /// property, and is excluded by the same test, since both set the internal-call flag.
        /// </remarks>
        internal bool HasCallerWork()
            => Multiplexer.ConnectionsIfCreated?.HasCallerWork(EndPoint) == true;

        /// <summary>
        /// Work this server still owes an answer on: written-and-awaiting-response, plus anything queued in
        /// the backlog. Zero means a retirement can complete without abandoning anyone.
        /// </summary>
        internal int GetOutstandingCount()
        {
            var counters = GetCounters();
            return counters.Interactive.SentItemsAwaitingResponse + counters.Interactive.PendingUnsentItems
                + counters.Subscription.SentItemsAwaitingResponse + counters.Subscription.PendingUnsentItems;
        }

        /// <summary>
        /// Retire this server gracefully: stop accepting new work, let what has already been written complete,
        /// then tear the connections down. Distinct from <see cref="Dispose"/>, which is the abrupt path and
        /// abandons anything outstanding.
        /// </summary>
        /// <param name="reason">Why this server is being retired; for logging.</param>
        /// <param name="drainTimeout">How long to allow the drain before closing regardless.</param>
        /// <param name="log">Optional logger.</param>
        internal async Task RetireAsync(string reason, TimeSpan drainTimeout, ILogger? log = null)
        {
            if (isDisposed) return;

            // stop being selected *first*, so the drain is bounded: nothing new arrives while we wait
            SetUnselectable(UnselectableFlags.Retiring);
            log?.LogInformationRetiringServer(new(EndPoint), reason);

            // Drain what a *caller* is waiting for, not everything outstanding. Our own probes to a node that
            // has gone away will never be answered, so draining on the total means always waiting out the full
            // timeout before letting go - measured: a departed node accumulates our autoconfigure traffic
            // indefinitely (500+ and climbing), so the drain never once completed early. Callers are who the
            // drain exists for; nobody is waiting on our keep-alives.
            // Stopwatch rather than TickCount64: the latter does not exist on the down-level targets
            var watch = ValueStopwatch.StartNew();
            while (HasCallerWork() && watch.ElapsedMilliseconds < drainTimeout.TotalMilliseconds)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(20)).ForAwait();
            }

            if (HasCallerWork())
            {
                // deliberately reported: an abandoned command is exactly what a caller will be asking about.
                // The count is the total, since that is what is actually being dropped on the floor
                log?.LogInformationRetiringServerAbandoned(new(EndPoint), GetOutstandingCount());
            }

            Dispose();
        }

        public void Dispose()
        {
            // the connections belong to the new core, which drops this endpoint's when the multiplexer retires
            // the server (RespConnectionManager.RetireEndpointAsync) and all of them when it is disposed
            isDisposed = true;
        }

        public RedisFeatures GetFeatures() => new RedisFeatures(version);

        /// <summary>
        /// The <c>CLUSTER SLOTS</c> view of the topology, keyed on node-id. Populated alongside
        /// <see cref="ClusterConfiguration"/> but not yet used for routing, so the two can be compared
        /// before anything depends on this one.
        /// </summary>
        internal ClusterTopology? ClusterTopology { get; private set; }

        internal void SetClusterSlots(ClusterSlotsResult? slots)
        {
            var topology = ClusterTopology.From(slots);
            if (topology is not null)
            {
                ClusterTopology = topology;
                Multiplexer.Trace($"Shadow topology: {topology.Nodes.Count} nodes");

                // not routing on this yet, but the identities are useful immediately: they let a node reached
                // by its other name resolve to the server we already have, rather than becoming a duplicate
                Multiplexer.RegisterServerIdentities(topology);
            }
        }

        public void SetClusterConfiguration(ClusterConfiguration configuration)
        {
            ClusterConfiguration = configuration;

            if (configuration != null)
            {
                Multiplexer.Trace("Updating cluster ranges...");

                // the SLOTS view drives the slot map when this server supplied one; NODES remains the source
                // for node relations below, and for anything SLOTS does not report
                if (ClusterTopology is { } topology)
                {
                    Multiplexer.UpdateClusterRange(topology);
                }
                else
                {
                    Multiplexer.UpdateClusterRange(configuration);
                }
                Multiplexer.ApplyClusterRoles(configuration, ClusterTopology);
                Multiplexer.Trace("Resolving genealogy...");
                UpdateNodeRelations(configuration);
                Multiplexer.Trace("Cluster configured");
            }
        }

        public void UpdateNodeRelations(ClusterConfiguration configuration)
        {
            var thisNode = GetClusterNode(configuration);
            if (thisNode != null)
            {
                Multiplexer.Trace($"Updating node relations for {Format.ToString(thisNode.EndPoint)}...");
                List<ServerEndPoint>? replicas = null;
                ServerEndPoint? primary = null;
                foreach (var node in configuration.Nodes)
                {
                    if (node.IgnoreFromClient) continue;

                    if (node.NodeId == thisNode.ParentNodeId)
                    {
                        primary = Multiplexer.GetServerEndPoint(node.EndPoint, ServerProvenance.ClusterTopology);
                    }
                    else if (node.ParentNodeId == thisNode.NodeId && node.EndPoint is not null)
                    {
                        (replicas ??= new List<ServerEndPoint>()).Add(Multiplexer.GetServerEndPoint(node.EndPoint, ServerProvenance.ClusterTopology));
                    }
                }
                Primary = primary;
                Replicas = replicas?.ToArray() ?? Array.Empty<ServerEndPoint>();
            }
        }

        private ClusterNode? GetClusterNode(ClusterConfiguration? configuration) =>
            configuration?[EndPoint];

        internal int? GetServableSlot()
        {
            if (ServerType != ServerType.Cluster || GetClusterNode(ClusterConfiguration) is not { } node) return null;
            if (node.Slots.Count == 0 && node.Parent is { } parent) node = parent;
            return node.Slots.Count == 0 ? null : node.Slots[0].From;
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
                    PublishSelectable();
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
                    PublishSelectable();
                }
            }
        }

        /// <summary>Tell the core's topology what reconfiguration has just decided about this server.</summary>
        /// <remarks>
        /// <b>A decision, not an observation</b>: whether a server is retiring or redundant is something the
        /// client concludes during reconfiguration, so there is nothing for the topology to discover on a
        /// connection - it has to be told. Pushed only when the answer CHANGES, which is rare.
        /// <para>
        /// <b><c>DidNotRespond</c> is excluded, and that reversed an earlier decision.</b> It was left in on
        /// the reasoning that agreeing about a server which did not respond costs nothing - but it was set
        /// until the v3 bridge had connected at least once, and while both cores coexisted those bridges
        /// largely did not connect at all. So every endpoint they had not dialled was published as
        /// unselectable, permanently: a replica the new core would happily have used was barred from ever
        /// being chosen, and a <c>DemandReplica</c> read went to the primary instead. Connectivity is the one
        /// thing the core tracks for itself, and it distinguishes "nobody has dialled this yet" from "this is
        /// down" - which this flag cannot.
        /// </para>
        /// </remarks>
        private void PublishSelectable()
            => Multiplexer.ConnectionsIfCreated?.OnSelectable(
                EndPoint,
                (unselectableReasons & ~UnselectableFlags.DidNotRespond) == UnselectableFlags.None);

        /// <summary>Tell the core's topology what this server turned out to be.</summary>
        /// <remarks>
        /// The core learns roles from its own handshakes and dials lazily, so an endpoint it has not needed
        /// has no role - and a <c>DemandReplica</c> read then has no replica to choose. See
        /// <c>RespConnectionManager.OnRole</c>, which says the rest.
        /// </remarks>
        private void PublishRole()
            => Multiplexer?.ConnectionsIfCreated?.OnRole(EndPoint, isReplica);

        public override string ToString() => Format.ToString(EndPoint);

        /// <summary>Whether this endpoint is believed to already hold <paramref name="script"/>.</summary>
        /// <remarks>
        /// The belief is <b>soft</b>, and safely so in both directions: believing wrongly that it is held
        /// costs a <c>NOSCRIPT</c> and a retry, and believing wrongly that it is not costs a redundant
        /// <c>SCRIPT LOAD</c>, which is idempotent. So it needs no careful invalidation beyond the
        /// <c>RunId</c> check and <see cref="FlushScriptCache"/> that already exist.
        /// <para>
        /// Unlike <see cref="GetScriptHash"/> this has no side effect: that one adopts a hash it was handed
        /// as an <c>EVALSHA</c> argument, which is a write, and is the wrong question to ask when all you
        /// want to know is whether a preamble can be skipped.
        /// </para>
        /// </remarks>
        internal bool IsScriptLoaded(string script) => knownScripts[script] is not null;

        internal void AddScript(string script, byte[] hash)
        {
            lock (knownScripts)
            {
                knownScripts[script] = hash;
            }
        }

        /// <summary>
        /// Whether <c>HELLO</c> has told us our replication role (and server mode) on the current connection.
        /// </summary>
        /// <remarks>Reset at the start of each handshake, and set when the <c>HELLO</c> reply is processed.</remarks>
        internal bool RoleKnownFromHello { get; set; }

        private int _nextReplicaOffset;

        /// <summary>
        /// Used to round-robin between multiple replicas.
        /// </summary>
        internal uint NextReplicaOffset()
            => (uint)Interlocked.Increment(ref _nextReplicaOffset);

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
            // filled from the core, which is where the queues, sockets and op counts are - see
            // RespConnectionManager.BacklogCount
            if (Multiplexer.ConnectionsIfCreated is { } core)
            {
                core.AddCounters(EndPoint, ConnectionType.Interactive, counters.Interactive);
                core.AddCounters(EndPoint, ConnectionType.Subscription, counters.Subscription);
            }

            return counters;
        }

        internal BridgeStatus GetBridgeStatus(ConnectionType connectionType)
            => Multiplexer.ConnectionsIfCreated?.ConnectionStatus(EndPoint, connectionType) ?? BridgeStatus.Zero;

        internal string GetProfile()
        {
            var sb = new StringBuilder(Format.ToString(EndPoint)).Append(": ");
            sb.Append("Circular op-count snapshot; int:");
            var core = Multiplexer.ConnectionsIfCreated;
            if (core is null) sb.Append(" n/a");
            else core.AppendProfile(EndPoint, ConnectionType.Interactive, sb);
            sb.Append("; sub:");
            if (core is null) sb.Append(" n/a");
            else core.AppendProfile(EndPoint, ConnectionType.Subscription, sb);
            return sb.ToString();
        }

        internal byte[]? GetScriptHash(string script, RedisCommand command)
        {
            var found = (byte[]?)knownScripts[script];
            if (found == null && (command == RedisCommand.EVALSHA || command == RedisCommand.EVALSHA_RO))
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

        /// <summary>
        /// The key for the <c>EXISTS</c> tracer, memoized because this runs on the heartbeat path.
        /// </summary>
        /// <remarks>
        /// Cached as one object rather than as separate slot and key fields: a <see cref="RedisKey"/> is two
        /// references, so writing one while a heartbeat on another thread reads it can hand that reader a
        /// prefix from the new key and a value from the old. Publishing a whole new instance makes the update
        /// a single reference write, which cannot tear. Two threads racing here both build the same key, so
        /// the duplicated work is harmless and needs no lock.
        /// </remarks>
        private sealed class TracerKeyCache
        {
            public TracerKeyCache(int? slot, RedisKey key)
            {
                Slot = slot;
                Key = key;
            }

            public int? Slot { get; }
            public RedisKey Key { get; }
        }

        internal RedisKey GetTracerKey()
        {
            var slot = GetServableSlot();
            var cache = tracerKeyCache; // one read: everything below works off this snapshot
            if (cache is null || cache.Slot != slot)
            {
                RedisKey key = slot is int value
                    ? ServerSelectionStrategy.CreateKeyForSlot(value, Multiplexer.UniqueId)
                    : Multiplexer.UniqueId;
                cache = new TracerKeyCache(slot, key);
                tracerKeyCache = cache;
            }
            return cache.Key;
        }

        internal UnselectableFlags GetUnselectableFlags() => unselectableReasons;

        internal bool IsSelectable(RedisCommand command, bool allowDisconnected = false)
        {
            // Until we've connected at least once, we're going to have a DidNotRespond unselectable reason present
            var usable = unselectableReasons == 0 || (allowDisconnected && unselectableReasons == UnselectableFlags.DidNotRespond);

            return usable
                && (allowDisconnected || Multiplexer.ConnectionsIfCreated?.IsConnected(EndPoint) == true);
        }

        internal void OnConnected(string source)
        {
            CompletePendingConnectionMonitors(source);
            Multiplexer.OnConnectionRestored(EndPoint, ConnectionType.Interactive, source);
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

        /// <summary>
        /// How many consecutive failures to connect justify re-reading the topology.
        /// </summary>
        /// <remarks>
        /// Three: enough to rule out a single transient refusal, few enough that recovery is seconds. The
        /// number matters less than that there *is* one - the failure this addresses lasted 37 hours.
        /// </remarks>
        private const int ConnectFailuresBeforeRefresh = 3;

        private int _lastConnectFailureRefreshTicks;

        /// <summary>
        /// Called when a connection attempt to this endpoint has failed, with the consecutive failure count.
        /// </summary>
        /// <remarks>
        /// The gap this closes: every existing path that re-reads the topology needs somebody *else* to notice
        /// first - a notification, a <c>MOVED</c> from a reachable node, a peer's config broadcast. A client
        /// with quiet healthy connections and one endpoint that only ever refuses has nobody to tell it, so it
        /// dials the dead address indefinitely. That is not hypothetical: a customer's client did exactly that
        /// for 37 hours across a Redis Cloud node replacement.
        /// <para>
        /// Rate-limited to <see cref="ConfigurationOptions.ConfigCheckSeconds"/>, deliberately reusing the
        /// knob that already means "how often may we re-read configuration" rather than inventing one. The
        /// limit is the part that makes this safe: the existing gate exists to stop a stampede - a dead
        /// endpoint, times a retry loop, times every client in a fleet, each issuing <c>CLUSTER NODES</c> - and
        /// removing the gate without replacing the restraint would trade a stuck client for a thundering herd.
        /// </para>
        /// <para>
        /// It repeats rather than firing once, because one refresh is not guaranteed to help: the topology may
        /// not have been updated server-side yet. A permanently dead endpoint therefore prompts a re-read at
        /// most once per interval until something changes, which is what makes recovery eventual rather than
        /// lucky.
        /// </para>
        /// </remarks>
        internal void OnRepeatedConnectFailure(int consecutiveFailures)
        {
            if (consecutiveFailures < ConnectFailuresBeforeRefresh || isDisposed) return;

            // nothing to learn about an endpoint we have already decided to let go of
            if ((unselectableReasons & UnselectableFlags.Retiring) != 0) return;

            var interval = Math.Max(Multiplexer.RawConfig.ConfigCheckSeconds, 5) * 1000;
            var now = Environment.TickCount;
            var last = Volatile.Read(ref _lastConnectFailureRefreshTicks);
            if (last != 0 && unchecked(now - last) < interval) return;

            if (Interlocked.CompareExchange(ref _lastConnectFailureRefreshTicks, NudgeFromZeroTicks(now), last) != last) return;

            Multiplexer.Logger?.LogInformationRefreshingAfterConnectFailures(new(this), consecutiveFailures);
            Multiplexer.ReconfigureIfNeeded(EndPoint, fromBroadcast: false, $"{consecutiveFailures} consecutive connect failures");
        }

        /// <summary>Zero means "never", so a tick count that lands on it moves by one.</summary>
        private static int NudgeFromZeroTicks(int ticks) => ticks == 0 ? 1 : ticks;

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

        internal Task<bool> SendTracerAsync(ILogger? log = null)
        {
            // On the connection that carries this endpoint's commands. The tracer is how availability is
            // PROVED - `ReconfigureAsync` sends it down the "already connected, show me" path of
            // `OnConnectedAsync`. v3 wrote it to the bridge via `WriteDirectAsync`; while both cores
            // coexisted that proved the wrong connection, and once the bridge stopped dialling the tracer was
            // written to a connection that would never carry it, never completed, and the endpoint ran out
            // the whole connect timeout (`ConnectFailTimeoutTests.NoticesConnectFail`). See design notes 9n.
            // Nothing to prove on a connection the core does not hold: not available, rather than a guess
            return TryTraceAsync() ?? Task.FromResult(false);
        }

        /// <summary>Prove this endpoint answers, on the core's connection.</summary>
        /// <returns>The pending proof, or null when the core has no connection to this endpoint.</returns>
        /// <remarks>
        /// Declines unless the core actually HAS this endpoint connected: a tracer is a question about a
        /// connection, and sending it on one that has not been dialled would turn the question into a dial.
        /// </remarks>
        private Task<bool>? TryTraceAsync()
        {
            if (Multiplexer.ConnectionsIfCreated is not { } core) return null;
            if (!core.IsConnected(EndPoint)) return null;

            return Traced(core.ServerContext(EndPoint).PingAsync(CommandFlags.NoRedirect));
        }

        private static async Task<bool> Traced(ValueTask pending)
        {
            try
            {
                await pending.ForAwait();
                return true;
            }
            catch (Exception ex)
            {
                // the v3 tracer answered false rather than throwing, and callers branch on that
                Debug.WriteLine(ex.Message);
                return false;
            }
        }

        internal string Summary()
        {
            var sb = new StringBuilder(Format.ToString(EndPoint))
                .Append(": ").Append(serverType).Append(" v").Append(version).Append(", ").Append(isReplica ? "replica" : "primary");

            if (databases > 0) sb.Append("; ").Append(databases).Append(" databases");
            if (writeEverySeconds > 0)
                sb.Append("; keep-alive: ").Append(TimeSpan.FromSeconds(writeEverySeconds));
            sb.Append("; int: ").Append(InteractiveConnectionState);
            sb.Append("; sub: ").Append(SubscriptionConnectionState);

            var flags = unselectableReasons;
            if (flags != 0)
            {
                sb.Append("; not in use: ").Append(flags);
            }
            return sb.ToString();
        }

        private void SetConfig<T>(ref T field, T value, [CallerMemberName] string? caller = null)
        {
            if (!EqualityComparer<T>.Default.Equals(field, value))
            {
                // multiplexer might be null here in some test scenarios; just roll with it...
                Multiplexer?.Trace(caller + " changed from " + field + " to " + value, "Configuration");
                field = value;
                ClearMemoized();
                Multiplexer?.ReconfigureIfNeeded(EndPoint, false, caller!);
            }
        }
        internal static string ClientInfoSanitize(string? value)
            => string.IsNullOrWhiteSpace(value) ? "" : nameSanitizer.Replace(value!.Trim(), "-");

        /// <summary>A client name the server will accept, or empty if there is nothing to set.</summary>
        /// <param name="value">The configured name.</param>
        /// <remarks>
        /// <b>Characters are REMOVED rather than replaced</b>, which is the difference from
        /// <see cref="ClientInfoSanitize"/> and is not arbitrary: <c>CLIENT SETNAME</c> rejects a name with a
        /// space outright, so "Test Rig" has to become "TestRig" and not "Test-Rig" - <c>ConfigTests.ClientName</c>
        /// reads the result. Shared so that a second handshake cannot get it subtly different; the new core's
        /// did, sent the name with its space in, had it refused, and left the connection nameless.
        /// </remarks>
        internal static string SanitizeClientName(string? value)
            => string.IsNullOrWhiteSpace(value) ? "" : nameSanitizer.Replace(value!, "");

        private void ClearMemoized()
        {
            supportsDatabases = null;
            supportsPrimaryWrites = null;
        }

        internal bool CanSimulateConnectionFailure => Multiplexer.RawConfig.AllowAdmin && IsConnected;

        internal void SimulateConnectionFailure(SimulatedFailureType failureType)
        {
            // admin-only, as it always was: it breaks real connections
            if (!Multiplexer.RawConfig.AllowAdmin)
            {
                throw ExceptionFactory.AdminModeNotEnabled(Multiplexer.RawConfig.IncludeDetailInExceptions, RedisCommand.DEBUG, null, this); // close enough
            }

            Multiplexer.ConnectionsIfCreated?.SimulateConnectionFailure(EndPoint, failureType);
        }

        public void SetLatency(DateTime startTime)
        {
            try
            {
                LatencyTicks = ConnectionGroupMember.ToLatencyTicks(DateTime.UtcNow - startTime);
            }
            catch (Exception ex)
            {
                Debug.WriteLine(ex.Message);
            }
        }

        internal uint LatencyTicks { get; private set; } = uint.MaxValue;

        private ProductVariant _productVariant = ProductVariant.Redis;
        private string _productVersion = "";

        internal void SetProductVariant(ProductVariant variant, string productVersion)
        {
            _productVariant = variant;
            _productVersion = productVersion;
            ClearMemoized(); // variant impacts multi-DB rules for cluster
        }

        internal ProductVariant GetProductVariant(out string productVersion)
        {
            productVersion = _productVersion;
            return _productVariant;
        }
    }
}
