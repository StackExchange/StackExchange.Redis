using System;
using System.Threading.Tasks;
using Xunit;

namespace StackExchange.Redis.Tests;

/// <summary>
/// <see cref="RespBatch"/> and <see cref="RespTransaction"/>: the context surface's own batch and
/// transaction, reached through <c>BeginBatch</c>/<c>BeginTransaction</c> and composed through the
/// ordinary groups because each is an <see cref="IRespKeyspaceTarget"/>.
/// </summary>
[RunPerProtocol]
public class RespBatchingTests(ITestOutputHelper output, SharedConnectionFixture fixture) : TestBase(output, fixture)
{
    [Fact]
    public async Task ABatchSendsNothingUntilItIsExecuted()
    {
        await using var conn = Create();
        var db = conn.GetDatabase();
        RedisKey key = Me();
        await db.KeyDeleteAsync(key);

        using var batch = db.BeginBatch();
        var set = batch.Strings.SetAsync(key, "v");
        var get = batch.Strings.GetAsync(key);
        Assert.Equal(2, batch.Count);

        Assert.False(await db.KeyExistsAsync(key)); // queued, not sent
        Assert.False(set.IsCompleted);

        await batch.ExecuteAsync();

        Assert.True(await set);
        Assert.Equal("v", (string?)await get);
    }

    /// <summary>The other half of the pair: the extension over the context itself.</summary>
    [Fact]
    public async Task ABatchCanBeStartedFromTheContext()
    {
        await using var conn = Create();
        var db = conn.GetDatabase();
        RedisKey key = Me();
        await db.KeyDeleteAsync(key);

        using var batch = db.Context.BeginBatch();
        var incr = batch.Strings.IncrementAsync(key);
        await batch.ExecuteAsync();

        Assert.Equal(1, await incr);
    }

    /// <summary>
    /// Why execution is explicit: a fault while composing must not send the half-built batch.
    /// </summary>
    [Fact]
    public async Task LeavingTheScopeDiscardsRatherThanSends()
    {
        await using var conn = Create();
        var db = conn.GetDatabase();
        RedisKey key = Me();
        await db.KeyDeleteAsync(key);

        ValueTask<bool> set = default;
        try
        {
            using var batch = db.BeginBatch();
            set = batch.Strings.SetAsync(key, "v");
            throw new InvalidOperationException("composing failed");
        }
        catch (InvalidOperationException ex) when (ex.Message == "composing failed")
        {
        }

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await set);
        Assert.False(await db.KeyExistsAsync(key));
    }

    [Fact]
    public async Task ABatchExecutesOnce()
    {
        await using var conn = Create();
        using var batch = conn.GetDatabase().BeginBatch();
        _ = batch.Strings.GetAsync(Me());

        await batch.ExecuteAsync();
        Assert.Throws<InvalidOperationException>(() => { _ = batch.ExecuteAsync(); });
    }

    [Fact]
    public async Task ABatchCannotBeExecutedAfterItIsDiscarded()
    {
        await using var conn = Create();
        var batch = conn.GetDatabase().BeginBatch();
        batch.Dispose();
        Assert.Throws<InvalidOperationException>(() => { _ = batch.ExecuteAsync(); });
    }

    [Fact]
    public void ADefaultBatchSaysSo()
    {
        RespBatch batch = default;
        Assert.Throws<InvalidOperationException>(() => batch.Context);
        Assert.Throws<InvalidOperationException>(() => { _ = batch.ExecuteAsync(); });
        Assert.Equal(0, batch.Count);
        batch.Dispose(); // nothing to discard, and not an error

        RespTransaction tran = default;
        Assert.Throws<InvalidOperationException>(() => tran.Context);
        Assert.Throws<InvalidOperationException>(() => tran.AddCondition(Condition.KeyExists("k")));
        tran.Dispose();
    }

    [Fact]
    public async Task NestingIsRefused()
    {
        await using var conn = Create();
        using var batch = conn.GetDatabase().BeginBatch();
        using var tran = conn.GetDatabase().BeginTransaction();

        Assert.Throws<NotSupportedException>(() => batch.BeginBatch());
        Assert.Throws<NotSupportedException>(() => batch.BeginTransaction());
        Assert.Throws<NotSupportedException>(() => tran.BeginBatch());
    }

    [Fact]
    public async Task ATransactionRunsAndSaysSo()
    {
        await using var conn = Create();
        var db = conn.GetDatabase();
        RedisKey key = Me();
        await db.KeyDeleteAsync(key);

        using var tran = db.BeginTransaction();
        var first = tran.Strings.IncrementAsync(key);
        var second = tran.Strings.IncrementAsync(key);

        Assert.True(await tran.ExecuteAsync());
        Assert.Equal(1, await first);
        Assert.Equal(2, await second);
        Assert.False(tran.WasWatchConflict);
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public async Task AConditionDecidesWhetherItRuns(bool keyExists, bool expectRan)
    {
        await using var conn = Create();
        var db = conn.GetDatabase();
        RedisKey key = Me(), guard = Me() + ":guard";
        await db.KeyDeleteAsync([key, guard]);
        if (keyExists) await db.StringSetAsync(guard, "x");

        using var tran = db.BeginTransaction();
        var condition = tran.AddCondition(Condition.KeyNotExists(guard));
        var incr = tran.Strings.IncrementAsync(key);

        Assert.Equal(expectRan, await tran.ExecuteAsync());
        Assert.Equal(expectRan, condition.WasSatisfied);
        if (expectRan)
        {
            Assert.Equal(1, await incr);
        }
        else
        {
            await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await incr);
            Assert.False(await db.KeyExistsAsync(key));
        }
    }

    [Fact]
    public async Task ATransactionExecutesOnce()
    {
        await using var conn = Create();
        using var tran = conn.GetDatabase().BeginTransaction();
        _ = tran.Strings.GetAsync(Me());

        Assert.True(await tran.ExecuteAsync());
        Assert.Throws<InvalidOperationException>(() => { _ = tran.ExecuteAsync(); });
    }
}
