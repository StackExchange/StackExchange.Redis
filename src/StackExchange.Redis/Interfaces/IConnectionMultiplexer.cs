using System;
using System.IO;
using System.Net;
using System.Threading.Tasks;
using StackExchange.Redis.Profiling;

namespace StackExchange.Redis
{
    internal interface IInternalConnectionMultiplexer : IConnectionMultiplexer
    {
        bool AllowConnect { get; set; }

        bool IgnoreConnect { get; set; }

        ReadOnlySpan<ServerEndPoint> GetServerSnapshot();
    }

    /// <summary>
    /// Represents the abstract multiplexer API
    /// </summary>
    public interface IConnectionMultiplexer : IDisposable
    {
        /// <summary>
        /// Gets the client-name that will be used on all new connections
        /// </summary>
        string ClientName { get; }

        /// <summary>
        /// Gets the configuration of the connection
        /// </summary>
        string Configuration { get; }

        /// <summary>
        /// Gets the timeout associated with the connections
        /// </summary>
        int TimeoutMilliseconds { get; }

        /// <summary>
        /// The number of operations that have been performed on all connections
        /// </summary>
        long OperationCount { get; }

        /// <summary>
        /// Gets or sets whether asynchronous operations should be invoked in a way that guarantees their original delivery order
        /// </summary>
        [Obsolete("Not supported; if you require ordered pub/sub, please see " + nameof(ChannelMessageQueue), false)]
        bool PreserveAsyncOrder { get; set; }

        /// <summary>
        /// Indicates whether any servers are connected
        /// </summary>
        bool IsConnected { get; }

        /// <summary>
        /// Indicates whether any servers are connected
        /// </summary>
        bool IsConnecting { get; }

        /// <summary>
        /// Should exceptions include identifiable details? (key names, additional .Data annotations)
        /// </summary>
        bool IncludeDetailInExceptions { get; set; }

        /// <summary>
        /// Limit at which to start recording unusual busy patterns (only one log will be retained at a time;
        /// set to a negative value to disable this feature)
        /// </summary>
        int StormLogThreshold { get; set; }

        /// <summary>
        /// Register a callback to provide an on-demand ambient session provider based on the
        /// calling context; the implementing code is responsible for reliably resolving the same provider
        /// based on ambient context, or returning null to not profile
        /// </summary>
        /// <param name="profilingSessionProvider">The profiling session provider.</param>
        void RegisterProfiler(Func<ProfilingSession> profilingSessionProvider);

        /// <summary>
        /// Get summary statistics associates with this server
        /// </summary>
        ServerCounters GetCounters();

        /// <summary>
        /// A server replied with an error message;
        /// </summary>
        event EventHandler<RedisErrorEventArgs> ErrorMessage;

        /// <summary>
        /// Raised whenever a physical connection fails
        /// </summary>
        event EventHandler<ConnectionFailedEventArgs> ConnectionFailed;

        /// <summary>
        /// Raised whenever an internal error occurs (this is primarily for debugging)
        /// </summary>
        event EventHandler<InternalErrorEventArgs> InternalError;

        /// <summary>
        /// Raised whenever a physical connection is established
        /// </summary>
        event EventHandler<ConnectionFailedEventArgs> ConnectionRestored;

        /// <summary>
        /// Raised when configuration changes are detected
        /// </summary>
        event EventHandler<EndPointEventArgs> ConfigurationChanged;

        /// <summary>
        /// Raised when nodes are explicitly requested to reconfigure via broadcast;
        /// this usually means master/replica changes
        /// </summary>
        event EventHandler<EndPointEventArgs> ConfigurationChangedBroadcast;

        /// <summary>
        /// Gets all endpoints defined on the server
        /// </summary>
        /// <param name="configuredOnly">Whether to return only the explicitly configured endpoints.</param>
        EndPoint[] GetEndPoints(bool configuredOnly = false);

        /// <summary>
        /// Wait for a given asynchronous operation to complete (or timeout)
        /// </summary>
        /// <param name="task">The task to wait on.</param>
        void Wait(Task task);

        /// <summary>
        /// Wait for a given asynchronous operation to complete (or timeout)
        /// </summary>
        /// <typeparam name="T">The type in <paramref name="task"/>.</typeparam>
        /// <param name="task">The task to wait on.</param>
        T Wait<T>(Task<T> task);

        /// <summary>
        /// Wait for the given asynchronous operations to complete (or timeout)
        /// </summary>
        /// <param name="tasks">The tasks to wait on.</param>
        void WaitAll(params Task[] tasks);

        /// <summary>
        /// Raised when a hash-slot has been relocated
        /// </summary>
        event EventHandler<HashSlotMovedEventArgs> HashSlotMoved;

        /// <summary>
        /// Compute the hash-slot of a specified key
        /// </summary>
        /// <param name="key">The key to get a slot ID for.</param>
        int HashSlot(RedisKey key);

        /// <summary>
        /// Obtain a pub/sub subscriber connection to the specified server
        /// </summary>
        /// <param name="asyncState">The async state to pass to the created <see cref="ISubscriber"/>.</param>
        ISubscriber GetSubscriber(object asyncState = null);

        /// <summary>
        /// Obtain an interactive connection to a database inside redis
        /// </summary>
        /// <param name="db">The database ID to get.</param>
        /// <param name="asyncState">The async state to pass to the created <see cref="IDatabase"/>.</param>
        IDatabase GetDatabase(int db = -1, object asyncState = null);

        /// <summary>
        /// Obtain a configuration API for an individual server
        /// </summary>
        /// <param name="host">The host to get a server for.</param>
        /// <param name="port">The specific port for <paramref name="host"/> to get a server for.</param>
        /// <param name="asyncState">The async state to pass to the created <see cref="IServer"/>.</param>
        IServer GetServer(string host, int port, object asyncState = null);

        /// <summary>
        /// Obtain a configuration API for an individual server
        /// </summary>
        /// <param name="hostAndPort">The "host:port" string to get a server for.</param>
        /// <param name="asyncState">The async state to pass to the created <see cref="IServer"/>.</param>
        IServer GetServer(string hostAndPort, object asyncState = null);

        /// <summary>
        /// Obtain a configuration API for an individual server
        /// </summary>
        /// <param name="host">The host to get a server for.</param>
        /// <param name="port">The specific port for <paramref name="host"/> to get a server for.</param>
        IServer GetServer(IPAddress host, int port);

        /// <summary>
        /// Obtain a configuration API for an individual server
        /// </summary>
        /// <param name="endpoint">The endpoint to get a server for.</param>
        /// <param name="asyncState">The async state to pass to the created <see cref="IServer"/>.</param>
        IServer GetServer(EndPoint endpoint, object asyncState = null);

        /// <summary>
        /// Reconfigure the current connections based on the existing configuration
        /// </summary>
        /// <param name="log">The log to write output to.</param>
        Task<bool> ConfigureAsync(TextWriter log = null);

        /// <summary>
        /// Reconfigure the current connections based on the existing configuration
        /// </summary>
        /// <param name="log">The log to write output to.</param>
        bool Configure(TextWriter log = null);

        /// <summary>
        /// Provides a text overview of the status of all connections
        /// </summary>
        string GetStatus();

        /// <summary>
        /// Provides a text overview of the status of all connections
        /// </summary>
        /// <param name="log">The log to write output to.</param>
        void GetStatus(TextWriter log);

        /// <summary>
        /// See Object.ToString()
        /// </summary>
        string ToString();

        /// <summary>
        /// Close all connections and release all resources associated with this object
        /// </summary>
        /// <param name="allowCommandsToComplete">Whether to allow in-queue commadns to complete first.</param>
        void Close(bool allowCommandsToComplete = true);

        /// <summary>
        /// Close all connections and release all resources associated with this object
        /// </summary>
        /// <param name="allowCommandsToComplete">Whether to allow in-queue commadns to complete first.</param>
        Task CloseAsync(bool allowCommandsToComplete = true);

        /// <summary>
        /// Obtains the log of unusual busy patterns
        /// </summary>
        string GetStormLog();

        /// <summary>
        /// Resets the log of unusual busy patterns
        /// </summary>
        void ResetStormLog();

        /// <summary>
        /// Request all compatible clients to reconfigure or reconnect
        /// </summary>
        /// <param name="flags">The command flags to use.</param>
        /// <returns>The number of instances known to have received the message (however, the actual number can be higher; returns -1 if the operation is pending)</returns>
        long PublishReconfigure(CommandFlags flags = CommandFlags.None);

        /// <summary>
        /// Request all compatible clients to reconfigure or reconnect
        /// </summary>
        /// <param name="flags">The command flags to use.</param>
        /// <returns>The number of instances known to have received the message (however, the actual number can be higher)</returns>
        Task<long> PublishReconfigureAsync(CommandFlags flags = CommandFlags.None);

        /// <summary>
        /// Get the hash-slot associated with a given key, if applicable; this can be useful for grouping operations
        /// </summary>
        /// <param name="key">The key to get a the slot for.</param>
        int GetHashSlot(RedisKey key);

        /// <summary>
        /// Write the configuration of all servers to an output stream
        /// </summary>
        /// <param name="destination">The destination stream to write the export to.</param>
        /// <param name="options">The options to use for this export.</param>
        void ExportConfiguration(Stream destination, ExportOptions options = ExportOptions.All);
    }
}
