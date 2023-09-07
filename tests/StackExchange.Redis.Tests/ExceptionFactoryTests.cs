using System;
using Xunit;
using Xunit.Abstractions;

namespace StackExchange.Redis.Tests;

public class ExceptionFactoryTests : TestBase
{
    public ExceptionFactoryTests(ITestOutputHelper output) : base (output) { }

    [Fact]
    public void NullLastException()
    {
        using var conn = Create(keepAlive: 1, connectTimeout: 10000, allowAdmin: true);

        conn.GetDatabase();
        Assert.Null(conn.GetServerSnapshot()[0].LastException);
        var ex = ExceptionFactory.NoConnectionAvailable(conn.UnderlyingMultiplexer, null, null);
        Assert.Null(ex.InnerException);
    }

    [Fact]
    public void CanGetVersion()
    {
        var libVer = Utils.GetLibVersion();
        Assert.Matches(@"2\.[0-9]+\.[0-9]+(\.[0-9]+)?", libVer);
    }

#if DEBUG
    [Fact]
    public void MultipleEndpointsThrowConnectionException()
    {
        try
        {
            using var conn = Create(keepAlive: 1, connectTimeout: 10000, allowAdmin: true, shared: false);

            conn.GetDatabase();
            conn.AllowConnect = false;

            foreach (var endpoint in conn.GetEndPoints())
            {
                conn.GetServer(endpoint).SimulateConnectionFailure(SimulatedFailureType.All);
            }

            var ex = ExceptionFactory.NoConnectionAvailable(conn.UnderlyingMultiplexer, null, null);
            var outer = Assert.IsType<RedisConnectionException>(ex);
            Assert.Equal(ConnectionFailureType.UnableToResolvePhysicalConnection, outer.FailureType);
            var inner = Assert.IsType<RedisConnectionException>(outer.InnerException);
            Assert.True(inner.FailureType == ConnectionFailureType.SocketFailure
                     || inner.FailureType == ConnectionFailureType.InternalFailure);
        }
        finally
        {
            ClearAmbientFailures();
        }
    }
#endif

    [Fact]
    public void ServerTakesPrecendenceOverSnapshot()
    {
        try
        {
            using var conn = Create(keepAlive: 1, connectTimeout: 10000, allowAdmin: true, shared: false, backlogPolicy: BacklogPolicy.FailFast);

            conn.GetDatabase();
            conn.AllowConnect = false;

            conn.GetServer(conn.GetEndPoints()[0]).SimulateConnectionFailure(SimulatedFailureType.All);

            var ex = ExceptionFactory.NoConnectionAvailable(conn.UnderlyingMultiplexer, null, conn.GetServerSnapshot()[0]);
            Assert.IsType<RedisConnectionException>(ex);
            Assert.IsType<RedisConnectionException>(ex.InnerException);
            Assert.Equal(ex.InnerException, conn.GetServerSnapshot()[0].LastException);
        }
        finally
        {
            ClearAmbientFailures();
        }
    }

    [Fact]
    public void NullInnerExceptionForMultipleEndpointsWithNoLastException()
    {
        try
        {
            using var conn = Create(keepAlive: 1, connectTimeout: 10000, allowAdmin: true);

            conn.GetDatabase();
            conn.AllowConnect = false;
            var ex = ExceptionFactory.NoConnectionAvailable(conn.UnderlyingMultiplexer, null, null);
            Assert.IsType<RedisConnectionException>(ex);
            Assert.Null(ex.InnerException);
        }
        finally
        {
            ClearAmbientFailures();
        }
    }

    [Fact]
    public void TimeoutException()
    {
        try
        {
            using var conn = Create(keepAlive: 1, connectTimeout: 10000, allowAdmin: true, shared: false);

            var server = GetServer(conn);
            conn.AllowConnect = false;
            var msg = Message.Create(-1, CommandFlags.None, RedisCommand.PING);
            var rawEx = ExceptionFactory.Timeout(conn.UnderlyingMultiplexer, "Test Timeout", msg, new ServerEndPoint(conn.UnderlyingMultiplexer, server.EndPoint));
            var ex = Assert.IsType<RedisTimeoutException>(rawEx);
            Log("Exception: " + ex.Message);

            // Example format: "Test Timeout, command=PING, inst: 0, qu: 0, qs: 0, aw: False, in: 0, in-pipe: 0, out-pipe: 0, last-in: 0, cur-in: 0, serverEndpoint: 127.0.0.1:6379, mgr: 10 of 10 available, clientName: TimeoutException, IOCP: (Busy=0,Free=1000,Min=8,Max=1000), WORKER: (Busy=2,Free=2045,Min=8,Max=2047), v: 2.1.0 (Please take a look at this article for some common client-side issues that can cause timeouts: https://stackexchange.github.io/StackExchange.Redis/Timeouts)";
            Assert.StartsWith("Test Timeout, command=PING", ex.Message);
            Assert.Contains("clientName: " + nameof(TimeoutException), ex.Message);
            // Ensure our pipe numbers are in place
            Assert.Contains("inst: 0, qu: 0, qs: 0, aw: False, bw: Inactive, in: 0, in-pipe: 0, out-pipe: 0, last-in: 0, cur-in: 0", ex.Message);
            Assert.Contains("mc: 1/1/0", ex.Message);
            Assert.Contains("serverEndpoint: " + server.EndPoint, ex.Message);
            Assert.Contains("IOCP: ", ex.Message);
            Assert.Contains("WORKER: ", ex.Message);
            Assert.Contains("sync-ops: ", ex.Message);
            Assert.Contains("async-ops: ", ex.Message);
            Assert.Contains("conn-sec: n/a", ex.Message);
            Assert.Contains("aoc: 1", ex.Message);
#if NETCOREAPP
            // ...POOL: (Threads=33,QueuedItems=0,CompletedItems=5547,Timers=60)...
            Assert.Contains("POOL: ", ex.Message);
            Assert.Contains("Threads=", ex.Message);
            Assert.Contains("QueuedItems=", ex.Message);
            Assert.Contains("CompletedItems=", ex.Message);
            Assert.Contains("Timers=", ex.Message);
#endif
            Assert.DoesNotContain("Unspecified/", ex.Message);
            Assert.EndsWith(" (Please take a look at this article for some common client-side issues that can cause timeouts: https://stackexchange.github.io/StackExchange.Redis/Timeouts)", ex.Message);
            Assert.Null(ex.InnerException);
        }
        finally
        {
            ClearAmbientFailures();
        }
    }

    [Theory]
    [InlineData(false, 0, 0, true, "Connection to Redis never succeeded (attempts: 0 - connection likely in-progress), unable to service operation: PING")]
    [InlineData(false, 1, 0, true, "Connection to Redis never succeeded (attempts: 1 - connection likely in-progress), unable to service operation: PING")]
    [InlineData(false, 12, 0, true, "Connection to Redis never succeeded (attempts: 12 - check your config), unable to service operation: PING")]
    [InlineData(false, 0, 0, false, "Connection to Redis never succeeded (attempts: 0 - connection likely in-progress), unable to service operation: PING")]
    [InlineData(false, 1, 0, false, "Connection to Redis never succeeded (attempts: 1 - connection likely in-progress), unable to service operation: PING")]
    [InlineData(false, 12, 0, false, "Connection to Redis never succeeded (attempts: 12 - check your config), unable to service operation: PING")]
    [InlineData(true, 0, 0, true, "No connection is active/available to service this operation: PING")]
    [InlineData(true, 1, 0, true, "No connection is active/available to service this operation: PING")]
    [InlineData(true, 12, 0, true, "No connection is active/available to service this operation: PING")]
    public void NoConnectionException(bool abortOnConnect, int connCount, int completeCount, bool hasDetail, string messageStart)
    {
        try
        {
            var options = new ConfigurationOptions()
            {
                AbortOnConnectFail = abortOnConnect,
                BacklogPolicy = BacklogPolicy.FailFast,
                ConnectTimeout = 1000,
                SyncTimeout = 500,
                KeepAlive = 5000
            };

            ConnectionMultiplexer conn;
            if (abortOnConnect)
            {
                options.EndPoints.Add(TestConfig.Current.PrimaryServerAndPort);
                conn = ConnectionMultiplexer.Connect(options, Writer);
            }
            else
            {
                options.EndPoints.Add($"doesnot.exist.{Guid.NewGuid():N}:6379");
                conn = ConnectionMultiplexer.Connect(options, Writer);
            }

            using (conn)
            {
                var server = conn.GetServer(conn.GetEndPoints()[0]);
                conn.AllowConnect = false;
                conn._connectAttemptCount = connCount;
                conn._connectCompletedCount = completeCount;
                options.IncludeDetailInExceptions = hasDetail;
                options.IncludePerformanceCountersInExceptions = hasDetail;

                var msg = Message.Create(-1, CommandFlags.None, RedisCommand.PING);
                var rawEx = ExceptionFactory.NoConnectionAvailable(conn, msg, new ServerEndPoint(conn, server.EndPoint));
                var ex = Assert.IsType<RedisConnectionException>(rawEx);
                Log("Exception: " + ex.Message);

                // Example format: "Exception: No connection is active/available to service this operation: PING, inst: 0, qu: 0, qs: 0, aw: False, in: 0, in-pipe: 0, out-pipe: 0, last-in: 0, cur-in: 0, serverEndpoint: 127.0.0.1:6379, mc: 1/1/0, mgr: 10 of 10 available, clientName: NoConnectionException, IOCP: (Busy=0,Free=1000,Min=8,Max=1000), WORKER: (Busy=2,Free=2045,Min=8,Max=2047), Local-CPU: 100%, v: 2.1.0.5";
                Assert.StartsWith(messageStart, ex.Message);

                // Ensure our pipe numbers are in place if they should be
                if (hasDetail)
                {
                    Assert.Contains("inst: 0, qu: 0, qs: 0, aw: False, bw: Inactive, in: 0, in-pipe: 0, out-pipe: 0, last-in: 0, cur-in: 0", ex.Message);
                    Assert.Contains($"mc: {connCount}/{completeCount}/0", ex.Message);
                    Assert.Contains("serverEndpoint: " + server.EndPoint.ToString()?.Replace("Unspecified/", ""), ex.Message);
                }
                else
                {
                    Assert.DoesNotContain("inst: 0, qu: 0, qs: 0, aw: False, bw: Inactive, in: 0, in-pipe: 0, out-pipe: 0, last-in: 0, cur-in: 0", ex.Message);
                    Assert.DoesNotContain($"mc: {connCount}/{completeCount}/0", ex.Message);
                    Assert.DoesNotContain("serverEndpoint: " + server.EndPoint.ToString()?.Replace("Unspecified/", ""), ex.Message);
                }
                Assert.DoesNotContain("Unspecified/", ex.Message);
            }
        }
        finally
        {
            ClearAmbientFailures();
        }
    }

    [Fact]
    public void NoConnectionPrimaryOnlyException()
    {
        using var conn = ConnectionMultiplexer.Connect(TestConfig.Current.ReplicaServerAndPort, Writer);

        var msg = Message.Create(0, CommandFlags.None, RedisCommand.SET, (RedisKey)Me(), (RedisValue)"test");
        Assert.True(msg.IsPrimaryOnly());
        var rawEx = ExceptionFactory.NoConnectionAvailable(conn, msg, null);
        var ex = Assert.IsType<RedisConnectionException>(rawEx);
        Log("Exception: " + ex.Message);

        // Ensure a primary-only operation like SET gives the additional context
        Assert.StartsWith("No connection (requires writable - not eligible for replica) is active/available to service this operation: SET", ex.Message);
    }

    [Theory]
    [InlineData(true, ConnectionFailureType.ProtocolFailure, "ProtocolFailure on [0]:GET myKey (StringProcessor), my annotation")]
    [InlineData(true, ConnectionFailureType.ConnectionDisposed, "ConnectionDisposed on [0]:GET myKey (StringProcessor), my annotation")]
    [InlineData(false, ConnectionFailureType.ProtocolFailure, "ProtocolFailure on [0]:GET (StringProcessor), my annotation")]
    [InlineData(false, ConnectionFailureType.ConnectionDisposed, "ConnectionDisposed on [0]:GET (StringProcessor), my annotation")]
    public void MessageFail(bool includeDetail, ConnectionFailureType failType, string messageStart)
    {
        using var conn = Create(shared: false);

        conn.RawConfig.IncludeDetailInExceptions = includeDetail;

        var message = Message.Create(0, CommandFlags.None, RedisCommand.GET, (RedisKey)"myKey");
        var resultBox = SimpleResultBox<string>.Create();
        message.SetSource(ResultProcessor.String, resultBox);

        message.Fail(failType, null, "my annotation", conn.UnderlyingMultiplexer);

        resultBox.GetResult(out var ex);
        Assert.NotNull(ex);

        Assert.StartsWith(messageStart, ex.Message);
    }
}
