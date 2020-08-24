using System;
using System.Linq;
using System.Security.Authentication;
using System.Threading.Tasks;
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
        public async Task SSLCertificateValidationError(bool isCertValidationSucceeded)
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
                connection.ConnectionFailed += (sender, e) =>
                    Assert.Equal(ConnectionFailureType.AuthenticationFailure, e.FailureType);
                if (!isCertValidationSucceeded)
                {
                    //validate that in this case it throws an certificatevalidation exception
                    var outer = Assert.Throws<RedisConnectionException>(() => connection.GetDatabase().Ping());
                    Assert.Equal(ConnectionFailureType.UnableToResolvePhysicalConnection, outer.FailureType);

                    Assert.NotNull(outer.InnerException);
                    var inner = Assert.IsType<RedisConnectionException>(outer.InnerException);
                    Assert.Equal(ConnectionFailureType.AuthenticationFailure, inner.FailureType);

                    Assert.NotNull(inner.InnerException);
                    var innerMost = Assert.IsType<AuthenticationException>(inner.InnerException);
                    Assert.Equal("The remote certificate is invalid according to the validation procedure.", innerMost.Message);
                }
                else
                {
                    connection.GetDatabase().Ping();
                }

                //wait for a second for connectionfailed event to fire
                await Task.Delay(1000).ForAwait();
            }
        }

        [Fact]
        public async Task AuthenticationFailureError()
        {
            Skip.IfNoConfig(nameof(TestConfig.Config.AzureCacheServer), TestConfig.Current.AzureCacheServer);

            var options = new ConfigurationOptions();
            options.EndPoints.Add(TestConfig.Current.AzureCacheServer);
            options.Ssl = true;
            options.Password = "";
            options.AbortOnConnectFail = false;
            options.CertificateValidation += SSL.ShowCertFailures(Writer);
            using (var muxer = ConnectionMultiplexer.Connect(options))
            {
                muxer.ConnectionFailed += (sender, e) =>
                {
                    if (e.FailureType == ConnectionFailureType.SocketFailure) Skip.Inconclusive("socket fail"); // this is OK too
                    Assert.Equal(ConnectionFailureType.AuthenticationFailure, e.FailureType);
                };
                var ex = Assert.Throws<RedisConnectionException>(() => muxer.GetDatabase().Ping());

                Assert.NotNull(ex.InnerException);
                var rde = Assert.IsType<RedisConnectionException>(ex.InnerException);
                Assert.Equal(CommandStatus.WaitingToBeSent, ex.CommandStatus);
                Assert.Equal(ConnectionFailureType.AuthenticationFailure, rde.FailureType);
                Assert.Equal("Error: NOAUTH Authentication required. Verify if the Redis password provided is correct.", rde.InnerException.Message);
                //wait for a second  for connectionfailed event to fire
                await Task.Delay(1000).ForAwait();
            }
        }

        [Fact]
        public void SocketFailureError()
        {
            var options = new ConfigurationOptions();
            options.EndPoints.Add($"{Guid.NewGuid():N}.redis.cache.windows.net");
            options.Ssl = true;
            options.Password = "";
            options.AbortOnConnectFail = false;
            options.ConnectTimeout = 1000;
            var outer = Assert.Throws<RedisConnectionException>(() =>
            {
                using (var muxer = ConnectionMultiplexer.Connect(options))
                {
                    muxer.GetDatabase().Ping();
                }
            });
            Assert.Equal(ConnectionFailureType.UnableToResolvePhysicalConnection, outer.FailureType);

            Assert.NotNull(outer.InnerException);
            if (outer.InnerException is RedisConnectionException rce)
            {
                Assert.Equal(ConnectionFailureType.UnableToConnect, rce.FailureType);
            }
            else if (outer.InnerException is AggregateException ae
                && ae.InnerExceptions.Any(e => e is RedisConnectionException rce2
                && rce2.FailureType == ConnectionFailureType.UnableToConnect))
            {
                // fine; at least *one* of them is the one we were hoping to see
            }
            else
            {
                Writer.WriteLine(outer.InnerException.ToString());
                if (outer.InnerException is AggregateException inner)
                {
                    foreach (var ex in inner.InnerExceptions)
                    {
                        Writer.WriteLine(ex.ToString());
                    }
                }
                Assert.False(true); // force fail
            }
        }

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
        public void TryGetAzureRoleInstanceIdNoThrow()
        {
            Assert.Null(ConnectionMultiplexer.TryGetAzureRoleInstanceIdNoThrow());
        }

#if DEBUG
        [Fact]
        public async Task CheckFailureRecovered()
        {
            try
            {
                using (var muxer = Create(keepAlive: 1, connectTimeout: 10000, allowAdmin: true))
                {
                    muxer.GetDatabase();
                    var server = muxer.GetServer(muxer.GetEndPoints()[0]);

                    muxer.AllowConnect = false;

                    server.SimulateConnectionFailure();

                    Assert.Equal(ConnectionFailureType.SocketFailure, ((RedisConnectionException)muxer.GetServerSnapshot()[0].LastException).FailureType);

                    // should reconnect within 1 keepalive interval
                    muxer.AllowConnect = true;
                    await Task.Delay(2000).ForAwait();

                    Assert.Null(muxer.GetServerSnapshot()[0].LastException);
                }
            }
            finally
            {
                ClearAmbientFailures();
            }
        }
#endif
    }
}
