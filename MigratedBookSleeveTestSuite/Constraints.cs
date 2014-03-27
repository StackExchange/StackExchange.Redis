//using BookSleeve;
//using NUnit.Framework;
//using System;
//using System.Collections.Generic;
//using System.Linq;
//using System.Text;
//using System.Threading.Tasks;

//namespace Tests
//{
//    [TestFixture]
//    public class Constraints
//    {
//        [Test]
//        public void TestManualIncr()
//        {
//            using (var conn = Config.GetUnsecuredConnection(syncTimeout: 120000)) // big timeout while debugging
//            {
//                for (int i = 0; i < 200; i++)
//                {
//                    conn.Keys.Remove(0, "foo");
//                    Assert.AreEqual(1, conn.Wait(ManualIncr(conn, 0, "foo")));
//                    Assert.AreEqual(2, conn.Wait(ManualIncr(conn, 0, "foo")));
//                    Assert.AreEqual(2, conn.Wait(conn.Strings.GetInt64(0, "foo")));
//                }
//            }
            
//        }

//        public async Task<long?> ManualIncr(RedisConnection connection, int db, string key)
//        {
//            var oldVal = await connection.Strings.GetInt64(db, key).SafeAwaitable();
//            var newVal = (oldVal ?? 0) + 1;
//            using (var tran = connection.CreateTransaction())
//            { // check hasn't changed

//#pragma warning disable 4014
//                tran.AddCondition(Condition.KeyEquals(db, key, oldVal));
//                tran.Strings.Set(db, key, newVal);
//#pragma warning restore 4014
//                if (!await tran.Execute().SafeAwaitable()) return null; // aborted
//                return newVal;
//            }    
//        }
//    }
//}
