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
            var options = ConfigurationOptions.Parse("preserveAsyncOrder=true");
            Assert.True(options.PreserveAsyncOrder);
            Assert.Equal("preserveAsyncOrder=True", options.ToString());

            options = ConfigurationOptions.Parse("preserveAsyncOrder=false");
            Assert.False(options.PreserveAsyncOrder);
            Assert.Equal("preserveAsyncOrder=False", options.ToString());
        }

        [Fact]
        public void DefaultValue_IsTrue()
        {
            var options = ConfigurationOptions.Parse("ssl=true");
            Assert.True(options.PreserveAsyncOrder);
        }

        [Fact]
        public void PreserveAsyncOrder_SetConnectionMultiplexerProperty()
        {
            var multiplexer = ConnectionMultiplexer.Connect(TestConfig.Current.MasterServerAndPort + ",preserveAsyncOrder=false");
            Assert.False(multiplexer.PreserveAsyncOrder);
        }
    }
}
