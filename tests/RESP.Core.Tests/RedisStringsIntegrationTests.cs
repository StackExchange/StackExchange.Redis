using System.Threading.Tasks;
using RESPite.Redis;
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

        using var conn = GetConnection(out var context);
        for (int i = 0; i < 5; i++)
        {
            context.Strings.Incr(key);
        }
        var result = context.Strings.GetInt32(key);
        Assert.Equal(5, result);
    }

    [Fact]
    public async Task IncrAsync()
    {
        var key = Me();

        await using var conn = GetConnection(out var context);
        for (int i = 0; i < 5; i++)
        {
            await context.Strings.IncrAsync(key);
        }
        var result = await context.Strings.GetInt32Async(key);
        Assert.Equal(5, result);
    }
}
