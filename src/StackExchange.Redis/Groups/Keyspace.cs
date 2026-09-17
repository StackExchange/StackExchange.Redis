using System;
using System.Threading;
using System.Threading.Tasks;
using RESPite;

namespace StackExchange.Redis;

/// <summary>
/// EXPERIMENTAL SPIKE. The keyspace of one server: the commands that ask about or act on <b>a node's
/// keys as a whole</b>, rather than on a key.
/// </summary>
/// <remarks>
/// <para>
/// <b>Not called <c>Keys</c>, and not by preference.</b> The plan was for the name to appear on both
/// sides - key-routed for <c>DEL</c>, server-scoped for <c>DBSIZE</c> - but <c>IServer</c> has shipped a
/// <i>method</i> called <c>Keys</c> since forever (the <c>KEYS</c>/<c>SCAN</c> enumerator), and a member
/// and an extension property cannot share a name: <c>server.Keys.CountAsync(0)</c> is <c>CS0119</c>.
/// Renaming the shipped method is a binary break, so the group takes the other name - and
/// <c>Keyspace</c> is arguably the better one anyway, because that is what these commands are about:
/// <c>DBSIZE</c>, <c>KEYS</c>, <c>SCAN</c>, <c>FLUSHDB</c>, <c>SWAPDB</c> all describe the whole
/// keyspace of one node rather than any key in it.
/// </para>
/// <para>
/// <b>There is deliberately no <c>Server</c> group.</b> Everything reachable from a server is a server
/// thing by definition, so a group called <c>Server</c> would repeat the receiver and name nothing:
/// <c>server.Server.DatabaseSizeAsync(0)</c> stutters because it carries no information that
/// <c>server.</c> did not already carry. <c>IServer</c> has around seventy members and they fall into
/// families - config, cluster, sentinel, latency, memory, clients, slowlog, replication, scripts - so one
/// <c>Server</c> group would be the flat interface again, one level down. Those families arrive as their
/// own groups; this is the first of them.
/// </para>
/// </remarks>
public readonly struct RespKeyspace
{
    private readonly RespContext _context;

    /// <summary>Group the keyspace commands of a context.</summary>
    /// <param name="context">The context to send through.</param>
    public RespKeyspace(in RespContext context) => _context = context;

    /// <summary>The underlying context.</summary>
    public RespContext Context => _context;
}

/// <summary>
/// EXPERIMENTAL SPIKE. Reaches the server-scoped command groups from a server.
/// </summary>
/// <remarks>
/// <para>
/// The twin of <see cref="RespDatabaseExtensions"/>, and separate from it for the same reason the two
/// target interfaces are: what a type offers is what says whether a command makes sense on it.
/// <c>server.Strings.GetAsync(key)</c> must not compile, and it does not.
/// </para>
/// <para>
/// <b>These accessors hang off <see cref="IRespServerTarget"/> only, never off a bare
/// <see cref="RespContext"/></b> - unlike the keyspace ones. A context does not know whether it is pinned
/// to an endpoint, and two extension properties named <c>Keys</c> on the same receiver would be ambiguous
/// at every call site anyway. Being pinned is what an <c>IServer</c> carries, so that is where the server
/// groups are offered.
/// </para>
/// </remarks>
public static partial class RespServerExtensions
{
    extension<TTarget>(TTarget target) where TTarget : IRespServerTarget
    {
        /// <summary>The keyspace commands of this server.</summary>
        public RespKeyspace Keyspace => new(target.Context);
    }
}

/// <summary>
/// EXPERIMENTAL SPIKE. The keyspace commands: <c>server.Keyspace.CountAsync(0)</c>.
/// </summary>
/// <remarks>
/// Here rather than in a single surface-wide class, so that a group is one place - the same arrangement
/// every keyspace group uses; see <see cref="Keys"/> for the key-routed half of the same subject.
/// </remarks>
public static partial class Keyspace
{
    /// <summary>DBSIZE: how many keys a database holds.</summary>
    /// <param name="keyspace">The keyspace command group.</param>
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
    /// than writing a different number down.
    /// </para>
    /// </remarks>
    public static ValueTask<long> CountAsync(this in RespKeyspace keyspace, int database, CommandFlags flags = CommandFlags.None, CancellationToken cancellationToken = default)
    {
        if (database < 0) throw new ArgumentOutOfRangeException(nameof(database), "A database is required; a server context has none of its own.");

        return keyspace.Context.WithDatabase(database).SendAsync<long>(
            $"{RedisCommand.DBSIZE}", flags, cancellationToken: cancellationToken);
    }
}
