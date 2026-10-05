using System;
using System.Threading.Tasks;
using Xunit;

namespace StackExchange.Redis.Tests;

public class ExceptionFactoryTests(ITestOutputHelper output, InProcServerFixture fixture) : TestBase(output, fixture)
{
    [Fact]
    public async Task NullLastException()
    {
        await using var conn = Create(keepAlive: 1, connectTimeout: 10000, allowAdmin: true);

        conn.GetDatabase();
        Assert.Null(conn.GetServerSnapshot()[0].LastException);
        var ex = ExceptionFactory.NoConnectionAvailable(conn.UnderlyingMultiplexer, null, null);
        Assert.Null(ex.InnerException);
    }

    /// <summary>The reported library version is a real version, whatever line we are on.</summary>
    /// <remarks>
    /// The major was pinned to <c>[2-3]</c>, which made moving to the v4 line fail here - a version
    /// assertion that has to be edited every major is asserting the wrong thing. What matters is that
    /// <c>GetLibVersion</c> returns something version-shaped at all, since it ends up in the message of
    /// every connection exception.
    /// </remarks>
    [Fact]
    public void CanGetVersion()
    {
        var libVer = Utils.GetLibVersion();
        Assert.Matches(@"^[0-9]+\.[0-9]+\.[0-9]+(\.[0-9]+)?", libVer);
    }

#if DEBUG
    [Fact]
    [Trait(TestCategories.Category, TestCategories.SimulatedConnectionFailure)]
    public async Task MultipleEndpointsThrowConnectionException()
    {
        try
        {
            await using var conn = Create(keepAlive: 1, connectTimeout: 10000, allowAdmin: true, allowSimulateConnectionFailure: true);

            conn.GetDatabase();
            conn.AllowConnect = false;

            foreach (var endpoint in conn.GetEndPoints())
            {
                var server = conn.GetServer(endpoint);
                Assert.SkipUnless(server.CanSimulateConnectionFailure(), "Skipping because server cannot simulate connection failure");
                server.SimulateConnectionFailure(SimulatedFailureType.All);
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
    [Trait(TestCategories.Category, TestCategories.SimulatedConnectionFailure)]
    public async Task ServerTakesPrecendenceOverSnapshot()
    {
        try
        {
            await using var conn = Create(keepAlive: 1, connectTimeout: 10000, allowAdmin: true, backlogPolicy: BacklogPolicy.FailFast, allowSimulateConnectionFailure: true);

            conn.GetDatabase();
            conn.AllowConnect = false;

            var server = conn.GetServer(conn.GetEndPoints()[0]);
            Assert.SkipUnless(server.CanSimulateConnectionFailure(), "Skipping because server cannot simulate connection failure");
            server.SimulateConnectionFailure(SimulatedFailureType.All);

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
    public async Task NullInnerExceptionForMultipleEndpointsWithNoLastException()
    {
        try
        {
            await using var conn = Create(keepAlive: 1, connectTimeout: 10000, allowAdmin: true);

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
    public async Task TimeoutException()
    {
        try
        {
            await using var conn = Create(keepAlive: 1, connectTimeout: 10000, allowAdmin: true, shared: false);

            var server = GetServer(conn);
            conn.AllowConnect = false;
            var msg = new FakeFault(RedisCommand.PING, "PING");
            var rawEx = ExceptionFactory.Timeout(conn.UnderlyingMultiplexer, "Test Timeout", msg, new ServerEndPoint(conn.UnderlyingMultiplexer, server.EndPoint, ServerProvenance.Configured));
            var ex = Assert.IsType<RedisTimeoutException>(rawEx);
            Log("Exception: " + ex.Message);

            // Example format: "Test Timeout, command=PING, inst: 0, qu: 0, qs: 0, aw: False, in: 0, in-pipe: 0, out-pipe: 0, last-in: 0, cur-in: 0, serverEndpoint: 127.0.0.1:6379, mgr: 10 of 10 available, clientName: TimeoutException, IOCP: (Busy=0,Free=1000,Min=8,Max=1000), WORKER: (Busy=2,Free=2045,Min=8,Max=2047), v: 2.1.0 (see https://seredis.dev/Timeouts for some common client-side issues that can cause timeouts)";
            Assert.StartsWith("Test Timeout, command=PING", ex.Message);
            Assert.Contains("clientName: " + nameof(TimeoutException), ex.Message);
            // Ensure our pipe numbers are in place - split for the reason NoConnectionException's are:
            // the socket and pipe byte counts exist only on the core that has a socket to poll and a pipe
            // to measure, and this test hands in a detached ServerEndPoint with no bridge
            Assert.Contains("inst: 0, qu: 0, qs: 0, aw: False, bw: Inactive", ex.Message);
            var fromNewCore = conn.UnderlyingMultiplexer.NewCoreIfCreated
                ?.ConnectionStatus(server.EndPoint, ConnectionType.Interactive) is not null;
            if (!fromNewCore)
            {
                Assert.Contains("in: 0, in-pipe: 0, out-pipe: 0, last-in: 0, cur-in: 0", ex.Message);
            }
            Assert.Contains("mc: 1/1/0", ex.Message);
            Assert.Contains("serverEndpoint: " + server.EndPoint, ex.Message);
            Assert.Contains("IOCP: ", ex.Message);
            Assert.Contains("WORKER: ", ex.Message);
            Assert.Contains("sync-ops: ", ex.Message);
            Assert.Contains("async-ops: ", ex.Message);
            Assert.Contains("conn-sec: n/a", ex.Message);
            Assert.Contains("aoc: 0", ex.Message);
#if NET
            // ...POOL: (Threads=33,QueuedItems=0,CompletedItems=5547,Timers=60)...
            Assert.Contains("POOL: ", ex.Message);
            Assert.Contains("Threads=", ex.Message);
            Assert.Contains("QueuedItems=", ex.Message);
            Assert.Contains("CompletedItems=", ex.Message);
            Assert.Contains("Timers=", ex.Message);
#endif
            Assert.DoesNotContain("Unspecified/", ex.Message);
            Assert.EndsWith(" (see https://seredis.dev/Timeouts for some common client-side issues that can cause timeouts)", ex.Message);
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
    public async Task NoConnectionException(bool abortOnConnect, int connCount, int completeCount, bool hasDetail, string messageStart)
    {
        try
        {
            var options = new ConfigurationOptions()
            {
                AbortOnConnectFail = abortOnConnect,
                BacklogPolicy = BacklogPolicy.FailFast,
                ConnectTimeout = 1000,
                SyncTimeout = 500,
                KeepAlive = 5000,
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

            await using (conn)
            {
                var server = conn.GetServer(conn.GetEndPoints()[0]);
                conn.AllowConnect = false;
                conn._connectAttemptCount = connCount;
                conn._connectCompletedCount = completeCount;
                options.IncludeDetailInExceptions = hasDetail;
                options.IncludePerformanceCountersInExceptions = hasDetail;

                var msg = new FakeFault(RedisCommand.PING, "PING");
                var rawEx = ExceptionFactory.NoConnectionAvailable(conn, msg, new ServerEndPoint(conn, server.EndPoint, ServerProvenance.Configured));
                var ex = Assert.IsType<RedisConnectionException>(rawEx);
                Log("Exception: " + ex.Message);

                // Example format: "Exception: No connection is active/available to service this operation: PING, inst: 0, qu: 0, qs: 0, aw: False, in: 0, in-pipe: 0, out-pipe: 0, last-in: 0, cur-in: 0, serverEndpoint: 127.0.0.1:6379, mc: 1/1/0, mgr: 10 of 10 available, clientName: NoConnectionException, IOCP: (Busy=0,Free=1000,Min=8,Max=1000), WORKER: (Busy=2,Free=2045,Min=8,Max=2047), Local-CPU: 100%, v: 2.1.0.5";
                Assert.StartsWith(messageStart, ex.Message);

                // Ensure our pipe numbers are in place if they should be
                if (hasDetail)
                {
                    // The counters that exist on every core...
                    Assert.Contains("inst: 0, qu: 0, qs: 0, aw: False, bw: Inactive", ex.Message);

                    // ...and the socket/pipe byte counts, which only one of them has. This test hands in a
                    // DETACHED ServerEndPoint with no bridge, so the status comes from whichever core has a
                    // connection to that endpoint - and the new core reports -1 for these three on purpose:
                    // it polls no socket and has no pipe, so there is no number to give. ExceptionFactory
                    // omits a negative rather than printing it, which is the honest outcome; filling them
                    // with zeroes would be inventing data in the one message people read when diagnosing.
                    // That core reports the equivalent in its own words - see the outbound/inbound figures
                    // on its timeouts.
                    // the same question GetBridgeStatus asks: does that core have an executor for this
                    // endpoint at all? Not whether it is connected - an endpoint it tried and failed to
                    // reach still has one, and still supplies the status that replaces the absent bridge's.
                    var fromNewCore = ((ConnectionMultiplexer)conn).NewCoreIfCreated
                        ?.ConnectionStatus(server.EndPoint, ConnectionType.Interactive) is not null;
                    if (!fromNewCore)
                    {
                        Assert.Contains("in: 0, in-pipe: 0, out-pipe: 0, last-in: 0, cur-in: 0", ex.Message);
                    }
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
    public async Task NoConnectionPrimaryOnlyException()
    {
        await using var conn = await ConnectionMultiplexer.ConnectAsync(TestConfig.Current.ReplicaServerAndPort, Writer);

        var msg = new FakeFault(RedisCommand.SET, "SET " + Me());
        Assert.True(msg.Command.IsPrimaryOnly());
        var rawEx = ExceptionFactory.NoConnectionAvailable(conn, msg, null);
        var ex = Assert.IsType<RedisConnectionException>(rawEx);
        Log("Exception: " + ex.Message);

        // Ensure a primary-only operation like SET gives the additional context
        Assert.StartsWith("No connection (requires writable - not eligible for replica) is active/available to service this operation: SET", ex.Message);
    }

    /// <summary>The least that describes a command to the exception factory.</summary>
    private sealed class FakeFault(RedisCommand command, string commandAndKey) : IFaultSubject
    {
        public string CommandAndKey => commandAndKey;
        public string CommandString => command.ToString();
        public RedisCommand Command => command;
        public CommandFlags Flags => CommandFlags.None;
        public CommandStatus Status => CommandStatus.WaitingToBeSent;
        public bool IsBacklogged => false;
        public bool IsAsync => true;
        public bool IsForSubscriptionBridge => false;
        public int GetHashSlot(ServerSelectionStrategy serverSelectionStrategy) => ServerSelectionStrategy.NoSlot;
    }
}
