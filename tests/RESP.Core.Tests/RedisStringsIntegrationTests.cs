using System.Threading.Tasks;
using RESPite.Redis;
using RESPite.Redis.Alt; // needed for AsStrings() etc
using Xunit;
using FactAttribute = StackExchange.Redis.Tests.FactAttribute;

namespace RESP.Core.Tests;

public class RedisStringsIntegrationTests(ConnectionFixture fixture, ITestOutputHelper log)
    : IntegrationTestBase(fixture, log)
{
    [Fact]
    public void Incr()
    {
        var key = Me();

        using var conn = GetConnection();
        var ctx = conn.Context;
        for (int i = 0; i < 5; i++)
        {
            ctx.AsStrings().Incr(key);
        }
        var result = ctx.AsStrings().GetInt32(key);
        Assert.Equal(5, result);
    }

    [Fact]
    public async Task IncrAsync()
    {
        var key = Me();

        await using var conn = GetConnection();
        var ctx = conn.Context;
        for (int i = 0; i < 5; i++)
        {
            await ctx.AsStrings().IncrAsync(key);
        }
        var result = await ctx.AsStrings().GetInt32Async(key);
        Assert.Equal(5, result);
    }
}
