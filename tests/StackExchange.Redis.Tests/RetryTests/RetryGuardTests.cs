using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using StackExchange.Redis.Availability;
using StackExchange.Redis.KeyspaceIsolation;
using Xunit;

namespace StackExchange.Redis.Tests.RetryTests;

// The rejection rules and hand-written members of the retry wrappers: what WithRetry refuses to wrap,
// what a retrying transaction refuses to do, and the members that are deliberately *not* retried
// (status probes, routing lookups, streaming scans) and so are implemented by hand.
public class RetryGuardTests(ITestOutputHelper log) : TestBase(log)
{
    private static RetryPolicy Policy() => new RetryPolicy.Builder { MaxAttempts = 3, RetryDelay = TimeSpan.Zero, JitterMax = TimeSpan.Zero };

    // Retrying inside a batch or a transaction makes no sense (the individual operations are not being
    // dispatched yet), and retry cannot be nested. All three are refused at wrap time.
    [Fact]
    public async Task WithRetry_RefusesBatchTransactionAndNesting()
    {
        using var server = new InProcessTestServer(Output);
        await using var conn = await server.ConnectAsync(log: Writer);
        var db = conn.GetDatabase();

        Assert.Throws<InvalidOperationException>(() => db.CreateBatch().WithRetry(Policy()));
        Assert.Throws<InvalidOperationException>(() => db.CreateTransaction().WithRetry(Policy()));
        Assert.Throws<InvalidOperationException>(() => db.WithRetry(Policy()).WithRetry(Policy()));
    }

    // A database's asyncState is stamped onto the task produced by a single dispatch. A retrying database
    // hands back its own task spanning however many attempts it takes, so it cannot carry that state -
    // and silently dropping it would be worse than refusing.
    [Fact]
    public async Task WithRetry_RefusesAsyncState()
    {
        using var server = new InProcessTestServer(Output);
        await using var conn = await server.ConnectAsync(log: Writer);

        object state = new();
        var ex = Assert.Throws<InvalidOperationException>(() => conn.GetDatabase(0, state).WithRetry(Policy()));
        Log(ex.Message);

        // ...including via a key-prefixed view of such a database, which inherits the inner state
        Assert.Throws<InvalidOperationException>(() => conn.GetDatabase(0, state).WithKeyPrefix("p:").WithRetry(Policy()));

        // and the same applies to a transaction created *from* a retrying database
        var retryDb = conn.GetDatabase().WithRetry(Policy());
        Assert.Throws<InvalidOperationException>(() => retryDb.CreateTransaction(state));

        // no state: fine
        Assert.NotNull(conn.GetDatabase().WithKeyPrefix("p:").WithRetry(Policy()));
        Assert.NotNull(retryDb.CreateTransaction());
    }

    // Key-prefixing composes with retry: the prefix is applied when the operation is captured, so it
    // survives being replayed. (Only one nesting order is expressible, since WithKeyPrefix needs an
    // IDatabase and WithRetry produces an async-only database.)
    [Fact]
    public async Task WithRetry_ComposesWithKeyPrefix()
    {
        using var server = new InProcessTestServer(Output);
        await using var conn = await server.ConnectAsync(log: Writer);

        var db = conn.GetDatabase().WithKeyPrefix("pfx:").WithRetry(Policy());
        Assert.True(await db.StringSetAsync("key", "value"));

        // visible under the prefixed name from an unprefixed database
        Assert.Equal("value", await conn.GetDatabase().StringGetAsync("pfx:key"));
    }

    // The transaction lifecycle guards: execute once, and do not accept work afterwards.
    [Fact]
    public async Task RetryTransaction_RefusesReuse()
    {
        using var server = new InProcessTestServer(Output);
        await using var conn = await server.ConnectAsync(log: Writer);

        var tran = conn.GetDatabase().WithRetry(Policy()).CreateTransaction();
        var set = tran.StringSetAsync("guard:key", "value");
        Assert.True(await tran.ExecuteAsync());
        Assert.True(await set);

        await Assert.ThrowsAsync<InvalidOperationException>(async () => await tran.ExecuteAsync());
        Assert.Throws<InvalidOperationException>(() => { _ = tran.StringSetAsync("guard:key", "again"); });
        Assert.Throws<InvalidOperationException>(() => tran.AddCondition(Condition.KeyExists("guard:key")));
    }

    // Nested transactions, and the cursored scans, cannot participate in a transaction at all.
    [Fact]
    public async Task RetryTransaction_RefusesNestingAndScans()
    {
        using var server = new InProcessTestServer(Output);
        await using var conn = await server.ConnectAsync(log: Writer);

        var tran = conn.GetDatabase().WithRetry(Policy()).CreateTransaction();

        Assert.Throws<NotSupportedException>(() => tran.CreateTransaction());
        Assert.Throws<NotSupportedException>(() => tran.HashScanAsync("k"));
        Assert.Throws<NotSupportedException>(() => tran.HashScanNoValuesAsync("k"));
        Assert.Throws<NotSupportedException>(() => tran.SetScanAsync("k"));
        Assert.Throws<NotSupportedException>(() => tran.SortedSetScanAsync("k"));
        Assert.Throws<NotSupportedException>(() => tran.VectorSetRangeEnumerateAsync("k", "a", "z"));
    }

    // Scans *can* be used on a retrying database: they are cursored, so they cannot be captured and
    // replayed as a unit, but rather than refusing them outright we forward straight through (giving up
    // retry, keeping the scan working). Needs a real server: the managed one has no *SCAN support.
    [Fact]
    public async Task WithRetry_ForwardsScans()
    {
        await using var conn = Create();

        var inner = conn.GetDatabase();
        RedisKey hash = Me() + ":hash", set = Me() + ":set", zset = Me() + ":zset";
        await inner.KeyDeleteAsync([hash, set, zset]);
        await inner.HashSetAsync(hash, [new HashEntry("a", "1"), new HashEntry("b", "2")]);
        await inner.SetAddAsync(set, ["x", "y"]);
        await inner.SortedSetAddAsync(zset, [new SortedSetEntry("p", 1), new SortedSetEntry("q", 2)]);

        var db = inner.WithRetry(Policy());

        Assert.Equal(2, await CountAsync(db.HashScanAsync(hash)));
        Assert.Equal(2, await CountAsync(db.HashScanNoValuesAsync(hash)));
        Assert.Equal(2, await CountAsync(db.SetScanAsync(set)));
        Assert.Equal(2, await CountAsync(db.SortedSetScanAsync(zset)));
    }

    // Commands with no return value (a bare Task, not Task<T>) go through their own funnel in the retry
    // database, and their own recorded-operation type inside a retrying transaction. Needs a real server:
    // the managed one implements no void-shaped command, so the fault/replay variant of this cannot
    // currently be driven in-process.
    [Fact]
    public async Task WithRetry_HandlesVoidOperations()
    {
        await using var conn = Create();

        var inner = conn.GetDatabase();
        RedisKey key = Me();
        await inner.KeyDeleteAsync(key);
        await inner.ListRightPushAsync(key, ["a", "b", "c", "d"]);

        var db = inner.WithRetry(Policy());
        await db.ListTrimAsync(key, 0, 1); // Task, not Task<T>
        Assert.Equal(2, await db.ListLengthAsync(key));

        // and the same shape recorded into (and replayed by) a retrying transaction
        var tran = db.CreateTransaction();
        var trim = tran.ListTrimAsync(key, 0, 0);
        var length = tran.ListLengthAsync(key);
        Assert.True(await tran.ExecuteAsync());

        await trim; // the void proxy resolved rather than hanging
        Assert.True(trim.IsCompletedSuccessfully);
        Assert.Equal(1, await length);
    }

    // The cheap status/routing members are pass-throughs rather than retried round-trips.
    [Fact]
    public async Task WithRetry_ForwardsProbes()
    {
        using var server = new InProcessTestServer(Output);
        await using var conn = await server.ConnectAsync(log: Writer);

        var inner = conn.GetDatabase();
        var db = inner.WithRetry(Policy());
        RedisKey key = "probe:key";

        Assert.Equal(inner.Database, db.Database);
        Assert.Same(conn, db.Multiplexer);
        Assert.True(db.IsConnected(key));
        Assert.NotNull(await db.IdentifyEndpointAsync(key));
        Log(db.ToString()!); // exercises the feature-flag description
    }

    private static async Task<int> CountAsync<T>(IAsyncEnumerable<T> source)
    {
        int count = 0;
        await foreach (var _ in source) count++;
        return count;
    }
}
