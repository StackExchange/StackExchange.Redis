using System;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
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
        private readonly ConcurrentDictionary<EndPoint, RespEndpointExecutor> _endpoints = new();

        /// <summary>What each endpoint's own handshake reported about itself.</summary>
        /// <remarks>
        /// Filled by the connect path, read by that endpoint's executor. An observation from the
        /// connection that will run the command, rather than a version borrowed from somebody else's
        /// topology.
        /// </remarks>
        private readonly ConcurrentDictionary<EndPoint, RedisFeatures> _observed = new();
        private readonly RespMultiplexerExecutor _router;

        internal RespNewCore(ConnectionMultiplexer multiplexer)
        {
            _multiplexer = multiplexer;
            _topology = new RespTopology(multiplexer.ServerSelectionStrategy.ServerType);
            _router = new RespMultiplexerExecutor(
                _topology,
                ForSlot,
                Any,
                multiplexer.RawConfig.DefaultDatabase.GetValueOrDefault(),
                ForEndpoint,
                OnSlotMoved,
                OnTopologySuspect);
        }

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
            => new(new RespContext(
                    _multiplexer.RawConfig.CommandMap,
                    database: database,
                    serverType: _multiplexer.ServerSelectionStrategy.ServerType)
                .WithTopology(_topology)
                .WithServices(new RedisBase.ServerFeatureProbe((RedisBase)_multiplexer.GetDatabase(database)))
                .WithExecutor(database == _router.Database ? _router : Rebind(database)));

        private RespMultiplexerExecutor Rebind(int database) => new(
            _topology, ForSlot, Any, database, ForEndpoint, OnSlotMoved, OnTopologySuspect);

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
        private RespExecutorBase? ForSlot(int slot, RedisCommand command, CommandFlags flags)
        {
            var server = _multiplexer.ServerSelectionStrategy.Select(slot, command, flags, allowDisconnected: true);
            return server is null ? Any(command, flags) : Executor(server.EndPoint);
        }

        private RespExecutorBase? Any(RedisCommand command, CommandFlags flags)
        {
            var server = _multiplexer.ServerSelectionStrategy.Select(
                ServerSelectionStrategy.NoSlot, command, flags, allowDisconnected: true);
            if (server is not null) return Executor(server.EndPoint);

            var endpoints = _multiplexer.GetEndPoints();
            return endpoints.Length == 0 ? null : Executor(endpoints[0]);
        }

        private RespExecutorBase? ForEndpoint(EndPoint endpoint) => Executor(endpoint);

        private void OnSlotMoved(int slot, EndPoint endpoint)
            => _multiplexer.ReconfigureIfNeeded(endpoint, false, "MOVED encountered");

        private void OnTopologySuspect()
            => _multiplexer.ReconfigureIfNeeded(null, false, "unroutable redirect");

        private RespEndpointExecutor Executor(EndPoint endpoint)
        {
            // the factory-with-state overload of GetOrAdd does not exist on the down-level targets, and
            // this is not a hot path - an endpoint is resolved once and then reused
            return _endpoints.TryGetValue(endpoint, out var existing)
                ? existing
                : _endpoints.GetOrAdd(endpoint, Create(endpoint));
        }

        private RespEndpointExecutor Create(EndPoint endpoint) => new(
            token => ConnectAsync(endpoint, token),
            _multiplexer.RawConfig.DefaultDatabase.GetValueOrDefault(),
            endpoint,
            _multiplexer.RawConfig.BacklogPolicy.QueueWhileDisconnected,
            () => _observed.TryGetValue(endpoint, out var features) ? features : null,
            StartProfile);

        /// <summary>Open a socket, hand it to the new stack, and bring it up.</summary>
        /// <remarks>
        /// <b>The handshake sets the topology before this returns</b>, which is what keeps the ordering
        /// invariant structural: the endpoint executor publishes the connection and drains its backlog the
        /// moment this completes, and a backlog draining against an unset topology is the window that
        /// loses per-slot ordering. See design notes 7h.
        /// </remarks>
        private async Task<RespConnection> ConnectAsync(EndPoint endpoint, CancellationToken cancellationToken)
        {
            var socket = new Socket(SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
            await ConnectSocketAsync(socket, endpoint).ConfigureAwait(false);

            var transport = new StreamDuplexTransport(new NetworkStream(socket, ownsSocket: true));
            var connection = new RespRedirectingConnection(transport, Follow);

            var config = _multiplexer.RawConfig;
            var context = new RespDatabaseContext(
                new RespContext(config.CommandMap, database: 0)
                    .WithExecutor(new RespConnectionExecutor(connection, 0)));

            var result = await RespHandshake.PerformAsync(
                context,
                config.User,
                config.Password,
                _multiplexer.ClientName,
                config.DefaultDatabase.GetValueOrDefault(),
                config.Protocol is null or RedisProtocol.Resp3,
                _topology,
                cancellationToken).ConfigureAwait(false);

            // recorded BEFORE the connection is handed back, for the same reason the topology is: the
            // endpoint executor publishes it and drains its backlog the moment this returns, and a
            // command choosing its spelling from "we have no idea" is the case this exists to avoid
            if (result.Version is { } version) _observed[endpoint] = new RedisFeatures(version);

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

        private static Task ConnectSocketAsync(Socket socket, EndPoint endpoint) => endpoint switch
        {
            DnsEndPoint dns => socket.ConnectAsync(dns.Host, dns.Port),
            _ => socket.ConnectAsync(endpoint),
        };

        /// <inheritdoc/>
        public async ValueTask DisposeAsync()
        {
            foreach (var executor in _endpoints.Values) await executor.DisposeAsync().ConfigureAwait(false);
            _endpoints.Clear();
        }
    }
}
