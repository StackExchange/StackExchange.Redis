using System.Threading.Tasks;
using NUnit.Framework;
using StackExchange.Redis;

namespace Tests
{
    [TestFixture]
    public class Constraints
    {
        [Test]
        public void ValueEquals()
        {
            RedisValue x = 1, y = "1";
            Assert.IsTrue(x.Equals(y), "equals");
            Assert.IsTrue(x == y, "operator");
            
        }

        [Test]
        public void TestManualIncr()
        {
            using (var muxer = Config.GetUnsecuredConnection(syncTimeout: 120000)) // big timeout while debugging
            {
                var conn = muxer.GetDatabase(0);
                for (int i = 0; i < 200; i++)
                {
                    conn.KeyDelete("foo");
                    Assert.AreEqual(1, conn.Wait(ManualIncr(conn, "foo")));
                    Assert.AreEqual(2, conn.Wait(ManualIncr(conn, "foo")));
                    Assert.AreEqual(2, (long)conn.StringGet("foo"));
                }
            }

        }

        public async Task<long?> ManualIncr(IDatabase connection, string key)
        {
            var oldVal = (long?)await connection.StringGetAsync(key).ConfigureAwait(false);
            var newVal = (oldVal ?? 0) + 1;
            var tran = connection.CreateTransaction();
            { // check hasn't changed

#pragma warning disable 4014
                tran.AddCondition(Condition.StringEqual(key, oldVal));
                tran.StringSetAsync(key, newVal);
#pragma warning restore 4014
                if (!await tran.ExecuteAsync().ConfigureAwait(false)) return null; // aborted
                return newVal;
            }
        }
    }
}
