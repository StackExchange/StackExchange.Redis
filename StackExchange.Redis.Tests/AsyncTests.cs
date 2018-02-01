using System;
using System.Linq;
using System.Threading.Tasks;
using Xunit;
using Xunit.Abstractions;

namespace StackExchange.Redis.Tests
{
    public class AsyncTests : TestBase
    {
        public AsyncTests(ITestOutputHelper output) : base (output) { }

        protected override string GetConfiguration() => TestConfig.Current.MasterServer + ":" + TestConfig.Current.MasterPort;

#if DEBUG // IRedisServerDebug and AllowConnect are only available if DEBUG is defined
        [Fact]
        public void AsyncTasksReportFailureIfServerUnavailable()
        {
            SetExpectedAmbientFailureCount(-1); // this will get messy

            using (var conn = Create(allowAdmin: true))
            {
                var server = conn.GetServer(TestConfig.Current.MasterServer, TestConfig.Current.MasterPort);

                RedisKey key = Me();
                var db = conn.GetDatabase();
                db.KeyDelete(key);
                var a = db.SetAddAsync(key, "a");
                var b = db.SetAddAsync(key, "b");

                Assert.True(conn.Wait(a));
                Assert.True(conn.Wait(b));

                conn.AllowConnect = false;
                server.SimulateConnectionFailure();
                var c = db.SetAddAsync(key, "c");

                Assert.True(c.IsFaulted, "faulted");
                var ex = c.Exception.InnerExceptions.Single();
                Assert.IsType<RedisConnectionException>(ex);
                Assert.StartsWith("No connection is available to service this operation: SADD AsyncTasksReportFailureIfServerUnavailable", ex.Message);
            }
        }
#endif
        [Fact]
        public void AsyncTimeout()
        {
            using (var conn = Create(allowAdmin:true, asynctimeout: 0))
            {
                var server = conn.GetServer(TestConfig.Current.MasterServer, TestConfig.Current.MasterPort);

                RedisKey key = Me();
                var db = conn.GetDatabase();
                byte[] b = new byte[10 * 1024 * 1024];
                Random r = new Random();
                r.NextBytes(b);
                var exception = Assert.ThrowsAsync<RedisTimeoutException>(async ()=> await db.StringSetAsync(key, b)).Result;
                Assert.StartsWith("StackExchange.Redis.RedisTimeoutException: Timeout performing async SET AsyncTimeout", exception.ToString());
            }
        }

        [Fact]
        public void NoAsyncTimeout()
        {
            using (var conn = Create(allowAdmin: true, syncTimeout: 1))
            {
                var server = conn.GetServer(TestConfig.Current.MasterServer, TestConfig.Current.MasterPort);

                RedisKey key = Me();
                var db = conn.GetDatabase();
                db.KeyDelete(key);
                byte[] b = new byte[10 * 1024 * 1024];
                Random r = new Random();
                r.NextBytes(b);
                //this should complete and not throw any exception.
                db.StringSetAsync(key, b).Wait(5000);
            }
        }
    }
}
