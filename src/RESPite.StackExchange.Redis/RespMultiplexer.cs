using System.Buffers;
using System.Net;
using RESPite.Connections;
using RESPite.Connections.Internal;
using StackExchange.Redis;
using StackExchange.Redis.Maintenance;
using StackExchange.Redis.Profiling;

namespace RESPite.StackExchange.Redis;

public sealed class RespMultiplexer : IConnectionMultiplexer
{
    private readonly RespConnectionManager _connectionManager = new();
    private ConfigurationOptions? _options;
    private string _clientName = "";

    private ConfigurationOptions Options
    {
        get
        {
            return _options ?? ThrowNotConnected();

            static ConfigurationOptions ThrowNotConnected() =>
                throw new InvalidOperationException("Not connected.");
        }
        set
        {
            if (value is null) throw new ArgumentNullException(nameof(Options));
            if (Interlocked.CompareExchange(ref _options, value, null) is not null)
                throw new InvalidOperationException("Options have already been set.");
        }
    }

    /// <inheritdoc cref="object.ToString"/>
    public override string ToString() => GetType().Name;

    public ValueTask DisposeAsync() => _connectionManager.DisposeAsync();

    public void Dispose() => _connectionManager.Dispose();

    public void Connect(string configurationString, TextWriter? log = null)
        => Connect(ConfigurationOptions.Parse(configurationString), log);

    public void Connect(ConfigurationOptions options, TextWriter? log = null)
    {
        Options = options;
        var parsed = ParseOptions(options, out _clientName);
        _connectionManager.Connect(parsed, GetEndpoints(options, out var oversized), log);
        ArrayPool<RespConnectionManager.EndpointPair>.Shared.Return(oversized);
    }

    public Task ConnectAsync(string configurationString, TextWriter? log = null)
        => ConnectAsync(ConfigurationOptions.Parse(configurationString), log);

    public async Task ConnectAsync(ConfigurationOptions options, TextWriter? log = null)
    {
        Options = options;
        var parsed = ParseOptions(options, out _clientName);
        await _connectionManager.ConnectAsync(parsed, GetEndpoints(options, out var oversized), log);
        ArrayPool<RespConnectionManager.EndpointPair>.Shared.Return(oversized);
    }

    private static RespConfiguration ParseOptions(ConfigurationOptions options, out string clientName)
    {
        var config = RespConfiguration.Default.AsBuilder();
        clientName = options.ClientName ?? options.Defaults.ClientName;
        config.SyncTimeout = TimeSpan.FromMilliseconds(options.SyncTimeout);
        config.DefaultDatabase = options.DefaultDatabase ?? 0;
        return config.CreateConfiguration();
    }

    private ReadOnlySpan<RespConnectionManager.EndpointPair> GetEndpoints(
        ConfigurationOptions options,
        out RespConnectionManager.EndpointPair[] oversized)
    {
        oversized = ArrayPool<RespConnectionManager.EndpointPair>.Shared.Rent(Math.Max(options.EndPoints.Count, 1));
        if (options.EndPoints.Count == 0)
        {
            oversized[0] = new("127.0.0.1", 6379);
            return oversized.AsSpan(0, 1);
        }
        else
        {
            int count = 0;
            foreach (var endpoint in options.EndPoints)
            {
                if (!_connectionManager.ConnectionFactory.TryParse(endpoint, out var host, out var port))
                {
                    throw new ArgumentException($"Could not parse host and port from {endpoint}", nameof(endpoint));
                }

                oversized[count++] = new(host, port);
            }

            return oversized.AsSpan(0, count);
        }
    }

    // ReSharper disable once ConvertToAutoProperty
    string IConnectionMultiplexer.ClientName => _clientName;

    string IConnectionMultiplexer.Configuration => Options.ToString(includePassword: false);

    private int SyncTimeoutMilliseconds => Options.SyncTimeout;
    int IConnectionMultiplexer.TimeoutMilliseconds => Options.SyncTimeout;

    long IConnectionMultiplexer.OperationCount => _connectionManager.OperationCount;

    bool IConnectionMultiplexer.PreserveAsyncOrder
    {
        get => false;
        set { }
    }

    public bool IsConnected => _connectionManager.IsConnected;

    bool IConnectionMultiplexer.IsConnecting => _connectionManager.IsConnecting;

    bool IConnectionMultiplexer.IncludeDetailInExceptions
    {
        get => Options.IncludeDetailInExceptions;
        set => Options.IncludeDetailInExceptions = value;
    }

    int IConnectionMultiplexer.StormLogThreshold
    {
        get => 0;
        set { }
    }

    void IConnectionMultiplexer.RegisterProfiler(Func<ProfilingSession?> profilingSessionProvider) { }

    ServerCounters IConnectionMultiplexer.GetCounters() => throw new NotImplementedException();

#pragma warning disable CS0067 // Event is never used
    private event EventHandler<RedisErrorEventArgs>? ErrorMessage;

    private event EventHandler<ConnectionFailedEventArgs>? ConnectionFailed, ConnectionRestored;
    private event EventHandler<InternalErrorEventArgs>? InternalError;
    private event EventHandler<EndPointEventArgs>? ConfigurationChanged, ConfigurationChangedBroadcast;
    private event EventHandler<ServerMaintenanceEvent>? ServerMaintenanceEvent;
    private event EventHandler<HashSlotMovedEventArgs>? HashSlotMoved;
#pragma warning restore CS0067 // Event is never used

    event EventHandler<RedisErrorEventArgs>? IConnectionMultiplexer.ErrorMessage
    {
        add => ErrorMessage += value;
        remove => ErrorMessage -= value;
    }

    event EventHandler<ConnectionFailedEventArgs>? IConnectionMultiplexer.ConnectionFailed
    {
        add => ConnectionFailed += value;
        remove => ConnectionFailed -= value;
    }

    event EventHandler<InternalErrorEventArgs>? IConnectionMultiplexer.InternalError
    {
        add => InternalError += value;
        remove => InternalError -= value;
    }

    event EventHandler<ConnectionFailedEventArgs>? IConnectionMultiplexer.ConnectionRestored
    {
        add => ConnectionRestored += value;
        remove => ConnectionRestored -= value;
    }

    event EventHandler<EndPointEventArgs>? IConnectionMultiplexer.ConfigurationChanged
    {
        add => ConfigurationChanged += value;
        remove => ConfigurationChanged -= value;
    }

    event EventHandler<EndPointEventArgs>? IConnectionMultiplexer.ConfigurationChangedBroadcast
    {
        add => ConfigurationChangedBroadcast += value;
        remove => ConfigurationChangedBroadcast -= value;
    }

    event EventHandler<ServerMaintenanceEvent>? IConnectionMultiplexer.ServerMaintenanceEvent
    {
        add => ServerMaintenanceEvent += value;
        remove => ServerMaintenanceEvent -= value;
    }

    public EndPoint[] GetEndPoints(bool configuredOnly = false)
    {
        throw new NotImplementedException();
    }

    void IConnectionMultiplexer.Wait(Task task)
    {
        if (!task.Wait(SyncTimeoutMilliseconds))
        {
            ThrowTimeout();
        }

        task.GetAwaiter().GetResult();
    }

    private static void ThrowTimeout() => throw new TimeoutException();

    T IConnectionMultiplexer.Wait<T>(Task<T> task)
    {
        if (!task.Wait(SyncTimeoutMilliseconds))
        {
            ThrowTimeout();
        }

        return task.GetAwaiter().GetResult();
    }

    void IConnectionMultiplexer.WaitAll(params Task[] tasks)
    {
        if (!Task.WaitAll(tasks, SyncTimeoutMilliseconds))
        {
            ThrowTimeout();
        }
    }

    event EventHandler<HashSlotMovedEventArgs>? IConnectionMultiplexer.HashSlotMoved
    {
        add => HashSlotMoved += value;
        remove => HashSlotMoved -= value;
    }

    int IConnectionMultiplexer.HashSlot(RedisKey key) => throw new NotImplementedException();

    ISubscriber IConnectionMultiplexer.GetSubscriber(object? asyncState) => throw new NotImplementedException();

    public IDatabase GetDatabase(int db = -1, object? asyncState = null)
    {
        if (db < 0) db = Options.DefaultDatabase ?? 0;
        return new RespContextDatabase(this, _connectionManager, db);
    }

    IServer IConnectionMultiplexer.GetServer(string host, int port, object? asyncState) =>
        GetServer(_connectionManager.GetNode(host, port), asyncState);

    IServer IConnectionMultiplexer.GetServer(string hostAndPort, object? asyncState) =>
        GetServer(_connectionManager.GetNode(hostAndPort), asyncState);

    IServer IConnectionMultiplexer.GetServer(IPAddress host, int port) =>
        GetServer(_connectionManager.GetNode(host.ToString(), port), null);

    public IServer GetServer(EndPoint endpoint, object? asyncState = null)
    {
        if (!_connectionManager.ConnectionFactory.TryParse(endpoint, out var host, out var port))
        {
            throw new ArgumentException($"Could not parse host and port from {endpoint}", nameof(endpoint));
        }

        return GetServer(_connectionManager.GetNode(host, port), asyncState);
    }

    private IServer GetServer(Node node, object? asyncState)
    {
        if (asyncState is not null) ThrowNotSupported();
        if (node.UserObject is not IServer server)
        {
            server = new RespContextServer(this, node);
            node.UserObject = server;
        }

        return server;
        static void ThrowNotSupported() => throw new NotSupportedException($"{nameof(asyncState)} is not supported");
    }

    IServer[] IConnectionMultiplexer.GetServers() => throw new NotImplementedException();

    public Task<bool> ConfigureAsync(TextWriter? log = null) => throw new NotImplementedException();

    public bool Configure(TextWriter? log = null) => throw new NotImplementedException();

    public string GetStatus() => throw new NotImplementedException();

    public void GetStatus(TextWriter log) => throw new NotImplementedException();

    public void Close(bool allowCommandsToComplete = true) => throw new NotImplementedException();

    public Task CloseAsync(bool allowCommandsToComplete = true) => throw new NotImplementedException();

    public string? GetStormLog() => throw new NotImplementedException();

    public void ResetStormLog() => throw new NotImplementedException();

    public long PublishReconfigure(CommandFlags flags = CommandFlags.None) => throw new NotImplementedException();

    public Task<long> PublishReconfigureAsync(CommandFlags flags = CommandFlags.None) =>
        throw new NotImplementedException();

    public int GetHashSlot(RedisKey key) => throw new NotImplementedException();

    public void ExportConfiguration(Stream destination, ExportOptions options = ExportOptions.All) =>
        throw new NotImplementedException();

    public void AddLibraryNameSuffix(string suffix) => throw new NotImplementedException();
}
