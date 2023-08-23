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

    [Theory]
    [InlineData(true, true)]
    [InlineData(true, false)]
    [InlineData(false, false)]
    [InlineData(false, true)]
    public async void GetNotification(bool listenToSelf, bool externalConnectionMakesChange)
    {
        bool expectNotification = listenToSelf || externalConnectionMakesChange;

        using var listen = Create(shared: false);
        using var send = externalConnectionMakesChange ? Create() : listen;

        int value = (new Random().Next() % 1024) + 1024, notifyCount = 0;

        var key = Me();
        var db = listen.GetDatabase();
        db.KeyDelete(key);
        db.StringSet(key, value);

        var options = listenToSelf ? ClientTrackingOptions.NotifyForOwnCommands : ClientTrackingOptions.None;
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
