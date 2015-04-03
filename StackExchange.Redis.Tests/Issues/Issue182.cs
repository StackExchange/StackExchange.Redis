using System;
using System.Linq;
using NUnit.Framework;

namespace StackExchange.Redis.Tests.Issues
{
    [TestFixture]
    public class Issue182 : TestBase
    {
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
    }
}