using System;
using System.IO;
using NUnit.Framework;

namespace StackExchange.Redis.Tests
{
    [TestFixture]
    public class VPNTest : TestBase
    {

        [Test]
        [MaxTime(100000)]
        [TestCase("co-devredis01.ds.stackexchange.com:6379")]
        public void Execute(string config)
        {
            for (int i = 0; i < 50; i++)
            {
                var log = new StringWriter();
                try
                {
                    var options = ConfigurationOptions.Parse(config);
                    options.SyncTimeout = 3000;
                    options.ConnectRetry = 5;
                    using (var conn = ConnectionMultiplexer.Connect(options, log))
                    {
                        var ttl = conn.GetDatabase().Ping();
                        Console.WriteLine(ttl);
                    }
                }
                catch
                {
                    Console.WriteLine(log);
                    Assert.Fail();
                }
                Console.WriteLine();
                Console.WriteLine("===");
                Console.WriteLine();
            }
        }
    }
}
