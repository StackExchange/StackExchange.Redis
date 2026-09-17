using System;
using System.Threading;
using System.Threading.Tasks;
using StackExchange.Redis.Interpolated;

namespace StackExchange.Redis;

/// <summary>
/// The server commands: <c>server.Server.DatabaseSizeAsync(0)</c>.
/// </summary>
/// <remarks>
/// Here rather than in a single surface-wide class, so that a group is one place. The extension methods
/// bind by namespace, and the namespace is <c>StackExchange.Redis</c>, so this costs a caller nothing.
/// </remarks>
public static partial class Server
{
    /// <summary>DBSIZE: how many keys a database holds.</summary>
    /// <param name="server">The server command group.</param>
    /// <param name="database">The database to count; required, see the remarks.</param>
    /// <param name="flags">Command flags.</param>
    /// <param name="cancellationToken">Cancels the request; only cancellation <i>before</i> the send is honoured today.</param>
    /// <remarks>
    /// <para>
    /// <b>The database is not optional here</b>, where <c>IServer.DatabaseSize</c> defaults it to
    /// <c>-1</c> and resolves that against the multiplexer's configured default. A server context carries
    /// no database at all, so the choice was between teaching a sentinel to resolve itself and asking the
    /// caller which database they mean. Asking is the model <c>IServer</c>'s own database-scoped members
    /// follow, and "which database did that number come from" is not a question worth leaving a reader.
    /// </para>
    /// <para>
    /// <b>It is a real routing change, not a label.</b> <c>DBSIZE</c> takes no operand - it answers about
    /// the connection's current database - so the count depends on the <c>SELECT</c> the pipeline
    /// applies, which is why this moves the context with <see cref="RespContext.WithDatabase"/> rather
    /// than writing a different number down. That method only started doing so yesterday; before the fix
    /// this command would have counted whichever database the context already had.
    /// </para>
    /// </remarks>
    public static ValueTask<long> DatabaseSizeAsync(this in RespServer server, int database, CommandFlags flags = CommandFlags.None, CancellationToken cancellationToken = default)
    {
        if (database < 0) throw new ArgumentOutOfRangeException(nameof(database), "A database is required; a server context has none of its own.");

        return server.Context.WithDatabase(database).SendAsync<long>(
            $"{RedisCommand.DBSIZE}", flags, cancellationToken: cancellationToken);
    }
}
