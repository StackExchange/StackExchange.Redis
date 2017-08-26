using System;
using System.IO;
using System.Threading;
using Xunit;
using Xunit.Abstractions;

namespace StackExchange.Redis.Tests
{
    public class RealWorld
    {
        public ITestOutputHelper Output { get; }
        public RealWorld(ITestOutputHelper output) => Output = output;

        [Fact]
        public void WhyDoesThisNotWork()
        {
            var sw = new StringWriter();
            Output.WriteLine("first:");
            using (var conn = ConnectionMultiplexer.Connect("localhost:6379,localhost:6380,name=Core (Q&A),tiebreaker=:RedisMaster,abortConnect=False", sw))
            {
                Output.WriteLine(sw.ToString());
                Output.WriteLine("");
                Output.WriteLine("pausing...");
                Thread.Sleep(200);
                Output.WriteLine("second:");

                sw = new StringWriter();
                bool result = conn.Configure(sw);
                Output.WriteLine("Returned: {0}", result);
                Output.WriteLine(sw.ToString());
            }
        }
    }
}
