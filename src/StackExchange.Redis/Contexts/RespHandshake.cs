using System;
using System.Threading;
using System.Threading.Tasks;
using RESPite.Messages;
using StackExchange.Redis.Protocol;

namespace StackExchange.Redis
{
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
        /// <param name="cancellationToken">Cancels the handshake.</param>
        /// <returns>The protocol the connection actually ended up speaking.</returns>
        internal static async Task<RedisProtocol> PerformAsync(
            RespDatabaseContext context,
            string? user = null,
            string? password = null,
            string? clientName = null,
            int database = 0,
            bool preferResp3 = true,
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
            if (preferResp3)
            {
                try
                {
                    // the server can decline in two ways: an error (HELLO unknown, or the version
                    // refused), or a perfectly successful reply that says proto 2. Both are normal, and
                    // only the reply distinguishes them - which is the thing the old handshake could not
                    // wait for.
                    var negotiated = await context.SendAsync($"{RedisCommand.HELLO}{3}", handler: HelloHandler.Instance)
                        .ConfigureAwait(false);
                    if (negotiated >= 3) protocol = RedisProtocol.Resp3;
                }
                catch (RedisServerException)
                {
                    // declined; RESP2 it is. Not a failure - plenty of servers and proxies have no HELLO.
                }
            }

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

            return protocol;
        }

        /// <summary>Reads the <c>proto</c> field out of a <c>HELLO</c> reply.</summary>
        /// <remarks>
        /// The reply is a RESP3 map or a RESP2 flat array depending on what the server decided, and the
        /// point of reading it is to find out which. So this walks pairs generically rather than
        /// assuming a shape - it is looking for one field, and the surrounding structure is exactly what
        /// is in question.
        /// </remarks>
        private sealed class HelloHandler : IRespHandler<int>
        {
            internal static readonly HelloHandler Instance = new();

            public int Parse(ref RespReader reader)
            {
                var count = reader.AggregateLength();
                for (var i = 0; i < count; i++)
                {
                    if (!reader.TryMoveNext()) break;
                    var isProto = reader.Is("proto"u8);
                    if (!reader.TryMoveNext()) break;
                    i++;

                    if (isProto && reader.IsScalar && reader.TryReadInt64(out var value))
                    {
                        return (int)value;
                    }

                    reader.SkipChildren();
                }

                return 2; // a reply we could not read is not a reason to claim RESP3
            }
        }
    }
}
