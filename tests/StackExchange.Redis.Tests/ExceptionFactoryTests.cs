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
            Assert.Matches(@"2\.[0-9]+\.[0-9]+\.[0-9]+", libVer);
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
    }
}
