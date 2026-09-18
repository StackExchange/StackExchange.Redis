using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using StackExchange.Redis.Availability;
using Xunit;

namespace StackExchange.Redis.FaultInjector.Tests;

/// <summary>
/// <para>
/// Client-side geographic failover against a real Redis Enterprise Active-Active deployment, driven by the
/// cross-client fault injector - the counterpart of the Jedis <c>ActiveActiveFailoverIT</c>. Needs a
/// <c>re-active-active</c> entry listing two member databases in the environment's <c>endpoints.json</c>.
/// </para>
/// <para>
/// Unlike the Jedis test - which disables client-side retry and hand-rolls a retry loop in the workload -
/// this uses the features a production SE.Redis application would: a connection group with weights, a tuned
/// circuit-breaker and health-check cadence, and a <c>WithRetry</c> database expected to ride the failover
/// transparently.
/// </para>
/// <para>
/// The only test in this tier that asserts on *timing* rather than logging it, and the only one that needs a
/// concurrent workload. Both follow from what is being measured: a circuit breaker trips on observed
/// failures, so the failover only happens under load, and "member0 stayed down for the whole outage" is the
/// property that distinguishes a real failover from a blip.
/// </para>
/// </summary>
[Trait("tier", "fault-injector")]
[Trait("scenario", "active-active")]
public class ActiveActiveFailoverScenarioTests(ExistingDatabaseFixture fixture, ITestOutputHelper output)
    : IClassFixture<ExistingDatabaseFixture>
{
    private const int NumWorkers = 18; // Jedis parity
    private const int NetworkFailureSeconds = 15;

    [Fact]
    public async Task Failover()
    {
        // Require covers both gates: no environment / not opted in, and no such database in this template.
        // An unreachable *injector* is deliberately not a skip - it surfaces as a failure from
        // FaultInjectorClient, naming the URL, because a configured-and-enabled run that silently skips
        // reports success for a test that never ran.
        var database = fixture.Require("re-active-active");
        Assert.SkipWhen(
            database.MemberCount < 2,
            $"'{database.Key}' lists {database.MemberCount} endpoint(s); an Active-Active failover test needs two member databases");

        var cancellationToken = TestContext.Current.CancellationToken;
        output.WriteLine($"database: {database}");
        output.WriteLine($"members: {string.Join(", ", database.Members.Select((m, i) => $"member{i}={m}"))}");

        // Configuration mapping from the Jedis scenario test:
        //   socket/connect timeout 2000ms        -> ConnectTimeout/SyncTimeout/AsyncTimeout below
        //   weights 1.0 / 0.5                    -> ConnectionGroupMember.Weight
        //   CB slidingWindowSize 1s / rate 10%   -> MetricsWindowSize / FailureRateThreshold
        //   (minimum meaningful sample)          -> MinimumNumberOfFailures = 5: the library default of 1000
        //                                           is sized for production traffic and would be inert here
        //   failbackCheckInterval 1000ms         -> HealthCheckInterval
        //   gracePeriod 2000ms                   -> FailbackDelay
        //   retry disabled + manual retry loop   -> replaced by the group RetryPolicy + WithRetry
        ConfigurationOptions MemberConfig(int index)
        {
            // Disabled, not the tier's Enabled: on a notification-capable build, delivering MOVING/MIGRATING
            // pushes would set the handoff machinery racing the group's health-check and circuit-breaker
            // machinery over the same failover - two features measured at once, and an ambiguous failure.
            // This test asserts about the *group*, so the other mechanism is off. (Note the fixture still
            // enables the cluster-level flag for its other consumers; that is a capability, not an opt-in.)
            var config = database.GetClientConfig(fixture.Environment, MaintenanceNotificationMode.Disabled, index);

            // 2000ms, not the tier's 15_000: the circuit breaker only trips on *observed* failures, and a 15s
            // command budget outlives the 15s outage - nothing would fail, so nothing would fail over.
            config.ConnectTimeout = 2000;
            config.SyncTimeout = 2000;
            config.AsyncTimeout = 2000;

            // GetClientConfig pins RESP3, where the Jedis test and this test's earlier form used the
            // protocol default. Accepted rather than overridden: geographic failover is protocol-agnostic
            // and this is free coverage. If it ever proves to matter, the answer is a [Theory] over both
            // protocols, not a permanent pin to RESP2.
            return config;
        }

        var member0 = new ConnectionGroupMember(MemberConfig(0), "member0") { Weight = 1.0 };
        var member1 = new ConnectionGroupMember(MemberConfig(1), "member1") { Weight = 0.5 };

        MultiGroupOptions options = new MultiGroupOptions.Builder
        {
            CircuitBreaker = new CircuitBreaker.Builder
            {
                MetricsWindowSize = TimeSpan.FromSeconds(1),
                FailureRateThreshold = 10,
                MinimumNumberOfFailures = 5,
            },
            HealthCheckInterval = TimeSpan.FromSeconds(1),
            FailbackDelay = TimeSpan.FromSeconds(2),
            RetryPolicy = new RetryPolicy.Builder
            {
                MaxAttempts = 5,
                RetryDelay = TimeSpan.FromMilliseconds(200),
                FailoverDelay = TimeSpan.FromSeconds(5),
                // XADD is an accumulating write, above the default (last-wins) retry cap; a network blackout
                // produces *ambiguous* faults, so without raising the cap they would never be retried.
                // At-least-once XADD is exactly the semantic the Jedis test's manual retry loop has, so this
                // is a like-for-like trade.
                MaxCommandRetryCategory = CommandFlags.CommandRetryWriteAccumulating,
            },
        };

        using var writer = new TestOutputWriter(output);
        var group = await ConnectionMultiplexer.ConnectGroupAsync([member0, member1], options, writer);
        try
        {
            // the endpoints file explicitly declared this deployment, so failing to reach it is a test
            // failure, not a skip
            Assert.True(group.IsConnected, "unable to connect to the re-active-active deployment");

            var reporter = new FailoverReporter(group, member0, output);
            group.ConnectionChanged += reporter.OnConnectionChanged;

            IDatabaseAsync db = group.GetDatabase().WithRetry(); // resolves the group RetryPolicy

            // fi- prefixed, unlike the Jedis test's bare "execution_log": this tier shares template
            // databases between scenario classes, so a generic key name is asking for a collision.
            RedisKey streamKey = "fi-aa-execution-log";
            await db.KeyDeleteAsync(streamKey); // housekeeping (divergence from Jedis: unbounded growth)

            long executed = 0, failedAfterFailover = 0, lastFailedAtTicks = 0;

            using var overallCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            overallCts.CancelAfter(TimeSpan.FromSeconds(240)); // nothing here may wedge the suite
            using var workloadCts = CancellationTokenSource.CreateLinkedTokenSource(overallCts.Token);
            var ct = overallCts.Token;

            var workload = new MultiThreadedWorkload(
                async (workerId, stopToken) =>
                {
                    // snapshot before executing (Jedis parity): did this attempt target the failed primary?
                    bool onPrimary = ReferenceEquals(group.ActiveMember, member0);
                    try
                    {
                        await db.StreamAddAsync(
                            streamKey,
                            [new("threadId", workerId), new("cluster", reporter.CurrentMemberName)]).ForAwait();
                        Interlocked.Increment(ref executed);
                    }
                    catch (Exception ex) when (ex is RedisConnectionException or RedisTimeoutException)
                    {
                        // WithRetry exhausted its attempts. During the failover window that is the metric the
                        // Jedis test tracks as failedCommandsAfterFailover; outside it, it is a real failure -
                        // rethrow so the workload captures it and the test fails.
                        if (reporter.FailoverHappened && !reporter.FailbackHappened && onPrimary)
                        {
                            Interlocked.Increment(ref failedAfterFailover);
                            Volatile.Write(ref lastFailedAtTicks, DateTime.UtcNow.Ticks);
                        }
                        else
                        {
                            throw;
                        }

                        await Task.Delay(5, stopToken).ForAwait(); // Jedis parity: pause after a failed attempt
                    }
                },
                NumWorkers);
            workload.Start(workloadCts.Token);

            // wait for traffic to actually flow before injecting the fault. Deliberately not Poll.UntilAsync:
            // that returns false on timeout rather than throwing, and takes no cancellation token - here a
            // workload that never gets going must fail the test, and must respect the 240s cap.
            await UntilAsync(() => Volatile.Read(ref executed) > 0, TimeSpan.FromSeconds(30), ct);
            output.WriteLine($"workload started; triggering a {NetworkFailureSeconds}s network failure on bdb {database.BdbId}...");

            // In an Active-Active topology this bdb id exists on *both* clusters, and /action has no
            // cluster_index (only the scenario routes do) - so which copy gets blackholed is the injector's
            // choice. It evidently picks the first cluster, which is Members[0]/member0; that is what the
            // final assertion depends on, hence the member logging above and the message below.
            await fixture.Injector.RunActionAsync(
                "network_failure",
                new Dictionary<string, object?>
                {
                    ["bdb_id"] = database.BdbId,
                    ["delay"] = NetworkFailureSeconds,
                },
                timeout: TimeSpan.FromSeconds(120),
                cancellationToken: ct);

            // keep executing after the fault completes, giving failback room to happen (Jedis parity)
            await Task.Delay(TimeSpan.FromSeconds(30), ct);
            workloadCts.Cancel();
            await workload.Completion;

            // report the timeline and metrics before asserting, so a failed run is diagnosable
            output.WriteLine($"executed commands: {Volatile.Read(ref executed)}");
            output.WriteLine($"failed commands after failover (escaped WithRetry): {Volatile.Read(ref failedAfterFailover)}");
            output.WriteLine($"failover at: {reporter.FailoverAt:O}; failback at: {reporter.FailbackAt:O}");
            var lastFailed = Volatile.Read(ref lastFailedAtTicks);
            output.WriteLine(lastFailed == 0
                ? "full failover time: n/a (no commands failed after failover)"
                : $"full failover time: {new DateTime(lastFailed, DateTimeKind.Utc) - reporter.FailoverAt}");

            // (the Jedis test also asserts its connection pools drained; a multiplexer has no pools)
            Assert.True(reporter.FailoverHappened, "no failover was observed");
            Assert.True(reporter.FailbackHappened, "no failback was observed");
            Assert.True(
                reporter.FailbackAt - reporter.FailoverAt >= TimeSpan.FromSeconds(NetworkFailureSeconds),
                $"failback after {reporter.FailbackAt - reporter.FailoverAt} - member0 should have stayed down for the full {NetworkFailureSeconds}s outage");
            Assert.True(
                ReferenceEquals(group.ActiveMember, member0),
                $"traffic should have returned to the highest-weight member0 ({database.Members[0]}) after failback; "
                + $"if the injector blackholed member1 ({database.Members[1]}) instead, this is measuring the wrong cluster");
            if (!workload.CapturedExceptions.IsEmpty)
            {
                Assert.Fail($"{workload.CapturedExceptions.Count} command(s) failed outside the failover window; first: {FirstOrNull(workload.CapturedExceptions)}");
            }
        }
        finally
        {
            group.Dispose();
        }
    }

    private static Exception? FirstOrNull(ConcurrentQueue<Exception> queue) => queue.TryPeek(out var ex) ? ex : null;

    private static async Task UntilAsync(Func<bool> condition, TimeSpan timeout, CancellationToken cancellationToken)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (!condition())
        {
            if (DateTime.UtcNow > deadline) throw new TimeoutException($"Condition not met within {timeout}.");
            await Task.Delay(100, cancellationToken).ForAwait();
        }
    }

    /// <summary>
    /// Watches <see cref="IConnectionGroup.ConnectionChanged"/>: logs every event, and classifies (by member
    /// identity, more robust than Jedis's "first event = failover, second = failback") the first switch away
    /// from the primary as the failover and the first switch back as the failback.
    /// </summary>
    private sealed class FailoverReporter(IConnectionGroup group, ConnectionGroupMember primary, ITestOutputHelper output)
    {
        private volatile string _currentMemberName = group.ActiveMember?.Name ?? "(none)";
        private volatile bool _failoverHappened, _failbackHappened;

        public string CurrentMemberName => _currentMemberName;
        public bool FailoverHappened => _failoverHappened;
        public bool FailbackHappened => _failbackHappened;
        public DateTime FailoverAt { get; private set; }
        public DateTime FailbackAt { get; private set; }

        public void OnConnectionChanged(object? sender, GroupConnectionChangedEventArgs e)
        {
            var now = DateTime.UtcNow;
            output.WriteLine($"[{now:HH:mm:ss.fff}] {e.Type}: {e.PreviousGroup?.Name ?? "(none)"} -> {e.Group.Name}");
            if (e.Type != GroupConnectionChangedEventArgs.ChangeType.ActiveChanged) return;

            _currentMemberName = e.Group.Name;
            if (!_failoverHappened && !ReferenceEquals(e.Group, primary))
            {
                FailoverAt = now;
                _failoverHappened = true;
            }
            else if (_failoverHappened && !_failbackHappened && ReferenceEquals(e.Group, primary))
            {
                FailbackAt = now;
                _failbackHappened = true;
            }
        }
    }
}
