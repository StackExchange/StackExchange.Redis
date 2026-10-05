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
using static StackExchange.Redis.PhysicalBridge;

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
        private PhysicalBridge? interactive, subscription;
        private bool isDisposed, replicaReadOnly, isReplica, allowReplicaWrites;
        private bool? supportsDatabases, supportsPrimaryWrites;
        private ServerType serverType;
        private TracerKeyCache? tracerKeyCache;
        private volatile UnselectableFlags unselectableReasons;
        private Version version;

        internal void ResetNonConnected()
        {
            interactive?.ResetNonConnected();
            subscription?.ResetNonConnected();
        }

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

        public bool IsConnecting => interactive?.IsConnecting == true;
        public bool IsConnected => interactive?.IsConnected == true
            || (ConnectionMultiplexer.NewCoreEngine && Multiplexer.NewCoreIfCreated?.IsConnected(EndPoint) == true);
        // ...and where there is no second bridge, SupportsSubscriptions is the term that would otherwise
        // go missing: a bridge for a disabled SUBSCRIBE never connects, so the shipped answer is no by
        // construction, where sharing one connection has to say no on purpose.
        public bool IsSubscriberConnected => UsesSubscriptionBridge
            ? subscription?.IsConnected == true
            : IsConnected && (KnowOrAssumeResp3() || SupportsSubscriptions);

        /// <summary>Whether a subscription here belongs on a SECOND bridge of this endpoint's own.</summary>
        /// <remarks>
        /// <para>
        /// Under RESP3 it does not, because one connection does everything. <b>And under the engine flag
        /// it does not either, whatever the protocol, because the subscription leg is the other core's</b>
        /// - it dials and owns its own subscription socket, and every subscription this client places
        /// goes there. A bridge for them would be a socket nothing writes to.
        /// </para>
        /// <para>
        /// This is what lets the bridge stop being CONSTRUCTED rather than merely stop being used, and
        /// the two cannot be separated: <see cref="IsSelectable"/> creates the bridge it asks about and
        /// then requires it to be connected, so a subscription bridge that exists and is never activated
        /// makes every subscription command unselectable - no server is chosen, and the subscribe
        /// silently does nothing. Measured as 70 failures, nearly all of them a publish reporting no
        /// subscribers.
        /// </para>
        /// </remarks>
        private bool UsesSubscriptionBridge => !KnowOrAssumeResp3() && !ConnectionMultiplexer.NewCoreEngine;

        public bool KnowOrAssumeResp3()
        {
            var protocol = interactive?.Protocol;
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
                if (autoConfigureIfConnected)
                {
                    await AutoConfigureAsync(null, log).ForAwait();
                }
                if (sendTracerIfConnected)
                {
                    await SendTracerAsync(log).ForAwait();
                }
                log?.LogInformationOnConnectedAsyncAlreadyConnectedEnd(new(this));
                return "Already connected";
            }

            if (!IsConnected)
            {
                log?.LogInformationOnConnectedAsyncInit(new(this), interactive?.ConnectionState);
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
        {
            get
            {
                var snapshot = interactive;
                var subEx = subscription?.LastException;
                var subExData = subEx?.Data;

                // check if subscription endpoint has a better last exception
                if (subExData != null && subExData.Contains("Redis-FailureType") && subExData["Redis-FailureType"]?.ToString() != nameof(ConnectionFailureType.UnableToConnect))
                {
                    return subEx;
                }

                // ...and the other core's, when the shipped bridges have nothing to say - which under the
                // engine flag is always, because they are not dialled. This is what ExceptionFactory
                // collects to explain WHY no connection was available, so an empty answer here is an
                // "unable to resolve physical connection" with no inner exception and nothing to act on.
                // ConnectionFailureErrorsTests.SocketFailureError asserts precisely that the inner is
                // there. Same spanning treatment as IsConnected, OperationCount and the connection state.
                return snapshot?.LastException
                    ?? Multiplexer.NewCoreIfCreated?.LastConnectFault(EndPoint, ConnectionType.Interactive)
                    ?? Multiplexer.NewCoreIfCreated?.LastConnectFault(EndPoint, ConnectionType.Subscription);
            }
        }

        /// <summary>What state this endpoint's ordinary connection is in, whichever core holds it.</summary>
        /// <remarks>
        /// <b>Spans both cores for the same reason <see cref="OperationCount"/> and <see cref="IsConnected"/>
        /// do.</b> This is a question about the endpoint, not about a bridge: once the other core carries
        /// this endpoint's commands, a property that consults only the shipped bridge reports
        /// <c>Disconnected</c> about a server the client is actively talking to.
        /// <para>
        /// <c>ConnectedEstablished</c> rather than a finer answer because that is all this core
        /// distinguishes: it has a usable connection or it does not, with no separate "established" step to
        /// be between. The shipped bridge's own state still wins wherever it has one, so nothing that
        /// watches a bridge come up loses the intermediate states it was watching for.
        /// </para>
        /// </remarks>
        internal State InteractiveConnectionState
        {
            get
            {
                var shipped = interactive?.ConnectionState ?? State.Disconnected;
                return shipped == State.Disconnected
                    && Multiplexer.NewCoreIfCreated?.IsInteractiveConnected(EndPoint) == true
                        ? State.ConnectedEstablished
                        : shipped;
            }
        }

        /// <summary>As above, for whatever carries this endpoint's deliveries.</summary>
        /// <remarks>
        /// Three cases rather than two, because "which connection is this?" has three answers: a shipped
        /// subscription bridge where one exists, this core's dedicated subscription socket where it has
        /// opened one, and otherwise the interactive connection - which is where RESP3 deliveries arrive,
        /// and where <see cref="SupportsSubscriptions"/> being false leaves nothing at all.
        /// </remarks>
        internal State SubscriptionConnectionState
        {
            get
            {
                if (UsesSubscriptionBridge)
                {
                    var shipped = subscription?.ConnectionState ?? State.Disconnected;
                    return shipped == State.Disconnected
                        && Multiplexer.NewCoreIfCreated?.IsSubscriptionConnected(EndPoint) == true
                            ? State.ConnectedEstablished
                            : shipped;
                }

                return Multiplexer.NewCoreIfCreated?.IsSubscriptionConnected(EndPoint) == true
                    ? State.ConnectedEstablished
                    : InteractiveConnectionState;
            }
        }

        public long OperationCount => (interactive?.OperationCount ?? 0) + (subscription?.OperationCount ?? 0)
            + (Multiplexer.NewCoreIfCreated?.OperationCount(EndPoint) ?? 0);

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

                // ...and tell the other core, which learns roles only from its own handshakes and dials
                // lazily: without this the replica of an ordinary standalone pair is never dialled, so its
                // role is never learned, so a DemandReplica read has no replica to choose and goes to the
                // primary. See RespNewCore.OnRole.
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

        /// <summary>
        /// If we have a connection (interactive), report the protocol being used.
        /// </summary>
        /// <summary>What this server is being spoken to in, as far as any connection to it has settled.</summary>
        /// <remarks>
        /// <b>Either core's connection answers.</b> The shipped bridge is asked first because its answer is
        /// the one every existing caller has been reading; under the engine flag it may never have
        /// handshaken at all, and then the question is about a connection this core owns. Reporting
        /// <c>null</c> for a server that has been answering RESP3 since the first command is a diagnostic
        /// that is wrong exactly when somebody is trying to find out what the protocol is.
        /// </remarks>
        public RedisProtocol? Protocol
            => interactive?.Protocol ?? Multiplexer?.NewCoreIfCreated?.ObservedProtocol(EndPoint);

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
        /// implementable as stated and turns out to be unnecessary: the only usage counter available
        /// (<c>PhysicalBridge.IncrementOpCount</c>) is incremented by our own heartbeat pings as well as by
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
        /// Subscriptions are asked of the registry <i>as well as</i> of the bridges. The bridge counters are
        /// the record of what those bridges subscribed, so they are silent about a subscription carried any
        /// other way - and a server whose only work is one of those would look idle and be pruned while
        /// still delivering. The registry entry is the fact that does not depend on who carried it.
        /// </remarks>
        internal bool IsIdle()
            => !Multiplexer.ServerSelectionStrategy.OwnsAnySlot(this)
            && (subscription?.SubscriptionCount ?? 0) == 0
            && (interactive?.SubscriptionCount ?? 0) == 0
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
            => interactive?.HasCallerWork() == true
            || subscription?.HasCallerWork() == true
            || Multiplexer.NewCoreIfCreated?.HasCallerWork(EndPoint) == true;

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
            isDisposed = true;
            var tmp = interactive;
            interactive = null;
            tmp?.Dispose();

            tmp = subscription;
            subscription = null;
            tmp?.Dispose();
        }

        public PhysicalBridge? GetBridge(ConnectionType type, bool create = true, ILogger? log = null)
        {
            if (isDisposed) return null;
            switch (type)
            {
                case ConnectionType.Interactive:
                case ConnectionType.Subscription when !UsesSubscriptionBridge:
                    return interactive ?? (create ? interactive = CreateBridge(ConnectionType.Interactive, log) : null);
                case ConnectionType.Subscription:
                    return subscription ?? (create ? subscription = CreateBridge(ConnectionType.Subscription, log) : null);
                default:
                    return null;
            }
        }

        public PhysicalBridge? GetBridge(Message message)
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
                case RedisCommand.SSUBSCRIBE:
                case RedisCommand.SUNSUBSCRIBE:
                    message.SetForSubscriptionBridge();
                    break;
            }

            return (message.IsForSubscriptionBridge && UsesSubscriptionBridge)
                ? subscription ??= CreateBridge(ConnectionType.Subscription, null)
                : interactive ??= CreateBridge(ConnectionType.Interactive, null);
        }

        /// <summary>
        /// Moves a subscription message that was queued against the interactive bridge over to the subscription
        /// bridge, which is where it belongs now that we know the connection is RESP2 (see #3154).
        /// </summary>
        /// <returns><c>true</c> if the message is now the subscription bridge's responsibility.</returns>
        internal bool TryRerouteToSubscriptionBridge(Message message, PhysicalBridge from)
        {
            if (isDisposed) return false;

            // not under the engine flag: there is no second bridge to move it to, and the connection
            // this would have moved it off is not one this core writes subscriptions to anyway. The
            // equivalent move for that core is RespEndpointExecutor.RerouteSubscription.
            if (ConnectionMultiplexer.NewCoreEngine) return false;

            // deliberately not via GetBridge: that consults the same expectation that got us here
            var target = subscription ??= CreateBridge(ConnectionType.Subscription, null);
            if (target is null || ReferenceEquals(target, from)) return false;

            target.AcceptRerouted(message);
            return true;
        }

        public PhysicalBridge? GetBridge(RedisCommand command, bool create = true)
        {
            if (isDisposed) return null;
            switch (command)
            {
                case RedisCommand.SUBSCRIBE:
                case RedisCommand.UNSUBSCRIBE:
                case RedisCommand.PSUBSCRIBE:
                case RedisCommand.PUNSUBSCRIBE:
                case RedisCommand.SSUBSCRIBE:
                case RedisCommand.SUNSUBSCRIBE:
                    if (UsesSubscriptionBridge)
                    {
                        return subscription ?? (create ? subscription = CreateBridge(ConnectionType.Subscription, null) : null);
                    }
                    break;
            }
            return interactive ?? (create ? interactive = CreateBridge(ConnectionType.Interactive, null) : null);
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

        /// <summary>Tell the other core what this one has just decided about this server.</summary>
        /// <remarks>
        /// <b>A decision, not an observation</b>: whether a server is retiring or redundant is something the
        /// client concludes during reconfiguration, so there is nothing for the other core to discover on a
        /// connection - it has to be told. Pushed only when the answer CHANGES, which is rare.
        /// <para>
        /// <b><c>DidNotRespond</c> is excluded, and that reversed an earlier decision.</b> It was left in on
        /// the reasoning that agreeing about a server which did not respond costs nothing - but it is set
        /// until THIS core's bridge has connected at least once, and under the engine flag this core's
        /// bridges largely do not connect at all, because the other one carries the commands. So every
        /// endpoint it had not dialled was published as unselectable, permanently: a replica that the other
        /// core would happily have used was barred from ever being chosen, and a <c>DemandReplica</c> read
        /// went to the primary instead. Connectivity is the one thing that core does track for itself, and
        /// it distinguishes "nobody has dialled this yet" from "this is down" - which this flag cannot.
        /// </para>
        /// </remarks>
        private void PublishSelectable()
            => Multiplexer.NewCoreIfCreated?.OnSelectable(
                EndPoint,
                (unselectableReasons & ~UnselectableFlags.DidNotRespond) == UnselectableFlags.None);

        /// <summary>Tell the other core what this server turned out to be.</summary>
        /// <remarks>
        /// This core learns roles from its own handshakes and dials lazily, so an endpoint it has not needed
        /// has no role - and a <c>DemandReplica</c> read then has no replica to choose. See
        /// <c>RespNewCore.OnRole</c>, which says the rest.
        /// </remarks>
        private void PublishRole()
            => Multiplexer?.NewCoreIfCreated?.OnRole(EndPoint, isReplica);

        public override string ToString() => Format.ToString(EndPoint);

        [Obsolete("prefer async")]
        public WriteResult TryWriteSync(Message message) => GetBridge(message)?.TryWriteSync(message, isReplica) ?? WriteResult.NoConnectionAvailable;

        public ValueTask<WriteResult> TryWriteAsync(Message message) => GetBridge(message)?.TryWriteAsync(message, isReplica) ?? new ValueTask<WriteResult>(WriteResult.NoConnectionAvailable);

        internal void Activate(ConnectionType type, ILogger? log) => GetBridge(type, true, log);

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

        /// <summary>
        /// Issues the topology/configuration discovery commands for this server.
        /// </summary>
        /// <param name="connection">The connection to write to; <c>null</c> for an already-established connection.</param>
        /// <param name="log">The log to write handshake details to.</param>
        /// <param name="extraFlags">Additional flags to apply to the messages issued.</param>
        /// <param name="helloPending">
        /// Whether a <c>HELLO</c> has been written to this same batch, but not yet answered; in that case
        /// we expect to learn our role from it, so we can skip the key-based fallback probe.
        /// </param>
        internal async Task AutoConfigureAsync(PhysicalConnection? connection, ILogger? log = null, CommandFlags extraFlags = CommandFlags.None, bool helloPending = false)
        {
            // Not on the new core, where every one of these facts already has an owner: the connection's
            // handshake discovers the server (RespHandshake.DiscoverServerConfigAsync), and a reconfiguration
            // re-reads slots and roles (RespNewCore.RefreshTopologyAsync) in the same pass that calls this.
            // Writing these messages anyway constructs a shipped bridge to carry them - which dials - so a
            // `reconfigureAll` pass was opening a second socket to every server it re-checked.
            if (ConnectionMultiplexer.NewCoreEngine) return;

            if (!serverType.SupportsAutoConfigure())
            {
                // Don't try to detect configuration.
                // All the config commands are disabled and the fallback primary/replica detection won't help
                return;
            }

            log?.LogInformationAutoConfiguring(new(this));

            var commandMap = Multiplexer.CommandMap;
            var flags = CommandFlags.FireAndForget | CommandFlags.NoRedirect | extraFlags;
            var features = GetFeatures();
            Message msg;

            var autoConfigProcessor = ResultProcessor.AutoConfigureProcessor.Create(log);

            if (commandMap.IsAvailable(RedisCommand.CONFIG))
            {
                if (Multiplexer.RawConfig.KeepAlive <= 0)
                {
                    msg = Message.Create(-1, flags | Message.NoFlushFlag, RedisCommand.CONFIG, RedisLiterals.GET, RedisLiterals.timeout);
                    msg.SetInternalCall();
                    await WriteDirectOrQueueFireAndForgetAsync(connection, msg, autoConfigProcessor).ForAwait();
                }
                msg = Message.Create(-1, flags | Message.NoFlushFlag, RedisCommand.CONFIG, RedisLiterals.GET, features.ReplicaCommands ? RedisLiterals.replica_read_only : RedisLiterals.slave_read_only);
                msg.SetInternalCall();
                await WriteDirectOrQueueFireAndForgetAsync(connection, msg, autoConfigProcessor).ForAwait();
                msg = Message.Create(-1, flags, RedisCommand.CONFIG, RedisLiterals.GET, RedisLiterals.databases);
                msg.SetInternalCall();
                await WriteDirectOrQueueFireAndForgetAsync(connection, msg, autoConfigProcessor).ForAwait();
            }
            if (commandMap.IsAvailable(RedisCommand.SENTINEL))
            {
                // SENTINEL MASTERS only reads the sentinel's view, despite SENTINEL defaulting to server-admin
                msg = Message.Create(-1, flags.WithRetryCategory(CommandFlags.CommandRetryReadOnly | Message.CommandServerSpecific), RedisCommand.SENTINEL, RedisLiterals.MASTERS);
                msg.SetInternalCall();
                await WriteDirectOrQueueFireAndForgetAsync(connection, msg, autoConfigProcessor).ForAwait();
            }
            if (commandMap.IsAvailable(RedisCommand.INFO))
            {
                lastInfoReplicationCheckTicks = Environment.TickCount;
                if (features.InfoSections)
                {
                    // note: Redis 7.0 has a multi-section usage, but we don't know
                    // the server version at this point; we *could* use the optional
                    // value on the config, but let's keep things simple: these
                    // commands are suitably cheap
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
            // Cluster replicas return MOVED rather than READONLY for writes to their primary's slots, so no
            // hash tag can make this role probe reliable; skip it whenever cluster mode is already known.
            // On the first handshake, serverType is seeded as Standalone until the CLUSTER NODES reply is
            // processed, so neither this guard nor the tie-breaker GET guard below suppresses their initial probes.
            else if (commandMap.IsAvailable(RedisCommand.SET)
                && !(helloPending || RoleKnownFromHello)
                && ServerType != ServerType.Cluster)
            {
                // This is a nasty way to find if we are a replica, and it will only work on up-level servers, but...
                // (note we only get here when HELLO isn't going to tell us: the HELLO reply carries "role", and
                // unlike this probe it doesn't need a key - which matters when ACLs restrict key patterns; see #2968)
                RedisKey key = Multiplexer.UniqueId;
                // The actual value here doesn't matter (we detect the error code if it fails).
                // The value here is to at least give some indication to anyone watching via "monitor",
                // but we could send two GUIDs (key/value) and it would work the same.
                msg = Message.Create(0, flags, RedisCommand.SET, key, RedisLiterals.replica_read_only, RedisLiterals.PX, 1, RedisLiterals.NX);
                msg.SetInternalCall();
                await WriteDirectOrQueueFireAndForgetAsync(connection, msg, autoConfigProcessor).ForAwait();
            }
            if (commandMap.IsAvailable(RedisCommand.CLUSTER))
            {
                // SLOTS first, deliberately: replies arrive in request order, so this is processed before
                // the NODES reply below, and the identities it carries are therefore known before NODES
                // starts creating servers by address. Without that ordering, a node created from a redirect
                // under its announced hostname is duplicated under its address moments later by its own
                // autoconfigure. Costs nothing: the burst stays a single pipeline with no round-trip stall
                msg = RedisServer.GetClusterSlotsMessage(flags);
                msg.SetInternalCall();
                await WriteDirectOrQueueFireAndForgetAsync(connection, msg, ResultProcessor.ClusterSlots).ForAwait();

                msg = RedisServer.GetClusterNodesMessage(flags);
                msg.SetInternalCall();
                await WriteDirectOrQueueFireAndForgetAsync(connection, msg, ResultProcessor.ClusterNodes).ForAwait();
            }
            // If we are going to fetch a tie breaker, do so last and we'll get it in before the tracer fires completing the connection
            // But if GETs are disabled on this, do not fail the connection - we just don't get tiebreaker benefits
            if (ServerType != ServerType.Cluster
                && Multiplexer.RawConfig.TryGetTieBreaker(out var tieBreakerKey)
                && Multiplexer.CommandMap.IsAvailable(RedisCommand.GET))
            {
                log?.LogInformationRequestingTieBreak(new(EndPoint), tieBreakerKey);
                msg = Message.Create(0, flags, RedisCommand.GET, tieBreakerKey);
                msg.SetInternalCall();
                msg = LoggingMessage.Create(log, msg);
                await WriteDirectOrQueueFireAndForgetAsync(connection, msg, ResultProcessor.TieBreaker).ForAwait();
            }
        }

        private int _nextReplicaOffset;

        /// <summary>
        /// Used to round-robin between multiple replicas.
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

        /// <summary>
        /// Ask the server to announce changes to the keys this connection cares about, if a cache wants them.
        /// </summary>
        /// <remarks>
        /// <para>
        /// <b>Interactive only, and RESP3 only.</b> Invalidations arrive as out-of-band pushes on the
        /// connection that asked for them, so they must land where the reads are - and in RESP2 a push has
        /// nowhere to go without <c>REDIRECT</c> to a subscriber connection, which this does not model.
        /// </para>
        /// <para>
        /// <b>It refuses loudly rather than degrading.</b> A cache that is filled but never invalidated is
        /// worse than no cache: it is silently, durably wrong, bounded only by the entry lifetime. So if
        /// tracking cannot be established the connection fails rather than quietly serving stale data - the
        /// one case where taking the connection down is the kinder outcome.
        /// </para>
        /// </remarks>
        private async Task EnableClientTrackingAsync(PhysicalConnection connection, ILogger? log, bool negotiateResp3)
        {
            var cache = Multiplexer.ClientCache;
            if (cache is null) return; // no cache, nothing to keep honest

            if (connection.BridgeCouldBeNull?.ConnectionType != ConnectionType.Interactive) return;

            // note we test the *intent*, not connection.Protocol: HELLO is written no-flush/fire-and-forget,
            // so its reply has not been seen yet and the protocol is still unknown here. If the server then
            // declines RESP3 anyway, it also declines CLIENT TRACKING, and DemandOK fails the connection -
            // which is the same loud outcome by a different route.
            if (!negotiateResp3)
            {
                const string Message =
                    "Client-side caching requires RESP3: invalidation arrives as an out-of-band push, which"
                    + " RESP2 cannot deliver on this connection. Set Protocol = RedisProtocol.Resp3, or clear"
                    + " ConfigurationOptions.ClientCache.";
                throw new RedisConnectionException(ConnectionFailureType.ProtocolFailure, CommandFlags.CommandRetryNever, Message);
            }

            // The REFUSAL above still belongs here, even when the other core owns the reads: it is a
            // statement about the configuration, and it has to be made while somebody is still connecting,
            // where the caller sees it. The REGISTRATION does not. Tracking has to be asked for by the
            // connection that does the reading - per-key mode registers what that connection read - so under
            // the engine flag this socket has nothing to be told about, and asking anyway buys a second
            // tracking client per endpoint: duplicate pushes in broadcast mode, and registrations against a
            // connection that reads nothing in per-key mode. RespHandshake asks on the one that does.
            if (ConnectionMultiplexer.NewCoreEngine) return;

            var options = cache.Options;
            var broadcast = options.ResolvedTrackingMode == CacheTrackingMode.Broadcast;
            var prefixes = options.Prefixes;

            // CLIENT TRACKING ON [BCAST] [PREFIX p]...
            var args = new RedisValue[2 + (broadcast ? 1 : 0) + (prefixes.Count * 2)];
            var index = 0;
            args[index++] = RedisLiterals.TRACKING;
            args[index++] = RedisLiterals.ON;
            if (broadcast) args[index++] = RedisLiterals.BCAST;
            foreach (var prefix in prefixes)
            {
                args[index++] = RedisLiterals.PREFIX;

                // a write, so the string really is the payload: AsRedisValue per docs/exp/StringToRedisValue.
                // The implicit conversion is [Experimental] in DEBUG only, so Release never sees this - which
                // is exactly how it was missed.
                args[index++] = prefix.AsRedisValue();
            }

            var tracking = Message.Create(-1, CommandFlags.FireAndForget | Message.NoFlushFlag, RedisCommand.CLIENT, args);
            tracking.SetInternalCall();
            await WriteDirectOrQueueFireAndForgetAsync(connection, tracking, ResultProcessor.DemandOK).ForAwait();
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
            interactive?.GetCounters(counters.Interactive);
            subscription?.GetCounters(counters.Subscription);

            // and whatever the other core is doing, which under the engine flag is where the commands
            // actually are - see RespNewCore.BacklogCount. Added rather than substituted: both cores'
            // queues, sockets and op counts are real for as long as both cores exist.
            if (Multiplexer.NewCoreIfCreated is { } core)
            {
                core.AddCounters(EndPoint, ConnectionType.Interactive, counters.Interactive);
                core.AddCounters(EndPoint, ConnectionType.Subscription, counters.Subscription);
            }

            return counters;
        }

        internal BridgeStatus GetBridgeStatus(ConnectionType connectionType)
        {
            try
            {
                var bridge = GetBridge(connectionType, false)?.GetStatus();

                // the OTHER core's connection, when it owns one: under the engine flag the commands are on
                // its socket and the shipped bridge - which is what every diagnostic surface reads - is
                // idle and reports zeroes. A timeout that says "nothing outstanding, nothing read" about a
                // connection with work on it is the diagnostic failing exactly when it is needed
                var other = Multiplexer.NewCoreIfCreated?.ConnectionStatus(EndPoint, connectionType);
                if (other is not { } mine) return bridge ?? BridgeStatus.Zero;
                if (bridge is not { } theirs) return mine;

                return theirs with
                {
                    IsWriterActive = theirs.IsWriterActive || mine.IsWriterActive,
                    BacklogMessagesPending = theirs.BacklogMessagesPending + mine.BacklogMessagesPending,
                    BacklogMessagesPendingCounter = theirs.BacklogMessagesPendingCounter + mine.BacklogMessagesPendingCounter,
                    BacklogStatus = mine.BacklogStatus == BacklogStatus.Inactive ? theirs.BacklogStatus : mine.BacklogStatus,
                    Connection = theirs.Connection with
                    {
                        MessagesSentAwaitingResponse =
                            theirs.Connection.MessagesSentAwaitingResponse + mine.Connection.MessagesSentAwaitingResponse,

                        // the newest fact wins rather than a sum: these describe "what did the last reply
                        // look like", which has one answer per connection and no meaningful total
                        BytesLastResult = mine.Connection.BytesLastResult != 0
                            ? mine.Connection.BytesLastResult
                            : theirs.Connection.BytesLastResult,
                        BytesInBuffer = theirs.Connection.BytesInBuffer + mine.Connection.BytesInBuffer,
                    },
                };
            }
            catch (Exception ex)
            {
                // only needs to be best efforts
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

        internal string? GetStormLog(Message message) => GetBridge(message)?.GetStormLog();

        internal Message GetTracerMessage(bool checkResponse)
        {
            // Different configurations block certain commands, as can ad-hoc local configurations, so
            //   we'll do the best with what we have available.
            // Note: muxer-ctor asserts that one of ECHO, PING, TIME of GET is available
            // See also: TracerProcessor
            var map = Multiplexer.CommandMap;
            Message msg;
            const CommandFlags flags = CommandFlags.NoRedirect | CommandFlags.FireAndForget;
            if (checkResponse && map.IsAvailable(RedisCommand.ECHO))
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
            else if (!checkResponse && map.IsAvailable(RedisCommand.ECHO))
            {
                // We'll use echo as a PING substitute if it is all we have (in preference to EXISTS)
                msg = Message.Create(-1, flags, RedisCommand.ECHO, (RedisValue)Multiplexer.UniqueId);
            }
            else
            {
                map.AssertAvailable(RedisCommand.EXISTS);
                msg = Message.Create(0, flags, RedisCommand.EXISTS, GetTracerKey());
            }
            msg.SetInternalCall();
            return msg;
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

            if (ConnectionMultiplexer.NewCoreEngine)
            {
                // connectivity from the core that carries the commands, and deliberately
                // without creating a bridge to ask about - creating one is what made an unactivated bridge
                // permanently unselectable.
                return usable
                    && (allowDisconnected || Multiplexer.NewCoreIfCreated?.IsConnected(EndPoint) == true);
            }

            var bridge = usable ? GetBridge(command, true) : null;
            return bridge != null && (allowDisconnected || bridge.IsConnected);
        }

        internal void OnNewCoreConnected(string source)
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

        internal void OnDisconnected(PhysicalBridge bridge)
        {
            if (bridge == interactive)
            {
                CompletePendingConnectionMonitors("Disconnected");
                if (Protocol is RedisProtocol.Resp3)
                {
                    Multiplexer.UpdateSubscriptions();
                }
            }
            else if (bridge == subscription)
            {
                Multiplexer.UpdateSubscriptions();
            }
        }

        internal Task OnEstablishingAsync(PhysicalConnection connection, ILogger? log)
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

                if (!handshake.IsCompletedSuccessfully)
                {
                    return OnEstablishingAsyncAwaited(connection, handshake);
                }
            }
            catch (Exception ex)
            {
                connection.RecordConnectionFailed(ConnectionFailureType.InternalFailure, ex);
            }
            return Task.CompletedTask;
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

        /// <summary>
        /// Subscribes to the configuration-change broadcast on a RESP3 connection. With RESP3 there is no
        /// separate subscription connection, and so no subscription handshake - which is where RESP2 subscribes
        /// to it. Left at that, the channel would silently have no subscriber, and the manual
        /// <c>PUBLISH</c> that clients have long used to announce a topology change would reach nobody.
        /// </summary>
        /// <remarks>
        /// Done once the connection is known to be RESP3 rather than as part of the handshake: a connection
        /// that fell back to RESP2 must not be put into subscriber mode.
        /// </remarks>
        private void SubscribeToConfigurationChannel(PhysicalBridge bridge)
        {
            var channel = Multiplexer.ConfigurationChangedChannel;
            if (channel is null || !SupportsSubscriptions || !Multiplexer.CommandMap.IsAvailable(RedisCommand.SUBSCRIBE)) return;

            var msg = Message.Create(-1, CommandFlags.FireAndForget, RedisCommand.SUBSCRIBE, RedisChannel.Literal(channel));
            msg.SetSource(ResultProcessor.TrackSubscriptions, null);
#pragma warning disable CS0618 // Type or member is obsolete
            bridge.TryWriteSync(msg, isReplica);
#pragma warning restore CS0618
        }

        internal void OnFullyEstablished(PhysicalConnection connection, string source)
        {
            try
            {
                var bridge = connection?.BridgeCouldBeNull;
                if (bridge != null)
                {
                    // Clear the unselectable flag ASAP since we are open for business
                    ClearUnselectable(UnselectableFlags.DidNotRespond);

                    // whatever a handoff pointed us at, we are connected now: resume normal resolution
                    ClearHandoffTarget();

                    // is *this specific* connection using RESP3? (without reference to config preferences)
                    bool isResp3 = connection?.Protocol is >= RedisProtocol.Resp3;

                    if (connection is not null && bridge == interactive)
                    {
                        ReconcileMaintenanceNotifications(connection);
                    }
                    if (bridge == subscription || isResp3)
                    {
                        // Note: this MUST be fire and forget, because we might be in the middle of a Sync processing
                        // TracerProcessor which is executing this line inside a SetResultCore().
                        // Since we're issuing commands inside a SetResult path in a message, we'd create a deadlock by waiting.
                        Multiplexer.EnsureSubscriptions(CommandFlags.FireAndForget);
                        if (isResp3 && bridge == interactive)
                        {
                            SubscribeToConfigurationChannel(bridge);
                        }
                    }
                    else if (SupportsSubscriptions && Multiplexer.RawConfig.Protocol > RedisProtocol.Resp2)
                    {
                        // interactive, and we wanted RESP3+, but we didn't get it; spin up pub/sub
                        Activate(ConnectionType.Subscription, null);
                    }
                    if (IsConnected && (IsSubscriberConnected || !SupportsSubscriptions || isResp3))
                    {
                        // Only connect on the second leg - we can accomplish this by checking both
                        // Or the first leg, if we're only making 1 connection because subscriptions aren't supported
                        CompletePendingConnectionMonitors(source);
                    }

                    Multiplexer.OnConnectionRestored(EndPoint, bridge.ConnectionType, connection?.ToString());
                }
            }
            catch (Exception ex)
            {
                connection?.RecordConnectionFailed(ConnectionFailureType.InternalFailure, ex);
            }
        }

        internal int LastInfoReplicationCheckSecondsAgo =>
            unchecked(Environment.TickCount - Volatile.Read(ref lastInfoReplicationCheckTicks)) / 1000;

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

            if (version.IsAtLeast(RedisFeatures.v2_8_0) && Multiplexer.CommandMap.IsAvailable(RedisCommand.INFO)
                && GetBridge(ConnectionType.Interactive, false) is PhysicalBridge bridge)
            {
                var msg = Message.Create(-1, CommandFlags.FireAndForget | CommandFlags.NoRedirect, RedisCommand.INFO, RedisLiterals.replication);
                msg.SetInternalCall();
                msg.SetSource(ResultProcessor.AutoConfigure, null);
#pragma warning disable CS0618 // Type or member is obsolete
                bridge.TryWriteSync(msg, isReplica);
#pragma warning restore CS0618
                return true;
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

        internal Task<T?> WriteDirectAsync<T>(Message message, ResultProcessor<T> processor, PhysicalBridge? bridge = null)
        {
            static async Task<T?> Awaited(ServerEndPoint @this, Message message, ValueTask<WriteResult> write, TaskCompletionSource<T?> tcs)
            {
                var result = await write.ForAwait();
                if (result != WriteResult.Success)
                {
                    var ex = @this.Multiplexer.GetException(result, message, @this);
                    ConnectionMultiplexer.ThrowFailed(tcs, ex);
                }
                return await tcs.Task.ForAwait();
            }

            var source = TaskResultBox<T?>.Create(out var tcs, null);
            message.SetSource(processor, source);
            bridge ??= GetBridge(message);

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

        internal void ReportNextFailure()
        {
            interactive?.ReportNextFailure();
            subscription?.ReportNextFailure();
        }

        internal Task<bool> SendTracerAsync(ILogger? log = null)
        {
            // On the core that carries this endpoint's commands. The tracer is how availability is PROVED
            // - `ReconfigureAsync` sends it down the "already connected, show me" path of
            // `OnConnectedAsync` - and `WriteDirectAsync` puts it on the shipped bridge, so under the
            // engine flag it proves the wrong connection. Measured against the step that stops that bridge
            // dialling: with the tracer still on the bridge it is written to a connection that will never
            // carry it, never completes, and the endpoint runs out the whole connect timeout
            // (`ConnectFailTimeoutTests.NoticesConnectFail`). See design notes 9n.
            if (TryTraceViaNewCore() is { } traced) return traced;

            var msg = GetTracerMessage(false);
            msg = LoggingMessage.Create(log, msg);
            return WriteDirectAsync(msg, ResultProcessor.Tracer);
        }

        /// <summary>Prove this endpoint answers, on the other core's connection.</summary>
        /// <returns>The pending proof, or null when this core should send it itself.</returns>
        /// <remarks>
        /// Declines unless that core actually HAS this endpoint connected: a tracer is a question about a
        /// connection, and asking it of a core that has not dialled would answer "unreachable" about a
        /// server the shipped bridge may be talking to perfectly well.
        /// </remarks>
        private Task<bool>? TryTraceViaNewCore()
        {
            if (!ConnectionMultiplexer.NewCoreEngine) return null;
            if (Multiplexer.NewCoreIfCreated is not { } core) return null;
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
                // the shipped tracer answers false rather than throwing, and callers branch on that
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
        /// Write the message directly to the pipe or fail...will not queue.
        /// </summary>
        /// <typeparam name="T">The type of the result processor.</typeparam>
        internal ValueTask WriteDirectOrQueueFireAndForgetAsync<T>(PhysicalConnection? connection, Message message, ResultProcessor<T> processor)
        {
            static async ValueTask Awaited(ValueTask<WriteResult> l_result) => await l_result.ForAwait();

            if (message != null)
            {
                message.SetSource(processor, null);
                ValueTask<WriteResult> result;
                if (connection == null)
                {
                    Multiplexer.Trace($"{Format.ToString(this)}: Enqueue (async): " + message);
                    // A bridge will be created if missing, so not nullable here
                    result = GetBridge(message)!.TryWriteAsync(message, isReplica);
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
                        result = bridge.WriteMessageTakingWriteLockAsync(connection, message, bypassBacklog: true);
                    }
                }

                if (!result.IsCompletedSuccessfully)
                {
                    return Awaited(result);
                }
                // Must consume the ValueTask even on success path
                result.GetAwaiter().GetResult();
            }
            return default;
        }

        private PhysicalBridge? CreateBridge(ConnectionType type, ILogger? log)
        {
            if (Multiplexer.IsDisposed) return null;
            Multiplexer.Trace(type.ToString());
            var bridge = new PhysicalBridge(this, type, Multiplexer.TimeoutMilliseconds);
            bridge.TryConnect(log);
            return bridge;
        }

        /// <summary>
        /// Issues <c>HELLO</c>, optionally carrying the credentials and client name.
        /// </summary>
        /// <remarks>The server can reject RESP3 either with an error (<c>HELLO</c> not understood, or an
        /// unsupported protocol version) or by simply reporting RESP2, so we don't assign the protocol here:
        /// that happens when the reply is processed (and as a last resort, when the tracer completes).</remarks>
        private async Task WriteHelloAsync(PhysicalConnection connection, ILogger? log, int protocolVersion, string? user, string? password, string? clientName)
        {
            var hello = Message.CreateHello(protocolVersion, user, password, clientName, CommandFlags.FireAndForget | Message.NoFlushFlag);
            hello.SetInternalCall();
            await WriteDirectOrQueueFireAndForgetAsync(connection, hello, ResultProcessor.AutoConfigureProcessor.Create(log)).ForAwait();
        }

        private async Task HandshakeAsync(PhysicalConnection connection, ILogger? log)
        {
            log?.LogInformationServerHandshake(new(this));
            if (connection == null)
            {
                Multiplexer.Trace("No connection!?");
                return;
            }

            Message msg;
            var config = Multiplexer.RawConfig;
            var user = config.User;
            // Note that we need "" (not null) for password in the case of 'nopass' logins
            var password = config.Password ?? "";
            var clientName = Multiplexer.ClientName;

            clientName = SanitizeClientName(clientName);

            // NOTE:
            // we might send the auth and client-name *twice* in RESP3 mode; this is intentional:
            // - we don't know for sure which commands are available; HELLO is not always available,
            //   even on v6 servers, and we don't usually even know the server version yet; likewise,
            //   CLIENT could be disabled/renamed
            // - on an authenticated server, you MUST issue HELLO with AUTH, so we can't avoid it there
            // - but if the HELLO with AUTH isn't recognized, we might still need to auth; the following is
            //   legal in all scenarios, and results in a consistent state:
            //
            //   (auth enabled)
            //
            //   HELLO 3 AUTH {user} {password} SETNAME {client}
            //   AUTH {user} {password}
            //   CLIENT SETNAME {client}
            //
            //   (auth disabled)
            //
            //   HELLO 3 SETNAME {client}
            //   CLIENT SETNAME {client}
            //
            // this might look a little redundant, but: we only do it once per connection, and it isn't
            // many bytes different; this allows us to pipeline the entire handshake without having to
            // add latency

            // note on the use of FireAndForget here; in F+F, the result processor is still invoked, which
            // is what we need for things to work; what *doesn't* happen is the result-box activation etc;
            // that's fine and doesn't cause a problem; if we wanted we could probably just discard (`_ =`)
            // the various tasks and just `return connection.FlushAsync();` - however, since handshake is low
            // volume, we can afford to optimize for a good stack-trace rather than avoiding state machines.
            ResultProcessor<bool>? autoConfig = null;
            bool isInteractive = connection.BridgeCouldBeNull?.ConnectionType == ConnectionType.Interactive;
            if (isInteractive)
            {
                // forget what the previous connection's HELLO told us; re-established below, if this one repeats it
                // (the subscription handshake is deliberately left out of this: it doesn't do the discovery step)
                RoleKnownFromHello = false;

                // likewise per-connection: re-armed from this handshake's reply, if we ask
                _maintenanceNotificationsActive = _maintenanceNotificationsRequested = false;
                _maintenanceNotificationsRefusal = null;
            }

            // HELLO serves two purposes: negotiating RESP3, and reporting details we would otherwise need INFO or
            // CONFIG for (see #2968) - so we issue it whenever the server should understand it, at 2 or 3.
            bool helloAvailable = Multiplexer.RawConfig.TryHello(out int helloProtocol); // includes an availability check on HELLO
            bool negotiateResp3 = helloAvailable && helloProtocol >= 3;

            // HELLO can carry the credentials, but we only use that when we have no other way of authenticating,
            // and *never* both HELLO AUTH and a standalone AUTH. This is defensive against a redis bug (7.4
            // through at least 8.x; valkey is unaffected): inside a pipelined batch, a *failing* AUTH only gets
            // a reply if it is the first command in that batch - otherwise the error is silently dropped, which
            // desynchronizes every reply that follows it on the connection. When that happens during the
            // handshake, the tracer never gets its answer and the connection never becomes usable: what should
            // have been a clean authentication failure instead presents as a connection that times out
            // everything. So AUTH goes first in the batch, and HELLO follows it (bare).
            bool haveCredentials = !string.IsNullOrWhiteSpace(user) || !string.IsNullOrWhiteSpace(password);
            bool canAuthDirectly = Multiplexer.CommandMap.IsAvailable(RedisCommand.AUTH);
            bool helloCarriesCredentials = helloAvailable && haveCredentials && !canAuthDirectly;

            // ...which also means HELLO doesn't have to come first: only the credential-carrying flavour does,
            // as nothing else can authenticate the connection in that case
            if (helloCarriesCredentials)
            {
                log?.LogInformationAuthenticatingViaHello(new(this));
                await WriteHelloAsync(connection, log, helloProtocol, user, password, clientName).ForAwait();
            }
            else if (!negotiateResp3)
            {
                // whether or not we issue HELLO for discovery below, we're RESP2
                connection.SetProtocol(RedisProtocol.Resp2);
            }

            if (!string.IsNullOrWhiteSpace(user) && canAuthDirectly)
            {
                log?.LogInformationAuthenticatingUserPassword(new(this));
                msg = Message.Create(-1, CommandFlags.FireAndForget | Message.NoFlushFlag, RedisCommand.AUTH, user.AsRedisValue(), password.AsRedisValue());
                msg.SetInternalCall();
                await WriteDirectOrQueueFireAndForgetAsync(connection, msg, ResultProcessor.DemandOK).ForAwait();
            }
            else if (!string.IsNullOrWhiteSpace(password) && canAuthDirectly)
            {
                log?.LogInformationAuthenticatingPassword(new(this));
                msg = Message.Create(-1, CommandFlags.FireAndForget | Message.NoFlushFlag, RedisCommand.AUTH, password.AsRedisValue());
                msg.SetInternalCall();
                await WriteDirectOrQueueFireAndForgetAsync(connection, msg, ResultProcessor.DemandOK).ForAwait();
            }

            // a bare HELLO, now that the connection is authenticated; for RESP2 this is discovery only, so we
            // limit it to the interactive connection (the subscription connection does no discovery)
            bool bareHello = helloAvailable && !helloCarriesCredentials && (negotiateResp3 || isInteractive);
            if (bareHello)
            {
                await WriteHelloAsync(connection, log, helloProtocol, user: null, password: null, clientName: null).ForAwait();
            }

            if (Multiplexer.CommandMap.IsAvailable(RedisCommand.CLIENT))
            {
                if (!string.IsNullOrWhiteSpace(clientName))
                {
                    log?.LogInformationSettingClientName(new(this), clientName);
                    msg = Message.Create(-1, CommandFlags.FireAndForget | Message.NoFlushFlag, RedisCommand.CLIENT, RedisLiterals.SETNAME, clientName.AsRedisValue());
                    msg.SetInternalCall();
                    await WriteDirectOrQueueFireAndForgetAsync(connection, msg, ResultProcessor.DemandOK).ForAwait();
                }

                if (config.SetClientLibrary)
                {
                    // note that this is a relatively new feature, but usually we won't know the
                    // server version, so we will use this speculatively and hope for the best
                    log?.LogInformationSettingClientLibVer(new(this));

                    var libName = Multiplexer.GetFullLibraryName();
                    if (!string.IsNullOrWhiteSpace(libName))
                    {
                        msg = Message.Create(-1, CommandFlags.FireAndForget | Message.NoFlushFlag, RedisCommand.CLIENT, RedisLiterals.SETINFO, RedisLiterals.lib_name, libName.AsRedisValue());
                        msg.SetInternalCall();
                        await WriteDirectOrQueueFireAndForgetAsync(connection, msg, ResultProcessor.DemandOK).ForAwait();
                    }

                    var version = ClientInfoSanitize(Utils.GetLibVersion());
                    if (!string.IsNullOrWhiteSpace(version))
                    {
                        msg = Message.Create(-1, CommandFlags.FireAndForget | Message.NoFlushFlag, RedisCommand.CLIENT, RedisLiterals.SETINFO, RedisLiterals.lib_ver, version.AsRedisValue());
                        msg.SetInternalCall();
                        await WriteDirectOrQueueFireAndForgetAsync(connection, msg, ResultProcessor.DemandOK).ForAwait();
                    }
                }

                msg = Message.Create(-1, CommandFlags.FireAndForget | Message.NoFlushFlag, RedisCommand.CLIENT, RedisLiterals.ID);
                msg.SetInternalCall();
                await WriteDirectOrQueueFireAndForgetAsync(connection, msg, autoConfig ??= ResultProcessor.AutoConfigureProcessor.Create(log)).ForAwait();

                // A suppressed opt-in has to be visible: a caller who wrote maintNotifications=Enabled asked for
                // a guarantee and is not getting it, and the alternative to saying so is a deployment where
                // the feature is silently absent and nothing explains why.
                if (isInteractive && Multiplexer.IsGroupMember
                    && Multiplexer.RawConfig.MaintenanceNotifications != MaintenanceNotificationMode.Disabled)
                {
                    log?.LogWarningMaintenanceNotificationsSuppressedForGroup(
                        new(this), Multiplexer.RawConfig.MaintenanceNotifications);
                }

                if (ShouldRequestMaintenanceNotifications(isInteractive, negotiateResp3))
                {
                    _maintenanceNotificationsRequested = true;
                    // speculative in the same way as the AUTH above: we don't yet know what HELLO negotiated,
                    // so we ask whenever we asked for RESP3, and ReconcileMaintenanceNotifications sorts out a
                    // downgrade once the reply has been processed. A bare ON is explicitly valid: the server
                    // then picks the endpoint type, which is what we want until we derive one ourselves.
                    log?.LogInformationRequestingMaintenanceNotifications(new(this), MaintenanceMode);

                    // A bare ON leaves the endpoint type to the server, and every MOVING observed that way
                    // carried no address at all - so when a caller asks for a specific form, say so.
                    var endpointType = MaintenanceMovingEndpointTypeLiteral(connection);
                    msg = endpointType.IsNull
                        ? Message.Create(-1, CommandFlags.FireAndForget | Message.NoFlushFlag, RedisCommand.CLIENT, RedisLiterals.MAINT_NOTIFICATIONS, RedisLiterals.ON)
                        : Message.Create(-1, CommandFlags.FireAndForget | Message.NoFlushFlag, RedisCommand.CLIENT, [RedisLiterals.MAINT_NOTIFICATIONS, RedisLiterals.ON, RedisLiterals.moving_endpoint_type, endpointType]);
                    msg.SetInternalCall();
                    await WriteDirectOrQueueFireAndForgetAsync(connection, msg, ResultProcessor.MaintenanceNotifications).ForAwait();
                }

                await EnableClientTrackingAsync(connection, log, negotiateResp3).ForAwait();
            }

            var bridge = connection.BridgeCouldBeNull;
            if (bridge is null)
            {
                return;
            }

            var connType = bridge.ConnectionType;
            if (connType == ConnectionType.Interactive && !ConnectionMultiplexer.NewCoreEngine)
            {
                await AutoConfigureAsync(connection, log, extraFlags: Message.NoFlushFlag, helloPending: helloAvailable).ForAwait();
            }

            // note that the final messages *are* flushed (no Message.NoFlushFlag)
            var tracer = GetTracerMessage(true);
            tracer.SetHandshakeCompletion();
            tracer = LoggingMessage.Create(log, tracer);
            log?.LogInformationSendingCriticalTracer(new(this), tracer.CommandAndKey);
            Debug.Assert(tracer.IsHandshakeCompletion, "Tracer message should identify as handshake completion");
            await WriteDirectOrQueueFireAndForgetAsync(connection, tracer, ResultProcessor.EstablishConnection).ForAwait();

            // Note: this **must** be the last thing on the subscription handshake, because after this
            // we will be in subscriber mode: regular commands cannot be sent
            if (connType == ConnectionType.Subscription)
            {
                var configChannel = Multiplexer.ConfigurationChangedChannel;
                if (configChannel != null)
                {
                    msg = Message.Create(-1, CommandFlags.FireAndForget, RedisCommand.SUBSCRIBE, RedisChannel.Literal(configChannel));
                    // Note: this is NOT internal, we want it to queue in a backlog for sending when ready if necessary
                    await WriteDirectOrQueueFireAndForgetAsync(connection, msg, ResultProcessor.TrackSubscriptions).ForAwait();
                }
            }
            log?.LogInformationFlushingOutboundBuffer(new(this));
            connection.Flush();
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

        internal bool CanSimulateConnectionFailure => interactive?.CanSimulateConnectionFailure == true;

        /// <summary>
        /// For testing only.
        /// </summary>
        internal void SimulateConnectionFailure(SimulatedFailureType failureType)
        {
            interactive?.SimulateConnectionFailure(failureType);
            subscription?.SimulateConnectionFailure(failureType);

            // and the other core's connections, which are the ones actually carrying commands under the
            // engine flag. Breaking only this one meant the simulation broke a socket nothing was using.
            Multiplexer.NewCoreIfCreated?.SimulateConnectionFailure(EndPoint, failureType);
        }

        internal bool HasPendingCallerFacingItems()
        {
            // check whichever bridges exist
            if (interactive?.HasPendingCallerFacingItems() == true) return true;
            return subscription?.HasPendingCallerFacingItems() ?? false;
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
