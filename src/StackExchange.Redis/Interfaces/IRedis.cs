using System;

namespace StackExchange.Redis
{
    /// <summary>
    /// Common operations available to all redis connections.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <see cref="IRespTarget"/> used to be inherited here - one member, in one place - and has moved down
    /// to <c>IDatabase</c>, <c>IServer</c> and <c>ISubscriber</c> individually, because they do not want
    /// the <i>same</i> member. Inheriting it here gave all three the keyspace command groups, so
    /// <c>server.Strings.Get(key)</c> compiled: an offer an <c>IServer</c> has no business making. Each now
    /// takes the narrower target that says which groups it actually has.
    /// </para>
    /// <para>
    /// The intent behind it is unchanged, and is still the reason it exists: that member is the LAST
    /// addition to these interfaces - once a context is reachable, new surface hangs off it as extension
    /// members and breaks nobody. See design notes section 9.4.
    /// </para>
    /// </remarks>
    public partial interface IRedis : IRedisAsync
    {
        /// <summary>
        /// This command is often used to test if a connection is still alive, or to measure latency.
        /// </summary>
        /// <param name="flags">The command flags to use when pinging.</param>
        /// <returns>The observed latency.</returns>
        /// <remarks><seealso href="https://redis.io/commands/ping"/></remarks>
        TimeSpan Ping(CommandFlags flags = CommandFlags.None);
    }
}
