using System;
using System.Threading.Tasks;
using Xunit;

namespace StackExchange.Redis.Tests;

[RunPerProtocol]
public class HeartbeatTests(ITestOutputHelper output, SharedConnectionFixture fixture) : TestBase(output, fixture)
{
    [Fact]
    public async Task TestAutomaticHeartbeat()
    {
        RedisValue oldTimeout = RedisValue.Null;
        await using var configConn = Create(allowAdmin: true);

        try
        {
            configConn.GetDatabase();
            var srv = GetAnyPrimary(configConn);
            oldTimeout = srv.ConfigGet("timeout")[0].Value;
            Log("Old Timeout: " + oldTimeout);
            srv.ConfigSet("timeout", 3);

            await using var innerConn = Create();
            var innerDb = innerConn.GetDatabase();
            await innerDb.PingAsync(); // need to wait to pick up configuration etc

            var before = innerConn.OperationCount;

            Log("sleeping to test heartbeat...");
            await Task.Delay(TimeSpan.FromSeconds(5)).ForAwait();

            var after = innerConn.OperationCount;
            Assert.True(after >= before + 1, $"after: {after}, before: {before}");
        }
        finally
        {
            if (!oldTimeout.IsNull)
            {
                Log("Resetting old timeout: " + oldTimeout);
                var srv = GetAnyPrimary(configConn);
                srv.ConfigSet("timeout", oldTimeout);
            }
        }
    }
}
