using System.Buffers;
using System.Diagnostics.CodeAnalysis;
using System.Net;
using RESPite.Connections.Internal;
using RESPite.Internal;

namespace RESPite.Connections;

public sealed class RespConnectionManager : IRespContextSource
{
    /// <inheritdoc cref="object.ToString"/>
    public override string ToString() => GetType().Name;

    // the routed connection performs message-inspection based routing; on a single node
    // instance that isn't necessary, so the default-connection abstracts over that:
    // in a single-node instance, the default-connection will be the single interactive connection
    // otherwise, the default-connection will be the routed connection
    private RoutedConnection? _routedConnection;
    private RespContext _defaultContext = RespContext.Null;
    internal ref readonly RespContext Context => ref _defaultContext;
    ref readonly RespContext IRespContextSource.Context => ref _defaultContext;

    private readonly CancellationTokenSource _lifetime = new();

    private RespConnectionFactory? _factory;

    public RespConnectionFactory ConnectionFactory
    {
        get => _factory ??= RespConnectionFactory.Default;
        set
        {
            // ReSharper disable once JoinNullCheckWithUsage
            if (value is null) throw new ArgumentNullException(nameof(ConnectionFactory));
            _factory = value;
        }
    }

    private Node[] _nodes = [];
    internal CancellationToken Lifetime => _lifetime.Token;
    private RespConfiguration? _options;
    internal RespConfiguration Options => _options ?? ThrowNotConnected();

    [DoesNotReturn]
    private RespConfiguration ThrowNotConnected()
        => throw new InvalidOperationException($"The {GetType().Name} has not been connected.");

    internal readonly struct EndpointPair(string endpoint, int port)
    {
        public override string ToString() => $"{Endpoint}:{Port}";

        public readonly string Endpoint = endpoint;
        public readonly int Port = port;
        public override int GetHashCode() => (Endpoint?.GetHashCode() ?? 0) ^ Port;

        public override bool Equals(object? obj) => obj is EndpointPair other &&
                                                    (Endpoint == other.Endpoint & Port == other.Port);
    }

    private void OnConnect(RespConfiguration options, ReadOnlySpan<EndpointPair> endpoints)
    {
        if (options is null) throw new ArgumentNullException(nameof(options));
        if (Interlocked.CompareExchange(ref _options, options, null) is not null)
        {
            throw new InvalidOperationException($"A {GetType().Name} can only be connected once.");
        }

        var nodes = new Node[Math.Max(endpoints.Length, 1)];
        var factory = ConnectionFactory;
        if (endpoints.IsEmpty)
        {
            nodes[0] = new Node(this, factory.DefaultHost, factory.DefaultPort);
        }
        else
        {
            for (int i = 0; i < endpoints.Length; i++)
            {
                var host = endpoints[i].Endpoint;
                if (string.IsNullOrWhiteSpace(host) || host is "." or "localhost")
                    host = "127.0.0.1";
                var port = endpoints[i].Port;
                if (port == 0) port = factory.DefaultPort;
                nodes[i] = new Node(this, host, port);
            }
        }

        _nodes = nodes;
    }

    internal void Connect(RespConfiguration options, ReadOnlySpan<EndpointPair> endpoints, TextWriter? log = null)
        // use sync over async; reduce code-duplication, and sync wouldn't add anything
        => ConnectAsync(options, endpoints, log).Wait(Lifetime);

    internal Task ConnectAsync(RespConfiguration options, ReadOnlySpan<EndpointPair> endpoints, TextWriter? log = null)
    {
        OnConnect(options, endpoints);
        var snapshot = _nodes;
        log.LogLocked($"Connecting to {snapshot.Length} nodes...");
        Task<bool>[] pending = new Task<bool>[snapshot.Length];
        for (int i = 0; i < snapshot.Length; i++)
        {
            pending[i] = snapshot[i].ConnectAsync(log);
        }

        return ConnectAsyncAwaited(pending, log, snapshot.Length);
    }

    private async Task ConnectAsyncAwaited(Task<bool>[] pending, TextWriter? log, int nodeCount)
    {
        await Task.WhenAll(pending).ConfigureAwait(false);
        int success = 0;
        foreach (var task in pending)
        {
            // note WhenAll ensures all connected
            if (task.Result) success++;
        }

        // configure our primary connection
        OnNodesChanged();

        log.LogLocked($"Connected to {success} of {nodeCount} nodes.");
    }

    public void Dispose()
    {
        var routed = _routedConnection;
        _routedConnection = null;
        _defaultContext = NullConnection.Disposed.Context;
        _lifetime.Cancel();
        routed?.Dispose();
        foreach (var node in _nodes)
        {
            node.Dispose();
        }
    }

    public async ValueTask DisposeAsync()
    {
        var routed = _routedConnection;
        _routedConnection = null;
        _defaultContext = NullConnection.Disposed.Context;
#if NET8_0_OR_GREATER
        await _lifetime.CancelAsync().ConfigureAwait(false);
#else
        _lifetime.Cancel();
#endif
        if (routed is not null)
        {
            await routed.DisposeAsync().ConfigureAwait(false);
        }

        foreach (var node in _nodes)
        {
            await node.DisposeAsync().ConfigureAwait(false);
        }
    }

    public string ClientName { get; private set; } = "";
    public int TimeoutMilliseconds => (int)Options.SyncTimeout.TotalMilliseconds;
    public long OperationCount => 0;

    public bool PreserveAsyncOrder
    {
        get => false;
        [Obsolete("This feature is no longer supported", false)]
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

    private void OnNodesChanged()
    {
        var nodes = _nodes;
        _defaultContext = nodes.Length switch
        {
            0 => NullConnection.NonRoutable.Context, // nowhere to go
            1 => nodes[0] is { IsConnected: true } conn
                ? conn.Context
                : NullConnection.NonRoutable.Context, // nowhere to go
            _ => BuildRouted(nodes),
        };
    }

    private ref readonly RespContext BuildRouted(Node[] nodes)
    {
        Shard[] oversized = ArrayPool<Shard>.Shared.Rent(nodes.Length);
        for (int i = 0; i < nodes.Length; i++)
        {
            oversized[i] = nodes[i].AsShard();
        }

        Array.Sort(oversized, 0, nodes.Length);
        var conn = _routedConnection ??= new();
        conn.SetRoutingTable(new ReadOnlySpan<Shard>(oversized, 0, nodes.Length));
        ArrayPool<Shard>.Shared.Return(oversized);
        return ref conn.Context;
    }

    internal Node GetNode(string host, int port)
    {
        foreach (var node in _nodes)
        {
            if (node.EndPoint == host && node.Port == port) return node;
        }

        throw new KeyNotFoundException($"No node found for {host}:{port}");
    }

    internal Node GetNode(string hostAndPort) => ConnectionFactory.TryParse(hostAndPort, out var host, out var port)
        ? GetNode(host, port)
        : throw new ArgumentException($"Could not parse host and port from '{hostAndPort}'", nameof(hostAndPort));

    internal Node? GetRandomNode()
    {
        var nodes = _nodes;
        if (nodes is { Length: > 0 })
        {
            var index = SharedRandom.Next(nodes.Length);
            return nodes[index];
        }

        return null;
    }

#if NET5_0_OR_GREATER
    private static Random SharedRandom => Random.Shared;
#else
    private static Random SharedRandom { get; } = new();
#endif
}
