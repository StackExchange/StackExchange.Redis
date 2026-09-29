using System;
using System.Collections.Generic;
using System.Globalization;
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
    internal readonly struct RespHandshakeResult(RedisProtocol protocol, ServerType serverType, Version? version = null)
    {
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
        /// <param name="cancellationToken">Cancels the handshake.</param>
        /// <returns>What the connection ended up speaking, and what it turned out to be.</returns>
        internal static async Task<RespHandshakeResult> PerformAsync(
            RespDatabaseContext context,
            string? user = null,
            string? password = null,
            string? clientName = null,
            int database = 0,
            bool preferResp3 = true,
            RespTopology? topology = null,
            EndPoint? endpoint = null,
            Caching.RespClientCache? clientCache = null,
            string? libraryName = null,
            string? libraryVersion = null,
            CancellationToken cancellationToken = default)
        {
            if (password is not null)
            {
                // "" is a legitimate password, for 'nopass' logins - hence null rather than empty as the
                // "no auth needed" signal, matching ConfigurationOptions
                if (user is { Length: > 0 })
                {
                    await context.SendAsync($"{RedisCommand.AUTH}{(RedisValue)user}{(RedisValue)password}").ConfigureAwait(false);
                }
                else
                {
                    await context.SendAsync($"{RedisCommand.AUTH}{(RedisValue)password}").ConfigureAwait(false);
                }
            }

            var protocol = RedisProtocol.Resp2;
            var serverType = ServerType.Standalone;
            var knowServerType = false;
            var clusterInfoDeclined = false;
            Version? version = null;
            // Disabled in the command map counts as "the server will not do this", and has to be answered
            // without sending: a disabled command throws RedisCommandException before it reaches a socket,
            // which is not the RedisServerException these catches expect, so it would escape and fail the
            // whole handshake rather than being the ordinary decline every one of them is written for.
            if (preferResp3 && context.Raw.CommandMap.IsAvailable(RedisCommand.HELLO))
            {
                try
                {
                    // the server can decline in two ways: an error (HELLO unknown, or the version
                    // refused), or a perfectly successful reply that says proto 2. Both are normal, and
                    // only the reply distinguishes them - which is the thing the old handshake could not
                    // wait for.
                    var hello = await context.SendAsync($"{RedisCommand.HELLO}{3}", handler: HelloHandler.Instance)
                        .ConfigureAwait(false);
                    if (hello.Proto >= 3) protocol = RedisProtocol.Resp3;
                    if (hello.Mode is { } mode)
                    {
                        serverType = mode;
                        knowServerType = true;
                    }

                    version = hello.Version;
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
            List<SlotRange>? ranges = null;
            var knowSlots = topology is { HasSlotMap: true, SlotMapSuspect: false };
            if ((serverType == ServerType.Cluster || clusterInfoDeclined)
                && !knowSlots
                && topology is not null
                && context.Raw.CommandMap.IsAvailable(RedisCommand.CLUSTER))
            {
                try
                {
                    ranges = await context.SendAsync(
                        $"{RedisCommand.CLUSTER}{RespLiterals.Slots}",
                        handler: ClusterSlotsHandler.Instance).ConfigureAwait(false);

                    // slots exist, so this is a cluster whatever CLUSTER INFO did or did not say
                    if (ranges.Count != 0) serverType = ServerType.Cluster;
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

            if (ranges is not null && topology is not null)
            {
                foreach (var range in ranges)
                {
                    topology.SetSlotRange(range.From, range.To, range.Endpoint, range.Replicas);
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
                }
                catch (RedisServerException)
                {
                    // INFO can be restricted or renamed; an unknown version falls back the way it did
                }
            }

            if (context.Raw.CommandMap.IsAvailable(RedisCommand.CLIENT))
            {
                await IdentifyAsync(context, clientName, libraryName, libraryVersion).ConfigureAwait(false);
            }

            if (clientCache is not null)
            {
                await EnableClientTrackingAsync(context, clientCache, protocol).ConfigureAwait(false);
            }

            if (database > 0)
            {
                await context.SendAsync($"{RedisCommand.SELECT}{database}").ConfigureAwait(false);
            }

            return new RespHandshakeResult(protocol, serverType, version);
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
        /// <summary>The two fields of a <c>HELLO</c> reply that change what we do next.</summary>
        /// <param name="proto">The protocol the server agreed to.</param>
        /// <param name="mode">What the server says it is, if it said.</param>
        /// <param name="version">The version it reported, if it did.</param>
        /// <remarks>
        /// A struct rather than a tuple: the library must not reference <c>System.ValueTuple</c>, which
        /// would add a facade dependency on the down-level targets - asserted by
        /// <c>SanityCheckTests.ValueTupleNotReferenced</c>, which is how this was caught.
        /// </remarks>
        private readonly struct HelloReply(int proto, ServerType? mode, Version? version)
        {
            internal int Proto { get; } = proto;

            internal ServerType? Mode { get; } = mode;

            internal Version? Version { get; } = version;
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
        internal readonly struct SlotRange(int from, int to, EndPoint endpoint, EndPoint[]? replicas)
        {
            /// <summary>First slot, inclusive.</summary>
            internal int From { get; } = from;

            /// <summary>Last slot, inclusive.</summary>
            internal int To { get; } = to;

            /// <summary>The primary serving the range.</summary>
            internal EndPoint Endpoint { get; } = endpoint;

            /// <summary>The endpoints replicating it; null when the reply listed none.</summary>
            internal EndPoint[]? Replicas { get; } = replicas;
        }

        internal sealed class ClusterSlotsHandler : IRespHandler<List<SlotRange>>
        {
            internal static readonly ClusterSlotsHandler Instance = new();

            public List<SlotRange> Parse(ref RespReader reader)
            {
                var ranges = new List<SlotRange>();
                var entries = reader.AggregateLength();

                for (var i = 0; i < entries; i++)
                {
                    if (!reader.TryMoveNext()) break;
                    var parts = reader.AggregateLength();
                    if (parts < 3)
                    {
                        reader.SkipChildren();
                        continue;
                    }

                    if (!reader.TryMoveNext() || !reader.TryReadInt64(out var from)
                        || !reader.TryMoveNext() || !reader.TryReadInt64(out var to)
                        || !reader.TryMoveNext())
                    {
                        break; // the reply is not the shape it promised; keep whatever parsed cleanly
                    }

                    // the primary for this range: [ip, port, id, ...]
                    var endpoint = TryReadHost(ref reader);

                    // every host after the first replicates it. Read rather than skipped, because this is
                    // the only place the pairing is stated: which replicas serve THESE slots, as opposed to
                    // the flat "is a replica" that INFO gives for one server.
                    List<EndPoint>? replicas = null;
                    for (var part = 3; part < parts; part++)
                    {
                        if (!reader.TryMoveNext()) break;
                        if (TryReadHost(ref reader) is { } replica) (replicas ??= new()).Add(replica);
                    }

                    if (endpoint is not null)
                    {
                        ranges.Add(new SlotRange((int)from, (int)to, endpoint, replicas?.ToArray()));
                    }
                }

                return ranges;
            }

            /// <summary>Read one <c>[ip, port, id, ...]</c> host entry, consuming all of it either way.</summary>
            /// <param name="reader">Positioned on the host entry.</param>
            /// <returns>The endpoint, or null if this entry was not one.</returns>
            /// <remarks>
            /// <b>Consuming all of it is the point, not a detail.</b> A host entry that was half-read leaves
            /// the reader inside an aggregate the caller believes it has passed, and every range after it is
            /// then read from the wrong place - a map that is wrong is far worse than a map that is short,
            /// because nothing downstream can tell.
            /// </remarks>
            private static EndPoint? TryReadHost(ref RespReader reader)
            {
                EndPoint? endpoint = null;
                var hostParts = reader.AggregateLength();
                if (hostParts >= 2 && reader.TryMoveNext())
                {
                    var host = reader.ReadString();
                    if (reader.TryMoveNext() && reader.TryReadInt64(out var port) && !string.IsNullOrEmpty(host))
                    {
                        _ = Format.TryParseEndPoint(host + ":" + port.ToString(CultureInfo.InvariantCulture), out endpoint);
                    }

                    for (var skipped = 2; skipped < hostParts; skipped++)
                    {
                        if (!reader.TryMoveNext()) break;
                        reader.SkipChildren();
                    }
                }
                else
                {
                    reader.SkipChildren();
                }

                return endpoint;
            }
        }

        private sealed class HelloHandler : IRespHandler<HelloReply>
        {
            internal static readonly HelloHandler Instance = new();

            public HelloReply Parse(ref RespReader reader)
            {
                var proto = 2; // a reply we could not read is not a reason to claim RESP3
                ServerType? mode = null;
                Version? version = null;

                var count = reader.AggregateLength();
                for (var i = 0; i < count; i++)
                {
                    if (!reader.TryMoveNext()) break;
                    var isProto = reader.Is("proto"u8);
                    var isMode = !isProto && reader.Is("mode"u8);
                    var isVersion = !isProto && !isMode && reader.Is("version"u8);
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

                return new HelloReply(proto, mode, version);
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
        private sealed class RoleHandler : IRespHandler<RoleReply>
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
