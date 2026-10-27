using System;
using System.Diagnostics.CodeAnalysis;
using System.Net;
using StackExchange.Redis.Caching;

namespace StackExchange.Redis
{
    /// <summary>
    /// Reaches a <see cref="RespContext"/> directly from a connection.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>A front door, not a shortcut.</b> <c>GetDatabase().Context</c> has always worked and still does;
    /// these exist so that the context surface is findable from the object everybody already has, without
    /// having to know that a database is the thing that carries one.
    /// </para>
    /// <para>
    /// <b>Extension methods rather than interface members</b>, deliberately. <see cref="IConnectionMultiplexer"/>
    /// is the most widely implemented type in this library's ecosystem - test doubles, wrappers, decorators -
    /// and adding a member to it is a binary break for every one of them. An extension breaks nobody and
    /// works on all of them, including implementations this library has never seen.
    /// </para>
    /// <para>
    /// <b>And no type test.</b> The obvious optimisation - sniff for the concrete multiplexer and take a
    /// direct path - would buy nothing: <see cref="IConnectionMultiplexer.GetDatabase"/> hands back a
    /// <i>cached</i> instance for databases 0-16 with no async-state, and that instance memoises its own
    /// context, so the route below is already two cached lookups and no allocation. It would also have to
    /// duplicate the default-database resolution and the proxy check that these methods inherit for free by
    /// going through the real ones - two paths that must agree, which is the failure this whole design
    /// keeps removing. If a genuinely cheaper route ever exists, it can be type-tested here without the
    /// signature changing; nothing is given up by not doing it now.
    /// </para>
    /// </remarks>
    // RS0026 warns that overloads carrying optional parameters can become ambiguous later. Not here: the
    // GetServerContext overloads are told apart by their FIRST parameter - an EndPoint or a host string -
    // and neither has a default, so a call can only bind to one of them however many optionals follow.
    [SuppressMessage("ApiDesign", "RS0026:Do not add multiple overloads with optional parameters", Justification = "Overloads differ in a leading parameter that has no default; see the comment above")]
    public static class RespConnectionExtensions
    {
        /// <summary>
        /// The context for a database on this connection: the entry point to the command groups.
        /// </summary>
        /// <param name="multiplexer">The connection.</param>
        /// <param name="db">The database index; the connection's default when omitted.</param>
        /// <param name="asyncState">The async-state to carry on tasks this context produces.</param>
        /// <remarks>Equivalent to <c>multiplexer.GetDatabase(db, asyncState).Context</c>, which remains available.</remarks>
        public static RespDatabaseContext GetDatabaseContext(this IConnectionMultiplexer multiplexer, int db = -1, object? asyncState = null)
        {
            if (multiplexer is null) throw new ArgumentNullException(nameof(multiplexer));
            return multiplexer.GetDatabase(db, asyncState).Context;
        }

        /// <summary>
        /// The client-side cache of this connection, or <see langword="null"/> if
        /// <see cref="ConfigurationOptions.ClientCache"/> did not ask for one.
        /// </summary>
        /// <param name="multiplexer">The connection.</param>
        /// <remarks>
        /// <para>
        /// Resolved through the default database's context - the same route as
        /// <see cref="GetDatabaseContext"/>, for the same reasons - so on a connection group it is the active
        /// member's cache. That is the right one to hold: an invalidation through it reaches every member, as a
        /// write from this process does, and its counters are the member currently answering reads.
        /// </para>
        /// <para>
        /// On a plain connection, the same instance every time, for the life of the connection. On a group it is
        /// the active member's cache <i>at the time of the call</i>: a view held across a failover still names
        /// the member that was active when it was obtained - its invalidations still reach every member, but its
        /// counters describe that member, not the one now answering reads. There is nothing to dispose.
        /// </para>
        /// </remarks>
        public static ClientCache? GetClientCache(this IConnectionMultiplexer multiplexer)
        {
            if (multiplexer is null) throw new ArgumentNullException(nameof(multiplexer));
            return multiplexer.GetDatabase().Context.Raw.Cache?.View;
        }

        /// <summary>
        /// The context for one server on this connection: the entry point to the server-scoped groups.
        /// </summary>
        /// <param name="multiplexer">The connection.</param>
        /// <param name="endpoint">The server to pin to.</param>
        /// <param name="asyncState">The async-state to carry on tasks this context produces.</param>
        /// <remarks>
        /// Equivalent to <c>multiplexer.GetServer(endpoint, asyncState).Context</c>, and inherits its rules:
        /// an endpoint this connection does not know is an error, and so is asking a proxy that has no
        /// server API.
        /// </remarks>
        public static RespServerContext GetServerContext(this IConnectionMultiplexer multiplexer, EndPoint endpoint, object? asyncState = null)
        {
            if (multiplexer is null) throw new ArgumentNullException(nameof(multiplexer));
            return multiplexer.GetServer(endpoint, asyncState).Context;
        }

        /// <inheritdoc cref="GetServerContext(IConnectionMultiplexer, EndPoint, object?)"/>
        /// <param name="multiplexer">The connection.</param>
        /// <param name="host">The server's host.</param>
        /// <param name="port">The server's port.</param>
        /// <param name="asyncState">The async-state to carry on tasks this context produces.</param>
        public static RespServerContext GetServerContext(this IConnectionMultiplexer multiplexer, string host, int port, object? asyncState = null)
        {
            if (multiplexer is null) throw new ArgumentNullException(nameof(multiplexer));
            return multiplexer.GetServer(host, port, asyncState).Context;
        }
    }
}
