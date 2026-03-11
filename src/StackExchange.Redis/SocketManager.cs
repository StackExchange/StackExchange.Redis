using System;
using System.Net;
using System.Net.Sockets;
using Pipelines.Sockets.Unofficial;

namespace StackExchange.Redis
{
    /// <summary>
    /// A SocketManager monitors multiple sockets for availability of data; this is done using
    /// the Socket.Select API and a dedicated reader-thread, which allows for fast responses
    /// even when the system is under ambient load.
    /// </summary>
    [Obsolete("SocketManager is no longer used by StackExchange.Redis")]
    public sealed partial class SocketManager : IDisposable
    {
        /// <summary>
        /// Gets the name of this SocketManager instance.
        /// </summary>
        public string Name { get; }

        /// <summary>
        /// Creates a new <see cref="SocketManager"/> instance.
        /// </summary>
        /// <param name="name">The name for this <see cref="SocketManager"/>.</param>
        public SocketManager(string name)
            : this(name, 0, SocketManagerOptions.None) { }

        /// <summary>
        /// Creates a new <see cref="SocketManager"/> instance.
        /// </summary>
        /// <param name="name">The name for this <see cref="SocketManager"/>.</param>
        /// <param name="useHighPrioritySocketThreads">Whether this <see cref="SocketManager"/> should use high priority sockets.</param>
        public SocketManager(string name, bool useHighPrioritySocketThreads)
            : this(name, 0, UseHighPrioritySocketThreads(useHighPrioritySocketThreads)) { }

        /// <summary>
        /// Creates a new (optionally named) <see cref="SocketManager"/> instance.
        /// </summary>
        /// <param name="name">The name for this <see cref="SocketManager"/>.</param>
        /// <param name="workerCount">the number of dedicated workers for this <see cref="SocketManager"/>.</param>
        /// <param name="useHighPrioritySocketThreads">Whether this <see cref="SocketManager"/> should use high priority sockets.</param>
        public SocketManager(string name, int workerCount, bool useHighPrioritySocketThreads)
            : this(name, workerCount, UseHighPrioritySocketThreads(useHighPrioritySocketThreads)) { }

        private static SocketManagerOptions UseHighPrioritySocketThreads(bool value)
            => value ? SocketManagerOptions.UseHighPrioritySocketThreads : SocketManagerOptions.None;

        /// <summary>
        /// Additional options for configuring the socket manager.
        /// </summary>
        [Flags]
        public enum SocketManagerOptions
        {
            /// <summary>
            /// No additional options.
            /// </summary>
            None = 0,

            /// <summary>
            /// Whether the <see cref="SocketManager"/> should use high priority sockets.
            /// </summary>
            UseHighPrioritySocketThreads = 1 << 0,

            /// <summary>
            /// Use the regular thread-pool for all scheduling.
            /// </summary>
            UseThreadPool = 1 << 1,
        }

        /// <summary>
        /// Creates a new (optionally named) <see cref="SocketManager"/> instance.
        /// </summary>
        /// <param name="name">The name for this <see cref="SocketManager"/>.</param>
        /// <param name="workerCount">The number of dedicated workers for this <see cref="SocketManager"/>.</param>
        /// <param name="options">Options to use when creating the socket manager.</param>
        public SocketManager(string? name = null, int workerCount = 0, SocketManagerOptions options = SocketManagerOptions.None)
        {
            if (name.IsNullOrWhiteSpace()) name = GetType().Name;
            Name = name;
            _ = workerCount;
            _ = options;
        }

        /// <summary>
        /// Default / shared socket manager using a dedicated thread-pool.
        /// </summary>
        public static SocketManager Shared => ThreadPool;

        /// <summary>
        /// Shared socket manager using the main thread-pool.
        /// </summary>
        public static SocketManager ThreadPool { get; } = new("ThreadPoolSocketManager", options: SocketManagerOptions.UseThreadPool);

        /// <summary>
        /// Returns a string that represents the current object.
        /// </summary>
        /// <returns>A string that represents the current object.</returns>
        public override string ToString() => Name;

        /// <summary>
        /// Releases all resources associated with this instance.
        /// </summary>
        public void Dispose() { }
    }
}
