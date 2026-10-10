using System;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace StackExchange.Redis.Tests;

/// <summary>
/// <see cref="ConnectionMultiplexer.GetStormLog"/>: what was awaiting a reply when a timeout found the queue deep.
/// </summary>
public class StormLogTests(ITestOutputHelper output) : TestBase(output)
{
    [Fact]
    public async Task ATimeoutBehindADeepQueueWritesTheStormLog()
    {
        using var server = new InProcessTestServer(Output);
        try
        {
            var config = server.GetClientConfig();
            config.AsyncTimeout = 500;
            await using var conn = await ConnectionMultiplexer.ConnectAsync(config);
            conn.StormLogThreshold = 3;
            var db = conn.GetDatabase();
            await db.PingAsync();
            Assert.Null(conn.GetStormLog());

            server.SetLatency(TimeSpan.FromSeconds(30)); // every reply from here on is held
            var pending = Enumerable.Range(0, 5).Select(i => db.StringGetAsync($"storm:{i}")).ToArray();
            foreach (var task in pending)
            {
                await Assert.ThrowsAsync<RedisTimeoutException>(() => task.WaitAsync(TimeSpan.FromSeconds(10)));
            }

            var log = conn.GetStormLog();
            Log(log ?? "(null)");
            Assert.NotNull(log);
            Assert.Contains("Storm log for", log);
            Assert.Contains("Sent, awaiting response from server:", log);
            Assert.Contains("GET storm:", log);

            conn.ResetStormLog();
            Assert.Null(conn.GetStormLog());
        }
        finally
        {
            server.SetLatency(TimeSpan.Zero);
        }
    }

    [Fact]
    public async Task AShallowQueueWritesNone()
    {
        using var server = new InProcessTestServer(Output);
        try
        {
            var config = server.GetClientConfig();
            config.AsyncTimeout = 500;
            await using var conn = await ConnectionMultiplexer.ConnectAsync(config);
            conn.StormLogThreshold = 10;
            var db = conn.GetDatabase();
            await db.PingAsync();

            server.SetLatency(TimeSpan.FromSeconds(30));
            await Assert.ThrowsAsync<RedisTimeoutException>(() => db.StringGetAsync("calm").WaitAsync(TimeSpan.FromSeconds(10)));

            Assert.Null(conn.GetStormLog());
        }
        finally
        {
            server.SetLatency(TimeSpan.Zero);
        }
    }
}
