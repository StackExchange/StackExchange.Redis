using System;
using System.Threading.Tasks;
using Xunit;
using Xunit.Abstractions;

namespace StackExchange.Redis.Tests.Issues
{
    public class Issue2392Tests : TestBase
    {
        public Issue2392Tests(ITestOutputHelper output) : base(output) { }

        [Fact]
        public async Task Execute()
        {
            var options = new ConfigurationOptions()
            {
                BacklogPolicy = new()
                {
                    QueueWhileDisconnected = true,
                    AbortPendingOnConnectionFailure = false,
                },
                AbortOnConnectFail = false,
                ConnectTimeout = 1,
                ConnectRetry = 0,
                AsyncTimeout = 1,
                SyncTimeout = 1,
                AllowAdmin = true,
            };
            options.EndPoints.Add("127.0.0.1:1234");

            using var conn = await ConnectionMultiplexer.ConnectAsync(options, Writer);
            var key = Me();
            var db = conn.GetDatabase();
            var server = conn.GetServerSnapshot()[0];

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
