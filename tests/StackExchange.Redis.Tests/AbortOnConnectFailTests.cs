using System;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using Xunit.Abstractions;

namespace StackExchange.Redis.Tests;

public class AbortOnConnectFailTests : TestBase
{
    public AbortOnConnectFailTests(ITestOutputHelper output) : base (output) { }

    [Fact]
    public void NeverEverConnectedNoBacklogThrowsConnectionNotAvailableSync()
    {
        using var conn = GetFailFastConn();
        var db = conn.GetDatabase();
        var key = Me();

        // No connection is active/available to service this operation: GET 6.0.14AbortOnConnectFailTests-NeverEverConnectedThrowsConnectionNotAvailable; UnableToConnect on doesnot.exist.0d034c26350e4ee199d6c5f385a073f7:6379/Interactive, Initializing/NotStarted, last: NONE, origin: BeginConnectAsync, outstanding: 0, last-read: 0s ago, last-write: 0s ago, keep-alive: 500s, state: Connecting, mgr: 10 of 10 available, last-heartbeat: never, global: 0s ago, v: 2.6.99.22667, mc: 1/1/0, mgr: 10 of 10 available, clientName: NAMISTOU-3(SE.Redis-v2.6.99.22667), IOCP: (Busy=0,Free=1000,Min=32,Max=1000), WORKER: (Busy=2,Free=32765,Min=32,Max=32767), POOL: (Threads=17,QueuedItems=0,CompletedItems=20), v: 2.6.99.22667
        var ex = Assert.Throws<RedisConnectionException>(() => db.StringGet(key));
        Log("Exception: " + ex.Message);
        Assert.Contains("No connection is active/available to service this operation", ex.Message);
    }

    [Fact]
    public async Task NeverEverConnectedNoBacklogThrowsConnectionNotAvailableAsync()
    {
        using var conn = GetFailFastConn();
        var db = conn.GetDatabase();
        var key = Me();

        // No connection is active/available to service this operation: GET 6.0.14AbortOnConnectFailTests-NeverEverConnectedThrowsConnectionNotAvailable; UnableToConnect on doesnot.exist.0d034c26350e4ee199d6c5f385a073f7:6379/Interactive, Initializing/NotStarted, last: NONE, origin: BeginConnectAsync, outstanding: 0, last-read: 0s ago, last-write: 0s ago, keep-alive: 500s, state: Connecting, mgr: 10 of 10 available, last-heartbeat: never, global: 0s ago, v: 2.6.99.22667, mc: 1/1/0, mgr: 10 of 10 available, clientName: NAMISTOU-3(SE.Redis-v2.6.99.22667), IOCP: (Busy=0,Free=1000,Min=32,Max=1000), WORKER: (Busy=2,Free=32765,Min=32,Max=32767), POOL: (Threads=17,QueuedItems=0,CompletedItems=20), v: 2.6.99.22667
        var ex = await Assert.ThrowsAsync<RedisConnectionException>(() => db.StringGetAsync(key));
        Log("Exception: " + ex.Message);
        Assert.Contains("No connection is active/available to service this operation", ex.Message);
    }

    [Fact]
    public void DisconnectAndReconnectThrowsConnectionExceptionSync()
    {
        using var conn = GetWorkingBacklogConn();

        var db = conn.GetDatabase();
        var key = Me();
        _ = db.Ping(); // Doesn't throw - we're connected

        // Disconnect and don't allow re-connection
        conn.AllowConnect = false;
        var server = conn.GetServerSnapshot()[0];
        server.SimulateConnectionFailure(SimulatedFailureType.All);

        // Exception: The message timed out in the backlog attempting to send because no connection became available- Last Connection Exception: InternalFailure on 127.0.0.1:6379/Interactive, Initializing/NotStarted, last: GET, origin: ConnectedAsync, outstanding: 0, last-read: 0s ago, last-write: 0s ago, keep-alive: 500s, state: Connecting, mgr: 10 of 10 available, last-heartbeat: never, last-mbeat: 0s ago, global: 0s ago, v: 2.6.99.22667, command=PING, inst: 0, qu: 0, qs: 0, aw: False, bw: CheckingForTimeout, last-in: 0, cur-in: 0, sync-ops: 1, async-ops: 1, serverEndpoint: 127.0.0.1:6379, conn-sec: n/a, aoc: 0, mc: 1/1/0, mgr: 10 of 10 available, clientName: NAMISTOU-3(SE.Redis-v2.6.99.22667), IOCP: (Busy=0,Free=1000,Min=32,Max=1000), WORKER: (Busy=2,Free=32765,Min=32,Max=32767), POOL: (Threads=18,QueuedItems=0,CompletedItems=65), v: 2.6.99.22667 (Please take a look at this article for some common client-side issues that can cause timeouts: https://stackexchange.github.io/StackExchange.Redis/Timeouts)
        var ex = Assert.ThrowsAny<Exception>(() => db.Ping());
        Log("Exception: " + ex.Message);
        Assert.True(ex is RedisConnectionException or RedisTimeoutException);
        Assert.StartsWith("The message timed out in the backlog attempting to send because no connection became available - Last Connection Exception: ", ex.Message);
        Assert.NotNull(ex.InnerException);
        var iex = Assert.IsType<RedisConnectionException>(ex.InnerException);
        Assert.Contains(iex.Message, ex.Message);
    }

    [Fact]
    public async Task DisconnectAndNoReconnectThrowsConnectionExceptionAsync()
    {
        using var conn = GetWorkingBacklogConn();

        var db = conn.GetDatabase();
        var key = Me();
        _ = db.Ping(); // Doesn't throw - we're connected

        // Disconnect and don't allow re-connection
        conn.AllowConnect = false;
        var server = conn.GetServerSnapshot()[0];
        server.SimulateConnectionFailure(SimulatedFailureType.All);

        // Exception: The message timed out in the backlog attempting to send because no connection became available- Last Connection Exception: InternalFailure on 127.0.0.1:6379/Interactive, Initializing/NotStarted, last: GET, origin: ConnectedAsync, outstanding: 0, last-read: 0s ago, last-write: 0s ago, keep-alive: 500s, state: Connecting, mgr: 10 of 10 available, last-heartbeat: never, last-mbeat: 0s ago, global: 0s ago, v: 2.6.99.22667, command=PING, inst: 0, qu: 0, qs: 0, aw: False, bw: CheckingForTimeout, last-in: 0, cur-in: 0, sync-ops: 1, async-ops: 1, serverEndpoint: 127.0.0.1:6379, conn-sec: n/a, aoc: 0, mc: 1/1/0, mgr: 10 of 10 available, clientName: NAMISTOU-3(SE.Redis-v2.6.99.22667), IOCP: (Busy=0,Free=1000,Min=32,Max=1000), WORKER: (Busy=2,Free=32765,Min=32,Max=32767), POOL: (Threads=18,QueuedItems=0,CompletedItems=65), v: 2.6.99.22667 (Please take a look at this article for some common client-side issues that can cause timeouts: https://stackexchange.github.io/StackExchange.Redis/Timeouts)
        var ex = await Assert.ThrowsAsync<RedisConnectionException>(() => db.PingAsync());
        Log("Exception: " + ex.Message);
        Assert.StartsWith("The message timed out in the backlog attempting to send because no connection became available - Last Connection Exception: ", ex.Message);
        Assert.NotNull(ex.InnerException);
        var iex = Assert.IsType<RedisConnectionException>(ex.InnerException);
        Assert.Contains(iex.Message, ex.Message);
    }

    private ConnectionMultiplexer GetFailFastConn() =>
        ConnectionMultiplexer.Connect(GetOptions(BacklogPolicy.FailFast).Apply(o => o.EndPoints.Add($"doesnot.exist.{Guid.NewGuid():N}:6379")), Writer);

    private ConnectionMultiplexer GetWorkingBacklogConn() =>
        ConnectionMultiplexer.Connect(GetOptions(BacklogPolicy.Default).Apply(o => o.EndPoints.Add(GetConfiguration())), Writer);

    private ConfigurationOptions GetOptions(BacklogPolicy policy) => new ConfigurationOptions()
    {
        AbortOnConnectFail = false,
        BacklogPolicy = policy,
        ConnectTimeout = 50,
        SyncTimeout = 100,
        KeepAlive = 100,
        AllowAdmin = true,
    };

    //[Theory]
    //[InlineData(false, 0, 0, true, "Connection to Redis never succeeded (attempts: 0 - connection likely in-progress), unable to service operation: PING")]
    //[InlineData(false, 1, 0, true, "Connection to Redis never succeeded (attempts: 1 - connection likely in-progress), unable to service operation: PING")]
    //[InlineData(false, 12, 0, true, "Connection to Redis never succeeded (attempts: 12 - check your config), unable to service operation: PING")]
    //[InlineData(false, 0, 0, false, "Connection to Redis never succeeded (attempts: 0 - connection likely in-progress), unable to service operation: PING")]
    //[InlineData(false, 1, 0, false, "Connection to Redis never succeeded (attempts: 1 - connection likely in-progress), unable to service operation: PING")]
    //[InlineData(false, 12, 0, false, "Connection to Redis never succeeded (attempts: 12 - check your config), unable to service operation: PING")]
    //[InlineData(true, 0, 0, true, "No connection is active/available to service this operation: PING")]
    //[InlineData(true, 1, 0, true, "No connection is active/available to service this operation: PING")]
    //[InlineData(true, 12, 0, true, "No connection is active/available to service this operation: PING")]
    //public void NoConnectionException(bool abortOnConnect, int connCount, int completeCount, bool hasDetail, string messageStart)
    //{
    //    try
    //    {
    //        var options = new ConfigurationOptions()
    //        {
    //            AbortOnConnectFail = abortOnConnect,
    //            BacklogPolicy = BacklogPolicy.FailFast,
    //            ConnectTimeout = 1000,
    //            SyncTimeout = 500,
    //            KeepAlive = 5000
    //        };

    //        ConnectionMultiplexer conn;
    //        if (abortOnConnect)
    //        {
    //            options.EndPoints.Add(TestConfig.Current.PrimaryServerAndPort);
    //            conn = ConnectionMultiplexer.Connect(options, Writer);
    //        }
    //        else
    //        {
    //            options.EndPoints.Add($"doesnot.exist.{Guid.NewGuid():N}:6379");
    //            conn = ConnectionMultiplexer.Connect(options, Writer);
    //        }

    //        using (conn)
    //        {
    //            var server = conn.GetServer(conn.GetEndPoints()[0]);
    //            conn.AllowConnect = false;
    //            conn._connectAttemptCount = connCount;
    //            conn._connectCompletedCount = completeCount;
    //            options.IncludeDetailInExceptions = hasDetail;
    //            options.IncludePerformanceCountersInExceptions = hasDetail;

    //            var msg = Message.Create(-1, CommandFlags.None, RedisCommand.PING);
    //            var rawEx = ExceptionFactory.NoConnectionAvailable(conn, msg, new ServerEndPoint(conn, server.EndPoint));
    //            var ex = Assert.IsType<RedisConnectionException>(rawEx);
    //            Writer.WriteLine("Exception: " + ex.Message);

    //            // Example format: "Exception: No connection is active/available to service this operation: PING, inst: 0, qu: 0, qs: 0, aw: False, in: 0, in-pipe: 0, out-pipe: 0, last-in: 0, cur-in: 0, serverEndpoint: 127.0.0.1:6379, mc: 1/1/0, mgr: 10 of 10 available, clientName: NoConnectionException, IOCP: (Busy=0,Free=1000,Min=8,Max=1000), WORKER: (Busy=2,Free=2045,Min=8,Max=2047), Local-CPU: 100%, v: 2.1.0.5";
    //            Assert.StartsWith(messageStart, ex.Message);

    //            // Ensure our pipe numbers are in place if they should be
    //            if (hasDetail)
    //            {
    //                Assert.Contains("inst: 0, qu: 0, qs: 0, aw: False, bw: Inactive, in: 0, in-pipe: 0, out-pipe: 0, last-in: 0, cur-in: 0", ex.Message);
    //                Assert.Contains($"mc: {connCount}/{completeCount}/0", ex.Message);
    //                Assert.Contains("serverEndpoint: " + server.EndPoint.ToString()?.Replace("Unspecified/", ""), ex.Message);
    //            }
    //            else
    //            {
    //                Assert.DoesNotContain("inst: 0, qu: 0, qs: 0, aw: False, bw: Inactive, in: 0, in-pipe: 0, out-pipe: 0, last-in: 0, cur-in: 0", ex.Message);
    //                Assert.DoesNotContain($"mc: {connCount}/{completeCount}/0", ex.Message);
    //                Assert.DoesNotContain("serverEndpoint: " + server.EndPoint.ToString()?.Replace("Unspecified/", ""), ex.Message);
    //            }
    //            Assert.DoesNotContain("Unspecified/", ex.Message);
    //        }
    //    }
    //    finally
    //    {
    //        ClearAmbientFailures();
    //    }
    //}
}
