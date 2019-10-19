using System.Threading.Tasks;
using Xunit;
using Xunit.Abstractions;

namespace StackExchange.Redis.Tests
{
    [Collection(SharedConnectionFixture.Key)]
    public class Latency : TestBase
    {

        public Latency(ITestOutputHelper output, SharedConnectionFixture fixture) : base(output, fixture) { }

        [Fact]
        public async Task CanCallDoctor()
        {
            using (var conn = Create())
            {
                var server = conn.GetServer(conn.GetEndPoints()[0]);
                string doctor = server.LatencyDoctor();
                Assert.NotNull(doctor);
                Assert.NotEqual("", doctor);

                doctor = await server.LatencyDoctorAsync();
                Assert.NotNull(doctor);
                Assert.NotEqual("", doctor);
            }
        }

        [Fact]
        public async Task CanReset()
        {
            using (var conn = Create())
            {
                var server = conn.GetServer(conn.GetEndPoints()[0]);
                _ = server.LatencyReset();
                var count = await server.LatencyResetAsync(new string[] { "command" });
                Assert.Equal(0, count);

                count = await server.LatencyResetAsync(new string[] { "command", "fast-command" });
                Assert.Equal(0, count);
            }
        }

        [Fact]
        public async Task GetLatest()
        {
            using (var conn = Create(allowAdmin: true))
            {
                var server = conn.GetServer(conn.GetEndPoints()[0]);
                server.ConfigSet("latency-monitor-threshold", 100);
                server.LatencyReset();
                var arr = server.LatencyLatest();
                Assert.Empty(arr);

                var now = await server.TimeAsync();
                server.Execute("debug", "sleep", "0.5"); // cause something to be slow

                arr = await server.LatencyLatestAsync();
                var item = Assert.Single(arr);
                Assert.Equal("command", item.EventName);
                Assert.True(item.DurationMilliseconds >= 400 && item.DurationMilliseconds <= 600);
                Assert.Equal(item.DurationMilliseconds, item.MaxDurationMilliseconds);
                Assert.True(item.Timestamp >= now.AddSeconds(-2) && item.Timestamp <= now.AddSeconds(2));
            }
        }

        [Fact]
        public async Task GetHistory()
        {
            using (var conn = Create(allowAdmin: true))
            {
                var server = conn.GetServer(conn.GetEndPoints()[0]);
                server.ConfigSet("latency-monitor-threshold", 100);
                server.LatencyReset();
                var arr = server.LatencyHistory("command");
                Assert.Empty(arr);

                var now = await server.TimeAsync();
                server.Execute("debug", "sleep", "0.5"); // cause something to be slow

                arr = await server.LatencyHistoryAsync("command");
                var item = Assert.Single(arr);
                Assert.True(item.DurationMilliseconds >= 400 && item.DurationMilliseconds <= 600);
                Assert.True(item.Timestamp >= now.AddSeconds(-2) && item.Timestamp <= now.AddSeconds(2));
            }
        }
    }
}
