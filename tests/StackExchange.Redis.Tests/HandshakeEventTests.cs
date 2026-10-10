using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Xunit;

namespace StackExchange.Redis.Tests;

/// <summary>
/// The connect log keeps v3's events, under v3's ids, wherever v4 does the equivalent thing.
/// </summary>
/// <remarks>
/// These were raised by the old core's types - <c>ServerEndPoint</c>'s handshake, <c>ResultProcessor</c>,
/// <c>PhysicalBridge</c> - and went with them when those were deleted. Event ids are filtered and alerted on,
/// so losing one silently is a breaking change for whoever was watching it.
/// </remarks>
public class HandshakeEventTests(ITestOutputHelper output) : TestBase(output)
{
    [Theory]
    [InlineData(RedisProtocol.Resp2)]
    [InlineData(RedisProtocol.Resp3)]
    public async Task AConnectRaisesTheHandshakeEvents(RedisProtocol protocol)
    {
        var events = new EventIdLogger();
        var options = ConfigurationOptions.Parse(TestConfig.Current.PrimaryServerAndPort);
        options.Protocol = protocol;
        options.LoggerFactory = events;

        await using var conn = await ConnectionMultiplexer.ConnectAsync(options);
        Assert.True(conn.IsConnected);

        Assert.True(events.Saw(63), "63: server handshake");
        Assert.True(events.Saw(69), "69: sending critical tracer");
        Assert.True(events.Saw(70), "70: flushing outbound buffer");
        Assert.True(events.Saw(71), "71: per-reply response");
        Assert.True(events.Saw(84), "84: auto-configured (HELLO) connection-id");
    }

    [Fact]
    public async Task AuthenticatingIsLogged()
    {
        var events = new EventIdLogger();
        var options = ConfigurationOptions.Parse(TestConfig.Current.SecureServerAndPort);
        options.Password = TestConfig.Current.SecurePassword;
        options.LoggerFactory = events;

        await using var conn = await ConnectionMultiplexer.ConnectAsync(options);
        Assert.SkipUnless(conn.IsConnected, "secure server unavailable");

        Assert.True(events.Saw(65) || events.Saw(66) || events.Saw(64), "64/65/66: authenticating");
    }

    private sealed class EventIdLogger : ILoggerFactory, ILogger
    {
        private readonly HashSet<int> _seen = [];

        public bool Saw(int id)
        {
            lock (_seen) return _seen.Contains(id);
        }

        public ILogger CreateLogger(string categoryName) => this;
        public void AddProvider(ILoggerProvider provider) { }
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Dispose() { }

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            lock (_seen) _seen.Add(eventId.Id);
        }
    }
}
