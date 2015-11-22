using System;
using System.Diagnostics;
using System.Linq;
using System.Text;
using NUnit.Framework;
using StackExchange.Redis;

namespace Tests
{
    [TestFixture]
    public class Scripting
    {
        static ConnectionMultiplexer GetScriptConn(bool allowAdmin = false)
        {
            int syncTimeout = 5000;
            if (Debugger.IsAttached) syncTimeout = 500000;
            var muxer = Config.GetUnsecuredConnection(waitForOpen: true, allowAdmin: allowAdmin, syncTimeout: syncTimeout);
            if (!Config.GetFeatures(muxer).Scripting)
            {
                Assert.Inconclusive("The server does not support scripting");
            }
            return muxer;

        }
        [Test]
        public void ClientScripting()
        {
            using (var conn = GetScriptConn())
            {
                var result = conn.GetDatabase().ScriptEvaluate("return redis.call('info','server')", null, null);
            }
        }

        [Test]
        public void BasicScripting()
        {
            using (var muxer = GetScriptConn())
            {
                var conn = muxer.GetDatabase();
                var noCache = conn.ScriptEvaluateAsync("return {KEYS[1],KEYS[2],ARGV[1],ARGV[2]}",
                    new RedisKey[] { "key1", "key2" }, new RedisValue[] { "first", "second" });
                var cache = conn.ScriptEvaluateAsync("return {KEYS[1],KEYS[2],ARGV[1],ARGV[2]}",
                    new RedisKey[] { "key1", "key2" }, new RedisValue[] { "first", "second" });
                var results = (string[])conn.Wait(noCache);
                Assert.AreEqual(4, results.Length);
                Assert.AreEqual("key1", results[0]);
                Assert.AreEqual("key2", results[1]);
                Assert.AreEqual("first", results[2]);
                Assert.AreEqual("second", results[3]);

                results = (string[])conn.Wait(cache);
                Assert.AreEqual(4, results.Length);
                Assert.AreEqual("key1", results[0]);
                Assert.AreEqual("key2", results[1]);
                Assert.AreEqual("first", results[2]);
                Assert.AreEqual("second", results[3]);
            }
        }
        [Test]
        public void KeysScripting()
        {
            using (var muxer = GetScriptConn())
            {
                var conn = muxer.GetDatabase();
                conn.StringSet("foo", "bar");
                var result = (string)conn.ScriptEvaluate("return redis.call('get', KEYS[1])", new RedisKey[] { "foo" }, null);
                Assert.AreEqual("bar", result);
            }
        }

        [Test]
        public void TestRandomThingFromForum()
        {
            const string script = @"local currentVal = tonumber(redis.call('GET', KEYS[1]));
                if (currentVal <= 0 ) then return 1 elseif (currentVal - (tonumber(ARGV[1])) < 0 ) then return 0 end;
                return redis.call('INCRBY', KEYS[1], -tonumber(ARGV[1]));";

            using (var muxer = GetScriptConn())
            {
                var conn = muxer.GetDatabase();
                conn.StringSetAsync("A", "0");
                conn.StringSetAsync("B", "5");
                conn.StringSetAsync("C", "10");

                var a = conn.ScriptEvaluateAsync(script, new RedisKey[] { "A" }, new RedisValue[] { 6 });
                var b = conn.ScriptEvaluateAsync(script, new RedisKey[] { "B" }, new RedisValue[] { 6 });
                var c = conn.ScriptEvaluateAsync(script, new RedisKey[] { "C" }, new RedisValue[] { 6 });

                var vals = conn.StringGetAsync(new RedisKey[] { "A", "B", "C" });

                Assert.AreEqual(1, (long)conn.Wait(a)); // exit code when current val is non-positive
                Assert.AreEqual(0, (long)conn.Wait(b)); // exit code when result would be negative
                Assert.AreEqual(4, (long)conn.Wait(c)); // 10 - 6 = 4
                Assert.AreEqual("0", (string)conn.Wait(vals)[0]);
                Assert.AreEqual("5", (string)conn.Wait(vals)[1]);
                Assert.AreEqual("4", (string)conn.Wait(vals)[2]);
            }
        }

        [Test]
        public void HackyGetPerf()
        {
            using (var muxer = GetScriptConn())
            {
                var conn = muxer.GetDatabase();
                conn.StringSetAsync("foo", "bar");
                var key = Config.CreateUniqueName();
                var result = (long)conn.ScriptEvaluate(@"
redis.call('psetex', KEYS[1], 60000, 'timing')
for i = 1,100000 do
    redis.call('set', 'ignore','abc')
end
local timeTaken = 60000 - redis.call('pttl', KEYS[1])
redis.call('del', KEYS[1])
return timeTaken
", new RedisKey[] { key }, null);
                Console.WriteLine(result);
                Assert.IsTrue(result > 0);
            }
        }

        [Test]
        public void MultiIncrWithoutReplies()
        {
            using (var muxer = GetScriptConn())
            {
                const int DB = 0; // any database number
                var conn = muxer.GetDatabase(DB);
                // prime some initial values
                conn.KeyDeleteAsync(new RedisKey[] { "a", "b", "c" });
                conn.StringIncrementAsync("b");
                conn.StringIncrementAsync("c");
                conn.StringIncrementAsync("c");

                // run the script, passing "a", "b", "c", "c" to
                // increment a & b by 1, c twice
                var result = conn.ScriptEvaluateAsync(
                    @"for i,key in ipairs(KEYS) do redis.call('incr', key) end",
                    new RedisKey[] { "a", "b", "c", "c" }, // <== aka "KEYS" in the script
                    null); // <== aka "ARGV" in the script

                // check the incremented values
                var a = conn.StringGetAsync("a");
                var b = conn.StringGetAsync("b");
                var c = conn.StringGetAsync("c");

                Assert.IsTrue(conn.Wait(result).IsNull, "result");
                Assert.AreEqual(1, (long)conn.Wait(a), "a");
                Assert.AreEqual(2, (long)conn.Wait(b), "b");
                Assert.AreEqual(4, (long)conn.Wait(c), "c");
            }
        }

        [Test]
        public void MultiIncrByWithoutReplies()
        {
            using (var muxer = GetScriptConn())
            {
                const int DB = 0; // any database number
                var conn = muxer.GetDatabase(DB);
                // prime some initial values
                conn.KeyDeleteAsync(new RedisKey[] { "a", "b", "c" });
                conn.StringIncrementAsync("b");
                conn.StringIncrementAsync("c");
                conn.StringIncrementAsync("c");

                //run the script, passing "a", "b", "c" and 1,2,3
                // increment a &b by 1, c twice
                var result = conn.ScriptEvaluateAsync(
                    @"for i,key in ipairs(KEYS) do redis.call('incrby', key, ARGV[i]) end",
                    new RedisKey[] { "a", "b", "c" }, // <== aka "KEYS" in the script
                    new RedisValue[] { 1, 1, 2 }); // <== aka "ARGV" in the script

                // check the incremented values
                var a = conn.StringGetAsync("a");
                var b = conn.StringGetAsync("b");
                var c = conn.StringGetAsync("c");

                Assert.IsTrue(conn.Wait(result).IsNull, "result");
                Assert.AreEqual(1, (long)conn.Wait(a), "a");
                Assert.AreEqual(2, (long)conn.Wait(b), "b");
                Assert.AreEqual(4, (long)conn.Wait(c), "c");
            }
        }

        [Test]
        public void DisableStringInference()
        {
            using (var muxer = GetScriptConn())
            {
                var conn = muxer.GetDatabase(0);
                conn.StringSet("foo", "bar");
                var result = (byte[])conn.ScriptEvaluate("return redis.call('get', KEYS[1])", new RedisKey[] { "foo" });
                Assert.AreEqual("bar", Encoding.UTF8.GetString(result));
            }
        }

        [Test]
        public void FlushDetection()
        { // we don't expect this to handle everything; we just expect it to be predictable
            using (var muxer = GetScriptConn(allowAdmin: true))
            {
                var conn = muxer.GetDatabase(0);
                conn.StringSet("foo", "bar");
                var result = (string)conn.ScriptEvaluate("return redis.call('get', KEYS[1])", new RedisKey[] { "foo" }, null);
                Assert.AreEqual("bar", result);

                // now cause all kinds of problems
                Config.GetServer(muxer).ScriptFlush();

                //expect this one to <strike>fail</strike> just work fine (self-fix)
                conn.ScriptEvaluate("return redis.call('get', KEYS[1])", new RedisKey[] { "foo" }, null);                

                result = (string)conn.ScriptEvaluate("return redis.call('get', KEYS[1])", new RedisKey[] { "foo" }, null);
                Assert.AreEqual("bar", result);
            }
        }

        [Test]
        public void PrepareScript()
        {
            string[] scripts = { "return redis.call('get', KEYS[1])", "return {KEYS[1],KEYS[2],ARGV[1],ARGV[2]}" };
            using (var muxer = GetScriptConn(allowAdmin: true))
            {
                var server = Config.GetServer(muxer);
                server.ScriptFlush();

                // when vanilla
                server.ScriptLoad(scripts[0]);
                server.ScriptLoad(scripts[1]);

                //when known to exist
                server.ScriptLoad(scripts[0]);
                server.ScriptLoad(scripts[1]);
            }
            using (var muxer = GetScriptConn())
            {
                var server = Config.GetServer(muxer);

                //when vanilla
                server.ScriptLoad(scripts[0]);
                server.ScriptLoad(scripts[1]);

                //when known to exist
                server.ScriptLoad(scripts[0]);
                server.ScriptLoad(scripts[1]);

                //when known to exist
                server.ScriptLoad(scripts[0]);
                server.ScriptLoad(scripts[1]);
            }
        }
        [Test]
        public void NonAsciiScripts()
        {
            using (var muxer = GetScriptConn())
            {
                const string evil = "return '僕'";
                var conn = muxer.GetDatabase(0);
                Config.GetServer(muxer).ScriptLoad(evil);

                var result = (string)conn.ScriptEvaluate(evil, null, null);
                Assert.AreEqual("僕", result);
            }
        }

        [Test]
        public void ScriptThrowsError()
        {
            Assert.Throws<RedisServerException>(() =>
            {
                using (var muxer = GetScriptConn())
                {
                    var conn = muxer.GetDatabase(0);
                    var result = conn.ScriptEvaluateAsync("return redis.error_reply('oops')", null, null);
                    try
                    {
                        conn.Wait(result);
                    }
                    catch (AggregateException ex)
                    {
                        throw ex.InnerExceptions[0];
                    }
                }
            },
            message: "oops");
        }

        [Test]
        public void ScriptThrowsErrorInsideTransaction()
        {
            using (var muxer = GetScriptConn())
            {
                const int db = 0;
                const string key = "ScriptThrowsErrorInsideTransaction";
                var conn = muxer.GetDatabase(db);
                conn.KeyDeleteAsync(key);
                var beforeTran = (string)conn.StringGet(key);
                Assert.IsNull(beforeTran);
                var tran = conn.CreateTransaction();
                {
                    var a = tran.StringIncrementAsync(key);
                    var b = tran.ScriptEvaluateAsync("return redis.error_reply('oops')", null, null);
                    var c = tran.StringIncrementAsync(key);
                    var complete = tran.ExecuteAsync();

                    Assert.IsTrue(tran.Wait(complete));
                    Assert.IsTrue(a.IsCompleted);
                    Assert.IsTrue(c.IsCompleted);
                    Assert.AreEqual(1L, a.Result);
                    Assert.AreEqual(2L, c.Result);

                    Assert.IsTrue(b.IsFaulted);
                    Assert.AreEqual(1, b.Exception.InnerExceptions.Count);
                    var ex = b.Exception.InnerExceptions.Single();
                    Assert.IsInstanceOf<RedisException>(ex);
                    Assert.AreEqual("oops", ex.Message);

                }
                var afterTran = conn.StringGetAsync(key);
                Assert.AreEqual(2L, (long)conn.Wait(afterTran));
            }
        }



        [Test]
        public void ChangeDbInScript()
        {
            using (var muxer = GetScriptConn())
            {
                muxer.GetDatabase(1).StringSet("foo", "db 1");
                muxer.GetDatabase(2).StringSet("foo", "db 2");

                var conn = muxer.GetDatabase(2);
                var evalResult = conn.ScriptEvaluateAsync(@"redis.call('select', 1)
        return redis.call('get','foo')", null, null);
                var getResult = conn.StringGetAsync("foo");

                Assert.AreEqual("db 1", (string)conn.Wait(evalResult));
                // now, our connection thought it was in db 2, but the script changed to db 1
                Assert.AreEqual("db 2", (string)conn.Wait(getResult));

            }
        }

        [Test]
        public void ChangeDbInTranScript()
        {
            using (var muxer = GetScriptConn())
            {
                muxer.GetDatabase(1).StringSet("foo", "db 1");
                muxer.GetDatabase(2).StringSet("foo", "db 2");

                var conn = muxer.GetDatabase(2);
                var tran = conn.CreateTransaction();
                var evalResult = tran.ScriptEvaluateAsync(@"redis.call('select', 1)
        return redis.call('get','foo')", null, null);
                var getResult = tran.StringGetAsync("foo");
                Assert.IsTrue(tran.Execute());

                Assert.AreEqual("db 1", (string)conn.Wait(evalResult));
                // now, our connection thought it was in db 2, but the script changed to db 1
                Assert.AreEqual("db 2", (string)conn.Wait(getResult));

            }
        }
    }
}
