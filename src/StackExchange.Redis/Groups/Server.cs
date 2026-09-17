using System;
using System.Diagnostics.CodeAnalysis;
using RESPite;
using StackExchange.Redis.Interpolated;

namespace StackExchange.Redis;

/// <summary>
/// EXPERIMENTAL SPIKE. The server-scoped commands.
/// </summary>
/// <remarks>
/// <para>
/// <b>The first group on <see cref="IRespServerTarget"/></b>, which is what that interface was declared
/// for. The keyspace groups deliberately do not reach an <c>IServer</c> - <c>server.Strings.GetAsync(key)</c>
/// must not compile - and this is the counterpart: commands whose answer belongs to the server that was
/// asked, rather than to a key.
/// </para>
/// <para>
/// <b>Empty here, and that is the point</b>, as with every other group: <c>Server.cs</c> holds the group
/// type and the accessor that reaches it, <c>Server.Methods.cs</c> the commands. The accessor cannot live
/// on this class - a member named <c>Server</c> inside a class named <c>Server</c> is <c>CS0542</c> - so
/// it hangs off <see cref="RespServerExtensions"/>, the server-side twin of
/// <see cref="RespDatabaseExtensions"/>.
/// </para>
/// <para>
/// <b>The name cost something to get.</b> A type <c>StackExchange.Redis.Server</c> and a namespace
/// <c>StackExchange.Redis.Server</c> are both reachable as <c>Server</c> from inside
/// <c>StackExchange.Redis</c>, which is <c>CS0435</c> - and the in-process test server had that
/// namespace. It moved to <c>StackExchange.Redis.ManagedServer</c> (assembly and package name unchanged)
/// so that the group could be called what it is, rather than <c>Servers</c> to dodge a collision.
/// </para>
/// </remarks>
public static partial class Server
{
}

/// <summary>
/// EXPERIMENTAL SPIKE. The server command group: <c>server.Server.DatabaseSizeAsync(0)</c>.
/// </summary>
/// <remarks>
/// A group is a context plus a name. This one's context carries database <c>-1</c>, because a server is
/// not database-scoped; the members that do need a database take it explicitly and move the context onto
/// it, which is the model <c>IServer</c> already follows.
/// </remarks>
public readonly struct RespServer
{
    private readonly RespContext _context;

    /// <summary>Group the server commands of a context.</summary>
    /// <param name="context">The context to send through.</param>
    public RespServer(in RespContext context) => _context = context;

    /// <summary>The underlying context.</summary>
    public RespContext Context => _context;
}

/// <summary>
/// EXPERIMENTAL SPIKE. Reaches the server-scoped command groups from a server.
/// </summary>
/// <remarks>
/// The twin of <see cref="RespDatabaseExtensions"/>, and separate from it for the same reason the two
/// target interfaces are separate: what a type offers is what says whether a command makes sense on it.
/// A few group <i>names</i> will end up on both with different members - <c>Keys</c> is key-routed for
/// <c>DEL</c> and server-scoped for <c>KEYS</c>/<c>SCAN</c> - and keeping the accessors apart is what
/// lets that happen without either side inheriting the other's members.
/// </remarks>
public static partial class RespServerExtensions
{
    extension(IRespServerTarget target)
    {
        /// <summary>The server commands.</summary>
        public RespServer Server => new(target.Context);
    }

    extension(in RespContext context)
    {
        /// <summary>The server commands.</summary>
        public RespServer Server => new(context);
    }
}
