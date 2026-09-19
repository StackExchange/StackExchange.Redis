using System;
using System.Threading;
using System.Threading.Tasks;
using RESPite.Messages;
using StackExchange.Redis.Protocol;

namespace StackExchange.Redis
{
    /// <summary>What a handshake established about a connection.</summary>
    /// <param name="protocol">The protocol the connection ended up speaking.</param>
    /// <param name="serverType">What the server turned out to be.</param>
    internal readonly struct RespHandshakeResult(RedisProtocol protocol, ServerType serverType)
    {
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
            if (preferResp3)
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
                }
                catch (RedisServerException)
                {
                    // declined; RESP2 it is. Not a failure - plenty of servers and proxies have no HELLO.
                }
            }

            if (!knowServerType)
            {
                // no HELLO, or a HELLO that did not say. CLUSTER INFO is unambiguous and works on RESP2;
                // a server where CLUSTER is unavailable is, by that very fact, not a cluster
                try
                {
                    serverType = await context.SendAsync(
                        $"{RedisCommand.CLUSTER}{RespLiterals.Info}",
                        handler: ClusterInfoHandler.Instance).ConfigureAwait(false);
                }
                catch (RedisServerException)
                {
                    serverType = ServerType.Standalone;
                }
            }

            // BEFORE the connection is handed back, and that ordering is the point: the endpoint executor
            // publishes the connection and drains its backlog the moment this returns, and a backlog
            // draining against an unset topology is exactly the window that loses per-slot ordering
            topology?.OnServerType(serverType);

            if (clientName is { Length: > 0 })
            {
                try
                {
                    await context.SendAsync($"{RedisCommand.CLIENT}{RedisLiterals.SETNAME}{(RedisValue)clientName}")
                        .ConfigureAwait(false);
                }
                catch (RedisServerException)
                {
                    // CLIENT can be disabled or renamed; a nameless connection still works, and failing
                    // the handshake over a diagnostic nicety would be the wrong trade
                }
            }

            if (database > 0)
            {
                await context.SendAsync($"{RedisCommand.SELECT}{database}").ConfigureAwait(false);
            }

            return new RespHandshakeResult(protocol, serverType);
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
        /// <remarks>
        /// A struct rather than a tuple: the library must not reference <c>System.ValueTuple</c>, which
        /// would add a facade dependency on the down-level targets - asserted by
        /// <c>SanityCheckTests.ValueTupleNotReferenced</c>, which is how this was caught.
        /// </remarks>
        private readonly struct HelloReply(int proto, ServerType? mode)
        {
            internal int Proto { get; } = proto;

            internal ServerType? Mode { get; } = mode;
        }

        private sealed class HelloHandler : IRespHandler<HelloReply>
        {
            internal static readonly HelloHandler Instance = new();

            public HelloReply Parse(ref RespReader reader)
            {
                var proto = 2; // a reply we could not read is not a reason to claim RESP3
                ServerType? mode = null;

                var count = reader.AggregateLength();
                for (var i = 0; i < count; i++)
                {
                    if (!reader.TryMoveNext()) break;
                    var isProto = reader.Is("proto"u8);
                    var isMode = !isProto && reader.Is("mode"u8);
                    if (!reader.TryMoveNext()) break;
                    i++;

                    if (isProto && reader.IsScalar && reader.TryReadInt64(out var value))
                    {
                        proto = (int)value;
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

                return new HelloReply(proto, mode);
            }
        }

        /// <summary>Reads <c>cluster_enabled</c> out of a <c>CLUSTER INFO</c> reply.</summary>
        /// <remarks>
        /// The fallback for a server that had no <c>HELLO</c> to tell us with. A bulk string of
        /// <c>key:value</c> lines, and exactly one line matters.
        /// </remarks>
        private sealed class ClusterInfoHandler : IRespHandler<ServerType>
        {
            internal static readonly ClusterInfoHandler Instance = new();

            public ServerType Parse(ref RespReader reader)
                => reader.TryGetSpan(out var span) && Contains(span, "cluster_enabled:1"u8)
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
