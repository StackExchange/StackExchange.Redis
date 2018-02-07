using System.Collections.Generic;
using System.IO;
using System.Linq;
using Xunit;
using Xunit.Abstractions;

namespace StackExchange.Redis.Tests
{
    public class VPNTest : TestBase
    {
        public VPNTest(ITestOutputHelper output) : base (output) { }

        public static IEnumerable<object[]> GetVPNConfigs =>
            TestConfig.Current.VPNConfigs?.Select(c => new object[] { c })
            ?? new[] { new[] { "" } }; // xUnit errors on an empty theory and I'm tired of it

        [Theory]
        [MemberData(nameof(GetVPNConfigs))]
        public void Execute(string config)
        {
            if (string.IsNullOrEmpty(config))
            {
                Skip.IfNoConfig(nameof(TestConfig.Config.VPNConfigs), TestConfig.Current.VPNConfigs);
            }

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
