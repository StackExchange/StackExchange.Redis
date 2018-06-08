using System;
using System.Diagnostics;

namespace StackExchange.Redis
{
    /// <summary>
    /// Common operations available to all redis connections
    /// </summary>
    public partial interface IRedis : IRedisAsync
    {
        /// <summary>
        /// This command is often used to test if a connection is still alive, or to measure latency.
        /// </summary>
        /// <param name="flags">The command flags to use when pinging.</param>
        /// <returns>The observed latency.</returns>
        /// <remarks>https://redis.io/commands/ping</remarks>
        TimeSpan Ping(CommandFlags flags = CommandFlags.None);
    }

    [Conditional("DEBUG")]
    [AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = true)]
    internal class IgnoreNamePrefixAttribute : Attribute
    {
        public IgnoreNamePrefixAttribute(bool ignoreEntireMethod = false)
        {
            IgnoreEntireMethod = ignoreEntireMethod;
        }

        public bool IgnoreEntireMethod { get; }
    }
}