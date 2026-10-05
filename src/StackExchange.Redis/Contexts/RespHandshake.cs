using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using RESPite.Messages;
using StackExchange.Redis.Protocol;

namespace StackExchange.Redis
{
    /// <summary>What a handshake established about a connection.</summary>
    /// <param name="protocol">The protocol the connection ended up speaking.</param>
    /// <param name="serverType">What the server turned out to be.</param>
    /// <param name="version">The server version, when HELLO reported one.</param>
    /// <param name="knowsServerType">Whether the server type was determined rather than defaulted.</param>
    /// <param name="connectionId">What the server calls this connection, when it would say.</param>
    /// <param name="roleFromHello">What <c>HELLO</c> said this server's role is, when it said.</param>
    /// <param name="clusterNodes">This node's own <c>CLUSTER NODES</c> text, when it answered one.</param>
    /// <param name="clusterSlots">The <c>CLUSTER SLOTS</c> view, when this connection asked for one.</param>
    internal readonly struct RespHandshakeResult(
        RedisProtocol protocol,
        ServerType serverType,
        Version? version = null,
        bool knowsServerType = false,
        long? connectionId = null,
        bool? roleFromHello = null,
        string? clusterNodes = null,
        ClusterSlotsResult? clusterSlots = null)
    {
        /// <summary>The <c>CLUSTER SLOTS</c> view, when this connection was the one that asked.</summary>
        /// <remarks>
        /// <b>Carried out so the shipped <c>ServerEndPoint.ClusterTopology</c> can be set from it</b>, which
        /// is what identity-merging and topology ageing read. Null on every connection that skipped the ask -
        /// which is most of them, since the map describes the deployment and one answer serves all.
        /// </remarks>
        internal ClusterSlotsResult? ClusterSlots { get; } = clusterSlots;

        /// <summary>This node's own <c>CLUSTER NODES</c> text, when it answered one.</summary>
        /// <remarks>
        /// <b>Carried out raw rather than parsed here, because parsing it needs a <c>ServerEndPoint</c></b> -
        /// <c>ClusterConfiguration</c> is built against the server that answered and the selection strategy -
        /// and the handshake deliberately has neither. <c>RespNewCore.Publish</c> is where this core's
        /// findings are written onto the shipped server object, so that is where it is turned into one.
        /// </remarks>
        internal string? ClusterNodes { get; } = clusterNodes;

        /// <summary>What <c>HELLO</c> reported as the role, when it reported one.</summary>
        /// <remarks>
        /// Null means <b>nobody has said</b>, which is the case the <c>SET</c> probe exists for - not
        /// "primary". The distinction is the whole reason this is nullable.
        /// </remarks>
        internal bool? RoleFromHello { get; } = roleFromHello;

        /// <summary>What the server calls this connection, when it would say.</summary>
        internal long? ConnectionId { get; } = connectionId;

        /// <summary>Whether <see cref="ServerType"/> was actually determined, rather than defaulted.</summary>
        /// <remarks>
        /// <b>The difference matters to whoever is told.</b> <see cref="ServerType"/> has no "not yet
        /// known" value, so an undetermined handshake reports <c>Standalone</c> - and a caller publishing
        /// that as a fact would demote a cluster, or a sentinel, to standalone on the strength of a
        /// question nobody answered.
        /// </remarks>
        internal bool KnowsServerType { get; } = knowsServerType;

        /// <summary>The server version, when <c>HELLO</c> reported one.</summary>
        /// <remarks>
        /// Read from the same reply that settles the protocol, so it costs nothing extra - and it is what
        /// lets a connection answer "what can this server do?" from its own observation rather than
        /// borrowing somebody else's topology.
        /// </remarks>
        internal Version? Version { get; } = version;

        /// <summary>The protocol the connection ended up speaking.</summary>
        internal RedisProtocol Protocol { get; } = protocol;

        /// <summary>What the server turned out to be.</summary>
        internal ServerType ServerType { get; } = serverType;
    }

    /// <summary>
    /// EXPERIMENTAL SPIKE. Brings a fresh connection up to the state a command can be issued on.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>This is much shorter than the handshake it replaces, and the reason is worth stating.</b> The
    /// existing one in <c>ServerEndPoint.HandshakeAsync</c> sends <c>AUTH</c> and <c>CLIENT SETNAME</c>
    /// <i>twice</i> - once folded into <c>HELLO</c> and once standalone - deliberately, because it writes
    /// fire-and-forget with no flush and therefore cannot see whether the <c>HELLO</c> was understood. It
    /// hedges because it is writing blind.
    /// </para>
    /// <para>
    /// The new connection can await a reply, so it can branch instead: authenticate first, then ask for
    /// RESP3 and <i>read the answer</i>. Same end state, no duplicate commands, and the protocol is known
    /// rather than inferred later from a tracer.
    /// </para>
    /// <para>
    /// <b>Order matters and is not arbitrary.</b> <c>AUTH</c> comes first because an authenticated server
    /// rejects <c>HELLO</c> with <c>NOAUTH</c>; <c>SELECT</c> comes last because it is the one step that
    /// says something about this connection's <i>use</i> rather than its identity, and a failed handshake
    /// should not leave a connection pointing at a database it never reached.
    /// </para>
    /// </remarks>
    internal static class RespHandshake
    {
        /// <summary>Authenticate, negotiate a protocol, name the connection, and select a database.</summary>
        /// <param name="context">The context over the new connection.</param>
        /// <param name="user">The user, for ACL logins; null for a password-only login.</param>
        /// <param name="password">The password, or null when the server needs none.</param>
        /// <param name="clientName">A name for this connection, or null.</param>
        /// <param name="database">The database to select; zero needs no command.</param>
        /// <param name="preferResp3">Whether to ask for RESP3.</param>
        /// <param name="helloAvailable">
        /// Whether <c>HELLO</c> should be sent at all. Separate from <paramref name="preferResp3"/>, which
        /// only decides the protover: a deployment can opt out of the command entirely, by the command map
        /// or by declaring an assumed server version older than 6.0 - and <c>ConfigurationOptions.TryHello</c>
        /// is the one place that weighs both.
        /// </param>
        /// <param name="topology">
        /// Told what the server turned out to be, before this returns. See the remarks: setting it here
        /// rather than afterwards is what makes the ordering invariant structural.
        /// </param>
        /// <param name="endpoint">
        /// Which endpoint this connection reached, when the caller knows. Only used to record a role: a
        /// standalone server names its own side of a replication pair and nothing else in the reply says
        /// which server said it.
        /// </param>
        /// <param name="clientCache">
        /// The client-side cache whose invalidations this connection must ask for, or null when there is
        /// none. Interactive connections only - a subscription connection reads nothing to invalidate.
        /// </param>
        /// <param name="libraryName">
        /// What to report as <c>lib-name</c>, suffixes included, or null/empty to report nothing.
        /// </param>
        /// <param name="libraryVersion">What to report as <c>lib-ver</c>, or null/empty to report nothing.</param>
        /// <param name="onAuthSuspect">
        /// Told when the server refused <c>AUTH</c>, which does not fail the handshake; see the catch.
        /// </param>
        /// <param name="cancellationToken">Cancels the handshake.</param>
        /// <param name="server">
        /// The server this connection reached: what was learned is logged against it, and the round-trip
        /// time is recorded on it. Null to do neither.
        /// </param>
        /// <returns>What the connection ended up speaking, and what it turned out to be.</returns>
        internal static async Task<RespHandshakeResult> PerformAsync(
            RespDatabaseContext context,
            string? user = null,
            string? password = null,
            string? clientName = null,
            int database = 0,
            bool preferResp3 = true,
            bool helloAvailable = true,
            RespTopology? topology = null,
            EndPoint? endpoint = null,
            Caching.RespClientCache? clientCache = null,
            string? libraryName = null,
            string? libraryVersion = null,
            Action<Exception>? onAuthSuspect = null,
            CancellationToken cancellationToken = default,
            ServerEndPoint? server = null)
        {
            // the shipped "Auto-configured (SOURCE) ..." events, by the same ids, each written where the fact
            // is learned and naming the command that taught it - which is what makes them worth reading
            var log = server?.Multiplexer.Logger;
            log?.LogInformationServerHandshake(new(server!));

            // WHO AUTHENTICATES depends on whether AUTH is available at all. A command map that disables it
            // is not a map without credentials - a proxy can require them and refuse the command - and in
            // that case HELLO is the only thing that can authenticate the connection, so the credentials
            // travel with it. The shipped handshake makes the same split, and says the same thing about
            // ordering: only the credential-carrying flavour of HELLO has to come first.
            var canAuthDirectly = context.Raw.CommandMap.IsAvailable(RedisCommand.AUTH);
            var helloCarriesCredentials = password is not null
                && !canAuthDirectly
                && helloAvailable
                && context.Raw.CommandMap.IsAvailable(RedisCommand.HELLO);

            if (password is not null && canAuthDirectly)
            {
                // "" is a legitimate password, for 'nopass' logins - hence null rather than empty as the
                // "no auth needed" signal, matching ConfigurationOptions
                try
                {
                    if (user is { Length: > 0 })
                    {
                        await context.SendAsync($"{RedisCommand.AUTH}{(RedisValue)user}{(RedisValue)password}").ConfigureAwait(false);
                    }
                    else
                    {
                        await context.SendAsync($"{RedisCommand.AUTH}{(RedisValue)password}").ConfigureAwait(false);
                    }
                }
                catch (RedisServerException ex)
                {
                    // A REFUSED AUTH DOES NOT FAIL THE CONNECTION, and that is parity rather than
                    // laxity. The shipped handshake writes AUTH fire-and-forget and cannot read the
                    // reply at all, so it necessarily continues and records the suspicion
                    // (`ConnectionMultiplexer.SetAuthSuspect`); commands then fail individually, with
                    // the server's own words, which is a far better diagnostic than a connection that
                    // never exists.
                    //
                    // The case that made this matter is not a wrong password: it is ONE configuration
                    // spanning servers with different requirements. A config carrying the secure
                    // server's password also reaches the plain one, which answers "Client sent AUTH, but
                    // no password is set" - so awaiting that reply took a perfectly good endpoint out of
                    // the deployment. `MultiPrimaryTests` reads it as "Single primary detected" where two
                    // were expected, after a full connect timeout.
                    onAuthSuspect?.Invoke(ex);

                    // ...and where the credentials themselves were refused, stop ROUTING to this endpoint
                    // without failing the connection. Those are two different things and both tests need
                    // them separated: ConfigTests.MutableOptions requires ConnectAsync to SUCCEED with
                    // AuthException set, while SecureTests.ConnectWithWrongPassword requires the first
                    // command to fail as a connection failure carrying that same auth text.
                    //
                    // Shipped arrives there by a longer road - a bridge whose AUTH failed never reaches
                    // ConnectedEstablished, so IsSelectable says no and the command is refused client-side
                    // by ExceptionFactory.UnableToConnect, which reads AuthException for its wording. This
                    // core's connection genuinely does establish, so the unselectability has to be said
                    // out loud. A later handshake that authenticates clears it, which is what lets a
                    // corrected password recover.
                    //
                    // TWO LEVERS TRIED, BOTH WRONG, recorded so the third one starts further along.
                    // Failing the handshake takes ConnectAsync down with it, which MutableOptions forbids
                    // outright. Marking the endpoint unselectable here does not stop the routing either -
                    // a single-endpoint standalone is still chosen - so it bought nothing and risked
                    // leaving a deployment unroutable after a transient refusal.
                    //
                    // What is actually missing is the COMMAND path: shipped converts a NOAUTH or WRONGPASS
                    // reply into auth suspicion plus a connection failure (ResultProcessor.SetAuthSuspect,
                    // with its own synthesised "NOAUTH Returned - connection has not yet authenticated"
                    // wording), and this core throws the server's error through untouched. That conversion
                    // wants to happen where the error kind is already classified, which is
                    // RespPayloadOperation.ParseFrame - and that has no multiplexer to report to, which is
                    // the real work in it.
                }
            }

            var protocol = RedisProtocol.Resp2;
            bool? roleFromHello = null;
            var serverType = ServerType.Standalone;
            var knowServerType = false;
            var clusterInfoDeclined = false;
            Version? version = null;
            // Disabled in the command map counts as "the server will not do this", and has to be answered
            // without sending: a disabled command throws RedisCommandException before it reaches a socket,
            // which is not the RedisServerException these catches expect, so it would escape and fail the
            // whole handshake rather than being the ordinary decline every one of them is written for.
            // ON RESP2 AS WELL, which is not a wasted round trip: the reply carries the server's version,
            // its mode and its role, so asking is cheaper than the INFO sections that are the alternative
            // source for all three - which is exactly why the shipped handshake sends a bare HELLO even
            // when it has no intention of speaking RESP3. Skipping it here meant this core asked for none
            // of that, and `HelloHandshakeTests` says so plainly: it asserts HELLO is issued for BOTH
            // protocols, with the protover that matches.
            if (helloAvailable && context.Raw.CommandMap.IsAvailable(RedisCommand.HELLO))
            {
                try
                {
                    // the server can decline in two ways: an error (HELLO unknown, or the version
                    // refused), or a perfectly successful reply that says proto 2. Both are normal, and
                    // only the reply distinguishes them - which is the thing the old handshake could not
                    // wait for.
                    //
                    // The protover asked for is the one we actually want. Asking for 3 while configured
                    // for RESP2 would be asking to be upgraded against the caller's wishes; asking for 2
                    // is a discovery request that cannot change the protocol.
                    var protover = preferResp3 ? 3 : 2;
                    var hello = helloCarriesCredentials
                        ? await context.SendAsync(
                                $"{RedisCommand.HELLO}{protover}{RespLiterals.Auth}{(RedisValue)(user is { Length: > 0 } ? user : RedisLiterals.@default)}{(RedisValue)password!}",
                                handler: HelloHandler.Instance)
                            .ConfigureAwait(false)
                        : await context.SendAsync(
                                $"{RedisCommand.HELLO}{protover}", handler: HelloHandler.Instance)
                            .ConfigureAwait(false);
                    if (preferResp3 && hello.Proto >= 3) protocol = RedisProtocol.Resp3;
                    if (hello.Mode is { } mode)
                    {
                        serverType = mode;
                        knowServerType = true;
                    }

                    version = hello.Version;
                    roleFromHello = hello.IsReplica;

                    if (log is not null)
                    {
                        log.LogInformationAutoConfiguredHelloProtocol(new(server!), protocol);
                        if (version is not null) log.LogInformationAutoConfiguredHelloServerVersion(new(server!), version);
                        if (hello.Mode is { } helloMode) log.LogInformationAutoConfiguredHelloServerType(new(server!), helloMode);
                        if (roleFromHello is { } helloReplica)
                        {
                            log.LogInformationAutoConfiguredHelloRole(new(server!), helloReplica ? "replica" : "primary");
                        }
                    }
                }
                catch (RedisServerException)
                {
                    // declined; RESP2 it is. Not a failure - plenty of servers and proxies have no HELLO.
                }
            }

            if (!knowServerType)
            {
                // no HELLO, or a HELLO that did not say. CLUSTER INFO is unambiguous and works on RESP2;
                // a server where CLUSTER is unavailable is, by that very fact, not a cluster.
                //
                // "Unavailable" includes disabled in the command map, which is how the proxy maps express
                // it - and that case has to be answered WITHOUT sending, because a disabled command throws
                // RedisCommandException before it reaches a socket. That is not a RedisServerException, so
                // it sailed past the catch below and failed the whole handshake: connecting through envoy
                // or twemproxy died with "This operation has been disabled in the command-map and cannot
                // be used: CLUSTER" instead of concluding, correctly, that this is not a cluster.
                if (!context.Raw.CommandMap.IsAvailable(RedisCommand.CLUSTER))
                {
                    serverType = ServerType.Standalone;
                }
                else
                {
                    try
                    {
                        serverType = await context.SendAsync(
                            $"{RedisCommand.CLUSTER}{RespLiterals.Info}",
                            handler: ClusterInfoHandler.Instance).ConfigureAwait(false);
                    }
                    catch (RedisServerException)
                    {
                        // Standalone is the LIKELY reading, not a settled one, and the difference matters:
                        // CLUSTER INFO is only one of the ways a deployment says it is a cluster, and a
                        // server that implements the routing surface without the diagnostic one - a proxy,
                        // a fake, an implementation that simply never needed INFO - answers an error here
                        // and a perfectly good map to CLUSTER SLOTS. Concluding standalone from this alone
                        // turns off slot routing for a deployment that plainly has slots.
                        serverType = ServerType.Standalone;
                        clusterInfoDeclined = true;
                    }
                }
            }

            // THE MAP, on the same connection - and it is asked BEFORE the server type is published, which
            // is a change from simply trusting CLUSTER INFO. A reply carrying slot ranges is itself proof
            // of a cluster, and a stronger one than the diagnostic command: it is the very thing routing
            // would use. So a server that declined CLUSTER INFO gets asked anyway, and an answer overrides
            // the standalone assumption that decline produced.
            //
            // Failure is not fatal and must not be: routing falls back to the selector while the map is
            // empty, so a server that will not answer CLUSTER SLOTS - a proxy, a permission - costs the
            // improvement and nothing else.
            //
            // ...and asked ONCE, not per connection. The map describes the deployment, not this socket, so
            // a second connection re-asking learns nothing - it just pays a round trip and, mid-reshard,
            // lets two nodes' differing answers flap the map by whichever order sockets happened to open.
            // The signal that it is worth asking again is a redirect, not a new connection.
            //
            // The first connection of ANY kind still asks, subscription connections included: a client that
            // only ever subscribes still has to know where things live.
            ClusterSlotsResult? slots = null;
            var knowSlots = topology is { HasSlotMap: true, SlotMapSuspect: false };
            if ((serverType == ServerType.Cluster || clusterInfoDeclined)
                && !knowSlots
                && topology is not null
                && context.Raw.CommandMap.IsAvailable(RedisCommand.CLUSTER))
            {
                try
                {
                    slots = await context.SendAsync(
                        $"{RedisCommand.CLUSTER}{RespLiterals.Slots}",
                        handler: ClusterSlotsHandler.Instance).ConfigureAwait(false);

                    // slots exist, so this is a cluster whatever CLUSTER INFO did or did not say
                    if (slots is { Assignments.Count: > 0 }) serverType = ServerType.Cluster;
                }
                catch (RedisServerException)
                {
                    // no map from this server; the selector still answers
                }
            }

            // A map we already hold is itself the answer to "is this a cluster?", and saying so here is what
            // makes the skip above safe. Without it a server that declines CLUSTER INFO - a proxy, the
            // in-process test server - would be read as standalone on its SECOND connection and publish that,
            // downgrading a topology the first connection had correctly established.
            if (knowSlots) serverType = ServerType.Cluster;

            // BEFORE the connection is handed back, and that ordering is the point: the endpoint executor
            // publishes the connection and drains its backlog the moment this returns, and a backlog
            // draining against an unset topology is exactly the window that loses per-slot ordering
            topology?.OnServerType(serverType);

            if (slots is not null && topology is not null)
            {
                foreach (var assignment in slots.Assignments)
                {
                    // a node the reply could not give a dialable endpoint for is skipped rather than
                    // guessed at: see ClusterSlotsHandler, and 9y for what guessing cost
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

                    topology.SetSlotRange(assignment.Slots.From, assignment.Slots.To, primary, replicas);
                }
            }

            // CLUSTER NODES, and PER CONNECTION - which is the opposite of the rule above, deliberately.
            // CLUSTER SLOTS describes the deployment, so one answer serves every connection; NODES is
            // answered from the point of view of the node asked, and `myself` is the part that matters.
            // ServerEndPoint.ClusterConfiguration is that per-server view, and several public surfaces are
            // nothing but a read of it: IServer.ClusterConfiguration itself, InventKey (via
            // GetServableSlot, which asks which slots THIS node serves), and the tracer key. Measured as
            // ClusterTests.TestIdentity and InventKeyRoutesBackToTheServerThatInventedIt, both of which
            // ask a server about itself and got null from the one server that had not been asked.
            //
            // The shipped core got this from AutoConfigureAsync on every connection, so asking here is
            // parity rather than a new cost - and it is the last thing keeping that sweep alive.
            string? clusterNodes = null;
            if (serverType == ServerType.Cluster
                && context.Raw.CommandMap.IsAvailable(RedisCommand.CLUSTER))
            {
                try
                {
                    clusterNodes = await context.SendAsync(
                        $"{RedisCommand.CLUSTER}{RespLiterals.Nodes}",
                        handler: RespHandlers.String).ConfigureAwait(false);
                }
                catch (RedisServerException)
                {
                    // a proxy or an ACL can refuse it; everything above still stands
                }
            }

            // The standalone counterpart of CLUSTER SLOTS, and it earns its round trip the same way: not by
            // naming this server's role - which would only ever describe the one endpoint that answered -
            // but by naming the OTHER side. A primary lists its replicas and a replica names its primary,
            // so one connection describes the pair, and a replica nobody has had reason to dial still has
            // a role. That is what makes a lazily-connecting core able to answer DemandReplica at all.
            //
            // ROLE rather than INFO REPLICATION: the same facts as a structured reply instead of a text
            // section to scan, and it is what the shipped core parses too.
            //
            // Asked only when it can change a decision. With one endpoint configured there is nothing to
            // prefer a replica OVER, so the round trip would be spent to learn something routing cannot
            // act on, on every connection, forever.
            else if (serverType != ServerType.Cluster
                && topology is { WantsRoles: true }
                && endpoint is not null
                && topology.RoleOf(endpoint) == RespEndpointRole.Unknown
                && context.Raw.CommandMap.IsAvailable(RedisCommand.ROLE))
            {
                try
                {
                    var role = await context.SendAsync(
                        $"{RedisCommand.ROLE}",
                        handler: RoleHandler.Instance).ConfigureAwait(false);

                    topology.OnRole(endpoint, role.Role);
                    if (role.Peers is not null)
                    {
                        foreach (var peer in role.Peers) topology.OnRole(peer, role.PeerRole);
                    }
                }
                catch (RedisServerException)
                {
                    // ROLE can be restricted or renamed; unknown roles route the way they did before
                }
            }

            // The version, when HELLO did not give one - which is every RESP2 connection, since the field
            // only exists in a reply RESP2 servers do not send. It matters more than it sounds: several
            // commands are CHOSEN from what the server supports (an all-GET BITFIELD goes out as
            // BITFIELD_RO where that exists, which is what lets a replica serve it), so a core that cannot
            // answer "what can this server do?" picks the writable spelling and a replica read is refused.
            //
            // A round trip, and only on the connections that have no other way to learn it. The
            // alternative was reading it off somebody else's topology, which is the coupling being removed.
            if (version is null && context.Raw.CommandMap.IsAvailable(RedisCommand.INFO))
            {
                try
                {
                    version = await context.SendAsync(
                        $"{RedisCommand.INFO}{RespLiterals.Server}",
                        handler: ServerVersionHandler.Instance).ConfigureAwait(false);
                    if (version is not null) log?.LogInformationAutoConfiguredInfoVersion(new(server!), version);
                }
                catch (RedisServerException)
                {
                    // INFO can be restricted or renamed; an unknown version falls back the way it did
                }
            }

            long? connectionId = null;
            if (context.Raw.CommandMap.IsAvailable(RedisCommand.CLIENT))
            {
                if (log is not null)
                {
                    if (clientName is { Length: > 0 }) log.LogInformationSettingClientName(new(server!), clientName);
                    if (libraryName is { Length: > 0 } || libraryVersion is { Length: > 0 }) log.LogInformationSettingClientLibVer(new(server!));
                }

                await IdentifyAsync(context, clientName, libraryName, libraryVersion).ConfigureAwait(false);

                // ...and what the server calls this connection, which is the only handle on it from
                // outside: CLIENT LIST names it, CLIENT KILL takes it. The shipped handshake asks in the
                // same breath as the rest of the CLIENT work.
                try
                {
                    // one plain round trip, so it doubles as the latency sample the shipped handshake took
                    // from its tracer - `MultiGroupMultiplexer` ranks groups by it, and without a sample
                    // every server reads as "not yet measured"
                    var started = DateTime.UtcNow;
                    connectionId = await context.SendAsync<long>(
                        $"{RedisCommand.CLIENT}{RespLiterals.Id}").ConfigureAwait(false);
                    server?.SetLatency(started);
                    log?.LogInformationAutoConfiguredClientConnectionId(new(server!), connectionId.GetValueOrDefault());
                }
                catch (RedisServerException)
                {
                    // CLIENT ID arrived in 5.0; older or restricted servers simply have no id to give
                }
            }

            if (clientCache is not null)
            {
                await EnableClientTrackingAsync(context, clientCache, protocol).ConfigureAwait(false);
            }

            if (database > 0)
            {
                await context.SendAsync($"{RedisCommand.SELECT}{database}").ConfigureAwait(false);
            }

            return new RespHandshakeResult(protocol, serverType, version, knowServerType, connectionId, roleFromHello, clusterNodes, slots);
        }

        /// <summary>Read the server-wide settings the client models, for a server nothing has described yet.</summary>
        /// <param name="context">A context over the connection to ask on.</param>
        /// <param name="server">The modelled server to tell.</param>
        /// <remarks>
        /// <para>
        /// <b>Discovery, moved one step at a time.</b> These are facts the client holds about a SERVER -
        /// how many databases it has, whether its replicas refuse writes - and today they are read by the
        /// shipped bridge's handshake, which is one of the reasons that bridge must connect at all. Reading
        /// them here is part of removing that reason; see design notes D2.8.
        /// </para>
        /// <para>
        /// <b>Only when nothing has described this server yet</b>, which <c>Databases == 0</c> says exactly:
        /// it is the "not discovered" value <c>ServerEndPoint</c> starts at, and both settings are
        /// discovered together on the shipped path. Asking unconditionally would put two round trips on
        /// every connection this core dials, which on a large cluster is precisely the cost the lazy design
        /// exists to avoid - and they are server-wide answers, so asking twice learns nothing. The effect is
        /// self-adjusting: while the other core still discovers, this does nothing; when it stops, this is
        /// what knows.
        /// </para>
        /// <para>
        /// <b>Not subject to admin mode</b>, because it runs during the dial on a context over the bare
        /// connection, and the admin check lives on the endpoint executor. That is the same exemption the
        /// shipped handshake gets from <c>SetInternalCall</c>, and for the same reason: <c>CONFIG</c> is
        /// restricted because a CALLER should not reconfigure a server by accident, not because the client
        /// may not know how many databases it has.
        /// </para>
        /// </remarks>
        /// <param name="connected">How the connection was made; needed by the maintenance opt-in.</param>
        internal static async Task DiscoverServerConfigAsync(
            RespDatabaseContext context,
            ServerEndPoint server,
            ConnectedTransportFacts connected = default)
        {
            server.Multiplexer.Logger?.LogInformationAutoConfiguring(new(server));
            // FIRST, and outside the gate below, because the two beliefs are independent: a server whose
            // database count somebody has already established can still have no product recorded.
            await DiscoverProductAsync(context, server).ConfigureAwait(false);
            await DiscoverReplicationAsync(context, server, connected.RoleKnown).ConfigureAwait(false);
            await DiscoverTieBreakerAsync(context, server).ConfigureAwait(false);
            await RequestMaintenanceNotificationsAsync(context, server, connected).ConfigureAwait(false);

            if (!context.Raw.CommandMap.IsAvailable(RedisCommand.CONFIG)) return;

            // the server's idle timeout sets how often the heartbeat must write to keep the connection, when
            // the caller did not choose - the shipped auto-configure reads it, and this did not, so under the
            // engine flag `WriteEverySeconds` silently kept its 60s default against a server that might drop
            // idle connections sooner. Same rule as the shipped processor: 20s spare above a minute, else 3/4.
            if (server.Multiplexer.RawConfig.KeepAlive <= 0
                && await ReadSettingAsync(context, "timeout").ConfigureAwait(false) is { } timeout)
            {
                ApplySetting(server, "timeout", timeout);
            }

            if (server.Databases > 0) return;

            // the spelling follows the server's own vocabulary, which changed: "replica" from 5.0, "slave"
            // before it. The shipped handshake picks by the same predicate.
            var readOnlyKey = server.GetFeatures().ReplicaCommands ? "replica-read-only" : "slave-read-only";

            if (await ReadSettingAsync(context, "databases").ConfigureAwait(false) is { } databases)
            {
                ApplySetting(server, "databases", databases);
            }

            if (await ReadSettingAsync(context, readOnlyKey).ConfigureAwait(false) is { } readOnly)
            {
                ApplySetting(server, readOnlyKey, readOnly);
            }
        }

        /// <summary>Record one server setting on the client's model of that server.</summary>
        /// <param name="server">The server the setting was read from.</param>
        /// <param name="name">The setting, as <c>CONFIG GET</c> names it.</param>
        /// <param name="value">Its value.</param>
        /// <returns>Whether the setting is one the client models.</returns>
        /// <remarks>
        /// <b>The one copy of the rules</b>, shared by discovery at connect and by <c>IServer.ConfigSet</c>'s
        /// read-back, which is how a setting a caller changes stays true on the model - the job the shipped
        /// auto-configure processor did for both. Logged under the shipped auto-configure ids.
        /// </remarks>
        internal static bool ApplySetting(ServerEndPoint server, string name, string value)
        {
            var log = server.Multiplexer.Logger;
            switch (name.ToLowerInvariant())
            {
                case "timeout":
                    // how often the heartbeat must write to keep an idle connection: 20s spare above a
                    // minute, three quarters below it - the shipped rule. Zero means the server never drops.
                    // Applied whenever the server says, even over a configured KeepAlive: that setting only
                    // decides whether discovery ASKS, and a server that will drop idle connections sooner
                    // than the configured interval wins - as it always did (HeartbeatTests measures it).
                    if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var timeoutSeconds)
                        && timeoutSeconds > 0)
                    {
                        var targetSeconds = timeoutSeconds >= 60 ? timeoutSeconds - 20 : (timeoutSeconds * 3) / 4;
                        log?.LogInformationAutoConfiguredConfigTimeout(new(server), targetSeconds);
                        server.WriteEverySeconds = targetSeconds;
                    }
                    return true;
                case "databases":
                    if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var count) && count > 0)
                    {
                        log?.LogInformationAutoConfiguredConfigDatabases(new(server), count);
                        server.Databases = count;
                    }
                    return true;
                case "replica-read-only":
                case "slave-read-only":
                    server.ReplicaReadOnly = !string.Equals(value, "no", StringComparison.OrdinalIgnoreCase);
                    log?.LogInformationAutoConfiguredConfigReadOnlyReplica(new(server), server.ReplicaReadOnly);
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>How the connection was made, as far as the maintenance opt-in needs to know.</summary>
        /// <param name="protocol">What the handshake settled on; notifications arrive as pushes.</param>
        /// <param name="remoteAddress">The address actually reached, when it was an IP one.</param>
        /// <param name="isEncrypted">Whether the connection ended up encrypted.</param>
        /// <param name="requestedResp3">Whether RESP3 was asked for, which is not whether it was got.</param>
        /// <param name="roleKnown">Whether <c>HELLO</c> already reported the role, so nothing need probe for it.</param>
        internal readonly struct ConnectedTransportFacts(
            RedisProtocol protocol,
            IPAddress? remoteAddress,
            bool isEncrypted,
            bool requestedResp3 = false,
            bool roleKnown = false)
        {
            /// <summary>Whether the role is already settled, so the <c>SET</c> probe is unnecessary.</summary>
            internal bool RoleKnown { get; } = roleKnown;

            internal RedisProtocol Protocol { get; } = protocol;

            /// <summary>Whether RESP3 was ASKED for, which is not the same as whether it was got.</summary>
            /// <remarks>
            /// The maintenance opt-in is sent on the intent, deliberately - see
            /// <c>RequestMaintenanceNotificationsAsync</c>.
            /// </remarks>
            internal bool RequestedResp3 { get; } = requestedResp3;

            internal IPAddress? RemoteAddress { get; } = remoteAddress;

            internal bool IsEncrypted { get; } = isEncrypted;
        }

        /// <summary>Ask this server to tell us when it is about to disrupt us.</summary>
        /// <param name="context">A context over the connection to ask on.</param>
        /// <param name="server">The server to record the answer against.</param>
        /// <param name="connected">How the connection was made; see the struct.</param>
        /// <remarks>
        /// <para>
        /// <b>The opt-in is the whole feature's gate.</b> Everything downstream - the relaxation windows,
        /// the retention and replay, the migration handling - hangs off a connection that has asked, and a
        /// connection that has not asked is simply never told. So with this core carrying the commands and
        /// only the shipped bridge opting in, the client was being warned on a connection it no longer
        /// used. That is the largest single cluster of failures in the coupled move: six test classes.
        /// </para>
        /// <para>
        /// <b>RESP3 only, because the notifications are pushes</b> - which is why the shipped core asks the
        /// same question of the same three facts (interactive, RESP3, feature enabled, <c>CLIENT</c>
        /// available), and why that decision is borrowed rather than restated here.
        /// </para>
        /// <para>
        /// The reply is read, and that is the point of asking from here rather than writing it blind: "the
        /// server agreed" and "the server declined, and here is why" are different facts that the shipped
        /// core can only distinguish through a result processor. A refusal is ordinary - plenty of
        /// deployments do not offer this - so it is recorded, never thrown.
        /// </para>
        /// </remarks>
        private static async Task RequestMaintenanceNotificationsAsync(
            RespDatabaseContext context, ServerEndPoint server, ConnectedTransportFacts connected)
        {
            // A GROUP MEMBER NEVER ASKS, and has to be told that it is not asking: a caller who wrote
            // maintNotifications=Enabled asked for a guarantee and is not getting it, and the alternative
            // to saying so is a deployment where the feature is silently absent with nothing to explain
            // it. The shipped handshake warns in exactly this position.
            if (server.Multiplexer.IsGroupMember
                && server.Multiplexer.RawConfig.MaintenanceNotifications != MaintenanceNotificationMode.Disabled)
            {
                server.Multiplexer.Logger?.LogWarningMaintenanceNotificationsSuppressedForGroup(
                    new(server), server.Multiplexer.RawConfig.MaintenanceNotifications);
            }

            // ON THE INTENT, not on what was negotiated, which looks like the worse choice and is not.
            // This core knows the protocol by now and could skip a request it can see is pointless - but
            // the shipped core asks whenever RESP3 was requested and settles a downgrade afterwards, and
            // that sequence is observable on the wire and asserted:
            // `MaintenanceOptInClientTests.AutoIsOffWhenTheServerDowngradesToResp2` requires the server to
            // have SEEN the opt-in and the client to disbelieve the acceptance anyway. Diverging here
            // would be an unflagged behaviour change dressed up as an optimisation.
            if (!server.ShouldRequestMaintenanceNotifications(connected.RequestedResp3))
            {
                // ...but a feature that is REQUIRED and could not even be asked for still has to be
                // settled: `Enabled` over a RESP2 connection is a contradiction, not a silent downgrade.
                Reconcile(server, connected.Protocol);
                return;
            }

            server.OnMaintenanceNotificationsRequested();

            var endpointType = server.MaintenanceMovingEndpointTypeLiteral(
                connected.RemoteAddress, connected.IsEncrypted);

            try
            {
                // a bare ON when no preference is configured, which is what the shipped core sends too
                if (endpointType.IsNull)
                {
                    await context.SendAsync(
                        $"{RedisCommand.CLIENT}{RespLiterals.Maint_Notifications}{RedisLiterals.ON}")
                        .ConfigureAwait(false);
                }
                else
                {
                    await context.SendAsync(
                        $"{RedisCommand.CLIENT}{RespLiterals.Maint_Notifications}{RedisLiterals.ON}{RespLiterals.MovingEndpointType}{endpointType}")
                        .ConfigureAwait(false);
                }

                server.OnMaintenanceNotificationsAccepted(null);
            }
            catch (RedisServerException ex)
            {
                // the server does not offer it, or will not right now; whether THAT is a fault is the
                // reconcile's decision, not this one's
                server.OnMaintenanceNotificationsRefused(null, ex.Message);
            }

            Reconcile(server, connected.Protocol);
        }

        /// <summary>Fail the connection when a required feature turned out to be unavailable.</summary>
        /// <param name="server">The server whose answer is being settled.</param>
        /// <param name="protocol">What this connection negotiated.</param>
        /// <remarks>
        /// <b>The shipped core records a connection failure here; this one throws, and that is the same
        /// thing in this core's terms.</b> A handshake that throws fails the connect, which is exactly
        /// what "Enabled means required: no notifications, no connection" asks for - and the decision
        /// itself, including the wording, is the shipped one rather than a second copy.
        /// </remarks>
        private static void Reconcile(ServerEndPoint server, RedisProtocol protocol)
        {
            if (server.ReconcileMaintenanceNotifications(protocol) is { } reason)
            {
                throw new RedisConnectionException(
                    ConnectionFailureType.ProtocolFailure, CommandFlags.None, reason, innerException: null);
            }
        }

        /// <summary>Whether this server replicates another, and which.</summary>
        /// <param name="context">A context over the connection to ask on.</param>
        /// <param name="server">The server to describe.</param>
        /// <param name="roleKnown">Whether <c>HELLO</c> already answered this, so nothing need probe.</param>
        /// <remarks>
        /// <para>
        /// <b>A role is not a preference, it is a routing fact</b>, and the client refuses writes to a
        /// replica on the strength of it. Unlearned, every server looks like a primary:
        /// <c>MultiPrimaryTests.CannotFlushReplica</c> connects to the replica, looks for the server
        /// where <c>IsReplica</c> is true, and finds none.
        /// </para>
        /// <para>
        /// <b>From <c>INFO replication</c>, which is the only source always available.</b> This core does
        /// ask <c>ROLE</c>, and that is better where it applies - a primary lists its replicas, so one
        /// reply describes both sides - but it is asked only when the topology says roles could change a
        /// decision, which with a single configured endpoint they cannot. That is exactly the case here.
        /// <c>HELLO</c> carries a role too, and only under RESP3.
        /// </para>
        /// <para>
        /// Re-read per handshake, like the tie-breaker and unlike the product: a failover changes this,
        /// and a new connection is a reasonable moment to find out. The shipped handshake asks the same
        /// two <c>INFO</c> sections for the same reason.
        /// </para>
        /// </remarks>
        private static async Task DiscoverReplicationAsync(
            RespDatabaseContext context, ServerEndPoint server, bool roleKnown)
        {
            if (!context.Raw.CommandMap.IsAvailable(RedisCommand.INFO))
            {
                // ...and only if nothing has already said. `HELLO` carries the role, so a server that
                // answered one has settled this already and the probe would be a write nobody needs -
                // which matters because the probe needs a KEY, and an ACL can forbid that (#2968).
                if (!roleKnown) await ProbeReplicaAsync(context, server).ConfigureAwait(false);
                return;
            }

            try
            {
                var replication = await context.SendAsync(
                    $"{RedisCommand.INFO}{RespLiterals.Replication}",
                    handler: ReplicationHandler.Instance).ConfigureAwait(false);

                if (replication.IsReplica is { } isReplica)
                {
                    server.IsReplica = isReplica;
                    server.Multiplexer.Logger?.LogInformationAutoConfiguredInfoRole(new(server), isReplica ? "replica" : "primary");
                }
                if (replication.Primary is { } primary) server.PrimaryEndPoint = primary;
            }
            catch (RedisServerException)
            {
                // a restricted INFO leaves the role as it was, which is what it did before this asked
            }
        }

        /// <summary>Find out whether this is a replica by trying to write to it.</summary>
        /// <param name="context">A context over the connection to ask on.</param>
        /// <param name="server">The server to describe.</param>
        /// <remarks>
        /// <para>
        /// <b>The last resort, and sometimes the only one.</b> With <c>HELLO</c>, <c>INFO</c> and
        /// <c>CONFIG</c> all unavailable - which a command map can do, and proxies do - there is nothing
        /// left that will answer "are you a replica?", so the client asks by attempting a write and
        /// reading the refusal. <c>HelloHandshakeTests.ReplicaProbeStillUsedWhenHelloUnavailable</c> is
        /// built on exactly that configuration.
        /// </para>
        /// <para>
        /// <b>Harmless by construction</b>, which is why it is acceptable at all: the value is set only if
        /// absent (<c>NX</c>) and expires in a millisecond (<c>PX 1</c>), and the key is the client's own
        /// unique id. The shipped probe is the same shape, and uses the same value as its own marker so
        /// that anyone watching with <c>MONITOR</c> can see what it was for.
        /// </para>
        /// <para>
        /// <b>Never in a cluster</b>, where a replica answers <c>-MOVED</c> for its primary's slots rather
        /// than <c>-READONLY</c>: no hash tag can make the probe reliable there, so a cluster is left to
        /// the slot map that already describes it.
        /// </para>
        /// </remarks>
        private static async Task ProbeReplicaAsync(RespDatabaseContext context, ServerEndPoint server)
        {
            if (server.ServerType == ServerType.Cluster) return;
            if (!context.Raw.CommandMap.IsAvailable(RedisCommand.SET)) return;

            try
            {
                await context.WithDatabase(0).Strings
                    .SetAsync(
                        server.Multiplexer.UniqueId,
                        RedisLiterals.replica_read_only,
                        expiry: TimeSpan.FromMilliseconds(1),
                        when: When.NotExists)
                    .ConfigureAwait(false);

                server.IsReplica = false;
            }
            catch (RedisServerException ex) when (ex.Kind == RedisErrorKind.ReadOnly)
            {
                // the refusal IS the answer
                server.Multiplexer.Logger?.LogInformationAutoConfiguredRoleReplica(new(server));
                server.IsReplica = true;
            }
            catch (RedisServerException)
            {
                // anything else says nothing about the role, so the role stays as it was
            }
        }

        /// <summary>What <c>INFO replication</c> said about this server's side of the pair.</summary>
        private readonly struct ReplicationReply(bool? isReplica, EndPoint? primary)
        {
            internal bool? IsReplica { get; } = isReplica;

            internal EndPoint? Primary { get; } = primary;
        }

        /// <summary>Reads <c>role</c>, and the primary it names when this is a replica.</summary>
        /// <remarks>
        /// <c>master_host</c> and <c>master_port</c> arrive in the same section as <c>role</c>, which is
        /// why they are read together rather than asked for separately - the shipped processor notes the
        /// same adjacency.
        /// </remarks>
        private sealed class ReplicationHandler : IRespHandler<ReplicationReply>
        {
            internal static readonly ReplicationHandler Instance = new();

            public ReplicationReply Parse(ref RespReader reader)
            {
                if (!reader.IsScalar) return default;

                var info = reader.ReadString();
                if (string.IsNullOrEmpty(info)) return default;

                bool? isReplica = null;
                string? host = null, port = null;

                using var lines = new StringReader(info!);
                while (lines.ReadLine() is { } line)
                {
                    if (string.IsNullOrWhiteSpace(line) || line.StartsWith("# ", StringComparison.Ordinal)) continue;

                    var split = line.IndexOf(':');
                    if (split < 0) continue;
                    if (!AutoConfigureInfoFieldMetadata.TryParse(line.AsSpan(0, split), out var field)) continue;

                    var value = line.AsSpan(split + 1).Trim();
                    switch (field)
                    {
                        case AutoConfigureInfoField.Role:
                            if (KnownRoleMetadata.TryParse(value, out var replica)) isReplica = replica;
                            break;
                        case AutoConfigureInfoField.MasterHost:
                            host = value.ToString();
                            break;
                        case AutoConfigureInfoField.MasterPort:
                            port = value.ToString();
                            break;
                    }
                }

                // only when the section actually named one: a primary reports no master_host at all
                EndPoint? primary = null;
                if (host is { Length: > 0 } && Format.TryParseEndPoint(host, port, out var parsed))
                {
                    primary = parsed;
                }

                return new ReplicationReply(isReplica, primary);
            }
        }

        /// <summary>Who this server says should be primary.</summary>
        /// <param name="context">A context over the connection to ask on.</param>
        /// <param name="server">The server to describe.</param>
        /// <remarks>
        /// <para>
        /// <b>The deciding vote in the primary election, and without it there is no election.</b> With
        /// several candidate primaries the client reads an agreed key from each and believes the answer
        /// they agree on; a client that never reads it logs "had no tiebreaker set" against every
        /// endpoint and then "No primaries detected" - which is what <c>MultiPrimaryTests</c> measures,
        /// ten tests of it.
        /// </para>
        /// <para>
        /// Re-read on every handshake rather than once, unlike the product: a tie-breaker is mutable
        /// state on the server and changing it is precisely how an operator moves the election. Caching
        /// it would make this client ignore the next change.
        /// </para>
        /// <para>
        /// Same three conditions the shipped handshake applies - not a cluster (where slots decide and
        /// there is nothing to elect), a tie-breaker actually configured, and <c>GET</c> available - and
        /// the same tolerance: a deployment that restricts <c>GET</c> loses the tie-breaker benefit
        /// rather than the connection.
        /// </para>
        /// </remarks>
        private static async Task DiscoverTieBreakerAsync(RespDatabaseContext context, ServerEndPoint server)
        {
            if (server.ServerType == ServerType.Cluster) return;
            if (!server.Multiplexer.RawConfig.TryGetTieBreaker(out var key)) return;
            if (!context.Raw.CommandMap.IsAvailable(RedisCommand.GET)) return;

            server.Multiplexer.Logger?.LogInformationRequestingTieBreak(new(server.EndPoint), key);
            try
            {
                // DATABASE ZERO explicitly, because a tie-breaker is a key and the context asking may
                // name no database at all - a server context carries -1, and `GET` without a database
                // is refused before it reaches a socket ("A target database is required for GET"). The
                // shipped message hard-codes 0 for the same reason.
                var elected = await context.WithDatabase(0).Strings.GetAsync(key).ConfigureAwait(false);
                server.TieBreakerResult = elected.IsNull ? null : (string?)elected;
            }
            catch (RedisServerException)
            {
                // a restricted or renamed GET costs the tie-breaker, not the connection
            }
        }

        /// <summary>Which product this is - Redis, Valkey, Garnet and the rest - and its own version.</summary>
        /// <param name="context">A context over the connection to ask on.</param>
        /// <param name="server">The server to describe.</param>
        /// <remarks>
        /// <para>
        /// <b>Not a cosmetic label: it changes routing rules.</b> <c>ServerEndPoint</c> decides whether a
        /// cluster supports multiple databases from this - Valkey does, Redis does not - so a client that
        /// never learns the product applies the wrong rule to a real deployment.
        /// </para>
        /// <para>
        /// <b>From <c>INFO</c>, not from <c>HELLO</c>, which is deliberate and matches the shipped core.</b>
        /// <c>HELLO</c> does carry a <c>server</c> field, and reading it would be free - but it names the
        /// product without its product version, and the version is half of what is being established. The
        /// variant is decided across the whole reply rather than per line, because both
        /// <c>redis_version</c> and <c>valkey_version</c> can be present in either order; that is the
        /// shipped processor's rule and this reuses its field table rather than growing a second copy.
        /// </para>
        /// <para>
        /// Asked once per server: an empty recorded product version means nobody has described this one
        /// yet, which is the same "has anyone said?" gate the database count uses. See design notes 9k.
        /// </para>
        /// </remarks>
        private static async Task DiscoverProductAsync(RespDatabaseContext context, ServerEndPoint server)
        {
            _ = server.GetProductVariant(out var described);
            if (!string.IsNullOrEmpty(described)) return;
            if (!context.Raw.CommandMap.IsAvailable(RedisCommand.INFO)) return;

            try
            {
                var product = await context.SendAsync(
                    $"{RedisCommand.INFO}{RespLiterals.Server}",
                    handler: ProductHandler.Instance).ConfigureAwait(false);

                if (product.ProductVersion is { Length: > 0 })
                {
                    server.SetProductVariant(product.Variant, product.ProductVersion);
                }

                // ...and the mode, with the same guard `Publish` uses: a sentinel or a proxy is not
                // something an INFO section gets to overrule.
                if (product.ServerType is { } mode
                    && server.ServerType is not (ServerType.Sentinel or ServerType.Twemproxy))
                {
                    server.ServerType = mode;
                }
            }
            catch (RedisServerException)
            {
                // INFO can be restricted or renamed; an undescribed product behaves as it did before
            }
        }

        /// <summary>What <c>INFO server</c> said this product is.</summary>
        private readonly struct ProductReply(ProductVariant variant, string productVersion, ServerType? serverType)
        {
            internal ProductVariant Variant { get; } = variant;

            internal string ProductVersion { get; } = productVersion;

            /// <summary>What the section called this server's mode, when it named one.</summary>
            /// <remarks>
            /// <b>Read from the same reply because it is in it</b>, and because <c>HELLO</c>'s <c>mode</c>
            /// is not always there to ask: a server with no <c>HELLO</c> is exactly the case that needs
            /// this, and a product that spells the field <c>server_mode</c> rather than <c>redis_mode</c>
            /// is another - <c>ValkeyUnitTests</c> is built on both at once, and read as Standalone.
            /// </remarks>
            internal ServerType? ServerType { get; } = serverType;
        }

        /// <summary>Reads the product and its version out of an <c>INFO server</c> reply.</summary>
        /// <remarks>
        /// Decodes the section and walks it a line at a time, which is what the shipped processor does with
        /// the same reply - and the point is to go through <c>AutoConfigureInfoFieldMetadata</c> so the
        /// field names have one spelling rather than two.
        /// </remarks>
        private sealed class ProductHandler : IRespHandler<ProductReply>
        {
            internal static readonly ProductHandler Instance = new();

            public ProductReply Parse(ref RespReader reader)
            {
                if (!reader.IsScalar) return default;

                var info = reader.ReadString();
                if (string.IsNullOrEmpty(info)) return default;

                var variant = ProductVariant.Redis;
                var productVersion = "";
                ServerType? serverType = null;

                using var lines = new StringReader(info!);
                while (lines.ReadLine() is { } line)
                {
                    if (string.IsNullOrWhiteSpace(line) || line.StartsWith("# ", StringComparison.Ordinal)) continue;

                    var split = line.IndexOf(':');
                    if (split < 0) continue;
                    if (!AutoConfigureInfoFieldMetadata.TryParse(line.AsSpan(0, split), out var field)) continue;

                    var value = line.AsSpan(split + 1).Trim();
                    switch (field)
                    {
                        case AutoConfigureInfoField.RedisVersion when variant is ProductVariant.Redis:
                            // only while nothing better has been seen: a Valkey server reports both
                            productVersion = value.ToString();
                            break;
                        case AutoConfigureInfoField.GarnetVersion:
                            variant = ProductVariant.Garnet;
                            productVersion = value.ToString();
                            break;
                        case AutoConfigureInfoField.ValkeyVersion:
                            variant = ProductVariant.Valkey;
                            productVersion = value.ToString();
                            break;
                        case AutoConfigureInfoField.DragonflyVersion:
                            variant = ProductVariant.Dragonfly;
                            productVersion = value.ToString();
                            break;
                        case AutoConfigureInfoField.MemuraiVersion:
                            variant = ProductVariant.Memurai;
                            productVersion = value.ToString();
                            break;
                        case AutoConfigureInfoField.RedictVersion:
                            variant = ProductVariant.Redict;
                            productVersion = value.ToString();
                            break;
                        case AutoConfigureInfoField.Executable when value.EndsWith("/keydb-server".AsSpan(), StringComparison.Ordinal):
                            variant = ProductVariant.KeyDB;
                            break;
                        case AutoConfigureInfoField.RedisMode:
                        case AutoConfigureInfoField.ServerMode:
                            // both spellings, which is the shipped processor's rule and not a guess
                            if (ServerTypeMetadata.TryParse(value, out var mode)) serverType = mode;
                            break;
                    }
                }

                return new ProductReply(variant, productVersion, serverType);
            }
        }

        /// <summary>One setting from <c>CONFIG GET</c>, or null when the server declined to say.</summary>
        /// <param name="context">A context over the connection to ask on.</param>
        /// <param name="setting">The setting's name.</param>
        /// <remarks>
        /// A declined <c>CONFIG</c> is ordinary - it is restricted on plenty of managed deployments - so
        /// this answers null rather than failing the connection over a fact the client has a default for.
        /// </remarks>
        private static async Task<string?> ReadSettingAsync(RespDatabaseContext context, string setting)
        {
            try
            {
                return await context.SendAsync(
                    $"{RedisCommand.CONFIG}{RespLiterals.Get}{setting.AsRedisValue()}",
                    handler: ConfigSettingHandler.Instance).ConfigureAwait(false);
            }
            catch (RedisServerException)
            {
                return null;
            }
        }

        /// <summary>Reads the value of a single-setting <c>CONFIG GET</c>.</summary>
        /// <remarks>
        /// <b>One reader for both protocols.</b> RESP3 answers a map and RESP2 a flat array, which differ
        /// in their header and not in their contents - name then value - so walking the elements rather
        /// than asserting a shape handles both. An empty reply means the server has no such setting, which
        /// is not an error.
        /// </remarks>
        private sealed class ConfigSettingHandler : IRespHandler<string?>
        {
            internal static readonly ConfigSettingHandler Instance = new();

            public string? Parse(ref RespReader reader)
            {
                if (!reader.IsAggregate) return null;

                // name, then value; anything else is a server that answered a different question
                return reader.TryMoveNext() && reader.IsScalar && reader.TryMoveNext() && reader.IsScalar
                    ? reader.ReadString()
                    : null;
            }
        }

        /// <summary>Tell the server who is calling: the client's name, and the library and version.</summary>
        /// <param name="context">The connection being brought up.</param>
        /// <param name="clientName">The connection name, already sanitised, or empty for none.</param>
        /// <param name="libraryName">The library name to report, or empty for none.</param>
        /// <param name="libraryVersion">The library version to report, or empty for none.</param>
        /// <remarks>
        /// <b>Every one of these is a diagnostic, so none of them may fail the handshake.</b> They are what
        /// somebody reads out of <c>CLIENT LIST</c> at three in the morning to find out which application is
        /// holding a connection - valuable, and not worth a connection over. <c>CLIENT</c> can be renamed or
        /// disabled, <c>SETINFO</c> arrived in 7.2, and an old server answers it with an error; all of that
        /// is ordinary and each step is tried independently so that one decline does not lose the others.
        /// </remarks>
        private static async Task IdentifyAsync(
            RespDatabaseContext context, string? clientName, string? libraryName, string? libraryVersion)
        {
            if (clientName is { Length: > 0 })
            {
                await TellAsync(context, RedisLiterals.SETNAME, default, clientName).ConfigureAwait(false);
            }

            if (libraryName is { Length: > 0 })
            {
                await TellAsync(context, RedisLiterals.SETINFO, RedisLiterals.lib_name, libraryName).ConfigureAwait(false);
            }

            if (libraryVersion is { Length: > 0 })
            {
                await TellAsync(context, RedisLiterals.SETINFO, RedisLiterals.lib_ver, libraryVersion).ConfigureAwait(false);
            }

            static async Task TellAsync(RespDatabaseContext context, RedisValue verb, RedisValue field, string value)
            {
                try
                {
                    if (field.IsNull)
                    {
                        await context.SendAsync($"{RedisCommand.CLIENT}{verb}{(RedisValue)value}").ConfigureAwait(false);
                    }
                    else
                    {
                        await context.SendAsync($"{RedisCommand.CLIENT}{verb}{field}{(RedisValue)value}").ConfigureAwait(false);
                    }
                }
                catch (RedisServerException)
                {
                    // declined; see the remarks - a nameless connection still works
                }
            }
        }

        /// <summary>Ask the server to tell this connection when the keys it reads change.</summary>
        /// <param name="context">The connection being brought up.</param>
        /// <param name="cache">The cache the invalidations are for.</param>
        /// <param name="protocol">What the handshake settled on; tracking needs RESP3.</param>
        /// <remarks>
        /// <para>
        /// <b>It has to be THIS connection, which is the whole reason this exists here.</b> Invalidations
        /// arrive as out-of-band pushes on the connection that asked for them, and in per-key mode the
        /// server registers what <i>that connection</i> read. Negotiating tracking on the shipped core's
        /// socket while the reads happen on this one gave a cache that was filled and never invalidated:
        /// broadcast mode survived it, because the server pushes regardless of who read, and per-key did
        /// not - `RespInProcTrackingTests.PerKeyTrackingAsksForNoBroadcastAndStillInvalidates` is the
        /// difference made visible.
        /// </para>
        /// <para>
        /// <b>It refuses loudly rather than degrading</b>, as <c>ServerEndPoint.EnableClientTrackingAsync</c>
        /// does: a cache that is filled but never invalidated is silently, durably wrong, so a connection
        /// that cannot establish tracking fails instead of serving stale data.
        /// </para>
        /// </remarks>
        private static async Task EnableClientTrackingAsync(
            RespDatabaseContext context, Caching.RespClientCache cache, RedisProtocol protocol)
        {
            if (protocol != RedisProtocol.Resp3)
            {
                const string Message =
                    "Client-side caching requires RESP3: invalidation arrives as an out-of-band push, which"
                    + " RESP2 cannot deliver on this connection. Set Protocol = RedisProtocol.Resp3, or clear"
                    + " ConfigurationOptions.ClientCache.";
                throw new RedisConnectionException(ConnectionFailureType.ProtocolFailure, CommandFlags.CommandRetryNever, Message);
            }

            var options = cache.Options;
            var prefixes = options.Prefixes;
            var command = RedisCommand.CLIENT;

            // CLIENT TRACKING ON [BCAST] [PREFIX p]...
            if (options.ResolvedTrackingMode == Caching.CacheTrackingMode.Broadcast)
            {
                if (prefixes.Count == 0)
                {
                    await context.SendAsync($"{command}{RedisLiterals.TRACKING}{RedisLiterals.ON}{RedisLiterals.BCAST}")
                        .ConfigureAwait(false);
                }
                else
                {
                    // built as values rather than composed in the interpolation, because the number of
                    // PREFIX pairs is not known until here
                    var args = new RedisValue[3 + (prefixes.Count * 2)];
                    var index = 0;
                    args[index++] = RedisLiterals.TRACKING;
                    args[index++] = RedisLiterals.ON;
                    args[index++] = RedisLiterals.BCAST;
                    foreach (var prefix in prefixes)
                    {
                        args[index++] = RedisLiterals.PREFIX;
                        args[index++] = prefix.AsRedisValue();
                    }

                    await context.SendAsync($"{command}{args}").ConfigureAwait(false);
                }
            }
            else
            {
                await context.SendAsync($"{command}{RedisLiterals.TRACKING}{RedisLiterals.ON}").ConfigureAwait(false);
            }
        }

        /// <summary>Reads the <c>proto</c> field out of a <c>HELLO</c> reply.</summary>
        /// <remarks>
        /// The reply is a RESP3 map or a RESP2 flat array depending on what the server decided, and the
        /// point of reading it is to find out which. So this walks pairs generically rather than
        /// assuming a shape - it is looking for one field, and the surrounding structure is exactly what
        /// is in question.
        /// </remarks>
        /// <summary>The fields of a <c>HELLO</c> reply that change what we do next.</summary>
        /// <param name="proto">The protocol the server agreed to.</param>
        /// <param name="mode">What the server says it is, if it said.</param>
        /// <param name="version">The version it reported, if it did.</param>
        /// <param name="isReplica">What it said this server's role is, when it said.</param>
        /// <remarks>
        /// A struct rather than a tuple: the library must not reference <c>System.ValueTuple</c>, which
        /// would add a facade dependency on the down-level targets - asserted by
        /// <c>SanityCheckTests.ValueTupleNotReferenced</c>, which is how this was caught.
        /// </remarks>
        private readonly struct HelloReply(int proto, ServerType? mode, Version? version, bool? isReplica)
        {
            internal int Proto { get; } = proto;

            internal ServerType? Mode { get; } = mode;

            internal Version? Version { get; } = version;

            /// <summary>What the reply said this server's role is, when it said.</summary>
            internal bool? IsReplica { get; } = isReplica;
        }

        /// <summary>
        /// Reads <c>CLUSTER SLOTS</c> into the ranges this core routes on.
        /// </summary>
        /// <remarks>
        /// <b><c>SLOTS</c> rather than <c>NODES</c>, deliberately.</b> The reply is nested arrays needing no
        /// text parsing, and the <c>NODES</c> parser - <c>ClusterConfiguration</c> - takes a
        /// <c>ServerSelectionStrategy</c>, which is precisely the coupling this exists to remove.
        /// <para>
        /// Each entry is <c>[from, to, [ip, port, id, ...], replica...]</c>: the first host is the primary
        /// for that range and every host after it is a replica of it, which is where a
        /// <see cref="CommandFlags.PreferReplica"/> read for a key is answered from. An entry that cannot be
        /// read is skipped rather than failing the probe - a partial map still routes the slots it knows and
        /// falls back for the rest, where a thrown handshake would leave the core with no map at all.
        /// </para>
        /// </remarks>
        /// <summary>One contiguous run of slots and the endpoint serving it.</summary>
        /// <remarks>
        /// A named struct rather than a tuple: <c>System.ValueTuple</c> is not referenced by this assembly
        /// - <c>SanityChecks.ValueTupleNotReferenced</c> enforces it, and caught this - because the
        /// down-level targets would take a package dependency for it.
        /// </remarks>
        /// <summary>Reads a <c>CLUSTER SLOTS</c> reply into the shipped model.</summary>
        /// <remarks>
        /// <b>A four-line handler over <see cref="ClusterSlotsResult.Parse"/>, which is the point.</b> This
        /// used to be its own ~90-line parser producing a reduced <c>SlotRange</c> - from, to, primary, and
        /// a flat list of replica endpoints - which was enough to route and not enough for anything else.
        /// Two consequences, both real:
        /// <para>
        /// It discarded the node ids while reading past them, so nothing downstream could merge a node that
        /// a reply named differently than we hold it, and <c>ServerEndPoint.ClusterTopology</c> - which
        /// identity-merging and topology ageing are both built on - could not be populated from this core at
        /// all. And it was a second implementation of the placeholder rules, which is how
        /// <c>"?":6379</c> got into the slot map in the first place (9y): the shipped parser had always
        /// refused those, in <c>ResolveEndPoint</c>, by the same test.
        /// </para>
        /// </remarks>
        internal sealed class ClusterSlotsHandler : IRespHandler<ClusterSlotsResult?>
        {
            internal static readonly ClusterSlotsHandler Instance = new();

            public ClusterSlotsResult? Parse(ref RespReader reader) => ClusterSlotsResult.Parse(ref reader);
        }

        private sealed class HelloHandler : IRespHandler<HelloReply>
        {
            internal static readonly HelloHandler Instance = new();

            public HelloReply Parse(ref RespReader reader)
            {
                var proto = 2; // a reply we could not read is not a reason to claim RESP3
                ServerType? mode = null;
                Version? version = null;
                bool? isReplica = null;

                var count = reader.AggregateLength();
                for (var i = 0; i < count; i++)
                {
                    if (!reader.TryMoveNext()) break;
                    var isProto = reader.Is("proto"u8);
                    var isMode = !isProto && reader.Is("mode"u8);
                    var isVersion = !isProto && !isMode && reader.Is("version"u8);
                    var isRole = !isProto && !isMode && !isVersion && reader.Is("role"u8);
                    if (!reader.TryMoveNext()) break;
                    i++;

                    if (isProto && reader.IsScalar && reader.TryReadInt64(out var value))
                    {
                        proto = (int)value;
                    }
                    else if (isVersion && reader.IsScalar)
                    {
                        _ = Format.TryParseVersion(reader.ReadString(), out version);
                    }
                    else if (isRole && reader.IsScalar)
                    {
                        // FREE, and it removes the need to ask: the shipped handshake tracks exactly this
                        // as RoleKnownFromHello, and skips its SET probe when it is set. #2968 is about
                        // that probe needing a key, which ACLs can forbid - so not having to send it is
                        // the point rather than a saving.
                        if (reader.TryGetSpan(out var span) && KnownRoleMetadata.TryParse(span, out var replica))
                        {
                            isReplica = replica;
                        }
                    }
                    else if (isMode && reader.IsScalar)
                    {
                        // "standalone" | "sentinel" | "cluster"; only the last changes routing
                        mode = reader.Is("cluster"u8) ? ServerType.Cluster
                            : reader.Is("sentinel"u8) ? ServerType.Sentinel
                            : ServerType.Standalone;
                    }
                    else
                    {
                        reader.SkipChildren();
                    }
                }

                return new HelloReply(proto, mode, version, isReplica);
            }
        }

        /// <summary>Reads <c>cluster_enabled</c> out of a <c>CLUSTER INFO</c> reply.</summary>
        /// <remarks>
        /// The fallback for a server that had no <c>HELLO</c> to tell us with. A bulk string of
        /// <c>key:value</c> lines, and exactly one line matters.
        /// </remarks>
        /// <summary>What <c>ROLE</c> said: this server's side of the pair, and who is on the other.</summary>
        /// <remarks>
        /// <b>The peers are the point.</b> This core connects on demand, so asking only "what am I?" would
        /// leave every endpoint nothing has yet dialled with no role - and a <see cref="CommandFlags"/>
        /// <c>.DemandReplica</c> refused for that reason would be refusing over laziness rather than over
        /// topology. A primary lists its replicas and a replica names its primary, so one reply describes
        /// both sides.
        /// </remarks>
        internal readonly struct RoleReply(RespEndpointRole role, RespEndpointRole peerRole, List<EndPoint>? peers)
        {
            /// <summary>What the answering server is.</summary>
            internal RespEndpointRole Role { get; } = role;

            /// <summary>What everything in <see cref="Peers"/> is.</summary>
            internal RespEndpointRole PeerRole { get; } = peerRole;

            /// <summary>The servers on the other side of the relationship; null when the reply named none.</summary>
            internal List<EndPoint>? Peers { get; } = peers;
        }

        /// <summary>Reads <c>ROLE</c>: a role, and whoever is on the other side of it.</summary>
        /// <remarks>
        /// Three shapes, and only two are useful here. A primary answers
        /// <c>["master", offset, [[ip, port, offset], ...]]</c> and a replica
        /// <c>["slave", ip, port, state, offset]</c> - note the replica names its primary as two separate
        /// scalars, not as a nested entry, which is why the two are parsed apart rather than shared. A
        /// sentinel answers something else entirely and is left <see cref="RespEndpointRole.Unknown"/>,
        /// which routes as before rather than guessing.
        /// <para>
        /// A reply that stops making sense partway is kept as far as it parsed: the role alone is still
        /// worth having, and it is the half that is certain by then.
        /// </para>
        /// </remarks>
        internal sealed class RoleHandler : IRespHandler<RoleReply>
        {
            internal static readonly RoleHandler Instance = new();

            public RoleReply Parse(ref RespReader reader)
            {
                if (!reader.IsAggregate || reader.IsNull || !reader.TryMoveNext() || !reader.IsScalar)
                {
                    return default;
                }

                // "slave" is the wire spelling and remains so; "replica" is accepted for anything that
                // reports the newer word, since neither is this library's own naming
                if (reader.Is("master"u8)) return ParsePrimary(ref reader);
                if (reader.Is("slave"u8) || reader.Is("replica"u8)) return ParseReplica(ref reader);
                return default;
            }

            private static RoleReply ParsePrimary(ref RespReader reader)
            {
                // offset, then the replicas
                if (!reader.TryMoveNext() || !reader.TryMoveNext() || !reader.IsAggregate)
                {
                    return new RoleReply(RespEndpointRole.Primary, RespEndpointRole.Unknown, null);
                }

                List<EndPoint>? peers = null;
                var count = reader.AggregateLength();
                for (var i = 0; i < count; i++)
                {
                    if (!reader.TryMoveNext()) break;

                    // [ip, port, offset] - and the port is a STRING here, unlike CLUSTER SLOTS
                    var parts = reader.AggregateLength();
                    if (parts < 2 || !reader.TryMoveNext())
                    {
                        reader.SkipChildren();
                        continue;
                    }

                    var host = reader.ReadString();
                    string? port = null;
                    if (reader.TryMoveNext()) port = reader.ReadString();

                    for (var skipped = 2; skipped < parts; skipped++)
                    {
                        if (!reader.TryMoveNext()) break;
                        reader.SkipChildren();
                    }

                    if (!string.IsNullOrEmpty(host) && !string.IsNullOrEmpty(port)
                        && Format.TryParseEndPoint(host + ":" + port, out var peer))
                    {
                        (peers ??= new()).Add(peer);
                    }
                }

                return new RoleReply(RespEndpointRole.Primary, RespEndpointRole.Replica, peers);
            }

            private static RoleReply ParseReplica(ref RespReader reader)
            {
                // the primary's host and port, as two scalars at this level
                if (!reader.TryMoveNext()) return new RoleReply(RespEndpointRole.Replica, RespEndpointRole.Unknown, null);
                var host = reader.ReadString();

                string? port = null;
                if (reader.TryMoveNext()) port = reader.IsScalar ? reader.ReadString() : null;

                List<EndPoint>? peers = null;
                if (!string.IsNullOrEmpty(host) && !string.IsNullOrEmpty(port)
                    && Format.TryParseEndPoint(host + ":" + port, out var primary))
                {
                    peers = new() { primary };
                }

                return new RoleReply(RespEndpointRole.Replica, RespEndpointRole.Primary, peers);
            }
        }

        /// <summary>Reads <c>redis_version</c> out of <c>INFO SERVER</c>.</summary>
        /// <remarks>
        /// <c>redis_version</c> specifically, and not the several other <c>*_version</c> fields a fork or a
        /// proxy may also report: those describe something else, and taking whichever appeared first would
        /// have the client enable commands on the strength of an unrelated number.
        /// </remarks>
        private sealed class ServerVersionHandler : IRespHandler<Version?>
        {
            internal static readonly ServerVersionHandler Instance = new();

            private static ReadOnlySpan<byte> Field => "redis_version:"u8;

            public Version? Parse(ref RespReader reader)
            {
                if (!reader.TryGetSpan(out var span)) return null;

                for (var i = 0; i + Field.Length <= span.Length; i++)
                {
                    if (!span.Slice(i, Field.Length).SequenceEqual(Field)) continue;
                    if (i != 0 && span[i - 1] != (byte)'\n') continue; // mid-line: a longer field name

                    var value = span.Slice(i + Field.Length);
                    var end = value.IndexOf((byte)'\r');
                    if (end < 0) end = value.IndexOf((byte)'\n');
                    if (end >= 0) value = value.Slice(0, end);

#if NETCOREAPP3_1_OR_GREATER
                    var text = System.Text.Encoding.UTF8.GetString(value);
#else
                    var text = System.Text.Encoding.UTF8.GetString(value.ToArray());
#endif
                    return Format.TryParseVersion(text, out var parsed) ? parsed : null;
                }

                return null;
            }
        }

        private sealed class ClusterInfoHandler : IRespHandler<ServerType>
        {
            internal static readonly ClusterInfoHandler Instance = new();

            /// <remarks>
            /// <para>
            /// <b><c>CLUSTER INFO</c> does not report <c>cluster_enabled</c>; <c>INFO</c> does.</b> This
            /// looked for that field, which cannot appear in this reply, so it answered
            /// <see cref="ServerType.Standalone"/> for every server including a cluster - and the handshake
            /// then OVERWROTE a correctly seeded cluster topology with it. The whole core stopped routing
            /// by slot and sent every key to whichever endpoint keyless routing picked, which the server
            /// answered with <c>MOVED</c>.
            /// </para>
            /// <para>
            /// Measured against the test topology: a cluster node answers
            /// <c>cluster_state:ok|cluster_slots_assigned:16384|...</c> with no <c>cluster_enabled</c> line
            /// anywhere, and a standalone answers <c>-ERR This instance has cluster support disabled</c> -
            /// an error, which the caller already turns into <see cref="ServerType.Standalone"/>. So a
            /// successful reply is itself the signal, and <c>cluster_state</c> is what identifies it.
            /// </para>
            /// <para>
            /// <c>cluster_enabled:1</c> is still accepted, for a proxy that answers this in <c>INFO</c>'s
            /// shape rather than the server's.
            /// </para>
            /// </remarks>
            public ServerType Parse(ref RespReader reader)
                => reader.TryGetSpan(out var span)
                    && (Contains(span, "cluster_state:"u8) || Contains(span, "cluster_enabled:1"u8))
                        ? ServerType.Cluster
                        : ServerType.Standalone;

            private static bool Contains(ReadOnlySpan<byte> haystack, ReadOnlySpan<byte> needle)
            {
                for (var i = 0; i + needle.Length <= haystack.Length; i++)
                {
                    if (haystack.Slice(i, needle.Length).SequenceEqual(needle)) return true;
                }

                return false;
            }
        }
    }
}
