using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using RESPite.Operations;
using RESPite.Transports;
using StackExchange.Redis.Protocol;

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

        /// <summary>The deployment's <c>CLUSTER SLOTS</c> view, as last answered by any of its nodes.</summary>
        /// <remarks>
        /// <b>Here rather than per-server because that is what it describes.</b> The shipped core kept it on
        /// each <see cref="ServerEndPoint"/> because its autoconfigure asked every connection; this core asks
        /// once, so one node holds the answer and every other node needs to be given it - otherwise a
        /// server publishing its own <c>CLUSTER NODES</c> has no SLOTS view beside it and
        /// <c>SetClusterConfiguration</c> falls back to the staler reply for the slot map.
        /// </remarks>
        private ClusterSlotsResult? _clusterSlots;

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

            // pulsed from here on, which is what makes this core's timeout claim true - see
            // ConnectionMultiplexer.PulseCores. Registered in the constructor rather than by whoever asked
            // for the core, because every core needs it and only one of them is the multiplexer's own.
            multiplexer.RegisterCore(this);
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

                // and whatever it learns goes straight back out: IsReplica is what refuses a write
                // client-side, so a role this core knows and the selector does not is a write that never
                // reaches a server. The bounce back in through ServerEndPoint.IsReplica -> PublishRole ->
                // OnRole terminates on the first hop, because the role recorded here already matches
                RoleLearned = PublishRoleToSelector,
            };
            _features = new NewCoreFeatureProbe(this);
            _select = new SelectPreamble(new RespContext(multiplexer.RawConfig.CommandMap));
            _defaultDatabase = multiplexer.RawConfig.DefaultDatabase.GetValueOrDefault();
            _router = Rebind(multiplexer.RawConfig.DefaultDatabase.GetValueOrDefault());
            SeedRoles();
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

        internal bool IsSelectableForTest(EndPoint endpoint) => _topology.IsSelectable(endpoint);

        internal int ConnectedEndpointCountForTest
        {
            get
            {
                var count = 0;
                foreach (var pair in _endpoints)
                {
                    if (pair.Value.IsConnectedNow) count++;
                }

                return count;
            }
        }

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
            channel => SubscribedExecutor(database, channel),
            // and the socket a redirected SUBSCRIBE belongs on, which is not the ordinary one. Without
            // this the fallback sends it to the target's ordinary executor, so a sharded subscribe that
            // follows a -MOVED puts the connection carrying ordinary commands into subscriber mode - and
            // the next publish to that node is refused with "only (P|S)SUBSCRIBE ... allowed in this
            // context". `ClusterShardedTests.KeepSubscribedThroughSlotMigrationAsync` reads that error
            // verbatim; `RespMultiplexerExecutor.TryFollowRedirect` describes the rule it could not apply.
            SubscriptionEndpoint)
        {
            HeartbeatDriven = true,
            Owner = _multiplexer,
        };

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

            // Still the selector when our map cannot answer, and the reason is now down to ONE thing: the
            // map is empty until this core has dialled something, because CLUSTER SLOTS is read by its own
            // handshake. Measured - with the fallback removed, every cluster-routing casualty disappears
            // under ConnectMode.Discover, which opens one socket at connect and fills the map from it. See
            // section 9d, D2.3: what is left is the mode default, and that waits on D2.8.
            var server = _multiplexer.ServerSelectionStrategy.Select(slot, command, flags, allowDisconnected: true);
            if (server is not null) return server.EndPoint;

            return EndpointForAny(command, flags);
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
            // A slot's owner that may not be used is not an answer: null falls through to the selector,
            // which knows what to do instead. Checked only when something is actually barred, since the
            // overwhelmingly common case is that nothing is.
            if (_topology.HasUnselectable && !_topology.IsSelectable(owners.Primary)) return null;

            if (command.IsPrimaryOnly()) return Usable(owners.Primary);

            switch (CommandFlagsInternal.GetPrimaryReplicaFlags(flags))
            {
                case CommandFlags.DemandReplica:
                    return Usable(PickReplica(owners.Replicas));
                case CommandFlags.PreferReplica:
                    return Usable(PickReplica(owners.Replicas)) ?? Usable(owners.Primary);
                default:
                    return Usable(owners.Primary);
            }
        }

        /// <summary>An endpoint, unless this core knows it cannot be reached.</summary>
        /// <param name="endpoint">The endpoint a slot's roles chose, or null if there was none.</param>
        /// <remarks>
        /// <para>
        /// <b>The one selection question this core has to answer for itself.</b> The shipped selector asks
        /// it in its signature - <c>Select(slot, command, flags, allowDisconnected: false)</c> - so a slot
        /// whose owner has gone away routes to something reachable instead of being aimed at a socket that
        /// can never write. Nothing here asked, so a downed owner kept being chosen and recorded;
        /// <c>RetirementUnderMaintenanceTests.ARefusingNodeAccumulatesOnlyOurOwnTrafficAndIsRetired</c>
        /// measures it as a channel still subscribed on the node that was black-holed.
        /// </para>
        /// <para>
        /// <b>"Known down", not "not connected", and that distinction is the whole of it.</b> This core
        /// dials lazily, so an endpoint nobody has needed yet is not connected and is perfectly usable -
        /// barring it would route every first command away from the node that owns the slot. So an
        /// endpoint counts as down only once it has been dialled AND has a connect fault to show for it.
        /// <c>ServerEndPoint.PublishSelectable</c> says the same thing from the other side, which is why
        /// it excludes <c>DidNotRespond</c>: that flag cannot tell the two apart and this can.
        /// </para>
        /// <para>
        /// Returning null rather than a substitute, because choosing the substitute is not this method's
        /// job: <see cref="EndpointForSlot"/> already falls through to the selector and then to
        /// <c>EndpointForAny</c>, both of which prefer somewhere reachable.
        /// </para>
        /// </remarks>
        private EndPoint? Usable(EndPoint? endpoint)
            => endpoint is null || IsKnownDown(endpoint) ? null : endpoint;

        /// <summary>Whether this core has dialled an endpoint and found it unreachable.</summary>
        /// <param name="endpoint">The endpoint.</param>
        /// <remarks>
        /// <b>Either socket counts, and asking only about the ordinary one missed the case that matters.</b>
        /// A node this core has only ever subscribed on has no entry among the ordinary executors, so a
        /// check that looks there alone reports it as fine - which is exactly the shape of a sharded
        /// channel's node going away: the subscription socket is the only one that was ever opened, it is
        /// the one that failed, and the routing decision being made is for that channel.
        /// </remarks>
        internal bool IsKnownDown(EndPoint endpoint)
            => Faulted(_endpoints, endpoint) || Faulted(_subscriptions, endpoint);

        private static bool Faulted(
            ConcurrentDictionary<EndPoint, RespEndpointExecutor> executors, EndPoint endpoint)
            => executors.TryGetValue(endpoint, out var dialled)
                && !dialled.IsConnectedNow
                && dialled.LastConnectFault is not null;

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
            EndPoint? usable = null;
            for (var i = 0; i < replicas.Length; i++)
            {
                var candidate = replicas[(offset + (uint)i) % (uint)replicas.Length];
                if (!_topology.IsSelectable(candidate)) continue; // retiring, redundant, being migrated from

                usable ??= candidate;
                if (_endpoints.TryGetValue(candidate, out var dialled) && dialled.IsConnectedNow) return candidate;
            }

            return usable;
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
            // A replica read, answered from our own roles WHERE WE HAVE THEM.
            var wantsReplica = !command.IsPrimaryOnly()
                && CommandFlagsInternal.GetPrimaryReplicaFlags(flags) is CommandFlags.DemandReplica or CommandFlags.PreferReplica;

            if (wantsReplica && PickReplica(_topology.Replicas) is { } replica) return replica;

            // ...and the selector for the keyless choice, for the same reason and with the same measurement:
            // it knows every configured endpoint's role and selectability without this core having dialled
            // any of them. The role-aware choice below is what will replace it.
            var keyless = _multiplexer.ServerSelectionStrategy.Select(
                ServerSelectionStrategy.NoSlot, command, flags, allowDisconnected: true);
            if (keyless is not null
                && _topology.IsSelectable(keyless.EndPoint)
                && _endpoints.TryGetValue(keyless.EndPoint, out var already)
                && already.IsConnectedNow)
            {
                return keyless.EndPoint;
            }

            var endpoints = _multiplexer.GetEndPoints();
            if (endpoints.Length == 0) return keyless?.EndPoint;

            // ROLE FIRST, and this is the half that cannot be skipped: without it a write goes to whichever
            // endpoint happened to be dialled, which on a primary/replica pair is the replica as soon as one
            // replica read has been served. The server refuses it, and the pre-send check refuses it sooner
            // (`Command cannot be issued to a replica: BITFIELD`) - which is how it was found.
            if (Choose(endpoints, requirePrimary: !wantsReplica) is { } chosen) return chosen;

            // ...and then without the role filter, because a client connected only to replicas still has to
            // send somewhere: the server's own refusal is a better answer than inventing one here.
            return Choose(endpoints, requirePrimary: false) ?? keyless?.EndPoint ?? endpoints[0];

            EndPoint? Choose(EndPoint[] candidates, bool requirePrimary)
            {
                EndPoint? usable = null;
                foreach (var candidate in candidates)
                {
                    if (!_topology.IsSelectable(candidate)) continue;
                    if (requirePrimary && _topology.RoleOf(candidate) == RespEndpointRole.Replica) continue;

                    // PREFERRING ONE ALREADY DIALLED, for the reason the replica rotation does: this core
                    // connects lazily, so taking the first answer would open a socket for a command an
                    // existing connection could serve.
                    usable ??= candidate;
                    if (_endpoints.TryGetValue(candidate, out var dialled) && dialled.IsConnectedNow) return candidate;
                }

                return usable;
            }
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

            // ...EXCEPT when the redirect points at a node we already know we cannot reach, because there
            // the reconfiguration is what closes a loop. Every reconfiguration ends in EnsureSubscriptions,
            // which re-sends each sharded subscribe; the server - still assigning the slot to the dead node
            // - answers each with -MOVED to it; and each -MOVED asked for another reconfiguration.
            // ReconfigureIfNeeded only coalesces while one is IN FLIGHT, so nothing capped the rate:
            // `RetirementUnderMaintenanceTests` measured ~1,200 reconfigurations a second, and thousands of
            // subscribes queued against the dead node.
            //
            // Nothing is lost by declining. A redirect to an unreachable node cannot be acted on, so
            // re-reading only relearns the same unusable answer - and re-reading BECAUSE a node is
            // unreachable already has an owner with the restraint this lacks:
            // ServerEndPoint.OnRepeatedConnectFailure, rate-limited to ConfigCheckSeconds.
            if (!IsKnownDown(endpoint))
            {
                _multiplexer.ReconfigureIfNeeded(endpoint, false, "MOVED encountered");
            }
        }

        /// <summary>The server redirected somewhere it could not name, so our map is wrong somewhere.</summary>
        /// <remarks>
        /// Marks the map suspect as well as asking the other core to reconfigure: an unroutable redirect is
        /// the server saying it does not know where a slot went either, which is as strong a statement that
        /// our copy is stale as a <c>MOVED</c> is - and the next connection is what acts on it.
        /// </remarks>
        private void OnTopologySuspect()
        {
            _topology.SlotMapSuspect = true;
            _multiplexer.ReconfigureIfNeeded(null, false, "unroutable redirect");
        }

        /// <summary>One executor - and so one socket - per endpoint for RESP2 deliveries, created on demand.</summary>
        private readonly ConcurrentDictionary<EndPoint, RespEndpointExecutor> _subscriptions = new();

        /// <summary>How many sockets exist purely for deliveries; zero unless something subscribed.</summary>
        internal int SubscriptionConnectionCount => _subscriptions.Count;

        /// <summary>
        /// Move a subscriber-mode command off the ordinary connection, when the protocol turned out to
        /// need a connection of its own.
        /// </summary>
        /// <param name="endpoint">The endpoint whose ordinary connection is about to write it.</param>
        /// <param name="operation">The command.</param>
        /// <remarks>
        /// <para>
        /// <b>The negotiated protocol, read at the write.</b> If it settled on RESP3 then sharing is
        /// right and there is nothing to move; if it settled below - or is still unknown, which a
        /// connection about to write has resolved by definition - then subscriptions need their own
        /// socket, and this creates it and hands the command over.
        /// </para>
        /// <para>
        /// The direct port of <c>ServerEndPoint.TryRerouteToSubscriptionBridge</c>, issue #3154's fix, and
        /// the thing that lets a subscription be composed against the ordinary connection at all: without
        /// it the socket has to be chosen pessimistically when the send is built, which is a socket per
        /// endpoint that RESP3 does not need.
        /// </para>
        /// </remarks>
        private bool TryRerouteSubscription(EndPoint endpoint, RespPayloadOperation operation)
        {
            if (_protocols.TryGetValue(endpoint, out var negotiated) && negotiated >= RedisProtocol.Resp3)
            {
                return false; // shared is correct here; nothing to move
            }

            // deliberately not via the "would it share?" question: that consults the same expectation
            // that got this command queued against the ordinary connection in the first place
            var target = _subscriptions.TryGetValue(endpoint, out var existing)
                ? existing
                : _subscriptions.GetOrAdd(endpoint, CreateSubscription(endpoint));

            return target.TryResend(operation);
        }

        /// <summary>Which endpoint a channel's subscription belongs on, after any redirect.</summary>
        /// <param name="channel">The channel or pattern.</param>
        /// <param name="command">The subscribe command, which decides whether a replica is eligible.</param>
        /// <param name="flags">The caller's preference.</param>
        /// <remarks>
        /// <b>Asked AFTER a sharded subscribe rather than before it</b>, because a <c>-MOVED</c> both
        /// moves the subscription and teaches this core where the slot went - <c>OnSlotMoved</c> updates
        /// the map on the way through - so the owner afterwards IS the answer, and no "where did the send
        /// finish" channel back from the executor is needed to learn it. A channel with no slot answers
        /// null: nothing about it was routed by key, so wherever it was aimed is where it is.
        /// </remarks>
        internal EndPoint? EndpointForChannel(in RedisChannel channel, RedisCommand command, CommandFlags flags)
        {
            if (!channel.IsKeyRouted && !channel.IsSharded) return null;

            // ServerSelectionStrategy.HashSlot, not a hand-rolled slot of the channel's own bytes, and the
            // difference is the CHANNEL PREFIX: the server routes by the name it receives, which is the
            // prefixed one, so a slot taken from the caller's unprefixed name names a different node
            // whenever a prefix is configured. It also honours IgnoreChannelPrefix, which is the one case
            // where the two are deliberately the same. ClusterTests.ClusterPubSub(withKeyPrefix: true) is
            // what disagreeing looks like: the subscription lands on one node and is recorded against
            // another.
            var slot = _multiplexer.ServerSelectionStrategy.HashSlot(channel);
            return slot == ServerSelectionStrategy.NoSlot ? null : EndpointForSlot(slot, command, flags);
        }

        /// <summary>Whether the connection a subscription on this endpoint lives on is up.</summary>
        /// <param name="endpoint">The endpoint.</param>
        /// <remarks>
        /// Asks about the socket <see cref="SubscriptionEndpoint"/> would choose, and deliberately does
        /// not create one: this is a question, and asking it must not dial. See
        /// <c>ConnectionMultiplexer.Subscription</c> for why it has to be put to the core that actually
        /// holds the subscription.
        /// </remarks>
        internal bool IsSubscriptionConnected(EndPoint endpoint)
        {
            if (_subscriptions.TryGetValue(endpoint, out var dedicated)) return dedicated.IsConnectedNow;

            return _protocols.TryGetValue(endpoint, out var negotiated)
                && negotiated >= RedisProtocol.Resp3
                && _endpoints.TryGetValue(endpoint, out var interactive)
                && interactive.IsConnectedNow;
        }

        /// <summary>How many sockets this core currently holds to one endpoint.</summary>
        /// <param name="endpoint">The endpoint.</param>
        /// <remarks>
        /// <b>For the tests that count what a SERVER can see.</b> While both cores exist, a command that
        /// travels on this core goes out on a socket the shipped bridges know nothing about, so a test
        /// asserting "this server has N clients" has to add this core's. The shipped answer and the
        /// one-engine answer are the same number; only the transitional state has the extra socket, and
        /// asserting the shipped number through it would be asserting something untrue of the process as
        /// it is actually running.
        /// </remarks>
        internal int ConnectionCount(EndPoint endpoint)
        {
            var count = 0;
            if (_endpoints.TryGetValue(endpoint, out var interactive) && interactive.IsConnectedNow) count++;
            if (_subscriptions.TryGetValue(endpoint, out var subscription) && subscription.IsConnectedNow) count++;
            return count;
        }

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
            // an endpoint that already has one keeps it, whatever the protocol turned out to be: moving
            // later subscriptions to the ordinary connection while earlier ones live here would split one
            // endpoint's subscriptions across two sockets for no benefit
            if (_subscriptions.TryGetValue(endpoint, out var existing)) return existing;

            // KNOWN RESP3, not assumed, and this is the one place that distinction is load-bearing rather
            // than cosmetic. Subscribing is STICKY: under RESP2 a connection that subscribes enters
            // subscriber mode and refuses everything but (un)subscribe, PING and QUIT - so guessing RESP3
            // and guessing wrong does not merely cost a socket, it poisons the ordinary connection for
            // every command that follows. `Resp3DowngradeTests.SubscribeQueuedBeforeDowngradeDoesNotPoisonInteractive`
            // is that hazard written down, and `Resp3HandshakeTests` reports it as
            // "ERR only [P|S][UN]SUBSCRIBE / PING / QUIT allowed in this context (got: 'PUBLISH')".
            //
            // So an unknown protocol takes the safe branch and opens a socket of its own. Under RESP3 that
            // is one socket more than necessary until something has handshaken this endpoint - which
            // ordinary traffic does almost immediately - and it is never WRONG, which the alternative is.
            if (_protocols.TryGetValue(endpoint, out var known) && known >= RedisProtocol.Resp3)
            {
                return Endpoint(endpoint);
            }

            return _subscriptions.GetOrAdd(endpoint, CreateSubscription(endpoint));
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
                .WithExecutor(ServerExecutor(endpoint)));

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
            () => _multiplexer.RawConfig.ReconnectRetryPolicy,
            server: () => ModelledServer(endpoint))
        {
            IsSubscriptionEndpoint = true,
            HeartbeatDriven = true,
        };

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

        /// <summary>This endpoint's connection, as an executor that names no database.</summary>
        /// <param name="endpoint">The endpoint the caller has already chosen.</param>
        /// <remarks>
        /// <b>For <c>IServer</c>, which names a server and no database.</b> Database <c>-1</c> is not
        /// "database zero": it means no <c>SELECT</c> is written, so a keyless server command leaves the
        /// connection's current selection alone - which is the behaviour
        /// <c>ServerExecuteDatabaseTests.CommandsNeedingNoDatabaseLeaveTheSelectionAlone</c> pins. A member
        /// that does need one moves the context with <c>WithDatabase</c>, and gets a view over this same
        /// socket.
        /// <para>
        /// Sharing the socket is the point rather than a saving: <c>IServer</c> on its own connection means
        /// a flush, a scan or a <c>CLIENT INFO</c> races the commands it is meant to describe. That is the
        /// artefact design notes 9b-xi measures.
        /// </para>
        /// </remarks>
        internal RespExecutorBase ServerExecutor(EndPoint endpoint) => Endpoint(endpoint).WithDatabase(-1);

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
            () => _multiplexer.RawConfig.ReconnectRetryPolicy,
            () => _multiplexer.EffectiveCircuitBreaker?.CreateAccumulator(),
            () => OnCircuitBroken(endpoint),
            (command, commandAndKey) => ExceptionFactory.NoConnectionAvailable(
                _multiplexer,
                null,
                _multiplexer.GetServerEndPoint(endpoint, ServerProvenance.Configured, activate: false),
                default,
                command,
                commandAndKey),
            () => _multiplexer.RawConfig.BacklogPolicy?.AbortPendingOnConnectionFailure ?? true,
            () => _multiplexer.TimeoutMilliseconds,
            () => ModelledServer(endpoint))
        {
            RerouteSubscription = operation => TryRerouteSubscription(endpoint, operation),
            HeartbeatDriven = true,
        };

        /// <summary>The <see cref="ServerEndPoint"/> this client models for an endpoint, if it models one.</summary>
        /// <remarks>
        /// <b>Shared with the shipped core on purpose.</b> Beliefs about a server that are not facts about a
        /// socket - whether a maintenance window is open, what it announced, when it closed - are recorded
        /// once, by whichever core saw the notification, and both read the same record. Duplicating them
        /// would make the two cores disagree about the same server, which is the failure mode design notes
        /// section 9 is trying to remove rather than reproduce.
        /// </remarks>
        private ServerEndPoint? ModelledServer(EndPoint endpoint)
            => _multiplexer.GetServerEndPoint(endpoint, ServerProvenance.Configured, activate: false);

        /// <summary>Announce that an endpoint's circuit breaker has judged it unhealthy.</summary>
        /// <param name="endpoint">The endpoint whose breaker tripped.</param>
        /// <remarks>
        /// Raised as an ordinary connection failure, which is what it is from everybody else's point of
        /// view - and specifically what a connection group listens for when it reroutes away from a member.
        /// The shipped core arrives at the same event by a longer road, through
        /// <c>PhysicalBridge.RecordConnectionFailed</c>.
        /// </remarks>
        private void OnCircuitBroken(EndPoint endpoint)
            => _multiplexer.OnConnectionFailed(
                endpoint,
                ConnectionType.Interactive,
                ConnectionFailureType.CircuitBreaker,
                new RedisConnectionException(
                    ConnectionFailureType.CircuitBreaker,
                    CommandFlags.CommandRetryAlways,
                    "The circuit breaker for this endpoint has tripped.",
                    null,
                    CommandStatus.Unknown),
                reconfigure: true,
                physicalName: null);

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

            // the test-only gate, honoured here for the same reason SimulateConnectionFailure is: it names
            // a state of the whole client, and a client that reconnects on a socket the test believes it
            // has forbidden is not testing what the test says. The shipped path checks it in
            // PhysicalConnection.BeginConnectAsync; this is the same check for this core's dial.
            if (!_multiplexer.AllowConnect)
            {
                throw new RedisConnectionException(
                    ConnectionFailureType.InternalFailure,
                    CommandFlags.None,
                    "Aborting (AllowConnect: False)",
                    null,
                    CommandStatus.WaitingToBeSent);
            }

            // SAY WHAT WE ARE DOING, which this core did not. A connect log is the artifact users paste
            // into issues, and the shipped bridge narrates its whole lifecycle into it; a core that opens
            // the sockets silently turns that log into a record of everything EXCEPT the connections.
            // LoggerTests.BasicLoggerConfig measures it bluntly, as a line count that fell by a third once
            // the bridges stopped dialling.
            var logger = _multiplexer.Logger;
            var connectionType = subscription ? ConnectionType.Subscription : ConnectionType.Interactive;
            // the shipped bridge's events and ids, and its name for the connection (`host:port/Type`): a
            // log filter or dashboard built against the shipped core keeps working against this one
            var logName = Format.ToString(endpoint) + "/" + connectionType;
            logger?.LogInformationConnecting(logName);
            logger?.LogInformationBeginConnectAsync(new(endpoint));

            // the shared chain: tunnel, proxy, socket, TLS. This used to be a bare socket here, which
            // silently ignored every one of those - and the TLS I added to it first was a second copy of
            // the shipped logic, which is worse than none: two versions of a security decision, free to
            // drift. DuplexTransport is the boundary; everything below it belongs to the factory.
            var connected = await RespTransportFactory.ConnectAsync(
                endpoint,
                config,
                connectionType,
                _multiplexer.SetAuthSuspect,
                logger,
                cancellationToken).ConfigureAwait(false);

            logger?.LogInformationTransportConnected(logName, connected.IsEncrypted);
            logger?.LogInformationConnected(logName);

            // the configured response pool goes to the connection, not just to the shipped core's reader:
            // every reply this core reads lands in an inbound buffer, and a caller who supplied a pool
            // asked to own the memory the replies live in
            // WEAKLY again, and for the reason `RespClientConnection.Server` spells out: `Follow` is an
            // instance method, so routing a redirect through a lambda held it - and through it this core
            // and the multiplexer - for the connection's whole life. A redirect arriving after the core
            // is gone has nothing to re-send to, so declining to follow is the whole of the answer.
            var coreRef = new WeakReference<RespNewCore>(this);
            var connection = new RespClientConnection(
                connected.Transport,
                (in RespRedirect redirect, RespPayloadOperation operation)
                    => coreRef.TryGetTarget(out var core) && core.Follow(endpoint, in redirect, operation),
                config.IncludeDetailInExceptions,
                config.ResponseBufferPool);
            logger?.LogInformationStartingRead(new(endpoint)); // the connection reads from construction
            var context = new RespDatabaseContext(
                new RespContext(config.CommandMap, database: 0)
                    .WithExecutor(new RespConnectionExecutor(connection, 0)));

            // AUTH only when there is something to authenticate WITH, matching the shipped handshake's
            // `!IsNullOrWhiteSpace` test. The handshake itself treats "" as a legitimate password - that is
            // how a 'nopass' ACL login is expressed, and it is right for a caller who says so explicitly -
            // but ConfigurationOptions carries "" to mean "none configured", so passing it straight through
            // sent AUTH to servers that have no password and answer it with an error.
            var credentials = !string.IsNullOrWhiteSpace(config.User) || !string.IsNullOrWhiteSpace(config.Password);

            // resolved before the handshake rather than after it, so that what the handshake learns is logged
            // against the server as it is learned - the shipped "Auto-configured ..." lines
            var server = _multiplexer.GetServerEndPoint(endpoint, ServerProvenance.Configured, activate: false);

            var result = await RespHandshake.PerformAsync(
                context,
                config.User,
                credentials ? config.Password : null,
                ServerEndPoint.SanitizeClientName(_multiplexer.ClientName),
                database,
                config.Protocol is null or RedisProtocol.Resp3,
                config.TryHello(out _),
                _topology,
                endpoint,
                subscription ? null : _multiplexer.ClientCache,
                _multiplexer.GetFullLibraryName(),
                ServerEndPoint.ClientInfoSanitize(Utils.GetLibVersion()),
                _multiplexer.SetAuthSuspect,
                cancellationToken,
                server: server).ConfigureAwait(false);

            // recorded BEFORE the connection is handed back, for the same reason the topology is: the
            // endpoint executor publishes it and drains its backlog the moment this returns, and a
            // command choosing its spelling from "we have no idea" is the case this exists to avoid
            if (result.Version is { } version) _observed[endpoint] = new RedisFeatures(version);
            _protocols[endpoint] = result.Protocol;

            // the handshake's SELECT is where this connection's database is decided; recording it is what
            // lets a later command for a different one know it has to say so first
            connection.CurrentDatabase = database;
            connection.ConnectionId = result.ConnectionId;
            connection.RemoteAddress = connected.RemoteAddress;

            // which server this reached, so a preamble gate can consult the endpoint's beliefs - a loaded
            // script is server-wide, and ServerEndPoint already tracks that and flushes it when a server's
            // identity changes underneath. Borrowed rather than reimplemented while both cores exist.
            connection.Server = server;

            // RESP2 has no push prefix, so a delivery on this connection is an ordinary array and the only
            // thing marking it as one is that this connection subscribes. Set it nowhere else: on an
            // interactive connection it would start eating replies. Read from what the handshake NEGOTIATED
            // rather than what was configured - a server can answer RESP2 to a RESP3 request.
            connection.DeliversArrays = subscription && result.Protocol < RedisProtocol.Resp3;

            // deliveries arrive here: on the subscription connection under RESP2, and on this one under
            // RESP3, where a push can land on any connection.
            //
            // WEAKLY, and that is a leak rather than a nicety. A subscription connection holds a standing
            // read - that is what waiting for deliveries IS - so the socket is rooted by the IO system for
            // as long as it is open, and a delegate capturing the multiplexer made the socket root the
            // multiplexer too. A caller who abandons a multiplexer without disposing it then never gets it
            // collected: `GarbageCollectionTests.MuxerIsCollected` is written for exactly that caller, and
            // the shipped core passes it while holding a subscription bridge of its own. An ordinary
            // connection hid the problem by having no standing read to be rooted by.
            //
            // A push that arrives after the multiplexer is gone has nowhere to go and nothing to tell, so
            // "not recognised" is the whole of the correct behaviour.
            var muxerRef = new WeakReference<ConnectionMultiplexer>(_multiplexer);
            connection.OnPush = frame => muxerRef.TryGetTarget(out var muxer)
                ? RespPushDispatch.Dispatch(frame, muxer, endpoint)
                : RespOutOfBandResult.NotRecognized;

            // BEFORE any discovery that can PROVOKE a push, which is not a detail. The maintenance opt-in
            // below makes the server replay whatever it retained for this shard, and a push arriving
            // before the dispatcher is wired is dropped as unrecognised - so the replay was lost and the
            // "(catch-up)" line `MaintenanceNotificationTests+Retention` looks for never appeared. The
            // configuration channel taught the same lesson at the other end of this method; this is the
            // same rule applied to the other thing that asks a server to start talking.
            // ...and told what this handshake just learned, which is the direction of travel for D2.8.
            // Today the client's beliefs about a server come from the SHIPPED bridge handshaking its own
            // socket; this core handshakes one too and learns the same facts from it. Publishing them is
            // what lets that other handshake eventually not happen - and in the meantime it corrects the
            // case where this core knows something first, because it dialled first.
            // what discovery learned was logged by the handshake as it learned it; this closes the narrative
            logger?.LogInformationOnEstablishingComplete(new(endpoint));

            if (connection.Server is { } modelled)
            {
                Publish(modelled, in result);

                // ...and the server-wide settings nothing has described yet, which is the next slice of the
                // same move. Interactive only: a subscription connection is not where a client asks
                // questions, and the answers are server-wide so one connection asking is enough.
                if (!subscription)
                {
                    await RespHandshake.DiscoverServerConfigAsync(
                        context,
                        modelled,
                        new RespHandshake.ConnectedTransportFacts(
                            result.Protocol,
                            connected.RemoteAddress,
                            connected.IsEncrypted,
                            requestedResp3: config.Protocol is null or RedisProtocol.Resp3,
                            roleKnown: result.RoleFromHello is not null))
                        .ConfigureAwait(false);
                }
            }

            // A connection that asked for RESP3 and was answered less than that needs a SUBSCRIPTION
            // socket, and needs it now rather than when something next subscribes. The shipped core
            // reaches the same conclusion in the same place - `OnFullyEstablished`'s
            // `else if (SupportsSubscriptions && Protocol > Resp2) Activate(Subscription)` - and for the
            // same reason: subscriptions composed while RESP3 was expected are about to be re-placed, and
            // a re-place that has to dial first loses the race against whatever the caller does next.
            // `Resp3DowngradeTests` measures exactly that race, as `PUBLISH => :0` arriving before the
            // re-subscribe.
            //
            // Not awaited: this is the establish path, so waiting for another connection here would wait
            // behind the one being established.
            //
            // Only when this core actually HOLDS a subscription for the endpoint, which is not a
            // refinement but the whole correctness of it: unconditionally, a socket gets dialled for an
            // endpoint whose subscriptions belong to the shipped bridge, and then re-placed onto it - so
            // the channel ends up subscribed twice and a publish reports two subscribers where the caller
            // asked for one. `Resp3DowngradeTests` measures that too, from the other side, as a RESP2
            // connection carrying both a handshake's `INFO` and a `SUBSCRIBE`.
            //
            // Two reasons to want one, and the second is not a caller's at all: the library's own
            // configuration channel also lives on the subscription socket under RESP2, and nothing else
            // will ever ask for it - a lazily-dialled socket that waits for a subscribe waits for ever
            // when the only subscriber is the thing that rides the socket's own handshake.
            //
            // Still only on a DOWNGRADE (`TryResp3`), which is what this hook is for. A client that asked
            // for RESP2 in the first place has already had its socket dialled by `ActivateServer`, which
            // knew it would need one without having to connect to find out - so dropping that condition
            // just dials a second time. `RespSubscriptionConnectionTests` counts the sockets and says so.
            if (!subscription
                && result.Protocol < RedisProtocol.Resp3
                && config.TryResp3()
                && _multiplexer.RawConfig.CommandMap.IsAvailable(RedisCommand.SUBSCRIBE)
                && (_multiplexer.NewCoreOwnsAnySubscription()
                    || _multiplexer.ConfigurationChangedChannel is not null))
            {
                DialSubscriptionSocket(endpoint);
            }

            // ...and under RESP3 this connection carries the configuration-change broadcast, because there
            // is no subscription connection to carry it. Backported alongside #3254, which fixed exactly
            // this for the shipped core: the channel is how a client is told BY HAND that the topology
            // moved, and left unsubscribed the broadcast reaches nobody. The shipped fix subscribes the
            // bridge's interactive connection; while both cores exist that is enough to act on, but it is
            // the bridge's socket - so this core's own connection needs the same, or the fix comes undone
            // the moment bridges stop being constructed.
            //
            // AFTER the handshake and on the NEGOTIATED protocol, never as part of it: a connection that
            // asked for RESP3 and was answered RESP2 must not be put into subscriber mode, which is the
            // caution the shipped version states too.
            //
            // AFTER OnPush, which is not a detail: under RESP3 a subscribe confirmation IS a push, and a
            // push arriving before the dispatcher is wired is dropped as unrecognised - so subscribing
            // any earlier means waiting for a reply that has already been thrown away, which presents as
            // the connection timing out in its own backlog.
            if (!subscription && result.Protocol >= RedisProtocol.Resp3)
            {
                await SubscribeToConfigurationChannelAsync(context, cancellationToken).ConfigureAwait(false);
            }

            if (!subscription && connection.Server is { } established)
            {
                established.OnNewCoreConnected($"{endpoint} connected on the new core");
            }

            // A connection deliveries arrive on is useless until the subscriptions are on it again, and
            // nothing else was going to notice: the shipped core re-subscribes when its own subscription
            // bridge establishes, so a socket THIS core brought back had no equivalent trigger and the
            // subscriptions stayed off until something unrelated happened to ask.
            //
            // Fire-and-forget, for the reason the shipped caller gives where it does the same thing: this
            // is the establish path, and waiting for a reply here waits behind the connection being
            // established.
            if (subscription)
            {
                try
                {
                    // the records FIRST: this socket is new and carries nothing, so anything still
                    // recorded against this endpoint is stale - and left in place it reads as "already
                    // subscribed" and the re-ensure below does nothing at all
                    _multiplexer.ForgetSubscriptionsOn(endpoint);
                    _multiplexer.EnsureSubscriptions(CommandFlags.FireAndForget);

                    // ...and the configuration-change broadcast, which under RESP2 belongs HERE rather
                    // than on the ordinary connection - subscribing it there would put the connection
                    // carrying ordinary commands into subscriber mode. The shipped core does exactly this
                    // and in exactly this position: the last step of the SUBSCRIPTION bridge's handshake
                    // (`ServerEndPoint.WriteDirectOrQueueFireAndForget`'s connType check), with the same
                    // note that nothing ordinary can follow it. So a bridge that stops being constructed
                    // takes the client's only way of hearing "the topology moved" with it unless this
                    // does the same thing - `ConfigurationChannelUnitTests` reads that as "the
                    // configuration channel has no subscriber".
                    await SubscribeToConfigurationChannelAsync(context, cancellationToken)
                        .ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    // best efforts: a subscription that cannot be re-established must not fail the dial,
                    // or nothing on this connection works either
                    _multiplexer.OnInternalError(ex);
                }
                finally
                {
                    // whatever happened, this is the moment anything waiting on the re-place is waiting
                    // for - see `SubscriptionsSettling`
                    SubscriptionsSettled(endpoint);
                }
            }

            return connection;
        }

        /// <summary>Dial a subscription socket for the library's own configuration channel.</summary>
        /// <param name="endpoint">The endpoint being activated.</param>
        /// <remarks>
        /// <para>
        /// <b>Because the configuration channel is subscribed by CONNECTING, not by anyone asking.</b>
        /// Under RESP2 it lives on the subscription socket - subscribing it on the ordinary connection
        /// would put the connection carrying ordinary commands into subscriber mode - and this core dials
        /// that socket lazily, when something subscribes. Nothing ever does: the only subscriber is the
        /// library itself, on the socket's own establish. So a client that never touches pub/sub or a
        /// database never hears "the topology moved" at all.
        /// </para>
        /// <para>
        /// <c>ConfigurationChannelUnitTests.TheLibrarysOwnBroadcastIsHeard</c> is exactly that client: it
        /// only ever calls <c>ReplicaOfAsync</c>, which is still a shipped <c>Message</c> on the
        /// interactive bridge, so this core had no reason to connect and the broadcast reached nobody.
        /// </para>
        /// <para>
        /// Conditioned on <c>KnowOrAssumeResp3</c> by its caller, which is the same question the shipped
        /// core asks in the same place before activating its subscription bridge - so an endpoint that
        /// turns out to speak RESP3 after all is left holding a spare subscription socket, exactly as the
        /// shipped core would have been left holding a spare bridge.
        /// </para>
        /// <para>
        /// <b>This has a known cost, recorded in design notes 9g rather than hidden:</b> the socket it
        /// opens roots the multiplexer, so a multiplexer that is abandoned without being disposed is no
        /// longer collected (<c>GarbageCollectionTests.MuxerIsCollected</c>). Kept anyway, because
        /// withdrawing it loses the configuration channel entirely under RESP2 - six failing tests
        /// against two - but it is not finished.
        /// </para>
        /// </remarks>
        internal void DialSubscriptionSocketForConfigurationChannel(EndPoint endpoint)
        {
            if (_multiplexer.ConfigurationChangedChannel is null
                || !_multiplexer.RawConfig.CommandMap.IsAvailable(RedisCommand.SUBSCRIBE))
            {
                return;
            }

            // deliberately NOT holding publishes: this dial exists for the library's own channel, and a
            // caller's publish has no reason to wait for it - see `SubscriptionsSettling`
            DialSubscriptionSocket(endpoint, holdPublishes: false);
        }

        /// <summary>Start this endpoint's ordinary connection, without waiting for it.</summary>
        /// <param name="endpoint">The endpoint to dial.</param>
        /// <remarks>
        /// Called from <c>ReconfigureAsync</c>'s per-endpoint loop, which is the first moment the endpoint
        /// is known. Not awaited: that loop then waits on every endpoint's availability task with one
        /// shared budget, and awaiting here would serialise what it deliberately runs in parallel.
        /// </remarks>
        internal void DialEndpointSoon(EndPoint endpoint)
            => ThreadPool.QueueUserWorkItem(
                static async state =>
                {
                    var dial = (SubscriptionDial)state!;
                    try
                    {
                        await dial.Core.Endpoint(dial.Endpoint).ConnectNowAsync(CancellationToken.None)
                            .ConfigureAwait(false);
                    }
                    catch (Exception ex)
                    {
                        dial.Core._multiplexer.OnInternalError(ex, dial.Endpoint);
                    }
                },
                new SubscriptionDial(this, endpoint, holdPublishes: false));

        /// <summary>Start this endpoint's subscription connection, without waiting for it.</summary>
        /// <param name="endpoint">The endpoint whose deliveries now need a socket of their own.</param>
        /// <param name="holdPublishes">
        /// Whether a publish should wait for this dial to place its subscriptions; see
        /// <see cref="SubscriptionsSettling"/> for why only the downgrade re-place says yes.
        /// </param>
        /// <remarks>
        /// <b>Fire-and-forget on the pool, never inline.</b> This runs while another connection is being
        /// established, and dialling one connection from inside another's establish path would wait
        /// behind it - which is the same reason the shipped core's re-subscribe is fire-and-forget where
        /// it sits. Failure is ordinary: the socket will be dialled by the next subscribe if it is still
        /// wanted.
        /// </remarks>
        private void DialSubscriptionSocket(EndPoint endpoint, bool holdPublishes = true)
        {
            // recorded BEFORE the dial starts, not inside it: the point of the record is that a publish
            // issued between here and the re-place can see that one is coming, and a record written from
            // the worker is written too late to be seen by the thing it exists to hold back.
            if (holdPublishes)
            {
                _settling[endpoint] = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            }

            ThreadPool.QueueUserWorkItem(
                static async state =>
                {
                    var dial = (SubscriptionDial)state!;
                    var core = dial.Core;
                    var ep = dial.Endpoint;
                    try
                    {
                        await core.SubscriptionEndpoint(ep).ConnectNowAsync(CancellationToken.None)
                            .ConfigureAwait(false);

                        // NOT released here, even though this is where the dial finishes: the connect
                        // completing is not the subscriptions being back, and measurably so - a socket
                        // this core had already opened once returns from here long before the replacement
                        // has run its establish hook. The hook releases it, by calling `SubscriptionsSettled`
                        // once the re-place has gone out; this only bounds the wait, so that a replacement
                        // which never arrives cannot hold a publish for ever.
                        //
                        // ONLY when something is actually waiting. A timer armed after every dial keeps
                        // this state - and through it the core and the multiplexer - reachable for the
                        // whole timeout, which is a rooted multiplexer per dial and six of them per
                        // cluster connect. `GarbageCollectionTests.MuxerIsCollected` says so directly.
                        if (dial.HoldsPublishes)
                        {
                            await Task.Delay(core._multiplexer.TimeoutMilliseconds).ConfigureAwait(false);
                        }
                    }
                    catch (Exception ex)
                    {
                        core._multiplexer.OnInternalError(ex, ep);
                    }
                    finally
                    {
                        // a dial that FAILED must still release whatever waits on it: a publish blocked
                        // for ever on a socket that is never coming is worse than one reaching nobody
                        core.SubscriptionsSettled(ep);
                    }
                },
                new SubscriptionDial(this, endpoint, holdPublishes));
        }

        /// <summary>The state a queued dial needs, as a class because this library cannot use tuples.</summary>
        /// <remarks>
        /// <c>System.ValueTuple</c> is not referenced - net461 would need the package, and
        /// <c>SanityChecks.ValueTupleNotReferenced</c> fails the build's intent if one creeps in. A single
        /// allocation per dial, and a dial is already opening a socket.
        /// </remarks>
        private sealed class SubscriptionDial(RespNewCore core, EndPoint endpoint, bool holdPublishes)
        {
            public RespNewCore Core => core;

            public EndPoint Endpoint => endpoint;

            public bool HoldsPublishes => holdPublishes;
        }

        private readonly ConcurrentDictionary<EndPoint, TaskCompletionSource<bool>> _settling = new();

        /// <summary>Release anything held back waiting for this endpoint's subscriptions.</summary>
        /// <param name="endpoint">The endpoint whose subscriptions are back on the wire.</param>
        private void SubscriptionsSettled(EndPoint endpoint)
        {
            if (_settling.TryRemove(endpoint, out var settled)) settled.TrySetResult(true);
        }

        /// <summary>Subscription re-placements still in flight, which a publish must not overtake.</summary>
        /// <remarks>
        /// <para>
        /// <b>The shipped core gets this ordering by accident and this one has to arrange it.</b> There,
        /// a connection failure drops the interactive and subscription bridges together and both
        /// reconnect on the same heartbeat, so by the time ordinary commands flow again the subscription
        /// bridge has had the same wall-clock to come back. Here the subscription socket is dialled
        /// lazily, and the need for one is only known once the handshake reports a protocol below the
        /// one asked for - which is strictly AFTER the ordinary connection is already warm. A publish
        /// then goes out immediately on the warm socket while the subscription socket is still shaking
        /// hands, and reaches nobody.
        /// </para>
        /// <para>
        /// Normally empty, so normally free; it holds an entry only between a downgrade being observed
        /// and that endpoint's subscriptions being back on the wire.
        /// </para>
        /// <para>
        /// <b>Only the downgrade re-place records one</b>, and that scoping is not tidiness. The dial for
        /// the library's own configuration channel runs at ACTIVATION, once per node - so in a cluster
        /// every publish waited on six of them, and any one that was slow to establish held the lot.
        /// Measured as a five-second publish against the shipped path's eight hundred milliseconds, and
        /// as `ClusterShardedTests.KeepSubscribedThroughSlotMigrationAsync` running out its own timeout.
        /// A caller's publish has no reason to wait for the library's channel: nothing it does depends
        /// on that subscription being in place.
        /// </para>
        /// </remarks>
        internal Task SubscriptionsSettling()
        {
            if (_settling.IsEmpty) return Task.CompletedTask;

            List<Task>? pending = null;
            foreach (var pair in _settling)
            {
                if (!pair.Value.Task.IsCompleted) (pending ??= new()).Add(pair.Value.Task);
            }

            return pending is null ? Task.CompletedTask : Task.WhenAll(pending);
        }

        /// <summary>Subscribe this connection to the configuration-change broadcast.</summary>
        /// <param name="context">A context over the connection to subscribe on.</param>
        /// <param name="cancellationToken">Cancels the request.</param>
        /// <remarks>
        /// <para>
        /// <b>The library's own subscription, not a caller's</b>, so it deliberately does not go through
        /// the subscription registry - there are no handlers to register and nothing should be able to
        /// unsubscribe it. The shipped core writes it straight to the bridge for the same reason; the
        /// delivery is recognised by <c>RespPushDispatch</c>, which checks for the configuration channel
        /// before handing anything to pub/sub handlers.
        /// </para>
        /// <para>
        /// <b>With the channel prefix applied</b>, because that is how it is subscribed and published
        /// everywhere else - the one thing #3254's third part had to fix was the library's own broadcasts
        /// passing a raw value and so reaching a name nobody was subscribed to.
        /// </para>
        /// </remarks>
        private async Task SubscribeToConfigurationChannelAsync(
            RespDatabaseContext context, CancellationToken cancellationToken)
        {
            var channel = _multiplexer.ConfigurationChangedChannel;
            if (channel is null || !context.Raw.CommandMap.IsAvailable(RedisCommand.SUBSCRIBE)) return;

            var prefixed = new RespPubSub(
                context.Raw.AppendChannelPrefix(_multiplexer.RawConfig.ChannelPrefix));

            try
            {
                await prefixed.SubscribeAsync(RedisChannel.Literal(channel)).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                // best efforts: a server that refuses SUBSCRIBE still serves commands, and failing the
                // dial over a broadcast nobody may ever send would be a worse trade
                _multiplexer.OnInternalError(ex);
            }
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

        /// <summary>Decide what to do about a redirect that arrived on one endpoint's connection.</summary>
        /// <param name="from">The endpoint whose connection received it.</param>
        /// <param name="redirect">What the server said.</param>
        /// <param name="operation">The command that was redirected; not yet completed.</param>
        /// <remarks>
        /// <b>A <c>MOVED</c> pointing at the endpoint we are already talking to is not about the slot map
        /// at all.</b> It happens when a name resolves to something that has changed underneath - DNS, a
        /// load balancer, a proxy - so the address is still right and the CONNECTION is stale. Re-sending
        /// on the same socket gets the same answer forever, which is exactly how it presented: the second
        /// <c>MOVED</c> surfaced to the caller as an error, because a command is only allowed to follow one.
        /// <para>
        /// The shipped core marks the bridge for reconnect and lets its reader loop act on it. This drops
        /// the connection and re-queues the command, which then goes out on the replacement.
        /// </para>
        /// </remarks>
        private bool Follow(EndPoint from, in RespRedirect redirect, RespPayloadOperation operation)
        {
            if (redirect.IsMoved && redirect.Endpoint is { } target && Equals(target, from))
            {
                ReconnectAndResend(from, operation);
                return true; // ours now; the operation must not be completed with the redirect
            }

            // A SUBSCRIBE that would follow a redirect to a node we know is down is declined rather than
            // queued there. Queueing is right for an ordinary command - it waits for the reconnect, which
            // is the backlog's whole job - but a subscription queued on a dead node is a subscription nobody
            // will deliver on, and the resubscribe machinery that sent it will send it again. Left alone,
            // that is how thousands came to be outstanding against one unreachable node.
            //
            // Declining fails the subscribe, which leaves it unplaced; that is the truth, and it is what
            // lets the next pass place it somewhere live. Deliberately NOT applied to ordinary commands:
            // IsKnownDown is also true for the length of a brief reconnect, and turning "wait for it" into
            // "fail now" for those is a behaviour change worth deciding on its own.
            if (redirect.Endpoint is { } redirectedTo
                && IsSubscriptionCommand(operation.Command)
                && IsKnownDown(redirectedTo))
            {
                return false;
            }

            return _router.TryFollowRedirect(in redirect, operation);
        }

        private static bool IsSubscriptionCommand(RedisCommand command)
            => command is RedisCommand.SUBSCRIBE or RedisCommand.PSUBSCRIBE or RedisCommand.SSUBSCRIBE
                or RedisCommand.UNSUBSCRIBE or RedisCommand.PUNSUBSCRIBE or RedisCommand.SUNSUBSCRIBE;

        /// <summary>Replace an endpoint's connection, then send the command again on the new one.</summary>
        /// <param name="endpoint">The endpoint to reconnect.</param>
        /// <param name="operation">The command to re-send once it has.</param>
        /// <remarks>
        /// <b>On the pool, never here.</b> This is called from the read loop of the very connection being
        /// dropped, and disposing a connection from inside its own loop is a self-join - which an earlier
        /// attempt at this found the hard way, and is why it was reverted rather than patched. The circuit
        /// breaker hands its teardown off for the same reason.
        /// </remarks>
        private void ReconnectAndResend(EndPoint endpoint, RespPayloadOperation operation)
            => ThreadPool.QueueUserWorkItem(
                static state => ((Reconnect)state!).Run(),
                new Reconnect(this, endpoint, operation));

        /// <summary>The work a same-endpoint <c>MOVED</c> hands to the pool.</summary>
        /// <remarks>
        /// A named type rather than a tuple: <c>System.ValueTuple</c> is not referenced by this assembly -
        /// <c>SanityChecks.ValueTupleNotReferenced</c> enforces it, and caught this - because the
        /// down-level targets would take a package dependency for it.
        /// </remarks>
        private sealed class Reconnect(RespNewCore core, EndPoint endpoint, RespPayloadOperation operation)
        {
            internal void Run()
            {
                try
                {
                    // immediately: the drop was deliberate and the remedy IS the reconnect, so the backoff
                    // meant for a refusing server would only strand the re-sent command
                    core.Endpoint(endpoint).DropConnection(reconnectImmediately: true);

                    // backlogged behind that reconnect and written on whatever replaces it.
                    // HasFollowedRedirect is already set, so a second MOVED stands as the error it is
                    // rather than looping.
                    var database = operation.Database < 0 ? core._defaultDatabase : operation.Database;
                    if (!core.Executor(database, endpoint).TryResend(operation))
                    {
                        operation.EnsureFaulted(operation.Flags, null);
                    }
                }
                catch (Exception ex)
                {
                    operation.EnsureFaulted(operation.Flags, ex);
                }
            }
        }

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

        /// <summary>Bring connections up, so the deployment has described itself before anybody asks.</summary>
        /// <param name="mode">How much to open; <see cref="ConnectMode.Lazy"/> opens nothing.</param>
        /// <param name="cancellationToken">Abandons the wait.</param>
        /// <returns>Whether an endpoint came up.</returns>
        /// <remarks>
        /// <para>
        /// <b>Under <see cref="ConnectMode.Discover"/>: one, not all.</b> The configured endpoints are dial
        /// targets and need no discovering; what needs discovering is where the slots live and which
        /// servers are replicas, and a single connection's handshake answers both for the WHOLE deployment
        /// (<c>CLUSTER SLOTS</c> names every node, <c>ROLE</c> names the other side of a pair). So a
        /// hundred-node cluster still opens one socket here and dials the rest when traffic asks for them.
        /// </para>
        /// <para>
        /// <b>What it buys is timing, not information.</b> These probes already rode on whichever
        /// connection happened to come up first; doing it during <c>ConnectAsync</c> is what lets
        /// <c>IsConnected</c> and <c>IdentifyEndpoint</c> - which answer without sending anything - be
        /// answered at all before the first command. That was what stopped the routing fallbacks from
        /// being removed; see section 9d.
        /// </para>
        /// <para>
        /// Endpoints are tried in order and the first success wins, because a configured endpoint that is
        /// down is ordinary. Failure is reported rather than thrown: the shipped core's own verdict still
        /// decides whether the multiplexer connected, and disagreeing with it here would be a second
        /// opinion nobody asked for.
        /// </para>
        /// </remarks>
        internal async Task<bool> ConnectEagerlyAsync(
            ConnectMode mode = ConnectMode.Discover, CancellationToken cancellationToken = default)
        {
            if (mode == ConnectMode.Lazy) return false;

            var any = false;
            foreach (var endpoint in _multiplexer.GetEndPoints())
            {
                // NOT the inert ones. Discovery registers a cluster node that serves no slots so that it stays
                // addressable, and deliberately does not dial it - nothing will ever be routed there, and a
                // socket to it is pure cost. Eager means "open everything we will use", and walking every
                // KNOWN endpoint instead quietly dialled those too, which
                // ClusterTopologyUnitTests.SlotLessNodesAreKnownButNotConnected caught as a node that was
                // connected without anyone having asked for it. A configured endpoint is always dialled,
                // whatever it serves: it is a seed the caller named.
                if (mode == ConnectMode.Eager
                    && _multiplexer.TryResolveServerEndPoint(endpoint) is { Provenance: ServerProvenance.ClusterTopology }
                    && !_topology.ServesAnySlot(endpoint))
                {
                    continue;
                }

                try
                {
                    await Endpoint(endpoint).ConnectNowAsync(cancellationToken).ConfigureAwait(false);
                    if (_endpoints.TryGetValue(endpoint, out var up) && up.IsConnectedNow)
                    {
                        any = true;

                        // Discover stops at the first success, because that connection's handshake has
                        // already described the whole deployment; Eager goes on to open the rest
                        if (mode == ConnectMode.Discover) return true;
                    }
                }
                catch (Exception ex)
                {
                    // an endpoint that will not come up is ordinary; the next one is tried, and if none
                    // does the caller is told rather than thrown at
                    _multiplexer.OnInternalError(ex, endpoint);
                }

                if (cancellationToken.IsCancellationRequested) break;
            }

            return any;
        }

        /// <summary>How many commands are waiting for a connection to this endpoint.</summary>
        /// <param name="endpoint">The endpoint.</param>
        /// <param name="connectionType">Which of its connections to count.</param>
        /// <remarks>
        /// <b>Reported so the client's own diagnostics are not wrong about it.</b> A backlog exists to be
        /// visible - it is what <c>GetCounters</c>, <c>GetStatus</c> and the timeout exception text are for
        /// - and under the engine flag the commands queue HERE while the shipped bridge, which is what
        /// those surfaces read, queues nothing and reports zero. A client waiting on a backlog it is told
        /// is empty is the diagnostic failing exactly when it is needed.
        /// </remarks>
        internal int BacklogCount(EndPoint endpoint, ConnectionType connectionType)
        {
            var map = connectionType == ConnectionType.Subscription ? _subscriptions : _endpoints;
            return endpoint is not null && map.TryGetValue(endpoint, out var executor) ? executor.BacklogCount : 0;
        }

        /// <summary>Whether this core has a live connection to an endpoint.</summary>
        /// <param name="endpoint">The endpoint.</param>
        /// <remarks>
        /// <b>Asked by <c>IServer.IsConnected</c>, which is a question about the client and not about a
        /// bridge.</b> Under the engine flag the connection carrying this endpoint's commands is this core's,
        /// so a surface that reported only the shipped bridge answered "not connected" about a server it was
        /// actively talking to - which <c>ClusterTopologyUnitTests</c> reads immediately after a successful
        /// ping.
        /// </remarks>
        internal bool IsConnected(EndPoint endpoint)
            => endpoint is not null
                && ((_endpoints.TryGetValue(endpoint, out var interactive) && interactive.IsConnectedNow)
                    || (_subscriptions.TryGetValue(endpoint, out var subscription) && subscription.IsConnectedNow));

        /// <summary>Whether this core's slot map says an endpoint serves a key-routed channel's slot.</summary>
        /// <param name="endpoint">The endpoint the channel is subscribed on.</param>
        /// <param name="channel">The channel.</param>
        /// <returns>True or false where the map has a view of that slot; null where it has none.</returns>
        /// <remarks>
        /// <b>The question <c>Subscription.RemoveIncorrectRouting</c> asks, answered from the map that is
        /// actually current.</b> It asked the shipped selector, whose map is only refreshed by a full
        /// reconfiguration - so in the window after a slot migration it still named the OLD owner, called
        /// the subscription on the NEW owner incorrect, and unsubscribed it. This core's map is corrected
        /// by the very <c>-MOVED</c> that announced the migration (see <see cref="OnSlotMoved"/>), so it has
        /// already caught up by the time anybody asks.
        /// <para>
        /// That mistake was hidden until today by a second one that undid it: the reply to that unsubscribe
        /// was misread as an unsolicited <c>sunsubscribe</c> and triggered a resubscribe on the same node.
        /// Correcting the misread (<c>RespClientConnection.OnOutOfBand</c>) exposed this, as
        /// <c>ClusterShardedTests.KeepSubscribedThroughSlotMigrationAsync</c> failing every time.
        /// </para>
        /// <para>
        /// A replica of the slot counts as serving it, since a replica-routed subscription is legitimately
        /// placed there - which is the shipped check's "suitable" rather than "correct" as well.
        /// </para>
        /// </remarks>
        internal bool? CanServe(EndPoint endpoint, in RedisChannel channel)
        {
            if (endpoint is null || !_topology.HasSlotMap) return null;

            var slot = _multiplexer.ServerSelectionStrategy.HashSlot(channel);
            if (slot < 0 || _topology.Owners(slot) is not { } owners) return null;

            if (Equals(owners.Primary, endpoint)) return true;
            foreach (var replica in owners.Replicas)
            {
                if (Equals(replica, endpoint)) return true;
            }

            return false;
        }

        /// <summary>Whether this core holds any live ordinary connection at all.</summary>
        /// <remarks>
        /// The question a reconfiguration asks before refreshing the topology: this core can only re-read a
        /// deployment over a connection it has, and under the flag alone it dials lazily - so a deployment
        /// nobody has sent a command to has nothing open here, and the shipped sweep is what answers.
        /// </remarks>
        internal bool HasAnyInteractiveConnection
        {
            get
            {
                foreach (var pair in _endpoints)
                {
                    if (pair.Value.IsConnectedNow) return true;
                }

                return false;
            }
        }

        /// <summary>Whether this core's ORDINARY connection to an endpoint is live.</summary>
        /// <param name="endpoint">The endpoint.</param>
        /// <remarks>
        /// Narrower than <see cref="IsConnected"/> on purpose. That one answers "can this core reach the
        /// server at all", which is what a caller wants; this one answers about one of the two connections,
        /// which is what a diagnostic reporting per-connection state needs - a subscription socket being up
        /// says nothing about whether commands have somewhere to go.
        /// </remarks>
        internal bool IsInteractiveConnected(EndPoint endpoint)
            => endpoint is not null
                && _endpoints.TryGetValue(endpoint, out var interactive)
                && interactive.IsConnectedNow;

        /// <summary>What the server calls this core's connection to an endpoint.</summary>
        /// <param name="endpoint">The endpoint.</param>
        /// <param name="connectionType">Which of its connections to name.</param>
        /// <remarks><inheritdoc cref="RespClientConnection.ConnectionId" path="/remarks"/></remarks>
        internal long? ConnectionId(EndPoint endpoint, ConnectionType connectionType)
        {
            if (endpoint is null) return null;

            // The subscription leg falls back to the ordinary connection when it IS the ordinary
            // connection, which under RESP3 it is - there is no second socket, so asking for "the
            // subscription connection's id" and being told null would describe a connection that does
            // not exist rather than the one carrying the deliveries. `IsSubscriptionConnected` resolves
            // it the same way and for the same reason.
            if (connectionType == ConnectionType.Subscription)
            {
                if (_subscriptions.TryGetValue(endpoint, out var dedicated)) return dedicated.ConnectionId;
                return _protocols.TryGetValue(endpoint, out var negotiated) && negotiated >= RedisProtocol.Resp3
                    ? ConnectionId(endpoint, ConnectionType.Interactive)
                    : null;
            }

            return _endpoints.TryGetValue(endpoint, out var interactive) ? interactive.ConnectionId : null;
        }

        /// <summary>Whether a caller is waiting on either of this core's connections to an endpoint.</summary>
        /// <param name="endpoint">The endpoint.</param>
        /// <remarks><inheritdoc cref="RespEndpointExecutor.HasCallerWork" path="/remarks"/></remarks>
        internal bool HasCallerWork(EndPoint endpoint)
        {
            if (endpoint is null) return false;

            return (_endpoints.TryGetValue(endpoint, out var interactive) && interactive.HasCallerWork())
                || (_subscriptions.TryGetValue(endpoint, out var subscription) && subscription.HasCallerWork());
        }

        /// <summary>Replace this core's connections to an endpoint, because it is being moved.</summary>
        /// <param name="endpoint">The endpoint being handed off.</param>
        /// <returns>Whether there was anything to replace.</returns>
        /// <remarks>
        /// <b>The point of a handoff is to stop using a connection before the server closes it</b>, and
        /// under the engine flag the connection in question is this core's - so a handoff that recycled
        /// only the shipped bridges replaced the sockets nobody was using and left the ones carrying
        /// commands to be cut mid-flight, which is the whole failure mode the feature exists to avoid.
        /// <para>
        /// Reconnecting immediately rather than lazily: the endpoint is still the one we route to until
        /// the topology says otherwise, so the next command should find a connection rather than pay the
        /// dial itself.
        /// </para>
        /// </remarks>
        internal bool RecycleConnections(EndPoint endpoint)
        {
            if (endpoint is null) return false;

            var recycled = false;
            if (_endpoints.TryGetValue(endpoint, out var interactive)
                && interactive.DropConnection(reconnectImmediately: true, wasRequested: true))
            {
                Report(endpoint, ConnectionType.Interactive);
                recycled = true;
            }

            if (_subscriptions.TryGetValue(endpoint, out var subscription)
                && subscription.DropConnection(reconnectImmediately: true, wasRequested: true))
            {
                Report(endpoint, ConnectionType.Subscription);
                recycled = true;
            }

            return recycled;

            // REPORTED, not dropped quietly, and that is the half a handoff is judged on: the replacement
            // raises ConnectionRestored, so a silent drop leaves a consumer tracking connection state with
            // an unpaired restore - and `MaintenanceHandoff` is what tells them it was deliberate rather
            // than a fault. `MaintenanceNotificationTests.MovingRecyclesTheConnectionBeforeTheServerCloses`
            // asserts exactly that, in those words.
            void Report(EndPoint reported, ConnectionType connectionType)
                => _multiplexer.OnConnectionFailed(
                    reported,
                    connectionType,
                    ConnectionFailureType.MaintenanceHandoff,
                    new RedisConnectionException(
                        ConnectionFailureType.MaintenanceHandoff,
                        CommandFlags.None,
                        "The connection was replaced for a maintenance handoff.",
                        innerException: null),
                    reconfigure: false,
                    physicalName: null);
        }

        /// <summary>The address this core's connection to an endpoint actually reached.</summary>
        /// <param name="endpoint">The endpoint.</param>
        /// <remarks><inheritdoc cref="RespClientConnection.RemoteAddress" path="/remarks"/></remarks>
        internal System.Net.IPAddress? RemoteAddress(EndPoint endpoint)
            => endpoint is not null && _endpoints.TryGetValue(endpoint, out var executor)
                ? executor.RemoteAddress
                : null;

        /// <summary>How many operations this core has run against an endpoint.</summary>
        /// <param name="endpoint">The endpoint.</param>
        /// <remarks>
        /// <b>Asked by <c>ServerEndPoint.OperationCount</c>, which sums bridges and so reported zero for a
        /// server this core had been talking to all along.</b> It is the quick path beside
        /// <see cref="AddCounters"/>, and it needed the same treatment for the same reason - most visibly
        /// where a test asks whether the heartbeat is doing anything at all
        /// (<c>ConfigTests.TestManualHeartbeat</c>), since under the flag the only thing keeping a
        /// connection alive is this core's own keep-alive.
        /// </remarks>
        internal long OperationCount(EndPoint endpoint)
        {
            if (endpoint is null) return 0;

            long count = 0;
            if (_endpoints.TryGetValue(endpoint, out var interactive)) count += interactive.OperationCount;
            if (_subscriptions.TryGetValue(endpoint, out var subscription)) count += subscription.OperationCount;
            return count;
        }

        /// <summary>Fold an endpoint's counters into a snapshot the shipped surface reports.</summary>
        /// <param name="endpoint">The endpoint.</param>
        /// <param name="connectionType">Which of its connections to count.</param>
        /// <param name="counters">The snapshot to add to.</param>
        /// <remarks><inheritdoc cref="BacklogCount" path="/remarks"/></remarks>
        internal void AddCounters(EndPoint endpoint, ConnectionType connectionType, ConnectionCounters counters)
        {
            var map = connectionType == ConnectionType.Subscription ? _subscriptions : _endpoints;
            if (endpoint is not null && map.TryGetValue(endpoint, out var executor)) executor.AddCounters(counters);
        }

        /// <summary>This core's state for an endpoint, or null when it has no executor for it.</summary>
        /// <param name="endpoint">The endpoint.</param>
        /// <param name="connectionType">Which of its connections to describe.</param>
        /// <remarks>
        /// <inheritdoc cref="BacklogCount" path="/remarks"/>
        /// </remarks>
        internal PhysicalBridge.BridgeStatus? ConnectionStatus(EndPoint endpoint, ConnectionType connectionType)
        {
            var map = connectionType == ConnectionType.Subscription ? _subscriptions : _endpoints;
            return endpoint is not null && map.TryGetValue(endpoint, out var executor) ? executor.GetStatus() : null;
        }

        /// <summary>What connecting to an endpoint last failed with, as this core saw it.</summary>
        /// <param name="endpoint">The endpoint.</param>
        /// <param name="connectionType">Which of its connections to ask about.</param>
        internal RedisConnectionException? LastConnectFault(EndPoint endpoint, ConnectionType connectionType)
        {
            var map = connectionType == ConnectionType.Subscription ? _subscriptions : _endpoints;
            return endpoint is not null && map.TryGetValue(endpoint, out var executor) ? executor.LastConnectFault : null;
        }

        /// <summary>Re-announce the library name on every connection this core already has.</summary>
        /// <param name="libraryName">The name to report, suffixes included.</param>
        /// <remarks>
        /// <b>The other half of <c>ConnectionMultiplexer.AddLibraryNameSuffix</c>.</b> A suffix added after
        /// connecting has to reach the connections that are already up, and that retro-fix goes through
        /// <c>IServer.Execute</c> - which reaches whichever core <c>IServer</c> is on and no other. While
        /// both cores exist one of them is always missed, so this is asked directly.
        /// <para>
        /// Fire-and-forget, and every failure swallowed: the name is a diagnostic, and a connection is not
        /// worth losing over one. The subscription connections are included because <c>CLIENT LIST</c> shows
        /// them too, and an unnamed one there is exactly as unhelpful.
        /// </para>
        /// </remarks>
        internal void SetLibraryName(string libraryName)
        {
            if (string.IsNullOrWhiteSpace(libraryName)) return;
            if (!_multiplexer.RawConfig.CommandMap.IsAvailable(RedisCommand.CLIENT)) return;

            foreach (var pair in _endpoints) Announce(pair.Key, pair.Value);
            foreach (var pair in _subscriptions) Announce(pair.Key, pair.Value);

            void Announce(EndPoint endpoint, RespEndpointExecutor executor)
            {
                if (!executor.IsConnectedNow) return;
                try
                {
                    var context = new RespContext(_multiplexer.RawConfig.CommandMap, database: -1)
                        .WithExecutor(executor.WithDatabase(-1));
                    _ = context.SendAsync(
                        $"{RedisCommand.CLIENT}{RedisLiterals.SETINFO}{RedisLiterals.lib_name}{(RedisValue)libraryName}",
                        CommandFlags.FireAndForget);
                }
                catch
                {
                    // best efforts, as the shipped retro-fix is; see the remarks
                }
            }
        }

        /// <summary>Told that an endpoint may or may not be chosen for new work.</summary>
        /// <param name="endpoint">The endpoint.</param>
        /// <param name="selectable">Whether it may be chosen.</param>
        /// <remarks>See <c>RespTopology.IsSelectable</c> for why this is pushed rather than discovered.</remarks>
        internal void OnSelectable(EndPoint endpoint, bool selectable) => _topology.OnSelectable(endpoint, selectable);

        /// <summary>Tell the client what this core's own handshake observed about a server.</summary>
        /// <param name="server">The modelled server.</param>
        /// <param name="result">What the handshake settled.</param>
        /// <remarks>
        /// <b>Only what was actually determined.</b> <c>ServerType</c> has no "unknown" value, so a
        /// handshake that could not tell reports <c>Standalone</c> - see
        /// <see cref="RespHandshakeResult.KnowsServerType"/> - and publishing that would demote a cluster
        /// on the strength of a question nobody answered.
        /// <para>
        /// <b>And never a demotion of a SENTINEL or a proxy.</b> Those are decided by configuration and
        /// discovery rather than read off a connection: when <c>HELLO</c> is unavailable the fallback is
        /// <c>CLUSTER INFO</c>, which can only distinguish cluster from not-cluster, so it would report a
        /// sentinel as standalone. The guard is not defensive tidiness - it is the difference between
        /// correcting a belief and overwriting one held for a better reason.
        /// </para>
        /// </remarks>
        private void Publish(ServerEndPoint server, in RespHandshakeResult result)
        {
            if (result.Version is { } version) server.Version = version;
            if (result.RoleFromHello is { } isReplica) server.IsReplica = isReplica;

            // The SLOTS view first, and this ordering is load-bearing: SetClusterConfiguration below reads
            // ServerEndPoint.ClusterTopology to decide which reply drives the shipped slot map, and falls
            // back to the NODES view when there is none. A node that reports a slot as migrated in SLOTS
            // only - which ClusterTopologyUnitTests.SlotMapIsDrivenByTheSlotsView builds on purpose - then
            // has that migration overwritten by the staler reply.
            //
            // Held on the core and given to EVERY server, not just the one that answered, because the map
            // describes the DEPLOYMENT: this core asks for it once (see the handshake, which explains why
            // asking per connection would let two nodes' answers flap it mid-reshard), so without this the
            // second connection publishes a NODES view with no SLOTS view beside it and clobbers the map.
            if (result.ClusterSlots is { Assignments.Count: > 0 } answered)
            {
                Volatile.Write(ref _clusterSlots, answered);
            }

            if (Volatile.Read(ref _clusterSlots) is { } clusterSlots)
            {
                server.SetClusterSlots(clusterSlots);
            }

            // this node's own view of the cluster, which only it can give: see
            // RespHandshakeResult.ClusterNodes for why it arrives here as text. One call sets the
            // per-server view AND feeds UpdateClusterRange, ApplyClusterRoles and UpdateNodeRelations, so
            // the shipped selector's slot map and the primary/replica genealogy come with it.
            if (result.ClusterNodes is { Length: > 0 } nodes)
            {
                server.SetClusterConfiguration(new ClusterConfiguration(
                    server.Multiplexer.ServerSelectionStrategy, nodes, server.EndPoint));
            }

            if (result.KnowsServerType
                && server.ServerType is not (ServerType.Sentinel or ServerType.Twemproxy))
            {
                server.ServerType = result.ServerType;
            }
        }

        /// <summary>What protocol this core's connection to an endpoint settled on, if it has one.</summary>
        /// <param name="endpoint">The endpoint.</param>
        /// <remarks>
        /// Asked by <c>ServerEndPoint.Protocol</c>, which otherwise answers from the shipped bridge alone -
        /// and under the engine flag that bridge may never have handshaken, so it reports nothing about a
        /// server this core has been talking RESP3 to all along.
        /// </remarks>
        internal RedisProtocol? ObservedProtocol(EndPoint endpoint)
            => endpoint is not null && _protocols.TryGetValue(endpoint, out var protocol) ? protocol : null;

        /// <summary>Told what role an endpoint turned out to play.</summary>
        /// <param name="endpoint">The endpoint.</param>
        /// <param name="isReplica">Whether it replicates a primary.</param>
        /// <remarks>
        /// <b>Pushed for the same reason selectability is, and with a sharper consequence.</b> This core
        /// learns roles from its OWN handshakes, and it dials lazily - so in the ordinary standalone pair
        /// the replica is never dialled, its role is never learned, and a <c>DemandReplica</c> read has no
        /// replica to choose. It went to the primary instead, silently, which
        /// <c>BitTests.BitFieldAllGetGoesOutAsReadOnlyAndReachesAReplica</c> reads off the profile.
        /// <para>
        /// The client already knows: reconfiguration reads <c>INFO replication</c> for every configured
        /// endpoint whether or not this core has dialled it. Borrowing that is not a dependency on the
        /// selector - it is one fact, discovered once, told to whoever needs it.
        /// </para>
        /// </remarks>
        internal void OnRole(EndPoint endpoint, bool isReplica)
            => _topology.OnRole(endpoint, isReplica ? RespEndpointRole.Replica : RespEndpointRole.Primary);

        /// <summary>Tell the selector a role this core discovered, which is the reverse of <see cref="OnRole"/>.</summary>
        /// <param name="endpoint">The endpoint whose role changed.</param>
        /// <param name="isReplica">What it turned out to be.</param>
        /// <remarks>
        /// <b>Roles are discovered here and enforced there.</b> <c>RespEndpointExecutor</c> refuses a
        /// primary-only command against a replica by reading <c>ServerEndPoint.IsReplica</c>, and so does
        /// the shipped selector - so a role learned from a <c>CLUSTER SLOTS</c> reply or a <c>-MOVED</c>
        /// that stays inside <see cref="RespTopology"/> leaves the flag stale, and a write to a just-promoted
        /// node is refused client-side rather than sent. Nothing corrects that: the refusal happens before
        /// any byte goes out, so there is no redirect to follow.
        /// <para>
        /// This is what <c>ApplyClusterRoles</c> did from the shipped <c>CLUSTER NODES</c> sweep, moved to
        /// where this core already reads the same facts. Never creates a server - a node nobody holds has no
        /// flag to be stale - and leaves a sentinel alone, which answers neither role.
        /// </para>
        /// </remarks>
        private void PublishRoleToSelector(EndPoint endpoint, bool isReplica)
        {
            if (_multiplexer.TryResolveServerEndPoint(endpoint) is { } server
                && server.ServerType != ServerType.Sentinel
                && server.IsReplica != isReplica)
            {
                server.IsReplica = isReplica;
            }
        }

        /// <summary>Re-ask the deployment what shape it is, over connections that already exist.</summary>
        /// <param name="log">Where to say what was found.</param>
        /// <remarks>
        /// <para>
        /// <b>Discovery happens at handshake, and a reconfiguration has no handshake.</b> The slot map and
        /// the roles are read once per deployment - deliberately, since they describe the deployment and not
        /// the socket - so every later change to them arrived via the shipped <c>INFO replication</c> and
        /// <c>CLUSTER NODES</c> sweep that <c>AutoConfigureAsync</c> ran. Take that away and a core with
        /// live connections has no way at all to learn that the deployment moved underneath it: nothing
        /// re-asks, because nothing reconnects.
        /// </para>
        /// <para>
        /// A failover is the case that makes it visible rather than merely stale. The promoted replica
        /// serves the slots, but this core still has it recorded as a replica, so a write to it is refused
        /// client-side before a byte goes out - which means no <c>-MOVED</c> comes back, and the one signal
        /// that would have corrected the map never arrives. <c>ClusterFailoverRolesUnitTests</c> is exactly
        /// that: a refresh is expected to repair the roles, and it can only do so if something asks.
        /// </para>
        /// <para>
        /// <b>SLOTS only, and NOT <c>CLUSTER NODES</c>.</b> A refresh re-reads what can have changed about
        /// the DEPLOYMENT, and the roles follow from it for free: <c>SetSlotRange</c> records the owners,
        /// which reaches <c>ServerEndPoint.IsReplica</c> through <see cref="RespTopology.RoleLearned"/>. The
        /// per-server <c>CLUSTER NODES</c> view stays a handshake-time fact.
        /// </para>
        /// <para>
        /// Re-reading NODES here was tried and withdrawn: publishing it calls <c>SetClusterConfiguration</c>,
        /// which <b>creates</b> a <c>ServerEndPoint</c> for nodes the reply names - and NODES names every
        /// node by address, including one <c>CLUSTER SLOTS</c> deliberately would not (the <c>"?"</c>
        /// placeholder). That is a topology refresh handing the client a way to reach a node it must not
        /// address, which <c>UnroutableRedirectUnitTests.RefreshingTheTopologyDoesNotMakeTheTargetRoutable</c>
        /// exists to catch - and once the slot map routes there directly, no redirect is issued and every
        /// other test in that class silently stops testing anything.
        /// </para>
        /// <para>
        /// <b>Once, from one connected endpoint, for the cluster case</b> - the same reasoning as the
        /// handshake's: one node's <c>CLUSTER SLOTS</c> describes them all, and asking several mid-reshard
        /// just lets their differing answers flap the map. The standalone case asks each endpoint, because
        /// there <c>ROLE</c> speaks for one replication pair and a deployment may hold several.
        /// </para>
        /// <para>
        /// Failures are not errors here. A reconfiguration triggered by trouble will find endpoints that
        /// cannot answer, and the point of the sweep is to take what it can get; the map it already holds
        /// stays until something better replaces it.
        /// </para>
        /// </remarks>
        internal async Task RefreshTopologyAsync(ILogger? log = null)
        {
            if (_topology.RoutesBySlot)
            {
                if (!_multiplexer.RawConfig.CommandMap.IsAvailable(RedisCommand.CLUSTER)) return;

                foreach (var pair in _endpoints)
                {
                    if (!pair.Value.IsConnectedNow) continue;

                    try
                    {
                        var slots = await Context(pair.Value).SendAsync(
                            $"{RedisCommand.CLUSTER}{RespLiterals.Slots}",
                            handler: RespHandshake.ClusterSlotsHandler.Instance).ConfigureAwait(false);

                        if (slots is not { Assignments.Count: > 0 }) continue; // answered, told us nothing

                        ApplySlots(pair.Key, slots);
                        log?.LogInformationSlotMapRefreshed(new(pair.Key), slots.Assignments.Count);

                        return; // one SLOTS answer is the whole deployment
                    }
                    catch (Exception ex)
                    {
                        log?.LogInformationSlotMapRefreshFailed(ex, new(pair.Key), ex.Message);
                    }
                }

                return;
            }

            if (!_topology.WantsRoles || !_multiplexer.RawConfig.CommandMap.IsAvailable(RedisCommand.ROLE)) return;

            foreach (var pair in _endpoints)
            {
                if (!pair.Value.IsConnectedNow) continue;

                try
                {
                    var role = await Context(pair.Value).SendAsync(
                        $"{RedisCommand.ROLE}", handler: RespHandshake.RoleHandler.Instance).ConfigureAwait(false);

                    _topology.OnRole(pair.Key, role.Role);
                    if (role.Peers is not null)
                    {
                        foreach (var peer in role.Peers) _topology.OnRole(peer, role.PeerRole);
                    }
                }
                catch (Exception ex)
                {
                    log?.LogInformationRoleRefreshFailed(ex, new(pair.Key), ex.Message);
                }
            }

            RespDatabaseContext Context(RespEndpointExecutor executor)
                => new(new RespContext(_multiplexer.RawConfig.CommandMap, database: -1).WithExecutor(executor));

            // this core's map AND the shipped one, for the same reason the handshake does both: the shipped
            // selector still answers for IServer and for the fallback write path, and a refresh that
            // corrected only one of them would leave the two disagreeing about where a slot lives
            void ApplySlots(EndPoint answeredBy, ClusterSlotsResult slots)
            {
                foreach (var assignment in slots.Assignments)
                {
                    if (assignment.Primary.EndPoint is not { } primary) continue;

                    EndPoint[]? replicas = null;
                    if (assignment.Replicas.Count != 0)
                    {
                        List<EndPoint>? usable = null;
                        foreach (var replica in assignment.Replicas)
                        {
                            if (replica.EndPoint is { } endPoint) (usable ??= new()).Add(endPoint);
                        }

                        replicas = usable?.ToArray();
                    }

                    _topology.SetSlotRange(assignment.Slots.From, assignment.Slots.To, primary, replicas);
                }

                // ...to EVERY server, not just the one that answered, and for a sharper reason than the
                // handshake's. SetClusterConfiguration calls ApplyClusterRoles, which prefers the SLOTS
                // view where it has one - so a server still holding its handshake-era slots view has the
                // STALE role win over the CLUSTER NODES reply that was just read for it. After a failover
                // that is the promoted node being told it is still a replica, which is the whole of
                // ClusterFailoverRolesUnitTests.
                Volatile.Write(ref _clusterSlots, slots);
                foreach (var peer in _multiplexer.GetServerSnapshot())
                {
                    if (peer.ServerType != ServerType.Sentinel) peer.SetClusterSlots(slots);
                }

                _ = answeredBy;
            }
        }

        /// <summary>Take the roles the client already knows, for endpoints this core has not dialled.</summary>
        /// <remarks>
        /// The push above only fires on a CHANGE, and a change that happened before this core existed is one
        /// nobody repeats - so a core created after the first reconfiguration would start with no roles at
        /// all. Seeded once, at construction, from the snapshot the multiplexer already holds.
        /// </remarks>
        private void SeedRoles()
        {
            foreach (var server in _multiplexer.GetServerSnapshot())
            {
                if (server.EndPoint is { } endpoint && server.ServerType != ServerType.Sentinel)
                {
                    _topology.OnRole(endpoint, server.IsReplica ? RespEndpointRole.Replica : RespEndpointRole.Primary);
                }
            }
        }

        /// <summary>For testing only: drop this core's connections to an endpoint.</summary>
        /// <param name="endpoint">The endpoint to disconnect from.</param>
        /// <param name="failureType">Which of its connections to drop.</param>
        /// <returns>Whether anything was dropped.</returns>
        /// <remarks>
        /// <b>The simulation has to reach BOTH cores or it tests nothing.</b> It was reaching only the
        /// shipped bridge, so under the engine flag the socket actually carrying commands stayed up and
        /// every test built on "now break the connection" quietly observed a command succeeding - which is
        /// why that family reads as "no exception was thrown" rather than as a wrong exception.
        /// <para>
        /// Coarser than the shipped simulation, which breaks one direction of the socket to reproduce a
        /// specific fault. This drops the connection outright, which is the part those tests are actually
        /// about: the connection went away, and what happens next.
        /// </para>
        /// </remarks>
        internal bool SimulateConnectionFailure(EndPoint endpoint, SimulatedFailureType failureType)
        {
            var dropped = false;

            if ((failureType & SimulatedFailureType.AllInteractive) != 0
                && _endpoints.TryGetValue(endpoint, out var interactive))
            {
                dropped |= interactive.DropConnection();
            }

            if ((failureType & SimulatedFailureType.AllSubscription) != 0
                && _subscriptions.TryGetValue(endpoint, out var subscription))
            {
                dropped |= subscription.DropConnection();
            }

            return dropped;
        }

        /// <summary>Let outstanding commands finish, before this core is torn down.</summary>
        /// <param name="timeoutMilliseconds">How long to wait before giving up on the stragglers.</param>
        /// <remarks>
        /// <para>
        /// <b>Closing is allowed to be orderly, and this core had no way to be.</b> Disposal tore the
        /// connections down at once, so anything still queued - or written but not yet on the wire, since
        /// the transport's flush signals its writer rather than promising delivery - was lost. A caller who
        /// awaits every command never notices; one who does not is exactly who this is for, and
        /// <c>CloseAsync</c> already advertises <c>allowCommandsToComplete</c> as meaning otherwise.
        /// </para>
        /// <para>
        /// Waits only on work that can still finish - see <c>UnfinishedCount</c> - because the alternative
        /// is waiting out the full timeout for commands stranded behind a connection that is never coming
        /// back, which is the ordinary state of a shutdown that follows a failure.
        /// </para>
        /// <para>
        /// Polled rather than signalled: this runs once per multiplexer, at the end of its life, and a
        /// completion source per endpoint would be machinery maintained forever for that one moment.
        /// </para>
        /// </remarks>
        internal async Task DrainAsync(int timeoutMilliseconds)
        {
            if (timeoutMilliseconds <= 0) return;

            var deadline = Environment.TickCount + timeoutMilliseconds;
            while (Unfinished() != 0)
            {
                if (unchecked(deadline - Environment.TickCount) <= 0) return;
                await Task.Delay(DrainPollMilliseconds).ConfigureAwait(false);
            }

            int Unfinished()
            {
                var total = 0;
                foreach (var pair in _endpoints) total += pair.Value.UnfinishedCount;
                foreach (var pair in _subscriptions) total += pair.Value.UnfinishedCount;
                return total;
            }
        }

        private const int DrainPollMilliseconds = 5;

        /// <summary>Periodic upkeep, driven by the multiplexer's heartbeat.</summary>
        /// <remarks>
        /// The shipped core pulses every <c>ServerEndPoint</c> from the same timer; this is the equivalent
        /// for the endpoints this core owns. Timeouts are the work that has to happen on a clock rather
        /// than in response to something: nothing arrives to tell you a reply is late.
        /// </remarks>
        internal void OnHeartbeat()
        {
            var timeout = _multiplexer.AsyncTimeoutMilliseconds;
            foreach (var pair in _endpoints) pair.Value.OnHeartbeat(timeout);
            foreach (var pair in _subscriptions) pair.Value.OnHeartbeat(timeout);
        }

        /// <summary>Release a connection-local claim on this core's interactive connection to an endpoint.</summary>
        /// <param name="endpoint">The endpoint.</param>
        /// <param name="id">What was claimed; see <c>RespClientConnection.ReleaseClaim</c>.</param>
        internal void ReleaseClaim(EndPoint endpoint, long id)
        {
            if (_endpoints.TryGetValue(endpoint, out var executor) && executor.CurrentConnection is RespClientConnection connection)
            {
                connection.ReleaseClaim(id);
            }
        }

        /// <summary>Stop holding a connection to an endpoint the client has stopped modelling.</summary>
        /// <param name="endpoint">The endpoint whose server was retired.</param>
        /// <remarks>
        /// <b>Called when the multiplexer retires a server</b>, and the reason it must be: this core keeps an
        /// executor per endpoint, and an executor reconnects. Its connect path resolves the endpoint's server
        /// - creating one if there is none - so a retired server whose executor lived on came straight back
        /// on the next reconnect. <c>SentinelTests.AbortOnConnectFailFalseRecoversOnceSentinelsAppear</c>
        /// measured it as the sentinel seed address lingering among a primary's endpoints after the switch.
        /// The views over the endpoint go too: they would otherwise hand out a disposed executor.
        /// </remarks>
        internal async Task RetireEndpointAsync(EndPoint endpoint)
        {
            foreach (var byEndpoint in _views.Values) byEndpoint.TryRemove(endpoint, out _);
            if (_endpoints.TryRemove(endpoint, out var interactive))
            {
                await interactive.DisposeAsync().ConfigureAwait(false);
            }

            if (_subscriptions.TryRemove(endpoint, out var subscription))
            {
                await subscription.DisposeAsync().ConfigureAwait(false);
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
