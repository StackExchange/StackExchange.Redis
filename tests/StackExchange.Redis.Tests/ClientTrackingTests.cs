using System;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using Xunit.Abstractions;

namespace StackExchange.Redis.Tests;

/// <summary>
/// Tests for <see href="https://redis.io/commands/client-tracking/"/>.
/// </summary>
[Collection(SharedConnectionFixture.Key)]
public class ClientTrackingTests : TestBase
{
    public ClientTrackingTests(ITestOutputHelper output, SharedConnectionFixture fixture) : base(output, fixture) { }

    [Fact]
    public async Task UseFlagWithoutEnabling()
    {
        using var conn = Create(shared: false);
        var key = Me();
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await conn.GetDatabase().StringGetAsync(key, CommandFlags.ClientCaching)
        );
        Assert.Equal("The ClientCaching flag can only be used if EnableServerAssistedClientSideTracking has been called", ex.Message);
    }

    [Fact]
    public void CallEnableTwice()
    {
        using var conn = Create(shared: false);
        conn.EnableServerAssistedClientSideTracking(key => default);
        var ex = Assert.Throws<InvalidOperationException>(() => conn.EnableServerAssistedClientSideTracking(key => default));
        Assert.Equal("The EnableServerAssistedClientSideTracking method can be invoked once-only per multiplexer instance", ex.Message);
    }

    [Fact]
    public void UsePrefixesWithoutBroadcast()
    {
        using var conn = Create(shared: false);
        var ex = Assert.Throws<ArgumentException>(() => conn.EnableServerAssistedClientSideTracking(key => default, prefixes: new RedisKey[] { "abc" }));
        Assert.StartsWith("Prefixes can only be specified when ClientTrackingOptions.Broadcast is used", ex.Message);
        Assert.Equal("prefixes", ex.ParamName);
    }

    [Theory]
    [InlineData(ClientTrackingOptions.None)]
    [InlineData(ClientTrackingOptions.Broadcast)]
    [InlineData(ClientTrackingOptions.NotifyForOwnCommands)]
    [InlineData(ClientTrackingOptions.Broadcast | ClientTrackingOptions.NotifyForOwnCommands)]
    [InlineData(ClientTrackingOptions.ConcurrentInvalidation)]
    [InlineData(ClientTrackingOptions.ConcurrentInvalidation | ClientTrackingOptions.Broadcast)]
    [InlineData(ClientTrackingOptions.ConcurrentInvalidation | ClientTrackingOptions.NotifyForOwnCommands)]
    [InlineData(ClientTrackingOptions.ConcurrentInvalidation |  ClientTrackingOptions.Broadcast | ClientTrackingOptions.NotifyForOwnCommands)]
    public Task GetNotificationFromOwnConnection(ClientTrackingOptions options) => GetNotification(options, false);

    [Theory]
    [InlineData(ClientTrackingOptions.None)]
    [InlineData(ClientTrackingOptions.Broadcast)]
    [InlineData(ClientTrackingOptions.NotifyForOwnCommands)]
    [InlineData(ClientTrackingOptions.Broadcast | ClientTrackingOptions.NotifyForOwnCommands)]
    [InlineData(ClientTrackingOptions.ConcurrentInvalidation)]
    [InlineData(ClientTrackingOptions.ConcurrentInvalidation | ClientTrackingOptions.Broadcast)]
    [InlineData(ClientTrackingOptions.ConcurrentInvalidation | ClientTrackingOptions.NotifyForOwnCommands)]
    [InlineData(ClientTrackingOptions.ConcurrentInvalidation | ClientTrackingOptions.Broadcast | ClientTrackingOptions.NotifyForOwnCommands)]
    public Task GetNotificationFromExternalConnection(ClientTrackingOptions options) => GetNotification(options, true);

    private async Task GetNotification(ClientTrackingOptions options, bool externalConnectionMakesChange)
    {
        bool expectNotification = ((options & ClientTrackingOptions.NotifyForOwnCommands) != 0) || externalConnectionMakesChange;

        using var listen = Create(shared: false);
        using var send = externalConnectionMakesChange ? Create() : listen;

        int value = (new Random().Next() % 1024) + 1024, notifyCount = 0;

        var key = Me();
        var db = listen.GetDatabase();
        db.KeyDelete(key);
        db.StringSet(key, value);

        listen.EnableServerAssistedClientSideTracking(rkey =>
        {
            if (rkey == key) Interlocked.Increment(ref notifyCount);
            return default;
        }, options);

        Assert.Equal(value, db.StringGet(key, CommandFlags.ClientCaching));
        Assert.Equal(0, Volatile.Read(ref notifyCount));

        send.GetDatabase().StringIncrement(key, 5);
        await Task.Delay(100); // allow time for the magic to happen

        Assert.Equal(expectNotification ? 1 : 0, Volatile.Read(ref notifyCount));
        Assert.Equal(value + 5, db.StringGet(key, CommandFlags.ClientCaching));

    }
}
