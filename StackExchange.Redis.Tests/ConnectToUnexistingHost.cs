using System.Diagnostics;
using System.Threading;
using Xunit;
using Xunit.Abstractions;

namespace StackExchange.Redis.Tests
{
    public class ConnectToUnexistingHost : TestBase
    {
        public ConnectToUnexistingHost(ITestOutputHelper output) : base (output) { }

#if DEBUG
        [Theory]
        [InlineData(CompletionType.Any)]
        [InlineData(CompletionType.Sync)]
        [InlineData(CompletionType.Async)]
        public void ConnectToUnexistingHostFailsWithinTimeout(CompletionType completionType)
        {
            var sw = Stopwatch.StartNew();
            try
            {
                var config = new ConfigurationOptions
                {
                    EndPoints = { { "invalid", 1234 } },
                    ConnectTimeout = 1000
                };

                SocketManager.ConnectCompletionType = completionType;

                using (var muxer = ConnectionMultiplexer.Connect(config))
                {
                    Thread.Sleep(10000);
                }

                Assert.True(false, "Connect should fail with RedisConnectionException exception");
            }
            catch (RedisConnectionException)
            {
                var elapsed = sw.ElapsedMilliseconds;
                Assert.True(elapsed < 9000, "Connect should fail within ConnectTimeout");
            }
            finally
            {
                SocketManager.ConnectCompletionType = CompletionType.Any;
            }
        }
#endif
    }
}