using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography.X509Certificates;
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
                    Assert.That(e.FailureType.ToString(), Is.EqualTo(ConnectionFailureType.AuthenticationFailure.ToString()));
                };
                if (!isCertValidationSucceeded)
                {
                    //validate that in this case it throws an certificatevalidation exception
                    var ex = Assert.Throws<RedisConnectionException>(() => connection.GetDatabase().Ping());
                  
                    ((AggregateException)ex.InnerException).Handle(e =>
                    {
                       var rde = (RedisConnectionException)e;
                       Assert.That(rde.FailureType.ToString(), Is.EqualTo(ConnectionFailureType.AuthenticationFailure.ToString()));
                       Assert.That(rde.InnerException.Message, Is.EqualTo("The remote certificate is invalid according to the validation procedure."));
                       return e is RedisConnectionException;
                    });
                    
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
                    Assert.That(e.FailureType.ToString(), Is.EqualTo(ConnectionFailureType.AuthenticationFailure.ToString()));
                };

                var ex = Assert.Throws<RedisConnectionException>(() => muxer.GetDatabase().Ping());

                ((AggregateException)ex.InnerException).Handle(e =>
                {
                    var rde = (RedisConnectionException)e;
                    Assert.That(rde.FailureType.ToString(), Is.EqualTo(ConnectionFailureType.AuthenticationFailure.ToString()));
                    return e is RedisConnectionException;
                });
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
                 ((AggregateException)ex.InnerException).Handle(e =>
                {
                    var rde = (RedisConnectionException)e;
                    Assert.That(rde.FailureType.ToString(), Is.EqualTo(ConnectionFailureType.SocketFailure.ToString()));
                    return e is RedisConnectionException;
                });
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

                    Assert.AreEqual(ConnectionFailureType.SocketFailure, ((RedisConnectionException)muxer.GetServerSnapShot()[0].LastException).FailureType);

                    // should reconnect within 1 keepalive interval
                    muxer.AllowConnect = true;
                    Thread.Sleep(2000);

                    Assert.Null(muxer.GetServerSnapShot()[0].LastException);
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
