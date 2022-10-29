using System;
using System.Threading.Tasks;

namespace StackExchange.Redis
{
    /// <summary>
    /// Common operations available to all redis connections.
    /// </summary>
    public partial interface IRedisAsync
    {
        /// <summary>
        /// Gets the multiplexer that created this instance.
        /// </summary>
        IConnectionMultiplexer Multiplexer { get; }

        /// <summary>
        /// This command is often used to test if a connection is still alive, or to measure latency.
        /// </summary>
        /// <param name="flags">The command flags to use.</param>
        /// <returns>The observed latency.</returns>
        /// <remarks><seealso href="https://redis.io/commands/ping"/></remarks>
        Task<TimeSpan> PingAsync(CommandFlags flags = CommandFlags.None);

        /// <summary>
        /// Wait for a given asynchronous operation to complete (or timeout), reporting which.
        /// </summary>
        /// <param name="task">The task to wait on.</param>
        bool TryWait(Task task);

        /// <summary>
        /// Wait for a given asynchronous operation to complete (or timeout).
        /// </summary>
        /// <param name="task">The task to wait on.</param>
        void Wait(Task task);

        /// <summary>
        /// Wait for a given asynchronous operation to complete (or timeout).
        /// </summary>
        /// <typeparam name="T">The type of task to wait on.</typeparam>
        /// <param name="task">The task to wait on.</param>
        T Wait<T>(Task<T> task);

        /// <summary>
        /// Wait for the given asynchronous operations to complete (or timeout).
        /// </summary>
        /// <param name="tasks">The tasks to wait on.</param>
        void WaitAll(params Task[] tasks);
    }
}
