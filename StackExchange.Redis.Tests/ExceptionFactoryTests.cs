using System;
using NUnit.Framework;

namespace StackExchange.Redis.Tests
{
    [TestFixture]
    public class ExceptionFactoryTests : TestBase
    {
        [Test]
        public void NullLastException()
        {
            using (var muxer = Create(keepAlive: 1, connectTimeout: 10000, allowAdmin: true))
            {
                var conn = muxer.GetDatabase();
                Assert.Null(muxer.GetServerSnapshot()[0].LastException);
                var ex = ExceptionFactory.NoConnectionAvailable(true, new RedisCommand(), null, null, muxer.GetServerSnapshot());
                Assert.Null(ex.InnerException);
            }

        }

        [Test]
        public void NullSnapshot()
        {
            var ex = ExceptionFactory.NoConnectionAvailable(true, new RedisCommand(), null, null, null);
            Assert.Null(ex.InnerException);
        }

        [Test]
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

                    var ex = ExceptionFactory.NoConnectionAvailable(true, new RedisCommand(), null, null, muxer.GetServerSnapshot());
                    Assert.IsInstanceOf<RedisConnectionException>(ex);
                    Assert.IsInstanceOf<AggregateException>(ex.InnerException);
                    var aggException = (AggregateException)ex.InnerException;
                    Assert.That(aggException.InnerExceptions.Count, Is.EqualTo(2));
                    for (int i = 0; i < aggException.InnerExceptions.Count; i++)
                    {
                        Assert.That(((RedisConnectionException)aggException.InnerExceptions[i]).FailureType, Is.EqualTo(ConnectionFailureType.SocketFailure));
                    }
                }
            }
            finally
            {
                SocketManager.ConnectCompletionType = CompletionType.Any;
                ClearAmbientFailures();
            }
        }

        [Test]
        public void NullInnerExceptionForMultipleEndpointsWithNoLastException()
        {
            try
            {
                using (var muxer = Create(keepAlive: 1, connectTimeout: 10000, allowAdmin: true))
                {
                    var conn = muxer.GetDatabase();
                    muxer.AllowConnect = false;
                    SocketManager.ConnectCompletionType = CompletionType.Async;
                    var ex = ExceptionFactory.NoConnectionAvailable(true, new RedisCommand(), null, null, muxer.GetServerSnapshot());
                    Assert.IsInstanceOf<RedisConnectionException>(ex);
                    Assert.Null(ex.InnerException);
                 }
            }
            finally
            {
                SocketManager.ConnectCompletionType = CompletionType.Any;
                ClearAmbientFailures();
            }
        }

        [Test]
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

                    var ex = ExceptionFactory.NoConnectionAvailable(true, new RedisCommand(), null,muxer.GetServerSnapshot()[0], muxer.GetServerSnapshot());
                    Assert.IsInstanceOf<RedisConnectionException>(ex);
                    Assert.IsInstanceOf<Exception>(ex.InnerException);
                    Assert.That(muxer.GetServerSnapshot()[0].LastException, Is.EqualTo(ex.InnerException));
                }
            }
            finally
            {
                SocketManager.ConnectCompletionType = CompletionType.Any;
                ClearAmbientFailures();
            }

        }

    }
}
