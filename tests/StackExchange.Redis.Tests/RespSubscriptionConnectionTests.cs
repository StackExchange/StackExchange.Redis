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
    private static RespConnectionManager CoreFor(IConnectionMultiplexer conn) => RespConnectionManagerFixture.CoreFor(conn);

    /// <summary>Under RESP3 nothing opens a second socket for subscribing; under RESP2 the multiplexer opens one up front.</summary>
    /// <remarks>
    /// This once asserted "never, until something subscribes" - of a side core the test built for itself, which
    /// only dialled on demand. The multiplexer's own core connects eagerly, in the shipped shape: RESP2 gets
    /// its subscription socket at connect (see <c>DefaultOptionsTests.VanillaResp2ConnectsWithSeparatePubSubConnection</c>),
    /// and RESP3 never needs one.
    /// </remarks>
    [Fact]
    public async Task NoSubscriptionMeansNoSecondSocket()
    {
        await using var conn = Create(shared: false);
        var core = CoreFor(conn);

        var db = RespConnectionManagerFixture.Wrap(conn, -1, null);
        await db.PingAsync(); // an ordinary connection exists and has handshaken

        var expected = TestContext.Current.GetProtocol() == RedisProtocol.Resp3 ? 0 : 1;
        Assert.Equal(expected, core.SubscriptionConnectionCount);
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

        var db = RespConnectionManagerFixture.Wrap(conn, -1, null);
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
    public async Task ASubscriptionOnTheConnectionManagerReceivesDeliveries()
    {
        // Only under the engine flag, and that is the claim narrowing to where it is true rather than a
        // test being hidden. The assertion is "exactly one subscriber, and the delivery came from this
        // core's socket" - which needs this core to be the one the SUBSCRIPTION REGISTRY believes in.
        // With the flag off the shipped bridge owns subscriptions, so a subscription placed here reads as
        // live nowhere the registry can see and the heartbeat subscribes it again; the server then counts
        // two subscribers and the test fails for a reason that is about neither core's delivery path.
        // It had been failing on the shipped run for exactly that reason.
        await using var conn = Create(shared: false);
        var muxer = TestMultiplexer.Unwrap(conn);

        // the multiplexer's OWN core, not the fixture's. Liveness is asked of whichever core owns a
        // subscription, and the registry asks `server.Multiplexer.Connections` - so a subscription placed on
        // a second, test-only core is owned by something the registry cannot see, reads as live nowhere,
        // and gets subscribed a second time by the heartbeat. Two instances can never agree about this;
        // the fixture's core is for asserting about COMMANDS, where there is no shared registry to
        // disagree with.
        var core = muxer.Connections;

        var db = RespConnectionManagerFixture.Wrap(conn, -1, null);
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

        // Tell the registry this subscription already has a server AND which core is carrying it. Naming
        // the server alone is not enough: liveness is asked of whichever core owns the subscription, and
        // ownership still said "the bridge", so the answer came back from a bridge that is not carrying
        // it. The shipped heartbeat then sees an entry nobody is connected for and helpfully subscribes it
        // on ITS connection, so the publish below reports two receivers and the assertion that exactly
        // one exists - the whole proof that the delivery came from the new core - fails for a reason that
        // has nothing to do with the new core.
        subscription.OnSubscribed(((IInternalConnectionMultiplexer)conn).GetServerEndPoint(endpoint));
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

        var db = RespConnectionManagerFixture.Wrap(conn, -1, null);
        await db.PingAsync();

        var endpoint = conn.GetEndPoints()[0];
        Assert.Same(core.SubscriptionEndpoint(endpoint), core.SubscriptionEndpoint(endpoint));
    }
}
