using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using StackExchange.Redis.Configuration;
using Xunit;

namespace StackExchange.Redis.Tests;

/// <summary>
/// Integration tests for MOVED-to-same-endpoint error handling.
/// When a MOVED error points to the same endpoint, the client should reconnect before retrying,
/// allowing the DNS record/proxy/load balancer to route to a different underlying server host.
/// </summary>
public class MovedUnitTests(ITestOutputHelper log)
{
    private RedisKey Me([CallerMemberName] string callerName = "") => callerName;

    [Theory]
    [InlineData(ServerType.Cluster)]
    [InlineData(ServerType.Standalone)]
    public async Task CrossSlotDisallowed(ServerType serverType)
    {
        // intentionally sending as strings (not keys) via execute to prevent the
        // client library from getting in our way
        string keyA = "abc", keyB = "def"; // known to be on different slots

        using var server = new InProcessTestServer(log) { ServerType = serverType };
        await using var muxer = await server.ConnectAsync();

        var db = muxer.GetDatabase();
        await db.StringSetAsync(keyA, "value", flags: CommandFlags.FireAndForget);

        var pending = db.ExecuteAsync("rename", keyA, keyB);
        if (serverType == ServerType.Cluster)
        {
            var ex = await Assert.ThrowsAsync<RedisServerException>(() => pending);
            Assert.Contains("CROSSSLOT", ex.Message);

            Assert.Equal("value", await db.StringGetAsync(keyA));
            Assert.False(await db.KeyExistsAsync(keyB));
        }
        else
        {
            await pending;
            Assert.False(await db.KeyExistsAsync(keyA));
            Assert.Equal("value", await db.StringGetAsync(keyB));
        }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task KeyMigrationFollowed(bool allowFollowRedirects)
    {
        RedisKey key = Me();
        using var server = new InProcessTestServer(log) { ServerType = ServerType.Cluster };
        var secondNode = server.AddEmptyNode();

        await using var muxer = await server.ConnectAsync();
        var db = muxer.GetDatabase();

        await db.StringSetAsync(key, "value");
        var value = await db.StringGetAsync(key);
        Assert.Equal("value", (string?)value);

        server.Migrate(key, secondNode);

        if (allowFollowRedirects)
        {
            value = await db.StringGetAsync(key, flags: CommandFlags.None);
            Assert.Equal("value", (string?)value);
        }
        else
        {
            var ex = await Assert.ThrowsAsync<RedisServerException>(() => db.StringGetAsync(key, flags: CommandFlags.NoRedirect));
            Assert.Contains("MOVED", ex.Message);
        }
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
    /// - Final SET operation should succeed with value stored.
    /// </summary>
    [Theory]
    [InlineData(ServerType.Cluster)]
    [InlineData(ServerType.Standalone)]
    public async Task MovedToSameEndpoint_TriggersReconnectAndRetry_CommandSucceeds(ServerType serverType)
    {
        RedisKey key = Me();

        using var testServer = new MovedTestServer(
            triggerKey: key,
            log: log) { ServerType = serverType, };

        // Act: Connect to the test server
        await using var conn = await testServer.ConnectAsync();
        // Ping the server to ensure it's responsive
        var server = conn.GetServer(testServer.DefaultEndPoint);

        var id = await server.ExecuteAsync("client", "id");
        log?.WriteLine($"Client id before: {id}");

        await server.PingAsync(); // init everything
        // Verify server is detected as per test config
        Assert.Equal(serverType, server.ServerType);
        var db = conn.GetDatabase();

        // Record baseline counters after initial connection
        Assert.Equal(0, testServer.SetCmdCount);
        Assert.Equal(0, testServer.MovedResponseCount);
        var initialConnectionCount = testServer.TotalClientCount;

        // Execute SET command: This should receive MOVED → reconnect → retry → succeed
        var setResult = await db.StringSetAsync(key, "testvalue");

        // Assert: Verify SET command succeeded
        Assert.True(setResult, "SET command should return true (OK)");

        // Verify the value was actually stored (proving retry succeeded)
        var retrievedValue = await db.StringGetAsync(key);
        Assert.Equal("testvalue", (string?)retrievedValue);

        // Verify SET command was executed twice: once with MOVED response, once successfully
        Assert.Equal(2, testServer.SetCmdCount);

        // Verify MOVED response was returned exactly once
        Assert.Equal(1, testServer.MovedResponseCount);

        // Verify reconnection occurred: connection count should have increased by 1
        Assert.Equal(initialConnectionCount + 1, testServer.TotalClientCount);
        id = await server.ExecuteAsync("client", "id");
        log?.WriteLine($"Client id after: {id}");
    }
}
