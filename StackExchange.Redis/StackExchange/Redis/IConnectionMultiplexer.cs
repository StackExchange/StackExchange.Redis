using System;
using System.IO;
using System.Net;
using System.Threading.Tasks;

namespace StackExchange.Redis
{
    /// <summary>
    /// Represents the abstract multiplexer API
    /// </summary>
    public interface IConnectionMultiplexer
    {

#if DEBUG
        /// <summary>
        /// For debugging; when not enabled, servers cannot connect
        /// </summary>
        bool AllowConnect { get; set; }

        /// <summary>
        /// For debugging; when not enabled, end-connect is silently ignored (to simulate a long-running connect)
        /// </summary>
        bool IgnoreConnect { get; set; }
#endif
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
        bool PreserveAsyncOrder { get; set; }

        /// <summary>
        /// Indicates whether any servers are connected
        /// </summary>
        bool IsConnected { get; }

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
        /// Sets an IProfiler instance for this ConnectionMultiplexer.
        /// 
        /// An IProfiler instances is used to determine which context to associate an
        /// IProfiledCommand with.  See BeginProfiling(object) and FinishProfiling(object)
        /// for more details.
        /// </summary>
        void RegisterProfiler(IProfiler profiler);

        /// <summary>
        /// Begins profiling for the given context.
        /// 
        /// If the same context object is returned by the registered IProfiler, the IProfiledCommands
        /// will be associated with each other.
        /// 
        /// Call FinishProfiling with the same context to get the assocated commands.
        /// 
        /// Note that forContext cannot be a WeakReference or a WeakReference&lt;T&gt;
        /// </summary>
        void BeginProfiling(object forContext);

        /// <summary>
        /// Stops profiling for the given context, returns all IProfiledCommands associated.
        /// 
        /// By default this may do a sweep for dead profiling contexts, you can disable this by passing "allowCleanupSweep: false".
        /// </summary>
        ProfiledCommandEnumerable FinishProfiling(object forContext, bool allowCleanupSweep = true);

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
        /// this usually means master/slave changes
        /// </summary>
        event EventHandler<EndPointEventArgs> ConfigurationChangedBroadcast;

        /// <summary>
        /// Gets all endpoints defined on the server
        /// </summary>
        /// <returns></returns>
        EndPoint[] GetEndPoints(bool configuredOnly = false);

        /// <summary>
        /// Wait for a given asynchronous operation to complete (or timeout)
        /// </summary>
        void Wait(Task task);

        /// <summary>
        /// Wait for a given asynchronous operation to complete (or timeout)
        /// </summary>
        T Wait<T>(Task<T> task);

        /// <summary>
        /// Wait for the given asynchronous operations to complete (or timeout)
        /// </summary>
        void WaitAll(params Task[] tasks);

        /// <summary>
        /// Raised when a hash-slot has been relocated
        /// </summary>
        event EventHandler<HashSlotMovedEventArgs> HashSlotMoved;

        /// <summary>
        /// Compute the hash-slot of a specified key
        /// </summary>
        int HashSlot(RedisKey key);

        /// <summary>
        /// Obtain a pub/sub subscriber connection to the specified server
        /// </summary>
        ISubscriber GetSubscriber(object asyncState = null);

        /// <summary>
        /// Obtain an interactive connection to a database inside redis
        /// </summary>
        IDatabase GetDatabase(int db = -1, object asyncState = null);

        /// <summary>
        /// Obtain a configuration API for an individual server
        /// </summary>
        IServer GetServer(string host, int port, object asyncState = null);

        /// <summary>
        /// Obtain a configuration API for an individual server
        /// </summary>
        IServer GetServer(string hostAndPort, object asyncState = null);

        /// <summary>
        /// Obtain a configuration API for an individual server
        /// </summary>
        IServer GetServer(IPAddress host, int port);

        /// <summary>
        /// Obtain a configuration API for an individual server
        /// </summary>
        IServer GetServer(EndPoint endpoint, object asyncState = null);

        /// <summary>
        /// Reconfigure the current connections based on the existing configuration
        /// </summary>
        Task<bool> ConfigureAsync(TextWriter log = null);

        /// <summary>
        /// Reconfigure the current connections based on the existing configuration
        /// </summary>
        bool Configure(TextWriter log = null);

        /// <summary>
        /// Provides a text overview of the status of all connections
        /// </summary>
        string GetStatus();

        /// <summary>
        /// Provides a text overview of the status of all connections
        /// </summary>
        void GetStatus(TextWriter log);

        /// <summary>
        /// See Object.ToString()
        /// </summary>
        string ToString();

        /// <summary>
        /// Close all connections and release all resources associated with this object
        /// </summary>
        void Close(bool allowCommandsToComplete = true);

        /// <summary>
        /// Close all connections and release all resources associated with this object
        /// </summary>
        Task CloseAsync(bool allowCommandsToComplete = true);

        /// <summary>
        /// Release all resources associated with this object
        /// </summary>
        void Dispose();

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
        /// <returns>The number of instances known to have received the message (however, the actual number can be higher; returns -1 if the operation is pending)</returns>
        long PublishReconfigure(CommandFlags flags = CommandFlags.None);

        /// <summary>
        /// Request all compatible clients to reconfigure or reconnect
        /// </summary>
        /// <returns>The number of instances known to have received the message (however, the actual number can be higher)</returns>
        Task<long> PublishReconfigureAsync(CommandFlags flags = CommandFlags.None);
    }
}