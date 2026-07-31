using System.Net;
using System.Threading.Tasks;
using RESPite.Messages;
using StackExchange.Redis.Server;
using Xunit;

namespace StackExchange.Redis.Tests;

/// <summary>
/// Covers the "WATCH drift" outcome of a conditional transaction: every condition was satisfied, so
/// <c>MULTI</c>/<c>EXEC</c> really was issued, but a watched key changed underneath us and the server
/// answered <c>EXEC</c> with a null array. This is distinct from an *elective* abort (a condition that
/// failed, where no <c>EXEC</c> is sent at all) and, unlike that case, it can only be produced by a
/// concurrent write - so it needs the in-process server to drive it deterministically.
/// </summary>
[RunPerProtocol]
public class TransactionWatchDriftTests(ITestOutputHelper log) : TestBase(log)
{
    // A null array is not an empty array: EXEC answering *-1 (RESP2) / _ (RESP3) means "watch failed",
    // whereas *0 means "a transaction of zero commands committed". The managed server used to collapse
    // the former into the latter, which made the whole drift path untestable (and, client-side, it
    // surfaced as a protocol failure instead).
    [Fact]
    public void NullArrayIsDistinctFromEmptyArray()
    {
        var nullArray = TypedRedisValue.NullArray(RespPrefix.Array);
        Assert.True(nullArray.IsNullArray);
        Assert.True(nullArray.IsNullValueOrArray);
        Assert.True(nullArray.Span.IsEmpty);

        var emptyArray = TypedRedisValue.EmptyArray(RespPrefix.Array);
        Assert.False(emptyArray.IsNullArray);
        Assert.False(emptyArray.IsNullValueOrArray);
        Assert.True(emptyArray.Span.IsEmpty);
    }

    // The headline case: the condition holds, EXEC is issued, and the server rejects it because the
    // watched key moved. Execute reports false (nothing was applied) and - the part that regressed -
    // every queued operation's task must reach a terminal state (cancelled), not hang forever.
    [Fact]
    public async Task WatchDrift_AbortsAndCancelsQueuedOperations()
    {
        using var server = new WatchDriftServer(Output);
        await using var conn = await server.ConnectAsync(log: Writer);

        var db = conn.GetDatabase();
        RedisKey key = "drift:cancel";
        Assert.True(await db.StringSetAsync(key, "seed"));

        server.DriftKey = key;
        server.DriftOps = 1; // the next EXEC observes a concurrent write to the watched key

        var tran = db.CreateTransaction();
        var cond = tran.AddCondition(Condition.StringEqual(key, "seed"));
        var setTask = tran.StringSetAsync(key, "committed");
        var incrTask = tran.StringIncrementAsync("drift:counter");

        Assert.False(await tran.ExecuteAsync()); // EXEC returned a null array
        Assert.True(cond.WasSatisfied); // the *condition* held; the server-side WATCH is what killed it
        Assert.True(tran.WasWatchConflict); // ...and this is how a caller tells the two apart
        Assert.Equal(1, server.ExecOpsReceived);

        // both per-operation tasks must complete (as cancelled); before the fix they sat forever in
        // WaitingForActivation, so assert with a timeout rather than awaiting them directly
        await AssertCancelledAsync(setTask);
        await AssertCancelledAsync(incrTask);

        Assert.Equal("seed", await db.StringGetAsync(key)); // nothing was applied
        Assert.False(await db.KeyExistsAsync("drift:counter"));
    }

    // Same shape, but confirming the *elective* abort still behaves: the condition fails, no EXEC is
    // ever issued, and the queued operations are cancelled. This is the path that already worked; it is
    // here so the two outcomes are pinned side by side (they are indistinguishable from Execute's bool
    // alone - WasWatchConflict, or inspecting the ConditionResults, is what separates them).
    [Fact]
    public async Task FailedCondition_AbortsElectively_WithoutExec()
    {
        using var server = new WatchDriftServer(Output);
        await using var conn = await server.ConnectAsync(log: Writer);

        var db = conn.GetDatabase();
        RedisKey key = "drift:elective";
        Assert.True(await db.StringSetAsync(key, "seed"));

        var tran = db.CreateTransaction();
        Assert.False(tran.WasWatchConflict); // false before execution, too

        var cond = tran.AddCondition(Condition.StringEqual(key, "different"));
        var setTask = tran.StringSetAsync(key, "committed");

        Assert.False(await tran.ExecuteAsync());
        Assert.False(cond.WasSatisfied); // this is what distinguishes an elective abort from drift
        Assert.False(tran.WasWatchConflict); // we chose not to issue an EXEC; nobody raced us
        Assert.Equal(0, server.ExecOpsReceived); // never even asked

        await AssertCancelledAsync(setTask);
        Assert.Equal("seed", await db.StringGetAsync(key));
    }

    // A transaction with conditions but no operations: drift still aborts it, and there are no
    // per-operation tasks to cancel. Guards the zero-length inner-operations edge in the processor.
    [Fact]
    public async Task WatchDrift_ConditionOnlyTransaction_Aborts()
    {
        using var server = new WatchDriftServer(Output);
        await using var conn = await server.ConnectAsync(log: Writer);

        var db = conn.GetDatabase();
        RedisKey key = "drift:condonly";
        Assert.True(await db.StringSetAsync(key, "seed"));

        server.DriftKey = key;
        server.DriftOps = 1;

        var tran = db.CreateTransaction();
        var cond = tran.AddCondition(Condition.StringEqual(key, "seed"));

        Assert.False(await tran.ExecuteAsync());
        Assert.True(cond.WasSatisfied);
        Assert.True(tran.WasWatchConflict);
        Assert.Equal(1, server.ExecOpsReceived);
    }

    // A transaction that commits cleanly must not report a conflict.
    [Fact]
    public async Task SatisfiedCondition_Commits_WithoutConflict()
    {
        using var server = new WatchDriftServer(Output);
        await using var conn = await server.ConnectAsync(log: Writer);

        var db = conn.GetDatabase();
        RedisKey key = "drift:clean";
        Assert.True(await db.StringSetAsync(key, "seed"));

        var tran = db.CreateTransaction();
        var cond = tran.AddCondition(Condition.StringEqual(key, "seed"));
        var setTask = tran.StringSetAsync(key, "committed");

        Assert.True(await tran.ExecuteAsync());
        Assert.True(cond.WasSatisfied);
        Assert.False(tran.WasWatchConflict);
        Assert.True(await setTask);
    }

    private async Task AssertCancelledAsync(Task task)
    {
        var completed = await Task.WhenAny(task, Task.Delay(5000));
        if (completed != task)
        {
            Log($"task did not complete; status: {task.Status}");
            Assert.Fail($"queued operation never completed (status: {task.Status})");
        }

        await Assert.ThrowsAnyAsync<System.OperationCanceledException>(async () => await task);
    }

    // An in-process server that, for the next DriftOps EXEC operations, simulates a concurrent write to
    // DriftKey immediately before the EXEC is processed. Touch is exactly what a real write from another
    // connection would do, so the transaction is doomed by the server's own WATCH bookkeeping and EXEC
    // replies with a null array - no special-casing of the reply itself.
    //
    // Driving this from a genuinely separate connection is not practical: SE.Redis does not issue the WATCH
    // when AddCondition is called, it issues WATCH, the condition reads, MULTI, the queued commands and
    // EXEC as one dispatch. So the window an interloper has to squeeze into is the gap between the
    // condition reads and the EXEC landing, within a single flush - which is the point of the feature, but
    // makes it useless as a test lever. Injecting the Touch server-side reproduces the same state exactly.
    private sealed class WatchDriftServer(ITestOutputHelper? log, EndPoint? endpoint = null) : InProcessTestServer(log, endpoint)
    {
        public int ExecOpsReceived { get; private set; }

        public int DriftOps { get; set; }

        public RedisKey DriftKey { get; set; }

        protected override TypedRedisValue Exec(RedisClient client, in RedisRequest request)
        {
            ExecOpsReceived++;

            if (DriftOps > 0)
            {
                DriftOps--;
                client.Touch(client.Database, DriftKey);
            }

            return base.Exec(client, in request);
        }
    }
}
