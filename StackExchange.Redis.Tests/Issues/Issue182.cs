using System;
using System.Linq;
using Xunit;
using Xunit.Abstractions;

namespace StackExchange.Redis.Tests.Issues
{
    public class Issue182 : TestBase
    {
        protected override string GetConfiguration() => $"{TestConfig.Current.MasterServerAndPort},responseTimeout=10000";

        public Issue182(ITestOutputHelper output) : base (output) { }

        [FactLongRunning]
        public void SetMembers()
        {
            using (var conn = Create())
            {
                conn.ConnectionFailed += (s, a) =>
                {
                    Output.WriteLine(a.FailureType.ToString());
                    Output.WriteLine(a.Exception.Message);
                    Output.WriteLine(a.Exception.StackTrace);
                };
                var db = conn.GetDatabase();

                var key = Me();
                const int count = (int)5e6;

                db.KeyDeleteAsync(key).Wait();
                foreach (var _ in Enumerable.Range(0, count))
                    db.SetAdd(key, Guid.NewGuid().ToByteArray(), CommandFlags.FireAndForget);

                Assert.Equal(count, db.SetLengthAsync(key).Result); // SCARD for set

                var task = db.SetMembersAsync(key);
                task.Wait();
                Assert.Equal(count, task.Result.Length); // SMEMBERS result length
            }
        }

        [FactLongRunning]
        public void SetUnion()
        {
            using (var conn = Create())
            {
                var db = conn.GetDatabase();

                var key1 = Me() + ":1";
                var key2 = Me() + ":2";
                var dstkey = Me() + ":dst";

                db.KeyDeleteAsync(key1).Wait();
                db.KeyDeleteAsync(key2).Wait();
                db.KeyDeleteAsync(dstkey).Wait();

                const int count = (int)5e6;
                foreach (var _ in Enumerable.Range(0, count))
                {
                    db.SetAdd(key1, Guid.NewGuid().ToByteArray(), CommandFlags.FireAndForget);
                    db.SetAdd(key2, Guid.NewGuid().ToByteArray(), CommandFlags.FireAndForget);
                }
                Assert.Equal(count, db.SetLengthAsync(key1).Result); // SCARD for set 1
                Assert.Equal(count, db.SetLengthAsync(key2).Result); // SCARD for set 2

                db.SetCombineAndStoreAsync(SetOperation.Union, dstkey, key1, key2).Wait();
                var dstLen = db.SetLength(dstkey);
                Assert.Equal(count * 2, dstLen); // SCARD for destination set
            }
        }
    }
}
