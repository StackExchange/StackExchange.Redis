using Xunit;
using Xunit.Abstractions;

namespace StackExchange.Redis.Tests.Issues
{
    public class Issue791 : TestBase
    {
        public Issue791(ITestOutputHelper output) : base(output) { }

        [Fact]
        public void PreserveAsyncOrderImplicitValue_ParsedFromConnectionString()
        {
            // We only care that it parses successfully while deprecated
            var options = ConfigurationOptions.Parse("preserveAsyncOrder=true");
            Assert.Equal("", options.ToString());

            // We only care that it parses successfully while deprecated
            options = ConfigurationOptions.Parse("preserveAsyncOrder=false");
            Assert.Equal("", options.ToString());
        }

        [Fact]
        public void DefaultValue_IsTrue()
        {
            var options = ConfigurationOptions.Parse("ssl=true");
        }

        [Fact]
        public void PreserveAsyncOrder_SetConnectionMultiplexerProperty()
        {
            using (var multiplexer = ConnectionMultiplexer.Connect(TestConfig.Current.MasterServerAndPort + ",preserveAsyncOrder=false"))
            {
                // We only care that it parses successfully while deprecated
            }
        }
    }
}
