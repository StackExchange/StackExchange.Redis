using System;
using System.Text;
using System.Threading.Tasks;
using StackExchange.Redis.KeyspaceIsolation;
using Xunit;

namespace StackExchange.Redis.Tests;

[RunPerProtocol]
public class RespResultTests(ITestOutputHelper output, SharedConnectionFixture fixture) : TestBase(output, fixture)
{
    [Fact]
    public async Task ExecuteResp_ScalarBlob()
    {
        await using var conn = Create();
        var db = conn.GetDatabase();
        RedisKey key = Me();
        db.StringSet(key, "hello world");

        using var result = db.ExecuteResp("GET", new RedisKeyOrValue[] { key });
        Assert.False(result.IsNull);
        Assert.Equal("hello world", (string?)result.ReadScalar().ReadRedisValue());
    }

    [Fact]
    public async Task ExecuteRespAsync_ScalarBlob()
    {
        await using var conn = Create();
        var db = conn.GetDatabase();
        RedisKey key = Me();
        await db.StringSetAsync(key, "hello world");

        using var result = await db.ExecuteRespAsync("GET", new RedisKeyOrValue[] { key });
        Assert.False(result.IsNull);
        Assert.Equal("hello world", (string?)result.ReadScalar().ReadRedisValue());
    }

    [Fact]
    public async Task ExecuteResp_MissingKey_IsNull()
    {
        await using var conn = Create();
        var db = conn.GetDatabase();
        RedisKey key = Me(); // never set

        using var result = db.ExecuteResp("GET", new RedisKeyOrValue[] { key });
        Assert.True(result.IsNull);
    }

    [Fact]
    public async Task ScriptEvaluateResp_ScalarBlob_ReadLease()
    {
        await using var conn = Create();
        var db = conn.GetDatabase();

        using var result = db.ScriptEvaluateResp("return 'hello world'", default, default);
        Assert.False(result.IsNull);

        using var lease = result.ReadScalar().ReadLease();
        Assert.Equal("hello world", Encoding.UTF8.GetString(lease!.Span));
    }

    [Fact]
    public async Task ScriptEvaluateResp_ScalarBlob_CopyToCallerBuffer()
    {
        await using var conn = Create();
        var db = conn.GetDatabase();

        using var result = db.ScriptEvaluateResp("return 'hello world'", default, default);
        var reader = result.ReadScalar();
        byte[] buffer = new byte[reader.ScalarLength()];
        var copied = reader.CopyTo(buffer);
        Assert.Equal("hello world", Encoding.UTF8.GetString(buffer, 0, copied));
    }

    [Fact]
    public async Task ScriptEvaluateRespAsync_Integer()
    {
        await using var conn = Create();
        var db = conn.GetDatabase();

        using var result = await db.ScriptEvaluateRespAsync("return 42", default, default);
        Assert.Equal(42, (long)result.ReadScalar().ReadRedisValue());
    }

    [Fact]
    public async Task ScriptEvaluateResp_KeysAndValues()
    {
        await using var conn = Create();
        var db = conn.GetDatabase();
        RedisKey key = Me();

        using var result = db.ScriptEvaluateResp(
            "redis.call('set', KEYS[1], ARGV[1]); return redis.call('get', KEYS[1])",
            new RedisKey[] { key },
            new RedisValue[] { "hello keys/values" });

        Assert.Equal("hello keys/values", (string?)result.ReadScalar().ReadRedisValue());
    }

    [Fact]
    public async Task ScriptEvaluateResp_Null_IsSharedSingleton()
    {
        await using var conn = Create();
        var db = conn.GetDatabase();

        using var r1 = db.ScriptEvaluateResp("return nil", default, default);
        var r2 = db.ScriptEvaluateResp("return nil", default, default);

        Assert.True(r1.IsNull);
        Assert.Same(r1, r2); // shared singleton - disposing r1 must not affect r2's usability
    }

    [Fact]
    public async Task ScriptEvaluateResp_Tree_ReadAndReadRedisResultAgree()
    {
        await using var conn = Create();
        var db = conn.GetDatabase();

        using var result = db.ScriptEvaluateResp("return {1,2,'three'}", default, default);
        var reader = result.Read();
        Assert.True(reader.IsAggregate);
        Assert.Equal(3, reader.AggregateLength());

        var redisResult = result.Read().ReadRedisResult();
        var values = (RedisValue[]?)redisResult;
        Assert.NotNull(values);
        Assert.Equal(3, values!.Length);
        Assert.Equal(1, (long)values[0]);
        Assert.Equal(2, (long)values[1]);
        Assert.Equal("three", (string?)values[2]);
    }

    [Fact]
    public async Task ScriptEvaluateResp_ScalarAccessorOnTree_ThrowsConsistently()
    {
        await using var conn = Create();
        var db = conn.GetDatabase();

        using var result = db.ScriptEvaluateResp("return {1,2,3}", default, default);
        Assert.Throws<InvalidOperationException>(() => result.ReadScalar());
    }

    [Fact]
    public async Task ScriptEvaluateRespAsync_ScriptError_ThrowsRedisServerException()
    {
        await using var conn = Create();
        var db = conn.GetDatabase();

        await Assert.ThrowsAsync<RedisServerException>(
            async () => await db.ScriptEvaluateRespAsync("this is not valid lua {{{", default, default));
    }

    [Fact]
    public async Task ScriptEvaluateReadOnlyResp_Works()
    {
        await using var conn = Create();
        var db = conn.GetDatabase();
        RedisKey key = Me();
        db.StringSet(key, "read-only value");

        using var result = db.ScriptEvaluateReadOnlyResp(
            "return redis.call('get', KEYS[1])",
            new RedisKey[] { key },
            default);

        Assert.Equal("read-only value", (string?)result.ReadScalar().ReadRedisValue());
    }

    [Fact]
    public async Task ExecuteResp_PING_SimpleString()
    {
        await using var conn = Create();
        var db = conn.GetDatabase();

        using var result = db.ExecuteResp("PING", default);
        Assert.Equal("PONG", (string?)result.ReadScalar().ReadRedisValue());
    }

    [Fact]
    public async Task ExecuteResp_ArrayReply_MGET()
    {
        await using var conn = Create();
        var db = conn.GetDatabase();
        RedisKey key1 = Me() + ":1";
        RedisKey key2 = Me() + ":2"; // deliberately left unset - reads back as a null array element
        await db.StringSetAsync(key1, "hello");
        await db.KeyDeleteAsync(key2);

        using var result = db.ExecuteResp("MGET", new RedisKeyOrValue[] { key1, key2 });
        var reader = result.Read();
        Assert.True(reader.IsAggregate);
        Assert.Equal(2, reader.AggregateLength());

        var children = reader.AggregateChildren();
        Assert.True(children.MoveNext());
        Assert.Equal("hello", (string?)children.Value.ReadRedisValue());
        Assert.True(children.MoveNext());
        Assert.True(children.Value.IsNull);
        Assert.False(children.MoveNext());
    }

    [Fact]
    public async Task ExecuteResp_KeyPrefixed_ShortKey_IsPrefixedCorrectly()
    {
        // regression coverage: RedisKeyOrValue must round-trip a short (<=8 byte) key value through
        // KeyPrefixed without it getting folded into RedisValue's inline ShortBlob/Simplify form.
        await using var conn = Create();
        var raw = conn.GetDatabase();
        string prefixText = Me() + ":";
        IDatabase db = new KeyPrefixedDatabase(raw, Encoding.UTF8.GetBytes(prefixText));

        RedisKey shortKey = "abc"; // <= 8 bytes - would be eligible for ShortBlob packing as a RedisValue
        RedisKey directKey = prefixText + "abc";
        await raw.KeyDeleteAsync(directKey);

        using var setResult = db.ExecuteResp("SET", new RedisKeyOrValue[] { shortKey, (RedisValue)"short-key-value" });
        Assert.Equal("OK", (string?)setResult.ReadScalar().ReadRedisValue());

        // confirm it actually landed at the *prefixed* key when read directly (unprefixed) connection
        var direct = await raw.StringGetAsync(directKey);
        Assert.Equal("short-key-value", (string?)direct);

        // and that reading it back through the prefixed wrapper also agrees
        using var getResult = db.ExecuteResp("GET", new RedisKeyOrValue[] { shortKey });
        Assert.Equal("short-key-value", (string?)getResult.ReadScalar().ReadRedisValue());
    }

    [Fact]
    public void RedisKeyOrValue_KeyAndValue_AreNeverEqualEvenWithSameText()
    {
        RedisKeyOrValue key = (RedisKey)"abc";
        RedisKeyOrValue value = (RedisValue)"abc";

        Assert.True(key.IsKey);
        Assert.False(key.IsValue);
        Assert.True(value.IsValue);
        Assert.False(value.IsKey);
        Assert.False(key.Equals(value));
        Assert.False(value.Equals(key));
    }

    [Fact]
    public void RedisKeyOrValue_Default_IsNullOnly()
    {
        RedisKeyOrValue none = default;
        Assert.True(none.IsNull);
        Assert.False(none.IsKey);
        Assert.False(none.IsValue);
    }

    [Fact]
    public void RedisKeyOrValue_InvalidCast_Throws()
    {
        RedisKeyOrValue key = (RedisKey)"abc";
        Assert.Throws<InvalidCastException>(() => (RedisValue)key);

        RedisKeyOrValue value = (RedisValue)"abc";
        Assert.Throws<InvalidCastException>(() => (RedisKey)value);
    }
}
