using System.Threading;
using Xunit;
using Xunit.Abstractions;

namespace StackExchange.Redis.Tests
{
    public class RealWorld : TestBase
    {
        public RealWorld(ITestOutputHelper output) : base(output) { }

        [Fact]
        public void WhyDoesThisNotWork()
        {
            Output.WriteLine("first:");
            var config = ConfigurationOptions.Parse("localhost:6379,localhost:6380,name=Core (Q&A),tiebreaker=:RedisMaster,abortConnect=False");
            Assert.Equal(2, config.EndPoints.Count);
            Output.WriteLine("Endpoint 0: {0} (AddressFamily: {1})", config.EndPoints[0], config.EndPoints[0].AddressFamily);
            Output.WriteLine("Endpoint 1: {0} (AddressFamily: {1})", config.EndPoints[1], config.EndPoints[1].AddressFamily);

            using (var conn = ConnectionMultiplexer.Connect("localhost:6379,localhost:6380,name=Core (Q&A),tiebreaker=:RedisMaster,abortConnect=False", Writer))
            {
                Output.WriteLine("");
                Output.WriteLine("pausing...");
                Thread.Sleep(200);
                Output.WriteLine("second:");

                bool result = conn.Configure(Writer);
                Output.WriteLine("Returned: {0}", result);
            }
        }
    }
}
