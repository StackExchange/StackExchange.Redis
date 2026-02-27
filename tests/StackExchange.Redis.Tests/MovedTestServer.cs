using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using StackExchange.Redis.Server;
using Xunit;

namespace StackExchange.Redis.Tests;

/// <summary>
/// Test Redis/Valkey server that simulates MOVED errors pointing to the same endpoint.
/// Used to verify client reconnection behavior when the server is behind DNS/load balancers/proxies.
/// When a MOVED error points to the same endpoint, it signals the client to reconnect before retrying the command,
/// allowing the DNS record/proxy/load balancer to route the connection to a different underlying server host.
/// </summary>
public class MovedTestServer : InProcessTestServer
{
    /// <summary>
    /// Represents the simulated server host state behind a proxy/load balancer.
    /// </summary>
    private enum SimulatedHost
    {
        /// <summary>
        /// Old server that returns MOVED errors for the trigger key (pre-migration state).
        /// </summary>
        OldServer,

        /// <summary>
        /// New server that handles requests normally (post-migration state).
        /// </summary>
        NewServer,
    }

    private int _setCmdCount = 0;
    private int _movedResponseCount = 0;

    private SimulatedHost _currentServerHost = SimulatedHost.OldServer;

    private readonly RedisKey _triggerKey;

    public MovedTestServer(in RedisKey triggerKey, ITestOutputHelper? log = null) : base(log)
    {
        _triggerKey = triggerKey;
    }

    private sealed class MovedTestClient(MovedTestServer server, Node node, SimulatedHost assignedHost) : RedisClient(node)
    {
        public SimulatedHost AssignedHost => assignedHost;

        public override void OnKey(in RedisKey key, KeyFlags flags)
        {
            if (AssignedHost == SimulatedHost.OldServer && key == server._triggerKey)
            {
                server.OnTrigger(Id, key, assignedHost);
            }
            base.OnKey(in key, flags);
        }
    }

    /// <summary>
    /// Called when a new client connection is established.
    /// Assigns the client to the current server host state (simulating proxy/load balancer routing).
    /// </summary>
    public override RedisClient CreateClient(Node node) => new MovedTestClient(this, node, _currentServerHost);

    public override void OnClientConnected(RedisClient client, object state)
    {
        if (client is MovedTestClient movedClient)
        {
            Log($"Client {client.Id} connected (assigned to {movedClient.AssignedHost}), total connections: {TotalClientCount}");
        }
        base.OnClientConnected(client, state);
    }

    /// <summary>
    /// Handles SET commands. Returns MOVED error for the trigger key when requested by clients
    /// connected to the old server, simulating a server migration behind a proxy/load balancer.
    /// </summary>
    protected override TypedRedisValue Set(RedisClient client, in RedisRequest request)
    {
        Interlocked.Increment(ref _setCmdCount);
        return base.Set(client, request);
    }

    private void OnTrigger(int clientId, in RedisKey key, SimulatedHost assignedHost)
    {
        // Transition server to new host (so future connections know they're on the new server)
        _currentServerHost = SimulatedHost.NewServer;

        Interlocked.Increment(ref _movedResponseCount);

        Log($"Triggering MOVED on Client {clientId} ({assignedHost}) with key: {key}");
        KeyMovedException.Throw(key);
    }

    /// <summary>
    /// Gets the number of SET commands executed.
    /// </summary>
    public int SetCmdCount => _setCmdCount;

    /// <summary>
    /// Gets the number of times MOVED response was returned.
    /// </summary>
    public int MovedResponseCount => _movedResponseCount;

    /// <summary>
    /// Resets all counters for test reusability.
    /// </summary>
    public override void ResetCounters()
    {
        Interlocked.Exchange(ref _setCmdCount, 0);
        Interlocked.Exchange(ref _movedResponseCount, 0);
        base.ResetCounters();
    }
}
