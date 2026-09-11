using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using StackExchange.Redis.Availability;
using Xunit;

namespace StackExchange.Redis.Tests;

/// <summary>
/// Maintenance notifications are not activated inside a multi-group (geo-redundant) connection, even when a
/// caller asks for them explicitly.
/// </summary>
/// <remarks>
/// How the two features should interact is not yet defined. Upstream disables maintenance notifications
/// alongside geographic failover, and our design notes said we follow that - but saying so and enforcing it
/// are different things, and until this it was only the former. Acting on a handoff in one region while the
/// group is deciding whether to fail away from that region is a risk with no test behind it, so the safe
/// position is to not start.
/// <para>
/// The explicit case is the one that needs a test rather than a comment: `Enabled` means *required* and
/// normally rejects a connection that cannot have it, so suppressing the request without also suppressing
/// that rule would turn an explicit opt-in into a connection that can never be established.
/// </para>
/// </remarks>
[Collection(NonParallelCollection.Name)]
public class MaintenanceInGroupTests(ITestOutputHelper log)
{
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

    [Theory]
    [InlineData(MaintenanceNotificationMode.Auto)]
    [InlineData(MaintenanceNotificationMode.Enabled)] // the one that would otherwise fail the connection
    public async Task AGroupMemberNeverAsksForMaintenanceNotifications(MaintenanceNotificationMode mode)
    {
        using var server = new InProcessTestServer(log);
        var logs = new CapturingLoggerFactory();

        var config = server.GetClientConfig(defaultOnly: true);
        config.Protocol = RedisProtocol.Resp3; // so nothing else could explain the absence of the opt-in
        config.MaintenanceNotifications = mode;
        config.LoggerFactory = logs;

        var group = await ConnectionMultiplexer.ConnectGroupAsync([new ConnectionGroupMember(config, "primary")]);
        await using (group as IAsyncDisposable ?? new NoopAsyncDisposable())
        {
            // it connected at all, which is the half that matters for Enabled: "required" must not mean
            // "impossible" here, or configuring a group with an explicit opt-in would be unusable
            var db = group.GetDatabase();
            Assert.True(await db.PingAsync() >= TimeSpan.Zero);

            Assert.Equal(0, server.TotalMaintenanceOptIns);
            log.WriteLine($"{mode}: opt-ins seen by the server = {server.TotalMaintenanceOptIns}");

            // ...and it said so, because a silently ignored explicit opt-in is worse than the gap it closes
            var warning = logs.Lines.FirstOrDefault(l => l.Contains("multi-group", StringComparison.Ordinal));
            log.WriteLine(warning ?? "(no warning logged)");
            Assert.NotNull(warning);
            Assert.Contains(mode.ToString(), warning);
        }
    }

    [Fact]
    public async Task AnOrdinaryMultiplexerStillAsks()
    {
        // The control: same server, same configuration, no group. Without this, the test above would pass just
        // as happily if the opt-in had stopped working altogether.
        using var server = new InProcessTestServer(log);
        var config = server.GetClientConfig(defaultOnly: true);
        config.Protocol = RedisProtocol.Resp3;
        config.MaintenanceNotifications = MaintenanceNotificationMode.Enabled;

        await using var conn = await ConnectionMultiplexer.ConnectAsync(config);
        Assert.True(await conn.GetDatabase().PingAsync() >= TimeSpan.Zero);

        log.WriteLine($"outside a group: opt-ins seen by the server = {server.TotalMaintenanceOptIns}");
        Assert.True(server.TotalMaintenanceOptIns > 0, "outside a group, an explicit opt-in must still be sent");
    }

    private sealed class NoopAsyncDisposable : IAsyncDisposable
    {
        public ValueTask DisposeAsync() => default;
    }
}
