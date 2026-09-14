using System;
using StackExchange.Redis.Interpolated;

namespace StackExchange.Redis
{
    /// <summary>
    /// Common operations available to all redis connections.
    /// </summary>
    /// <remarks>
    /// <see cref="IRespTarget"/> is inherited here rather than on each of <c>IDatabase</c>, <c>IServer</c>
    /// and <c>ISubscriber</c>: one member, in one place. That member is intended to be the LAST addition to
    /// these interfaces - once a context is reachable, new surface hangs off it as extension members and
    /// breaks nobody. See design notes section 9.4.
    /// </remarks>
    public partial interface IRedis : IRedisAsync, IRespTarget
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
