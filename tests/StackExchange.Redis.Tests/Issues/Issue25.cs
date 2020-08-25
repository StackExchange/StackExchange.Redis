using System;
using Xunit;
using Xunit.Abstractions;

namespace StackExchange.Redis.Tests.Issues
{
    public class Issue25 : TestBase
    {
        public Issue25(ITestOutputHelper output) : base (output) { }

        [Fact]
        public void CaseInsensitive()
        {
            var options = ConfigurationOptions.Parse("ssl=true");
            Assert.True(options.Ssl);
            Assert.Equal("ssl=True", options.ToString());

            options = ConfigurationOptions.Parse("SSL=TRUE");
            Assert.True(options.Ssl);
            Assert.Equal("ssl=True", options.ToString());
        }

        [Fact]
        public void UnkonwnKeywordHandling_Ignore()
        {
            ConfigurationOptions.Parse("ssl2=true", true);
        }

        [Fact]
        public void UnkonwnKeywordHandling_ExplicitFail()
        {
            var ex = Assert.Throws<ArgumentException>(() => {
                ConfigurationOptions.Parse("ssl2=true", false);
            });
            Assert.StartsWith("Keyword 'ssl2' is not supported", ex.Message);
            Assert.Equal("ssl2", ex.ParamName);
        }

        [Fact]
        public void UnkonwnKeywordHandling_ImplicitFail()
        {
            var ex = Assert.Throws<ArgumentException>(() => {
                ConfigurationOptions.Parse("ssl2=true");
            });
            Assert.StartsWith("Keyword 'ssl2' is not supported", ex.Message);
            Assert.Equal("ssl2", ex.ParamName);
        }
    }
}
