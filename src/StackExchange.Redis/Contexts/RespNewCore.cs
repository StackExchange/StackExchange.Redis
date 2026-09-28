using System;
using System.Collections.Concurrent;
using System.IO;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Threading;
using System.Threading.Tasks;
using RESPite.Operations;
using RESPite.Transports;

namespace StackExchange.Redis
{
    /// <summary>
    /// EXPERIMENTAL SPIKE. Builds the new executor chain over a multiplexer's endpoints and configuration.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Not a replacement for <c>ConnectionMultiplexer</c>, and not trying to be.</b> It borrows the
    /// parts of one that are about <i>configuration and topology</i> - which endpoints exist, which node
    /// owns a slot, what the credentials are - and replaces only the part that is about <i>sending</i>.
    /// Those are the two halves worth separating: topology discovery is a large, subtle, well-tested body
    /// of work that the new core has no quarrel with, and re-implementing it to prove a point about the
    /// send path would be the wrong experiment.
    /// </para>
    /// <para>
    /// The point of it is to get the whole new stack - operation, connection, transport, handshake,
    /// endpoint/multiplexer executors, redirects - in front of the existing test suite, which knows far
    /// more about what this library must do than any test written alongside the spike.
    /// </para>
    /// </remarks>
    internal sealed class RespNewCore : IAsyncDisposable
    {
        private readonly ConnectionMultiplexer _multiplexer;
        private readonly RespTopology _topology;

        /// <summary>One executor - and so one connection - per endpoint, whatever databases are in use.</summary>
        private readonly ConcurrentDictionary<EndPoint, RespEndpointExecutor> _endpoints = new();

        /// <summary>A per-database view of each endpoint, keyed by database then endpoint.</summary>
        private readonly ConcurrentDictionary<int, ConcurrentDictionary<EndPoint, RespExecutorBase>> _views = new();

        /// <summary>The <c>SELECT</c> frames the views inject; rendered once per database.</summary>
        private readonly SelectPreamble _select;

        /// <summary>
        /// The database each connection's handshake selects, and so the one its owning executor needs no
        /// <c>SELECT</c> for.
        /// </summary>
        private readonly int _defaultDatabase;

        /// <summary>The protocol each endpoint's handshake actually negotiated.</summary>
        /// <remarks>
        /// <b>Recorded because it is not knowable in advance.</b> Configuration says what to ask for;
        /// only the <c>HELLO</c> reply says what was agreed - a server that predates RESP3, or has it
        /// disabled, answers RESP2 however the client was configured. Anything that depends on the
        /// protocol therefore cannot be decided at connect time, which is the whole reason this exists
        /// rather than reading <c>RawConfig.Protocol</c>.
        /// </remarks>
        private readonly ConcurrentDictionary<EndPoint, RedisProtocol> _protocols = new();

        /// <summary>What each endpoint's own handshake reported about itself.</summary>
        /// <remarks>
        /// Filled by the connect path, read by that endpoint's executor. An observation from the
        /// connection that will run the command, rather than a version borrowed from somebody else's
        /// topology.
        /// </remarks>
        private readonly ConcurrentDictionary<EndPoint, RedisFeatures> _observed = new();
        private readonly RespMultiplexerExecutor _router;
        private readonly NewCoreFeatureProbe _features;

        internal RespNewCore(ConnectionMultiplexer multiplexer)
        {
            _multiplexer = multiplexer;
            // UNKNOWN, not "whatever the strategy says right now". ServerType has no "not yet determined"
            // value, so a multiplexer that has not finished discovering reports Standalone - and taking
            // that as an answer makes this core believe, permanently, that a cluster is not one: no slots
            // are folded, so a batch or transaction has nothing to route on and goes to whichever node
            // answers first. Single commands survive that by being corrected with -MOVED; a MULTI/EXEC run
            // cannot be redirected mid-flight, so it simply aborts.
            //
            // Unknown is exactly the state that window is for: slots are computed speculatively - a hash
            // per key until the first handshake reports back - and RespHandshake settles it via
            // OnServerType. See RespTopology, which says this in its own remarks.
            _topology = new RespTopology(SeedTopology(multiplexer))
            {
                // only worth an INFO REPLICATION per connection when there is something to prefer a
                // replica OVER; a cluster learns the same thing from a reply it reads anyway
                WantsRoles = multiplexer.RawConfig.EndPoints.Count > 1,
            };
            _features = new NewCoreFeatureProbe(this);
            _select = new SelectPreamble(new RespContext(multiplexer.RawConfig.CommandMap));
            _defaultDatabase = multiplexer.RawConfig.DefaultDatabase.GetValueOrDefault();
            _router = Rebind(multiplexer.RawConfig.DefaultDatabase.GetValueOrDefault());
        }

        /// <summary>What this core should believe about cluster-ness before its own first handshake.</summary>
        /// <remarks>
        /// <para>
        /// <b>Three states, and <see cref="ServerType"/> can only express two.</b> There is no "not yet
        /// determined" server type, so a multiplexer still discovering reports <c>Standalone</c> - and
        /// taking that as an answer makes this core believe, permanently, that a cluster is not one. No
        /// slots are folded, so a batch or transaction has nothing to route on and goes to whichever node
        /// answers first. A single command survives that: it is corrected with <c>-MOVED</c>, which is also
        /// how the topology shakes out in the first place. A <c>MULTI</c>/<c>EXEC</c> run cannot be
        /// redirected mid-flight, so it simply aborts.
        /// </para>
        /// <para>
        /// So connectedness is the discriminator the type lacks: a connected multiplexer has an answer and
        /// this core may use it, and an unconnected one has not, which is <see cref="RespClusterState.Unknown"/>
        /// - slots computed speculatively until this core's own handshake reports back. Note that the
        /// multiplexer being connected says nothing about THIS core's connections, which are its own; what
        /// is borrowed is the discovered shape of the deployment, not a connection.
        /// </para>
        /// </remarks>
        private static RespClusterState SeedTopology(ConnectionMultiplexer multiplexer)
            => multiplexer.ServerSelectionStrategy.ServerType == ServerType.Cluster ? RespClusterState.Yes
                : multiplexer.IsConnected ? RespClusterState.No
                : RespClusterState.Unknown;

        internal bool RoutesBySlotForTest => _topology.RoutesBySlot;

        internal bool HasSlotMapForTest => _topology.HasSlotMap;

        internal System.Net.EndPoint? SlotOwnerForTest(int slot) => _topology.SlotOwner(slot);

        internal EndPoint[] SlotReplicasForTest(int slot)
            => _topology.Owners(slot)?.Replicas ?? Array.Empty<EndPoint>();

        internal string RoleOfForTest(EndPoint endpoint) => _topology.RoleOf(endpoint).ToString();

        internal string TopologyStateForTest => _topology.State.ToString();

        /// <summary>The one database this core can reach; see <c>GetDatabase</c>.</summary>
        internal int Database => _router.Database;

        /// <summary>A database context that sends through the new core.</summary>
        /// <param name="database">The database index.</param>
        /// <remarks>
        /// <b>The feature probe is not optional, and leaving it out is not merely a missing nicety.</b>
        /// Several commands are <i>chosen</i> from what the server supports - an all-GET <c>BITFIELD</c>
        /// goes out as <c>BITFIELD_RO</c> when the server has it, which is what lets a replica serve it.
        /// Without the probe the surface reports "unknown", picks the writable command, and a caller who
        /// demanded a replica is then refused for a read. The selection logic already handles "unknown"
        /// gracefully; it just answers a question nobody had asked properly.
        /// </remarks>
        internal RespDatabaseContext GetDatabase(int database = 0)
        {
            return new(new RespContext(
                    _multiplexer.RawConfig.CommandMap,
                    database: database,
                    serverType: _multiplexer.ServerSelectionStrategy.ServerType)
                .WithTopology(_topology)

                // the multiplexer's, not one of this core's own: a loaded script is a fact about the
                // SERVER, so both cores must consult the same record or each will reload what the other
                // already sent. Without it the registry is null, which also meant no NOSCRIPT repair -
                // see the null-registry branch in Scripts.Methods
                .WithScriptCache(_multiplexer.ScriptCache)

                // As with the script cache, the multiplexer's and not one of this core's own: invalidation
                // arrives on whichever connection is tracking, and RespPushDispatch applies it to
                // multiplexer.ClientCache, so a context holding any other instance would fill one cache and
                // have a different one invalidated. Without this the new core had NO cache at all - not a
                // cold one, an absent one - so every counter read zero and the client-side cache silently
                // did nothing whenever the engine flag was on.
                .WithCache(_multiplexer.ClientCache)
                .AppendChannelPrefix(_multiplexer.RawConfig.ChannelPrefix)
                .WithServices(_features)
                .WithExecutor(database == _router.Database ? _router : Rebind(database)));
        }

        /// <summary>A router whose endpoints are the ones connected to <paramref name="database"/>.</summary>
        /// <remarks>
        /// <b>A connection per (endpoint, database), which is not what the shipped core does.</b> There,
        /// one connection serves every database and a <c>SELECT</c> is injected immediately before any
        /// command for a different one - cheaper in sockets, and the reason the shipped path needs a
        /// preamble mechanism at all.
        /// <para>
        /// This is the honest trade for now: <c>SELECT</c> is sticky connection state, so multiplexing
        /// databases over one connection means every such command must be written as a contiguous pair,
        /// which is the capability section 7s recorded as unreachable. A connection each is correct today
        /// and costs a socket per database actually used - and databases are rare, discouraged in cluster,
        /// and usually one. Revisit when the preamble lands; the write slot from 7t is already the
        /// primitive the pair-write needs.
        /// </para>
        /// <para>
        /// It replaced something far worse: before this, every database resolved to the SAME connection and
        /// silently read and wrote the handshake's database. See section 7x.
        /// </para>
        /// </remarks>
        private RespMultiplexerExecutor Rebind(int database) => new(
            _topology,
            (slot, command, flags) => ForSlot(database, slot, command, flags),
            (command, flags) => Any(database, command, flags),
            database,
            endpoint => Executor(database, endpoint),
            OnSlotMoved,
            OnTopologySuspect,
            channel => SubscribedExecutor(database, channel));

        /// <summary>
        /// Which endpoint owns a slot, according to the multiplexer's own topology.
        /// </summary>
        /// <remarks>
        /// <b>Borrowed, not reimplemented.</b> <c>ServerSelectionStrategy</c> already maintains the slot
        /// map from <c>CLUSTER NODES</c>, keeps it fresh across reshards, and knows about replica
        /// preference and unreachable nodes. A second map maintained by the spike would be a second thing
        /// to get wrong, and would not make the send path any more or less correct.
        /// </remarks>
        /// <remarks>
        /// The command and flags travel because <c>Select</c> needs them: primary/replica preference is
        /// part of choosing a node, not a separate step, and <c>ServerSelectionStrategy</c> already knows
        /// which endpoints are replicas and which are reachable.
        /// </remarks>
        /// <summary>The executor for a keyed command, from this core's own slot map where it has one.</summary>
        /// <remarks>
        /// <b>Our map first, the shipped selector only as a fallback.</b> That selector's map is a
        /// <c>ServerEndPoint[]</c> filled from a <c>CLUSTER NODES</c> the SHIPPED core issued during its
        /// auto-configure, so consulting it is what made this core unable to route until the other one had
        /// connected - the single fact behind every two-core symptom in section 9a. The handshake now fills
        /// a map of our own from <c>CLUSTER SLOTS</c> on the connection it just brought up.
        /// <para>
        /// Falling back while the map is empty is what makes this safe to adopt before it is finished: a
        /// deployment whose server will not answer <c>CLUSTER SLOTS</c>, or one still mid-discovery, routes
        /// exactly as it did before rather than routing wrongly.
        /// </para>
        /// <para>
        /// Replica preference is answered from the map too, because the <c>CLUSTER SLOTS</c> that filled it
        /// names the replicas of each range - and that pairing is the part no other source has: a server
        /// being "a replica" says nothing about WHICH slots it replicates. A demand this map cannot satisfy
        /// still falls through to the selector rather than being quietly served a primary.
        /// </para>
        /// </remarks>
        private RespExecutorBase? ForSlot(int database, int slot, RedisCommand command, CommandFlags flags)
            => EndpointForSlot(slot, command, flags) is { } endpoint ? Executor(database, endpoint) : null;

        /// <summary>Which endpoint serves a slot, with no executor involved.</summary>
        /// <param name="slot">The hash slot.</param>
        /// <param name="command">The command, which decides whether a replica is eligible.</param>
        /// <param name="flags">The caller's preference.</param>
        /// <remarks>
        /// Separate from <see cref="ForSlot"/> because one caller wants the answer and not the machinery:
        /// the feature probe asks "which server would take this?" purely to read its version, and creating
        /// - or worse, dialling - an executor to answer that would make a question into an action.
        /// </remarks>
        private EndPoint? EndpointForSlot(int slot, RedisCommand command, CommandFlags flags)
        {
            if (_topology.Owners(slot) is { } owners && ChooseByRole(owners, command, flags) is { } chosen)
            {
                return chosen;
            }

            var server = _multiplexer.ServerSelectionStrategy.Select(slot, command, flags, allowDisconnected: true);
            return server is null ? EndpointForAny(command, flags) : server.EndPoint;
        }

        /// <summary>Which of a slot's servers should take this command.</summary>
        /// <param name="owners">The primary and replicas for the slot.</param>
        /// <param name="command">The command, which decides whether a replica is even eligible.</param>
        /// <param name="flags">The caller's preference.</param>
        /// <returns>The endpoint, or null when the map cannot honour what was asked for.</returns>
        /// <remarks>
        /// <b>The command outranks the preference, and that is not a courtesy.</b> A write is refused by a
        /// replica, so sending one there on a <see cref="CommandFlags.PreferReplica"/> would turn a
        /// preference into a failure. <c>Route</c> already throws for the harder case - a write that
        /// DEMANDED a replica - so by here "primary-only" simply means the preference does not apply.
        /// <para>
        /// Null for an unsatisfiable demand rather than the primary: a caller who demanded a replica and
        /// got a primary has been told something untrue about where their read ran. The selector may still
        /// know a replica this map does not, and if nobody does the demand fails, which is what a demand is.
        /// </para>
        /// </remarks>
        private EndPoint? ChooseByRole(RespTopology.SlotOwners owners, RedisCommand command, CommandFlags flags)
        {
            if (command.IsPrimaryOnly()) return owners.Primary;

            switch (Message.GetPrimaryReplicaFlags(flags))
            {
                case CommandFlags.DemandReplica:
                    return PickReplica(owners.Replicas);
                case CommandFlags.PreferReplica:
                    return PickReplica(owners.Replicas) ?? owners.Primary;
                default:
                    return owners.Primary;
            }
        }

        /// <summary>Choose one of a set of replicas.</summary>
        /// <param name="replicas">The candidates; may be empty.</param>
        /// <returns>The chosen endpoint, or null when there are none.</returns>
        /// <remarks>
        /// <b>Round-robin from a rotating offset, preferring one already connected.</b> Taking the first
        /// every time would put every replica read of a range on one node, which is most of what asking for
        /// a replica is for; always taking the next would dial a fresh socket for a read an existing
        /// connection could serve, because this core connects lazily.
        /// <para>
        /// Scanning FROM the rotating offset gets both: once several are up the offset spreads across them,
        /// and while none is up it still advances, so the replicas get dialled rather than one of them
        /// being picked forever.
        /// </para>
        /// </remarks>
        private EndPoint? PickReplica(EndPoint[] replicas)
        {
            if (replicas.Length == 0) return null;

            var offset = (uint)Interlocked.Increment(ref _replicaRotation);
            for (var i = 0; i < replicas.Length; i++)
            {
                var candidate = replicas[(offset + (uint)i) % (uint)replicas.Length];
                if (_endpoints.TryGetValue(candidate, out var dialled) && dialled.IsConnectedNow) return candidate;
            }

            return replicas[offset % (uint)replicas.Length];
        }

        /// <summary>Advances every time a replica is chosen; see <see cref="PickReplica"/>.</summary>
        private int _replicaRotation;

        /// <summary>The executor for a command with no key, which any acceptable server could serve.</summary>
        /// <remarks>
        /// <para>
        /// <b>Prefers an endpoint this core has already dialled.</b> The core connects lazily, so in a
        /// cluster most endpoints have no connection until something needs them - while the selector's
        /// keyless choice ROUND-ROBINS (<c>AnyServer</c> advances an offset every call). Taking the first
        /// answer therefore opened a new socket for a command any existing connection could have served,
        /// and made keyless routing differ from call to call.
        /// </para>
        /// <para>
        /// That was visible from outside: <c>IsConnected(default)</c> resolves a keyless route and asks
        /// whether THAT endpoint is connected, so on a six-node cluster with one endpoint dialled it
        /// answered true or false depending on where the round-robin happened to land - the same code and
        /// the same healthy cluster giving different answers on consecutive calls.
        /// </para>
        /// <para>
        /// <b>Asked through the selector rather than by scanning our own connections</b>, so only servers
        /// the selector would have offered are considered and its primary/replica rules still decide. The
        /// loop is bounded by the endpoint count, and the first answer is kept as the fallback for when
        /// nothing is dialled yet - which is every keyless command on a fresh multiplexer, so there has to
        /// be one.
        /// </para>
        /// </remarks>
        private RespExecutorBase? Any(int database, RedisCommand command, CommandFlags flags)
            => EndpointForAny(command, flags) is { } endpoint ? Executor(database, endpoint) : null;

        /// <inheritdoc cref="Any"/>
        /// <param name="command">The command.</param>
        /// <param name="flags">The caller's preference.</param>
        /// <remarks>See <see cref="EndpointForSlot"/> for why the endpoint and the executor are separated.</remarks>
        private EndPoint? EndpointForAny(RedisCommand command, CommandFlags flags)
        {
            // A keyless replica read, answered from our own roles. There is no slot to look the pairing up
            // by here, which is why the flat role record exists beside the map: in a cluster every replica
            // replicates SOMETHING, and for a command that names no key that is all the question needs.
            if (!command.IsPrimaryOnly()
                && Message.GetPrimaryReplicaFlags(flags) is CommandFlags.DemandReplica or CommandFlags.PreferReplica
                && PickReplica(_topology.Replicas) is { } replica)
            {
                return replica;
            }

            var strategy = _multiplexer.ServerSelectionStrategy;
            ServerEndPoint? fallback = null;

            var attempts = _multiplexer.GetEndPoints().Length;
            if (attempts < 1) attempts = 1;

            for (var i = 0; i < attempts; i++)
            {
                var candidate = strategy.Select(
                    ServerSelectionStrategy.NoSlot, command, flags, allowDisconnected: true);
                if (candidate is null) break;

                fallback ??= candidate;
                if (_endpoints.TryGetValue(candidate.EndPoint, out var dialled) && dialled.IsConnectedNow)
                {
                    return candidate.EndPoint;
                }
            }

            if (fallback is not null) return fallback.EndPoint;

            var endpoints = _multiplexer.GetEndPoints();
            return endpoints.Length == 0 ? null : endpoints[0];
        }

        /// <summary>The executor for the server this client is subscribed on for a channel, if any.</summary>
        /// <remarks>
        /// Borrowed from the multiplexer's subscription registry rather than kept here, for the same reason
        /// the slot map is: the subscriptions are the multiplexer's, and a second record of them would be a
        /// second thing to get wrong. Null - no subscription, or no server for it - means "no preference",
        /// and the publish routes on the channel's slot like anything else.
        /// </remarks>
        private RespExecutorBase? SubscribedExecutor(int database, RedisChannel channel)
            => _multiplexer.GetSubscribedServer(channel) is { } server
                ? Executor(database, server.EndPoint)
                : null;

        /// <summary>
        /// Whether deliveries for this endpoint arrive on its ordinary connection, rather than needing one
        /// of their own.
        /// </summary>
        /// <param name="endpoint">The endpoint in question.</param>
        /// <remarks>
        /// <b>Know, or assume - and the difference matters.</b> Once a handshake has completed this is a
        /// fact; before that it is a guess from configuration, because the answer does not exist yet. The
        /// shipped core draws exactly this distinction in <c>ServerEndPoint.KnowOrAssumeResp3</c>, and it
        /// has to: callers ask whether the subscriber is connected long before anything has connected.
        /// <para>
        /// Under RESP3 a delivery is a push frame on the same connection, so there is no second socket at
        /// all. Only RESP2 needs one, which is why nothing here creates one speculatively.
        /// </para>
        /// </remarks>
        private bool KnowOrAssumeResp3(EndPoint endpoint)
            => _protocols.TryGetValue(endpoint, out var known)
                ? known >= RedisProtocol.Resp3
                : _multiplexer.RawConfig.TryResp3();

        private void OnSlotMoved(int slot, EndPoint endpoint)
        {
            // our own map learns directly from the redirect - the server just told us where the slot went,
            // which is more current than anything a rediscovery would find - and the shipped core is still
            // asked to reconfigure, because its map is what everything else reads until phase D
            _topology.OnSlotMoved(slot, endpoint);
            _multiplexer.ReconfigureIfNeeded(endpoint, false, "MOVED encountered");
        }

        private void OnTopologySuspect()
            => _multiplexer.ReconfigureIfNeeded(null, false, "unroutable redirect");

        /// <summary>One executor - and so one socket - per endpoint for RESP2 deliveries, created on demand.</summary>
        private readonly ConcurrentDictionary<EndPoint, RespEndpointExecutor> _subscriptions = new();

        /// <summary>How many sockets exist purely for deliveries; zero unless something subscribed.</summary>
        internal int SubscriptionConnectionCount => _subscriptions.Count;

        /// <summary>The ordinary connection for an endpoint, for tests that compare the two.</summary>
        /// <param name="endpoint">The endpoint.</param>
        internal RespEndpointExecutor InteractiveEndpoint(EndPoint endpoint) => Endpoint(endpoint);

        /// <summary>
        /// The connection deliveries arrive on for this endpoint, creating one <b>only if this endpoint
        /// needs a second socket and something is actually asking</b>.
        /// </summary>
        /// <param name="endpoint">The endpoint to subscribe on.</param>
        /// <remarks>
        /// <para>
        /// <b>Fully lazy, deliberately, and not merely as an optimisation.</b> Deployments actively reduce
        /// their server socket count - disabling the legacy notification channel among other things - so a
        /// subscription socket has to be pay-per-play. Opening one for an endpoint nobody subscribes on
        /// would spend exactly the resource that effort is protecting.
        /// </para>
        /// <para>
        /// Under RESP3 this returns the ORDINARY executor: deliveries are push frames on the connection
        /// that is already there, so a second socket would buy nothing. Only RESP2 gets one of its own,
        /// and only then is <c>DeliversArrays</c> true - which is the invariant that flag documents, now
        /// with something able to establish it.
        /// </para>
        /// </remarks>
        internal RespEndpointExecutor SubscriptionEndpoint(EndPoint endpoint)
        {
            if (KnowOrAssumeResp3(endpoint)) return Endpoint(endpoint);

            return _subscriptions.TryGetValue(endpoint, out var existing)
                ? existing
                : _subscriptions.GetOrAdd(endpoint, CreateSubscription(endpoint));
        }

        /// <summary>
        /// A context pinned to one server: the already-routed piece <see cref="IServer"/> wants.
        /// </summary>
        /// <param name="endpoint">The server to send to.</param>
        /// <remarks>
        /// <para>
        /// <b>There is no routing decision left to make</b>, which is the whole point: an endpoint executor
        /// IS a server, so pinning to one is choosing it rather than wrapping it in something that chooses.
        /// That is why this is three lines and the multiplexer context is not.
        /// </para>
        /// <para>
        /// <b>No database</b> - a server is not database-scoped, so the context carries <c>-1</c> and a
        /// command that does need one fails loudly rather than quietly running against whichever database
        /// the handshake happened to select. <c>IServer</c>'s own database-scoped members take the number
        /// explicitly. And <b>no cache</b>: invalidation is reported by key and server commands are
        /// keyless, so nothing could ever invalidate a cached <c>INFO</c>.
        /// </para>
        /// </remarks>
        internal RespServerContext ServerContext(EndPoint endpoint)
            => new(new RespContext(
                    _multiplexer.RawConfig.CommandMap,
                    database: -1,
                    serverType: _multiplexer.ServerSelectionStrategy.ServerType)
                .WithScriptCache(_multiplexer.ScriptCache)
                .WithServices(_features)
                .WithExecutor(Endpoint(endpoint)));

        /// <summary>
        /// A context that sends on the connection deliveries arrive on for an endpoint.
        /// </summary>
        /// <param name="endpoint">The endpoint to subscribe on.</param>
        /// <remarks>
        /// <b>No database, no script cache, no topology.</b> Pub/sub is not per-database - a subscription
        /// made on database 0 receives what was published on database 7 - and a connection in subscriber
        /// mode cannot run the commands those services exist for. What it does carry is the channel
        /// prefix, because that IS the channel as far as the wire is concerned.
        /// </remarks>
        internal RespContext SubscriptionContext(EndPoint endpoint)
            => new RespContext(_multiplexer.RawConfig.CommandMap)
                .AppendChannelPrefix(_multiplexer.RawConfig.ChannelPrefix)
                .WithServices(_features)
                .WithExecutor(SubscriptionEndpoint(endpoint));

        /// <summary>An endpoint executor whose connection delivers RESP2 pub/sub arrays.</summary>
        private RespEndpointExecutor CreateSubscription(EndPoint endpoint) => new(
            token => ConnectAsync(_defaultDatabase, endpoint, subscription: true, token),
            _defaultDatabase,
            endpoint,
            _multiplexer.RawConfig.BacklogPolicy.QueueWhileDisconnected,
            () => _observed.TryGetValue(endpoint, out var features) ? features : null,
            StartProfile,
            _select,
            _multiplexer.RawConfig.ConnectTimeout,
            () => _multiplexer.RawConfig.ReconnectRetryPolicy);

        /// <summary>The executor for one database on one endpoint, over that endpoint's single connection.</summary>
        /// <remarks>
        /// <b>One connection per endpoint, and a view per database over it</b> - which is what the shipped
        /// core does, and what this could not do until <c>SELECT</c> could be injected as a preamble. It
        /// replaces a connection per (endpoint, database): correct either way, but a socket per database
        /// actually used, and a <c>SELECT</c> that could never move once the handshake had chosen.
        /// <para>
        /// The endpoint's own executor is the view for the database the handshake selected; the rest wrap
        /// it. That keeps the common single-database deployment exactly as it was - no view, no injection,
        /// no per-command question - since an executor whose database matches its connection never needs a
        /// <c>SELECT</c> at all.
        /// </para>
        /// </remarks>
        private RespExecutorBase Executor(int database, EndPoint endpoint)
        {
            var owner = Endpoint(endpoint);
            if (owner.Database == database) return owner;

            // the factory-with-state overload of GetOrAdd does not exist on the down-level targets, and
            // this is not a hot path - a view is resolved once and then reused
            var byEndpoint = _views.TryGetValue(database, out var known)
                ? known
                : _views.GetOrAdd(database, new ConcurrentDictionary<EndPoint, RespExecutorBase>());

            return byEndpoint.TryGetValue(endpoint, out var existing)
                ? existing
                : byEndpoint.GetOrAdd(endpoint, new RespDatabaseExecutor(owner, database));
        }

        /// <summary>The one executor that owns this endpoint's connection.</summary>
        private RespEndpointExecutor Endpoint(EndPoint endpoint)
            => _endpoints.TryGetValue(endpoint, out var existing)
                ? existing
                : _endpoints.GetOrAdd(endpoint, Create(_defaultDatabase, endpoint));

        private RespEndpointExecutor Create(int database, EndPoint endpoint) => new(
            token => ConnectAsync(database, endpoint, token),
            database,
            endpoint,
            _multiplexer.RawConfig.BacklogPolicy.QueueWhileDisconnected,
            () => _observed.TryGetValue(endpoint, out var features) ? features : null,
            StartProfile,
            _select,
            _multiplexer.RawConfig.ConnectTimeout,
            () => _multiplexer.RawConfig.ReconnectRetryPolicy);

        /// <summary>Open a socket, hand it to the new stack, and bring it up.</summary>
        /// <remarks>
        /// <b>The handshake sets the topology before this returns</b>, which is what keeps the ordering
        /// invariant structural: the endpoint executor publishes the connection and drains its backlog the
        /// moment this completes, and a backlog draining against an unset topology is the window that
        /// loses per-slot ordering. See design notes 7h.
        /// </remarks>
        private Task<RespConnection> ConnectAsync(int database, EndPoint endpoint, CancellationToken cancellationToken)
            => ConnectAsync(database, endpoint, subscription: false, cancellationToken);

        private async Task<RespConnection> ConnectAsync(
            int database, EndPoint endpoint, bool subscription, CancellationToken cancellationToken)
        {
            var config = _multiplexer.RawConfig;

            // the shared chain: tunnel, proxy, socket, TLS. This used to be a bare socket here, which
            // silently ignored every one of those - and the TLS I added to it first was a second copy of
            // the shipped logic, which is worse than none: two versions of a security decision, free to
            // drift. DuplexTransport is the boundary; everything below it belongs to the factory.
            var transport = await RespTransportFactory.ConnectAsync(
                endpoint,
                config,
                subscription ? ConnectionType.Subscription : ConnectionType.Interactive,
                _multiplexer.SetAuthSuspect,
                cancellationToken).ConfigureAwait(false);

            var connection = new RespClientConnection(transport, Follow, config.IncludeDetailInExceptions);
            var context = new RespDatabaseContext(
                new RespContext(config.CommandMap, database: 0)
                    .WithExecutor(new RespConnectionExecutor(connection, 0)));

            // AUTH only when there is something to authenticate WITH, matching the shipped handshake's
            // `!IsNullOrWhiteSpace` test. The handshake itself treats "" as a legitimate password - that is
            // how a 'nopass' ACL login is expressed, and it is right for a caller who says so explicitly -
            // but ConfigurationOptions carries "" to mean "none configured", so passing it straight through
            // sent AUTH to servers that have no password and answer it with an error.
            var credentials = !string.IsNullOrWhiteSpace(config.User) || !string.IsNullOrWhiteSpace(config.Password);

            var result = await RespHandshake.PerformAsync(
                context,
                config.User,
                credentials ? config.Password : null,
                _multiplexer.ClientName,
                database,
                config.Protocol is null or RedisProtocol.Resp3,
                _topology,
                endpoint,
                cancellationToken).ConfigureAwait(false);

            // recorded BEFORE the connection is handed back, for the same reason the topology is: the
            // endpoint executor publishes it and drains its backlog the moment this returns, and a
            // command choosing its spelling from "we have no idea" is the case this exists to avoid
            if (result.Version is { } version) _observed[endpoint] = new RedisFeatures(version);
            _protocols[endpoint] = result.Protocol;

            // the handshake's SELECT is where this connection's database is decided; recording it is what
            // lets a later command for a different one know it has to say so first
            connection.CurrentDatabase = database;

            // which server this reached, so a preamble gate can consult the endpoint's beliefs - a loaded
            // script is server-wide, and ServerEndPoint already tracks that and flushes it when a server's
            // identity changes underneath. Borrowed rather than reimplemented while both cores exist.
            connection.Server = _multiplexer.GetServerEndPoint(endpoint, ServerProvenance.Configured, activate: false);

            // RESP2 has no push prefix, so a delivery on this connection is an ordinary array and the only
            // thing marking it as one is that this connection subscribes. Set it nowhere else: on an
            // interactive connection it would start eating replies. Read from what the handshake NEGOTIATED
            // rather than what was configured - a server can answer RESP2 to a RESP3 request.
            connection.DeliversArrays = subscription && result.Protocol < RedisProtocol.Resp3;

            // deliveries arrive here: on the subscription connection under RESP2, and on this one under
            // RESP3, where a push can land on any connection
            connection.OnPush = frame => RespPushDispatch.Dispatch(frame, _multiplexer);

            return connection;
        }

        /// <summary>Begin a profiling record, if anyone is profiling right now.</summary>
        /// <remarks>
        /// <b>Asked per command, not cached.</b> A profiling session is ambient - <c>RegisterProfiler</c>
        /// hands back a provider that can return a different session, or none, on every call - so caching
        /// the answer would profile the wrong session, or keep profiling after someone stopped.
        /// </remarks>
        private object? StartProfile(
            RespPayloadOperation operation, RedisCommand command, CommandFlags flags, int database, EndPoint? endpoint)
        {
            if (endpoint is null) return null;

            var session = _multiplexer.CurrentProfilingSession;
            if (session is null) return null;

            var server = _multiplexer.GetServerEndPoint(endpoint, ServerProvenance.Configured);
            if (server is null) return null;

            var profile = Profiling.ProfiledCommand.NewWithContext(session, server);
            profile.SetOperation(command, flags, database, operation.Diagnostics.CreatedDateTime, operation.Diagnostics.CreatedTimestamp);

            // ENQUEUED IS HERE, and it has to be set by somebody: nothing did, so EnqueuedTimeStamp stayed
            // zero and both spans either side of it were nonsense - CreationToEnqueued measured back to the
            // epoch and EnqueuedToSending measured forward from it. MovedProfiling asserts they are
            // positive, which is a fair thing to ask of a timing record.
            //
            // This is the right moment rather than a convenient one: routing has resolved, so the endpoint
            // is known - which is why the profile is started here at all - and the operation is about to be
            // handed to a connection. The shipped core stamps it at the equivalent point, as the message
            // goes to a bridge.
            //
            // Null connection type: this core does not yet distinguish interactive from subscription at
            // this point, and the shipped core also passes null where it does not know (PhysicalBridge).
            // Claiming "interactive" would be right most of the time, which is not the same as right.
            profile.SetEnqueued(null);

            operation.Profile = profile;
            return profile;
        }

        private bool Follow(in RespRedirect redirect, RespPayloadOperation operation)
            => _router.TryFollowRedirect(in redirect, operation);

        /// <summary>Which endpoint would take this command, routed exactly as the command would be.</summary>
        /// <param name="command">The command.</param>
        /// <param name="key">The key, or a null key for a command that names none.</param>
        /// <param name="flags">The caller's preference.</param>
        /// <remarks>
        /// The same two-way split <c>ResolveFor</c> makes, so that "which server answers this?" cannot
        /// drift from where the command actually goes - two routing rules with one of them only used by a
        /// feature check is exactly the kind of near-duplicate that stays wrong for months.
        /// </remarks>
        private EndPoint? RouteEndpoint(RedisCommand command, in RedisKey key, CommandFlags flags)
            => _topology.RoutesBySlot && !key.IsNull
                ? EndpointForSlot(ServerSelectionStrategy.GetHashSlot(key), command, flags)
                : EndpointForAny(command, flags);

        /// <summary>
        /// Answers "what can the server that would take this command do", from what this core observed.
        /// </summary>
        /// <remarks>
        /// <para>
        /// <b>This used to be <c>RedisBase.ServerFeatureProbe</c> over a <c>RedisDatabase</c> built for the
        /// purpose</b> - an entire command surface constructed so that one method could be called on it.
        /// That method is <c>RedisBase.GetFeatures</c>, whose base implementation is two lines of
        /// multiplexer: select the server, read its version. Only <c>RedisServer</c> overrides it, to pin
        /// the answer to its own node, and a database is not a server - so the database contributed
        /// nothing except a reference to a type that is being deleted.
        /// </para>
        /// <para>
        /// <b>It then read the version off the SHIPPED core's <c>ServerEndPoint</c>, which is the coupling
        /// section 9 exists to remove</b> - the third of the three, after routing and roles. The version
        /// this core needs is one its own handshake already learns, from <c>HELLO</c> where the connection
        /// speaks RESP3 and from <c>INFO SERVER</c> where it does not, and recorded per endpoint before the
        /// connection is handed back.
        /// </para>
        /// <para>
        /// The selector remains the fallback for the same reason it does in routing: while nothing has been
        /// dialled there is nothing to have observed, and answering from the other core's knowledge is
        /// better than answering from the configured default. It goes with the rest of the fallbacks in
        /// phase D.
        /// </para>
        /// <para>
        /// <b>The bool is not a formality.</b> It says whether this is an observation or a guess, and
        /// callers choose command spellings on it - a server reported as older than it is sends the
        /// writable form of a command a replica would have served read-only.
        /// </para>
        /// </remarks>
        private sealed class NewCoreFeatureProbe(RespNewCore core) : IRespServerFeatures
        {
            public bool TryGetFeatures(RedisCommand command, in RedisKey key, CommandFlags flags, out RedisFeatures features)
            {
                if (core.RouteEndpoint(command, in key, flags) is { } endpoint
                    && core._observed.TryGetValue(endpoint, out features))
                {
                    return true;
                }

                var server = core._multiplexer.SelectServer(command, flags, key);

                // usable either way - the configured default version stands in - but only a known server
                // makes this an observation rather than a guess, which is what the bool reports
                features = new RedisFeatures(server is null ? core._multiplexer.RawConfig.DefaultVersion : server.Version);
                return server is not null;
            }
        }

        /// <inheritdoc/>
        public async ValueTask DisposeAsync()
        {
            foreach (var executor in _endpoints.Values)
            {
                await executor.DisposeAsync().ConfigureAwait(false);
            }

            foreach (var executor in _subscriptions.Values)
            {
                await executor.DisposeAsync().ConfigureAwait(false);
            }

            _subscriptions.Clear();

            // the views own nothing - they are a database index over an endpoint's connection - so
            // disposing the endpoints disposes everything there is to dispose
            _views.Clear();
            _endpoints.Clear();
        }
    }
}
