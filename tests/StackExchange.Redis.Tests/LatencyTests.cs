using System.Threading.Tasks;
using Xunit;

namespace StackExchange.Redis.Tests;

[Collection(NonParallelCollection.Name)]
public class LatencyTests(ITestOutputHelper output, SharedConnectionFixture fixture) : TestBase(output, fixture)
{
    [Fact]
    public async Task CanCallDoctor()
    {
        await using var conn = Create();

        var server = conn.GetServer(conn.GetEndPoints()[0]);
        string? doctor = server.LatencyDoctor();
        Assert.NotNull(doctor);
        Assert.NotEqual("", doctor);

        doctor = await server.LatencyDoctorAsync();
        Assert.NotNull(doctor);
        Assert.NotEqual("", doctor);
    }

    [Fact]
    public async Task CanReset()
    {
        await using var conn = Create();

        var server = conn.GetServer(conn.GetEndPoints()[0]);
        _ = server.LatencyReset();
        var count = await server.LatencyResetAsync(["command"]);
        Assert.Equal(0, count);

        count = await server.LatencyResetAsync(["command", "fast-command"]);
        Assert.Equal(0, count);
    }

    [Fact]
    public async Task GetLatest()
    {
        Skip.UnlessLongRunning();
        await using var conn = Create(allowAdmin: true);

        var server = conn.GetServer(conn.GetEndPoints()[0]);
        server.ConfigSet("latency-monitor-threshold", 50);
        server.LatencyReset();
        var arr = server.LatencyLatest();
        Assert.Empty(arr);

        var now = await server.TimeAsync();
        server.Execute("debug", "sleep", "0.5"); // cause something to be slow

        arr = await server.LatencyLatestAsync();
        var item = Assert.Single(arr);
        Assert.Equal("command", item.EventName);
        Assert.True(item.DurationMilliseconds >= 400 && item.DurationMilliseconds <= 600);
        Assert.Equal(item.DurationMilliseconds, item.MaxDurationMilliseconds);
        Assert.True(item.Timestamp >= now.AddSeconds(-2) && item.Timestamp <= now.AddSeconds(2));
    }

    [Fact]
    public async Task GetHistory()
    {
        Skip.UnlessLongRunning();
        await using var conn = Create(allowAdmin: true);

        var server = conn.GetServer(conn.GetEndPoints()[0]);
        server.ConfigSet("latency-monitor-threshold", 50);
        server.LatencyReset();
        var arr = server.LatencyHistory("command");
        Assert.Empty(arr);

        var now = await server.TimeAsync();
        server.Execute("debug", "sleep", "0.5"); // cause something to be slow

        arr = await server.LatencyHistoryAsync("command");
        var item = Assert.Single(arr);
        Assert.True(item.DurationMilliseconds >= 400 && item.DurationMilliseconds <= 600);
        Assert.True(item.Timestamp >= now.AddSeconds(-2) && item.Timestamp <= now.AddSeconds(2));
    }
}
