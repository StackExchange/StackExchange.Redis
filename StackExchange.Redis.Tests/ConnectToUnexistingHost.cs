using NUnit.Framework;
using System.Diagnostics;
using System.Threading;

namespace StackExchange.Redis.Tests
{
    [TestFixture]
    public class ConnectToUnexistingHost : TestBase
    {
#if DEBUG
        [Test]
        [TestCase(CompletionType.Any)]
        [TestCase(CompletionType.Sync)]
        [TestCase(CompletionType.Async)]
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

                Assert.Fail("Connect should fail with RedisConnectionException exception");
            }
            catch (RedisConnectionException)
            {
                var elapsed = sw.ElapsedMilliseconds;
                if (elapsed > 9000) 
                {
                    Assert.Fail("Connect should fail within ConnectTimeout");
                }
            }
            finally
            {
                SocketManager.ConnectCompletionType = CompletionType.Any;
            }
        }
#endif
    }
}