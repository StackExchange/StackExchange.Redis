using System;
using System.Diagnostics;
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
                var conn = muxer.GetDatabase();
                Assert.Null(muxer.GetServerSnapshot()[0].LastException);
                var ex = ExceptionFactory.NoConnectionAvailable(true, true, new RedisCommand(), null, null, muxer.GetServerSnapshot());
                Assert.Null(ex.InnerException);
            }
        }

        [Fact]
        public void CanGetVersion()
        {
            var libVer = ExceptionFactory.GetLibVersion();
            Assert.Matches(@"2\.[0-9]+\.[0-9]+(\.[0-9]+)?", libVer);
        }

        [Fact]
        public void NullSnapshot()
        {
            var ex = ExceptionFactory.NoConnectionAvailable(true, true, new RedisCommand(), null, null, null);
            Assert.Null(ex.InnerException);
        }

#if DEBUG
        [Fact]
        public void MultipleEndpointsThrowConnectionException()
        {
            try
            {
                using (var muxer = Create(keepAlive: 1, connectTimeout: 10000, allowAdmin: true))
                {
                    var conn = muxer.GetDatabase();
                    muxer.AllowConnect = false;

                    foreach (var endpoint in muxer.GetEndPoints())
                    {
                        muxer.GetServer(endpoint).SimulateConnectionFailure();
                    }

                    var ex = ExceptionFactory.NoConnectionAvailable(true, true, new RedisCommand(), null, null, muxer.GetServerSnapshot());
                    var outer = Assert.IsType<RedisConnectionException>(ex);
                    Assert.Equal(ConnectionFailureType.UnableToResolvePhysicalConnection, outer.FailureType);
                    var inner = Assert.IsType<RedisConnectionException>(outer.InnerException);
                    Assert.Equal(ConnectionFailureType.SocketFailure, inner.FailureType);
                }
            }
            finally
            {
                ClearAmbientFailures();
            }
        }

        [Fact]
        public void ServerTakesPrecendenceOverSnapshot()
        {
            try
            {
                using (var muxer = Create(keepAlive: 1, connectTimeout: 10000, allowAdmin: true))
                {
                    var conn = muxer.GetDatabase();
                    muxer.AllowConnect = false;

                    muxer.GetServer(muxer.GetEndPoints()[0]).SimulateConnectionFailure();

                    var ex = ExceptionFactory.NoConnectionAvailable(true, true, new RedisCommand(), null, muxer.GetServerSnapshot()[0], muxer.GetServerSnapshot());
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
#endif

        [Fact]
        public void NullInnerExceptionForMultipleEndpointsWithNoLastException()
        {
            try
            {
                using (var muxer = Create(keepAlive: 1, connectTimeout: 10000, allowAdmin: true))
                {
                    var conn = muxer.GetDatabase();
                    muxer.AllowConnect = false;
                    var ex = ExceptionFactory.NoConnectionAvailable(true, true, new RedisCommand(), null, null, muxer.GetServerSnapshot());
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
                    var conn = muxer.GetDatabase();
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
                    Assert.Contains("serverEndpoint: " + server.EndPoint.ToString(), ex.Message);
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
    }
}
