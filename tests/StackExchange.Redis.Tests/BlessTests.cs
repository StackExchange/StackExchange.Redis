using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using StackExchange.Redis.KeyspaceIsolation;
using Xunit;

namespace StackExchange.Redis.Tests;

/// <summary>
/// Integration tests for the <c>BLESS</c> eviction-protection flags (Redis 8.12+).
/// </summary>
[RunPerProtocol]
public class BlessTests(ITestOutputHelper output, SharedConnectionFixture fixture) : TestBase(output, fixture)
{
    [Fact]
    public async Task BlessUnblessLifecycle()
    {
        await using var conn = Create(require: RedisFeatures.v8_12_0);
        var db = conn.GetDatabase();
        RedisKey key = Me();
        await db.KeyDeleteAsync(key);
        await db.StringSetAsync(key, "value");

        Assert.Equal(BlessFlags.None, await db.KeyBlessFlagsAsync(key));
        Assert.True(await db.KeyBlessAsync(key, BlessFlags.NoEvict));
        Assert.False(await db.KeyBlessAsync(key, BlessFlags.NoEvict)); // already set; still success
        Assert.Equal(BlessFlags.NoEvict, await db.KeyBlessFlagsAsync(key));

        // the flag is key metadata, not part of the value
        await db.StringSetAsync(key, "overwritten");
        Assert.Equal(BlessFlags.NoEvict, db.KeyBlessFlags(key));

        Assert.True(db.KeyUnbless(key, BlessFlags.NoEvict));
        Assert.False(db.KeyUnbless(key, BlessFlags.NoEvict));
        Assert.Equal(BlessFlags.None, db.KeyBlessFlags(key));
    }

    [Fact]
    public async Task MissingKeyIsAnError()
    {
        await using var conn = Create(require: RedisFeatures.v8_12_0);
        var db = conn.GetDatabase();
        RedisKey key = Me();
        await db.KeyDeleteAsync(key);

        var ex = await Assert.ThrowsAsync<RedisServerException>(() => db.KeyBlessAsync(key, BlessFlags.NoEvict));
        Assert.Contains("no such key", ex.Message);
        ex = await Assert.ThrowsAsync<RedisServerException>(() => db.KeyUnblessAsync(key, BlessFlags.NoEvict));
        Assert.Contains("no such key", ex.Message);
        ex = await Assert.ThrowsAsync<RedisServerException>(() => db.KeyBlessFlagsAsync(key));
        Assert.Contains("no such key", ex.Message);
    }

    [Fact]
    public async Task DeletingTheKeyClearsTheFlag()
    {
        await using var conn = Create(require: RedisFeatures.v8_12_0);
        var db = conn.GetDatabase();
        RedisKey key = Me();
        await db.StringSetAsync(key, "value");
        await db.KeyBlessAsync(key, BlessFlags.NoEvict);

        await db.KeyDeleteAsync(key);
        await db.StringSetAsync(key, "value");
        Assert.Equal(BlessFlags.None, await db.KeyBlessFlagsAsync(key));
    }

    [Fact]
    public async Task KeyPrefixIsApplied()
    {
        await using var conn = Create(require: RedisFeatures.v8_12_0);
        var db = conn.GetDatabase();
        RedisKey prefix = Me() + ":", key = "key", full = Me() + ":key";
        var prefixed = db.WithKeyPrefix(prefix);
        await db.StringSetAsync(full, "value");

        Assert.True(await prefixed.KeyBlessAsync(key, BlessFlags.NoEvict));
        Assert.Equal(BlessFlags.NoEvict, await db.KeyBlessFlagsAsync(full));
        Assert.Equal(BlessFlags.NoEvict, await prefixed.KeyBlessFlagsAsync(key));
        Assert.True(await prefixed.KeyUnblessAsync(key, BlessFlags.NoEvict));
    }

    [Fact]
    public async Task BlessedKeysPagesThroughAllKeys()
    {
        await using var conn = Create(require: RedisFeatures.v8_12_0);
        var dbId = TestConfig.GetDedicatedDB(conn);
        var db = conn.GetDatabase(dbId);
        var server = GetAnyPrimary(conn);
        var prefix = Me();
        await server.FlushDatabaseAsync(dbId);

        const int Count = 100;
        for (int i = 0; i < Count; i++)
        {
            RedisKey key = prefix + i;
            await db.StringSetAsync(key, i);
            if (i % 2 == 0) await db.KeyBlessAsync(key, BlessFlags.NoEvict);
        }

        // small pages, so the cursor has to be followed; the server may repeat keys across pages
        var sync = server.BlessedKeys(BlessFlags.NoEvict, dbId, pageSize: 7).Select(k => (string)k!).Distinct().ToList();
        Assert.Equal(Count / 2, sync.Count);
        Assert.All(sync, k => Assert.Equal(0, int.Parse(k.Substring(prefix.Length)) % 2));

        var async = new HashSet<string>();
        await foreach (var key in server.BlessedKeysAsync(BlessFlags.NoEvict, dbId, pageSize: 7))
        {
            async.Add((string)key!);
        }
        Assert.Equal(Count / 2, async.Count);
    }
}
