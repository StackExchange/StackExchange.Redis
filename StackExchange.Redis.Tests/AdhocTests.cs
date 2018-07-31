using Xunit;
using Xunit.Abstractions;

namespace StackExchange.Redis.Tests
{
    public class AdhocTests : TestBase
    {
        public AdhocTests(ITestOutputHelper output) : base (output) { }

        [Fact]
        public void TestAdhocCommandsAPI()
        {
            using (var conn = Create())
            {
                var db = conn.GetDatabase();

                // needs explicit RedisKey type for key-based
                // sharding to work; will still work with strings,
                // but no key-based sharding support
                RedisKey key = "some_key";

                // note: if command renames are configured in
                // the API, they will still work automatically 
                db.Execute("del", key);
                db.Execute("set", key, "12");
                db.Execute("incrby", key, 4);
                int i = (int)db.Execute("get", key);

                Assert.Equal(16, i);
            }
        }
    }
}
