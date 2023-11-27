using System;
using System.Threading.Tasks;
using Xunit;
using Xunit.Abstractions;

namespace StackExchange.Redis.Tests;

[Collection(NonParallelCollection.Name)]
public class CommandTimeoutTests : TestBase
{
    public CommandTimeoutTests(ITestOutputHelper output) : base (output) { }

    [FactLongRunning]
    public async Task DefaultHeartbeatTimeout()
    {
        var options = ConfigurationOptions.Parse(TestConfig.Current.PrimaryServerAndPort);
        options.AllowAdmin = true;
        options.AsyncTimeout = 1000;

        using var pauseConn = ConnectionMultiplexer.Connect(options);
        using var conn = ConnectionMultiplexer.Connect(options);

        var pauseServer = GetServer(pauseConn);
        var pauseTask = pauseServer.ExecuteAsync("CLIENT", "PAUSE", 5000);

        var key = Me();
        var db = conn.GetDatabase();
        var sw = ValueStopwatch.StartNew();
        var ex = await Assert.ThrowsAsync<RedisTimeoutException>(async () => await db.StringGetAsync(key));
        Log(ex.Message);
        var duration = sw.GetElapsedTime();
        Assert.True(duration < TimeSpan.FromSeconds(4000), $"Duration ({duration.Milliseconds} ms) should be less than 4000ms");

        // Await as to not bias the next test
        await pauseTask;
    }

#if DEBUG
    [Fact]
    public async Task DefaultHeartbeatLowTimeout()
    {
        var options = ConfigurationOptions.Parse(TestConfig.Current.PrimaryServerAndPort);
        options.AllowAdmin = true;
        options.AsyncTimeout = 50;
        options.HeartbeatInterval = TimeSpan.FromMilliseconds(100);

        using var pauseConn = ConnectionMultiplexer.Connect(options);
        using var conn = ConnectionMultiplexer.Connect(options);

        var pauseServer = GetServer(pauseConn);
        var pauseTask = pauseServer.ExecuteAsync("CLIENT", "PAUSE", 2000);

        var key = Me();
        var db = conn.GetDatabase();
        var sw = ValueStopwatch.StartNew();
        var ex = await Assert.ThrowsAsync<RedisTimeoutException>(async () => await db.StringGetAsync(key));
        Log(ex.Message);
        var duration = sw.GetElapsedTime();
        Assert.True(duration < TimeSpan.FromSeconds(250), $"Duration ({duration.Milliseconds} ms) should be less than 250ms");

        // Await as to not bias the next test
        await pauseTask;
    }
#endif
}
