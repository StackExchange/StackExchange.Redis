using System;
using System.Threading.Tasks;
using Xunit;

namespace StackExchange.Redis.Tests;

[RunPerProtocol]
public class InProcessDatabaseUnitTests(ITestOutputHelper output)
{
    [Fact]
    public async Task DatabasesAreIsolatedAndCanBeFlushed()
    {
        using var server = new InProcessTestServer(output);
        await using var conn = await server.ConnectAsync();

        var admin = conn.GetServer(conn.GetEndPoints()[0]);
        var key = (RedisKey)Guid.NewGuid().ToString("n");
        var db0 = conn.GetDatabase(0);
        var db1 = conn.GetDatabase(1);

        db0.KeyDelete(key, CommandFlags.FireAndForget);
        db1.KeyDelete(key, CommandFlags.FireAndForget);
        db0.StringSet(key, "a");
        db1.StringSet(key, "b");

        Assert.Equal("a", db0.StringGet(key));
        Assert.Equal("b", db1.StringGet(key));
        Assert.Equal(1, admin.DatabaseSize(0));
        Assert.Equal(1, admin.DatabaseSize(1));

        admin.FlushDatabase(0);
        Assert.True(db0.StringGet(key).IsNull);
        Assert.Equal("b", db1.StringGet(key));

        admin.FlushAllDatabases();
        Assert.True(db1.StringGet(key).IsNull);
        Assert.Equal(0, admin.DatabaseSize(0));
        Assert.Equal(0, admin.DatabaseSize(1));
    }
}
