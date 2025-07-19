using System.Threading.Tasks;
using Xunit;

namespace StackExchange.Redis.Tests;

public class SSDBTests(ITestOutputHelper output) : TestBase(output)
{
    [Fact]
    public async Task ConnectToSSDB()
    {
        Skip.IfNoConfig(nameof(TestConfig.Config.SSDBServer), TestConfig.Current.SSDBServer);

        await using var conn = await ConnectionMultiplexer.ConnectAsync(new ConfigurationOptions
        {
            EndPoints = { { TestConfig.Current.SSDBServer, TestConfig.Current.SSDBPort } },
            CommandMap = CommandMap.SSDB,
        });

        RedisKey key = Me();
        var db = conn.GetDatabase();
        db.KeyDelete(key, CommandFlags.FireAndForget);
        Assert.True(db.StringGet(key).IsNull);
        db.StringSet(key, "abc", flags: CommandFlags.FireAndForget);
        Assert.Equal("abc", db.StringGet(key));
    }
}
