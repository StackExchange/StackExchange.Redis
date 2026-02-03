using System;
using System.Collections.Generic;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using StackExchange.Redis.Server;

namespace StackExchange.Redis.Tests;

/// <summary>
/// Test Redis/Valkey server that simulates MOVED errors pointing to the same endpoint.
/// Used to verify client reconnection behavior when the server is behind DNS/load balancers/proxies.
/// When a MOVED error points to the same endpoint, it signals the client to reconnect before retrying the command,
/// allowing the DNS record/proxy/load balancer to route the connection to a different underlying server host.
/// </summary>
public class MovedTestServer : MemoryCacheRedisServer
{
    private int _setCmdCount = 0;
    private int _movedResponseCount = 0;
    private int _connectionCount = 0;
    private readonly Func<string> _getEndpoint;
    private readonly string _triggerKey;
    private readonly int _hashSlot;
    private EndPoint? _actualEndpoint;

    public MovedTestServer(Func<string> getEndpoint, string triggerKey = "testkey", int hashSlot = 12345)
    {
        _getEndpoint = getEndpoint;
        _triggerKey = triggerKey;
        _hashSlot = hashSlot;
    }

    /// <summary>
    /// Called when a new client connection is established. Increments the connection counter.
    /// </summary>
    public override RedisClient CreateClient()
    {
        Interlocked.Increment(ref _connectionCount);
        Log($"New client connection established (total connections: {_connectionCount}), endpoint: {_actualEndpoint}");
        return base.CreateClient();
    }

    /// <summary>
    /// Handles the INFO command, reporting cluster mode as enabled.
    /// </summary>
    protected override TypedRedisValue Info(RedisClient client, RedisRequest request)
    {
        // Override INFO to report cluster mode enabled
        var section = request.Count >= 2 ? request.GetString(1) : null;

        // Return cluster-enabled info
        var infoResponse = section?.Equals("CLUSTER", StringComparison.OrdinalIgnoreCase) == true
            ? "# Cluster\r\ncluster_enabled:1\r\n"
            : "# Server\r\nredis_version:7.0.0\r\n# Cluster\r\ncluster_enabled:1\r\n";

        Log($"Returning INFO response (cluster_enabled:1), endpoint: {_actualEndpoint}");

        return TypedRedisValue.BulkString(infoResponse);
    }

    /// <summary>
    /// Handles CLUSTER commands, supporting SLOTS and NODES subcommands for cluster mode simulation.
    /// </summary>
    protected override TypedRedisValue Cluster(RedisClient client, RedisRequest request)
    {
        if (request.Count < 2)
        {
            return TypedRedisValue.Error("ERR wrong number of arguments for 'cluster' command");
        }

        var subcommand = request.GetString(1);

        // Handle CLUSTER SLOTS command to support cluster mode
        if (subcommand.Equals("SLOTS", StringComparison.OrdinalIgnoreCase))
        {
            Log($"Returning CLUSTER SLOTS response, endpoint: {_actualEndpoint}");
            return GetClusterSlotsResponse();
        }

        // Handle CLUSTER NODES command
        if (subcommand.Equals("NODES", StringComparison.OrdinalIgnoreCase))
        {
            Log($"Returning CLUSTER NODES response, endpoint: {_actualEndpoint}");
            return GetClusterNodesResponse();
        }

        return TypedRedisValue.Error($"ERR Unknown CLUSTER subcommand '{subcommand}'");
    }

    /// <summary>
    /// Handles SET commands. Returns MOVED error on first attempt for the trigger key,
    /// then processes normally on subsequent attempts.
    /// </summary>
    protected override TypedRedisValue Set(RedisClient client, RedisRequest request)
    {
        var key = request.GetKey(1);

        // Only trigger MOVED on FIRST attempt for the trigger key
        if (key == _triggerKey && Interlocked.Increment(ref _setCmdCount) == 1)
        {
            Interlocked.Increment(ref _movedResponseCount);
            var endpoint = _getEndpoint();
            Log($"Returning MOVED {_hashSlot} {endpoint} for key '{key}', actual endpoint: {_actualEndpoint}");

            // Return MOVED error pointing to same endpoint
            // Don't close the connection - let the client handle reconnection naturally
            return TypedRedisValue.Error($"MOVED {_hashSlot} {endpoint}");
        }

        // Normal processing on retry or other keys
        Log($"Processing SET normally for key '{key}', endpoint: {_actualEndpoint}");
        return base.Set(client, request);
    }

    /// <summary>
    /// Returns a CLUSTER SLOTS response indicating this endpoint serves all slots (0-16383).
    /// </summary>
    private TypedRedisValue GetClusterSlotsResponse()
    {
        // Return a minimal CLUSTER SLOTS response indicating this endpoint serves all slots (0-16383)
        // Format: Array of slot ranges, each containing:
        // [start_slot, end_slot, [host, port, node_id]]
        if (_actualEndpoint == null)
        {
            return TypedRedisValue.Error("ERR endpoint not set");
        }

        var endpoint = _getEndpoint();
        var parts = endpoint.Split(':');
        var host = parts.Length > 0 ? parts[0] : "127.0.0.1";
        var port = parts.Length > 1 ? parts[1] : "6379";

        // Build response: [[0, 16383, [host, port, node-id]]]
        // Inner array: [host, port, node-id]
        var hostPortArray = TypedRedisValue.MultiBulk((ICollection<TypedRedisValue>)new[]
        {
            TypedRedisValue.BulkString(host),
            TypedRedisValue.Integer(int.Parse(port)),
            TypedRedisValue.BulkString("test-node-id"),
        });
        // Slot range: [start_slot, end_slot, [host, port, node-id]]
        var slotRange = TypedRedisValue.MultiBulk((ICollection<TypedRedisValue>)new[]
        {
            TypedRedisValue.Integer(0),      // start slot
            TypedRedisValue.Integer(16383),  // end slot
            hostPortArray,
        });

        // Outer array containing the single slot range
        return TypedRedisValue.MultiBulk((ICollection<TypedRedisValue>)new[] { slotRange });
    }

    /// <summary>
    /// Returns a CLUSTER NODES response.
    /// </summary>
    private TypedRedisValue GetClusterNodesResponse()
    {
        // Return CLUSTER NODES response
        // Format: node-id host:port@cport flags master - ping-sent pong-recv config-epoch link-state slot-range
        // Example: test-node-id 127.0.0.1:6379@16379 myself,master - 0 0 1 connected 0-16383
        if (_actualEndpoint == null)
        {
            return TypedRedisValue.Error("ERR endpoint not set");
        }

        var endpoint = _getEndpoint();
        var nodesInfo = $"test-node-id {endpoint}@1{endpoint.Split(':')[1]} myself,master - 0 0 1 connected 0-16383\r\n";

        return TypedRedisValue.BulkString(nodesInfo);
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
    /// Gets the number of client connections established.
    /// </summary>
    public int ConnectionCount => _connectionCount;

    /// <summary>
    /// Gets the actual endpoint the server is listening on.
    /// </summary>
    public EndPoint? ActualEndpoint => _actualEndpoint;

    /// <summary>
    /// Sets the actual endpoint the server is listening on.
    /// This should be called externally after the server starts.
    /// </summary>
    public void SetActualEndpoint(EndPoint endPoint)
    {
        _actualEndpoint = endPoint;
        Log($"MovedTestServer endpoint set to {endPoint}");
    }

    /// <summary>
    /// Resets all counters for test reusability.
    /// </summary>
    public void ResetCounters()
    {
        Interlocked.Exchange(ref _setCmdCount, 0);
        Interlocked.Exchange(ref _movedResponseCount, 0);
        Interlocked.Exchange(ref _connectionCount, 0);
    }
}
