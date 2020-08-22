using System.Threading.Tasks;
using Xunit;
using Xunit.Abstractions;

namespace StackExchange.Redis.Tests
{
    [Collection(SharedConnectionFixture.Key)]
    public class Constraints : TestBase
    {
        public Constraints(ITestOutputHelper output, SharedConnectionFixture fixture) : base(output, fixture) { }

        [Fact]
        public void ValueEquals()
        {
            RedisValue x = 1, y = "1";
            Assert.True(x.Equals(y), "equals");
            Assert.True(x == y, "operator");
        }

        [Fact]
        public async Task TestManualIncr()
        {
            using (var muxer = Create(syncTimeout: 120000)) // big timeout while debugging
            {
                var key = Me();
                var conn = muxer.GetDatabase();
                for (int i = 0; i < 10; i++)
                {
                    conn.KeyDelete(key, CommandFlags.FireAndForget);
                    Assert.Equal(1, await ManualIncrAsync(conn, key).ForAwait());
                    Assert.Equal(2, await ManualIncrAsync(conn, key).ForAwait());
                    Assert.Equal(2, (long)conn.StringGet(key));
                }
            }
        }

        public async Task<long?> ManualIncrAsync(IDatabase connection, RedisKey key)
        {
            var oldVal = (long?)await connection.StringGetAsync(key).ForAwait();
            var newVal = (oldVal ?? 0) + 1;
            var tran = connection.CreateTransaction();
            { // check hasn't changed
                tran.AddCondition(Condition.StringEqual(key, oldVal));
                _ = tran.StringSetAsync(key, newVal);
                if (!await tran.ExecuteAsync().ForAwait()) return null; // aborted
                return newVal;
            }
        }
    }
}
