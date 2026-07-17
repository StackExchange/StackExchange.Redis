using System;
using System.Net;
using System.Threading.Tasks;
using StackExchange.Redis.Availability;
using StackExchange.Redis.Server;
using Xunit;

namespace StackExchange.Redis.Tests.RetryTests;

[RunPerProtocol]
public class RetryEndToEndTests(ITestOutputHelper log) : TestBase(log)
{
    // End-to-end: a server that answers the first couple of GETs with a transient LOADING error, then
    // serves normally. Wrapping the database with .WithRetry should transparently ride through the LOADING
    // responses; we can then observe (via the server's counter) that it really did take three GETs before
    // one succeeded.
    [Fact]
    public async Task WithRetry_RidesOutTransientLoading()
    {
        using var server = new LoadingServer(Output);
        await using var conn = await server.ConnectAsync(log: Writer);
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

        var value = await retryDb.StringGetAsync(key);

        Assert.Equal("hello", value); // retries rode out the LOADING responses
        Assert.Equal(0, server.LoadingOps); // both LOADING responses were consumed
        Assert.Equal(3, server.GetOpsReceived); // 2 x LOADING + 1 x success
    }

    // Multi-group + WithRetry: two backends hold *different* values for the same key. The group is weighted
    // towards A, so a normal read returns A's value. When A drops into LOADING, the faults trip A's
    // (deliberately hair-trigger) circuit-breaker; the group reroutes to B, and the retry wrapper rides that
    // failover transparently - the very same StringGet now returns B's value, with no caller intervention and
    // no configuration change.
    [Fact]
    public async Task WithRetry_FailsOverBetweenGroupsOnLoading()
    {
        EndPoint alpha = new DnsEndPoint("alpha", 6379);
        EndPoint bravo = new DnsEndPoint("bravo", 6379);
        using var serverA = new InProcessTestServer(Output, endpoint: alpha);
        using var serverB = new InProcessTestServer(Output, endpoint: bravo);

        RedisKey key = "retry:multigroup";

        // seed each backend with its own distinct value (direct connections, so each cache gets its own)
        await using (var seedA = await serverA.ConnectAsync())
        {
            Assert.True(await seedA.GetDatabase().StringSetAsync(key, "from-A"));
        }
        await using (var seedB = await serverB.ConnectAsync())
        {
            Assert.True(await seedB.GetDatabase().StringSetAsync(key, "from-B"));
        }

        var probe = new ControllableProbe();

        // A carries a hair-trigger breaker (trips on the first fault); B keeps the default (never trips here)
        var configA = serverA.GetClientConfig();
        configA.CircuitBreaker = new CircuitBreaker.Builder
        {
            MinimumNumberOfFailures = 1,
            FailureRateThreshold = 1,
        };

        ConnectionGroupMember[] members =
        [
            new(configA, "A") { Weight = 9 }, // highest weight -> initially active
            new(serverB.GetClientConfig(), "B") { Weight = 1 }, // failover target
        ];

        var options = new MultiGroupOptions
        {
            HealthCheck = new HealthCheck
            {
                Probe = probe,
                Interval = TimeSpan.FromMinutes(30), // huge: the breaker fast-path is what reroutes us
                ProbeCount = 1,
                ProbeTimeout = TimeSpan.FromSeconds(5),
            },
        };

        await using var conn = await ConnectionMultiplexer.ConnectGroupAsync(members, options);
        Assert.True(conn.IsConnected);
        Assert.Same(members[0], conn.ActiveMember); // A is active (highest weight)

        // failover enabled, plenty of attempts, no artificial delay between them
        var policy = new RetryPolicy
        {
            MaxAttempts = 20,
            MaxAttemptsBeforeFailover = 1,
            RetryDelay = TimeSpan.Zero,
            JitterMax = TimeSpan.Zero,
        };
        var db = conn.GetDatabase().WithRetry(policy);

        // normal read: routed to the active (weighted) member, A
        string? before = await db.StringGetAsync(key);
        Assert.Equal("from-A", before);

        // knock A into LOADING and hold it down; nothing else about the setup changes
        serverA.IsLoading = true;
        probe.MarkDown(alpha);

        // the *same* call now transparently rides the circuit-break failover across to B
        string? after = await db.StringGetAsync(key);
        Assert.Equal("from-B", after);
        Assert.Same(members[1], conn.ActiveMember); // we really did move to B
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
