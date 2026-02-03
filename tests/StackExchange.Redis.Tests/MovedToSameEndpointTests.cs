using System;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;
using StackExchange.Redis.Server;
using Xunit;

namespace StackExchange.Redis.Tests;

/// <summary>
/// Integration tests for MOVED-to-same-endpoint error handling.
/// When a MOVED error points to the same endpoint, the client should reconnect before retrying,
/// allowing the DNS record/proxy/load balancer to route to a different underlying server host.
/// </summary>
public class MovedToSameEndpointTests
{
    /// <summary>
    /// Gets a free port by temporarily binding to port 0 and retrieving the OS-assigned port.
    /// </summary>
    private static int GetFreePort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

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
    /// - Final SET operation should succeed with value stored
    /// </summary>
    [Fact]
    public async Task MovedToSameEndpoint_TriggersReconnectAndRetry_CommandSucceeds()
    {
        // Arrange: Get a free port to avoid conflicts when tests run in parallel
        var port = GetFreePort();
        var listenEndpoint = new IPEndPoint(IPAddress.Loopback, port);

        var testServer = new MovedTestServer(
            getEndpoint: () => Format.ToString(listenEndpoint),
            triggerKey: "testkey");

        var socketServer = new RespSocketServer(testServer);

        try
        {
            // Start listening on the free port
            socketServer.Listen(listenEndpoint);
            testServer.SetActualEndpoint(listenEndpoint);

            // Wait a moment for the server to fully start
            await Task.Delay(100);

            // Act: Connect to the test server
            var config = new ConfigurationOptions
            {
                EndPoints = { listenEndpoint },
                ConnectTimeout = 5000,
                SyncTimeout = 5000,
                AsyncTimeout = 5000,
            };

            await using var conn = await ConnectionMultiplexer.ConnectAsync(config);
            var db = conn.GetDatabase();

            // Record baseline counters after initial connection
            var initialSetCmdCount = testServer.SetCmdCount;
            var initialMovedResponseCount = testServer.MovedResponseCount;
            var initialConnectionCount = testServer.ConnectionCount;

            // Execute SET command: This should receive MOVED → reconnect → retry → succeed
            var setResult = await db.StringSetAsync("testkey", "testvalue");

            // Assert: Verify SET command succeeded
            Assert.True(setResult, "SET command should return true (OK)");

            // Verify the value was actually stored (proving retry succeeded)
            var retrievedValue = await db.StringGetAsync("testkey");
            Assert.Equal("testvalue", (string?)retrievedValue);

            // Verify SET command was executed twice: once with MOVED response, once successfully
            var expectedSetCmdCount = initialSetCmdCount + 2;
            Assert.Equal(expectedSetCmdCount, testServer.SetCmdCount);

            // Verify MOVED response was returned exactly once
            var expectedMovedResponseCount = initialMovedResponseCount + 1;
            Assert.Equal(expectedMovedResponseCount, testServer.MovedResponseCount);

            // Verify reconnection occurred: connection count should have increased by 1
            var expectedConnectionCount = initialConnectionCount + 1;
            Assert.Equal(expectedConnectionCount, testServer.ConnectionCount);
        }
        finally
        {
            socketServer?.Dispose();
        }
    }
}
