using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using RESPite.Messages;
using StackExchange.Redis.Configuration;
using StackExchange.Redis.Server;
using Xunit;

namespace StackExchange.Redis.Tests;

/// <summary>
/// An in-process sentinel that monitors one service on an <see cref="InProcessTestServer"/>: it answers the
/// <c>SENTINEL</c> queries a client makes from the monitored server's topology, and does nothing on its own -
/// no failover, no <c>+switch-master</c>. Which node is the primary is whatever the test says it is, which is
/// what lets a test put sentinel and the client's cached view out of step.
/// </summary>
public sealed class InProcessSentinelServer : InProcessTestServer
{
    private readonly InProcessTestServer _monitored;
    private EndPoint _primary;

    public InProcessSentinelServer(InProcessTestServer monitored, string serviceName, EndPoint primary, EndPoint endpoint, ITestOutputHelper? log = null)
        : base(log, endpoint)
    {
        _monitored = monitored ?? throw new ArgumentNullException(nameof(monitored));
        ServiceName = serviceName ?? throw new ArgumentNullException(nameof(serviceName));
        _primary = primary ?? throw new ArgumentNullException(nameof(primary));
        ServerType = ServerType.Sentinel;
    }

    public string ServiceName { get; }

    /// <summary>
    /// The primary this sentinel reports for <see cref="ServiceName"/>; changing it announces nothing.
    /// </summary>
    public EndPoint Primary
    {
        get => Volatile.Read(ref _primary);
        set => Volatile.Write(ref _primary, value ?? throw new ArgumentNullException(nameof(value)));
    }

    /// <summary>
    /// A tunnel that routes each endpoint to whichever of these servers owns it, so that one client configuration
    /// can reach the sentinel and the nodes it monitors.
    /// </summary>
    public static Tunnel CreateTunnel(params InProcessTestServer[] servers) => new RoutingTunnel(servers);

    private int _primaryQueries, _peerQueries, _replicaQueries, _legacyQueries;
    private string _primaryFlags = "master";

    /// <summary>How many times a client has asked this sentinel for the primary.</summary>
    public int PrimaryQueries => Volatile.Read(ref _primaryQueries);

    public Action? OnPrimaryQuery { get; set; }
    public int PeerQueries => Volatile.Read(ref _peerQueries);
    public int ReplicaQueries => Volatile.Read(ref _replicaQueries);
    public int LegacyQueries => Volatile.Read(ref _legacyQueries);
    public string? PrimaryError { get; set; }
    public EndPoint[]? ReplicaAddressesOverride { get; set; }
    public Func<Task>? BeforePrimaryReplyAsync { get; set; }
    public Func<Task>? BeforePeerReplyAsync { get; set; }

    protected override ValueTask ClientPauseAsync(RedisClient client, in RedisRequest request)
    {
        var pause = base.ClientPauseAsync(client, request);
        var hook = request.Count > 1 && request.KnownCommand == RedisCommand.SENTINEL
            ? request.GetString(1).ToLowerInvariant() switch
            {
                "master" or "get-master-addr-by-name" => BeforePrimaryReplyAsync,
                "sentinels" => BeforePeerReplyAsync,
                _ => null,
            } : null;
        return hook is null ? pause : new ValueTask(AwaitPausedReplyAsync(hook, pause));

        static async Task AwaitPausedReplyAsync(Func<Task> hook, ValueTask pause)
        {
            // Execute has already captured the response; the barrier controls delivery, not the view being read.
            await hook();
            await pause;
        }
    }

    /// <summary>Replicas to report with flags, including nodes that are no longer reachable.</summary>
    public ConcurrentDictionary<EndPoint, string> ReplicaFlags { get; } = new();

    /// <summary>Peer routes to advertise; deliberately no MYID/runid support is needed for address discovery.</summary>
    public ConcurrentDictionary<EndPoint, string> PeerFlags { get; } = new();

    public string PrimaryFlags
    {
        get => Volatile.Read(ref _primaryFlags);
        set => Volatile.Write(ref _primaryFlags, value);
    }

    public bool SupportsSentinelMaster { get; set; } = true;

    private void CountPrimaryQuery()
    {
        Interlocked.Increment(ref _primaryQueries);
        OnPrimaryQuery?.Invoke();
    }

    [RedisCommand(3, nameof(RedisCommand.SENTINEL), "master", LockFree = true)]
    private TypedRedisValue SentinelMaster(RedisClient client, in RedisRequest request)
    {
        if (!SupportsSentinelMaster) return TypedRedisValue.Error("ERR Unknown sentinel subcommand 'master'");
        var reply = PrimaryError is string error ? TypedRedisValue.Error(error)
            : IsService(request) ? Describe(Primary, PrimaryFlags) : TypedRedisValue.Error("ERR No such master with that name");
        CountPrimaryQuery();
        return reply;
    }

    [RedisCommand(3, nameof(RedisCommand.SENTINEL), "get-master-addr-by-name", LockFree = true)]
    private TypedRedisValue SentinelGetMasterAddrByName(RedisClient client, in RedisRequest request)
    {
        Interlocked.Increment(ref _legacyQueries);
        if (!IsService(request)) return TypedRedisValue.NullArray(RespPrefix.Array);
        var host = GetHost(Primary, out var port);
        var reply = TypedRedisValue.Rent(2, out var span, RespPrefix.Array);
        span[0] = TypedRedisValue.BulkString(host);
        span[1] = TypedRedisValue.BulkString(port.ToString());
        CountPrimaryQuery();
        return reply;
    }

    [RedisCommand(3, nameof(RedisCommand.SENTINEL), "replicas", LockFree = true)]
    private TypedRedisValue SentinelReplicas(RedisClient client, in RedisRequest request) => Replicas(request);

    [RedisCommand(3, nameof(RedisCommand.SENTINEL), "slaves", LockFree = true)]
    private TypedRedisValue SentinelSlaves(RedisClient client, in RedisRequest request) => Replicas(request);

    [RedisCommand(3, nameof(RedisCommand.SENTINEL), "sentinels", LockFree = true)]
    private TypedRedisValue SentinelSentinels(RedisClient client, in RedisRequest request)
    {
        Interlocked.Increment(ref _peerQueries);
        if (!IsService(request)) return TypedRedisValue.Error("ERR No such master with that name");
        var peers = PeerFlags.ToArray();
        var reply = TypedRedisValue.Rent(peers.Length, out var span, RespPrefix.Array);
        for (int i = 0; i < peers.Length; i++) span[i] = Describe(peers[i].Key, "sentinel," + peers[i].Value);
        return reply;
    }

    [RedisCommand(2, nameof(RedisCommand.SENTINEL), "masters", LockFree = true)]
    private TypedRedisValue SentinelMasters(RedisClient client, in RedisRequest request)
    {
        var reply = TypedRedisValue.Rent(1, out var span, RespPrefix.Array);
        span[0] = Describe(Primary, PrimaryFlags);
        return reply;
    }

    private bool IsService(in RedisRequest request) => string.Equals(request.GetString(2), ServiceName, StringComparison.Ordinal);

    private TypedRedisValue Replicas(in RedisRequest request)
    {
        if (!IsService(request)) return TypedRedisValue.Error("ERR No such master with that name");

        Interlocked.Increment(ref _replicaQueries);
        // sentinel learns the replicas from the primary, so report whatever replicates it right now
        var replicas = new List<EndPoint>();
        if (_monitored.TryGetNode(Primary, out var primaryNode))
        {
            foreach (var endpoint in _monitored.GetEndPoints())
            {
                if (_monitored.TryGetNode(endpoint, out var node) && node.PrimaryId == primaryNode.Id)
                {
                    replicas.Add(endpoint);
                }
            }
        }

        if (ReplicaAddressesOverride is { } addresses)
        {
            replicas.Clear();
            replicas.AddRange(addresses);
        }
        // Real sentinel keeps reporting failed replicas with down/disconnected flags until reset.
        foreach (var endpoint in ReplicaFlags.Keys)
            if (!replicas.Contains(endpoint)) replicas.Add(endpoint);
        var reply = TypedRedisValue.Rent(replicas.Count, out var span, RespPrefix.Array);
        for (int i = 0; i < replicas.Count; i++)
        {
            span[i] = Describe(replicas[i], ReplicaFlags.TryGetValue(replicas[i], out var flags) ? "slave," + flags : "slave");
        }
        return reply;
    }

    private TypedRedisValue Describe(EndPoint endpoint, string flags)
    {
        var host = GetHost(endpoint, out var port);
        var item = TypedRedisValue.Rent(8, out var span, RespPrefix.Array);
        span[0] = TypedRedisValue.BulkString("name");
        span[1] = TypedRedisValue.BulkString(flags.StartsWith("master", StringComparison.Ordinal) ? ServiceName : $"{host}:{port}");
        span[2] = TypedRedisValue.BulkString("ip");
        span[3] = TypedRedisValue.BulkString(host);
        span[4] = TypedRedisValue.BulkString("port");
        span[5] = TypedRedisValue.BulkString(port.ToString());
        span[6] = TypedRedisValue.BulkString("flags");
        span[7] = TypedRedisValue.BulkString(flags);
        return item;
    }

    private sealed class RoutingTunnel(InProcessTestServer[] servers) : Tunnel
    {
        private Tunnel? Route(EndPoint endpoint)
        {
            foreach (var server in servers)
            {
                if (server.TryGetNode(endpoint, out _)) return server.Tunnel;
            }
            return null;
        }

        public override ValueTask<EndPoint?> GetSocketConnectEndpointAsync(EndPoint endpoint, CancellationToken cancellationToken)
            => Route(endpoint) is { } tunnel ? tunnel.GetSocketConnectEndpointAsync(endpoint, cancellationToken) : throw new IOException("No in-process route for " + endpoint);

        public override ValueTask<Stream?> BeforeAuthenticateAsync(EndPoint endpoint, ConnectionType connectionType, Socket? socket, CancellationToken cancellationToken)
            => Route(endpoint) is { } tunnel ? tunnel.BeforeAuthenticateAsync(endpoint, connectionType, socket, cancellationToken) : throw new IOException("No in-process route for " + endpoint);
    }
}
