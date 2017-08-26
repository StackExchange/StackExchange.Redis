using System.IO;
using Xunit;
using Xunit.Abstractions;

namespace StackExchange.Redis.Tests
{
    public class VPNTest : TestBase
    {
        public VPNTest(ITestOutputHelper output) : base (output) { }

        [Theory]
        [InlineData("co-devredis01.ds.stackexchange.com:6379")]
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
                        Output.WriteLine(ttl.ToString());
                    }
                }
                catch
                {
                    Output.WriteLine(log.ToString());
                    throw;
                }
                Output.WriteLine("");
                Output.WriteLine("===");
                Output.WriteLine("");
            }
        }
    }
}
