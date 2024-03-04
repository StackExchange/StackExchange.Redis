using System;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Xunit;
using Xunit.Abstractions;

namespace StackExchange.Redis.Tests;

[Collection(SharedConnectionFixture.Key)]
public sealed class RespProtocolTests : TestBase
{
    public RespProtocolTests(ITestOutputHelper output, SharedConnectionFixture fixture) : base(output, fixture) { }

    [Fact]
    [RunPerProtocol]
    public async Task ConnectWithTiming()
    {
        using var conn = Create(shared: false, log: Writer);
        await conn.GetDatabase().PingAsync();
    }

    [Theory]
    // specify nothing
    [InlineData("someserver", false)]
    // specify *just* the protocol; sure, we'll believe you
    [InlineData("someserver,protocol=resp3", true)]
    [InlineData("someserver,protocol=resp3,$HELLO=", false)]
    [InlineData("someserver,protocol=resp3,$HELLO=BONJOUR", true)]
    [InlineData("someserver,protocol=3", true, "resp3")]
    [InlineData("someserver,protocol=3,$HELLO=", false, "resp3")]
    [InlineData("someserver,protocol=3,$HELLO=BONJOUR", true, "resp3")]
    [InlineData("someserver,protocol=2", false, "resp2")]
    [InlineData("someserver,protocol=2,$HELLO=", false, "resp2")]
    [InlineData("someserver,protocol=2,$HELLO=BONJOUR", false, "resp2")]
    // specify a pre-6 version - only used if protocol specified
    [InlineData("someserver,version=5.9", false)]
    [InlineData("someserver,version=5.9,$HELLO=", false)]
    [InlineData("someserver,version=5.9,$HELLO=BONJOUR", false)]
    [InlineData("someserver,version=5.9,protocol=resp3", true)]
    [InlineData("someserver,version=5.9,protocol=resp3,$HELLO=", false)]
    [InlineData("someserver,version=5.9,protocol=resp3,$HELLO=BONJOUR", true)]
    [InlineData("someserver,version=5.9,protocol=3", true, "resp3")]
    [InlineData("someserver,version=5.9,protocol=3,$HELLO=", false, "resp3")]
    [InlineData("someserver,version=5.9,protocol=3,$HELLO=BONJOUR", true, "resp3")]
    [InlineData("someserver,version=5.9,protocol=2", false, "resp2")]
    [InlineData("someserver,version=5.9,protocol=2,$HELLO=", false, "resp2")]
    [InlineData("someserver,version=5.9,protocol=2,$HELLO=BONJOUR", false, "resp2")]
    // specify a post-6 version; attempt by default
    [InlineData("someserver,version=6.0", false)]
    [InlineData("someserver,version=6.0,$HELLO=", false)]
    [InlineData("someserver,version=6.0,$HELLO=BONJOUR", false)]
    [InlineData("someserver,version=6.0,protocol=resp3", true)]
    [InlineData("someserver,version=6.0,protocol=resp3,$HELLO=", false)]
    [InlineData("someserver,version=6.0,protocol=resp3,$HELLO=BONJOUR", true)]
    [InlineData("someserver,version=6.0,protocol=3", true, "resp3")]
    [InlineData("someserver,version=6.0,protocol=3,$HELLO=", false, "resp3")]
    [InlineData("someserver,version=6.0,protocol=3,$HELLO=BONJOUR", true, "resp3")]
    [InlineData("someserver,version=6.0,protocol=2", false, "resp2")]
    [InlineData("someserver,version=6.0,protocol=2,$HELLO=", false, "resp2")]
    [InlineData("someserver,version=6.0,protocol=2,$HELLO=BONJOUR", false, "resp2")]
    [InlineData("someserver,version=7.2", false)]
    [InlineData("someserver,version=7.2,$HELLO=", false)]
    [InlineData("someserver,version=7.2,$HELLO=BONJOUR", false)]
    public void ParseFormatConfigOptions(string configurationString, bool tryResp3, string? formatProtocol = null)
    {
        var config = ConfigurationOptions.Parse(configurationString);

        string expectedConfigurationString = formatProtocol is null ? configurationString : Regex.Replace(configurationString, "(?<=protocol=)[^,]+", formatProtocol);

        Assert.Equal(expectedConfigurationString, config.ToString(true)); // check round-trip
        Assert.Equal(expectedConfigurationString, config.Clone().ToString(true)); // check clone
        Assert.Equal(tryResp3, config.TryResp3());
    }

    [Fact]
    [RunPerProtocol]
    public async Task TryConnect()
    {
        var muxer = Create(shared: false);
        await muxer.GetDatabase().PingAsync();

        var server = muxer.GetServerEndPoint(muxer.GetEndPoints().Single());
        if (Context.IsResp3 && !server.GetFeatures().Resp3)
        {
            Skip.Inconclusive("server does not support RESP3");
        }
        if (Context.IsResp3)
        {
            Assert.Equal(RedisProtocol.Resp3, server.Protocol);
        }
        else
        {
            Assert.Equal(RedisProtocol.Resp2, server.Protocol);
        }
        var cid = server.GetBridge(RedisCommand.GET)?.ConnectionId;
        if (server.GetFeatures().ClientId)
        {
            Assert.NotNull(cid);
        }
        else
        {
            Assert.Null(cid);
        }
    }

    [Theory]
    [InlineData("HELLO", true)]
    [InlineData("BONJOUR", false)]
    public async Task ConnectWithBrokenHello(string command, bool isResp3)
    {
        var config = ConfigurationOptions.Parse(TestConfig.Current.SecureServerAndPort);
        config.Password = TestConfig.Current.SecurePassword;
        config.Protocol = RedisProtocol.Resp3;
        config.CommandMap = CommandMap.Create(new() { ["hello"] = command });

        using var muxer = await ConnectionMultiplexer.ConnectAsync(config, Writer);
        await muxer.GetDatabase().PingAsync(); // is connected
        var ep = muxer.GetServerEndPoint(muxer.GetEndPoints()[0]);
        if (!ep.GetFeatures().Resp3) // this is just a v6 check
        {
            isResp3 = false; // then, no: it won't be
        }
        Assert.Equal(isResp3 ? RedisProtocol.Resp3 : RedisProtocol.Resp2, ep.Protocol);
        var result = await muxer.GetDatabase().ExecuteAsync("latency", "doctor");
        Assert.Equal(isResp3 ? ResultType.VerbatimString : ResultType.BulkString, result.Resp3Type);
    }

    [Theory]
    [InlineData("return 42", RedisProtocol.Resp2, ResultType.Integer, ResultType.Integer, 42)]
    [InlineData("return 'abc'", RedisProtocol.Resp2, ResultType.BulkString, ResultType.BulkString, "abc")]
    [InlineData(@"return {1,2,3}", RedisProtocol.Resp2, ResultType.Array, ResultType.Array, ARR_123)]
    [InlineData("return nil", RedisProtocol.Resp2, ResultType.BulkString, ResultType.Null, null)]
    [InlineData(@"return redis.pcall('hgetall', 'key')", RedisProtocol.Resp2, ResultType.Array, ResultType.Array, MAP_ABC)]
    [InlineData(@"redis.setresp(3)
return redis.pcall('hgetall', 'key')", RedisProtocol.Resp2, ResultType.Array, ResultType.Array, MAP_ABC)]
    [InlineData("return true", RedisProtocol.Resp2, ResultType.Integer, ResultType.Integer, 1)]
    [InlineData("return false", RedisProtocol.Resp2, ResultType.BulkString, ResultType.Null, null)]
    [InlineData("redis.setresp(3) return true", RedisProtocol.Resp2, ResultType.Integer, ResultType.Integer, 1)]
    [InlineData("redis.setresp(3) return false", RedisProtocol.Resp2, ResultType.Integer, ResultType.Integer, 0)]

    [InlineData("return { map = { a = 1, b = 2, c = 3 } }", RedisProtocol.Resp2, ResultType.Array, ResultType.Array, MAP_ABC, 6)]
    [InlineData("return { set = { a = 1, b = 2, c = 3 } }", RedisProtocol.Resp2, ResultType.Array, ResultType.Array, SET_ABC, 6)]
    [InlineData("return { double = 42 }", RedisProtocol.Resp2, ResultType.BulkString, ResultType.BulkString, 42.0, 6)]

    [InlineData("return 42", RedisProtocol.Resp3, ResultType.Integer, ResultType.Integer, 42)]
    [InlineData("return 'abc'", RedisProtocol.Resp3, ResultType.BulkString, ResultType.BulkString, "abc")]
    [InlineData("return {1,2,3}", RedisProtocol.Resp3, ResultType.Array, ResultType.Array, ARR_123)]
    [InlineData("return nil", RedisProtocol.Resp3, ResultType.BulkString, ResultType.Null, null)]
    [InlineData(@"return redis.pcall('hgetall', 'key')", RedisProtocol.Resp3, ResultType.Array, ResultType.Array, MAP_ABC)]
    [InlineData(@"redis.setresp(3)
return redis.pcall('hgetall', 'key')", RedisProtocol.Resp3, ResultType.Array, ResultType.Map, MAP_ABC)]
    [InlineData("return true", RedisProtocol.Resp3, ResultType.Integer, ResultType.Integer, 1)]
    [InlineData("return false", RedisProtocol.Resp3, ResultType.BulkString, ResultType.Null, null)]
    [InlineData("redis.setresp(3) return true", RedisProtocol.Resp3, ResultType.Integer, ResultType.Boolean, true)]
    [InlineData("redis.setresp(3) return false", RedisProtocol.Resp3, ResultType.Integer, ResultType.Boolean, false)]

    [InlineData("return { map = { a = 1, b = 2, c = 3 } }", RedisProtocol.Resp3, ResultType.Array, ResultType.Map, MAP_ABC, 6)]
    [InlineData("return { set = { a = 1, b = 2, c = 3 } }", RedisProtocol.Resp3, ResultType.Array, ResultType.Set, SET_ABC, 6)]
    [InlineData("return { double = 42 }", RedisProtocol.Resp3, ResultType.SimpleString, ResultType.Double, 42.0, 6)]
    public async Task CheckLuaResult(string script, RedisProtocol protocol, ResultType resp2, ResultType resp3, object expected, int serverMin = 1)
    {
        // note Lua does not appear to return RESP3 types in any scenarios
        var muxer = Create(protocol: protocol);
        var ep = muxer.GetServerEndPoint(muxer.GetEndPoints().Single());
        if (serverMin > ep.Version.Major)
        {
            Skip.Inconclusive($"applies to v{serverMin} onwards - detected v{ep.Version.Major}");
        }
        if (script.Contains("redis.setresp(3)") && !ep.GetFeatures().Resp3) /* v6 check */
        {
            Skip.Inconclusive("debug protocol not available");
        }
        if (ep.Protocol is null) throw new InvalidOperationException($"No protocol! {ep.InteractiveConnectionState}");
        Assert.Equal(protocol, ep.Protocol);

        var db = muxer.GetDatabase();
        if (expected is MAP_ABC)
        {
            db.KeyDelete("key");
            db.HashSet("key", "a", 1);
            db.HashSet("key", "b", 2);
            db.HashSet("key", "c", 3);
        }
        var result = await db.ScriptEvaluateAsync(script, flags: CommandFlags.NoScriptCache);
        Assert.Equal(resp2, result.Resp2Type);
        Assert.Equal(resp3, result.Resp3Type);

        switch (expected)
        {
            case null:
                Assert.True(result.IsNull);
                break;
            case ARR_123:
                Assert.Equal(3, result.Length);
                for (int i = 0; i < result.Length; i++)
                {
                    Assert.Equal(i + 1, result[i].AsInt32());
                }
                break;
            case MAP_ABC:
                var map = result.ToDictionary();
                Assert.Equal(3, map.Count);
                Assert.True(map.TryGetValue("a", out var value));
                Assert.Equal(1, value.AsInt32());
                Assert.True(map.TryGetValue("b", out value));
                Assert.Equal(2, value.AsInt32());
                Assert.True(map.TryGetValue("c", out value));
                Assert.Equal(3, value.AsInt32());
                break;
            case SET_ABC:
                Assert.Equal(3, result.Length);
                var arr = result.AsStringArray()!;
                Assert.Contains("a", arr);
                Assert.Contains("b", arr);
                Assert.Contains("c", arr);
                break;
            case string s:
                Assert.Equal(s, result.AsString());
                break;
            case double d:
                Assert.Equal(d, result.AsDouble());
                break;
            case int i:
                Assert.Equal(i, result.AsInt32());
                break;
            case bool b:
                Assert.Equal(b, result.AsBoolean());
                break;
        }
    }


    [Theory]
    //[InlineData("return 42", false, ResultType.Integer, ResultType.Integer, 42)]
    //[InlineData("return 'abc'", false, ResultType.BulkString, ResultType.BulkString, "abc")]
    //[InlineData(@"return {1,2,3}", false, ResultType.Array, ResultType.Array, ARR_123)]
    //[InlineData("return nil", false, ResultType.BulkString, ResultType.Null, null)]
    //[InlineData(@"return redis.pcall('hgetall', 'key')", false, ResultType.Array, ResultType.Array, MAP_ABC)]
    //[InlineData("return true", false, ResultType.Integer, ResultType.Integer, 1)]

    //[InlineData("return 42", true, ResultType.Integer, ResultType.Integer, 42)]
    //[InlineData("return 'abc'", true, ResultType.BulkString, ResultType.BulkString, "abc")]
    //[InlineData("return {1,2,3}", true, ResultType.Array, ResultType.Array, ARR_123)]
    //[InlineData("return nil", true, ResultType.BulkString, ResultType.Null, null)]
    //[InlineData(@"return redis.pcall('hgetall', 'key')", true, ResultType.Array, ResultType.Array, MAP_ABC)]
    //[InlineData("return true", true, ResultType.Integer, ResultType.Integer, 1)]


    [InlineData("incrby", RedisProtocol.Resp2, ResultType.Integer, ResultType.Integer, 42, "ikey", 2)]
    [InlineData("incrby", RedisProtocol.Resp3, ResultType.Integer, ResultType.Integer, 42, "ikey", 2)]
    [InlineData("incrby", RedisProtocol.Resp2, ResultType.Integer, ResultType.Integer, 2, "nkey", 2)]
    [InlineData("incrby", RedisProtocol.Resp3, ResultType.Integer, ResultType.Integer, 2, "nkey", 2)]

    [InlineData("get", RedisProtocol.Resp2, ResultType.BulkString, ResultType.BulkString, "40", "ikey")]
    [InlineData("get", RedisProtocol.Resp3, ResultType.BulkString, ResultType.BulkString, "40", "ikey")]
    [InlineData("get", RedisProtocol.Resp2, ResultType.BulkString, ResultType.Null, null, "nkey")]
    [InlineData("get", RedisProtocol.Resp3, ResultType.BulkString, ResultType.Null, null, "nkey")]

    [InlineData("smembers", RedisProtocol.Resp2, ResultType.Array, ResultType.Array, SET_ABC, "skey")]
    [InlineData("smembers", RedisProtocol.Resp3, ResultType.Array, ResultType.Set, SET_ABC, "skey")]
    [InlineData("smembers", RedisProtocol.Resp2, ResultType.Array, ResultType.Array, EMPTY_ARR, "nkey")]
    [InlineData("smembers", RedisProtocol.Resp3, ResultType.Array, ResultType.Set, EMPTY_ARR, "nkey")]

    [InlineData("hgetall", RedisProtocol.Resp2, ResultType.Array, ResultType.Array, MAP_ABC, "hkey")]
    [InlineData("hgetall", RedisProtocol.Resp3, ResultType.Array, ResultType.Map, MAP_ABC, "hkey")]
    [InlineData("hgetall", RedisProtocol.Resp2, ResultType.Array, ResultType.Array, EMPTY_ARR, "nkey")]
    [InlineData("hgetall", RedisProtocol.Resp3, ResultType.Array, ResultType.Map, EMPTY_ARR, "nkey")]

    [InlineData("sismember", RedisProtocol.Resp2, ResultType.Integer, ResultType.Integer, true, "skey", "b")]
    [InlineData("sismember", RedisProtocol.Resp3, ResultType.Integer, ResultType.Integer, true, "skey", "b")]
    [InlineData("sismember", RedisProtocol.Resp2, ResultType.Integer, ResultType.Integer, false, "nkey", "b")]
    [InlineData("sismember", RedisProtocol.Resp3, ResultType.Integer, ResultType.Integer, false, "nkey", "b")]
    [InlineData("sismember", RedisProtocol.Resp2, ResultType.Integer, ResultType.Integer, false, "skey", "d")]
    [InlineData("sismember", RedisProtocol.Resp3, ResultType.Integer, ResultType.Integer, false, "skey", "d")]

    [InlineData("latency", RedisProtocol.Resp2, ResultType.BulkString, ResultType.BulkString, STR_DAVE, "doctor")]
    [InlineData("latency", RedisProtocol.Resp3, ResultType.BulkString, ResultType.VerbatimString, STR_DAVE, "doctor")]

    [InlineData("incrbyfloat", RedisProtocol.Resp2, ResultType.BulkString, ResultType.BulkString, 41.5, "ikey", 1.5)]
    [InlineData("incrbyfloat", RedisProtocol.Resp3, ResultType.BulkString, ResultType.BulkString, 41.5, "ikey", 1.5)]

    /* DEBUG PROTOCOL <type>
     * Reply with a test value of the specified type. <type> can be: string,
     * integer, double, bignum, null, array, set, map, attrib, push, verbatim,
     * true, false.,
     *
     * NOTE: "debug protocol" may be disabled in later default server configs; if this starts
     * failing when we upgrade the test server: update the config to re-enable the command
     */
    [InlineData("debug", RedisProtocol.Resp2, ResultType.BulkString, ResultType.BulkString, ANY, "protocol", "string")]
    [InlineData("debug", RedisProtocol.Resp3, ResultType.BulkString, ResultType.BulkString, ANY, "protocol", "string")]

    [InlineData("debug", RedisProtocol.Resp2, ResultType.BulkString, ResultType.BulkString, ANY, "protocol", "double")]
    [InlineData("debug", RedisProtocol.Resp3, ResultType.SimpleString, ResultType.Double, ANY, "protocol", "double")]

    [InlineData("debug", RedisProtocol.Resp2, ResultType.BulkString, ResultType.BulkString, ANY, "protocol", "bignum")]
    [InlineData("debug", RedisProtocol.Resp3, ResultType.SimpleString, ResultType.BigInteger, ANY, "protocol", "bignum")]

    [InlineData("debug", RedisProtocol.Resp2, ResultType.BulkString, ResultType.Null, null, "protocol", "null")]
    [InlineData("debug", RedisProtocol.Resp3, ResultType.BulkString, ResultType.Null, null, "protocol", "null")]

    [InlineData("debug", RedisProtocol.Resp2, ResultType.Array, ResultType.Array, ANY, "protocol", "array")]
    [InlineData("debug", RedisProtocol.Resp3, ResultType.Array, ResultType.Array, ANY, "protocol", "array")]

    [InlineData("debug", RedisProtocol.Resp2, ResultType.Array, ResultType.Array, ANY, "protocol", "set")]
    [InlineData("debug", RedisProtocol.Resp3, ResultType.Array, ResultType.Set, ANY, "protocol", "set")]

    [InlineData("debug", RedisProtocol.Resp2, ResultType.Array, ResultType.Array, ANY, "protocol", "map")]
    [InlineData("debug", RedisProtocol.Resp3, ResultType.Array, ResultType.Map, ANY, "protocol", "map")]

    [InlineData("debug", RedisProtocol.Resp2, ResultType.BulkString, ResultType.BulkString, ANY, "protocol", "verbatim")]
    [InlineData("debug", RedisProtocol.Resp3, ResultType.BulkString, ResultType.VerbatimString, ANY, "protocol", "verbatim")]

    [InlineData("debug", RedisProtocol.Resp2, ResultType.Integer, ResultType.Integer, true, "protocol", "true")]
    [InlineData("debug", RedisProtocol.Resp3, ResultType.Integer, ResultType.Boolean, true, "protocol", "true")]

    [InlineData("debug", RedisProtocol.Resp2, ResultType.Integer, ResultType.Integer, false, "protocol", "false")]
    [InlineData("debug", RedisProtocol.Resp3, ResultType.Integer, ResultType.Boolean, false, "protocol", "false")]

    public async Task CheckCommandResult(string command, RedisProtocol protocol, ResultType resp2, ResultType resp3, object expected, params object[] args)
    {
        var muxer = Create(protocol: protocol);
        var ep = muxer.GetServerEndPoint(muxer.GetEndPoints().Single());
        if (command == "debug" && args.Length > 0 && args[0] is "protocol" && !ep.GetFeatures().Resp3 /* v6 check */ )
        {
            Skip.Inconclusive("debug protocol not available");
        }
        Assert.Equal(protocol, ep.Protocol);

        var db = muxer.GetDatabase();
        if (args.Length > 0)
        {
            await db.KeyDeleteAsync((string)args[0]);
            switch (args[0])
            {
                case "ikey":
                    await db.StringSetAsync("ikey", "40");
                    break;
                case "skey":
                    await db.SetAddAsync("skey", new RedisValue[] { "a", "b", "c" });
                    break;
                case "hkey":
                    await db.HashSetAsync("hkey", new HashEntry[] { new("a", 1), new("b", 2), new("c",3) });
                    break;
            }
        }
        var result = await db.ExecuteAsync(command, args);
        Assert.Equal(resp2, result.Resp2Type);
        Assert.Equal(resp3, result.Resp3Type);

        switch (expected)
        {
            case null:
                Assert.True(result.IsNull);
                break;
            case ANY:
                // not checked beyond type
                break;
            case EMPTY_ARR:
                Assert.Equal(0, result.Length);
                break;
            case ARR_123:
                Assert.Equal(3, result.Length);
                for (int i = 0; i < result.Length; i++)
                {
                    Assert.Equal(i + 1, result[i].AsInt32());
                }
                break;
            case STR_DAVE:
                var scontent = result.ToString();
                Log(scontent);
                Assert.NotNull(scontent);
                var isExpectedContent = scontent.StartsWith("Dave, ") || scontent.StartsWith("I'm sorry, Dave");
                Assert.True(isExpectedContent);
                Log(scontent);

                scontent = result.ToString(out var type);
                Assert.NotNull(scontent);
                isExpectedContent = scontent.StartsWith("Dave, ") || scontent.StartsWith("I'm sorry, Dave");
                Assert.True(isExpectedContent);
                Log(scontent);
                if (protocol == RedisProtocol.Resp3)
                {
                    Assert.Equal("txt", type); 
                }
                else
                {
                    Assert.Null(type);
                }
                break;
            case SET_ABC:
                Assert.Equal(3, result.Length);
                var arr = result.AsStringArray()!;
                Assert.Contains("a", arr);
                Assert.Contains("b", arr);
                Assert.Contains("c", arr);
                break;
            case MAP_ABC:
                var map = result.ToDictionary();
                Assert.Equal(3, map.Count);
                Assert.True(map.TryGetValue("a", out var value));
                Assert.Equal(1, value.AsInt32());
                Assert.True(map.TryGetValue("b", out value));
                Assert.Equal(2, value.AsInt32());
                Assert.True(map.TryGetValue("c", out value));
                Assert.Equal(3, value.AsInt32());
                break;
            case string s:
                Assert.Equal(s, result.AsString());
                break;
            case int i:
                Assert.Equal(i, result.AsInt32());
                break;
            case bool b:
                Assert.Equal(b, result.AsBoolean());
                Assert.Equal(b ? 1 : 0, result.AsInt32());
                Assert.Equal(b ? 1 : 0, result.AsInt64());
                break;
        }


    }

    private const string SET_ABC = nameof(SET_ABC);
    private const string ARR_123 = nameof(ARR_123);
    private const string MAP_ABC = nameof(MAP_ABC);
    private const string EMPTY_ARR = nameof(EMPTY_ARR);
    private const string STR_DAVE = nameof(STR_DAVE);
    private const string ANY = nameof(ANY);
}
