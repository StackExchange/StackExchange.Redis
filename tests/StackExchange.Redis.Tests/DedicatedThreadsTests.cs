using System;
using System.Net;
using System.Threading.Tasks;
using Xunit;

namespace StackExchange.Redis.Tests;

/// <summary>
/// End-to-end coverage for the <c>DedicatedThreads</c> feature flag: that asking for it actually moves the
/// reader and writer off the thread-pool, and that pub/sub is left alone.
/// </summary>
/// <remarks>
/// <para>
/// <c>DedicatedThreadsUnitTests</c> pins the decision; this pins the consequence. They are worth having
/// separately, because the decision is a pure function and this needs a live connection to observe.
/// </para>
/// <para>
/// The flag is process-wide static and is read when a connection is *established*, so every test here builds
/// its own unshared connection inside the window where the flag is set, and the class is non-parallel: a
/// shared connection would have been made before the flag was, and would report the old answer.
/// </para>
/// </remarks>
[RunPerProtocol]
[Collection(NonParallelCollection.Name)]
public class DedicatedThreadsTests(ITestOutputHelper output) : TestBase(output)
{
    private const string Flag = "DedicatedThreads";

    /// <summary>Connect with the flag in a known state, and put it back afterwards.</summary>
    private async Task WithFlagAsync(bool enabled, Func<IInternalConnectionMultiplexer, EndPoint, Task> assert)
    {
        var wasSet = ConnectionMultiplexer.GetFeatureFlag(Flag);
        ConnectionMultiplexer.SetFeatureFlag(Flag, enabled);
        try
        {
            await using var conn = Create(shared: false);
            var endpoint = conn.GetEndPoints()[0];

            // the flag is consumed while the connection is being established, so make sure one exists before
            // asking anything about it - and touch pub/sub too, since under RESP2 that is a second connection
            // that is not created until it is needed
            var db = conn.GetDatabase();
            await db.PingAsync();
            await conn.GetSubscriber().PingAsync();

            await assert(conn, endpoint);
        }
        finally
        {
            ConnectionMultiplexer.SetFeatureFlag(Flag, wasSet);
        }
    }

    [Fact]
    public Task WithoutTheFlag_TheThreadPoolServicesTheConnection() => WithFlagAsync(false, (conn, endpoint) =>
    {
        Assert.False(conn.IsSyncReader(endpoint, ConnectionType.Interactive));
        Assert.False(conn.IsSyncWriter(endpoint, ConnectionType.Interactive));
        return Task.CompletedTask;
    });

    [Fact]
    public Task WithTheFlag_TheInteractiveConnectionOwnsItsThreads() => WithFlagAsync(true, (conn, endpoint) =>
    {
        Assert.True(conn.IsSyncReader(endpoint, ConnectionType.Interactive));
        Assert.True(conn.IsSyncWriter(endpoint, ConnectionType.Interactive));
        return Task.CompletedTask;
    });

    /// <summary>
    /// Pub/sub keeps using the thread-pool, which is the claim the docs make and the reason the flag's cost is
    /// quoted per node rather than per connection.
    /// </summary>
    /// <remarks>
    /// Only checkable under RESP2. Under RESP3 there *is* no separate subscription connection - the bridge
    /// lookup returns the interactive one - so the question does not arise, and asserting "false" there would
    /// be asserting against the shared connection we just required to be true.
    /// </remarks>
    [Fact]
    public Task WithTheFlag_PubSubStaysOnTheThreadPool() => WithFlagAsync(true, (conn, endpoint) =>
    {
        var protocol = conn.GetServerEndPoint(endpoint).Protocol ?? RedisProtocol.Resp2;
        if (protocol >= RedisProtocol.Resp3)
        {
            // one connection carries both, so the subscription lookup is the interactive connection
            Assert.True(conn.IsSyncReader(endpoint, ConnectionType.Subscription));
            Assert.True(conn.IsSyncWriter(endpoint, ConnectionType.Subscription));
        }
        else
        {
            Assert.False(conn.IsSyncReader(endpoint, ConnectionType.Subscription));
            Assert.False(conn.IsSyncWriter(endpoint, ConnectionType.Subscription));

            // ...and the interactive one is still dedicated, so this is a difference between the two
            // connections rather than the flag having failed to apply at all
            Assert.True(conn.IsSyncReader(endpoint, ConnectionType.Interactive));
        }

        return Task.CompletedTask;
    });
}
