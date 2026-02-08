using System;
using System.Threading.Tasks;
using Xunit;

namespace StackExchange.Redis.Tests;

[Collection(NonParallelCollection.Name)]
public class HotKeysTests(ITestOutputHelper output, SharedConnectionFixture fixture) : TestBase(output, fixture)
{
    private IInternalConnectionMultiplexer GetServer(out IServer server)
        => GetServer(RedisKey.Null, out server);

    private IInternalConnectionMultiplexer GetServer(in RedisKey key, out IServer server)
    {
        var muxer = Create(require: RedisFeatures.v8_4_0_rc1); // TODO: 8.6
        server = key.IsNull ? muxer.GetServer(muxer.GetEndPoints()[0]) : muxer.GetServer(key);
        server.HotKeysStop(CommandFlags.FireAndForget);
        server.HotKeysReset(CommandFlags.FireAndForget);
        return muxer;
    }

    [Fact]
    public void GetWhenEmptyIsNull()
    {
        using var muxer = GetServer(out var server);
        Assert.Null(server.HotKeysGet());
    }

    [Fact]
    public async Task GetWhenEmptyIsNullAsync()
    {
        await using var muxer = GetServer(out var server);
        Assert.Null(await server.HotKeysGetAsync());
    }

    [Fact]
    public void StopWhenNotRunningIsFalse()
    {
        using var muxer = GetServer(out var server);
        Assert.False(server.HotKeysStop());
    }

    [Fact]
    public async Task StopWhenNotRunningIsFalseAsync()
    {
        await using var muxer = GetServer(out var server);
        Assert.False(await server.HotKeysStopAsync());
    }

    [Fact]
    public void CanStartStopReset()
    {
        RedisKey key = Me();
        using var muxer = GetServer(key, out var server);
        server.HotKeysStart();
        var db = muxer.GetDatabase();
        db.KeyDelete(key, flags: CommandFlags.FireAndForget);
        for (int i = 0; i < 20; i++)
        {
            db.StringIncrement(key, flags: CommandFlags.FireAndForget);
        }

        var result = server.HotKeysGet();
        Assert.NotNull(result);
        Assert.True(result.TrackingActive);
        CheckSimpleWithKey(key, result);

        Assert.True(server.HotKeysStop());
        result = server.HotKeysGet();
        Assert.NotNull(result);
        Assert.False(result.TrackingActive);
        CheckSimpleWithKey(key, result);

        server.HotKeysReset();
        result = server.HotKeysGet();
        Assert.Null(result);
    }

    private static void CheckSimpleWithKey(RedisKey key, HotKeysResult hotKeys)
    {
        Assert.True(hotKeys.CollectionDuration > TimeSpan.Zero);
        Assert.True(hotKeys.CollectionStartTime > new DateTime(2026, 2, 1));
        var cpu = Assert.Single(hotKeys.CpuByKey);
        Assert.Equal(key, cpu.Key);
        Assert.True(cpu.Duration > TimeSpan.Zero);
        var net = Assert.Single(hotKeys.NetworkBytesByKey);
        Assert.Equal(key, net.Key);
        Assert.True(net.Bytes > 0);

        Assert.Equal(1, hotKeys.SampleRatio);
        var slots = Assert.Single(hotKeys.SelectedSlots);
        Assert.Equal(0, slots.From);
        Assert.Equal(16383, slots.To);

        Assert.True(hotKeys.TotalCpuTime > TimeSpan.Zero);
        Assert.True(hotKeys.TotalCpuTimeSystem >= TimeSpan.Zero);
        Assert.True(hotKeys.TotalCpuTimeUser >= TimeSpan.Zero);
        Assert.True(hotKeys.TotalNetworkBytes > 0);
        Assert.True(hotKeys.TotalNetworkBytes2 > 0);
    }

    [Fact]
    public async Task CanStartStopResetAsync()
    {
        RedisKey key = Me();
        await using var muxer = GetServer(key, out var server);
        await server.HotKeysStartAsync();
        var db = muxer.GetDatabase();
        await db.KeyDeleteAsync(key, flags: CommandFlags.FireAndForget);
        for (int i = 0; i < 20; i++)
        {
            await db.StringIncrementAsync(key, flags: CommandFlags.FireAndForget);
        }

        var result = await server.HotKeysGetAsync();
        Assert.NotNull(result);
        Assert.True(result.TrackingActive);
        CheckSimpleWithKey(key, result);

        Assert.True(await server.HotKeysStopAsync());
        result = await server.HotKeysGetAsync();
        Assert.NotNull(result);
        Assert.False(result.TrackingActive);
        CheckSimpleWithKey(key, result);

        await server.HotKeysResetAsync();
        result = await server.HotKeysGetAsync();
        Assert.Null(result);
    }
}
