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
                var conn = muxer.GetDatabase();
                Assert.Null(muxer.GetServerSnapshot()[0].LastException);
                var ex = ExceptionFactory.NoConnectionAvailable(true, true, new RedisCommand(), null, null, muxer.GetServerSnapshot());
                Assert.Null(ex.InnerException);
            }
        }

        [Fact]
        public void NullSnapshot()
        {
            var ex = ExceptionFactory.NoConnectionAvailable(true, true, new RedisCommand(), null, null, null);
            Assert.Null(ex.InnerException);
        }
#if DEBUG // needs debug connection features
        [Fact]
        public void MultipleEndpointsThrowAggregateException()
        {
            try
            {
                using (var muxer = Create(keepAlive: 1, connectTimeout: 10000, allowAdmin: true))
                {
                    var conn = muxer.GetDatabase();
                    muxer.AllowConnect = false;
                    SocketManager.ConnectCompletionType = CompletionType.Async;

                    foreach (var endpoint in muxer.GetEndPoints())
                    {
                        muxer.GetServer(endpoint).SimulateConnectionFailure();
                    }

                    var ex = ExceptionFactory.NoConnectionAvailable(true, true, new RedisCommand(), null, null, muxer.GetServerSnapshot());
                    Assert.IsType<RedisConnectionException>(ex);
                    Assert.IsType<AggregateException>(ex.InnerException);
                    var aggException = (AggregateException)ex.InnerException;
                    Assert.Equal(2, aggException.InnerExceptions.Count);
                    for (int i = 0; i < aggException.InnerExceptions.Count; i++)
                    {
                        Assert.Equal(ConnectionFailureType.SocketFailure, ((RedisConnectionException)aggException.InnerExceptions[i]).FailureType);
                    }
                }
            }
            finally
            {
                SocketManager.ConnectCompletionType = CompletionType.Any;
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
                    var conn = muxer.GetDatabase();
                    muxer.AllowConnect = false;
                    SocketManager.ConnectCompletionType = CompletionType.Async;
                    var ex = ExceptionFactory.NoConnectionAvailable(true, true, new RedisCommand(), null, null, muxer.GetServerSnapshot());
                    Assert.IsType<RedisConnectionException>(ex);
                    Assert.Null(ex.InnerException);
                }
            }
            finally
            {
                SocketManager.ConnectCompletionType = CompletionType.Any;
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
                    SocketManager.ConnectCompletionType = CompletionType.Async;

                    muxer.GetServer(muxer.GetEndPoints()[0]).SimulateConnectionFailure();

                    var ex = ExceptionFactory.NoConnectionAvailable(true, true, new RedisCommand(), null, muxer.GetServerSnapshot()[0], muxer.GetServerSnapshot());
                    Assert.IsType<RedisConnectionException>(ex);
                    Assert.IsType<RedisConnectionException>(ex.InnerException);
                    Assert.Equal(ex.InnerException, muxer.GetServerSnapshot()[0].LastException);
                }
            }
            finally
            {
                SocketManager.ConnectCompletionType = CompletionType.Any;
                ClearAmbientFailures();
            }
        }
#endif
    }
}
