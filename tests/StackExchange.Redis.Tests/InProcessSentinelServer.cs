using System;
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

    private int _primaryQueries;

    /// <summary>How many times a client has asked this sentinel for the primary.</summary>
    public int PrimaryQueries => Volatile.Read(ref _primaryQueries);

    [RedisCommand(3, nameof(RedisCommand.SENTINEL), "get-master-addr-by-name", LockFree = true)]
    private TypedRedisValue SentinelGetMasterAddrByName(RedisClient client, in RedisRequest request)
    {
        Interlocked.Increment(ref _primaryQueries);
        if (!IsService(request)) return TypedRedisValue.NullArray(RespPrefix.Array);
        var host = GetHost(Primary, out var port);
        var reply = TypedRedisValue.Rent(2, out var span, RespPrefix.Array);
        span[0] = TypedRedisValue.BulkString(host);
        span[1] = TypedRedisValue.BulkString(port.ToString());
        return reply;
    }

    [RedisCommand(3, nameof(RedisCommand.SENTINEL), "replicas", LockFree = true)]
    private TypedRedisValue SentinelReplicas(RedisClient client, in RedisRequest request) => Replicas(request);

    [RedisCommand(3, nameof(RedisCommand.SENTINEL), "slaves", LockFree = true)]
    private TypedRedisValue SentinelSlaves(RedisClient client, in RedisRequest request) => Replicas(request);

    // only this sentinel exists
    [RedisCommand(3, nameof(RedisCommand.SENTINEL), "sentinels", LockFree = true)]
    private TypedRedisValue SentinelSentinels(RedisClient client, in RedisRequest request)
        => IsService(request) ? TypedRedisValue.EmptyArray(RespPrefix.Array) : TypedRedisValue.Error("ERR No such master with that name");

    [RedisCommand(2, nameof(RedisCommand.SENTINEL), "masters", LockFree = true)]
    private TypedRedisValue SentinelMasters(RedisClient client, in RedisRequest request)
    {
        var reply = TypedRedisValue.Rent(1, out var span, RespPrefix.Array);
        span[0] = Describe(Primary, "master");
        return reply;
    }

    private bool IsService(in RedisRequest request) => string.Equals(request.GetString(2), ServiceName, StringComparison.Ordinal);

    private TypedRedisValue Replicas(in RedisRequest request)
    {
        if (!IsService(request)) return TypedRedisValue.Error("ERR No such master with that name");

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

        var reply = TypedRedisValue.Rent(replicas.Count, out var span, RespPrefix.Array);
        for (int i = 0; i < replicas.Count; i++)
        {
            span[i] = Describe(replicas[i], "slave");
        }
        return reply;
    }

    private TypedRedisValue Describe(EndPoint endpoint, string flags)
    {
        var host = GetHost(endpoint, out var port);
        var item = TypedRedisValue.Rent(8, out var span, RespPrefix.Array);
        span[0] = TypedRedisValue.BulkString("name");
        span[1] = TypedRedisValue.BulkString(flags == "master" ? ServiceName : $"{host}:{port}");
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
            => Route(endpoint) is { } tunnel ? tunnel.GetSocketConnectEndpointAsync(endpoint, cancellationToken) : base.GetSocketConnectEndpointAsync(endpoint, cancellationToken);

        public override ValueTask<Stream?> BeforeAuthenticateAsync(EndPoint endpoint, ConnectionType connectionType, Socket? socket, CancellationToken cancellationToken)
            => Route(endpoint) is { } tunnel ? tunnel.BeforeAuthenticateAsync(endpoint, connectionType, socket, cancellationToken) : base.BeforeAuthenticateAsync(endpoint, connectionType, socket, cancellationToken);
    }
}
