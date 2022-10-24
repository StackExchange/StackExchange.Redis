using System.Linq;
using System.Threading.Tasks;
using Xunit;
using Xunit.Abstractions;

namespace StackExchange.Redis.Tests;

[Collection(SharedConnectionFixture.Key)]
public class ExecuteTests : TestBase
{
    public ExecuteTests(ITestOutputHelper output, SharedConnectionFixture fixture) : base(output, fixture) { }

    [Fact]
    public async Task DBExecute()
    {
        using var conn = Create();

        var db = conn.GetDatabase(4);
        RedisKey key = Me();
        db.StringSet(key, "some value");

        var actual = (string?)db.Execute("GET", key);
        Assert.Equal("some value", actual);

        actual = (string?)await db.ExecuteAsync("GET", key).ForAwait();
        Assert.Equal("some value", actual);
    }

    [Fact]
    public async Task ServerExecute()
    {
        using var conn = Create();

        var server = conn.GetServer(conn.GetEndPoints().First());
        var actual = (string?)server.Execute("echo", "some value");
        Assert.Equal("some value", actual);

        actual = (string?)await server.ExecuteAsync("echo", "some value").ForAwait();
        Assert.Equal("some value", actual);
    }
}
