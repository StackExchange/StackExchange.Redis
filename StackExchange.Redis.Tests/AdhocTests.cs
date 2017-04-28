using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.Text;

namespace StackExchange.Redis.Tests
{
    [TestFixture]
    public class AdhocTests : TestBase
    {
        [Test]
        public void TestAdhocCommandsAPI()
        {
            using (var conn = Create())
            {
                var db = conn.GetDatabase();

                // needs explicit RedisKey type for key-based
                // sharding to work; will still work with strings,
                // but no key-based sharding support
                RedisKey key = "some_key";

                // note: if command renames are configured in
                // the API, they will still work automatically 
                db.Execute("del", key);
                db.Execute("set", key, "12");
                db.Execute("incrby", key, 4);
                int i = (int) db.Execute("get", key);

                Assert.AreEqual(16, i);

            }
        }
    }
}
