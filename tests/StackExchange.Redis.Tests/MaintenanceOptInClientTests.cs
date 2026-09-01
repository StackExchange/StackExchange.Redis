﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Xunit;
using static StackExchange.Redis.Server.RedisServer;

namespace StackExchange.Redis.Tests;

/// <summary>
/// The client half of the opt-in: what we send during handshake, and what we make of the answer. The server
/// half is <see cref="MaintenanceOptInServerTests"/>.
/// </summary>
[RunPerProtocol]
public class MaintenanceOptInClientTests(ITestOutputHelper log)
{
    private static InProcessTestServer CreateServer(ITestOutputHelper log) => new(log);

    private static ConfigurationOptions Config(InProcessTestServer server, MaintenanceNotificationMode mode)
    {
        var config = server.GetClientConfig(defaultOnly: true);
        config.MaintenanceNotifications = mode;
        return config;
    }

    private static bool IsActive(IConnectionMultiplexer conn, InProcessTestServer server)
        => ((IInternalConnectionMultiplexer)conn).GetServerEndPoint(server.DefaultEndPoint).MaintenanceNotificationsActive;

    private static List<Server.RedisClient> OptedIn(InProcessTestServer server)
    {
        var found = new List<Server.RedisClient>();
        server.ForAllClients(c =>
        {
            if (c.MaintenanceNotifications) found.Add(c);
        });
        return found;
    }

    /// <summary>
    /// Asserts that <c>Enabled</c> refused: either the connect threw, or it returned a connection that does
    /// not stay usable.
    /// </summary>
    /// <remarks>
    /// Both outcomes are the same refusal, and which one a caller sees is a race: the reconcile records the
    /// failure from the handshake-completion path, which can land either side of <c>ConnectAsync</c> deciding
    /// it has a connection. Asserting only the throw made this flaky on a two-core runner, and asserting the
    /// throw is not the point - not being left with a working connection is.
    /// </remarks>
    private static async Task AssertRefusedAsync(ConfigurationOptions config, ITestOutputHelper log)
    {
        ConnectionMultiplexer? conn = null;
        try
        {
            conn = await ConnectionMultiplexer.ConnectAsync(config);
        }
        catch (RedisConnectionException ex)
        {
            log.WriteLine($"refused at connect: {ex.Message}");
            return;
        }

        await using (conn)
        {
            var endpoint = conn.GetEndPoints().Single();
            var unusable = await Poll.UntilAsync(() => !conn.GetServer(endpoint).IsConnected);
            log.WriteLine($"connected, then unusable: {unusable}");
            Assert.True(unusable, "the connection should not have stayed usable");
        }
    }

    [Fact]
    public async Task HandshakeOptsInWhenAuto()
    {
        using var server = CreateServer(log);
        await using var conn = await ConnectionMultiplexer.ConnectAsync(Config(server, MaintenanceNotificationMode.Auto));

        var resp3 = TestContext.Current.IsResp3();
        log.WriteLine($"protocol: {TestContext.Current.GetProtocol()}, opted in: {OptedIn(server).Count}");

        // RESP3-only by design: under RESP2 the server could accept the request and then never deliver
        // anything, so we don't ask - and under Auto that is simply the feature being off
        Assert.Equal(resp3, IsActive(conn, server));
        Assert.Equal(resp3 ? 1 : 0, OptedIn(server).Count);

        if (resp3)
        {
            // a bare ON, so the server chooses; nothing here invents an endpoint type
            Assert.Null(Assert.Single(OptedIn(server)).MovingEndpointType);
        }

        // ...and the connection is entirely usable either way
        Assert.Equal("value", await Set(conn));
    }

    [Fact]
    public async Task DisabledSendsNothing()
    {
        using var server = CreateServer(log);
        await using var conn = await ConnectionMultiplexer.ConnectAsync(Config(server, MaintenanceNotificationMode.Disabled));

        Assert.Empty(OptedIn(server));
        Assert.False(IsActive(conn, server));
    }

    [Fact]
    public async Task DefaultIsOff()
    {
        // the library default has to be Disabled: OSS Redis, Valkey and Garnet don't know the subcommand, and
        // an unsolicited error reply on every connection would be a poor first impression
        using var server = CreateServer(log);
        var config = server.GetClientConfig(defaultOnly: true); // untouched, so whatever the provider says
        await using var conn = await ConnectionMultiplexer.ConnectAsync(config);

        Assert.Equal(MaintenanceNotificationMode.Disabled, config.MaintenanceNotifications);
        Assert.Empty(OptedIn(server));
    }

    [Theory]
    [InlineData(MaintenanceNotificationSupport.UnknownSubcommand)]
    [InlineData(MaintenanceNotificationSupport.Disabled)]
    public async Task AutoToleratesAServerThatRefuses(MaintenanceNotificationSupport support)
    {
        // the whole point of Auto: ask, and carry on regardless. This is what lets the entire test suite run
        // with the opt-in on against servers that will never send a notification
        using var server = CreateServer(log);
        server.MaintenanceNotifications = support;

        await using var conn = await ConnectionMultiplexer.ConnectAsync(Config(server, MaintenanceNotificationMode.Auto));

        Assert.False(IsActive(conn, server));
        Assert.Empty(OptedIn(server));
        Assert.True(conn.GetServer(server.DefaultEndPoint).IsConnected);
        Assert.Equal("value", await Set(conn));
    }

    [Fact]
    public async Task EnabledFailsAgainstAServerThatRefuses()
    {
        // Enabled is a requirement, not a preference: a connection that silently won't deliver notifications
        // is not the connection that was asked for
        Assert.SkipUnless(TestContext.Current.IsResp3(), "the opt-in is only sent under RESP3");

        using var server = CreateServer(log);
        server.MaintenanceNotifications = MaintenanceNotificationSupport.UnknownSubcommand;

        var config = Config(server, MaintenanceNotificationMode.Enabled);
        config.AbortOnConnectFail = true;
        config.ConnectRetry = 1;

        await AssertRefusedAsync(config, log);
    }

    [Fact]
    public async Task AutoIsOffWhenTheServerDowngradesToResp2()
    {
        // the case that makes this RESP3-only: the server accepts the opt-in and then answers HELLO as RESP2,
        // so nothing would ever arrive. We asked, the server said OK, and we still treat it as off
        using var server = CreateServer(log);
        server.MaxProtocolVersion = RedisProtocol.Resp2;
        var config = Config(server, MaintenanceNotificationMode.Auto);
        config.Protocol = RedisProtocol.Resp3;

        await using var conn = await ConnectionMultiplexer.ConnectAsync(config);

        Assert.Single(OptedIn(server)); // the server took it...
        Assert.False(IsActive(conn, server)); // ...and we know better than to believe it
        Assert.Equal("value", await Set(conn));
    }

    [Fact]
    public async Task EnabledFailsWhenTheServerDowngradesToResp2()
    {
        using var server = CreateServer(log);
        server.MaxProtocolVersion = RedisProtocol.Resp2;
        var config = Config(server, MaintenanceNotificationMode.Enabled);
        config.Protocol = RedisProtocol.Resp3;
        config.AbortOnConnectFail = true;
        config.ConnectRetry = 1;

        await AssertRefusedAsync(config, log);
    }

    [Fact]
    public async Task EnabledFailsWhenResp2WasOurOwnChoice()
    {
        // no exemption for a contradiction we configured ourselves: requiring a RESP3-only feature over a
        // RESP2 connection cannot be honoured, and half-honouring it silently is what Enabled exists to avoid
        using var server = CreateServer(log);
        var config = Config(server, MaintenanceNotificationMode.Enabled);
        config.Protocol = RedisProtocol.Resp2;
        config.AbortOnConnectFail = true;
        config.ConnectRetry = 1;

        await AssertRefusedAsync(config, log);
    }

    [Fact]
    public async Task AutoIsHappyOnResp2()
    {
        // the counterpart: Auto is best-effort, so an explicit RESP2 just means the feature is off
        using var server = CreateServer(log);
        var config = Config(server, MaintenanceNotificationMode.Auto);
        config.Protocol = RedisProtocol.Resp2;

        await using var conn = await ConnectionMultiplexer.ConnectAsync(config);

        Assert.Empty(OptedIn(server));
        Assert.False(IsActive(conn, server));
        Assert.Equal("value", await Set(conn));
    }

    /// <summary>
    /// Captures log messages so a test can assert on what an operator would actually see.
    /// </summary>
    private sealed class CapturingLoggerFactory : ILoggerFactory, ILogger
    {
        public List<string> Messages { get; } = [];

        public ILogger CreateLogger(string categoryName) => this;

        public void AddProvider(ILoggerProvider provider) { }

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            lock (Messages) Messages.Add(formatter(state, exception));
        }

        public string All
        {
            get { lock (Messages) return string.Join("\n", Messages); }
        }

        public void Dispose() { }
    }

    [Theory]
    [InlineData(MaintenanceNotificationSupport.Supported, "Maintenance notifications accepted")]
    [InlineData(MaintenanceNotificationSupport.Disabled, "Maintenance notifications refused")]
    public async Task TheLogSaysWhetherTheFeatureIsLive(MaintenanceNotificationSupport support, string expected)
    {
        // This is the diagnostic docs/ServerMaintenanceEvent.md tells people to use, so it is worth a test.
        // It also guards a mistake that was live for a while: the refusal was reported via
        // PhysicalConnection.OnDetailLog, which is [Conditional("PARSE_DETAIL")] and compiles away in any normal
        // build - so the reason a server declined was invisible to everybody who was not debugging the parser.
        Assert.SkipUnless(TestContext.Current.IsResp3(), "the opt-in is only sent under RESP3");

        using var server = CreateServer(log);
        server.MaintenanceNotifications = support;

        var captured = new CapturingLoggerFactory();
        var config = Config(server, MaintenanceNotificationMode.Auto);
        config.LoggerFactory = captured;

        await using var conn = await ConnectionMultiplexer.ConnectAsync(config);

        log.WriteLine(captured.All);
        Assert.Contains(expected, captured.All);
    }

    [Theory]
    [InlineData(MaintenanceEndpointType.ServerDefault, null)]
    [InlineData(MaintenanceEndpointType.InternalIp, "internal-ip")]
    [InlineData(MaintenanceEndpointType.InternalFqdn, "internal-fqdn")]
    [InlineData(MaintenanceEndpointType.ExternalIp, "external-ip")]
    [InlineData(MaintenanceEndpointType.ExternalFqdn, "external-fqdn")]
    [InlineData(MaintenanceEndpointType.None, "none")]
    public async Task MovingEndpointTypeIsSentWhenAskedFor(MaintenanceEndpointType type, string? expected)
    {
        // The point of asking: every MOVING observed on a real deployment carried no address, and every one of
        // those was requested with a bare ON - so the working theory is that the server default amounts to
        // "none". ServerDefault keeps that behaviour (send nothing); anything else says so explicitly.
        Assert.SkipUnless(TestContext.Current.IsResp3(), "the opt-in is only sent under RESP3");

        using var server = CreateServer(log);
        var config = Config(server, MaintenanceNotificationMode.Enabled);
        config.MaintenanceMovingEndpointType = type;

        await using var conn = await ConnectionMultiplexer.ConnectAsync(config);
        Assert.True(IsActive(conn, server), "the server should have accepted the opt-in");

        var client = Assert.Single(OptedIn(server));
        log.WriteLine($"{type} -> moving-endpoint-type: {client.MovingEndpointType ?? "(none sent)"}");
        Assert.Equal(expected, client.MovingEndpointType);
    }

    [Fact]
    public async Task UnsupportedMovingEndpointTypeIsRefusedNotFatal()
    {
        // A server that does not know a type answers with an error, and that is a refusal like any other: with
        // Auto we carry on without the feature rather than failing the connection.
        Assert.SkipUnless(TestContext.Current.IsResp3(), "the opt-in is only sent under RESP3");

        using var server = CreateServer(log);
        server.SupportedMovingEndpointTypes = ["external-fqdn"]; // this deployment offers one form only

        var config = Config(server, MaintenanceNotificationMode.Auto);
        config.MaintenanceMovingEndpointType = MaintenanceEndpointType.InternalIp;

        await using var conn = await ConnectionMultiplexer.ConnectAsync(config);
        Assert.False(IsActive(conn, server), "an unsupported endpoint type is a refusal");
        Assert.Equal("value", await Set(conn)); // ...and the connection is still perfectly usable
    }

    [Fact]
    public async Task OptInIsReArmedOnReconnect()
    {
        // per-connection state, so a reconnect that didn't re-send it would leave us silently unsubscribed
        Assert.SkipUnless(TestContext.Current.IsResp3(), "the opt-in is only sent under RESP3");

        using var server = CreateServer(log);
        var config = Config(server, MaintenanceNotificationMode.Auto);
        config.AllowSimulateConnectionFailure = true;
        await using var conn = await ConnectionMultiplexer.ConnectAsync(config);
        Assert.True(IsActive(conn, server));

        var before = server.TotalMaintenanceOptIns;
        conn.GetServer(server.DefaultEndPoint).SimulateConnectionFailure(SimulatedFailureType.All);
        await UntilCondition(() => server.TotalMaintenanceOptIns > before);

        log.WriteLine($"opt-ins: {before} -> {server.TotalMaintenanceOptIns}");
        Assert.True(server.TotalMaintenanceOptIns > before, "the opt-in should be sent again on the new connection");

        // ...and then wait for *our* side of it. The count above is the server having processed the opt-in,
        // whereas IsActive is us having processed its reply - a beat later, so asserting it directly is a race
        // that only shows up when the runner is starved of cores.
        await UntilCondition(() => IsActive(conn, server));
        Assert.True(IsActive(conn, server), "the feature should be live again on the replacement connection");
    }

    private static async Task UntilCondition(System.Func<bool> condition, int timeoutMilliseconds = 5000)
    {
        for (int i = 0; i < timeoutMilliseconds / 50 && !condition(); i++)
        {
            await Task.Delay(50);
        }
    }

    private static async Task<string?> Set(IConnectionMultiplexer conn)
    {
        var db = conn.GetDatabase();
        await db.StringSetAsync("maint-optin", "value");
        return await db.StringGetAsync("maint-optin");
    }
}
