using System;
using System.IO;
using System.Threading.Tasks;
using StackExchange.Redis.Availability;
using StackExchange.Redis.KeyspaceIsolation;
using StackExchange.Redis.Server;
using StackExchange.Redis.Tests.Helpers;
using Xunit;

namespace StackExchange.Redis.Tests.RetryTests;

[RunPerProtocol]
public class RetryEndToEndTests(ITestOutputHelper log)
{
    protected TextWriter Log { get; } = new TextWriterOutputHelper(log);

    // End-to-end: a server that answers the first couple of GETs with a transient LOADING error, then
    // serves normally. Wrapping the database with .WithRetry should transparently ride through the LOADING
    // responses; we can then observe (via the server's counter) that it really did take three GETs before
    // one succeeded.
    [Fact]
    public async Task WithRetry_RidesOutTransientLoading()
    {
        using var server = new LoadingServer(log);
        await using var conn = await server.ConnectAsync(log: Log);
        Assert.True(conn.IsConnected);

        var db = conn.GetDatabase();

        RedisKey key = "retry:loading";
        Assert.True(await db.StringSetAsync(key, "hello")); // seed the value before we start failing GETs

        // queue up two LOADING responses; the third GET should succeed
        server.LoadingOps = 2;

        // zero delay/jitter so the test isn't paying the default ~1s retry backoff between attempts
        var policy = new RetryPolicy
        {
            MaxAttempts = 3,
            RetryDelay = TimeSpan.Zero,
            JitterMax = TimeSpan.Zero,
        };
        var retryDb = db.WithRetry(policy);

        // NOTE: explicit category, pending the wrapper picking this up from command categorization
        var value = await retryDb.StringGetAsync(key, CommandFlags.CommandRetryReadOnly);

        Assert.Equal("hello", value); // retries rode out the LOADING responses
        Assert.Equal(0, server.LoadingOps); // both LOADING responses were consumed
        Assert.Equal(3, server.GetOpsReceived); // 2 x LOADING + 1 x success
    }

    // An in-proc server that fails the first LoadingOps GET operations with a transient LOADING error
    // (decrementing the counter each time), then serves normally. Every GET bumps GetOpsReceived so the
    // test can confirm how many attempts actually reached the server.
    private sealed class LoadingServer(ITestOutputHelper? log) : InProcessTestServer(log)
    {
        // the server core processes operations under a lock (single-threaded, like Redis), so plain fields
        // are fine here
        public int GetOpsReceived { get; private set; }

        public int LoadingOps { get; set; }

        protected override TypedRedisValue Get(RedisClient client, in RedisRequest request)
        {
            GetOpsReceived++;

            // while LOADING ops remain, consume one and reply with a transient LOADING error
            if (LoadingOps > 0)
            {
                LoadingOps--;
                return TypedRedisValue.Error("LOADING Redis is loading the dataset in memory");
            }

            return base.Get(client, in request);
        }
    }
}
