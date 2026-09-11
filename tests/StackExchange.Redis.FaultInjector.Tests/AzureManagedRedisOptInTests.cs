using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Xunit;

namespace StackExchange.Redis.FaultInjector.Tests;

/// <summary>
/// The pre-emptive Azure Managed Redis default, checked against a real AMR instance.
/// </summary>
/// <remarks>
/// <see cref="Configuration.AzureManagedRedisOptionsProvider"/> asks for maintenance notifications on every
/// AMR connection, ahead of AMR emitting them, on the reasoning that asking is harmless when the answer is
/// no. That reasoning has one assumption in it: that the server refuses *tidily*. The opt-in is pipelined with
/// <c>HELLO</c>, <c>AUTH</c>, <c>CLIENT SETNAME</c>, <c>CLIENT SETINFO</c> and <c>CLIENT ID</c>, all written
/// before any reply is read, so a server that closed the connection, desynchronised the reply stream, or
/// answered a hopeful <c>+OK</c> would make the default actively harmful rather than merely useless.
/// <para>
/// Measured 2026-09-10 against AMR 7.4.3 (standalone): <c>ERR unknown subcommand 'MAINT_NOTIFICATIONS'</c>,
/// the connection survives, and the reply stream stays aligned - an ordinary error, which is the good
/// outcome. These tests are what makes that reproducible when the rollout does land, at which point the same
/// tests should start reporting the feature as accepted instead.
/// </para>
/// <para>
/// Point them at an instance with <c>SER_AMR_ENDPOINT</c> (<c>host:port</c>) and <c>SER_AMR_KEY</c>; without
/// both they skip. Nothing is written to the database.
/// </para>
/// </remarks>
[Trait("tier", "azure-managed-redis")]
public class AzureManagedRedisOptInTests(ITestOutputHelper log)
{
    private const string EndpointVariable = "SER_AMR_ENDPOINT", KeyVariable = "SER_AMR_KEY";

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

    private static (string EndPoint, string Key) Require()
    {
        var endpoint = Environment.GetEnvironmentVariable(EndpointVariable);
        var key = Environment.GetEnvironmentVariable(KeyVariable);
        if (string.IsNullOrWhiteSpace(endpoint) || string.IsNullOrWhiteSpace(key))
        {
            Assert.Skip($"set {EndpointVariable} (host:port) and {KeyVariable} to run the Azure Managed Redis checks");
        }

        return (endpoint!, key!);
    }

    private static ConfigurationOptions Options(string endpoint, string key)
    {
        // Deliberately minimal: the point is that the *provider* recognises the hostname and applies the
        // defaults, so anything set here beyond credentials would be testing our own configuration instead.
        var options = ConfigurationOptions.Parse(endpoint);
        options.Password = key;
        options.AbortOnConnectFail = false;
        return options;
    }

    [Fact]
    public async Task TheProviderAsksAndAmrRefusesWithoutHarm()
    {
        var (endpoint, key) = Require();
        var logs = new CapturingLoggerFactory();
        var options = Options(endpoint, key);
        options.LoggerFactory = logs;

        // the defaults under test, resolved from the hostname alone
        Assert.Equal(MaintenanceNotificationMode.Auto, options.MaintenanceNotifications);
        log.WriteLine($"provider defaults: maintNotifications={options.MaintenanceNotifications}, protocol={options.Protocol?.ToString() ?? "(unset)"}, ssl={options.Ssl}");

        await using var conn = await ConnectionMultiplexer.ConnectAsync(options);
        var db = conn.GetDatabase();

        // The handshake carried a command this server does not know, so the thing to prove is that everything
        // after it still works and the reply stream is aligned - not merely that connect() returned.
        Assert.True(await db.PingAsync() >= TimeSpan.Zero);
        Assert.Equal("alignment-check", (string?)await db.ExecuteAsync("ECHO", "alignment-check"));
        Assert.True(await db.PingAsync() >= TimeSpan.Zero);

        // SER_AMR_VERBOSE dumps the whole connect log, which is how endpoint discovery is traced when an
        // AMR instance turns out to advertise more endpoints than you configured.
        var verbose = string.Equals(Environment.GetEnvironmentVariable("SER_AMR_VERBOSE"), "true", StringComparison.OrdinalIgnoreCase);
        foreach (var line in logs.Lines.Where(l => verbose || l.Contains("aintenance", StringComparison.Ordinal)))
        {
            log.WriteLine(line);
        }

        log.WriteLine($"endpoints: {string.Join(", ", conn.GetEndPoints().Select(e => e.ToString()))}");

        Assert.Contains(logs.Lines, l => l.Contains("Requesting maintenance notifications", StringComparison.Ordinal));

        // Either outcome is a pass, and which one it is *is* the finding: refused today, accepted once AMR
        // ships it. What must not happen is neither line appearing, which would mean we never asked, or the
        // reply never being matched to the request.
        var accepted = logs.Lines.Any(l => l.Contains("Maintenance notifications accepted", StringComparison.Ordinal));
        var refused = logs.Lines.Any(l => l.Contains("Maintenance notifications refused", StringComparison.Ordinal));
        log.WriteLine($"=> accepted={accepted}, refused={refused}");
        Assert.True(accepted ^ refused, "exactly one of accepted/refused should have been reported");
    }

    [Fact]
    public async Task EnabledFailsWhileAmrCannotDeliver()
    {
        var (endpoint, key) = Require();
        var options = Options(endpoint, key);
        options.MaintenanceNotifications = MaintenanceNotificationMode.Enabled;
        options.AbortOnConnectFail = true; // the point is the failure, so do not let it be tolerated

        // Enabled means *required*. While the server refuses, connecting must fail rather than quietly
        // running without the feature - and if this starts passing, AMR has begun supporting it, which is
        // information rather than a broken test.
        var ex = await Record.ExceptionAsync(() => ConnectionMultiplexer.ConnectAsync(options));
        log.WriteLine(ex is null ? "connected - AMR now delivers maintenance notifications" : $"rejected: {ex.GetType().Name}: {ex.Message}");

        if (ex is null)
        {
            Assert.Skip("the server accepted the opt-in, so there is nothing for Enabled to reject; AMR support has landed");
        }

        Assert.IsAssignableFrom<RedisConnectionException>(ex);
    }
}
