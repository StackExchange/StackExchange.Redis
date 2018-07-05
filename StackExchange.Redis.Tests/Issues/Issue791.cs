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
#pragma warning disable CS0618
            Assert.True(options.PreserveAsyncOrder);
#pragma warning restore CS0618
            Assert.Equal("preserveAsyncOrder=True", options.ToString());

            options = ConfigurationOptions.Parse("preserveAsyncOrder=false");
#pragma warning disable CS0618
            Assert.False(options.PreserveAsyncOrder);
#pragma warning restore CS0618
            Assert.Equal("preserveAsyncOrder=False", options.ToString());
        }

        [Fact]
        public void DefaultValue_IsTrue()
        {
            var options = ConfigurationOptions.Parse("ssl=true");
#pragma warning disable CS0618
            Assert.True(options.PreserveAsyncOrder);
#pragma warning restore CS0618
        }

        [Fact]
        public void PreserveAsyncOrder_SetConnectionMultiplexerProperty()
        {
            var multiplexer = ConnectionMultiplexer.Connect(TestConfig.Current.MasterServerAndPort + ",preserveAsyncOrder=false");
#pragma warning disable CS0618
            Assert.False(multiplexer.PreserveAsyncOrder);
#pragma warning restore CS0618
        }
    }
}
