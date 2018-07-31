using System;
using System.Diagnostics;
using System.Linq;
using Xunit;
using Xunit.Abstractions;

namespace StackExchange.Redis.Tests
{
    public class Scripting : TestBase
    {
        public Scripting(ITestOutputHelper output) : base (output) { }

        [Fact]
        public void TestBasicScripting()
        {
            using (var conn = Create())
            {
                Skip.IfMissingFeature(conn, nameof(RedisFeatures.Scripting), f => f.Scripting);
                RedisValue newId = Guid.NewGuid().ToString();
                RedisKey custKey = Me();
                var db = conn.GetDatabase();
                db.KeyDelete(custKey);
                db.HashSet(custKey, "id", 123);

                var wasSet = (bool)db.ScriptEvaluate("if redis.call('hexists', KEYS[1], 'UniqueId') then return redis.call('hset', KEYS[1], 'UniqueId', ARGV[1]) else return 0 end",
                    new RedisKey[] { custKey }, new RedisValue[] { newId });

                Assert.True(wasSet);

                wasSet = (bool)db.ScriptEvaluate("if redis.call('hexists', KEYS[1], 'UniqueId') then return redis.call('hset', KEYS[1], 'UniqueId', ARGV[1]) else return 0 end",
                    new RedisKey[] { custKey }, new RedisValue[] { newId });
                Assert.False(wasSet);
            }
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void CheckLoads(bool async)
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
                    try
                    {
                        db.Wait(db.ScriptEvaluateAsync(script));
                        Assert.True(false, "ScriptEvaluateAsync should fail");
                    }
                    catch (AggregateException ex)
                    {
                        Assert.Single(ex.InnerExceptions);
                        Assert.IsType<RedisServerException>(ex.InnerExceptions[0]);
                        Assert.Equal("NOSCRIPT No matching script. Please use EVAL.", ex.InnerExceptions[0].Message);
                    }
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
                for (int i = 1; i < LOOP; i++) // the i=1 is to do all-but-one
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

                Assert.Equal(LOOP, (long)scriptResult);
                Assert.Equal(LOOP, directResult);

                Output.WriteLine("script: {0}ms; direct: {1}ms",
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
                server.FlushAllDatabases();
                server.ScriptFlush();

                byte[] hash = server.ScriptLoad(Script);

                var db = conn.GetDatabase();
                RedisKey[] keys = { Me() };

                string hexHash = string.Concat(hash.Select(x => x.ToString("X2")));
                Assert.Equal("2BAB3B661081DB58BD2341920E0BA7CF5DC77B25", hexHash);

                db.ScriptEvaluate(hexHash, keys);
                db.ScriptEvaluate(hash, keys);

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
                server.FlushAllDatabases();
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
                server.FlushAllDatabases();
                server.ScriptFlush();

                var script = LuaScript.Prepare(Script);

                var db = conn.GetDatabase();

                var p = new { key = (RedisKey)"testkey", value = 123 };

                script.Evaluate(db, p);
                var val = db.StringGet("testkey");
                Assert.Equal(123, (int)val);

                // no super clean way to extract this; so just abuse InternalsVisibleTo
                script.ExtractParameters(p, null, out RedisKey[] keys, out RedisValue[] args);
                Assert.NotNull(keys);
                Assert.Single(keys);
                Assert.Equal("testkey", keys[0]);
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
                server.FlushAllDatabases();
                server.ScriptFlush();

                var script = LuaScript.Prepare(Script);

                Assert.Equal("redis.call('set', ARGV[1], 'hello@example')", script.ExecutableScript);

                var db = conn.GetDatabase();

                var p = new { key = (RedisKey)"key" };

                script.Evaluate(db, p);
                var val = db.StringGet("key");
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
                server.FlushAllDatabases();
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
                server.FlushAllDatabases();
                server.ScriptFlush();

                var script = LuaScript.Prepare(Script);
                var prepared = script.Load(server);

                var db = conn.GetDatabase();

                var p = new { key = (RedisKey)"testkey", value = 123 };

                prepared.Evaluate(db, p);
                var val = db.StringGet("testkey");
                Assert.Equal(123, (int)val);

                // no super clean way to extract this; so just abuse InternalsVisibleTo
                prepared.Original.ExtractParameters(p, null, out RedisKey[] keys, out RedisValue[] args);
                Assert.NotNull(keys);
                Assert.Single(keys);
                Assert.Equal("testkey", keys[0]);
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

        [Fact]
        public void PurgeLuaScriptOnFinalize()
        {
            const string Script = "redis.call('set', @PurgeLuaScriptOnFinalizeKey, @PurgeLuaScriptOnFinalizeValue)";
            LuaScript.PurgeCache();
            Assert.Equal(0, LuaScript.GetCachedScriptCount());

            // This has to be a separate method to guarantee that the created LuaScript objects go out of scope,
            //   and are thus available to be GC'd
            _PurgeLuaScriptOnFinalize(Script);

            GC.Collect(2, GCCollectionMode.Forced, blocking: true);
            GC.WaitForPendingFinalizers();

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
                db.ScriptEvaluate(script, new { key = (RedisKey)"key", value = "value" });
                var val = db.StringGet("key");
                Assert.Equal("value", val);

                var prepared = script.Load(conn.GetServer(conn.GetEndPoints()[0]));

                db.ScriptEvaluate(prepared, new { key = (RedisKey)"key2", value = "value2" });
                var val2 = db.StringGet("key2");
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

                var prepared = server.ScriptLoad(script);

                db.ScriptEvaluate(prepared, new { key = (RedisKey)"key3", value = "value3" });
                var val = db.StringGet("key3");
                Assert.Equal("value3", val);
            }
        }

        [Fact]
        public void LuaScriptPrefixedKeys()
        {
            const string Script = "redis.call('set', @key, @value)";
            var prepared = LuaScript.Prepare(Script);
            var p = new { key = (RedisKey)"key", value = "hello" };

            // no super clean way to extract this; so just abuse InternalsVisibleTo
            prepared.ExtractParameters(p, "prefix-", out RedisKey[] keys, out RedisValue[] args);
            Assert.NotNull(keys);
            Assert.Single(keys);
            Assert.Equal("prefix-key", keys[0]);
            Assert.Equal(2, args.Length);
            Assert.Equal("prefix-key", args[0]);
            Assert.Equal("hello", args[1]);
        }

        [Fact]
        public void LuaScriptWithWrappedDatabase()
        {
            const string Script = "redis.call('set', @key, @value)";

            using (var conn = Create(allowAdmin: true))
            {
                Skip.IfMissingFeature(conn, nameof(RedisFeatures.Scripting), f => f.Scripting);
                var db = conn.GetDatabase(0);
                var wrappedDb = KeyspaceIsolation.DatabaseExtensions.WithKeyPrefix(db, "prefix-");

                var prepared = LuaScript.Prepare(Script);
                wrappedDb.ScriptEvaluate(prepared, new { key = (RedisKey)"mykey", value = 123 });
                var val1 = wrappedDb.StringGet("mykey");
                Assert.Equal(123, (int)val1);

                var val2 = db.StringGet("prefix-mykey");
                Assert.Equal(123, (int)val2);

                var val3 = db.StringGet("mykey");
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
                var db = conn.GetDatabase(0);
                var wrappedDb = KeyspaceIsolation.DatabaseExtensions.WithKeyPrefix(db, "prefix2-");

                var server = conn.GetServer(conn.GetEndPoints()[0]);
                var prepared = LuaScript.Prepare(Script).Load(server);
                wrappedDb.ScriptEvaluate(prepared, new { key = (RedisKey)"mykey", value = 123 });
                var val1 = wrappedDb.StringGet("mykey");
                Assert.Equal(123, (int)val1);

                var val2 = db.StringGet("prefix2-mykey");
                Assert.Equal(123, (int)val2);

                var val3 = db.StringGet("mykey");
                Assert.True(val3.IsNull);
            }
        }
    }
}
