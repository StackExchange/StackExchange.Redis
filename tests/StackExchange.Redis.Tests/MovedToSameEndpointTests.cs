using System.Net;
using System.Threading.Tasks;
using Xunit;

namespace StackExchange.Redis.Tests;

/// <summary>
/// Integration tests for MOVED-to-same-endpoint error handling.
/// When a MOVED error points to the same endpoint, the client should reconnect before retrying,
/// allowing the DNS record/proxy/load balancer to route to a different underlying server host.
/// </summary>
public class MovedToSameEndpointTests(ITestOutputHelper log)
{
    /// <summary>
    /// Integration test: Verifies that when a MOVED error points to the same endpoint,
    /// the client reconnects and successfully retries the operation.
    ///
    /// Test scenario:
    /// 1. Client connects to test server
    /// 2. Client sends SET command for trigger key
    /// 3. Server returns MOVED error pointing to same endpoint
    /// 4. Client detects MOVED-to-same-endpoint and triggers reconnection
    /// 5. Client retries SET command after reconnection
    /// 6. Server processes SET normally on retry
    ///
    /// Expected behavior:
    /// - SET command count should increase by 2 (initial attempt + retry)
    /// - MOVED response count should increase by 1 (only on first attempt)
    /// - Connection count should increase by 1 (reconnection after MOVED)
    /// - Final SET operation should succeed with value stored.
    /// </summary>
    [Fact]
    public async Task MovedToSameEndpoint_TriggersReconnectAndRetry_CommandSucceeds()
    {
        var keyName = "MovedToSameEndpoint_TriggersReconnectAndRetry_CommandSucceeds";

        var listenEndpoint = new IPEndPoint(IPAddress.Loopback, 6382);
        using var testServer = new MovedTestServer(
            getEndpoint: () => Format.ToString(listenEndpoint),
            triggerKey: keyName,
            log: log);

        testServer.SetActualEndpoint(listenEndpoint);

        // Wait a moment for the server to fully start
        await Task.Delay(100);

        // Act: Connect to the test server
        var config = new ConfigurationOptions
        {
            EndPoints = { listenEndpoint },
            ConnectTimeout = 10000,
            SyncTimeout = 5000,
            AsyncTimeout = 5000,
            AllowAdmin = true,
            Tunnel = testServer.Tunnel,
        };

        await using var conn = await ConnectionMultiplexer.ConnectAsync(config);
        // Ping the server to ensure it's responsive
        var server = conn.GetServer(listenEndpoint);
        log?.WriteLine((await server.InfoRawAsync()) ?? "");
        var id = await server.ExecuteAsync("client", "id");
        log?.WriteLine($"client id: {id}");

        await server.PingAsync();
        // Verify server is detected as cluster mode
        Assert.Equal(ServerType.Cluster, server.ServerType);
        var db = conn.GetDatabase();

        // Record baseline counters after initial connection
        var initialSetCmdCount = testServer.SetCmdCount;
        var initialMovedResponseCount = testServer.MovedResponseCount;
        var initialConnectionCount = testServer.TotalClientCount;
        // Execute SET command: This should receive MOVED → reconnect → retry → succeed
        var setResult = await db.StringSetAsync(keyName, "testvalue");

        // Assert: Verify SET command succeeded
        Assert.True(setResult, "SET command should return true (OK)");

        // Verify the value was actually stored (proving retry succeeded)
        var retrievedValue = await db.StringGetAsync(keyName);
        Assert.Equal("testvalue", (string?)retrievedValue);

        // Verify SET command was executed twice: once with MOVED response, once successfully
        var expectedSetCmdCount = initialSetCmdCount + 2;
        Assert.Equal(expectedSetCmdCount, testServer.SetCmdCount);

        // Verify MOVED response was returned exactly once
        var expectedMovedResponseCount = initialMovedResponseCount + 1;
        Assert.Equal(expectedMovedResponseCount, testServer.MovedResponseCount);

        // Verify reconnection occurred: connection count should have increased by 1
        var expectedConnectionCount = initialConnectionCount + 1;
        Assert.Equal(expectedConnectionCount, testServer.TotalClientCount);
        id = await server.ExecuteAsync("client", "id");
        log?.WriteLine($"client id: {id}");
    }
}
