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

        /// <summary>What each endpoint's own handshake reported about itself.</summary>
        /// <remarks>
        /// Filled by the connect path, read by that endpoint's executor. An observation from the
        /// connection that will run the command, rather than a version borrowed from somebody else's
        /// topology.
        /// </remarks>
        private readonly ConcurrentDictionary<EndPoint, RedisFeatures> _observed = new();
        private readonly RespMultiplexerExecutor _router;
        private readonly MultiplexerFeatureProbe _features;

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
            _topology = new RespTopology(SeedTopology(multiplexer));
            _features = new MultiplexerFeatureProbe(multiplexer);
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
        private RespExecutorBase? ForSlot(int database, int slot, RedisCommand command, CommandFlags flags)
        {
            var server = _multiplexer.ServerSelectionStrategy.Select(slot, command, flags, allowDisconnected: true);
            return server is null ? Any(database, command, flags) : Executor(database, server.EndPoint);
        }

        private RespExecutorBase? Any(int database, RedisCommand command, CommandFlags flags)
        {
            var server = _multiplexer.ServerSelectionStrategy.Select(
                ServerSelectionStrategy.NoSlot, command, flags, allowDisconnected: true);
            if (server is not null) return Executor(database, server.EndPoint);

            var endpoints = _multiplexer.GetEndPoints();
            return endpoints.Length == 0 ? null : Executor(database, endpoints[0]);
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

        private void OnSlotMoved(int slot, EndPoint endpoint)
            => _multiplexer.ReconfigureIfNeeded(endpoint, false, "MOVED encountered");

        private void OnTopologySuspect()
            => _multiplexer.ReconfigureIfNeeded(null, false, "unroutable redirect");

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
            _select);

        /// <summary>Open a socket, hand it to the new stack, and bring it up.</summary>
        /// <remarks>
        /// <b>The handshake sets the topology before this returns</b>, which is what keeps the ordering
        /// invariant structural: the endpoint executor publishes the connection and drains its backlog the
        /// moment this completes, and a backlog draining against an unset topology is the window that
        /// loses per-slot ordering. See design notes 7h.
        /// </remarks>
        private async Task<RespConnection> ConnectAsync(int database, EndPoint endpoint, CancellationToken cancellationToken)
        {
            var config = _multiplexer.RawConfig;

            // the shared chain: tunnel, proxy, socket, TLS. This used to be a bare socket here, which
            // silently ignored every one of those - and the TLS I added to it first was a second copy of
            // the shipped logic, which is worse than none: two versions of a security decision, free to
            // drift. DuplexTransport is the boundary; everything below it belongs to the factory.
            var transport = await RespTransportFactory.ConnectAsync(
                endpoint,
                config,
                ConnectionType.Interactive,
                _multiplexer.SetAuthSuspect,
                cancellationToken).ConfigureAwait(false);

            var connection = new RespClientConnection(transport, Follow);
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
                cancellationToken).ConfigureAwait(false);

            // recorded BEFORE the connection is handed back, for the same reason the topology is: the
            // endpoint executor publishes it and drains its backlog the moment this returns, and a
            // command choosing its spelling from "we have no idea" is the case this exists to avoid
            if (result.Version is { } version) _observed[endpoint] = new RedisFeatures(version);

            // the handshake's SELECT is where this connection's database is decided; recording it is what
            // lets a later command for a different one know it has to say so first
            connection.CurrentDatabase = database;

            // which server this reached, so a preamble gate can consult the endpoint's beliefs - a loaded
            // script is server-wide, and ServerEndPoint already tracks that and flushes it when a server's
            // identity changes underneath. Borrowed rather than reimplemented while both cores exist.
            connection.Server = _multiplexer.GetServerEndPoint(endpoint, ServerProvenance.Configured, activate: false);

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
            operation.Profile = profile;
            return profile;
        }

        private bool Follow(in RespRedirect redirect, RespPayloadOperation operation)
            => _router.TryFollowRedirect(in redirect, operation);

        /// <summary>
        /// Answers "what can the server that would take this command do", from the multiplexer's topology.
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
        /// Borrowed rather than reimplemented, in the same sense as the topology: <c>SelectServer</c>
        /// already knows about replica preference, reachability and the configured default version.
        /// </para>
        /// </remarks>
        private sealed class MultiplexerFeatureProbe(ConnectionMultiplexer multiplexer) : IRespServerFeatures
        {
            public bool TryGetFeatures(RedisCommand command, in RedisKey key, CommandFlags flags, out RedisFeatures features)
            {
                var server = multiplexer.SelectServer(command, flags, key);

                // usable either way - the configured default version stands in - but only a selected server
                // makes this an observation rather than a guess, which is what the bool reports
                features = new RedisFeatures(server is null ? multiplexer.RawConfig.DefaultVersion : server.Version);
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

            // the views own nothing - they are a database index over an endpoint's connection - so
            // disposing the endpoints disposes everything there is to dispose
            _views.Clear();
            _endpoints.Clear();
        }
    }
}
