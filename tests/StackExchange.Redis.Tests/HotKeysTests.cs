using System;
using System.Threading.Tasks;
using Xunit;

namespace StackExchange.Redis.Tests;

[RunPerProtocol]
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
        Assert.True(hotKeys.CollectionDurationMilliseconds >= 0, nameof(hotKeys.CollectionDurationMilliseconds));
        Assert.True(hotKeys.CollectionStartTimeUnixMilliseconds >= 0, nameof(hotKeys.CollectionStartTimeUnixMilliseconds));

        Assert.Equal(1, hotKeys.CpuByKey.Length);
        var cpu = hotKeys.CpuByKey[0];
        Assert.Equal(key, cpu.Key);
        Assert.True(cpu.DurationMicroseconds >= 0,  nameof(cpu.DurationMicroseconds));

        Assert.Equal(1,  hotKeys.NetworkBytesByKey.Length);
        var net = hotKeys.NetworkBytesByKey[0];
        Assert.Equal(key, net.Key);
        Assert.True(net.Bytes > 0, nameof(net.Bytes));

        Assert.Equal(1, hotKeys.SampleRatio);

        Assert.Equal(1, hotKeys.SelectedSlots.Length);
        var slots = hotKeys.SelectedSlots[0];
        Assert.Equal(SlotRange.MinSlot, slots.From);
        Assert.Equal(SlotRange.MaxSlot, slots.To);

        Assert.True(hotKeys.TotalCpuTimeMicroseconds >= 0,  nameof(hotKeys.TotalCpuTimeMicroseconds));
        Assert.True(hotKeys.TotalCpuTimeSystemMilliseconds >= 0, nameof(hotKeys.TotalCpuTimeSystemMilliseconds));
        Assert.True(hotKeys.TotalCpuTimeUserMilliseconds >= 0,  nameof(hotKeys.TotalCpuTimeUserMilliseconds));
        Assert.True(hotKeys.TotalNetworkBytes > 0,  nameof(hotKeys.TotalNetworkBytes));
        Assert.True(hotKeys.TotalNetworkBytes2 > 0,   nameof(hotKeys.TotalNetworkBytes2));
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
