using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using StackExchange.Redis.Configuration;
using StackExchange.Redis.Server;
using Xunit;

namespace StackExchange.Redis.Tests;

/// <summary>
/// Re-reading the topology when an endpoint will not accept a connection at all.
/// </summary>
/// <remarks>
/// The gap: every other path that re-reads the topology needs somebody *else* to notice first - a maintenance
/// notification, a <c>MOVED</c> from a reachable node, a peer's configuration broadcast. A client with quiet
/// healthy connections and one endpoint that only ever refuses has nobody to tell it, and
/// <c>reconfigureNextFailure</c> is set only once a connection has been *established*, so a node that never
/// established could be retried indefinitely.
/// <para>
/// Not hypothetical: a customer's client dialled three endpoints that no longer existed for 37 hours across a
/// Redis Cloud node replacement, recovering only when something unrelated finally provoked a re-read.
/// </para>
/// </remarks>
[Collection(NonParallelCollection.Name)]
public class ConnectFailureRefreshTests(ITestOutputHelper log)
{
    /// <summary>Counts inbound <c>CLUSTER</c> commands, so a test can see a topology read happen.</summary>
    private sealed class CountingServer(ITestOutputHelper log) : InProcessTestServer(log)
    {
        private int _clusterCommands;

        public int ClusterCommands => Volatile.Read(ref _clusterCommands);

        public override TypedRedisValue Execute(RedisClient client, in RedisRequest request)
        {
            if (request.Count > 0 && string.Equals(request.GetString(0), "cluster", StringComparison.OrdinalIgnoreCase))
            {
                Interlocked.Increment(ref _clusterCommands);
            }

            return base.Execute(client, in request);
        }
    }

    private (CountingServer Server, ConfigurationOptions Config, EndPoint Doomed, CapturingLogger Logger) Arrange(int configCheckSeconds)
    {
        var server = new CountingServer(log) { ServerType = ServerType.Cluster };
        var doomed = BlackHoleTunnel.GetRefusingEndPoint();

        // a real member of the topology - it holds a slot, and CLUSTER SLOTS advertises it - that simply
        // cannot be reached; the client learns about it from the healthy node and then dials it forever
        server.AddEmptyNode(doomed);
        server.Migrate((RedisKey)"leaving", doomed);

        var config = server.GetClientConfig(defaultOnly: true);
        config.Protocol = RedisProtocol.Resp3;
        config.AbortOnConnectFail = false;
        config.ConfigCheckSeconds = configCheckSeconds; // the refresh rate limit reuses this
        config.ConnectTimeout = 2000;
        config.ReconnectRetryPolicy = new LinearRetry(500); // else the default backoff makes this a minutes-long test
        var tunnel = new BlackHoleTunnel(server.Tunnel);
        tunnel.BlackHole(doomed); // refused from the outset: this endpoint never accepts a connection at all
        config.Tunnel = tunnel;
        var logger = new CapturingLogger();
        config.LoggerFactory = logger;
        return (server, config, doomed, logger);
    }

    [Fact]
    public async Task AnEndpointThatOnlyEverRefusesProvokesATopologyRead()
    {
        var (server, config, doomed, logger) = Arrange(configCheckSeconds: 5);
        using (server)
        {
            await using var conn = await ConnectionMultiplexer.ConnectAsync(config);
            Assert.True(
                await Poll.UntilAsync(() => conn.GetEndPoints().Contains(doomed), timeoutMilliseconds: 10_000),
                $"{doomed} was never discovered, so this test would prove nothing");

            var before = server.ClusterCommands;
            log.WriteLine($"cluster commands before: {before}");

            var refreshed = await Poll.UntilAsync(() => server.ClusterCommands > before, timeoutMilliseconds: 30_000);

            // dumped before the assertion, so a failure arrives with the evidence rather than just a verdict
            log.WriteLine($"cluster commands after: {server.ClusterCommands}");
            log.WriteLine(logger.All);
            Assert.True(refreshed, "repeated connect failures should have provoked a topology read");
            Assert.NotEmpty(logger.Matching("consecutive connect failures"));
        }
    }

    [Fact]
    public async Task TheReadIsRateLimitedRatherThanOncePerFailure()
    {
        // The restraint is the part that makes this safe to do at all. The gate it replaces exists to prevent a
        // stampede - a dead endpoint, times a retry loop, times every client in a fleet, each issuing a
        // topology read - so trading a stuck client for a thundering herd would be no improvement.
        const int WindowSeconds = 12, ConfigCheckSeconds = 5;
        var (server, config, doomed, logger) = Arrange(ConfigCheckSeconds);
        using (server)
        {
            await using var conn = await ConnectionMultiplexer.ConnectAsync(config);
            Assert.True(await Poll.UntilAsync(() => conn.GetEndPoints().Contains(doomed), timeoutMilliseconds: 10_000));

            await Task.Delay(TimeSpan.FromSeconds(WindowSeconds), TestContext.Current.CancellationToken);

            var attempts = logger.Matching("Resurrecting").Count;
            var refreshes = logger.Matching("consecutive connect failures").Count;
            log.WriteLine($"{attempts} connect attempts, {refreshes} topology reads in {WindowSeconds}s");

            // the ratio is the assertion: many failures, few reads. The bound is generous because the
            // heartbeat that drives both is only ~1s accurate, but it is nowhere near one-read-per-failure.
            Assert.True(attempts >= 5, $"expected the endpoint to be retried repeatedly, but saw {attempts} attempts");
            var permitted = (WindowSeconds / ConfigCheckSeconds) + 2;
            Assert.True(refreshes <= permitted, $"expected at most {permitted} rate-limited reads, but saw {refreshes}");
        }
    }

    private sealed class CapturingLogger : ILoggerFactory, ILogger
    {
        private readonly List<string> _messages = [];

        public ILogger CreateLogger(string categoryName) => this;

        public void AddProvider(ILoggerProvider provider) { }

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            lock (_messages) _messages.Add(formatter(state, exception));
        }

        public List<string> Matching(string fragment)
        {
            lock (_messages) return _messages.FindAll(x => x.Contains(fragment, StringComparison.Ordinal));
        }

        public string All
        {
            get { lock (_messages) return string.Join("\n", _messages); }
        }

        public void Dispose() { }
    }
}
