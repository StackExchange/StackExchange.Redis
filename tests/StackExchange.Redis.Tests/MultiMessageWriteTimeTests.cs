using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Xunit;

namespace StackExchange.Redis.Tests;

/// <summary>
/// The sub-commands of a transaction (and the SELECT/READONLY/ASKING prefixes written ahead of a command) are
/// added to the awaiting-response queue without going through the normal enqueue path. If they carry no write
/// time, the heartbeat sees them as overdue by the process uptime, and on a connection that has been idle for
/// a while that is enough to trip the dead-socket heuristic while the reply is still in flight; see #3292.
/// </summary>
[Collection(NonParallelCollection.Name)]
public class MultiMessageWriteTimeTests(ITestOutputHelper output) : TestBase(output)
{
    [Fact]
    public async Task HeartbeatDuringTransactionOnIdleConnectionDoesNotTearItDown()
    {
        const int Timeout = 600;
        var logs = new CapturingLoggerFactory();
        using var server = new InProcessTestServer(Output);
        var config = server.GetClientConfig(defaultOnly: true);
        config.SyncTimeout = config.AsyncTimeout = Timeout;
        config.HeartbeatInterval = TimeSpan.FromHours(1); // only the heartbeats this test issues
        config.KeepAlive = 600; // no pings resetting the last-read clock during the idle period
        config.LoggerFactory = logs;

        await using var conn = await ConnectionMultiplexer.ConnectAsync(config);
        var db = conn.GetDatabase(1); // a non-default database, so a SELECT prefix is written ahead of the transaction
        var key = Me();
        await db.StringSetAsync(key, "before");

        // the heuristic needs the connection silent for more than 4x the timeout (whole seconds) before the write
        await Task.Delay((4 * Timeout) + 1_000);

        // replies now take longer than the gap to the heartbeat, but well inside the timeout
        server.SetLatency(TimeSpan.FromMilliseconds(250));
        var tran = db.CreateTransaction();
        tran.AddCondition(Condition.StringEqual(key, "before")); // WATCH + GET, written by the transaction itself
        _ = tran.StringSetAsync(key, "after");
        var exec = Task.Run(() => tran.ExecuteAsync());

        await Task.Delay(50); // the sub-commands are on the wire, their replies are not back yet
        conn.OnHeartbeat();

        var committed = await exec.ContinueWith(t => t.IsCompletedSuccessfully && t.Result, TaskScheduler.Default);
        Assert.DoesNotContain(logs.Lines, l => l.Contains("Dead socket", StringComparison.Ordinal));
        Assert.True(committed, "the transaction should have committed");
        Assert.Equal("after", await db.StringGetAsync(key));
    }

    private sealed class CapturingLoggerFactory : ILoggerFactory, ILogger
    {
        private readonly List<string> _lines = [];

        public IReadOnlyList<string> Lines
        {
            get { lock (_lines) return [.. _lines]; }
        }

        public ILogger CreateLogger(string categoryName) => this;

        public void AddProvider(ILoggerProvider provider) { }

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            lock (_lines) _lines.Add(formatter(state, exception));
        }

        public void Dispose() { }
    }
}
