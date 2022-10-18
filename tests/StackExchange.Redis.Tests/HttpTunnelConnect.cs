using System;
using System.Threading.Tasks;
using Xunit;

namespace StackExchange.Redis.Tests
{
    public class HttpTunnelConnect
    {
        [Theory]
        [InlineData("")]
        [InlineData(",gateway=http:127.0.0.1:8080")]
        public async Task Connect(string suffix)
        {
            var cs = Environment.GetEnvironmentVariable("HACK_TUNNEL_ENDPOINT");
            if (string.IsNullOrWhiteSpace(cs))
            {
                Skip.Inconclusive("Need HACK_TUNNEL_ENDPOINT environment variable");
            }
            var config = ConfigurationOptions.Parse(cs + suffix);
            if (!string.IsNullOrWhiteSpace(suffix))
            {
                Assert.NotNull(config.Gateway);
                Assert.NotNull(config.BeforeAuthenticate);
            }
            await using var conn = await ConnectionMultiplexer.ConnectAsync(config);
            await conn.GetDatabase().PingAsync();
        }
    }
}
