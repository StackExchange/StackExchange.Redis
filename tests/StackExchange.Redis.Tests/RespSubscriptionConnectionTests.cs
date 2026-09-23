using System.Linq;
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
