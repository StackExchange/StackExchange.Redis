using System.Threading.Tasks;
using Xunit;

namespace StackExchange.Redis.Build.Tests;

/// <summary>
/// Blocking on a redis call rather than awaiting it - sync over async.
/// </summary>
/// <remarks>
/// The rule is about <em>blocking</em> specifically: an ordinary <c>await</c> is correct usage and must never
/// be flagged, which is most of what the negative cases below are for. Transactions and batches are
/// <see cref="SER305"/>'s, and fire-and-forget is <see cref="SER306"/>'s; this one owns everything else.
/// </remarks>
public class SER307 : Verifier<QueuedResultAnalyzer>
{
    [Fact]
    public Task BlockingOnResult_IsFlagged() => VerifyAsync(
        """
        using StackExchange.Redis;
        class C
        {
            public void M(IDatabase db, RedisKey key)
            {
                var value = {|#0:{|#1:db.StringGetAsync(key)|}.Result|};
            }
        }
        """,
        Diagnostic("SER307").WithLocation(0).WithLocation(1).WithArguments("StringGetAsync"));

    [Fact]
    public Task BlockingOnWait_IsFlagged() => VerifyAsync(
        """
        using StackExchange.Redis;
        class C
        {
            public void M(IDatabase db, RedisKey key)
            {
                {|#0:{|#1:db.StringSetAsync(key, "value")|}.Wait()|};
            }
        }
        """,
        Diagnostic("SER307").WithLocation(0).WithLocation(1).WithArguments("StringSetAsync"));

    [Fact]
    public Task BlockingOnGetAwaiterGetResult_IsFlagged() => VerifyAsync(
        """
        using StackExchange.Redis;
        class C
        {
            public void M(IDatabase db, RedisKey key)
            {
                var value = {|#0:{|#1:db.StringGetAsync(key)|}.GetAwaiter().GetResult()|};
            }
        }
        """,
        Diagnostic("SER307").WithLocation(0).WithLocation(1).WithArguments("StringGetAsync"));

    /// <summary>
    /// <c>IRedisAsync</c> is the root of every async surface, so a server or subscriber call is covered by the
    /// same test rather than needing its own.
    /// </summary>
    [Fact]
    public Task BlockingOnServerCall_IsFlagged() => VerifyAsync(
        """
        using StackExchange.Redis;
        class C
        {
            public void M(IServer server)
            {
                {|#0:{|#1:server.PingAsync()|}.Wait()|};
            }
        }
        """,
        Diagnostic("SER307").WithLocation(0).WithLocation(1).WithArguments("PingAsync"));

    [Fact]
    public Task BlockingOnSubscriberCall_IsFlagged() => VerifyAsync(
        """
        using StackExchange.Redis;
        class C
        {
            public void M(ISubscriber sub, RedisChannel channel)
            {
                var count = {|#0:{|#1:sub.PublishAsync(channel, "value")|}.Result|};
            }
        }
        """,
        Diagnostic("SER307").WithLocation(0).WithLocation(1).WithArguments("PublishAsync"));

    /// <summary>
    /// Unlike SER305, an unreadable flags argument still warns: the harmful reading is far the more likely,
    /// and being wrong here costs a warning rather than a build.
    /// </summary>
    [Fact]
    public Task BlockingWithNonConstantFlags_IsFlagged() => VerifyAsync(
        """
        using StackExchange.Redis;
        class C
        {
            public void M(IDatabase db, RedisKey key, CommandFlags flags)
            {
                var value = {|#0:{|#1:db.StringGetAsync(key, flags)|}.Result|};
            }
        }
        """,
        Diagnostic("SER307").WithLocation(0).WithLocation(1).WithArguments("StringGetAsync"));

    // ---- negative cases ----

    /// <summary>Awaiting is the whole point of the async API, and must never be flagged.</summary>
    [Fact]
    public Task Awaited_IsClean() => VerifyAsync(
        """
        using StackExchange.Redis;
        using System.Threading.Tasks;
        class C
        {
            public async Task M(IDatabase db, RedisKey key)
            {
                var value = await db.StringGetAsync(key);
                await db.StringSetAsync(key, value);
            }
        }
        """);

    /// <summary>
    /// The synchronous API is out of scope for this rule, which is about blocking on the *async* API.
    /// </summary>
    /// <remarks>
    /// Not an endorsement: a blocked thread is a blocked thread, and a synchronous call from a thread-pool
    /// thread starves the pool exactly as sync-over-async does. It is unflagged because deciding when a
    /// synchronous call is legitimate needs to know which thread the caller is on, which an analyzer cannot
    /// see - and a rule that fired on every synchronous call would be unusable.
    /// </remarks>
    [Fact]
    public Task SynchronousApi_IsClean() => VerifyAsync(
        """
        using StackExchange.Redis;
        class C
        {
            public void M(IDatabase db, RedisKey key)
            {
                var value = db.StringGet(key);
                db.StringSet(key, value);
            }
        }
        """);

    /// <summary>Fire-and-forget completes before the call returns, so blocking waits for nothing: SER306.</summary>
    [Fact]
    public Task BlockingOnFireAndForget_IsSER306() => VerifyAsync(
        """
        using StackExchange.Redis;
        class C
        {
            public void M(IDatabase db, RedisKey key)
            {
                {|#0:{|#1:db.StringSetAsync(key, "value", flags: CommandFlags.FireAndForget)|}.Wait()|};
            }
        }
        """,
        Diagnostic("SER306").WithLocation(0).WithLocation(1).WithArguments("StringSetAsync"));

    /// <summary>Blocking on a non-redis task is somebody else's business.</summary>
    [Fact]
    public Task BlockingOnUnrelatedTask_IsClean() => VerifyAsync(
        """
        using System.Threading.Tasks;
        class C
        {
            public void M()
            {
                Task.Delay(1).Wait();
            }
        }
        """);
}
