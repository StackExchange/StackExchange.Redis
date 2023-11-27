using System.Threading.Tasks;
using Xunit;
using Xunit.Abstractions;

namespace StackExchange.Redis.Tests;

[Collection(SharedConnectionFixture.Key)]
public class MemoryTests : TestBase
{
    public MemoryTests(ITestOutputHelper output, SharedConnectionFixture fixture) : base(output, fixture) { }

    [Fact]
    public async Task CanCallDoctor()
    {
        using var conn = Create(require: RedisFeatures.v4_0_0);

        var server = conn.GetServer(conn.GetEndPoints()[0]);
        string? doctor = server.MemoryDoctor();
        Assert.NotNull(doctor);
        Assert.NotEqual("", doctor);

        doctor = await server.MemoryDoctorAsync();
        Assert.NotNull(doctor);
        Assert.NotEqual("", doctor);
    }

    [Fact]
    public async Task CanPurge()
    {
        using var conn = Create(require: RedisFeatures.v4_0_0);

        var server = conn.GetServer(conn.GetEndPoints()[0]);
        server.MemoryPurge();
        await server.MemoryPurgeAsync();

        await server.MemoryPurgeAsync();
    }

    [Fact]
    public async Task GetAllocatorStats()
    {
        using var conn = Create(require: RedisFeatures.v4_0_0);

        var server = conn.GetServer(conn.GetEndPoints()[0]);

        var stats = server.MemoryAllocatorStats();
        Assert.False(string.IsNullOrWhiteSpace(stats));

        stats = await server.MemoryAllocatorStatsAsync();
        Assert.False(string.IsNullOrWhiteSpace(stats));
    }

    [Fact]
    public async Task GetStats()
    {
        using var conn = Create(require: RedisFeatures.v4_0_0);

        var server = conn.GetServer(conn.GetEndPoints()[0]);
        var stats = server.MemoryStats();
        Assert.NotNull(stats);
        Assert.Equal(ResultType.Array, stats.Resp2Type);

        var parsed = stats.ToDictionary();

        var alloc = parsed["total.allocated"];
        Assert.Equal(ResultType.Integer, alloc.Resp2Type);
        Assert.True(alloc.AsInt64() > 0);

        stats = await server.MemoryStatsAsync();
        Assert.NotNull(stats);
        Assert.Equal(ResultType.Array, stats.Resp2Type);

        alloc = parsed["total.allocated"];
        Assert.Equal(ResultType.Integer, alloc.Resp2Type);
        Assert.True(alloc.AsInt64() > 0);
    }
}
