using System;
using System.Diagnostics;
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

        [Test]
        [TestCase(true)]
        [TestCase(false)]
        public void CheckLoads(bool async)
        {
            using (var conn0 = Create(allowAdmin: true))
            using (var conn1 = Create(allowAdmin: true))
            {
                // note that these are on different connections (so we wouldn't expect
                // the flush to drop the local cache - assume it is a surprise!)
                var server = conn0.GetServer(PrimaryServer, PrimaryPort);
                var db = conn1.GetDatabase();
                const string script = "return 1;";

                // start empty
                server.ScriptFlush();
                Assert.IsFalse(server.ScriptExists(script));

                // run once, causes to be cached
                Assert.IsTrue((bool)db.ScriptEvaluate(script));
                Assert.IsTrue(server.ScriptExists(script));

                // can run again
                Assert.IsTrue((bool)db.ScriptEvaluate(script));

                // ditch the scripts; should no longer exist
                db.Ping();
                server.ScriptFlush();
                Assert.IsFalse(server.ScriptExists(script));
                db.Ping();

                if (async)
                {
                    // now: fails the first time
                    try
                    {
                        db.Wait(db.ScriptEvaluateAsync(script));
                        Assert.Fail();
                    }
                    catch(AggregateException ex)
                    {
                        Assert.AreEqual(1, ex.InnerExceptions.Count);
                        Assert.IsInstanceOf<RedisServerException>(ex.InnerExceptions[0]);
                        Assert.AreEqual("NOSCRIPT No matching script. Please use EVAL.", ex.InnerExceptions[0].Message);
                    }
                } else
                {
                    // just works; magic
                    Assert.IsTrue((bool)db.ScriptEvaluate(script));
                }

                // but gets marked as unloaded, so we can use it again...
                Assert.IsTrue((bool)db.ScriptEvaluate(script));

                // which will cause it to be cached
                Assert.IsTrue(server.ScriptExists(script));
            }
        }

        [Test]
        public void CompareScriptToDirect()
        {
            const string Script = "return redis.call('incr', KEYS[1])";

            using (var conn = Create(allowAdmin: true))
            {
                var server = conn.GetServer(PrimaryServer, PrimaryPort);
                server.FlushAllDatabases();
                server.ScriptFlush();

                server.ScriptLoad(Script);
                var db = conn.GetDatabase();
                db.Ping(); // k, we're all up to date now; clean db, minimal script cache

                // we're using a pipeline here, so send 1000 messages, but for timing: only care about the last
                const int LOOP = 5000;
                RedisKey key = "foo";
                RedisKey[] keys = new[] { key }; // script takes an array

                // run via script
                db.KeyDelete(key);
                CollectGarbage();
                var watch = Stopwatch.StartNew();
                for(int i = 1; i < LOOP; i++) // the i=1 is to do all-but-one
                {
                    db.ScriptEvaluate(Script, keys, flags: CommandFlags.FireAndForget);
                }
                var scriptResult = db.ScriptEvaluate(Script, keys); // last one we wait for (no F+F)
                watch.Stop();
                TimeSpan scriptTime = watch.Elapsed;

                // run via raw op
                db.KeyDelete(key);
                CollectGarbage();
                watch = Stopwatch.StartNew();
                for (int i = 1; i < LOOP; i++) // the i=1 is to do all-but-one
                {
                    db.StringIncrement(key, flags: CommandFlags.FireAndForget);
                }
                var directResult = db.StringIncrement(key); // last one we wait for (no F+F)
                watch.Stop();
                TimeSpan directTime = watch.Elapsed;

                Assert.AreEqual(LOOP, (long)scriptResult, "script result");
                Assert.AreEqual(LOOP, (long)directResult, "direct result");

                Console.WriteLine("script: {0}ms; direct: {1}ms",
                    scriptTime.TotalMilliseconds,
                    directTime.TotalMilliseconds);
            }
        }
    }
}
