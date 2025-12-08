using System;
using System.Buffers;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Threading.Tasks;
using StackExchange.Redis.Maintenance;
using StackExchange.Redis.Profiling;

namespace StackExchange.Redis;

internal sealed class MultiGroupMultiplexer : IConnectionMultiplexer
{
    private ConnectionMultiplexer[] _muxers;

    private ConnectionMultiplexer? _active;

    public override string ToString() => _active is { } active ? active.ToString() : "No active connection";

    internal ConnectionMultiplexer Active
    {
        get
        {
            return _active ?? Throw();
            static ConnectionMultiplexer Throw() => throw new ObjectDisposedException("All connections are unavailable.");
        }
    }

    internal static async Task<IConnectionMultiplexer> ConnectAsync(ConfigurationOptions[] configs, TextWriter? log)
    {
        var pending = new Task<ConnectionMultiplexer>[configs.Length];
        for (int i = 0; i < configs.Length; i++)
        {
            var config = configs[i];
            config.AbortOnConnectFail = false;
            pending[i] = ConnectionMultiplexer.ConnectAsync(config, log);
        }
        ConnectionMultiplexer[] muxers = new ConnectionMultiplexer[pending.Length];
        for (int i = 0; i < pending.Length; i++)
        {
            muxers[i] = await pending[i].ConfigureAwait(false);
        }
        return new MultiGroupMultiplexer(muxers);
    }
    private MultiGroupMultiplexer(ConnectionMultiplexer[] muxers)
    {
        _muxers = muxers;
        if (muxers.Length == 0) throw new ArgumentException("No muxers specified");
        _active = null;
        SelectPreferredGroup();
    }

    private void SelectPreferredGroup()
    {
        var arr = _muxers;
        var weights = ArrayPool<GroupWeight>.Shared.Rent(arr.Length);
        var indices = ArrayPool<int>.Shared.Rent(arr.Length);
        for (int i = 0; i < arr.Length; i++)
        {
            var grp = arr[i];
            weights[i] = new GroupWeight(grp.RawConfig.Weight, grp.LatencyTicks);
            indices[i] = i;
        }
        Array.Sort(weights, indices, 0, arr.Length);
        for (int i = 0; i < arr.Length; i++)
        {
            var muxer = arr[indices[i]];
            if (muxer.IsConnected)
            {
                _active = muxer;
                break;
            }
        }

        ArrayPool<GroupWeight>.Shared.Return(weights);
        ArrayPool<int>.Shared.Return(indices);
    }

    private readonly struct GroupWeight(float weight, int latency) : IComparable<GroupWeight>
    {
        public readonly float Weight = weight;
        public readonly int Latency = latency;

        public int CompareTo(GroupWeight other)
        {
            var delta = this.Weight.CompareTo(other.Weight);
            return delta != 0 ? delta : this.Latency.CompareTo(other.Latency);
        }
    }

    public void Dispose() => throw new NotImplementedException();

    public ValueTask DisposeAsync() => throw new NotImplementedException();

    public string ClientName => Active.ClientName;
    public string Configuration => Active.Configuration;
    public int TimeoutMilliseconds => Active.TimeoutMilliseconds;

    public long OperationCount
    {
        get
        {
            long count = 0;
            foreach (var muxer in _muxers)
            {
                count += muxer.OperationCount;
            }
            return count;
        }
    }

    [Obsolete]
    public bool PreserveAsyncOrder
    {
        get => Active.PreserveAsyncOrder;
        set => Active.PreserveAsyncOrder = value;
    }

    public bool IsConnected => Active.IsConnected;
    public bool IsConnecting => Active.IsConnecting;

    [Obsolete]
    public bool IncludeDetailInExceptions
    {
        get => Active.IncludeDetailInExceptions;
        set => Active.IncludeDetailInExceptions = value;
    }

    public int StormLogThreshold
    {
        get => Active.StormLogThreshold;
        set => Active.StormLogThreshold = value;
    }

    public void RegisterProfiler(Func<ProfilingSession?> profilingSessionProvider)
    {
        foreach (var muxer in _muxers)
        {
            muxer.RegisterProfiler(profilingSessionProvider);
        }
    }

    public ServerCounters GetCounters() => Active.GetCounters();

    public event EventHandler<RedisErrorEventArgs>? ErrorMessage
    {
        add
        {
            foreach (var muxer in _muxers)
            {
                muxer.ErrorMessage += value;
            }
        }
        remove
        {
            foreach (var muxer in _muxers)
            {
                muxer.ErrorMessage -= value;
            }
        }
    }
    public event EventHandler<ConnectionFailedEventArgs>? ConnectionFailed
    {
        add
        {
            foreach (var muxer in _muxers)
            {
                muxer.ConnectionFailed += value;
            }
        }
        remove
        {
            foreach (var muxer in _muxers)
            {
                muxer.ConnectionFailed -= value;
            }
        }
    }
    public event EventHandler<InternalErrorEventArgs>? InternalError
    {
        add
        {
            foreach (var muxer in _muxers)
            {
                muxer.InternalError += value;
            }
        }
        remove
        {
            foreach (var muxer in _muxers)
            {
                muxer.InternalError -= value;
            }
        }
    }
    public event EventHandler<ConnectionFailedEventArgs>? ConnectionRestored
    {
        add
        {
            foreach (var muxer in _muxers)
            {
                muxer.ConnectionRestored += value;
            }
        }
        remove
        {
            foreach (var muxer in _muxers)
            {
                muxer.ConnectionRestored -= value;
            }
        }
    }
    public event EventHandler<EndPointEventArgs>? ConfigurationChanged
    {
        add
        {
            foreach (var muxer in _muxers)
            {
                muxer.ConfigurationChanged += value;
            }
        }
        remove
        {
            foreach (var muxer in _muxers)
            {
                muxer.ConfigurationChanged -= value;
            }
        }
    }
    public event EventHandler<EndPointEventArgs>? ConfigurationChangedBroadcast
    {
        add
        {
            foreach (var muxer in _muxers)
            {
                muxer.ConfigurationChangedBroadcast += value;
            }
        }
        remove
        {
            foreach (var muxer in _muxers)
            {
                muxer.ConfigurationChangedBroadcast -= value;
            }
        }
    }
    public event EventHandler<ServerMaintenanceEvent>? ServerMaintenanceEvent
    {
        add
        {
            foreach (var muxer in _muxers)
            {
                muxer.ServerMaintenanceEvent += value;
            }
        }
        remove
        {
            foreach (var muxer in _muxers)
            {
                muxer.ServerMaintenanceEvent -= value;
            }
        }
    }
    public EndPoint[] GetEndPoints(bool configuredOnly = false) => Active.GetEndPoints(configuredOnly);

    public void Wait(Task task) => Active.Wait(task);

    public T Wait<T>(Task<T> task) => Active.Wait(task);

    public void WaitAll(params Task[] tasks) => Active.WaitAll(tasks);

    public event EventHandler<HashSlotMovedEventArgs>? HashSlotMoved
    {
        add
        {
            foreach (var muxer in _muxers)
            {
                muxer.HashSlotMoved += value;
            }
        }
        remove
        {
            foreach (var muxer in _muxers)
            {
                muxer.HashSlotMoved -= value;
            }
        }
    }

    public int HashSlot(RedisKey key) => Active.HashSlot(key);

    public ISubscriber GetSubscriber(object? asyncState = null) => throw new NotImplementedException();

    public IDatabase GetDatabase(int db = -1, object? asyncState = null)
    {
        if (asyncState is null & db >= -1 & db <= ConnectionMultiplexer.MaxCachedDatabaseInstance)
        {
            return _databases[db + 1] ??= new MultiGroupDatabase(this, db, null);
        }
        return new MultiGroupDatabase(this, db, asyncState);
    }

    private readonly IDatabase?[] _databases = new IDatabase?[ConnectionMultiplexer.MaxCachedDatabaseInstance + 2];

    public IServer GetServer(string host, int port, object? asyncState = null)
    {
        Exception ex;
        try
        {
            // try "active" first, and preserve the exception
            return Active.GetServer(host, port, asyncState);
        }
        catch (Exception e)
        {
            ex = e;
        }
        foreach (var muxer in _muxers)
        {
            try
            {
                return muxer.GetServer(host, port, asyncState);
            }
            catch (Exception e) { Debug.WriteLine(e.Message); }
        }
        throw ex;
    }

    public IServer GetServer(string hostAndPort, object? asyncState = null)
    {
        Exception ex;
        try
        {
            // try "active" first, and preserve the exception
            return Active.GetServer(hostAndPort, asyncState);
        }
        catch (Exception e)
        {
            ex = e;
        }
        foreach (var muxer in _muxers)
        {
            try
            {
                return muxer.GetServer(hostAndPort, asyncState);
            }
            catch (Exception e) { Debug.WriteLine(e.Message); }
        }
        throw ex;
    }

    public IServer GetServer(IPAddress host, int port)
    {
        Exception ex;
        try
        {
            // try "active" first, and preserve the exception
            return Active.GetServer(host, port);
        }
        catch (Exception e)
        {
            ex = e;
        }
        foreach (var muxer in _muxers)
        {
            try
            {
                return muxer.GetServer(host, port);
            }
            catch (Exception e) { Debug.WriteLine(e.Message); }
        }
        throw ex;
    }

    public IServer GetServer(EndPoint endpoint, object? asyncState = null)
    {
        Exception ex;
        try
        {
            // try "active" first, and preserve the exception
            return Active.GetServer(endpoint, asyncState);
        }
        catch (Exception e)
        {
            ex = e;
        }
        foreach (var muxer in _muxers)
        {
            try
            {
                return muxer.GetServer(endpoint, asyncState);
            }
            catch (Exception e) { Debug.WriteLine(e.Message); }
        }
        throw ex;
    }

    public IServer GetServer(RedisKey key, object? asyncState = null, CommandFlags flags = CommandFlags.None) => Active.GetServer(key, asyncState, flags);

    public IServer[] GetServers() => Active.GetServers();

    public Task<bool> ConfigureAsync(TextWriter? log = null) => Active.ConfigureAsync(log);

    public bool Configure(TextWriter? log = null) => Active.Configure(log);

    public string GetStatus() => Active.GetStatus();

    public void GetStatus(TextWriter log) => Active.GetStatus(log);

    public void Close(bool allowCommandsToComplete = true) => Active.Close(allowCommandsToComplete);

    public Task CloseAsync(bool allowCommandsToComplete = true) => Active.CloseAsync(allowCommandsToComplete);

    public string? GetStormLog() => Active.GetStormLog();

    public void ResetStormLog() => Active.ResetStormLog();

    public long PublishReconfigure(CommandFlags flags = CommandFlags.None) => Active.PublishReconfigure(flags);

    public Task<long> PublishReconfigureAsync(CommandFlags flags = CommandFlags.None) => Active.PublishReconfigureAsync(flags);

    public int GetHashSlot(RedisKey key) => Active.GetHashSlot(key);

    public void ExportConfiguration(Stream destination, ExportOptions options = ExportOptions.All) => Active.ExportConfiguration(destination, options);

    public void AddLibraryNameSuffix(string suffix)
    {
        foreach (var muxer in _muxers)
        {
            muxer.AddLibraryNameSuffix(suffix);
        }
    }
}
