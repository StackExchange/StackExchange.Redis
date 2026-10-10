using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace StackExchange.Redis.Tests;

[RunPerProtocol]

public class ClientKillTests(ITestOutputHelper output) : TestBase(output)
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ClientKill(bool sharedSubscriptionConnection)
    {
        SetExpectedAmbientFailureCount(-1);
        await using var otherConnection = Create(
            allowAdmin: true,
            shared: false,
            backlogPolicy: BacklogPolicy.FailFast,
            require: RedisFeatures.v7_4_0_rc1,
            configuration: sharedSubscriptionConnection ? GetConfiguration() + ",sharedSubscriptionConnection=true" : null);
        var id = otherConnection.GetDatabase().Execute(RedisCommand.CLIENT.ToString(), RedisLiterals.ID);

        await using var conn = Create(allowAdmin: true, shared: false, backlogPolicy: BacklogPolicy.FailFast);
        var server = conn.GetServer(conn.GetEndPoints()[0]);

        // when RESP3 shares the interactive connection with pub/sub, it carries the configuration-channel
        // subscription - and the server counts a subscribed client as pubsub, not normal (which is exactly
        // why sharing is opt-in: pubsub clients get much tighter output-buffer limits)
        var protocol = TestContext.Current.GetProtocol();
        var client = server.ClientList().Single(x => x.Id == id.AsInt64());
        Assert.Equal(protocol, client.Protocol);
        var expectedType = protocol == RedisProtocol.Resp3 && sharedSubscriptionConnection ? ClientType.PubSub : ClientType.Normal;
        Assert.Equal(expectedType, client.ClientType);

        long result = server.ClientKill(id.AsInt64(), expectedType, null, true);
        Assert.Equal(1, result);
    }

    /// <summary>
    /// The server closing our connection is reported and recovered from, not merely survived.
    /// </summary>
    /// <remarks>
    /// A close we did not ask for - an output-buffer limit, <c>CLIENT KILL</c>, a restart - used to fail the commands
    /// in flight and stop there: no log, no <see cref="IConnectionMultiplexer.ConnectionFailed"/>, and no reconnect until
    /// a later command happened to try the dead connection. Anything that watches for failures (a multi-group's
    /// failover among them) never heard about it. The command in flight should fail as v3 failed it, with a
    /// <see cref="RedisConnectionException"/> saying the socket closed.
    /// </remarks>
    [Fact]
    public async Task AConnectionTheServerClosesIsReportedAndRecovered()
    {
        SetExpectedAmbientFailureCount(-1);
        await using var conn = Create(allowAdmin: true, shared: false);
        var db = conn.GetDatabase();
        var id = (long)await db.ExecuteAsync("CLIENT", "ID");

        var failed = new TaskCompletionSource<ConnectionFailedEventArgs>(TaskCreationOptions.RunContinuationsAsynchronously);
        conn.ConnectionFailed += (_, e) =>
        {
            if (e.ConnectionType == ConnectionType.Interactive) failed.TrySetResult(e);
        };

        // a command the server is still holding when it closes the connection
        var key = Me();
        await db.KeyDeleteAsync(key);
        var inFlight = db.ExecuteAsync("BLPOP", key, 10);

        await using var killer = Create(allowAdmin: true, shared: false);
        Assert.Equal(1, (long)await killer.GetDatabase().ExecuteAsync("CLIENT", "KILL", "ID", id));

        var ex = await Assert.ThrowsAsync<RedisConnectionException>(() => inFlight);
        Assert.Equal(ConnectionFailureType.SocketClosed, ex.FailureType);

        var report = await failed.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(ConnectionFailureType.SocketClosed, report.FailureType);

        // and it comes back by itself: the next command is served on a new connection
        await db.StringSetAsync(key, "after");
        Assert.Equal("after", await db.StringGetAsync(key));
        Assert.NotEqual(id, (long)await db.ExecuteAsync("CLIENT", "ID"));
    }

    [Fact]
    public async Task ClientKillWithMaxAge()
    {
        SetExpectedAmbientFailureCount(-1);
        await using var otherConnection = Create(allowAdmin: true, shared: false, backlogPolicy: BacklogPolicy.FailFast, require: RedisFeatures.v7_4_0_rc1);
        var id = otherConnection.GetDatabase().Execute(RedisCommand.CLIENT.ToString(), RedisLiterals.ID);
        await Task.Delay(1000);

        await using var conn = Create(allowAdmin: true, shared: false, backlogPolicy: BacklogPolicy.FailFast);
        var server = conn.GetServer(conn.GetEndPoints()[0]);
        var filter = new ClientKillFilter().WithId(id.AsInt64()).WithMaxAgeInSeconds(1).WithSkipMe(true);
        long result = server.ClientKill(filter, CommandFlags.DemandMaster);
        Assert.Equal(1, result);
    }

    [Fact]
    public void TestClientKillMessageWithAllArguments()
    {
        long id = 101;
        ClientType type = ClientType.Normal;
        string userName = "user1";
        EndPoint endpoint = new IPEndPoint(IPAddress.Parse("127.0.0.1"), 1234);
        EndPoint serverEndpoint = new IPEndPoint(IPAddress.Parse("198.0.0.1"), 6379);
        bool skipMe = true;
        long maxAge = 102;

        var filter = new ClientKillFilter().WithId(id).WithClientType(type).WithUsername(userName).WithEndpoint(endpoint).WithServerEndpoint(serverEndpoint).WithSkipMe(skipMe).WithMaxAgeInSeconds(maxAge);
        List<RedisValue> expected =
        [
            "KILL", "ID", "101", "TYPE", "normal", "USERNAME", "user1", "ADDR", "127.0.0.1:1234", "LADDR", "198.0.0.1:6379", "SKIPME", "yes", "MAXAGE", "102",
        ];
        Assert.Equal(expected, filter.ToList(true));
    }
}
