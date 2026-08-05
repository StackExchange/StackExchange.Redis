using System;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using StackExchange.Redis.Availability;
using StackExchange.Redis.Server;
using Xunit;

// The whole point of this file is what a WATCH-based transaction does when EXEC is retried, so the analyzer's
// advice to collapse these into a single atomic command is exactly what must not happen here: there would be no
// WATCH left to retry, and nothing to test. Suppressed file-wide rather than per-site for that reason.
#pragma warning disable SER301 // Transaction can be replaced by a single atomic operation

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
        RetryPolicy policy = new RetryPolicy.Builder
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

    // Retries are bounded: once MaxAttempts is used up the original server fault is surfaced to the
    // caller unchanged (not wrapped, not swallowed), and the server saw exactly MaxAttempts requests.
    [Fact]
    public async Task WithRetry_WhenAttemptsExhausted_ThrowsOriginalFault()
    {
        using var server = new LoadingServer(Output);
        await using var conn = await server.ConnectAsync(log: Writer);

        var db = conn.GetDatabase();
        RedisKey key = "retry:exhaust";
        Assert.True(await db.StringSetAsync(key, "hello"));

        server.LoadingOps = 100; // more than we will ever attempt

        RetryPolicy policy = new RetryPolicy.Builder { MaxAttempts = 3, RetryDelay = TimeSpan.Zero, JitterMax = TimeSpan.Zero };
        var ex = await Assert.ThrowsAsync<RedisServerException>(async () => await db.WithRetry(policy).StringGetAsync(key));

        Assert.Equal(RedisErrorKind.Loading, ex.Kind);
        Assert.Equal(3, server.GetOpsReceived); // tried exactly MaxAttempts times
    }

    // A fault that will not fix itself is not worth repeating: WRONGTYPE is an application error, so it
    // surfaces on the first attempt regardless of how many attempts the policy allows.
    [Fact]
    public async Task WithRetry_NonTransientFault_IsNotRetried()
    {
        using var server = new LoadingServer(Output);
        await using var conn = await server.ConnectAsync(log: Writer);

        var db = conn.GetDatabase();
        RedisKey key = "retry:wrongtype";
        Assert.True(await db.StringSetAsync(key, "hello"));

        server.LoadingOps = 100;
        server.ErrorText = "WRONGTYPE Operation against a key holding the wrong kind of value";

        RetryPolicy policy = new RetryPolicy.Builder { MaxAttempts = 3, RetryDelay = TimeSpan.Zero, JitterMax = TimeSpan.Zero };
        var ex = await Assert.ThrowsAsync<RedisServerException>(async () => await db.WithRetry(policy).StringGetAsync(key));

        Assert.Equal(RedisErrorKind.WrongType, ex.Kind);
        Assert.Equal(1, server.GetOpsReceived); // gave up immediately
    }

    // An ad-hoc command whose *name* is recognised gets that command's category for free, so a plain
    // Execute("get", ...) is retried like any other read - the caller does not have to say anything.
    [Fact]
    public async Task WithRetry_AdHocCommand_InheritsKnownCommandCategory()
    {
        using var server = new LoadingServer(Output);
        await using var conn = await server.ConnectAsync(log: Writer);

        var db = conn.GetDatabase();
        RedisKey key = "retry:adhoc:known";
        Assert.True(await db.StringSetAsync(key, "hello"));

        server.LoadingOps = 2;

        RetryPolicy policy = new RetryPolicy.Builder { MaxAttempts = 3, RetryDelay = TimeSpan.Zero, JitterMax = TimeSpan.Zero };
        var result = await db.WithRetry(policy).ExecuteAsync("get", [key]);

        Assert.Equal("hello", result.AsString());
        Assert.Equal(3, server.GetOpsReceived); // recognised as GET, i.e. read-only, so retried
    }

    // A command the library does *not* recognise could do anything, so it is treated pessimistically and
    // never retried; a caller who knows better can say so via the flags.
    [Theory]
    [InlineData(CommandFlags.None, 1)] // unrecognised: assume the worst, do not retry
    [InlineData(CommandFlags.CommandRetryReadOnly, 3)] // caller asserts it is a pure read
    public async Task WithRetry_UnrecognisedCommand_RespectsSuppliedCategory(CommandFlags flags, int expectedAttempts)
    {
        using var server = new LoadingServer(Output);
        await using var conn = await server.ConnectAsync(log: Writer);

        var db = conn.GetDatabase();
        server.LoadingOps = 2; // the third attempt would succeed, if we are allowed a third

        RetryPolicy policy = new RetryPolicy.Builder { MaxAttempts = 3, RetryDelay = TimeSpan.Zero, JitterMax = TimeSpan.Zero };
        var retryDb = db.WithRetry(policy);

        if (expectedAttempts == 1)
        {
            await Assert.ThrowsAsync<RedisServerException>(async () => await retryDb.ExecuteAsync("notarealcommand", [], flags));
        }
        else
        {
            var result = await retryDb.ExecuteAsync("notarealcommand", [], flags);
            Assert.Equal("made-up-ok", result.AsString());
        }

        Assert.Equal(expectedAttempts, server.UnknownOpsReceived);
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

        MultiGroupOptions options = new MultiGroupOptions.Builder
        {
            HealthCheckInterval = TimeSpan.FromMinutes(30), // huge: the breaker fast-path is what reroutes us
            HealthCheck = new HealthCheck.Builder
            {
                Probe = probe,
                ProbeCount = 1,
                ProbeTimeout = TimeSpan.FromSeconds(5),
            },
        };

        await using var conn = await ConnectionMultiplexer.ConnectGroupAsync(members, options);
        Assert.True(conn.IsConnected);
        Assert.Same(members[0], conn.ActiveMember); // A is active (highest weight)

        // failover enabled, plenty of attempts, no artificial delay between them
        RetryPolicy policy = new RetryPolicy.Builder
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

    // The unhappy path of the failover threshold: the attempt count says "wait for a failover now", but no
    // failover ever comes (the member stays nominally healthy - a couple of LOADING replies are not enough
    // to trip the default breaker). We wait out FailoverDelay and then carry on retrying the original
    // member rather than giving up, and the group never moves.
    [Fact]
    public async Task WithRetry_WhenFailoverNeverArrives_KeepsRetryingSameMember()
    {
        EndPoint alpha = new DnsEndPoint("alpha", 6379);
        EndPoint bravo = new DnsEndPoint("bravo", 6379);
        using var serverA = new LoadingServer(Output, endpoint: alpha);
        using var serverB = new InProcessTestServer(Output, endpoint: bravo);

        RedisKey key = "retry:nofailover";
        await using (var seedA = await serverA.ConnectAsync())
        {
            Assert.True(await seedA.GetDatabase().StringSetAsync(key, "from-A"));
        }

        ConnectionGroupMember[] members =
        [
            new(serverA.GetClientConfig(), "A") { Weight = 9 }, // highest weight -> active, and stays active
            new(serverB.GetClientConfig(), "B") { Weight = 1 },
        ];

        MultiGroupOptions options = new MultiGroupOptions.Builder
        {
            HealthCheckInterval = TimeSpan.FromMinutes(30),
            HealthCheck = new HealthCheck.Builder
            {
                Probe = new ControllableProbe(), // never marked down
                ProbeCount = 1,
                ProbeTimeout = TimeSpan.FromSeconds(5),
            },
        };

        await using var conn = await ConnectionMultiplexer.ConnectGroupAsync(members, options);
        Assert.Same(members[0], conn.ActiveMember);

        // failover is armed after the first attempt, but nothing will ever trigger it; keep the wait short
        RetryPolicy policy = new RetryPolicy.Builder
        {
            MaxAttempts = 5,
            MaxAttemptsBeforeFailover = 1,
            FailoverDelay = TimeSpan.FromMilliseconds(200),
            RetryDelay = TimeSpan.Zero,
            JitterMax = TimeSpan.Zero,
        };
        var db = conn.GetDatabase().WithRetry(policy);

        serverA.LoadingOps = 2; // two transient faults, then A answers normally

        Assert.Equal("from-A", await db.StringGetAsync(key));
        Assert.Equal(3, serverA.GetOpsReceived); // 2 x LOADING + 1 x success, all on A
        Assert.Same(members[0], conn.ActiveMember); // never moved to B
    }

    // End-to-end for a retryable transaction: a server that fails the first EXEC with a transient LOADING
    // error (discarding that attempt's queued commands), then commits the second. A transaction created via
    // the retrying database should ride this out: the per-operation tasks handed out at build time resolve
    // from the winning (second) attempt, the value is actually written, and the server saw two EXECs.
    [Fact]
    public async Task WithRetry_Transaction_RidesOutTransientExec()
    {
        using var server = new ExecFailServer(Output);
        await using var conn = await server.ConnectAsync(log: Writer);
        Assert.True(conn.IsConnected);

        var db = conn.GetDatabase();

        RedisKey key = "retry:tran";
        Assert.True(await db.StringSetAsync(key, "seed")); // seed a value we expect the transaction to overwrite

        server.FailExecOps = 1; // fail the first EXEC; the second should commit

        RetryPolicy policy = new RetryPolicy.Builder
        {
            MaxAttempts = 3,
            RetryDelay = TimeSpan.Zero,
            JitterMax = TimeSpan.Zero,
        };
        var retryDb = db.WithRetry(policy);

        var tran = retryDb.CreateTransaction();
        var setTask = tran.StringSetAsync(key, "committed");
        var getTask = tran.StringGetAsync(key);
        bool committed = await tran.ExecuteAsync();

        Assert.True(committed); // rode out the transient EXEC failure
        Assert.True(await setTask); // per-op proxy resolved from the winning attempt
        Assert.Equal("committed", await getTask); // read reflects the committed value
        Assert.Equal("committed", await db.StringGetAsync(key)); // and it really landed on the server
        Assert.Equal(0, server.FailExecOps); // the transient failure was consumed
        Assert.Equal(2, server.ExecOpsReceived); // 1 x LOADING + 1 x commit
    }

    // A transaction's effective retry category is the most side-effecting of its operations, so an INCR
    // makes the whole thing "accumulating" - beyond what the default policy (capped at write-last-wins)
    // would normally repeat. But a LOADING reply to EXEC *proves* the server discarded the transaction
    // wholesale, so replaying it cannot double-count: the category cap does not apply, and even the
    // default policy rides it out. Without that distinction, most interesting transactions (i.e. the ones
    // that mutate something) would never be retryable at all.
    [Fact]
    public async Task WithRetry_Transaction_RejectedExec_RetriesRegardlessOfCategory()
    {
        using var server = new ExecFailServer(Output);
        await using var conn = await server.ConnectAsync(log: Writer);
        Assert.True(conn.IsConnected);

        var db = conn.GetDatabase();
        RedisKey key = "retry:tran:incr";

        RetryPolicy policy = new RetryPolicy.Builder { MaxAttempts = 3, RetryDelay = TimeSpan.Zero, JitterMax = TimeSpan.Zero };
        Assert.Equal(CommandFlags.CommandRetryWriteLastWins, policy.MaxCommandRetryCategory); // i.e. the default

        server.FailExecOps = 1;
        var tran = db.WithRetry(policy).CreateTransaction();
        var incr = tran.StringIncrementAsync(key);

        Assert.True(await tran.ExecuteAsync());
        Assert.Equal(1, await incr); // the discarded attempt applied nothing -> incremented exactly once
        Assert.Equal(2, server.ExecOpsReceived); // 1 x LOADING + 1 x commit
    }

    // The other half of that story: when the outcome is genuinely *ambiguous*, the category still bites.
    // OOM is deliberately *not* treated as "known not applied": a Lua script can hit the memory limit
    // part-way through, having already written something, and it reports the inner error verbatim - so from
    // the client's side an OOM reply proves nothing about whether the command took effect. Here the server
    // models exactly that: it applies the INCR and *then* reports OOM. Under the default cap the fault
    // surfaces after one attempt; raising the cap opts into replaying it, and the value shows the
    // triple-count that the cap exists to prevent.
    [Theory]
    [InlineData(false, 1)] // default cap: not repeated
    [InlineData(true, 3)] // caller opted in: repeated, and every attempt landed
    public async Task WithRetry_AmbiguousFault_IsStillGatedByCategory(bool allowAccumulating, int expectedValue)
    {
        using var server = new AppliedThenFailedServer(Output);
        await using var conn = await server.ConnectAsync(log: Writer);

        var db = conn.GetDatabase();
        RedisKey key = "retry:ambiguous:incr";

        RetryPolicy policy = new RetryPolicy.Builder
        {
            MaxAttempts = 3,
            RetryDelay = TimeSpan.Zero,
            JitterMax = TimeSpan.Zero,
            MaxCommandRetryCategory = allowAccumulating
                ? CommandFlags.CommandRetryWriteAccumulating
                : RetryPolicy.Default.MaxCommandRetryCategory,
        };

        var ex = await Assert.ThrowsAsync<RedisServerException>(async () => await db.WithRetry(policy).StringIncrementAsync(key));
        Assert.Equal(RedisErrorKind.OutOfMemory, ex.Kind);

        Assert.Equal(expectedValue, server.IncrOpsReceived);
        server.FailIncr = false;
        Assert.Equal(expectedValue, (long)await db.StringGetAsync(key)); // and each one really did apply
    }

    // A WATCH constraint is replayed on every attempt. Here the condition is satisfied, and the first EXEC
    // hits a transient LOADING; the transaction should ride it out and the *durable* ConditionResult handed
    // back at build time should reflect the winning attempt (satisfied). Confirms the condition survives the
    // discard+replay and that its outcome is forwarded onto the caller-visible result.
    [Fact]
    public async Task WithRetry_Transaction_SatisfiedCondition_RidesOutTransientExec()
    {
        using var server = new ExecFailServer(Output);
        await using var conn = await server.ConnectAsync(log: Writer);
        Assert.True(conn.IsConnected);

        var db = conn.GetDatabase();
        RedisKey key = "retry:tran:cond";
        Assert.True(await db.StringSetAsync(key, "seed"));

        server.FailExecOps = 1; // fail the first EXEC; the condition must still hold on the replay

        RetryPolicy policy = new RetryPolicy.Builder { MaxAttempts = 3, RetryDelay = TimeSpan.Zero, JitterMax = TimeSpan.Zero };
        var tran = db.WithRetry(policy).CreateTransaction();
        var cond = tran.AddCondition(Condition.StringEqual(key, "seed")); // satisfied on both attempts
        var setTask = tran.StringSetAsync(key, "committed");
        bool committed = await tran.ExecuteAsync();

        Assert.True(committed); // rode out the transient EXEC failure
        Assert.True(cond.WasSatisfied); // durable condition result forwarded from the winning attempt
        Assert.True(await setTask); // per-op proxy resolved
        Assert.Equal("committed", await db.StringGetAsync(key)); // the write really landed
        Assert.Equal(2, server.ExecOpsReceived); // 1 x LOADING + 1 x commit -> the WATCH replayed
    }

    // A WATCH constraint that is *not* satisfied is a business outcome, not a transient fault: the transaction
    // aborts electively (no EXEC is ever issued), the per-operation proxies are cancelled, and - crucially -
    // it is NOT retried. Confirms the ForwardSuccess cancellation branch and that a failed WATCH doesn't loop.
    [Fact]
    public async Task WithRetry_Transaction_UnsatisfiedCondition_AbortsWithoutRetry()
    {
        using var server = new ExecFailServer(Output);
        await using var conn = await server.ConnectAsync(log: Writer);
        Assert.True(conn.IsConnected);

        var db = conn.GetDatabase();
        RedisKey key = "retry:tran:cond:fail";
        Assert.True(await db.StringSetAsync(key, "seed"));

        server.FailExecOps = 0; // no transient fault; the condition itself aborts the transaction

        RetryPolicy policy = new RetryPolicy.Builder { MaxAttempts = 3, RetryDelay = TimeSpan.Zero, JitterMax = TimeSpan.Zero };
        var tran = db.WithRetry(policy).CreateTransaction();
        var cond = tran.AddCondition(Condition.StringEqual(key, "different")); // NOT satisfied
        var setTask = tran.StringSetAsync(key, "committed");
        bool committed = await tran.ExecuteAsync();

        Assert.False(committed); // electively aborted via the failed WATCH
        Assert.False(cond.WasSatisfied);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await setTask); // proxy cancelled, not hung
        Assert.Equal("seed", await db.StringGetAsync(key)); // nothing was written
        Assert.Equal(0, server.ExecOpsReceived); // never EXEC'd, and (the point) never retried
    }

    // A committed transaction can still carry a per-command error (e.g. INCR against a non-numeric string
    // errors while EXEC itself succeeds). committed is true, the good op resolves, and only the offending op's
    // proxy faults - exercising the ForwardSuccess faulted branch, which the transient-EXEC tests never hit.
    [Fact]
    public async Task WithRetry_Transaction_PerOpError_FaultsOnlyThatProxy()
    {
        using var server = new ExecFailServer(Output);
        await using var conn = await server.ConnectAsync(log: Writer);
        Assert.True(conn.IsConnected);

        var db = conn.GetDatabase();
        RedisKey badKey = "retry:tran:notnum";
        RedisKey goodKey = "retry:tran:str";
        Assert.True(await db.StringSetAsync(badKey, "abc")); // non-numeric: INCR will error at EXEC time

        server.FailExecOps = 0; // EXEC commits; one queued op errors at execution time

        // allow accumulating so the INCR doesn't gate retries - though nothing here retries anyway (EXEC commits)
        RetryPolicy policy = new RetryPolicy.Builder
        {
            MaxAttempts = 3,
            RetryDelay = TimeSpan.Zero,
            JitterMax = TimeSpan.Zero,
            MaxCommandRetryCategory = CommandFlags.CommandRetryWriteAccumulating,
        };
        var tran = db.WithRetry(policy).CreateTransaction();
        var badTask = tran.StringIncrementAsync(badKey); // errors at EXEC: INCR on a non-numeric value
        var goodTask = tran.StringSetAsync(goodKey, "ok");
        bool committed = await tran.ExecuteAsync();

        Assert.True(committed); // EXEC still committed
        Assert.True(await goodTask); // the good op resolved
        await Assert.ThrowsAsync<RedisServerException>(async () => await badTask); // only the bad op faulted
        Assert.Equal("ok", await db.StringGetAsync(goodKey)); // the good write landed
        Assert.Equal(1, server.ExecOpsReceived); // committed first time; no retry
    }

    // Multi-group + a retryable transaction: A fails every EXEC and is knocked out, so the group reroutes to B.
    // Because RetryTransaction builds a *fresh* inner transaction against the currently-active member on each
    // attempt, the replay lands on B and commits there - the transaction analogue of the single-command
    // WithRetry_FailsOverBetweenGroupsOnLoading test.
    [Fact]
    public async Task WithRetry_Transaction_FailsOverBetweenGroups()
    {
        EndPoint alpha = new DnsEndPoint("alpha", 6379);
        EndPoint bravo = new DnsEndPoint("bravo", 6379);
        using var serverA = new ExecFailServer(Output, endpoint: alpha); // EXEC always fails on A
        using var serverB = new InProcessTestServer(Output, endpoint: bravo);

        RedisKey key = "retry:tran:failover";

        var probe = new ControllableProbe();

        // A carries a hair-trigger breaker (trips on the first fault); B keeps the default
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

        MultiGroupOptions options = new MultiGroupOptions.Builder
        {
            HealthCheckInterval = TimeSpan.FromMinutes(30), // huge: the breaker fast-path is what reroutes us
            HealthCheck = new HealthCheck.Builder
            {
                Probe = probe,
                ProbeCount = 1,
                ProbeTimeout = TimeSpan.FromSeconds(5),
            },
        };

        await using var conn = await ConnectionMultiplexer.ConnectGroupAsync(members, options);
        Assert.True(conn.IsConnected);
        Assert.Same(members[0], conn.ActiveMember); // A is active (highest weight)

        RetryPolicy policy = new RetryPolicy.Builder
        {
            MaxAttempts = 20,
            MaxAttemptsBeforeFailover = 1,
            RetryDelay = TimeSpan.Zero,
            JitterMax = TimeSpan.Zero,
        };
        var db = conn.GetDatabase().WithRetry(policy);

        // A will fail every EXEC; knock it out so the group reroutes to B
        serverA.FailExecOps = int.MaxValue;
        probe.MarkDown(alpha);

        var tran = db.CreateTransaction();
        var setTask = tran.StringSetAsync(key, "committed-on-B");
        bool committed = await tran.ExecuteAsync();

        Assert.True(committed); // rode the failover across to B and committed there
        Assert.True(await setTask);
        Assert.Same(members[1], conn.ActiveMember); // we really did move to B

        // the write landed on B, not A
        await using var checkB = await serverB.ConnectAsync();
        Assert.Equal("committed-on-B", await checkB.GetDatabase().StringGetAsync(key));
    }

    // Replay has to be repeatable, not just possible once: two transient EXEC failures in a row, with a
    // mixed bag of operations, and every proxy still resolves from the third (winning) attempt.
    [Fact]
    public async Task WithRetry_Transaction_ReplaysRepeatedly()
    {
        using var server = new ExecFailServer(Output);
        await using var conn = await server.ConnectAsync(log: Writer);

        var db = conn.GetDatabase();
        RedisKey key = "retry:tran:repeat", counter = "retry:tran:repeat:count", list = "retry:tran:repeat:list";
        Assert.True(await db.StringSetAsync(key, "seed"));

        server.FailExecOps = 2; // fail twice; the third EXEC commits

        RetryPolicy policy = new RetryPolicy.Builder
        {
            MaxAttempts = 4,
            RetryDelay = TimeSpan.Zero,
            JitterMax = TimeSpan.Zero,
            MaxCommandRetryCategory = CommandFlags.CommandRetryWriteAccumulating, // INCR/LPUSH are accumulating
        };
        var tran = db.WithRetry(policy).CreateTransaction();
        var cond = tran.AddCondition(Condition.StringEqual(key, "seed"));
        var set = tran.StringSetAsync(key, "committed");
        var incr = tran.StringIncrementAsync(counter);
        var push = tran.ListLeftPushAsync(list, "item");
        var get = tran.StringGetAsync(key);

        Assert.True(await tran.ExecuteAsync());

        Assert.True(cond.WasSatisfied);
        Assert.True(await set);
        Assert.Equal(1, await incr); // the two discarded attempts applied nothing
        Assert.Equal(1, await push);
        Assert.Equal("committed", await get);
        Assert.Equal(3, server.ExecOpsReceived); // 2 x LOADING + 1 x commit
    }

    // When a transaction runs out of attempts, the failure must reach *every* per-operation proxy as well
    // as the ExecuteAsync caller; a proxy left unresolved would hang the caller forever.
    [Fact]
    public async Task WithRetry_Transaction_WhenAttemptsExhausted_FaultsEveryProxy()
    {
        using var server = new ExecFailServer(Output);
        await using var conn = await server.ConnectAsync(log: Writer);

        var db = conn.GetDatabase();
        RedisKey key = "retry:tran:exhaust";

        server.FailExecOps = 100; // never succeeds

        RetryPolicy policy = new RetryPolicy.Builder { MaxAttempts = 2, RetryDelay = TimeSpan.Zero, JitterMax = TimeSpan.Zero };
        var tran = db.WithRetry(policy).CreateTransaction();
        var set = tran.StringSetAsync(key, "never");
        var get = tran.StringGetAsync(key);

        var fault = await Assert.ThrowsAsync<RedisServerException>(async () => await tran.ExecuteAsync());
        Assert.Equal(RedisErrorKind.Loading, fault.Kind);

        // both proxies carry the same terminal fault rather than being left pending
        Assert.Same(fault, await Assert.ThrowsAsync<RedisServerException>(async () => await set));
        Assert.Same(fault, await Assert.ThrowsAsync<RedisServerException>(async () => await get));
        Assert.Equal(2, server.ExecOpsReceived); // exactly MaxAttempts
    }

    // WATCH drift under retry: the condition holds, so EXEC really is issued, but the server refuses it
    // because a watched key moved. Nothing was applied and nothing faulted - we simply lost a race - so
    // the transaction is re-attempted (re-reading the condition), and the second attempt commits. This is
    // contention, not a fault, so the fault budget is untouched and the side-effect category is irrelevant.
    [Fact]
    public async Task WithRetry_Transaction_WatchDrift_IsReattempted()
    {
        using var server = new ExecFailServer(Output);
        await using var conn = await server.ConnectAsync(log: Writer);
        Assert.True(conn.IsConnected);

        var db = conn.GetDatabase();
        RedisKey key = "retry:tran:drift";
        Assert.True(await db.StringSetAsync(key, "seed"));

        server.DriftKey = key;
        server.DriftOps = 1; // the next EXEC observes a concurrent write to the watched key

        // MaxAttempts = 1: no *fault* retries at all, proving the watch budget is separate
        RetryPolicy policy = new RetryPolicy.Builder { MaxAttempts = 1, RetryDelay = TimeSpan.Zero, JitterMax = TimeSpan.Zero };
        var tran = db.WithRetry(policy).CreateTransaction();
        var cond = tran.AddCondition(Condition.StringEqual(key, "seed"));
        var setTask = tran.StringSetAsync(key, "committed");

        var execute = tran.ExecuteAsync();
        if (await Task.WhenAny(execute, Task.Delay(5000)) != execute)
        {
            Assert.Fail("ExecuteAsync never completed");
        }

        Assert.True(await execute); // rode out the lost race
        Assert.True(cond.WasSatisfied);
        Assert.True(await setTask);
        Assert.False(tran.WasWatchConflict); // reports the *final* attempt: we got there in the end
        Assert.Equal(2, server.ExecOpsReceived); // 1 x watch conflict + 1 x commit
        Assert.Equal("committed", await db.StringGetAsync(key));
    }

    // Watch contention is bounded, and the bound is opt-out-able: with MaxAttemptsOnWatchConflict = 1 a
    // conflict aborts exactly as it did before. ExecuteAsync reports false with every condition satisfied
    // (which is what distinguishes drift from an elective abort), and the per-operation proxies are
    // cancelled rather than left dangling - the case that used to hang the caller outright.
    [Fact]
    public async Task WithRetry_Transaction_WatchDrift_CanBeDisabled()
    {
        using var server = new ExecFailServer(Output);
        await using var conn = await server.ConnectAsync(log: Writer);

        var db = conn.GetDatabase();
        RedisKey key = "retry:tran:drift:off";
        Assert.True(await db.StringSetAsync(key, "seed"));

        server.DriftKey = key;
        server.DriftOps = 1;

        RetryPolicy policy = new RetryPolicy.Builder
        {
            MaxAttempts = 3,
            MaxAttemptsOnWatchConflict = 1, // i.e. do not re-attempt on contention
            RetryDelay = TimeSpan.Zero,
            JitterMax = TimeSpan.Zero,
        };
        var tran = db.WithRetry(policy).CreateTransaction();
        var cond = tran.AddCondition(Condition.StringEqual(key, "seed"));
        var setTask = tran.StringSetAsync(key, "committed");

        var execute = tran.ExecuteAsync();
        if (await Task.WhenAny(execute, Task.Delay(5000)) != execute)
        {
            Assert.Fail("ExecuteAsync never completed");
        }

        Assert.False(await execute);
        Assert.True(cond.WasSatisfied); // the condition held; the server-side WATCH is what killed it
        Assert.True(tran.WasWatchConflict); // ...and the caller can see exactly that
        Assert.Equal(1, server.ExecOpsReceived);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await setTask);
        Assert.Equal("seed", await db.StringGetAsync(key)); // nothing was applied
    }

    // Contention that never clears must not loop forever: the server conflicts on every EXEC, so we give
    // up after MaxAttemptsOnWatchConflict attempts and report the ordinary "did not commit" outcome.
    [Fact]
    public async Task WithRetry_Transaction_PersistentWatchDrift_GivesUp()
    {
        using var server = new ExecFailServer(Output);
        await using var conn = await server.ConnectAsync(log: Writer);

        var db = conn.GetDatabase();
        RedisKey key = "retry:tran:drift:forever";
        Assert.True(await db.StringSetAsync(key, "seed"));

        server.DriftKey = key;
        server.DriftOps = int.MaxValue; // every EXEC loses the race

        RetryPolicy policy = new RetryPolicy.Builder
        {
            MaxAttempts = 3,
            MaxAttemptsOnWatchConflict = 4,
            RetryDelay = TimeSpan.Zero,
            JitterMax = TimeSpan.Zero,
        };
        var tran = db.WithRetry(policy).CreateTransaction();
        var cond = tran.AddCondition(Condition.StringEqual(key, "seed"));
        var setTask = tran.StringSetAsync(key, "committed");

        Assert.False(await tran.ExecuteAsync());
        Assert.True(cond.WasSatisfied);
        Assert.True(tran.WasWatchConflict); // still losing the race when we ran out of attempts
        Assert.Equal(4, server.ExecOpsReceived); // bounded by MaxAttemptsOnWatchConflict
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await setTask);
        Assert.Equal("seed", await db.StringGetAsync(key));
    }

    // A transaction with no conditions has no WATCH, so it can never lose a watch race; the watch budget
    // must not be spent on the ordinary "aborted" path. (Belt and braces: the aggregate outcome here is a
    // clean commit, so this mostly guards against the budget logic firing on a false positive.)
    [Fact]
    public async Task WithRetry_Transaction_WithoutConditions_IgnoresWatchBudget()
    {
        using var server = new ExecFailServer(Output);
        await using var conn = await server.ConnectAsync(log: Writer);

        var db = conn.GetDatabase();
        RedisKey key = "retry:tran:nocond";

        RetryPolicy policy = new RetryPolicy.Builder { MaxAttempts = 1, MaxAttemptsOnWatchConflict = 5, RetryDelay = TimeSpan.Zero, JitterMax = TimeSpan.Zero };
        var tran = db.WithRetry(policy).CreateTransaction();
        var set = tran.StringSetAsync(key, "committed");

        Assert.True(await tran.ExecuteAsync());
        Assert.True(await set);
        Assert.Equal(1, server.ExecOpsReceived);
    }

    // ---- the synchronous ITransaction surface --------------------------------------------------------
    // A transaction built from a retrying database is an ITransaction, not just an ITransactionAsync, so code
    // written against the long-standing interface - including its *synchronous* Execute - can move onto a
    // retrying database unchanged. The static type of CreateTransaction is still ITransactionAsync (that is
    // what IDatabaseAsync declares), so a down-level caller reaches it with a cast.
    [Fact]
    public async Task WithRetry_Transaction_IsAnITransaction()
    {
        using var server = new ExecFailServer(Output);
        await using var conn = await server.ConnectAsync(log: Writer);

        var db = conn.GetDatabase();
        RedisKey key = "retry:tran:sync";

        RetryPolicy policy = new RetryPolicy.Builder { MaxAttempts = 3, RetryDelay = TimeSpan.Zero, JitterMax = TimeSpan.Zero };
        ITransaction tran = Assert.IsAssignableFrom<ITransaction>(db.WithRetry(policy).CreateTransaction());

        var set = tran.StringSetAsync(key, "committed");
        var get = tran.StringGetAsync(key);

        Assert.True(tran.Execute()); // sync-over-async; note this binds to ITransaction's bool overload

        Assert.True(await set); // the durable proxies resolve exactly as on the async path
        Assert.Equal("committed", await get);
        Assert.Equal("committed", await db.StringGetAsync(key));
        Assert.Equal(1, server.ExecOpsReceived);
    }

    // The point of the exercise: retries (and therefore the delays between them) happen *inside* the blocking
    // Execute, so a down-level caller gets the retry behaviour without touching their code.
    [Fact]
    public async Task WithRetry_Transaction_SyncExecute_RidesOutTransientExec()
    {
        using var server = new ExecFailServer(Output);
        await using var conn = await server.ConnectAsync(log: Writer);

        var db = conn.GetDatabase();
        RedisKey key = "retry:tran:sync:transient";
        Assert.True(await db.StringSetAsync(key, "seed"));

        server.FailExecOps = 1; // fail the first EXEC; the second should commit

        // a real (if small) delay between attempts, so the blocking wait genuinely has to outlast it
        RetryPolicy policy = new RetryPolicy.Builder
        {
            MaxAttempts = 3,
            RetryDelay = TimeSpan.FromMilliseconds(50),
            JitterMax = TimeSpan.Zero,
        };
        ITransaction tran = (ITransaction)db.WithRetry(policy).CreateTransaction();
        var cond = tran.AddCondition(Condition.StringEqual(key, "seed"));
        var set = tran.StringSetAsync(key, "committed");

        Assert.True(tran.Execute());

        Assert.True(cond.WasSatisfied); // the WATCH constraint was replayed onto the winning attempt
        Assert.True(await set);
        Assert.Equal("committed", await db.StringGetAsync(key));
        Assert.Equal(2, server.ExecOpsReceived); // 1 x LOADING + 1 x commit, all within Execute()
    }

    // A transaction that does not commit reports false from the sync Execute, just as it does from
    // ExecuteAsync - and WasWatchConflict still distinguishes an elective abort from a lost race.
    [Fact]
    public async Task WithRetry_Transaction_SyncExecute_UnsatisfiedCondition_ReturnsFalse()
    {
        using var server = new ExecFailServer(Output);
        await using var conn = await server.ConnectAsync(log: Writer);

        var db = conn.GetDatabase();
        RedisKey key = "retry:tran:sync:cond";
        Assert.True(await db.StringSetAsync(key, "seed"));

        RetryPolicy policy = new RetryPolicy.Builder { MaxAttempts = 3, RetryDelay = TimeSpan.Zero, JitterMax = TimeSpan.Zero };
        ITransaction tran = (ITransaction)db.WithRetry(policy).CreateTransaction();
        var cond = tran.AddCondition(Condition.StringEqual(key, "different")); // NOT satisfied
        var set = tran.StringSetAsync(key, "committed");

        Assert.False(tran.Execute());

        Assert.False(cond.WasSatisfied);
        Assert.False(tran.WasWatchConflict); // an elective abort, not a lost race
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await set);
        Assert.Equal("seed", await db.StringGetAsync(key));
        Assert.Equal(0, server.ExecOpsReceived); // never EXEC'd
    }

    // When the attempts run out, the sync Execute surfaces the *original* server fault - not an
    // AggregateException wrapping it (which is what a naive Task.Wait would have produced).
    [Fact]
    public async Task WithRetry_Transaction_SyncExecute_WhenAttemptsExhausted_ThrowsOriginalFault()
    {
        using var server = new ExecFailServer(Output);
        await using var conn = await server.ConnectAsync(log: Writer);

        var db = conn.GetDatabase();
        RedisKey key = "retry:tran:sync:exhaust";

        server.FailExecOps = 100; // never succeeds

        RetryPolicy policy = new RetryPolicy.Builder { MaxAttempts = 2, RetryDelay = TimeSpan.Zero, JitterMax = TimeSpan.Zero };
        ITransaction tran = (ITransaction)db.WithRetry(policy).CreateTransaction();
        var set = tran.StringSetAsync(key, "never");

        var fault = Assert.Throws<RedisServerException>(() => tran.Execute());
        Assert.Equal(RedisErrorKind.Loading, fault.Kind);

        Assert.Same(fault, await Assert.ThrowsAsync<RedisServerException>(async () => await set)); // proxy faulted too
        Assert.Equal(2, server.ExecOpsReceived); // exactly MaxAttempts
    }

    // IBatch.Execute() is the "enqueue and don't ask" shape, which RedisTransaction maps onto fire-and-forget;
    // a retrying transaction does the same. Fire-and-forget requests no reply for the EXEC, so there is
    // nothing for the retry machinery to observe and it collapses to a single attempt - but the queued
    // operations were not themselves fire-and-forget, so their proxies still resolve when the replies land.
    [Fact]
    public async Task WithRetry_Transaction_BatchExecute_IsFireAndForget()
    {
        using var server = new ExecFailServer(Output);
        await using var conn = await server.ConnectAsync(log: Writer);

        var db = conn.GetDatabase();
        RedisKey key = "retry:tran:sync:ff";

        RetryPolicy policy = new RetryPolicy.Builder { MaxAttempts = 3, RetryDelay = TimeSpan.Zero, JitterMax = TimeSpan.Zero };
        var tran = (ITransaction)db.WithRetry(policy).CreateTransaction();
        var set = tran.StringSetAsync(key, "committed");

        ((IBatch)tran).Execute(); // the void overload: no outcome is reported

        Assert.True(await set); // ...but the per-operation proxy still resolves
        Assert.Equal("committed", await db.StringGetAsync(key));
        Assert.Equal(1, server.ExecOpsReceived);
    }

    // ...and it must not *wait* for the outcome either. A replied EXEC populates the queued operations'
    // results before its own task completes, so the retry machinery can normally read each attempt's outcome
    // inline; a fire-and-forget EXEC completes as soon as it has been written, with the replies still in
    // flight. Forwarding must therefore be deferred rather than blocking for a round-trip the caller
    // explicitly declined - exactly what a plain transaction does. The server here holds the EXEC reply back,
    // so "did we wait for it?" is directly observable.
    [Fact]
    public async Task WithRetry_Transaction_BatchExecute_DoesNotWaitForReplies()
    {
        using var server = new SlowExecServer(Output);
        await using var conn = await server.ConnectAsync(log: Writer);

        var db = conn.GetDatabase();
        RedisKey key = "retry:tran:sync:ff:nowait";

        RetryPolicy policy = new RetryPolicy.Builder { MaxAttempts = 3, RetryDelay = TimeSpan.Zero, JitterMax = TimeSpan.Zero };
        var tran = (ITransaction)db.WithRetry(policy).CreateTransaction();
        var set = tran.StringSetAsync(key, "committed");

        ((IBatch)tran).Execute();

        // the server is still sitting on the EXEC reply, so this proves we returned without waiting for it
        Assert.False(set.IsCompleted);

        // ...and the proxy is still resolved once the reply does land
        Assert.True(await set);
        Assert.Equal("committed", await db.StringGetAsync(key));
    }

    // An in-proc server that *applies* each INCR and then reports OOM: the write happened, but the client
    // has no way to know that. Counts the applications so a test can tell "the client gave up" from "the
    // client repeated a write it could not account for".
    private sealed class AppliedThenFailedServer(ITestOutputHelper? log) : InProcessTestServer(log)
    {
        private int _incrOpsReceived;

        public int IncrOpsReceived => Volatile.Read(ref _incrOpsReceived);

        public bool FailIncr { get; set; } = true;

        protected override TypedRedisValue Incr(RedisClient client, in RedisRequest request)
        {
            var applied = base.Incr(client, in request);
            if (!FailIncr) return applied;

            Interlocked.Increment(ref _incrOpsReceived);
            return TypedRedisValue.Error("OOM command not allowed when used memory > 'maxmemory'");
        }
    }

    // An in-proc server that fails the first FailExecOps EXEC operations with a transient LOADING error,
    // discarding that attempt's queued commands so nothing is applied, then commits normally. It can
    // also simulate WATCH drift (a concurrent write to DriftKey immediately before EXEC is processed),
    // which is a clean server-side rejection rather than a fault.
    private sealed class ExecFailServer(ITestOutputHelper? log, EndPoint? endpoint = null) : InProcessTestServer(log, endpoint)
    {
        public int ExecOpsReceived { get; private set; }

        public int FailExecOps { get; set; }

        public int DriftOps { get; set; }

        public RedisKey DriftKey { get; set; }

        protected override TypedRedisValue Exec(RedisClient client, in RedisRequest request)
        {
            ExecOpsReceived++;

            if (FailExecOps > 0)
            {
                FailExecOps--;
                client.Discard(); // drop this attempt's queued commands; nothing is applied
                return TypedRedisValue.Error("LOADING Redis is loading the dataset in memory");
            }

            if (DriftOps > 0)
            {
                DriftOps--;
                client.Touch(client.Database, DriftKey); // as if another connection wrote the watched key
            }

            return base.Exec(client, in request);
        }
    }

    // An in-proc server that holds each EXEC reply back for a while, so a test can tell whether the client
    // waited for it. (The server core is single-threaded, so sleeping here delays only this connection.)
    private sealed class SlowExecServer(ITestOutputHelper? log, EndPoint? endpoint = null) : InProcessTestServer(log, endpoint)
    {
        protected override TypedRedisValue Exec(RedisClient client, in RedisRequest request)
        {
            Thread.Sleep(500);
            return base.Exec(client, in request);
        }
    }

    // An in-proc server that fails the first LoadingOps GET operations with a transient LOADING error
    // (decrementing the counter each time), then serves normally. Every GET bumps GetOpsReceived so the
    // test can confirm how many attempts actually reached the server.
    private sealed class LoadingServer(ITestOutputHelper? log, EndPoint? endpoint = null) : InProcessTestServer(log, endpoint)
    {
        // the server core processes operations under a lock (single-threaded, like Redis), so plain fields
        // are fine here
        public int GetOpsReceived { get; private set; }

        public int LoadingOps { get; set; }

        // the error to reply with; transient by default, but overridable so the same harness can present
        // a fault that is *not* worth retrying
        public string ErrorText { get; set; } = "LOADING Redis is loading the dataset in memory";

        public int UnknownOpsReceived { get; private set; }

        // a command the *client* cannot categorise; answered from the same LoadingOps budget so the retry
        // decision is isolated to the category, not the error kind
        public override TypedRedisValue OnUnknownCommand(RedisClient client, in RedisRequest request, ReadOnlySpan<byte> command)
        {
            UnknownOpsReceived++;

            if (LoadingOps > 0)
            {
                LoadingOps--;
                return TypedRedisValue.Error(ErrorText);
            }

            return TypedRedisValue.SimpleString("made-up-ok");
        }

        protected override TypedRedisValue Get(RedisClient client, in RedisRequest request)
        {
            GetOpsReceived++;

            // while LOADING ops remain, consume one and reply with a transient LOADING error
            if (LoadingOps > 0)
            {
                LoadingOps--;
                return TypedRedisValue.Error(ErrorText);
            }

            return base.Get(client, in request);
        }
    }
}
