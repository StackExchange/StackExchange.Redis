using System;
using System.IO;
using System.Threading.Tasks;
using StackExchange.Redis.Tests.Helpers;
using Xunit;

namespace StackExchange.Redis.Tests.RetryTests;

[RunPerProtocol]
public class RetryEndToEndTests(ITestOutputHelper log)
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

    // Exploratory (no retry wrapper yet): flip the server into a LOADING state *after* connecting,
    // then issue a GET and observe what exception type SE.Redis surfaces. No hard assertion on the
    // type yet - we just want to see it in the log so we can decide how the circuit-breaker should
    // classify it.
    [Fact]
    public async Task LoadingSurfacesAs()
    {
        using var server = new InProcessTestServer(log);
        await using var conn = await server.ConnectAsync(log: Log);
        Assert.True(conn.IsConnected);

        var db = conn.GetDatabase();
        Assert.True(await db.StringSetAsync("retry:loading", "before")); // works before loading

        server.IsLoading = true;
        try
        {
            var value = await db.StringGetAsync("retry:loading");
            Log.WriteLine($"No exception; got value: {value}");
        }
        catch (Exception ex)
        {
            Log.WriteLine($"Exception type: {ex.GetType().FullName}");
            Log.WriteLine($"Message: {ex.Message}");
        }
    }
}
