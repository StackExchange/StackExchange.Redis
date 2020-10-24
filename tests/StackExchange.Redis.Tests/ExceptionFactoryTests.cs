using System;
using Xunit;
using Xunit.Abstractions;

namespace StackExchange.Redis.Tests
{
    public class ExceptionFactoryTests : TestBase
    {
        public ExceptionFactoryTests(ITestOutputHelper output) : base (output) { }

        [Fact]
        public void NullLastException()
        {
            using (var muxer = Create(keepAlive: 1, connectTimeout: 10000, allowAdmin: true))
            {
                muxer.GetDatabase();
                Assert.Null(muxer.GetServerSnapshot()[0].LastException);
                var ex = ExceptionFactory.NoConnectionAvailable(muxer as ConnectionMultiplexer, null, null);
                Assert.Null(ex.InnerException);
            }
        }

        [Fact]
        public void CanGetVersion()
        {
            var libVer = ExceptionFactory.GetLibVersion();
            Assert.Matches(@"2\.[0-9]+\.[0-9]+(\.[0-9]+)?", libVer);
        }

#if DEBUG
        [Fact]
        public void MultipleEndpointsThrowConnectionException()
        {
            try
            {
                using (var muxer = Create(keepAlive: 1, connectTimeout: 10000, allowAdmin: true, shared: false))
                {
                    muxer.GetDatabase();
                    muxer.AllowConnect = false;

                    foreach (var endpoint in muxer.GetEndPoints())
                    {
                        muxer.GetServer(endpoint).SimulateConnectionFailure();
                    }

                    var ex = ExceptionFactory.NoConnectionAvailable(muxer as ConnectionMultiplexer, null, null);
                    var outer = Assert.IsType<RedisConnectionException>(ex);
                    Assert.Equal(ConnectionFailureType.UnableToResolvePhysicalConnection, outer.FailureType);
                    var inner = Assert.IsType<RedisConnectionException>(outer.InnerException);
                    Assert.True(inner.FailureType == ConnectionFailureType.SocketFailure
                             || inner.FailureType == ConnectionFailureType.InternalFailure);
                }
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
                using (var muxer = Create(keepAlive: 1, connectTimeout: 10000, allowAdmin: true, shared: false))
                {
                    muxer.GetDatabase();
                    muxer.AllowConnect = false;

                    muxer.GetServer(muxer.GetEndPoints()[0]).SimulateConnectionFailure();

                    var ex = ExceptionFactory.NoConnectionAvailable(muxer as ConnectionMultiplexer, null, muxer.GetServerSnapshot()[0]);
                    Assert.IsType<RedisConnectionException>(ex);
                    Assert.IsType<RedisConnectionException>(ex.InnerException);
                    Assert.Equal(ex.InnerException, muxer.GetServerSnapshot()[0].LastException);
                }
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
                using (var muxer = Create(keepAlive: 1, connectTimeout: 10000, allowAdmin: true))
                {
                    muxer.GetDatabase();
                    muxer.AllowConnect = false;
                    var ex = ExceptionFactory.NoConnectionAvailable(muxer as ConnectionMultiplexer, null, null);
                    Assert.IsType<RedisConnectionException>(ex);
                    Assert.Null(ex.InnerException);
                }
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
                using (var muxer = Create(keepAlive: 1, connectTimeout: 10000, allowAdmin: true, shared: false) as ConnectionMultiplexer)
                {
                    var server = GetServer(muxer);
                    muxer.AllowConnect = false;
                    var msg = Message.Create(-1, CommandFlags.None, RedisCommand.PING);
                    var rawEx = ExceptionFactory.Timeout(muxer, "Test Timeout", msg, new ServerEndPoint(muxer, server.EndPoint));
                    var ex = Assert.IsType<RedisTimeoutException>(rawEx);
                    Writer.WriteLine("Exception: " + ex.Message);

                    // Example format: "Test Timeout, command=PING, inst: 0, qu: 0, qs: 0, aw: False, in: 0, in-pipe: 0, out-pipe: 0, serverEndpoint: 127.0.0.1:6379, mgr: 10 of 10 available, clientName: TimeoutException, IOCP: (Busy=0,Free=1000,Min=8,Max=1000), WORKER: (Busy=2,Free=2045,Min=8,Max=2047), v: 2.1.0 (Please take a look at this article for some common client-side issues that can cause timeouts: https://stackexchange.github.io/StackExchange.Redis/Timeouts)";
                    Assert.StartsWith("Test Timeout, command=PING", ex.Message);
                    Assert.Contains("clientName: " + nameof(TimeoutException), ex.Message);
                    // Ensure our pipe numbers are in place
                    Assert.Contains("inst: 0, qu: 0, qs: 0, aw: False, in: 0, in-pipe: 0, out-pipe: 0", ex.Message);
                    Assert.Contains("mc: 1/1/0", ex.Message);
                    Assert.Contains("serverEndpoint: " + server.EndPoint, ex.Message);
                    Assert.DoesNotContain("Unspecified/", ex.Message);
                    Assert.EndsWith(" (Please take a look at this article for some common client-side issues that can cause timeouts: https://stackexchange.github.io/StackExchange.Redis/Timeouts)", ex.Message);
                    Assert.Null(ex.InnerException);
                }
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
                    ConnectTimeout = 500,
                    SyncTimeout = 500,
                    KeepAlive = 5000
                };

                ConnectionMultiplexer muxer;
                if (abortOnConnect)
                {
                    options.EndPoints.Add(TestConfig.Current.MasterServerAndPort);
                    muxer = ConnectionMultiplexer.Connect(options);
                }
                else
                {
                    options.EndPoints.Add($"doesnot.exist.{Guid.NewGuid():N}:6379");
                    muxer = ConnectionMultiplexer.Connect(options);
                }

                using (muxer)
                {
                    var server = muxer.GetServer(muxer.GetEndPoints()[0]);
                    muxer.AllowConnect = false;
                    muxer._connectAttemptCount = connCount;
                    muxer._connectCompletedCount = completeCount;
                    muxer.IncludeDetailInExceptions = hasDetail;
                    muxer.IncludePerformanceCountersInExceptions = hasDetail;

                    var msg = Message.Create(-1, CommandFlags.None, RedisCommand.PING);
                    var rawEx = ExceptionFactory.NoConnectionAvailable(muxer, msg, new ServerEndPoint(muxer, server.EndPoint));
                    var ex = Assert.IsType<RedisConnectionException>(rawEx);
                    Writer.WriteLine("Exception: " + ex.Message);

                    // Example format: "Exception: No connection is active/available to service this operation: PING, inst: 0, qu: 0, qs: 0, aw: False, in: 0, in-pipe: 0, out-pipe: 0, serverEndpoint: 127.0.0.1:6379, mc: 1/1/0, mgr: 10 of 10 available, clientName: NoConnectionException, IOCP: (Busy=0,Free=1000,Min=8,Max=1000), WORKER: (Busy=2,Free=2045,Min=8,Max=2047), Local-CPU: 100%, v: 2.1.0.5";
                    Assert.StartsWith(messageStart, ex.Message);

                    // Ensure our pipe numbers are in place if they should be
                    if (hasDetail)
                    {
                        Assert.Contains("inst: 0, qu: 0, qs: 0, aw: False, in: 0, in-pipe: 0, out-pipe: 0", ex.Message);
                        Assert.Contains($"mc: {connCount}/{completeCount}/0", ex.Message);
                        Assert.Contains("serverEndpoint: " + server.EndPoint.ToString().Replace("Unspecified/", ""), ex.Message);
                    }
                    else
                    {
                        Assert.DoesNotContain("inst: 0, qu: 0, qs: 0, aw: False, in: 0, in-pipe: 0, out-pipe: 0", ex.Message);
                        Assert.DoesNotContain($"mc: {connCount}/{completeCount}/0", ex.Message);
                        Assert.DoesNotContain("serverEndpoint: " + server.EndPoint.ToString().Replace("Unspecified/", ""), ex.Message);
                    }
                    Assert.DoesNotContain("Unspecified/", ex.Message);
                }
            }
            finally
            {
                ClearAmbientFailures();
            }
        }
    }
}
