using System;
using System.Diagnostics;
using System.Threading.Tasks;
using Xunit;
using Xunit.Abstractions;

namespace StackExchange.Redis.Tests
{
    public class HttpTunnelConnectTests
    {
        private ITestOutputHelper Log { get; }
        public HttpTunnelConnectTests(ITestOutputHelper log) => Log = log;

        [Theory]
        [InlineData("")]
        [InlineData(",tunnel=http:127.0.0.1:8080")]
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
                Assert.NotNull(config.Tunnel);
            }
            await using var conn = await ConnectionMultiplexer.ConnectAsync(config);
            var db = conn.GetDatabase();
            await db.PingAsync();
            RedisKey key = "HttpTunnel";
            await db.KeyDeleteAsync(key);

            // latency test
            var watch = Stopwatch.StartNew();
            const int LATENCY_LOOP = 25, BANDWIDTH_LOOP = 10;
            for (int i = 0; i < LATENCY_LOOP; i++)
            {
                await db.StringIncrementAsync(key);
            }
            watch.Stop();
            int count = (int)await db.StringGetAsync(key);
            Log.WriteLine($"{LATENCY_LOOP}xINCR: {watch.ElapsedMilliseconds}ms");
            Assert.Equal(LATENCY_LOOP, count);

            // bandwidth test
            var chunk = new byte[4096];
            var rand = new Random(1234);
            for (int i = 0; i < BANDWIDTH_LOOP; i++)
            {
                rand.NextBytes(chunk);
                watch = Stopwatch.StartNew();
                await db.StringSetAsync(key, chunk);
                using var fetch = await db.StringGetLeaseAsync(key);
                watch.Stop();
                Assert.NotNull(fetch);
                Log.WriteLine($"SET+GET {chunk.Length} bytes: {watch.ElapsedMilliseconds}ms");
                Assert.True(fetch.Span.SequenceEqual(chunk));
            }
        }
    }
}
