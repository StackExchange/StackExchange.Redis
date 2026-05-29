using System;
using System.Threading.Tasks;
using Xunit;

namespace StackExchange.Redis.Tests.Issues
{
    public class Issue2392Tests(ITestOutputHelper output) : TestBase(output)
    {
        [Fact]
        [Trait(TestCategories.Category, TestCategories.SimulatedConnectionFailure)]
        public async Task Execute()
        {
            var options = ConfigurationOptions.Parse(GetConfiguration());
            options.Protocol = TestContext.Current.GetProtocol();
            options.BacklogPolicy = new()
            {
                QueueWhileDisconnected = true,
                AbortPendingOnConnectionFailure = false,
            };
            options.AbortOnConnectFail = false;
            options.ConnectRetry = 0;
            options.AsyncTimeout = 1;
            options.SyncTimeout = 1;
            options.AllowAdmin = true;
            options.AllowSimulateConnectionFailure = true;

            await using var conn = await ConnectionMultiplexer.ConnectAsync(options, Writer);
            var key = Me();
            var db = conn.GetDatabase();
            var server = conn.GetServerSnapshot()[0];
            Assert.SkipUnless(server.CanSimulateConnectionFailure, "Skipping because server cannot simulate connection failure");

            // Fail the connection
            conn.AllowConnect = false;
            server.SimulateConnectionFailure(SimulatedFailureType.All);
            Assert.False(conn.IsConnected);

            await db.StringGetAsync(key, flags: CommandFlags.FireAndForget);
            var ex = await Assert.ThrowsAnyAsync<Exception>(() => db.StringGetAsync(key).WithTimeout(5000));
            Assert.True(ex is RedisTimeoutException or RedisConnectionException);
        }
    }
}
