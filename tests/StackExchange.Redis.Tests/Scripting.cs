using System;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using StackExchange.Redis.KeyspaceIsolation;
using Xunit;
using Xunit.Abstractions;

namespace StackExchange.Redis.Tests
{
    [Collection(SharedConnectionFixture.Key)]
    public class Scripting : TestBase
    {
        public Scripting(ITestOutputHelper output, SharedConnectionFixture fixture) : base (output, fixture) { }

        private IConnectionMultiplexer GetScriptConn(bool allowAdmin = false)
        {
            int syncTimeout = 5000;
            if (Debugger.IsAttached) syncTimeout = 500000;
            var muxer = Create(allowAdmin: allowAdmin, syncTimeout: syncTimeout);

            Skip.IfMissingFeature(muxer, nameof(RedisFeatures.Scripting), r => r.Scripting);
            return muxer;
        }

        [Fact]
        public void ClientScripting()
        {
            using (var conn = GetScriptConn())
            {
                var result = conn.GetDatabase().ScriptEvaluate("return redis.call('info','server')", null, null);
            }
        }

        [Fact]
        public async Task BasicScripting()
        {
            using (var muxer = GetScriptConn())
            {
                var conn = muxer.GetDatabase();
                var noCache = conn.ScriptEvaluateAsync("return {KEYS[1],KEYS[2],ARGV[1],ARGV[2]}",
                    new RedisKey[] { "key1", "key2" }, new RedisValue[] { "first", "second" });
                var cache = conn.ScriptEvaluateAsync("return {KEYS[1],KEYS[2],ARGV[1],ARGV[2]}",
                    new RedisKey[] { "key1", "key2" }, new RedisValue[] { "first", "second" });
                var results = (string[])await noCache;
                Assert.Equal(4, results.Length);
                Assert.Equal("key1", results[0]);
                Assert.Equal("key2", results[1]);
                Assert.Equal("first", results[2]);
                Assert.Equal("second", results[3]);

                results = (string[])await cache;
                Assert.Equal(4, results.Length);
                Assert.Equal("key1", results[0]);
                Assert.Equal("key2", results[1]);
                Assert.Equal("first", results[2]);
                Assert.Equal("second", results[3]);
            }
        }

        [Fact]
        public void KeysScripting()
        {
            using (var muxer = GetScriptConn())
            {
                var conn = muxer.GetDatabase();
                var key = Me();
                conn.StringSet(key, "bar", flags: CommandFlags.FireAndForget);
                var result = (string)conn.ScriptEvaluate("return redis.call('get', KEYS[1])", new RedisKey[] { key }, null);
                Assert.Equal("bar", result);
            }
        }

        [Fact]
        public async Task TestRandomThingFromForum()
        {
            const string script = @"local currentVal = tonumber(redis.call('GET', KEYS[1]));
                if (currentVal <= 0 ) then return 1 elseif (currentVal - (tonumber(ARGV[1])) < 0 ) then return 0 end;
                return redis.call('INCRBY', KEYS[1], -tonumber(ARGV[1]));";

            using (var muxer = GetScriptConn())
            {
                var prefix = Me();
                var conn = muxer.GetDatabase();
                conn.StringSet(prefix + "A", "0", flags: CommandFlags.FireAndForget);
                conn.StringSet(prefix + "B", "5", flags: CommandFlags.FireAndForget);
                conn.StringSet(prefix + "C", "10", flags: CommandFlags.FireAndForget);

                var a = conn.ScriptEvaluateAsync(script, new RedisKey[] { prefix + "A" }, new RedisValue[] { 6 }).ForAwait();
                var b = conn.ScriptEvaluateAsync(script, new RedisKey[] { prefix + "B" }, new RedisValue[] { 6 }).ForAwait();
                var c = conn.ScriptEvaluateAsync(script, new RedisKey[] { prefix + "C" }, new RedisValue[] { 6 }).ForAwait();

                var vals = await conn.StringGetAsync(new RedisKey[] { prefix + "A", prefix + "B", prefix + "C" }).ForAwait();

                Assert.Equal(1, (long)await a); // exit code when current val is non-positive
                Assert.Equal(0, (long)await b); // exit code when result would be negative
                Assert.Equal(4, (long)await c); // 10 - 6 = 4
                Assert.Equal("0", vals[0]);
                Assert.Equal("5", vals[1]);
                Assert.Equal("4", vals[2]);
            }
        }

        [Fact]
        public void HackyGetPerf()
        {
            using (var muxer = GetScriptConn())
            {
                var key = Me();
                var conn = muxer.GetDatabase();
                conn.StringSet(key + "foo", "bar", flags: CommandFlags.FireAndForget);
                var result = (long)conn.ScriptEvaluate(@"
redis.call('psetex', KEYS[1], 60000, 'timing')
for i = 1,100000 do
    redis.call('set', 'ignore','abc')
end
local timeTaken = 60000 - redis.call('pttl', KEYS[1])
redis.call('del', KEYS[1])
return timeTaken
", new RedisKey[] { key }, null);
                Log(result.ToString());
                Assert.True(result > 0);
            }
        }

        [Fact]
        public async Task MultiIncrWithoutReplies()
        {
            using (var muxer = GetScriptConn())
            {
                var conn = muxer.GetDatabase();
                var prefix = Me();
                // prime some initial values
                conn.KeyDelete(new RedisKey[] { prefix + "a", prefix + "b", prefix + "c" }, CommandFlags.FireAndForget);
                conn.StringIncrement(prefix + "b", flags: CommandFlags.FireAndForget);
                conn.StringIncrement(prefix + "c", flags: CommandFlags.FireAndForget);
                conn.StringIncrement(prefix + "c", flags: CommandFlags.FireAndForget);

                // run the script, passing "a", "b", "c", "c" to
                // increment a & b by 1, c twice
                var result = conn.ScriptEvaluateAsync(
                    "for i,key in ipairs(KEYS) do redis.call('incr', key) end",
                    new RedisKey[] { prefix + "a", prefix + "b", prefix + "c", prefix + "c" }, // <== aka "KEYS" in the script
                    null).ForAwait(); // <== aka "ARGV" in the script

                // check the incremented values
                var a = conn.StringGetAsync(prefix + "a").ForAwait();
                var b = conn.StringGetAsync(prefix + "b").ForAwait();
                var c = conn.StringGetAsync(prefix + "c").ForAwait();

                Assert.True((await result).IsNull, "result");
                Assert.Equal(1, (long)await a);
                Assert.Equal(2, (long)await b);
                Assert.Equal(4, (long)await c);
            }
        }

        [Fact]
        public async Task MultiIncrByWithoutReplies()
        {
            using (var muxer = GetScriptConn())
            {
                var conn = muxer.GetDatabase();
                var prefix = Me();
                // prime some initial values
                conn.KeyDelete(new RedisKey[] { prefix + "a", prefix + "b", prefix + "c" }, CommandFlags.FireAndForget);
                conn.StringIncrement(prefix + "b", flags: CommandFlags.FireAndForget);
                conn.StringIncrement(prefix + "c", flags: CommandFlags.FireAndForget);
                conn.StringIncrement(prefix + "c", flags: CommandFlags.FireAndForget);

                //run the script, passing "a", "b", "c" and 1,2,3
                // increment a &b by 1, c twice
                var result = conn.ScriptEvaluateAsync(
                    "for i,key in ipairs(KEYS) do redis.call('incrby', key, ARGV[i]) end",
                    new RedisKey[] { prefix + "a", prefix + "b", prefix + "c" }, // <== aka "KEYS" in the script
                    new RedisValue[] { 1, 1, 2 }).ForAwait(); // <== aka "ARGV" in the script

                // check the incremented values
                var a = conn.StringGetAsync(prefix + "a").ForAwait();
                var b = conn.StringGetAsync(prefix + "b").ForAwait();
                var c = conn.StringGetAsync(prefix + "c").ForAwait();

                Assert.True((await result).IsNull, "result");
                Assert.Equal(1, (long)await a);
                Assert.Equal(2, (long)await b);
                Assert.Equal(4, (long)await c);
            }
        }

        [Fact]
        public void DisableStringInference()
        {
            using (var muxer = GetScriptConn())
            {
                var conn = muxer.GetDatabase();
                var key = Me();
                conn.StringSet(key, "bar", flags: CommandFlags.FireAndForget);
                var result = (byte[])conn.ScriptEvaluate("return redis.call('get', KEYS[1])", new RedisKey[] { key });
                Assert.Equal("bar", Encoding.UTF8.GetString(result));
            }
        }

        [Fact]
        public void FlushDetection()
        { // we don't expect this to handle everything; we just expect it to be predictable
            using (var muxer = GetScriptConn(allowAdmin: true))
            {
                var conn = muxer.GetDatabase();
                var key = Me();
                conn.StringSet(key, "bar", flags: CommandFlags.FireAndForget);
                var result = (string)conn.ScriptEvaluate("return redis.call('get', KEYS[1])", new RedisKey[] { key }, null);
                Assert.Equal("bar", result);

                // now cause all kinds of problems
                GetServer(muxer).ScriptFlush();

                //expect this one to <strike>fail</strike> just work fine (self-fix)
                conn.ScriptEvaluate("return redis.call('get', KEYS[1])", new RedisKey[] { key }, null);

                result = (string)conn.ScriptEvaluate("return redis.call('get', KEYS[1])", new RedisKey[] { key }, null);
                Assert.Equal("bar", result);
            }
        }

        [Fact]
        public void PrepareScript()
        {
            string[] scripts = { "return redis.call('get', KEYS[1])", "return {KEYS[1],KEYS[2],ARGV[1],ARGV[2]}" };
            using (var muxer = GetScriptConn(allowAdmin: true))
            {
                var server = GetServer(muxer);
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
                var server = GetServer(muxer);

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

        [Fact]
        public void NonAsciiScripts()
        {
            using (var muxer = GetScriptConn())
            {
                const string evil = "return '僕'";
                var conn = muxer.GetDatabase();
                GetServer(muxer).ScriptLoad(evil);

                var result = (string)conn.ScriptEvaluate(evil, null, null);
                Assert.Equal("僕", result);
            }
        }

        [Fact]
        public async Task ScriptThrowsError()
        {
            await Assert.ThrowsAsync<RedisServerException>(async () =>
            {
                using (var muxer = GetScriptConn())
                {
                    var conn = muxer.GetDatabase();
                    try
                    {
                        await conn.ScriptEvaluateAsync("return redis.error_reply('oops')", null, null).ForAwait();
                    }
                    catch (AggregateException ex)
                    {
                        throw ex.InnerExceptions[0];
                    }
                }
            }).ForAwait();
        }

        [Fact]
        public void ScriptThrowsErrorInsideTransaction()
        {
            using (var muxer = GetScriptConn())
            {
                var key = Me();
                var conn = muxer.GetDatabase();
                conn.KeyDelete(key, CommandFlags.FireAndForget);
                var beforeTran = (string)conn.StringGet(key);
                Assert.Null(beforeTran);
                var tran = conn.CreateTransaction();
                {
                    var a = tran.StringIncrementAsync(key);
                    var b = tran.ScriptEvaluateAsync("return redis.error_reply('oops')", null, null);
                    var c = tran.StringIncrementAsync(key);
                    var complete = tran.ExecuteAsync();

                    Assert.True(muxer.Wait(complete));
                    Assert.True(QuickWait(a).IsCompleted, a.Status.ToString());
                    Assert.True(QuickWait(c).IsCompleted, "State: " + c.Status);
                    Assert.Equal(1L, a.Result);
                    Assert.Equal(2L, c.Result);

                    Assert.True(QuickWait(b).IsFaulted, "should be faulted");
                    Assert.Single(b.Exception.InnerExceptions);
                    var ex = b.Exception.InnerExceptions.Single();
                    Assert.IsType<RedisServerException>(ex);
                    Assert.Equal("oops", ex.Message);
                }
                var afterTran = conn.StringGetAsync(key);
                Assert.Equal(2L, (long)conn.Wait(afterTran));
            }
        }
        private static Task<T> QuickWait<T>(Task<T> task)
        {
            if (!task.IsCompleted)
            {
                try { task.Wait(200); } catch { }
            }
            return task;
        }

        [Fact]
        public async Task ChangeDbInScript()
        {
            using (var muxer = GetScriptConn())
            {
                var key = Me();
                muxer.GetDatabase(1).StringSet(key, "db 1", flags: CommandFlags.FireAndForget);
                muxer.GetDatabase(2).StringSet(key, "db 2", flags: CommandFlags.FireAndForget);

                Log("Key: " + key);
                var conn = muxer.GetDatabase(2);
                var evalResult = conn.ScriptEvaluateAsync(@"redis.call('select', 1)
        return redis.call('get','" + key + "')", null, null);
                var getResult = conn.StringGetAsync(key);

                Assert.Equal("db 1", (string)await evalResult);
                // now, our connection thought it was in db 2, but the script changed to db 1
                Assert.Equal("db 2", await getResult);
            }
        }

        [Fact]
        public async Task ChangeDbInTranScript()
        {
            using (var muxer = GetScriptConn())
            {
                var key = Me();
                muxer.GetDatabase(1).StringSet(key, "db 1", flags: CommandFlags.FireAndForget);
                muxer.GetDatabase(2).StringSet(key, "db 2", flags: CommandFlags.FireAndForget);

                var conn = muxer.GetDatabase(2);
                var tran = conn.CreateTransaction();
                var evalResult = tran.ScriptEvaluateAsync(@"redis.call('select', 1)
        return redis.call('get','" + key + "')", null, null);
                var getResult = tran.StringGetAsync(key);
                Assert.True(tran.Execute());

                Assert.Equal("db 1", (string)await evalResult);
                // now, our connection thought it was in db 2, but the script changed to db 1
                Assert.Equal("db 2", await getResult);
            }
        }

        [Fact]
        public void TestBasicScripting()
        {
            using (var conn = Create())
            {
                Skip.IfMissingFeature(conn, nameof(RedisFeatures.Scripting), f => f.Scripting);
                RedisValue newId = Guid.NewGuid().ToString();
                RedisKey key = Me();
                var db = conn.GetDatabase();
                db.KeyDelete(key, CommandFlags.FireAndForget);
                db.HashSet(key, "id", 123, flags: CommandFlags.FireAndForget);

                var wasSet = (bool)db.ScriptEvaluate("if redis.call('hexists', KEYS[1], 'UniqueId') then return redis.call('hset', KEYS[1], 'UniqueId', ARGV[1]) else return 0 end",
                    new RedisKey[] { key }, new RedisValue[] { newId });

                Assert.True(wasSet);

                wasSet = (bool)db.ScriptEvaluate("if redis.call('hexists', KEYS[1], 'UniqueId') then return redis.call('hset', KEYS[1], 'UniqueId', ARGV[1]) else return 0 end",
                    new RedisKey[] { key }, new RedisValue[] { newId });
                Assert.False(wasSet);
            }
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public async Task CheckLoads(bool async)
        {
            using (var conn0 = Create(allowAdmin: true))
            using (var conn1 = Create(allowAdmin: true))
            {
                Skip.IfMissingFeature(conn0, nameof(RedisFeatures.Scripting), f => f.Scripting);
                // note that these are on different connections (so we wouldn't expect
                // the flush to drop the local cache - assume it is a surprise!)
                var server = conn0.GetServer(TestConfig.Current.MasterServerAndPort);
                var db = conn1.GetDatabase();
                const string script = "return 1;";

                // start empty
                server.ScriptFlush();
                Assert.False(server.ScriptExists(script));

                // run once, causes to be cached
                Assert.True((bool)db.ScriptEvaluate(script));
                Assert.True(server.ScriptExists(script));

                // can run again
                Assert.True((bool)db.ScriptEvaluate(script));

                // ditch the scripts; should no longer exist
                db.Ping();
                server.ScriptFlush();
                Assert.False(server.ScriptExists(script));
                db.Ping();

                if (async)
                {
                    // now: fails the first time
                    var ex = await Assert.ThrowsAsync<RedisServerException>(async () => await db.ScriptEvaluateAsync(script).ForAwait()).ForAwait();
                    Assert.Equal("NOSCRIPT No matching script. Please use EVAL.", ex.Message);
                }
                else
                {
                    // just works; magic
                    Assert.True((bool)db.ScriptEvaluate(script));
                }

                // but gets marked as unloaded, so we can use it again...
                Assert.True((bool)db.ScriptEvaluate(script));

                // which will cause it to be cached
                Assert.True(server.ScriptExists(script));
            }
        }

        [Fact]
        public void CompareScriptToDirect()
        {
            const string Script = "return redis.call('incr', KEYS[1])";

            using (var conn = Create(allowAdmin: true))
            {
                Skip.IfMissingFeature(conn, nameof(RedisFeatures.Scripting), f => f.Scripting);
                var server = conn.GetServer(TestConfig.Current.MasterServerAndPort);
                server.ScriptFlush();

                server.ScriptLoad(Script);
                var db = conn.GetDatabase();
                db.Ping(); // k, we're all up to date now; clean db, minimal script cache

                // we're using a pipeline here, so send 1000 messages, but for timing: only care about the last
                const int LOOP = 5000;
                RedisKey key = Me();
                RedisKey[] keys = new[] { key }; // script takes an array

                // run via script
                db.KeyDelete(key, CommandFlags.FireAndForget);
                var watch = Stopwatch.StartNew();
                for (int i = 1; i < LOOP; i++) // the i=1 is to do all-but-one
                {
                    db.ScriptEvaluate(Script, keys, flags: CommandFlags.FireAndForget);
                }
                var scriptResult = db.ScriptEvaluate(Script, keys); // last one we wait for (no F+F)
                watch.Stop();
                TimeSpan scriptTime = watch.Elapsed;

                // run via raw op
                db.KeyDelete(key, CommandFlags.FireAndForget);
                watch = Stopwatch.StartNew();
                for (int i = 1; i < LOOP; i++) // the i=1 is to do all-but-one
                {
                    db.StringIncrement(key, flags: CommandFlags.FireAndForget);
                }
                var directResult = db.StringIncrement(key); // last one we wait for (no F+F)
                watch.Stop();
                TimeSpan directTime = watch.Elapsed;

                Assert.Equal(LOOP, (long)scriptResult);
                Assert.Equal(LOOP, directResult);

                Log("script: {0}ms; direct: {1}ms",
                    scriptTime.TotalMilliseconds,
                    directTime.TotalMilliseconds);
            }
        }

        [Fact]
        public void TestCallByHash()
        {
            const string Script = "return redis.call('incr', KEYS[1])";

            using (var conn = Create(allowAdmin: true))
            {
                Skip.IfMissingFeature(conn, nameof(RedisFeatures.Scripting), f => f.Scripting);
                var server = conn.GetServer(TestConfig.Current.MasterServerAndPort);
                server.ScriptFlush();

                byte[] hash = server.ScriptLoad(Script);

                var db = conn.GetDatabase();
                var key = Me();
                db.KeyDelete(key, CommandFlags.FireAndForget);
                RedisKey[] keys = { key };

                string hexHash = string.Concat(hash.Select(x => x.ToString("X2")));
                Assert.Equal("2BAB3B661081DB58BD2341920E0BA7CF5DC77B25", hexHash);

                db.ScriptEvaluate(hexHash, keys, flags: CommandFlags.FireAndForget);
                db.ScriptEvaluate(hash, keys, flags: CommandFlags.FireAndForget);

                var count = (int)db.StringGet(keys)[0];
                Assert.Equal(2, count);
            }
        }

        [Fact]
        public void SimpleLuaScript()
        {
            const string Script = "return @ident";

            using (var conn = Create(allowAdmin: true))
            {
                Skip.IfMissingFeature(conn, nameof(RedisFeatures.Scripting), f => f.Scripting);
                var server = conn.GetServer(TestConfig.Current.MasterServerAndPort);
                server.ScriptFlush();

                var prepared = LuaScript.Prepare(Script);

                var db = conn.GetDatabase();

                {
                    var val = prepared.Evaluate(db, new { ident = "hello" });
                    Assert.Equal("hello", (string)val);
                }

                {
                    var val = prepared.Evaluate(db, new { ident = 123 });
                    Assert.Equal(123, (int)val);
                }

                {
                    var val = prepared.Evaluate(db, new { ident = 123L });
                    Assert.Equal(123L, (long)val);
                }

                {
                    var val = prepared.Evaluate(db, new { ident = 1.1 });
                    Assert.Equal(1.1, (double)val);
                }

                {
                    var val = prepared.Evaluate(db, new { ident = true });
                    Assert.True((bool)val);
                }

                {
                    var val = prepared.Evaluate(db, new { ident = new byte[] { 4, 5, 6 } });
                    Assert.True(new byte[] { 4, 5, 6 }.SequenceEqual((byte[])val));
                }
            }
        }

        [Fact]
        public void LuaScriptWithKeys()
        {
            const string Script = "redis.call('set', @key, @value)";

            using (var conn = Create(allowAdmin: true))
            {
                Skip.IfMissingFeature(conn, nameof(RedisFeatures.Scripting), f => f.Scripting);
                var server = conn.GetServer(TestConfig.Current.MasterServerAndPort);
                server.ScriptFlush();

                var script = LuaScript.Prepare(Script);

                var db = conn.GetDatabase();
                var key = Me();
                db.KeyDelete(key, CommandFlags.FireAndForget);

                var p = new { key = (RedisKey)key, value = 123 };

                script.Evaluate(db, p);
                var val = db.StringGet(key);
                Assert.Equal(123, (int)val);

                // no super clean way to extract this; so just abuse InternalsVisibleTo
                script.ExtractParameters(p, null, out RedisKey[] keys, out RedisValue[] args);
                Assert.NotNull(keys);
                Assert.Single(keys);
                Assert.Equal(key, keys[0]);
            }
        }

        [Fact]
        public void NoInlineReplacement()
        {
            const string Script = "redis.call('set', @key, 'hello@example')";
            using (var conn = Create(allowAdmin: true))
            {
                Skip.IfMissingFeature(conn, nameof(RedisFeatures.Scripting), f => f.Scripting);
                var server = conn.GetServer(TestConfig.Current.MasterServerAndPort);
                server.ScriptFlush();

                var script = LuaScript.Prepare(Script);

                Assert.Equal("redis.call('set', ARGV[1], 'hello@example')", script.ExecutableScript);

                var db = conn.GetDatabase();
                var key = Me();
                db.KeyDelete(key, CommandFlags.FireAndForget);

                var p = new { key };

                script.Evaluate(db, p, flags: CommandFlags.FireAndForget);
                var val = db.StringGet(key);
                Assert.Equal("hello@example", val);
            }
        }

        [Fact]
        public void EscapeReplacement()
        {
            const string Script = "redis.call('set', @key, @@escapeMe)";
            var script = LuaScript.Prepare(Script);

            Assert.Equal("redis.call('set', ARGV[1], @escapeMe)", script.ExecutableScript);
        }

        [Fact]
        public void SimpleLoadedLuaScript()
        {
            const string Script = "return @ident";

            using (var conn = Create(allowAdmin: true))
            {
                Skip.IfMissingFeature(conn, nameof(RedisFeatures.Scripting), f => f.Scripting);
                var server = conn.GetServer(TestConfig.Current.MasterServerAndPort);
                server.ScriptFlush();

                var prepared = LuaScript.Prepare(Script);
                var loaded = prepared.Load(server);

                var db = conn.GetDatabase();

                {
                    var val = loaded.Evaluate(db, new { ident = "hello" });
                    Assert.Equal("hello", (string)val);
                }

                {
                    var val = loaded.Evaluate(db, new { ident = 123 });
                    Assert.Equal(123, (int)val);
                }

                {
                    var val = loaded.Evaluate(db, new { ident = 123L });
                    Assert.Equal(123L, (long)val);
                }

                {
                    var val = loaded.Evaluate(db, new { ident = 1.1 });
                    Assert.Equal(1.1, (double)val);
                }

                {
                    var val = loaded.Evaluate(db, new { ident = true });
                    Assert.True((bool)val);
                }

                {
                    var val = loaded.Evaluate(db, new { ident = new byte[] { 4, 5, 6 } });
                    Assert.True(new byte[] { 4, 5, 6 }.SequenceEqual((byte[])val));
                }
            }
        }

        [Fact]
        public void LoadedLuaScriptWithKeys()
        {
            const string Script = "redis.call('set', @key, @value)";

            using (var conn = Create(allowAdmin: true))
            {
                Skip.IfMissingFeature(conn, nameof(RedisFeatures.Scripting), f => f.Scripting);
                var server = conn.GetServer(TestConfig.Current.MasterServerAndPort);
                server.ScriptFlush();

                var script = LuaScript.Prepare(Script);
                var prepared = script.Load(server);

                var db = conn.GetDatabase();
                var key = Me();
                db.KeyDelete(key, CommandFlags.FireAndForget);

                var p = new { key = (RedisKey)key, value = 123 };

                prepared.Evaluate(db, p, flags: CommandFlags.FireAndForget);
                var val = db.StringGet(key);
                Assert.Equal(123, (int)val);

                // no super clean way to extract this; so just abuse InternalsVisibleTo
                prepared.Original.ExtractParameters(p, null, out RedisKey[] keys, out RedisValue[] args);
                Assert.NotNull(keys);
                Assert.Single(keys);
                Assert.Equal(key, keys[0]);
            }
        }

        [Fact]
        public void PurgeLuaScriptCache()
        {
            const string Script = "redis.call('set', @PurgeLuaScriptCacheKey, @PurgeLuaScriptCacheValue)";
            var first = LuaScript.Prepare(Script);
            var fromCache = LuaScript.Prepare(Script);

            Assert.True(ReferenceEquals(first, fromCache));

            LuaScript.PurgeCache();
            var shouldBeNew = LuaScript.Prepare(Script);

            Assert.False(ReferenceEquals(first, shouldBeNew));
        }

        private static void _PurgeLuaScriptOnFinalize(string script)
        {
            var first = LuaScript.Prepare(script);
            var fromCache = LuaScript.Prepare(script);
            Assert.True(ReferenceEquals(first, fromCache));
            Assert.Equal(1, LuaScript.GetCachedScriptCount());
        }

        [FactLongRunning]
        public void PurgeLuaScriptOnFinalize()
        {
            const string Script = "redis.call('set', @PurgeLuaScriptOnFinalizeKey, @PurgeLuaScriptOnFinalizeValue)";
            LuaScript.PurgeCache();
            Assert.Equal(0, LuaScript.GetCachedScriptCount());

            // This has to be a separate method to guarantee that the created LuaScript objects go out of scope,
            //   and are thus available to be GC'd
            _PurgeLuaScriptOnFinalize(Script);
            CollectGarbage();

            Assert.Equal(0, LuaScript.GetCachedScriptCount());

            var shouldBeNew = LuaScript.Prepare(Script);
            Assert.Equal(1, LuaScript.GetCachedScriptCount());
        }

        [Fact]
        public void IDatabaseLuaScriptConvenienceMethods()
        {
            const string Script = "redis.call('set', @key, @value)";

            using (var conn = Create(allowAdmin: true))
            {
                Skip.IfMissingFeature(conn, nameof(RedisFeatures.Scripting), f => f.Scripting);
                var script = LuaScript.Prepare(Script);
                var db = conn.GetDatabase();
                var key = Me();
                db.KeyDelete(key, CommandFlags.FireAndForget);
                db.ScriptEvaluate(script, new { key = (RedisKey)key, value = "value" }, flags: CommandFlags.FireAndForget);
                var val = db.StringGet(key);
                Assert.Equal("value", val);

                var prepared = script.Load(conn.GetServer(conn.GetEndPoints()[0]));

                db.ScriptEvaluate(prepared, new { key = (RedisKey)(key + "2"), value = "value2" }, flags: CommandFlags.FireAndForget);
                var val2 = db.StringGet(key + "2");
                Assert.Equal("value2", val2);
            }
        }

        [Fact]
        public void IServerLuaScriptConvenienceMethods()
        {
            const string Script = "redis.call('set', @key, @value)";

            using (var conn = Create(allowAdmin: true))
            {
                Skip.IfMissingFeature(conn, nameof(RedisFeatures.Scripting), f => f.Scripting);
                var script = LuaScript.Prepare(Script);
                var server = conn.GetServer(conn.GetEndPoints()[0]);
                var db = conn.GetDatabase();
                var key = Me();
                db.KeyDelete(key, CommandFlags.FireAndForget);

                var prepared = server.ScriptLoad(script);

                db.ScriptEvaluate(prepared, new { key = (RedisKey)key, value = "value3" });
                var val = db.StringGet(key);
                Assert.Equal("value3", val);
            }
        }

        [Fact]
        public void LuaScriptPrefixedKeys()
        {
            const string Script = "redis.call('set', @key, @value)";
            var prepared = LuaScript.Prepare(Script);
            var key = Me();
            var p = new { key = (RedisKey)key, value = "hello" };

            // no super clean way to extract this; so just abuse InternalsVisibleTo
            prepared.ExtractParameters(p, "prefix-", out RedisKey[] keys, out RedisValue[] args);
            Assert.NotNull(keys);
            Assert.Single(keys);
            Assert.Equal("prefix-" + key, keys[0]);
            Assert.Equal(2, args.Length);
            Assert.Equal("prefix-" +  key, args[0]);
            Assert.Equal("hello", args[1]);
        }

        [Fact]
        public void LuaScriptWithWrappedDatabase()
        {
            const string Script = "redis.call('set', @key, @value)";

            using (var conn = Create(allowAdmin: true))
            {
                Skip.IfMissingFeature(conn, nameof(RedisFeatures.Scripting), f => f.Scripting);
                var db = conn.GetDatabase();
                var wrappedDb = KeyspaceIsolation.DatabaseExtensions.WithKeyPrefix(db, "prefix-");
                var key = Me();
                db.KeyDelete(key, CommandFlags.FireAndForget);

                var prepared = LuaScript.Prepare(Script);
                wrappedDb.ScriptEvaluate(prepared, new { key = (RedisKey)key, value = 123 });
                var val1 = wrappedDb.StringGet(key);
                Assert.Equal(123, (int)val1);

                var val2 = db.StringGet("prefix-" + key);
                Assert.Equal(123, (int)val2);

                var val3 = db.StringGet(key);
                Assert.True(val3.IsNull);
            }
        }

        [Fact]
        public void LoadedLuaScriptWithWrappedDatabase()
        {
            const string Script = "redis.call('set', @key, @value)";

            using (var conn = Create(allowAdmin: true))
            {
                Skip.IfMissingFeature(conn, nameof(RedisFeatures.Scripting), f => f.Scripting);
                var db = conn.GetDatabase();
                var wrappedDb = KeyspaceIsolation.DatabaseExtensions.WithKeyPrefix(db, "prefix2-");
                var key = Me();
                db.KeyDelete(key, CommandFlags.FireAndForget);

                var server = conn.GetServer(conn.GetEndPoints()[0]);
                var prepared = LuaScript.Prepare(Script).Load(server);
                wrappedDb.ScriptEvaluate(prepared, new { key = (RedisKey)key, value = 123 }, flags: CommandFlags.FireAndForget);
                var val1 = wrappedDb.StringGet(key);
                Assert.Equal(123, (int)val1);

                var val2 = db.StringGet("prefix2-" + key);
                Assert.Equal(123, (int)val2);

                var val3 = db.StringGet(key);
                Assert.True(val3.IsNull);
            }
        }

        [Fact]
        public void ScriptWithKeyPrefixViaTokens()
        {
            using (var conn = Create())
            {
                var p = conn.GetDatabase().WithKeyPrefix("prefix/");

                var args = new { x = "abc", y = (RedisKey)"def", z = 123 };
                var script = LuaScript.Prepare(@"
local arr = {};
arr[1] = @x;
arr[2] = @y;
arr[3] = @z;
return arr;
");
                var result = (RedisValue[])p.ScriptEvaluate(script, args);
                Assert.Equal("abc", (string)result[0]);
                Assert.Equal("prefix/def", (string)result[1]);
                Assert.Equal("123", (string)result[2]);
            }
        }

        [Fact]
        public void ScriptWithKeyPrefixViaArrays()
        {
            using (var conn = Create())
            {
                var p = conn.GetDatabase().WithKeyPrefix("prefix/");

                var args = new { x = "abc", y = (RedisKey)"def", z = 123 };
                const string script = @"
local arr = {};
arr[1] = ARGV[1];
arr[2] = KEYS[1];
arr[3] = ARGV[2];
return arr;
";
                var result = (RedisValue[])p.ScriptEvaluate(script, new RedisKey[] { "def" }, new RedisValue[] { "abc", 123 });
                Assert.Equal("abc", (string)result[0]);
                Assert.Equal("prefix/def", (string)result[1]);
                Assert.Equal("123", (string)result[2]);
            }
        }

        [Fact]
        public void ScriptWithKeyPrefixCompare()
        {
            using (var conn = Create())
            {
                var p = conn.GetDatabase().WithKeyPrefix("prefix/");
                var args = new { k = (RedisKey)"key", s = "str", v = 123 };
                LuaScript lua = LuaScript.Prepare(@"return {@k, @s, @v}");
                var viaArgs = (RedisValue[])p.ScriptEvaluate(lua, args);

                var viaArr = (RedisValue[])p.ScriptEvaluate(@"return {KEYS[1], ARGV[1], ARGV[2]}", new[] { args.k }, new RedisValue[] { args.s, args.v });
                Assert.Equal(string.Join(",", viaArr), string.Join(",", viaArgs));
            }
        }
    }
}
