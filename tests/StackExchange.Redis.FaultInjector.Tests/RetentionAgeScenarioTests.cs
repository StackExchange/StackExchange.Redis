using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using Microsoft.Extensions.Logging;
using System.Threading;
using System.Threading.Tasks;
using StackExchange.Redis.Maintenance;
using Xunit;

namespace StackExchange.Redis.FaultInjector.Tests;

/// <summary>
/// How long a retained completion stays retained - the last unmeasured property of the catch-up channel.
/// </summary>
/// <remarks>
/// What is already known, from captures: a connection that opts in *after* a shard-scoped event gets the
/// event's **completion** replayed (<c>MIGRATED</c>, <c>FAILED_OVER</c>; never a starter, never
/// <c>MOVING</c>), delivered within ~17ms of the opt-in being accepted, most-recent-replaces. What is not
/// known is whether that retention ever ages out. It matters because a client is entitled to act on what it
/// receives: a completion replayed hours later would open a relaxed window for an event that finished long
/// ago, which is harmless-but-wasteful for us and would be worth an age guard if the server does not have one.
/// <para>
/// Written as a measurement rather than a pass/fail: the schedule is a ladder, every probe is logged, and the
/// only hard assertions are the ones that say the measurement itself is sound. Opt in with
/// <c>SER_FI_RETENTION_AGE_MINUTES=&lt;minutes&gt;</c>; without it this skips, because it fires one failover
/// and then spends the rest of its time waiting, which has no place in an ordinary run of the tier.
/// </para>
/// </remarks>
[Trait("tier", "fault-injector")]
[Trait("scenario", "retention-age")]
public class RetentionAgeScenarioTests(ReplicatedDatabaseFixture fixture, ITestOutputHelper log)
    : IClassFixture<ReplicatedDatabaseFixture>
{
    private const string HorizonVariable = "SER_FI_RETENTION_AGE_MINUTES";
    private const string ProgressVariable = "SER_FI_RETENTION_AGE_LOG";

    /// <summary>
    /// Progress is written to a file as it happens, as well as to the test output.
    /// </summary>
    /// <remarks>
    /// <see cref="ITestOutputHelper"/> is buffered until the test finishes, and this test runs for hours - so
    /// through the only channel a test normally has, a run in progress and a run that has wedged look
    /// identical. The file is flushed per line, so the ladder can be read while it is still being climbed.
    /// </remarks>
    private static string ProgressPath =>
        Environment.GetEnvironmentVariable(ProgressVariable) is { Length: > 0 } configured
            ? configured
            : Path.Combine(Path.GetTempPath(), "ser-retention-age.log");

    /// <summary>Probe ages, in minutes since the completion; trimmed to whatever horizon was asked for.</summary>
    /// <remarks>
    /// Dense early and sparse later: an expiry at 30 seconds and an expiry at two hours are both plausible, and
    /// a geometric ladder pins either to within a factor of two without spending the whole cluster lease.
    /// </remarks>
    private static readonly int[] LadderMinutes = [1, 2, 5, 10, 20, 30, 45, 60, 90, 120, 180, 240];

    [Fact]
    public async Task HowLongIsACompletionRetained()
    {
        fixture.RequireAvailable();
        var horizon = ReadHorizon();
        if (horizon is null)
        {
            Assert.Skip(
                $"set {HorizonVariable}=<minutes> to run this; it fires one failover and then probes for that "
                + "long, so it is opt-in even within this tier");
        }

        var cancellationToken = TestContext.Current.CancellationToken;
        var database = fixture.Database;
        Assert.NotNull(database);

        using var progress = new StreamWriter(ProgressPath, append: true) { AutoFlush = true };
        _progress = progress;
        Note($"--- retention age, horizon {horizon} minutes, started {DateTime.UtcNow:u} ---");
        Note($"provisioned {database}");

        // The witness stays connected for the whole run, for one reason: retention is most-recent-replaces, so
        // any *further* event on this database resets the age we are measuring. If one arrives, the ladder from
        // that point on is measuring the new event, and the log has to show that rather than hide it.
        var clock = Stopwatch.StartNew();
        var witnessed = new List<(TimeSpan At, PushMaintenanceEvent Push)>();
        await using var witness = await ConnectionMultiplexer.ConnectAsync(database.GetClientConfig());
        witness.ServerMaintenanceEvent += (_, e) =>
        {
            if (e is not PushMaintenanceEvent push) return;
            lock (witnessed) witnessed.Add((clock.Elapsed, push));
            Note($"  witness +{clock.Elapsed.TotalSeconds,6:0.0}s  {push.NotificationType} seq={push.SequenceId} {push.RawMessage}");
        };

        await witness.GetDatabase().StringSetAsync("fi-retention-age", "before");

        clock.Restart();
        try
        {
            await fixture.Injector.RunActionAsync(
                "failover",
                new Dictionary<string, object?> { ["bdb_id"] = database.BdbId.ToString() },
                cancellationToken: cancellationToken);
        }
        catch (Exception ex)
        {
            Assert.Skip($"the injector would not run 'failover' against bdb {database.BdbId}: {ScenarioSupport.Summarize(ex.Message)}");
        }

        Note($"  +{clock.Elapsed.TotalSeconds,6:0.0}s  injector reports the failover finished");

        // A completion is what gets retained, so the clock we care about starts when one arrives - not when the
        // scenario was fired, and not when the injector called it done.
        var completed = await Poll.UntilAsync(
            () =>
            {
                lock (witnessed) return witnessed.Any(w => IsCompletion(w.Push.NotificationType));
            },
            timeoutMilliseconds: 120_000);

        if (!completed)
        {
            lock (witnessed)
            {
                var seen = witnessed.Count == 0 ? "nothing" : string.Join(", ", witnessed.Select(w => w.Push.NotificationType));
                Assert.Skip($"no completion was announced within 120s (saw {seen}), so there is nothing whose retention could be measured");
            }
        }

        TimeSpan completionAt;
        long completionSequence;
        MaintenanceNotificationType completionType;
        lock (witnessed)
        {
            var completion = witnessed.First(w => IsCompletion(w.Push.NotificationType));
            completionAt = completion.At;
            completionSequence = completion.Push.SequenceId;
            completionType = completion.Push.NotificationType;
        }

        Note($"completion to measure: {completionType} seq={completionSequence} at +{completionAt.TotalSeconds:0.0}s");

        var results = new List<Probe>();
        foreach (var minutes in LadderMinutes.Where(m => m <= horizon))
        {
            var due = completionAt + TimeSpan.FromMinutes(minutes);
            var wait = due - clock.Elapsed;
            if (wait > TimeSpan.Zero) await Task.Delay(wait, cancellationToken);

            // Two connections per rung, back to back. If the first sees the replay and the second does not, the
            // server clears the retained item once it has been delivered - in which case every later rung is
            // measuring an empty channel rather than an expired one, and the ladder means nothing. That
            // confound is invisible with one connection per rung, and it would look exactly like an early
            // expiry.
            var first = await ProbeAsync(database, minutes, "a", cancellationToken);
            var second = await ProbeAsync(database, minutes, "b", cancellationToken);
            results.Add(first);
            results.Add(second);

            if (first.Replayed && !second.Replayed)
            {
                Note(
                    $"  !! at {minutes}m the first probe saw the replay and the second did not: retention looks "
                    + "consumed on delivery, so rungs beyond this one cannot be read as ages");
            }
        }

        Note(string.Empty);
        Note("age     probe  replayed  type          notes");
        foreach (var probe in results)
        {
            var verdict = probe.Conclusive ? (probe.Replayed ? "yes" : "no") : "n/a";
            Note($"{probe.Minutes,4}m   {probe.Label,-5}  {verdict,-8}  {probe.Type,-12}  {probe.Notes}");
        }

        lock (witnessed)
        {
            var later = witnessed.Where(w => w.At > completionAt).ToList();
            if (later.Count != 0)
            {
                Note(
                    "  !! further events arrived after the completion, so the retained item was replaced and the "
                    + $"ages above are relative to the wrong event: {string.Join(", ", later.Select(w => $"{w.Push.NotificationType}@+{w.At.TotalSeconds:0}s"))}");
            }
        }

        // The measurement is the output; these two assertions exist so that a run which proves nothing says so
        // instead of being read as "retention expires immediately".
        Assert.NotEmpty(results);
        Assert.True(results[0].Conclusive, $"the first probe could not connect: {results[0].Notes}");
        Assert.True(
            results[0].Replayed,
            $"the first probe ({results[0].Minutes}m after a {completionType}) saw no replay at all, so this run "
            + "measured nothing - either retention is shorter than the first rung, or the opt-in is not being honoured");

        // Once it stops being replayed it must stay stopped. An age guard is monotone; a completion that
        // reappears after a gap would mean something much stranger than expiry, and is worth failing on.
        var byRung = results.Where(r => r is { Label: "a", Conclusive: true }).ToList();
        if (byRung.Count == 0)
        {
            Assert.Fail("every probe failed to connect, so nothing was measured");
        }

        var firstMiss = byRung.FindIndex(r => !r.Replayed);
        if (firstMiss >= 0)
        {
            var after = byRung.Skip(firstMiss).Where(r => r.Replayed).ToList();
            Assert.True(
                after.Count == 0,
                $"the replay stopped at {byRung[firstMiss].Minutes}m and then came back at "
                + $"{string.Join(", ", after.Select(r => r.Minutes + "m"))}, which no expiry rule explains");
            Note($"=> retention lapsed between {(firstMiss == 0 ? 0 : byRung[firstMiss - 1].Minutes)}m and {byRung[firstMiss].Minutes}m");
        }
        else
        {
            Note($"=> still replayed at {byRung[^1].Minutes}m: retention outlasts the horizon asked for");
        }
    }

    /// <summary>
    /// One fresh connection, and what the server told it on the way in.
    /// </summary>
    /// <remarks>
    /// The observable is the client's own log, not the <c>ServerMaintenanceEvent</c>, and that is not a
    /// convenience: a retained completion arrives within ~17ms of the opt-in being accepted, which is *inside*
    /// <c>ConnectAsync</c>, so a handler attached after connecting has already missed it. A logger can be
    /// attached through the configuration beforehand.
    /// <para>
    /// It used to read the endpoint's relaxed window instead, which was simpler and is no longer true: since a
    /// catch-up completion is history rather than news, it deliberately opens no window - a change this
    /// measurement is what prompted. Reading the window would now report "not replayed" at every rung, which
    /// would look exactly like an expiry at the first one.
    /// </para>
    /// </remarks>
    private async Task<Probe> ProbeAsync(ProvisionedDatabase database, int minutes, string label, CancellationToken cancellationToken)
    {
        try
        {
            var options = database.GetClientConfig();
            var notifications = new NotificationLog();
            options.LoggerFactory = notifications;

            await using var conn = await ConnectionMultiplexer.ConnectAsync(options);
            var endpoint = ((IInternalConnectionMultiplexer)conn).GetServerEndPoint(conn.GetEndPoints()[0]);
            var received = notifications.Received;
            var relaxed = received.Count != 0;
            var type = received.Count == 0 ? MaintenanceNotificationType.None : TypeOf(received[0]);
            if (endpoint.IsMaintenanceRelaxed)
            {
                // not expected on a fresh connection any more; if it happens, something arrived *live* while
                // we were connecting, and the rung is measuring that instead
                Note($"  probe {minutes}m/{label}: note - the window is open ({endpoint.ActiveMaintenanceType}), so a live event may be in play");
            }

            // a live event arriving *during* the probe would also relax the window, so record anything that
            // shows up while we are here; the witness sees it too, and the two together tell them apart
            var live = new List<MaintenanceNotificationType>();
            conn.ServerMaintenanceEvent += (_, e) =>
            {
                if (e is PushMaintenanceEvent push) lock (live) live.Add(push.NotificationType);
            };
            await conn.GetDatabase().PingAsync();
            await Task.Delay(2000, cancellationToken);

            string notes;
            lock (live)
            {
                notes = live.Count == 0 ? string.Empty : $"also received live: {string.Join(", ", live)}";
            }

            if (received.Count != 0)
            {
                notes = string.IsNullOrEmpty(notes) ? received[0] : $"{received[0]}; {notes}";
            }

            Note($"  probe {minutes}m/{label}: relaxed={relaxed} type={type} {notes}");
            return new Probe(minutes, label, relaxed, type, notes, Conclusive: true);
        }
        catch (Exception ex)
        {
            // Inconclusive, emphatically not "no replay": this runs for hours against a cluster with a lease on
            // it, and a probe that cannot connect at all says nothing about retention. Counting it as a miss
            // would report an expiry at whatever minute the environment went away.
            Note($"  probe {minutes}m/{label}: failed to connect: {ex.GetType().Name}: {ex.Message}");
            return new Probe(minutes, label, false, MaintenanceNotificationType.None, $"connect failed: {ex.GetType().Name}", Conclusive: false);
        }
    }

    private StreamWriter? _progress;

    private void Note(string message)
    {
        log.WriteLine(message);
        _progress?.WriteLine(message.Length == 0 ? message : $"{DateTime.UtcNow:HH:mm:ss} {message}");
    }

    /// <summary>Reads the notification type back out of the log line, which is the only place it is stated.</summary>
    private static MaintenanceNotificationType TypeOf(string logLine)
    {
        foreach (var candidate in Enum.GetValues<MaintenanceNotificationType>())
        {
            if (candidate != MaintenanceNotificationType.None
                && logLine.Contains(candidate.ToString(), StringComparison.Ordinal))
            {
                return candidate;
            }
        }

        return MaintenanceNotificationType.None;
    }

    /// <summary>Captures the client's own "maintenance notification" lines, attached before connecting.</summary>
    private sealed class NotificationLog : ILoggerFactory, ILogger
    {
        private readonly List<string> _received = [];

        public List<string> Received
        {
            get { lock (_received) return [.. _received]; }
        }

        public ILogger CreateLogger(string categoryName) => this;

        public void AddProvider(ILoggerProvider provider) { }

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            var message = formatter(state, exception);
            if (message.Contains("Maintenance notification:", StringComparison.Ordinal))
            {
                lock (_received) _received.Add(message);
            }
        }

        public void Dispose() { }
    }

    private static bool IsCompletion(MaintenanceNotificationType type)
        => type is MaintenanceNotificationType.Migrated or MaintenanceNotificationType.FailedOver;

    private static int? ReadHorizon()
    {
        var raw = Environment.GetEnvironmentVariable(HorizonVariable);
        return int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var minutes) && minutes > 0
            ? minutes
            : null;
    }

    private readonly record struct Probe(
        int Minutes,
        string Label,
        bool Replayed,
        MaintenanceNotificationType Type,
        string Notes,
        bool Conclusive);
}
