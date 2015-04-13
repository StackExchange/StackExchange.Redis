using System;
using System.Linq;
using NUnit.Framework;

namespace StackExchange.Redis.Tests.Issues
{
    [TestFixture]
    public class Issue182 : TestBase
    {
        protected override string GetConfiguration()
        {
            return "127.0.0.1:6379";
        }
        [Test]
        public void SetMembers()
        {
            using (var conn = Create())
            {
                var db = conn.GetDatabase();

                var key = Me();
                const int count = (int)5e6;

                db.KeyDeleteAsync(key).Wait();
                foreach (var _ in Enumerable.Range(0, count))
                    db.SetAdd(key, Guid.NewGuid().ToByteArray(), CommandFlags.FireAndForget);

                Assert.AreEqual(count, db.SetLengthAsync(key).Result, "SCARD for set");

                var task = db.SetMembersAsync(key);
                task.Wait();
                Assert.AreEqual(count, task.Result.Length, "SMEMBERS result length");
            }
        }

        [Test]
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
                Assert.AreEqual(count, db.SetLengthAsync(key1).Result, "SCARD for set 1");
                Assert.AreEqual(count, db.SetLengthAsync(key2).Result, "SCARD for set 2");

                db.SetCombineAndStoreAsync(SetOperation.Union, dstkey, key1, key2).Wait();
                var dstLen = db.SetLength(dstkey);
                Assert.AreEqual(count * 2, dstLen, "SCARD for destination set");
            }
        }
    }
}