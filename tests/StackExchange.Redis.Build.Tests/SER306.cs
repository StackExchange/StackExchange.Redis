using System.Threading.Tasks;
using Xunit;

namespace StackExchange.Redis.Build.Tests;

/// <summary>
/// The fire-and-forget case: waiting completes, but reads the default value rather than the server's answer.
/// </summary>
/// <remarks>
/// A warning rather than <see cref="SER305"/>'s error because the code runs - it just cannot tell you anything.
/// <c>QueuedResultTests</c> in the main suite pins the behaviour this rests on against a live server.
/// </remarks>
public class SER306 : Verifier<QueuedResultAnalyzer>
{
    [Fact]
    public Task AwaitedFireAndForget_IsFlagged() => VerifyAsync(
        """
        using StackExchange.Redis;
        using System.Threading.Tasks;
        class C
        {
            public async Task M(IDatabase db, RedisKey key)
            {
                var tran = db.CreateTransaction();
                var value = {|#0:await {|#1:tran.StringGetAsync(key, CommandFlags.FireAndForget)|}|};
                await tran.ExecuteAsync();
            }
        }
        """,
        Diagnostic("SER306").WithLocation(0).WithLocation(1).WithArguments("StringGetAsync"));

    /// <summary>
    /// Combined flags are still a compile-time constant, so the bit is visible through the <c>|</c>.
    /// </summary>
    [Fact]
    public Task AwaitedCombinedFireAndForget_IsFlagged() => VerifyAsync(
        """
        using StackExchange.Redis;
        using System.Threading.Tasks;
        class C
        {
            public async Task M(IDatabase db, RedisKey key)
            {
                var tran = db.CreateTransaction();
                var value = {|#0:await {|#1:tran.StringGetAsync(key, CommandFlags.FireAndForget | CommandFlags.DemandMaster)|}|};
                await tran.ExecuteAsync();
            }
        }
        """,
        Diagnostic("SER306").WithLocation(0).WithLocation(1).WithArguments("StringGetAsync"));

    [Fact]
    public Task BlockingOnFireAndForgetResult_IsFlagged() => VerifyAsync(
        """
        using StackExchange.Redis;
        using System.Threading.Tasks;
        class C
        {
            public void M(IDatabase db, RedisKey key)
            {
                var tran = db.CreateTransaction();
                var value = {|#0:{|#1:tran.StringGetAsync(key, CommandFlags.FireAndForget)|}.Result|};
                tran.Execute();
            }
        }
        """,
        Diagnostic("SER306").WithLocation(0).WithLocation(1).WithArguments("StringGetAsync"));

    [Fact]
    public Task AwaitedFireAndForgetOnBatch_IsFlagged() => VerifyAsync(
        """
        using StackExchange.Redis;
        using System.Threading.Tasks;
        class C
        {
            public async Task M(IDatabase db, RedisKey key)
            {
                var batch = db.CreateBatch();
                {|#0:await {|#1:batch.StringSetAsync(key, "value", flags: CommandFlags.FireAndForget)|}|};
                batch.Execute();
            }
        }
        """,
        Diagnostic("SER306").WithLocation(0).WithLocation(1).WithArguments("StringSetAsync"));

    /// <summary>
    /// Not a constant, but <c>|</c> only sets bits - so the flag is there whatever <c>flags</c> holds.
    /// </summary>
    [Fact]
    public Task AwaitedNonConstantOrFireAndForget_IsFlagged() => VerifyAsync(
        """
        using StackExchange.Redis;
        using System.Threading.Tasks;
        class C
        {
            public async Task M(IDatabase db, RedisKey key, CommandFlags flags)
            {
                var tran = db.CreateTransaction();
                var value = {|#0:await {|#1:tran.StringGetAsync(key, flags | CommandFlags.FireAndForget)|}|};
                await tran.ExecuteAsync();
            }
        }
        """,
        Diagnostic("SER306").WithLocation(0).WithLocation(1).WithArguments("StringGetAsync"));

    /// <summary>
    /// The other side of that coin, and the reason the test exists: <c>&amp;~</c> would prove the flag *absent*,
    /// which would promote a partly-understood expression to SER305 - an error. We decline to reason about it,
    /// so this stays silent rather than becoming a build break.
    /// </summary>
    [Fact]
    public Task AwaitedNonConstantWithoutFireAndForget_IsClean() => VerifyAsync(
        """
        using StackExchange.Redis;
        using System.Threading.Tasks;
        class C
        {
            public async Task M(IDatabase db, RedisKey key, CommandFlags flags)
            {
                var tran = db.CreateTransaction();
                var value = await tran.StringGetAsync(key, flags & ~CommandFlags.FireAndForget);
                await tran.ExecuteAsync();
            }
        }
        """);

    /// <summary>An <c>|</c> of something else says nothing either way, so nothing is said.</summary>
    [Fact]
    public Task AwaitedNonConstantOrUnrelatedFlag_IsClean() => VerifyAsync(
        """
        using StackExchange.Redis;
        using System.Threading.Tasks;
        class C
        {
            public async Task M(IDatabase db, RedisKey key, CommandFlags flags)
            {
                var tran = db.CreateTransaction();
                var value = await tran.StringGetAsync(key, flags | CommandFlags.DemandMaster);
                await tran.ExecuteAsync();
            }
        }
        """);

    [Fact]
    public Task DiscardedFireAndForget_IsClean() => VerifyAsync(
        """
        using StackExchange.Redis;
        using System.Threading.Tasks;
        class C
        {
            public async Task M(IDatabase db, RedisKey key)
            {
                var tran = db.CreateTransaction();
                _ = tran.StringSetAsync(key, "value", flags: CommandFlags.FireAndForget);
                await tran.ExecuteAsync();
            }
        }
        """);

    /// <summary>
    /// Away from a transaction, *awaiting* fire-and-forget is a no-op await rather than a mistake, and is far
    /// too common to flag - the library's own tests contain hundreds. Blocking on one is a different matter,
    /// and is covered in <see cref="SER307"/>.
    /// </summary>
    [Fact]
    public Task AwaitedFireAndForgetOnDatabase_IsClean() => VerifyAsync(
        """
        using StackExchange.Redis;
        using System.Threading.Tasks;
        class C
        {
            public async Task M(IDatabase db, RedisKey key)
            {
                await db.StringSetAsync(key, "value", flags: CommandFlags.FireAndForget);
            }
        }
        """);

    /// <summary>...but an ordinary await, with no fire-and-forget, is exactly right and says nothing.</summary>
    [Fact]
    public Task AwaitedOnDatabase_IsClean() => VerifyAsync(
        """
        using StackExchange.Redis;
        using System.Threading.Tasks;
        class C
        {
            public async Task M(IDatabase db, RedisKey key)
            {
                await db.StringSetAsync(key, "value");
            }
        }
        """);
}
