using System;
using System.Diagnostics;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using StackExchange.Redis.KeyspaceIsolation;
using Xunit;
using Xunit.Abstractions;

namespace StackExchange.Redis.Tests;

[RunPerProtocol]
[Collection(SharedConnectionFixture.Key)]
public class ScriptingTests : TestBase
{
    public ScriptingTests(ITestOutputHelper output, SharedConnectionFixture fixture) : base(output, fixture) { }

    private IConnectionMultiplexer GetScriptConn(bool allowAdmin = false)
    {
        int syncTimeout = 5000;
        if (Debugger.IsAttached) syncTimeout = 500000;
        return Create(allowAdmin: allowAdmin, syncTimeout: syncTimeout, require: RedisFeatures.v2_6_0);
    }

    [Fact]
    public void ClientScripting()
    {
        using var conn = GetScriptConn();
        _ = conn.GetDatabase().ScriptEvaluate("return redis.call('info','server')", null, null);
    }

    [Fact]
    public async Task BasicScripting()
    {
        using var conn = GetScriptConn();

        var db = conn.GetDatabase();
        var noCache = db.ScriptEvaluateAsync("return {KEYS[1],KEYS[2],ARGV[1],ARGV[2]}",
            new RedisKey[] { "key1", "key2" }, new RedisValue[] { "first", "second" });
        var cache = db.ScriptEvaluateAsync("return {KEYS[1],KEYS[2],ARGV[1],ARGV[2]}",
            new RedisKey[] { "key1", "key2" }, new RedisValue[] { "first", "second" });
        var results = (string[]?)await noCache;
        Assert.NotNull(results);
        Assert.Equal(4, results.Length);
        Assert.Equal("key1", results[0]);
        Assert.Equal("key2", results[1]);
        Assert.Equal("first", results[2]);
        Assert.Equal("second", results[3]);

        results = (string[]?)await cache;
        Assert.NotNull(results);
        Assert.Equal(4, results.Length);
        Assert.Equal("key1", results[0]);
        Assert.Equal("key2", results[1]);
        Assert.Equal("first", results[2]);
        Assert.Equal("second", results[3]);
    }

    [Fact]
    public void KeysScripting()
    {
        using var conn = GetScriptConn();

        var db = conn.GetDatabase();
        var key = Me();
        db.StringSet(key, "bar", flags: CommandFlags.FireAndForget);
        var result = (string?)db.ScriptEvaluate("return redis.call('get', KEYS[1])", new RedisKey[] { key }, null);
        Assert.Equal("bar", result);
    }

    [Fact]
    public async Task TestRandomThingFromForum()
    {
        const string script = @"local currentVal = tonumber(redis.call('GET', KEYS[1]));
                if (currentVal <= 0 ) then return 1 elseif (currentVal - (tonumber(ARGV[1])) < 0 ) then return 0 end;
                return redis.call('INCRBY', KEYS[1], -tonumber(ARGV[1]));";

        using var conn = GetScriptConn();

        var prefix = Me();
        var db = conn.GetDatabase();
        db.StringSet(prefix + "A", "0", flags: CommandFlags.FireAndForget);
        db.StringSet(prefix + "B", "5", flags: CommandFlags.FireAndForget);
        db.StringSet(prefix + "C", "10", flags: CommandFlags.FireAndForget);

        var a = db.ScriptEvaluateAsync(script, new RedisKey[] { prefix + "A" }, new RedisValue[] { 6 }).ForAwait();
        var b = db.ScriptEvaluateAsync(script, new RedisKey[] { prefix + "B" }, new RedisValue[] { 6 }).ForAwait();
        var c = db.ScriptEvaluateAsync(script, new RedisKey[] { prefix + "C" }, new RedisValue[] { 6 }).ForAwait();

        var vals = await db.StringGetAsync(new RedisKey[] { prefix + "A", prefix + "B", prefix + "C" }).ForAwait();

        Assert.Equal(1, (long)await a); // exit code when current val is non-positive
        Assert.Equal(0, (long)await b); // exit code when result would be negative
        Assert.Equal(4, (long)await c); // 10 - 6 = 4
        Assert.Equal("0", vals[0]);
        Assert.Equal("5", vals[1]);
        Assert.Equal("4", vals[2]);
    }

    [Fact]
    public async Task MultiIncrWithoutReplies()
    {
        using var conn = GetScriptConn();

        var db = conn.GetDatabase();
        var prefix = Me();
        // prime some initial values
        db.KeyDelete(new RedisKey[] { prefix + "a", prefix + "b", prefix + "c" }, CommandFlags.FireAndForget);
        db.StringIncrement(prefix + "b", flags: CommandFlags.FireAndForget);
        db.StringIncrement(prefix + "c", flags: CommandFlags.FireAndForget);
        db.StringIncrement(prefix + "c", flags: CommandFlags.FireAndForget);

        // run the script, passing "a", "b", "c", "c" to
        // increment a & b by 1, c twice
        var result = db.ScriptEvaluateAsync(
            "for i,key in ipairs(KEYS) do redis.call('incr', key) end",
            new RedisKey[] { prefix + "a", prefix + "b", prefix + "c", prefix + "c" }, // <== aka "KEYS" in the script
            null).ForAwait(); // <== aka "ARGV" in the script

        // check the incremented values
        var a = db.StringGetAsync(prefix + "a").ForAwait();
        var b = db.StringGetAsync(prefix + "b").ForAwait();
        var c = db.StringGetAsync(prefix + "c").ForAwait();

        var r = await result;
        Assert.NotNull(r);
        Assert.True(r.IsNull, "result");
        Assert.Equal(1, (long)await a);
        Assert.Equal(2, (long)await b);
        Assert.Equal(4, (long)await c);
    }

    [Fact]
    public async Task MultiIncrByWithoutReplies()
    {
        using var conn = GetScriptConn();

        var db = conn.GetDatabase();
        var prefix = Me();
        // prime some initial values
        db.KeyDelete(new RedisKey[] { prefix + "a", prefix + "b", prefix + "c" }, CommandFlags.FireAndForget);
        db.StringIncrement(prefix + "b", flags: CommandFlags.FireAndForget);
        db.StringIncrement(prefix + "c", flags: CommandFlags.FireAndForget);
        db.StringIncrement(prefix + "c", flags: CommandFlags.FireAndForget);

        //run the script, passing "a", "b", "c" and 1,2,3
        // increment a &b by 1, c twice
        var result = db.ScriptEvaluateAsync(
            "for i,key in ipairs(KEYS) do redis.call('incrby', key, ARGV[i]) end",
            new RedisKey[] { prefix + "a", prefix + "b", prefix + "c" }, // <== aka "KEYS" in the script
            new RedisValue[] { 1, 1, 2 }).ForAwait(); // <== aka "ARGV" in the script

        // check the incremented values
        var a = db.StringGetAsync(prefix + "a").ForAwait();
        var b = db.StringGetAsync(prefix + "b").ForAwait();
        var c = db.StringGetAsync(prefix + "c").ForAwait();

        Assert.True((await result).IsNull, "result");
        Assert.Equal(1, (long)await a);
        Assert.Equal(2, (long)await b);
        Assert.Equal(4, (long)await c);
    }

    [Fact]
    public void DisableStringInference()
    {
        using var conn = GetScriptConn();

        var db = conn.GetDatabase();
        var key = Me();
        db.StringSet(key, "bar", flags: CommandFlags.FireAndForget);
        var result = (byte[]?)db.ScriptEvaluate("return redis.call('get', KEYS[1])", new RedisKey[] { key });
        Assert.NotNull(result);
        Assert.Equal("bar", Encoding.UTF8.GetString(result));
    }

    [Fact]
    public void FlushDetection()
    {
        // we don't expect this to handle everything; we just expect it to be predictable
        using var conn = GetScriptConn(allowAdmin: true);

        var db = conn.GetDatabase();
        var key = Me();
        db.StringSet(key, "bar", flags: CommandFlags.FireAndForget);
        var result = (string?)db.ScriptEvaluate("return redis.call('get', KEYS[1])", new RedisKey[] { key }, null);
        Assert.Equal("bar", result);

        // now cause all kinds of problems
        GetServer(conn).ScriptFlush();

        //expect this one to <strike>fail</strike> just work fine (self-fix)
        db.ScriptEvaluate("return redis.call('get', KEYS[1])", new RedisKey[] { key }, null);

        result = (string?)db.ScriptEvaluate("return redis.call('get', KEYS[1])", new RedisKey[] { key }, null);
        Assert.Equal("bar", result);
    }

    [Fact]
    public void PrepareScript()
    {
        string[] scripts = { "return redis.call('get', KEYS[1])", "return {KEYS[1],KEYS[2],ARGV[1],ARGV[2]}" };
        using (var conn = GetScriptConn(allowAdmin: true))
        {
            var server = GetServer(conn);
            server.ScriptFlush();

            // when vanilla
            server.ScriptLoad(scripts[0]);
            server.ScriptLoad(scripts[1]);

            //when known to exist
            server.ScriptLoad(scripts[0]);
            server.ScriptLoad(scripts[1]);
        }
        using (var conn = GetScriptConn())
        {
            var server = GetServer(conn);

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
        using var conn = GetScriptConn();

        const string evil = "return '僕'";
        var db = conn.GetDatabase();
        GetServer(conn).ScriptLoad(evil);

        var result = (string?)db.ScriptEvaluate(evil, null, null);
        Assert.Equal("僕", result);
    }

    [Fact]
    public async Task ScriptThrowsError()
    {
        using var conn = GetScriptConn();
        await Assert.ThrowsAsync<RedisServerException>(async () =>
        {
            var db = conn.GetDatabase();
            try
            {
                await db.ScriptEvaluateAsync("return redis.error_reply('oops')", null, null).ForAwait();
            }
            catch (AggregateException ex)
            {
                throw ex.InnerExceptions[0];
            }
        }).ForAwait();
    }

    [Fact]
    public void ScriptThrowsErrorInsideTransaction()
    {
        using var conn = GetScriptConn();

        var key = Me();
        var db = conn.GetDatabase();
        db.KeyDelete(key, CommandFlags.FireAndForget);
        var beforeTran = (string?)db.StringGet(key);
        Assert.Null(beforeTran);
        var tran = db.CreateTransaction();
        {
            var a = tran.StringIncrementAsync(key);
            var b = tran.ScriptEvaluateAsync("return redis.error_reply('oops')", null, null);
            var c = tran.StringIncrementAsync(key);
            var complete = tran.ExecuteAsync();

            Assert.True(conn.Wait(complete));
            Assert.True(QuickWait(a).IsCompleted, a.Status.ToString());
            Assert.True(QuickWait(c).IsCompleted, "State: " + c.Status);
            Assert.Equal(1L, a.Result);
            Assert.Equal(2L, c.Result);

            Assert.True(QuickWait(b).IsFaulted, "should be faulted");
            Assert.NotNull(b.Exception);
            Assert.Single(b.Exception.InnerExceptions);
            var ex = b.Exception.InnerExceptions.Single();
            Assert.IsType<RedisServerException>(ex);
            // 7.0 slightly changes the error format, accept either.
            Assert.Contains(ex.Message, new[] { "ERR oops", "oops" });
        }
        var afterTran = db.StringGetAsync(key);
        Assert.Equal(2L, (long)db.Wait(afterTran));
    }
    private static Task<T> QuickWait<T>(Task<T> task)
    {
        if (!task.IsCompleted)
        {
            try { task.Wait(200); } catch { /* But don't error */ }
        }
        return task;
    }

    [Fact]
    public async Task ChangeDbInScript()
    {
        using var conn = GetScriptConn();

        var key = Me();
        conn.GetDatabase(1).StringSet(key, "db 1", flags: CommandFlags.FireAndForget);
        conn.GetDatabase(2).StringSet(key, "db 2", flags: CommandFlags.FireAndForget);

        Log("Key: " + key);
        var db = conn.GetDatabase(2);
        var evalResult = db.ScriptEvaluateAsync(@"redis.call('select', 1)
        return redis.call('get','" + key + "')", null, null);
        var getResult = db.StringGetAsync(key);

        Assert.Equal("db 1", (string?)await evalResult);
        // now, our connection thought it was in db 2, but the script changed to db 1
        Assert.Equal("db 2", await getResult);
    }

    [Fact]
    public async Task ChangeDbInTranScript()
    {
        using var conn = GetScriptConn();

        var key = Me();
        conn.GetDatabase(1).StringSet(key, "db 1", flags: CommandFlags.FireAndForget);
        conn.GetDatabase(2).StringSet(key, "db 2", flags: CommandFlags.FireAndForget);

        var db = conn.GetDatabase(2);
        var tran = db.CreateTransaction();
        var evalResult = tran.ScriptEvaluateAsync(@"redis.call('select', 1)
        return redis.call('get','" + key + "')", null, null);
        var getResult = tran.StringGetAsync(key);
        Assert.True(tran.Execute());

        Assert.Equal("db 1", (string?)await evalResult);
        // now, our connection thought it was in db 2, but the script changed to db 1
        Assert.Equal("db 2", await getResult);
    }

    [Fact]
    public void TestBasicScripting()
    {
        using var conn = Create(require: RedisFeatures.v2_6_0);

        RedisValue newId = Guid.NewGuid().ToString();
        RedisKey key = Me();
        var db = conn.GetDatabase();
        db.KeyDelete(key, CommandFlags.FireAndForget);
        db.HashSet(key, "id", 123, flags: CommandFlags.FireAndForget);

        var wasSet = (bool)db.ScriptEvaluate("if redis.call('hexists', KEYS[1], 'UniqueId') then return redis.call('hset', KEYS[1], 'UniqueId', ARGV[1]) else return 0 end",
            new[] { key }, new[] { newId });

        Assert.True(wasSet);

        wasSet = (bool)db.ScriptEvaluate("if redis.call('hexists', KEYS[1], 'UniqueId') then return redis.call('hset', KEYS[1], 'UniqueId', ARGV[1]) else return 0 end",
            new[] { key }, new[] { newId });
        Assert.False(wasSet);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task CheckLoads(bool async)
    {
        using var conn0 = Create(allowAdmin: true, require: RedisFeatures.v2_6_0);
        using var conn1 = Create(allowAdmin: true, require: RedisFeatures.v2_6_0);

        // note that these are on different connections (so we wouldn't expect
        // the flush to drop the local cache - assume it is a surprise!)
        var server = conn0.GetServer(TestConfig.Current.PrimaryServerAndPort);
        var db = conn1.GetDatabase();
        const string script = "return 1;";

        // start empty
        server.ScriptFlush();
        Assert.False(server.ScriptExists(script));

        // run once, causes to be cached
        Assert.True(await EvaluateScript());

        Assert.True(server.ScriptExists(script));

        // can run again
        Assert.True(await EvaluateScript());

        // ditch the scripts; should no longer exist
        db.Ping();
        server.ScriptFlush();
        Assert.False(server.ScriptExists(script));
        db.Ping();

        // just works; magic
        Assert.True(await EvaluateScript());

        // but gets marked as unloaded, so we can use it again...
        Assert.True(await EvaluateScript());

        // which will cause it to be cached
        Assert.True(server.ScriptExists(script));

        async Task<bool> EvaluateScript()
        {
            return async ?
            (bool)await db!.ScriptEvaluateAsync(script) :
            (bool)db!.ScriptEvaluate(script);
        }
    }

    [Fact]
    public void CompareScriptToDirect()
    {
        using var conn = Create(allowAdmin: true, require: RedisFeatures.v2_6_0);

        const string Script = "return redis.call('incr', KEYS[1])";
        var server = conn.GetServer(TestConfig.Current.PrimaryServerAndPort);
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

    [Fact]
    public void TestCallByHash()
    {
        using var conn = Create(allowAdmin: true, require: RedisFeatures.v2_6_0);

        const string Script = "return redis.call('incr', KEYS[1])";
        var server = conn.GetServer(TestConfig.Current.PrimaryServerAndPort);
        server.ScriptFlush();

        byte[]? hash = server.ScriptLoad(Script);
        Assert.NotNull(hash);

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

    [Fact]
    public void SimpleLuaScript()
    {
        using var conn = Create(allowAdmin: true, require: RedisFeatures.v2_6_0);

        const string Script = "return @ident";
        var server = conn.GetServer(TestConfig.Current.PrimaryServerAndPort);
        server.ScriptFlush();

        var prepared = LuaScript.Prepare(Script);

        var db = conn.GetDatabase();

        {
            var val = prepared.Evaluate(db, new { ident = "hello" });
            Assert.Equal("hello", (string?)val);
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
            var valArray = (byte[]?)val;
            Assert.NotNull(valArray);
            Assert.True(new byte[] { 4, 5, 6 }.SequenceEqual(valArray));
        }

        {
            var val = prepared.Evaluate(db, new { ident = new ReadOnlyMemory<byte>(new byte[] { 4, 5, 6 }) });
            var valArray = (byte[]?)val;
            Assert.NotNull(valArray);
            Assert.True(new byte[] { 4, 5, 6 }.SequenceEqual(valArray));
        }
    }

    [Fact]
    public void SimpleRawScriptEvaluate()
    {
        using var conn = Create(allowAdmin: true, require: RedisFeatures.v2_6_0);

        const string Script = "return ARGV[1]";
        var server = conn.GetServer(TestConfig.Current.PrimaryServerAndPort);
        server.ScriptFlush();

        var db = conn.GetDatabase();

        {
            var val = db.ScriptEvaluate(Script, values: new RedisValue[] { "hello" });
            Assert.Equal("hello", (string?)val);
        }

        {
            var val = db.ScriptEvaluate(Script, values: new RedisValue[] { 123 });
            Assert.Equal(123, (int)val);
        }

        {
            var val = db.ScriptEvaluate(Script, values: new RedisValue[] { 123L });
            Assert.Equal(123L, (long)val);
        }

        {
            var val = db.ScriptEvaluate(Script, values: new RedisValue[] { 1.1 });
            Assert.Equal(1.1, (double)val);
        }

        {
            var val = db.ScriptEvaluate(Script, values: new RedisValue[] { true });
            Assert.True((bool)val);
        }

        {
            var val = db.ScriptEvaluate(Script, values: new RedisValue[] { new byte[] { 4, 5, 6 } });
            var valArray = (byte[]?)val;
            Assert.NotNull(valArray);
            Assert.True(new byte[] { 4, 5, 6 }.SequenceEqual(valArray));
        }

        {
            var val = db.ScriptEvaluate(Script, values: new RedisValue[] { new ReadOnlyMemory<byte>(new byte[] { 4, 5, 6 }) });
            var valArray = (byte[]?)val;
            Assert.NotNull(valArray);
            Assert.True(new byte[] { 4, 5, 6 }.SequenceEqual(valArray));
        }
    }

    [Fact]
    public void LuaScriptWithKeys()
    {
        using var conn = Create(allowAdmin: true, require: RedisFeatures.v2_6_0);

        const string Script = "redis.call('set', @key, @value)";
        var server = conn.GetServer(TestConfig.Current.PrimaryServerAndPort);
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
        script.ExtractParameters(p, null, out RedisKey[]? keys, out _);
        Assert.NotNull(keys);
        Assert.Single(keys);
        Assert.Equal(key, keys[0]);
    }

    [Fact]
    public void NoInlineReplacement()
    {
        using var conn = Create(allowAdmin: true, require: RedisFeatures.v2_6_0);

        const string Script = "redis.call('set', @key, 'hello@example')";
        var server = conn.GetServer(TestConfig.Current.PrimaryServerAndPort);
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
        using var conn = Create(allowAdmin: true, require: RedisFeatures.v2_6_0);

        const string Script = "return @ident";
        var server = conn.GetServer(TestConfig.Current.PrimaryServerAndPort);
        server.ScriptFlush();

        var prepared = LuaScript.Prepare(Script);
        var loaded = prepared.Load(server);

        var db = conn.GetDatabase();

        {
            var val = loaded.Evaluate(db, new { ident = "hello" });
            Assert.Equal("hello", (string?)val);
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
            var valArray = (byte[]?)val;
            Assert.NotNull(valArray);
            Assert.True(new byte[] { 4, 5, 6 }.SequenceEqual(valArray));
        }

        {
            var val = loaded.Evaluate(db, new { ident = new ReadOnlyMemory<byte>(new byte[] { 4, 5, 6 }) });
            var valArray = (byte[]?)val;
            Assert.NotNull(valArray);
            Assert.True(new byte[] { 4, 5, 6 }.SequenceEqual(valArray));
        }
    }

    [Fact]
    public void LoadedLuaScriptWithKeys()
    {
        using var conn = Create(allowAdmin: true, require: RedisFeatures.v2_6_0);

        const string Script = "redis.call('set', @key, @value)";
        var server = conn.GetServer(TestConfig.Current.PrimaryServerAndPort);
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
        prepared.Original.ExtractParameters(p, null, out RedisKey[]? keys, out _);
        Assert.NotNull(keys);
        Assert.Single(keys);
        Assert.Equal(key, keys[0]);
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

    private static void PurgeLuaScriptOnFinalizeImpl(string script)
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
        PurgeLuaScriptOnFinalizeImpl(Script);
        CollectGarbage();

        Assert.Equal(0, LuaScript.GetCachedScriptCount());

        LuaScript.Prepare(Script);
        Assert.Equal(1, LuaScript.GetCachedScriptCount());
    }

    [Fact]
    public void IDatabaseLuaScriptConvenienceMethods()
    {
        using var conn = Create(allowAdmin: true, require: RedisFeatures.v2_6_0);

        const string Script = "redis.call('set', @key, @value)";
        var script = LuaScript.Prepare(Script);
        var db = conn.GetDatabase();
        var key = Me();
        db.KeyDelete(key, CommandFlags.FireAndForget);
        db.ScriptEvaluate(script, new { key = (RedisKey)key, value = "value" });
        var val = db.StringGet(key);
        Assert.Equal("value", val);

        var prepared = script.Load(conn.GetServer(conn.GetEndPoints()[0]));

        db.ScriptEvaluate(prepared, new { key = (RedisKey)(key + "2"), value = "value2" });
        var val2 = db.StringGet(key + "2");
        Assert.Equal("value2", val2);
    }

    [Fact]
    public void IServerLuaScriptConvenienceMethods()
    {
        using var conn = Create(allowAdmin: true, require: RedisFeatures.v2_6_0);

        const string Script = "redis.call('set', @key, @value)";
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

    [Fact]
    public void LuaScriptPrefixedKeys()
    {
        const string Script = "redis.call('set', @key, @value)";
        var prepared = LuaScript.Prepare(Script);
        var key = Me();
        var p = new { key = (RedisKey)key, value = "hello" };

        // no super clean way to extract this; so just abuse InternalsVisibleTo
        prepared.ExtractParameters(p, "prefix-", out RedisKey[]? keys, out RedisValue[]? args);
        Assert.NotNull(keys);
        Assert.Single(keys);
        Assert.Equal("prefix-" + key, keys[0]);
        Assert.NotNull(args);
        Assert.Equal(2, args.Length);
        Assert.Equal("prefix-" +  key, args[0]);
        Assert.Equal("hello", args[1]);
    }

    [Fact]
    public void LuaScriptWithWrappedDatabase()
    {
        using var conn = Create(allowAdmin: true, require: RedisFeatures.v2_6_0);

        const string Script = "redis.call('set', @key, @value)";
        var db = conn.GetDatabase();
        var wrappedDb = db.WithKeyPrefix("prefix-");
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

    [Fact]
    public async Task AsyncLuaScriptWithWrappedDatabase()
    {
        using var conn = Create(allowAdmin: true, require: RedisFeatures.v2_6_0);

        const string Script = "redis.call('set', @key, @value)";
        var db = conn.GetDatabase();
        var wrappedDb = db.WithKeyPrefix("prefix-");
        var key = Me();
        await db.KeyDeleteAsync(key, CommandFlags.FireAndForget);

        var prepared = LuaScript.Prepare(Script);
        await wrappedDb.ScriptEvaluateAsync(prepared, new { key = (RedisKey)key, value = 123 });
        var val1 = await wrappedDb.StringGetAsync(key);
        Assert.Equal(123, (int)val1);

        var val2 = await db.StringGetAsync("prefix-" + key);
        Assert.Equal(123, (int)val2);

        var val3 = await db.StringGetAsync(key);
        Assert.True(val3.IsNull);
    }

    [Fact]
    public void LoadedLuaScriptWithWrappedDatabase()
    {
        using var conn = Create(allowAdmin: true, require: RedisFeatures.v2_6_0);

        const string Script = "redis.call('set', @key, @value)";
        var db = conn.GetDatabase();
        var wrappedDb = db.WithKeyPrefix("prefix2-");
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

    [Fact]
    public async Task AsyncLoadedLuaScriptWithWrappedDatabase()
    {
        using var conn = Create(allowAdmin: true, require: RedisFeatures.v2_6_0);

        const string Script = "redis.call('set', @key, @value)";
        var db = conn.GetDatabase();
        var wrappedDb = db.WithKeyPrefix("prefix2-");
        var key = Me();
        await db.KeyDeleteAsync(key, CommandFlags.FireAndForget);

        var server = conn.GetServer(conn.GetEndPoints()[0]);
        var prepared = await LuaScript.Prepare(Script).LoadAsync(server);
        await wrappedDb.ScriptEvaluateAsync(prepared, new { key = (RedisKey)key, value = 123 }, flags: CommandFlags.FireAndForget);
        var val1 = await wrappedDb.StringGetAsync(key);
        Assert.Equal(123, (int)val1);

        var val2 = await db.StringGetAsync("prefix2-" + key);
        Assert.Equal(123, (int)val2);

        var val3 = await db.StringGetAsync(key);
        Assert.True(val3.IsNull);
    }

    [Fact]
    public void ScriptWithKeyPrefixViaTokens()
    {
        using var conn = Create();

        var p = conn.GetDatabase().WithKeyPrefix("prefix/");

        var args = new { x = "abc", y = (RedisKey)"def", z = 123 };
        var script = LuaScript.Prepare(@"
local arr = {};
arr[1] = @x;
arr[2] = @y;
arr[3] = @z;
return arr;
");
        var result = (RedisValue[]?)p.ScriptEvaluate(script, args);
        Assert.NotNull(result);
        Assert.Equal("abc", result[0]);
        Assert.Equal("prefix/def", result[1]);
        Assert.Equal("123", result[2]);
    }

    [Fact]
    public void ScriptWithKeyPrefixViaArrays()
    {
        using var conn = Create();

        var p = conn.GetDatabase().WithKeyPrefix("prefix/");

        const string script = @"
local arr = {};
arr[1] = ARGV[1];
arr[2] = KEYS[1];
arr[3] = ARGV[2];
return arr;
";
        var result = (RedisValue[]?)p.ScriptEvaluate(script, new RedisKey[] { "def" }, new RedisValue[] { "abc", 123 });
        Assert.NotNull(result);
        Assert.Equal("abc", result[0]);
        Assert.Equal("prefix/def", result[1]);
        Assert.Equal("123", result[2]);
    }

    [Fact]
    public void ScriptWithKeyPrefixCompare()
    {
        using var conn = Create();

        var p = conn.GetDatabase().WithKeyPrefix("prefix/");
        var args = new { k = (RedisKey)"key", s = "str", v = 123 };
        LuaScript lua = LuaScript.Prepare("return {@k, @s, @v}");
        var viaArgs = (RedisValue[]?)p.ScriptEvaluate(lua, args);

        var viaArr = (RedisValue[]?)p.ScriptEvaluate("return {KEYS[1], ARGV[1], ARGV[2]}", new[] { args.k }, new RedisValue[] { args.s, args.v });
        Assert.NotNull(viaArr);
        Assert.NotNull(viaArgs);
        Assert.Equal(string.Join(",", viaArr), string.Join(",", viaArgs));
    }

    [Fact]
    public void RedisResultUnderstandsNullArrayArray() => TestNullArray(RedisResult.NullArray);

    [Fact]
    public void RedisResultUnderstandsNullArrayNull() => TestNullArray(null);

    [Theory]
    [InlineData(null, false)]
    [InlineData("", false)]
    [InlineData("829c3804401b0727f70f73d4415e162400cbe57b", true)]
    [InlineData("$29c3804401b0727f70f73d4415e162400cbe57b", false)]
    [InlineData("829c3804401b0727f70f73d4415e162400cbe57", false)]
    [InlineData("829c3804401b0727f70f73d4415e162400cbe57bb", false)]
    public void Sha1Detection(string candidate, bool isSha)
    {
        Assert.Equal(isSha, ResultProcessor.ScriptLoadProcessor.IsSHA1(candidate));
    }

    private static void TestNullArray(RedisResult? value)
    {
        Assert.True(value == null || value.IsNull);

        Assert.Null((RedisValue[]?)value);
        Assert.Null((RedisKey[]?)value);
        Assert.Null((bool[]?)value);
        Assert.Null((long[]?)value);
        Assert.Null((ulong[]?)value);
        Assert.Null((string[]?)value);
        Assert.Null((int[]?)value);
        Assert.Null((double[]?)value);
        Assert.Null((byte[][]?)value);
        Assert.Null((RedisResult[]?)value);
    }

    [Fact]
    public void RedisResultUnderstandsNullNull() => TestNullValue(null);
    [Fact]
    public void RedisResultUnderstandsNullValue() => TestNullValue(RedisResult.Create(RedisValue.Null, ResultType.None));

    [Fact]
    public void TestEvalReadonly()
    {
        using var conn = GetScriptConn();
        var db = conn.GetDatabase();

        string script = "return KEYS[1]";
        RedisKey[] keys = new RedisKey[1] { "key1" };
        RedisValue[] values = new RedisValue[1] { "first" };

        var result = db.ScriptEvaluateReadOnly(script, keys, values);
        Assert.Equal("key1", result.ToString());
    }

    [Fact]
    public async Task TestEvalReadonlyAsync()
    {
        using var conn = GetScriptConn();
        var db = conn.GetDatabase();

        string script = "return KEYS[1]";
        RedisKey[] keys = new RedisKey[1] { "key1" };
        RedisValue[] values = new RedisValue[1] { "first" };

        var result = await db.ScriptEvaluateReadOnlyAsync(script, keys, values);
        Assert.Equal("key1", result.ToString());
    }

    [Fact]
    public void TestEvalShaReadOnly()
    {
        using var conn = GetScriptConn();
        var db = conn.GetDatabase();
        db.StringSet("foo", "bar");
        db.ScriptEvaluate("return redis.call('get','foo')");
        // Create a SHA1 hash of the script: 6b1bf486c81ceb7edf3c093f4c48582e38c0e791
        SHA1 sha1Hash = SHA1.Create();

        byte[] hash = sha1Hash.ComputeHash(Encoding.UTF8.GetBytes("return redis.call('get','foo')"));
        var result = db.ScriptEvaluateReadOnly(hash);

        Assert.Equal("bar", result.ToString());
    }

    [Fact]
    public async Task TestEvalShaReadOnlyAsync()
    {
        using var conn = GetScriptConn();
        var db = conn.GetDatabase();
        db.StringSet("foo", "bar");
        db.ScriptEvaluate("return redis.call('get','foo')");
        // Create a SHA1 hash of the script: 6b1bf486c81ceb7edf3c093f4c48582e38c0e791
        SHA1 sha1Hash = SHA1.Create();

        byte[] hash = sha1Hash.ComputeHash(Encoding.UTF8.GetBytes("return redis.call('get','foo')"));
        var result = await db.ScriptEvaluateReadOnlyAsync(hash);

        Assert.Equal("bar", result.ToString());
    }

    [Fact, TestCulture("en-US")]
    public void LuaScriptEnglishParameters() => LuaScriptParameterShared();

    [Fact, TestCulture("tr-TR")]
    public void LuaScriptTurkishParameters() => LuaScriptParameterShared();

    private void LuaScriptParameterShared()
    {
        const string Script = "redis.call('set', @key, @testIId)";
        var prepared = LuaScript.Prepare(Script);
        var key = Me();
        var p = new { key = (RedisKey)key, testIId = "hello" };

        prepared.ExtractParameters(p, null, out RedisKey[]? keys, out RedisValue[]? args);
        Assert.NotNull(keys);
        Assert.Single(keys);
        Assert.Equal(key, keys[0]);
        Assert.NotNull(args);
        Assert.Equal(2, args.Length);
        Assert.Equal(key, args[0]);
        Assert.Equal("hello", args[1]);
    }

    private static void TestNullValue(RedisResult? value)
    {
        Assert.True(value == null || value.IsNull);

        Assert.True(((RedisValue)value).IsNull);
        Assert.True(((RedisKey)value).IsNull);
        Assert.Null((bool?)value);
        Assert.Null((long?)value);
        Assert.Null((ulong?)value);
        Assert.Null((string?)value);
        Assert.Null((double?)value);
        Assert.Null((byte[]?)value);
    }
}
