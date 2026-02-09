using System;
using System.Threading.Tasks;
using Xunit;

namespace StackExchange.Redis.Tests;

public class HotKeysClusterTests(ITestOutputHelper output, SharedConnectionFixture fixture) : HotKeysTests(output, fixture)
{
    protected override string GetConfiguration() => TestConfig.Current.ClusterServersAndPorts + ",connectTimeout=10000";

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void CanUseClusterFilter(bool sample)
    {
        var key = Me();
        using var muxer = GetServer(key, out var server);
        Log($"server: {Format.ToString(server.EndPoint)}, key: '{key}'");

        var slot = muxer.HashSlot(key);
        server.HotKeysStart(slots: [(short)slot], sampleRatio: sample ? 3 : 1, duration: Duration);

        var db = muxer.GetDatabase();
        db.KeyDelete(key, flags: CommandFlags.FireAndForget);
        for (int i = 0; i < 20; i++)
        {
            db.StringIncrement(key, flags: CommandFlags.FireAndForget);
        }

        server.HotKeysStop();
        var result = server.HotKeysGet();
        Assert.NotNull(result);
        Assert.True(result.IsSlotFiltered, nameof(result.IsSlotFiltered));
        var slots = result.SelectedSlots;
        Assert.Equal(1, slots.Length);
        Assert.Equal(slot, slots[0].From);
        Assert.Equal(slot, slots[0].To);

        Assert.Equal(1, result.CpuByKey.Length);
        Assert.Equal(key, result.CpuByKey[0].Key);

        Assert.Equal(1, result.NetworkBytesByKey.Length);
        Assert.Equal(key, result.NetworkBytesByKey[0].Key);

        Assert.True(result.AllCommandSelectedSlotsMicroseconds >= 0, nameof(result.AllCommandSelectedSlotsMicroseconds));
        Assert.True(result.TotalCpuTimeUserMicroseconds >= 0, nameof(result.TotalCpuTimeUserMicroseconds));

        Assert.Equal(sample, result.IsSampled);
        if (sample)
        {
            Assert.Equal(3, result.SampleRatio);
            Assert.True(result.SampledCommandsSelectedSlotsMicroseconds >= 0, nameof(result.SampledCommandsSelectedSlotsMicroseconds));
            Assert.True(result.NetworkBytesSampledCommandsSelectedSlotsRaw >= 0, nameof(result.NetworkBytesSampledCommandsSelectedSlotsRaw));
        }
        else
        {
            Assert.Equal(1, result.SampleRatio);
            Assert.Equal(-1, result.SampledCommandsSelectedSlotsMicroseconds);
            Assert.Equal(-1, result.NetworkBytesSampledCommandsSelectedSlotsRaw);
        }
    }
}

[RunPerProtocol]
[Collection(NonParallelCollection.Name)]
public class HotKeysTests(ITestOutputHelper output, SharedConnectionFixture fixture) : TestBase(output, fixture)
{
    protected TimeSpan Duration => TimeSpan.FromMinutes(1); // ensure we don't leave profiling running

    private protected IConnectionMultiplexer GetServer(out IServer server)
        => GetServer(RedisKey.Null, out server);

    private protected IConnectionMultiplexer GetServer(in RedisKey key, out IServer server)
    {
        var muxer = Create(require: RedisFeatures.v8_6_0, allowAdmin: true);
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
        server.HotKeysStart(duration: Duration);
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
        Assert.Equal(HotKeysMetrics.Cpu | HotKeysMetrics.Network, hotKeys.Metrics);
        Assert.True(hotKeys.CollectionDurationMicroseconds >= 0, nameof(hotKeys.CollectionDurationMicroseconds));
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
        Assert.False(hotKeys.IsSampled, nameof(hotKeys.IsSampled));
        Assert.False(hotKeys.IsSlotFiltered, nameof(hotKeys.IsSlotFiltered));

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

        Assert.True(hotKeys.AllCommandsAllSlotsMicroseconds >= 0,  nameof(hotKeys.AllCommandsAllSlotsMicroseconds));
        Assert.True(hotKeys.TotalCpuTimeSystemMicroseconds >= 0, nameof(hotKeys.TotalCpuTimeSystemMicroseconds));
        Assert.True(hotKeys.TotalCpuTimeUserMicroseconds >= 0,  nameof(hotKeys.TotalCpuTimeUserMicroseconds));
        Assert.True(hotKeys.AllCommandsAllSlotsNetworkBytes > 0,  nameof(hotKeys.AllCommandsAllSlotsNetworkBytes));
        Assert.True(hotKeys.TotalNetworkBytes > 0, nameof(hotKeys.TotalNetworkBytes));
    }

    [Fact]
    public async Task CanStartStopResetAsync()
    {
        RedisKey key = Me();
        await using var muxer = GetServer(key, out var server);
        await server.HotKeysStartAsync(duration: Duration);
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

        var millis = after.CollectionDuration.TotalMilliseconds;
        Log($"Duration: {millis}ms");
        Assert.True(millis > 900 && millis < 1100);
    }

    [Theory]
    [InlineData(HotKeysMetrics.Cpu)]
    [InlineData(HotKeysMetrics.Network)]
    [InlineData(HotKeysMetrics.Network | HotKeysMetrics.Cpu)]
    public async Task MetricsChoiceAsync(HotKeysMetrics metrics)
    {
        RedisKey key = Me();
        await using var muxer = GetServer(key, out var server);
        await server.HotKeysStartAsync(metrics, duration: Duration);
        var db = muxer.GetDatabase();
        await db.KeyDeleteAsync(key, flags: CommandFlags.FireAndForget);
        for (int i = 0; i < 20; i++)
        {
            await db.StringIncrementAsync(key, flags: CommandFlags.FireAndForget);
        }
        await server.HotKeysStopAsync(flags: CommandFlags.FireAndForget);
        var result = await server.HotKeysGetAsync();
        Assert.NotNull(result);
        Assert.Equal(metrics, result.Metrics);
    }

    [Fact]
    public async Task SampleRatioUsageAsync()
    {
        RedisKey key = Me();
        await using var muxer = GetServer(key, out var server);
        await server.HotKeysStartAsync(sampleRatio: 3, duration: Duration);
        var db = muxer.GetDatabase();
        await db.KeyDeleteAsync(key, flags: CommandFlags.FireAndForget);
        for (int i = 0; i < 20; i++)
        {
            await db.StringIncrementAsync(key, flags: CommandFlags.FireAndForget);
        }

        await server.HotKeysStopAsync(flags: CommandFlags.FireAndForget);
        var result = await server.HotKeysGetAsync();
        Assert.NotNull(result);
        Assert.True(result.IsSampled, nameof(result.IsSampled));
        Assert.Equal(3, result.SampleRatio);
        Assert.True(result.TotalNetworkBytes.HasValue);
        Assert.True(result.TotalCpuTime.HasValue);
    }
}
