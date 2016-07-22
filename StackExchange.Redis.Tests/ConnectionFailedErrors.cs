using NUnit.Framework;
using System.Threading;

namespace StackExchange.Redis.Tests
{
    [TestFixture]
    public class ConnectionFailedErrors : TestBase
    {
        [Test]
        [TestCase(true)]
        [TestCase(false)]
        public void SSLCertificateValidationError(bool isCertValidationSucceeded)
        {
            string name, password;
            GetAzureCredentials(out name, out password);
            var options = new ConfigurationOptions();
            options.EndPoints.Add(name + ".redis.cache.windows.net");
            options.Ssl = true;
            options.Password = password;
            options.CertificateValidation += (sender, cert, chain, errors) => { return isCertValidationSucceeded; };
            options.AbortOnConnectFail = false;

            using (var connection = ConnectionMultiplexer.Connect(options))
            {
                connection.ConnectionFailed += (object sender, ConnectionFailedEventArgs e) =>
                {
                    Assert.That(e.FailureType, Is.EqualTo(ConnectionFailureType.AuthenticationFailure));
                };
                if (!isCertValidationSucceeded)
                {
                    //validate that in this case it throws an certificatevalidation exception
                    var ex = Assert.Throws<RedisConnectionException>(() => connection.GetDatabase().Ping());
                    var rde = (RedisConnectionException)ex.InnerException;
                    Assert.That(rde.FailureType, Is.EqualTo(ConnectionFailureType.AuthenticationFailure));
                    Assert.That(rde.InnerException.Message, Is.EqualTo("The remote certificate is invalid according to the validation procedure."));
                }
                else
                {
                    Assert.DoesNotThrow(() => connection.GetDatabase().Ping());
                }

                //wait for a second for connectionfailed event to fire
                Thread.Sleep(1000);
            }


        }

        [Test]
        public void AuthenticationFailureError()
        {
            string name, password;
            GetAzureCredentials(out name, out password);
            var options = new ConfigurationOptions();
            options.EndPoints.Add(name + ".redis.cache.windows.net");
            options.Ssl = true;
            options.Password = "";
            options.AbortOnConnectFail = false;
            using (var muxer = ConnectionMultiplexer.Connect(options))
            {
                muxer.ConnectionFailed += (object sender, ConnectionFailedEventArgs e) =>
                {
                    Assert.That(e.FailureType, Is.EqualTo(ConnectionFailureType.AuthenticationFailure));
                };
                var ex = Assert.Throws<RedisConnectionException>(() => muxer.GetDatabase().Ping());
                var rde = (RedisConnectionException)ex.InnerException;
                Assert.That(rde.FailureType, Is.EqualTo(ConnectionFailureType.AuthenticationFailure));
                Assert.That(rde.InnerException.Message, Is.EqualTo("Error: NOAUTH Authentication required. Verify if the Redis password provided is correct."));
                //wait for a second  for connectionfailed event to fire
                Thread.Sleep(1000);
            }
        }

        [Test]
        public void SocketFailureError()
        {
            var options = new ConfigurationOptions();
            options.EndPoints.Add(".redis.cache.windows.net");
            options.Ssl = true;
            options.Password = "";
            options.AbortOnConnectFail = false;
            using (var muxer = ConnectionMultiplexer.Connect(options))
            {
                var ex = Assert.Throws<RedisConnectionException>(() => muxer.GetDatabase().Ping());
                var rde = (RedisConnectionException)ex.InnerException;
                Assert.That(rde.FailureType, Is.EqualTo(ConnectionFailureType.SocketFailure));
            }
        }

        [Test]
        public void CheckFailureRecovered()
        {
            try
            {
                using (var muxer = Create(keepAlive: 1, connectTimeout: 10000, allowAdmin: true))
                {
                    var conn = muxer.GetDatabase();
                    var server = muxer.GetServer(muxer.GetEndPoints()[0]);

                    muxer.AllowConnect = false;
                    SocketManager.ConnectCompletionType = CompletionType.Async;

                    server.SimulateConnectionFailure();

                    Assert.AreEqual(ConnectionFailureType.SocketFailure, ((RedisConnectionException)muxer.GetServerSnapshot()[0].LastException).FailureType);

                    // should reconnect within 1 keepalive interval
                    muxer.AllowConnect = true;
                    Thread.Sleep(2000);

                    Assert.Null(muxer.GetServerSnapshot()[0].LastException);
                }
            }
            finally
            {
                SocketManager.ConnectCompletionType = CompletionType.Any;
                ClearAmbientFailures();
            }
        }

        [Test]
        public void TryGetAzureRoleInstanceIdNoThrow()
        {
            Assert.IsNull(ConnectionMultiplexer.TryGetAzureRoleInstanceIdNoThrow());
        }
    }
}
