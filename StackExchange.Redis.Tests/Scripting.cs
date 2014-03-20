using System;
using NUnit.Framework;

namespace StackExchange.Redis.Tests
{
    [TestFixture]
    public class Scripting : TestBase
    {
        [Test]
        public void TestBasicScripting()
        {
            using (var conn = Create())
            {
                RedisValue newId = Guid.NewGuid().ToString();
                RedisKey custKey = Me();
                var db = conn.GetDatabase();
                db.KeyDelete(custKey);
                db.HashSet(custKey, "id", 123);

                var wasSet = (bool) db.ScriptEvaluate(@"if redis.call('hexists', KEYS[1], 'UniqueId') then return redis.call('hset', KEYS[1], 'UniqueId', ARGV[1]) else return 0 end",
                    new RedisKey[] { custKey }, new RedisValue[] { newId });

                Assert.IsTrue(wasSet);

                wasSet = (bool)db.ScriptEvaluate(@"if redis.call('hexists', KEYS[1], 'UniqueId') then return redis.call('hset', KEYS[1], 'UniqueId', ARGV[1]) else return 0 end",
                    new RedisKey[] { custKey }, new RedisValue[] { newId });
                Assert.IsFalse(wasSet);
            }
        }
    }
}
