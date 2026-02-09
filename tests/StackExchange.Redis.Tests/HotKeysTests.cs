using System;
using System.Threading.Tasks;
using Xunit;

namespace StackExchange.Redis.Tests;

public class HotKeysClusterTests(ITestOutputHelper output, SharedConnectionFixture fixture) : HotKeysTests(output, fixture)
{
    protected override string GetConfiguration() => TestConfig.Current.ClusterServersAndPorts + ",connectTimeout=10000";

    [Fact]
    public void CanUseClusterFilter()
    {
        var key = Me();
        using var muxer = GetServer(key, out var server);
        Log($"server: {Format.ToString(server.EndPoint)}, key: '{key}'");

        var slot = muxer.HashSlot(key);
        server.HotKeysStart(slots: [(short)slot]);

        var db = muxer.GetDatabase();
        db.KeyDelete(key, flags: CommandFlags.FireAndForget);
        for (int i = 0; i < 20; i++)
        {
            db.StringIncrement(key, flags: CommandFlags.FireAndForget);
        }

        server.HotKeysStop();
        var result = server.HotKeysGet();
        Assert.NotNull(result);
        var slots = result.SelectedSlots;
        Assert.Equal(1, slots.Length);
        Assert.Equal(slot, slots[0].From);
        Assert.Equal(slot, slots[0].To);

        Assert.Equal(1, result.CpuByKey.Length);
        Assert.Equal(key, result.CpuByKey[0].Key);

        Assert.Equal(1, result.NetworkBytesByKey.Length);
        Assert.Equal(key, result.NetworkBytesByKey[0].Key);
    }
}

[RunPerProtocol]
[Collection(NonParallelCollection.Name)]
public class HotKeysTests(ITestOutputHelper output, SharedConnectionFixture fixture) : TestBase(output, fixture)
{
    private protected IInternalConnectionMultiplexer GetServer(out IServer server)
        => GetServer(RedisKey.Null, out server);

    private protected IInternalConnectionMultiplexer GetServer(in RedisKey key, out IServer server)
    {
        var muxer = Create(require: RedisFeatures.v8_4_0_rc1, allowAdmin: true); // TODO: 8.6
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
        CheckSimpleWithKey(key, result, server);

        Assert.True(server.HotKeysStop());
        result = server.HotKeysGet();
        Assert.NotNull(result);
        Assert.False(result.TrackingActive);
        CheckSimpleWithKey(key, result, server);

        server.HotKeysReset();
        result = server.HotKeysGet();
        Assert.Null(result);
    }

    private void CheckSimpleWithKey(RedisKey key, HotKeysResult hotKeys, IServer server)
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

        if (server.ServerType is ServerType.Cluster)
        {
            Assert.NotEqual(0, hotKeys.SelectedSlots.Length);
            Log("Cluster mode detected; not enforcing slots, but:");
            foreach (var slot in hotKeys.SelectedSlots)
            {
                Log($"  {slot}");
            }
        }
        else
        {
            Assert.Equal(1, hotKeys.SelectedSlots.Length);
            var slots = hotKeys.SelectedSlots[0];
            Assert.Equal(SlotRange.MinSlot, slots.From);
            Assert.Equal(SlotRange.MaxSlot, slots.To);
        }

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
        CheckSimpleWithKey(key, result, server);

        Assert.True(await server.HotKeysStopAsync());
        result = await server.HotKeysGetAsync();
        Assert.NotNull(result);
        Assert.False(result.TrackingActive);
        CheckSimpleWithKey(key, result, server);

        await server.HotKeysResetAsync();
        result = await server.HotKeysGetAsync();
        Assert.Null(result);
    }

    [Fact]
    public async Task DurationFilterAsync()
    {
        Skip.UnlessLongRunning(); // time-based tests are horrible

        RedisKey key = Me();
        await using var muxer = GetServer(key, out var server);
        await server.HotKeysStartAsync(duration: TimeSpan.FromSeconds(1));
        var db = muxer.GetDatabase();
        await db.KeyDeleteAsync(key, flags: CommandFlags.FireAndForget);
        for (int i = 0; i < 20; i++)
        {
            await db.StringIncrementAsync(key, flags: CommandFlags.FireAndForget);
        }
        var before = await server.HotKeysGetAsync();
        await Task.Delay(TimeSpan.FromSeconds(2));
        var after = await server.HotKeysGetAsync();

        Assert.NotNull(before);
        Assert.True(before.TrackingActive);

        Assert.NotNull(after);
        Assert.False(after.TrackingActive);

        Log($"Duration: {after.CollectionDurationMilliseconds}ms");
        Assert.True(after.CollectionDurationMilliseconds > 900 && after.CollectionDurationMilliseconds < 1100);
    }
}
