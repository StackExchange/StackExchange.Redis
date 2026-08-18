using System;
using System.Threading.Tasks;
using StackExchange.Redis.Availability;
using Xunit;

namespace StackExchange.Redis.Tests;

/// <summary>
/// What the task handed back by a command queued on an <see cref="ITransaction"/> or <see cref="IBatch"/>
/// actually does before the batch is sent.
/// </summary>
/// <remarks>
/// <para>
/// These are the facts the SER305/SER306 analyzer rules are built on, which is why they are pinned here rather
/// than left as received wisdom: awaiting a queued command before <c>Execute</c> hangs forever, *except* under
/// <see cref="CommandFlags.FireAndForget"/>, where the task is already completed and carries nothing.
/// </para>
/// <para>
/// The waits below are deliberately asymmetric. Asserting a task *is* complete is exact and instant; asserting
/// one never completes cannot be, so it is a bounded wait - long enough that a passing run means something,
/// short enough not to dominate the suite. A false pass there would mean the rule is unnecessary, not that it
/// is wrong.
/// </para>
/// </remarks>
public class QueuedResultTests(ITestOutputHelper output, SharedConnectionFixture fixture) : TestBase(output, fixture)
{
    /// <summary>How long to wait before accepting that a task is not going to complete.</summary>
    private static readonly TimeSpan NeverCompletes = TimeSpan.FromSeconds(2);

    [Fact]
    public async Task TransactionQueuedCommandDoesNotCompleteBeforeExecute()
    {
        await using var conn = Create();

        RedisKey key = Me();
        var db = conn.GetDatabase();
        db.StringSet(key, "abc", flags: CommandFlags.FireAndForget);

        var tran = db.CreateTransaction();
        var pending = tran.StringGetAsync(key);

        // the whole point of the rule: this is what awaiting here would do
        Assert.False(pending.IsCompleted);
        Assert.NotSame(pending, await Task.WhenAny(pending, Task.Delay(NeverCompletes, TestContext.Current.CancellationToken)));
        Assert.False(pending.IsCompleted);

        Assert.True(await tran.ExecuteAsync());
        Assert.Equal("abc", await pending);
    }

    [Fact]
    public async Task TransactionQueuedFireAndForgetCompletesImmediatelyWithDefault()
    {
        await using var conn = Create();

        RedisKey key = Me();
        var db = conn.GetDatabase();
        db.StringSet(key, "abc", flags: CommandFlags.FireAndForget);

        var tran = db.CreateTransaction();
        var pending = tran.StringGetAsync(key, CommandFlags.FireAndForget);

        // completed before anything has been sent, so awaiting it is legal - but the value is not the
        // server's answer and never will be, whenever it is awaited
        Assert.True(pending.IsCompleted);
        Assert.True((await pending).IsNull);

        Assert.True(await tran.ExecuteAsync());
        Assert.True((await pending).IsNull);
    }

    [Fact]
    public async Task TransactionQueuedFireAndForgetStillExecutes()
    {
        await using var conn = Create();

        RedisKey key = Me();
        var db = conn.GetDatabase();
        db.KeyDelete(key, CommandFlags.FireAndForget);

        var tran = db.CreateTransaction();
        var pending = tran.StringSetAsync(key, "written", flags: CommandFlags.FireAndForget);
        Assert.True(pending.IsCompleted);

        // the discarded result is the only thing fire-and-forget gives up; the command itself is queued and
        // sent like any other, so "it completed early" must not be read as "it did not happen"
        Assert.True(await tran.ExecuteAsync());
        Assert.Equal("written", await db.StringGetAsync(key));
    }

    /// <summary>
    /// A retrying transaction pre-completes a fire-and-forget operation, exactly as the plain one does.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is a regression test for a real divergence. <c>RetryTransaction</c> records every queued call and
    /// hands back a durable proxy task; it used to do so without regard to the flags - which it could not see,
    /// as they are captured inside the generated per-command state - so a fire-and-forget result stayed
    /// incomplete until <c>ExecuteAsync</c> forwarded it.
    /// </para>
    /// <para>
    /// The effect was that identical caller code returned instantly on a plain transaction and blocked for
    /// good on a retrying one, which is precisely the drop-in substitution <c>WithRetry</c> is meant to
    /// support. The generated state now exposes its <c>CommandFlags</c> (<c>IFlaggedRedisArgs</c>) so the two
    /// agree.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task RetryTransactionQueuedFireAndForgetCompletesImmediatelyWithDefault()
    {
        await using var conn = Create();

        RedisKey key = Me();
        var db = conn.GetDatabase();
        db.StringSet(key, "abc", flags: CommandFlags.FireAndForget);

        var tran = db.WithRetry().CreateTransaction();
        var pending = tran.StringGetAsync(key, CommandFlags.FireAndForget);

        Assert.True(pending.IsCompleted);
        Assert.True((await pending).IsNull);

        Assert.True(await tran.ExecuteAsync());
        Assert.True((await pending).IsNull);
    }

    /// <summary>
    /// A fire-and-forget operation builds no <see cref="System.Threading.Tasks.TaskCompletionSource{T}"/> at
    /// all: every one hands back the same shared completed task.
    /// </summary>
    /// <remarks>
    /// Asserted as identity rather than as a byte count, which would be brittle. There is nothing for a proxy
    /// to carry when the reply has been declined, so building one costs two objects per queued operation - the
    /// source and the <c>Task</c> it creates in its own constructor - for a value that is fixed in advance.
    /// The instance handed back is the one a plain <see cref="ITransaction"/> returns for the same case.
    /// </remarks>
    [Fact]
    public async Task RetryTransactionFireAndForgetSharesOneCompletedTask()
    {
        await using var conn = Create();

        RedisKey key = Me();
        var db = conn.GetDatabase();
        var tran = db.WithRetry().CreateTransaction();

        var first = tran.StringGetAsync(key, CommandFlags.FireAndForget);
        var second = tran.StringGetAsync(key, CommandFlags.FireAndForget);
        Assert.Same(first, second);

        // the void-returning shape has its own proxy type, and the same applies to it
        var firstVoid = tran.HashSetAsync(key, [new HashEntry("f", "v")], CommandFlags.FireAndForget);
        Assert.Same(Task.CompletedTask, firstVoid);

        // ...while an operation that did *not* decline its reply still gets a durable proxy of its own
        var pending = tran.StringGetAsync(key);
        Assert.NotSame(first, pending);
        Assert.False(pending.IsCompleted);

        Assert.True(await tran.ExecuteAsync());
        Assert.True(pending.IsCompleted);
    }

    /// <summary>...and the command itself is still queued, replayed and sent.</summary>
    [Fact]
    public async Task RetryTransactionQueuedFireAndForgetStillExecutes()
    {
        await using var conn = Create();

        RedisKey key = Me();
        var db = conn.GetDatabase();
        db.KeyDelete(key, CommandFlags.FireAndForget);

        var tran = db.WithRetry().CreateTransaction();
        var pending = tran.StringSetAsync(key, "written", flags: CommandFlags.FireAndForget);
        Assert.True(pending.IsCompleted);

        Assert.True(await tran.ExecuteAsync());
        Assert.Equal("written", await db.StringGetAsync(key));
    }

    /// <summary>The non-fire-and-forget case, which behaves the same on both.</summary>
    [Fact]
    public async Task RetryTransactionQueuedCommandDoesNotCompleteBeforeExecute()
    {
        await using var conn = Create();

        RedisKey key = Me();
        var db = conn.GetDatabase();
        db.StringSet(key, "abc", flags: CommandFlags.FireAndForget);

        var tran = db.WithRetry().CreateTransaction();
        var pending = tran.StringGetAsync(key);

        Assert.False(pending.IsCompleted);
        Assert.NotSame(pending, await Task.WhenAny(pending, Task.Delay(NeverCompletes, TestContext.Current.CancellationToken)));

        Assert.True(await tran.ExecuteAsync());
        Assert.Equal("abc", await pending);
    }

    [Fact]
    public async Task BatchQueuedCommandDoesNotCompleteBeforeExecute()
    {
        await using var conn = Create();

        RedisKey key = Me();
        var db = conn.GetDatabase();
        db.StringSet(key, "abc", flags: CommandFlags.FireAndForget);

        var batch = db.CreateBatch();
        var pending = batch.StringGetAsync(key);

        Assert.False(pending.IsCompleted);
        Assert.NotSame(pending, await Task.WhenAny(pending, Task.Delay(NeverCompletes, TestContext.Current.CancellationToken)));
        Assert.False(pending.IsCompleted);

        batch.Execute();
        Assert.Equal("abc", await pending);
    }

    [Fact]
    public async Task BatchQueuedFireAndForgetCompletesImmediatelyWithDefault()
    {
        await using var conn = Create();

        RedisKey key = Me();
        var db = conn.GetDatabase();
        db.StringSet(key, "abc", flags: CommandFlags.FireAndForget);

        var batch = db.CreateBatch();
        var pending = batch.StringGetAsync(key, CommandFlags.FireAndForget);

        Assert.True(pending.IsCompleted);
        Assert.True((await pending).IsNull);

        batch.Execute();
        Assert.True((await pending).IsNull);
    }
}
