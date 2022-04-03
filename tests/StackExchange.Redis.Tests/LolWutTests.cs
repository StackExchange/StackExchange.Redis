using System.Threading.Tasks;
using Xunit;
using Xunit.Abstractions;

namespace StackExchange.Redis.Tests
{
    public class LolWutTests : TestBase
    {
        public LolWutTests(ITestOutputHelper output, SharedConnectionFixture? fixture = null) : base(output, fixture)
        {
        }

        [Fact]
        public void CanGetRedisVersionWithArt()
        {
            using var connection = Create();
            var db = connection.GetDatabase();

            var result = db.LolWut();

            Assert.Contains("Redis ver", result.ToString());
        }

        [Fact]
        public async Task CanGetRedisVersionWithArtAsync()
        {
            using var connection = Create();
            var db = connection.GetDatabase();

            var result = await db.LolWutAsync();

            Assert.Contains("Redis ver", result.ToString());
        }
    }
}



