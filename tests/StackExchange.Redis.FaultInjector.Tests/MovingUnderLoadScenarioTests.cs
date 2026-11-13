using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using StackExchange.Redis.Maintenance;
using Xunit;

namespace StackExchange.Redis.FaultInjector.Tests;

/// <summary>
/// The MOVING handoff under steady traffic, against a real deployment: how long after the notification the
/// connections are replaced, how long until the client is serving again, and how many commands failed meanwhile.
/// </summary>
/// <remarks>
/// A drain that waits for "no caller work" never finishes under load - there is always a command in flight - so it
/// recycles at the end of the announced window and fails whatever is in flight at that instant. Holding new work
/// back while the written commands complete is what lets the recycle happen within a round trip. This records the
/// timeline rather than asserting a bound, so that a run against any branch yields a comparable row.
/// </remarks>
[Trait("tier", "fault-injector")]
[Trait("scenario", "moving-handoff")]
public class MovingUnderLoadScenarioTests(ExistingDatabaseFixture fixture, ITestOutputHelper log)
    : IClassFixture<ExistingDatabaseFixture>
{
    [Theory]
    [InlineData(MaintenanceEndpointType.Auto)]
    public async Task HandoffUnderSteadyLoad(MaintenanceEndpointType endpointType)
    {
        fixture.RequireAvailable();
        var cancellationToken = TestContext.Current.CancellationToken;

        await using var scenario = await ScenarioRun.SetupAsync(
            fixture.Injector, "topology-change-standalone", "conn_drop", "endpoint_rebind", log.WriteLine,
            cancellationToken: cancellationToken);

        var database = scenario.Database;
        Assert.NotNull(database);

        var clock = Stopwatch.StartNew();
        var timeline = new List<string>();
        void Note(string what)
        {
            var entry = $"  +{clock.Elapsed.TotalSeconds,6:0.0}s  {what}";
            lock (timeline) timeline.Add(entry);
            log.WriteLine(entry);
        }

        var logs = new CapturingLogger();
        var config = database.GetClientConfig(fixture.Environment);
        config.MaintenanceMovingEndpointType = endpointType;
        config.LoggerFactory = logs;
        log.WriteLine($"moving-endpoint-type: {endpointType}");

        await using var conn = await ConnectionMultiplexer.ConnectAsync(config);
        var muxer = (IInternalConnectionMultiplexer)conn;
        var endpoint = muxer.GetServerEndPoint(conn.GetEndPoints()[0]);

        TimeSpan? movingAt = null, recycledAt = null, restoredAt = null;
        var failures = new List<(TimeSpan At, string What)>();
        conn.ConnectionFailed += (_, e) => Note($"disconnect: {e.FailureType} ({e.ConnectionType})");
        conn.ConnectionRestored += (_, e) =>
        {
            restoredAt ??= clock.Elapsed;
            Note($"reconnected ({e.ConnectionType})");
        };
        conn.ServerMaintenanceEvent += (_, e) =>
        {
            if (e is not PushMaintenanceEvent push) return;
            Note($"{push.NotificationType} seq={push.SequenceId} time={push.Time?.TotalSeconds.ToString() ?? "-"}");
            if (push.NotificationType == MaintenanceNotificationType.Moving) movingAt ??= clock.Elapsed;
        };

        // steady load: sixteen workers, each issuing its next PING the moment the previous one is answered, so there
        // is never an instant with nothing in flight and a drain that waits for an idle connection cannot finish (a
        // first cut paused 10ms between commands, and the old drain slipped through the gaps)
        long completed = 0;
        using var stop = new CancellationTokenSource();
        var db = conn.GetDatabase();
        var workload = new MultiThreadedWorkload(
            async (_, _) =>
            {
                try
                {
                    await db.PingAsync().ForAwait();
                    Interlocked.Increment(ref completed);
                }
                catch (Exception ex) when (ex is RedisException or TimeoutException)
                {
                    lock (failures) failures.Add((clock.Elapsed, ex.GetType().Name + ": " + FirstLine(ex.Message)));
                }
            },
            workerCount: 16);
        workload.Start(stop.Token);
        await Task.Delay(3000, cancellationToken);
        var warmup = Interlocked.Read(ref completed);
        log.WriteLine($"load: {warmup / 3.0:0} ops/s before the scenario fires");

        clock.Restart();
        // the injector call blocks until the scenario has run its course (about 20s), so watch for the recycle
        // alongside it rather than after it; then cover the announced window plus the measured overshoot
        var fire = scenario.FireAsync(cancellationToken);
        var deadline = TimeSpan.FromSeconds(100);
        while (clock.Elapsed < deadline)
        {
            if (recycledAt is null && endpoint.HandoffRecycles > 0)
            {
                recycledAt = clock.Elapsed;
                Note($"connections replaced (recycles={endpoint.HandoffRecycles}); handoff: {endpoint.LastHandoffOutcome}");
            }
            if (fire is { IsCompleted: true })
            {
                await fire;
                fire = null;
                Note("injector reports the scenario finished");
            }
            await Task.Delay(100, cancellationToken);
        }
        if (fire is not null) await fire;

        stop.Cancel();
        try
        {
            await workload.Completion;
        }
        catch (OperationCanceledException)
        {
        }

        var total = Interlocked.Read(ref completed);
        int failed;
        lock (failures)
        {
            failed = failures.Count;
            foreach (var (at, what) in failures.Take(20)) log.WriteLine($"  failed at +{at.TotalSeconds,6:0.0}s: {what}");
            if (failures.Count > 20) log.WriteLine($"  ... and {failures.Count - 20} more");
        }
        var missed = logs.Lines.Where(l => l.Contains("did not establish a replacement", StringComparison.Ordinal)).ToList();
        foreach (var line in missed) log.WriteLine("  " + line);

        log.WriteLine(string.Empty);
        log.WriteLine("| event | at |");
        log.WriteLine("|---|---|");
        log.WriteLine($"| MOVING received | {Format(movingAt)} |");
        log.WriteLine($"| connections replaced | {Format(recycledAt)} |");
        log.WriteLine($"| ConnectionRestored | {Format(restoredAt)} |");
        log.WriteLine($"| completed commands | {total} |");
        log.WriteLine($"| failed commands | {failed} |");
        log.WriteLine($"| missed-deadline warning | {(missed.Count > 0 ? "yes" : "no")} |");
        log.WriteLine($"| handoff outcome | {endpoint.LastHandoffOutcome ?? "(none)"} |");

        Assert.NotNull(movingAt);
        Assert.True(endpoint.HandoffRecycles > 0, "the handoff should have replaced the connections");

        static string Format(TimeSpan? at) => at is { } t ? $"+{t.TotalSeconds:0.0}s" : "(never)";
        static string FirstLine(string s)
        {
            var i = s.IndexOf('\n');
            return i < 0 ? s : s.Substring(0, i);
        }
    }

    private sealed class CapturingLogger : ILoggerFactory, ILogger
    {
        private readonly List<string> _lines = new();
        public IReadOnlyList<string> Lines
        {
            get
            {
                lock (_lines) return _lines.ToArray();
            }
        }

        public void AddProvider(ILoggerProvider provider) { }
        public ILogger CreateLogger(string categoryName) => this;
        public void Dispose() { }
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            lock (_lines) _lines.Add($"{logLevel}: {formatter(state, exception)}");
        }
    }
}
