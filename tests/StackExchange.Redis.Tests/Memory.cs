using System.Threading.Tasks;
using Xunit;
using Xunit.Abstractions;

namespace StackExchange.Redis.Tests
{
    [Collection(SharedConnectionFixture.Key)]
    public class Memory : TestBase
    {
        public Memory(ITestOutputHelper output, SharedConnectionFixture fixture) : base(output, fixture) { }

        [Fact]
        public async Task CanCallDoctor()
        {
            using (var conn = Create())
            {
                Skip.IfMissingFeature(conn, nameof(RedisFeatures.Memory), r => r.Streams);
                var server = conn.GetServer(conn.GetEndPoints()[0]);
                string doctor = server.MemoryDoctor();
                Assert.NotNull(doctor);
                Assert.NotEqual("", doctor);

                doctor = await server.MemoryDoctorAsync();
                Assert.NotNull(doctor);
                Assert.NotEqual("", doctor);
            }
        }

        [Fact]
        public async Task CanPurge()
        {
            using (var conn = Create())
            {
                Skip.IfMissingFeature(conn, nameof(RedisFeatures.Memory), r => r.Streams);
                var server = conn.GetServer(conn.GetEndPoints()[0]);
                server.MemoryPurge();
                await server.MemoryPurgeAsync();

                await server.MemoryPurgeAsync();
            }
        }

        [Fact]
        public async Task GetAllocatorStats()
        {
            using (var conn = Create())
            {
                Skip.IfMissingFeature(conn, nameof(RedisFeatures.Memory), r => r.Streams);
                var server = conn.GetServer(conn.GetEndPoints()[0]);

                var stats = server.MemoryAllocatorStats();
                Assert.False(string.IsNullOrWhiteSpace(stats));

                stats = await server.MemoryAllocatorStatsAsync();
                Assert.False(string.IsNullOrWhiteSpace(stats));
            }
        }

        [Fact]
        public async Task GetStats()
        {
            using (var conn = Create())
            {
                Skip.IfMissingFeature(conn, nameof(RedisFeatures.Memory), r => r.Streams);
                var server = conn.GetServer(conn.GetEndPoints()[0]);
                var stats = server.MemoryStats();
                Assert.Equal(ResultType.MultiBulk, stats.Type);

                var parsed = stats.ToDictionary();

                var alloc = parsed["total.allocated"];
                Assert.Equal(ResultType.Integer, alloc.Type);
                Assert.True(alloc.AsInt64() > 0);

                stats = await server.MemoryStatsAsync();
                Assert.Equal(ResultType.MultiBulk, stats.Type);

                alloc = parsed["total.allocated"];
                Assert.Equal(ResultType.Integer, alloc.Type);
                Assert.True(alloc.AsInt64() > 0);
            }
        }
    }
}
