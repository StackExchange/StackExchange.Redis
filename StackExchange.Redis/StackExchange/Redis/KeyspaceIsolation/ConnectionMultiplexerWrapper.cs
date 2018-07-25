using System;
using System.IO;
using System.Net;
using System.Threading.Tasks;

namespace StackExchange.Redis.KeyspaceIsolation
{
    /// <summary>
    /// Uses prefix to provide specific database and channels keyspace
    /// </summary>
    internal class ConnectionMultiplexerWrapper : IConnectionMultiplexer
    {
        private readonly IConnectionMultiplexer _inner;
        private readonly string _prefix;

        /// <summary>
        /// Initializes a new instance of the <see cref="ConnectionMultiplexerWrapper"/> class.
        /// </summary>
        /// <param name="inner">The multiplexer to wrap</param>
        /// <param name="prefix">The prefix for keys and channel names</param>
        /// <remarks>
        /// The caller is responsible for releasing the wrapped <paramref name="inner"/> connection multiplexer
        /// </remarks>
        public ConnectionMultiplexerWrapper(IConnectionMultiplexer inner, string prefix)
        {
            if (inner == null)
            {
                throw new ArgumentNullException(nameof(inner));
            }

            if (prefix == null)
            {
                throw new ArgumentNullException(nameof(prefix));
            }

            if (prefix.Length == 0)
            {
                throw new ArgumentException("Prefix must not be empty");
            }

            _inner = inner;
            _prefix = prefix;
        }

        public void RegisterProfiler(IProfiler profiler) => _inner.RegisterProfiler(profiler);

        public void BeginProfiling(object forContext) => _inner.BeginProfiling(forContext);

        public ProfiledCommandEnumerable FinishProfiling(object forContext, bool allowCleanupSweep = true) => _inner.FinishProfiling(forContext, allowCleanupSweep);

        public ServerCounters GetCounters() => _inner.GetCounters();

        public EndPoint[] GetEndPoints(bool configuredOnly = false) => _inner.GetEndPoints(configuredOnly);

        public void Wait(Task task) => _inner.Wait(task);

        public T Wait<T>(Task<T> task) => _inner.Wait(task);

        public void WaitAll(params Task[] tasks) => _inner.WaitAll(tasks);

        public int HashSlot(RedisKey key) => _inner.HashSlot(key);

        public ISubscriber GetSubscriber(object asyncState = null) => _inner.GetSubscriber(asyncState).WithChannelPrefix(_prefix);

        public IDatabase GetDatabase(int db = -1, object asyncState = null) => _inner.GetDatabase(db, asyncState).WithKeyPrefix(_prefix);

        public IServer GetServer(string host, int port, object asyncState = null) => _inner.GetServer(host, port, asyncState);

        public IServer GetServer(string hostAndPort, object asyncState = null) => _inner.GetServer(hostAndPort, asyncState);

        public IServer GetServer(IPAddress host, int port) => _inner.GetServer(host, port);

        public IServer GetServer(EndPoint endpoint, object asyncState = null) => _inner.GetServer(endpoint, asyncState);

        public Task<bool> ConfigureAsync(TextWriter log = null) => _inner.ConfigureAsync(log);

        public bool Configure(TextWriter log = null) => _inner.Configure(log);

        public string GetStatus() => _inner.GetStatus();

        public void GetStatus(TextWriter log) => _inner.GetStatus(log);

        public void Close(bool allowCommandsToComplete = true) => _inner.Close(allowCommandsToComplete);

        public Task CloseAsync(bool allowCommandsToComplete = true) => _inner.CloseAsync(allowCommandsToComplete);

        public string GetStormLog() => _inner.GetStormLog();

        public void ResetStormLog() => _inner.ResetStormLog();

        public long PublishReconfigure(CommandFlags flags = CommandFlags.None) => _inner.PublishReconfigure(flags);

        public Task<long> PublishReconfigureAsync(CommandFlags flags = CommandFlags.None) => _inner.PublishReconfigureAsync(flags);

        public bool AllowConnect
        {
            get => _inner.AllowConnect;
            set => _inner.AllowConnect = value;
        }

        public bool IgnoreConnect
        {
            get => _inner.IgnoreConnect;
            set => _inner.IgnoreConnect = value;
        }

        public string ClientName => _inner.ClientName;

        public string Configuration => _inner.Configuration;

        public int TimeoutMilliseconds => _inner.TimeoutMilliseconds;

        public long OperationCount => _inner.OperationCount;

        public bool PreserveAsyncOrder
        {
            get => _inner.PreserveAsyncOrder;
            set => _inner.PreserveAsyncOrder = value;
        }

        public bool IsConnected => _inner.IsConnected;

        public bool IsConnecting  => _inner.IsConnecting;

        public bool IncludeDetailInExceptions
        {
            get => _inner.IncludeDetailInExceptions;
            set => _inner.IncludeDetailInExceptions = value;
        }

        public int StormLogThreshold
        {
            get => _inner.StormLogThreshold;
            set => _inner.StormLogThreshold = value;
        }

        public event EventHandler<RedisErrorEventArgs> ErrorMessage
        {
            add => _inner.ErrorMessage += value;
            remove => _inner.ErrorMessage -= value;
        }

        public event EventHandler<ConnectionFailedEventArgs> ConnectionFailed
        {
            add => _inner.ConnectionFailed += value;
            remove => _inner.ConnectionFailed -= value;
        }

        public event EventHandler<InternalErrorEventArgs> InternalError
        {
            add => _inner.InternalError += value;
            remove => _inner.InternalError -= value;
        }

        public event EventHandler<ConnectionFailedEventArgs> ConnectionRestored
        {
            add => _inner.ConnectionRestored += value;
            remove => _inner.ConnectionRestored -= value;
        }

        public event EventHandler<EndPointEventArgs> ConfigurationChanged
        {
            add => _inner.ConfigurationChanged += value;
            remove => _inner.ConfigurationChanged -= value;
        }

        public event EventHandler<EndPointEventArgs> ConfigurationChangedBroadcast
        {
            add => _inner.ConfigurationChangedBroadcast += value;
            remove => _inner.ConfigurationChangedBroadcast -= value;
        }

        public event EventHandler<HashSlotMovedEventArgs> HashSlotMoved
        {
            add => _inner.HashSlotMoved += value;
            remove => _inner.HashSlotMoved -= value;
        }

        public void Dispose()
        {
            // do not dispose wrapped multiplexer - creator is responsible to dispose it
        }
    }
}
