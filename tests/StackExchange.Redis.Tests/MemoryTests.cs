﻿using System.Threading.Tasks;
using Xunit;

namespace StackExchange.Redis.Tests;

public class MemoryTests(ITestOutputHelper output, SharedConnectionFixture fixture) : TestBase(output, fixture)
{
    [Fact]
    public async Task CanCallDoctor()
    {
        await using var conn = Create(require: RedisFeatures.v4_0_0);

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
        await using var conn = Create(require: RedisFeatures.v4_0_0);

        var server = conn.GetServer(conn.GetEndPoints()[0]);
        server.MemoryPurge();
        await server.MemoryPurgeAsync();

        await server.MemoryPurgeAsync();
    }

    [Fact]
    public async Task GetAllocatorStats()
    {
        await using var conn = Create(require: RedisFeatures.v4_0_0);

        var server = conn.GetServer(conn.GetEndPoints()[0]);

        var stats = server.MemoryAllocatorStats();
        Assert.False(string.IsNullOrWhiteSpace(stats));

        stats = await server.MemoryAllocatorStatsAsync();
        Assert.False(string.IsNullOrWhiteSpace(stats));
    }

    [Fact]
    public async Task GetStats()
    {
        await using var conn = Create(require: RedisFeatures.v4_0_0);

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
