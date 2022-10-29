using System;

namespace StackExchange.Redis
{
    /// <summary>
    /// Common operations available to all redis connections.
    /// </summary>
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
