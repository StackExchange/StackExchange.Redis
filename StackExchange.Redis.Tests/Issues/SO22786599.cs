using System.Diagnostics;
using System.Linq;
using NUnit.Framework;

namespace StackExchange.Redis.Tests.Issues
{
    [TestFixture]
    public class SO22786599 : TestBase
    {
        [Test]
        public void Execute()
        {
            string CurrentIdsSetDbKey = Me() + ".x";
            string CurrentDetailsSetDbKey = Me() + ".y";

            RedisValue[] stringIds = Enumerable.Range(1, 750).Select(i => (RedisValue)(i + " id")).ToArray();
            RedisValue[] stringDetails = Enumerable.Range(1, 750).Select(i => (RedisValue)(i + " detail")).ToArray();

            using (var conn = Create())
            {
                var db = conn.GetDatabase();
                var tran = db.CreateTransaction();

                tran.SetAddAsync(CurrentIdsSetDbKey, stringIds);
                tran.SetAddAsync(CurrentDetailsSetDbKey, stringDetails);

                var watch = Stopwatch.StartNew();
                var isOperationSuccessful = tran.Execute();
                watch.Stop();
                System.Console.WriteLine("{0}ms", watch.ElapsedMilliseconds);
                Assert.IsTrue(isOperationSuccessful);                
            }
        }
    }
}
