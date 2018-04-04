using System.Threading;
using Xunit;
using Xunit.Abstractions;

namespace StackExchange.Redis.Tests
{
    public class ConnectionFailedErrors : TestBase
    {
        public ConnectionFailedErrors(ITestOutputHelper output) : base (output) { }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void SSLCertificateValidationError(bool isCertValidationSucceeded)
        {
            Skip.IfNoConfig(nameof(TestConfig.Config.AzureCacheServer), TestConfig.Current.AzureCacheServer);
            Skip.IfNoConfig(nameof(TestConfig.Config.AzureCachePassword), TestConfig.Current.AzureCachePassword);

            var options = new ConfigurationOptions();
            options.EndPoints.Add(TestConfig.Current.AzureCacheServer);
            options.Ssl = true;
            options.Password = TestConfig.Current.AzureCachePassword;
            options.CertificateValidation += (sender, cert, chain, errors) => isCertValidationSucceeded;
            options.AbortOnConnectFail = false;

            using (var connection = ConnectionMultiplexer.Connect(options))
            {
                connection.ConnectionFailed += (object sender, ConnectionFailedEventArgs e) =>
                    Assert.Equal(ConnectionFailureType.AuthenticationFailure, e.FailureType);
                if (!isCertValidationSucceeded)
                {
                    //validate that in this case it throws an certificatevalidation exception
                    var ex = Assert.Throws<RedisConnectionException>(() => connection.GetDatabase().Ping());
                    var rde = (RedisConnectionException)ex.InnerException;
                    Assert.Equal(ConnectionFailureType.AuthenticationFailure, rde.FailureType);
                    Assert.Equal("The remote certificate is invalid according to the validation procedure.", rde.InnerException.Message);
                }
                else
                {
                    connection.GetDatabase().Ping();
                }

                //wait for a second for connectionfailed event to fire
                Thread.Sleep(1000);
            }
        }

        [Fact]
        public void AuthenticationFailureError()
        {
            Skip.IfNoConfig(nameof(TestConfig.Config.AzureCacheServer), TestConfig.Current.AzureCacheServer);

            var options = new ConfigurationOptions();
            options.EndPoints.Add(TestConfig.Current.AzureCacheServer);
            options.Ssl = true;
            options.Password = "";
            options.AbortOnConnectFail = false;
            using (var muxer = ConnectionMultiplexer.Connect(options))
            {
                muxer.ConnectionFailed += (object sender, ConnectionFailedEventArgs e) =>
                    Assert.Equal(ConnectionFailureType.AuthenticationFailure, e.FailureType);
                var ex = Assert.Throws<RedisConnectionException>(() => muxer.GetDatabase().Ping());
                var rde = (RedisConnectionException)ex.InnerException;
                Assert.Equal(CommandStatus.WaitingToBeSent, ex.CommandStatus);
                Assert.Equal(ConnectionFailureType.AuthenticationFailure, rde.FailureType);
                Assert.Equal("Error: NOAUTH Authentication required. Verify if the Redis password provided is correct.", rde.InnerException.Message);
                //wait for a second  for connectionfailed event to fire
                Thread.Sleep(1000);
            }
        }

        [Fact]
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
                Assert.Equal(ConnectionFailureType.SocketFailure, rde.FailureType);
            }
        }
#if DEBUG // needs AllowConnect, which is DEBUG only
        [Fact]
        public void AbortOnConnectFailFalseConnectTimeoutError()
        {
            Skip.IfNoConfig(nameof(TestConfig.Config.AzureCacheServer), TestConfig.Current.AzureCacheServer);
            Skip.IfNoConfig(nameof(TestConfig.Config.AzureCachePassword), TestConfig.Current.AzureCachePassword);

            var options = new ConfigurationOptions();
            options.EndPoints.Add(TestConfig.Current.AzureCacheServer);
            options.Ssl = true;
            options.ConnectTimeout = 0;
            options.Password = TestConfig.Current.AzureCachePassword;
            using (var muxer = ConnectionMultiplexer.Connect(options))
            {
                var ex = Assert.Throws<RedisConnectionException>(() => muxer.GetDatabase().Ping());
                Assert.Contains("ConnectTimeout", ex.Message);
            }
        }

        [Fact]
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

                    Assert.Equal(ConnectionFailureType.SocketFailure, ((RedisConnectionException)muxer.GetServerSnapshot()[0].LastException).FailureType);

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

#endif
        [Fact]
        public void TryGetAzureRoleInstanceIdNoThrow()
        {
            Assert.Null(ConnectionMultiplexer.TryGetAzureRoleInstanceIdNoThrow());
        }
    }
}
