using System.Linq;
using NUnit.Framework;

namespace StackExchange.Redis.Tests
{
    [TestFixture]
    public class AsyncTests : TestBase
    {
        protected override string GetConfiguration()
        {
            return PrimaryServer + ":" + PrimaryPortString;
        }

#if DEBUG // IRedisServerDebug and AllowConnect are only available if DEBUG is defined
        [Test]
        public void AsyncTasksReportFailureIfServerUnavailable()
        {
            SetExpectedAmbientFailureCount(-1); // this will get messy

            using(var conn = Create(allowAdmin: true))
            {
                var server = conn.GetServer(PrimaryServer, PrimaryPort);

                RedisKey key = Me();
                var db = conn.GetDatabase();
                db.KeyDelete(key);
                var a = db.SetAddAsync(key, "a");
                var b = db.SetAddAsync(key, "b");

                Assert.AreEqual(true, conn.Wait(a));
                Assert.AreEqual(true, conn.Wait(b));

                conn.AllowConnect = false;
                server.SimulateConnectionFailure();
                var c = db.SetAddAsync(key, "c");

                Assert.IsTrue(c.IsFaulted, "faulted");
                var ex = c.Exception.InnerExceptions.Single();
                Assert.IsInstanceOf<RedisConnectionException>(ex);
                Assert.AreEqual("No connection is available to service this operation: SADD AsyncTasksReportFailureIfServerUnavailable", ex.Message);
            }
        }
#endif
    }
}
