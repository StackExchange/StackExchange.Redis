using System.Diagnostics.CodeAnalysis;
using System.Net;
using RESPite.Connections.Internal;
using StackExchange.Redis;
using StackExchange.Redis.Maintenance;
using StackExchange.Redis.Profiling;

namespace RESPite.StackExchange.Redis;

public sealed class RespMultiplexer : IConnectionMultiplexer, IRespContextSource
{
    /// <inheritdoc cref="object.ToString"/>
    public override string ToString() => GetType().Name;

    public RespMultiplexer()
    {
        _routedConnection = RespContext.Null.Connection; // until we've connected
        _defaultContext = _routedConnection.Context;
    }

    private int _defaultDatabase;

    // the routed connection performs message-inspection based routing; on a single node
    // instance that isn't necessary, so the default-connection abstracts over that:
    // in a single-node instance, the default-connection will be the single interactive connection
    // otherwise, the default-connection will be the routed connection
    private RespConnection _routedConnection;
    private RespContext _defaultContext;
    internal ref readonly RespContext Context => ref _defaultContext;
    ref readonly RespContext IRespContextSource.Context => ref _defaultContext;
    RespContextProxyKind IRespContextSource.RespContextProxyKind => RespContextProxyKind.Multiplexer;
    RespMultiplexer IRespContextSource.Multiplexer => this;

    private readonly CancellationTokenSource _lifetime = new();
    private ConfigurationOptions? _options;
    internal RespConfiguration Configuration { get; private set; } = RespConfiguration.Default;
    private Node[] _nodes = [];
    internal CancellationToken Lifetime => _lifetime.Token;
    internal ConfigurationOptions Options => _options ?? ThrowNotConnected();

    [DoesNotReturn]
    private static ConfigurationOptions ThrowNotConnected()
        => throw new InvalidOperationException($"The {nameof(RespMultiplexer)} has not been connected.");

    private void OnConnect(ConfigurationOptions options)
    {
        if (options is null) throw new ArgumentNullException(nameof(options));
        if (Interlocked.CompareExchange(ref _options, options, null) is not null)
        {
            throw new InvalidOperationException($"A {GetType().Name} can only be connected once.");
        }

        // fixup the endpoints in an isolated collection
        var ep = options.EndPoints.Clone();
        if (ep.Count == 0)
        {
            // no endpoints; add a default, deferring the port to the SSL setting
            ep.Add(new IPEndPoint(IPAddress.Loopback, 0));
        }
        else
        {
            for (int i = 0; i < ep.Count; i++)
            {
                if (ep[i] is DnsEndPoint { Host: "." or "localhost" } dns)
                {
                    // unroll loopback
                    ep[i] = new IPEndPoint(IPAddress.Loopback, dns.Port);
                }
            }
        }
        ep.SetDefaultPorts(ServerType.Standalone, ssl: options.Ssl);

        // add nodes from the endpoints
        var nodes = new Node[ep.Count];
        for (int i = 0; i < nodes.Length; i++)
        {
            nodes[i] = new Node(this, ep[i]);
        }
        _nodes = nodes;

        _defaultDatabase = options.DefaultDatabase ?? 0;

        // setup a basic connection that comes via ourselves
        var ctx = RespContext.Null; // this is just the template
        _routedConnection = new RoutingRespConnection(this, ctx);
        // set the default context (this might get simplified later, in OnNodesChanged)
        _defaultContext = _routedConnection.Context;
    }

    public void Connect(string configuration = "", TextWriter? log = null)
    {
        // ReSharper disable once NullCoalescingConditionIsAlwaysNotNullAccordingToAPIContract
        var config = ConfigurationOptions.Parse(configuration ?? "");
        Connect(config, log);
    }

    public void Connect(ConfigurationOptions options, TextWriter? log = null)
        // use sync over async; reduce code-duplication, and sync wouldn't add anything
        => ConnectAsync(options, log).Wait(Lifetime);

    public Task ConnectAsync(string configuration = "", TextWriter? log = null)
    {
        // ReSharper disable once NullCoalescingConditionIsAlwaysNotNullAccordingToAPIContract
        if (string.IsNullOrWhiteSpace(configuration)) configuration = "."; // localhost by default
        var config = ConfigurationOptions.Parse(configuration ?? "");
        return ConnectAsync(config, log);
    }

    public async Task ConnectAsync(ConfigurationOptions options, TextWriter? log = null)
    {
        OnConnect(options);
        var snapshot = _nodes;
        log.LogLocked($"Connecting to {snapshot.Length} nodes...");
        Task<bool>[] pending = new Task<bool>[snapshot.Length];
        for (int i = 0; i < snapshot.Length; i++)
        {
            pending[i] = snapshot[i].ConnectAsync(log);
        }

        await Task.WhenAll(pending).ConfigureAwait(false);
        int success = 0;
        foreach (var task in pending)
        {
            // note WhenAll ensures all connected
            if (task.Result) success++;
        }

        // configure our primary connection
        OnNodesChanged();

        log.LogLocked($"Connected to {success} of {snapshot.Length} nodes.");
    }

    public void Dispose()
    {
        RespConnection conn = _routedConnection;
        _routedConnection = NullConnection.Disposed;
        _defaultContext = _routedConnection.Context;
        _lifetime.Cancel();
        conn.Dispose();
        _routedConnection.Dispose();
        foreach (var node in _nodes)
        {
            node.Dispose();
        }
    }

    public async ValueTask DisposeAsync()
    {
        RespConnection conn = _routedConnection;
        _routedConnection = RespContext.Null.Connection;
        _defaultContext = _routedConnection.Context;
#if NET8_0_OR_GREATER
        await _lifetime.CancelAsync().ConfigureAwait(false);
#else
        _lifetime.Cancel();
#endif
        await conn.DisposeAsync().ConfigureAwait(false);
        await _routedConnection.DisposeAsync().ConfigureAwait(false);
        foreach (var node in _nodes)
        {
            await node.DisposeAsync().ConfigureAwait(false);
        }
    }

    public string ClientName { get; private set; } = "";
    string IConnectionMultiplexer.Configuration => Options.ToString(includePassword: false);
    public int TimeoutMilliseconds => (int)Configuration.SyncTimeout.TotalMilliseconds;
    public long OperationCount => 0;

    public bool PreserveAsyncOrder
    {
        get => false;
        [Obsolete(
            "Not supported; if you require ordered pub/sub, please see " + nameof(ChannelMessageQueue) +
            " - this will be removed in 3.0.",
            false)]
        set { }
    }

    public bool IsConnected
    {
        get
        {
            foreach (var node in _nodes)
            {
                if (node.IsConnected) return true;
            }

            return false;
        }
    }

    public bool IsConnecting
    {
        get
        {
            foreach (var node in _nodes)
            {
                if (node.IsConnecting) return true;
            }

            return false;
        }
    }

    public bool IncludeDetailInExceptions { get; set; }
    public int StormLogThreshold { get; set; }

    public void RegisterProfiler(Func<ProfilingSession?> profilingSessionProvider) =>
        throw new NotImplementedException();

    public ServerCounters GetCounters() => throw new NotImplementedException();

    public event EventHandler<ConnectionFailedEventArgs>? ConnectionFailed;
    public event EventHandler<ConnectionFailedEventArgs>? ConnectionRestored;
    internal EventHandler<ConnectionFailedEventArgs>? DirectConnectionFailed => ConnectionFailed;
    internal EventHandler<ConnectionFailedEventArgs>? DirectConnectionRestored => ConnectionRestored;

    public event EventHandler<RedisErrorEventArgs>? ErrorMessage;
    public event EventHandler<InternalErrorEventArgs>? InternalError;
    public event EventHandler<EndPointEventArgs>? ConfigurationChanged;
    public event EventHandler<EndPointEventArgs>? ConfigurationChangedBroadcast;
    public event EventHandler<ServerMaintenanceEvent>? ServerMaintenanceEvent;

    internal void OnErrorMessage(RedisErrorEventArgs e) => ErrorMessage?.Invoke(this, e);
    internal void OnInternalError(InternalErrorEventArgs e) => InternalError?.Invoke(this, e);
    internal void OnConfigurationChanged(EndPointEventArgs e) => ConfigurationChanged?.Invoke(this, e);

    internal void OnConfigurationChangedBroadcast(EndPointEventArgs e) =>
        ConfigurationChangedBroadcast?.Invoke(this, e);

    internal void OnServerMaintenanceEvent(ServerMaintenanceEvent e) => ServerMaintenanceEvent?.Invoke(this, e);

    public EndPoint[] GetEndPoints(bool configuredOnly = false) => configuredOnly
        ? Options.EndPoints.ToArray()
        : Array.ConvertAll(_nodes, x => x.EndPoint);

    public bool TryWait(Task task) => task.Wait(Configuration.SyncTimeout);

    public void Wait(Task task)
    {
        bool timeout;
        try
        {
            timeout = !task.Wait(Configuration.SyncTimeout);
        }
        catch (AggregateException ex) when (ex.InnerExceptions.Count == 1)
        {
            throw ex.InnerException ?? ex;
        }

        if (timeout) ThrowTimeout();
    }

    private static void ThrowTimeout() => throw new TimeoutException();

    public T Wait<T>(Task<T> task)
    {
        Wait((Task)task);
        return task.Result;
    }

    public void WaitAll(params Task[] tasks) => throw new NotImplementedException();

    public event EventHandler<HashSlotMovedEventArgs>? HashSlotMoved;
    internal void OnHashSlotMoved(HashSlotMovedEventArgs e) => HashSlotMoved?.Invoke(this, e);
    public int HashSlot(RedisKey key) => throw new NotImplementedException();

    public ISubscriber GetSubscriber(object? asyncState = null) => throw new NotImplementedException();

    public IDatabase GetDatabase(int db = -1, object? asyncState = null)
    {
        if (db < 0) db = _defaultDatabase;
        if (db < LowDatabaseCount) return _lowDatabases[db] ??= new RespContextDatabase(this, db);
        return new RespContextDatabase(this, db);
    }

    private const int LowDatabaseCount = 16;
    private readonly IDatabase?[] _lowDatabases = new IDatabase?[LowDatabaseCount];

    public IServer GetServer(string host, int port, object? asyncState = null)
        => GetServer(Format.ParseEndPoint(host, port), asyncState);

    public IServer GetServer(string hostAndPort, object? asyncState = null) =>
        Format.TryParseEndPoint(hostAndPort, out var ep)
            ? GetServer(ep, asyncState)
            : throw new ArgumentException($"The specified host and port could not be parsed: {hostAndPort}", nameof(hostAndPort));

    public IServer GetServer(IPAddress host, int port)
    {
        foreach (var node in _nodes)
        {
            if (node.EndPoint is IPEndPoint ep && ep.Address.Equals(host) && ep.Port == port)
            {
                return node.AsServer();
            }
        }
        throw new ArgumentException("The specified endpoint is not defined", nameof(host));
    }

    public IServer GetServer(EndPoint endpoint, object? asyncState = null)
    {
        foreach (var node in _nodes)
        {
            if (node.EndPoint.Equals(endpoint))
            {
                return node.AsServer();
            }
        }

        throw new ArgumentException("The specified endpoint is not defined", nameof(endpoint));
    }

    private void OnNodesChanged()
    {
        var nodes = _nodes;
        _defaultContext = nodes.Length switch
        {
            0 => NullConnection.NonRoutable.Context, // nowhere to go
            1 when nodes[0] is { IsConnected: true } node => node.InteractiveConnection.Context,
            _ => _routedConnection.Context,
        };
    }

    public IServer[] GetServers() => Array.ConvertAll(_nodes, static x => x.AsServer());

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
