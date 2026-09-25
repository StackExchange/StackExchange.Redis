using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace StackExchange.Redis.Tests;

/// <summary>
/// When the new core opens a second socket for deliveries - and, mostly, when it does not.
/// </summary>
/// <remarks>
/// <para>
/// <b>Pay per play.</b> Deployments actively reduce their server socket count, disabling things like the
/// legacy notification channel to do it, so a subscription socket that appears because a connection
/// exists rather than because somebody subscribed spends exactly the resource that effort protects.
/// </para>
/// <para>
/// <b>And RESP2 only.</b> Under RESP3 a delivery is a push frame on the connection that is already
/// there; a second socket would buy nothing. The protocol is not knowable until the HELLO reply, so this
/// is a decision made after connecting, never from configuration alone.
/// </para>
/// </remarks>
[RunPerProtocol]
public class RespSubscriptionConnectionTests(ITestOutputHelper output, SharedConnectionFixture fixture) : TestBase(output, fixture)
{
    private static RespNewCore CoreFor(IConnectionMultiplexer conn) => RespNewCoreFixture.CoreFor(conn);

    /// <summary>Nothing subscribes, so nothing opens a socket for subscribing.</summary>
    [Fact]
    public async Task NoSubscriptionMeansNoSecondSocket()
    {
        await using var conn = Create(shared: false);
        var core = CoreFor(conn);

        var db = RespNewCoreFixture.Wrap(conn, -1, null);
        await db.PingAsync(); // an ordinary connection exists and has handshaken

        Assert.Equal(0, core.SubscriptionConnectionCount);
    }

    /// <summary>
    /// Asking for the delivery endpoint gives one - but under RESP3 it is the connection already open,
    /// not a new one.
    /// </summary>
    [Fact]
    public async Task Resp3DeliversOnTheConnectionItAlreadyHas()
    {
        await using var conn = Create(shared: false);
        var core = CoreFor(conn);

        var db = RespNewCoreFixture.Wrap(conn, -1, null);
        await db.PingAsync(); // handshake completes, so the protocol is KNOWN rather than assumed

        var endpoint = conn.GetEndPoints()[0];
        var subscription = core.SubscriptionEndpoint(endpoint);

        if (TestContext.Current.GetProtocol() == RedisProtocol.Resp3)
        {
            Assert.Same(core.InteractiveEndpoint(endpoint), subscription);
            Assert.Equal(0, core.SubscriptionConnectionCount);
        }
        else
        {
            // RESP2: a socket of its own, and only now that one was asked for
            Assert.NotSame(core.InteractiveEndpoint(endpoint), subscription);
            Assert.Equal(1, core.SubscriptionConnectionCount);
        }
    }

    /// <summary>
    /// The whole point of the machinery: a subscription made on the new core receives a real message from
    /// a real server.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The handler is registered without sending anything.</b> Subscribing through <c>ISubscriber</c>
    /// would put a second subscription on the shipped connection, and the delivery being asserted could
    /// then have come from either - the test would pass with the new core's socket doing nothing at all.
    /// Registering in the registry directly leaves exactly one subscription on the wire: this one.
    /// </para>
    /// <para>
    /// So this covers the lot end to end: the socket (its own under RESP2, the existing one under RESP3),
    /// subscriber mode, the delivery arriving as an array or a push depending on protocol, the dispatch,
    /// and the registry lookup that finds the handler.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task ASubscriptionOnTheNewCoreReceivesDeliveries()
    {
        await using var conn = Create(shared: false);
        var core = CoreFor(conn);
        var muxer = TestMultiplexer.Unwrap(conn);

        var db = RespNewCoreFixture.Wrap(conn, -1, null);
        await db.PingAsync(); // the protocol is known rather than assumed, so the socket decision is real

        // Unique per RUN, not merely per test: the assertion below is that exactly one subscriber exists,
        // and the server is shared and long-lived, so a subscription left behind by an earlier run of this
        // same test answers the publish too. A deterministic name collides with its own history - which is
        // why the pub/sub tests here have always appended a guid.
        var channel = RedisChannel.Literal($"{Me()}-{TestContext.Current.GetProtocol()}-{Guid.NewGuid():N}");
        var delivered = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        var subscription = muxer.GetOrAddSubscription(channel, CommandFlags.None);
        subscription.Add((_, payload) => delivered.TrySetResult((string?)payload ?? ""), null);

        var endpoint = conn.GetEndPoints()[0];

        // Tell the registry this subscription already has a server. Without it the shipped heartbeat sees
        // an entry nobody is connected for and helpfully subscribes it on ITS connection, so the publish
        // below reports two receivers and the assertion that exactly one exists - the whole proof that the
        // delivery came from the new core - fails for a reason that has nothing to do with the new core.
        subscription.AddEndpoint(((IInternalConnectionMultiplexer)conn).GetServerEndPoint(endpoint));
        await core.SubscriptionContext(endpoint).SendAsync($"{RedisCommand.SUBSCRIBE}{channel}");

        Assert.Equal(1, await conn.GetSubscriber().PublishAsync(channel, "delivered"));

        var done = await Task.WhenAny(delivered.Task, Task.Delay(TimeSpan.FromSeconds(10)));
        Assert.True(ReferenceEquals(done, delivered.Task), "nothing was delivered on the new core's connection");
        Assert.Equal("delivered", await delivered.Task);
    }

    /// <summary>Asking twice does not open twice.</summary>
    [Fact]
    public async Task TheDeliveryEndpointIsReused()
    {
        await using var conn = Create(shared: false);
        var core = CoreFor(conn);

        var db = RespNewCoreFixture.Wrap(conn, -1, null);
        await db.PingAsync();

        var endpoint = conn.GetEndPoints()[0];
        Assert.Same(core.SubscriptionEndpoint(endpoint), core.SubscriptionEndpoint(endpoint));
    }
}
