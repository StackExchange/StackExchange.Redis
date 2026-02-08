using System.Threading.Tasks;
using Xunit;

namespace StackExchange.Redis.Tests;

[Collection(NonParallelCollection.Name)]
public class HotKeysTests(ITestOutputHelper output, SharedConnectionFixture fixture) : TestBase(output, fixture)
{
    private IInternalConnectionMultiplexer GetServer(out IServer server)
        => GetServer(RedisKey.Null,  out server);

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
        muxer.GetDatabase().StringSet(key, "value1");
        var result = server.HotKeysGet();
        Assert.NotNull(result);
        Assert.True(result.TrackingActive);

        Assert.True(server.HotKeysStop());
        result = server.HotKeysGet();
        Assert.NotNull(result);
        Assert.False(result.TrackingActive);
        Assert.NotNull(result);

        server.HotKeysReset();
        result = server.HotKeysGet();
        Assert.Null(result);
    }

    [Fact]
    public async Task CanStartStopResetAsync()
    {
        RedisKey key = Me();
        await using var muxer = GetServer(key, out var server);
        await server.HotKeysStartAsync();
        await muxer.GetDatabase().StringSetAsync(key, "value1");
        var result = await server.HotKeysGetAsync();
        Assert.NotNull(result);
        Assert.True(result.TrackingActive);

        Assert.True(await server.HotKeysStopAsync());
        result = await server.HotKeysGetAsync();
        Assert.NotNull(result);
        Assert.False(result.TrackingActive);
        Assert.NotNull(result);

        await server.HotKeysResetAsync();
        result = await server.HotKeysGetAsync();
        Assert.Null(result);
    }
}
