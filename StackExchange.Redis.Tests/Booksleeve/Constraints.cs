using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Xunit;
using Xunit.Abstractions;

namespace StackExchange.Redis.Tests.Booksleeve
{
    public class Constraints : BookSleeveTestBase
    {
        public Constraints(ITestOutputHelper output) : base(output) { }

        [Fact]
        public void ValueEquals()
        {
            RedisValue x = 1, y = "1";
            Assert.True(x.Equals(y), "equals");
            Assert.True(x == y, "operator");
        }

        [Fact]
        public void TestManualIncr()
        {
            using (var muxer = GetUnsecuredConnection(syncTimeout: 120000)) // big timeout while debugging
            {
                var key = Me();
                var conn = muxer.GetDatabase();
                for (int i = 0; i < 200; i++)
                {
                    conn.KeyDelete(key);
                    Assert.Equal(1, conn.Wait(ManualIncr(conn, key)));
                    Assert.Equal(2, conn.Wait(ManualIncr(conn, key)));
                    Assert.Equal(2, (long)conn.StringGet(key));
                }
            }
        }

        public async Task<long?> ManualIncr(IDatabase connection, RedisKey key)
        {
            var oldVal = (long?)await connection.StringGetAsync(key).ForAwait();
            var newVal = (oldVal ?? 0) + 1;
            var tran = connection.CreateTransaction();
            { // check hasn't changed
#pragma warning disable 4014
                tran.AddCondition(Condition.StringEqual(key, oldVal));
                tran.StringSetAsync(key, newVal);
#pragma warning restore 4014
                if (!await tran.ExecuteAsync().ForAwait()) return null; // aborted
                return newVal;
            }
        }
    }
}
