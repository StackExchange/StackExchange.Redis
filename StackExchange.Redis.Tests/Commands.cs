using System.Net;
using Xunit;

namespace StackExchange.Redis.Tests
{
    public class Commands
    {
        [Fact]
        public void Basic()
        {
            var config = ConfigurationOptions.Parse(".,$PING=p");
            Assert.Single(config.EndPoints);
            config.SetDefaultPorts();
            Assert.Contains(new DnsEndPoint(".", 6379), config.EndPoints);
            var map = config.CommandMap;
            Assert.Equal("$PING=p", map.ToString());
            Assert.Equal(".:6379,$PING=p", config.ToString());
        }
    }
}
