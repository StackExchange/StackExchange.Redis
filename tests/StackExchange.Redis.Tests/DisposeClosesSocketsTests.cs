using System;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace StackExchange.Redis.Tests;

/// <summary>Disposing a multiplexer closes every socket it opened - the subscription one included.</summary>
public class DisposeClosesSocketsTests(ITestOutputHelper output) : TestBase(output)
{
    [Theory]
    [InlineData(RedisProtocol.Resp2)]
    [InlineData(RedisProtocol.Resp3)]
    public async Task DisposingAMultiplexerLeavesNoClientsBehind(RedisProtocol protocol)
    {
        var name = $"{nameof(DisposingAMultiplexerLeavesNoClientsBehind)}-{protocol}-{Guid.NewGuid():N}";
        await using var observer = Create(allowAdmin: true, shared: false);
        var server = observer.GetServer(TestConfig.Current.PrimaryServerAndPort);

        var options = ConfigurationOptions.Parse(TestConfig.Current.PrimaryServerAndPort);
        options.ClientName = name;
        options.Protocol = protocol;
        options.AllowAdmin = true;
        var conn = await ConnectionMultiplexer.ConnectAsync(options, Writer);
        await conn.GetDatabase().PingAsync();
        var open = (await server.ClientListAsync()).Count(c => c.Name == name);
        Log($"open while connected: {open}");
        Assert.True(open >= 1);

        await conn.DisposeAsync();

        Assert.True(
            await Poll.UntilAsync(() => server.ClientList().All(c => c.Name != name), timeoutMilliseconds: 5000),
            "clients left behind: " + string.Join(", ", server.ClientList().Where(c => c.Name == name).Select(c => $"id={c.Id} cmd={c.LastCommand} flags={c.FlagsRaw}")));
    }
}
