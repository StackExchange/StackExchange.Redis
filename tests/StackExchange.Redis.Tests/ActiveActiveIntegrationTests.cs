using System;
using System.Threading.Tasks;
using StackExchange.Redis.Tests.Helpers;
using Xunit;

namespace StackExchange.Redis.Tests;

public class ActiveActiveIntegrationTests(ITestOutputHelper output)
{
    [Fact]
    public async Task ProductionActiveActive()
    {
        var config = TestConfig.Current;
        var eps = config.ActiveActiveEndpoints;
        Assert.SkipUnless(eps is { Length: > 0 }, "no active:active endpoints");

        var writer = new TextWriterOutputHelper(output);
        ConnectionGroupMember[] members = Array.ConvertAll(eps, x => new ConnectionGroupMember(x));
        var muxer = await ConnectionMultiplexer.ConnectGroupAsync(members, log: writer);

        Assert.True(muxer.IsConnected);
        var db = muxer.GetDatabase();
        Assert.True(db.IsConnected(default));
        await db.PingAsync();

        Task last = Task.CompletedTask;
        var ttl = TimeSpan.FromMinutes(5);
        for (int i = 0; i < 100; i++)
        {
            RedisKey key = Guid.NewGuid().ToString();
            _ = db.StringSetAsync(key, i, ttl, flags: CommandFlags.FireAndForget);
            last.RedisFireAndForget(); // in case of fault
            last = db.StringGetAsync(key); // observe the last
        }

        await last;

        foreach (var member in muxer.GetMembers())
        {
            output?.WriteLine($"{member.Name}: {member.Latency.TotalMilliseconds}us");
        }
        output?.WriteLine($"Active: {muxer.ActiveMember?.Name}");
    }
}
