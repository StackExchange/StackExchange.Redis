using System.IO;
using System.Threading.Tasks;
using StackExchange.Redis.Tests.Helpers;
using Xunit;

namespace StackExchange.Redis.Tests.RetryTests;

[RunPerProtocol]
public class BasicRetryTests(ITestOutputHelper log)
{
    protected TextWriter Log { get; } = new TextWriterOutputHelper(log);

    // Baseline: connect to a single in-proc server and exercise the *regular* (non-retry) database.
    // This is the control case the retry suite will build on - no RetryDatabase wrapper yet.
    [Fact]
    public async Task ConnectAndGet()
    {
        using var server = new InProcessTestServer(log);
        await using var conn = await server.ConnectAsync(log: Log);
        Assert.True(conn.IsConnected);

        var db = conn.GetDatabase();

        RedisKey key = "retry:basic";
        Assert.True(await db.StringSetAsync(key, "hello"));

        var value = await db.StringGetAsync(key);
        Assert.Equal("hello", value);

        // a couple more gets, including a miss
        Assert.Equal("hello", await db.StringGetAsync(key));
        Assert.Equal(RedisValue.Null, await db.StringGetAsync("retry:missing"));
    }
}
