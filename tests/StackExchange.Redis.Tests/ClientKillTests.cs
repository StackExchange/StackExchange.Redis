using System.Collections.Generic;
using System.Net;
using System.Threading;
using Xunit;
using Xunit.Abstractions;

namespace StackExchange.Redis.Tests;

[RunPerProtocol]

public class ClientKillTests : TestBase
{
    protected override string GetConfiguration() => TestConfig.Current.PrimaryServerAndPort;
    public ClientKillTests(ITestOutputHelper output) : base(output) { }

    [Fact]
    public void ClientKill()
    {
        var db = Create(require: RedisFeatures.v7_4_0_rc1).GetDatabase();

        SetExpectedAmbientFailureCount(-1);
        using var otherConnection = Create(allowAdmin: true, shared: false, backlogPolicy: BacklogPolicy.FailFast);
        var id = otherConnection.GetDatabase().Execute(RedisCommand.CLIENT.ToString(), RedisLiterals.ID);

        using var conn = Create(allowAdmin: true, shared: false, backlogPolicy: BacklogPolicy.FailFast);
        var server = conn.GetServer(conn.GetEndPoints()[0]);
        long result = server.ClientKill(id.AsInt64(), ClientType.Normal, null, true);
        Assert.Equal(1, result);
    }

    [Fact]
    public void ClientKillWithMaxAge()
    {
        var db = Create(require: RedisFeatures.v7_4_0_rc1).GetDatabase();

        SetExpectedAmbientFailureCount(-1);
        using var otherConnection = Create(allowAdmin: true, shared: false, backlogPolicy: BacklogPolicy.FailFast);
        var id = otherConnection.GetDatabase().Execute(RedisCommand.CLIENT.ToString(), RedisLiterals.ID);
        Thread.Sleep(1000);

        using var conn = Create(allowAdmin: true, shared: false, backlogPolicy: BacklogPolicy.FailFast);
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
        List<RedisValue> expected = new List<RedisValue>()
        {
            "KILL", "ID", "101", "TYPE", "normal", "USERNAME", "user1", "ADDR", "127.0.0.1:1234", "LADDR", "198.0.0.1:6379", "SKIPME", "yes", "MAXAGE", "102",
        };
        Assert.Equal(expected, filter.ToList(true));
    }
}
