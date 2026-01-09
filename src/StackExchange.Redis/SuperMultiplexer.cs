using System;
using System.Buffers;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Net;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Threading.Tasks;
using StackExchange.Redis.Maintenance;
using StackExchange.Redis.Profiling;

namespace StackExchange.Redis;

internal sealed class SuperMultiplexer : IInternalConnectionMultiplexer, IMessageExecutor
{
    private sealed class WeightedEndpoint(float weight, ConnectionMultiplexer muxer)
    {
        public readonly float Weight = weight;
        public readonly ConnectionMultiplexer Muxer = muxer;
    }

    private readonly object _syncLock = new();
    private readonly List<WeightedEndpoint> _unsorted = new();
    private ConnectionMultiplexer _active = null!;

    public static async Task<SuperMultiplexer> ConnectAsync(ConfigurationOptions configuration, float weight = 1.0f, TextWriter? log = null)
    {
        var result = new SuperMultiplexer();
        await result.AddAsync(configuration, weight, log).ForAwait();
        return result;
    }

    public async Task AddAsync(ConfigurationOptions configuration, float weight = 1.0f, TextWriter? log = null)
    {
        var muxer = await ConnectionMultiplexer.ConnectAsync(configuration, log).ForAwait();
        var weighted = new WeightedEndpoint(weight, muxer);
        lock (_syncLock)
        {
            // ReSharper disable once ConditionIsAlwaysTrueOrFalseAccordingToNullableAPIContract
            _active ??= muxer; // assume if first
            _unsorted.Add(weighted);
        }
    }

    private ReadOnlyMemory<WeightedEndpoint> GetWeightedEndpoints(out WeightedEndpoint[] oversized)
    {
        lock (_syncLock)
        {
            var count = _unsorted.Count;
            if (count == 0)
            {
                oversized = [];
                return default;
            }

            oversized = ArrayPool<WeightedEndpoint>.Shared.Rent(count);
            _unsorted.CopyTo(oversized, 0);
            return oversized.AsMemory(0, count);
        }
    }

    private static void Return(WeightedEndpoint[] oversized) => ArrayPool<WeightedEndpoint>.Shared.Return(oversized);

    private SuperMultiplexer()
    {
    }

    public override string ToString() => _active.ToString();

    void IDisposable.Dispose()
    {
        var eps = GetWeightedEndpoints(out var oversized);
        foreach (var ep in eps.Span)
        {
            ep.Muxer.Dispose();
        }

        Return(oversized);
    }

    async ValueTask IAsyncDisposable.DisposeAsync()
    {
        var eps = GetWeightedEndpoints(out var oversized);
        var len = eps.Length;
        for (int i = 0; i < len; i++)
        {
            await eps.Span[i].Muxer.DisposeAsync().ForAwait();
        }
        Return(oversized);
    }

    string IConnectionMultiplexer.ClientName => _active.ClientName;

    string IConnectionMultiplexer.Configuration => _active.Configuration;

    int IConnectionMultiplexer.TimeoutMilliseconds => _active.TimeoutMilliseconds;

    long IConnectionMultiplexer.OperationCount => _active.OperationCount;

    [Obsolete]
    bool IConnectionMultiplexer.PreserveAsyncOrder
    {
        get => _active.PreserveAsyncOrder;
        set => _active.PreserveAsyncOrder = value;
    }

    bool IConnectionMultiplexer.IsConnected => _active.IsConnected;

    bool IConnectionMultiplexer.IsConnecting => _active.IsConnecting;

    [Obsolete]
    bool IConnectionMultiplexer.IncludeDetailInExceptions
    {
        get => _active.IncludeDetailInExceptions;
        set => _active.IncludeDetailInExceptions = value;
    }

    int IConnectionMultiplexer.StormLogThreshold
    {
        get => _active.StormLogThreshold;
        set => _active.StormLogThreshold = value;
    }

    void IConnectionMultiplexer.RegisterProfiler(Func<ProfilingSession?> profilingSessionProvider)
    {
        _active.RegisterProfiler(profilingSessionProvider);
    }

    ServerCounters IConnectionMultiplexer.GetCounters()
    {
        return _active.GetCounters();
    }

    event EventHandler<RedisErrorEventArgs>? IConnectionMultiplexer.ErrorMessage
    {
        add => _active.ErrorMessage += value;
        remove => _active.ErrorMessage -= value;
    }

    event EventHandler<ConnectionFailedEventArgs>? IConnectionMultiplexer.ConnectionFailed
    {
        add => _active.ConnectionFailed += value;
        remove => _active.ConnectionFailed -= value;
    }

    event EventHandler<InternalErrorEventArgs>? IConnectionMultiplexer.InternalError
    {
        add => _active.InternalError += value;
        remove => _active.InternalError -= value;
    }

    event EventHandler<ConnectionFailedEventArgs>? IConnectionMultiplexer.ConnectionRestored
    {
        add => _active.ConnectionRestored += value;
        remove => _active.ConnectionRestored -= value;
    }

    event EventHandler<EndPointEventArgs>? IConnectionMultiplexer.ConfigurationChanged
    {
        add => _active.ConfigurationChanged += value;
        remove => _active.ConfigurationChanged -= value;
    }

    event EventHandler<EndPointEventArgs>? IConnectionMultiplexer.ConfigurationChangedBroadcast
    {
        add => _active.ConfigurationChangedBroadcast += value;
        remove => _active.ConfigurationChangedBroadcast -= value;
    }

    event EventHandler<ServerMaintenanceEvent>? IConnectionMultiplexer.ServerMaintenanceEvent
    {
        add => _active.ServerMaintenanceEvent += value;
        remove => _active.ServerMaintenanceEvent -= value;
    }

    EndPoint[] IConnectionMultiplexer.GetEndPoints(bool configuredOnly)
    {
        return _active.GetEndPoints(configuredOnly);
    }

    void IConnectionMultiplexer.Wait(Task task)
    {
        _active.Wait(task);
    }

    T IConnectionMultiplexer.Wait<T>(Task<T> task)
    {
        return _active.Wait(task);
    }

    void IConnectionMultiplexer.WaitAll(params Task[] tasks)
    {
        _active.WaitAll(tasks);
    }

    event EventHandler<HashSlotMovedEventArgs>? IConnectionMultiplexer.HashSlotMoved
    {
        add => _active.HashSlotMoved += value;
        remove => _active.HashSlotMoved -= value;
    }

    int IConnectionMultiplexer.HashSlot(RedisKey key)
    {
        return _active.HashSlot(key);
    }

    public ISubscriber GetSubscriber(object? asyncState = null)
    {
        return _active.GetSubscriber(asyncState);
    }

    public IDatabase GetDatabase(int db = -1, object? asyncState = null)
    {
        db = ConnectionMultiplexer.ApplyDefaultDatabase(_active.RawConfig, db);

        // if there's no async-state, and the DB is suitable, we can hand out a re-used instance
        return (asyncState == null && db <= ConnectionMultiplexer.MaxCachedDatabaseInstance)
            ? GetCachedDatabaseInstance(db)
            : new RedisDatabase(this, db, asyncState);
    }

    private IDatabase? _dbCacheZero;
    private IDatabase[]? _dbCacheLow;

    private IDatabase GetCachedDatabaseInstance(int db) // note that we already trust db here; only caller checks range
    {
        // Note: we don't need to worry about *always* returning the same instance.
        // If two threads ask for db 3 at the same time, it is OK for them to get
        // different instances, one of which (arbitrarily) ends up cached for later use.
        if (db == 0)
        {
            return _dbCacheZero ??= new RedisDatabase(this, 0, null);
        }

        var arr = _dbCacheLow ??= new IDatabase[ConnectionMultiplexer.MaxCachedDatabaseInstance];
        return arr[db - 1] ??= new RedisDatabase(this, db, null);
    }

    IServer IConnectionMultiplexer.GetServer(string host, int port, object? asyncState)
    {
        return _active.GetServer(host, port, asyncState);
    }

    IServer IConnectionMultiplexer.GetServer(string hostAndPort, object? asyncState)
    {
        return _active.GetServer(hostAndPort, asyncState);
    }

    IServer IConnectionMultiplexer.GetServer(IPAddress host, int port)
    {
        return _active.GetServer(host, port);
    }

    IServer IConnectionMultiplexer.GetServer(EndPoint endpoint, object? asyncState)
    {
        return _active.GetServer(endpoint, asyncState);
    }

    IServer IConnectionMultiplexer.GetServer(RedisKey key, object? asyncState, CommandFlags flags)
    {
        return _active.GetServer(key, asyncState, flags);
    }

    IServer[] IConnectionMultiplexer.GetServers()
    {
        return _active.GetServers();
    }

    Task<bool> IConnectionMultiplexer.ConfigureAsync(TextWriter? log)
    {
        return _active.ConfigureAsync(log);
    }

    bool IConnectionMultiplexer.Configure(TextWriter? log)
    {
        return _active.Configure(log);
    }

    string IConnectionMultiplexer.GetStatus()
    {
        return _active.GetStatus();
    }

    void IConnectionMultiplexer.GetStatus(TextWriter log)
    {
        _active.GetStatus(log);
    }

    public void Close(bool allowCommandsToComplete = true)
    {
        var eps = GetWeightedEndpoints(out var oversized);
        foreach (var ep in eps.Span)
        {
            ep.Muxer.Close(allowCommandsToComplete);
        }
        Return(oversized);
    }

    public async Task CloseAsync(bool allowCommandsToComplete = true)
    {
        var eps = GetWeightedEndpoints(out var oversized);
        var len = eps.Length;
        for (int i = 0; i < len; i++)
        {
            await eps.Span[i].Muxer.CloseAsync(allowCommandsToComplete).ForAwait();
        }
        Return(oversized);
    }

    string? IConnectionMultiplexer.GetStormLog()
    {
        return _active.GetStormLog();
    }

    void IConnectionMultiplexer.ResetStormLog()
    {
        _active.ResetStormLog();
    }

    long IConnectionMultiplexer.PublishReconfigure(CommandFlags flags)
    {
        return _active.PublishReconfigure(flags);
    }

    Task<long> IConnectionMultiplexer.PublishReconfigureAsync(CommandFlags flags)
    {
        return _active.PublishReconfigureAsync(flags);
    }

    int IConnectionMultiplexer.GetHashSlot(RedisKey key)
    {
        return _active.GetHashSlot(key);
    }

    void IConnectionMultiplexer.ExportConfiguration(Stream destination, ExportOptions options)
    {
        _active.ExportConfiguration(destination, options);
    }

    void IConnectionMultiplexer.AddLibraryNameSuffix(string suffix)
    {
        _active.AddLibraryNameSuffix(suffix);
    }

    bool IInternalConnectionMultiplexer.AllowConnect
    {
        get => ((IInternalConnectionMultiplexer)_active).AllowConnect;
        set => ((IInternalConnectionMultiplexer)_active).AllowConnect = value;
    }

    bool IInternalConnectionMultiplexer.IgnoreConnect
    {
        get => ((IInternalConnectionMultiplexer)_active).IgnoreConnect;
        set => ((IInternalConnectionMultiplexer)_active).IgnoreConnect = value;
    }

    ReadOnlySpan<ServerEndPoint> IInternalConnectionMultiplexer.GetServerSnapshot()
    {
        return ((IInternalConnectionMultiplexer)_active).GetServerSnapshot();
    }

    ServerEndPoint IInternalConnectionMultiplexer.GetServerEndPoint(EndPoint endpoint)
    {
        return ((IInternalConnectionMultiplexer)_active).GetServerEndPoint(endpoint);
    }

    ConfigurationOptions IInternalConnectionMultiplexer.RawConfig => ((IInternalConnectionMultiplexer)_active).RawConfig;

    long? IInternalConnectionMultiplexer.GetConnectionId(EndPoint endPoint, ConnectionType type)
    {
        return ((IInternalConnectionMultiplexer)_active).GetConnectionId(endPoint, type);
    }

    ServerSelectionStrategy IInternalConnectionMultiplexer.ServerSelectionStrategy =>
        ((IInternalConnectionMultiplexer)_active).ServerSelectionStrategy;

    int IInternalConnectionMultiplexer.GetSubscriptionsCount()
    {
        return ((IInternalConnectionMultiplexer)_active).GetSubscriptionsCount();
    }

    ConcurrentDictionary<RedisChannel, ConnectionMultiplexer.Subscription> IInternalConnectionMultiplexer.GetSubscriptions()
    {
        return ((IInternalConnectionMultiplexer)_active).GetSubscriptions();
    }

    ServerEndPoint? IInternalConnectionMultiplexer.GetSubscribedServer(RedisChannel channel)
    {
        return ((IInternalConnectionMultiplexer)_active).GetSubscribedServer(channel);
    }

    void IInternalConnectionMultiplexer.OnInternalError(
        Exception exception,
        EndPoint? endpoint,
        ConnectionType connectionType,
        string? origin)
    {
        ((IInternalConnectionMultiplexer)_active).OnInternalError(exception, endpoint, connectionType, origin);
    }

    void IInternalConnectionMultiplexer.Trace(string message, string? category)
    {
        ((IInternalConnectionMultiplexer)_active).Trace(message, category);
    }

    ConnectionMultiplexer IInternalConnectionMultiplexer.UnderlyingMultiplexer =>
        ((IInternalConnectionMultiplexer)_active).UnderlyingMultiplexer;

    IInternalConnectionMultiplexer IMessageExecutor.Multiplexer => ((IMessageExecutor)_active).Multiplexer;

    CommandMap IMessageExecutor.CommandMap => ((IMessageExecutor)_active).CommandMap;

    ReadOnlyMemory<byte> IMessageExecutor.UniqueId => ((IMessageExecutor)_active).UniqueId;

    ServerEndPoint? IMessageExecutor.SelectServer(Message message)
    {
        return ((IMessageExecutor)_active).SelectServer(message);
    }

    ServerEndPoint? IMessageExecutor.SelectServer(RedisCommand command, CommandFlags flags, in RedisKey key)
    {
        return ((IMessageExecutor)_active).SelectServer(command, flags, in key);
    }

    Task<T> IMessageExecutor.ExecuteAsyncImpl<T>(
        Message? message,
        ResultProcessor<T>? processor,
        object? state,
        ServerEndPoint? server,
        T defaultValue)
    {
        return ((IMessageExecutor)_active).ExecuteAsyncImpl<T>(message, processor, state, server, defaultValue);
    }

    Task<T?> IMessageExecutor.ExecuteAsyncImpl<T>(Message? message, ResultProcessor<T>? processor, object? state, ServerEndPoint? server) where T : default
    {
        return ((IMessageExecutor)_active).ExecuteAsyncImpl<T>(message, processor, state, server);
    }

    [return: NotNullIfNotNull("defaultValue")]
    T? IMessageExecutor.ExecuteSyncImpl<T>(Message message, ResultProcessor<T>? processor, ServerEndPoint? server, T? defaultValue) where T : default
    {
        return ((IMessageExecutor)_active).ExecuteSyncImpl<T>(message, processor, server, defaultValue);
    }

    void IMessageExecutor.CheckMessage(Message message)
    {
        ((IMessageExecutor)_active).CheckMessage(message);
    }
}
